# Spaarke AI Platform Unification R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-17
> **Source**: design.md + architecture.md
> **Project Type**: Platform Rebuild (frontend) + Platform Extension (backend)

---

## Executive Summary

Rebuild the Spaarke AI frontend and extend the BFF backend to deliver an AI-directed experience where users accomplish work through conversation and intelligent workflows. R2 replaces R1's chat-centric UI with a three-pane coordinated architecture (Conversation, Workspace, Context), adds dynamic capability orchestration with per-turn tool injection, implements a full AI safety perimeter (prompt shields, groundedness, citation verification, privilege filtering), and introduces work history persistence via Cosmos DB.

**This is NOT an incremental enhancement of R1. The frontend is a rebuild; the backend is a significant extension.**

---

## Project Classification: Rebuild vs. Enhancement

### Why This Is a Rebuild (Frontend)

R1's SpaarkeAi Code Page (~13,000 lines across 4 packages) has structural limitations that cannot be incrementally extended to deliver R2's architecture:

| R1 Architecture | R2 Requirement | Why Incremental Fails |
|---|---|---|
| SprkChat is the root experience; panes are subordinate | Three-pane shell is the root; SprkChat is a child of the Conversation pane | Inverting the component hierarchy requires restructuring the root component tree |
| Single-subscriber pane events (`subscribePaneEvents` last-write-wins) | Multi-subscriber event bus with typed channels per pane | The single-subscriber pattern is a fundamental architectural constraint in `StandaloneAiProvider` |
| Dual event buses (1: `@spaarke/ai-outputs` CrossPaneLinkEvent, 2: raw DOM `sprk-ai-pane-event`) | Unified cross-pane event system with typed contracts | Two ad hoc event systems cannot be cleanly unified without restructuring |
| Output/Source registries are display-only (render data, no interaction) | Workspace/Context widgets are interactive mini-apps (actions, serialize/restore, cross-pane events) | The widget contract changes from `render(data)` to `render(data) + actions + serialize/restore + events` |
| `StandaloneAiProvider` owns streaming state + entity context + session ID | Pane-centric state: each pane owns its widget state; shell coordinates via event bus | State management architecture changes from centralized provider to distributed pane ownership |
| No workspace tabs | Tabbed workspace (max 3 active widgets) | Tab management requires new state, lifecycle, and persistence abstractions |
| No widget persistence | Widgets serialize/restore for session resume | Requires new interface contract across all widgets |

### What Is Preserved From R1

| Component | Status | Rationale |
|---|---|---|
| **SprkChat** (2,091 lines + 14 sub-components) | Preserved as Conversation pane child | Core chat functionality (SSE streaming, message rendering, suggestions, citations, document upload, plan approval) is valuable and complete |
| **SprkChat sub-components** (MessageRenderer, Input, Suggestions, TypingIndicator, UploadZone, CitationPopover, ActionConfirmationDialog, etc.) | Preserved | These are leaf components with no architectural coupling to the shell |
| **Existing output widgets** (7 implemented: BudgetDashboard, SearchResults, AnalysisEditor, ContractComparison, StatusSummary, Recommendation, ActionPlan) | Migrated to new `@spaarke/ai-widgets` with added serialize/restore | Widget rendering logic is reusable; they gain new interface methods |
| **Existing source widgets** (6 implemented: DocumentViewer, WebSource, LegalLibrary, Citation, ImageViewer, CodeViewer) | Migrated to new `@spaarke/ai-widgets` as ContextWidgets | Same rendering, new widget contract |
| **ThreePaneLayout** (296 lines in `@spaarke/ui-components`) | Preserved as layout primitive | Handles splitter mechanics, collapse, keyboard; shell builds on top |
| **main.tsx bootstrap** (258 lines: auth, config, URL params) | Preserved with minor updates | Bootstrap pattern is correct; root component changes from `<App>` to new shell |
| **BFF API services** (SprkChatAgentFactory, ChatSessionManager, ChatEndpoints, AnalysisOrchestrationService) | Extended, not replaced | Well-structured; R2 adds new services alongside |
| **Tool classes** (DocumentSearchTools, KnowledgeRetrievalTools, TextRefinementTools, etc.) | Preserved, registered via new CapabilityManifest | Tool implementations unchanged; registration moves from manual `ResolveTools` to manifest-driven |
| **Playbook model** (sprk_analysisplaybook entity, N:N relationships, capabilities) | Extended with new fields | Schema additions, not replacements |

### What Is Replaced or New

