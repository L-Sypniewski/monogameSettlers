#!/usr/bin/env bash
# usage: herdr-issue.sh [--repo DIR] [--no-launch] [--focus] [--no-board-sync] (--branch NAME [--prompt TEXT] | <issue-number>...)
#   <issue-number>...   one or more GitHub issue numbers, space- or comma-separated
#   --branch NAME       skip GitHub issues; open the named branch directly
#   --prompt TEXT       launch prompt (only valid with --branch)
#   --repo DIR          repository to operate on (default: cwd)
#   --no-launch         prepare worktree + push, but do not start devcontainer/opencode
#   --focus             focus the new worktree workspace (default: background - the workspace
#                       is created and the launch typed without switching your view, so a
#                       focused pane you are typing in is never disturbed)
#   --no-focus          accepted as a no-op (background is the default)
#   --no-board-sync     skip assignee + project-board status sync for issues
#   --agent NAME        agent runtime for launches (opencode|claude, default opencode);
#                       a live agent in the worktree's devcontainer blocks the launch

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

usage() {
  cat <<'EOF'
usage: herdr-issue.sh [--repo DIR] [--no-launch] [--focus] [--no-board-sync] [--agent opencode|claude] (--branch NAME [--prompt TEXT] | <issue-number>...)
  issue numbers may be given space- or comma-separated; each gets its own worktree
EOF
}

REPO_DIR=$PWD
NO_LAUNCH=0
FOCUS=0
NO_BOARD_SYNC=0
BRANCH=""
PROMPT=""
AGENT="opencode"
ISSUES=()

