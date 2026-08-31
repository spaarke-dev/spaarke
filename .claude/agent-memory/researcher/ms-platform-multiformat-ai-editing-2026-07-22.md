---
name: ms-platform-multiformat-ai-editing-2026-07-22
description: Clean-slate architecture research — Microsoft-platform (SPE/Dataverse/Azure) multi-format (docx/xlsx/pptx/pdf) ingestion + AI interaction + light in-app editing + AI redlining + open-web/desktop + versioning, with NO commercial-licensed component and NO AGPL. Confirms SPE+WOPI+Office delegates fidelity/editing/versioning/co-authoring to Microsoft. Supersedes the "build a web Word-clone" framing for Compose.
metadata:
  type: project
---

# MS-platform multi-format AI editing — delegate-to-Office architecture (2026-07-22)

**Question**: Clean-slate. MS-platform product (SPE/Dataverse/Azure) must ingest MULTIPLE formats (docx first, also pdf/xlsx/pptx), let AI interact accurately, provide light in-app editing + AI redlining, support open-to-web/desktop + versioning — with NO commercial component and NO AGPL. Who else solved this; what to delegate vs build.

**Findings**:

1. **THE BIGGEST LEVER (CONFIRMED, authoritative): SPE + WOPI + Office delegates fidelity + editing + versioning + co-authoring to Microsoft, for free.** Microsoft Learn "Open Office Files From Your App" (learn.microsoft.com/sharepoint/dev/embedded/build/open-office-files, ms.date 2026-07-13, updated 2026-07-15) confirms SPE natively provides: open in **Office for the web** (via DriveItem `webUrl` + `action=view|edit|default`), open in **Office desktop** (via `ms-word:ofe|u|{webUrl}` / `ms-excel` / `ms-powerpoint` URI schemes), **AutoSave**, **version history** (auto-enabled per Word/Excel/PowerPoint file — see/compare/restore/recover, incl. co-authoring sessions), **co-authoring** (real-time, presence), **sharing links** (4 scopes), **comments/mentions** (license-gated), **breadcrumbs**. Microsoft handles storage + Office-for-web rendering + co-authoring infra + WOPI (SPE = Microsoft's API-only successor to WOPI/CSPP). **PDF opens in an embedded viewer**; unsupported types redirect via container-type `urlTemplate`. This is the platform doing all the hard fidelity/locking/versioning work.

2. **Spaarke ALREADY has this wired** (knowledge/sharepoint-embedded/NOTES.md): `src/client/code-pages/SpeDocumentViewer/` resolves `webUrl` and launches Office (web + desktop); Word/Outlook add-ins upload with "SPE First, Dataverse Second"; identity flows so Word Copilot grounds on the SPE-stored file. The open-web/desktop/versioning/co-authoring lever is not a build — it's already partly built and just needs to be leaned on harder.

3. **Multi-format model — one MIT OOXML engine covers docx/xlsx/pptx.** `DocumentFormat.OpenXml` (Open XML SDK, MIT, .NET 8, 3.x active) exposes `WordprocessingDocument` + `SpreadsheetDocument` + `PresentationDocument` over the same OPC/zip package model — one library, server-side read/address/write for all three OOXML families. PDF is fixed-layout, NOT OOXML — separate lane: **PDF.js (Apache-2.0)** for view/annotate; **Azure AI Document Intelligence** (layout/prebuilt models) for structured extraction; there is NO permissive true-edit-in-place for arbitrary PDF (fixed layout makes reflow editing intrinsically hard — even commercial tools mostly annotate/overlay). SPE's own embedded PDF viewer (read-only annotations landing ~March 2026 per SPE what's-new) covers the view/annotate case natively. A common "addressable document model + operations" abstraction over OOXML is realistic (shared OPC part addressing); extending it to PDF is only realistic at the annotate/extract level, not full edit.

4. **Permissive stack that composes** (no commercial, no AGPL): Open XML SDK (MIT) for server OOXML read/write across docx/xlsx/pptx + `Codeuctivity.OpenXmlPowerTools` (MIT) for diff/redline; ProseMirror/TipTap (MIT) for the light web-edit surface; PDF.js (Apache-2.0) for PDF view/annotate; Eigenpal `docx-editor` (Apache-2.0, archived — reference/fork only) for the byte-preserving projection idea. AVOID: OnlyOffice (AGPL), Collabora (MPL server engine — permissive but heavy), Syncfusion/Apryse/Aspose/TX (commercial). The Office-for-web path (item 1) means you don't need a full in-browser engine at all for heavy editing.

