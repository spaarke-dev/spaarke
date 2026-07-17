# R3 Research — TipTap / OOXML Fidelity / AI Authoring Best Practices (as of July 2026)

> **Created**: 2026-07-16
> **Method**: three parallel `researcher` subagent threads (editor ecosystem + licensing · DOCX fidelity round-trip · AI authoring + track-changes UX), primary-source-cited (GitHub API, npm, vendor docs, MS-DOCX spec, HCI papers).
> **Goal it serves**: a CREDIBLE AI authoring surface with HIGH Word fidelity + expected core editing tools, **without recreating Word**, on the **MIT TipTap base** (no TipTap product features, paid or unpaid).
> **Feeds**: `design.md` (E1/E2/E3 + toolset boundary + spike plan).

---

## TL;DR — what the research changes in our design

1. **Our architecture is sound and current, not dated.** "MIT TipTap base + our own ProseMirror plugins" is the 2026 consensus for a serious document editor (TipTap v3.28.0, 2026-07-15, MIT). Keep it.
2. **Our home-grown track-changes/comments marks are the right call.** Every independent OSS suggestion lib is a young 0.x, sub-100★ project; TipTap's TrackChanges/Comments/Conversion are all **paid**. Don't rip out working OOXML-mapped code.
3. **NEW LIBRARY — `Docxodus` 7.1.0 (MIT, .NET 10, 2026-07-12)** largely solves E1's hardest sub-problem. Its `WmlComparer` emits minimal `w:ins`/`w:del` between two DOCX with **Move Detection + Format-Change Detection**. We do NOT hand-write a paragraph-diff→OOXML patch algorithm; we rebuild edited paragraphs and let WmlComparer synthesize the revision markup. Supersedes the Codeuctivity PowerTools fork.
4. **Drop `docx.js` from the export path entirely.** No MIT JS library does high-fidelity round-trip. All fidelity-preserving OOXML work is **server-side .NET** (Open XML SDK + Docxodus). JS is for editing UX only (+ optional `docx-preview` read-only preview).
5. **Our splice model is MORE conservative than even SuperDoc** (the OSS gold standard), which re-serializes the whole doc rather than byte-preserving untouched content. Byte-preservation of untouched paragraphs is only achievable via a server-side Open XML splice keyed on `w14:paraId` — our .NET stack.
6. **`w14:paraId` is the right E2 anchor** — but with a critical caveat: **Word regenerates all paraIds when tracked changes/comments are added**, so paraId is stable *within our round-trip* but NOT across a Word-for-Web/desktop edit session. Our existing fuzzy anchor (`textPattern` + Levenshtein in `AnnotationReanchorService`) is NOT thrown away — it becomes the cross-Word-session re-anchor fallback.
7. **E3 confidence needs a redesign.** 2025–26 HCI research shows numeric confidence scores drive *over-reliance* and false precision. Lead with cited rationale; if we show confidence, use **coarse qualitative bands tied to grounding/verifiability**, not a 0–100 model self-report; engineer *against* rubber-stamping.
8. **"Open in Word" is a validated architecture, not a shortcoming.** Harvey and Spellbook ride *native* Word track changes rather than build a bespoke web redline surface. Reinforces our "without recreating Word" line — defer heavy features (pagination, footnote numbering, complex numbering, TOC) to Open-in-Word.

---

## Thread A — TipTap ecosystem + licensing (mid-2026)

- **TipTap `@tiptap/core` v3.28.0, 2026-07-15, MIT** (GitHub API verified). ~9M downloads/mo. v3.0 stable since 2025-07-15.
- **June 2025: TipTap open-sourced 10 formerly-Pro extensions under MIT** — Details, Emoji, **DragHandle**, FileHandler, InvisibleCharacters, **Mathematics**, **TableOfContents**, **UniqueID**. These are NOT on our forbidden list — fair game. **`UniqueID` is directly relevant to E2** (stable per-node ids). *Verify each package's npm scope resolves to MIT `@tiptap/extension-*` (not `@tiptap-pro/*`) before adding.*
- **Paid product tiers (NOT MIT, must build ourselves / server-side):** Track Changes/Suggestions, Comments, Real-time Collaboration (Hocuspocus), **DOCX/PDF Import-Export ("Conversion")**, Content AI, Pages, Version History. All four capabilities we care about (track changes, comments, collaboration, DOCX conversion) are paid — confirms the whole R3 build-it-ourselves posture.
- **Architecture consensus 2026**: TipTap-on-ProseMirror = default for document-grade editors. Lexical only wins on extreme performance/mobile; Slate for fully custom models. Because TipTap extensions *are* ProseMirror plugins, independent MIT PM plugins drop in directly and we can reach raw PM APIs (decorations/steps/schema) for the track-changes layer.
- **SuperDoc** (`Harbour-Enterprises/SuperDoc`, **AGPL-3.0 / commercial dual**) — strongest public proof high DOCX fidelity is achievable on ProseMirror; borrow the **OOXML-as-source-of-truth, editor-as-projection** pattern (= our retained-original model), **never the code** (AGPL copyleft).

