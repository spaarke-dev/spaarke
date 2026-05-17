# SOURCE.md — work-iq

Curation date: **2026-05-14**
Topic owner: Ralph Schroeder (initial)
Curation budget: docs-heavy, ~80–200 KB expected, very few code samples (preview surface, sparse public samples)

---

## Summary

This topic was anticipated by the project directive to be a **gap-heavy curation** — Work IQ MCP is in public preview and Microsoft has limited public samples. The curated content reflects that reality:

- **Documentation snapshots (5 files)** — captured from Microsoft Learn before they evolve. These constitute the bulk of the curation. The Work IQ MCP catalog, the Work IQ Copilot reference (omnibus `copilot_chat` tool), per-server references for Mail + Calendar + Teams, the Work IQ CLI, and the canonical Copilot APIs overview (which copilot-camp calls "Work IQ API").
- **Synthesized catalog (`tool-catalog.md`)** — collapses the per-server docs into a single navigation surface for AI agents, with explicit notice of unverified server IDs.
- **copilot-camp lab fragment (`copilot-camp-baf6/`)** — the one piece of Microsoft sample code that exercises the Work IQ surface (Retrieval API path), with comparison to the MCP server path.

No declarative agent manifest example wires Work IQ MCP in — none exists in the curated repos as of this date. See GAPs below.

---

## Sources actually used

### 1. Microsoft Learn — Work IQ overview

- **URL**: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-02 · **`updated_at`**: 2026-04-30
- **Curated to**: `docs/work-iq-overview.md`
- **Demonstrates**: The three-layer model (Data / Context / Skills & Tools), conceptual framing, the Work IQ MCP tools section under Skills & Tools, links to dependent topics. The cross-link to `tooling-servers-overview` was the breadcrumb that found the MCP catalog.

### 2. Microsoft Learn — Work IQ MCP overview (Agent 365)

- **URL**: https://learn.microsoft.com/en-us/microsoft-agent-365/tooling-servers-overview
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-29 · **`updated_at`**: 2026-05-02
- **Curated to**: `docs/work-iq-mcp-overview.md`
- **Demonstrates**: The full Work IQ MCP catalog (9 servers), governance model (M365 admin center, Defender observability), Copilot Studio + Foundry getting-started walkthroughs, MCP Management Server tools, and concrete `.mcp.json` configuration examples for Claude Code / GitHub Copilot CLI / VS Code with the canonical remote server URL pattern `https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/{serverId}`.

### 3. Microsoft Learn — Work IQ CLI (preview)

- **URL**: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-cli (canonical; entered via `workiq-overview` redirect)
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-05-12 · **`updated_at`**: 2026-05-12 (newest doc — published ~2 days before curation)
- **Curated to**: `docs/work-iq-cli.md`
- **Demonstrates**: The npm-installed Work IQ CLI (`@microsoft/workiq`) — both `workiq ask` CLI mode and `workiq mcp` stdio-MCP-server mode. This is the local-machine path to Work IQ (as opposed to the remote `agent365.svc.cloud.microsoft` MCP servers). GitHub repo: https://github.com/microsoft/work-iq-mcp.

### 4. Microsoft Learn — Microsoft 365 Copilot APIs Overview

- **URL**: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/copilot-apis-overview
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2025-10-24 · **`updated_at`**: 2026-04-02
- **Curated to**: `docs/copilot-apis-overview.md`
- **Demonstrates**: The Microsoft Learn–canonical name for the surface that copilot-camp calls "Work IQ API" — the REST APIs under `graph.microsoft.com/v1.0/copilot`: Retrieval, Search (preview), Interaction Export, AI Interactions Change Notifications (preview), Meeting Insights, AI Insights Change Notifications (preview), Chat (preview), Copilot usage reports, Package management. Licensing requirements and Copilot-vs-Graph distinction.

### 5. Microsoft Learn — Copilot Chat API (preview)

- **URL**: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/chat/overview
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2025-09-18 · **`updated_at`**: 2026-03-24
- **Curated to**: `docs/copilot-chat-api-overview.md`
- **Demonstrates**: The REST equivalent of the Work IQ Copilot MCP `copilot_chat` tool. Capabilities, licensing, limitations. Useful for understanding what `copilot_chat` does and doesn't do regardless of transport.

### 6. Microsoft Learn — Work IQ Copilot MCP server reference (the `copilot_chat` tool)

- **URL**: https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/searchtools
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-28 · **`updated_at`**: 2026-05-01
- **Curated to**: `docs/workiq-mcp-server-reference-copilot.md`
- **Demonstrates**: The omnibus Work IQ MCP server `mcp_M365Copilot` and its single tool **Copilot Chat** (`copilot_chat`) — required parameter (`message`), optional parameters (`conversationId`, `agentId`, `fileUris`), and the catalog directive to invoke this tool as the primary retrieval mechanism when no workload-specific tool fits the request.

### 7. Microsoft Learn — Work IQ Mail MCP server reference