while (( $# > 0 )); do
  case "$1" in
    --repo)
      (( $# >= 2 )) || hr_die "--repo requires a value"
      REPO_DIR=$2; shift 2 ;;
    --repo=*)
      REPO_DIR=${1#--repo=}; shift ;;
    --no-launch) NO_LAUNCH=1; shift ;;
    --focus) FOCUS=1; shift ;;
    --no-focus) FOCUS=0; shift ;;  # compat no-op: background is the default
    --no-board-sync) NO_BOARD_SYNC=1; shift ;;
    --agent)
      (( $# >= 2 )) || hr_die "--agent requires a value"
      AGENT=$2; shift 2 ;;
    --agent=*)
      AGENT=${1#--agent=}; shift ;;
    --branch)
      (( $# >= 2 )) || hr_die "--branch requires a value"
      BRANCH=$2; shift 2 ;;
    --prompt)
      (( $# >= 2 )) || hr_die "--prompt requires a value"
      PROMPT=$2; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    -*) usage >&2; hr_die "unknown option: $1" ;;
    *)
      IFS=',' read -r -a _parts <<< "$1"
      for p in "${_parts[@]}"; do
        p=${p#"${p%%[![:space:]]*}"}
        p=${p%"${p##*[![:space:]]}"}
        [[ "$p" =~ ^[0-9]+$ ]] || hr_die "invalid issue number: $p"
        ISSUES+=("$p")
      done
      shift ;;
  esac
done

if [[ -n "$BRANCH" && ${#ISSUES[@]} -gt 0 ]]; then
  hr_die "--branch cannot be combined with issue numbers"
fi
if [[ -z "$BRANCH" && ${#ISSUES[@]} -eq 0 ]]; then
  usage >&2
  hr_die "provide one or more issue numbers, or --branch NAME"
fi
if [[ -n "$PROMPT" && -z "$BRANCH" ]]; then
  hr_die "--prompt is only valid together with --branch"
fi
if [[ "$AGENT" != "opencode" && "$AGENT" != "claude" ]]; then
  hr_die "--agent must be opencode or claude"
fi

# Launch deps: devcontainer + docker are only required when the target repo has a devcontainer
# config; repos without one launch the agent on the host and skip both.
if (( NO_LAUNCH )); then hr_require_cmds; else hr_require_launch_cmds "$REPO_DIR"; fi

repo_root=$(hr_repo_root "$REPO_DIR")
repo_name=$(basename "$repo_root")
cd "$repo_root"

run_flow() {
  local branch="$1" label="$2" prompt="$3" n="${4:-}"
  local primary="" found="" open_ws="" wt_path="" wt_ws="" pane=""
  local pushed="-" launched=no reused=no
  primary=$(hr_primary_workspace "$repo_root" "$repo_name")

  found=$(hr_find_worktree "$primary" "$branch")
  if [[ -n "$found" ]]; then
    # hr_find_worktree emits path<TAB>open_workspace_id; the path is never empty,
    # so it anchors the split (an empty first field would be eaten by read).
    IFS=$'\t' read -r wt_path open_ws <<< "$found"
  fi

  if [[ -n "$open_ws" ]]; then
    hr_info "branch $branch already open in herdr workspace $open_ws - reusing"
    if (( FOCUS )); then
      hr_json herdr workspace focus "$open_ws" >/dev/null
    fi
    reused=yes
    wt_ws=$open_ws
  else
    local base="" exists_local=0 exists_remote=0 remote_ref create_json default_branch
    if git show-ref --verify --quiet "refs/heads/$branch"; then exists_local=1; fi
    remote_ref=$(git ls-remote --quiet origin "refs/heads/$branch" || true)
    if [[ -n "$remote_ref" ]]; then exists_remote=1; fi

    if (( exists_local )); then
      hr_info "branch $branch exists locally - checking out as-is (no rebase)"
    elif (( exists_remote )); then
      hr_info "branch $branch exists on origin - fetching into local"
      git fetch origin "${branch}:${branch}"
    else
      default_branch=$(hr_default_branch)
      git fetch origin "$default_branch"
      base="origin/${default_branch}"
    fi

    local focus_flag=--no-focus
    if (( FOCUS )); then focus_flag=--focus; fi

    if [[ -n "$wt_path" ]]; then
      create_json=$(hr_json herdr worktree open --workspace "$primary" --path "$wt_path" --label "$label" "$focus_flag")
      wt_ws=$(printf '%s' "$create_json" | jq -r '.result.workspace.workspace_id // empty')
      pane=$(printf '%s' "$create_json" | jq -r '.result.root_pane.pane_id // empty')
    elif [[ -n "$base" ]]; then
      create_json=$(hr_json herdr worktree create --workspace "$primary" --branch "$branch" --base "$base" --label "$label" "$focus_flag")
      wt_ws=$(printf '%s' "$create_json" | jq -r '.result.workspace.workspace_id // empty')
      pane=$(printf '%s' "$create_json" | jq -r '.result.root_pane.pane_id // empty')
      wt_path=$(printf '%s' "$create_json" | jq -r '.result.worktree.path // empty')
    else
      create_json=$(hr_json herdr worktree create --workspace "$primary" --branch "$branch" --label "$label" "$focus_flag")
      wt_ws=$(printf '%s' "$create_json" | jq -r '.result.workspace.workspace_id // empty')
      pane=$(printf '%s' "$create_json" | jq -r '.result.root_pane.pane_id // empty')
      wt_path=$(printf '%s' "$create_json" | jq -r '.result.worktree.path // empty')
    fi

    [[ -n "$wt_ws" ]] || hr_die "worktree response missing .result.workspace.workspace_id"
    [[ -n "$wt_path" ]] || hr_die "worktree response missing worktree path"

    local fresh=0
    [[ -n "$base" ]] && fresh=1
    pushed=$(hr_tracking_guard "$wt_path" "$branch" "$fresh")

    if (( NO_LAUNCH == 0 )); then
      [[ -n "$pane" ]] || hr_die "worktree response missing .result.root_pane.pane_id (cannot launch)"
      if alive_cid=$(hr_agent_alive_in_devcontainer "$wt_path" "$AGENT"); then
        hr_warn "$AGENT is still running inside devcontainer $alive_cid - not launching a second agent into the same worktree; docker stop $alive_cid first if it is an orphan from a closed pane"
        launched=skipped-agent-alive
      else
        hr_launch "$pane" "$prompt" "$wt_path" 0 "$AGENT"
        launched=yes
      fi
    fi
  fi

  echo "summary:"
  if [[ -n "$n" ]]; then printf 'issue=%s\n' "$n"; fi
  printf 'branch=%s\n' "$branch"
  printf 'worktree_path=%s\n' "${wt_path:--}"
  printf 'worktree_workspace=%s\n' "${wt_ws:--}"
  printf 'pane=%s\n' "${pane:--}"
  printf 'primary_workspace=%s\n' "$primary"
  printf 'pushed=%s\n' "$pushed"
  printf 'launched=%s\n' "$launched"
  printf 'reused=%s\n' "$reused"
  printf 'agent=%s\n' "$AGENT"
}

process_issue() {
  local n="$1" json number title url slug mention
  hr_info "processing issue #$n"
  json=$(gh issue view "$n" --json number,title,url) || hr_die "gh issue view $n failed"
  number=$(printf '%s' "$json" | jq -r '.number // empty')
  title=$(printf '%s' "$json" | jq -r '.title // empty')
  url=$(printf '%s' "$json" | jq -r '.url // empty')
  [[ -n "$number" && -n "$title" ]] || hr_die "unexpected gh issue view response for #$n"
  slug=$(hr_slugify "$title")
  mention=$(hr_issue_prompt_mention "$repo_root")
  run_flow "issue-${number}-${slug}" "#${number} ${slug}" "$(hr_issue_prompt "$mention" "$number" "$title" "$url")" "$number"
  if (( NO_BOARD_SYNC == 0 )); then
    hr_board_sync "$repo_root" "$number" "$url"
  fi
}

if [[ -n "$BRANCH" ]]; then
  run_flow "$BRANCH" "$BRANCH" "$PROMPT" ""
else
  for n in "${ISSUES[@]}"; do
    if ! ( process_issue "$n" ); then
      hr_warn "issue #$n processing failed - continuing with remaining issues"
    fi
  done
fi
