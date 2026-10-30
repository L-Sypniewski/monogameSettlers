import { z } from "zod";
import { defineTool } from "../define-tool.ts";
import { evaluateGate } from "./loop";
import {
  refreshConfig,
  autoMergeRef,
  autoMergeArmed,
  repoSlug,
} from "../config.ts";
import {
  bodyHasReviewContract,
  evaluateAutoMergeProfile,
} from "./auto-merge.ts";
import { bodyHasFinalEli5 } from "./pr-body.ts";
import {
  createWiredIssue,
  failureSummary,
  ghRunner,
} from "./issue-intake";

export const create_pr = defineTool({
  description:
    "Create a DRAFT PR via gh, but ONLY after the loop gate passes (replan_count<=3 and last_verify=pass). " +
    "If the gate fails, returns a BLOCKED reason and does NOT run gh. The PR body must end with a " +
    "non-empty ## ELI5 section or the call is BLOCKED the same way.",
  args: {
    slug: z.string(),
    title: z.string(),
    body: z.string(),
    head: z.string(),
    base: z.string(),
  },
  async execute(args, context) {
    if (!bodyHasFinalEli5(args.body)) {
      return {
        title: "ship_create_pr - BLOCKED",
        output:
          "BLOCKED: the PR body does not end with a non-empty `## ELI5` section (3-6 plain-language sentences, " +
          "no code identifiers or jargon), which the ship agent's step 5.1 PR-body requirement mandates " +
          "(`.opencode/agents/ship.md`). Append a final `## ELI5` section at the very end of the body and retry.",
        metadata: {
          shipped: false,
          eli5: false,
        },
      };
    }
    const g = evaluateGate(context.worktree, args.slug);
    if (!g.may_ship) {
      const lv = g.last_verify ? g.last_verify.result : "none";
      return {
        title: "ship_create_pr - BLOCKED",
        output: `BLOCKED: gate failed (replan_count=${g.replan_count}, last_verify=${lv})`,
        metadata: {
          shipped: false,
          replan_count: g.replan_count,
          last_verify: g.last_verify,
        },
      };
    }
    try {
      // argv form keeps Bun.$'s no-shell-substitution guarantee and adds
      // session-abort propagation the tagged template cannot express.
      const proc = Bun.spawn(
        [
          "gh",
          "pr",
          "create",
          "--draft",
          "--title",
          args.title,
          "--body",
          args.body,
          "--head",
          args.head,
          "--base",
          args.base,
        ],
        {
          cwd: context.worktree,
          stdout: "pipe",
          stderr: "pipe",
        },
      );
      const onAbort = () => proc.kill();
      context.signal?.addEventListener("abort", onAbort, { once: true });
      let out: string;
      try {
        out = await new Response(proc.stdout).text();
      } finally {
        context.signal?.removeEventListener("abort", onAbort);
      }
      const url = out.trim().split("\n").pop()!.trim();
      const pr = /\/pull\/(\d+)/.exec(url);
      return {
        title: "ship_create_pr",
        output: `created draft PR: ${url}`,
        metadata: {
          shipped: true,
          url,
          prNumber: pr ? Number(pr[1]) : undefined,
          slug: args.slug,
        },
      };
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      return {
        title: "ship_create_pr - gh error",
        output: `gh pr create failed: ${msg}`,
        metadata: { shipped: false, error: msg },
      };
    }
  },
});

