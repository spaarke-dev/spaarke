---
name: browser-docx-editor-engines-landscape-2026-07-22
description: Competitive landscape of browser/embeddable docx editing engines that preserve OOXML fidelity + support tracked changes (SuperDoc, OnlyOffice, Collabora, Syncfusion, TX Text Control, Aspose.Words, CKEditor 5). Why ProseMirror/Tiptap loses docx fidelity and mitigations.
metadata:
  type: project
---

# Browser/embeddable docx editing engines — fidelity + track-changes landscape (2026-07-22)

**Question**: Independent architecture assessment for an AI legal-drafting tool (docx → browser editor → docx, high fidelity, AI redlining + comments). Currently mammoth.js → Tiptap/ProseMirror and losing fidelity. Survey current engines.

**Findings**:

Two architectural classes. (A) **OOXML-native browser editors** that keep the original zip and pass untouched parts through — best fidelity. (B) **Server-rendered real office engines** (canvas streamed to browser) — highest fidelity but heavy, hard to do granular programmatic insertion.

1. **SuperDoc** (highest relevance). ProseMirror + Yjs + JSZip. OOXML-native: unedited parts (styles.xml, numbering, theme, headers/footers, custom XML, VBA, embedded fonts, OLE) copied through the ZIP untouched — this is the key fidelity mechanism vs HTML bridge. Preserves styles, numbering, section breaks, tables, headers/footers, fonts/themes as references, content controls (SDT), native w:ins/w:del tracked changes, threaded comments. Drops on SAVE (of edited content): drawing shapes→bounding rect, WMF/EMF, OLE, charts/SmartArt, picture effects. Has a headless Node agent SDK + MCP server (`@superdoc-dev/mcp`, mcp-v0.17.x Jul 2026) for LLM redlining/templates — directly matches an AI-drafting use case. Dual AGPL-3.0 / commercial. ~900 GitHub stars, 7k commits, active; used in legal-AI contract tooling. No explicitly documented stable node-id/anchor API (gap to verify).

2. **OnlyOffice Document Server**. Real office engine (own rendering, x2t converter), server-rendered, NOT ProseMirror. Excellent docx fidelity (Office Open XML is effectively native). Native track changes + comments. Rich programmatic surface: Automation API + Office-JS API + plugins + macros + Connector (external edit), AI plugin (NL macros, VBA→JS, content gen, custom LLM providers, Dec-2025 API expansion v9.2). Anchoring via bookmarks/content controls/InsertContent. Community Edition AGPLv3; embedding/white-label needs commercial Developer Edition. Self-host via Docker. Mature, widely deployed.

3. **Collabora Online / LibreOffice core**. Real office engine, server-rendered tiles to browser canvas. Integrates via WOPI (drop-in). Native track changes + comments. Fidelity best-in-class for ODF; docx good but LibreOffice's Word import/export has known layout-shift quirks on complex Word features. Open source (MPL-2.0); production support via subscription. Self-host. Weak fit for granular programmatic insertion — it's a full app, not an insertion API (server-side automation is UNO/headless LibreOffice, coarse). Best when you want a Word-like UX + storage-agnostic WOPI, not AI-precise node edits.

4. **Syncfusion Document Editor**. Client JS/React/Angular editor over **SFDT** (proprietary JSON intermediate), server-side .NET DocIO for docx↔SFDT. Track changes + comments (2025 added custom metadata on revisions). KEY LIMITATION: **track-change IDs are NOT preserved through SFDT→DOCX conversion** — stable revision IDs only survive if you stay in SFDT. SFDT is a schema JSON model (ProseMirror-like risk class), so it is a re-model not a byte-preserving round-trip. Commercial (community license for small orgs).

5. **TX Text Control**. Commercial Word-compatible engine, .NET server + JS editor. MS-Word-compatible track changes, docx/doc/rtf export with changes intact. Expensive (~$4,114+/dev, v34 SP1 Dec 2025). Self-host. Mature, enterprise.

