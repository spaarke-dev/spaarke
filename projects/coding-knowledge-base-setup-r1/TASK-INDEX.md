# Task Index — Coding Knowledge Base Setup R1

> **Legend**: 🔲 pending · ▶️ in progress · ✅ complete · ⚠️ blocked/gap

Canonical plan: [`SPAARKE-KNOWLEDGE-BASE-SETUP.md`](./SPAARKE-KNOWLEDGE-BASE-SETUP.md)

---

## Phase progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 0 | Verify environment and prerequisites | ✅ | gh 2.80.0 authed (spaarke-dev); learn.microsoft.com / github.com / raw.githubusercontent.com reachable; no existing `knowledge/` |
| 1 | Create `knowledge/` skeleton + README + REFRESH-LOG | ✅ | 11 topic dirs with `.gitkeep`; `knowledge/README.md` + `REFRESH-LOG.md` authored |
| 2 | Populate 11 topic folders (see breakdown below) | 🔲 | Parallelizable |
| 3 | Wire 6 new `.claude/skills/*` files | 🔲 | Main session only (write boundary) |
| 4 | Senior-engineer annotation pass on NOTES.md | 🔲 | **Ralph owns** — async after Phase 3 |
| 5 | Verify each skill influences agent output | 🔲 | 5 representative prompts per skill |
| 6 | Establish monthly refresh ritual | 🔲 | REFRESH-PROCEDURE.md + owner + calendar |

## Phase 2 — Topic populate (each = SOURCE.md + curated samples + stub NOTES.md)

