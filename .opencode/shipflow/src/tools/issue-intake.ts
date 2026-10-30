import { z } from "zod";
import { defineTool } from "../define-tool.ts";
import {
  refreshConfig,
  boardRef,
  WORKFLOW_RESERVED_LABELS,
} from "../config.ts";

export type GhRunner = (
  argv: string[],
) => Promise<{ stdout: string; exitCode: number }>;

export type CreateWiredIssueDeps = {
  worktree: string;
  run?: GhRunner;
};

export type CreateWiredIssueInput = {
  title: string;
  body: string;
  parentIssueNumber?: number;
  epic?: string;
  priority?: "Urgent" | "High" | "Medium" | "Low";
  size?: "XS" | "S" | "M" | "L" | "XL";
  labels?: string[];
  blockedBy?: number[];
  dryRun?: boolean;
};

export type WiringStep = {
  step: string;
  status: "wired" | "skipped" | "failed" | "unknown";
  detail: string;
};

export type WiredIssueResult = {
  ok: boolean;
  dryRun?: boolean;
  url?: string;
  number?: number;
  planned?: string[];
  steps: WiringStep[];
};

type RunOutcome = { stdout: string; stderr: string; exitCode: number };

// The exported runner contract carries only what test fakes need to provide;
// the default runner additionally returns stderr so failure details quote it.
async function runGh(run: GhRunner, argv: string[]): Promise<RunOutcome> {
  const res = await run(argv);
  return {
    stdout: res.stdout,
    stderr: (res as { stderr?: string }).stderr ?? "",
    exitCode: res.exitCode,
  };
}

export function ghRunner(worktree: string, signal?: AbortSignal): GhRunner {
  return async (argv: string[]) => {
    // Bun.$ accepts only tagged templates and this helper builds argv dynamically,
    // so spawn directly; the argv form keeps the same no-shell-substitution guarantee.
    const proc = Bun.spawn(argv, {
      cwd: worktree,
      stdout: "pipe",
      stderr: "pipe",
    });
    // Session abort (stop button) kills the in-flight gh call.
    const onAbort = () => proc.kill();
    signal?.addEventListener("abort", onAbort, { once: true });
    try {
      const stdout = await new Response(proc.stdout).text();
      const stderr = await new Response(proc.stderr).text();
      const exitCode = await proc.exited;
      return { stdout, stderr, exitCode };
    } finally {
      signal?.removeEventListener("abort", onAbort);
    }
  };
}

function parseJson<T>(raw: string): T | undefined {
  try {
    return JSON.parse(raw) as T;
  } catch {
    return undefined;
  }
}

function snippet(out: RunOutcome): string {
  const text = (out.stderr || out.stdout || "gh exited without output").trim();
  return text.length > 200 ? `${text.slice(0, 200)}...` : text;
}

