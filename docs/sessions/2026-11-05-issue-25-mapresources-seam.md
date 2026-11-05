# Session decision log — issue #25 PR 1: MapResources extraction (2026-11-05)

## Goal

Resume the interrupted ship run for issue #25 PR 1 (slug `issue-25-mapresources-seam`): extract map growth/depletion/regeneration from `Production.cs` into a new partial-class file `MapResources.cs` — behavior-identical, save-compatible, named in the German architecture doc. The prior session died right after the loop-1 re-review; the resume contract was to re-do nothing (no re-plan, no re-implement) and continue the pipeline tail from verified state.

## Pipeline

- **Plan**: `.plans/issue-25-mapresources-seam.md` (plan-orchestrator, plan-only; prior session). Phase 1 = verbatim extraction (9 methods + 5 state collections into `public sealed partial class Production`), Phase 2 = German arch-doc bullet.
- **Implement** (prior session, fresh software-engineer): `1f9c196` (extraction), `face135` (doc bullet).
- **Review loop** (prior session): iter 1 → 1 Should (F-EXTRACT-1, `MapResources.cs` self-description gap around `_stoneCuts`) + 1 Could (F-DOCS-1, German bullet mis-grouped `_depletionDue`) → `loop_record_replan` 1 → plan amendment L1.1/L1.2 → both fixed doc-text-only in `6d81f86` → full sweep **505/505** at `6d81f86` (sweep checkpoint = HEAD, kind full).
- **Re-review** (recovered by resume — report mtime 09:41 postdates the 09:39 verify): **APPROVE**, both findings verified FIXED, 0 Must / 0 Should / 0 Could / 0 Won't open + 2 no-action observations; the move was verified mechanically (145 non-blank code lines identical and in order; `Production.cs` diff deletion-only).
- **Exit** (this session): no fix-apply pass (0 open Could; the two observations are no-action by reviewer disposition); no exit sweep (checkpoint == HEAD, kind already full); 4c PR-readiness gate via fresh software-engineer — skills-lock **pass** (the one PR-CI step the sweep doesn't cover), build+tests covered by the sweep at the same commit, security probes N/A (Branch 2 evaluated: diff touches no auth/session surface), monorepo regen checks confirmed absent in this repo; 4d screenshots N/A (no frontend surface).
- **Ship** (this session, after credential restoration mid-run): branch pushed (`gh auth setup-git` + `git push -u`), draft **PR #56** via gated `ship_create_pr` (independently verified via `gh pr view`: draft, `issue-25-mapresources-seam` → `main`), follow-up **issue #57** (user-approved), ready-flip via gated `ship_finalize_pr` (autoMerge off — `.shipflow.json` has no `autoMerge` block).

## Review audit summary

Iter 1: 0 Must / 1 Should / 0 Could / 0 Won't + 2 observations → Should **fixed-in-branch** (`6d81f86`, XML-doc text only — F-EXTRACT-1/L1.1); Could **fixed-in-branch** (`6d81f86`, German bullet re-grouped per-kind vs per-field depletion — F-DOCS-1/L1.2); re-review confirmed both and found nothing new. Observations **declined-with-reason** (reviewer's own dispositions): O-1 — `Production.cs`'s class summary still announces growth/regeneration because it documents the *type*, which still does all of it via the partial (same precedent as `Healing`/`Shipbuilding`); O-2 — cross-partial field-initializer order is compiler-unspecified but harmless (independent empty-collection initializers, seeded `Random` stays in the core ctor, name-keyed save reader). Net: every finding reached a terminal disposition; nothing deferred, PR carries no non-blocking checklist.

## Key decisions

- **Resume on evidence, not checkboxes.** The plan's checkboxes were all unticked (tracking lost, work intact). Position was reconstructed from `loop.json` (replan 1; verify pass at `6d81f86`), the commit graph (3 commits, base `origin/main` = `669ddf9`) and the report mtime ordering (re-review 09:41 > verify 09:39) → resumed at the loop exit, re-ran nothing. The six phase checkboxes were ticked retroactively on that evidence.
- **`Closes #25` rides PR 1 of 3** (flag for the human). Carried per the plan's `## Linked issues` line — ship's mechanical rule. Since #25 also covers PR 2 (SmithScheduler) and PR 3 (gatherers), remove the `Closes #25` line from PR #56's body before merging if the issue must stay open for the remaining PRs; nothing else in the PR depends on it.
- **Credential outage handled without re-running phases.** The container was born without `GH_TOKEN` (devcontainer `remoteEnv` forwarded an empty host value; no gh login, no SSH keys) → push/PR/finalize blocked. All local phases completed first (4c gate, PR body stored durably in the plan, recovery notes in the Loop log); each auth-dependent step was attempted exactly once for an authoritative record; the run parked with exact handoff steps. The operator restored auth mid-run (`hosts.yml` in this container; dynamic `GH_TOKEN` export in `~/.zshrc.secrets` so future pane-launched containers are born authenticated) and the tail completed from the parked state.
- **`ship_create_pr` false positive — live repro, filed as #57.** With gh failing unauthenticated, the tool returned `shipped: true` with an **empty URL** (`create_pr.execute()` reads only stdout; gh's non-zero exit code and stderr are never checked). Detected because the empty URL was treated as failure evidence; branch/PR absence proven via `ls-remote` + the public API. Filed as follow-up #57 (user-approved hatch candidate — vendored tooling, out of a port-pure PR's scope per `AGENTS.md` fork/port rules). After auth, the real call succeeded and was re-verified independently.
- **Transport fallback.** The plugin tool channel lost the result envelope twice on `ship_create_pr`; the repo's established bun shell transport (2026-11-03 session log precedent — same gate, same validation, stdout result channel) was used once during the outage, then the tool channel worked once credentials existed.
- **Measurement ledger N/A**: `scripts/agentic/ledger.ts` does not exist in this repo's vendored shipflow — markers and the ledger table omitted per best-effort policy (same as the 2026-11-03 run).

## Artifacts

- PR: https://github.com/L-Sypniewski/monogameSettlers/pull/56 (draft → ready via gated finalize; autoMerge off by repo config)
- Follow-up issues: #57 — shipflow `ship_create_pr` reports success when `gh pr create` fails (bug, S, user-approved; the org-standard `ship-ready` label does not exist in this repo, so a provenance comment on the issue is the readiness record)
- Wiki: none (pure engineering run)
- Measurement ledger: not present in this repo's vendored shipflow (no `scripts/agentic/ledger.ts`) — markers omitted per best-effort policy.