| Topic | Status | Primary sources | Commit SHA |
|---|---|---|---|
| 2.1 `m365-copilot` | ✅ | **pnp/copilot-pro-dev-samples** (migrated from OfficeDev — see Gaps), microsoft/copilot-camp | `7db7c758` |
| 2.2 `mcp-apps` | ✅ | microsoft/mcp-interactiveUI-samples (modelcontextprotocol/servers not needed) | pending (batch 2) |
| 2.3 `declarative-agents` | 🔲 | OfficeDev/microsoft-365-copilot-samples (DA subdirs), microsoft/teams-toolkit, MVP samples | — |
| 2.4 `agent-framework` | ✅ | microsoft/agent-framework (.NET samples only — Spaarke is .NET 8) | `7db7c758` |
| 2.5 `foundry-agent-service` | ✅ | Azure-Samples/azureai-samples, **Azure-Samples/ai-foundry-agents-samples** (azure-ai-foundry 404 — see Gaps), Azure/ai-foundry-isv-mcp-agent | pending (batch 2) |
| 2.6 `foundry-iq` | 🔲 | Azure-Samples/azure-ai-foundry, Azure-Samples/azureai-samples | — |
| 2.7 `work-iq` | 🔲 | microsoft/copilot-camp (Work IQ MCP labs); docs snapshot (preview surface — gap risk) | — |
| 2.8 `dataverse-mcp` | ✅ | microsoft/PowerApps-Samples (sparse), **microsoft/Dataverse-MCP** + **microsoft/dataverse-business-skills** (Dataverse-Web-API-Samples doesn't exist — see Gaps) | pending (batch 2) |
| 2.9 `sharepoint-embedded` | 🔲 | microsoft/SharePoint-Embedded-Samples, microsoft/SharePoint-Embedded-VS-Code-Extension | — |
| 2.10 `azure-ai-search` | 🔲 | Azure/azure-search-vector-samples, Azure-Samples/azure-search-openai-demo, azure-search-openai-demo-csharp | — |
| 2.11 `github-mcp` | ✅ | github/github-mcp-server | `7db7c758` |

## Gaps / blocks log

**Batch 1 (2026-05-14):**
- ⚠️ **Repo migration** — `OfficeDev/microsoft-365-copilot-samples` was migrated by Microsoft on 2026-01-02 to **`pnp/copilot-pro-dev-samples`** and is retiring 2026-01-30. **Affects Phase 2.3 declarative-agents** which lists the same primary source — Batch 3 agent must be redirected to `pnp/copilot-pro-dev-samples`. Recorded in `knowledge/m365-copilot/SOURCE.md`.
- ⚠️ **404 on Learn doc paths** for Agent Framework concepts (`/concepts/agents`, `/concepts/workflows`) — Microsoft restructured to `/agents/` and `/workflows/`. Substitutions recorded in `knowledge/agent-framework/docs/*.md` frontmatter.
- ⚠️ **404 on GitHub Copilot "skillsets"** doc — Microsoft folded skillsets into Custom agents / Skills. Logged in `knowledge/github-mcp/SOURCE.md`; next refresh should hunt for canonical replacement.

**Batch 2 (2026-05-14):**
- ⚠️ **`Azure-Samples/azure-ai-foundry` 404** — replaced with **`Azure-Samples/ai-foundry-agents-samples`**. **Affects Phase 2.6 foundry-iq** — batch 3 agent must use the replacement. Also: `Azure/ai-foundry-isv-mcp-agent` is canonical for HITL approval patterns.
- ⚠️ **`microsoft/Dataverse-Web-API-Samples` does not exist** (directive had wrong name) — replaced with **`microsoft/Dataverse-MCP`** (Build 2025 labs) + **`microsoft/dataverse-business-skills`** (canonical Business Skill examples).
- ⚠️ **Azure AI Foundry Learn URL rebrand**: `/azure/ai-foundry/` → `/azure/foundry/`. 3 of 4 directive URLs 404'd; canonical replacements captured. **Affects Phase 2.6 foundry-iq** — batch 3 agent must search for current Foundry IQ doc paths.
- ⚠️ **Power Platform Dataverse MCP Learn URLs moved** to `/power-apps/maker/data-platform/`. All 4 directive URLs 404'd; canonical replacements captured.
- ⚠️ **`declarative-agent-ui-widgets` Learn doc consolidated** byte-identical into `plugin-mcp-apps` upstream — not separately snapshotted.
- ⚠️ **`wait_for_external_event` HITL primitive not in public SDK samples** — partial coverage via `McpTool.set_approval_mode("prompt")` + `SubmitToolApprovalAction`. Recorded as TODO in `foundry-agent-service/NOTES.md`.
- ⚠️ **A2A protocol composition + graph-based workflow DSL** — no public Python/.NET sample yet (workflow runtime is UI-first; A2A may be preview-only). Logged as gaps in `foundry-agent-service/SOURCE.md`.
- ⚠️ **No Microsoft Learn page for "App MCP" as distinct concept** or Business Skill authoring format — only normative source for Business Skills is the example library in `microsoft/dataverse-business-skills`.

**Pre-batch-3 redirections** (will be applied to batch 3 prompts):
- `2.3 declarative-agents`: use `pnp/copilot-pro-dev-samples` (not `OfficeDev/microsoft-365-copilot-samples`)
- `2.6 foundry-iq`: use `Azure-Samples/ai-foundry-agents-samples` (not `azure-ai-foundry`); search current `/azure/foundry/` Learn paths

---

## Phase 3 — Skills to create

| Skill file | References | Status |
|---|---|---|
| `.claude/skills/mcp-tool-handler/SKILL.md` | `knowledge/mcp-apps/`, `knowledge/foundry-agent-service/` | 🔲 |
| `.claude/skills/declarative-agent/SKILL.md` | `knowledge/declarative-agents/`, `knowledge/m365-copilot/` | 🔲 |
| `.claude/skills/foundry-agent/SKILL.md` | `knowledge/foundry-agent-service/`, `knowledge/foundry-iq/`, `knowledge/agent-framework/` | 🔲 |
| `.claude/skills/dataverse-mcp-usage/SKILL.md` | `knowledge/dataverse-mcp/` | 🔲 |
| `.claude/skills/spe-integration/SKILL.md` | `knowledge/sharepoint-embedded/` | 🔲 |
| `.claude/skills/widget-design/SKILL.md` | `knowledge/mcp-apps/` | 🔲 |
