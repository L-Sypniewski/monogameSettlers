#!/usr/bin/env bash
# usage: herdr-restore.sh --repo DIR [--snapshot FILE] [--agent opencode|claude] [--no-launch]
#   Rebuild a closed repo group (primary + linked worktree workspaces) from a herdr session snapshot.
#   --repo DIR        repository whose group to restore
#   --snapshot FILE   snapshot to read (default: newest one that still contains the repo's linked worktrees)
#   --no-launch       recreate the workspaces only; start no agents
#   --agent NAME      agent runtime for launches (opencode|claude, default opencode)
#   Each worktree is resumed through herdr-open.sh's liveness guard: a worktree whose agent still
#   runs inside its devcontainer is skipped (launched=skipped-agent-alive), never double-run; stop
#   the container to force a resume.

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

usage() {
  cat <<'USAGE'
usage: herdr-restore.sh --repo DIR [--snapshot FILE] [--agent opencode|claude] [--no-launch]
USAGE
}

REPO_DIR=""
SNAPSHOT=""
NO_LAUNCH=0
AGENT="opencode"

while (( $# > 0 )); do
  case "$1" in
    --repo)
      (( $# >= 2 )) || hr_die "--repo requires a value"
      REPO_DIR=$2; shift 2 ;;
    --repo=*)
      REPO_DIR=${1#--repo=}; shift ;;
    --snapshot)
      (( $# >= 2 )) || hr_die "--snapshot requires a value"
      SNAPSHOT=$2; shift 2 ;;
    --snapshot=*)
      SNAPSHOT=${1#--snapshot=}; shift ;;
    --no-launch) NO_LAUNCH=1; shift ;;
    --agent)
      (( $# >= 2 )) || hr_die "--agent requires a value"
      AGENT=$2; shift 2 ;;
    --agent=*)
      AGENT=${1#--agent=}; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) usage >&2; hr_die "unknown option: $1" ;;
    *) usage >&2; hr_die "unexpected argument: $1" ;;
  esac
done

[[ -n "$REPO_DIR" ]] || { usage >&2; hr_die "--repo is required"; }
if [[ "$AGENT" != "opencode" && "$AGENT" != "claude" ]]; then
  hr_die "--agent must be opencode or claude"
fi

if (( NO_LAUNCH )); then hr_require_cmds; else hr_require_launch_cmds "$REPO_DIR"; fi

repo_root=$(realpath "$(hr_repo_root "$REPO_DIR")")

# path<TAB>label per linked worktree of the repo; the path comes first so an empty label cannot shift fields
worktrees_in() {
  jq -r --arg root "$repo_root" '
    .workspaces[]?
    | select(.worktree_space != null and .worktree_space.repo_root == $root and .worktree_space.is_linked_worktree == true)
    | [.worktree_space.checkout_path, (.custom_name // "")] | @tsv' "$1"
}

if [[ -z "$SNAPSHOT" ]]; then
  snap_dir="${HERDR_CONFIG_DIR:-$HOME/.config/herdr}/session-snapshots"
  [[ -d "$snap_dir" ]] || hr_die "no snapshot directory: $snap_dir"
  while IFS= read -r f; do
    if [[ -n "$(worktrees_in "$f" 2>/dev/null)" ]]; then SNAPSHOT=$f; break; fi
  done < <(ls -t "$snap_dir"/*.json 2>/dev/null)
  [[ -n "$SNAPSHOT" ]] || hr_die "no snapshot in $snap_dir contains linked worktrees of $repo_root"
fi
[[ -f "$SNAPSHOT" ]] || hr_die "--snapshot: not a file: $SNAPSHOT"

rows=$(worktrees_in "$SNAPSHOT")
[[ -n "$rows" ]] || hr_die "snapshot $SNAPSHOT has no linked worktrees of $repo_root"

hr_info "restoring from $SNAPSHOT"
hr_info "worktrees whose agent still runs inside their devcontainer are skipped, never double-run (stop the container to force a resume)"

restored=0
skipped=0
agent_alive=0
while IFS=$'\t' read -r path label; do
  if [[ ! -d "$path" ]]; then
    hr_warn "worktree no longer on disk, skipping: $path"
    skipped=$((skipped + 1))
    continue
  fi
  args=(--path "$path" --resume --no-focus --agent "$AGENT")
  if [[ -n "$label" ]]; then args+=(--label "$label"); fi
  if (( NO_LAUNCH )); then args+=(--no-launch); fi
  if out=$("$SCRIPT_DIR/herdr-open.sh" "${args[@]}" 2>&1); then
    printf '%s\n' "$out"
  else
    hr_warn "restore of $path failed - continuing with remaining worktrees"
    printf '%s\n' "$out"
    skipped=$((skipped + 1))
    continue
  fi
  if grep -q '^launched=skipped-agent-alive$' <<<"$out"; then
    agent_alive=$((agent_alive + 1))
  else
    restored=$((restored + 1))
  fi
done <<< "$rows"

echo "summary:"
printf 'snapshot=%s\n' "$SNAPSHOT"
printf 'restored=%s\n' "$restored"
printf 'skipped=%s\n' "$skipped"
printf 'agent=%s\n' "$AGENT"
printf 'agent_alive=%s\n' "$agent_alive"