| Component | Action | Why |
|---|---|---|
| **App.tsx root component tree** | Replace | New `ThreePaneShell` root with event bus, widget registries, pane coordination |
| **StandaloneAiProvider** | Replace | New `AiSessionProvider` with multi-subscriber event bus, distributed pane state |
| **OutputPanel** (404 lines) | Replace | New `WorkspacePane` with TabManager, interactive widgets, workspace_widget SSE handling |
| **SourcePanel** (370 lines) | Replace | New `ContextPane` with adaptive widget rendering, stage-based lifecycle |
| **LeftPane** (143 lines) | Replace | New `ConversationPane` wrapping SprkChat + history, with pane-aware event dispatch |
| **ChatPanel** (223 lines) | Replace | New `ConversationPane` subsumes this role with stage-aware transitions |
| **WelcomePanel** (673 lines) | Redesign | Landing stage of the three-pane lifecycle (Stage 1) |
| **Cross-pane event system** | Replace | Unified `PaneEventBus` replacing dual DOM CustomEvent buses |
| **`@spaarke/ai-outputs` registries** | Superseded | New `@spaarke/ai-widgets` package with interactive widget contract |
| **`@spaarke/ai-context`** | Replace | New `@spaarke/ai-session` (or equivalent) with Cosmos-backed session state |

---

## Scope

### In Scope (R2)

**Frontend Rebuild:**
1. Three-pane shell with unified event bus (`ThreePaneShell`, `PaneEventBus`)
2. `@spaarke/ai-widgets` shared library (WorkspaceWidgetRegistry, ContextWidgetRegistry, widget base interfaces, interactive widget contract)
3. Workspace pane with tab management (max 3 tabs)
4. Context pane with adaptive stage-based rendering (playbook gallery -> entity info -> sources -> progress)
5. Migrate R1 output widgets (7) and source widgets (6) to new widget contract
6. New widgets: RedlineViewerWidget, FindingsWidget, ProgressTrackerWidget, PlaybookGalleryWidget, EntityInfoWidget
7. Widget serialize/restore for session resume (data-refreshed restore, not stale snapshot)
8. Embedded wizards adapted for Workspace rendering (CreateMatter, DocumentUpload, SearchSelect)
9. Cross-pane interaction protocol (citation click -> source highlight, text selection -> chat refinement, playbook selection -> chat specialization, tab change -> context adaptation)
10. Safety annotation UI (groundedness highlights, citation verification badges, confidence indicators)
11. Feedback collection UI (thumbs up/down + optional text per AI response)

**Backend Extension:**
12. Dynamic capability orchestration: CapabilityManifest (in-memory), ManifestRefreshService (webhook + 15-min polling), CapabilityRouter (three-layer: keyword -> GPT-4o-mini -> broad superset)
13. OrchestratorPromptBuilder: stable system prompt prefix + per-turn tool schema injection
14. ISprkAgent interface + DirectOpenAiAgent implementation (multi-agent boundary for R3)
15. AI Safety perimeter: PromptShieldService (Azure AI Content Safety), GroundednessCheckService, CitationVerificationService (multi-provider with IVerificationProvider), ConfidenceScoringService, structured output validation (JSON Schema for SSE payloads)
16. Privilege-aware retrieval: `privilege_group_ids` security filter on AI Search queries
17. Audit trail: append-only compliance log in Cosmos DB (immutable policy)
18. Work history: Redis + Cosmos DB write-through (every message), session restore (<500ms target)
19. Session summarization: GPT-4o at 25 messages, extract key legal conclusions as structured data
20. Matter-scoped AI memory: persistent structured facts per matter in Cosmos DB
21. Prompt library: personal/team in Cosmos, org/system in Dataverse
22. Feedback service: per-response storage in Cosmos, aggregated per-capability
23. Hybrid search: DataverseQueryTools (OData: QueryEntities, GetEntityDetail), spaarke-records-index sync pipeline (BackgroundService)
24. Document comparison: CompareDocumentsTool + RedlineViewerWidget
25. SSE event contract: new event types (workspace_widget, context_update, context_highlight, workspace_action, capability_change, safety_annotation) with JSON Schema validation
26. Per-tool error isolation in SprkChatAgentFactory.ResolveTools
27. Default "Spaarke AI General" playbook record in Dataverse

**Infrastructure:**
28. Cosmos DB provisioning (serverless, `spaarke-ai` database, `sessions`/`prompts`/`audit`/`memory`/`feedback` containers)
29. Azure AI Content Safety resource verification/provisioning
30. GPT-4o-mini deployment for Layer 2 routing + summarization (separate from main GPT-4o deployment)
31. AI Search index updates: `privilege_group_ids` field on document index, spaarke-records-index sync
32. Citation reference data population in spaarke-references index

**ADR Updates:**
33. Amend ADR-015 with "Governed Data Stores" section (distinguish application logs vs. compliance audit vs. work history)

