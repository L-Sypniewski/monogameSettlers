#!/usr/bin/env bash
# usage: herdr-cleanup.sh (--branch B | --path P | --workspace ID) [--repo DIR] [--delete-branch] [--delete-remote-branch] [--force] [--yes]
#   Removes a herdr worktree workspace, prunes git worktree metadata, optionally
#   deletes the local and/or remote branch. Requires confirmation unless --yes.

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

usage() {
  cat <<'EOF'
usage: herdr-cleanup.sh (--branch B | --path P | --workspace ID) [--repo DIR] [--delete-branch] [--delete-remote-branch] [--force] [--yes]
EOF
}

REPO_DIR=$PWD
BRANCH=""
PATH_ARG=""
WS_ID=""
DELETE_BRANCH=0
DELETE_REMOTE=0
FORCE=0
ASSUME_YES=0

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
    --path)
      (( $# >= 2 )) || hr_die "--path requires a value"
      PATH_ARG=$2; shift 2 ;;
    --workspace)
      (( $# >= 2 )) || hr_die "--workspace requires a value"
      WS_ID=$2; shift 2 ;;
    --delete-branch) DELETE_BRANCH=1; shift ;;
    --delete-remote-branch) DELETE_REMOTE=1; shift ;;
    --force) FORCE=1; shift ;;
    --yes) ASSUME_YES=1; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) usage >&2; hr_die "unknown option: $1" ;;
    *) usage >&2; hr_die "unexpected argument: $1" ;;
  esac
done

selectors=0
[[ -n "$BRANCH" ]] && selectors=$((selectors + 1))
[[ -n "$PATH_ARG" ]] && selectors=$((selectors + 1))
[[ -n "$WS_ID" ]] && selectors=$((selectors + 1))
if (( selectors != 1 )); then
  usage >&2
  hr_die "exactly one of --branch, --path or --workspace is required"
fi

hr_require_cmds herdr jq git

