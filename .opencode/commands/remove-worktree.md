---
description: Tear down the CURRENT worktree safely: devcontainer container + per-worktree volumes, herdr/git worktree removal, branch deletion only on explicit confirmation
---

Tear down the CURRENT git worktree completely: its devcontainer
(container + per-worktree docker volumes), then the worktree itself.
Branch deletion is a separate, explicitly confirmed step. Sequence is
DETECT, GATE, REPORT+CONFIRM, DESTROY: this session usually runs
inside the tree being removed and dies with it, so everything
verifiable is done and printed BEFORE any destructive step. Never
force anything the user did not explicitly confirm; when a step is
skipped or impossible, say so plainly.

## 0. Context gate: where is this session running?

If the cwd is `/workspace` or `/.dockerenv` exists, this session runs
INSIDE the devcontainer. Host docker is unreachable from here (herdr
relays only its API socket, not docker), and the docker CLI inside
answers from the INNER docker-in-docker daemon: ALL docker output
obtained in this session is inner-daemon noise - disqualified, never
used, and never reported as "no container found". `/workspace` is a
standalone host-seeded clone, not the host worktree: its git dir
equals its git common dir (a spurious step 1.2 "main checkout" REFUSE),
and its basename yields a wrong volume name. So do NOT run step 1 here
(1.1-1.3 resolve host-wrong values, 1.4 can never match a host
checkout_path from `/workspace`, 1.5-1.6 see only the inner DinD
daemon). Resolve ONLY what is genuinely resolvable inside: the branch
(`git branch --show-current`) and the clone's git state (dirty files,
unpushed commits), for the report. Then print a HOST-RESOLUTION
TEMPLATE - the step 4 procedure with explicit placeholders, each with
the exact host command that fills it:

- `<HOST WORKTREE PATH>`: host `herdr workspace list` (this pane's
  workspace) or `git worktree list` from the main checkout
- `<MAIN CHECKOUT PATH>`: the first entry of that same `git worktree
list` output (the main checkout), or the step 1.2 git-common-dir
  derivation run host-side - step 4.4's fallback and the shared prune
  line consume it
- `<CONTAINER IDS>`: host `docker ps -aq --filter
"label=devcontainer.local_folder=<HOST WORKTREE PATH>"`
- `<VOLUME NAMES>`: host `docker inspect <CONTAINER IDS>`
  `.Mounts[].Name`, plus the expected
  `monogameSettlers-$(basename <HOST WORKTREE PATH>)`
- `<HERDR WORKSPACE ID>`: host `herdr workspace list | jq` (step 1.4)
- `<MERGED-PR STATE>`: host `gh pr list --state merged --head
<branch> --json number,url`

Tell the user to fill the placeholders on the host and run the
procedure from a host terminal or a host-side session. Nothing has
been removed at this point; never claim teardown success from inside.
Stop there.

## 1. Detect (read-only)

1. `wt="$(git rev-parse --show-toplevel)"` and
   `branch="$(git branch --show-current)"` (if branch is empty: detached
   HEAD, note it and skip step 5).
2. Main checkout: `git rev-parse --git-dir` vs `git rev-parse
--git-common-dir`. Equal values mean this IS the main checkout: REFUSE
   and point at `bash scripts/prune-merged-work.sh` for merged-branch
   sweeps. Otherwise `main="$(dirname "$(git rev-parse
--git-common-dir)")"` (already absolute from a linked worktree; if it
   prints a relative `.git`, absolutize against the cwd).
3. Expected workspace volume: `vol="monogameSettlers-$(basename "$wt")"`.
4. herdr (if `command -v herdr` succeeds): workspace owning this worktree
   by exact path match: `herdr workspace list | jq -r --arg p "$wt"
'[.result.workspaces[] | select(.worktree != null and
.worktree.checkout_path == $p and .worktree.is_linked_worktree ==
true) | .workspace_id][0] // ""'` - the `[0] // ""` head guard keeps a
   stale + live duplicate pair from printing two ids at a destructive
   step. Empty output = not open in herdr. If a check without the
   guard ever shows more than one id, stop and ask which workspace to
   remove.
5. Containers (host docker): `docker ps -aq --filter
"label=devcontainer.local_folder=$wt"` (the devcontainer CLI stamps
   this label with the resolved workspace folder; pass `$wt` verbatim, no
   realpath). No output = no container (OK, idempotent).
