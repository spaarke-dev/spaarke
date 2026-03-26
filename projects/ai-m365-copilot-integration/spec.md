# M365 Copilot in Power Apps — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-26
> **Source**: design.md
> **Target**: M365 Copilot GA in MDA (April 13, 2026)

## Executive Summary

Integrate Spaarke's AI stack — playbook engine, Azure OpenAI, custom RAG pipeline, and SPE document access — into the M365 Copilot side pane on Dataverse model-driven app forms. M365 Copilot replaces SprkChat as the general-purpose chat AI surface across all MDA pages; SprkChat is repositioned as a special-purpose AI companion exclusively for the Analysis Workspace (editor integration, inline toolbar, streaming write-back).

The integration uses two tiers: a **Declarative Agent with API Plugin** (Tier 1, ships for GA day) and a **Custom Engine Agent** (Tier 2, high priority — full control over AI orchestration, playbook execution, and multi-step interactions).

## Scope

### In Scope

- **Declarative Agent + API Plugin** — three manifest files (`declarativeAgent.json`, `spaarke-api-plugin.json`, `spaarke-bff-openapi.yaml`) deployed via M365 Agents Toolkit
- **Custom Engine Agent** (high priority) — M365 Agents SDK `SpaarkeAgentHandler`, agent gateway BFF endpoint, full AI orchestration through our stack
- **BFF Agent Gateway** — `POST /api/agent/message` endpoint translating agent activities to BFF chat/tool operations
- **Playbook Menu Endpoint** — `GET /api/agent/playbooks?documentType={type}` returning available playbooks for document context
- **Playbook Invocation Endpoint** — `POST /api/agent/run-playbook` executing playbooks and returning structured results
- **Adaptive Card Templates** — document list, matter card, risk findings, playbook menu, email preview, task confirmation, progress indicator
- **Adaptive Card Formatter** — service transforming playbook output JSON → Adaptive Card JSON
- **SSO Token Flow** — M365 → OBO → BFF token exchange (extends existing Outlook add-in OBO pattern)
- **Azure Bot Service Registration** — bot registration, channel config, Teams app manifest
- **Handoff URL Builder** — deep-link generation for Analysis Workspace, wizard Code Pages
- **Document Attachment Flow** — retrieve user-attached files from OneDrive via `ConversationFileReference` → BFF → SPE upload or quick analysis
- **Agent 365 Registration** — register Spaarke agents in Microsoft's enterprise control plane (proceed as if available)
- **Admin Configuration** — enable/disable Copilot per app, per environment
- **Telemetry** — Copilot interaction logging, playbook invocation metrics, handoff tracking
- **Error Handling** — graceful degradation for BFF unavailable, token failures, playbook timeouts

### Out of Scope

- **MCP Server** — deferred to R2; not needed when we control both sides (BFF + agent)
- **Teams bot channel** — different UX patterns, deferred to R2
- **Outlook plugin** — requires message extension architecture, deferred to R2
- **Copilot Chat standalone** — broader distribution, different auth model, deferred to R2
- **Power Pages Agent API** — external portal, different security model, deferred to R2
- **BYOK deployment** — not in scope, but architecture MUST support customer-tenant deployment (environment variables, no hardcoded env params)
- **SprkChat modifications** — SprkChat continues as-is in Analysis Workspace; no changes in this project
- **Cross-agent orchestration** — multi-agent delegation, deferred to R2

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — new agent gateway endpoints, playbook menu endpoint, OpenAPI spec generation
- `src/server/api/Sprk.Bff.Api/Api/Ai/` — new AI-related endpoints for agent interactions
- `src/server/api/Sprk.Bff.Api/Infrastructure/` — SSO/OBO token flow, Adaptive Card formatter service
- `src/client/shared/Spaarke.UI.Components/` — handoff URL builder utility (if shared)
- `src/client/webresources/` — wizard command updates for deep-link parameter handling
- New: agent manifest files (declarativeAgent.json, API plugin, OpenAPI spec)
- New: Adaptive Card JSON templates
- New: Azure Bot Service registration configuration

## Requirements

### Functional Requirements

1. **FR-01: Matter Dashboard Queries (UC-M1)** — User asks Copilot natural language questions about matters, tasks, assignments. Copilot returns structured Adaptive Card results with action buttons. Acceptance: "What are my overdue tasks?" returns correct task list with [Open Task] deep-links.

2. **FR-02: Document Search + Playbook Selection (UC-M2)** — User describes a document; agent searches SPE via BFF, returns document card with available playbook options as `Action.Submit` buttons. Acceptance: "I need to review the Smith lease" returns document card with playbook options determined by document type.

