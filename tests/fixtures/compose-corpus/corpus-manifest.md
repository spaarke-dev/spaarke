# R4 Fidelity Corpus Manifest

> **Created**: 2026-07-22 by task 002 (`spaarkeai-compose-r4`)
> **Purpose**: Catalog of the OOXML fixtures in `tests/fixtures/compose-corpus/` — feature coverage per document
> and the known R3 UAT defect(s) each one exercises. Consumed by task 004 (byte-diff harness) and task 006
> (Phase 0 hard-replace gate) as their acceptance evidence base (spec NFR-01, NFR-08).
> **Storage**: All `.docx` files are Git-LFS pointers (`*.docx filter=lfs` in `.gitattributes`). Verify with
> `git lfs ls-files` — do NOT commit a raw binary under this path.

---

## 1. Seed corpus (3 owner sample docs — Phase 0 minimum)

| # | Filename | Track Changes | Fields | SDT / Content Controls | Tables | Tabs | Multi-level Numbering | Headers/Footers | Empty Paragraphs | Multi-Section | Known Defect(s) Exercised |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx` | See note (1a) | Yes — `PAGE` field (`w:fldChar`/`w:instrText`) in `footer2.xml` | Yes — Page-Number building-block SDT wrapping the `PAGE` field in `footer2.xml` | No | No (`w:tab` not present; claim clauses use `w:ind` indentation, not tab stops) | No (`w:numPr` not present — claims are numbered as literal text, e.g. `"1. A computer implemented method..."`, not Word auto-numbering) | Yes — 3 header refs + 3 footer refs (`even`/`default`/`first`); headers are empty, `footer2` carries the page-number SDT, `footer1`/`footer3` empty | See note (1b) | No — single `w:sectPr` (one section) | **THE Phase-0 spike/UAT document.** Interior-location HTTP 422 (`DocxAnnotationWriter.LocateTarget` whole-doc text-search miss) + empty-paragraph drift (legacy mammoth import dropped 9 paragraphs, 48 vs 39) — both per `notes/as-built-inventory.md`. |
| 2 | `Engagement Letter.docx` | No | No | No | No | No (uses `w:br` line breaks for the letterhead block, not tabs) | No (no `w:numPr`; body has no numbered clauses in this saved copy — see note (2a)) | No (no `word/header*.xml` / `word/footer*.xml` parts in the package) | Yes — 1 trailing empty paragraph before `sectPr` | No — single `w:sectPr` | Formatted legal-contract shape (multi-paragraph prose, mid-run line breaks, `lastRenderedPageBreak` hint) — general redline/prose round-trip fidelity, not tied to a specific numbered R3 defect. |
| 3 | `01 - Test Matter Create Fields Only.docx` | No | No — see note (3a) | No — see note (3a) | No | No | No | No (no `word/header*.xml` / `word/footer*.xml` parts) | Yes — 1 trailing empty paragraph (`<w:p .../>` self-closing, no runs) before `sectPr` | No — single `w:sectPr` | Flat single-run paragraphs with mixed `w:rsidR` ownership per run (multi-author-style run splitting) — plain-paragraph round-trip + trailing-empty-paragraph preservation. |

### Verification method
Feature-coverage cells above were derived by unzipping each `.docx` and grepping/parsing `word/document.xml`
(+ `word/header*.xml` / `word/footer*.xml`) for the literal OOXML markers: `w:ins`/`w:del` (track changes),
`w:fldSimple`/`w:fldChar` (fields), `w:sdt` (content controls), `w:tbl` (tables), `w:tab` (tab stops), `w:numPr`
(list numbering), `w:headerReference`/`w:footerReference` (headers/footers), empty-text `<w:p>` blocks (empty
paragraphs), and `w:sectPr` count (sections) — verified 2026-07-22 against the exact bytes now staged in this
directory (byte-identical to `notes/sample-docs/`, confirmed via `diff -q`).

### Notes / discrepancies vs. the task's narrative description
This section exists so downstream harness/gate authors (tasks 004, 006) get ground truth, not marketing copy —
"code (bytes) wins, docs lag" (root CLAUDE.md §2).