### Out of Scope (R3+)

- Multi-agent orchestration (MultiAgentOrchestrator implementing ISprkAgent)
- Chunk-level groundedness (real-time per-sentence safety during streaming)
- PWA distribution (Azure Static Web App, service worker)
- Teams Tab distribution (NAA auth, Teams manifest)
- Command palette (Ctrl+K overlay)
- Adaptive complexity (guided vs. expert mode)
- Notification integration (background process alerts)
- External citation providers (Westlaw, LexisNexis, USPTO, EDGAR — IVerificationProvider interface ships in R2, implementations in R3)
- Mobile-responsive UI
- Real-time collaboration (multi-user sessions)

### Affected Areas

**Frontend (rebuild):**
- `src/solutions/SpaarkeAi/` - Code Page shell (rebuild root component tree)
- `src/client/shared/Spaarke.AI.Outputs/` - Superseded by new `@spaarke/ai-widgets`
- `src/client/shared/Spaarke.AI.Context/` - Superseded by new session provider
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` - Preserved, updated props interface
- `src/client/shared/Spaarke.UI.Components/src/components/ThreePaneLayout/` - Preserved as layout primitive

**Frontend (new):**
- `src/client/shared/Spaarke.AI.Widgets/` - NEW: `@spaarke/ai-widgets` shared library

**Backend (extend):**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/` - NEW: CapabilityManifest, Router, ManifestRefreshService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` - Extend: ISprkAgent, DirectOpenAiAgent, OrchestratorPromptBuilder
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/` - NEW: PromptShield, Groundedness, CitationVerification, Confidence
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/` - NEW: AuditLogService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/` - NEW: MatterMemoryService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/` - NEW: SessionPersistenceService, SessionRestoreService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Feedback/` - NEW: FeedbackService
- `src/server/api/Sprk.Bff.Api/Services/Ai/PromptLibrary/` - NEW: PromptLibraryService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` - Extend: DataverseQueryTools, CompareDocumentsTool
- `src/server/api/Sprk.Bff.Api/Services/Jobs/` - Extend: RecordSyncJob
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` - Extend: new feature modules

**Infrastructure:**
- `infrastructure/` - Cosmos DB Bicep templates
- `.claude/adr/ADR-015-ai-data-governance.md` - Amendment

---

## Requirements

### Functional Requirements

#### FR-100: Three-Pane Shell (Frontend Rebuild)

1. **FR-101**: Build `ThreePaneShell` root component that coordinates Conversation, Workspace, and Context panes via a unified `PaneEventBus` - Acceptance: All three panes render, events flow between them, layout persists across sessions
2. **FR-102**: Build `PaneEventBus` supporting typed, multi-subscriber event channels replacing the dual DOM CustomEvent buses - Acceptance: Multiple subscribers receive events without last-write-wins; typed event contracts enforced
3. **FR-103**: Build `ConversationPane` that wraps SprkChat as a child component with stage-aware transitions (Welcome -> Active Chat -> History) - Acceptance: SprkChat renders in left pane with all R1 functionality preserved
4. **FR-104**: Build `WorkspacePane` with `WorkspaceTabManager` supporting up to 3 active widget tabs - Acceptance: AI tool results render as tabs; oldest tab auto-closes at max; tab switching dispatches context adaptation events
5. **FR-105**: Build `ContextPane` with `ContextPaneController` that adapts content based on workflow stage (playbook gallery -> entity info -> sources/citations -> progress tracker -> related items) - Acceptance: Context pane transitions automatically as workflow progresses
6. **FR-106**: Implement four-stage pane lifecycle: Landing (no context), Playbook Selected (gathering context), Active Work (document/data loaded), Multi-Task (workspace tabs) - Acceptance: Each stage renders the correct pane configuration per design.md Section 2.3

#### FR-200: Widget Framework

7. **FR-201**: Create `@spaarke/ai-widgets` shared library with `WorkspaceWidget<TData, TActions>` interface including `render()`, `actions`, `serializeState()`, `restoreState()`, `onSelectionChange()`, `onActionComplete()` - Acceptance: Interface compiles; at least one widget implements full contract
8. **FR-202**: Implement `WorkspaceWidgetRegistry` and `ContextWidgetRegistry` with lazy-load dynamic import pattern - Acceptance: Widgets load on demand; unknown widget types fall back to generic text display
9. **FR-203**: Migrate 7 existing R1 output widgets to WorkspaceWidgetRegistry with added serialize/restore methods - Acceptance: All 7 widgets render in workspace tabs; data-refreshed restore works on session resume
10. **FR-204**: Migrate 6 existing R1 source widgets to ContextWidgetRegistry - Acceptance: All 6 widgets render in context pane; citation highlighting works
11. **FR-205**: Build `RedlineViewerWidget` for document comparison (side-by-side diff with change tracking highlights + AI-narrated summary) - Acceptance: Two document versions display with additions/deletions highlighted; AI summary in Context pane
12. **FR-206**: Adapt existing `WizardDialog` components for embedded Workspace rendering (CreateMatter, DocumentUpload, SearchSelect) where chat guides wizard steps - Acceptance: Wizard renders in Workspace; chat can trigger step progression
13. **FR-207**: Implement cross-pane interaction protocol per design.md Section 2.2 (citation click -> source highlight, text selection -> refinement suggestion, playbook selection -> chat specialization, tab change -> context adaptation) - Acceptance: All 5 cross-pane interactions from design.md work end-to-end