- **URL**: https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/mail
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-28 · **`updated_at`**: 2026-05-01
- **Used to populate `tool-catalog.md`** (Mail section). Server ID `mcp_MailTools`. 10 tools cataloged.

### 8. Microsoft Learn — Work IQ Calendar MCP server reference

- **URL**: https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/calendar
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-20 · **`updated_at`**: 2026-05-01
- **Used to populate `tool-catalog.md`** (Calendar section). Server ID `mcp_CalendarTools`. 11 tools cataloged.

### 9. Microsoft Learn — Work IQ Teams MCP server reference

- **URL**: https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/teams
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-04-28 · **`updated_at`**: 2026-05-01
- **Used to populate `tool-catalog.md`** (Teams section). Server ID `mcp_TeamsServer`, scope `McpServers.Teams.All`. 24 tools cataloged across chat + channel/team surfaces.

### 10. Microsoft Learn — Copilot Studio: Work IQ MCP overview

- **URL**: https://learn.microsoft.com/en-us/microsoft-copilot-studio/use-work-iq
- **Fetched**: 2026-05-14
- **Page `ms.date`**: 2026-03-11 · **`updated_at`**: 2026-03-12
- **Used to corroborate**: Copilot Studio "Add Tool" workflow shown in `docs/work-iq-mcp-overview.md` (the Mail-tool walkthrough). Page is essentially a Copilot-Studio-flavored summary of the Agent 365 doc — no new content captured separately to avoid duplication.

### 11. `microsoft/copilot-camp` GitHub repo

- **URL**: https://github.com/microsoft/copilot-camp
- **Cloned**: 2026-05-14 to `c:/tmp/copilot-camp-workiq` (subsequently deleted)
- **Commit SHA**: `f0ebf675a85aaee81749d07670a221faf4169b31` (committer date 2026-05-12)
- **Files referenced**: `docs/pages/work-iq/index.md`, `docs/pages/agent-365/index.md`, `docs/pages/integrate/index.md`, `docs/pages/custom-engine/agent-framework/index.md`, `docs/pages/custom-engine/agent-framework/06-add-copilot-api.md`, `docs/pages/custom-engine/agent-framework/07-add-mcp-tools.md`
- **Curated to**: `copilot-camp-baf6/README.md` (BAF6 lab fragment — Retrieval API code excerpt + comparison to MCP path)

---

## URLs attempted — full transparency

