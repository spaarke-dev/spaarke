# Spike S1 — Docxodus `WmlComparer` fidelity + `w14:paraId` preservation

> **Date**: 2026-07-16
> **Question (the gating R3 spike)**: Does Docxodus `WmlComparer` (a) preserve `w14:paraId` on *unchanged* paragraphs, (b) preserve untouched structural parts (headers/footers/numbering/styles/footnotes), (c) synthesize minimal `w:ins`/`w:del` incl. run-format changes — on a firm-styled legal `.docx`? And (S3) what's the publish-size cost?
> **Verdict**: ✅ **PASS** — the E1/E2 core is validated. One refinement (WmlComparer re-serializes, not byte-preserves) + one mitigation (exclude SkiaSharp) fold into the design. **No design pivot required.**
> **Harness**: `scratchpad/s1-spike` — .NET 10 console; `Docxodus` 7.1.0; a programmatically-generated firm-styled fixture (Title + custom `ClauseHeading` style, 2-level clause numbering, header "CONFIDENTIAL — DRAFT", footer w/ PAGE field, a footnote, `w14:paraId` on all 8 paragraphs). Edited variant changes 2 paragraphs' text + adds bold to 1 run.

---

## Environment facts (verified)
- **.NET 10.0.101 SDK installed** (alongside 8.0.423, 9.0.205) → Docxodus 7.1.0 (net10.0) builds + runs.
- **Docxodus 7.1.0 is real on NuGet**, MIT, "Fork of OpenXmlPowerTools upgraded to .NET 10.0." Root namespace is **`Docxodus`** (not `OpenXmlPowerTools`). Types: `Docxodus.WmlComparer`, `Docxodus.WmlComparerSettings`, `Docxodus.WmlDocument`. Also present: `Docxodus.DocxSession`, `Docxodus.Ir.Diff`, `Docxodus.RevisionProcessor`.
- **Dependencies**: `DocumentFormat.OpenXml 3.5.1` (BFF already references 3.4.1 → minor bump), `SkiaSharp 4.148.0` (+ native assets).
- **API used**: `WmlComparer.Compare(new WmlDocument(orig), new WmlDocument(edited), new WmlComparerSettings { AuthorForRevisions = "Spaarke AI" })` → `WmlDocument`; `.SaveAs(path)`. Worked first try.

## Results

### (a) `w14:paraId` preservation — ✅ THE critical risk is retired
All 8 paraIds present in the comparison output; **every unchanged paragraph (101,103,104,106,107) PRESERVED and text-identical.** WmlComparer does **not** drop or regenerate `w14:paraId` on unchanged paragraphs.

> This directly answers the design's flagged "if it regenerates paraIds, we must comparer-on-a-copy + content-map." **We don't** — the paraId anchor survives our own load→edit→compare round-trip. (The separate MS-DOCX caveat — Word regenerates paraIds across an *external* Word edit session — is unchanged and still handled by the retained fuzzy re-anchor fallback.)

### (b) Untouched structural parts — ✅ preserved, ⚠️ re-serialized (not byte-identical)
Comparison output retains **all** parts: `styles, numbering, footnotes, header0, footer0`. Header still contains "CONFIDENTIAL"; styles still contain `ClauseHeading`; numbering still present.

**But `numbering.xml` is NOT byte-identical to the original.** The diff is **purely cosmetic re-serialization**:
- BOM added; `encoding="UTF-8"` → `encoding="utf-8"`; `<w:start w:val="1"/>` → `<w:start w:val="1" />` (space before `/>`).
- Semantically identical: same `abstractNum`, same 2 levels, same `numFmt`/`lvlText`/`ind` values. 607 → 620 bytes, all whitespace/BOM.

**Interpretation**: WmlComparer's *output document* is **structurally/semantically faithful but re-serialized** — it does not byte-preserve untouched content (same as SuperDoc, per the research). This is a **refinement to the design's "byte-identical untouched paragraphs" claim** (see design impact).

### (c) Revision synthesis — ✅ works, incl. format detection
- **3 `w:ins` + 2 `w:del`** for the 3 edited paragraphs (2 text edits + 1 format-only).
- **Format-Change Detection works**: the bold-only change on para 108 produced an `rPr/pPrChange`, not a delete+re-insert. **Supports D4 (text + run-level formatting) directly.**
- **Author attribution works**: `AuthorForRevisions = "Spaarke AI"` surfaced in the output.

### (S3) Publish-size — ✅ mitigated
- `Docxodus.dll` (managed) = **2.44 MB**; `SkiaSharp.dll` (managed) = 0.5 MB; **`libSkiaSharp.dll` (win-x64 native) = 11.6 MB** ← the real risk.
- **WmlComparer runs with SkiaSharp fully removed** (managed + native deleted → identical output). SkiaSharp is only pulled for HTML/image rendering paths (`HtmlToWml`, `FormattingAssembler`), which E1 does not use.
- **Mitigation**: exclude SkiaSharp assets from the BFF publish (`<ExcludeAssets>runtime;native</ExcludeAssets>` on the transitive ref, or a targeted exclusion). **Net publish add ≈ Docxodus 2.44 MB uncompressed (~1 MB compressed)** + the OpenXml 3.4.1→3.5.1 bump (≈0). Comfortably under the 60 MB ceiling / +5 MB single-task trigger. **Verify the exclusion holds when the BFF actually builds (a real task-time check).**

## Design impact (fold into design.md)
1. **E1/E2 core VALIDATED** — hybrid (D1) stands; paraId anchor is sound; Docxodus adoption CONFIRMED (S1 + S3 pass). No pivot.
2. **Refine the fidelity claim** — from "untouched paragraphs byte-identical" to **"structurally/semantically faithful; cosmetically re-serialized."** If saving `WmlComparer`'s output directly (Approach A), untouched content is *semantically* preserved, not byte-identical. **True byte-identity (Approach B)** — splice WmlComparer's `w:ins`/`w:del` back into the retained-original bytes — is an available hardening step, warranted only if a hard requirement appears (e.g. OOXML-level digital signatures, byte-diffing). **Recommend Approach A for the MVP** (re-serialization is cosmetic-lossless).
3. **Record the SkiaSharp exclusion** as a binding BFF-packaging note in §12.
4. **`Docxodus` namespace + API** confirmed for the spec's implementation notes.

## Caveats / follow-ups (not blocking)
- Fixture is a *representative* generated legal doc, not a real 40-page firm template. Before build-freeze, **re-run S1 on 2–3 real firm templates** (nested tables, deep multi-level numbering, cross-references) — WmlComparer has historical edge cases on complex numbering/nested tables (inherited from PowerTools).
- Approach A's re-serialization means a **digitally-signed** source doc would have its signature invalidated on save — flag if any Compose source docs are signed at the package level (unlikely during drafting).
- Docxodus is a young single-maintainer fork — pin the version; keep the Codeuctivity fork as the documented fallback.
