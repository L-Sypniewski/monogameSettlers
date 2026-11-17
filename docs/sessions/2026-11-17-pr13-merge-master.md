# 2026-11-17 — Merge latest upstream/main into port/neural-texture-upscale (upstream PR #13)

**Goal** (user): `https://github.com/meue/monogameSettlers/pull/13` — merge latest master and solve conflicts.
**Plan**: `.plans/pr13-merge-master.md` · **Result**: done — merge commit `6f5eff9` pushed to
`origin/port/neural-texture-upscale` (`59f2363..6f5eff9`); upstream PR #13 now carries 18 commits and its
conflict against `meue:main` is resolved.

## What happened

- Merged `upstream/main` @ `807c6cf` into the PR branch (side panel UI fixes #17, macOS app bundle
  script #18, multiplayer hardening #19). Merge base was `1f8eeb0` (#20 docs→English).
- **2 real conflicts**, resolved per the plan's contracts and proven by two-sided intent diffs
  (`git diff` vs each parent shows only the other side's changes, no line lost):
  - `CLAUDE.md` — union: upstream's new bullets (handler messages → `Lang.T`; determinism rule;
    GUI images via `GfxDirectory`/`SpriteLibrary`) + branch's bullets (pixel-diff capture verification;
    neural upscaling).
  - `docs/architektur/capture.md` — upstream's trimmed env-var block + new `TOOLTIP=`, with the
    branch's `ZOOM=<n>` / `SCALING=` wording re-applied on top.
- Auto-merge seams checked and confirmed clean: `MapGame.cs` (all upstream #17/#19 draw/UI additions
  sit outside `BeginHiResWorldPass`/`EndHiResWorldPass` on the 1x path; set-identity proof vs both
  parents), `SettlerSprites.cs` (`ShadowBounds` reads the always-1x shadow, panel-only consumer),
  `data/lang/{de,en,pl}.json` (branch +9 / upstream +53 keys, intersection ∅, 640 keys per language),
  `docs/architektur/client.md` (disjoint sections).

## Verification

- `dotnet build Homestead.sln` — pass. `dotnet test` full suite — **37/37 Client.Tests + 658/658
  Simulation.Tests, 0 failed** (`RequiresModel` stayed env-gated off). Environment quirk: no .NET 9
  runtime on this machine → tests ran with `DOTNET_ROLL_FORWARD=Major` on 10.0.14; no repo change.
- Review (`docs/reviews/2026-11-17-pr13-merge-master.md`, local scratch): **0 Must / 0 Should /
  0 Could / 0 Won't + 1 observation** — APPROVE; every scoped check rests on deterministic git/JSON
  evidence (independently re-verified the two-sided intent cleanliness, the hi-res-pass seam,
  `ShadowBounds` isolation, key parity, union coherence, and a zero-hit conflict-marker sweep over
  all 76 merge-touched files).

## Key decisions

- **Merged `upstream/main`, not fork `origin/main`** — the fork's main carries 20 fork-only tooling
  commits (shipflow, devcontainer, vendored skills) that must never enter an upstream PR; matches the
  branch's own earlier merge pattern (`139adfc`).
- **No push before review** — the merge was committed locally in the herdr worktree
  (`~/.herdr/worktrees/monogameSettlers/port-neural-texture-upscale`) and pushed only after the
  review approved, so the live upstream PR never saw an unreviewed intermediate state.
- **Run artifacts stayed off the port branch** — session log on fork `main`, review report local
  scratch; upstream-PR hygiene (no fork references in upstream-facing content).
- **OBS-1 declined-with-reason** (recorded, not fixed): upstream #17 deliberately deleted ~20
  capture-variable doc lines (`EVENT`, `BATTLE`, `DOCK`, …) whose supporting code still exists on
  both sides. Re-adding them in this PR would fight upstream's editorial choice; surfaced to the
  human, who may raise it with meue separately.
- No follow-up issues, no new PRs (PR #13 exists; upstream interaction is human-only per CLAUDE.md).

## Process notes (honest deviations)

- The implementer subagent could not fan out workers (subagent depth limit) and had no `edit` tool,
  so the two conflict resolutions were applied by an assertion-guarded script constructing each
  union from the conflict bodies, then proven with the two-sided intent diffs above.
- The review ran inline single-pass (same depth limit) instead of per-category fan-out + consensus;
  acceptable because every check is deterministic git/JSON evidence, no judgment quorum needed.

## Measurement ledger

N/A — `scripts/agentic/ledger.ts` does not exist in this repo's vendored shipflow; no phase markers
emitted, no token/wall-clock rollup (best-effort policy, consistent with all prior runs).
