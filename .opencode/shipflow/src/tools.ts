// Tool registration: fixed ids, codemode off (the Code Mode default would
// rename tools to tools.<name> and unmatch permission globs), JSON Schema
// from live zod shapes, {content, title, metadata} results.
import type { Plugin } from "@opencode/plugin";
import { shapeToJsonSchema } from "./schema.ts";
import type { ToolDefinition, ToolOutput } from "./define-tool.ts";

export interface ToolSpec {
  name: string;
  tool: ToolDefinition;
}

export async function registerTools(
  ctx: Pick<Plugin.Context, "tool">,
  specs: readonly ToolSpec[],
  worktree: string,
): Promise<void> {
  await ctx.tool.transform((editor) => {
    const add = editor.add as (tool: {
      name: string;
      description: string;
      input: Record<string, unknown>;
      options: { codemode: false };
      execute: (input: any, context: any) => Promise<unknown>;
    }) => void;
    for (const spec of specs) {
      add({
        name: spec.name,
        description: spec.tool.description,
        input: shapeToJsonSchema(spec.tool.args),
        options: { codemode: false },
        async execute(
          input: Record<string, unknown>,
          context?: {
            sessionID?: string;
            agent?: string;
            signal?: AbortSignal;
            progress?: (update: Record<string, unknown>) => Promise<void>;
          },
        ) {
          // Tolerate a missing context (direct calls in tests): the runtime
          // always supplies signal/progress, tests may omit them.
          const toolContext = context ?? {};
          const result = (await spec.tool.execute(input, {
            sessionID: toolContext.sessionID,
            agent: toolContext.agent,
            signal:
              toolContext.signal ?? new AbortController().signal,
            progress:
              toolContext.progress ?? (async () => {}),
            worktree,
          })) as string | ToolOutput | undefined;
          if (typeof result === "string" || result === undefined) {
            return { content: result ?? "" };
          }
          return {
            content: result.output ?? "",
            ...(result.title !== undefined ? { title: result.title } : {}),
            ...(result.metadata !== undefined
              ? { metadata: result.metadata }
              : {}),
          };
        },
      });
    }
  });
}
