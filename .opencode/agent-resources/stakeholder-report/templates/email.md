# Stakeholder Email Format

The email companion is a plain-text file the user can select-all and paste directly
into a mail client's compose window (e.g. Thunderbird). It is derived from the same
Markdown report but is NOT the Markdown report reformatted - it is a much shorter,
one-bullet-per-task summary with the PDF as the pointer to detail.

## Skeleton

```text
Cześć,

Krótkie podsumowanie tego, co zrobiłem od ostatniego raportu:

- <Polish task name, condensed to one clause> – <one concise, non-technical
  sentence: what changed from the stakeholder's point of view>.
- <next task> – <one sentence>.

Pełny raport ze szczegółami i zrzutami ekranu wysyłam osobno (PDF).

Pozdrawiam
```

## Rules

- **Zero Markdown syntax** - no `**bold**`, no `##` headings, no `[text](url)`
  links, no backticks. Plain dashes (`-`) for the top-level bullet list; if a
  bullet needs a nested list, indent it and use `•`, never `-` (keeps the two
  levels visually distinct in a plain-text render). This file is pasted verbatim
  into a mail compose window - any leftover Markdown character shows up literally
  in the sent message.
- **First-person singular voice** ("zrobiłem", "sprawdziłem", "dodałem") - this is
  one person reporting their own work, never "zrobiliśmy"/"zespół" unless the
  invoking prompt explicitly says the work involved other people.
- **One bullet per task**, in the same order as the Markdown report's `##`
  sections, condensed from that section's 2-4 sentence description down to a
  single sentence - drop implementation framing, keep only the visible outcome.
  A task hidden or dropped from the Markdown report (see the main agent's step 5)
  gets no bullet here either - the two files must stay in sync.
- **No greeting name and no signature name** by default - `Cześć,` and
  `Pozdrawiam` alone, unless the invoking prompt names the recipient or the
  sender explicitly (then use those names). Do not guess a name from git config
  or prior conversation context.
- **No subject line inside the file** - the subject goes in the mail client's own
  subject field; do not prepend a `Temat:` line to the body text.
- Point to the PDF once, near the end, as a separate deliverable ("wysyłam osobno")
  - never re-describe screenshots in the email; that is the PDF's job.
- Keep it short - a stakeholder should be able to read the whole email in under a
  minute. If the invoking prompt or a follow-up explicitly asks to elaborate on one
  topic (e.g. "explain how the new role works in the UI"), add that as its own
  plain-text section (a short, capitalized header line instead of a `##` heading,
  then bullets) - but only on request, never by default for every task.