6. **Aspose.Words**. NOT an editor — server-side OOXML library (.NET/Java/Python). Full-fidelity manipulation, revisions (Insertion/Deletion/FormatChange/StyleDefinitionChange), comments, compare/merge, rendering. This is the paid, more-capable peer of Microsoft's MIT Open XML SDK. Commercial per-dev license. Use for the server authoring/redline layer, not the browser editor.

7. **CKEditor 5**. Custom tree model (ProseMirror-like, not ProseMirror). Import-from-Word and export-to-Word are **cloud conversion services** (docx→HTML-ish model) — one-way conversion, NOT byte-preserving OOXML round-trip; loses OOXML specifics not representable in its model. Strong real-time collab, track changes + comments (premium/commercial). Good if collab UX matters more than OOXML fidelity.

**Why ProseMirror/Tiptap loses docx fidelity** (consensus): ProseMirror enforces a strict schema — content that doesn't fit a declared node/mark is **thrown away**. Mammoth compounds it by converting docx→HTML first, which structurally cannot carry numbering.xml, section properties, headers/footers, content controls, fields, or revision markup. So you lose twice: HTML bridge lossy + schema drops unknowns.

**Mitigations** (in order of leverage): (1) Drop the HTML bridge — go OOXML-native, keep original zip and pass untouched parts through (SuperDoc's model). (2) Server authoritative for OOXML; LLM/editor only touches TEXT, deterministic server code authors the XML (Harvey pattern — see [[server-docx-authoring-numbering-2026-07-18]]). (3) If staying in ProseMirror, expand schema + carry OOXML props as node attributes and retain original bytes to apply deltas. (4) For redline specifically, native w:ins/w:del must be first-class, not synthesized from HTML.

**Recommendation shape**: SuperDoc for the browser editor (OOXML-native, agent SDK, matches use case) OR OnlyOffice/Collabora if a full Word-like server-rendered UX is preferred over AI-precise node edits; pair either with a server OOXML authoring layer (Open XML SDK free, or Aspose.Words paid) so deterministic code — not the schema-constrained editor — owns fidelity-critical structure. Avoid CKEditor/Syncfusion/mammoth-Tiptap as the fidelity source of truth.

**Sources**:
- SuperDoc: github.com/superdoc-dev/superdoc, docs.superdoc.dev/getting-started/import-export, `@superdoc-dev/mcp`
- docx-editor.dev/docs/1.x/word-fidelity (honest OOXML-native preserve/drop enumeration; Apache-2.0, archived ~Jun 2026 per prior memo)
- OnlyOffice: api.onlyoffice.com/docs (Automation API, Connector), onlyoffice.com/blog/2025/12/api-updates-december-2025, deepwiki.com/ONLYOFFICE/DocumentServer/12-ai-features
- Collabora: collaboraonline.com (WOPI, based-on-libreoffice, comparing-collabora-with-onlyoffice)
- Syncfusion: syncfusion.com/docx-editor-sdk, forum 188899 (track-change-id loss on SFDT→docx), 2025 Vol 3 blog
- TX Text Control: textcontrol.com blog 2026/01/14, componentsource releases
- Aspose.Words: docs.aspose.com/words/net/track-changes-in-a-document
- CKEditor 5: ckeditor.com/docs/.../import-word, .../track-changes, npm @ckeditor/ckeditor5-comments
- ProseMirror fidelity: tiptap.dev/docs/editor/core-concepts/schema; prosemirror.net/docs/guide (strict schema throws away non-conforming content)

**Open questions**:
- SuperDoc stable node-id/anchor API for programmatic insertion — not documented; needs a spike (critical for AI-targeted redline insertion).
- SuperDoc commercial license terms/pricing for a closed-source legal SaaS (AGPL is viral).
- OnlyOffice/Collabora granular-insertion precision vs whole-doc-session model for AI redlining.

**Related to**: [[openxml-docx-compose-r2-2026-06-29]], [[server-docx-authoring-numbering-2026-07-18]], [[adeu-architecture-study-2026-06-29]]
