# Session decision log — issue #57: ship_create_pr false-positive fix (2026-11-05)

## Goal

User: "do it" — after the #25 tidy-ups, the one open loose end was #57 (the shipflow `ship_create_pr` tool reported `shipped: true` with an empty URL when `gh pr create` failed; live repro earlier the same day in a tokenless container). Ship run `issue-57-createpr-exit-code`, branch cut from latest `origin/main` @ `10d06ec`.

## Pipeline

- **Plan**: `.plans/issue-57-createpr-exit-code.md` (plan-orchestrator, plan-only). Single-phase surgical fix reusing the in-file `spawnText` helper (drains both streams, awaits exit, timeout/abort — the pattern `finalize_pr` already uses), plus a zero-config `bun:test` offline regression guard with a PATH-shimmed fake `gh`. Exhaustive spawn-site audit: `create_pr` was the ONLY exit-code-blind spawn in the package.
- **Implement** @ `7841aca` (fork-pure, 2 files +204/−16): failure returns in the existing gh-error shape with stderr; exit-0-URL-less → defensive failure; success byte-identical; 4 test cases. Verify 4/4 + cli load smoke. Sweep: scope-gate SKIP (`.opencode/**`-only diff — the C# suite imports nothing from it; the bun Verify is the real gate).
- **Review 1**: APPROVE with 3 Should (F-FIX-1 unbounded trace label — the multi-KB PR body landed in host logs and unbounded in `metadata.error` on timeout; F-FIX-2 "no PR created" overconfident for timeout/abort where the POST may have succeeded server-side; F-TEST-1 no abort/timeout regression case) + 1 Could (F-TEST-2 spawn wrapper never restored) + 3 observations (O-1 tmp-leak guard — folded; O-2 CI-wiring = the plan's deliberate fork-purity tradeoff — decided, not deferred; O-3 plan-line citing the disproven env mechanism — historical artifact, corrected in the Loop log).
- **Loop iteration 1** @ `7ca1c6b`: amendment A1-A3 — bounded label (per-element 60-char cap), additive `killed` flag + "PR state unknown, verify before retrying" wording, 5th test case (abort → `shipped:false`), `afterAll` wrapper restore, leak guard. Verify 5/5 + smoke; sweep SKIP again.
- **Re-review 1**: APPROVE — all loop-1 findings verified RESOLVED (label cap empirically confirmed hiding the body; ripple contract deterministically verified: `finalize_pr`/`create_followup_issue`/`cli.ts` byte-identical), 0 new graded findings, 3 no-action observations (R1 witness-race errs conservative; R2 surrogate-split log nit; R3 timeout-flavor test explicitly optional per amendment).
- **Exit**: no fix-apply pass, no exit sweep (scope-gate skip holds for the whole diff). **4c gate**: skills-lock pass; C# suite skip re-stated; probes N/A; nothing regenerates. **4d**: N/A.
- **Ship**: draft **PR #59** via gated `ship_create_pr` (verified: draft, MERGEABLE — fittingly created through the fixed tool, with a real URL), session log committed, ready-flip via gated finalize.

## Review audit summary

Two passes. Pass 1: 0 Must / 3 Should / 0 Could / 0 Won't + 3 obs → all four findings **fixed-in-branch** (`7ca1c6b`, amendment A1-A3); obs O-1 **fixed-in-branch**; O-2 **decided** (CI-wiring rejected for fork purity by the plan itself — a designed tradeoff, not a deferred fix); O-3 **corrected** (plan file's Loop log carries the mechanism correction). Pass 2 (re-review): 0/0/0/0 + 3 obs, all **declined-with-reason** (R1 conservative-direction race is philosophy-conformant "never overclaim"; R2 taste-level UTF-16 surrogate nit in a log-only truncation; R3 explicitly optional per the amendment's own scope). Net: every finding terminal; nothing on a PR checklist; nothing hatch-eligible.

## Key decisions

- **Fix inside `spawnText`, not just `create_pr`** (loop 1): bounding the label at the helper fixes trace, timeout message, and `metadata.error` bound in one change for all callers — with the ripple contract explicitly verified (no importers outside `ship.ts`; `finalize_pr` derives outcomes from exit codes only; its two stderr regexes safe because gh's real stderr stays verbatim after the prefix).
- **Never-overclaim envelope**: timeout/abort kills now say "PR state unknown, verify before retrying" — mirroring `create_followup_issue`'s existing wording — instead of "no PR created". The residual µs-race in the kill discriminator errs toward "unknown" (conservative).
- **Two runtime facts about bun 1.3.14 (linux-arm64), empirically pinned and documented at the seam**: (1) JS-level `process.env` mutations do NOT propagate to `Bun.spawn` children (matches upstream bun PR #34972) — the test delegates `Bun.spawn` with explicit env for its PATH shim to take effect; the plan's original mechanism citation was wrong and the executor's correction was verified correct by review against upstream. (2) `proc.kill()` returns `undefined` — the plan's abort-discriminator sketch was void; implemented as a liveness witness (`proc.exited` race).
- **Test isolation is three layers deep** on purpose: the container HAS live gh auth (`hosts.yml`), so beyond the PATH shim the tests set empty `GH_TOKEN`/`GITHUB_TOKEN`, redirect `GH_CONFIG_DIR` to an empty temp dir, and use a tmp non-git worktree — a bypassed shim cannot create a real PR. Review walked the bypass matrix incl. the delegation seam.
- **Fork purity held as a hard constraint**: the whole diff is 2 files under `.opencode/shipflow/**`; tests are NOT CI-wired because that would breach `.github/**` purity — they run ad hoc and as the plan's Verify (deliberate, recorded as O-2).
- **Measurement ledger N/A** (no `scripts/agentic/ledger.ts` in this repo's vendored shipflow).

## Artifacts

- PR: https://github.com/L-Sypniewski/monogameSettlers/pull/59 (draft → ready via gated finalize; autoMerge off by repo config)
- Closes #57 on merge (issue OPEN at PR creation — closing keyword correct)
- Follow-up issues: none (nothing Won't, nothing hatch-eligible)
- Wiki: none (tooling fix, no domain content)
- Measurement ledger: not present in this repo's vendored shipflow — markers omitted per best-effort policy.