#### FR-300: Dynamic Capability Orchestration

14. **FR-301**: Build `CapabilityManifest` singleton service that caches all available capabilities, playbooks, and knowledge sources in memory from Dataverse - Acceptance: Manifest loads on startup; <1ms access time; contains all active capabilities from Dataverse
15. **FR-302**: Build `ManifestRefreshService` (BackgroundService) with webhook endpoint (Dataverse plugin on capability publish) + 15-min polling backstop - Acceptance: Manifest updates within seconds of Dataverse publish; polling fills in if webhook missed
16. **FR-303**: Build `CapabilityRouter` with three-layer routing: Layer 1 keyword classifier (0ms, extends existing `AgentServiceRoutingMiddleware` patterns), Layer 2 GPT-4o-mini pre-check (200-500ms), Layer 3 broad superset fallback (0ms) - Acceptance: Every turn routes through exactly one main LLM call; no double-call penalty; Layer 1 handles >60% of turns
17. **FR-304**: Build `OrchestratorPromptBuilder` that constructs stable system prompt prefix (~2000 tokens: persona + capability index) + per-turn tool schemas (max 6-8 active tools) - Acceptance: Prompt token budget stays within ~9000 tokens; prefix is cacheable across turns
18. **FR-305**: Implement per-turn dynamic tool injection where CapabilityRouter output (selected tools[]) is passed to SprkChatAgentFactory for Azure OpenAI chat-completions call - Acceptance: Tools vary per turn based on user intent; LLM only sees active tools
19. **FR-306**: Implement BFF capability validation per activation: user permission, tenant toggle (kill switch), context compatibility, concurrency limits - Acceptance: Unauthorized capabilities are silently excluded; kill-switched capabilities never activate

#### FR-400: AI Safety & Governance

20. **FR-401**: Build `PromptShieldService` calling Azure AI Content Safety Prompt Shields API to scan user messages + RAG-retrieved documents before LLM call - Acceptance: Injection attempts in documents are blocked with user-friendly message; latency <100ms
21. **FR-402**: Build `GroundednessCheckService` calling Azure AI Content Safety Groundedness Detection API after response completion, emitting `safety_annotation` SSE event with ungrounded segments - Acceptance: Ungrounded claims are visually flagged in UI ~200ms after last token; streaming is not blocked
22. **FR-403**: Build `CitationVerificationService` with `IVerificationProvider` interface, `InternalIndexProvider` implementation (queries spaarke-references AI Search index), citation type detection (case law, statute, patent, SEC filing, regulation patterns) - Acceptance: Legal citations in AI responses are annotated as verified/unverified/partial; future providers can be added without code changes
23. **FR-404**: Build `VerifyCitationsTool` as both a post-LLM safety check (automatic every response) and an AI-callable tool ("verify these citations for me") - Acceptance: Dual-mode operation works; tool appears in capability library
24. **FR-405**: Implement privilege-aware retrieval via `privilege_group_ids` security filter on AI Search queries, resolved from Azure AD token claims - Acceptance: Cross-matter searches never return documents from unauthorized matters
25. **FR-406**: Build `ConfidenceScoringService` that scores AI output confidence (high/medium/low) based on source passage count per claim segment - Acceptance: Confidence indicators appear in UI per-response; calibration matches design.md Section 9.2.5
26. **FR-407**: Implement structured output validation: JSON Schema for each SSE event type (workspace_widget, context_update, safety_annotation); BFF validates before emitting; malformed output falls back to generic widget - Acceptance: Invalid tool output never breaks the UI; validation failures logged
27. **FR-408**: Implement cross-matter conversation safety: strip retrieved document content from history when matter context changes, keeping only AI conclusions; notify user - Acceptance: Matter pivot clears source content; user sees notification; no privilege leakage

#### FR-500: Work History & Session Persistence