export const create_followup_issue = defineTool({
  description:
    "Create a GitHub follow-up issue via gh issue create (Won't items (real but out-of-scope) + " +
    "intake-triaged hatch-eligible items ONLY - genuinely substantial (fixing inline would roughly " +
    "double the PR) or truly out of the goal's scope). Everything else non-blocking (Could findings, " +
    "observations) is fixed in-branch per the shared fix-now rule - only mechanically unfixable " +
    "leftovers (test-breaking after genuine fix attempts, environment-blocked) land on the PR's " +
    "non-blocking checklist; each creation is ask-gated per item (or a user pre-approved batch), " +
    "never auto-created. " +
    "The issue is ALWAYS added to Projects board #3 with Status=Todo; parent sub-issue link, " +
    "Epic/Size fields, labels, and blocked-by edges are wired when the optional wiring args " +
    "are provided. The authoritative field schema is at the repo's issue-schema doc.",
  args: {
    slug: z.string(),
    title: z.string(),
    body: z.string(),
    parentIssueNumber: z.number().optional(),
    epic: z
      .string()
      .optional()
      .describe(
        "Epic single-select option name on board #3, e.g. 'Agentic Workflows'",
      ),
    priority: z
      .enum(["Urgent", "High", "Medium", "Low"])
      .optional()
      .describe("Human-owned: pass ONLY when a human explicitly provided it"),
    size: z.enum(["XS", "S", "M", "L", "XL"]).optional(),
    labels: z
      .array(z.string())
      .optional()
      .describe(
        "Agent-settable at creation: exactly the label vocabulary the repo declares in .shipflow.json (agentLabels); anything else is rejected before any gh call. 'ship-ready' is applied post-creation, add-only, with a provenance comment (human, readiness-audit under manual invocation, or an agent that authored the issue / just resolved its blocker) - never settable through agent issue-creation calls; 'needs-spec' is dispatcher-applied when intake fails quality. The authoritative field schema is the repo's issue-schema doc.",
      ),
    blockedBy: z.array(z.number()).optional(),
    dryRun: z
      .boolean()
      .optional()
      .describe(
        "Plan only: return the exact command sequence without executing anything",
      ),
  },
  async execute(args, context) {
    refreshConfig(context.worktree);
    try {
      const result = await createWiredIssue(
        {
          worktree: context.worktree,
          run: ghRunner(context.worktree, context.signal),
        },
        {
          title: args.title,
          body: args.body,
          parentIssueNumber: args.parentIssueNumber,
          epic: args.epic,
          priority: args.priority,
          size: args.size,
          labels: args.labels,
          blockedBy: args.blockedBy,
          dryRun: args.dryRun,
        },
      );
      if (result.dryRun) {
        return {
          title: "ship_create_followup_issue",
          output: `dry-run: ${result.planned?.length ?? 0} planned commands, nothing executed`,
          metadata: { slug: args.slug, wiring: result },
        };
      }
      const wired = result.steps.filter((s) => s.status === "wired").length;
      const skipped = result.steps.filter((s) => s.status === "skipped").length;
      const failedSteps = result.steps.filter((s) => s.status === "failed");
      if (result.ok) {
        return {
          title: "ship_create_followup_issue",
          output: `created issue: ${result.url} (wired=${wired} skipped=${skipped} failed=${failedSteps.length} - failed steps need manual retry per .agents/skills/github-projects)`,
          metadata: { url: result.url, slug: args.slug, wiring: result },
        };
      }
      return {
        title: "ship_create_followup_issue - gh error",
        output: failureSummary(result),
        metadata: {
          error: failedSteps[0]?.detail ?? "no failed step recorded",
          slug: args.slug,
          wiring: result,
        },
      };
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      return {
        title: "ship_create_followup_issue - gh error",
        output: `gh issue create failed: ${msg}`,
        metadata: { error: msg },
      };
    }
  },
});

// jq as a string constant so the pipes stay argv-escaped by interpolation,
// not shell-parsed. Counts required-status-check entries among the rules
// GitHub applies to the PR head branch (rulesets matched by branch pattern).
const JQ_REQUIRED_CHECKS =
  '[.[]? | select(.type == "required_status_checks") | .parameters.required_status_checks[]?] | length';

// A sequential gh chain must never hold the tool unsettled until the session
// runtime interrupts it: a hung command (gh against a half-broken auth
// state) becomes a normal non-zero result, and every spawn leaves a trace in
// the host log so an empty abort envelope always has a correlatable cause.
const DEFAULT_SPAWN_TIMEOUT_MS = 120_000;

function spawnTimeoutMs(): number {
  const v = Number(process.env.SHIPFLOW_SPAWN_TIMEOUT_MS);
  return Number.isFinite(v) && v > 0 ? v : DEFAULT_SPAWN_TIMEOUT_MS;
}

export async function spawnText(
  argv: string[],
  worktree: string,
  signal: AbortSignal | undefined,
  timeoutMs: number = spawnTimeoutMs(),
): Promise<{ exitCode: number; stdout: string; stderr: string }> {
  const label = argv.join(" ");
  if (signal?.aborted) {
    return {
      exitCode: 134,
      stdout: "",
      stderr: `session aborted before spawn: ${label}`,
    };
  }
  const startedAt = Date.now();
  const proc = Bun.spawn(argv, {
    cwd: worktree,
    stdout: "pipe",
    stderr: "pipe",
  });
  const onAbort = () => proc.kill();
  signal?.addEventListener("abort", onAbort, { once: true });
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    proc.kill();
  }, timeoutMs);
  let result: { exitCode: number; stdout: string; stderr: string };
  try {
    const stdout = await new Response(proc.stdout).text();
    const stderr = await new Response(proc.stderr).text();
    const exitCode = await proc.exited;
    result = timedOut
      ? {
          exitCode: 124,
          stdout,
          stderr: `timed out after ${timeoutMs}ms: ${label}\n${stderr}`.trimEnd(),
        }
      : { exitCode, stdout, stderr };
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", onAbort);
  }
  console.log(
    `[shipflow] ${label} -> exit ${result.exitCode} (${Date.now() - startedAt}ms)`,
  );
  return result;
}

