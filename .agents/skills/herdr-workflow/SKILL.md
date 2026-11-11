---
name: herdr-workflow
description: Automates the herdr issue-driven worktree workflow end-to-end - creates a grouped herdr worktree workspace for a GitHub issue or free-text task, pushes the branch to origin immediately, and launches opencode in the worktree's root pane (devcontainer when the repo has one, host otherwise). Use when the user mentions herdr, asks for a worktree for an issue or task, wants to open, restore (after closing a group) or clean up existing herdr worktree workspaces, wants to launch an agent (devcontainer+opencode) for an issue or task, or asks for a status overview of herdr workspaces, worktrees, and agents.
argument-hint: "[issue-numbers | task description | restore | cleanup | status]"
allowed-tools: Bash(${CLAUDE_SKILL_DIR}/scripts/herdr-issue.sh:*) Bash(${CLAUDE_SKILL_DIR}/scripts/herdr-open.sh:*) Bash(${CLAUDE_SKILL_DIR}/scripts/herdr-restore.sh:*) Bash(${CLAUDE_SKILL_DIR}/scripts/herdr-cleanup.sh:*) Bash(${CLAUDE_SKILL_DIR}/scripts/herdr-status.sh:*) Bash(herdr:*) Bash(gh issue view:*) Bash(git fetch:*) Bash(git worktree list:*)
---

# herdr Workflow

Automates the multi-repo herdr workflow: issue-driven git worktrees as grouped herdr workspaces, branch pushed to origin from the start, opencode launched in the worktree's root pane (devcontainer when the repo has one, host otherwise).

## Purpose & Mental Model

```
▸ <repo>                     ← primary workspace (label = repo name)
    ▸ #123 fix-login-timeout   ← worktree workspace (grouped)
    ▸ #145 export-csv          ← worktree workspace
```

- **Repo = primary herdr workspace** - deterministic label equal to the repo name, cwd = main checkout. This is the group anchor.
- **Each worktree = its own workspace** - created herdr-natively via `herdr worktree create`, checked out at `~/.herdr/worktrees/<repo>/<branch-slug>`, auto-grouped under the repo's primary workspace.
- **Issue-driven**: GitHub issue → branch `issue-<n>-<title-slug>` off latest `origin/<default-branch>` → pushed immediately → opencode launched (devcontainer when the repo has one, host otherwise).
- **Worktree = workspace, never a tab** - each worktree workspace still owns its own tabs/panes inside it.

## Runtime herdr knowledge (REQUIRED first step)

herdr evolves; this skill must NOT hardcode herdr's skill content. Before any herdr usage:

1. If a herdr skill is already in this session's context (e.g. this session runs inside a herdr pane with integration), skip this step.
2. Otherwise run `herdr --skill` and treat the printed skill as the authoritative reference for CLI semantics and safety rules.
3. Get exact flags from group help - running the group alone prints usage: `herdr workspace`, `herdr worktree`, `herdr tab`, `herdr pane`, `herdr agent`.
4. Never run bare `herdr` - it launches the TUI. Never probe a mutating nested command without its required args; commands like `herdr workspace create` are valid with defaults and WILL execute.
5. herdr exit codes: `1` = server error (JSON on stderr), `2` = usage error. If a command fails with exit 2, re-derive syntax from `herdr --skill` / group help and adapt.

## Preflight

Run once per session before dispatching:

```bash
command -v herdr && herdr status
```

- `herdr status` confirms the CLI is reachable and the server is running; it works from outside herdr panes.
- If this session itself runs inside a devcontainer without the herdr socket, **stop and explain** - the scripts must run where herdr's socket is reachable.
- Repo preflight (in the repo dir, or the one the user named):
  - `gh auth status` - must succeed.
  - `.devcontainer/devcontainer.json` exists - assumed present for devcontainer launches (devcontainer CLI + Docker required, daemon running; `docker info` warn-only).
  - `.devcontainer/devcontainer.json` MISSING - host-mode fallback: skip the devcontainer launch and start the agent on the host in the worktree pane through the launch shim (see Host-mode fallback).

The bundled scripts self-check herdr/jq/gh/git themselves and only require devcontainer/docker when the repo has a devcontainer config; preflight here catches problems before the pipeline starts.

