# Task 002 — Fidelity Corpus Deviations

> **Date**: 2026-07-22
> **Task**: `projects/spaarkeai-compose-r4/tasks/002-fidelity-corpus.poml`
> **Status**: No LFS-tracking surprises (the escalation trigger did NOT fire — all three `.docx` fixtures
> landed as clean LFS pointers on first `git add`, verified via `git lfs ls-files` + `git show :<path>`).
> The deviations below are content-fidelity findings from direct OOXML inspection, recorded per Step 5
> ("Record any deviation... in `projects/spaarkeai-compose-r4/notes/`").

## What was found

While authoring `tests/fixtures/compose-corpus/corpus-manifest.md`, the three sample docs were unzipped and
their `word/document.xml` (+ header/footer parts) inspected directly for OOXML feature markers, rather than
trusting the task prompt's narrative description at face value. Three discrepancies surfaced between the
POML's background description and the actual bytes in the staged copies (byte-identical to
`notes/sample-docs/`, confirmed via `diff -q`):

1. **CIPO patent letter — no live track-changes markup.** The task prompt/spec/`as-built-inventory.md`
   describe this doc as carrying "pre-existing track-changes import." The staged copy has zero `w:ins`/`w:del`
   in `document.xml` and no `trackChanges` setting in `settings.xml` — it is track-changes-clean as currently
   saved. Supporting circumstantial evidence it *did* pass through a tracked-changes/comment workflow before
   being flattened for the sample corpus: `word/people.xml` (reviewer-identity infra) is present, and the
   filename encodes a Word "Compare vs US12470413" provenance. The doc IS confirmed to carry: a `PAGE` field +
   page-number-building-block SDT (in `footer2.xml`), 3 header + 3 footer references, single section, 108
   body paragraphs (none empty in the raw XML — the documented "48 vs 39" empty-paragraph drift is a behavior
   of the legacy mammoth HTML-conversion heuristic, not a static raw-XML paragraph-emptiness count, so it
   is NOT contradicted by this finding).
2. **Engagement Letter — no numbered clauses.** Described as having "numbered clauses" in the prompt
   background; the staged copy (12 paragraphs, read in full) has no `w:numPr` and no manually-typed clause
   numbers. It is unnumbered prose with `w:br` line breaks for the letterhead block.
3. **"01 - Test Matter Create Fields Only.docx" — no OOXML fields/SDT.** Described as "the fields /
   content-controls (SDT) case." The staged copy is 8 flat plain-paragraph runs describing matter-creation
   field *values* as prose sentences (e.g. "The matter type is Commercial") — zero `w:fldSimple`/`w:fldChar`/
   `w:sdt` markup. "Fields" here reads as business/semantic data fields, not Word field codes.

## Disposition

All three files were still copied verbatim per the task's explicit instruction (constraint: "MUST be copied
verbatim... do NOT fabricate"). The manifest (`tests/fixtures/compose-corpus/corpus-manifest.md`) documents
both the task's narrative framing AND the empirically-verified feature coverage side by side, with an explicit
"Notes / discrepancies" section, so downstream consumers (task 004 byte-diff harness, task 006 Phase 0 gate)
work from ground truth rather than an unverified description.

## Follow-up recommendation for Phase 0 owner intake

The manifest's §2 placeholder table already carries 5 generic worst-offender intake slots. Two of those slots
now double as fill-ins for the gaps this finding surfaces:
- **Row 4** (live track-changes-heavy redline) — needed because the CIPO doc, despite being THE Phase-0
  spike/UAT doc, does not itself carry live `w:ins`/`w:del` content.
- **Row 6** (literal OOXML fields/content-controls document) — needed because doc #3 does not carry `w:sdt`/
  `w:fldSimple` despite its filename; the CIPO doc's footer page-number SDT is the corpus's only current SDT
  coverage.

No action required to unblock tasks 004/005 — the CIPO doc alone is sufficient to stand up the harness and
applier spike per the task's own notes ("Blocks the final NFR-01 acceptance bar, NOT Phase-0 start"). Flagging
for the owner's Phase-0 worst-offender supply pass.
