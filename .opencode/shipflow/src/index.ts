// The shipflow plugin: pipeline tools, guards, worktree strategy and
// per-agent temperature, all option-driven (see README). Board wiring comes
// from the repo's own .shipflow.json; agents/commands/rules ship as
// templates kept in .opencode/.
import { Plugin } from "@opencode/plugin";
import { registerPagerGuard } from "./hooks/pager-guard.ts";
import {
  registerSecretGuard,
  guardToolArgs,
} from "./hooks/secret-guard.ts";
import {
  registerFastChecksHook,
  isSourceEdit,
  DEFAULT_SOURCE_DIRS,
} from "./hooks/fast-checks-hook.ts";
import { registerGraphifyReminder } from "./hooks/graphify-reminder.ts";
import { makeWorktreeStrategy } from "./worktree.ts";
import { registerTools } from "./tools.ts";
import { shapeToJsonSchema } from "./schema.ts";
import { applyTemperatures } from "./session-context.ts";
import {
  configureShipflow,
  configureAutoMergeArm,
  autoMergeArmed,
  refreshConfig,
  repoSlug,
  shipflowConfig,
  WORKFLOW_RESERVED_LABELS,
  type ShipflowConfig,
  type BoardConfig,
  type AutoMergeConfig,
} from "./config.ts";
import { record_replan, record_verify, gate_check, evaluateGate } from "./tools/loop.ts";
import {
  create_pr,
  create_followup_issue,
  finalize_pr,
} from "./tools/ship.ts";
import { create_wired } from "./tools/issue-intake.ts";
import {
  pick_next,
  dispatch_issue,
  settle_run,
} from "./tools/ship-next.ts";
import {
  stakeholder_report_check_presented,
  stakeholder_report_record_presented,
} from "./tools/stakeholder-report.ts";

export { shapeToJsonSchema } from "./schema.ts";
export { registerTools } from "./tools.ts";
export { applyTemperatures } from "./session-context.ts";
export { makeWorktreeStrategy } from "./worktree.ts";
export {
  registerPagerGuard,
  registerSecretGuard,
  registerFastChecksHook,
  registerGraphifyReminder,
  guardToolArgs,
  isSourceEdit,
  DEFAULT_SOURCE_DIRS,
};
export {
  configureShipflow,
  configureAutoMergeArm,
  autoMergeArmed,
  refreshConfig,
  repoSlug,
  shipflowConfig,
  WORKFLOW_RESERVED_LABELS,
  type ShipflowConfig,
  type BoardConfig,
  type AutoMergeConfig,
};
export {
  record_replan,
  record_verify,
  gate_check,
  evaluateGate,
  create_pr,
  create_followup_issue,
  create_wired,
  pick_next,
  dispatch_issue,
  settle_run,
  stakeholder_report_check_presented,
  stakeholder_report_record_presented,
};

export interface ShipflowOptions {
  // Register the pipeline tools (default on) under their fixed ids. Board
  // wiring comes from the repo's .shipflow.json (refreshConfig), not options.
  tools?: boolean;
  // Kill switch for ship_finalize_pr's auto-merge arm (the ready-flip always
  // runs; false keeps even a configured repo merge-arm-off).
  autoMerge?: boolean;
  // Per-agent temperature pins, applied per model request.
  temperatures?: Record<string, number>;
  // All guards default ON; pass false to opt out.
  pagerGuard?: boolean;
  secretGuard?: boolean;
  graphify?: boolean | { directory?: string; reminder?: string };
  // Auto-run a drift-gate script when the session idles after source edits
  // under the given source dirs (default backend/ + frontend/).
  fastChecks?: { script: string; sourceDirs?: string[] };
  // Explicit opt-in: registers the worktree strategy for opencode-side
  // create/remove/list (git worktree + sync contract + hooks).
  worktree?: {
    copyFiles?: string[];
    symlinkDirs?: string[];
    postCreate?: string[];
  };
}

const DEFAULT_GRAPHIFY_REMINDER =
  'echo "[graphify] knowledge graph at graphify-out/. For focused questions, run graphify query with your question (scoped subgraph, usually much smaller than GRAPH_REPORT.md) instead of grepping raw files. Read GRAPH_REPORT.md only for broad architecture context." ; ';

// Fixed tool ids: `<file>_<export>` naming (permission rules and agent
// prompts reference these ids).
const TOOL_SPECS: ReadonlyArray<{ name: string; tool: any }> = [
  { name: "loop_record_replan", tool: record_replan },
  { name: "loop_record_verify", tool: record_verify },
  { name: "loop_gate_check", tool: gate_check },
  { name: "ship_create_pr", tool: create_pr },
  { name: "ship_create_followup_issue", tool: create_followup_issue },
  { name: "ship_finalize_pr", tool: finalize_pr },
  { name: "issue_intake_create_wired", tool: create_wired },
  { name: "ship-next_pick_next", tool: pick_next },
  { name: "ship-next_dispatch_issue", tool: dispatch_issue },
  { name: "ship-next_settle_run", tool: settle_run },
  {
    name: "stakeholder_report_check_presented",
    tool: stakeholder_report_check_presented,
  },
  {
    name: "stakeholder_report_record_presented",
    tool: stakeholder_report_record_presented,
  },
];

export const TOOL_IDS = TOOL_SPECS.map((s) => s.name);

async function setup(ctx: Plugin.Context): Promise<(() => void) | void> {
  const options = (ctx.options ?? {}) as ShipflowOptions;
  configureAutoMergeArm(options.autoMerge);
  const cleanups: Array<() => void> = [];
  const add = (d: unknown) => {
    if (typeof d === "function") cleanups.push(d as () => void);
  };

  if (options.tools !== false) {
    await registerTools(ctx, TOOL_SPECS, ctx.location.directory);
  }
  if (options.temperatures) {
    await applyTemperatures(ctx as never, options.temperatures);
  }
  if (options.pagerGuard !== false) await registerPagerGuard(ctx);
  if (options.secretGuard !== false)
    add(await registerSecretGuard(ctx, ctx.location.directory));
  if (options.graphify !== false) {
    const cfg =
      typeof options.graphify === "object" ? options.graphify : undefined;
    add(
      await registerGraphifyReminder(ctx, {
        directory: cfg?.directory ?? ctx.location.directory,
        reminder: cfg?.reminder ?? DEFAULT_GRAPHIFY_REMINDER,
      }),
    );
  }
  if (options.fastChecks)
    add(
      await registerFastChecksHook(ctx, {
        script: options.fastChecks.script,
        sourceDirs: options.fastChecks.sourceDirs,
      }),
    );
  if (options.worktree) {
    await ctx.worktree.transform((editor) => {
      editor.add(
        makeWorktreeStrategy({
          sourceDirectory: ctx.location.directory,
          copyFiles: options.worktree?.copyFiles,
          symlinkDirs: options.worktree?.symlinkDirs,
          postCreate: options.worktree?.postCreate,
        }) as never,
      );
    });
  }

  return cleanups.length > 0
    ? () => cleanups.forEach((c) => c())
    : undefined;
}

export default Plugin.define({
  id: "shipflow",
  setup,
});