28. **FR-501**: Build `SessionPersistenceService` implementing Redis + Cosmos DB write-through (every message written to both stores simultaneously) - Acceptance: No data loss on browser close, Redis eviction, or interrupted session; Cosmos has complete history
29. **FR-502**: Build `SessionRestoreService` that loads session from Cosmos, checks entity staleness (ETag comparison), reconstructs context (LLM summary + last 10 verbatim messages), restores workspace widgets - Acceptance: Session restore completes in <500ms; widgets show current data (not stale snapshots)
30. **FR-503**: Implement session summarization at 25 messages using GPT-4o (not mini), extracting key legal conclusions as structured data alongside narrative summary - Acceptance: Summary preserves legal qualifications; structured conclusions queryable; full messages always available in Cosmos
31. **FR-504**: Build `AuditLogService` writing append-only compliance log to Cosmos DB with immutable policy (timestamp, userId, sessionId, action, toolsCalled, documentsAccessed, responseHash SHA-256, safetyResults, matterContext) - Acceptance: Log entries are immutable; configurable retention (default 7 years); exportable to Azure Blob
32. **FR-505**: Build `MatterMemoryService` for persistent structured facts per matter in Cosmos DB (parties, key dates, prior analyses, key facts), loaded into system prompt on session start (~200-500 tokens) - Acceptance: Returning users on same matter see AI that "remembers" prior context; user can validate/clear memory
33. **FR-506**: Build `PromptLibraryService` for saved prompt templates with 4-tier ownership (personal/team in Cosmos, org/system in Dataverse), template variables with entity-ref pickers - Acceptance: Users can save, reuse, and share prompt templates; admin-managed templates appear for all users
34. **FR-507**: Build `FeedbackService` storing thumbs up/down + optional text per AI response in Cosmos, aggregated per-playbook and per-capability - Acceptance: Non-intrusive thumbs icons on each AI message; text input only on thumbs-down; aggregation queryable

#### FR-600: Hybrid Search

35. **FR-601**: Build `DataverseQueryTools` with `QueryEntities(entityType, filter, top)` and `GetEntityDetail(entityType, identifier)` using Dataverse OData API - Acceptance: AI can query structured entity data (matters, projects, contacts, accounts, documents); results are entity-scoped per user permissions
36. **FR-602**: Build `RecordSyncJob` (BackgroundService) that syncs key entity fields from Dataverse to spaarke-records-index in AI Search - Acceptance: SearchDiscovery finds entities AND documents in one query; sync runs on schedule
37. **FR-603**: Build `CompareDocumentsTool` that accepts two document IDs, fetches both from SPE, produces structured diff (additions, deletions, modifications per section) - Acceptance: Diff output renders in RedlineViewerWidget; AI generates narrative summary of material differences

#### FR-700: ISprkAgent Boundary (R3 Prep)

38. **FR-701**: Define `ISprkAgent` interface with `ProcessAsync(ChatMessage, AgentContext, CancellationToken)` returning `IAsyncEnumerable<SseEvent>` - Acceptance: Interface compiles; R3 can implement MultiAgentOrchestrator without changing callers
39. **FR-702**: Build `DirectOpenAiAgent` implementing ISprkAgent as single direct Azure OpenAI call per turn - Acceptance: All R2 chat flows use DirectOpenAiAgent via the interface; no direct Azure OpenAI calls outside this boundary

#### FR-800: SSE Event Contract

40. **FR-801**: Define and implement SSE event types with JSON Schema validation: `workspace_widget` (widgetType, widgetData, tabId?), `context_update` (contextType, contextData), `context_highlight` (citationId, selectionRef), `workspace_action` (action, targetWidgetId, params), `suggestions` (chips[]), `capability_change` (activated[], deactivated[]), `safety_annotation` (groundedness, citations, confidence) - Acceptance: All event types documented with JSON Schema; BFF validates before emission; client handles all types

#### FR-900: Technical Debt & Hardening

41. **FR-901**: Implement per-tool error isolation in SprkChatAgentFactory.ResolveTools (one broken tool must not kill all tools) - Acceptance: A failing tool returns error result; other tools continue working
42. **FR-902**: Create "Spaarke AI General" default playbook record in Dataverse with core tools (SearchDocuments, SearchDiscovery, GetKnowledgeSource, RefineText, GenerateSummary, QueryEntities) - Acceptance: Standalone SpaarkeAi loads this playbook automatically; configurable by admins
43. **FR-903**: Consolidate duplicate `useSseStream` implementations (exists in both `@spaarke/ui-components` and `@spaarke/ai-context`) into single implementation - Acceptance: One SSE streaming hook; both consumers reference the same source

### Non-Functional Requirements

