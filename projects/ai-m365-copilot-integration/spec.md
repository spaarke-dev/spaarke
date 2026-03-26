# M365 Copilot in Power Apps — Spaarke AI Integration — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-26
> **Source**: design.md
> **Target Launch**: June 2026 (product launch)
> **Branch**: `work/ai-m365-copilot-integration-r1`

## Executive Summary

Integrate Spaarke's AI capabilities — playbook engine, Azure OpenAI, custom RAG pipeline, and SPE document access — into M365 Copilot within Power Apps model-driven apps. This project delivers a Declarative Agent with API Plugin (Tier 1) and Custom Engine Agent (Tier 2) that expose the full Spaarke BFF API through M365 Copilot, enabling document search, playbook invocation, matter queries, email drafting, and Analysis Workspace handoff — all from the Copilot side pane.

**Strategic positioning**: M365 Copilot replaces SprkChat as the general-purpose chat UX across all MDA pages. SprkChat is repositioned as a special-purpose AI companion exclusively for the Analysis Workspace, where deep editor integration, streaming write-back, and inline toolbar capabilities exceed what Copilot can deliver. Copilot hands off to SprkChat when deep analysis work is needed.

## Scope

### In Scope (R1 — All 4 Phases)

**Tier 1: Declarative Agent with API Plugin**
- `declarativeAgent.json` — agent manifest with Spaarke-scoped instructions, conversation starters, capabilities
- `spaarke-api-plugin.json` — function definitions for BFF API endpoints
- `spaarke-bff-openapi.yaml` — OpenAPI spec exposing full (or near-full) BFF API surface
- Deployment via M365 Agents Toolkit → org app catalog
- Dataverse knowledge grounding for native entity queries

**Tier 2: Custom Engine Agent**
- `SpaarkeAgentHandler` — M365 Agents SDK ActivityHandler
- Agent gateway adapter endpoints on BFF API (thin facades over existing services)
- Adaptive Card templates and formatter service
- SSO token flow (M365 → OBO → BFF → Graph/Dataverse)
- Azure Bot Service registration and channel configuration

**Adaptive Card Templates**
- Document list, matter summary, task list, playbook results
- Risk findings, email preview, progress indicator
- Playbook menu with `Action.Submit` buttons
- Handoff card with deep-links to Analysis Workspace and wizard Code Pages

**Enterprise Readiness**
- Error handling and graceful degradation
- Telemetry: interaction logging, playbook invocation metrics, handoff tracking
- Admin controls: which playbooks are exposed, per-role restrictions
- BYOK deployment: Bicep templates for customer-hosted infrastructure
- Documentation: admin guide, user guide, troubleshooting

**Technical Spikes (Phase 1 priority)**
- Spike 1: Validate `ConversationFileReference` — does it include Graph-resolvable driveItem ID?
- Spike 2: Validate `Action.Submit` in API plugin responses within MDA Copilot
- Spike 3: End-to-end file pipeline (attach → API plugin → BFF → playbook → Adaptive Card)

### Out of Scope (Deferred to R2)

- Teams bot channel
- Outlook plugin / message extension
- Copilot Chat standalone channel
- Power Pages integration / Agent API embed
- MCP server (Tier 3 — universal tool exposure)
- Agent 365 governance (Tier 4 — full control plane registration)
- Cross-agent orchestration
- SprkChat modifications (separate project)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — New agent gateway adapter endpoints, Adaptive Card formatter service, OpenAPI spec
- `src/server/api/Sprk.Bff.Api/Infrastructure/` — SSO/OBO token flow for M365 agent auth, Bot Service registration
- `src/server/api/Sprk.Bff.Api/Features/Ai/` — Existing AI services reused by agent adapter layer
- `src/solutions/` — Declarative Agent manifest files, Teams app manifest
- `infrastructure/` — Bicep templates for Azure Bot Service, BYOK deployment
- `docs/` — Admin guide, user guide, troubleshooting documentation

## Requirements

### Functional Requirements

1. **FR-01: Declarative Agent Manifest** — Create `declarativeAgent.json` with Spaarke-scoped instructions (system prompt), conversation starters, and capability declarations. Acceptance: Agent loads in MDA Copilot side pane with Spaarke identity and suggested prompts.

