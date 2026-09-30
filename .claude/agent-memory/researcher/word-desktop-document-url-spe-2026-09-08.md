---
name: word-desktop-document-url-spe-2026-09-08
description: Is Office.context.document.url a viable identity source for SPE-backed files opened in Word desktop? Verdict AMBER (docs silent on SPE desktop behaviour; needs live probe).
metadata:
  type: project
---

# Word desktop document.url viability for SPE (2026-09-08)

**Question**: Is `Office.context.document.url` a viable identity source for SharePoint Embedded-backed Word desktop files?

**Findings**: Office.js docs say the property returns the current document URL or null, but do not establish desktop
behaviour for SPE-backed files. The repo does not use a real document URL as identity: `WordAdapter.ts` synthesizes a
hash from title/author/creation date, while the server contract (`OfficeDocumentPersistence.cs`) expects real Graph
drive + item IDs. Graph `/shares/{encodedUrl}/driveItem` can resolve a URL to a DriveItem, but that chain needs a real
URL from the host. Verdict AMBER until a live Word desktop + SPE check confirms the URL is exposed and resolvable.

**Sources**:
- https://learn.microsoft.com/en-us/javascript/api/office/office.document?view=word-js-preview
- https://learn.microsoft.com/en-us/graph/api/shares-get?view=graph-rest-1.0
- src/client/office-addins/shared/adapters/WordAdapter.ts ; src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs

**Open questions**: What does live Word desktop return for an SPE file opened via the Spaarke open flow, and does it
resolve through `/shares/{encodedUrl}/driveItem` without host-specific mutation?
Related: [[word-document-identity-desk-research-2026-09-09]].
