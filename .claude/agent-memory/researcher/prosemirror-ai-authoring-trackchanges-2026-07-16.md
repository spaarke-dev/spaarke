---
name: prosemirror-ai-authoring-trackchanges-2026-07-16
description: Editor-side (ProseMirror/TipTap) research for Spaarke Compose — independent-OSS track-changes/suggestion libraries, AI inline-edit UX, confidence+rationale display, minimum credible legal-editor toolset. Complements the save-side OOXML memos.
metadata:
  type: project
---

# ProseMirror/TipTap AI authoring + track-changes (2026-07-16)

**Question**: Current (mid-2026) best practices for AI authoring UX + tracked-changes/suggestions in a ProseMirror/TipTap editor for Spaarke Compose (TipTap MIT base; HARD constraint = no TipTap Pro paid/unpaid features; home-grown ins/del/commentAnchor marks → OOXML on save). Four parts: OSS track-changes libs, AI inline-edit UX, confidence+rationale display, minimum credible toolset.

**Findings**:

1. **OSS track-changes on ProseMirror = two mark-based libs + one changeset primitive, all MIT.** TipTap's own Tracked Changes + Comments + AI Toolkit are all PAID Cloud add-ons (confirmed, tiptap.dev/pricing $49–$999/mo) — off-limits. Independent MIT options: (a) `@handlewithcare/prosemirror-suggest-changes` (v0.1.8 Nov 2025, ~60★, `insertion`/`deletion`/`modification` marks, `applySuggestion()`/`revertSuggestion()`, decorates `dispatchTransaction`); (b) `davefowler/prosemirror-suggestion-mode` (~45★, no npm release/tags, `suggestion_insert`/`suggestion_delete` marks, green/red highlight + hover tooltip w/ metadata, range+batch accept/reject, ships an `applySuggestion`/`createApplySuggestionCommand` text-match helper explicitly "handy for AI"); (c) `prosemirror-changeset` (Marijn/ProseMirror official, MIT, the low-level insert/delete-range distiller powering the core track example — a primitive, not a full workflow). Both suggestion libs are EARLY (0.x, <100★, both released Feb–Mar 2025, briefly discussed merging). Also `emergence-engineering/prosemirror-suggestcat-plugin` v2.2.0 — mark-based AI suggestions w/ streaming + QUEUED→PROCESSING→DONE states + ghost-text + grammar, but historically coupled to their hosted SuggestCat API (verify license/backend before adopting). **Verdict: our home-grown ins/del/commentAnchor marks are the RIGHT architecture** — mark-based-mapping-to-OOXML is exactly what every credible OSS lib does; none is mature enough to justify ripping out working code that already maps to w:ins/w:del/w:comment. Worth mining davefowler's text-match `applySuggestion` helper as a reference for the AI-suggestion apply path.

2. **AI inline-edit UX consensus (2026): inline redline in-document as tracked changes + per-edit rationale as comment; accept/reject per-edit + accept-all/reject-all; streaming inline.** Legal leaders both lean on NATIVE Word track changes, NOT a bespoke web editor: Spellbook redlines inside Word ("all edits appear as standard track changes under your name"); Harvey (web app + Word add-in, "improved Word experience" blog Nov 18 2025) outputs automated Word redlines + explanatory comments, adding party-perspective (first/third-party paper) early 2026. General tools: Google Docs "Refine" floating bar → Accept suggestion / Accept all / Reject all, edits shown as colored suggestions; Notion "Suggest edits"; Lex `++` inline command → accept/decline without leaving the doc; SuggestCat = Notion-like inline + ghost-text (Tab accept / Esc dismiss). The "suggest → review → accept as tracked change" flow Spaarke describes IS the emerging standard; the differentiator is that Harvey/Spellbook defer the surface to Word — a signal that "Open in Word" is a legitimate escape hatch, not a failure.