Sources: GitHub API `ueberdosis/tiptap` releases+license; tiptap.dev/pricing; tiptap.dev/blog "were-open-sourcing-more-of-tiptap" (Jun 2025); GitHub `Harbour-Enterprises/SuperDoc` license.

## Thread B — DOCX fidelity round-trip (mid-2026)

**Landscape splits into native-OOXML editors (Apryse/ONLYOFFICE/LibreOffice — commercial or AGPL) vs JSON/HTML converters (CKEditor, Syncfusion, TipTap, SuperDoc).** SuperDoc is in the *converter* camp — high *structural* fidelity but re-serializes the whole document; does not byte-preserve untouched runs. **True byte-preservation = server-side Open XML SDK splice keyed on `w14:paraId`.**

**Library table (versions + licenses verified):**

| Library | Version | License | Role | Round-trip verdict |
|---|---|---|---|---|
| mammoth | 1.12.0 | MIT | DOCX→HTML import | Intentionally lossy; OK as read-only preview, wrong for round-trip. (Our current importer — keep for editor projection only.) |
| docx (docx.js) | ^9.5.0 | MIT | Build DOCX from JSON | The lossy culprit in our current export. **DROP from export path.** |
| docx-preview | 0.4.0 | MIT | OOXML→HTML render | Good read-only preview; no export. |
| **SuperDoc** | mcp-v0.17.1 | **AGPL-3.0** | Full OOXML PM editor | Highest OSS fidelity; AGPL blocks embedding. Patterns only. |
| Eigenpal `@eigenpal/docx-editor` | v1.9.0 | Apache-2.0 | Canonical-OOXML PM editor | **ARCHIVED June 2026 — do not adopt.** |
| **Docxodus** | **7.1.0** | **MIT** | .NET Open-Xml-PowerTools fork; `WmlComparer` | **The redline/diff engine.** Move + Format-Change detection. See below. |