2. **FR-02: API Plugin with OpenAPI Spec** — Create `spaarke-api-plugin.json` function definitions and `spaarke-bff-openapi.yaml` OpenAPI spec exposing the full BFF API surface. Copilot's AI reads function descriptions and decides when to call them. Acceptance: Copilot can invoke BFF endpoints for document search, playbook operations, matter queries, and all other exposed API operations.

3. **FR-03: Agent Gateway Adapter Endpoints** — Create thin adapter endpoints on BFF API that translate M365 agent activity format into existing BFF service calls and format responses as Adaptive Cards. These are NOT new AI logic — they wrap existing services. Acceptance: `POST /api/agent/message` routes to existing chat/search/playbook services; `GET /api/agent/playbooks` wraps existing playbook listing; `POST /api/agent/run-playbook` wraps existing analysis execution.

4. **FR-04: Adaptive Card Templates** — Design and implement JSON templates for structured content display: document list, matter card, risk findings, playbook menu, email preview, task confirmation, progress indicator. Acceptance: All 8 use cases (UC-M1 through UC-M8) render correctly in Copilot side pane using Adaptive Card schema 1.5.

5. **FR-05: Adaptive Card Formatter Service** — Create a service that transforms playbook output JSON, BFF API responses, and entity data into Adaptive Card JSON. Acceptance: Any playbook result can be rendered as a structured card with action buttons.

6. **FR-06: Document Search via BFF** — M365 Copilot searches SPE documents through BFF API (not direct Copilot grounding). SPE containers remain `discoverabilityDisabled = true`. Acceptance: "Find the NDA for Acme" returns correct documents with authorization enforced; unauthorized documents never appear.

7. **FR-07: Playbook Invocation from Copilot** — Users trigger playbook analysis from the Copilot side pane. For quick playbooks, return Adaptive Card results inline. For long-running playbooks, use async pattern or deep-link to Analysis code page. Acceptance: User can invoke any available playbook; results display as structured Adaptive Card or open in separate code page.

8. **FR-08: Playbook Menu Context Resolution** — When a document is identified, resolve available playbooks based on document type via existing `ChatContextMappingService`. Present playbook options as `Action.Submit` buttons in Adaptive Card. Acceptance: Correct playbooks appear for document type; clicking invokes execution.

9. **FR-09: Matter/Entity Queries** — Users query matters, tasks, events, and projects through natural language in Copilot. Copilot queries Dataverse natively for entity data. Acceptance: "What are my overdue tasks?" returns correct results as Adaptive Card list.

10. **FR-10: Email Drafting** — Users request email drafts contextualized with matter data, recent activity, and document content. Acceptance: "Draft an update to outside counsel on the Smith matter" produces contextual email preview card with edit/send actions.

11. **FR-11: Analysis Workspace Handoff** — "Open in Analysis Workspace" generates deep-link URL with `analysisId`, `sourceFileId`, and playbook context params. SprkChat auto-launches with full context. Acceptance: One-click handoff opens Analysis Workspace with correct document and analysis loaded.

12. **FR-12: Wizard Deep-Links** — Agent generates deep-link URLs to existing wizard Code Pages (DocumentUploadWizard, SummarizeFilesWizard, CreateMatterWizard, CreateEventWizard, PlaybookLibrary) with pre-filled parameters. Acceptance: Deep-links open correct wizard with context pre-populated.

13. **FR-13: SSO Token Flow** — M365 Copilot user identity flows through to BFF API via OAuth 2.0 delegated permissions. BFF exchanges for OBO tokens to access Graph API (SPE documents) and Dataverse (entity queries). Acceptance: End-to-end auth works without user re-authentication; SPE authorization model enforced.

14. **FR-14: Document Attachment Handling** — If Spike 1 confirms `ConversationFileReference` includes Graph-resolvable driveItem ID: BFF retrieves file from OneDrive, offers quick analysis or full onboarding. If not: user describes document by name, agent searches SPE. Acceptance: Either path produces a working document discovery flow.

15. **FR-15: Long-Running Playbook Handling** — For playbooks exceeding API plugin response timeout: return async status with deep-link to Analysis code page modal, OR return job ID with status polling endpoint. Acceptance: User is never left waiting with no feedback; progress is visible or redirected.

16. **FR-16: SpaarkeAgentHandler** — M365 Agents SDK `ActivityHandler` implementation that receives activities from M365 channels, extracts user context and message content, and routes to BFF agent gateway. Acceptance: Handler processes `MessageActivity`, `InvokeActivity` types and returns Adaptive Card responses.