// gh's stderr is the only diagnostic a failing spawn carries; bounded so a
// verbose failure cannot flood the tool result.
function why(stderr: string): string {
  const t = stderr.trim();
  if (t === "") return "";
  return t.length <= 240 ? `: ${t}` : `: ${t.slice(0, 240)}...`;
}

export const finalize_pr = defineTool({
  description:
    "Flip a draft PR ready via gh pr ready, then enable evidence-gated auto-merge (--auto --squash) " +
    "and enables evidence-gated auto-merge (--auto --squash) ONLY when the repo's auto-merge risk " +
    "profile (.shipflow.json autoMerge) fully passes: zero-loop gate pass, docs/test-only diff, " +
    "PR-body review contract, and repo ruleset required_status_checks present. The ready-flip runs " +
    "on the loop gate alone; the merge arm stays off until autoMerge is configured (or the plugin " +
    "option disables it). Never merges directly.",
  args: {
    slug: z.string(),
    prNumber: z.number(),
    base: z.string(),
  },
  async execute(args, context) {
    const wt = context.worktree;
    if (context.signal?.aborted) {
      return {
        title: "ship_finalize_pr - aborted",
        output:
          "session aborted before finalize started; nothing was run (no ready-flip, no merge arm)",
        metadata: {
          flipped: false,
          autoMergeEnabled: false,
          tier: undefined,
          reasons: ["session aborted before finalize started"],
        },
      };
    }
    try {
      // Config + gate stay inside the try: a malformed .shipflow.json must
      // surface as this tool's own graceful error, never as an escaping
      // throw the runtime reports as an interrupted tool.
      refreshConfig(wt);
      const g = evaluateGate(wt, args.slug);
      if (!g.may_ship) {
        const lv = g.last_verify ? g.last_verify.result : "none";
        return {
          title: "ship_finalize_pr - BLOCKED",
          output: `BLOCKED: gate failed (replan_count=${g.replan_count}, last_verify=${lv})`,
          metadata: {
            flipped: false,
            autoMergeEnabled: false,
            tier: undefined,
            reasons: [
              `ship gate failed (replan_count=${g.replan_count}, last_verify=${lv})`,
            ],
          },
        };
      }
      const cfg = autoMergeRef();
      const armed = cfg !== undefined && autoMergeArmed();
      const diffRes = await spawnText(
        [
          "git",
          "-c",
          "core.quotePath=false",
          "diff",
          `${args.base}...HEAD`,
          "--name-only",
        ],
        wt,
        context.signal,
      );
      if (diffRes.exitCode !== 0) {
        return {
          title: "ship_finalize_pr - git error",
          output: `git diff ${args.base}...HEAD failed (exit ${diffRes.exitCode})${why(diffRes.stderr)}; refusing to finalize without the diff`,
          metadata: {
            flipped: false,
            autoMergeEnabled: false,
            tier: undefined,
            reasons: ["diff unavailable"],
          },
        };
      }
      const paths = diffRes.stdout
        .split("\n")
        .map((l) => l.trim())
        .filter((l) => l !== "");

      const prRes = await spawnText(
        [
          "gh",
          "pr",
          "view",
          String(args.prNumber),
          "--json",
          "body,headRefOid,headRefName,baseRefName",
        ],
        wt,
        context.signal,
      );
      if (prRes.exitCode !== 0) {
        return {
          title: "ship_finalize_pr - gh error",
          output: `gh pr view ${args.prNumber} failed (exit ${prRes.exitCode})${why(prRes.stderr)}; cannot check the review contract`,
          metadata: {
            flipped: false,
            autoMergeEnabled: false,
            tier: undefined,
            reasons: ["PR body unreadable"],
          },
        };
      }
      const pr = JSON.parse(prRes.stdout) as {
        body: string;
        headRefOid: string;
        headRefName: string;
        baseRefName: string;
      };
      const hasContract = bodyHasReviewContract(pr.body);

      if (!armed) {
        // Flip-only path: no classifier/ruleset/merge work when the arm is
        // off (unconfigured repo or the plugin kill switch).
        const readyFlip = await spawnText(
          ["gh", "pr", "ready", String(args.prNumber)],
          wt,
          context.signal,
        );
        let flippedNow = readyFlip.exitCode === 0;
        let noteNow = "";
        if (!flippedNow) {
          const out = `${readyFlip.stdout} ${readyFlip.stderr}`;
          if (/already (?:ready|in a non-draft)|not a draft/i.test(out)) {
            flippedNow = true;
            noteNow = " (already ready)";
          }
        }
        return {
          title: "ship_finalize_pr",
          output: `flipped=${flippedNow}${noteNow} autoMergeEnabled=false reasons=1`,
          metadata: {
            flipped: flippedNow,
            autoMergeEnabled: false,
            tier: undefined,
            reasons: [
              cfg
                ? "auto-merge disabled (plugin option)"
                : "auto-merge not configured (.shipflow.json autoMerge)",
            ],
          },
        };
      }

      const rulesRes = await spawnText(
        [
          "gh",
          "api",
          `repos/${repoSlug(wt)}/rules/branches/${pr.headRefName}`,
          "--jq",
          JQ_REQUIRED_CHECKS,
        ],
        wt,
        context.signal,
      );
      let requiredChecksPresent = false;
      const extraReasons: string[] = [];
      if (rulesRes.exitCode !== 0) {
        // Fail-closed: --auto degenerates to an immediate merge when nothing
        // waits on it - an unreadable rules answer must never arm it.
        extraReasons.push(
          `branch rules unreadable (gh exit ${rulesRes.exitCode})`,
        );
      } else {
        const n = Number(rulesRes.stdout.trim());
        requiredChecksPresent = Number.isFinite(n) && n > 0;
      }

      // The profile classifies the LOCAL worktree while gh merges the pushed
      // PR head, so identity mismatches must skip the merge arm, not the
      // ready-flip.
      const headRes = await spawnText(
        ["git", "rev-parse", "HEAD"],
        wt,
        context.signal,
      );
      let headMatches = false;
      if (headRes.exitCode !== 0) {
        extraReasons.push(
          `git rev-parse HEAD failed (exit ${headRes.exitCode}) - merge arm skipped`,
        );
      } else {
        const localHead = headRes.stdout;
        headMatches = pr.headRefOid === localHead.trim();
        if (!headMatches) {
          extraReasons.push(
            `PR head ${pr.headRefOid} !== local HEAD ${localHead.trim()} - merge arm skipped`,
          );
        }
      }
      const baseMatches = pr.baseRefName === args.base;
      if (!baseMatches) {
        extraReasons.push(
          `PR base ${pr.baseRefName} !== ${args.base} - merge arm skipped`,
        );
      }

      const profile = evaluateAutoMergeProfile(
        {
          paths,
          replanCount: g.replan_count,
          lastVerifyResult: g.last_verify?.result ?? null,
          bodyHasContract: hasContract,
          requiredChecksPresent,
        },
        cfg,
      );

      const readyRes = await spawnText(
        ["gh", "pr", "ready", String(args.prNumber)],
        wt,
        context.signal,
      );
      let flipped = readyRes.exitCode === 0;
      let note = "";
      if (!flipped) {
        const readyOut = `${readyRes.stdout} ${readyRes.stderr}`;
        if (/already (?:ready|in a non-draft)|not a draft/i.test(readyOut)) {
          flipped = true;
          note = " (already ready)";
        }
      }

      // A refused tier is a normal outcome (a pipeline-code PR must land
      // ready-only).
      let autoMergeEnabled = false;
      if (
        flipped &&
        profile.eligible &&
        requiredChecksPresent &&
        headMatches &&
        baseMatches
      ) {
        const mergeRes = await spawnText(
          ["gh", "pr", "merge", String(args.prNumber), "--auto", "--squash"],
          wt,
          context.signal,
        );
        autoMergeEnabled = mergeRes.exitCode === 0;
        if (!autoMergeEnabled) {
          extraReasons.push(
            `gh pr merge --auto --squash failed (exit ${mergeRes.exitCode})${why(mergeRes.stderr)}`,
          );
        }
      }

      const reasons = [...profile.reasons, ...extraReasons];
      if (!flipped) {
        reasons.push(
          `gh pr ready failed (exit ${readyRes.exitCode})${why(readyRes.stderr)}`,
        );
      }
      return {
        title: "ship_finalize_pr",
        output: `flipped=${flipped}${note} autoMergeEnabled=${autoMergeEnabled} tier=${profile.tier} reasons=${reasons.length}`,
        metadata: { flipped, autoMergeEnabled, tier: profile.tier, reasons },
      };
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      return {
        title: "ship_finalize_pr - gh error",
        output: `finalize failed: ${msg}`,
        metadata: {
          flipped: false,
          autoMergeEnabled: false,
          tier: undefined,
          reasons: [msg],
        },
      };
    }
  },
});
