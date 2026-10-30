#!/usr/bin/env bash
# Shell transport for ship_create_pr parity in Claude Code (unrelated to
# ship_finalize_pr, which already has its own wrapper at
# .opencode/scripts/finalize-pr.sh). Runs the package's gated create (same
# loop gate + ELI5 check) when Claude Code has no plugin loader to register
# the custom tool directly. This is the sanctioned create path, not a
# bypass - direct `gh pr create` stays denied.
#
# Usage (from anywhere inside the repo/worktree):
#   ship-create-pr.sh --slug <slug> --title <title> \
#     (--body <body> | --body-file <path>) --head <branch> --base <branch>
#
# Exit 0 iff the PR was created. The script is committed, so it exists in
# every worktree; it heals the package install via the committed guard first.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

bash "$script_dir/../../.opencode/scripts/ensure-shipflow-deps.sh" "$repo_root" >&2

cli="$repo_root/.claude/scripts/ship-create-pr-cli.ts"
if [ ! -f "$cli" ]; then
  echo "ship-create-pr: $cli missing - report a manual step (gh pr create --draft) for a human" >&2
  exit 1
fi
command -v bun >/dev/null 2>&1 || { echo "ship-create-pr: bun not found in PATH" >&2; exit 1; }

exec bun "$cli" --cwd "$repo_root" "$@"