- **NFR-01**: Prompt token budget must stay within ~9000 tokens per turn (capability index 500 + tool schemas 3000 + system prompt 1500 + conversation history 4000)
- **NFR-02**: Layer 1 keyword classification must complete in <50ms (no LLM call)
- **NFR-03**: Layer 2 GPT-4o-mini pre-check must complete in <500ms
- **NFR-04**: Session restore must complete in <500ms from Cosmos
- **NFR-05**: Safety perimeter total added latency must not exceed 400ms worst case (Prompt Shields 100ms + Groundedness 200ms + Citation Verification 100ms)
- **NFR-06**: Write-through to Cosmos must not block SSE streaming (async, fire-and-forget with retry)
- **NFR-07**: All UI must use Fluent UI v9, support dark mode and high-contrast (ADR-021)
- **NFR-08**: SpaarkeAi Code Page must use React 19 with bundled React/Fluent (ADR-022, Code Page surface)
- **NFR-09**: BFF DI registrations must stay within ADR-010 constraints using feature modules
- **NFR-10**: All new endpoints must use endpoint filters for authorization (ADR-008)
- **NFR-11**: Prefer BackgroundService for async work (ADR-001); Azure Functions acceptable with justification when they are the better solution
- **NFR-12**: AI data governance: application logs must not contain content (ADR-015); governed stores (audit, work history) are explicit exceptions per ADR-015 amendment
- **NFR-13**: Latency monitoring via Azure Monitor: TTFT, TBT, TTLT, prompt token count with alert thresholds per design.md Section 8.10
- **NFR-14**: Tenant isolation: Cosmos partition key = tenantId; no cross-tenant queries
- **NFR-15**: Widget lazy loading: each widget is a dynamic import; unknown types fall back to generic display (never crash)

---

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService -- all endpoints, all async jobs. Note: the blanket prohibition on Azure Functions is under review; if a Function is the best solution for a specific R2 workload (e.g., event-driven Cosmos change feed processing), it is acceptable with documented justification.
- **ADR-004**: Job contract -- RecordSyncJob follows job contract pattern
- **ADR-006**: PCF for form controls, Code Pages for standalone -- SpaarkeAi is a Code Page
- **ADR-007**: SpeFileStore facade -- CompareDocumentsTool accesses documents via facade
- **ADR-008**: Endpoint filters -- all new AI endpoints use filters
- **ADR-009**: Redis-first caching -- CapabilityManifest uses IMemoryCache with ADR-009 documented exception (metadata, <=15 min TTL); session hot cache in Redis
- **ADR-010**: DI minimalism -- R2 services organized into feature modules (AddAiSafetyModule, AddAiCapabilitiesModule, AddAiPersistenceModule, AddAiChatModule); Program.cs stays at ~12 lines
- **ADR-012**: Shared component library -- `@spaarke/ai-widgets` follows shared library patterns; widgets import from `@spaarke/ui-components`
- **ADR-013**: AI architecture -- extend BFF, not separate service; follow AI endpoint patterns
- **ADR-014**: AI caching -- cache expensive AI results (groundedness checks, citation lookups)
- **ADR-015**: AI data governance -- AMENDED: application logs follow strict data minimization; governed stores (audit log, work history, matter memory) are explicit exceptions with retention policies
- **ADR-016**: AI rate limits -- new AI endpoints under `ai-stream` rate limiter
- **ADR-021**: Fluent UI v9 -- all new widgets, all safety annotation UI
- **ADR-022**: PCF platform libraries -- SpaarkeAi is Code Page (React 19 bundled)

### MUST Rules

- MUST use single LLM call per turn (no meta-tools, no double-call penalty)
- MUST pre-select tools[] via CapabilityRouter before main LLM call (LLM never "sees" tools appear/disappear)
- MUST validate capability activations against user permission, tenant toggle, kill switch
- MUST block prompt injection attempts (hard block, not annotation)
- MUST stream optimistically then annotate retroactively for groundedness (stream + retroactive, not block)
- MUST use GPT-4o for main conversation and legal summarization (not mini)
- MUST use GPT-4o-mini for Layer 2 classification and session summarization (cost optimization)
- MUST use write-through to Cosmos (not idle-flush) for zero data loss
- MUST strip source content on cross-matter pivot (keep conclusions only, prevent privilege leakage)
- MUST use data-refreshed restore for widgets (re-fetch current data, not stale snapshot)
- MUST use append-only immutable policy for audit log (no deletes, no updates)
- MUST organize new BFF services into feature modules per ADR-010
- MUST prefer BackgroundService for async work; Azure Functions acceptable with documented justification when they are the better fit (ADR-001 under review)

### MUST NOT Rules

