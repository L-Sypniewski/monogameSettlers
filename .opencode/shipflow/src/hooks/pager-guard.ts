// Forces cat-style pagers on every tool shell: PTY shells block forever
// waiting for pager interaction that never comes.
const PAGER_ENV: Record<string, string> = {
  PAGER: "cat",
  GIT_PAGER: "cat",
  // gh pager precedence: GH_PAGER > GITHUB_PAGER > PAGER.
  GH_PAGER: "cat",
  GIT_CONFIG_COUNT: "1",
  GIT_CONFIG_KEY_0: "core.pager",
  GIT_CONFIG_VALUE_0: "cat",
};

import type { Plugin } from "@opencode/plugin";

export async function registerPagerGuard(
  ctx: Pick<Plugin.Context, "shell">,
): Promise<void> {
  await ctx.shell.hook("create.before", (event) => {
    Object.assign(event.env, PAGER_ENV);
  });
}
