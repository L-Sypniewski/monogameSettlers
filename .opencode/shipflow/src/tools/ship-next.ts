import { z } from "zod";
import { defineTool } from "../define-tool.ts";
import { dirname } from "node:path";
import { ghRunner, type GhRunner } from "./issue-intake";
import {
  refreshConfig,
  boardRef,
  repoSlug,
} from "../config.ts";

// Read lazily: the config is supplied after module load (plugin options).
function dispatchableStatuses(): ReadonlySet<string> {
  return new Set([boardRef().statusTodo, "Next"]);
}

const PUSH_CONFIRMED = new Set(["yes", "up-to-date"]);

// Spelling mirrors .herdr-workflow.json inProgressOption.
const STATUS_IN_PROGRESS = "In progress";

const HEADLESS_LOG_DIR = "/tmp/ship-next";

// Fixed script + "$@" passing keep every dynamic value a literal argv element,
// preserving the no-shell-substitution guarantee the runner contract makes.
const HEADLESS_LAUNCH_SH = [
  'mkdir -p "$(dirname "$0")"',
  'cd "$1"',
  "shift",
  'nohup "$@" >"$0" 2>&1 &',
  "echo $!",
].join("\n");

const WRITABLE_PROBE_SH = 'test -w "$1"';

// Mirrors the devcontainer PATH the herdr-workflow skill injects for worktree sessions.
const DEVCONTAINER_REMOTE_PATH =
  "/home/vscode/.opencode/bin:/home/vscode/.bun/bin:/mnt/mise-data/shims:/usr/local/share/nvm/current/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/home/vscode/.dotnet:/home/vscode/.dotnet/tools:/home/vscode/.local/bin";

export type LaunchMode =
  | "herdr"
  | "devcontainer-headless"
  | "opencode-headless"
  | "claude-headless"
  | "prepare-only";

export type DispatchStep = {
  step: string;
  status: "done" | "skipped" | "failed" | "conflict";
  detail: string;
};

export type NormalizedItem = {
  number: number;
  title: string;
  url: string;
  state: "open" | "closed";
  status?: string;
  epic?: string;
  labels: string[];
  createdAt?: string;
  body?: string;
  priority?: string;
  size?: string;
  openBlockedBy?: number[];
};

export type RawPr = {
  number: number;
  title: string;
  body: string | null;
  headRefName: string;
  url: string;
};

export type FrontierEntry = {
  number: number;
  title: string;
  url: string;
  priority?: string;
  size?: string;
  status?: string;
  epic?: string;
  decision: "selectable" | "flag-needs-spec" | "skip";
  reasons: string[];
  annotations: string[];
};

export type Snapshot = {
  projectId?: string;
  fieldIds: Map<string, string>;
  optionIds: Map<string, Map<string, string>>;
  epicOptionIndex: Map<string, number>;
  itemIds: Map<number, string>;
  items: NormalizedItem[];
  prs: RawPr[];
  branchNames: string[];
  steps: DispatchStep[];
};

export type Shortlist = {
  matched: boolean;
  entries: FrontierEntry[];
};

export type PickReport = {
  frontierEmpty: boolean;
  entries: FrontierEntry[];
  shortlist: Shortlist;
  steps: DispatchStep[];
};

type RunOutcome = { stdout: string; stderr: string; exitCode: number };

type ItemListShape = {
  items?: Array<{
    id?: string;
    title?: string;
    status?: string | null;
    epic?: string | null;
    labels?: string[];
    content?: {
      body?: string | null;
      number?: number;
      title?: string;
      url?: string;
    } | null;
  }>;
};

type OpenIssuesShape = Array<{ number: number; createdAt?: string }>;

type FieldListShape = {
  fields?: Array<{
    name: string;
    id: string;
    options?: Array<{ name: string; id: string }>;
  }>;
};

type GraphqlNodeShape = {
  content?: {
    blockedBy?: { nodes?: Array<{ number: number; state?: string }> };
  };
  fieldValues?: {
    nodes?: Array<{ name?: string; field?: { name?: string } }>;
  };
};

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