| URL | Status | Outcome |
|---|---|---|
| `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-mcp` | **404** | Page does not exist (was the directive's first guess). Replaced by the actual canonical page: `https://learn.microsoft.com/en-us/microsoft-agent-365/tooling-servers-overview` |
| `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq` | 200 | Curated to `docs/work-iq-overview.md`. This was the breadcrumb that pointed at `tooling-servers-overview`. |
| `https://learn.microsoft.com/en-us/microsoft-agent-365/tooling-servers-overview` | 200 | Curated to `docs/work-iq-mcp-overview.md` |
| `https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/searchtools` | 200 | Curated to `docs/workiq-mcp-server-reference-copilot.md` |
| `https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/mail` | 200 | Used in `tool-catalog.md` |
| `https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/calendar` | 200 | Used in `tool-catalog.md` |
| `https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/teams` | 200 | Used in `tool-catalog.md` |
| `https://learn.microsoft.com/en-us/microsoft-copilot-studio/use-work-iq` | 200 | Corroborates Copilot Studio workflow; not separately snapshotted |
| `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview` | redirect | Resolves to `work-iq-cli` (the page renamed). Curated to `docs/work-iq-cli.md` |
| `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/copilot-apis-overview` | 200 | Curated to `docs/copilot-apis-overview.md` |
| `https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview` | 200 | Curated to `docs/copilot-chat-api-overview.md` |
| `https://devblogs.microsoft.com/microsoft365dev/work-iq-mcp/` | **404** | No matching post |
| `https://devblogs.microsoft.com/microsoft365dev/introducing-work-iq-the-foundation-of-microsoft-365-copilot/` | **404** | No matching post |
| `https://devblogs.microsoft.com/microsoft365dev/` (homepage scrape) | 200 | **No posts found** mentioning "Work IQ" or "Agent 365 MCP" on the visible homepage as of 2026-05-14 |
| `https://www.bing.com/search?q=%22Work+IQ+MCP%22+site%3Amicrosoft.com` | 200 (CAPTCHA-walled) | No useful results extractable via WebFetch — Bing returned auth challenge UI |

---

## GAPs and what wasn't found

### Reference pages not yet snapshotted

These server reference pages exist (linked from `tooling-servers-overview`) but were not fetched in this curation batch. Confirmed server IDs and per-server permission scopes are missing for them in `tool-catalog.md`:

- `mcp-server-reference/sharepoint` (Work IQ SharePoint)
- `mcp-server-reference/onedrive` (Work IQ OneDrive)
- `mcp-server-reference/me` (Work IQ User)
- `mcp-server-reference/word` (Work IQ Word)
- `mcp-server-reference/dataverse` (Dataverse and Dynamics 365)

**Action for next refresh**: WebFetch each of these to fill out `tool-catalog.md` and add per-server doc snapshots if useful.

### No agent manifest examples found

Searched `microsoft/copilot-camp` for any declarative-agent manifest or Foundry agent definition that wires a Work IQ MCP server in. **None exist** as of commit `f0ebf67`. The only relevant lab (BAF6) calls the Work IQ surface via REST, not MCP. Lab BAF7 demonstrates MCP integration in general but uses a fictional `insurance-mcp` server hosted on Azure Functions, not a Work IQ MCP server.

**Action for next refresh**: Check `microsoft/work-iq-mcp` GitHub repo (https://github.com/microsoft/work-iq-mcp — sourced for the Work IQ CLI), the `pnp/copilot-pro-dev-samples` repo, and Microsoft Foundry sample repos for any Work IQ MCP wiring examples.

### Microsoft Mechanics video transcript

The project directive referenced "the Microsoft Mechanics video transcript on 'Work IQ: Copilot, Data & Agent Skills' — Jeremy Chapman walkthrough." **Not curated**: Microsoft Mechanics is YouTube video content with no convenient markdown transcript surface. Searched `microsoft.com/insidetrack` and `youtube.com/microsoft` patterns but no transcript-as-doc page was located. The `docs/work-iq-overview.md` snapshot covers the same conceptual territory as the Mechanics video and is more durably citable.

### Microsoft 365 Developer Blog posts

The directive noted Microsoft 365 Developer Blog posts as likely best sources. **No matching posts found** on the blog homepage as of 2026-05-14. The blog has moved on to other topics; the Work IQ MCP material is being maintained as Microsoft Learn documentation, not blog posts.

### `copilot_chat` tool — naming convention note

The directive references "the Work IQ Copilot MCP server's `copilot_chat` tool documentation specifically." The actual catalog (`docs/workiq-mcp-server-reference-copilot.md`) names this tool **"Copilot Chat"** (display name) on the `mcp_M365Copilot` server. The lowercase-underscore form `copilot_chat` is the conventional tool ID one would use in code, but is not explicitly printed in the docs. The conversational reference to it as `copilot_chat` is preserved in the directive and in `NOTES.md` for consistency.

### Naming evolution

- The directive notes "Work IQ MCP was rebranded from 'Agent 365 MCP'." This is **partially supported** by the snapshots: the Microsoft Learn docs now consistently use "Work IQ MCP" / "Work IQ <Workload>" naming. The container product is still **Microsoft Agent 365** (the control plane). The `copilot-camp` `docs/pages/agent-365/index.md` page exists separately from `docs/pages/work-iq/index.md`, suggesting Microsoft now treats them as distinct concepts: Agent 365 = control plane (governance, registry, observability); Work IQ = intelligence layer (data + context + skills/tools, exposed via MCP). The MCP servers themselves are called "Work IQ MCP" but live under the Agent 365 control plane.

### `Agent 365 SDK` reference

Multiple snapshots reference "Microsoft Agent 365 SDK and CLI" at `/en-us/microsoft-agent-365/developer/`. **Not snapshotted** in this curation batch (out of scope for Work IQ MCP specifically — that's an SDK for building Agent 365–managed agents, not for consuming Work IQ MCP). If a future Spaarke project builds an Agent 365–managed agent, fetch then.

---

## Refresh checklist for next cycle

1. Re-fetch all 9 URLs marked 200 above; diff for changes (preview surface — high change velocity).
2. Fetch the 5 missing `mcp-server-reference/*` pages (SharePoint, OneDrive, me, Word, Dataverse) — update `tool-catalog.md` with confirmed server IDs.
3. Check `github.com/microsoft/work-iq-mcp` for source code that might inform the CLI doc or expose new tools.
4. Re-search `devblogs.microsoft.com/microsoft365dev` for any new Work IQ posts (none found in this batch).
5. Re-check `microsoft/copilot-camp` for a new lab (Work IQ MCP lab is listed as "coming soon" on the `work-iq/index.md` page).
6. Re-check `pnp/copilot-pro-dev-samples` for any declarative-agent manifest wiring Work IQ MCP in.

---

## Files in this folder

```
work-iq/
├── SOURCE.md                              (this file)
├── NOTES.md                               (stub — engineer review pending)
├── tool-catalog.md                        (synthesized catalog)
├── docs/
│   ├── work-iq-overview.md                (M365 Copilot extensibility/work-iq)
│   ├── work-iq-mcp-overview.md            (microsoft-agent-365/tooling-servers-overview)
│   ├── work-iq-cli.md                     (M365 Copilot extensibility/work-iq-cli)
│   ├── workiq-mcp-server-reference-copilot.md  (Work IQ Copilot — the omnibus server)
│   ├── copilot-apis-overview.md           (canonical Work IQ API docs)
│   └── copilot-chat-api-overview.md       (REST equivalent of `copilot_chat` MCP tool)
└── copilot-camp-baf6/
    └── README.md                          (lab fragment + REST-vs-MCP comparison)
```
