// Prepends a knowledge-graph reminder to the first shell command when a
// graph exists. Reminder text must stay free of backticks and $(...) - it is
// prepended inside a double-quoted echo.
import { existsSync } from "node:fs";
import { join } from "node:path";
import type { Plugin } from "@opencode/plugin";

export const DEFAULT_REMINDER =
  'echo "[graphify] knowledge graph at graphify-out/. For focused questions, run graphify query with your question (scoped subgraph, usually much smaller than GRAPH_REPORT.md) instead of grepping raw files. Read GRAPH_REPORT.md only for broad architecture context." ; ';

export async function registerGraphifyReminder(
  ctx: Pick<Plugin.Context, "tool">,
  options: { directory?: string; reminder?: string } = {},
): Promise<void> {
  const directory = options.directory ?? process.cwd();
  const reminder = options.reminder ?? DEFAULT_REMINDER;
  let reminded = false;
  await ctx.tool.hook("execute.before", (event) => {
    if (reminded) return;
    if (event.tool !== "shell" && event.tool !== "bash") return;
    if (!existsSync(join(directory, "graphify-out", "graph.json"))) return;
    const command = (event.input as { command?: string } | undefined)?.command;
    if (typeof command !== "string") return;
    (event.input as { command: string }).command = reminder + command;
    reminded = true;
  });
}