3. **FR-03: Playbook Invocation + Results (UC-M3)** — User selects a playbook; agent invokes via BFF, shows progress, returns structured results as Adaptive Card. Acceptance: Playbook execution completes with progressive updates and final results card including risk flags, key terms, and action buttons.

4. **FR-04: Playbook Library Browse (UC-M4)** — User asks what analysis tools are available; agent returns categorized playbook list. Acceptance: "What analysis tools are available?" returns grouped playbook list filtered by user permissions.

5. **FR-05: Email Draft from Matter Context (UC-M5)** — User requests email draft; agent resolves matter context, generates draft via Azure OpenAI, presents in Adaptive Card with edit/send/cancel actions. Acceptance: "Draft an update to outside counsel on the Smith matter" generates contextual email with resolved recipient.

6. **FR-06: Corporate Workspace Queries (UC-M6)** — Copilot handles general dashboard queries previously planned for SprkChat: due dates, assignments, matter activity, record creation. Acceptance: Declarative Agent handles native Dataverse queries without BFF for simple entity reads.

7. **FR-07: Handoff to Analysis Workspace (UC-M7)** — Agent recognizes when requests exceed Adaptive Card capabilities and proactively offers deep-link handoff to Analysis Workspace + SprkChat. Acceptance: "I need a detailed clause-by-clause review" offers [Open Analysis Workspace] with pre-filled context.

8. **FR-08: Document Upload + Analysis Pipeline (UC-M8)** — User attaches file via '+' button; agent retrieves from OneDrive via Graph, offers quick analysis (no SPE upload) or full onboarding (SPE upload + `sprk_document` record + Document Profile). Acceptance: Both paths work end-to-end; guided upload flow (deep-link to DocumentUploadWizard) works when attachment not available.

9. **FR-09: Agent Gateway Endpoint** — `POST /api/agent/message` accepts M365 Agents SDK activities, resolves user context, routes to appropriate BFF operations, returns Adaptive Card or text responses. Acceptance: Custom Engine Agent sends activity → BFF returns structured response within 5 seconds for non-playbook operations.

10. **FR-10: Declarative Agent Manifest** — Three-file manifest (agent JSON, API plugin JSON, OpenAPI YAML) deployed via M365 Agents Toolkit to org app catalog. Acceptance: Sideloaded agent appears in MDA Copilot and successfully calls BFF endpoints.

11. **FR-11: Deep-Link Handoff to Wizards** — Agent generates `navigateTo`-compatible URLs with pre-filled params for all wizard Code Pages. Acceptance: Deep-links work for DocumentUploadWizard, SummarizeFilesWizard, CreateMatterWizard, CreateProjectWizard, CreateEventWizard, PlaybookLibrary, and AnalysisWorkspace.

12. **FR-12: Agent 365 Registration** — Register Spaarke Copilot agent in Agent 365 for enterprise governance, policy enforcement, audit trail, and deployment control. Acceptance: Agent visible in Agent 365 admin console with usage analytics.

### Non-Functional Requirements

- **NFR-01: Environment Portability** — All endpoints, service URLs, and configuration MUST use environment variables. No hardcoded environment parameters. Architecture MUST support BYOK/customer-tenant deployment without code changes.
- **NFR-02: Authorization** — All document access through BFF with per-matter/per-project authorization. M365 Copilot MUST NOT directly access SPE containers (`discoverabilityDisabled = true`).
- **NFR-03: Performance** — Non-playbook agent responses within 5 seconds. Playbook execution uses progressive Adaptive Card updates. Typing indicators during processing.
- **NFR-04: Adaptive Card Compatibility** — All cards MUST use Adaptive Card schema 1.5 (Copilot maximum).
- **NFR-05: Error Handling** — Return `ProblemDetails` for all API errors with correlation IDs. Graceful degradation when BFF unavailable. User-friendly error cards in Copilot.
- **NFR-06: Telemetry** — Copilot interaction logging, playbook invocation metrics, handoff tracking, error rates. Per-operation counts/latencies (not content).
- **NFR-07: Rate Limiting** — All agent endpoints MUST apply rate limiting (`ai-stream`, `ai-batch` policies). Bound concurrency for upstream AI service calls.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern required for all new endpoints (no Azure Functions)
- **ADR-003**: Authorization via `IAuthorizationRule` chain; per-request UAC caching only
- **ADR-008**: Endpoint filters for authorization (no global middleware)
- **ADR-009**: Redis-first caching; tenant-scoped keys for agent context
- **ADR-010**: DI minimalism — concrete registrations, ≤15 non-framework DI lines, single typed `HttpClient` per upstream
- **ADR-013**: AI endpoints extend BFF (no separate AI microservice); flow `ChatHostContext` through pipeline
- **ADR-014**: Cache final AI outcomes only (not streaming tokens); tenant+user scoped keys
- **ADR-016**: Rate limiting on all AI endpoints; bound concurrency; return 429/503 ProblemDetails
- **ADR-019**: ProblemDetails for all errors; stable `errorCode` extension; terminal SSE error events
- **ADR-020**: SemVer for packages; tolerant readers; version cache keys

