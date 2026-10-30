# Classification Pass Prompt Template

The top agent fills the placeholders and spawns 3 independent subagent calls IN
PARALLEL (one assistant message, three subagent calls, `agent: general`, each
with its own `description` label).
Each pass sees ONLY its own inputs - not the other two passes' reasoning, not
their evidence, not their verdicts.

## Delegation template (fill every placeholder)

```text
Objective: Independently classify EVERY candidate item in the extraction
  file below. You are pass {A|B|C} of 3. You do NOT see the other two
  passes - your job is to do a genuinely fresh, complete verification, not
  a quick sanity check of someone else's work.

Context:
  - Extraction file (read it fully): {EXTRACTION_FILE_PATH}
  - Target repo: {OWNER}/{REPO}
  - Target grouping resolved in Step 0: {GROUPING_DESCRIPTION}

Instructions: READ .agents/skills/transcript-task-sync/references/classification-phase.md
  in full and follow it - including the "Verification completeness" section's
  four traps (existence vs. constraint-match, spot-check vs. category-wide
  proof, capped search vs. absence, partial-scope vs. full-scope CLOSE). Do
  not skip the uncapped-search discipline or the full-DTO-enumeration
  discipline to save time - those are exactly what this pass exists to
  enforce. This repo's `graphify` hook is mandatory before raw grepping.

  For EVERY candidate item in the extraction file, produce a verdict: ADD,
  UPDATE, CLOSE, or SKIP - all four verdict types, not a selective subset.
  Cite verbatim quotes (not paraphrase) for every requirement, per the
  skill's citation requirement.

Output: write your complete verdict table (item, verdict, target issue #,
  full evidence, and for ADD/UPDATE/CLOSE the drafted title/body or closing
  comment) to {OUTPUT_FILE_PATH}. Return ONLY that file path plus a one-line
  summary in your final message - not your raw search/grep transcripts, not
  intermediate reasoning. Keeping the top agent's context clean is the
  entire reason this runs as a subagent instead of inline.

Boundaries: classify the items given. Do not invent new candidate items not
  present in the extraction file - if you notice something the extraction
  missed, note it in your output as a flagged observation, don't silently
  fold it into the verdict table as if Phase 1 had produced it.
```

## After all 3 passes return

Hand off to
`.opencode/agent-resources/transcript-task-sync/rules/reconciliation.md` for
the resolution protocol - do not eyeball the three tables and pick one
without following it.
