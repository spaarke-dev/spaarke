# Task Index — Coding Knowledge Base Setup R1

> **Legend**: 🔲 pending · ▶️ in progress · ✅ complete · ⚠️ blocked/gap

Canonical plan: [`SPAARKE-KNOWLEDGE-BASE-SETUP.md`](./SPAARKE-KNOWLEDGE-BASE-SETUP.md)

---

## Phase progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 0 | Verify environment and prerequisites | ✅ | gh 2.80.0 authed (spaarke-dev); learn.microsoft.com / github.com / raw.githubusercontent.com reachable; no existing `knowledge/` |
| 1 | Create `knowledge/` skeleton + README + REFRESH-LOG | ✅ | 11 topic dirs with `.gitkeep`; `knowledge/README.md` + `REFRESH-LOG.md` authored |
| 2 | Populate 11 topic folders (see breakdown below) | ✅ | All 11 topics curated · 2.5 MB total · 11 commits via 4 parallel batches |
| 3 | Wire 6 new `.claude/skills/*` files | ✅ | 6 SKILL.md files (107–139 lines each); INDEX.md + root CLAUDE.md updated with knowledge/ refs |
| 4 | Senior-engineer annotation pass on NOTES.md | 🔲 | **Ralph owns** — see [`PHASE-4-ANNOTATION-HANDOFF.md`](./PHASE-4-ANNOTATION-HANDOFF.md) for recommended order + quality bar |
| 5 | Verify each skill influences agent output | ✅ | 6 verification sub-agents (1 prompt per skill) — 3 wired clean (mcp-tool-handler, foundry-agent, widget-design); 1 minor (dataverse-mcp-usage: no ADR refs, non-blocking); 2 had defects fixed in `bd87e019` (declarative-agent + spe-integration: sample dir path + ADR-005 framing + doc filename prefixes) |
| 6 | Establish monthly refresh ritual | ✅ | [`knowledge/REFRESH-PROCEDURE.md`](../../knowledge/REFRESH-PROCEDURE.md) — owner: Ralph Schroeder, cadence: 1st business day, budget: ~3 hrs |

## Phase 2 — Topic populate (each = SOURCE.md + curated samples + stub NOTES.md)

| Topic | Status | Primary sources | Commit SHA |
|---|---|---|---|
| 2.1 `m365-copilot` | ✅ | **pnp/copilot-pro-dev-samples** (migrated from OfficeDev — see Gaps), microsoft/copilot-camp | `7db7c758` |
| 2.2 `mcp-apps` | ✅ | microsoft/mcp-interactiveUI-samples (modelcontextprotocol/servers not needed) | `d79c3f78` |
| 2.3 `declarative-agents` | ✅ | **pnp/copilot-pro-dev-samples** (OfficeDev migrated), MCAPSTechConnect26 lab (inspected, not curated). teams-toolkit 404, no MVP DA repos found via gh search. | `0437c38f` |
| 2.4 `agent-framework` | ✅ | microsoft/agent-framework (.NET samples only — Spaarke is .NET 8) | `7db7c758` |
| 2.5 `foundry-agent-service` | ✅ | Azure-Samples/azureai-samples, **Azure-Samples/ai-foundry-agents-samples** (azure-ai-foundry 404 — see Gaps), Azure/ai-foundry-isv-mcp-agent | `d79c3f78` |
| 2.6 `foundry-iq` | ✅ | **microsoft/iq-series** (canonical), Azure-Samples/sharepoint-foundryIQ-secure-sync, MSFT-Innovation-Hub-India/FoundryIQ-kb-Sample1, RobertEichenseer/AgenticAI.FoundryIQ. (azure-ai-foundry still 404) | `0437c38f` |
| 2.7 `work-iq` | ✅ | microsoft/copilot-camp (BAF6 lab fragment). Doc-heavy: 6 Learn snapshots + synthesized tool catalog. **Naming clarified**: Agent 365 = control plane, Work IQ = intelligence layer. | `873d1a84` |
| 2.8 `dataverse-mcp` | ✅ | microsoft/PowerApps-Samples (sparse), **microsoft/Dataverse-MCP** + **microsoft/dataverse-business-skills** (Dataverse-Web-API-Samples doesn't exist — see Gaps) | `d79c3f78` |
| 2.9 `sharepoint-embedded` | ✅ | microsoft/SharePoint-Embedded-Samples (C# + TS); SharePoint-Embedded-VS-Code-Extension (README only — extension, no runnable samples) | `0437c38f` |
| 2.10 `azure-ai-search` | ✅ | Azure/azure-search-vector-samples, Azure-Samples/azure-search-openai-demo-csharp (Spaarke .NET focus), Azure-Samples/azure-search-openai-demo (Python skillset reference only) | `873d1a84` |
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

**Pre-batch-3 redirections applied successfully.**

**Batch 3 (2026-05-14):**
- ⚠️ **`microsoft/teams-toolkit` 404** — renamed to "M365 Agents Toolkit" VS Code extension; no canonical GitHub repo at the directive's name. No samples lost (declarative-agents had ample sources elsewhere).
- ⚠️ **MVP declarative-agent repos** — `gh search` did not surface any `declarative-agent`-named repos under bobgerman/garrytrinder/waldekmastykarz at this snapshot. Recorded; next refresh should hunt again.
- ⚠️ **EmbeddedKnowledge capability** flagged "not yet available" on Microsoft Learn — no sample to curate.
- ⚠️ **All 3 Foundry IQ Learn URLs 404'd**. Replacements found under **`/azure/foundry/agents/concepts/`** and surprisingly under **`/azure/search/`** for some KB-creation pages (`agentic-retrieval-how-to-create-knowledge-base`, `agentic-knowledge-source-overview`).
- ⚠️ **SPE Learn URL** `concepts/app-concepts/containers` 404'd — fell back to `development/app-architecture` + `getting-started/containertypes`.
- ⚠️ **No runnable SPE embedded-chat sample** — the boilerplate's `ChatSidebar.tsx` body is commented out upstream (out-of-registry .tgz). Captured the annotated prompt snippet as next-best reference.
- ⚠️ **Foundry IQ gaps**: standalone `remoteSharePoint` KS sample, OneLake KS sample, end-to-end retrieval pipeline tutorial, native Purview sensitivity label enforcement — all preview/not-yet-public.

---

## Phase 3 — Skills to create

| Skill file | References | Status |
|---|---|---|
| `.claude/skills/mcp-tool-handler/SKILL.md` | `knowledge/mcp-apps/`, `knowledge/foundry-agent-service/` | ✅ (107 lines) |
| `.claude/skills/declarative-agent/SKILL.md` | `knowledge/declarative-agents/`, `knowledge/m365-copilot/` | ✅ (110 lines) |
| `.claude/skills/foundry-agent/SKILL.md` | `knowledge/foundry-agent-service/`, `knowledge/foundry-iq/`, `knowledge/agent-framework/` | ✅ (120 lines) |
| `.claude/skills/dataverse-mcp-usage/SKILL.md` | `knowledge/dataverse-mcp/` | ✅ (121 lines) |
| `.claude/skills/spe-integration/SKILL.md` | `knowledge/sharepoint-embedded/` | ✅ (122 lines) |
| `.claude/skills/widget-design/SKILL.md` | `knowledge/mcp-apps/` | ✅ (139 lines) |
