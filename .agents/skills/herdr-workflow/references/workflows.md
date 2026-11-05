# herdr Workflow - Detailed Reference

Loaded on demand (SKILL.md stays lean). Contains: the grouping/tab model, verified JSON shapes, manual fallback steps for every workflow (what the bundled scripts do, so you can hand-roll when a script fails), and a troubleshooting matrix.

All herdr commands below were verified against **herdr 0.9.2**. The installed binary remains the authority - when in doubt, re-run `herdr --skill` and the relevant group help (`herdr workspace`, `herdr worktree`, `herdr pane`, ...). Running a group alone prints usage; never run bare `herdr`; never probe a mutating command without its required args.

## The grouping / tab model

- herdr organizes terminals as **workspaces → tabs → panes**. The sidebar groups workspaces; `herdr worktree create --workspace <primary>` creates a linked git worktree **and** a new workspace that herdr auto-groups under that primary.
- **Worktree = workspace, not a tab.** Each worktree workspace owns its own tabs and panes inside it. Creation returns a `root_pane` - the shell pane sitting at the worktree checkout, where the devcontainer launch (when the repo has a config) and `opencode` are launched.
- **Primary workspace** = the repo's main checkout. Deterministic label = repo name; cwd = canonical main checkout. It anchors the group.
- **Worktree workspaces** are checked out at `~/.herdr/worktrees/<repo>/<branch-slug>`. Label: `#<n> <title-slug>` for issues, or custom `--label`.
- `herdr worktree list --workspace <primary>` includes the **main checkout entry** (`is_linked_worktree: false`) alongside linked worktrees (`true`). Do not treat the main-checkout entry as a worktree to remove.
- Closing a primary workspace that has open linked worktree workspaces fails with `workspace_group_close_required` - `workspace close --group` is the group-wide close (see Safety in SKILL.md).

## Verified JSON shapes (herdr 0.9.2)

Additional fields may be present (e.g. `number`, `active_tab_id` in workspace entries) - rely only on the keys listed here.

### `herdr workspace list` → `.result.workspaces[]`

```json
{
  "workspace_id": "w3",
  "label": "<repo>",
  "focused": false,
  "agent_status": "unknown",
  "tab_count": 1,
  "pane_count": 1,
  "worktree": {
    "checkout_path": "/home/<user>/.herdr/worktrees/<repo>/issue-123-fix-login-timeout",
    "is_linked_worktree": true,
    "repo_key": "…",
    "repo_name": "<repo>",
    "repo_root": "/home/<user>/Documents/Developer/<repo>"
  }
}
```

- `worktree` is present only for git-backed workspaces; non-git workspaces omit it entirely.
- `repo_root` = canonical main checkout. The **primary** workspace has `checkout_path == repo_root` and `is_linked_worktree: false`; linked worktree workspaces have their own `checkout_path` under `~/.herdr/worktrees/` and `is_linked_worktree: true`.

### `herdr worktree create` → `.result`

- `.result.workspace.workspace_id` - the **new worktree workspace** id.
- `.result.root_pane.pane_id` - the shell pane at the worktree checkout (launch target).
- `.result.worktree`:

```json
{
  "branch": "issue-123-fix-login-timeout",
  "path": "/home/<user>/.herdr/worktrees/<repo>/issue-123-fix-login-timeout",
  "open_workspace_id": "w5",
  "label": "#123 fix-login-timeout"
}
```

### `herdr worktree list --workspace <id>` → `.result.worktrees[]`

```json
{
  "branch": "issue-123-fix-login-timeout",
  "path": "/home/<user>/.herdr/worktrees/<repo>/issue-123-fix-login-timeout",
  "open_workspace_id": "w5",
  "is_linked_worktree": true,
  "is_detached": false,
  "is_prunable": false,
  "label": "#123 fix-login-timeout"
}
```

- Includes the **main checkout entry** (path = repo root, `is_linked_worktree: false`).
- `open_workspace_id` is present only while the worktree has an open workspace in this session; absent otherwise.

## Manual fallback - issue → worktree → launch

What `herdr-issue.sh <n>` does, step by step. Run each step yourself if the script fails.