**`w14:paraId` (E2 anchor):**
- On `w:p`/`w:tr`; `ST_LongHexNumber`, unique in the part, `0 < x < 0x80000000` (MS-DOCX spec). **`paraId` assigned on creation, preserved across edits; `textId` refreshes on content change** → paraId = stable anchor, textId = content-dirty flag.
- Carry through TipTap as a **hidden ProseMirror node attribute** (never rendered to DOM).
- **Pitfall 1**: Word regenerates *all* paraIds on save when tracked changes/comments added (Open-XML-SDK #925) → stable within our round-trip, NOT across an external Word session → re-map via content hash/fuzzy after external edits (our `AnnotationReanchorService` becomes the fallback).
- **Pitfall 2**: Open XML SDK has `Paragraph.ParagraphId` property but **no unique-paraId generator** (#962 open) → we mint + collision-check our own (random 32-bit < 0x80000000).

**E1 diff→patch algorithm — largely solved by Docxodus/WmlComparer:**
- **Do NOT hand-write "paragraph text → OOXML delta."** Rebuild the edited paragraph's OOXML (reuse retained runs where possible) and let `WmlComparer` synthesize minimal `w:ins`/`w:del` against the original. Matches the **adeu asymmetry**: editor/LLM produces typed text edits; the engine produces valid OOXML — never ask the model/diff layer to emit raw revision XML.
- **WmlComparer adds** (vs vanilla PowerTools): Move Detection (`MoveGroupId` instead of delete+re-insert), Format-Change Detection (bold/italic/font-size-only).
- **Still hard** (WmlComparer mitigates, doesn't fully solve): paragraph-boundary deletes, split/merge, reorder (Move Detection helps reorder).
- Related prior art: JSv4/Python-Redlines, redlines.opensource.legal, Eric White's WmlComparer intro (canonical algorithm writeup). Avoid generic emmetio/xml-diff (not OOXML-revision-aware).

**Recommended architecture (fits our stack):**
1. Retain original DOCX bytes as source of truth.
2. Server stamps every `w:p` with a self-generated `w14:paraId`; TipTap carries it as a hidden PM node attr.
3. On save, client sends changed paragraphs by `paraId` + new text (not the whole doc).
4. Server rebuilds only those paragraphs' OOXML, runs Docxodus/WmlComparer → minimal `w:ins`/`w:del`; **untouched paragraphs byte-preserved.**
5. Drop docx.js from export; keep mammoth/docx-preview as optional read-only preview.

Sources: GitHub `JSv4/Docxodus` + NuGet Docxodus; Open-XML-SDK #925/#962/#245; MS-DOCX spec PDF; Learn `Paragraph.ParagraphId`; GitHub `superdoc-dev/superdoc` + docs.superdoc.dev; Apryse DOCX-editor comparison (vendor, directional); npm mammoth/docx/docx-preview; Eric White WmlComparer blog.

**⚠️ Highest-priority open question (spike):** does Docxodus `WmlComparer` **preserve `w14:paraId` on unchanged paragraphs, or regenerate them like Word?** If it regenerates, run the comparer on a copy and map results back by content. Unverified.

## Thread C — AI authoring + track-changes UX (2026)

- **Keep our marks.** Independent OSS suggestion libs are young: `@handlewithcare/prosemirror-suggest-changes` (MIT, v0.1.8 Nov 2025, ~60★), `davefowler/prosemirror-suggestion-mode` (MIT, ~45★, no npm release), `prosemirror-changeset` (MIT primitive), `prosemirror-suggestcat-plugin` (AI-native but historically coupled to a hosted API — verify). Mine **davefowler's `applySuggestion` text-match helper** as the reference for our AI-apply path (LLM `target_text`/`new_text` → suggestion marks) — pairs with the adeu structured-write pattern.
- **AI inline-edit UX consensus = our model**: inline redline shown as a tracked change, rationale on/near the edit (comment-anchor), per-edit **and** bulk accept/reject. Google Docs "Refine" (Accept / Accept-all / Reject-all), Notion "Suggest edits", Lex inline commands, SuggestCat ghost-text (Tab/Esc).
- **Harvey & Spellbook ride NATIVE Word track changes** (Word add-in), not a bespoke web editor — validates "Open in Word" as an accepted architecture. Harvey also outputs redlines **+ explanatory comments**.
- **Confidence (E3) is the riskiest item.** HCI research: high confidence scores raise trust *and* over-reliance, degrading decision accuracy (arXiv 2402.07632; tandfonline 2025 appropriate-reliance; verification-bottleneck arXiv 2601.17055, directional). Implications: lead with **specific cited rationale** (the primary trust cue for lawyers); if confidence shown, **coarse qualitative bands tied to verifiability** (grounded vs generated), not a 0–100 self-report; **do not auto-accept/pre-select low-confidence edits** — force explicit review.
- **Credible toolset (table stakes 2026):** bold/italic/underline, headings/styles, ordered+nested+unordered lists, tables, links, find/replace, comments, track-changes accept/reject, undo/redo, clean paste. **Defer to Open-in-Word:** pagination/page layout, footnotes/endnotes numbering, complex multi-level numbering, cross-references/TOC, full styles-pane, print fidelity. *The gap to "credible" is the everyday formatting toolbar + find/replace + tables — we already own the hard part (ins/del/comment marks + OOXML map + accept/reject).*

Sources: tiptap.dev/pricing + tracked-changes docs; GitHub handlewithcare/prosemirror-suggest-changes, davefowler/prosemirror-suggestion-mode, prosemirror/prosemirror-changeset; discuss.prosemirror.net suggestion-mode thread; harvey.ai/blog/improved-word-experience (Nov 2025); spellbook.com redline docs; Google Docs Refine help; arXiv 2402.07632, tandfonline 2025, arXiv 2601.17055.

---

## Net decisions for design.md

| Area | Decision |
|---|---|
| **E1 diff engine** | Adopt **Docxodus 7.1.0 (MIT)** `WmlComparer` as the redline synthesizer; rebuild edited paragraphs → comparer emits minimal `w:ins`/`w:del`. Retire the "hand-written paragraph-diff→OOXML patch" as a net-new component. Measure publish-size (NEW NuGet — not free). |
| **Export path** | Drop `docx.js` from export. All fidelity work server-side. mammoth/docx-preview = read-only projection only. |
| **E2 anchor** | `w14:paraId` as hidden PM node attr (candidate: MIT `UniqueID` extension + a split/merge minting plugin); server generates+collision-checks paraIds; keep `AnnotationReanchorService` fuzzy match as the **cross-Word-session** fallback (Word regenerates paraIds). |
| **E3 confidence** | Rationale-first; confidence as coarse qualitative bands tied to grounding, not a numeric self-report; no auto-accept of low-confidence; cite HCI risk in spec. |
| **Toolset boundary** | Add an explicit "credible toolset vs Open-in-Word" section; table-stakes toolbar + find/replace + tables in R3 scope; heavy Word features round-trip to Open-in-Word. |
| **Licensing guardrail** | MIT only. **Avoid AGPL** (SuperDoc, ONLYOFFICE, LibreOffice) — patterns not code. Docxodus MIT = OK. Verify each TipTap MIT extension's scope before adding. |
| **New spikes** | (1) Docxodus paraId-preservation on unchanged paragraphs [highest]; (2) WmlComparer vs Word "Compare Documents" on real firm templates; (3) paraId-as-PM-node-attr carry-through incl. split/merge minting; (4) track-changes-as-PM-plugin credibility proof. |

## Caveats carried forward
- Docxodus is a **young single-maintainer fork** at 7.1.0 — validate against real firm-styled legal DOCX before committing (PowerTools WmlComparer has historical edge cases on complex numbering + nested tables).
- Post-Jan-2026 facts (Docxodus 7.1.0, SuperDoc mcp-v0.17.1, Eigenpal archived, mammoth 1.12.0) are from live sources, past training cutoff — treat as current-but-verify.
- No public engineering writeup exists of a legal-specific *web* editor doing end-to-end inline-AI-redline-as-tracked-change; the UX pattern is synthesized, not a single reference impl.
