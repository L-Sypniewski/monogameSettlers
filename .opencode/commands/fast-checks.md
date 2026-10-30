---
description: Run local fast-checks (build + lint + drift gates) and fix any failure
---

Run `bash scripts/fast-checks.sh` from the repo root and report the per-step
results. It mirrors CI's `fast-checks` + `pr-common-checks` jobs (backend
build, OpenAPI freshness, frontend quality, doc drift, composite-tenant-FK,
agents-sync) - the one local command that catches drift CI enforces.

For any step that fails, read its tail in the output and apply the minimal fix
the step prints, then re-run the script. Common fixes:

- openapi freshness → `(cd frontend && pnpm run regen-openapi)`
- agents sync → `bun run .opencode/scripts/sync-agents.ts && git add .claude`
- doc drift → follow `scripts/check-docs.ts` output (update the offending AGENTS.md count/claim)

Re-run until every step is green before committing/pushing. Add `--full` to also
run the frontend component Playwright tier (needs chromium).
