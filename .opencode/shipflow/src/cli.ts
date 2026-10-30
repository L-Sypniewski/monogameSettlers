// Shell transport for ship_finalize_pr. The plugin-tool result channel can
// break at the infrastructure level (result envelope never arrives, zero side
// effects) while ordinary shell commands keep working in the same session;
// the draft->ready flip must not strand on that. This entry runs the exact
// registered tool - same loop gate, same risk profile, same kill switch - and
// reports through stdout + exit code instead of the tool-result channel.
//
//   bun .opencode/shipflow/src/cli.ts finalize \
//     [--cwd <repo-root>] --slug <slug> --pr <number> --base <branch>
//
// Exit 0 iff the PR ended up ready (metadata.flipped). Consumer repos invoke
// the committed wrapper (.opencode/scripts/finalize-pr.sh) which heals the
// package install first; direct gh pr ready / gh pr merge stay denied - this
// is the gated path, not a bypass.
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { finalize_pr } from "./tools/ship.ts";
import { configureAutoMergeArm } from "./config.ts";
import type { ToolOutput } from "./define-tool.ts";

export interface FinalizeCliArgs {
  slug: string;
  prNumber: number;
  base: string;
  cwd: string;
}

export function parseFinalizeArgs(
  argv: readonly string[],
): FinalizeCliArgs | { error: string } {
  const out: Partial<FinalizeCliArgs> = { cwd: process.cwd() };
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    const eq = token.startsWith("--") && token.includes("=") ? token.indexOf("=") : -1;
    const flag = eq === -1 ? token : token.slice(0, eq);
    const take = (): string | undefined => {
      if (eq !== -1) return token.slice(eq + 1);
      const v = argv[i + 1];
      if (v === undefined || v.startsWith("--")) return undefined;
      i++;
      return v;
    };
    if (flag === "--slug") {
      const v = take();
      if (!v) return { error: "--slug requires a non-empty value" };
      out.slug = v;
    } else if (flag === "--pr") {
      const v = take();
      const n = Number(v);
      if (!v || !Number.isInteger(n) || n <= 0)
        return { error: `--pr requires a positive integer, got '${v ?? ""}'` };
      out.prNumber = n;
    } else if (flag === "--base") {
      const v = take();
      if (!v) return { error: "--base requires a non-empty value" };
      out.base = v;
    } else if (flag === "--cwd") {
      const v = take();
      if (!v) return { error: "--cwd requires a path" };
      out.cwd = v;
    } else if (flag === "finalize") {
      // subcommand (the wrapper injects it); nothing to capture
    } else {
      return { error: `unknown argument '${token}'` };
    }
  }
  if (!out.slug) return { error: "--slug is required" };
  if (out.prNumber === undefined) return { error: "--pr is required" };
  if (!out.base) return { error: "--base is required" };
  return out as FinalizeCliArgs;
}

// opencode.jsonc is JSON-with-comments; read it without a parser dependency.
// String-aware so comment markers and trailing commas inside string values
// survive.
function parseJsonc(source: string): unknown {
  // Next non-space, non-comment character at or after i (for the
  // trailing-comma decision, which must see through an interleaved comment:
  // `false, // note\n }`).
  const significant = (i: number): string => {
    let j = i;
    while (j < source.length) {
      const c = source[j];
      if (/\s/.test(c)) {
        j++;
        continue;
      }
      if (c === "/" && source[j + 1] === "/") {
        while (j < source.length && source[j] !== "\n") j++;
        continue;
      }
      if (c === "/" && source[j + 1] === "*") {
        j += 2;
        while (j < source.length && !(source[j] === "*" && source[j + 1] === "/")) j++;
        j += 2;
        continue;
      }
      return c;
    }
    return "";
  };
  let out = "";
  let i = 0;
  let str: '"' | "'" | undefined;
  while (i < source.length) {
    const c = source[i];
    if (str) {
      out += c;
      if (c === "\\") {
        out += source[i + 1] ?? "";
        i += 2;
        continue;
      }
      if (c === str) str = undefined;
      i++;
      continue;
    }
    if (c === '"' || c === "'") {
      str = c;
      out += c;
      i++;
      continue;
    }
    if (c === "/" && source[i + 1] === "/") {
      while (i < source.length && source[i] !== "\n") i++;
      continue;
    }
    if (c === "/" && source[i + 1] === "*") {
      i += 2;
      while (i < source.length && !(source[i] === "*" && source[i + 1] === "/")) i++;
      i += 2;
      continue;
    }
    if (c === "," && "}]".includes(significant(i + 1))) {
      i++;
      continue;
    }
    out += c;
    i++;
  }
  return JSON.parse(out);
}

// The plugin-option kill switch ("autoMerge": false in the shipflow plugins[]
// entry) is applied by plugin setup in the tool transport; the CLI transport
// must honor it too or a kill-switched repo would arm the merge arm here.
export function readAutoMergeKillSwitch(repoRoot: string): boolean | undefined {
  const path = ["opencode.jsonc", "opencode.json"]
    .map((f) => join(repoRoot, f))
    .find((p) => existsSync(p));
  if (!path) return undefined;
  let parsed: any;
  try {
    parsed = parseJsonc(readFileSync(path, "utf8"));
  } catch {
    return undefined;
  }
  const plugins = Array.isArray(parsed?.plugins) ? parsed.plugins : [];
  for (const entry of plugins) {
    const pkg = typeof entry?.package === "string" ? entry.package : "";
    if (pkg.includes("shipflow")) {
      const v = entry?.options?.autoMerge;
      return typeof v === "boolean" ? v : undefined;
    }
  }
  return undefined;
}

export interface CliOutcome {
  exitCode: number;
  report: string;
}

function render(result: string | ToolOutput | undefined): string {
  if (typeof result === "string" || result === undefined)
    return result ?? "(no output)";
  const flipped = result.metadata?.flipped;
  // Every outcome carries an explicit flipped= line: the error paths' own
  // output strings do not, and the shell consumer greps exactly this.
  const flipLine =
    typeof flipped === "boolean" ? `flipped=${flipped}` : undefined;
  const reasons = Array.isArray(result.metadata?.reasons)
    ? (result.metadata.reasons as unknown[])
        .map((r) => `  - ${String(r)}`)
        .join("\n")
    : "";
  return [result.title, result.output, flipLine, reasons]
    .filter(Boolean)
    .join("\n");
}

export async function runFinalizeCli(argv: readonly string[]): Promise<CliOutcome> {
  const parsed = parseFinalizeArgs(argv);
  if ("error" in parsed) {
    return {
      exitCode: 2,
      report:
        `usage: cli.ts finalize [--cwd <repo-root>] --slug <slug> --pr <number> --base <branch>\n` +
        `${parsed.error}`,
    };
  }
  configureAutoMergeArm(readAutoMergeKillSwitch(parsed.cwd));
  const signal = new AbortController().signal;
  try {
    const result = (await finalize_pr.execute(
      {
        slug: parsed.slug,
        prNumber: parsed.prNumber,
        base: parsed.base,
      },
      { worktree: parsed.cwd, signal, progress: async () => {} },
    )) as string | ToolOutput | undefined;
    const flipped =
      typeof result === "object" && result?.metadata?.flipped === true;
    return { exitCode: flipped ? 0 : 1, report: render(result) };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return { exitCode: 1, report: `ship_finalize_pr (shell transport) - error: ${msg}` };
  }
}

if (import.meta.main) {
  const outcome = await runFinalizeCli(process.argv.slice(2));
  console.log(outcome.report);
  process.exit(outcome.exitCode);
}
