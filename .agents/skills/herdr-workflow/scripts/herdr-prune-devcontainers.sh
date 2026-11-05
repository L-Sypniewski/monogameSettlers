#!/usr/bin/env bash
# WHY: /prune-devcontainers invokes this via ~/.local/bin (no extension);
# herdr-watchdog.sh --install-systemd stages it there.
set -euo pipefail

PROTECTED_VOLUMES="${HERDR_PRUNE_PROTECTED_VOLUMES:-nuget-cache-volume mise-data-volume playwright-browsers-volume}"
ASSUME_YES=0
DRY_RUN=0
EXTRA_KEEP_FOLDERS=()

usage() {
  cat <<'EOF'
Usage: herdr-prune-devcontainers [--yes] [--dry-run] [--keep <folder>]...

Removes devcontainers (and their volumes) that are not attached to an active
herdr agent session. A devcontainer is KEPT when its devcontainer.local_folder
matches the cwd of any open herdr pane with a detected agent, or when passed
via --keep.

Options:
  -y, --yes        execute without confirmation
  -n, --dry-run    print the plan only, exit 0
  --keep <folder>  additionally keep the container for this workspace folder
  -h, --help       show this help

Protected shared volumes (never deleted, override via HERDR_PRUNE_PROTECTED_VOLUMES):
EOF
  echo "  $PROTECTED_VOLUMES"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -y|--yes) ASSUME_YES=1 ;;
    -n|--dry-run) DRY_RUN=1 ;;
    --keep) EXTRA_KEEP_FOLDERS+=("$2"); shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

die() { echo "error: $*" >&2; exit 1; }

command -v herdr >/dev/null || die "herdr not found in PATH"
command -v jq >/dev/null || die "jq not found in PATH"
command -v docker >/dev/null || die "docker not found in PATH"
herdr status >/dev/null 2>&1 || die "herdr server not reachable"

active_folders="$(
  herdr workspace list | jq -r '.result.workspaces[].workspace_id' | while read -r ws; do
    herdr pane list --workspace "$ws" | jq -r '.result.panes[] | select(.agent != null) | .cwd'
  done | sort -u
)"

is_kept() {
  local folder="$1"
  [[ -z "$folder" ]] && return 1
  grep -qxF "$folder" <<<"$active_folders" && return 0
  local k
  for k in "${EXTRA_KEEP_FOLDERS[@]:-}"; do
    [[ "$k" == "$folder" ]] && return 0
  done
  return 1
}

declare -a KEEP_IDS=() KEEP_NAMES=() KEEP_FOLDERS=()
declare -a PRUNE_IDS=() PRUNE_NAMES=() PRUNE_FOLDERS=()

while IFS=$'\t' read -r id name status folder; do
  if is_kept "$folder"; then
    KEEP_IDS+=("$id"); KEEP_NAMES+=("$name"); KEEP_FOLDERS+=("$folder")
  else
    PRUNE_IDS+=("$id"); PRUNE_NAMES+=("$name"); PRUNE_FOLDERS+=("$folder")
  fi
done < <(docker ps -a --filter label=devcontainer.local_folder \
          --format '{{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Label "devcontainer.local_folder"}}')

protected_volumes="$(sort -u <<<"$PROTECTED_VOLUMES")"
keep_volumes=""
if [[ ${#KEEP_IDS[@]} -gt 0 ]]; then
  keep_volumes="$(docker inspect "${KEEP_IDS[@]}" --format '{{range .Mounts}}{{.Name}}{{"\n"}}{{end}}' | grep -v '^$' | sort -u)"
fi

prune_volumes=""
volume_failures=()
if [[ ${#PRUNE_IDS[@]} -gt 0 ]]; then
  prune_volumes="$(docker inspect "${PRUNE_IDS[@]}" --format '{{range .Mounts}}{{.Name}}{{"\n"}}{{end}}' | grep -v '^$' | sort -u | \
    grep -vxF -f <(printf '%s\n' "$keep_volumes" "$protected_volumes" | grep -v '^$') || true)"
fi

echo "=== KEEP (${#KEEP_NAMES[@]}) ==="
for i in "${!KEEP_NAMES[@]}"; do
  echo "  ${KEEP_NAMES[$i]}  ${KEEP_FOLDERS[$i]}"
done
echo
echo "=== PRUNE (${#PRUNE_NAMES[@]}) ==="
for i in "${!PRUNE_NAMES[@]}"; do
  echo "  ${PRUNE_NAMES[$i]}  ${PRUNE_FOLDERS[$i]}"
done
echo
echo "=== VOLUMES TO DELETE ==="
if [[ -n "$prune_volumes" ]]; then
  sed 's/^/  /' <<<"$prune_volumes"
else
  echo "  (none)"
fi

if [[ ${#PRUNE_IDS[@]} -eq 0 ]]; then
  echo "nothing to prune"
  exit 0
fi

if [[ $DRY_RUN -eq 1 ]]; then
  echo "(dry run: nothing executed)"
  exit 0
fi

if [[ $ASSUME_YES -ne 1 ]]; then
  if [[ -t 0 ]]; then
    read -r -p "Type 'prune' to confirm: " reply
    [[ "$reply" == "prune" ]] || die "aborted"
  else
    die "not a TTY and --yes not given; re-run with --yes (or --dry-run)"
  fi
fi

docker rm -f "${PRUNE_IDS[@]}"

if [[ -n "$prune_volumes" ]]; then
  while read -r v; do
    [[ -z "$v" ]] && continue
    docker volume rm "$v" || volume_failures+=("$v")
  done <<<"$prune_volumes"
fi

if [[ ${#volume_failures[@]} -gt 0 ]]; then
  echo "warning: failed to remove volumes:" >&2
  printf '  %s\n' "${volume_failures[@]}" >&2
  exit 1
fi
echo "done: removed ${#PRUNE_IDS[@]} container(s) and their volumes"
