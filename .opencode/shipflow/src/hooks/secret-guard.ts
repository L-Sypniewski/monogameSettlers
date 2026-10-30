// Aborts any tool call whose arguments reference a gitignored (secret/local)
// path - `git check-ignore` against the repo's own .gitignore files is the
// single source of truth, so no secret list is maintained. Fails open only
// on spawn failure; a poisoned batch (exit 128) falls back to per-path
// checks. Text-content fields (content/old_string/...) are stripped before
// scanning - mentioning a path is not touching it; apply_patch file headers
// survive the strip. Runtime-assembled paths (shell expansion, mid-token
// globs) are out of scope for a tool hook.
import { spawnSync } from "node:child_process";
import type { Plugin } from "@opencode/plugin";
import { realpathSync } from "node:fs";
import { basename, dirname, isAbsolute, join, relative, resolve } from "node:path";

const PATH_TOKEN = /[^\s"'`|;&<>(){}\[\],$]+/g;
const NON_SECRET_DIR =
  /(?:^|\/)(?:node_modules|vendor|\.?venv|dist|build|bin|obj|coverage|\.git|\.next|\.turbo|__pycache__|\.cache|target|out|release|graphify-out|\.plans|docs\/reviews|\.pnpm-store|\.graphify-venv|tmp\/act-artifacts|\.playwright-cli|\.astro|test-results[^\/]*|TestResults|playwright-report[^\/]*|frontend\/tests\/\.start-stack-regression-[^\/]*|scripts\/agentic\/results|[^\/]*\.egg-info)(?:\/|$)/;
// A gitignored FILE (not a dir) - the dir-segment regex above can't match it.
const NON_SECRET_FILE =
  /(?:^|\/)(?:[^\/]*\.py[cod]|[^\/]*\.log|[^\/]*\.onnx|[^\/]*\.dcp\.json|aspire-manifest\.json|scripts\/benchmark\/results\/logs|\.parallel-stacks\.json|\.system-stack\.json)$/;
// .gitignore's .env family (.env, .env.local, .env.*.local, unanchored, incl. dir
// form dist/.env/) always wins over the dir carve-out: re-flagged even under
// dist/ or node_modules/.
const SECRET_FILE = /(?:^|\/)\.env(?:[\/.]|$)/;
const CONTENT_KEYS = new Set([
  "content",
  "old_string",
  "oldString",
  "new_string",
  "newString",
  "patchText",
  "diff",
]);

// apply_patch header lines carry the touched paths - the guard is the patch
// tool's only automated layer, so these survive the patchText strip.
const PATCH_FILE_HEADER =
  /\*\*\* (?:Add File|Update File|Delete File|Move to): (\S+)/g;

const TRAILING_GLOB = /[*?[\]]+$/;

function pathCandidates(text: string): string[] {
  const out = new Set<string>();
  for (const m of text.matchAll(PATH_TOKEN)) {
    if (m[0].includes("/") || m[0].includes(".")) {
      out.add(m[0]);
      // check-ignore takes literals while bash expands globs: `backend/.env*`
      // misses as a literal, so probe the de-globged spelling as well.
      const literal = m[0].replace(TRAILING_GLOB, "");
      if (literal) out.add(literal);
    }
  }
  return [...out];
}

// Text-content fields mention secret paths without touching them (docs quoting
// `.act/.secrets`); strip them so only path-bearing fields are scanned - except
// patchText, which keeps its file-header lines (see PATCH_FILE_HEADER).
function scanText(args: unknown): string {
  if (typeof args === "string") return args;
  if (args === null || typeof args !== "object") return "";
  const stripped: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(args)) {
    if (!CONTENT_KEYS.has(key)) stripped[key] = value;
    else if (key === "patchText" && typeof value === "string")
      stripped[key] = value.match(PATCH_FILE_HEADER)?.join(" ");
  }
  return JSON.stringify(stripped);
}

