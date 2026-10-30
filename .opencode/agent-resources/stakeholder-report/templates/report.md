# Stakeholder Report Format

The report is written for a non-technical stakeholder: Polish, concise, high-level,
zero implementation detail. Every task is a `##` heading - pandoc/weasyprint turns
each `##` into its own PDF bookmark, which is the whole reason the chapter split
matters here.

## Skeleton

```markdown
# <Polish report title>

<Optional 1-3 sentence intro - ONLY when the invoking prompt supplies context worth
summarizing (e.g. "po wczorajszym demo z ...", "w odpowiedzi na przesłane uwagi ...").
Omit the paragraph entirely when the prompt is just a bare issue/label list - never
pad with a generic filler sentence.>

## <Polish task name> ([#123](https://github.com/L-Sypniewski/monogameSettlers/issues/123))

**Status:** Zrealizowane

<2-4 sentences, concise and non-technical: what changed from the stakeholder's point
of view, derived from the issue body. Never mention file names, code, or internal
architecture.>

**Przed:**
![przed](https://github.com/user-attachments/assets/<uuid>)

**Po:**
![po](https://github.com/user-attachments/assets/<uuid>)

<Optional extra shot - only when the prose calls out a second distinct visible
outcome the pair does not cover (omit entirely otherwise):>

**Po - <krótko, czego dotyczy ten zrzut>:**
![po](https://github.com/user-attachments/assets/<uuid>)

## <Next task> ([#124](https://github.com/L-Sypniewski/monogameSettlers/issues/124))

**Status:** W trakcie realizacji

<description, no screenshots section - omitted entirely when the issue has no
linked PR with images, never a placeholder line>
```

## Rules

- One `##` heading per task - this is the PDF bookmark unit. Never use `#` for a
  task (that level is reserved for the report title) and never merge two tasks
  under one heading.
- Status vocabulary is fixed: GitHub issue `state: closed` → **Zrealizowane**;
  `state: open` → **W trakcie realizacji**. Do not invent other status words.
- Task name and description are translated/authored in Polish by the agent itself
  from the issue's title/body - there is no translation tool to call.
- Screenshots: only the GitHub `user-attachments` URLs found in the related PR's
  body/comments, kept as plain Markdown image links - **never** download or embed
  image bytes into this file.
- Screenshot selection is demonstrative, not positional: use the PR's caption /
  table-header / alt-text context to pick the image(s) that actually SHOW what the
  task's prose describes. An after-only shot that demonstrates the change beats a
  first-in-file pair that does not; when the prose calls out two distinct visible
  outcomes, include both (the pair plus the extra shot). When none exist, omit the
  images entirely - no placeholder.
- Discard baseline-evidence candidates before shortlisting: any image whose PR
  caption context (nearest heading + table/paragraph) marks it as evidence of an
  UNCHANGED surface - "no change", "baseline evidence", "unchanged",
  "pixel-identical", "bez zmian" - shows the stakeholder nothing. PRs routinely
  OPEN with such a pair (an unchanged list view captured for reviewers), so "the
  first before/after table" is exactly the wrong heuristic. This is mechanically
  enforced: the PDF generator hard-fails on any baseline-captioned report image.
- Prefer active-state shots: dropdown open, dialog visible, data populated, the
  feature mid-interaction - over closed/empty-state shots of the same surface.
  The `Po:` shot must visibly show what the prose describes; a shot where the
  only trace of the feature is a placeholder hint does not count.
- Every image that lands in the report is visually verified before writing
  (download to a `/tmp` scratch dir, then have the `image-analyst` leaf view it and
  confirm it depicts the prose) - captions can be stale or ambiguous; pixels
  cannot lie.
- Every image gets a visible caption line directly above it: `**Przed:**` /
  `**Po:**` for pairs (before first, after second; `przed`/`po` alt text kept),
  `**Po - <opis>:**` for after-only or extra shots. Alt text is invisible in the
  rendered PDF - the caption line is the only label the reader sees.
- Link the issue number in the heading itself (`[#123](<issue url>)`), not as
  separate prose.
- Keep each task's prose to 2-4 sentences. This is a status update, not a change
  log - skip anything that reads like a commit message or a code review note.
