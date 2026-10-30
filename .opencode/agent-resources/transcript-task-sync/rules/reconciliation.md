# Reconciliation Protocol

Adapted from this repo's `code-review` agent's consensus protocol
(`.opencode/agent-resources/code-review/rules/consensus.md`), with one
deliberate divergence flagged below. Read that file too if you want the
sibling protocol for context - the mechanics are close enough that it's
worth knowing where and why they differ.

## Scope

Unlike `code-review`'s consensus (selective - only `Must`/high-stakes
findings), this protocol runs on **every candidate item, all four verdict
types** - that scope was an explicit user choice for this agent, trading
cost for coverage. Do not narrow it back to a selective subset on your own
judgment.

## Per-item resolution

For each candidate item, compare the three passes' verdicts (and, for
CLOSE/UPDATE, their target issue numbers):

- **Unanimous** (same verdict, same target where applicable) - adopt as-is.
  High confidence, nothing further needed.
- **2-of-3 agree** - adopt the majority verdict and target. **Do not
  discard the dissent.** Append the minority pass's verdict and evidence as
  a "Dissenting finding" note on the item in the reconciled output - if the
  outlier caught something real (a field the other two missed, a search
  they didn't run, a closed-issue's scope they checked more thoroughly),
  that is exactly the case this whole apparatus exists to surface, not
  paper over.
- **No majority** (three different verdicts, or two passes cite evidence
  that directly contradicts each other even under the same verdict label -
  e.g. two both say CLOSE but cite different, mutually exclusive
  justifications) - **do NOT auto-resolve.** Mark the item for escalation:
  carry all three passes' verdicts and evidence forward into the "needs
  your call" block Step 4 presents to the user. Let them decide.

### Why this diverges from `code-review`'s consensus

`code-review`'s protocol has the top agent resolve a 3-way split itself and
never blocks on the user, because a review finding is advisory text in a
report - a wrong resolution costs a re-read, not an external-system write.
This agent's verdicts, once approved, become `gh issue create/edit/close`
calls - a wrong auto-resolution here is a visible, hard-to-fully-undo write
to a shared board that teammates see. The asymmetry in cost between "wrong
advisory text" and "wrong GitHub write" is the entire reason this protocol
escalates instead of top-resolving. Do not "simplify" this back to
`code-review`'s auto-resolve behavior - it was considered and rejected
deliberately.

## Degraded quorum (a pass fails or returns empty)

- **2 of 3 returned, and they agree** - adopt that verdict, note "2 passes
  returned; 1 failed" in the item's evidence.
- **2 of 3 returned, and they disagree** - treat as a no-majority case
  (escalate), same as a true 3-way split - two passes disagreeing is not a
  majority of anything.
- **Only 1 pass returned** - do not adopt its verdict as if it were
  reconciled. Mark the item `single-pass (degraded quorum)` and carry it to
  Step 4 as its own flagged category, distinct from both the clean
  majority-adopted items and the escalated splits - the user should know
  this one only got the equivalent of the single-pass run that already
  produced wrong verdicts once, not treat it as verified.
- **0 of 3 returned** - do not silently drop the item. Flag it
  "classification unavailable (all passes failed) - needs a manual
  re-check" and carry it to Step 4.

## What this protocol does NOT do

- It does not let a pass introduce new candidate items - each pass
  classifies exactly the items in the shared `extraction.md`, nothing more
  (see the worker prompt template's Boundaries section). An observation a
  pass flagged outside that scope goes into the reconciled output as a
  separate "flagged by pass X, not classified" note, surfaced to the user,
  not silently merged into the verdict table.
- It does not reduce below three passes to save budget on its own judgment
  - see the parent agent's "Scope reminders" section.
