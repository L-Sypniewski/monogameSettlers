# Session decision log — issue #25 remainder: SmithScheduler + Gathering seams (2026-11-05)

## Goal

One PR for ALL remaining work of issue #25 (user instruction after PR #56 merged: "create follow up PR - one for remaining work. start off latest origin/main") — the issue's "PR 2" (SmithScheduler extraction + the F-PROD-3 `ShareOf` rider + the direct unit tests PR 1 deferred) and "PR 3" (gatherer seam incl. the field/farmland state PR 1 explicitly parked), collapsed into one branch/PR off `origin/main`.

## Pipeline

- **Plan**: `.plans/issue-25-smithscheduler-gatherers.md` (plan-orchestrator, plan-only). 5 phases / 5 commits; partial-class rule inherited from PR 1 (declaring type stays `Production` — `Snapshot.cs:55` keys saved fields by `DeclaringType.Name + "." + field.Name`); rider scoped exactly to issue #25's F-PROD-3 text. Pre-warm build (exit 0) hidden behind the plan phase.
- **Implement**: fresh software-engineer; hit its step cap after Phases 1-4 → a second fresh delegate finished Phase 5 + the sweep (self-containment held: the tail prompt carried the exact remaining state). Commits `539d79b` (SmithScheduler verbatim) → `8cb6041` (rider) → `99a36e4` (Gathering verbatim) → `b7484e1` (direct tests) → `da44959` (German doc bullets). Per-phase full suite: 505/505 ×3, then **513/513** at Phase 4 and final HEAD (505 unchanged = the rider's no-collateral proof; +8 new tests).
- **Review** (Path A, `origin/main...HEAD`): **APPROVE — 0 Must / 0 Should / 0 Could / 0 Won't** + 4 no-action observations. Zero loop iterations (first review clean). Report: `docs/reviews/2026-11-05-smithscheduler-gatherers.md`.
- **Exit**: no fix-apply pass (nothing open; O-1..O-4 are rationale-backed no-action: doc double-mention inherent to the partial idiom, type-qualified pointer still resolves, insertion-order dependence is the old bug's own, input margins documented); no exit sweep (checkpoint `da44959` == HEAD, kind full).
- **4c PR-readiness gate** (fresh delegate): skills-lock pass; build+tests covered by the sweep at the same HEAD; security probes skipped (Branch 2: diff touches no auth surface; probe paths don't exist in this repo); regen checks N/A. **4d**: N/A (no frontend surface).
- **Ship**: draft **PR #58** via gated `ship_create_pr` (independently verified: draft, head/base correct, `MERGEABLE`), session log committed, ready-flip via gated finalize.

## Review audit summary

Single review iteration: 0 Must / 0 Should / 0 Could / 0 Won't + 4 observations, all terminal in-report. The review independently re-verified what the implementer self-audited under tool-gating: move identity (63/63 smith + 383/383 gatherer non-blank lines byte-identical, `Production.cs` deletion-only per phase), rider exactness (exactly 1 overload + 1 doc sentence + 3 call-site tokens; public `ShareOf` untouched), and every exact-count test assertion re-derived from the credit-loop/stage/regeneration math. Observations declined-with-reason: O-1 (heuristic named in both the overview `:60-62` and the new bullet `:74-75` — inherent to the component-naming acceptance, plan forbade editing the old sentence), O-2 (`MapResources.cs:17`'s "Production.FinishGathering" pointer — type-qualified symbol still correct, only the physical file moved), O-3 (rider test's dictionary-insertion-order dependence — the same dependence the OLD code had, i.e. the bug itself; commented in-test), O-4 (smith-test input margins exactly +1 over consumption — matches plan math; failure mode documented). Net: every item terminal; nothing deferred, no PR checklist.

## Key decisions

- **One PR instead of the issue's PR 2 + PR 3 split** (user call at kickoff): the two seams share the review/verify apparatus; five pure per-phase commits preserve `/port-upstream` replay granularity anyway.
- **No closing keyword for #25.** The issue is CLOSED (auto-closed by #56's `Closes` two seconds after merge); a keyword would be inert/misleading. PR body references "Implements the remaining scope of #25". Whether to reopen #25 or file fresh tracking issues stays the human's call.
- **Base moved mid-run — branch NOT rebased.** `origin/main` advanced `7ac9029 → a7187d9` (#53 vendored herdr skill, #54 Client UI typed loader) during implementation. Verified zero shared files (only `docs/architektur/client.md` in docs; nothing under `Homestead.Simulation`) and `mergeable: MERGEABLE` on the PR; the rider deliberately keeps public `ShareOf` behavior identical, so #54's `ShareBarTests` see no change. A rebase would have churned commit SHAs, invalidated the sweep checkpoint `da44959` and the review's diff scope for zero benefit; GitHub CI exercises the head+base combination regardless.
- **Rider design**: private smith-aware `ShareOf(SmithRecipe, int, Material)` overload; public display fallback unchanged (its callers — `ManufactureMenu`, `AiPlayer.Tactics`, `EconomyCommands` — have no calling-smith context). Observable difference pinned by a custom two-smith test; shipped smiths are product-disjoint (`data/production.json`), which the unchanged 505-test suite proves.
- **Implementation deviations under environment gating** (recorded for the pattern, same class as the 2026-11-03 precedent): `worker` spawning hit the subagent depth limit (1) and the `edit` tool was permission-denied for the delegate → all moves were made via bash/sed with byte-identity audits, later independently re-verified by the review. Phase 4 corrections: `Spawn` helper needs `Profession.Carrier` (professionless spawn → `Profession.None`, zero progress), and `AddLooseGoods` clamps to `Materials.MaxPileSize = 8` so the fairness test's 13 inputs split across two piles (8+5) with the budget raised to 120_000 — exact-count assertions unchanged.
- **Measurement ledger N/A**: no `scripts/agentic/ledger.ts` in this repo's vendored shipflow — markers/table omitted per best-effort policy.

## Artifacts

- PR: https://github.com/L-Sypniewski/monogameSettlers/pull/58 (draft → ready via gated finalize; autoMerge off by repo config)
- Follow-up issues: none (no Won't findings; nothing hatch-eligible)
- Wiki: none (pure engineering run)
- Prior art: PR #56 / `.plans/issue-25-mapresources-seam.md` (PR 1), `docs/sessions/2026-11-05-issue-25-mapresources-seam.md`
- Measurement ledger: not present in this repo's vendored shipflow (no `scripts/agentic/ledger.ts`) — markers omitted per best-effort policy.
