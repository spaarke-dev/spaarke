# R4 / R4.5 Fidelity Corpus Manifest

> **Created**: 2026-07-22 by task 002 (`spaarkeai-compose-r4`)
> **Extended**: 2026-07-28 by task 001 (`spaarkeai-compose-fidelity-r4.5`) — §1.5 legal-numbering exemplars
> **Purpose**: Catalog of the OOXML fixtures in `tests/fixtures/compose-corpus/` — feature coverage per document
> and the known R3 UAT defect(s) / R4.5 numbering defect(s) each one exercises. Consumed by task 004 (R4 byte-diff
> harness), task 006 (R4 Phase 0 hard-replace gate), and task 002 (`spaarkeai-compose-fidelity-r4.5` — the
> text-exactness + numbering-exactness harness) as their acceptance evidence base (R4 spec NFR-01/NFR-08; R4.5
> spec NFR-01/NFR-02).
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

## 1.5. Legal-numbering exemplars (R4.5 additions — task 001)

**All five docs below are SYNTHETIC** — authored for this task by writing raw OOXML parts (`word/document.xml`,
`word/numbering.xml`, `word/styles.xml`) directly into a zip container (no owner-supplied document exists for
these constructs; per the task constraint, no owner document is fabricated — these are clearly labelled
synthetic exemplars, not sourced from a real filing). Each reproduces one specific OOXML numbering construct
named in spec §Dependencies / design §5, and each row records the **golden numbering label(s)** — i.e. the
exact label Word's numbering algorithm computes for that paragraph, per NFR-02 — because the author of the
fixture controls the OOXML numbering model and can compute the label directly from the standard algorithm
(single document-order walk, one counter per `(abstractNumId, level)`, reset-lower-levels-on-higher-increment).
These are the golden values the R4.5 task 002 harness asserts against.