- MUST NOT create separate AI microservice (ADR-013)
- MUST NOT use Azure Functions without documented justification (ADR-001 under review -- Functions acceptable when they are the best solution, e.g., event-driven triggers)
- MUST NOT log full prompts or model responses to application logs (ADR-015)
- MUST NOT cache authorization decisions (ADR-009)
- MUST NOT use Fluent v8 or hard-coded colors (ADR-021)
- MUST NOT bundle React in PCF controls (ADR-022) -- N/A for this project (Code Page only)
- MUST NOT exceed 6-8 active tool schemas per turn (~3000 tokens tool budget)
- MUST NOT store full response text in audit log (hash only; full text in work history governed store)
- MUST NOT allow cross-tenant queries in Cosmos (partition key enforcement)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` for agent construction + middleware wrapping pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs` for keyword classification pattern (Layer 1 extends this)
- See `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` for feature module DI registration pattern
- See `src/client/shared/Spaarke.AI.Outputs/src/registry/output-registry.ts` for lazy-load widget registry pattern (migrated to `@spaarke/ai-widgets`)
- See `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useSseStream.ts` for SSE streaming implementation
- See `.claude/patterns/api/endpoint-definition.md` for endpoint registration
- See `.claude/patterns/api/service-registration.md` for DI module pattern

---

## Success Criteria

1. [ ] Three-pane shell renders with coordinated event flow between all panes - Verify: manual testing of all 5 cross-pane interactions
2. [ ] Dynamic capability orchestration routes >60% of turns via Layer 1 (no LLM), <10% via Layer 3 fallback - Verify: telemetry metrics over 100+ test turns
3. [ ] Single LLM call per turn always (no double-call) - Verify: Azure OpenAI request logs show exactly 1 chat-completions call per user turn
4. [ ] Prompt token budget stays within ~9000 tokens - Verify: Azure Monitor prompt token metric stays below threshold
5. [ ] Safety perimeter operational: prompt injection blocked, groundedness annotated, citations verified - Verify: test with known injection payloads, ungrounded claims, valid/invalid citations
6. [ ] Session restore in <500ms with widget data refresh - Verify: load test with session containing 25+ messages and 3 workspace widgets
7. [ ] Audit log is append-only with immutable policy - Verify: attempt update/delete operations fail; retention policy configured
8. [ ] All R1 SprkChat functionality preserved (SSE streaming, suggestions, citations, document upload, plan approval, action confirmation) - Verify: regression test of all 23 SSE event types
9. [ ] Widget serialize/restore works for all migrated widgets - Verify: close and resume session; all workspace/context widgets restore with current data
10. [ ] Layer 2 classification completes in <500ms - Verify: p95 latency measurement over 100+ classification calls
11. [ ] Cosmos write-through does not block streaming - Verify: SSE token latency unchanged with write-through enabled vs. disabled
12. [ ] ADR-015 amendment accepted and all logging compliant - Verify: grep application logs for content leakage; verify governed stores have retention policies

---

## Dependencies

### Prerequisites

- R1 SpaarkeAi Code Page deployed and functional (baseline for SprkChat preservation)
- Azure AI Content Safety resource provisioned (S0 SKU) with Prompt Shields + Groundedness API enabled
- Cosmos DB serverless account provisioned (`spaarke-ai` database)
- GPT-4o-mini deployment created on `spaarke-openai-dev` Azure OpenAI resource (separate from main GPT-4o deployment)
- AI Search index schema updated with `privilege_group_ids` field

### External Dependencies

