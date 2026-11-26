---
name: cowork-access-to-spe-file-bytes-2026-11-26
description: Cowork cannot natively read SPE file bytes (Observed 403; discoverability off; no doc names SPE); base64 contract is workspace->server only, so SPE->sandbox via MCP tool result is undocumented; OneDrive staging or device upload are the routes
metadata:
  type: reference
---

## 2026-11-26: Can Cowork get SPE .docx bytes into a skill sandbox?
**Question**: Can Cowork (or Work IQ / SharePoint connector) read an SPE file so a skill script can open it; link attach; documented SPE-in-Copilot limits.
**Findings**: No Cowork page names SPE as a source (picker = OneDrive/SharePoint/Teams; use-cowork 2026-11-25). Observed 403 in Spaarke tenant (POC 2026-10-30) and discoverability off on the Spaarke type. Work IQ `retrieve` now states SPE support (page 2026-11-11, preview) but returns Markdown extracts, not bytes; `fetch_blob` (4 MB default) has no SPE statement. KEY CORRECTION: the `contentEncoding: base64` limits (8 files / 150 MiB) apply to tool INPUT (workspace -> server) only; nothing documents a tool RESULT being saved as a workspace file, so design.md section 10's "spaarke_get_document returns base64 (cited limits)" misreads the source. Saving back (sandbox -> SPE) via a base64 parameter is documented. Ranked routes: device upload; BFF stages file to user's OneDrive then attach via picker; test whether MCP resource results materialise as files.
**Sources**:
- projects/spaarke-legal-ai-accelerator-r1/notes/research/cowork-access-to-spe-files-2026-11.md (full write-up and tests)
- learn.microsoft.com/en-us/copilot/cowork/{use-cowork,cowork-faq,cowork-plugin-development}
- learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/mcp/{retrieve,tool-reference}
- learn.microsoft.com/en-us/sharepoint/dev/embedded/build/{agent-experiences,search-containers-files}
**Open questions**: Does a tool result (resource/blob/resource_link) become a workspace file? Sandbox egress? Does Cowork picker/link work with discoverability on (needs trial container type)? Does Work IQ fetch_blob open an SPE drive item? Related: [[cowork-spe-playbooks-automation-2026-10-30]]
