---
name: legal-ai-docx-editing-comparison-2026-07-21
description: Grounded comparison of how leading legal-AI products (Harvey, Legora) and OSS docx editors (SuperDoc, docx-editor/Eigenpal, OnlyOffice, Collabora) edit/round-trip Word .docx without a per-seat commercial editor engine. Resolves the "Mike OSS" pointer. For Compose R3 web-editor reliability decision.
metadata:
  type: project
---

# Legal-AI .docx editing architecture comparison (2026-07-21)

**Question**: How do leading legal-AI drafting products edit + round-trip complex Word .docx without licensing a per-seat commercial text-editor engine? Word Add-in vs custom web editor; server-side deterministic OOXML vs client. Plus OSS engine licensing (SuperDoc, docx-editor, OnlyOffice, Collabora) and identify "Mike OSS".

**Findings**:

1. **Harvey (CONFIRMED, most authoritative).** Dual surface: (a) **Word Add-in via OfficeJS = the primary surface for editing existing complex documents** — edits applied through OfficeJS become native tracked changes via a word-level diff; (b) web app for AI orchestration. Core principle: OOXML ↔ natural-language **reversible mapping**; LLM proposes edits over TEXT only; deterministic backend code translates back to precise OOXML mutations preserving styles/structure. "Asking models to simultaneously perform legal reasoning and XML parsing → regression in both." List numbering / styles / tables handled by deterministic code, not model-generated XML (numbering.xml is separate). This is exactly the split-surface + deterministic-server-OOXML pattern.

2. **Legora (formerly Leya).** Same dual-surface split, confirmed from product pages: **Word Add-in = surface for redlining/reviewing EXISTING Word documents** (playbooks, markup suggestions, native redline). **"Editor" = a separate custom web editor** for drafting from Legora analysis/research, real-time collaboration, then **export to Word using firm templates**. Legora explicitly defers redlining of existing Word docs to the Word Add-in — signal that their web Editor is NOT a full-fidelity Word round-tripper; it's a drafting/authoring surface with template-based export. No public detail on underlying editor tech or server OOXML. (INFERRED: web Editor is generation-oriented, not arbitrary-docx round-trip.)

3. **SuperDoc (github.com/superdoc-dev/superdoc, Harbour Enterprises).** **Dual-licensed: AGPLv3 OR SuperDoc Commercial License** (contact q@superdoc.dev / superdocportal.dev). Built on **ProseMirror + Yjs + JSZip + Vite** (NOT TipTap directly; TipTap is also ProseMirror-based). OOXML-native ("Real DOCX, not rich text"; real pagination/section breaks/headers/footers). Tracked changes + comments + multiplayer. Has an **Agent SDK** — headless Node.js, bring-your-own-LLM for redlining/template workflows. Latest ~v54.5.3 (Jan 2026), actively developed, production-positioned. **Adoptable commercially ONLY via the paid commercial license** — AGPL is a non-starter for Spaarke's distributed product.

4. **docx-editor / Eigenpal = the "Mike OSS" pointer (RESOLVED, medium-high confidence).** `github.com/mhurhangee/docx-editor` — a **read-only preserved fork by mhurhangee** (likely Michael/"Mike" Hurhangee) of **Eigenpal's `@eigenpal/docx-editor`** (docx-editor.dev). **Apache-2.0** (commercially adoptable, no copyleft). **ProseMirror**-based, framework-agnostic core (`@eigenpal/docx-editor-core` = OOXML parser + serializer + layout engine) with React/Vue3/Nuxt adapters. Tracked changes = **insertions/deletions as ProseMirror marks with author attribution**, accept/reject individually or bulk. Claims "canonical OOXML," "round-trips .docx without quality loss," client-side only, agent APIs. **CRITICAL CAVEAT: the original Eigenpal repo was pulled ~June 2026; what survives is archived/unmaintained forks (sorenlouv, chitwitgit, mhurhangee), frozen at v1.9.0.** Same project I flagged in the R2 memo as "DOCX Editor / Eigenpal (Apache-2.0 but archived June 2026)." Architecturally it is the closest OSS analog to Spaarke Compose's own approach (ProseMirror + canonical OOXML + marks-based tracked changes) — which is a double-edged signal: validates the design, but its abandonment hints the arbitrary-docx round-trip problem is hard to sustain as a small OSS effort.

