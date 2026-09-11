---
name: word-extensibility-platform-2026-09-01
description: Full-stack Word extensibility survey (Sept 2026) for a Microsoft-native Word AI product — Office.js capability map by requirement set (WordApi 1.9 latest GA, WordApiDesktop 1.5, preview = VBA-parity port incl. exportAsFixedFormat/Revisions), NAA GA, unified manifest GA July 2026, add-in-as-Copilot-skill state (Excel-only preview via SKILL.md/Cowork plugin model), declarative agents DO surface in Word Copilot, Agent Mode GA Apr 2026, SPE desktop-open + Copilot discoverability/Retrieval API facts.
metadata:
  type: project
---

# Word extensibility platform deep-dive (2026-09-01)

**Question**: Full technical survey of the Word extensibility platform (Office.js capabilities by requirement set, runtime/manifest/auth/deployment, Copilot extensibility surfaces in Word, SPE document-storage angle) to inform Spaarke's Microsoft-native Word AI product architecture.

**Findings** (headline; full report returned to caller 2026-09-01):
1. **Requirement-set state (Sept 2026)**: WordApi **1.9** is the newest cross-platform GA set (content-control list items; Win 2411+, Mac 16.91, web, iPad). WordApi 1.4=comments+changeTrackingMode, 1.5=footnotes/endnotes+CC events+styles, 1.6=TrackedChange read/accept/reject + paragraph events, 1.7/1.8=annotations (critiques; **needs M365 subscription service**) + Range.highlight, 1.9=CC dropdowns. **WordApiDesktop 1.1–1.5** (Win+Mac only): 1.1=`Document.compare`+`importStylesFromJson`, 1.2=`compareFromBase64`, 1.3–1.5=list templates/styles/borders. **Preview** channel is a large VBA-parity port: `exportAsFixedFormat` (PDF!), `RevisionCollection`, `ReviewerCollection`, `ConflictCollection`, `Selection`, `Window`, `SourceCollection` (bibliography), comment events (onCommentAdded/Changed on Body). XML manifest CANNOT declare WordApiDesktop as activation requirement; unified manifest CAN.
2. **Track changes**: unchanged from my 2026-07-22 memo — TrackAll + ordinary edits is still the ONLY way to author revisions; TrackedChange is read/accept/reject only; authorship = signed-in user. Revisions-object write API still nowhere, even in preview (RevisionCollection preview = read/getItem).
3. **Runtime/manifest/auth**: **Unified JSON manifest GA'd ~2026-07-17** for Word/Excel/PPT+Outlook (direct support = web + Win 2304+; Mac/iPad/perpetual served via auto-generated XML when deployed through AppSource/M365 admin center). **NAA GA since May 2025** across hosts (`createNestablePublicClientApplication`); recommended over legacy getAccessToken/OBO; can mint tokens for a custom BFF scope directly. Office.js payload limit 5MB (documented for Excel web; Word web behaves similarly), getFileAsync slices ≤4MB (64KB on iPad).
4. **Copilot extensibility**: **Declarative agents DO surface inside Word** (Copilot pane agent picker — Teams/Word/PPT/M365 chat). **Agent Mode GA 2026-04-22**, default UX per Build 2026; no third-party in-app extensibility of Agent Mode itself — extension goes via DA/connectors/MCP. The Build-2025 "add-in actions in Copilot agents" model has been **restructured (Aug 2026) into "Copilot skills" using SKILL.md + Office.js scripts packaged as Cowork-style plugins — currently EXCEL-ONLY preview (build 2608, Insider)**; the old agent-and-add-in docs were pulled from office-js-docs-pr live. Word parity not yet shipped. Licensing: Copilot Chat + PAYG Copilot Credits covers DA usage for non-Copilot-licensed users (instructions+web-only DAs are free class).
5. **SPE angle**: SPE files open in **Word desktop via Office URI schemes** (`ms-word:ofe|u|{folder.webUrl}/{fileName}`; use `webDavUrl` for canonical path) with co-authoring/AutoSave/versioning; `isOfficeRestricted` (beta) can disable Office opens per container type; `urlTemplate`/`ApplicationRedirectUrl` route users back to the app. **Copilot: nothing auto-exposed** — container-type **discoverability setting** gates M365 Copilot; **Copilot Retrieval API `dataSource: sharePointEmbedded` (preview, PAYG on Copilot Studio meter, needs ≥1 Copilot license in tenant for index init)**; the SPE agent SDK (React ChatEmbedded) was **deprecated March 2026** → Foundry Agent Service + SharePoint knowledge source. Add-in↔record correlation: `Office.context.document.url` returns the contentstorage SharePoint URL → Graph `/shares/u!{base64url}` → driveItem.
6. **insertOoxml fidelity**: cannot replace numbering.xml (open bugs #5243/#5381/#2991: styles.xml changes apply, numbering.xml changes silently DON'T; numbering drift on getOoxml/insertOoxml round-trip). Microsoft's own guidance: prefer typed APIs, OOXML only as fallback.

**Sources**:
- learn.microsoft.com/javascript/api/requirement-sets/word/word-api-requirement-sets (availability matrix, 2026-04-21)
- .../word-api-1-7|1-8|1-9-requirement-set, .../word-api-desktop-1-5-requirement-set, .../word-preview-apis
- devblogs.microsoft.com/microsoft365dev/nested-app-authentication-now-generally-available-across-microsoft-365/
- devblogs.microsoft.com/microsoft365dev/unified-manifest-for-office-add-ins-now-ga/ (July 2026)
- devblogs.microsoft.com/microsoft365dev/office-addins-at-build-2025/ (add-in actions announce)
- github.com/OfficeDev/office-js-docs-pr live branch: docs/excel/excel-skills.md + excel-copilot-skill.md (2026-08-13, Excel-only skills preview)
- learn.microsoft.com/microsoft-365/copilot/extensibility/overview-declarative-agent (DA surfaces incl. Word)
- microsoft.com/en-us/microsoft-365/blog/2026/04/22/copilots-agentic-capabilities... (Agent Mode GA)
- learn.microsoft.com/sharepoint/dev/embedded/build/open-office-files (desktop URI schemes, isOfficeRestricted, urlTemplate)
- learn.microsoft.com/sharepoint/dev/embedded/build/agent-experiences (discoverability, Retrieval API sharePointEmbedded preview, ChatEmbedded deprecation)
- learn.microsoft.com/office/dev/add-ins/concepts/resource-limits-and-performance-optimization
- OfficeDev/office-js issues #5243, #5381, #4684, #2991 (insertOoxml numbering)
- Prior memos: [[legal-ai-redline-surface-landscape-2026-07-22]] (track-changes mechanics), [[spe-wopi-coauthoring-lock-423-2026-07-30]], [[spe-crosstenant]], knowledge/sharepoint-embedded/NOTES.md + knowledge/declarative-agents/NOTES.md

**Open questions**:
- When do Copilot skills (SKILL.md model) reach Word? Watch office-js-docs-pr for a docs/word/word-skills.md analog.
- Exact GA state of RemoteMCPServer runtime in DA actions (samples exist; GA vs preview not pinned this pass).
- Empirical: does `Office.context.document.url` return the contentstorage URL for SPE docs opened in Word DESKTOP (vs web)? Needs a spike with Spaarke's existing add-in.
- Whether Word-web honors the 5MB rich-API payload limit identically to Excel-web (documented only for Excel).
