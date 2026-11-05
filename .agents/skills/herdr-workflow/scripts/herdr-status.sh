#!/usr/bin/env bash
# usage: herdr-status.sh [--repo DIR]
#   Human-readable overview: herdr workspaces, linked worktrees per repo, and agents.

set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib.sh"

usage() {
  cat <<'EOF'
usage: herdr-status.sh [--repo DIR]
EOF
}

REPO_DIR=""
while (( $# > 0 )); do
  case "$1" in
    --repo)
      (( $# >= 2 )) || hr_die "--repo requires a value"
      REPO_DIR=$2; shift 2 ;;
    --repo=*)
      REPO_DIR=${1#--repo=}; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) usage >&2; hr_die "unknown option: $1" ;;
    *) usage >&2; hr_die "unexpected argument: $1" ;;
  esac
done

hr_require_cmds herdr jq

filter_root=""
filter_name=""
if [[ -n "$REPO_DIR" ]]; then
  [[ -d "$REPO_DIR" ]] || hr_die "--repo: not a directory: $REPO_DIR"
  filter_root=$(realpath "$REPO_DIR")
  filter_name=$(basename "$filter_root")
fi

list=$(hr_json herdr workspace list)

echo "herdr workspaces:"
all_ws_ids=()
ws_count=0
while IFS=$'\t' read -r id label focused agent tabs panes root checkout linked; do
  all_ws_ids+=("$id")
  if [[ -n "$filter_root" && "$root" != "$filter_root" && "$label" != "$filter_name" ]]; then
    continue
  fi
  marker=""
  if [[ "$focused" == "true" ]]; then marker=" [focused]"; fi
  printf '  %s  %s%s  agent=%s  tabs=%s  panes=%s\n' "$id" "$label" "$marker" "$agent" "$tabs" "$panes"
  if [[ "$linked" == "true" ]]; then
    printf '      worktree: %s (linked worktree, repo root: %s)\n' "${checkout:-$root}" "$root"
  elif [[ -n "$root" ]]; then
    printf '      checkout: %s (primary checkout)\n' "${checkout:-$root}"
  fi
  ws_count=$((ws_count + 1))
done < <(printf '%s' "$list" | jq -r '.result.workspaces[]?
  | [(.workspace_id // "?" | tostring),
     (.label // "" | tostring),
     (.focused // false | tostring),
     (.agent_status // "unknown" | tostring),
     (.tab_count // 0 | tostring),
     (.pane_count // 0 | tostring),
     (.worktree.repo_root // "" | tostring),
     (.worktree.checkout_path // "" | tostring),
     (.worktree.is_linked_worktree // "" | tostring)] | @tsv')

if (( ws_count == 0 )); then
  echo "  (no matching workspaces)"
fi

declare -A seen=()
while IFS=$'\t' read -r id root; do
  [[ -n "$root" ]] || continue
  if [[ -n "$filter_root" && "$root" != "$filter_root" ]]; then continue; fi
  if [[ -n "${seen[$root]:-}" ]]; then continue; fi
  seen[$root]=1
  echo
  echo "worktrees of $root (workspace $id):"
  wts=$(hr_json herdr worktree list --workspace "$id" | jq -r '.result.worktrees[]?
    | select(.is_linked_worktree == true)
    | [(.branch // "?" | tostring),
       (.path // "" | tostring),
       ((.open_workspace_id // "-") | tostring)] | @tsv')
  if [[ -z "$wts" ]]; then
    echo "  (none)"
  else
    while IFS=$'\t' read -r b p o; do
      printf '  %s  %s  open=%s\n' "$b" "$p" "$o"
    done <<< "$wts"
  fi
done < <(printf '%s' "$list" | jq -r '.result.workspaces[]?
  | select(.worktree != null and .worktree.is_linked_worktree == false)
  | [(.workspace_id // "?" | tostring), (.worktree.repo_root // "" | tostring)] | @tsv')

echo
echo "agents:"
agents=$(hr_json herdr agent list | jq -r '.result.agents[]?
  | [((.name // .label // .agent // .id // "?") | tostring),
     ((.pane // .pane_id // "") | tostring),
     ((.state // .status // "") | tostring)] | @tsv')
if [[ -z "$agents" ]]; then
  echo "  (none)"
else
  while IFS=$'\t' read -r name pane state; do
    ws="(unassigned)"
    for cand in "${all_ws_ids[@]}"; do
      if [[ -n "$cand" && "$pane" == "$cand"* ]]; then
        ws=$cand
        break
      fi
    done
    printf '  %s  pane=%s  state=%s  workspace=%s\n' "$name" "$pane" "$state" "$ws"
  done <<< "$agents"
fi
