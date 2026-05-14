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
| 2.1 `m365-copilot` | 🔲 | OfficeDev/microsoft-365-copilot-samples, microsoft/copilot-camp | — |
| 2.2 `mcp-apps` | 🔲 | microsoft/mcp-interactiveUI-samples, modelcontextprotocol/servers | — |
| 2.3 `declarative-agents` | 🔲 | OfficeDev/microsoft-365-copilot-samples (DA subdirs), microsoft/teams-toolkit, MVP samples | — |
| 2.4 `agent-framework` | 🔲 | microsoft/agent-framework | — |
| 2.5 `foundry-agent-service` | 🔲 | Azure-Samples/azureai-samples, Azure-Samples/azure-ai-foundry, Azure-Samples/openai-end-to-end-baseline | — |
| 2.6 `foundry-iq` | 🔲 | Azure-Samples/azure-ai-foundry, Azure-Samples/azureai-samples | — |
| 2.7 `work-iq` | 🔲 | microsoft/copilot-camp (Work IQ MCP labs); docs snapshot (preview surface — gap risk) | — |
| 2.8 `dataverse-mcp` | 🔲 | microsoft/PowerApps-Samples, microsoft/Dataverse-Web-API-Samples | — |
| 2.9 `sharepoint-embedded` | 🔲 | microsoft/SharePoint-Embedded-Samples, microsoft/SharePoint-Embedded-VS-Code-Extension | — |
| 2.10 `azure-ai-search` | 🔲 | Azure/azure-search-vector-samples, Azure-Samples/azure-search-openai-demo, azure-search-openai-demo-csharp | — |
| 2.11 `github-mcp` | 🔲 | github/github-mcp-server | — |

## Gaps / blocks log

(empty — populate as Microsoft URLs 404 or repos move; preview surfaces like Work IQ MCP are highest risk)

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