function defaultRunner(worktree: string, signal?: AbortSignal): GhRunner {
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

// Display-only quoting for dry-run plans; real invocations pass the argv array verbatim.
export function shellQuote(arg: string): string {
  return /[\s']/.test(arg) ? `'${arg.replaceAll("'", "'\\''")}'` : arg;
}

export function normalizeBoard(
  itemListJson: string,
  openIssuesJson: string,
  fieldListJson: string,
): { items: NormalizedItem[]; epicOptionIndex: Map<string, number> } {
  const itemList = parseJson<ItemListShape>(itemListJson);
  const openIssues = parseJson<OpenIssuesShape>(openIssuesJson);
  const fieldList = parseJson<FieldListShape>(fieldListJson);

  const epicOptionIndex = new Map<string, number>();
  for (const [i, opt] of (
    fieldList?.fields?.find((f) => f.name === "Epic")?.options ?? []
  ).entries()) {
    epicOptionIndex.set(opt.name, i);
  }

  const openByNumber = new Map<number, { createdAt?: string }>();
  for (const issue of openIssues ?? []) {
    openByNumber.set(issue.number, { createdAt: issue.createdAt });
  }

  const items: NormalizedItem[] = [];
  for (const raw of itemList?.items ?? []) {
    const number = raw.content?.number;
    if (number === undefined) continue;
    const open = openByNumber.get(number);
    items.push({
      number,
      title: raw.content?.title ?? raw.title ?? "",
      url: raw.content?.url ?? "",
      state: open !== undefined ? "open" : "closed",
      ...(raw.status != null ? { status: raw.status } : {}),
      ...(raw.epic != null ? { epic: raw.epic } : {}),
      labels: raw.labels ?? [],
      ...(open?.createdAt !== undefined ? { createdAt: open.createdAt } : {}),
      ...(raw.content?.body != null ? { body: raw.content.body } : {}),
    });
  }
  return { items, epicOptionIndex };
}

export function prReferencesIssue(pr: RawPr, n: number): boolean {
  return (
    pr.headRefName.startsWith(`issue-${n}-`) ||
    new RegExp(`#${n}\\b`).test(`${pr.title}\n${pr.body ?? ""}`)
  );
}

export function branchClaimsIssue(branchNames: string[], n: number): boolean {
  return branchNames.some((b) => b.startsWith(`issue-${n}-`));
}

function toEntry(
  item: NormalizedItem,
  decision: FrontierEntry["decision"],
  reasons: string[],
  annotations: string[] = [],
): FrontierEntry {
  return {
    number: item.number,
    title: item.title,
    url: item.url,
    ...(item.priority !== undefined ? { priority: item.priority } : {}),
    ...(item.size !== undefined ? { size: item.size } : {}),
    ...(item.status !== undefined ? { status: item.status } : {}),
    ...(item.epic !== undefined ? { epic: item.epic } : {}),
    decision,
    reasons,
    annotations,
  };
}

export function frontierFilter(
  items: NormalizedItem[],
  prs: RawPr[],
  branchNames: string[],
): {
  qualified: Array<{ item: NormalizedItem; annotations: string[] }>;
  skipped: FrontierEntry[];
} {
  const qualified: Array<{ item: NormalizedItem; annotations: string[] }> = [];
  const skipped: FrontierEntry[] = [];
  for (const item of items) {
    if (item.state !== "open" || !item.labels.includes("ship-ready")) continue;
    const hardReasons: string[] = [];
    if (!dispatchableStatuses().has(item.status ?? "")) {
      hardReasons.push(
        item.status === undefined
          ? "status unset"
          : `status is '${item.status}'`,
      );
    }
    const referencingPr = prs.find((pr) => prReferencesIssue(pr, item.number));
    if (referencingPr) {
      hardReasons.push(
        `open PR #${referencingPr.number} references this issue`,
      );
    }
    if (branchClaimsIssue(branchNames, item.number)) {
      hardReasons.push(`branch issue-${item.number}-* exists on origin`);
    }
    if (hardReasons.length > 0) {
      skipped.push(toEntry(item, "skip", hardReasons));
      continue;
    }
    const annotations: string[] = [];
    if (item.priority === undefined) annotations.push("priority unset");
    if (item.size === undefined) annotations.push("size unset");
    const blockers = item.openBlockedBy ?? [];
    if (blockers.length > 0) {
      annotations.push(
        `blocked-by: ${blockers.map((n) => `#${n}`).join(", ")}`,
      );
    }
    qualified.push({ item, annotations });
  }
  return { qualified, skipped };
}

const AC_HEADING_RE = /^##\s+acceptance criteria\s*$/i;
const SECTION_END_RE = /^#{1,2}\s/;
const UNCHECKED_BOX_RE = /^\s*[-*]\s+\[ \]/;

export function intakeGate(body: string): { ok: boolean; missing: string[] } {
  const lines = body.split("\n");
  const headingIdx = lines.findIndex((line) => AC_HEADING_RE.test(line));
  if (headingIdx === -1) {
    return { ok: false, missing: ["missing '## Acceptance criteria' section"] };
  }
  for (const line of lines.slice(headingIdx + 1)) {
    if (SECTION_END_RE.test(line)) break;
    if (UNCHECKED_BOX_RE.test(line)) return { ok: true, missing: [] };
  }
  return {
    ok: false,
    missing: ["no unchecked '- [ ]' item in the acceptance criteria section"],
  };
}

const PRIORITY_RANK: Record<string, number> = {
  Urgent: 0,
  High: 1,
  Medium: 2,
  Low: 3,
};

export function pickOrder(
  items: NormalizedItem[],
  epicOptionIndex: Map<string, number>,
): NormalizedItem[] {
  const keyed = items.map((item, idx) => ({ item, idx }));
  keyed.sort((a, b) => {
    const pa = PRIORITY_RANK[a.item.priority ?? ""] ?? 4;
    const pb = PRIORITY_RANK[b.item.priority ?? ""] ?? 4;
    if (pa !== pb) return pa - pb;
    const ea = epicOptionIndex.get(a.item.epic ?? "") ?? Infinity;
    const eb = epicOptionIndex.get(b.item.epic ?? "") ?? Infinity;
    if (ea !== eb) return ea < eb ? -1 : 1;
    const ca = a.item.createdAt ?? "";
    const cb = b.item.createdAt ?? "";
    if (ca !== cb) return ca < cb ? -1 : 1;
    return a.idx - b.idx;
  });
  return keyed.map(({ item }) => item);
}

export function shortlist(
  qualified: NormalizedItem[],
  epicOptionIndex: Map<string, number>,
  steer?: string,
  k = 8,
): { matched: boolean; entries: NormalizedItem[] } {
  const base = pickOrder(qualified, epicOptionIndex);
  const tokens = [
    ...new Set(
      (steer ?? "")
        .toLowerCase()
        .split(/\s+/)
        .filter((t) => t !== ""),
    ),
  ];
  if (tokens.length === 0) {
    return { matched: true, entries: base.slice(0, k) };
  }
  const scored = base.map((item, idx) => {
    const haystack =
      `${item.title} ${item.body ?? ""} ${item.epic ?? ""}`.toLowerCase();
    return {
      item,
      idx,
      score: tokens.filter((t) => haystack.includes(t)).length,
    };
  });
  scored.sort((a, b) => b.score - a.score || a.idx - b.idx);
  if (!scored.some((s) => s.score > 0)) {
    return { matched: false, entries: [] };
  }
  return { matched: true, entries: scored.slice(0, k).map((s) => s.item) };
}

function itemIdsByNumber(itemListJson: string): Map<number, string> {
  const ids = new Map<number, string>();
  for (const raw of parseJson<ItemListShape>(itemListJson)?.items ?? []) {
    if (raw.content?.number !== undefined && raw.id) {
      ids.set(raw.content.number, raw.id);
    }
  }
  return ids;
}

export async function boardSnapshot(run: GhRunner): Promise<Snapshot> {
  const steps: DispatchStep[] = [];
  const fieldIds = new Map<string, string>();
  const optionIds = new Map<string, Map<string, string>>();

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
      step: "board-id",
      status: "failed",
      detail: snippet(boardOut),
    });
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
      ? parseJson<FieldListShape>(fieldsOut.stdout)?.fields
      : undefined;
  if (!fields) {
    steps.push({
      step: "field-list",
      status: "failed",
      detail: snippet(fieldsOut),
    });
  } else {
    for (const f of fields) {
      fieldIds.set(f.name, f.id);
      optionIds.set(
        f.name,
        new Map((f.options ?? []).map((o): [string, string] => [o.name, o.id])),
      );
    }
  }

  const itemOut = await runGh(run, [
    "gh",
    "project",
    "item-list",
    String(boardRef().number),
    "--owner",
    boardRef().owner,
    "--limit",
    "1000",
    "--format",
    "json",
  ]);
  const issueOut = await runGh(run, [
    "gh",
    "issue",
    "list",
    "--state",
    "open",
    "--limit",
    "1000",
    "--json",
    "number,title,createdAt,labels,url",
  ]);
  const boardFailed = itemOut.exitCode !== 0;
  const issuesFailed = issueOut.exitCode !== 0;
  const { items, epicOptionIndex } =
    boardFailed || issuesFailed
      ? {
          items: [] as NormalizedItem[],
          epicOptionIndex: new Map<string, number>(),
        }
      : normalizeBoard(itemOut.stdout, issueOut.stdout, fieldsOut.stdout);
  if (boardFailed) {
    steps.push({
      step: "item-list",
      status: "failed",
      detail: snippet(itemOut),
    });
  } else if (issuesFailed) {
    steps.push({
      step: "issue-list",
      status: "failed",
      detail: snippet(issueOut),
    });
  }
  const itemIds = boardFailed
    ? new Map<number, string>()
    : itemIdsByNumber(itemOut.stdout);

  const prOut = await runGh(run, [
    "gh",
    "pr",
    "list",
    "--state",
    "open",
    "--limit",
    "1000",
    "--json",
    "number,title,body,headRefName,url",
  ]);
  let prs: RawPr[] = [];
  if (prOut.exitCode !== 0) {
    steps.push({ step: "pr-list", status: "failed", detail: snippet(prOut) });
  } else {
    const parsed = parseJson<
      Array<{
        number: number;
        title: string;
        body?: string | null;
        headRefName?: string;
        url?: string;
      }>
    >(prOut.stdout);
    if (!parsed) {
      steps.push({
        step: "pr-list",
        status: "failed",
        detail: "unparseable gh pr list output",
      });
    } else {
      prs = parsed.map((p) => ({
        number: p.number,
        title: p.title,
        body: p.body ?? null,
        headRefName: p.headRefName ?? "",
        url: p.url ?? "",
      }));
    }
  }

  const branchOut = await runGh(run, ["git", "ls-remote", "--heads", "origin"]);
  let branchNames: string[] = [];
  if (branchOut.exitCode !== 0) {
    steps.push({
      step: "branch-list",
      status: "failed",
      detail: snippet(branchOut),
    });
  } else {
    branchNames = branchOut.stdout
      .split("\n")
      .map((line) => (line.split("\t")[1] ?? "").replace(/^refs\/heads\//, ""))
      .filter((name) => name !== "");
  }

  // gh project item-list omits Priority, Size, and blocked-by in this gh
  // version, so each prefiltered candidate gets one GraphQL read for them.
  for (const item of items) {
    if (
      item.state !== "open" ||
      !item.labels.includes("ship-ready") ||
      !dispatchableStatuses().has(item.status ?? "")
    ) {
      continue;
    }
    const itemId = itemIds.get(item.number);
    if (!itemId) continue;
    const query = `{ node(id: "${itemId}") { ... on ProjectV2Item { content { ... on Issue { number blockedBy(first: 50) { nodes { number state } } } fieldValues(first: 20) { nodes { ... on ProjectV2ItemFieldSingleSelectValue { name field { ... on ProjectV2FieldCommon { name } } } } } } } } }`;
    try {
      const out = await runGh(run, [
        "gh",
        "api",
        "graphql",
        "-f",
        `query=${query}`,
      ]);
      const node =
        out.exitCode === 0
          ? parseJson<{ data?: { node?: GraphqlNodeShape } }>(out.stdout)?.data
              ?.node
          : undefined;
      if (!node) {
        steps.push({
          step: `enrich-#${item.number}`,
          status: "failed",
          detail: snippet(out),
        });
        continue;
      }
      for (const value of node.fieldValues?.nodes ?? []) {
        if (value.field?.name === "Priority" && value.name !== undefined) {
          item.priority = value.name;
        }
        if (value.field?.name === "Size" && value.name !== undefined) {
          item.size = value.name;
        }
      }
      item.openBlockedBy = (node.content?.blockedBy?.nodes ?? [])
        .filter((blocker) => blocker.state === "OPEN")
        .map((blocker) => blocker.number);
    } catch (e) {
      steps.push({
        step: `enrich-#${item.number}`,
        status: "failed",
        detail: errMsg(e),
      });
    }
  }

  return {
    ...(projectId !== undefined ? { projectId } : {}),
    fieldIds,
    optionIds,
    epicOptionIndex,
    itemIds,
    items,
    prs,
    branchNames,
    steps,
  };
}

function gradedEntries(
  qualified: Array<{ item: NormalizedItem; annotations: string[] }>,
  skipped: FrontierEntry[],
  epicOptionIndex: Map<string, number>,
): FrontierEntry[] {
  const entries: FrontierEntry[] = [...skipped];
  const annotationsByNumber = new Map(
    qualified.map(({ item, annotations }) => [item.number, annotations]),
  );
  for (const item of pickOrder(
    qualified.map(({ item }) => item),
    epicOptionIndex,
  )) {
    const annotations = annotationsByNumber.get(item.number) ?? [];
    const gate = intakeGate(item.body ?? "");
    if (!gate.ok) {
      entries.push(toEntry(item, "flag-needs-spec", gate.missing, []));
      continue;
    }
    entries.push(toEntry(item, "selectable", [], annotations));
  }
  return entries;
}

export async function pickNextReport(
  deps: { run: GhRunner },
  args: { steer?: string },
): Promise<PickReport> {
  const snapshot = await boardSnapshot(deps.run);
  const { qualified, skipped } = frontierFilter(
    snapshot.items,
    snapshot.prs,
    snapshot.branchNames,
  );
  const entries = gradedEntries(qualified, skipped, snapshot.epicOptionIndex);
  const entryByNumber = new Map(entries.map((e) => [e.number, e]));
  const selectableItems = qualified
    .filter(
      ({ item }) => entryByNumber.get(item.number)?.decision === "selectable",
    )
    .map(({ item }) => item);
  const short = shortlist(
    selectableItems,
    snapshot.epicOptionIndex,
    args.steer,
  );
  return {
    frontierEmpty: selectableItems.length === 0,
    entries,
    shortlist: {
      matched: short.matched,
      entries: short.entries.map((i) => entryByNumber.get(i.number)!),
    },
    steps: snapshot.steps,
  };
}

export const pick_next = defineTool({
  description:
    "Read-only frontier report for backlog dispatch over Projects board #3 - queries board + " +
    "open issues/PRs/branches, applies the frontier qualification (open + ship-ready label + " +
    "Status Todo/Next + unclaimed: no open PR referencing the issue, no issue-<N>-* branch on " +
    "origin), then ranks by Priority > epic order > oldest and returns a shortlist for human " +
    "multi-select. Priority and Size are ranking inputs and selection metadata, NOT gates; " +
    "near-miss issues (Priority/Size unset, open blockers) appear as selectable entries " +
    "annotated with their reason; the intake gate flags issues without a verifiable acceptance " +
    "criteria section. Performs zero mutations. The authoritative field schema is at " +
    "the repo's issue-schema doc.",
  args: {
    steer: z
      .string()
      .optional()
      .describe(
        "free-text ranking hint - re-ranks the shortlist only, never changes qualification",
      ),
  },
  async execute(args, context) {
    refreshConfig(context.worktree);
    await context.progress?.({ status: "shortlisting the board frontier" });
    try {
      const report = await pickNextReport(
        { run: ghRunner(context.worktree, context.signal) },
        { steer: args.steer },
      );
      const steerNote = report.shortlist.matched
        ? ""
        : " (steer matched nothing)";
      const output = report.frontierEmpty
        ? `${report.entries.length} entries, frontier empty`
        : `${report.entries.length} entries, shortlist ${report.shortlist.entries.length}${steerNote}`;
      return {
        title: "ship-next_pick_next",
        output,
        metadata: report,
      };
    } catch (e) {
      const msg = errMsg(e);
      return {
        title: "ship-next_pick_next - gh error",
        output: `pick_next failed: ${msg}`,
        metadata: { error: msg },
      };
    }
  },
});

export function missingItemsComment(missing: string[]): string {
  return [
    "The /ship-next dispatch intake gate failed for this issue:",
    ...missing.map((m) => `- ${m}`),
    "",
    "Fix: add a `## Acceptance criteria` section with at least one unchecked `- [ ]` item, then re-run `/ship-next`.",
    "The `needs-spec` label lifts automatically on the next passing pick.",
  ].join("\n");
}

export function prLinkComment(prUrl: string): string {
  return `Draft PR: ${prUrl}\n\nPosted by the /ship-next dispatcher.`;
}

export function parseHerdrSummary(stdout: string): Map<string, string> {
  const lines = stdout.split("\n");
  let summaryIdx = -1;
  for (let i = lines.length - 1; i >= 0; i--) {
    if (lines[i].trim() === "summary:") {
      summaryIdx = i;
      break;
    }
  }
  const values = new Map<string, string>();
  if (summaryIdx === -1) return values;
  for (const line of lines.slice(summaryIdx + 1)) {
    const eq = line.indexOf("=");
    if (eq === -1) break;
    values.set(line.slice(0, eq), line.slice(eq + 1));
  }
  return values;
}

// Mirrors hr_slugify from the herdr-workflow scripts so non-herdr branches
// land on the same names herdr would pick.
function slugifyTitle(title: string): string {
  return (
    title
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .split("-")
      .filter((p) => p !== "")
      .slice(0, 6)
      .join("-") || "task"
  );
}

// Bun.spawn throws when argv[0] is missing from PATH, so availability probes
// treat a thrown call the same as a non-zero exit: absent.
async function probeAvailable(
  run: GhRunner,
  argv: string[],
): Promise<{ ok: boolean; reason: string }> {
  try {
    const out = await runGh(run, argv);
    return out.exitCode === 0
      ? { ok: true, reason: "" }
      : { ok: false, reason: `exit ${out.exitCode}` };
  } catch (e) {
    return { ok: false, reason: errMsg(e) };
  }
}

export async function flagIntake(
  run: GhRunner,
  issue: { number: number; labels: string[]; missing: string[] },
): Promise<{ steps: DispatchStep[] }> {
  const steps: DispatchStep[] = [];
  if (issue.labels.includes("needs-spec")) {
    return {
      steps: [
        {
          step: "label-needs-spec",
          status: "skipped",
          detail: "already labeled - no comment (idempotent)",
        },
      ],
    };
  }
  const listOut = await runGh(run, [
    "gh",
    "label",
    "list",
    "--search",
    "needs-spec",
    "--json",
    "name",
  ]);
  if (listOut.exitCode !== 0) {
    steps.push({
      step: "label-needs-spec",
      status: "failed",
      detail: snippet(listOut),
    });
    return { steps };
  }
  const names = parseJson<Array<{ name: string }>>(listOut.stdout);
  if (!names?.some((l) => l.name === "needs-spec")) {
    steps.push({
      step: "label-needs-spec",
      status: "failed",
      detail:
        "label 'needs-spec' does not exist on the repo; create it manually - the dispatcher never auto-creates labels",
    });
    return { steps };
  }
  const addOut = await runGh(run, [
    "gh",
    "issue",
    "edit",
    String(issue.number),
    "--add-label",
    "needs-spec",
  ]);
  if (addOut.exitCode !== 0) {
    steps.push({
      step: "add-label",
      status: "failed",
      detail: snippet(addOut),
    });
    return { steps };
  }
  steps.push({ step: "add-label", status: "done", detail: "needs-spec" });
  const commentOut = await runGh(run, [
    "gh",
    "issue",
    "comment",
    String(issue.number),
    "--body",
    missingItemsComment(issue.missing),
  ]);
  steps.push(
    commentOut.exitCode === 0
      ? {
          step: "intake-comment",
          status: "done",
          detail: "what's-missing comment",
        }
      : {
          step: "intake-comment",
          status: "failed",
          detail: snippet(commentOut),
        },
  );
  return { steps };
}

export type IssueDispatchResult = {
  issue: number;
  ok: boolean;
  dryRun?: boolean;
  conflict?: string;
  flagged?: boolean;
  planned?: string[];
  launchMode?: LaunchMode;
  runtime?: "opencode" | "claude";
  worktree?: string;
  log?: string;
  pid?: number;
  branch?: string;
  launch?: {
    issue: number;
    branch: string;
    worktreePath: string;
    workspace: string;
    pane: string;
    launched: boolean;
    reused: boolean;
    agent?: string;
  };
  steps: DispatchStep[];
};

export type DispatchReport = {
  ok: boolean;
  dryRun?: boolean;
  conflict?: string;
  entries: FrontierEntry[];
  steps: DispatchStep[];
  results: IssueDispatchResult[];
};

export async function dispatchIssue(
  deps: { worktree: string; run?: GhRunner },
  input: {
    issues?: number[];
    dryRun?: boolean;
    runtime?: "opencode" | "claude";
  },
): Promise<DispatchReport> {
  const run = deps.run ?? ghRunner(deps.worktree);
  const snapshot = await boardSnapshot(run);
  const { qualified, skipped } = frontierFilter(
    snapshot.items,
    snapshot.prs,
    snapshot.branchNames,
  );
  const entries = gradedEntries(qualified, skipped, snapshot.epicOptionIndex);
  const base = { entries, steps: [...snapshot.steps] };

  if (!input.issues || input.issues.length === 0) {
    return {
      ok: false,
      conflict:
        "no selection: call ship-next_pick_next and pass the human selection",
      results: [],
      ...base,
    };
  }

  const results: IssueDispatchResult[] = [];
  // Duplicates race the same branch/worktree claim; the pre-loop snapshot
  // cannot see the first iteration's new branch, so dispatch each number once.
  for (const n of new Set(input.issues)) {
    // One selection failing must never abort the remaining claims.
    try {
      results.push(await dispatchOne(deps, run, snapshot, n, input));
    } catch (e) {
      results.push({
        issue: n,
        ok: false,
        conflict: `dispatch failed: ${errMsg(e)}`,
        steps: [],
      });
    }
  }
  return {
    ok: results.every((r) => r.ok),
    ...(input.dryRun ? { dryRun: true } : {}),
    results,
    ...base,
  };
}

async function dispatchOne(
  deps: { worktree: string },
  run: GhRunner,
  snapshot: Snapshot,
  n: number,
  input: { dryRun?: boolean; runtime?: "opencode" | "claude" },
): Promise<IssueDispatchResult> {
  const steps: DispatchStep[] = [];
  const planned: string[] = [];

  const target = snapshot.items.find((i) => i.number === n);
  if (!target || target.state !== "open") {
    return {
      issue: n,
      ok: false,
      conflict: `issue #${n} is not a frontier candidate (open + board item required)`,
      steps,
    };
  }
  const hardReasons: string[] = [];
  if (!dispatchableStatuses().has(target.status ?? "")) {
    hardReasons.push(
      target.status === undefined
        ? "status unset"
        : `status is '${target.status}'`,
    );
  }
  const referencingPr = snapshot.prs.find((pr) => prReferencesIssue(pr, n));
  if (referencingPr) {
    hardReasons.push(`open PR #${referencingPr.number} references this issue`);
  }
  if (branchClaimsIssue(snapshot.branchNames, n)) {
    hardReasons.push(`branch issue-${n}-* exists on origin`);
  }
  if (hardReasons.length > 0) {
    return {
      issue: n,
      ok: false,
      conflict: `issue #${n} is claimed or not ready: ${hardReasons.join("; ")}`,
      steps,
    };
  }

  const gate = intakeGate(target.body ?? "");
  if (!gate.ok) {
    if (input.dryRun) {
      return {
        issue: n,
        ok: false,
        flagged: true,
        dryRun: true,
        planned: [
          "gh label list --search needs-spec --json name",
          `gh issue edit ${n} --add-label needs-spec`,
          `gh issue comment ${n} --body ${shellQuote(missingItemsComment(gate.missing))}`,
        ],
        steps,
      };
    }
    const flag = await flagIntake(run, {
      number: n,
      labels: target.labels,
      missing: gate.missing,
    });
    return {
      issue: n,
      ok: false,
      flagged: true,
      steps: [...steps, ...flag.steps],
    };
  }

  if (target.labels.includes("needs-spec")) {
    if (input.dryRun) {
      planned.push(`gh issue edit ${target.number} --remove-label needs-spec`);
    } else {
      const out = await runGh(run, [
        "gh",
        "issue",
        "edit",
        String(target.number),
        "--remove-label",
        "needs-spec",
      ]);
      steps.push(
        out.exitCode === 0
          ? {
              step: "lift-needs-spec",
              status: "done",
              detail: "intake gate passes; needs-spec removed",
            }
          : { step: "lift-needs-spec", status: "failed", detail: snippet(out) },
      );
    }
  }

  const probe = await probeAvailable(run, ["herdr", "workspace", "list"]);
  steps.push(
    probe.ok
      ? { step: "herdr-probe", status: "done", detail: "herdr reachable" }
      : {
          step: "herdr-probe",
          status: "skipped",
          detail: `herdr unavailable (${probe.reason})`,
        },
  );

  // --help is the capability probe: the usage line advertises --agent
  // only when the vendored skill supports the flag.
  const herdrScript = `${deps.worktree}/.agents/skills/herdr-workflow/scripts/herdr-issue.sh`;
  let herdrSupportsAgent = false;
  if (probe.ok && input.runtime === "claude") {
    const helpOut = await runGh(run, ["bash", herdrScript, "--help"]);
    herdrSupportsAgent =
      helpOut.exitCode === 0 && helpOut.stdout.includes("--agent");
    if (!herdrSupportsAgent) {
      steps.push({
        step: "herdr-probe",
        status: "skipped",
        detail:
          "vendored herdr-issue.sh predates --agent; explicit claude runtime routes headless",
      });
    }
  }

  if (probe.ok && (input.runtime !== "claude" || herdrSupportsAgent)) {
    const herdrArgv = [
      "bash",
      herdrScript,
      String(target.number),
      "--repo",
      deps.worktree,
      ...(input.runtime === "claude" ? ["--agent", "claude"] : []),
    ];
    if (input.dryRun) {
      planned.push(herdrArgv.map(shellQuote).join(" "));
      return {
        issue: n,
        ok: true,
        dryRun: true,
        launchMode: "herdr",
        runtime: input.runtime ?? "opencode",
        planned,
        steps,
      };
    }
    const launchOut = await runGh(run, herdrArgv);
    if (launchOut.exitCode !== 0) {
      steps.push({
        step: "herdr-launch",
        status: "failed",
        detail: snippet(launchOut),
      });
      return { issue: n, ok: false, conflict: "herdr launch failed", steps };
    }
    const summary = parseHerdrSummary(launchOut.stdout);
    // herdr swallows per-issue failures (warn-and-continue), exits 0 with no summary,
    // and its diagnostics never reach stdout - a branch-bearing summary proves the launch.
    if (summary.size === 0 || !summary.has("branch")) {
      steps.push({
        step: "herdr-launch",
        status: "failed",
        detail: "no branch-bearing summary in herdr output",
      });
      return { issue: n, ok: false, conflict: "herdr launch failed", steps };
    }
    if (summary.get("reused") === "yes") {
      return {
        issue: n,
        ok: false,
        conflict: `worktree/branch already open in herdr workspace ${summary.get("worktree_workspace") ?? "-"} - a run may already be active; not supervising`,
        steps,
      };
    }
    const pushed = summary.get("pushed");
    if (pushed === undefined || !PUSH_CONFIRMED.has(pushed)) {
      steps.push({
        step: "herdr-launch",
        status: "conflict",
        detail: `branch push to origin not confirmed (pushed=${pushed ?? "missing"})`,
      });
      return {
        issue: n,
        ok: false,
        conflict: `herdr branch push failed (pushed=${pushed ?? "missing"}) - issue not claimed on origin`,
        steps,
      };
    }
    steps.push({
      step: "herdr-launch",
      status: "done",
      detail: `branch ${summary.get("branch") ?? "-"} in ${summary.get("worktree_workspace") ?? "-"}`,
    });
    // herdr's board sync is best-effort: warn lines land on stderr while the
    // launch still reports ok, so the degradation must be ledgered, not dropped.
    const boardSyncWarns = launchOut.stderr
      .split("\n")
      .filter((l) => l.includes("WARN: board sync:"));
    if (boardSyncWarns.length > 0) {
      const text = boardSyncWarns.join(" | ").trim();
      steps.push({
        step: "herdr-board-sync",
        status: "failed",
        detail: text.length > 200 ? `${text.slice(0, 200)}...` : text,
      });
    }
    return {
      issue: n,
      ok: true,
      launchMode: "herdr",
      runtime: input.runtime ?? "opencode",
      launch: {
        issue: Number(summary.get("issue") ?? target.number),
        branch: summary.get("branch") ?? "",
        worktreePath: summary.get("worktree_path") ?? "",
        workspace: summary.get("worktree_workspace") ?? "",
        pane: summary.get("pane") ?? "",
        launched: summary.get("launched") === "yes",
        reused: summary.get("reused") === "yes",
        ...(summary.has("agent") ? { agent: summary.get("agent") } : {}),
      },
      worktree: summary.get("worktree_path") ?? "",
      steps,
    };
  }

  const branch = `issue-${target.number}-${slugifyTitle(target.title)}`;
  const parent = dirname(deps.worktree);
  const writableOut = await runGh(run, [
    "bash",
    "-c",
    WRITABLE_PROBE_SH,
    "w",
    parent,
  ]);
  const parentWritable = writableOut.exitCode === 0;
  const worktreeDir = parentWritable
    ? `${parent}/${branch}`
    : `${HEADLESS_LOG_DIR}/${branch}`;
  const logPath = `${HEADLESS_LOG_DIR}/${branch}.log`;
  const goal = `Work on issue #${target.number}: ${target.title} - ${target.url}`;

  const statusFid = snapshot.fieldIds.get("Status");
  const statusOid = snapshot.optionIds.get("Status")?.get(STATUS_IN_PROGRESS);
  const itemId = snapshot.itemIds.get(target.number);
  const itemEditArgv =
    snapshot.projectId !== undefined &&
    statusFid !== undefined &&
    statusOid !== undefined &&
    itemId !== undefined
      ? [
          "gh",
          "project",
          "item-edit",
          "--id",
          itemId,
          "--field-id",
          statusFid,
          "--project-id",
          snapshot.projectId,
          "--single-select-option-id",
          statusOid,
        ]
      : undefined;

  // Resolution is best-effort: failed, thrown, or empty output falls back to "master".
  let defaultBranch = "master";
  try {
    const headRef = await runGh(run, [
      "git",
      "symbolic-ref",
      "--short",
      "refs/remotes/origin/HEAD",
    ]);
    const resolved = headRef.stdout.trim().replace(/^origin\//, "");
    if (headRef.exitCode === 0 && resolved !== "") {
      defaultBranch = resolved;
    }
  } catch {}

  if (input.dryRun) {
    if (!parentWritable) planned.push(`mkdir -p ${HEADLESS_LOG_DIR}`);
    planned.push(
      ["git", "fetch", "origin", defaultBranch].map(shellQuote).join(" "),
    );
    planned.push(
      [
        "git",
        "worktree",
        "add",
        worktreeDir,
        "-b",
        branch,
        `origin/${defaultBranch}`,
      ]
        .map(shellQuote)
        .join(" "),
    );
    planned.push(
      ["git", "push", "-u", "origin", branch].map(shellQuote).join(" "),
    );
    planned.push(
      ["gh", "issue", "edit", String(target.number), "--add-assignee", "@me"]
        .map(shellQuote)
        .join(" "),
    );
    if (itemEditArgv) planned.push(itemEditArgv.map(shellQuote).join(" "));
  } else {
    if (!parentWritable) {
      await runGh(run, ["mkdir", "-p", HEADLESS_LOG_DIR]);
    }
    steps.push({
      step: "worktree-dir",
      status: "done",
      detail: parentWritable
        ? worktreeDir
        : `${worktreeDir} (parent unwritable; mkdir fallback)`,
    });
    const fetchOut = await runGh(run, [
      "git",
      "fetch",
      "origin",
      defaultBranch,
    ]);
    if (fetchOut.exitCode !== 0) {
      steps.push({
        step: "fetch",
        status: "failed",
        detail: snippet(fetchOut),
      });
      return {
        issue: n,
        ok: false,
        conflict: `git fetch origin ${defaultBranch} failed: ${snippet(fetchOut)}`,
        steps,
      };
    }
    steps.push({
      step: "fetch",
      status: "done",
      detail: `origin/${defaultBranch}`,
    });
    const addOut = await runGh(run, [
      "git",
      "worktree",
      "add",
      worktreeDir,
      "-b",
      branch,
      `origin/${defaultBranch}`,
    ]);
    if (addOut.exitCode !== 0) {
      steps.push({
        step: "worktree-add",
        status: "failed",
        detail: snippet(addOut),
      });
      return {
        issue: n,
        ok: false,
        conflict: `git worktree add failed for ${branch}: ${snippet(addOut)}`,
        steps,
      };
    }
    steps.push({ step: "worktree-add", status: "done", detail: worktreeDir });
    const pushOut = await runGh(run, ["git", "push", "-u", "origin", branch]);
    if (pushOut.exitCode !== 0) {
      steps.push({ step: "push", status: "failed", detail: snippet(pushOut) });
      return {
        issue: n,
        ok: false,
        conflict: `git push failed for ${branch}: ${snippet(pushOut)}`,
        steps,
      };
    }
    steps.push({ step: "push", status: "done", detail: `origin/${branch}` });
    const assigneeOut = await runGh(run, [
      "gh",
      "issue",
      "edit",
      String(target.number),
      "--add-assignee",
      "@me",
    ]);
    steps.push(
      assigneeOut.exitCode === 0
        ? { step: "assignee", status: "done", detail: "@me" }
        : { step: "assignee", status: "failed", detail: snippet(assigneeOut) },
    );
    if (!itemEditArgv) {
      steps.push({
        step: "board-sync",
        status: "skipped",
        detail: `unresolved ids: ${[
          snapshot.projectId === undefined ? "project id" : "",
          statusFid === undefined ? "Status field id" : "",
          statusOid === undefined
            ? `Status option '${STATUS_IN_PROGRESS}' id`
            : "",
          itemId === undefined ? `board item id for #${target.number}` : "",
        ]
          .filter((s) => s !== "")
          .join(", ")}`,
      });
    } else {
      const editOut = await runGh(run, itemEditArgv);
      steps.push(
        editOut.exitCode === 0
          ? {
              step: "board-sync",
              status: "done",
              detail: `Status -> ${STATUS_IN_PROGRESS}`,
            }
          : {
              step: "board-sync",
              status: "failed",
              detail: snippet(editOut),
            },
      );
    }
  }

  const devcontainerProbe = await probeAvailable(run, [
    "devcontainer",
    "--version",
  ]);
  const runtimeMissing: string[] = [];
  let runtime: "opencode" | "claude" = input.runtime ?? "opencode";
  let launchMode: LaunchMode = "prepare-only";
  if (devcontainerProbe.ok) {
    launchMode = "devcontainer-headless";
  } else {
    const candidates: Array<"opencode" | "claude"> =
      input.runtime === "claude" ? ["claude"] : ["opencode", "claude"];
    for (const candidate of candidates) {
      const rtProbe = await probeAvailable(run, [candidate, "--version"]);
      steps.push({
        step: `runtime-probe-${candidate}`,
        status: rtProbe.ok ? "done" : "skipped",
        detail: rtProbe.ok
          ? `${candidate} reachable`
          : `${candidate} unreachable (${rtProbe.reason})`,
      });
      if (!rtProbe.ok) {
        runtimeMissing.push(`${candidate} unreachable (${rtProbe.reason})`);
        continue;
      }
      runtime = candidate;
      launchMode =
        candidate === "opencode" ? "opencode-headless" : "claude-headless";
      break;
    }
  }
  const opencodeManualCmd = `cd ${worktreeDir} && opencode --auto --prompt ${shellQuote(`@ship ${goal}`)}`;
  const claudeManualCmd = `cd ${worktreeDir} && claude -p ${shellQuote(goal)}`;

  if (launchMode === "prepare-only") {
    if (input.dryRun) {
      planned.push(`manual: ${opencodeManualCmd}`);
      planned.push(`manual: ${claudeManualCmd}`);
      return {
        issue: n,
        ok: true,
        dryRun: true,
        launchMode,
        worktree: worktreeDir,
        planned,
        steps,
      };
    }
    steps.push({
      step: "launch",
      status: "skipped",
      detail: `prepare-only; ${opencodeManualCmd} or ${claudeManualCmd}`,
    });
    return {
      issue: n,
      ok: false,
      launchMode,
      branch,
      worktree: worktreeDir,
      conflict: `no agent runtime (${runtimeMissing.join("; ")}) - claim prepared in ${worktreeDir} on branch ${branch}; run manually: ${opencodeManualCmd} or ${claudeManualCmd}`,
      steps,
    };
  }

  // Plain `-p` is the repo's only sanctioned headless claude form; no
  // permission-bypass flag has any in-repo precedent.
  const claudeLaunchCmd = ["claude", "-p", goal];
  const launchCmd =
    launchMode === "devcontainer-headless"
      ? [
          "devcontainer",
          "exec",
          "--default-user-env-probe",
          "none",
          "--remote-env",
          `PATH=${DEVCONTAINER_REMOTE_PATH}`,
          "--workspace-folder",
          worktreeDir,
          ...(runtime === "claude"
            ? claudeLaunchCmd
            : ["opencode", "--auto", "--prompt", goal]),
        ]
      : runtime === "claude"
        ? claudeLaunchCmd
        : ["opencode", "--auto", "--prompt", goal];
  const launchArgv = [
    "bash",
    "-c",
    HEADLESS_LAUNCH_SH,
    logPath,
    worktreeDir,
    ...launchCmd,
  ];

  if (input.dryRun) {
    if (launchMode === "devcontainer-headless") {
      planned.push(
        ["devcontainer", "up", "--workspace-folder", worktreeDir]
          .map(shellQuote)
          .join(" "),
      );
    }
    planned.push(launchArgv.map(shellQuote).join(" "));
    return {
      issue: n,
      ok: true,
      dryRun: true,
      launchMode,
      runtime,
      worktree: worktreeDir,
      planned,
      steps,
    };
  }

  if (launchMode === "devcontainer-headless") {
    const upOut = await runGh(run, [
      "devcontainer",
      "up",
      "--workspace-folder",
      worktreeDir,
    ]);
    if (upOut.exitCode !== 0) {
      steps.push({
        step: "launch",
        status: "failed",
        detail: `devcontainer up: ${snippet(upOut)}`,
      });
      return {
        issue: n,
        ok: false,
        launchMode,
        branch,
        worktree: worktreeDir,
        conflict: `devcontainer up failed in ${worktreeDir}: ${snippet(upOut)}`,
        steps,
      };
    }
  }
  const launchOut = await runGh(run, launchArgv);
  const pid = parseInt(launchOut.stdout.trim(), 10);
  if (Number.isNaN(pid)) {
    steps.push({
      step: "launch",
      status: "failed",
      detail: `no pid from background launch: ${snippet(launchOut)}`,
    });
    return {
      issue: n,
      ok: false,
      launchMode,
      branch,
      worktree: worktreeDir,
      conflict: `background launch failed in ${worktreeDir}: ${snippet(launchOut)}`,
      steps,
    };
  }
  const modeLabel =
    launchMode === "devcontainer-headless"
      ? `devcontainer-headless (${runtime})`
      : launchMode;
  steps.push({
    step: "launch",
    status: "done",
    detail: `${modeLabel}; log ${logPath}, pid ${pid}`,
  });
  return {
    issue: n,
    ok: true,
    launchMode,
    runtime,
    branch,
    worktree: worktreeDir,
    log: logPath,
    pid,
    steps,
  };
}

function renderIssueResult(result: IssueDispatchResult): string {
  if (result.launch) {
    const claudePane =
      result.launchMode === "herdr" && result.runtime === "claude"
        ? " (herdr (claude))"
        : "";
    return `dispatched #${result.launch.issue} on branch ${result.launch.branch}${claudePane}`;
  }
  if (
    result.ok &&
    (result.launchMode === "devcontainer-headless" ||
      result.launchMode === "opencode-headless" ||
      result.launchMode === "claude-headless")
  ) {
    const mode =
      result.launchMode === "devcontainer-headless"
        ? `devcontainer-headless (${result.runtime ?? "opencode"})`
        : result.launchMode;
    return `dispatched #${result.issue} on branch ${result.branch} (${mode}; log ${result.log}, pid ${result.pid})`;
  }
  if (result.flagged) {
    return `flagged #${result.issue} needs-spec (intake gate failed)`;
  }
  return `#${result.issue} conflict: ${result.conflict ?? "unknown"}`;
}

export function renderDispatchOutput(report: DispatchReport): string {
  if (report.results.length === 0) {
    return `conflict: ${report.conflict ?? "unknown"}`;
  }
  if (report.dryRun) {
    const plannedCount = report.results.reduce(
      (count, r) => count + (r.planned?.length ?? 0),
      0,
    );
    return `dry-run: ${plannedCount} planned commands across ${report.results.length} issue(s), nothing executed`;
  }
  return report.results.map(renderIssueResult).join("; ");
}

export function renderSettleOutput(
  result: {
    ok: boolean;
    outcome: "open" | "merged" | "rolled-back" | "unknown";
    prUrl?: string;
  },
  branch: string,
): string {
  if (result.outcome === "unknown") {
    return `PR state unknown on ${branch}: pr list unreadable`;
  }
  return result.outcome === "merged"
    ? `PR merged: ${result.prUrl} - no rollback`
    : result.outcome === "open"
      ? result.ok
        ? `PR open: ${result.prUrl} (link-back ensured)`
        : `PR open: ${result.prUrl} (link-back failed - see steps)`
      : result.ok
        ? `no open PR on ${branch}: Status rolled back to Todo`
        : `no open PR on ${branch}: Status rollback incomplete`;
}

export async function settleRun(
  deps: { worktree: string; run?: GhRunner },
  input: { issue: number; branch: string },
): Promise<{
  ok: boolean;
  outcome: "open" | "merged" | "rolled-back" | "unknown";
  prUrl?: string;
  steps: DispatchStep[];
}> {
  const run = deps.run ?? ghRunner(deps.worktree);
  const steps: DispatchStep[] = [];

  const prOut = await runGh(run, [
    "gh",
    "pr",
    "list",
    "--head",
    input.branch,
    "--state",
    "all",
    "--json",
    "number,url,state",
  ]);
  const prList =
    prOut.exitCode === 0
      ? parseJson<Array<{ number: number; url?: string; state?: string }>>(
          prOut.stdout,
        )
      : undefined;
  // An unreadable pr list must not degrade into the rollback path: it mutates.
  if (!prList) {
    steps.push({ step: "pr-list", status: "failed", detail: snippet(prOut) });
    return { ok: false, outcome: "unknown", steps };
  }
  const prUrl = prList.find((p) => p.state === "OPEN")?.url;
  if (prUrl) {
    const commentsOut = await runGh(run, [
      "gh",
      "api",
      `repos/${repoSlug(deps.worktree)}/issues/${input.issue}/comments`,
      "--paginate",
      "--slurp",
    ]);
    // --slurp emits one outer JSON array holding each page's array; flatten
    // before scanning so page-2 comments still dedupe.
    const commentPages =
      commentsOut.exitCode === 0
        ? parseJson<Array<Array<{ body?: string | null }>>>(commentsOut.stdout)
        : undefined;
    const comments = commentPages?.flat();
    if (comments?.some((c) => (c.body ?? "").includes(prUrl))) {
      steps.push({
        step: "link-back",
        status: "skipped",
        detail: "comment already contains PR URL",
      });
      return { ok: true, outcome: "open", prUrl, steps };
    }
    if (comments === undefined) {
      steps.push({
        step: "link-back",
        status: "failed",
        detail: `cannot verify existing comments: ${snippet(commentsOut)}`,
      });
      return { ok: false, outcome: "open", prUrl, steps };
    }
    const commentOut = await runGh(run, [
      "gh",
      "issue",
      "comment",
      String(input.issue),
      "--body",
      prLinkComment(prUrl),
    ]);
    steps.push(
      commentOut.exitCode === 0
        ? { step: "link-back", status: "done", detail: prUrl }
        : { step: "link-back", status: "failed", detail: snippet(commentOut) },
    );
    return { ok: commentOut.exitCode === 0, outcome: "open", prUrl, steps };
  }

  // A merged PR (auto-merge or human) resolves the run; rolling the issue
  // back to Todo would resurrect it on the dispatch frontier.
  const mergedUrl = prList.find((p) => p.state === "MERGED")?.url;
  if (mergedUrl) {
    steps.push({
      step: "status-rollback",
      status: "skipped",
      detail: `PR merged - no rollback (${mergedUrl})`,
    });
    return { ok: true, outcome: "merged", prUrl: mergedUrl, steps };
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
      step: "status-rollback",
      status: "skipped",
      detail: `board ${boardRef().number} id not resolvable (${snippet(boardOut)})`,
    });
    return { ok: false, outcome: "rolled-back", steps };
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
  const statusField =
    fieldsOut.exitCode === 0
      ? parseJson<FieldListShape>(fieldsOut.stdout)?.fields?.find(
          (f) => f.name === "Status",
        )
      : undefined;
  const statusFid = statusField?.id;
  if (!statusFid) {
    steps.push({
      step: "status-rollback",
      status: "skipped",
      detail: `field 'Status' not found on board ${boardRef().number}`,
    });
    return { ok: false, outcome: "rolled-back", steps };
  }
  const todoOid = statusField?.options?.find((o) => o.name === boardRef().statusTodo)?.id;
  if (!todoOid) {
    steps.push({
      step: "status-rollback",
      status: "skipped",
      detail: `option '${boardRef().statusTodo}' not found on field 'Status'`,
    });
    return { ok: false, outcome: "rolled-back", steps };
  }

  const itemOut = await runGh(run, [
    "gh",
    "project",
    "item-list",
    String(boardRef().number),
    "--owner",
    boardRef().owner,
    "--limit",
    "1000",
    "--format",
    "json",
  ]);
  let itemId: string | undefined;
  if (itemOut.exitCode === 0) {
    for (const raw of parseJson<ItemListShape>(itemOut.stdout)?.items ?? []) {
      if (raw.content?.number === input.issue && raw.id) {
        itemId = raw.id;
        break;
      }
    }
  }
  if (!itemId) {
    steps.push({
      step: "status-rollback",
      status: "skipped",
      detail: `issue #${input.issue} is not an item on board ${boardRef().number}`,
    });
    return { ok: false, outcome: "rolled-back", steps };
  }

  const editOut = await runGh(run, [
    "gh",
    "project",
    "item-edit",
    "--id",
    itemId,
    "--field-id",
    statusFid,
    "--project-id",
    projectId,
    "--single-select-option-id",
    todoOid,
  ]);
  steps.push(
    editOut.exitCode === 0
      ? {
          step: "status-rollback",
          status: "done",
          detail: `Status -> ${boardRef().statusTodo}`,
        }
      : { step: "status-rollback", status: "failed", detail: snippet(editOut) },
  );
  return { ok: editOut.exitCode === 0, outcome: "rolled-back", steps };
}

export const dispatch_issue = defineTool({
  description:
    "Selection-driven multi-dispatch: each selected issue goes into its own worktree " +
    "with its own ship run. The selection IS the per-issue human green light (L/XL " +
    "sizes included) - obtain it from the ship-next_pick_next shortlist plus the " +
    "operator's multi-select answer, or an explicit /ship-next <N[,...]> invocation. " +
    "Re-runs the gates on live board state per issue: candidate must be an open " +
    "board item with Status Todo/Next and unclaimed (no open PR referencing it, no " +
    "issue-<N>-* branch on origin, never a reused worktree); no ship-ready label, " +
    "Priority, Size, or blocked-by requirement - the human selected with those " +
    "annotations visible upstream. Issues failing the intake gate are flagged with " +
    "needs-spec plus one what's-missing comment instead of being claimed. Launches " +
    "the ship session via herdr when available, else headless in a devcontainer or " +
    "bare opencode with claude as the fallback when opencode is unreachable; an " +
    "explicit claude runtime launches in the herdr pane via the vendored skill's " +
    "--agent claude when the skill supports it, falling back to headless when the " +
    "vendored skill predates the flag; when no runtime is reachable the claim is " +
    "prepared and both manual commands (opencode, claude) are reported as a " +
    "per-issue conflict. Selections run sequentially; one issue's failure never " +
    "aborts the remaining selections. Never asks - the attended question lives in " +
    "the command layer. dryRun plans every mutation without executing it. The " +
    "authoritative field schema is at the repo's issue-schema doc.",
  args: {
    issues: z
      .array(z.number())
      .min(1)
      .describe(
        "One or more selected issue numbers; the per-issue human green light (also unlocks L/XL); obtained from the pick_next shortlist + the operator's multi-select answer, or an explicit /ship-next <N[,...]> invocation",
      ),
    dryRun: z
      .boolean()
      .optional()
      .describe(
        "Plan only: list the would-be mutations without executing anything",
      ),
    runtime: z
      .enum(["opencode", "claude"])
      .optional()
      .describe(
        "Agent runtime for the launch: opencode (default) or claude (explicit choice; also the automatic fallback when opencode is unreachable)",
      ),
  },
  async execute(args, context) {
    refreshConfig(context.worktree);
    await context.progress?.({ status: `dispatching issue #${args.issue}` });
    try {
      const report = await dispatchIssue(
        { worktree: context.worktree, run: ghRunner(context.worktree, context.signal) },
        { issues: args.issues, dryRun: args.dryRun, runtime: args.runtime },
      );
      const output = renderDispatchOutput(report);
      return {
        title: "ship-next_dispatch_issue",
        output,
        metadata: report,
      };
    } catch (e) {
      const msg = errMsg(e);
      return {
        title: "ship-next_dispatch_issue - gh error",
        output: `dispatch_issue failed: ${msg}`,
        metadata: { error: msg },
      };
    }
  },
});

export const settle_run = defineTool({
  description:
    "Settle a dispatched run once its ship session ends: with an open PR on the branch, post " +
    "the PR link-back comment on the issue (idempotent - skipped when an existing comment " +
    "already contains the PR URL); with no open PR, roll the Projects board Status back to " +
    "Todo so the issue re-enters the dispatch frontier; rollback leaves the issue assignee " +
    "in place. Resolution misses skip with a detail rather than abort. Creates no PRs and " +
    "pushes nothing.",
  args: {
    issue: z
      .number()
      .describe("Issue number the run was dispatched for"),
    branch: z
      .string()
      .describe("Branch the run worked on (issue-<n>-<slug>)"),
  },
  async execute(args, context) {
    refreshConfig(context.worktree);
    try {
      const result = await settleRun(
        { worktree: context.worktree, run: ghRunner(context.worktree, context.signal) },
        { issue: args.issue, branch: args.branch },
      );
      const output = result.prUrl
        ? `PR open: ${result.prUrl} (link-back ensured)`
        : result.ok
          ? `no open PR on ${args.branch}: Status rolled back to Todo`
          : `no open PR on ${args.branch}: Status rollback incomplete`;
      return {
        title: "ship-next_settle_run",
        output,
        metadata: result,
      };
    } catch (e) {
      const msg = errMsg(e);
      return {
        title: "ship-next_settle_run - gh error",
        output: `settle_run failed: ${msg}`,
        metadata: { error: msg },
      };
    }
  },
});