## Workflows → Script Dispatch

| User intent                                                                 | Dispatch                                                                                                                                  |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Issue number(s) - `/herdr-workflow 123`, "work on issue 123"                | `${CLAUDE_SKILL_DIR}/scripts/herdr-issue.sh 123`                                                                                          |
| Multiple issues - `/herdr-workflow 123,124`                                 | One `herdr-issue.sh <n>` call per issue (one worktree each)                                                                               |
| Free-text task - `/herdr-workflow add dark mode toggle`                     | `herdr-issue.sh --branch <slug> --prompt "<task>"` - derive a short kebab-case branch slug from the task yourself                         |
| Open existing branch/worktree                                               | `herdr-open.sh --branch <B>` or `--path <P>` (idempotent)                                                                                 |
| Restore a closed repo group (workspaces closed by mistake, herdr restarted) | `herdr-restore.sh --repo DIR` - reads the newest session snapshot, reopens each linked worktree via `herdr-open.sh --resume`              |
| Cleanup / remove worktree                                                   | `herdr-cleanup.sh (--branch B \| --path P \| --workspace ID)` - always show the full plan and get user confirmation for destructive parts |
| Status overview                                                             | `herdr-status.sh`                                                                                                                         |
| Auto-continue interrupted agents                                            | `herdr-watchdog.sh` (install once as a user service: `--install-systemd`)                                                                 |

All five scripts accept `--repo DIR` to select the repo when it is not the cwd; for `herdr-restore.sh` the flag is required. `herdr-issue.sh` and `herdr-open.sh` also accept `--no-launch` (skip the launch) and `--focus` (switch your view to the new workspace); creation is BACKGROUND by default - the workspace is created and the launch typed without stealing focus, so a pane you are typing in is never disturbed (`--no-focus` remains accepted as a no-op); `herdr-restore.sh` accepts `--snapshot` and `--no-launch`; `herdr-cleanup.sh` accepts only `--branch`, `--path`, `--workspace`, `--delete-branch`, `--delete-remote-branch`, `--force`, and `--yes`; `herdr-status.sh` accepts only `--repo`. `herdr-issue.sh` additionally accepts `--no-board-sync` (see Issue board sync). `herdr-issue.sh`, `herdr-open.sh`, and `herdr-restore.sh` accept `--agent <opencode|claude>` (agent runtime for launches, default `opencode`; the launch summaries report `agent=`).

Repos without `.devcontainer/devcontainer.json` run the host-mode launch fallback (see Host-mode fallback) - the summary semantics (`launched=yes` once the agent start line was typed) are unchanged.

**Judgment rules:**

- Multiple issues → one worktree each, by default. Combine into a single worktree only on explicit request.
- Reused already-open worktrees are never re-launched by a plain open (focused only when `--focus` is passed; background by default); `--resume` relaunches into the workspace's existing pane exactly when no live agent runs in the worktree's devcontainer - a live one blocks with a warn and `launched=skipped-agent-alive` without typing anything (typing into a pane hosting a running agent corrupts the session; see Determinism & Launch Semantics).
- Restore always goes through `herdr-restore.sh` (or `herdr-open.sh --resume`), never hand-rolled `herdr workspace create` / `worktree open`: plain panes have no `opencode` shim in their process tree, so herdr discards the agent's hook reports and the agent stays undetected. Agents left running from the closed panes are not adopted - a resume refuses to double-run (skips the relaunch while the old agent lives, tallied as `agent_alive=` in the restore summary); stop the stale agent and resume again to relaunch.
- A worktree created outside herdr (plain `git worktree add`, any path) is invisible in the sidebar until linked: `herdr worktree open --cwd <main-checkout> --path <worktree-path> --label <branch-slug> [--no-focus]` registers the existing checkout as its own workspace under the repo group - nothing is recreated and no agent is launched. Scope: surfacing a pre-existing checkout for the human; it is not a restore/launch path (the rule above stands - the plain-pane agent-detection caveat is irrelevant precisely because nothing is launched).
- Issue prompt format: `[<mention> ]Work on issue #<n>: <title> - <url>`, where the optional mention prefix comes from the repo's `issuePromptMention` config / `HERDR_ISSUE_PROMPT_MENTION` env (see Issue board sync) and is absent by default; the prefix pins the pane session's agent, because opencode's TUI `--auto --prompt` path selects the agent by `@mention` in the prompt and a configured `default_agent` is not honored in every launch environment. Free-text tasks pass the description verbatim - never prefixed.
- Cleanup: `herdr worktree remove` never deletes branches by itself - branch deletion (local/remote) is a separate, explicitly confirmed step.

