// Tool definition contract. The runtime context carries no worktree field,
// so registration closes over the project checkout and supplies it here.
import type { z } from "zod";

// forwards (signal/progress/session identity) plus the registered worktree.
export interface ToolRuntime {
  readonly sessionID?: string;
  readonly agent?: string;
  readonly signal: AbortSignal;
  readonly progress: (update: Record<string, unknown>) => Promise<void>;
  readonly worktree: string;
}

// by the engine (content = output; title/metadata carried verbatim - the
export interface ToolOutput {
  title?: string;
  output?: string;
  metadata?: Record<string, unknown>;
}

export interface ToolDefinition {
  readonly description: string;
  // zod stays the authoring format: typed args + the single source the
  // engine derives each tool's JSON Schema input from (no drift possible).
  readonly args: Record<string, z.ZodTypeAny>;
  execute(
    args: any,
    context: ToolRuntime,
  ): Promise<string | ToolOutput | undefined>;
}

export function defineTool(def: ToolDefinition): ToolDefinition {
  return def;
}
