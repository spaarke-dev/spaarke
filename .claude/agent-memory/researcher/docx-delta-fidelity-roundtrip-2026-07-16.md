---
name: docx-delta-fidelity-roundtrip-2026-07-16
description: Compose R2/R3 high-fidelity DOCX round-trip research (Jul 2026) — retained-original+delta model, w14:paraId identity, JS/TS lib landscape+licenses, paragraph-diff→OOXML patch prior art. Updates prior openxml-docx-compose-r2 memo.
metadata:
  type: project
---

# High-fidelity DOCX round-trip: retained-original + delta (2026-07-16)

**Question**: 2026 best practice for high-fidelity .docx round-trip in an AI editor that today imports via mammoth (DOCX→HTML) + exports via docx.js (rebuild from JSON) = LOSSY. Want retained-original + delta (byte-preserve untouched content). Stack: server-side .NET Open XML SDK; editor TipTap/ProseMirror MIT-only. 5 sub-questions: delta architecture, paraId identity, JS/TS lib landscape+licenses, tracked-changes/comments, paragraph-diff→OOXML patch algorithm.

**Findings**:

1. **Retained-original + delta is the right instinct and MORE conservative than any OSS editor does.** Even SuperDoc (the OSS gold standard, AGPL-3.0/commercial, mcp-v0.17.1 2026-07-15) does NOT splice — its super-converter fully parses OOXML→ProseMirror and re-serializes the whole doc on export ("zero conversion" = no HTML intermediate, NOT byte-preservation). Apryse's vendor comparison classifies SuperDoc/Tiptap/Syncfusion(SFDT)/CKEditor/Nutrient all as lossy JSON/HTML converters vs native (Apryse/ONLYOFFICE/LibreOffice). True byte-preserve-untouched = server-side Open XML SDK splice, keyed off a stable paragraph anchor. **Server-side is the right place** (client PM can't model headers/footers/fields/numbering; browser has no full OOXML DOM). Client sends per-paragraph text edits; server splices into retained original.

2. **Paragraph identity = w14:paraId (ST_LongHexNumber, unique per part, 0<x<0x80000000).** paraId assigned on creation + preserved across edits; w14:textId refreshes when paragraph content changes. THE anchor to carry through the TipTap flatten (store paraId as a PM node attr; never render it). PITFALL: Word regenerates ALL paraIds on save when tracked changes/comments are added (Open-XML-SDK issue #925) — so paraId is stable within OUR round-trip but NOT across a Word-for-Web edit session; must re-map after external edits. Open XML SDK has `Paragraph.ParagraphId` typed property but NO built-in unique-paraId generator (issue #962 still open) — generate our own.

3. **JS/TS lib landscape (mid-2026, all versions/licenses verified):** mammoth 1.12.0 MIT — import-only, intentionally lossy, drops headers/footers/fields/numbering (fine as read-only preview, wrong for round-trip). docx (docx.js) ^9.5.0 MIT — build-from-scratch, no import, rebuild = data loss (our current export = the lossy culprit). docx-preview 0.4.0 (docxjs) MIT — render-only OOXML→HTML, no export. Eigenpal @eigenpal/docx-editor Apache-2.0 v1.9.0 ARCHIVED (went dark June 2026; canonical-OOXML+delta model but rebuild-on-serialize) — do not depend. SuperDoc AGPL/commercial — license blocks us unless commercial. **Verdict: no MIT JS lib does high-fidelity round-trip; keep the splice server-side in .NET.**

4. **Tracked changes/comments**: covered in [[openxml-docx-compose-r2-2026-06-29]] — Open XML SDK 3.x first-class w:ins/w:del/w:comment; comments-before-track-changes ordering; Modern Comments 4-part XML. NEW: anchor comments/annotations to paraId not run-offsets (offsets shift on edit).

5. **Paragraph-diff→OOXML patch = partly solved, partly a known-hard problem.** Prior art: **Docxodus 7.1.0 (MIT, released 2026-07-12, .NET 10, also TS/Python/WASM)** — fork of Microsoft's archived Open-Xml-PowerTools, upgraded; WmlComparer compares two DOCX and emits w:ins/w:del revision markup, PLUS Move Detection (MoveGroupId) + Format Change Detection; also ships DocxSession stateful edit API + markdown projection for LLM pipelines. **This supersedes my prior Codeuctivity.OpenXmlPowerTools recommendation** for the redline engine. Approach: don't hand-write a paragraph-text→OOXML differ — build the edited paragraph's OOXML (reusing retained runs where possible) and let WmlComparer/Docxodus synthesize minimal ins/del against the original. Hard edges remain: paragraph-boundary deletes, split/merge, reorder (Move Detection helps), format-only changes (Format Detection helps). Also JSv4/Python-Redlines (wraps same engine), redlines.opensource.legal (browser). Generic emmetio/xml-diff (uses Google diff-match-patch) is NOT OOXML-aware — avoid.

**Recommendation**: Server-side pipeline — retain original DOCX bytes; TipTap carries paraId per node; on save, client sends changed paragraphs (by paraId) + new text; server rebuilds only those paragraphs' OOXML and runs Docxodus/WmlComparer to emit minimal w:ins/w:del; untouched paragraphs byte-preserved. Drop docx.js from the export path. Keep mammoth/docx-preview ONLY as read-only preview if at all.

**Sources**:
- Apryse DOCX Editor SDK comparison — apryse.com/capabilities/docx-editor/comparison (native vs JSON, vendor-biased toward native)
- github.com/JSv4/Docxodus + nuget.org/packages/Docxodus (7.1.0, MIT, .NET 10, WmlComparer+Move/Format detection) — standout find
- github.com/superdoc-dev/superdoc + docs.superdoc.dev (super-converter pipeline, AGPL, mcp-v0.17.1 2026-07-15)
- github.com/OfficeDev/Open-XML-SDK issues #925 (paraId regen), #962 (no generator), #245
- learn.microsoft.com Paragraph.ParagraphId / W14.paraId; MS-DOCX spec
- npm: mammoth 1.12.0, docx ^9.5.0, docx-preview 0.4.0; github.com/mhurhangee/docx-editor (Eigenpal archive, Apache-2.0 v1.9.0)
- ericwhite.com WmlComparer intro; github.com/JSv4/Python-Redlines; redlines.opensource.legal

**Open questions**:
- Docxodus WmlComparer accuracy on real firm-styled legal DOCX vs Word's own Compare — needs a spike (my prior openxml memo flagged same).
- Does Docxodus WmlComparer preserve paraId on unchanged paragraphs, or regenerate like Word does? Unverified — critical for our anchor stability.
- WASM Docxodus in-browser vs server .NET — perf/size tradeoff unmeasured.
- SuperDoc commercial license cost if AGPL is a blocker and we ever want its editor.

**Related**: [[openxml-docx-compose-r2-2026-06-29]] (comments/track-changes + SPE round-trip), [[adeu-architecture-study-2026-06-29]] (LLM reads markup / writes typed edits asymmetry — the diff-avoidance strategy).