6. Volume names + container image (read-only; step 3's report lists
   them, so harvest at detection, not after confirmation): for each
   container id from step 1.5, `docker inspect <id>` and collect
   `.Mounts[].Name` (non-empty names) plus the container's image
   (`.Config.Image`, feeds gate 2.3's probe). Drop deny-listed names (gate 2.2). Any remaining name that
   is neither the expected `monogameSettlers-$(basename "$wt")` nor `dind-*`
   prefixed is flagged here for explicit approval in step 3.
7. Merged-PR state for step 5: `gh pr list --state merged --head
"$branch" --json number,url`. An array with entries = a merged PR
   exists; `[]` = none (gh always prints a JSON array, so a literal
   "non-empty" reading is always true; this repo squash/rebase-merges,
   so `git branch --merged` lies). gh failure = unknown; treat as not
   merged and never fall back to `--merged`.

## 2. Safety gates (refuse with evidence unless the user confirms)

1. Dirty tree: `git -C "$wt" status --porcelain` non-empty (untracked
   files count as dirty). Default = stop and print the manual review
   commands; only if the user explicitly confirms deletion anyway, pass
   `--force` in step 4.4 and say so.
2. Volume gate: the workspace volume must be exactly
   `monogameSettlers-$(basename "$wt")`. HARD DENY-LIST, never remove even if
   harvested from the container: `mise-data-volume`,
   `playwright-browsers-volume`, `nuget-cache-volume` (shared host-global
   volumes mounted into every worktree).
3. Volume-side dirt: the volume clone is permanently divergent from
   the host tree (host edits never reach it), so gate 2.1 says nothing
   about it. When step 1.5 found a container, probe the expected volume
   `$vol` read-only BEFORE any destruction: `docker run --rm --quiet
-v "$vol":/workspace --entrypoint /usr/bin/git <image> -C
/workspace -c safe.directory=/workspace status --porcelain` -
   image = the container's own image harvested in step 1.6, or
   `mcr.microsoft.com/devcontainers/base:ubuntu-24.04`; the helper
   container form works for stopped containers too, where `docker
exec` cannot; `safe.directory` is required because the helper's
   mount uid differs from the clone's owner. Non-empty output =
   volume-side dirt: surface it in the step 3 report and require an
   explicit "delete anyway" - same override semantics as gate 2.1.
   Probe failure = report "volume state unknown" and require the same
   explicit confirm. No container found = skip the probe (a stale
   volume may still hold frozen dirt) and say plainly in step 3 that
   its content is destroyed unverified.

## 3. Report + confirm (last guaranteed output moment)

Print: worktree path, branch, main checkout path, herdr workspace id (or
"not open in herdr"), container ids found, volume names to remove (the
step 1.6 set PLUS the expected volume `monogameSettlers-$(basename "$wt")`,
which step 4.3 removes even when no container exists; the set =
expected + `dind-*` extras + any flagged odd names, which need
explicit approval HERE), merged-PR state (PR numbers/urls, none, or
gh-failed/unknown), dirty state (host tree per gate 2.1 AND volume
tree per gate 2.3, or "volume state unknown"), and the exact commands
step 4 will run. Also print
NOW the branch-deletion commands from step 5 (the pane may die in step
4). Ask the user to confirm the teardown explicitly (yes/no). No = stop.

## 4. Destroy (idempotent; "not found" outcomes are OK, say so)

Order matters; the volume set was harvested read-only in step 1.6,
BEFORE any container removal.

1. Volume set: the names harvested in step 1.6 (deny-list already
   dropped there). A name that is neither the expected
   `monogameSettlers-$(basename "$wt")` nor `dind-*` prefixed is removed only
   if the user approved it in step 3; otherwise ask now and skip on
   refusal.
2. `docker rm -f <ids>` (skip if none).
3. `docker volume rm` the approved name set PLUS the expected volume
   (a stale volume with no container is still removed; "no such volume"
   is OK).
4. Remove the worktree, from the main checkout context only (never from
   inside the tree being removed):
   - herdr path: `herdr worktree remove --workspace <id>` (add `--force`
     only per gate 2.1; herdr never deletes branches).
   - fallback: `git -C "$main" worktree remove "$wt"` (same `--force`
     rule).
     Either path: `git -C "$main" worktree prune`.
5. Warn: this pane/session likely just lost its tree; the user should
   close it.

## 5. Branch deletion (OFF unless explicitly confirmed per invocation)

Run only if this session survived step 4 AND the user separately
confirms local deletion, then remote deletion (branches are pushed at
creation, so origin usually has this branch). If the session died with
the worktree, the commands printed in step 3 are the handoff.

1. Gate on step 1.7: merged PR exists = safe to delete; no merged PR or
   gh failed = say so plainly and require an explicit "delete anyway".
2. Local: `git -C "$main" branch -d "$branch"`; if `-d` refuses (squash
   merges never satisfy `-d`), use `branch -D` when gh said merged OR
   the user gave 5.1's explicit "delete anyway" - never otherwise.
3. Remote (separate confirmation): `git -C "$main" push origin --delete
"$branch"`.
