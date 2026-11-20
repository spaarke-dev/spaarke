---
name: federated-connector-reference-id-not-exist-2026-11-09
description: Admin center "Connect to MCP server" OAuth form says "The requested resource does not exist" on Reference ID/Authorize - no documented cause; candidate causes, verdicts, steps (Learn + blogs, 2026-11-09)
metadata:
  type: reference
---

## 2026-11-09: "The requested resource does not exist" on admin-center MCP connector (OAuth Reference ID)
**Question**: Why does the admin center Connect-to-MCP form reject a valid Teams Dev Portal OAuth registration ID with "The requested resource does not exist"?
**Findings**: The exact string is NOT documented anywhere (Learn, Q&A, GitHub, blogs). Learn's set-up page (2026-10-19) lists no Authorize button and no troubleshooting for the form; the live UI (Authentication type / Reference ID / Authorize) is matched only by developerscantina.com. The only documented 404-class auth errors are runtime ones (plugin-authentication-troubleshooting): App ID mismatch (404), Base URL mismatch (401), no matching referenceID, org policy (404). Documented warnings: registration must be "Any Teams app" (specific app -> 404 on tool calls); Support traced one Q&A failure to a trailing space in the registration CLIENT ID (not the reference ID). Working precedents: developerscantina (OAuth, Base URL incl. /mcp, Authorize -> green check) and martin-machacek (Entra SSO type, Base URL without /mcp, SSO ID). Cantina saw Entra SSO fail but OAuth with a dedicated client work.
**Sources**: learn.microsoft.com/microsoft-365/copilot/connectors/set-up-custom-federated-connectors; .../extensibility/plugin-authentication-oauth, -troubleshooting, -entra-sso, configure-plugin-mcp-servers; Q&A 5919386, 5986452; developerscantina.com/p/mcp-federated-connector; martin-machacek.com blogPost 6eeffde9
**Open questions**: whether registrations are visible per-creating-user; whether editing a registration keeps its ID; whether Authorize resolves the config via Teams Graph in a different data scope than Dev Portal shows. Needs a Microsoft support case if steps fail.
