# Spaarke AI Platform Unification R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-15
> **Source**: design.md
> **Branch**: `work/spaarke-ai-platform-unification-r1`

## Executive Summary

Unify Spaarke's AI capabilities into a standalone three-pane Code Page (`sprk_spaarkeai`) with chat, output/work, and research/source panes — then integrate with Azure AI Foundry Agent Service for enhanced capabilities (Code Interpreter, Bing Grounding). Build three shared AI libraries (`@spaarke/ai-context`, `@spaarke/ai-outputs`, `@spaarke/ui-components` extensions) to enable reuse across all surfaces. 80% of the work leverages existing production components; 20% is new code.

## Scope

### In Scope
- `sprk_spaarkeai` standalone Code Page with three-pane layout (chat + output + source)
- `ThreePaneLayout` shared component in `@spaarke/ui-components`
- `@spaarke/ai-context` shared library (context providers, service clients, hooks)
- `@spaarke/ai-outputs` shared library (output pane widgets, source pane widgets, component registries)
- `StandaloneAiContext` — new context provider for entity-scoped standalone sessions
- `StandaloneChatContextProvider` — BFF service resolving playbooks/tools for standalone surface
- `GET /api/ai/chat/context-mappings/standalone` — new BFF endpoint
- Output pane component registry with 11 purpose-built widgets
- Source pane component registry with 6 purpose-built widgets
- SSE event types for pane control (`output_pane`, `source_pane`, `source_highlight`)
- Chat history panel (session list with search)
- Launch points (workspace button, entity form buttons, deep-link, M365 handoff)
- AI Foundry Agent Service integration (`AgentServiceClient`, `AgentServiceNodeExecutor` AT 60)
- Code Interpreter bridge (data analysis, chart generation via Python sandbox)
- Bing Grounding integration (legal research, company research with citations)
- `CodeInterpreterTools.cs` + `LegalResearchTools.cs` — 2 new SprkChat tool classes
- `AgentServiceRoutingMiddleware` — routing decision tree (direct vs Agent Service)
- Spaarke Legal AI Agent definition deployed to Foundry with BFF tools registered
- Evaluation pipeline with legal-specific metrics (groundedness, citation accuracy)
- OpenTelemetry tracing for agent routing and Foundry calls
- Refactor `AnalysisAiContext` from AnalysisWorkspace to `@spaarke/ai-context`
- Dark mode support for all output/source widgets
- BYOK-compatible configuration (all Foundry resources via environment variables)

### Out of Scope
- Multi-agent orchestration (agent-to-agent collaboration)
- Custom model fine-tuning in Foundry
- Semantic Kernel migration
- Agent marketplace (customer-authored agents)
- Voice input/output
- Real-time collaboration (multiple users in same chat)
- Offline / disconnected mode

### Affected Areas
- `src/solutions/SpaarkeAi/` — new Code Page (React 19, Vite single-file)
- `src/client/shared/Spaarke.AI.Context/` — new shared library
- `src/client/shared/Spaarke.AI.Outputs/` — new shared library
- `src/client/shared/Spaarke.UI.Components/` — add ThreePaneLayout, extend PanelSplitter
- `src/client/code-pages/AnalysisWorkspace/` — refactor to import from `@spaarke/ai-context`
- `src/server/api/Sprk.Bff.Api/Api/Ai/` — new standalone context endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` — new StandaloneChatContextProvider, routing middleware
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` — 2 new tool classes
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` — AgentServiceNodeExecutor (AT 60)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` — AgentServiceClient, CodeInterpreterBridge
- `infrastructure/ai-foundry/` — agent definition, tool registration

## Requirements

### Functional Requirements

**Phase 1: Standalone Code Page + Shared Libraries**
1. **FR-01**: Three-pane layout renders with chat (left, always visible), output (center, dynamic), and source (right, collapsible) — Acceptance: All panes render, splitters are draggable, source pane collapses/expands
2. **FR-02**: `StandaloneAiContext` resolves entity context from URL parameters (`?matterId=`, `?projectId=`, `?documentId=`) — Acceptance: Opening with `?matterId=abc` loads matter context, playbooks, and scoped tools
3. **FR-03**: BFF `StandaloneChatContextProvider` returns playbooks, tools, quick actions, and slash commands scoped to the resolved entity — Acceptance: Context response includes correct playbooks for matter type
4. **FR-04**: All existing SprkChat capabilities (streaming, tools, chips, commands, upload) work in standalone mode — Acceptance: Each of the 7 tool categories executes successfully from standalone surface
5. **FR-05**: Output pane renders purpose-built widgets from component registry based on `output_pane` SSE events — Acceptance: Budget dashboard, search results, analysis editor all render correctly
6. **FR-06**: Source pane renders reference material from component registry based on `source_pane` SSE events — Acceptance: Document viewer opens SPE file at specified page, web sources show citations
7. **FR-07**: Cross-pane linking works — clicking a citation in output pane navigates source pane to that reference — Acceptance: `source_highlight` SSE event scrolls and highlights referenced section
8. **FR-08**: Chat history panel shows previous sessions with search — Acceptance: User can view, search, and resume prior conversations
9. **FR-09**: Launch points work from workspace command bar, entity form command bar, deep-link URL, and M365 Copilot handoff — Acceptance: Each launch point opens standalone AI with correct entity context
10. **FR-10**: `@spaarke/ai-context` library extracts context providers and service clients from SprkChat hooks — Acceptance: `useChatSession`, `useChatContextMapping`, `useChatPlaybooks` work from new library
11. **FR-11**: `@spaarke/ai-outputs` library provides output + source widget registries — Acceptance: Registries resolve correct component for each output type
12. **FR-12**: `AnalysisAiContext` refactored to `@spaarke/ai-context` — Analysis Workspace imports from shared library with no behavior change — Acceptance: Analysis Workspace works identically after refactor

