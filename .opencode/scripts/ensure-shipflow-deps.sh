#!/usr/bin/env bash
# Installs the vendored shipflow plugin's dependencies (.opencode/shipflow) when missing. Idempotent and quiet.
set -u

repo_root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
pkg_dir="$repo_root/.opencode/shipflow"

[ -d "$pkg_dir/node_modules/zod" ] && exit 0
echo "ensure-shipflow-deps: installing dependencies in .opencode/shipflow"
bun install --cwd "$pkg_dir" >/dev/null
