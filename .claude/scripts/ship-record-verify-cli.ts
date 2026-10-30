// Claude Code has no plugin loader, so it cannot register OpenCode's
// plugin-provided `loop_record_verify` custom tool - the same gap
// `.claude/scripts/ship-create-pr-cli.ts` closes for `ship_create_pr` (see
// that file's header + `.opencode/scripts/sync-agents.ts`'s `key.endsWith("_*")`
// branch). Without a recorded verify pass, `ship_create_pr`'s gate
// (`evaluateGate`) always reports `last_verify=none` and refuses to ship,
// even when real tests were run and passed - there is no other way to write
// `.plans/<slug>.loop.json` from this runtime.
//
// This script closes that gap the same way: it calls the REAL `record_verify`
// tool from the installed `shipflow` package, so the written state
// is read by the identical `evaluateGate` the real `loop_gate_check`/
// `ship_create_pr` tools use - never a hand-rolled parallel state format.
//
//   bun .claude/scripts/ship-record-verify-cli.ts [--cwd <repo-root>] \
//     --slug <slug> --result <pass|fail> --command <description> \
//     [--sweep-commit-sha <sha>]
//
// Exit 0 on a successful record (any result value) and non-zero only on a
// usage/execution error - recording a true "fail" is itself a successful
// call. Consumers invoke the committed wrapper (.claude/scripts/ship-record-verify.sh).
import { existsSync } from "node:fs";
import { join } from "node:path";
import { record_verify } from "../../.opencode/shipflow/src/index.ts";

interface ShipToolOutput {
  title?: string;
  output?: string;
  metadata?: Record<string, unknown>;
}

export interface RecordVerifyCliArgs {
  slug: string;
  result: "pass" | "fail";
  command: string;
  sweepCommitSha?: string;
  cwd: string;
}

export function parseRecordVerifyArgs(
  argv: readonly string[],
): RecordVerifyCliArgs | { error: string } {
  const out: Partial<RecordVerifyCliArgs> = { cwd: process.cwd() };
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    const eq =
      token.startsWith("--") && token.includes("=") ? token.indexOf("=") : -1;
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
    } else if (flag === "--result") {
      const v = take();
      if (v !== "pass" && v !== "fail")
        return { error: `--result must be 'pass' or 'fail', got '${v ?? ""}'` };
      out.result = v;
    } else if (flag === "--command") {
      const v = take();
      if (!v) return { error: "--command requires a non-empty value" };
      out.command = v;
    } else if (flag === "--sweep-commit-sha") {
      const v = take();
      if (!v) return { error: "--sweep-commit-sha requires a value" };
      out.sweepCommitSha = v;
    } else if (flag === "--cwd") {
      const v = take();
      if (!v) return { error: "--cwd requires a path" };
      out.cwd = v;
    } else {
      return { error: `unknown argument '${token}'` };
    }
  }
  if (!out.slug) return { error: "--slug is required" };
  if (!out.result) return { error: "--result is required (pass|fail)" };
  if (!out.command) return { error: "--command is required" };
  return {
    slug: out.slug,
    result: out.result,
    command: out.command,
    sweepCommitSha: out.sweepCommitSha,
    cwd: out.cwd!,
  };
}

export interface CliOutcome {
  exitCode: number;
  report: string;
}

function render(result: string | ShipToolOutput | undefined): string {
  if (typeof result === "string" || result === undefined)
    return result ?? "(no output)";
  return [result.title, result.output].filter(Boolean).join("\n");
}

export async function runRecordVerifyCli(
  argv: readonly string[],
): Promise<CliOutcome> {
  const parsed = parseRecordVerifyArgs(argv);
  if ("error" in parsed) {
    return {
      exitCode: 2,
      report:
        `usage: ship-record-verify-cli.ts [--cwd <repo-root>] --slug <slug> ` +
        `--result <pass|fail> --command <description> [--sweep-commit-sha <sha>]\n` +
        `${parsed.error}`,
    };
  }
  if (!existsSync(join(parsed.cwd, ".plans"))) {
    return {
      exitCode: 1,
      report:
        `loop_record_verify (Claude Code shell transport) - error: no .plans directory at ` +
        `'${join(parsed.cwd, ".plans")}'; --cwd may be wrong`,
    };
  }
  try {
    const result = (await record_verify.execute(
      {
        slug: parsed.slug,
        result: parsed.result,
        command: parsed.command,
        ...(parsed.sweepCommitSha
          ? { sweep_commit_sha: parsed.sweepCommitSha }
          : {}),
      },
      {
        worktree: parsed.cwd,
        signal: new AbortController().signal,
        progress: async () => {},
      },
    )) as string | ShipToolOutput | undefined;
    return { exitCode: 0, report: render(result) };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return {
      exitCode: 1,
      report: `loop_record_verify (Claude Code shell transport) - error: ${msg}`,
    };
  }
}

if (import.meta.main) {
  const outcome = await runRecordVerifyCli(process.argv.slice(2));
  console.log(outcome.report);
  process.exit(outcome.exitCode);
}