### MUST Rules

- ✅ MUST use Minimal API for agent gateway endpoints
- ✅ MUST use endpoint filters for agent authorization (not middleware)
- ✅ MUST access SPE documents through `SpeFileStore` facade only (no direct Graph SDK leakage)
- ✅ MUST use environment variables for all deployment-specific configuration
- ✅ MUST apply rate limiting to all agent AI endpoints
- ✅ MUST return ProblemDetails with correlation IDs for all errors
- ✅ MUST cache agent context with tenant+user scoped Redis keys
- ✅ MUST use OBO token flow for user-delegated Graph/Dataverse access
- ❌ MUST NOT allow M365 Copilot to directly access SPE containers
- ❌ MUST NOT hardcode environment URLs, tenant IDs, or resource identifiers
- ❌ MUST NOT create separate AI microservice for agent handling
- ❌ MUST NOT create interfaces without genuine seam requirement
- ❌ MUST NOT cache authorization decisions (cache data only)

### Existing Patterns to Follow

- OBO token flow: existing Outlook add-in implementation (`src/server/api/Sprk.Bff.Api/`)
- AI endpoints: `AiToolEndpoints.cs`, `AiToolService`, `IAiToolHandler` pattern
- Playbook invocation: `PlaybookExecutionEngine`, `ChatContextMappingService`
- File upload: `SpeFileStore` → `UploadSessionManager` (small + chunked upload)
- See `.claude/patterns/api/` for endpoint, error handling, and service registration patterns

### Known Platform Limitations (March 2026)

| Limitation | Impact | Workaround |
|---|---|---|
| File attachments silently dropped in Copilot Chat for Custom Engine Agents | Users cannot drag-and-drop docs for analysis via Custom Engine Agents | Users describe docs by name; agent searches SPE |
| ConversationFileReference contents unknown for Declarative Agents | When user attaches via "+", unclear if driveItem ID is Graph-resolvable | **Spike 1 required** — gates file attachment flow |
| `Action.OpenUrl` not supported for Custom Engine Agents in Copilot Chat | Cannot use URL buttons in Adaptive Cards | Use text deep-links or `Action.Submit` |
| `Action.OpenUrlDialog` only works for Declarative Agents | Cannot open modal dialogs from Custom Engine Agents | Return deep-link in text response |
| No true SSE streaming in Copilot Chat | Cannot stream token-by-token | Typing indicators + progressive card updates |
| Adaptive Card schema 1.5 maximum | Some newer card features unavailable | Design within 1.5 constraints |

## Success Criteria

1. [ ] **Copilot aware of Spaarke** — Declarative Agent in MDA Copilot can search documents, invoke playbooks, and return Adaptive Card results — Verify by: sideload agent, execute UC-M1 through UC-M8
2. [ ] **Custom Engine Agent operational** — Agent gateway endpoint receives activities, resolves context, calls BFF, returns structured responses — Verify by: end-to-end test with M365 Agents SDK
3. [ ] **Playbook results in Copilot** — Full flow: search → select playbook → execute → Adaptive Card results with deep-link handoff — Verify by: run playbook from Copilot, verify card content
4. [ ] **Handoff works** — "Open in Workspace" deep-link opens Analysis Workspace with correct context and SprkChat auto-launches — Verify by: click handoff link, verify context params
5. [ ] **Agent 365 registered** — Spaarke agents visible in Agent 365 admin console — Verify by: IT admin can see and manage agents
6. [ ] **Environment portable** — All configuration via environment variables; no hardcoded env params — Verify by: deploy to second environment with only env var changes

## Phases (R1 Implementation Order)

### Phase 1: Declarative Agent + API Plugin (MVP for GA Day)

- `declarativeAgent.json` + `spaarke-api-plugin.json` + `spaarke-bff-openapi.yaml`
- Deploy via M365 Agents Toolkit → org app catalog
- Dataverse knowledge grounding for native entity queries
- Adaptive Card templates (document list, matter summary, task list, playbook results)
- Deep-link handoff to Analysis Workspace and wizard Code Pages
- Admin configuration: enable/disable Copilot per app, per environment
- **Technical spikes**: ConversationFileReference contents, Action.Submit in API plugin, end-to-end file pipeline

### Phase 2: Custom Engine Agent + BFF Agent Gateway

