# Knowledge Base Refresh Log

> Chronological log of refresh activity, curation events, gaps encountered, and interim updates between scheduled refreshes.

Format per entry: `## YYYY-MM-DD — <type>` where type is `Initial setup`, `Monthly refresh`, `Interim update`, or `Gap`.

---

## 2026-05-26 — New topic: `fluent-ui-v9/` (Microsoft Fluent UI v9 + Fluent 2)

Added a new top-level topic for **Microsoft Fluent UI React v9** + the broader **Fluent 2 design system**. Driver: Spaarke ships Fluent v9 across 10+ PCFs, 4+ Code Pages, the external SPA, and the Office add-ins; `Spaarke.UI.Components` is on `@fluentui/react-components ^9.73.2` while several PCFs are still on `^9.46.2` (drift candidate). Curation matches existing topic conventions (`SOURCE.md` + stub `NOTES.md` + `docs/` snapshots + `samples/`).

**Source repos cloned**:
- `microsoft/fluentui` @ `0aa62de59fe5845eeba40c9028d527fd93d88f27` (2026-05-26)
- `microsoft/PowerApps-Samples` @ `a6d30c10d17938fbeb85245e57a4a2cb435c71c8` (2026-04-02)

**Curated samples** (~120 KB total):
- `PowerApps-Samples_FluentThemingAPIControl/` — the canonical PCF + Fluent v9 + modern theming sample (four theming approaches).
- `fluentui_react-v9/Provider/{Default, Nested, ApplyStylesToPortals}.stories.tsx` — `FluentProvider` patterns including the critical Portal styling gotcha.
- `fluentui_react-v9/Button/{Appearance, Icon}.stories.tsx` — slot composition examples.
- `fluentui_react-v9/Theme/{Colors, Spacing}.stories.tsx` — token reference.

**Docs snapshotted (Microsoft official)**: `overview.md`, `quickstart.md`, `theming.md`, `styling-griffel.md`, `slots-architecture.md`, `react-version-support.md`, `accessibility.md`, `pcf-modern-theming.md`, `pcf-virtual-controls.md`, `modern-theming-api-control-sample.md`, `fluent2-overview.md`, `fluent2-develop.md`, `fluent2-design-principles.md`, `fluent2-whats-new.md`.

**Docs snapshotted (community / MVPs)**: Diana Birkelbach × 3 (style PCFs / virtual after GA / standard-vs-custom theming), David Rivard × 2 (develop PCF with v9 / adapting PCFs for the new look), Aric Levin × 1 (virtual PCF init walkthrough), Paul Gildea × 2 (what's new in v9 / custom variants).

**GAPs logged**:
- `react.fluentui.dev/` is JS-rendered Storybook — WebFetch can't extract. Substituted with the upstream MDX files in `microsoft/fluentui/apps/public-docsite-v9/src/Concepts/` (which are the Storybook source). URLs retained in frontmatter for human reference.
- `microsoft.com/en-us/power-platform/blog/.../virtual-code-components-...` — JS-heavy; WebFetch returned semi-summarized content. Captured as orientation; marked verification caveat in frontmatter.
- Clavin Fernandes 2025-01-21 PCF virtual control post — coverage overlaps Birkelbach + Levin + Rivard. Skipped to avoid redundancy. Add at next refresh if it grows distinctive content.
- Fluent 2 cross-platform design-token reference (`fluent2.microsoft.design/design-tokens/...`) — not curated; React-side covered in `theming.md`. Add at next refresh if cross-platform consumers emerge.

**Phase 4 follow-ups** (senior engineer annotation):
- `NOTES.md` is intentionally a stub with structured TODOs spanning §1 architecture map + §2 build conventions. The code review checklist starter in §2 is the most immediately-useful section once filled in.

**Skills not yet wired** to this topic — opportunities surfaced for future work: a `fluent-v9-component` skill (when authoring a new component in `Spaarke.UI.Components`), and additions to `pcf-deploy` referencing the platform-libraries + modern-theming setup. Tracked as a separate task.

## 2026-05-19 — Insights Engine pre-design research (researcher subagent)

Added four new reference-only topics + one supplement, ahead of `projects/ai-spaarke-insights-engine-r1/` design work:

- **NEW** [`cosmos-gremlin/`](./cosmos-gremlin/) — current state of Cosmos DB Gremlin (2026), partitioning, RU sizing for 1M-10M vertices, and a comparison vs. Cosmos NoSQL + adjacency / Postgres+AGE / Neo4j Aura. Note: Microsoft's product investment is visibly moving away from Gremlin; documented migration triggers.
- **NEW** [`azure-functions-isv/`](./azure-functions-isv/) — Flex Consumption hosting, per-tenant Bicep + UAMI + Service Bus topic pattern, Application Insights correlation across BFF + Functions, cold-start mitigations.
- **NEW** [`dataverse-sync/`](./dataverse-sync/) — webhook vs. Service Bus vs. change-tracking trigger comparison, idempotent indexing patterns, schema evolution, backfill strategies. Why Microsoft 365 Copilot connectors are NOT the right answer for application-side AI Search.
- **NEW** [`foundry-memory-patterns/`](./foundry-memory-patterns/) — Foundry memory primitive now publicly documented (2026-04 docs); two-tier memory pattern (user_profile / chat_summary), scope-based partitioning, decision rubric for Foundry-hosted vs. custom BFF agent. Supersedes the `foundry-agent-service/docs/GAP-memory.md` entry.
- **SUPPLEMENT** [`azure-ai-search/insights-engine-supplement.md`](./azure-ai-search/insights-engine-supplement.md) — Insights Engine-specific Q&A on integrated vectorization, ACL trimming via `vectorFilterMode=preFilter`, index schema for Observations, tier pricing for 1M-10M artifacts.

All five documents are reference-only (no curated sample code). Future refreshes should:
1. Curate runnable samples for `azure-functions-isv` (per-tenant Bicep + Flex Consumption Function App).
2. Curate Dataverse change-tracking + Service Bus integration samples for `dataverse-sync`.
3. Re-fetch Foundry memory docs to track GA pricing model.
4. Re-evaluate Cosmos Gremlin product direction (deprecation watch).

The `foundry-agent-service/docs/GAP-memory.md` entry should be updated next refresh to point at `foundry-memory-patterns/README.md`.

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
