---
description: Dispatch selected backlog issue(s) through the ship pipeline (shortlist, ask, launch, supervise, settle) - works with or without herdr
---

Dispatch backlog issues through the full `@ship` pipeline, one worktree per
issue. Parse `$ARGUMENTS` as ONE whole string - never as per-word
positionals, which truncate a multi-word steer or a space-separated issue
list (`/ship-next 1391,1392 [opencode|claude]` or
`/ship-next <steer text> [opencode|claude]`). Leading numeric/comma tokens
are explicit issue numbers, a trailing `opencode` or `claude` token is the
agent runtime (passed through as `runtime` to `dispatch_issue`), and the
remainder is the selection steer, verbatim. Full policy:
`docs/architecture/backlog-dispatch.md`.

## Preconditions

No herdr requirement: run from anywhere with `git` + `gh` (plus an agent
runtime for the launch step - opencode by default, claude as the explicit
choice or the automatic fallback when opencode is unreachable). Dispatch
always creates a fresh worktree
for the target issue; herdr availability only changes how the unattended
session launches and is watched:

- herdr route available - herdr workspace worktree, devcontainer +
  `opencode --auto` in the herdr pane (`--agent claude` in the same pane
  for an explicit claude runtime when the vendored skill supports the
  flag; headless bypass when it predates it), supervised via herdr panes;
- no herdr, devcontainer CLI + docker usable - plain git worktree,
  headless `devcontainer up` + `opencode --auto` (or `claude -p "<goal>"`
  when claude is the chosen runtime) as a background process
  (log file + PID recorded), supervised by tailing the log;
- no devcontainer CLI either - plain git worktree, headless bare
  `opencode --auto` (or `claude -p "<goal>"` for an explicit or fallback
  claude runtime) background launch in the worktree dir, supervised by
  tailing the log;
- no agent runtime reachable (neither opencode nor claude) - worktree +
  push + board claim still done; the report names both missing runtimes
  and the exact manual command for each.

Full launch policy: `## Launch policy` in `docs/architecture/backlog-dispatch.md`.
The goal prompt is `Work on issue #N: <title> - <url>`; the repo's
`opencode.jsonc` `default_agent: "ship"` makes the session a `@ship` run in
every mode. A claude runtime runs the same goal prompt under claude code's
own defaults - the `default_agent` wiring is opencode-specific.

## Args

Parse `$ARGUMENTS` (the invocation text after the command name) into three
parts:

- Explicit issue numbers: LEADING tokens that are numeric or
  comma-separated numbers - `1391,1392` and `1391 1392` both select both
  issues (split each token on commas). Any numbers present select the
  explicit flow below.
- Agent runtime (optional): a TRAILING `opencode` or `claude` token, passed
  through as `runtime` to `dispatch_issue`. Only a trailing token counts -
  a non-runtime word, even `claude` mid-sentence, is never a runtime and
  stays steer text.
- Steer: the remainder after the leading numbers and without the trailing
  runtime token, verbatim - multi-word steers survive intact
  (`/ship-next welding diaries` -> steer "welding diaries"). No numbers
  runs the attended flow, steered or unsteered.

Runtime precedence: an explicit runtime beats availability; the default is
opencode; claude is the automatic fallback when opencode is unreachable.

## Attended flow (no leading issue numbers)

1. Call the `ship-next_pick_next` tool - with `steer` when the args left
   steer text - and review the report: every open `ship-ready` issue appears as
   an entry with a decision (`selectable` / `flag-needs-spec` / `skip`),
   its reasons, and its annotations (`priority unset`, `size unset`,
   `blocked-by: #M`).
2. Present ONE `question` tool call - `multiple: true`, one option per
   shortlist entry. Option label format:
   `#N title - P:<priority|unset> S:<size|unset> Epic:<epic|->`, plus the
   entry's annotations in brackets when present
   (`[near-miss: priority unset]`, `[near-miss: size unset]`,
   `[blocked-by: #M]`). The question tool's built-in custom answer lets
   the operator type an issue number outside the shortlist or a steer
   refinement - a refinement restarts at step 1 with the new steer. When
   the report says the steer matched nothing, present that verbatim,
   re-call `pick_next` without `steer`, and offer the plain top-K instead.
3. Pass the selection to `ship-next_dispatch_issue` as
   `{issues: [<numbers>]}` - plus `runtime` when a trailing token named
   one. NEVER
   dispatch without a human selection in this flow. `flag-needs-spec`
   entries are flagged by the tool automatically (`needs-spec` label plus
   one what's-missing comment) - surface them in the summary, don't fix
   them. Conflicts and `reused=yes` worktrees stop there - never force a
   launch.

## Explicit flow (leading issue numbers)

Dispatch directly via `ship-next_dispatch_issue` with `{issues: [N, ...]}`

- plus `runtime` when a trailing token named one. NEVER ask: the invocation
  itself is
  the per-issue go, and no `ship-ready` label is required. The tool still
  re-runs the gates per issue on live board state (Status Todo/Next, the
  open-PR / branch / reused-worktree hard guards, the intake gate);
  `needs-spec` flag failures surface verbatim.

## After launch

4. After launch, verify each session actually landed in a `ship` run -
   herdr mode: check the launched pane; headless modes: check the log
   (`tail -f <log>` with the log path from the dispatch report). If it
   did not, send it the goal text `@ship Work on issue #N: <title> - <url>`.
5. Supervise per mode - herdr mode via `herdr agent wait` /
   `herdr agent read` (a `blocked` state means an ask-gate: surface it to
   the human, NEVER auto-answer it); headless modes by tailing the log. A
   herdr workspace can be opened around the worktree later if herdr
   becomes available.
6. When a run settles, call `ship-next_settle_run` with `{issue: N, branch}`
   once per dispatched issue - it posts (or verifies) the draft-PR
   link-back comment, or rolls board Status back to Todo when no open PR
   exists (the assignee stays).
7. Summarize per issue: issue, branch, PR URL (or rollback), frontier state.

## Empty frontier

When the report says `frontier empty`, print it verbatim and stop - no open
issue currently has a selectable entry. The report's skip reasons (status,
referencing PRs, existing branches) are the human-triage surface;
`flag-needs-spec` entries may carry unset-field annotations
(`priority unset`, `size unset`). This is distinct from a steer matching
nothing: an empty-match shortlist says so in the report - present that
verbatim and offer the plain top-K instead.