- Azure AI Content Safety Groundedness API regional availability (verify West US 2)
- Azure OpenAI prompt caching GA for stable prefix optimization
- Citation reference data population (statutes, cases) in spaarke-references index -- can start with partial data

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| ADR-010 DI | Should R2's ~15+ new services use feature modules? | Yes, ADR-010 unchanged. Feature modules handle the scale. | New feature modules: AddAiSafetyModule, AddAiCapabilitiesModule, AddAiPersistenceModule, AddAiChatModule |
| ADR-015 Data Governance | Should audit log follow ADR-015 strictly or get an exception? | Amend ADR-015 with "Governed Data Stores" section. App logs strict; audit/work-history/memory are governed exceptions. | ADR-015 amendment is a task. Three store types with different content rules. |
| Frontend restructure | Enhance R1 in-place or rebuild the shell? | Rebuild the shell. This is a platform rebuild, not an enhancement. SprkChat preserved. | Project scoping changes: more tasks, clean architecture, migration of R1 widgets required |
| Widget library | New `@spaarke/ai-widgets` or extend `@spaarke/ai-outputs`? | New `@spaarke/ai-widgets` package. Separate from ai-outputs. | New package in build pipeline; ai-outputs eventually deprecated |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Cosmos DB provisioning**: Assumed provisioned before session persistence development begins (Phase 2). Bicep template created in infrastructure tasks.
- **Citation index population**: Assumed partial initial population is sufficient for R2 (InternalIndexProvider checks what's available; unverified citations get "Not found in available sources" annotation)
- **Privilege group mapping**: Assumed user -> matter access -> group_ids mapping derived from existing Dataverse security model (sprk_useraccesscontrol or equivalent). Implementation details resolved during task execution.
- **Layer 2 latency**: Assumed GPT-4o-mini classification is truly 200-500ms. If benchmarking shows otherwise, Layer 1 keyword coverage will be broadened to handle more turns.
- **R1 widget migration effort**: Assumed 1-2 hours per widget to add serialize/restore methods (13 widgets total = ~2-3 days of widget migration work)
- **Existing `@spaarke/ai-outputs` and `@spaarke/ai-context`**: These packages continue to exist during migration but are deprecated once `@spaarke/ai-widgets` and new session provider are complete. No breaking changes to other consumers (e.g., AnalysisWorkspace) during R2.

## Unresolved Questions

*Need answers during implementation (not blocking spec):*

- [ ] Maximum active capabilities per turn: design says 6-8 recommended. Validate with prompt token measurement during implementation. - Blocks: FR-304 (OrchestratorPromptBuilder token budget)
- [ ] Knowledge scope transitions: when RAG scope changes mid-conversation, do earlier retrieved chunks stay in context? - Blocks: FR-408 (cross-matter safety)
- [ ] Playbook guardrail enforcement: how does a regulated playbook prevent the router from adding extra capabilities? Boolean `exclusive` flag proposed. - Blocks: FR-306 (capability validation)
- [ ] Separate Azure OpenAI deployments: should GPT-4o-mini have its own deployment to avoid workload mixing? Microsoft recommends separation. - Blocks: FR-303 (CapabilityRouter Layer 2)
- [ ] Widget state serialization granularity: serialize widget TYPE + query parameters + layout (not data)? Or include minimal data for instant visual restore before data refresh? - Blocks: FR-203 (widget migration)

---

## Design Decisions Log

All significant design decisions resolved in design.md, carried into spec for traceability:

| # | Decision | Rationale |
|---|---|---|
| D-01 | Single LLM call per turn always (no meta-tools) | Eliminates double-call latency; prompt caching preserved |
| D-02 | Router pre-selects tools[] (BFF controls, LLM doesn't see changes) | Decouples tool selection from prompt content |
| D-03 | Stream + retroactive annotation for groundedness | Best tradeoff of perceived latency vs. safety for R2 |
| D-04 | GPT-4o for legal summarization (not mini) | Legal context requires high quality to retain qualifications |
| D-05 | 25 messages before summarization (not 15) | Legal conversations are denser |
| D-06 | Write-through to Cosmos (not idle-flush) | No data loss on interruption |
| D-07 | Prompt library split: personal in Cosmos, org/system in Dataverse | Matches admin UX expectations |
| D-08 | Data-refreshed widget restore (not stale snapshot) | Legal professionals need current data |
| D-09 | Strip source content on cross-matter pivot (keep conclusions only) | Prevents privilege leakage in conversation history |
| D-10 | Append-only audit log separate from work history | Compliance artifact with immutable policy |
| D-11 | No Foundry Agent Framework for orchestration | We need per-turn tool control + custom SSE; Foundry is capability provider only |
| D-12 | ISprkAgent boundary drawn now, multi-agent in R3 | Future-proofs without over-engineering R2 |
| D-13 | Webhook + polling for manifest refresh | Webhook is primary (low-latency), polling is backstop |
| D-14 | Architecture is model-agnostic | Config-driven model selection; estimates are for current GPT-4o |
| D-15 | Citation verification is multi-provider (IVerificationProvider) | Tenants use different legal databases; plug-in without code changes |
| D-16 | Citation verification is safety check + tool + knowledge source | First-class capability in the library, not just post-LLM check |
| D-17 | Frontend is a REBUILD (shell + event architecture), not enhancement | R1 structural limitations (single-subscriber events, dual buses, chat-centric hierarchy) prevent incremental R2 delivery |
| D-18 | New `@spaarke/ai-widgets` package (separate from ai-outputs) | Interactive widget contract is architecturally distinct from display-only output rendering |
| D-19 | ADR-010 unchanged, use feature modules for R2 services | Feature modules handle scale without modifying the ADR constraint |
| D-20 | ADR-015 amended with "Governed Data Stores" section | Distinguishes application logs (strict) from compliance/work-history stores (governed exceptions) |

---

*AI-optimized specification. Original design: design.md + architecture.md*
