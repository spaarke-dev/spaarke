---
name: mcp-ecosystem-publishing-2026-09-01
description: Sept 2026 MCP ecosystem snapshot for the "Spaarke publishes MCP servers for third-party legal AI tools" strategy — spec 2026-07-28 stateless, Harvey/Legora ARE MCP clients, Entra has no DCR/CIMD
metadata:
  type: reference
---

# MCP ecosystem for Spaarke-as-MCP-publisher — 2026-09-01

**Question**: If Spaarke ships MCP servers over Dataverse + SPE, who can consume them (esp. legal AI Word add-ins), and what does a correct enterprise server look like?

## Protocol
- **Spec 2026-07-28 is current** (RC locked 2026-05-21, final 2026-07-28). STATELESS core: `Mcp-Session-Id` + `initialize` handshake REMOVED; per-request `_meta` carries protocolVersion/clientInfo; new `server/discover` RPC; SSE resumability removed; `subscriptions/listen` replaces GET stream. Prior rev **2025-11-25** added Tasks (experimental), Extensions framework, URL-mode elicitation, OAuth client-credentials ext. **Roots/Sampling/Logging DEPRECATED** (12-mo window). **HTTP+SSE transport formally Deprecated** (since 2025-03-26). **DCR (RFC 7591) DEPRECATED in favor of Client ID Metadata Documents (CIMD)**. Tasks + **MCP Apps** are now official extensions. Most deployed clients still speak 2025-06-18/2025-11-25 — servers must negotiate down; statelessness is the design target either way.
- **Auth spec**: server = OAuth 2.1 resource server; **MUST publish PRM (RFC 9728)**; MUST validate audience (RFC 8707 resource param); token passthrough FORBIDDEN; scope challenges via WWW-Authenticate + step-up flow; AS discovery RFC 8414/OIDC. Security-best-practices doc: confused-deputy (per-client consent page required for OAuth-proxy servers), state-handle hijacking (bind handles to verified user), scope minimization, SSRF.

## Who can consume a remote MCP server (Sept 2026)
- **Claude.ai/Desktop/Cowork**: custom connectors GA all plans; **manual pre-registered OAuth client ID/secret supported** (→ Entra works); Team/Enterprise admin gating; enterprise-managed auth beta.
- **ChatGPT**: connectors search/fetch-only for company knowledge/Deep Research; **Developer Mode (beta) = full MCP tools** Pro/Plus/Business/Enterprise w/ admin toggle; Responses API `mcp` tool GA for API builders.
- **Copilot Studio**: MCP GA (May 2025), Streamable HTTP only (SSE dropped Aug 2025), via Power Platform custom connector infra (`x-ms-agentic-protocol: mcp-streamable-1.0`) or wizard.
- **M365 Copilot**: declarative agents with MCP **GA 2025-12-15** (Agents Toolkit wires MCP URL; SSO + static OAuth 2.0); MCP-based agents in admin center rollout Nov 2025; agent-workflow MCP tools GA 2026-07-15; Word Agent + custom MCP connectors in Apr 2026 wave. Copilot-credit/licensing varies by path.
- **Harvey IS an MCP CLIENT** — Connector Library: partners submit form; requires OAuth 2.1+PKCE(S256), RFC 8414, **RFC 9728 PRM; DCR optional**; workspace admin enables; caution: Harvey may not prompt before write actions. Harvey also SHIPS an MCP server (Q&A, Vault, research) for Claude/Gemini/M365 Copilot.
- **Legora IS an MCP CLIENT** — "connects to any MCP server"; customers can build bespoke servers; proprietary secure-file-transfer extension beyond base spec (blog 2026-02-26).
- **CoCounsel/TR**: Claude↔CoCounsel MCP integration (2026-05-12); TR+iManage MCP partnership (Aug 2026). Consuming arbitrary third-party servers: NOT documented.
- **NO public MCP evidence**: Spellbook (content marketing only), Robin AI, Luminance. **Definely + Ironclad publish MCP servers** (Claude Cowork connectors / Ironclad MCP server) but no evidence they consume external ones.
- **Word add-in nuance**: NO evidence any vendor's WORD ADD-IN surface consumes MCP connectors — connector consumption is in the vendors' web assistant surfaces. The "inside Word" path to Spaarke data via MCP is unproven everywhere.

## Microsoft building blocks
- **Entra ID supports NEITHER DCR nor CIMD** → patterns: (1) pre-registered client + pre-authorized client IDs (VS Code etc.) per Pamela Fox blog 2026-04; (2) OAuth proxy in front of Entra (FastMCP OAuth-proxy, obot OAuthShim) — but then YOU own confused-deputy mitigations (per-client consent, exact redirect match, __Host- cookies). Claude's manual client-id entry makes pre-registration workable for Claude; Harvey's "DCR optional" implies partner-supplied client config.
- **Dataverse MCP**: GA `/api/mcp`, delegated-only (user security roles), PPAC allow-list, `Dynamics CRM/mcp.tools` scope; metered since 2025-12-15 outside Copilot Studio; NEW: **management MCP server GA 2026-04-30** = registry/build surface for composing MCP servers from connector actions + custom APIs; tool-shape rework blogged 2026-06-08. Still no app-only.
- **SPE**: `microsoft/SharePoint-Embedded-MCP-Server` GitHub = alpha stdio DEV/PoC tool (40 admin/provisioning tools), NOT customer-facing. Work IQ SharePoint remote MCP (Agent 365) = preview, Copilot-license gated, tenant SharePoint not SPE. **SPE ChatEmbedded SDK deprecated Mar 2026** → Foundry Agent Service SharePoint knowledge source; **Copilot Retrieval API GA with `sharePointEmbedded` dataSource (preview), PAYG billing, no per-user Copilot license** — the natural retrieval backend for a Spaarke SPE MCP `search` tool alongside Graph driveItem CRUD.

## Bottom line delivered
Ship ONE remote Streamable-HTTP stateless MCP server (BFF-adjacent), Entra as AS with pre-registered clients per consumer, PRM published, OBO to Dataverse/Graph (Spaarke already has this spine), OpenAI-compatible `search`+`fetch` pair plus a small set of task-shaped tools. Day-1 consumers: Claude (incl. enterprise), Copilot Studio/M365 Copilot agents, ChatGPT (dev mode/Responses), **Harvey + Legora assistants (the strategic prize)**. NOT reachable: any in-Word add-in surface; Spellbook/Robin.

## Open questions
- Do Harvey connectors surface in Harvey's Word add-in? (No public evidence.)
- Harvey connector library client-registration mechanics without DCR (partner-supplied client_id?).
- Entra CIMD roadmap; whether MS adds DCR for MCP (watch — CIMD is the spec's future).
- M365 Copilot declarative-agent MCP: exact in-Word surfacing + credit metering for customer tenants.