5. **OnlyOffice vs Collabora.** OnlyOffice = **AGPLv3 open-core** (Community capped ~20 concurrent connections; commercial license beyond), OOXML-native (reads/writes DOCX/XLSX/PPTX directly), in-browser. Collabora Online = **MPL-2.0** (fully open, no user/feature caps), built on **LibreOffice**, ODF-first with OOXML interop. Both are **full office engines rendered in-browser via WOPI/canvas**, not embeddable ProseMirror libraries — heavyweight server-side document engines. MPL-2.0 Collabora is the only copyleft-safe-for-distribution option of the two, but adopting it means running a LibreOffice-based server render farm, not embedding a JS editor.

**Synthesis answer**: The leading legal-AI products (Harvey, Legora, and by reputation Spellbook) do **NOT** build a full-fidelity web Word-clone for heavy editing of arbitrary complex .docx. The dominant pattern is **(b)**: lean on the **Word Add-in (OfficeJS) as the primary surface for editing existing complex documents** (native tracked changes, native fidelity — Microsoft owns the rendering), use **deterministic server-side OOXML authoring** for generation, and use a **lighter custom web editor for AI orchestration + drafting-from-analysis + review**, with template-based export rather than arbitrary round-trip. The web editor's job is authoring new/structured content, not faithfully round-tripping every firm's legacy .docx. The only players attempting full web-fidelity round-trip are the OSS engines (SuperDoc, Eigenpal/docx-editor) — and one is AGPL/commercial, the other got abandoned.

**Takeaways for Compose (custom TipTap round-trip reliability crisis)**:
- The reliability pain is structural, not a bug backlog — even funded legal-AI leaders sidestep arbitrary-docx web round-trip by delegating heavy editing to the Word Add-in.
- Consider the Harvey split: Word Add-in (OfficeJS native tracked changes) for editing existing complex docs; keep TipTap web editor scoped to NEW-doc authoring + AI review where server owns OOXML.
- Spaarke's own architecture (ProseMirror/TipTap MIT + server canonical OOXML + paraId delta) mirrors Eigenpal's abandoned design — validating direction but warning that full-fidelity web round-trip of arbitrary legal .docx is a deep, possibly unbounded, engineering commitment.
- Copyleft-safe embeddable options are thin: Eigenpal/docx-editor (Apache-2.0 but unmaintained/archived), or pay for SuperDoc commercial. Collabora (MPL) = server office engine, not an embed.

**Sources**:
- harvey.ai/blog/building-an-agent-for-complex-document-drafting-and-editing + /enabling-document-wide-edits-in-harveys-word-add-in; ZenML LLMOps writeup (MOST authoritative on legal-AI docx)
- legora.com/product/editor, /product/word-add-in, /blog/introducing-editor, /blog/introducing-legora-word-actions
- github.com/superdoc-dev/superdoc + docs.superdoc.dev/resources/license (dual AGPLv3/commercial)
- github.com/mhurhangee/docx-editor (archived Eigenpal fork, Apache-2.0) + docx-editor.dev
- collaboraonline.com/terms/collabora-online-mplv2 (MPL-2.0); onlyoffice.com/license-faq (AGPLv3 open-core)
- Prior memos: [[server-docx-authoring-numbering-2026-07-18]], [[openxml-docx-compose-r2-2026-06-29]], [[adeu-architecture-study-2026-06-29]]

**Open questions**:
- Legora Editor's underlying editor tech + whether it does any real round-trip — undisclosed; inferred generation-only.
- Whether Eigenpal/docx-editor's OOXML core is worth forking as a starting point despite being unmaintained (license permits it).
- Does Spellbook render server-side or rely purely on Word-native track changes? Still unpublished (assume Word-add-in-native).

**Related to**: [[server-docx-authoring-numbering-2026-07-18]], [[openxml-docx-compose-r2-2026-06-29]], [[adeu-architecture-study-2026-06-29]]