17. **FR-17: Azure Bot Service Registration** — Register bot in Azure, configure channels (Teams, Copilot), create Teams app manifest for deployment. Acceptance: Bot responds in MDA Copilot side pane and Teams (for testing).

18. **FR-18: Playbook Library Browse** — Users discover available playbooks without a specific document ("What analysis tools are available?"). Acceptance: Returns categorized playbook list as Adaptive Card.

19. **FR-19: Admin Controls** — Configurable controls for which playbooks are exposed to Copilot, per-role restrictions on agent capabilities. Acceptance: Admin can toggle playbook visibility and restrict agent functions by security role.

20. **FR-20: BYOK Deployment Templates** — Bicep templates enabling customer-hosted deployment of BFF API + Azure OpenAI + AI Search serving the same agent. Acceptance: Customer can deploy Spaarke AI infrastructure in their own Azure subscription.

### Non-Functional Requirements

- **NFR-01: Authorization** — All document access through BFF with per-matter/per-project authorization. Agent does NOT cache document content. Tenant isolation enforced at every layer (AI Search `tenantId` filter, SPE container scoping). SPE containers remain `discoverabilityDisabled = true`.
- **NFR-02: Performance** — Agent gateway responses must complete within M365 API plugin timeout limits. For operations exceeding timeout, use async pattern with deep-link fallback.
- **NFR-03: Adaptive Card Compatibility** — All cards must conform to Adaptive Card schema 1.5 (maximum supported in Copilot).
- **NFR-04: Error Handling** — Graceful degradation when BFF is unavailable, token exchange fails, or playbook times out. Return user-friendly error cards, not raw errors. Follow ADR-019 ProblemDetails patterns.
- **NFR-05: Telemetry** — Log interaction types, playbook invocation counts, handoff events, response latencies. Do NOT log document content or prompts (ADR-015).
- **NFR-06: Rate Limiting** — Apply rate limiting to all agent gateway endpoints per ADR-016 policies.
- **NFR-07: Cost Control** — Agent gateway must bound concurrency for upstream AI service calls. Use async jobs for batch/heavy operations (ADR-016).
- **NFR-08: Multi-Tenant** — Agent must work in both Spaarke-hosted and customer-hosted (BYOK) deployment models without code changes.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern — all new endpoints use Minimal API, registered in `Program.cs` pipeline, return `ProblemDetails` for errors
- **ADR-008**: Endpoint filters for authorization — agent gateway endpoints use endpoint filters, not global middleware
- **ADR-010**: DI minimalism — ≤15 non-framework registrations; concrete types by default; no unnecessary interfaces
- **ADR-013**: AI Architecture — extend BFF (not separate service); use AI Tool Framework patterns; flow `ChatHostContext` for entity-scoped operations
- **ADR-014**: AI Caching — use Redis for cross-request caching; scope keys by tenant; don't cache streaming tokens
- **ADR-015**: AI Data Governance — send minimum text to AI; log only identifiers/timings; scope artifacts by tenant
- **ADR-016**: AI Cost & Rate Limits — rate limit all agent endpoints; bound upstream concurrency; explicit timeouts
- **ADR-019**: ProblemDetails & Error Handling — consistent error shapes; stable `errorCode`; correlation IDs; don't leak prompts/content

### MUST Rules

- MUST use Minimal API for all new agent gateway endpoints (ADR-001)
- MUST use endpoint filters for agent endpoint authorization (ADR-008)
- MUST keep SPE containers `discoverabilityDisabled = true` — no direct Copilot grounding on SPE
- MUST route all document access through BFF with per-matter/per-project authorization
- MUST use existing BFF services (chat, search, playbook execution) — agent endpoints are adapters, not reimplementations
- MUST use Adaptive Card schema 1.5 (Copilot maximum)
- MUST return `ProblemDetails` for all API errors (ADR-019)
- MUST apply rate limiting to all agent gateway endpoints (ADR-016)
- MUST flow `ChatHostContext` through chat pipeline for entity-scoped operations (ADR-013)
- MUST scope all cached data by tenant (ADR-014)
- MUST NOT log document content, prompts, or model output (ADR-015)
- MUST NOT leak document content or prompts in error responses (ADR-019)
- MUST NOT create new AI orchestration logic — reuse PlaybookExecutionEngine, ChatContextMappingService, existing tool handlers
- MUST NOT bypass SPE authorization by exposing SPE containers to Copilot directly

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Features/Ai/Chat/` — existing chat endpoint pattern (session management, SSE streaming, context resolution)
- See `src/server/api/Sprk.Bff.Api/Features/Ai/Analysis/` — existing analysis creation and execution pattern
- See `src/server/api/Sprk.Bff.Api/Features/Ai/Playbooks/` — existing playbook management and execution
- See `src/server/api/Sprk.Bff.Api/Features/Ai/Search/` — existing semantic/hybrid search
- See `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — central endpoint registration pattern
- See `.claude/patterns/api/` — API endpoint patterns and conventions