main_root_of_worktree() {
  local wt="$1" common
  common=$(git -C "$wt" rev-parse --git-common-dir 2>/dev/null) || hr_die "not inside a git repository: $wt"
  case "$common" in
    /*) ;;
    *) common="$(git -C "$wt" rev-parse --show-toplevel)/$common" ;;
  esac
  realpath "$(dirname "$common")"
}

find_primary() {
  local real="$1" name="$2"
  hr_json herdr workspace list | jq -r --arg root "$real" --arg name "$name" '
    [.result.workspaces[]
      | select(.worktree != null and .worktree.repo_root == $root and .worktree.is_linked_worktree == false)
      | .workspace_id][0] // ""'
}

repo_root=""
wt_path=""
branch=""
target_ws=""

if [[ -n "$WS_ID" ]]; then
  wjson=$(hr_json herdr workspace get "$WS_ID")
  wt_path=$(printf '%s' "$wjson" | jq -r '.result.workspace.worktree.checkout_path // .result.worktree.checkout_path // .result.workspace.checkout_path // empty')
  [[ -n "$wt_path" ]] || hr_die "workspace $WS_ID has no worktree (nothing to clean up)"
  linked=$(printf '%s' "$wjson" | jq -r '.result.workspace.worktree.is_linked_worktree // .result.worktree.is_linked_worktree // true')
  [[ "$linked" == "true" ]] || hr_die "workspace $WS_ID is the primary checkout - refusing to clean up"
  [[ -d "$wt_path" ]] || hr_die "worktree path missing on disk: $wt_path"
  repo_root=$(main_root_of_worktree "$wt_path")
  branch=$(git -C "$wt_path" rev-parse --abbrev-ref HEAD 2>/dev/null) || hr_die "cannot determine branch of $wt_path"
  target_ws=$WS_ID
elif [[ -n "$PATH_ARG" ]]; then
  [[ -d "$PATH_ARG" ]] || hr_die "--path: not a directory: $PATH_ARG"
  git_dir=$(git -C "$PATH_ARG" rev-parse --git-dir 2>/dev/null) || hr_die "--path: not inside a git work tree: $PATH_ARG"
  common_dir=$(git -C "$PATH_ARG" rev-parse --git-common-dir 2>/dev/null) || hr_die "--path: git query failed: $PATH_ARG"
  [[ "$git_dir" != "$common_dir" ]] || hr_die "--path: $PATH_ARG is the main checkout, not a linked worktree"
  wt_path=$(realpath "$PATH_ARG")
  repo_root=$(main_root_of_worktree "$wt_path")
  branch=$(git -C "$wt_path" rev-parse --abbrev-ref HEAD 2>/dev/null) || hr_die "cannot determine branch of $wt_path"
  target_ws=$(hr_json herdr workspace list | jq -r --arg p "$wt_path" '
    [.result.workspaces[]
      | select(.worktree != null and .worktree.checkout_path == $p and .worktree.is_linked_worktree == true)
      | .workspace_id][0] // ""')
else
  repo_root=$(hr_repo_root "$REPO_DIR")
  primary=$(find_primary "$(realpath "$repo_root")" "$(basename "$repo_root")")
  [[ -n "$primary" ]] || hr_die "no primary herdr workspace found for $(basename "$repo_root") - nothing to clean up"
  found=$(hr_find_worktree "$primary" "$BRANCH")
  [[ -n "$found" ]] || hr_die "no git worktree found for branch $BRANCH"
  # hr_find_worktree emits path<TAB>open_workspace_id; the path is never empty,
  # so it anchors the split (an empty first field would be eaten by read).
  IFS=$'\t' read -r wt_path target_ws <<< "$found"
  branch=$BRANCH
fi

local_delete=no
if (( DELETE_BRANCH )); then local_delete=yes; fi
remote_delete=no
if (( DELETE_REMOTE )); then remote_delete=yes; fi

echo "herdr cleanup plan:"
echo "  worktree path  : $wt_path"
echo "  herdr workspace: ${target_ws:-(not open in herdr)}"
echo "  branch         : $branch"
echo "  repo root      : $repo_root"
echo "  delete local branch : $local_delete"
echo "  delete remote branch: $remote_delete"

dirty=""
if [[ -d "$wt_path" ]]; then
  dirty=$(git -C "$wt_path" status --porcelain 2>/dev/null || true)
fi
if [[ -n "$dirty" ]]; then
  hr_warn "worktree has uncommitted changes"
  if (( FORCE == 0 )); then
    hr_die "refusing to remove dirty worktree - commit or stash first, or pass --force"
  fi
else
  echo "  checkout       : clean"
fi
echo "  hint: leftover per-worktree docker volumes may remain - check: docker volume ls | grep $(basename "$wt_path")"

if (( ASSUME_YES == 0 )); then
  prompt='Proceed with removal?'
  if (( DELETE_BRANCH || DELETE_REMOTE )); then
    prompt="Proceed with worktree removal AND branch deletion (local=$local_delete remote=$remote_delete)"
  fi
  printf '%s [y/N] ' "$prompt"
  if ! read -r reply </dev/tty; then
    hr_die "confirmation needs an interactive terminal - re-run with --yes"
  fi
  case "$reply" in
    y|Y|yes|YES) ;;
    *) hr_die "aborted" ;;
  esac
fi

removed=no
if [[ -n "$target_ws" ]]; then
  if (( FORCE )); then
    hr_json herdr worktree remove --workspace "$target_ws" --force >/dev/null
  else
    hr_json herdr worktree remove --workspace "$target_ws" >/dev/null
  fi
else
  hr_info "worktree not open in herdr - using plain git worktree remove"
  if (( FORCE )); then
    git -C "$repo_root" worktree remove --force "$wt_path"
  else
    git -C "$repo_root" worktree remove "$wt_path"
  fi
fi
removed=yes
git -C "$repo_root" worktree prune
hr_info "pruned worktree metadata"

branch_deleted=skipped
if (( DELETE_BRANCH )); then
  if git -C "$repo_root" branch -d "$branch" 2>/dev/null; then
    branch_deleted=yes
  elif (( FORCE )) && git -C "$repo_root" branch -D "$branch" 2>/dev/null; then
    branch_deleted=yes-forced
  else
    hr_warn "branch $branch not deleted (not fully merged) - re-run with --force to delete it"
    branch_deleted=no
  fi
fi

remote_branch_deleted=skipped
if (( DELETE_REMOTE )); then
  # Warn-only: a failed fetch must not abort cleanup after the removal steps above.
  git -C "$repo_root" fetch origin >/dev/null 2>&1 || hr_warn "fetch origin failed - merge check uses stale tracking refs"
  unmerged_reason=""
  if ! default_branch=$(cd "$repo_root" && hr_default_branch 2>/dev/null); then default_branch=""; fi
  if [[ -n "$default_branch" ]]; then
    # The gate checks the remote-tracking ref because branch -d above may have already removed the local one.
    merge_status=0
    git -C "$repo_root" merge-base --is-ancestor "origin/$branch" "origin/$default_branch" 2>/dev/null || merge_status=$?
    if (( merge_status == 1 )); then
      unmerged_reason="origin/$branch has commits not merged into origin/$default_branch"
    elif (( merge_status > 1 )); then
      hr_warn "could not verify merge status of origin/$branch (merge-base exit $merge_status) - remote delete proceeds unverified"
    fi
  else
    hr_warn "unable to determine default branch of origin - cannot verify merge status of origin/$branch, treating it as unmerged"
    unmerged_reason="origin/$branch may have unmerged commits (remote default branch unknown)"
  fi
  proceed_remote_delete=1
  if [[ -n "$unmerged_reason" ]] && (( FORCE == 0 )); then
    proceed_remote_delete=0
    if (( ASSUME_YES == 0 )); then
      printf '%s - delete anyway? [y/N] ' "$unmerged_reason"
      if ! read -r reply </dev/tty; then
        hr_die "confirmation needs an interactive terminal - re-run with --yes"
      fi
      case "$reply" in
        y|Y|yes|YES) proceed_remote_delete=1 ;;
        *) hr_warn "keeping remote branch origin/$branch"; remote_branch_deleted=kept ;;
      esac
    else
      hr_warn "$unmerged_reason - remote branch not deleted; re-run with --force to delete it"
      remote_branch_deleted=kept
    fi
  fi
  if (( proceed_remote_delete )); then
    if git -C "$repo_root" push origin --delete "$branch" 2>/dev/null; then
      remote_branch_deleted=yes
    else
      hr_warn "failed to delete remote branch origin/$branch"
      remote_branch_deleted=no
    fi
  fi
fi

echo "summary:"
printf 'branch=%s\n' "$branch"
printf 'worktree_path=%s\n' "$wt_path"
printf 'workspace=%s\n' "${target_ws:--}"
printf 'removed=%s\n' "$removed"
printf 'branch_deleted=%s\n' "$branch_deleted"
printf 'remote_branch_deleted=%s\n' "$remote_branch_deleted"
