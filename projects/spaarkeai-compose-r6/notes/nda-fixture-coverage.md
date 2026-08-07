# NDA fixture — empirical OOXML coverage (task 004)

> Source of truth for corpus-manifest.md §1.6 row 14. Derived by unzipping the working-copy bytes of
> `AppligentNDA_Signed.docx` (real content; the committed git blob is an LFS pointer,
> `oid sha256:e94081390378a8fafd708337797ec4e3c2f7da8761eb52461863d44da590e939`, `size 27986`) and grepping
> `word/document.xml` for literal OOXML markers, per the manifest's existing verification-method convention.

## Package parts

No `word/header*.xml` / `word/footer*.xml` parts. No `_xmlsignatures` part (no digital signature). Has
`customXml/item1.xml` — an empty Word bibliography-sources part (`b:Sources`), unrelated to content controls.

## Marker counts (word/document.xml)

| Marker | Count | Notes |
|---|---|---|
| `w:ins` / `w:del` | 0 | No live track changes |
| `w:fldChar` / `w:fldSimple` | 0 | No fields |
| `w:sdt` | 0 | No content controls |
| `w:tbl` | 0 | No tables |
| `w:tab` (`<w:tab/>`) | 10 | Tab stops present |
| `w:numPr` | 10 | All at `numId=1`, `ilvl=0`, `w:numFmt="decimal"` (single-level only) |
| `w:headerReference` / `w:footerReference` | 0 | Confirmed no header/footer parts exist either |
| `w:sectPr` | 3 | Multi-section |
| Self-closing empty `<w:p/>` | 0 | Total `<w:p ` count = 55, all carry runs |
| `mc:AlternateContent` | 7 | Choice/Fallback pairs |
| `w:drawing` (Choice branch) | 7 | 1:1 with AlternateContent blocks |
| `w:pict` (Fallback branch) | 7 | 1:1 with AlternateContent blocks |
| `w:txbxContent` | 12 | Textbox content regions |
| `wps:txbx` (DrawingML textbox) | 6 | |
| `v:textbox` (VML textbox) | 6 | |
| `w14:paraId` total / unique | 55 / 52 | **3 duplicates**: `2BBF07C9`, `2BBF07CA`, `2BBF07CB` — each appears exactly twice |

## Duplicate paraId locations (the 422 root cause)

All 3 duplicated paraIds sit in/around the signature block:

- `2BBF07C9` — `"For:   Appligent,   Inc."` (recital line), appears twice
- `2BBF07CA` — `"signature"` (label), appears twice
- `2BBF07CB` — blank underscore line + `"signature Ralph  Schroeder"`, appears twice

This is consistent with Word assigning the same `paraId` to both the DrawingML (`w:drawing`/`wps:txbx`) Choice
branch and the VML (`w:pict`/`v:textbox`) Fallback branch of the same `mc:AlternateContent` block when the
signature-block content is duplicated across both representations. A naive whole-doc anchor lookup keyed by
`paraId` (or text-search, per `DocxAnnotationWriter.LocateTarget`) collides on these duplicates and cannot
resolve a unique interior target — the interior-location HTTP 422.

## Provenance (explains the AlternateContent-heavy structure)

`docProps/core.xml`: `dc:creator = "Virginia Gavin"`, `dc:title = "AppligentNDA..fm"`.
`docProps/custom.xml`: `Creator = "FrameMaker 5.5.6"`, `Producer = "Acrobat Distiller 4.0 for Macintosh"`.

Likely lineage: FrameMaker → PDF (Distiller) → PDF-to-Word conversion. PDF-to-DOCX converters commonly emit
each fixed-position PDF text run as an anchored textbox (DrawingML + VML fallback pair) rather than flowed
paragraph text — this is the mechanical reason this document, unlike the other corpus rows, exercises the
`mc:AlternateContent`/duplicate-`paraId` defect class.

## Render-on-save implication

The R6 core invariant (save renders from the canonical model; no surgical anchoring/text-search on save)
eliminates the interior-location 422 by construction for this document. On the **load/projection** side, the
render-on-save pipeline must:

1. Flatten each `mc:AlternateContent` pair to a single branch (Choice/DrawingML preferred; Fallback/VML
   discarded) rather than double-emitting both branches' paragraphs into the canonical model.
2. Not assume `w14:paraId` uniqueness for node identity — treat it as advisory/Word-authored metadata, not a
   primary key.

Expected round-trip behavior: **accept + flatten with a user-visible warning; never a 422.** This is the
acceptance target for task 013 (NDA 422 regression test) and the seed input for task 060 (fidelity harness).