```bash
REPO_ROOT="$(git -C "${REPO_DIR:-.}" rev-parse --show-toplevel)"   # resolve main checkout
REPO_NAME="$(basename "$REPO_ROOT")"
```

1. **Fetch the issue:**

```bash
gh issue view <n> --repo <slug> --json number,title,url
BRANCH="issue-<n>-<title-slug>"        # kebab-case slug of the title
LABEL="#<n> <title-slug>"
```

2. **Resolve the mention prefix and build the launch prompt:**

```bash
MENTION="${HERDR_ISSUE_PROMPT_MENTION:-}"
if [[ -z "$MENTION" ]]; then
  # config problems mean an empty MENTION, never a failed step
  MENTION="$(jq -ser '.[0] | select(.issuePromptMention | type == "string" and length > 0) | .issuePromptMention' \
    "$REPO_ROOT/.herdr-workflow.json" 2>/dev/null || true)"
fi
# e.g. @ship - pins the pane session's agent via opencode mention syntax
PROMPT="${MENTION:+$MENTION }Work on issue #<n>: <title> - <url>"
```

3. **Resolve base** - latest `origin/<default-branch>`, never current HEAD:

```bash
git -C "$REPO_ROOT" fetch origin
DEFAULT_BRANCH="$(git -C "$REPO_ROOT" symbolic-ref --short refs/remotes/origin/HEAD | sed 's|^origin/||')"
```

4. **Find the primary workspace** (label = repo name, main checkout, not a linked worktree); create it if missing:

```bash
herdr workspace list | jq -r --arg root "$REPO_ROOT" '
  .result.workspaces[]
  | select((.worktree.repo_root? // "") == $root and ((.worktree.is_linked_worktree // false) | not))
  | .workspace_id' | head -n1
# empty → create:
herdr workspace create --cwd "$REPO_ROOT" --label "$REPO_NAME"
# → .result.workspace.workspace_id, .result.tab.tab_id, .result.root_pane.pane_id
```

5. **Find or create the worktree:**

```bash
herdr worktree list --workspace "$PRIMARY_WS" | jq -r --arg branch "$BRANCH" \
  '.result.worktrees[] | select(.branch == $branch and .is_linked_worktree)'
```

- Hit with `open_workspace_id` → reuse: focus that workspace, **never re-launch** (a pane may host a running opencode).
- Hit without `open_workspace_id` → `herdr worktree open --workspace "$PRIMARY_WS" --branch "$BRANCH" --label "$LABEL"` (parse the same fields as create).
- Miss → create:

```bash
herdr worktree create --workspace "$PRIMARY_WS" --branch "$BRANCH" \
  --base "origin/$DEFAULT_BRANCH" --label "$LABEL"
# → WT_WS=.result.workspace.workspace_id, ROOT_PANE=.result.root_pane.pane_id,
#   WT_PATH=.result.worktree.path
```

6. **Tracking guard** (basing on `origin/<default>` mis-sets upstream via `autoSetupMerge`):

```bash
git -C "$WT_PATH" branch --unset-upstream 2>/dev/null || true
```

7. **Push - before any devcontainer command.** Devcontainers seed a docker volume by cloning the branch from origin; the branch MUST be on origin first. Never force:

```bash
git -C "$WT_PATH" push -u origin "$BRANCH"
```

8. **Launch in the worktree workspace's root pane:**

```bash
SHIM="$HOME/.herdr/shim/opencode"   # bash renamed so herdr's process-name detection fires
mkdir -p "$(dirname "$SHIM")" && cp -f /bin/bash "$SHIM"
# precreate missing worktree-relative bind-source dirs first: Docker --mount type=bind
# errors on a missing source instead of auto-creating it (e.g. a gitignored session-db dir);
# script runs do this via hr_precreate_bind_dirs in scripts/lib.sh - skip .devcontainer/**
# (initializeCommand-staged) and dot-suffixed basenames (file binds)
grep -o '"source=${localWorkspaceFolder}/[^",]*' "$WT_PATH/.devcontainer/devcontainer.json" \
  | sed 's/^"source=\${localWorkspaceFolder}\///' | grep -v '^\.devcontainer/' \
  | while IFS= read -r rel; do case "${rel##*/}" in ?*.*) ;; *) [[ -e "$WT_PATH/$rel" ]] || mkdir -p "$WT_PATH/$rel" ;; esac; done
# trailing echo: bash exec-optimizes the final -c command, which would replace the shim
# and erase herdr's process-name agent evidence
herdr pane run "$ROOT_PANE" "$SHIM -c 'devcontainer up --workspace-folder . && devcontainer exec --default-user-env-probe none --remote-env PATH=/home/vscode/.opencode/bin:/home/vscode/.bun/bin:/mnt/mise-data/shims:/usr/local/share/nvm/current/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/home/vscode/.dotnet:/home/vscode/.dotnet/tools:/home/vscode/.local/bin --workspace-folder . opencode --auto --prompt \"$PROMPT\" && echo \"[herdr] agent chain exited\"'"
```

