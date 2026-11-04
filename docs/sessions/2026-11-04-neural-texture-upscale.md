# Session log: neural x2 texture upscaling pilot (`neural-texture-upscale`)

**Date**: 2026-11-04 · **Plan**: `.plans/neural-texture-upscale.md` · **Branch**: `neural-texture-upscale` (base `2aae72b` = origin/main) · **PR**: https://github.com/L-Sypniewski/monogameSettlers/pull/44 · **Loop**: 1 replan iteration (cap 3)

## Goal

Opt-in AI/neural x2 sprite upscaling at texture-load time — runtime-only, no original assets touched (conduct), Mac ARM (CoreML) first, cross-platform builds stay green — plus an in-game hotkey to toggle it live. Grew out of a research pass into on-the-fly graphics improvements (pixel-art scalers vs neural; license asymmetry GPL xBRZ vs MIT shaders/BSD models was decisive).

## What ran (pipeline)

1. **Plan** (plan-orchestrator, plan-only): 5 phases, decision gate resolved to "x4 ONNX + Lanczos2-halve to x2" (no trustworthy x2 export exists; the official x2 *is* x4+0.5-resize per upscayl's ncnn graph). Model SHA-256 verified first-party before any commit.
2. **Implement + sweep** (software-engineer, single delegation): 5 commits `895b78c..f799ffd`, phase Verifies green. Sweep: build clean, 530 tests, skills-lock 11/11, osx-arm64 publish smoke (model byte-matched in published dll), real-model CoreML test 1.8 ms/call, end-to-end capture proof (ON vs OFF frames all differ, 261–316 fps).
3. **Review** (code-review Path A): 1 Must (`DrawClipped` 2× footprint — clipped strips/window occupants half-width/mispositioned), 3 Should, 1 Could, 5 observations.
4. **Loop iter 1**: plan amended (Phase 6), fresh software-engineer fixed all six red-first in `31012d5` (clip mapping extracted to pure `ClipMapping`, parity tests pinned to legacy 1× rects; Console.WriteLine fallback/EP notices per `GameAudio.cs:227` convention; Trait-visible skip; `BlockingCollection` blocking take). Sweep re-green (533/533).
5. **Re-review**: **approved**, 0/0/0/0.
6. **PR gate** at final HEAD: skills-lock + publish smoke re-run fresh — all pass, nothing committed.
7. **PR**: draft #44 created via gated `ship-create-pr.sh` (loop gate `may_ship`, ELI5 enforced).

## Review audit summary

Iter 1: 1 Must / 3 Should / 1 Could / 0 Won't + 5 observations → **all 5 findings + O-4 fixed inline** (`31012d5`); re-review verified each fix. O-1/O-2/O-3/O-5/O-6 = documented design choices (decided, not deferred): Dispose-join may abandon worker mid-compile (background thread, harmless); `ResolveResolution.High` unused in prod; failed sequence stays 1× for the session; ≤½px seam rounding in the unclipped path and 1px `hidden`-rounding between scales (O-6 explicitly warns against fixing half the seam). No follow-up issues, no PR-checklist leftovers.

## Key decisions & rationale

- **Model**: `realesr-animevideov3` (BSD-3, 0.6M params) over RRDB x2plus — video-trained (temporal consistency for walking settlers is the top risk for per-frame upscaled animation), 27× smaller, ~1.5 ms/call on CoreML.
- **x4+halve over x2 export**: provenance (pinned HF revision + SHA) beats unvetted x2 artifacts; official x2 == x4 body + 0.5 resize, so halving with the repo's own Lanczos2 kernel is faithful.
- **Hotkey F4**: F1 conventionally help, F2 scaling, F3 perf, F11 fullscreen, F12 capture — F4 next free. Session-only semantics mirroring F11 (no settings.json writes from the hotkey).
- **Dual atlas + mode-aware `SettlerSprites` layers** (not library rebuild): cached `SettlerSprites` refs (`StatusIcons`, `WalkPocGame`) stay correct with zero renderer changes; instant OFF; `Bounds()` stays 1× so UI never shifts. Cost: ~4× VRAM for loaded sprites while ON (accepted; 512px input cap bounds it).
- **GL discipline**: worker thread only computes `uint[]`; every `Texture2D`/`SetData` happens in `Pump` from `MapGame.Update` (per-frame upload cap).
- **CoreML-first kept** even though the probe showed CPU marginally faster at sprite sizes (2.73 vs 2.39 ms — dispatch overhead dominates tiny tensors): difference is noise at these latencies; flip is a trivial follow-up if it ever matters.

## Environment notes

- Local runtime is .NET 10 (mise, no 9.0 runtime) → all test runs used `DOTNET_ROLL_FORWARD=Major`; CI runs 9.0.x. Publish smoke also roll-forward (benign for a smoke).
- Subagent fan-out unavailable inside this pipeline's nested sessions (both engineers implemented directly); code-review ran degraded-mode (inline, no consensus voters) — F-RENDER-1's fix is backed by exact-value red-first tests instead.
- No `scripts/agentic` ledger in this repo → no measurement markers; no ledger table in this log.
- `.plans/` and `docs/reviews/` remain untracked scratch in the worktree (repo convention — the prior run left them untracked too); this log is the durable record.

## Pending (human)

Plan task 5.4 manual visual pass: crispness judgment, Activity-Monitor memory over minutes, mid-walk F4 toggle. Automated capture evidence exists; aesthetics need your eyes.
