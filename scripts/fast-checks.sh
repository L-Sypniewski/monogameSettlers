#!/usr/bin/env bash
# Drift gate run by shipflow (opencode.jsonc -> fastChecks) and by CI: build with warnings visible, then the tests.
set -euo pipefail
cd "$(dirname "$0")/.."

# Vendored agent skills must stay lock-pinned (skills-lock.json, written by `npx skills add`).
bun run scripts/check-skills-lock.ts

dotnet build Homestead.sln -c Release --nologo -v q
dotnet test Homestead.sln -c Release --no-build --nologo
