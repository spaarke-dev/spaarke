---
name: word-document-identity-desk-research-2026-09-09
description: Desk-only evidence pack for Word add-in document identity (Office.context.document.url, getFilePropertiesAsync, Word.Document.path/fullName, custom XML parts, Graph /shares, SPE contentstorage URLs) with exact requirement-set versions and documentation-silence findings
metadata:
  type: project
---

Desk research (NO live Office host) for `spaarkeai-word-add-in-r1` task 002 — document-identity surfaces for a Word add-in over SharePoint Embedded.

**Why:** the add-in currently synthesizes a fake identity hash (`WordAdapter.ts`) while the server contract wants real Graph driveId/itemId; task 002 asked which host surfaces could supply real identity, verified against docs only.

**How to apply:** treat everything below as DESK-RESEARCHED. Nothing was empirically verified. The four hard version facts are the load-bearing part.

### Hard version facts (all doc-cited)
- `Word.Document.customXmlParts` = **WordApi 1.4**. `Word.Document.settings` = **WordApi 1.4**.
- `Word.Document.path` and `Word.Document.fullName` = **WordApiDesktop 1.4** — desktop-only set, "Not applicable" for Office on the web, "Not available" for volume-licensed/LTSC and iPad. Win min build 2508 (19127.20264); Mac 16.100.4.
- Common-API `Office.context.document.customXmlParts` = **CustomXmlParts** requirement set → Word web + Windows + Mac + iPad.
- `Document.getFilePropertiesAsync` is **NOT in any requirement set** ("Methods that aren't part of a requirement set"), but the table lists Word web/Windows/Mac/iPad support. Must be declared via `<Methods>`/`<Method>` or runtime-checked.
- **Live repo defect surfaced:** both `src/client/office-addins/word/manifest.json` and `word-manifest.xml` declare `WordApi` minVersion **1.3**. Any use of `Word.Document.customXmlParts` or `.settings` needs 1.4. This is exactly the "installs then fails at runtime" trap.

### Documentation SILENCES (findings, not gaps to guess at)
- `Office.context.document.url` has **no requirement set and no host-support table** anywhere — requirement sets cover methods, not properties. Docs say only "Returns null if the URL is unavailable." Desktop vs web vs Mac behavior, cloud vs local vs never-saved: **undocumented**.
- Graph `/shares` docs never mention SharePoint Embedded, containers, or `contentstorage`. No status code documented for no-access. Response examples omit `parentReference` (only `publication` is documented as not-returned-by-default on driveItem) → `$select` defensively.
- `/shares` **does** support Application permissions (Files.ReadWrite.All / Sites.ReadWrite.All) — but SPE content normally requires `FileStorageContainer.Selected`, which `/shares` doesn't list. Unreconciled.

### Custom XML part durability caveat
Custom XML parts live inside the OPC .docx package and "persist with the file, independent of the add-in" (persisting-add-in-state doc). BUT the Word Document Inspector ships a built-in **"Custom XML data"** removal module → a user can strip the identity stamp. Separately, the "custom XML markup is no longer supported / Word removes it" support article is about in-body `w:customXml` markup, NOT custom XML parts — do not conflate the two.

### SPE desktop-open shape
`open-office-files` (ms.date 2026-07-13) says SPE `webUrl` is the doc2.aspx Office-for-web URL, **not** the file path; desktop launch is built as `ms-word:ofe|u|{folder.webUrl}/{item.name}` → `https://{tenant}.sharepoint.com/contentstorage/CSP_{id}/Document%20Library/MyDocument.docx`, and "If you need the canonical file URL, use the DriveItem `webDavUrl` property instead of `webUrl`." Whether Word desktop then reports that path back through `document.url` is **inference, untested**.

Related: [[spe-dedup-content-identity-2026-07]], [[word-extensibility-platform-2026-09-01]], [[graph-spe-standards-2026-08-16]]