Never pass `--model` - repos preselect models in their `opencode.jsonc`.

When `$WT_PATH/.devcontainer/devcontainer.json` is absent, skip the devcontainer chain and run the host-mode launch instead (no `devcontainer up` - it would fail with "Dev container config not found"):

```bash
herdr pane run "$ROOT_PANE" "$SHIM -c 'bash /tmp/herdr-host-launch; rc=$?; echo \"[herdr] launch exited \$rc\"; rm -f /tmp/herdr-host-launch'"
# /tmp/herdr-host-launch content (cd + bridge echo + agent start, run on the host):
#   cd <wt> || exit 1
#   echo "[herdr] agent bridge up"
#   opencode --auto --prompt "<task prompt>"
```

9. **Verify:** `herdr pane wait-output "$ROOT_PANE" --match "..." --timeout 60000` for a devcontainer/opencode marker; then `herdr agent list` to see whether opencode was detected as an agent (see Troubleshooting).

10. **Focus** (unless `--no-focus`): `herdr workspace focus "$WT_WS"`.

## Manual fallback - issue board sync

What `hr_board_sync` (lib.sh) runs after the worktree flow succeeds, on issues only. Driven by `.herdr-workflow.json` at the repo root (`projectOwner`, `projectNumber`, `statusField`, `inProgressOption`); a missing file, invalid JSON, or missing or mistyped keys turn the whole step off with a single info line. Best-effort throughout - a failure logs a warn and the flow continues - and idempotent.

1. **Assign the issue to the gh-authenticated account:**

```bash
gh issue edit <n> --add-assignee @me
```

2. **Resolve the project's internal id** - `item-edit` speaks ids, not names:

```bash
PROJECT_ID="$(gh project view <number> --owner <owner> --format json | jq -r .id)"
```

3. **Resolve the status field and option ids.** Option names match case-sensitively - the config's spelling is exact:

```bash
gh project field-list <number> --owner <owner> --format json --limit 100 | jq -r \
  --arg f "$statusField" --arg o "$inProgressOption" '
  [.fields[]? | select(.name == $f)][0]
  | if . == null or .options == null then empty
    else [.id, ([.options[]? | select(.name == $o) | .id][0] // "")] | @tsv end'
```

4. **Find the issue's item by URL** (issue numbers repeat across repos in one project; draft items carry no `content`), then add it only if absent - item-list pages internally, so when the first-page cap misses the issue the script re-lists at `.totalCount`, and if item-add reports the content already exists it re-lists (totalCount-aware) to find the item instead of warning:

```bash
gh project item-list <number> --owner <owner> --format json --limit 500   # match .content.url
gh project item-add <number> --owner <owner> --url <issue-url> --format json
```

5. **Move the item to the in-progress option:**

```bash
gh project item-edit --id <item-id> --field-id <field-id> --project-id <project-id> \
  --single-select-option-id <option-id>
```

## Manual fallback - free-text task

Same pipeline as above, skipping the `gh` step: the agent derives a short kebab-case branch slug (no `issue-<n>-` prefix), and the prompt passed to `--prompt` / opencode is the task description **verbatim**.

## Manual fallback - open existing

What `herdr-open.sh (--branch B | --path P)` does:

