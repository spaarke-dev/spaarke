# Knowledge Base Refresh Log

> Chronological log of refresh activity, curation events, gaps encountered, and interim updates between scheduled refreshes.

Format per entry: `## YYYY-MM-DD — <type>` where type is `Initial setup`, `Monthly refresh`, `Interim update`, or `Gap`.

---

## 2026-05-14 — Project complete (Phases 0-3, 5, 6)

All AI-executable phases of `coding-knowledge-base-setup-r1` complete:
- **Phase 0**: env verified (gh authed, network reachable, no existing `knowledge/`)
- **Phase 1**: skeleton created (11 topic dirs, README, REFRESH-LOG)
- **Phase 2**: all 11 topics curated · 2.5 MB · 226 files · 4 parallel batches
- **Phase 3**: 6 trigger-loadable skills wired (107-139 lines each); INDEX.md + root CLAUDE.md registered
- **Phase 5**: 6 verification sub-agents (1 representative prompt each) — 3 defects found and fixed in commit `bd87e019`
- **Phase 6**: `REFRESH-PROCEDURE.md` established; monthly cadence on first business day; owner Ralph

**Phase 4** (senior-engineer annotation pass on stub NOTES.md files) remains — owned by Ralph. See `projects/coding-knowledge-base-setup-r1/PHASE-4-ANNOTATION-HANDOFF.md` for recommended annotation order, quality bar, and per-topic guidance.

The knowledge base is **functional now** — skills load on triggers and read curated samples + provenance. Annotation pass converts honest stubs into substantive Spaarke commentary, dramatically improving agent output on Microsoft platform tasks.

## 2026-05-14 — Initial setup

- Created `knowledge/` directory skeleton with 11 topic subdirectories.
- Authored `knowledge/README.md` documenting conventions, topics, and refresh cadence.
- Refresh owner: Ralph Schroeder.
- Curation of topic folders proceeds in Phase 2 of the [`coding-knowledge-base-setup-r1`](../projects/coding-knowledge-base-setup-r1/) project.

## 2026-05-14 — Initial curation (Batch 4: work-iq, azure-ai-search) — Phase 2 complete

Final batch gaps and platform changes:

- **Work IQ MCP — naming clarification.** The directive's framing "Work IQ MCP was rebranded from Agent 365 MCP" is partially correct but more nuanced. After research: **Agent 365 = control plane** (admin/governance), **Work IQ = intelligence layer / MCP catalog**. They're distinct products now. Remote server URL pattern: `https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/{serverId}`. 9 Work IQ MCP servers + MCP Management Server documented.
- **Work IQ Learn coverage richer than feared.** Directive expected sparse public docs; in fact Microsoft Learn has full catalog (Mail = 10 tools, Calendar = 11 tools, Teams = 24 tools) plus `.mcp.json` config examples for Claude Code / GitHub Copilot CLI / VS Code. 5 server reference pages (SharePoint, OneDrive, User, Word, Dataverse) remain unfetched — **next-refresh action item**.
- **No public agent manifest** wires Work IQ MCP in (yet). No Microsoft Mechanics transcript page is curatable.
- **Original directive Learn URL `work-iq-mcp` 404'd.** Real page is `agent-365/tooling/servers-overview`.
- **Azure AI Search C# samples** — preferred over Python per Spaarke .NET 8 stack. `azure-search-openai-demo-csharp` curated; Python sibling referenced only.
- **No first-party C# sample** for Document Intelligence layout-skill chunking — used Python custom-skill sample (platform-side, language-agnostic).
- **No SPE-specific AI Search indexer** exists (SharePoint Online indexer is the closest; left as `NOTES.md` TODO).

## 2026-05-14 — Initial curation (Batch 3: declarative-agents, foundry-iq, sharepoint-embedded)

Additional gaps and platform changes discovered:

- **`microsoft/teams-toolkit` 404.** Renamed to "M365 Agents Toolkit" VS Code extension — no canonical GitHub repo at the directive's name. No samples lost.
- **MVP declarative-agent repos** — `gh search` did not surface `declarative-agent`-named repos under bobgerman/garrytrinder/waldekmastykarz at this snapshot. Try again next refresh.
- **EmbeddedKnowledge capability** flagged "not yet available" on Microsoft Learn — no sample exists yet.
- **All 3 Foundry IQ Learn URLs 404'd.** Knowledge layer pages have moved partly under `/azure/foundry/agents/concepts/` and surprisingly partly under `/azure/search/` (`agentic-retrieval-how-to-create-knowledge-base`, `agentic-knowledge-source-overview`).
- **SPE Learn URL** `concepts/app-concepts/containers` 404'd — replaced with `development/app-architecture` + `getting-started/containertypes`.
- **No runnable SPE embedded-chat sample** — boilerplate's `ChatSidebar.tsx` body commented out upstream (out-of-registry .tgz). Annotated prompt snippet captured as next-best reference.

## 2026-05-14 — Initial curation (Batch 2: mcp-apps, foundry-agent-service, dataverse-mcp)

Additional gaps and platform changes discovered:

- **`Azure-Samples/azure-ai-foundry` retired.** No 30x redirect. Replaced with `Azure-Samples/ai-foundry-agents-samples` (canonical Python samples) and `Azure/ai-foundry-isv-mcp-agent` (HITL approval patterns). Affects topics: `foundry-agent-service`, pending `foundry-iq`.
- **`microsoft/Dataverse-Web-API-Samples` does not exist** in any current `microsoft/*` namespace. Directive had incorrect repo name. Substituted `microsoft/Dataverse-MCP` (Build 2025 labs) + `microsoft/dataverse-business-skills` (canonical authoring format).
- **Azure AI Foundry Learn URL rebrand.** `/azure/ai-foundry/...` → `/azure/foundry/...`. Affects all upcoming Foundry-topic doc snapshots.
- **Power Platform Dataverse MCP Learn URLs moved** to `/power-apps/maker/data-platform/`. All 4 directive URLs 404'd; canonical replacements captured.
- **`declarative-agent-ui-widgets` Learn doc consolidated** byte-identical into `plugin-mcp-apps` upstream — single source going forward.
- **`wait_for_external_event` HITL primitive** absent from public SDK samples. Partial coverage via `McpTool.set_approval_mode("prompt")` + `SubmitToolApprovalAction`.
- **A2A protocol composition + graph-based workflow DSL** — no public Python/.NET samples yet. Workflow runtime is UI-first (YAML export only); A2A may be preview-only.
- **No Microsoft Learn page** for Business Skill authoring format or "App MCP" as distinct concept — example library in `microsoft/dataverse-business-skills` is the only normative source.

## 2026-05-14 — Initial curation (Batch 1: m365-copilot, agent-framework, github-mcp)

Gaps and platform changes discovered during curation:

- **`OfficeDev/microsoft-365-copilot-samples` retired.** Microsoft migrated this repo to `pnp/copilot-pro-dev-samples` on 2026-01-02; original retiring 2026-01-30. Now-canonical primary source for declarative agent samples. Affects topics: `m365-copilot`, `declarative-agents`.
- **Agent Framework Learn doc restructure.** `/concepts/agents` → `/agents/`, `/concepts/workflows` → `/workflows/`. Snapshots in `knowledge/agent-framework/docs/` reference the current locations.
- **GitHub Copilot "skillsets" doc retired.** No direct replacement located in current docs TOC — appears folded into Custom agents / Skills. Recorded in `knowledge/github-mcp/SOURCE.md` for next refresh to hunt.
