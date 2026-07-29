# Task 001 — Legal-Numbering Corpus Deviations / Notes

> Written 2026-07-28 by the task 001 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

All five legal-numbering exemplars named in spec §Dependencies / design §5 / the task POML were authored as
**synthetic** `.docx` fixtures and land under `tests/fixtures/compose-corpus/`:

1. `nda-interrupted-clauses.docx`
2. `heading-style-numbering.docx`
3. `multilevel-1-1-1.docx`
4. `symbol-section-mark.docx`
5. `line-numbered-pleading.docx`

No deviation from the escalation trigger fired: all five staged as Git-LFS pointers (`git lfs ls-files` /
`git show :<path>` confirmed, `version https://git-lfs.github.com/spec/v1` pointer text, not raw bytes).

## Build approach

Per the task's RECOMMENDED approach, each `.docx` was authored by writing raw OOXML parts (`[Content_Types].xml`,
`_rels/.rels`, `word/document.xml`, `word/_rels/document.xml.rels`, `word/numbering.xml`, `word/styles.xml`,
`word/settings.xml`, `docProps/core.xml`, `docProps/app.xml`) directly into a `zipfile.ZipFile`, rather than via
`python-docx` — this gave exact control over the multi-level/style-linked/`w:sym`/`w:lnNumType` constructs the
task requires, which `python-docx` does not model faithfully. Build script + a standalone verification script
(zip integrity + XML well-formedness + required-part presence + construct-marker counts) were run in the
session scratchpad, not committed to the repo (ephemeral tooling, not a project artifact).

## Verification performed

- `zipfile.testzip()` on each `.docx` — no corrupt entries.
- Every `.xml`/`.rels` part parsed via `xml.etree.ElementTree.fromstring` — all well-formed.
- Required OOXML parts present in all five packages (`[Content_Types].xml`, `_rels/.rels`, `word/document.xml`,
  `word/_rels/document.xml.rels`, `word/numbering.xml`, `word/styles.xml`, `word/settings.xml`).
- Construct-presence spot checks: `nda-interrupted-clauses.docx` has 6 `w:numPr` (the 6 clause paragraphs) +
  1 `w:tbl` (the interrupting table); `heading-style-numbering.docx` has **0** `w:numPr` in `document.xml`
  body (by design — the numbering is style-linked, defined in `word/styles.xml`, confirmed present there:
  2 `w:numPr` blocks, one per heading style) + `w:pStyle` references only; `multilevel-1-1-1.docx` has 7
  `w:numPr` across `ilvl` 0/1/2; `symbol-section-mark.docx` has 2 `w:sym` runs + 2 `w:numPr` (Wingdings bullet
  paragraphs); `line-numbered-pleading.docx` has `w:lnNumType` present in `sectPr` + 12 `w:numPr` (the pleading's
  numbered paragraphs).
- `git check-attr filter` confirmed `*.docx filter=lfs` applies to the corpus path BEFORE staging (Step 1).
- `git add` (staged only, no commit per dispatch instructions) + `git lfs ls-files` confirmed all five land as
  LFS pointers; `git show :<path>` spot-checked `nda-interrupted-clauses.docx` shows the LFS pointer text
  (`version https://git-lfs.github.com/spec/v1`, `oid sha256:...`, `size 4380`), not raw zip bytes.

## Golden numbering labels

Recorded per-doc in `tests/fixtures/compose-corpus/corpus-manifest.md` §1.5. Computed by hand-simulating Word's
numbering algorithm (single document-order walk, one counter per `(abstractNumId, level)`, honoring
`w:start`/`w:lvlText`/`w:numFmt`, resetting lower-level counters on higher-level increment) directly against the
`numbering.xml`/`document.xml` this task authored — i.e. derived from the OOXML source of truth the author
controls, not narrative assumption, per the NFR-02 constraint. Headline values:

- Row 9 (NDA interrupted): clauses continue **1.→6.** across the heading/body/table interruption (the
  "restarts at 1" defect's correct-answer sequence).
- Row 10 (heading-style): **"4.2 Confidentiality"** — the literal FR-12 acceptance example, produced via
  style-linked `w:numPr` (zero direct `w:numPr` in the document body).
- Row 11 (multi-level): **1. / 1.1. / 1.1.1. / 1.1.2. / 1.2. / 2. / 2.1.**
- Row 12 (symbol): section-mark paragraphs render **"§ 2.01 …"** / **"§ 2.02 …"** (§ = U+00A7, the correct
  Symbol-font `F0A7` mapping); the Wingdings-bullet paragraphs deliberately have **no golden Unicode value** —
  this is the negative/unmapped case FR-06 must warn-and-placeholder on, not an omission.
- Row 13 (line-numbered pleading): paragraph numbers continue **1.→12.** across 4 section headings (same
  construct as row 9, in pleading form); **no golden line-number value is recorded** — per design §5.5 page/line
  numbers are a rendering-time layout artifact, not derivable from OOXML alone, and this task does not fabricate
  one (WS-5/task 050's job).

## Deviations from the task's literal file list

None — all five named exemplars were authored exactly as specified, no substitutions.

## Placeholder-row interaction (§2 of the manifest)

The pre-existing R4 manifest §2 placeholder row 7 ("owner-supplied — multi-level numbered document") remains
OPEN — the new synthetic `multilevel-1-1-1.docx` (row 11) does not close it, since the task's constraint is
"do NOT fabricate owner documents." A note was added directly under §2's intro clarifying this so a future
reader doesn't mistake row 11 as having satisfied the owner-intake ask for row 7.

## Not done in this task (explicitly out of scope)

- No test code was written (no `Mock<HttpMessageHandler>` / DI-registration / ctor-null test — ADR-038
  constraint honored by not touching `tests/**/*.cs` at all).
- No golden-value harness assertions were wired up — that is task 002's job; this task only supplies the
  corpus + manifest evidence base task 002 consumes.
- `TASK-INDEX.md` and `current-task.md` were NOT modified — sub-agent write boundary; the main session updates
  task 001 to ✅ and advances `current-task.md`.