// git reports the root symlink-resolved (macOS /tmp -> /private/tmp) while the
// session dir and tool paths may use the alias; candidates need not exist, so
// the longest existing ancestor is resolved and the remainder re-appended.
function canonical(p: string): string {
  const tail: string[] = [];
  let cur = resolve(p);
  for (;;) {
    try {
      return join(realpathSync.native(cur), ...tail.reverse());
    } catch {
      const parent = dirname(cur);
      if (parent === cur) return resolve(p);
      tail.push(basename(cur));
      cur = parent;
    }
  }
}

// Relatives arrive session-dir-relative (a backend/ cwd's
// `supabase/signing_keys.json`): resolve against the session dir first, then
// relativize against the git root, so sub-dir relatives and in-repo `../` forms
// map to the right root-relative path before check-ignore.
function repoRelative(
  candidates: string[],
  root: string,
  dir: string,
): string[] {
  const out: string[] = [];
  const realRoot = canonical(root);
  const realDir = canonical(dir);
  for (const candidate of candidates) {
    const abs = canonical(isAbsolute(candidate) ? candidate : resolve(realDir, candidate));
    const rel = relative(realRoot, abs);
    if (rel === "" || rel.startsWith("..")) continue; // out-of-repo / `..` escape
    out.push(rel);
  }
  return out;
}

// null = spawn failure (fail open), exitCode 0 = stdout lists ignored paths,
// 1 = nothing ignored, 128/other = poisoned batch.
type CheckRunner = (
  paths: string[],
) => { exitCode: number; stdout: string } | null | Promise<{ exitCode: number; stdout: string } | null>;

export async function guardToolArgs(
  args: unknown,
  root: string,
  dir: string,
  check: CheckRunner,
): Promise<void> {
  const paths = pathCandidates(scanText(args));
  if (paths.length === 0) return;

  const candidates = repoRelative(paths, root, dir);
  if (candidates.length === 0) return;

  const result = await check(candidates);
  if (!result) return;

  let ignored: string[] = [];
  if (result.exitCode === 0) {
    ignored = result.stdout.split("\n").filter(Boolean);
  } else if (result.exitCode === 1) {
    return; // nothing ignored
  } else {
    // 128/other: the batch is poisoned (e.g. one unresolvable path).
    // Per-path retry - a failing single check drops only its own path.
    for (const candidate of candidates) {
      const single = await check([candidate]);
      if (single?.exitCode === 0) {
        ignored.push(...single.stdout.split("\n").filter(Boolean));
      }
    }
  }

  const secrets = ignored.filter(
    (p) =>
      SECRET_FILE.test(p) ||
      (!NON_SECRET_DIR.test(p) && !NON_SECRET_FILE.test(p)),
  );
  if (secrets.length > 0) {
    throw new Error(
      `Refused: tool call references a gitignored (secret) path: ${secrets.join(", ")}. Use the committed *.example template instead.`,
    );
  }
}

export async function registerSecretGuard(
  ctx: Pick<Plugin.Context, "tool">,
  locationDirectory?: string,
): Promise<void> {
  {
    const dir = locationDirectory ?? process.cwd();
    let root = dir;
    try {
      const rev = spawnSync("git", ["-C", dir, "rev-parse", "--show-toplevel"], {
        encoding: "utf8",
      });
      const toplevel = rev.status === 0 ? rev.stdout.trim() : "";
      if (toplevel) root = toplevel;
    } catch {
      // not a repo / git missing: keep the session dir as the anchor
    }
    await ctx.tool.hook("execute.before", (event) => {
      void guardToolArgs(event.input, root, dir, (pathsToCheck) => {
        const r = spawnSync(
          "git",
          [
            "-c",
            "core.quotePath=false",
            "-C",
            root,
            "check-ignore",
            "--",
            ...pathsToCheck,
          ],
          { encoding: "utf8", maxBuffer: 16 * 1024 * 1024 },
        );
        if (r.error) return null;
        return { exitCode: r.status ?? 1, stdout: r.stdout ?? "" };
      });
    });
  }
}