## Determinism & Launch Semantics

The scripts are the source of truth - idempotent, deterministic, printing a `KEY=VALUE` summary (`issue`, `branch`, `worktree_path`, `worktree_workspace`, `pane`, `primary_workspace`, `pushed`, `launched`, `reused`, `agent`). Parse the summary and report: branch, worktree path, herdr workspace/tab/pane ids, push state, launch state. `launched` is `yes` (agent launched into the pane), `no` (launch not requested), or `skipped-agent-alive` (the launch guard found a live agent in the worktree's devcontainer and typed nothing; the warn names the container). `herdr-restore.sh` additionally reports `restored=` (reopened AND agent launched), `agent_alive=` (reopened but the agent already runs - stop it and re-run to take it over), `skipped=` (failures), and `agent=`.

Pipeline order - push comes **before** devcontainer:

1. Branch `issue-<n>-<title-slug>` created off latest `origin/<default-branch>`.
2. Tracking guard: `git branch --unset-upstream` (basing on `origin/<default>` mis-sets upstream via `autoSetupMerge`).
3. Immediate `git push -u origin <branch>` - the user's devcontainers seed a docker volume by cloning the branch **from origin**, so the branch MUST exist on origin before `devcontainer up`.
4. Precreate missing worktree-relative bind-source dirs from the repo's `devcontainer.json` (`hr_precreate_bind_dirs`, called by `hr_launch`): Docker `--mount type=bind` errors on a missing source instead of auto-creating it, so a gitignored scratch dir would kill every fresh worktree at `docker run`. `.devcontainer/**` (initializeCommand-staged) and dot-suffixed file binds are skipped - a missing staged artifact must fail loudly.
5. Wait for the pane's shell to settle (an oh-my-zsh `[Y/n]` update prompt is declined first - it would swallow the typed line), then in the worktree workspace's root pane: `devcontainer up --workspace-folder . && devcontainer exec --default-user-env-probe none --remote-env PATH=/home/vscode/.opencode/bin:/home/vscode/.bun/bin:/mnt/mise-data/shims:/usr/local/share/nvm/current/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/home/vscode/.dotnet:/home/vscode/.dotnet/tools:/home/vscode/.local/bin --workspace-folder . opencode --auto --prompt '<task prompt>'` (run through the agent-bridge shim).
6. **Never pass `--model` to opencode** - repos preselect models in their `opencode.jsonc`.

**Launch guard** (caller-level, in `herdr-open.sh` and `herdr-issue.sh`, before anything is typed into a pane): launches and resumes resolve the worktree's running devcontainer (`docker ps` filtered by the `devcontainer.local_folder` label) and probe it with `docker exec … ps -eo args` for a live agent process. The match is argv0-exact (basename == agent) - a substring match would false-positive on post-create setup shells whose args merely mention the agent's config paths or download URLs (and `docker top` would miss rootless/userns-remapped processes). A `serve --service` daemon process does not block - matched by invocation shape (agent basename with `serve` as its first argument), so flag-carrying daemons are excluded while a client whose prompt text merely mentions the service still counts as alive. A failing probe on a running container counts as alive (conservative: a wrong "dead" answer double-runs a session, a wrong "alive" one only strands a relaunch behind an explicit warn). A live agent blocks the launch with a warn and `launched=skipped-agent-alive`; no running container or no live agent means the launch proceeds - so a resume relaunches dead agents and never double-runs a live one. Plain (non-resume) opens of already-open worktrees never relaunch.

## Host-mode fallback (no devcontainer)

Repos without `.devcontainer/devcontainer.json` (e.g. the toolkit itself) launch the agent on the **host** instead of attempting a devcontainer launch - `devcontainer up` would fail with "Dev container config not found".

When the config is missing, `hr_launch`:

1. logs a clear `no devcontainer config - host-mode agent launch in pane <pane>` line,
2. skips the devcontainer chain (no `devcontainer up`, no bind-source precreate, no eviction patch, no default-agent gate),
3. types the agent start line through the same agent-bridge shim path as container launches into the worktree pane (shim `-c` payload: `cd <wt> || exit 1`, the `[herdr] agent bridge up` echo, then the agent start - e.g. `opencode --auto --prompt <task>`, `opencode --auto --continue`, interactive `claude "<prompt>"`),
4. runs the agent on the host in the worktree directory.

Devcontainer launches (eviction patch, default-agent gate, bind-source precreate) stay for repos that have the config; host-mode launches run the plain agent start. The launch guard finds no container that could hold a live agent in a config-less repo, so the host launch proceeds - and a `--resume` in host mode relaunches the dead agent into the same pane.

## Interrupted-turn recovery (opencode v2 location eviction)

opencode v2's background service interrupts every active session in a location once its idle TTL (60 minutes, no config knob in the shipped build) lapses, then logs `location services evicted` - long unattended turns (a lengthy subagent call, a turn blocked on a question form) die mid-flight. Two halves harden the workflow against it:

- **Preventive - eviction-TTL patch, automatic.** `hr_launch` runs `eviction-patch.cjs` inside the freshly provisioned container between `devcontainer up` and the agent exec, byte-patching the TTL (the precompiled string-table entry the runtime executes; the JS source text alone is inert) `60 minutes -> 9999 weeks` (unique occurrence, length-preserving) and restarting the service so the patched code hosts the session. Warn-and-continue on any drift - a version bump never blocks a launch. Opt out with `HERDR_NO_EVICT_PATCH=1`.
- **Recovery - `herdr-watchdog.sh`.** Polls open herdr worktree panes and, when an opencode turn shows as interrupted, submits a continuation prompt. Guards: linked-worktree workspaces only, never a focused workspace, never while an interactive form is open (typing would answer it), never while a turn is actively working, sentinel + per-pane cooldown against double-fire. Install once: `herdr-watchdog.sh --install-systemd` (user systemd unit, `Restart=always`); remove with `--uninstall-systemd`.

## Issue board sync

When `herdr-issue.sh` finishes an issue's worktree flow (fresh or reused - either state means "being worked on", regardless of `--no-launch`), it triages the issue on GitHub: assigns it to the gh-authenticated account (`gh issue edit <n> --add-assignee @me` - the account driving the CLI, i.e. the prompting developer) and adds it to the project if not on the board, then moves it to the in-progress status.

The board mapping is repo-specific and lives in `.herdr-workflow.json` at the target repo's root - the skill stays generic:

```json
{
  "projectOwner": "<org-or-user>",
  "projectNumber": 3,
  "statusField": "Status",
  "inProgressOption": "In progress",
  "issuePromptMention": "@<agent>"
}
```

- No config file, invalid JSON, or missing or mistyped board-sync keys: feature OFF - one info log line, behavior otherwise unchanged; unknown keys are ignored. The mention off-path is silent: an absent, mistyped, or non-string `issuePromptMention` composes the prompt unprefixed, with no log line.
- `issuePromptMention`: optional repo-specific opt-in, off by default. When set to a non-empty value, `herdr-issue.sh` prefixes the issue-mode launch prompt with the value plus a single space - the lever for pinning the pane session's agent (agent binding follows the launch environment, so `default_agent` alone may not decide it). `HERDR_ISSUE_PROMPT_MENTION` overrides the config value; an empty value counts as unset. The same unknown-keys-ignored / invalid-config leniency as the other keys applies - the feature is simply off. The prefix is opencode mention syntax: in `--agent claude` launches the same composed prompt runs under claude code, where the prefix is inert plain text (claude has no mention routing).
- Field and option names match **case-sensitively** ("In progress" and "In Progress" are different options in different projects) - the config carries the board's exact spelling.
- Best-effort and idempotent: any failure is a warn log and never fails worktree creation; re-running on an already-assigned, already-moved issue is a no-op.
- `--no-board-sync` skips the whole step (issue runs only; parsed like the other flags).

## Agent bridge

`hr_launch` runs the devcontainer chain through `~/.herdr/shim/opencode` - a copy of bash renamed so herdr's process-name agent detection sees an `opencode` in the pane's process tree (on macOS the copy is re-signed ad-hoc: macOS 26 SIGKILLs a copied Apple platform binary at exec). Before launching it calls `hr_ensure_herdr_bridge`: when `~/.herdr/bridge/herdr.sock` is missing it best-effort starts `herdr-bridge.service` (Linux) or the `dev.herdr.bridge` LaunchAgent, falling back on darwin to an unmanaged session-scoped socat spawn when neither path yields a socket, and **dies if it is still absent** - the bridge is required for launches; a silent miss would degrade herdr agent visibility with no signal at launch time. Set `HERDR_BRIDGE_OPTIONAL=1` to downgrade the failure to a warning and continue. The launch chain's first command echoes `[herdr] agent bridge up` in the pane, so the scrollback shows the bridge was verified at launch time. Claude Code launches (`--agent claude`) run the same devcontainer chain with an interactive `claude "<prompt>"` in the pane (never headless `-p`: it draws nothing and cannot be steered; plain interactive `claude` when no prompt is given; an interactive resume resolves the worktree's newest session and resumes it by id - claude leaves headless-created sessions out of `--continue` - falling back to `claude --continue`); claude detection uses the claude integration hook (`herdr integration install claude` host-side, staged by the target devcontainer) plus the same bridge, and the pane shim is renamed per agent so the process tree carries the right name.

