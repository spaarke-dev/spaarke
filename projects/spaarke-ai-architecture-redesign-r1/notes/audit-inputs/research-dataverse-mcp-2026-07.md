# Research brief — Dataverse MCP + Microsoft AI platform, as of 2026-07-05

> Produced by the researcher subagent (knowledge/ + Microsoft Learn + official GitHub) at operator
> request to challenge draft decision D10. Full sources at bottom. Consumed by canonical doc §7.10
> D10 (revised) and GREENFIELD-CONCEPTUAL-DESIGN.md §5.3.

## Headline

**Dataverse MCP server is GA and first-class** (`https://<org>.crm.dynamics.com/api/mcp`) — the
operator is right. The decisive fact: it authenticates with **delegated user tokens** (Entra app
granted Dynamics CRM → `mcp.tools`, a *delegated* permission, client ID allow-listed in PPAC) and
**runs every call under the caller's Dataverse security roles + row-level security**. There is
**no documented app-only/service-principal flow**. So end-user context is native to the delegated
model on either surface — the decision pivots to metering, catalog control, and swap cost.

## Facts

### Q1 — server status (Learn ms.date 2026-06-05)
- GA at `/api/mcp`; preview tools on `/api/mcp_preview`.
- **GA tool surface (CHANGED since our 2026-05-14 knowledge snapshot)**: `search_data`, `search`
  (now metadata/schema search), `read_query` (SQL SELECT), `create_record`, `update_record`,
  `delete_record`, `create_table`, `update_table`, `delete_table`, `describe` (replaces
  `describe_table`/`list_tables`/`fetch`), `upsert_skill`, `delete_skill`, `init_file_upload`,
  `commit_file_upload`, `file_download`.
- Business Skills partly GA (`upsert_skill`/`delete_skill`, `describe` over skills/apps); "run
  prompt"-style tools remain preview.
- **Metering**: chargeable since 2025-12-15 when called by agents built outside Copilot Studio,
  unless the user holds D365 Premium or M365 Copilot USL. `search_data` at tenant-graph-grounding
  rate; other tools at Copilot-credit "Text and generative AI (basic)" rate. **Recurring per-call
  runtime cost the native-handler path avoids.**

### Q2 — server-side consumption + user context (the crux)
- Custom clients: npm local proxy OR direct HTTP to `/api/mcp` with custom Entra app +
  delegated `mcp.tools` + PPAC allow-list.
- FAQ verbatim: "The Dataverse MCP server respects Dataverse security roles and row-level
  security."
- BFF-as-MCP-client preserving user context is plausible IF the BFF can OBO-exchange the incoming
  user token for the delegated `mcp.tools` scope — **not documented, unproven; needs a spike**.
- Spaarke's existing BFF OBO → Dataverse Web API already gives the identical end-user-context
  guarantee with zero new dependencies and no metering.

### Q3 — MCP client tooling in .NET
- Official MCP C# SDK (`ModelContextProtocol`): Microsoft-maintained, **preview**.
- `McpClientTool : AIFunction` → drops directly into `Microsoft.Extensions.AI` `IChatClient`
  `ChatOptions.Tools` (and Agent Framework / SK) with **no adapter code**. Swap cost later is a
  transport/registration change, not a loop rewrite.

### Q4 — Foundry Agent Service + MCP
- Foundry MCP tool **GA** (all langs; .NET Foundry SDK still preview); Responses-API loop with
  `require_approval` + `allowed_tools` + approval request/response gating; long-running mode
  preview (gpt-5.4/5.5), else 100s sync timeout.
- User-context option = **OAuth identity passthrough with per-user per-tool interactive consent
  links** — poor fit for a headless multi-tenant BFF flow. Shared identity options have no user
  context. Microsoft-audience tokens can't be forwarded to custom MCP endpoints.
- Separate "Microsoft Dataverse MCP Server (Frontier)" via Agent 365 Tools app
  (`McpServers.Dataverse.All`) is **Frontier-tenant-only** — not the org `/api/mcp`.

### Q5 — other 2026 changes
- **Responses API GA** + structured outputs on GA v1; **Assistants API decommissions 2026-08-26**
  (build nothing on it).
- **Foundry Toolboxes**: bundle tools/MCP servers behind one governed MCP endpoint — future option
  to centralize the closed catalog.

## Recommendation (adopted into revised D10)

**Hybrid leaning (b)**: native typed `dataverse.*` handlers over BFF OBO → Dataverse Web API as the
runtime path now (closed catalog + no metering + proven user-context), with contracts
**name-and-semantics aligned to the GA MCP tool surface** (`describe`, `read_query`,
`create_record`, `update_record`, `delete_record`, `search_data`) so swapping selected tools to
`/api/mcp` (or a Foundry Toolbox) later is transport-only. **One de-risking spike**: confidential
BFF client OBO → `mcp.tools` → `/api/mcp` tool list under a test user's roles. Do not relocate the
bounded planner into Foundry yet.

## Follow-ups filed
1. OBO spike (above) — decides whether direct MCP consumption stays open.
2. Verify all flows D10 exposes run user-OBO today (any app-only path is the real
   authorization-side-channel risk, independent of MCP).
3. Freeze `dataverse.*` handler names against the GA MCP names before implementation.
4. Refresh `knowledge/dataverse-mcp/` (tool surface materially changed since 2026-05-14).

## Sources
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp (2026-06-05)
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients (2026-06-05)
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-faq (2026-06-05)
- https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/mcp-authentication (upd 2026-07-01)
- https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/model-context-protocol (upd 2026-07-03)
- https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-client · MCP C# SDK
- https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses · structured-outputs
- Repo knowledge baseline (2026-05-14): knowledge/dataverse-mcp/, knowledge/foundry-agent-service/
