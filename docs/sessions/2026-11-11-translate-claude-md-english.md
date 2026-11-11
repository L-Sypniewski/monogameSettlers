# Ship run: translate the German docs to English (CLAUDE.md + docs/) — 2026-11-11

Goal: translate the repo's German agent-instruction file (`CLAUDE.md`, with `AGENTS.md` as
symlink) into idiomatic technical English, solving upstream
[meue/monogameSettlers#15](https://github.com/meue/monogameSettlers/issues/15). Mid-run the
user extended the goal to the remaining German docs and mandated the working method:
**two independent translation sessions (opencode + Claude Code via herdr tabs), each with
deep research, then a merge of both results, then the PR**.

- Plan: `.plans/translate-claude-md-english.md` (untracked, worktree
  `monogameSettlers-translate-claude-md`) · PR: [#66](https://github.com/L-Sypniewski/monogameSettlers/pull/66)
  (draft → ready) · Branch: `translate-claude-md-english` (base `9764ee2` = origin/main tip)

## What shipped (commits, in order)

| Commit | What |
|---|---|
| `e0913a5` | Batch 1 session A: CLAUDE.md translated (9/9 parity markers) |
| `673ecf0` | Batch 1 merge: 22 adoptions from Claude session B (`9cad54d` on `claude-en-claude-md`) |
| `f12dbca` | Review observation OBS-4: "amount step" aligned to `production.json` vocabulary |
| `444796b` | Batch 2 session A': 11 German docs translated (gates ×11) |
| `e3321ab` | Batch 2 merge: ~286 adoptions from Claude session B' (`d125805` on `claude-en-docs`) |
| `44a07bc` | Review fixes: spell names to en.json anchor (Should) + double adverb (Could) |
| `2cba559` | User-directed content fix: PROJEKT.md multiplayer staleness (#9) — the only intentional divergence from the German source |

Final diff: exactly 12 files (CLAUDE.md + 11 docs; `MULTIPLAYER.md`/`VERSIONING.md` verified
already-English, untouched; no file renames — names are link targets).

## Loop & verify

- 2/3 loop iterations used: iteration 1 = user-directed scope extension (docs batch),
  iteration 2 = review findings (1 Should + 1 Could). Loop state in
  `.plans/translate-claude-md-english.loop.json`: `replan_count=2`, `last_verify=pass`.
- Verify per phase: mechanical parity gate (heading levels, link targets, code spans,
  zero umlauts outside the allowlist, German-word scan adjudicated, diff scope, symlink)
  + full side-by-side meaning audits vs the German originals. Docs-only diff at every
  step → diff-scoped sweep = scope-gate skip each time (no suite can exercise a docs
  diff); `check-skills-lock` (the one applicable CI cheap-check) green at final HEAD.

## Review audit summary (durable record; report is local scratch)

Three Path-A iterations over `git diff 9764ee2...HEAD`:
iter 0 (CLAUDE.md only): APPROVE, 0/0/0, 1 Won't (remaining German docs — resolved by
iteration 1's extension), 7 observations (1 fixed as OBS-4; rest informational).
iter 1 (12 files): APPROVE, **1 Should** (F-TACT-1 spell gerunds vs en.json anchor) +
**1 Could** (F-STYLE-1 double adverb) → both fixed inline `44a07bc`.
iter 2 (delta): APPROVE **clean — 0 Must / 0 Should / 0 Could**.
Terminal dispositions: F-TACT-1, F-STYLE-1 fixed-inline; Won't-1 resolved-in-PR;
OBS-1 (PROJEKT.md stale "single player only") → user directed fix-now (`2cba559`),
declined-with-reason = "no follow-up issue, fix now" (user reply).

## Key decisions & rationale

- **Two-session pattern, twice.** Batch 1: opencode session + Claude Code (herdr tab
  `w18`, branch `claude-en-claude-md`). Batch 2 (user extension): same again (tab `w19`,
  branch `claude-en-docs`). Merges were fix-apply (wording-level adoption, ties → A).
  The method earned its cost: each side caught real errors the other made (A': Träger→
  "settler" (should be *carrier*), Takt→"per tick" (should be *per cycle*); B': blanket
  keep-German for UI quotes, Spiel/Partie both collapsed to "match").
- **Claude Code headless failed first** (OAuth expired, no API key). Per user directive
  the canonical session B ran via **herdr `--agent claude`** in a focused tab; the user
  completed `/login` interactively. A substitute opencode second opinion (scratch-only)
  from the failed-launch window was kept as a bonus tiebreaker for batch 1.
- **Terminology authority chain:** translated CLAUDE.md anchor → `data/lang/en.json`
  (the game's own English) → README/CONTRIBUTING house vocabulary → official Settlers III
  English manual/web research. Notable adjudications: dock (`MapObjectKind.Dock=219`),
  professions, match-vs-game per source instance, UI quotes in the game's English,
  hospital vs "Healer", "license" (house Americanism beats British spelling).
- **Fidelity over editorializing:** the communication-in-German rule was translated, not
  changed; stale content (PROJEKT.md multiplayer claim) was carried faithfully first and
  only fixed on explicit user directive, as a separately documented commit.
- **No follow-up issues created:** both hatch candidates (remaining German docs; PROJEKT
  staleness) were resolved inside this PR by user choice.
- **Domain wiki: skipped** — pure documentation/engineering work; no product or domain
  assumption was confirmed, reversed, or deferred by this run.
- **Upstream:** the fork PR carries no closing keyword (cross-repo auto-close is
  impossible); the human opens the upstream PR via `/port-upstream`, which can carry
  `Closes #15`.

## Measurement ledger

Not available for this run: `scripts/agentic/ledger.ts` does not exist in this repo, so
no `run/plan/implement/review/...` markers were recorded (attempted once at run start,
then skipped as best-effort). Per-phase wall-clock and token rollups would have to come
from the opencode session logs.

## Resume/audit pointers

German originals: `git show 9764ee2:<path>`. Full glossary + per-file substitution and
allowlist records: `.plans/translate-claude-md-english.md` (Phase-5 record, Phase-6/7
history, Loop log). Review history: `docs/reviews/2026-11-11-translate-claude-md-english.md`
(local). Claude sessions' own notes: `TERMINOLOGY.md` / `DOCS-TERMINOLOGY.md` (uncommitted)
in their herdr worktrees `~/.herdr/worktrees/monogameSettlers/claude-en-claude-md` and
`.../claude-en-docs`.
