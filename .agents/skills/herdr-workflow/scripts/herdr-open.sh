#!/usr/bin/env bash
# usage: herdr-open.sh (--branch B | --path P) [--repo DIR] [--prompt TEXT] [--label TEXT] [--base REF] [--resume] [--no-launch] [--focus] [--agent opencode|claude]
#   --branch B          open a worktree for branch B (created from --base if new)
#   --path P            open an existing git worktree directory in herdr
#   --repo DIR          repository to operate on (default: cwd; ignored with --path)
#   --prompt TEXT       prompt passed to opencode --auto
#   --label TEXT        herdr workspace label (default: branch name)
#   --base REF          start point when --branch names a branch that does not exist yet
#   --resume            continue the agent's last session in the worktree (opencode --auto --continue);
#                      relaunches a dead agent into the existing pane, never double-runs a live one
#   --no-launch         prepare worktree + push, but do not start devcontainer/agent
#   --focus             focus the worktree workspace (default: background - the workspace
#                       is created and the launch typed without switching your view, so a
#                       focused pane you are typing in is never disturbed)
#   --no-focus          accepted as a no-op (background is the default)
#   --agent NAME        agent runtime for launches (opencode|claude, default opencode)

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

usage() {
  cat <<'EOF'
usage: herdr-open.sh (--branch B | --path P) [--repo DIR] [--prompt TEXT] [--label TEXT] [--base REF] [--resume] [--no-launch] [--focus] [--agent opencode|claude]
EOF
}

REPO_DIR=$PWD
BRANCH=""
PATH_ARG=""
PROMPT=""
LABEL=""
BASE=""
NO_LAUNCH=0
FOCUS=0
RESUME=0
AGENT="opencode"