Install the shipped unit once (Linux):

```bash
cp "${CLAUDE_SKILL_DIR}/scripts/herdr-bridge.service" ~/.config/systemd/user/ && systemctl --user daemon-reload && systemctl --user enable --now herdr-bridge
```

or the LaunchAgent (macOS - paths expand at load time; adjust the socat prefix on Intel Homebrew):

```bash
cp "${CLAUDE_SKILL_DIR}/scripts/herdr-bridge.launchd.plist" ~/Library/LaunchAgents/dev.herdr.bridge.plist && launchctl bootstrap "gui/$(id -u)" ~/Library/LaunchAgents/dev.herdr.bridge.plist
```

The bridge socket is mode 0666 so the rootless-docker uid-shifted container process can connect; that mode also exposes the herdr API (including `herdr pane run` - arbitrary command execution in the user's panes) to any other local OS user. The unit/agent assume a single-user host.

In host-mode launches (no devcontainer config, see Host-mode fallback) the same shim runs the agent directly on the host, so the bridge line and everything downstream are unchanged - only the container chain is skipped.

## Safety Rules

Adapted from herdr's own skill - `herdr --skill` output is authoritative:

- Use `--trust-repository` only after the user has verified the repository. It is not a routine retry flag.
- Never run `herdr server stop` from an active session unless the user explicitly requests it.
- Never close workspaces, tabs, or panes the user didn't ask about.
- `worktree remove --force` only with user consent (required for a dirty checkout).
- `workspace close --group` closes the primary workspace AND its linked worktree workspaces - always ask first.

## Error Handling

- **herdr exit 1** - surface the JSON stderr to the user.
- **herdr exit 2** - usage error: re-derive syntax via the runtime-knowledge protocol above and adapt.
- **devcontainer failure** - capture pane output and report: `herdr pane read <pane> --source recent-unwrapped --lines 80`.
- **no devcontainer config** - the launch auto-falls back to host mode (see Host-mode fallback); no devcontainer command is attempted and the agent starts on the host in the worktree pane.
- **agent detection**: opencode needs herdr's opencode integration artifacts staged by the target repo's devcontainer (repo-side pattern, shipped in target repos; the launch shim, see Agent bridge, covers the pane-side process tree) plus the host bridge service; claude needs herdr's claude integration hook staged by that devcontainer, which also wires claude's container-local settings to fire the hook at SessionStart, plus the same bridge. Host-side install for either kind: `herdr integration install opencode` / `herdr integration install claude`. Check `herdr agent list`: if detected, `herdr agent read`/`wait`/`prompt` work; otherwise use the pane surface (`pane read`, `pane wait-output`).

## References

- **`references/workflows.md`** - manual fallback steps for every workflow with exact herdr/git/gh commands (what the scripts do, for hand-rolling when a script fails), verified JSON shapes captured from herdr 0.9.2, a troubleshooting matrix, and the grouping/tab model in depth. Load it when a script fails or herdr semantics are unclear.