**Phase 2: AI Foundry Agent Service Integration**
13. **FR-13**: `AgentServiceClient` wraps Azure.AI.Projects SDK for thread/run operations — Acceptance: Create thread, send message, stream response all work
14. **FR-14**: `AgentServiceNodeExecutor` (ActionType 60) executes as a playbook node, delegating to AI Agent Service — Acceptance: Playbook with AT 60 node produces result from Agent Service
15. **FR-15**: Code Interpreter generates charts and data analysis from matter financial data — Acceptance: "Show budget burndown" produces chart image rendered in output pane
16. **FR-16**: Bing Grounding returns legal research with structured citations — Acceptance: Research results show source URLs, case names, and attribution
17. **FR-17**: `AgentServiceRoutingMiddleware` routes complex queries to Agent Service and standard queries through existing pipeline — Acceptance: Routing is transparent to user; correct path chosen based on intent
18. **FR-18**: New SprkChat tools (`CodeInterpreterTools`, `LegalResearchTools`) register in tool framework — Acceptance: `/analyze-data` and `/research` commands invoke correct tools

**Phase 3: Evaluation & Quality**
19. **FR-19**: Evaluation pipeline runs automatically on playbook outputs with legal-specific metrics — Acceptance: Groundedness, relevance, citation accuracy scores generated
20. **FR-20**: OpenTelemetry tracing captures full request lifecycle (frontend → BFF → Foundry → response) — Acceptance: Traces visible in Application Insights with correct span hierarchy

### Non-Functional Requirements
- **NFR-01**: Output pane widget rendering < 200ms after SSE event received
- **NFR-02**: Source pane document load < 2 seconds for files up to 10MB (via SpeFileStore)
- **NFR-03**: Agent Service routing decision < 50ms (intent classification, not LLM call)
- **NFR-04**: All output/source widgets support dark mode via Fluent v9 semantic tokens
- **NFR-05**: BYOK deployment works with customer-provisioned Foundry resources (all config via env vars)
- **NFR-06**: No regression in Analysis Workspace performance after `AnalysisAiContext` refactor

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API — all AI calls through BFF, BackgroundService for scheduling
- **ADR-006**: Code Page for standalone `sprk_spaarkeai` (not PCF)
- **ADR-007**: SpeFileStore facade — SPE access in source pane goes through BFF
- **ADR-008**: Endpoint filters for new standalone context endpoints
- **ADR-009**: Redis caching for agent sessions, Foundry thread IDs
- **ADR-010**: DI minimalism — max 3 new registrations (AgentServiceClient, StandaloneChatContextProvider, routing middleware)
- **ADR-012**: Shared component library — ThreePaneLayout, PanelSplitter in `@spaarke/ui-components`
- **ADR-013**: AI features extend BFF — Foundry integration is additive, not replacement
- **ADR-021**: Fluent v9 for all output/source pane widgets; dark mode required
- **ADR-026**: Vite single-file build for standalone Code Page

### MUST Rules
- MUST use existing `SprkChatAgent` pipeline for standard queries — Agent Service is additive, not replacement
- MUST route all AI calls through BFF — frontend never calls AI Foundry directly
- MUST use `IChatClient` abstraction — agent routing is transparent to consumers
- MUST support all 3 deployment models (multi-customer, dedicated, customer tenant) via env var config
- MUST separate AI context/services (`@spaarke/ai-context`) from AI output widgets (`@spaarke/ai-outputs`)
- MUST NOT require Semantic Kernel — existing playbook engine is more capable for legal ops
- MUST NOT break existing Analysis Workspace integration
- MUST NOT hardcode Foundry resource IDs, agent IDs, or tenant IDs
- MUST label AI-generated charts/research clearly with source attribution
- MUST use `PanelSplitter` from `@spaarke/ui-components` for three-pane layout (same as Analysis Workspace)