3. **Confidence display is genuinely risky — show rationale always, confidence carefully.** 2025-26 HCI research (arXiv 2402.07632 miscalibrated-confidence; tandfonline 2025 appropriate-reliance + clinical-CDSS studies; arXiv 2601.17055 verification-bottleneck): high confidence scores raise trust AND over-reliance, degrading decision accuracy; miscalibration is hard for users to detect; merely showing calibrated confidence does NOT guarantee appropriate reliance and can even suppress trust in otherwise-fine systems. Verification-bottleneck study: on hard problems AI-use rose 50→74% while verification confidence FELL 85.7→68.1%. Implications for Compose: lead with the RATIONALE (cited, specific) as the primary trust signal; treat a numeric confidence % as the weakest form — prefer coarse/qualitative bands, tie the signal to verifiability (what's grounded vs. generated), and design against rubber-stamping (e.g., force-review low-confidence, don't auto-select). Do NOT surface a false-precision 0-100 score to lawyers.

4. **Minimum credible legal-editor toolset (2026) — and the Word line.** Expected core: bold/italic/underline, headings/styles, ordered+nested+unordered lists, tables, links, find/replace, comments, track-changes/accept-reject, undo/redo, paste-clean. The "credible vs. recreating Word" line: teams building on ProseMirror include the above but DEFER pagination/page layout, footnotes/endnotes-with-numbering, complex numbering schemes, cross-references/TOC, styles-pane management, and print fidelity to "Open in Word." Harvey/Spellbook's own choice to ride Word's native track changes is the strongest evidence that round-tripping to Word for heavy features is the accepted pattern, not a cop-out. Compose already has the hard part (ins/del/comment marks + OOXML map + accept/reject) — the gap to "credible" is the everyday formatting toolbar + find/replace + tables, not Word-parity.

**Sources**:
- tiptap.dev/pricing + tiptap.dev/docs/editor/extensions/functionality/tracked-changes (confirms Tracked Changes/Comments/AI are paid) ; eddyter.com/blogs/tiptap-pricing-explained-2026
- github.com/handlewithcarecollective/prosemirror-suggest-changes (MIT, v0.1.8 Nov 2025)
- github.com/davefowler/prosemirror-suggestion-mode (MIT, AI applySuggestion helper) ; prosemirror-suggestion-mode.netlify.app (examples)
- github.com/prosemirror/prosemirror-changeset (MIT primitive) ; prosemirror.net/examples/track/
- discuss.prosemirror.net/t/releasing-prosemirror-suggestion-mode/8239 (two-lib design tradeoffs, Feb–Mar 2025)
- github.com/emergence-engineering/prosemirror-suggestcat-plugin + discuss.prosemirror.net/t/suggestcat-ai-plugin-for-prosemirror/5623
- harvey.ai/blog/improved-word-experience (Nov 18 2025) ; spellbook.com/learn/redline-contracts ; gc.ai/blog/spellbook-vs-harvey
- support.google.com/docs/answer/13447609 (Docs Refine/accept-reject) ; notion.com/help (Suggest edits) ; lex.page
- arXiv 2402.07632 ; tandfonline 10.1080/12460125.2025.2593251 + 10.1080/10447318.2025.2539458 ; arXiv 2601.17055

**Open questions / post-Jan-2026 flags**:
- SuggestCat plugin license + whether v2.2.0 still requires the hosted backend — UNVERIFIED (npm 403'd; check GitHub LICENSE directly).
- Harvey party-perspective + Spellbook specifics are vendor marketing, dated late-2025/early-2026 — post-cutoff, treat as directional not verified.
- No public engineering writeup of a legal-specific *web* editor doing inline-AI-redline-as-tracked-change end-to-end; the pattern is inferred from Google Docs + the two OSS libs + Word-add-in leaders.
- arXiv 2601.17055 and 2605.28255 are 2026 preprints (post-cutoff) — cited for direction, not settled findings.

**Related**: [[openxml-docx-compose-r2-2026-06-29]] (save-side OOXML), [[adeu-architecture-study-2026-06-29]] (CriticMarkup read / structured-write asymmetry — directly relevant to the AI-suggestion apply path).