### Existing BFF Services Reused (No New AI Logic)

| Existing Service | Agent Adapter Usage |
|---|---|
| Chat endpoints (`/api/ai/chat/*`) | Agent message routing, conversation management |
| Search endpoints (`/api/ai/search`) | Document search from Copilot queries |
| Playbook endpoints (`/api/ai/playbooks/*`) | Playbook listing, invocation, results |
| Analysis endpoints (`/api/ai/analysis/*`) | Analysis creation, execution, handoff |
| Context mappings (`/api/ai/chat/context-mappings`) | Document type → playbook resolution |
| RAG endpoints (`/api/ai/rag/search`) | Semantic document retrieval |
| Document endpoints (`/api/documents`) | Document metadata and access |
| Workspace endpoints (`/api/workspace/*`) | Matter, project, file queries |
| Communications endpoints (`/api/communications`) | Email drafting |

### New Components to Build

| Component | Type | Description |
|---|---|---|
| `declarativeAgent.json` | Manifest | Agent definition with instructions, capabilities, conversation starters |
| `spaarke-api-plugin.json` | Manifest | Function definitions mapping to BFF API endpoints |
| `spaarke-bff-openapi.yaml` | OpenAPI Spec | Full BFF API surface for API Plugin consumption |
| Agent gateway adapter endpoints | BFF Endpoints | `POST /api/agent/message`, `GET /api/agent/playbooks`, `POST /api/agent/run-playbook` — thin facades |
| `SpaarkeAgentHandler` | Agents SDK | M365 Agents SDK ActivityHandler — routes to BFF |
| `AdaptiveCardFormatterService` | BFF Service | Transforms BFF responses → Adaptive Card JSON |
| Adaptive Card templates | JSON Templates | 8+ card templates for different response types |
| `HandoffUrlBuilder` | BFF Service | Generates deep-link URLs to Analysis Workspace and wizard Code Pages |
| Bot Service registration | Infrastructure | Azure Bot Service + channel config + Teams app manifest |
| BYOK Bicep templates | Infrastructure | Customer-hosted deployment templates |

### Known Platform Limitations (March 2026)

| Limitation | Impact | Workaround |
|---|---|---|
| File attachments silently dropped for Custom Engine Agents in Copilot Chat | Users cannot drag-and-drop docs for analysis via Custom Engine Agents | Users describe docs by name; agent searches SPE |
| `ConversationFileReference` contents unknown for Declarative Agents | File attachment flow uncertain | Spike 1 validates; fallback: search-by-name |
| `Action.OpenUrl` not supported for Custom Engine Agents in Copilot Chat | Cannot use URL buttons in Adaptive Cards | Use text deep-links or `Action.Submit` with navigation advice |
| No true SSE streaming in Copilot Chat | Cannot stream token-by-token like SprkChat | Typing indicators (Teams); final card (Copilot Chat) |
| Adaptive Card schema 1.5 maximum | Newer card features unavailable | Design within 1.5 constraints |
| API plugin response timeout (exact limit TBD) | Long-running playbooks may timeout | Async pattern + deep-link to Analysis code page |

## Functional Use Cases

### UC-M1: Matter Dashboard Queries
User on any MDA page asks "What are my overdue tasks?" → Agent queries Dataverse → returns Adaptive Card list of tasks with due dates and deep-links.

### UC-M2: Document Search + Playbook Selection
User asks "I need to review the Smith lease agreement" → Agent searches SPE via BFF → presents document card with available playbook options as `Action.Submit` buttons.

### UC-M3: Playbook Invocation + Results
User selects a playbook (e.g., Lease Review) → Agent invokes via BFF → for quick playbooks: returns Adaptive Card with findings; for long playbooks: returns deep-link to Analysis code page.

### UC-M4: Playbook Library Browse
User asks "What analysis tools are available?" → Agent queries available playbooks → returns categorized Adaptive Card list.