function errMsg(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

function lastNonEmptyLine(text: string): string {
  const lines = text.trim().split("\n");
  for (let i = lines.length - 1; i >= 0; i--) {
    if (lines[i].trim() !== "") return lines[i].trim();
  }
  return "";
}

const LABEL_REJECTION_REASONS: Record<string, string> = {
  "ship-ready":
    "queue signal applied post-creation, add-only, with a provenance comment (human, readiness-audit under manual invocation, or an agent that authored the issue / just resolved its blocker); never settable through agent issue-creation calls",
  "needs-spec":
    "dispatcher-applied when intake fails quality, not accepted at creation",
};

// Display-only quoting for the dry-run plan; real invocations pass the argv array verbatim.
function shellQuote(arg: string): string {
  return /[\s']/.test(arg) ? `'${arg.replaceAll("'", "'\\''")}'` : arg;
}

function planCommands(
  input: CreateWiredIssueInput,
  blockers: number[],
): string[] {
  const planned: string[] = [];
  if (input.parentIssueNumber !== undefined) {
    planned.push(`gh issue view ${input.parentIssueNumber} --json id`);
  }
  for (const n of blockers) planned.push(`gh issue view ${n} --json id`);
  planned.push(`gh project list --owner ${boardRef().owner} --format json`);
  planned.push(
    `gh project field-list ${boardRef().number} --owner ${boardRef().owner} --format json`,
  );
  if ((input.labels ?? []).length > 0) {
    planned.push("gh label list --json name --limit 100");
  }
  const createArgv = [
    "gh",
    "issue",
    "create",
    "--title",
    input.title,
    "--body",
    input.body,
  ];
  for (const l of input.labels ?? []) createArgv.push("--label", l);
  planned.push(createArgv.map(shellQuote).join(" "));
  planned.push("gh issue view <issue-number> --json id");
  planned.push(
    `gh project item-add ${boardRef().number} --owner ${boardRef().owner} --url <issue-url> --format json --jq .id`,
  );
  planned.push(
    "gh project item-edit --id <item-id> --field-id <status-field-id> --project-id <project-id> --single-select-option-id <todo-option-id>",
  );
  if (input.epic !== undefined) {
    planned.push(
      "gh project item-edit --id <item-id> --field-id <epic-field-id> --project-id <project-id> --single-select-option-id <epic-option-id>",
    );
  }
  if (input.size !== undefined) {
    planned.push(
      "gh project item-edit --id <item-id> --field-id <size-field-id> --project-id <project-id> --single-select-option-id <size-option-id>",
    );
  }
  if (input.priority !== undefined) {
    planned.push(
      "gh project item-edit --id <item-id> --field-id <priority-field-id> --project-id <project-id> --single-select-option-id <priority-option-id>",
    );
  }
  if (input.parentIssueNumber !== undefined) {
    planned.push(
      'gh api graphql -f query=mutation { addSubIssue(input: {issueId: "<parent-node-id>", subIssueId: "<new-node-id>"}) { issue { number } subIssue { number } } }',
    );
  }
  for (const n of blockers) {
    planned.push(
      'gh api graphql -f query=mutation { addBlockedBy(input: {issueId: "<new-node-id>", blockingIssueId: "<blocker-node-id>"}) { issue { number } blockingIssue { number } } }',
    );
  }
  return planned;
}

export async function createWiredIssue(
  deps: CreateWiredIssueDeps,
  input: CreateWiredIssueInput,
): Promise<WiredIssueResult> {
  const requestedLabels = input.labels ?? [];
  const forbidden = requestedLabels.find((l) =>
    WORKFLOW_RESERVED_LABELS.includes(l),
  );
  if (forbidden !== undefined) {
    const reason = LABEL_REJECTION_REASONS[forbidden];
    return {
      ok: false,
      steps: [
        {
          step: "label-guard",
          status: "failed",
          detail: `label '${forbidden}' rejected: ${reason}`,
        },
      ],
    };
  }

  const blockers = [...new Set(input.blockedBy ?? [])];
  const run = deps.run ?? ghRunner(deps.worktree);

  if (input.dryRun) {
    return {
      ok: true,
      dryRun: true,
      planned: planCommands(input, blockers),
      steps: [],
    };
  }

  const steps: WiringStep[] = [];

  let parentNodeId: string | undefined;
  if (input.parentIssueNumber !== undefined) {
    const out = await runGh(run, [
      "gh",
      "issue",
      "view",
      String(input.parentIssueNumber),
      "--json",
      "id",
    ]);
    const id =
      out.exitCode === 0
        ? parseJson<{ id: string }>(out.stdout)?.id
        : undefined;
    if (!id) {
      steps.push({
        step: "preflight-parent",
        status: "failed",
        detail: snippet(out),
      });
      return { ok: false, steps };
    }
    parentNodeId = id;
  }

  const blockerNodeIds = new Map<number, string>();
  for (const n of blockers) {
    const out = await runGh(run, [
      "gh",
      "issue",
      "view",
      String(n),
      "--json",
      "id",
    ]);
    const id =
      out.exitCode === 0
        ? parseJson<{ id: string }>(out.stdout)?.id
        : undefined;
    if (!id) {
      steps.push({
        step: `preflight-blocker-${n}`,
        status: "failed",
        detail: snippet(out),
      });
      return { ok: false, steps };
    }
    blockerNodeIds.set(n, id);
  }

  const boardOut = await runGh(run, [
    "gh",
    "project",
    "list",
    "--owner",
    boardRef().owner,
    "--format",
    "json",
  ]);
  const projectId =
    boardOut.exitCode === 0
      ? parseJson<{ projects?: Array<{ number: number; id: string }> }>(
          boardOut.stdout,
        )?.projects?.find((p) => p.number === boardRef().number)?.id
      : undefined;
  if (!projectId) {
    steps.push({
      step: "preflight-board",
      status: "failed",
      detail: snippet(boardOut),
    });
    return { ok: false, steps };
  }

  const fieldsOut = await runGh(run, [
    "gh",
    "project",
    "field-list",
    String(boardRef().number),
    "--owner",
    boardRef().owner,
    "--format",
    "json",
  ]);
  const fields =
    fieldsOut.exitCode === 0
      ? parseJson<{
          fields?: Array<{
            name: string;
            id: string;
            options?: Array<{ name: string; id: string }>;
          }>;
        }>(fieldsOut.stdout)?.fields
      : undefined;
  if (!fields) {
    steps.push({
      step: "preflight-fields",
      status: "failed",
      detail: snippet(fieldsOut),
    });
    return { ok: false, steps };
  }
  const fieldIds = new Map<string, string>();
  const optionIds = new Map<string, Map<string, string>>();
  for (const f of fields) {
    fieldIds.set(f.name, f.id);
    optionIds.set(
      f.name,
      new Map((f.options ?? []).map((o): [string, string] => [o.name, o.id])),
    );
  }

  let survivingLabels = requestedLabels;
  if (requestedLabels.length > 0) {
    const out = await runGh(run, [
      "gh",
      "label",
      "list",
      "--json",
      "name",
      "--limit",
      "100",
    ]);
    let names: Set<string> | undefined;
    if (out.exitCode === 0) {
      const parsed = parseJson<Array<{ name: string }>>(out.stdout);
      if (parsed) names = new Set(parsed.map((l) => l.name));
    }
    if (!names) {
      // Labels never justify failing the whole create; degrade to a warning.
      for (const l of requestedLabels) {
        steps.push({
          step: `label-${l}`,
          status: "skipped",
          detail: `label list unavailable (${snippet(out)}); skipped because gh issue create fails on an unknown label`,
        });
      }
      survivingLabels = [];
    } else {
      const present = names;
      survivingLabels = requestedLabels.filter((l) => {
        if (present.has(l)) return true;
        steps.push({
          step: `label-${l}`,
          status: "skipped",
          detail:
            "not present on GitHub; skipped because gh issue create fails on an unknown label",
        });
        return false;
      });
    }
  }

  const createArgv = [
    "gh",
    "issue",
    "create",
    "--title",
    input.title,
    "--body",
    input.body,
  ];
  for (const l of survivingLabels) createArgv.push("--label", l);
  const createOut = await runGh(run, createArgv);
  if (createOut.exitCode !== 0) {
    steps.push({
      step: "create",
      status: "failed",
      detail: snippet(createOut),
    });
    return { ok: false, steps };
  }
  const url = lastNonEmptyLine(createOut.stdout);
  const numberText = url.match(/issues\/(\d+)/)?.[1];
  if (!url || !numberText) {
    // Exit 0 means the create may have landed, so a retry can duplicate it.
    steps.push({
      step: "create-url",
      status: "unknown",
      detail: `gh issue create exited 0 but no issue URL in output: ${snippet(createOut)} - the issue MAY exist, verify before retrying`,
    });
    return { ok: false, steps };
  }
  const number = Number(numberText);
  steps.push({ step: "create", status: "wired", detail: url });

  const nodeOut = await runGh(run, [
    "gh",
    "issue",
    "view",
    numberText,
    "--json",
    "id",
  ]);
  const newNodeId =
    nodeOut.exitCode === 0
      ? parseJson<{ id: string }>(nodeOut.stdout)?.id
      : undefined;
  if (!newNodeId) {
    steps.push({ step: "node-id", status: "failed", detail: snippet(nodeOut) });
  }

  const addOut = await runGh(run, [
    "gh",
    "project",
    "item-add",
    String(boardRef().number),
    "--owner",
    boardRef().owner,
    "--url",
    url,
    "--format",
    "json",
    "--jq",
    ".id",
  ]);
  const itemId = addOut.exitCode === 0 ? addOut.stdout.trim() : "";
  if (!itemId) {
    steps.push({
      step: "board-add",
      status: "failed",
      detail: `${snippet(addOut)} (manual fallback = .agents/skills/github-projects)`,
    });
  } else {
    steps.push({ step: "board-add", status: "wired", detail: itemId });
  }

  const edits: Array<{ fieldName: string; optionName: string }> = [
    { fieldName: "Status", optionName: boardRef().statusTodo },
  ];
  if (input.epic !== undefined) {
    edits.push({ fieldName: "Epic", optionName: input.epic });
  }
  if (input.size !== undefined) {
    edits.push({ fieldName: "Size", optionName: input.size });
  }
  if (input.priority !== undefined) {
    edits.push({ fieldName: "Priority", optionName: input.priority });
  }

  for (const e of edits) {
    if (!itemId) {
      steps.push({
        step: `field-${e.fieldName}`,
        status: "skipped",
        detail: "not on board",
      });
      continue;
    }
    const fid = fieldIds.get(e.fieldName);
    if (!fid) {
      steps.push({
        step: `field-${e.fieldName}`,
        status: "skipped",
        detail: `field '${e.fieldName}' not found on board ${boardRef().number}`,
      });
      continue;
    }
    const oid = optionIds.get(e.fieldName)?.get(e.optionName);
    if (!oid) {
      steps.push({
        step: `field-${e.fieldName}`,
        status: "skipped",
        detail: `option '${e.optionName}' not found on field '${e.fieldName}'`,
      });
      continue;
    }
    try {
      const out = await runGh(run, [
        "gh",
        "project",
        "item-edit",
        "--id",
        itemId,
        "--field-id",
        fid,
        "--project-id",
        projectId,
        "--single-select-option-id",
        oid,
      ]);
      steps.push(
        out.exitCode === 0
          ? {
              step: `field-${e.fieldName}`,
              status: "wired",
              detail: e.optionName,
            }
          : {
              step: `field-${e.fieldName}`,
              status: "failed",
              detail: snippet(out),
            },
      );
    } catch (e2) {
      steps.push({
        step: `field-${e.fieldName}`,
        status: "failed",
        detail: errMsg(e2),
      });
    }
  }

  if (input.parentIssueNumber !== undefined) {
    if (!newNodeId || !parentNodeId) {
      steps.push({
        step: "sub-issue",
        status: "skipped",
        detail: "new-issue node id unavailable",
      });
    } else {
      const query = `mutation { addSubIssue(input: {issueId: "${parentNodeId}", subIssueId: "${newNodeId}"}) { issue { number } subIssue { number } } }`;
      try {
        const out = await runGh(run, [
          "gh",
          "api",
          "graphql",
          "-f",
          `query=${query}`,
        ]);
        steps.push(
          out.exitCode === 0
            ? {
                step: "sub-issue",
                status: "wired",
                detail: `parent #${input.parentIssueNumber}`,
              }
            : { step: "sub-issue", status: "failed", detail: snippet(out) },
        );
      } catch (e2) {
        steps.push({
          step: "sub-issue",
          status: "failed",
          detail: errMsg(e2),
        });
      }
    }
  }

  for (const n of blockers) {
    const blockerNodeId = blockerNodeIds.get(n);
    if (!newNodeId || !blockerNodeId) {
      steps.push({
        step: `blocked-by-${n}`,
        status: "skipped",
        detail: "new-issue node id unavailable",
      });
      continue;
    }
    // issueId is the blocked issue and blockingIssueId its blocker; swapped
    // args would create the reverse edge.
    const query = `mutation { addBlockedBy(input: {issueId: "${newNodeId}", blockingIssueId: "${blockerNodeId}"}) { issue { number } blockingIssue { number } } }`;
    try {
      const out = await runGh(run, [
        "gh",
        "api",
        "graphql",
        "-f",
        `query=${query}`,
      ]);
      steps.push(
        out.exitCode === 0
          ? {
              step: `blocked-by-${n}`,
              status: "wired",
              detail: `blocker #${n}`,
            }
          : { step: `blocked-by-${n}`, status: "failed", detail: snippet(out) },
      );
    } catch (e2) {
      steps.push({
        step: `blocked-by-${n}`,
        status: "failed",
        detail: errMsg(e2),
      });
    }
  }

  return { ok: true, url, number, steps };
}

export function failureSummary(result: WiredIssueResult): string {
  if (result.steps.some((s) => s.status === "unknown")) {
    return "issue MAY exist but URL not parsed - verify before retrying";
  }
  return `not created: ${result.steps
    .filter((s) => s.status === "failed")
    .map((s) => s.step)
    .join(", ")}`;
}

export const create_wired = defineTool({
  description:
    "Create a FULLY WIRED GitHub issue in one call: sub-issue link to a parent, " +
    "Projects board #3 membership, Status=Todo, Epic/Size single-select fields, labels " +
    "from the agent-settable vocabulary (epic, research), and blocked-by edges. Size is " +
    "agent-proposed; Priority is human-owned, so pass it ONLY when a human explicitly " +
    "provided it. Approval and gating are the CALLER's responsibility: this tool is the " +
    "mechanics layer, not an approval gate. The authoritative field schema is at " +
    "the repo's issue-schema doc.",
  args: {
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
        { worktree: context.worktree, run: ghRunner(context.worktree, context.signal) },
        args,
      );
      const wired = result.steps.filter((s) => s.status === "wired").length;
      const skipped = result.steps.filter((s) => s.status === "skipped").length;
      const failed = result.steps.filter((s) => s.status === "failed").length;
      const output = result.dryRun
        ? `dry-run: ${result.planned?.length ?? 0} planned commands, nothing executed`
        : result.ok
          ? `${result.url} wired=${wired} skipped=${skipped} failed=${failed}`
          : failureSummary(result);
      return {
        title: "issue_intake_create_wired",
        output,
        metadata: result,
      };
    } catch (e) {
      const msg = errMsg(e);
      return {
        title: "issue_intake_create_wired - gh error",
        output: `gh issue create failed: ${msg}`,
        metadata: { error: msg },
      };
    }
  },
});