### Existing Patterns to Follow
- Three-pane layout: `src/client/code-pages/AnalysisWorkspace/src/App.tsx`
- Code Page bootstrap: `src/solutions/EventsPage/src/main.tsx`
- BFF endpoint group: `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`
- Tool registration: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/`
- Node executor: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`
- Context provider: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs`

### Architecture Reference Documents
- `docs/architecture/playbook-architecture.md` — Playbook engine, node executors, orchestration
- `docs/architecture/AI-ARCHITECTURE.md` — AI platform overview, tool framework
- `docs/guides/JPS-AUTHORING-GUIDE.md` — Playbook design, node types
- `infrastructure/ai-foundry/README.md` — Foundry Hub, Project, Prompt Flows

## Success Criteria

1. [ ] Standalone three-pane Code Page renders and functions without Analysis Workspace — Verify: open `sprk_spaarkeai` and complete a full conversation with output pane rendering
2. [ ] Entity context (matter/project/document) loads from URL parameters — Verify: open with `?matterId=` and confirm scoped playbooks/tools
3. [ ] All 7 existing tool categories work in standalone mode — Verify: test each tool (search, analysis, refinement, etc.)
4. [ ] Output pane renders purpose-built widgets (not markdown) — Verify: trigger budget dashboard, search results, analysis editor
5. [ ] Source pane loads SPE documents and highlights cited sections — Verify: analysis cites Section 7.2, source pane scrolls to it
6. [ ] Cross-pane linking works (output citation → source navigation) — Verify: click citation, source pane navigates
7. [ ] `@spaarke/ai-context` and `@spaarke/ai-outputs` are separate installable libraries — Verify: `import { StandaloneAiContext } from '@spaarke/ai-context'`
8. [ ] Analysis Workspace works identically after `AnalysisAiContext` refactor — Verify: full regression test
9. [ ] Code Interpreter generates charts from matter financial data — Verify: "budget burndown" produces rendered chart in output pane
10. [ ] Bing Grounding returns legal research with structured citations — Verify: research query returns sources with URLs
11. [ ] Agent Service routing is transparent to user — Verify: user cannot tell which path was used
12. [ ] Evaluation pipeline produces quality scores — Verify: automated metrics generated for sample playbook runs
13. [ ] Works in all 3 deployment models — Verify: test with multi-customer, dedicated, and BYOK config
14. [ ] Dark mode renders correctly across all output/source widgets — Verify: toggle theme, inspect all widgets
15. [ ] BYOK deployment works with customer-provisioned Foundry resources — Verify: change env vars to customer endpoints, confirm functionality

## Dependencies

### Prerequisites
- Azure AI Foundry Hub + Project (exists: `sprkspaarkedev-aif-hub`, `sprkspaarkedev-aif-proj`)
- Azure OpenAI deployment (exists: `spaarke-openai-dev`)
- AI Search index (exists: `spaarke-search-dev`)
- `SprkChat` shared component (exists in `@spaarke/ui-components`)
- `SprkChatAgent` + 7 tool categories (exists in BFF)
- `PlaybookOrchestrationService` + 12 node executors (exists in BFF)
- `PanelSplitter` component (exists in `@spaarke/ui-components`)

### New Azure Resources
- AI Agent Service provisioning (part of existing Foundry project)
- Bing Grounding resource (if legal research feature enabled)
- F-SKU capacity for Agent Service (shared with existing Foundry project)

### External Dependencies
- `Azure.AI.Projects` NuGet package (Agent Service SDK)
- No new npm packages — `powerbi-client-react` pattern for embedding is not needed here

### Related Projects
- `ai-m365-copilot-integration-r1` — M365 surface, Declarative Agent with handoff
- `spaarke-daily-update-service` — notification playbooks use same engine
- `ai-sprk-chat-extensibility-r1` — context enrichment patterns inform `StandaloneAiContext`
- `spaarke-workspace-user-configuration-r1` — workspace may embed standalone AI as a section

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Rich output rendering | Inline in chat or separate pane? | Separate output pane — same pattern as Analysis Workspace | Three-pane layout with component registry |
| AI shared libraries | Combine UX + services or separate? | Separate — `ai-context` (services) and `ai-outputs` (widgets) | 3 libraries, clear dependency chain |
| M365 Copilot scope | Primary surface or handoff? | Declarative Agent with handoff only — not primary | Reduced M365 scope, focus on standalone |
| SPE file access | New integration needed? | No — source pane uses same `SpeFileStore` endpoints | No new SPE work |
| Three-pane source pane | Always visible? | Collapsible — expands when AI loads reference material | Source pane has collapse/expand toggle |

## Assumptions

- **AnalysisAiContext migration**: Refactoring to `@spaarke/ai-context` is non-breaking — Analysis Workspace becomes thin shell importing from shared library
- **Agent Service availability**: Azure AI Agent Service is GA and available in West US 2 region
- **Foundry capacity**: F-SKU capacity sufficient for both existing Prompt Flows and new Agent Service
- **Output widget count**: 11 output + 6 source widgets in R1 — additional widgets added as needed without architecture changes

## Unresolved Questions

- [ ] **Agent Service function calling limits**: How many BFF functions can be registered as Agent Service tools before performance degrades? — Blocks: tool registration scope decision
- [ ] **Code Interpreter file size limits**: Max file size for CSV/data uploads to Code Interpreter sandbox? — Blocks: budget data export format
- [ ] **Bing Grounding legal source quality**: Does Bing Grounding return case law from authoritative sources (Justia, PACER, Westlaw-indexed)? — Blocks: legal research feature confidence level

---

*AI-optimized specification. Original: design.md*