### UC-M5: Email Draft from Matter Context
User asks "Draft an update to outside counsel on the Smith matter" → Agent resolves matter, queries activity, generates draft → returns email preview card with edit/send actions.

### UC-M6: Corporate Workspace Dashboard
User on Corporate Workspace asks dashboard queries → Copilot queries Dataverse natively for matters, tasks, events → returns entity card lists.

### UC-M7: Handoff to Analysis Workspace
User requests deep analysis exceeding Copilot capabilities → Agent presents handoff card with "Open in Analysis Workspace" deep-link → SprkChat auto-launches with context.

### UC-M8: Document Upload + Analysis Pipeline
User wants to upload and analyze a document → Agent deep-links to DocumentUploadWizard with pre-filled params (matter, container). After upload, user returns to Copilot for analysis via search-by-name.

## Phases

### Phase 1: Declarative Agent + API Plugin (MVP)
- Declarative Agent manifest (`declarativeAgent.json`, `spaarke-api-plugin.json`, `spaarke-bff-openapi.yaml`)
- Deploy via M365 Agents Toolkit → org app catalog
- Dataverse knowledge grounding for native entity queries
- Adaptive Card templates (document list, matter summary, task list, playbook results)
- Deep-link handoff to Analysis Workspace and wizard Code Pages
- Technical spikes (ConversationFileReference, Action.Submit, end-to-end file pipeline)
- Admin configuration: enable/disable Copilot per app

### Phase 2: Custom Connector + BFF Agent Gateway
- Agent gateway adapter endpoints (`POST /api/agent/message`, etc.)
- SSO token flow (M365 → OBO → BFF → Graph/Dataverse)
- Adaptive Card formatter service
- SPE document search through BFF (not direct grounding)
- Multi-turn conversation support with session management

### Phase 3: Playbook Integration + Rich Interactions
- Full document search → playbook selection → execution → results flow
- Async pattern for long-running playbooks (deep-link to Analysis code page)
- Playbook library browse
- Email drafting via communications module
- Handoff to wizard Code Pages with context
- Write-back confirmation pattern

### Phase 4: Enterprise Readiness
- Error handling and graceful degradation
- Telemetry and interaction logging
- Admin controls (playbook visibility, per-role restrictions)
- BYOK Bicep deployment templates
- Documentation (admin guide, user guide, troubleshooting)

## Success Criteria

1. [ ] **Declarative Agent live** — Agent deployed to org app catalog, loads in MDA Copilot side pane with Spaarke identity — Verify: sideload in dev tenant, confirm instructions and conversation starters appear
2. [ ] **Document search works** — "Find the NDA for Acme" returns correct documents from SPE via BFF with authorization enforced — Verify: search returns only documents user is authorized to see
3. [ ] **Playbook invocation** — User triggers playbook from Copilot → receives Adaptive Card results or deep-link to Analysis code page — Verify: invoke NDA Review playbook, confirm structured findings card
4. [ ] **Handoff works** — "Open in Workspace" deep-link opens Analysis Workspace with correct context — Verify: click handoff link, confirm SprkChat auto-launches with `analysisId`
5. [ ] **BYOK deployment** — Customer-hosted Azure infrastructure serves the same agent — Verify: deploy Bicep templates to isolated subscription, confirm end-to-end flow
6. [ ] **Full BFF API exposure** — OpenAPI spec covers all (or near-all) BFF API endpoints — Verify: API Plugin function count matches BFF endpoint inventory

## Dependencies

### Prerequisites
- Existing BFF API with AI endpoints (chat, search, playbooks, analysis) — **already built**
- PlaybookExecutionEngine and ChatContextMappingService — **already built**
- Azure OpenAI, AI Search, Document Intelligence services — **already provisioned**
- SPE document access via Graph — **already built**
- OBO token pattern (proven in Outlook add-in `sdap-office-integration`) — **pattern exists**
- Wizard Code Pages (DocumentUploadWizard, SummarizeFilesWizard, etc.) — **already deployed**

