#!/usr/bin/env bash
# Shell transport for ship_finalize_pr: runs the package's gated finalize
# (identical loop gate + risk profile) when the plugin-tool result channel is
# broken. This is the sanctioned flip path, not a bypass - direct
# `gh pr ready` / `gh pr merge` stay denied.
#
# Usage (from anywhere inside the repo/worktree):
#   finalize-pr.sh --slug <slug> --pr <number> --base <branch>
#
# Exit 0 iff the PR ended up ready. The script is committed, so it exists in
# every worktree; it heals the package install via the committed guard first.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

bash "$script_dir/ensure-shipflow-deps.sh" "$repo_root" >&2

cli="$repo_root/.opencode/shipflow/src/cli.ts"
if [ ! -f "$cli" ]; then
  echo "finalize-pr: $cli missing after install guard - report a manual step (gh pr ready <n>) for a human" >&2
  exit 1
fi
command -v bun >/dev/null 2>&1 || { echo "finalize-pr: bun not found in PATH" >&2; exit 1; }

exec bun "$cli" finalize --cwd "$repo_root" "$@"
