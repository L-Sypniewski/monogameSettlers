# Fix-Now Policy

Constituents carry operational one-liners + pointers; this file is the
authority - change both together.

Every review surface resolves its non-blocking findings before the run ends.
This rule binds ship's in-loop fix-apply (in-branch auto-resolve) and steps 5.1/5.2 (PR
checklist + follow-up issues), standalone `code-review` Step 7, standalone
`software-engineer` runs in both modes, and `plan-orchestrator` execution -
pipeline and standalone invocations alike.

## Fix-now default

Every non-blocking finding - `Could`-class findings and review observations -
is fixed in the same working set before the run ends. In the ship
pipeline they ride along with the blocking findings in each loop iteration's
single fix-apply pass (one confirm-sweep), plus one final fix-apply pass if
only they remain - never a dedicated pass. Reports, PR checklists,
and run summaries end with zero undecided items: each item carries exactly one
disposition - fixed-inline, follow-up issue (user-approved), or
declined-with-reason. Nothing is silently parked; nothing is deferred by
omission. A report written inside the ship pipeline is the exception in form
only: code-review writes it before any disposition exists, so it carries a
single line noting that the pipeline owns dispositions (loop fix-apply / 5.2 / the
session-log audit) instead of per-item rows. The fix-inline default is cheap
by construction: the code-review `Blocker categories` rule guarantees
residual `Could`-class findings are cosmetic/taste-level only. MoSCoW
classification is untouched by this policy - disposition changes, severity
semantics do not.

## Escape hatch

A non-blocking finding becomes a follow-up issue ONLY when it is genuinely
substantial - fixing it inline would roughly double the PR/changeset - or
truly out of the goal's scope. Hatch items go through the ask-gated tools:
`ship_create_followup_issue` in the ship pipeline, `issue_intake_create_wired`
everywhere else. Approval is per item, or a batch policy the user pre-approves
up front for a run's remaining hatch-eligible items. Follow-up issues are
never auto-created, and no tool-logic changes are involved or permitted to
implement this rule.

Hatch eligibility is evaluated at finding triage - ship's loop intake, or the
standalone disposition step - before fix attempts. An item that would double
the PR/changeset is identifiable up front; classifying only after a reverted fix
attempt mislabels oversized items as unfixable.

## Pipeline last resort

The PR's non-blocking considerations checklist carries ONLY mechanically
unfixable items: fixes that still break tests after genuine fix attempts, or
items blocked by the environment. Hatch-eligible items never land there -
they route to gated follow-up issues.

## Pipeline vs standalone

In the ship pipeline, ship declines code-review's Step 7 disposition handoff:
ship owns the fix loop. In a standalone review, Step 7 IS the disposition
step. This split prevents double-fixing. A code-review spawned by a standalone
`software-engineer` follows the standalone arm and owns its Step 7
disposition; the spawner does not re-dispose those findings. If the nested
review's user gate cannot surface, the nested review returns its findings
undecided and the outer run records their dispositions in its exit summary.

## Plan Non-goals are not a parking lot

A plan's Non-goals section is synthesis-time scoping, decided before execution
starts; execution-time discoveries never extend it. An in-scope discovery is
fixed via a worker Task in the same working set. A substantial or
out-of-scope discovery goes to the user gate: fix inline, gated follow-up
issue, or decline.

`Won't` findings (real but out-of-scope) follow the escape hatch: a
user-gated follow-up issue, never silently dropped.