### External Dependencies
- M365 Copilot GA in model-driven apps (April 13, 2026) — for Declarative Agent deployment in production
- M365 Agents SDK (GA) — for Custom Engine Agent
- Azure Bot Service — for hosting agent handler
- M365 Agents Toolkit (VS Code extension) — for packaging and deploying agent
- Agent 365 (GA May 1, 2026) — for Tier 4 governance (Phase 4, if in scope)

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Timeline | Is April 13 GA the target, or full R1? | Full R1 — pre-product launch with June 2026 date. April 13 is not a hard deadline for delivery. | Tasks prioritized for quality, not speed. Spikes first, then iterative delivery. |
| Agent endpoints | New AI logic or wrappers? | Preference is to use existing BFF services. Agent endpoints are thin adapter/facade over existing capabilities. | Agent gateway is an adapter layer, not new orchestration. Significantly reduces effort and risk. |
| OpenAPI scope | Agent-only or full BFF? | Full (or near-full) BFF API surface must be exposed. | Larger OpenAPI spec; more API Plugin functions; Copilot has broader capability set. |
| Testing | M365 access available? | Yes — dev environment and developers have full M365 access. | Can sideload agents, test in real Copilot environment from day one. |
| Card designs | Formal designs or ASCII? | ASCII mockups in design doc are the spec for now. | Implement matching ASCII mockup structure; iterate based on testing. |
| File attachment fallback | Fallback if Spike 1 fails? | Yes — search-by-name, matching Custom Engine Agent workaround. | Always implement search-by-name path; file attachment is enhancement if spike succeeds. |
| Long-running playbooks | How to handle timeout? | Async pattern or deep-link to separate Analysis code page modal. | Design for both: quick playbooks return inline cards; long playbooks deep-link to code page. |

## Assumptions

- **M365 Agents Toolkit**: Assuming VS Code extension is available and functional for packaging/sideloading agents to dev tenant. Will be set up as part of Phase 1.
- **Copilot Studio**: NOT required for R1. Direct API Plugin path (Path A) is the primary integration. Copilot Studio is deferred to customer extensibility (R2 + MCP server).
- **Adaptive Card schema 1.5**: All card designs constrained to this version. No newer features assumed available.
- **Bot Service pricing**: Assumed included in existing Azure subscription cost. BYOK customers provision their own.
- **SprkChat unchanged**: This project does NOT modify SprkChat. SprkChat continues as-is in Analysis Workspace. Copilot hands off to it via deep-link.
- **API plugin response timeout**: Assumed to be <30 seconds based on typical M365 patterns. Long operations use async/deep-link pattern.

## Unresolved Questions

- [ ] **Spike 1: ConversationFileReference** — Does it include a Graph-resolvable driveItem ID? Determines file attachment flow. Blocks: UC-M8 attachment path.
- [ ] **Spike 2: Action.Submit in API Plugin** — Do `Action.Submit` buttons work in API plugin responses within MDA Copilot? Blocks: Playbook selection UX (UC-M2).
- [ ] **Spike 3: End-to-end file pipeline** — Full chain validation (attach → API plugin → BFF → playbook → card). Blocks: Complete UC-M8 flow.
- [ ] **API plugin response timeout** — Exact timeout limit for API plugin responses in M365 Copilot. Affects: async vs. inline pattern decision threshold.
- [ ] **Adaptive Card complexity ceiling** — Maximum practical card complexity for analysis results. Affects: Card template design decisions.
- [ ] **Agent identity model** — Does Custom Engine Agent authenticate as user (delegated) or as itself (app-only)? Delegated is required for SPE authorization. Blocks: SSO token flow design.
- [ ] **Graph Connector for Dataverse metadata** — Should we create a Graph Connector for entity metadata to improve Declarative Agent's native knowledge grounding? Low priority; evaluate in Phase 3-4.

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| OBO token flow complexity (M365 → BFF → Graph) | Auth failures in multi-hop scenarios | Existing OBO pattern proven in Outlook add-in; reuse pattern |
| Adaptive Card limitations for rich AI output | Can't match SprkChat's interactive experience | Design for "light interaction + handoff" pattern; don't replicate SprkChat |
| Customer confusion: two AI surfaces (Copilot + SprkChat) | Users don't know which to use | Clear UX: Copilot for general queries; SprkChat for deep analysis — connected by handoff |
| Full OpenAPI spec maintenance burden | Keeping spec in sync with BFF API changes | Generate from code annotations or auto-generate from endpoint metadata |
| Platform limitations change before launch | Microsoft may fix or break current behaviors | Spikes validate current state; architecture designed for adaptation |
| API plugin timeout too short for playbook execution | Inline playbook results may not work for all playbooks | Async pattern + deep-link to Analysis code page as primary fallback |

---

*AI-optimized specification. Original design: design.md*