- `SpaarkeAgentHandler` (M365 Agents SDK ActivityHandler)
- `POST /api/agent/message` — agent gateway endpoint
- `GET /api/agent/playbooks?documentType={type}` — playbook menu
- `POST /api/agent/run-playbook` — playbook invocation
- SSO/OBO token flow (M365 → BFF → Graph/Dataverse)
- Adaptive Card formatter service
- Multi-turn conversation support with session management

### Phase 3: Playbook Integration + Rich Interactions

- Full playbook flow: document search → playbook selection → execution → Adaptive Card results
- Progressive card updates for long-running playbook execution
- Playbook library browse via Copilot
- Email drafting via `sprk_communication` module
- Handoff to wizard Code Pages with context
- Write-back confirmation pattern

### Phase 4: Enterprise Readiness

- Agent 365 registration
- Error handling and graceful degradation
- Telemetry: interaction logging, playbook metrics, handoff tracking
- Admin controls: which playbooks exposed to Copilot, per-role restrictions
- Documentation: admin guide, user guide, troubleshooting

## Dependencies

### Prerequisites

- Spaarke BFF API — existing endpoints + new agent gateway endpoints
- M365 Copilot GA in MDA (April 13, 2026) — for Declarative Agent deployment
- M365 Agents SDK (GA) — for Custom Engine Agent
- Azure Bot Service — hosting for the agent handler
- Agent 365 (GA May 1, 2026) — for enterprise governance (not a blocker)

### Existing Components Reused (No Changes)

- BFF AI endpoints (chat, search, summarize)
- PlaybookExecutionEngine
- ChatContextMappingService
- Azure OpenAI + AI Search
- SPE document access via Graph (`SpeFileStore`)
- OBO token pattern (from Outlook add-in)
- PlaybookDispatcher (semantic matching)

## Deployment Models

### Model 1: Spaarke-Hosted (Primary)

Customer's M365 tenant → Spaarke Azure subscription (BFF API, Azure OpenAI, AI Search, Playbook Engine). Standard multi-tenant deployment.

### Model 2: Customer-Hosted / BYOK (Architecture-Ready)

Not in scope for R1 implementation, but all components MUST support:
- Environment variables for all service URLs, keys, and tenant identifiers
- No hardcoded resource names, endpoints, or Azure subscription references
- Bicep template compatibility for customer Azure subscriptions
- Customer-controlled Azure OpenAI, AI Search, and AI Foundry instances

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| MCP Server | Should MCP be in Phase 1? | No — not needed when we control both sides (BFF + agent). MCP is a customer extensibility play for R2 | Removed from R1 scope; simplifies Phase 1 |
| Custom Engine Agent priority | Is Custom Engine Agent in scope for R1? | Yes, high priority — included in this project | Phase 2 delivers full Custom Engine Agent |
| BYOK deployment | Is BYOK in scope? | Not in scope, but architecture MUST support it (env vars, no hardcoded params) | All config via environment variables |
| Agent 365 dependency | Is Agent 365 GA a blocker? | No — proceed as if available, not a blocker | Phase 4 includes registration without waiting |
| Dataverse record updates | What path for record updates? | API Plugin (Declarative Agent) + Custom Engine Agent → BFF → Dataverse. Full control, confirmation UX | No MCP layer needed; BFF endpoints are the tools |

## Assumptions

- **Adaptive Card complexity**: Playbook results can be meaningfully rendered in Adaptive Card schema 1.5. If not, handoff pattern applies.
- **ConversationFileReference**: Spike 1 will confirm Graph-resolvable driveItem ID. If not available, file attachment flow falls back to guided upload (deep-link to DocumentUploadWizard).
- **Agent 365 API stability**: Agent 365 registration API is stable enough for Phase 4 integration. If unstable, registration becomes R2.
- **M365 Agents SDK channel support**: Custom Engine Agent works in MDA Copilot side pane (not just Teams/Copilot Chat).

## Unresolved Questions

- [ ] **Spike 1: ConversationFileReference** — Does the reference include a Graph-resolvable driveItem ID? Blocks: UC-M8 file attachment flow
- [ ] **Spike 2: Adaptive Card Action.Submit** — Do Action.Submit buttons work in API plugin responses within MDA Copilot? Blocks: UC-M2 playbook selection UX
- [ ] **Spike 3: End-to-end file pipeline** — Does the complete flow work: user attaches file → API plugin receives reference → BFF retrieves from OneDrive → runs playbook → returns Adaptive Card? Blocks: UC-M8 quick analysis path
- [ ] **Agent identity model** — Does the Custom Engine Agent authenticate as the user (delegated) or as itself (app-only) when calling BFF? Delegated is required for SPE authorization.
- [ ] **Graph Connector for Dataverse metadata** — Should we create a Graph Connector to make Spaarke entity metadata available to the Declarative Agent's knowledge grounding?

---

*AI-optimized specification. Original: design.md*