- **(1a) Track changes** — the CIPO doc's filename and the project's design docs (`as-built-inventory.md`,
  `spec.md`) describe it as carrying "pre-existing track-changes import." Empirically, the copy now staged has
  **zero** `w:ins`/`w:del`/`w:commentRangeStart` markers in `document.xml` and no `trackChanges` setting in
  `settings.xml` — i.e., this saved copy is track-changes-clean. Two corroborating signals suggest the doc
  *did* pass through a track-changes/comment workflow before being flattened for the sample corpus: `word/people.xml`
  is present (reviewer-identity infra, normally only populated for tracked-changes/comments authorship) and the
  filename literally encodes a Word "Compare vs US12470413" provenance. **Net effect**: this fixture is solid for
  the interior-location-422 (text-search-miss) and empty-paragraph-drift defects (both confirmed applicable per
  `as-built-inventory.md`), but it does **not**, as currently saved, exercise live track-changes import/round-trip.
  If FR-01/NFR-02 acceptance needs a corpus doc with *live* `w:ins`/`w:del` content, that is an open gap — flag to
  the owner alongside the Phase-0 worst-offender ask (see §2 placeholder rows, esp. row for "track-changes-heavy
  redline").
- **(1b) Empty-paragraph drift** — the documented defect (48 vs 39, mammoth `ignoreEmptyParagraphs` dropped 9
  paragraphs) describes a *behavior of the legacy mammoth-based HTML conversion*, not a static count of
  zero-text `<w:p>` elements in the raw XML. The current `document.xml` body has 108 paragraphs, none with
  empty `<w:t>` content — consistent with the defect being about mammoth's whitespace-collapsing heuristic
  during conversion (a runtime behavior), not a property directly countable from the static XML. Retained as
  documented per `as-built-inventory.md` (already mitigated in Phase 1's projection builder; this corpus doc is
  the regression guard).
- **(2a) "Numbered clauses"** — the task prompt's background description characterizes `Engagement Letter.docx`
  as having "numbered clauses." The staged copy's body (12 paragraphs, fully read) has no `w:numPr` and no
  manually-typed clause numbers — it is unnumbered prose. Retained in the corpus as the formatted-letter /
  line-break case regardless; the numbered-clause coverage gap is a candidate for an owner-supplied placeholder
  (see §2).
- **(3a) "Fields / content-controls (SDT) case"** — the task prompt describes this doc as exercising OOXML
  fields/SDT. The staged copy is a flat plain-paragraph document (matter-creation field *values* as prose
  sentences, e.g. "The matter type is Commercial") — it contains **no** `w:fldSimple`/`w:fldChar`/`w:sdt`
  markup. Read literally, "Fields" in this doc's name refers to semantic/business data fields (matter name,
  practice area, attorney, paralegal, parties) extracted from prose text, not Word field codes or content
  controls. The CIPO doc (row 1) is the corpus's actual SDT/field coverage (footer page-number building block).
  This is retained per the task's explicit file list (verbatim copy required), but the OOXML-SDT coverage gap
  this doc was expected to fill is a candidate for an owner-supplied placeholder (see §2).

---

## 2. Owner-supplied worst-offenders (Phase 0 intake — PLACEHOLDER rows)

Per spec Unresolved Question ("Corpus documents — owner to supply the worst-offender set"), the following rows
are **intake slots only**. No owner documents are fabricated here; each row is added to this table (and its
`.docx` copied into this directory as an LFS fixture) once supplied.

| # | Filename | Track Changes | Fields | SDT / Content Controls | Tables | Tabs | Multi-level Numbering | Headers/Footers | Empty Paragraphs | Multi-Section | Known Defect(s) Exercised | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 4 | *(owner-supplied — live track-changes-heavy redline; fills the (1a) gap above — real `w:ins`/`w:del` content, ideally multi-author)* | — | — | — | — | — | — | — | — | — | Track-changes import/placement round-trip (FR-01/NFR-02) | **PLACEHOLDER — not yet supplied** |
| 5 | *(owner-supplied — table-heavy document, incl. nested tables and merged cells)* | — | — | — | — | — | — | — | — | — | Table structural-edit round-trip (FR-05); table-cell paraId resolution | **PLACEHOLDER — not yet supplied** |
| 6 | *(owner-supplied — literal OOXML fields/content-controls document, e.g. mail-merge or form-fillable template)* | — | — | — | — | — | — | — | — | — | Opaque-atom preservation for SDT/fields (FR-02) — fills the (3a) gap above | **PLACEHOLDER — not yet supplied** |
| 7 | *(owner-supplied — multi-level numbered document, e.g. outline-numbered policy or nested defined-terms list)* | — | — | — | — | — | — | — | — | — | Multi-level numbering (`w:numPr`/`w:ilvl`) round-trip | **PLACEHOLDER — not yet supplied** |
| 8 | *(owner-supplied — multi-section document with distinct header/footer per section, incl. page-numbering restarts)* | — | — | — | — | — | — | — | — | — | Multi-section `sectPr` + header/footer round-trip | **PLACEHOLDER — not yet supplied** |

Owner intake process: land the redacted `.docx` under this directory (auto-registers as LFS per
`.gitattributes`), replace the corresponding placeholder row above with verified feature-coverage cells
(same method as §1), and update task 004/006 corpus references if the harness enumerates files by name.

---

## 3. Consumers

| Task | Uses this corpus for |
|---|---|
| 004 — byte-diff harness | Round-trip (load → no-op save) byte-identity verification per NFR-01, across all fixtures in this directory. |
| 005 — applier spike | Operation-schema applier spike against the CIPO doc specifically (row 1). |
| 006 — Phase 0 hard-replace gate | Gate evidence: schema + applier spike (CIPO) + corpus byte-diff harness (all fixtures) green, per NFR-08. |