| # | Filename | Feature Coverage | Known Defect Exercised | Golden Numbering Label(s) Word Displays | Status |
|---|---|---|---|---|---|
| 9 | `nda-interrupted-clauses.docx` | Direct `w:numPr` (single `numId`, `ilvl=0`, `w:numFmt="decimal"`, `w:lvlText="%1."`) on 6 clause paragraphs, **interrupted** by a `Heading1`-styled heading, a plain body paragraph, and a `w:tbl` table (all non-list, no `w:numPr`) | "Every clause restarts at 1 on interruption" — the projection defect where a naive `<ol>`-per-contiguous-run reconstruction starts a new `<ol>` after each interruption and restarts the count at 1, instead of replaying Word's single per-`numId` counter across the whole document | Clauses 1–3 (pre-interruption): **"1."** Confidentiality Obligations, **"2."** Term, **"3."** Definitions. [interruption: heading / body / table — none numbered]. Clauses 4–6 (post-interruption, SAME `numId`): **"4."** Remedies, **"5."** Indemnification, **"6."** Miscellaneous. The correct (Word) behavior is the **continuous** sequence 1→6; a defective reader emits 1,2,3 then 1,2,3 again. | Synthetic |
| 10 | `heading-style-numbering.docx` | **Style-linked** `w:numPr` — the `w:numPr` (`ilvl`/`numId`) is defined on the `Heading1`/`Heading2` **paragraph styles** in `word/styles.xml`, not directly on any paragraph; paragraphs reference the style only (`w:pStyle`). Multi-level `abstractNum`: level 0 `w:lvlText="%1"`, level 1 `w:lvlText="%1.%2"` | Dropped heading numbers — a reader that only scans paragraph-level `w:numPr` sees **zero** `w:numPr` elements in `document.xml` (confirmed: `w:numPr` count = 0 in this doc's body) and drops the numbering entirely unless it resolves numbering through the style chain (FR-12) | `Heading1` "Recitals" → **"1"**; `Heading1` "Definitions" → **"2"**; `Heading1` "Term" → **"3"**; `Heading1` "Confidentiality" → **"4"**; `Heading2` "Purpose" (under heading 4) → **"4.1"**; `Heading2` "Confidentiality" (under heading 4) → **"4.2"** — i.e. the doc renders **"4.2 Confidentiality"**, the literal FR-12 acceptance example. | Synthetic |
| 11 | `multilevel-1-1-1.docx` | Single `numId` / `abstractNum` with 3 levels (`ilvl` 0/1/2), `w:lvlText` `"%1."` / `"%1.%2."` / `"%1.%2.%3."`, direct `w:numPr` per paragraph at varying `ilvl` | Multi-level numbering "discarded to a warning count" — exercises `w:lvlText` template composition across levels AND the standard reset-lower-levels-on-higher-increment behavior (level-1/2 counters reset when a new level-0 paragraph appears) | `ilvl0` "Introduction" → **"1."**; `ilvl1` "Background" → **"1.1."**; `ilvl2` "History" → **"1.1.1."**; `ilvl2` "Current State" → **"1.1.2."**; `ilvl1` "Scope" → **"1.2."**; `ilvl0` "Definitions" → **"2."** (level-1/2 counters reset here); `ilvl1` "Key Terms" → **"2.1."** | Synthetic |
| 12 | `symbol-section-mark.docx` | (a) Two paragraphs each with a `w:sym` run (`w:font="Symbol" w:char="F0A7"`) followed by a text run — the Symbol-font PUA code point `F0A7` has a **known** Unicode mapping (§, U+00A7); (b) two bulleted-list paragraphs (`w:numFmt="bullet"`) whose level bullet glyph is defined via `w:rFonts w:ascii="Wingdings"` + a PUA `w:lvlText` char — Wingdings PUA glyphs have **no** canonical Unicode equivalent | Silent `w:sym` drop (FR-06) — a reader that ignores `w:sym` runs entirely loses the section-mark glyph from the visible text; the Wingdings-bullet case is the harder "no mapping exists" branch that MUST warn + placeholder rather than silently drop or mis-render as a random PUA codepoint | Symbol-run paragraphs: **"§  2.01  Confidentiality Obligations. …"** and **"§  2.02  Term. …"** (§ = U+00A7, the correct Unicode target for Symbol-font `F0A7`). Wingdings-bullet paragraphs: bullet glyph has **NO golden Unicode value** — expected disposition is FR-06's "visible placeholder + warning", not a specific character; this is deliberately the negative/unmapped case, not an oversight. | Synthetic |
| 13 | `line-numbered-pleading.docx` | `w:sectPr/w:lnNumType` (`w:countBy="1" w:start="1" w:distance="360" w:restart="newPage"`) enabling Word's rendered line numbering; ALSO carries direct `w:numPr` paragraph numbering (`numId`, decimal, `"%1."`) across 4 headed sections (Parties / Jurisdiction and Venue / Factual Allegations / First Cause of Action) so the doc exercises the SAME interrupted-numbering construct as row 9, in pleading form; 12 numbered paragraphs total | WS-5 (task 050) divergence-measurement input — page/line numbers are a **rendering-time layout artifact**, not derivable from OOXML alone (design §5.5); this fixture supplies `w:lnNumType` + enough prose (12 substantive numbered paragraphs + 4 headings + caption block) to be a meaningful pagination/line-count input once run through a layout engine (LibreOffice-headless / Word-rendering service per WS-5) | Paragraph-number sequence (same "continuous across headings" rule as row 9): PARTIES → **"1."**, **"2."**; JURISDICTION AND VENUE → **"3."**, **"4."**; FACTUAL ALLEGATIONS → **"5."**–**"8."**; FIRST CAUSE OF ACTION → **"9."**–**"12."**. **Line numbers: NO golden value recorded here** — per design §5.5 they are a layout artifact only measurable by rendering; WS-5/task 050 measures actual Word-divergence, this task does not fabricate a page/line "100%" claim (spec MUST NOT rule). | Synthetic |

### Verification method (§1.5)

Golden labels above were derived by hand-simulating Word's numbering algorithm (per design §5, FR-11: single
document-order walk, one counter per `(abstractNumId, level)`, honoring `w:start`/`w:lvlText`/`w:numFmt`,
resetting lower-level counters when a higher level increments) directly against the `numbering.xml` +
`document.xml` this task authored — i.e. computed from the OOXML source of truth, not assumed from narrative,
consistent with the NFR-02 constraint ("capture labels by opening each doc in Word, or by OOXML-derived
computation you verify"). Each `.docx` was round-tripped through `zipfile`/`xml.etree.ElementTree` to confirm
well-formed XML and the presence of the required parts (`[Content_Types].xml`, `_rels/.rels`, `word/document.xml`,
`word/_rels/document.xml.rels`, `word/numbering.xml`, `word/styles.xml`, `word/settings.xml`) before being
zipped as the final `.docx`.

---

## 1.6. R6 render-on-save regression exemplar (task 004)

`AppligentNDA_Signed.docx` was moved here from `projects/spaarkeai-compose-r6/notes/` (task 004) as the seed
regression fixture for the render-on-save re-architecture (spec FR-08): it is the current **interior-location
HTTP 422** hard-fail document, and the R6 core invariant ("save renders from the model — never patches
inherited bytes") is required to eliminate this defect **by construction** rather than by surgical anchoring.

| # | Filename | Track Changes | Fields | SDT / Content Controls | Tables | Tabs | Multi-level Numbering | Headers/Footers | Empty Paragraphs | Multi-Section | Text Boxes / `mc:AlternateContent` | Duplicate `w14:paraId` | Known Defect(s) Exercised |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 14 | `AppligentNDA_Signed.docx` | No — zero `w:ins`/`w:del` in `document.xml` | No — zero `w:fldChar`/`w:fldSimple` | No — zero `w:sdt`; `customXml/item1.xml` present but is only an empty Word bibliography-sources part (`b:Sources`), unrelated to content controls | No — zero `w:tbl` | Yes — 10 `<w:tab/>` stops | No — single-level only: one `numId` (`1`) / `ilvl` (`0`), `w:numFmt="decimal"`, referenced 10×; `numbering.xml` also defines 8 unused `bullet`-format abstractNums (dead numbering defs, not exercised by the body) | No — package has no `word/header*.xml` / `word/footer*.xml` parts and no `w:headerReference`/`w:footerReference` | No — zero self-closing empty `<w:p/>` (55 `<w:p ` total, all carry runs) | Yes — 3 `<w:sectPr>` | **Yes** — 7 `mc:AlternateContent` blocks, each a Choice/Fallback pair (7 `w:drawing` DrawingML Choice branches + 7 `w:pict` VML Fallback branches); 12 `w:txbxContent` regions split across 6 `wps:txbx` (DrawingML textbox) + 6 `v:textbox` (VML textbox) elements | **Yes** — 55 `w14:paraId` attributes total, only 52 unique; 3 values (`2BBF07C9`, `2BBF07CA`, `2BBF07CB`) each occur exactly twice, located in/around the signature block (`"For:   Appligent,   Inc."` recital line and the `"______________ signature Ralph  Schroeder"` signature line) — i.e. the duplicate paraIds sit inside the Choice/Fallback textbox content, consistent with Word assigning the same `paraId` to both branches of an `mc:AlternateContent` pair when it duplicates a paragraph across the DrawingML and VML representations | **Interior-location HTTP 422** — a whole-doc text-search/`paraId`-keyed anchor lookup (`DocxAnnotationWriter.LocateTarget`-style) collides on the duplicate `paraId`s inside the AlternateContent signature-block textboxes and fails to resolve a unique anchor. Render-on-save (this project's core invariant) removes the failure mode by construction: there is no anchor/text-search on the save path, and the load-side projection must flatten each `mc:AlternateContent` pair to a single branch (Choice/DrawingML preferred, Fallback/VML discarded) rather than double-emitting both branches' paragraphs, and must not assume `paraId` uniqueness for node identity. **Expected round-trip behavior: accept + flatten with a user-visible warning; never a 422.** |

### Verification method (§1.6)

Feature-coverage cells above were derived empirically by unzipping the working-copy `.docx` (real bytes staged
before/at the `git mv` in task 004 — the file was already a Git-LFS pointer once committed, `oid
sha256:e94081390378a8fafd708337797ec4e3c2f7da8761eb52461863d44da590e939`, `size 27986`; confirmed via
`git cat-file -p <commit>:<path>`) and grepping `word/document.xml` for the same literal OOXML markers used in
§1/§1.5 (`w:ins`/`w:del`, `w:fldChar`/`w:fldSimple`, `w:sdt`, `w:tbl`, `w:tab`, `w:numPr`/`w:ilvl`/`w:numId`,
`w:headerReference`/`w:footerReference`, `w:sectPr`), PLUS the two markers specific to this document's defect:
`mc:AlternateContent` / `w:txbxContent` / `v:textbox` / `wps:txbx` (text boxes and DrawingML-vs-VML fallback
pairs), and `w14:paraId` duplicate detection (`sort | uniq -c` over all `w14:paraId="..."` attribute values —
55 total, 52 unique, 3 duplicated). The package part listing (`unzip -l`) was also enumerated directly to
confirm the absence of `word/header*.xml`/`word/footer*.xml` parts, ruling out headers/footers rather than
inferring it from the absence of reference elements alone.

### Notes / discrepancies vs. the "Signed" filename and NDA provenance

- **Provenance, not a digital signature.** `docProps/core.xml` records `dc:creator = "Virginia Gavin"`,
  `dc:title = "AppligentNDA..fm"`; `docProps/custom.xml` records `Creator = "FrameMaker 5.5.6"` and
  `Producer = "Acrobat Distiller 4.0 for Macintosh"`. The package contains **no** `_xmlsignatures` part and no
  OOXML digital-signature markup — "Signed" in the filename refers to a scanned/typed signature block
  embedded as ordinary text/textbox content (`"signature Ralph  Schroeder"` literal run text), not a
  cryptographic signature. This is consistent with the doc's likely lineage: FrameMaker → PDF (Distiller) →
  PDF-to-Word conversion, which is the typical origin of the heavy `mc:AlternateContent`
  DrawingML/VML-textbox-per-line-block structure seen here (PDF-to-DOCX converters frequently emit each
  fixed-position PDF text run as an anchored textbox rather than flowed paragraph text) — this is *why* this
  doc, rather than the other corpus rows, is the one that exercises the AlternateContent/duplicate-paraId
  defect class.
- **Multi-level numbering column reads "No" despite `numbering.xml` defining several abstractNums.** Only one
  `numId`/`ilvl` pair (`1`/`0`, decimal) is actually referenced by `document.xml`; the additional bullet-format
  abstractNum definitions in `numbering.xml` are present in the package but unused by any paragraph in this
  saved copy — recorded per the same "code (bytes) wins" convention as §1's notes.

---

## 2. Owner-supplied worst-offenders (Phase 0 intake — PLACEHOLDER rows)

Per spec Unresolved Question ("Corpus documents — owner to supply the worst-offender set"), the following rows
are **intake slots only**. No owner documents are fabricated here; each row is added to this table (and its
`.docx` copied into this directory as an LFS fixture) once supplied.

> **Note on row 7 (added 2026-07-28 by task 001)**: row 7's placeholder is for a real owner-supplied multi-level
> document and remains open — it is NOT closed by §1.5. Row 11 (`multilevel-1-1-1.docx`) is a **synthetic**
> multi-level exemplar (1 / 1.1 / 1.1.1) that gives the R4.5 numbering-exactness harness something to run
> against today; it does not substitute for real owner-authored multi-level content (e.g. an outline-numbered
> policy or nested defined-terms list) if/when the owner supplies one.

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

| Project | Task | Uses this corpus for |
|---|---|---|
| `spaarkeai-compose-r4` | 004 — byte-diff harness | Round-trip (load → no-op save) byte-identity verification per NFR-01, across all fixtures in this directory. |
| `spaarkeai-compose-r4` | 005 — applier spike | Operation-schema applier spike against the CIPO doc specifically (row 1). |
| `spaarkeai-compose-r4` | 006 — Phase 0 hard-replace gate | Gate evidence: schema + applier spike (CIPO) + corpus byte-diff harness (all fixtures) green, per NFR-08. |
| `spaarkeai-compose-fidelity-r4.5` | 002 — text-exactness + numbering-exactness harness | Text-exact (character-for-character run text) + numbering-exact (computed label == golden label in §1.5) assertions across the full corpus, per R4.5 NFR-01/NFR-02; any ❌ is a release blocker (spec Success Criteria 2+3). |
| `spaarkeai-compose-fidelity-r4.5` | 050 — WS-5 page/line spike | `line-numbered-pleading.docx` (row 13) is the ONLY corpus input carrying `w:lnNumType` — used to measure rendered-line divergence against a layout engine (LibreOffice-headless / Word-rendering service), per FR-19. |
| `spaarkeai-compose-r6` | 013 — NDA 422 regression test | `AppligentNDA_Signed.docx` (row 14) is the seed input for the seam/regression test proving render-on-save no longer hits `DocxAnnotationWriter.LocateTarget` interior-location HTTP 422 on this doc (text-box `mc:AlternateContent` + duplicate `w14:paraId`) — handled by construction, not surgical anchoring. |
| `spaarkeai-compose-r6` | 060 — fidelity harness | `AppligentNDA_Signed.docx` (row 14) is a harness seed exercising `mc:AlternateContent` Choice/Fallback textbox flattening + duplicate-`paraId` de-duplication in the render-on-save path. |
