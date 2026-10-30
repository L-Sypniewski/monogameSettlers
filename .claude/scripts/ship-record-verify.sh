#!/usr/bin/env bash
# Shell transport for loop_record_verify parity in Claude Code (sibling of
# ship-create-pr.sh - see that file + ship-record-verify-cli.ts headers).
# Writes a verify record through the real, installed package function so
# ship_create_pr's gate (evaluateGate) sees it exactly as it would from an
# OpenCode session. Not a bypass - it records real test outcomes, never
# fabricates a pass.
#
# Usage (from anywhere inside the repo/worktree):
#   ship-record-verify.sh --slug <slug> --result <pass|fail> \
#     --command <description> [--sweep-commit-sha <sha>]
#
# Exit 0 on a successful record (any result value). The script is committed,
# so it exists in every worktree; it heals the package install first.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

bash "$script_dir/../../.opencode/scripts/ensure-shipflow-deps.sh" "$repo_root" >&2

cli="$repo_root/.claude/scripts/ship-record-verify-cli.ts"
if [ ! -f "$cli" ]; then
  echo "ship-record-verify: $cli missing - record .plans/<slug>.loop.json manually for a human" >&2
  exit 1
fi
command -v bun >/dev/null 2>&1 || { echo "ship-record-verify: bun not found in PATH" >&2; exit 1; }

exec bun "$cli" --cwd "$repo_root" "$@"
