// Evidence-gated auto-merge machinery: the PR-body review contract (Merge
// Danger + Merge plan tier line) and the diff classifier whose allowlists
// come from the repo's .shipflow.json `autoMerge` block. The tool never
// merges directly - it flips the draft ready and lets `gh pr merge --auto`
// wait on the ruleset's required checks.
import type { AutoMergeConfig } from "../config.ts";

export type MergePlanTier = "auto-merge eligible" | "human review requested";

// Fences hide quoted examples so they cannot read as declarations; per
// CommonMark a close needs the same fence char, the opener's run length or
// more, no info string.
const FENCE_OPEN = /^ {0,3}(`{3,}|~{3,})/;
const FENCE_CLOSE = /^ {0,3}(`{3,}|~{3,})[ \t]*$/;

// Streams every line outside fenced code blocks with its original index, so
// body scanners share one fence interpretation.
export function* unfencedLines(
  body: string,
): Generator<{ index: number; line: string }> {
  const lines = body.split(/\r?\n/);
  let fence: string | null = null;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]!;
    const m: RegExpExecArray | null = (fence ? FENCE_CLOSE : FENCE_OPEN).exec(line);
    if (m) {
      if (!fence) fence = m[1]!;
      else if (m[1]![0] === fence[0] && m[1]!.length >= fence.length)
        fence = null;
      continue;
    }
    if (fence) continue;
    yield { index: i, line };
  }
}

export function parseMergePlanTier(body: string): MergePlanTier | null {
  let plan = false;
  for (const { line } of unfencedLines(body)) {
    if (!plan) {
      if (line.trimEnd() === "## Merge plan") plan = true;
      continue;
    }
    const trimmed = line.trim();
    if (trimmed === "") continue;
    return trimmed === "auto-merge eligible" ||
      trimmed === "human review requested"
      ? trimmed
      : null;
  }
  return null;
}

export function hasAutoMergeMarker(body: string): boolean {
  return parseMergePlanTier(body) === "auto-merge eligible";
}

export function bodyHasReviewContract(body: string): boolean {
  if (!body.split(/\r?\n/).some((l) => l.trim() === "## Merge Danger")) {
    return false;
  }
  return parseMergePlanTier(body) === "auto-merge eligible";
}

export type AutoMergeTier = "docs-only" | "test-only" | "docs-and-tests" | "mixed";

export function classifyPaths(
  paths: readonly string[],
  cfg: AutoMergeConfig,
): AutoMergeTier {
  if (paths.length === 0) return "mixed";
  let docs = false;
  let tests = false;
  for (const p of paths) {
    if (cfg.docsPrefixes.some((d: string) => p.startsWith(d))) docs = true;
    else if (!p.includes("/") && cfg.docsRootGlobs.some((g: string) => globTail(g, p)))
      docs = true;
    else if (
      cfg.testPrefixes.some((t: string) => p.startsWith(t)) &&
      !(cfg.testExclusionPrefixes ?? []).some((t: string) => p.startsWith(t))
    )
      tests = true;
    else return "mixed";
  }
  if (docs && tests) return "docs-and-tests";
  return docs ? "docs-only" : "test-only";
}

// "*.md" style tail glob against a basename.
function globTail(glob: string, name: string): boolean {
  const idx = glob.indexOf("*");
  if (idx === -1) return glob === name;
  const pre = glob.slice(0, idx);
  const post = glob.slice(idx + 1);
  return name.startsWith(pre) && name.endsWith(post) && name.length >= pre.length + post.length;
}

export interface AutoMergeProfileInput {
  readonly paths: readonly string[];
  readonly replanCount: number;
  readonly lastVerifyResult: string | null;
  readonly bodyHasContract: boolean;
  readonly requiredChecksPresent: boolean;
}

export interface AutoMergeProfileResult {
  readonly eligible: boolean;
  readonly tier: AutoMergeTier;
  readonly reasons: readonly string[];
}

export function evaluateAutoMergeProfile(
  input: AutoMergeProfileInput,
  cfg: AutoMergeConfig,
): AutoMergeProfileResult {
  const tier = classifyPaths(input.paths, cfg);
  const reasons: string[] = [];
  if (tier === "mixed") {
    reasons.push(
      "diff contains paths outside the docs/test allowlists (tier=mixed)",
    );
  }
  if (input.replanCount !== 0) reasons.push("replan_count must be 0");
  if (input.lastVerifyResult !== "pass")
    reasons.push("last_verify must be pass");
  if (!input.bodyHasContract) {
    reasons.push(
      "PR body missing the review contract (Merge Danger + Merge plan with the auto-merge eligible tier line)",
    );
  }
  if (!input.requiredChecksPresent) {
    reasons.push("ruleset required_status_checks absent");
  }
  return { eligible: reasons.length === 0, tier, reasons };
}