5. **Who else solved it — the dominant enterprise pattern is DELEGATE-FIDELITY-TO-OFFICE, build only the AI layer + a light surface.**
   - **Microsoft 365 Copilot / Harvey / Legora**: heavy editing happens in Office (Word for the web / Word Add-in via OfficeJS = native track changes); AI proposes text-level edits; deterministic code (server or add-in) produces OOXML. Nobody rebuilds Word rendering.
   - **Box AI**: doc platform that keeps files in its store, delegates Office editing to Office Online integration / its own viewer, layers AI (Q&A, extract, generate) on top — does NOT rebuild a fidelity editor.
   - **Glean** (enterprise search/assistant, ~$4.6B) and **Hebbia** (Matrix data-grid, iterative source decomposition, multi-agent, full-document not RAG-chunks): both are AI-interaction-over-documents layers — retrieval/synthesis/citation — with NO document editing engine at all. They confirm the AI layer is a SEPARATE concern from the editing/fidelity layer.
   - Recurring architecture: **platform (Office/WOPI/SPE) owns fidelity + editing + versioning; the product owns (a) the AI reasoning/redline layer over a reversible content model, and (b) a light custom surface for orchestration/review.** This is Harvey's model generalized.

6. **AI-interaction layer best practice (multi-format):** reversible mapping OOXML(or PDF-extract) ↔ natural-language/structured content; LLM edits TEXT/structured ops only; deterministic server code emits bytes (Harvey principle, validated repeatedly — numbering/styles/tables never model-authored). For multi-format scale, normalize each format into a common addressable content model (paraId-style stable IDs for OOXML; page/bbox spans for PDF) so the AI sees one abstraction; per-format serializers own byte production. Byte-preserving projection (only reinterpret edited spans, round-trip untouched parts) keeps fidelity.

**Recommended clean-slate architecture for THIS context**:
- **Storage/fidelity/versioning/co-authoring/open-web+desktop** → DELEGATE to SPE + WOPI + Office (already wired). Do NOT build. `webUrl` for web, `ms-word:ofe` URIs for desktop, auto version history, auto co-authoring, embedded PDF viewer.
- **Server document engine** → Open XML SDK (MIT) over docx/xlsx/pptx for parse/address/write + OpenXmlPowerTools for redline diff; retain original bytes as source of truth; byte-preserving deltas.
- **AI layer** → reversible OOXML↔content model, LLM edits text/structured ops, deterministic server produces OOXML redlines (w:ins/w:del) — multi-format via a common addressable model.
- **Light in-app edit surface** → TipTap (MIT) scoped to "the basics" of NEW-doc authoring + AI review, as a byte-preserving projection (NOT a lossy HTML-subset round-tripper). Heavy edits of arbitrary complex docs → hand off to Office-for-web/desktop.
- **PDF** → SPE embedded viewer + PDF.js annotate + Document Intelligence extract; no full PDF edit.

**Delegate vs build (explicit)**:
- DELEGATE to SPE+WOPI+Office: open-web, open-desktop, version history, co-authoring, locking, AutoSave, comments/mentions, sharing, PDF view — ALL of it, GA today.
- BUILD: (a) AI reasoning + redline layer over reversible content model; (b) server OOXML byte-production (Open XML SDK); (c) light TipTap authoring/review surface; (d) Dataverse metadata/routing/security projection (already exists); (e) multi-format ingestion + extraction (Document Intelligence + AI Search, already exists).

**Sources**:
- learn.microsoft.com/sharepoint/dev/embedded/build/open-office-files (ms.date 2026-07-13) — DEFINITIVE on open-web/desktop/versioning/co-authoring/PDF viewer
- learn.microsoft.com/sharepoint/dev/embedded/development/content-experiences/office-experience
- learn.microsoft.com/sharepoint/dev/embedded/overview + whats-new (SPE = API-only WOPI successor; PDF viewer ~Mar 2026)
- knowledge/sharepoint-embedded/NOTES.md (Spaarke already wired: SpeDocumentViewer webUrl flow, add-ins, Copilot grounding chain)
- Hebbia (sacra.com/c/hebbia, medium ISD writeup), Glean (agent.nexus/blog/hebbia-vs-glean) — AI layer separate from editing
- github.com/dotnet/Open-XML-SDK (MIT, WordprocessingDocument/SpreadsheetDocument/PresentationDocument); PDF.js (Apache-2.0)
- Prior memos: [[browser-docx-editing-market-patterns-2026-07-21]], [[legal-ai-docx-editing-comparison-2026-07-21]], [[server-docx-authoring-numbering-2026-07-18]], [[openxml-docx-compose-r2-2026-06-29]], [[spe-dedup-content-identity-2026-07]]

**Open questions**:
- Confirmed vs inferred: SPE Office delegation = CONFIRMED (Learn, dated). Box AI internals = inferred from product behavior. PDF edit = confirmed no permissive full-edit path.
- Does leaning harder on Office-for-web reduce the NEED for the TipTap round-tripper to near-zero (i.e., TipTap only for AI-draft preview, never for arbitrary-docx fidelity)? Design decision for main session.
- Co-authoring + programmatic OOXML writes concurrency (423 Locked) — still the open race from openxml-docx-compose-r2 spike list.

**Related to**: [[browser-docx-editing-market-patterns-2026-07-21]], [[legal-ai-docx-editing-comparison-2026-07-21]], [[openxml-docx-compose-r2-2026-06-29]]
