# Knowledge Base Refresh Log

> Chronological log of refresh activity, curation events, gaps encountered, and interim updates between scheduled refreshes.

Format per entry: `## YYYY-MM-DD — <type>` where type is `Initial setup`, `Monthly refresh`, `Interim update`, or `Gap`.

---

## 2026-05-14 — Initial setup

- Created `knowledge/` directory skeleton with 11 topic subdirectories.
- Authored `knowledge/README.md` documenting conventions, topics, and refresh cadence.
- Refresh owner: Ralph Schroeder.
- Curation of topic folders proceeds in Phase 2 of the [`coding-knowledge-base-setup-r1`](../projects/coding-knowledge-base-setup-r1/) project.

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
