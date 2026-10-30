// Per-agent temperature, applied per model request: the runtime preserves
// frontmatter temperature but never sends it, and drops an agent's entire
// permissions frontmatter when the field is present.
import type { Plugin } from "@opencode/plugin";

export async function applyTemperatures(
  ctx: Pick<Plugin.Context, "session">,
  temperatures: Record<string, number>,
): Promise<void> {
  await ctx.session.hook("context", async (event) => {
    if (!event.agent) return;
    const temperature = temperatures[event.agent];
    if (temperature !== undefined) {
      event.options.temperature = temperature;
    }
  });
}
