// Claude Code has no plugin loader, so it cannot register OpenCode's
// plugin-provided `ship_create_pr` custom tool the way `ship_finalize_pr`
// is also missing there. `.opencode/scripts/sync-agents.ts` mirrors the
// `gh pr create*` deny into Claude Code's rules (it mirrors agents/
// commands/rules, which are plain files) but drops every custom tool during
// that mirror - see sync-agents.ts's own `key.endsWith("_*")` branch, which
// explicitly treats a `ship_*`-shaped permission key as "not an MCP server"
// and emits nothing for it. That leaves Claude Code's `ship` agent denied
// `gh pr create` with no substitute to call instead.
//
// This script closes the gap the same way `.opencode/scripts/finalize-pr.sh`
// + `cli.ts`'s `finalize` subcommand already close it for `ship_finalize_pr`:
// it calls the REAL `create_pr.execute` from the installed `shipflow`
// package - same loop gate via `evaluateGate`, same ELI5 check via
// `bodyHasFinalEli5`, same `gh pr create --draft` invocation - and reports
// the outcome through stdout + exit code instead of a tool-result channel.
// It is never a reimplementation of the gate or the gh call.
//
//   bun .claude/scripts/ship-create-pr-cli.ts [--cwd <repo-root>] \
//     --slug <slug> --title <title> (--body <body> | --body-file <path>) \
//     --head <branch> --base <branch>
//
// Exit 0 iff the PR was created (metadata.shipped). Consumers invoke the
// committed wrapper (.claude/scripts/ship-create-pr.sh), which heals the
// package install first; direct `gh pr create` stays denied - this is the
// gated path, not a bypass.
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
// A bare `shipflow` specifier would need a `node_modules` ancestor
// directory reachable from this file's own path to resolve (Bun's resolver
// walks up from the importing file, not from cwd) - there is none above
// `.claude/scripts/`, only under `.opencode/node_modules/`. A relative path
// to the installed package's barrel module sidesteps that without adding
// any files; it is still the barrel (src/index.ts), not a blocked subpath -
// the package's exports map only gates bare-specifier subpaths.
import { create_pr } from "../../.opencode/shipflow/src/index.ts";

// Local structural type: `shipflow`'s package.json exports map
// only publishes "." and "./sync" subpaths, so a type-only import of the
// package's own `ToolOutput` (a relative-path type inside the package) is
// blocked from outside. The real return value is a plain object matching
// this shape, so a duck-typed interface works identically with zero
// behavior change.
interface ShipToolOutput {
  title?: string;
  output?: string;
  metadata?: Record<string, unknown>;
}

export interface CreatePrCliArgs {
  slug: string;
  title: string;
  body: string;
  head: string;
  base: string;
  cwd: string;
}

export function parseCreatePrArgs(
  argv: readonly string[],
): CreatePrCliArgs | { error: string } {
  const out: Partial<CreatePrCliArgs> & { bodyFile?: string } = {
    cwd: process.cwd(),
  };
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
    } else if (flag === "--title") {
      const v = take();
      if (!v) return { error: "--title requires a non-empty value" };
      out.title = v;
    } else if (flag === "--body") {
      const v = take();
      if (v === undefined) return { error: "--body requires a value" };
      out.body = v;
    } else if (flag === "--body-file") {
      const v = take();
      if (!v) return { error: "--body-file requires a path" };
      out.bodyFile = v;
    } else if (flag === "--head") {
      const v = take();
      if (!v) return { error: "--head requires a non-empty value" };
      out.head = v;
    } else if (flag === "--base") {
      const v = take();
      if (!v) return { error: "--base requires a non-empty value" };
      out.base = v;
    } else if (flag === "--cwd") {
      const v = take();
      if (!v) return { error: "--cwd requires a path" };
      out.cwd = v;
    } else {
      return { error: `unknown argument '${token}'` };
    }
  }
  if (!out.slug) return { error: "--slug is required" };
  if (!out.title) return { error: "--title is required" };
  if (out.body === undefined && !out.bodyFile)
    return { error: "exactly one of --body or --body-file is required" };
  if (out.body !== undefined && out.bodyFile)
    return { error: "pass only one of --body or --body-file, not both" };
  if (!out.head) return { error: "--head is required" };
  if (!out.base) return { error: "--base is required" };
  if (out.bodyFile) {
    if (!existsSync(out.bodyFile))
      return { error: `--body-file path does not exist: '${out.bodyFile}'` };
    out.body = readFileSync(out.bodyFile, "utf8");
  }
  return {
    slug: out.slug,
    title: out.title,
    body: out.body!,
    head: out.head,
    base: out.base,
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
  const shipped = result.metadata?.shipped;
  // Every outcome carries an explicit shipped= line: the shell consumer
  // greps exactly this to decide success vs BLOCKED/error.
  const shippedLine =
    typeof shipped === "boolean" ? `shipped=${shipped}` : undefined;
  const url =
    typeof result.metadata?.url === "string" ? result.metadata.url : undefined;
  const urlLine = url ? `url=${url}` : undefined;
  return [result.title, result.output, shippedLine, urlLine]
    .filter(Boolean)
    .join("\n");
}

export async function runCreatePrCli(
  argv: readonly string[],
): Promise<CliOutcome> {
  const parsed = parseCreatePrArgs(argv);
  if ("error" in parsed) {
    return {
      exitCode: 2,
      report:
        `usage: ship-create-pr-cli.ts [--cwd <repo-root>] --slug <slug> --title <title> ` +
        `(--body <body> | --body-file <path>) --head <branch> --base <branch>\n` +
        `${parsed.error}`,
    };
  }
  // The gate reads .plans/<slug>.loop.json; a missing .plans directory means
  // the wrong --cwd was given, not a genuine gate failure - fail with a
  // clear message instead of letting create_pr.execute report a confusing
  // "no loop state" BLOCKED.
  if (!existsSync(join(parsed.cwd, ".plans"))) {
    return {
      exitCode: 1,
      report:
        `ship_create_pr (Claude Code shell transport) - error: no .plans directory at ` +
        `'${join(parsed.cwd, ".plans")}'; --cwd may be wrong (loop state for this slug lives at ` +
        `.plans/${parsed.slug}.loop.json)`,
    };
  }
  try {
    const result = (await create_pr.execute(
      {
        slug: parsed.slug,
        title: parsed.title,
        body: parsed.body,
        head: parsed.head,
        base: parsed.base,
      },
      {
        worktree: parsed.cwd,
        signal: new AbortController().signal,
        progress: async () => {},
      },
    )) as string | ShipToolOutput | undefined;
    const shipped =
      typeof result === "object" && result?.metadata?.shipped === true;
    return { exitCode: shipped ? 0 : 1, report: render(result) };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return {
      exitCode: 1,
      report: `ship_create_pr (Claude Code shell transport) - error: ${msg}`,
    };
  }
}

if (import.meta.main) {
  const outcome = await runCreatePrCli(process.argv.slice(2));
  console.log(outcome.report);
  process.exit(outcome.exitCode);
}
