# Session decision log — correctness fixes #12, #27, #36 (2026-11-03)

## Goal

Pick up the next low-hanging fruit from the backlog and implement it upstreamable. Operator chose to widen the scope mid-run: all three Size-S bugs of epic #5 in one branch/PR instead of just #27 (the initially picked issue). Worktree `../monogameSettlers-issue-27`, branch `issues-12-27-36-correctness` (the branch predates the widening — name kept, remote stale twin `issue-27-front-garrison-hysteresis` deletion was permission-blocked, harmless: points at main tip).

## Pipeline

- **Plan**: `.plans/correctness-fixes-12-27-36.md` (plan-orchestrator, plan-only; absorbed the earlier per-issue #27 plan `.plans/ai-front-garrison-hysteresis.md` as prior research). 4 phases → 3 fixes, one commit per issue for cherry-pick-friendly upstream porting.
- **Implement** (fresh software-engineer, single delegation incl. sweep): all phases TDD red-first; full sweep `scripts/fast-checks.sh` green **505/505** at cc374db (determinism, lockstep ×6, save/load round-trip included). Commits 083ad87 (#12), bd9e7db (#27), cc374db (#36). Implementation deviation: nested `worker` delegation unavailable (subagent depth limit) → direct edits with TDD evidence kept.
- **Review** (Path A, cumulative `origin/main...HEAD`): iteration 1 → 0 Must · 1 Should · 0 Could · 0 Won't + 4 observations (`docs/reviews/2026-11-03-correctness-fixes-12-27-36.md`).
- **Loop iteration 1**: F-GAME-1 (AI last-garrison teardown constraint undocumented) → plan amendment Phase 5 → c079898 (one German bullet, `docs/KI-TACTICS.md:40-41`; docs-only → sweep scope-gate SKIP). Re-review: **0/0/0/0**, all dispositions verified (`docs/reviews/2026-11-03-correctness-fixes-12-27-36-iter2.md`).
- **Exit**: no fix-apply pass (nothing non-blocking left); no exit sweep (docs-only delta since the full sweep).
- **Ship**: draft PR **#43** via the gated `ship_create_pr` (ELI5 + loop gate passed: replan_count=1, last_verify=pass, sweep checkpoint cc374db full).

## Review audit summary

Iter 1: 0 Must / 1 Should / 0 Could / 0 Won't / 4 observations → Should fixed-in-branch (c079898); obs-1 (stale `_front` ids) declined-with-reason after ship verification (`_buildings` append-only, `WorldEconomy.cs:782` ids = list count, `RemoveBuilding` only flips `Exists` → ids never reused, zero behavior impact); obs-2 (multi-player RNG call count) declined-with-reason (by-design consequence of per-group draws; same-build determinism green); obs-3 (guard over-strict for under-construction garrisons) declined-with-reason (unreachable — selectors are civil-only; errs safe); obs-4 (`_front` frozen during assaults) declined-with-reason (deliberate lazy ternary, `AiPlayer.Military.cs:157`). Iter 2 re-review confirmed all four hold. Net: every finding reached a terminal disposition; nothing deferred.

## Key decisions

- **One PR for three issues** (operator call): per-issue commits keep the upstream port cherry-pickable; review noted `docs/KI-TACTICS.md` is touched by two commits but hunks are ~120 lines apart — all four apply independently, any order.
- **#12 rider shape**: rejected both issue-named shapes on source evidence (force-flag in `RemoveBuilding` = actor-blind across ~10 call sites; `Military.Abandon` runs only after removal) → `TearDown(Building, why)` guard at the AI's three civil teardown seams, prophylactic, no dedicated test (no `InternalsVisibleTo` in src; selectors can't pick occupier-carrying buildings).
- **#36 test scenario redesigned by the planner**: the issue's sketched lottery-weights scenario was non-distinguishing; the landed test uses a deterministic `AllZero`-skip asymmetry (RED confirmed the exact bug: `Expected: 8, Actual: 4`).
- **Transport fallback for pipeline tools**: the shipflow plugin registers its tools `codemode: false`; in this orchestrator session only the ship-next tools were reachable. The loop_* history was recorded by invoking the registered tool code via a bun shell transport, and `ship_create_pr` was invoked the same way — the repo's own established pattern (`cli.ts` header, `finalize-pr.sh`): same gate, same validation, different result channel. No raw `gh pr create` was run.
- **Environment note**: local `dotnet` is 10.0.403 while the repo pins net9.0 (`.mise.toml` 9.0.320) — all suite runs went through `mise exec --`; CI unaffected (setup-dotnet 9.0.x).

## Artifacts

- PR: https://github.com/L-Sypniewski/monogameSettlers/pull/43 (draft → ready via gated finalize; autoMerge off by repo config)
- Follow-up issues: none (no Won't, nothing hatch-eligible)
- Wiki: none (pure engineering run)
- Measurement ledger: not present in this repo's vendored shipflow (no `scripts/agentic/ledger.ts`) — markers omitted per best-effort policy.