while (( $# > 0 )); do
  case "$1" in
    --repo)
      (( $# >= 2 )) || hr_die "--repo requires a value"
      REPO_DIR=$2; shift 2 ;;
    --repo=*)
      REPO_DIR=${1#--repo=}; shift ;;
    --branch)
      (( $# >= 2 )) || hr_die "--branch requires a value"
      BRANCH=$2; shift 2 ;;
    --branch=*)
      BRANCH=${1#--branch=}; shift ;;
    --path)
      (( $# >= 2 )) || hr_die "--path requires a value"
      PATH_ARG=$2; shift 2 ;;
    --path=*)
      PATH_ARG=${1#--path=}; shift ;;
    --prompt)
      (( $# >= 2 )) || hr_die "--prompt requires a value"
      PROMPT=$2; shift 2 ;;
    --label)
      (( $# >= 2 )) || hr_die "--label requires a value"
      LABEL=$2; shift 2 ;;
    --base)
      (( $# >= 2 )) || hr_die "--base requires a value"
      BASE=$2; shift 2 ;;
    --resume) RESUME=1; shift ;;
    --no-launch) NO_LAUNCH=1; shift ;;
    --focus) FOCUS=1; shift ;;
    --no-focus) FOCUS=0; shift ;;  # compat no-op: background is the default
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

if [[ -n "$BRANCH" && -n "$PATH_ARG" ]]; then
  hr_die "--branch and --path are mutually exclusive"
fi
if [[ -z "$BRANCH" && -z "$PATH_ARG" ]]; then
  usage >&2
  hr_die "exactly one of --branch or --path is required"
fi
if [[ "$AGENT" != "opencode" && "$AGENT" != "claude" ]]; then
  hr_die "--agent must be opencode or claude"
fi

# Launch deps: devcontainer + docker are only required when the target repo has a devcontainer
# config; repos without one launch the agent on the host and skip both.
REQUIRE_CTX="$REPO_DIR"
if [[ -n "$PATH_ARG" ]]; then REQUIRE_CTX="$PATH_ARG"; fi
if (( NO_LAUNCH )); then hr_require_cmds; else hr_require_launch_cmds "$REQUIRE_CTX"; fi

main_root_of_worktree() {
  local wt="$1" common
  common=$(git -C "$wt" rev-parse --git-common-dir 2>/dev/null) || hr_die "not inside a git repository: $wt"
  case "$common" in
    /*) ;;
    *) common="$(git -C "$wt" rev-parse --show-toplevel)/$common" ;;
  esac
  realpath "$(dirname "$common")"
}

repo_root=""
branch=""
wt_path=""
if [[ -n "$PATH_ARG" ]]; then
  [[ -d "$PATH_ARG" ]] || hr_die "--path: not a directory: $PATH_ARG"
  git_dir=$(git -C "$PATH_ARG" rev-parse --git-dir 2>/dev/null) || hr_die "--path: not inside a git work tree: $PATH_ARG"
  common_dir=$(git -C "$PATH_ARG" rev-parse --git-common-dir 2>/dev/null) || hr_die "--path: git query failed: $PATH_ARG"
  [[ "$git_dir" != "$common_dir" ]] || hr_die "--path: $PATH_ARG is the main checkout, not a linked worktree"
  repo_root=$(main_root_of_worktree "$PATH_ARG")
  branch=$(git -C "$PATH_ARG" rev-parse --abbrev-ref HEAD)
  [[ "$branch" != "HEAD" ]] || hr_die "--path: detached HEAD not supported"
  wt_path=$(realpath "$PATH_ARG")
else
  repo_root=$(hr_repo_root "$REPO_DIR")
  branch=$BRANCH
fi

git -C "$repo_root" remote get-url origin >/dev/null 2>&1 || hr_die "remote 'origin' not configured in $repo_root"
cd "$repo_root"
repo_name=$(basename "$repo_root")
primary=$(hr_primary_workspace "$repo_root" "$repo_name")
label=${LABEL:-$branch}

found=$(hr_find_worktree "$primary" "$branch")
open_ws=""
if [[ -n "$found" ]]; then
  # hr_find_worktree emits path<TAB>open_workspace_id; the path is never empty,
  # so it anchors the split (an empty first field would be eaten by read).
  IFS=$'\t' read -r wt_path open_ws <<< "$found"
fi

wt_ws=""
pane=""
pushed="-"
launched=no
reused=no
res=""
fresh=0

if [[ -n "$open_ws" ]]; then
  hr_info "branch $branch already open in herdr workspace $open_ws - reusing"
  if (( FOCUS )); then
    hr_json herdr workspace focus "$open_ws" >/dev/null
  fi
  reused=yes
  wt_ws=$open_ws
  # A docker-stopped devcontainer leaves its workspace open with a dead agent;
  # a resume must relaunch into the same pane instead of only focusing it.
  if (( RESUME && NO_LAUNCH == 0 )); then
    if alive_cid=$(hr_agent_alive_in_devcontainer "$wt_path" "$AGENT"); then
      hr_warn "$AGENT is still running inside devcontainer $alive_cid - not relaunching a second process on the same session; docker stop $alive_cid first if it is an orphan from a closed pane"
      launched=skipped-agent-alive
    else
      pane=$(hr_workspace_first_pane "$open_ws")
      [[ -n "$pane" ]] || hr_die "workspace $open_ws has no pane to relaunch $AGENT into - close it and re-run"
      hr_launch "$pane" "$PROMPT" "$wt_path" "$RESUME" "$AGENT"
      launched=yes
    fi
  fi
else
  focus_flag=--no-focus
  if (( FOCUS )); then focus_flag=--focus; fi

  if [[ -n "$wt_path" ]]; then
    hr_info "opening existing worktree $wt_path in herdr"
    res=$(hr_json herdr worktree open --workspace "$primary" --path "$wt_path" --label "$label" "$focus_flag")
  else
    exists_local=0
    exists_remote=0
    if git show-ref --verify --quiet "refs/heads/$branch"; then exists_local=1; fi
    remote_ref=$(git ls-remote --quiet origin "refs/heads/$branch" || true)
    if [[ -n "$remote_ref" ]]; then exists_remote=1; fi
    if (( exists_local )); then
      hr_info "branch $branch exists locally - checking out as-is (no rebase)"
      res=$(hr_json herdr worktree create --workspace "$primary" --branch "$branch" --label "$label" "$focus_flag")
    elif (( exists_remote )); then
      hr_info "branch $branch exists on origin - fetching into local"
      git fetch origin "${branch}:${branch}"
      res=$(hr_json herdr worktree create --workspace "$primary" --branch "$branch" --label "$label" "$focus_flag")
    elif [[ -n "$BASE" ]]; then
      hr_info "creating new branch $branch from $BASE"
      fresh=1
      res=$(hr_json herdr worktree create --workspace "$primary" --branch "$branch" --base "$BASE" --label "$label" "$focus_flag")
    else
      hr_die "branch '$branch' not found locally or on origin; pass --base <ref> to create it"
    fi
  fi

  wt_ws=$(printf '%s' "$res" | jq -r '.result.workspace.workspace_id // empty')
  pane=$(printf '%s' "$res" | jq -r '.result.root_pane.pane_id // empty')
  res_path=$(printf '%s' "$res" | jq -r '.result.worktree.path // empty')
  if [[ -n "$res_path" ]]; then wt_path=$res_path; fi

  [[ -n "$wt_ws" ]] || hr_die "worktree response missing .result.workspace.workspace_id"
  [[ -n "$wt_path" ]] || hr_die "worktree response missing worktree path"

  pushed=$(hr_tracking_guard "$wt_path" "$branch" "$fresh")

  if (( NO_LAUNCH == 0 )); then
    [[ -n "$pane" ]] || hr_die "worktree response missing .result.root_pane.pane_id (cannot launch)"
    if (( RESUME )) && alive_cid=$(hr_agent_alive_in_devcontainer "$wt_path" "$AGENT"); then
      hr_warn "$AGENT is still running inside devcontainer $alive_cid - not resuming on top of it; docker stop $alive_cid first"
      launched=skipped-agent-alive
    else
      hr_launch "$pane" "$PROMPT" "$wt_path" "$RESUME" "$AGENT"
      launched=yes
    fi
  fi
fi

echo "summary:"
printf 'branch=%s\n' "$branch"
printf 'worktree_path=%s\n' "${wt_path:--}"
printf 'worktree_workspace=%s\n' "${wt_ws:--}"
printf 'pane=%s\n' "${pane:--}"
printf 'primary_workspace=%s\n' "$primary"
printf 'pushed=%s\n' "$pushed"
printf 'launched=%s\n' "$launched"
printf 'reused=%s\n' "$reused"
printf 'agent=%s\n' "$AGENT"
