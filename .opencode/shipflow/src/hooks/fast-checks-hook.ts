// Runs the configured drift-gate script when a root session goes idle after
// backend/frontend source edits. Serialized, non-blocking, non-fatal;
// FAST_CHECKS_HOOK=0 disables. Edits arm on edit-tool completions; idle is
// the session.execution.succeeded event, skipping subagent sessions.
import { execFile } from "node:child_process";
import type { Plugin } from "@opencode/plugin";

const SOURCE_EXT = /\.(cs|ts|tsx|astro|json)$/i;
const EXCLUDE_RE =
  /(?:^|\/)(?:node_modules|obj|bin|dist|target|\.turbo)(?:\/|$)|(?:^|\/)(?:pnpm-lock\.yaml|package-lock\.json|bun\.lockb)$/;
const EDIT_TOOLS = new Set(["edit", "write", "patch"]);

// A concurrent build holds obj/ + index.lock and fails the build step for
// infra reasons, not drift - a silent skip so an active dev loop is not
// spammed.
const CONTENTION_RE =
  /MSB30(?:26|27|41|42)|used by another process|text file is busy|index\.lock|Could not copy|Response file .* is being used/i;

// Edits that arm the hook: backend/** or frontend/** source. Generated dirs
// and lockfiles are ignored; openapi.json is intentionally tracked (a manual
// edit to it signals drift worth checking).
export const DEFAULT_SOURCE_DIRS = ["backend/", "frontend/"];

export function isSourceEdit(
  file: unknown,
  directory: string,
  sourceDirs: readonly string[] = DEFAULT_SOURCE_DIRS,
): boolean {
  if (typeof file !== "string" || !file) return false;
  let rel = file;
  if (directory && rel.startsWith(directory + "/"))
    rel = rel.slice(directory.length + 1);
  rel = rel.replace(/^\.\//, "");
  if (!SOURCE_EXT.test(rel)) return false;
  if (EXCLUDE_RE.test(rel)) return false;
  return sourceDirs.some((dir) => rel.startsWith(dir));
}

type Outcome = {
  title: string;
  message: string;
  variant: "info" | "success" | "warning" | "error";
  warnOnly?: boolean;
};

export type SpawnResult = { exitCode: number; output: string } | null;

export function interpret(
  result: SpawnResult,
  onOutcome: (o: Outcome) => void,
): void {
  if (!result) {
    onOutcome({
      title: "fast-checks",
      message: "script did not run (spawn failed) - checks skipped",
      variant: "warning",
    });
    return;
  }
  const out = result.output;
  const failed = Array.from(out.matchAll(/^\s*\[FAIL\]\s+(.+?)\s+\d/gm)).map(
    (m) => m[1].trim(),
  );
  const summary = out.match(/pass=(\d+)\s+fail=(\d+)\s+skip=(\d+)/);
  const pass = summary ? Number(summary[1]) : 0;
  const skip = summary ? Number(summary[3]) : 0;
  const onlyBuildLocked =
    failed.length === 1 &&
    /backend build/i.test(failed[0]) &&
    CONTENTION_RE.test(out);
  if (onlyBuildLocked) {
    onOutcome({
      title: "fast-checks",
      message: "build locked by a concurrent dotnet process - skipped",
      variant: "info",
      warnOnly: true,
    });
    return;
  }
  if (result.exitCode === 0) {
    if (skip > 0 && pass === 0) {
      onOutcome({
        title: "fast-checks",
        message: "all checks skipped - nothing verified",
        variant: "warning",
      });
    } else {
      onOutcome({
        title: "fast-checks",
        message:
          skip > 0
            ? `passed - local drift gates green (${skip} step${skip === 1 ? "" : "s"} skipped)`
            : "passed - local drift gates green",
        variant: "success",
      });
    }
  } else {
    onOutcome({
      title: "fast-checks failed",
      message: failed.length
        ? `${failed.join(", ")} - run /fast-checks`
        : "run /fast-checks",
      variant: "error",
    });
  }
}

// Serialize runs: only one gate run at a time; edits landing mid-run trigger
// one trailing re-run so the latest state is always checked.
export function makeDrain(run: () => Promise<void>): () => Promise<void> {
  let inFlight = false;
  let dirty = false;
  return async () => {
    if (inFlight) {
      dirty = true;
      return;
    }
    do {
      dirty = false;
      inFlight = true;
      try {
        await run();
      } catch {
        // run is already non-fatal; belt-and-suspenders.
      } finally {
        inFlight = false;
      }
    } while (dirty);
  };
}

function execFilePromise(
  cmd: string,
  args: string[],
): Promise<{ stdout: string; stderr: string }> {
  return new Promise((res, rej) => {
    execFile(
      cmd,
      args,
      { maxBuffer: 32 * 1024 * 1024 },
      (err, stdout, stderr) =>
        err
          ? rej(Object.assign(err, { stdout: String(stdout), stderr: String(stderr) }))
          : res({ stdout: String(stdout), stderr: String(stderr) }),
    );
  });
}

export async function registerFastChecksHook(
  ctx: Pick<Plugin.Context, "location" | "tool" | "session" | "event">,
  options: { script: string; sourceDirs?: readonly string[] },
): Promise<() => void> {
  if (process.env.FAST_CHECKS_HOOK === "0") return () => {};

  const dir = ctx.location.directory ?? process.cwd();
  const sourceDirs = options.sourceDirs ?? DEFAULT_SOURCE_DIRS;
  const script = options.script.startsWith("/")
    ? options.script
    : `${dir}/${options.script}`;
  const edited = new Set<string>();

  const drain = makeDrain(async () => {
    let result: SpawnResult = null;
    try {
      const r = await execFilePromise("bash", [script]);
      result = { exitCode: 0, output: `${r.stdout}${r.stderr}` };
    } catch (e) {
      const err = e as { code?: number; stdout?: string; stderr?: string };
      if (typeof err.stdout === "string" || typeof err.stderr === "string") {
        result = {
          exitCode: typeof err.code === "number" ? err.code : 1,
          output: `${err.stdout ?? ""}${err.stderr ?? ""}`,
        };
      }
    }
    if (!result)
      console.warn("[fast-checks-hook] script did not run (spawn failed) - skipped");
    interpret(result, (o) => console.log(`[fast-checks-hook] ${o.title}: ${o.message}`));
  });

  await ctx.tool.hook("execute.after", (event) => {
    try {
      if (event.status !== "completed") return;
      if (!EDIT_TOOLS.has(event.tool)) return;
      const input = event.input as { filePath?: unknown } | undefined;
      const file = input?.filePath;
      if (isSourceEdit(file, dir, sourceDirs)) edited.add(file as string);
    } catch {
      // never throw out of the hook
    }
  });

  const controller = new AbortController();
  void (async () => {
    try {
      for await (const event of ctx.event.subscribe({
        signal: controller.signal,
      })) {
        if (event.type !== "session.execution.succeeded") continue;
        if (edited.size === 0) continue;
        const data = event.data as { sessionID?: string } | undefined;
        const sessionID =
          data?.sessionID ??
          (event.durable as { aggregateID?: string } | undefined)?.aggregateID;
        if (sessionID) {
          try {
            const info = (await ctx.session.get({ sessionID })) as {
              parentID?: string;
            } | undefined;
            if (info?.parentID) continue; // subagent - not our pause point
          } catch {
          }
        }
        edited.clear();
        void drain();
      }
    } catch {
      // stream aborted - never an unhandled rejection
    }
  })();

  return () => controller.abort();
}