1. Resolve repo + primary workspace as above.
2. `herdr worktree list --workspace "$PRIMARY_WS"` - match on `.branch == B` or `.path == P`.
3. If `open_workspace_id` present → idempotent reuse: `herdr workspace focus <open_workspace_id>`; a plain open never relaunches. Only a `--resume` open relaunches, resolving the workspace's first pane (`herdr pane list --workspace <open_workspace_id>`, first `pane_id`) and typing into it only when the launch guard (SKILL.md, Determinism & Launch Semantics) sees no live agent in the worktree's devcontainer - a live agent reports `launched=skipped-agent-alive` and types nothing (sending text into a pane hosting a running opencode corrupts the session).
4. If not open → `herdr worktree open --workspace "$PRIMARY_WS" (--path P | --branch B) [--label TEXT]`, then optionally launch (step 8 above) if the root pane is an idle shell and launch was requested.

## Manual fallback - restore a closed group

What `herdr-restore.sh --repo DIR` does:

1. Pick the newest `~/.config/herdr/session-snapshots/*.json` containing linked worktrees of the repo (`.workspaces[].worktree_space.repo_root`, `is_linked_worktree == true`).
2. For each such worktree still on disk: `herdr-open.sh --path <checkout_path> --label <custom_name> --resume --no-focus [--agent opencode|claude]` - recreates the primary workspace if absent, opens the worktree, launches through the shim with `opencode --auto --continue` (`claude --resume <session-id>` - claude leaves headless-created sessions out of `--continue`, so the launcher resolves the worktree's newest session id and falls back to `claude --continue`). The launch guard never double-runs: a worktree whose agent still lives in its devcontainer reports `launched=skipped-agent-alive` (nothing typed) and counts as `agent_alive=` instead of `restored=` - stop the stale agent (e.g. `docker exec <cid> pkill -f opencode`) and re-run to take it over. `restored=` counts only worktrees whose agent launched; `skipped=` counts failures (missing on disk, herdr-open.sh error).

Never restore with bare `herdr workspace create` / `worktree open`: the pane then has no `opencode` process in its tree and herdr discards the agent's hook reports (agent shows as unknown). Snapshots hold workspaces, labels and cwds only - agents, extra tabs and layouts are not restored.

## Manual fallback - cleanup

What `herdr-cleanup.sh` does. **Confirmation-gated: always show the plan and get explicit consent for destructive parts.**

1. Identify the target: `herdr workspace list` → match `worktree.checkout_path` (or `--workspace ID` directly); `herdr worktree list --workspace "$PRIMARY_WS"` for branch/path cross-check.
2. Dirty check: `git -C "$WT_PATH" status --porcelain` - if non-empty, `--force` requires explicit user consent.
3. Remove the worktree + its workspace (never deletes branches by itself):

```bash
herdr worktree remove --workspace "$WT_WS" [--force]
git -C "$REPO_ROOT" worktree prune
```

4. Branch deletion - separate, explicitly confirmed steps only:

```bash
git -C "$REPO_ROOT" branch -D "$BRANCH"            # local (--delete-branch)
git -C "$REPO_ROOT" push origin --delete "$BRANCH"  # remote (--delete-remote-branch)
```

5. Leftover docker volumes - devcontainer volumes survive worktree removal:

```bash
docker volume ls | grep "$(basename "$WT_PATH")"
```

Remove volumes only with explicit consent (they may hold build caches worth keeping).

## Manual fallback - status

What `herdr-status.sh` composes:

```bash
herdr workspace list                       # all workspaces, worktree metadata, agent_status
herdr worktree list --workspace <primary>  # per repo: branches, paths, open workspaces
herdr agent list                           # recognized agents and their states
```

Optional manual supplement (the script does not run git): `git -C "$REPO_ROOT" worktree list` for the git-side view incl. prunable entries.

## Troubleshooting matrix

| Symptom                                                                 | Likely cause                                                                                                                                                                                                                                                                                                                                                                                         | Resolution                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ----------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| herdr commands fail / socket not found                                  | server not running, or session runs in a devcontainer without the herdr socket                                                                                                                                                                                                                                                                                                                       | `herdr status`; if this session is inside a devcontainer without the socket, stop and explain                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Commands target the wrong workspaces                                    | `HERDR_SESSION` / `--session` mismatch between shell and script                                                                                                                                                                                                                                                                                                                                      | compare `herdr status` output; use one consistent session context                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `detected dubious ownership` / git trust error from worktree commands   | git `safe.directory` protection on `~/.herdr/worktrees/...`                                                                                                                                                                                                                                                                                                                                          | ask the user to verify the repository, then retry once with `--trust-repository` - it is not a routine retry flag                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `workspace_group_close_required` on close                               | primary workspace has open linked worktree workspaces                                                                                                                                                                                                                                                                                                                                                | `workspace close --group` closes the primary AND all linked worktree workspaces - confirm scope with the user before using it                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `worktree remove` refuses                                               | dirty checkout (uncommitted changes)                                                                                                                                                                                                                                                                                                                                                                 | show `git -C <path> status --porcelain`, get explicit consent, retry with `--force`                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `git push -u origin <branch>` rejected                                  | remote branch already has commits (pre-existing branch)                                                                                                                                                                                                                                                                                                                                              | never force-push; show the divergence (`git fetch` + `git log`) and ask the user                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `devcontainer up` fails                                                 | build/config error in `.devcontainer/devcontainer.json`, or a missing worktree bind-source dir (Docker `--mount type=bind` errors on missing sources instead of auto-creating, e.g. a gitignored session-db dir)                                                                                                                                                                                     | `herdr pane read <pane> --source recent-unwrapped --lines 80`; for a missing bind-source dir, create it on the host (script runs precreate these via `hr_precreate_bind_dirs`; manual fallback step 8 includes the one-liner) and retry                                                                                                                                                                                                                                                                                                                          |
| Launch fails with "Dev container config not found"                | worktree has no `.devcontainer/devcontainer.json` - the launch should host-mode instead of attempting `devcontainer up`                                                                                                                                                                                                                                                                                                                             | scripts auto-fallback (SKILL.md, Host-mode fallback): log `no devcontainer config - host-mode agent launch in pane <pane>`, skip the devcontainer chain, run the agent via the shim on the host in the worktree pane; manual fallback step 8 host branch applies
| Agent in a pane shows as unknown / `agent: null` although opencode runs | pane was not launched through the shim (restored or re-attached by hand), so herdr drops the hook reports                                                                                                                                                                                                                                                                                            | relaunch through `herdr-open.sh --resume` once the current turn finishes - the launch guard skips the relaunch while the agent still runs, so stop it first (its container: `docker ps -q --filter "label=devcontainer.local_folder=$WT_PATH"`, then `docker exec <cid> pkill -f opencode`); `herdr pane process-info --pane <id>` should list a foreground process named `opencode`                                                                                                                                                                             |
| Leftover docker volumes after cleanup                                   | devcontainer named volumes survive worktree removal                                                                                                                                                                                                                                                                                                                                                  | `docker volume ls \| grep <worktree-basename>`; remove only with explicit consent                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| launch aborts: `herdr bridge socket unavailable`                        | bridge socket still absent after the best-effort `systemctl --user start herdr-bridge.service` - the bridge is required for launches                                                                                                                                                                                                                                                                 | install the shipped unit: `cp "${CLAUDE_SKILL_DIR}/scripts/herdr-bridge.service" ~/.config/systemd/user/ && systemctl --user daemon-reload && systemctl --user enable --now herdr-bridge`, or start it: `systemctl --user start herdr-bridge`; `HERDR_BRIDGE_OPTIONAL=1` downgrades the failure to a warning                                                                                                                                                                                                                                                     |
| opencode or claude missing from `herdr agent list`                      | one of three: host bridge down (`~/.herdr/bridge/herdr.sock` absent - required at launch; the pane's `[herdr] agent bridge up` line confirms it was up when the chain started); target repo not staging herdr's integration for that agent kind in its devcontainer (opencode: plugin files + `remoteEnv`; claude: hook + SessionStart settings wiring); herdr integration absent from the container | the shim (launcher-side) handles process-tree detection and the bridge handles the socket - check `systemctl --user status herdr-bridge` (unit ships as `scripts/herdr-bridge.service`) and the repo's devcontainer staging; otherwise use the pane surface: `herdr pane read`, `herdr pane wait-output`; if the agent IS detected, `herdr agent read`/`wait`/`prompt` work normally; for claude, registration happens at SessionStart, so a session already running before the wiring landed stays invisible until its next session start (restart or `/clear`) |
