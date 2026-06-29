# Project Plan: Spaarke AI Platform Unification R6

> **Last Updated**: 2026-06-25 (post-UAT)
> **Status**: Backend complete + deployed (PR #401 merged 2026-06-24); Surface completion sprint pending; closeout (089/090) deferred until UAT passes
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)
> **Surface audit**: [r6-deliverables-audit.md](r6-deliverables-audit.md) — gaps identified post-implementation

---

## 1. Executive Summary

**Purpose**: Architecture convergence phase that aligns the conversational chat-agent with the production-mature playbook side. Closes 9 systemic architecture gaps surfaced during R5 SC-18 walkthrough so R7+ feature work becomes "design a playbook in data + declare its output schema + reference scopes" while conversational primacy (NFR-01) is preserved.

**Scope**:
- 9 architectural pillars (data-driven persona, tool registry convergence, generic invoke_playbook, playbook FK fix, schema-aware rendering, tri-directional workspace state, cross-conversation memory, command router, widget visibility contract)
- 8 typed tool handlers (parallel workstream)
- 10-tool migration (single batch in Phase A per Q9)
- 4-action `outputSchema` migration (Pillar 5)
- Full Memory Management UI (Q7 scope expansion this session)
- Lightweight eval baseline (Q10)

**Timeline**: ~6–7 weeks (5–7 in spec; +1–2 weeks from Q7 expansion) | **Estimated Effort**: 60–90 engineer-days + ~10 days parallel handler workstream

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-008**: Endpoint Filters for Authorization — new R6 endpoints (`/api/ai/scopes/personas`, `/api/workspace/state`) inherit existing filter pattern
- **ADR-010**: DI Minimalism — module pattern for new registrations; ZERO new Program.cs lines; all services register inside existing modules (`AnalysisServicesModule`, `AiCapabilitiesModule`)
- **ADR-013**: AI Architecture — AI as BFF extension; PublicContracts facade boundary; new `IInvokePlaybookAi` facade per Q11
- **ADR-014**: AI Caching — Redis hot tier + Cosmos warm tier; per-tenant cache keys (`chat:session:{tenantId}:{sessionId}`)
- **ADR-015**: AI Data Governance — `context.tool_call_*` events log tool name + decision + timestamp ONLY; never user message content
- **ADR-016**: Rate Limiting — `ai-context` policy inherited by all R6 endpoints
- **ADR-018**: Feature Flag Discipline — no new feature flags introduced; R6 services unconditionally registered OR gated by existing capability flags
- **ADR-028**: Spaarke Auth v2 — no token snapshots; OBO + MI patterns preserved
- **ADR-029**: BFF Publish Hygiene — ≤60 MB compressed ceiling (hard); ≤+5 MB total R6 budget (spec NFR-02); per-task verification required
- **ADR-030**: PaneEventBus — closed at 4 channels (workspace / context / conversation / safety); additive event types only
- **ADR-031**: Stage Lifecycle — 4 stages closed (welcome / loading / active-chat / review); no new stages

**Additional relevant**:
- ADR-012 (Shared Components / Fluent v9) — Pillar 6c trace widget + Pillar 7 memory UI use Fluent v9
- ADR-027 (Dataverse Solution Management) — `sprk_aipersona` entity creation follows unmanaged solution conventions

**From Spec**:
- ZERO Microsoft Agent Framework references in code OR design (NFR-04 binding)
- Pre-fill flow signatures + 45s timeout + `useAiPrefill` UNCHANGED (NFR-07 binding); R6 may extend `IWorkspacePrefillAi` with new methods but existing signatures stay
- 11 production node executors preserved (NFR-08 binding); R6 does NOT modify any
- M365 Copilot integration thinness preserved (NFR-09); Agent Gateway endpoints stay thin adapters
- 8K system prompt budget preserved (NFR-10)
- Safety pipeline preserved (NFR-13): PromptShield + Groundedness + Citations + Privilege + Cross-matter middleware chain unchanged

### Key Technical Decisions (resolved Q1–Q11 + sequencing)

| ID | Decision | Impact |
|---|---|---|
| **Q1** | Persona inheritance = most-specific-wins (SYS- < CUST- < playbook-attached) | FR-03 resolution logic in `SprkChatAgentFactory.CreateAgentAsync` |
| **Q2** | Persona = standalone `sprk_aipersona` (5th scope entity) | FR-01 entity model; new Dataverse table follows existing scope schema |
| **Q3** | Scope admin UI deferred R7; Power Apps Dataverse forms in R6 | FR-05; no custom UI work in R6 for persona / outputSchema authoring |
| **Q4** | Workspace tab persistence = hybrid (agent-ephemeral / user-pinned-persistent) | FR-31, FR-32; Redis 24h TTL + Cosmos durable on pin |
| **Q5** | `outputSchema` on action (intrinsic shape); destination + widgetType on **node config** in playbook canvas; `StructuredOutputStreamWidget` schema-aware; duplicate-fire fix at `CapabilityRouter` (NOT action metadata). Migrate 4 existing actions: summarize-chat, summarize-workspace, matter-prefill, project-prefill. Pre-fill hook signatures + 45s timeout + `useAiPrefill` UNCHANGED per NFR-07. | FR-27 rewritten (`outputSchema` only on action); FR-30 rewritten (CapabilityRouter dedup) |
| **Q6** | 6 hard + 4 soft slashes per spec.md proposal: hard = `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter [matterId]`, `/pin`; soft = `/summarize`, `/draft`, `/extract-entities`, `/analyze` | FR-49, FR-50 |
| **Q7** | **SCOPE EXPANSION**: Build full memory management UI in R6 (was R7-deferred in spec). Adds ~1–2 weeks to Phase C. Pillar 7 ships Pinned Memory CRUD + visualization in Context pane. | FR-47 + new FR for Pinned Memory CRUD UI |
| **Q8** | Workspace conflict resolution = user wins (agent checks `lastUserEditAt`, refuses on write if newer; no OT/CRDT) | FR-40 implementation |
| **Q9** | Tool registry migration = **all 10 in single Phase A batch** (faster but riskier; demands comprehensive regression test + clear rollback) | FR-12 packaged as single large task with batch regression gate |
| **Q10** | Lightweight eval baseline in R6 (markdown transcripts per persona+playbook); full harness R7 | Single eval-baseline task added to Phase D |
| **Q11** | invoke_playbook facade = new `IInvokePlaybookAi` in `Services/Ai/PublicContracts/` per ADR-013 | FR-21 facade design |
| **seq-1** | 8 handlers order: Wave 1 deterministic (DateExtractor, FinancialCalculator, ClauseComparison, FinancialCalculation) parallel → Wave 2 LLM-assisted (EntityExtractor, ClauseAnalyzer, RiskDetector, InvoiceExtraction) parallel | FR-13–20 grouping |
| **seq-2** | Pillar 6 split: 6a = state model + persistence + endpoint + prompt snapshot (FR-31–34); 6b = chat tools for tab mutation + user affordances + conflict (FR-35, 39, 40); 6c = execution-trace widget + additive PaneEventBus events (FR-36, 37, 38). 6a gates 6c; 6b parallel with 6c after 6a. | Phase C internal sequencing |
| **seq-3** | ADR-015 trace logging: `context.tool_call_*` events log tool name + decision + timestamp ONLY; never user message text. Deterministic IDs (`matterId`, `scopeId`) acceptable. | Bound into Pillar 6c task constraints |

### Discovered Resources

**Applicable ADRs** (verified by resource discovery):
- `.claude/adr/ADR-008-endpoint-filters.md` — authorization filters
- `.claude/adr/ADR-010-di-minimalism.md` — module DI pattern
- `.claude/adr/ADR-012-shared-components.md` — Fluent v9 component patterns
- `.claude/adr/ADR-013-ai-architecture.md` — AI as BFF extension + PublicContracts facade boundary
- `.claude/adr/ADR-014-ai-caching.md` — Redis hot + Cosmos warm
- `.claude/adr/ADR-015-ai-data-governance.md` — no user message logging
- `.claude/adr/ADR-016-ai-rate-limits.md` — `ai-context` policy
- `.claude/adr/ADR-018-feature-flags.md` — no new flags
- `.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md` — unmanaged solution conventions
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — Spaarke Auth v2 (OBO + MI)
- `.claude/adr/ADR-029-bff-publish-hygiene.md` — ≤60 MB ceiling
- `.claude/adr/ADR-030-pane-event-bus.md` — 4-channel closed
- `.claude/adr/ADR-031-stage-lifecycle.md` — 4-stage closed

**Applicable Skills**:
- `.claude/skills/dataverse-create-schema/` — create `sprk_aipersona` entity + `sprk_analysisaction.outputSchema` field + node config schema changes via Web API + PowerShell
- `.claude/skills/dataverse-deploy/` — deploy solutions / data rows to Dataverse via PAC CLI
- `.claude/skills/dataverse-mcp-usage/` — Dataverse MCP for schema validation + data seeding
- `.claude/skills/bff-deploy/` — deploy BFF API to Azure App Service
- `.claude/skills/code-review/` — quality gate for FULL-rigor tasks
- `.claude/skills/adr-check/` — ADR compliance verification at Step 9.5
- `.claude/skills/fluent-v9-component/` — Pillar 6c trace widget + Pillar 7 memory UI
- `.claude/skills/pcf-deploy/` — N/A for R6 (no new PCF; existing PCFs untouched)
- `.claude/skills/push-to-github/` — commit gate

**Relevant patterns** (`.claude/patterns/`):
- `ai/analysis-scopes.md`, `ai/indexing-pipeline.md`, `ai/streaming-endpoints.md`, `ai/text-extraction.md`
- `dataverse/entity-operations.md`, `dataverse/web-api-client.md`, `dataverse/relationship-navigation.md`, `dataverse/polymorphic-resolver.md`
- `api/endpoint-definition.md`, `api/endpoint-filters.md`, `api/error-handling.md`, `api/service-registration.md`
- `ui/fluent-v9-component-authoring.md`, `ui/fluent-v9-theming.md`
- `auth/oauth-scopes.md`, `auth/obo-flow.md`, `auth/spaarke-sso-binding.md`
- `caching/distributed-cache.md`
- `testing/unit-test-structure.md`, `testing/mocking-patterns.md`

**Architecture docs** (canonical references):
- `docs/architecture/playbook-architecture.md` — node type system, execution engine, canvas data model
- `docs/architecture/scope-architecture.md` — scope CRUD, SYS-/CUST- ownership, inheritance, SaveAs
- `docs/architecture/chat-architecture.md` — chat agent factory, middleware pipeline, capability routing
- `docs/architecture/AI-ARCHITECTURE.md` — Tier 4 architecture, scope library + composition + execution runtime
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — three-pane shell, PaneEventBus integration
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — `@spaarke/*` library inventory, PaneEventBus 4-channel contract
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — widget registry, two-wrapper architecture
- `docs/guides/JPS-AUTHORING-GUIDE.md` — 6-layer JPS pipeline, `$choices` resolution
- `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` — scope authoring, playbook composition
- `docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md` — canonical "playbook from anywhere" pattern (Pillar 3 reuses this)
- `docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md` — chat-agent BOTH playbook AND RAG routing; `forceMode` override preserved

**Reusable code patterns** (no new code where these suffice):
- `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs` → rename target for `IToolHandler` (FR-06)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs` + `IInsightsAi.cs` + `IInvoiceAi.cs` + `IBriefingAi.cs` → pattern for new `IInvokePlaybookAi` facade
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` → pattern for `AnalysisPersona` resolver methods
- `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` → pattern for `/api/ai/scopes/personas` endpoint
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` → DI module for R6 service registrations per ADR-010
- Cosmos `memory` container write-through pattern → reused for workspace_tabs + pinned_context
- Redis per-tenant cache key pattern → reused for workspace state hot tier

**R5 carry-forward** (mandatory reading):
- `projects/spaarke-ai-platform-unification-r5/notes/lessons-learned.md` — 4 gap families (A/B/C/D), 7 carry-forward patterns, 3 process learnings. Each R6 pillar maps to a gap family.

---

## 3. Implementation Approach

### Phase Structure

```
Phase A (Week 1–2, heavier than spec from Q9 batch)
├─ Pillar 1: Persona as scope
├─ Pillar 2-infra + 10-tool batch migration
├─ Pillar 3: Generic invoke_playbook
└─ Pillar 4: PlaybookExecutionEngine FK fix

Phase B (Week 2–3)
└─ Pillar 5: outputSchema on action + destination on node + schema-aware widget + CapabilityRouter dedup

Phase C (Week 3–6, Q7 expansion adds 1–2 weeks)
├─ Pillar 6a: state model + persistence + endpoint + prompt snapshot
├─ Pillar 6b: chat tools for tab mutation + user affordances + conflict
├─ Pillar 6c: execution-trace widget + additive PaneEventBus events
├─ Pillar 7: memory + full Pinned Memory UI (Q7)
└─ Pillar 9: widget visibility contract

Phase D (Week 6–7)
├─ Pillar 8: Command Router
├─ Integration: full vertical-slice validation
├─ Eval baseline: markdown transcripts per persona+playbook
└─ Wrap-up

Parallel handler workstream (spans Phase A–C)
├─ Shared infra (registry + adapter wiring)
├─ Wave 1 deterministic (4 parallel): DateExtractor, FinancialCalculator, ClauseComparison, FinancialCalculation
└─ Wave 2 LLM-assisted (4 parallel): EntityExtractor, ClauseAnalyzer, RiskDetector, InvoiceExtraction
```

### Critical Path

**Phase A** (blocking sequence):
1. Pillar 1 entity creation → Pillar 1 endpoint + scope resolver → Pillar 1 persona seed data → Pillar 1 agent factory integration (FR-01 → FR-02 → FR-04 → FR-03)
2. Pillar 2 infra (IToolHandler rename + JsonSchema + adapter) → Pillar 2 batch migration (10 tools) → Pillar 2 regression test gate (FR-06–11 → FR-12)
3. Pillar 3 facade + tool (depends on Pillar 2 infra) → specialized bridge removal (FR-21 → FR-22 → FR-24)
4. Pillar 4 playbook FK fix → SessionSummarizeOrchestrator refactor (FR-25 → FR-26)
5. Phase A exit gate (all 4 exit criteria met)

**Phase B**:
6. `sprk_analysisaction.outputSchema` field + Dataverse migration → node config schema for destination/widgetType → 4-action data migration (summarize-chat, summarize-workspace, matter-prefill, project-prefill) → widget schema-aware rendering → CapabilityRouter dedup → Phase B exit gate

**Phase C** (Pillar 6 internal sequencing):
7. Pillar 6a state model + Cosmos+Redis persistence + endpoint + prompt snapshot → unblocks 6b + 6c + 7 + 9
8. Pillar 6b/6c/7/9 parallel after 6a
9. Pillar 7 memory infrastructure → Pinned Memory UI (Q7) → memory UI integration test
10. Pillar 6 + 7 + 9 cross-pillar integration test → Phase C exit gate

**Phase D**:
11. Pillar 8 CommandRouter parser → hard/soft slashes wiring → references resolver → composition test
12. Full vertical-slice integration test (Summarize playbook end-to-end exercises every pillar)
13. Eval baseline capture → wrap-up

### High-Risk Items

- **R1 Q9 batch tool migration** (Phase A, T0XX): All 10 chat tools migrate in single task → high blast radius. Mitigation: regression test covering every code path; rollback = git revert + DI flag for hardcoded classes; staging validation gate.
- **R2 Pre-fill regression** (Phase B, T0XX): `outputSchema` migration of matter-prefill + project-prefill actions could break NFR-07 binding. Mitigation: explicit before/after pre-fill test in every Pillar 5 task; hook signatures untouched.
- **R3 Pillar 6 cross-pillar contract drift** (Phase C, multiple): 6a state model is consumed by 7 + 9. Drift in 6a's `WorkspaceTab` shape would break 7's memory composition + 9's widget visibility. Mitigation: 6a lands first; 7 + 9 reference 6a interface tokens.
- **R4 Q7 calendar slippage**: Memory UI adds 1–2 weeks. Mitigation: monitor Phase B duration; re-defer Memory UI if Phase B slips by >5 days.
- **R5 BFF publish-size budget breach (NFR-02)**: 8 new handlers + new facade + memory services + workspace state service could push ≤+5 MB. Mitigation: per-task size verification; total tracked in TASK-INDEX.
- **R6 ADR-015 trace event leak**: Pillar 6c new events could accidentally log user message content. Mitigation: explicit constraint in Pillar 6c task; reviewer checklist; data-governance audit before Phase C exit.

---

## 4. Phase Breakdown

### Phase A: Data-driven Foundation (Week 1–2)

**Objectives**:
1. Persona becomes a Dataverse-driven scope
2. Tool registry generalized + all 10 pre-R5 chat tools migrated to data-driven registration
3. One generic `invoke_playbook` chat tool replaces specialized bridges
4. Chat `/summarize` routes through `PlaybookExecutionEngine` (no alternate-key bypass)

**Deliverables (D-codes)**:
- D-A-01: `sprk_aipersona` entity created (schema + relationships + SYS-/CUST- enforcement)
- D-A-02: `GET /api/ai/scopes/personas` endpoint
- D-A-03: Persona resolution in `IScopeResolverService` (most-specific-wins)
- D-A-04: Default SYS- persona row seeded (verbatim current `BuildDefaultSystemPrompt()` text)
- D-A-05: `SprkChatAgentFactory.CreateAgentAsync` uses scope-resolved persona
- D-A-06: `IAnalysisToolHandler` renamed to `IToolHandler`; type alias preserved
- D-A-07: `AvailableInContexts` enum + `JsonSchema` field added to `AnalysisTool` DTO + Dataverse column
- D-A-08: Execution context split (`ToolExecutionContext` for playbook; `ChatInvocationContext` for chat)
- D-A-09: `ToolHandlerToAIFunctionAdapter` built
- D-A-10: `SprkChatAgentFactory.ResolveTools()` reads from `sprk_analysistool` rows
- D-A-11: 10 pre-R5 chat tools migrated to `IToolHandler` + `sprk_analysistool` rows (Q9 batch; comprehensive regression test gate)
- D-A-12: `IInvokePlaybookAi` facade in `Services/Ai/PublicContracts/`
- D-A-13: Generic `invoke_playbook(playbookId, parameters)` chat tool
- D-A-14: Dynamic playbook list in tool description at build time
- D-A-15: `InvokeSummarizePlaybookTool` + `InvokeInsightsQueryTool` bridges removed
- D-A-16: Playbook FK fix: `summarize-document-for-chat@v1` → `SUM-CHAT@v1` action
- D-A-17: `SessionSummarizeOrchestrator` refactored to invoke `PlaybookExecutionEngine.ExecuteAsync(playbookId)`
- D-A-18: Phase A exit-gate validation (4 exit criteria from spec)

**Inputs**: spec.md FR-01–26; design.md §1.2 (Pillar 1) §1.3 (Pillar 2) §1.4 (Pillar 3) §1.5 (Pillar 4)
**Outputs**: ~22 task files (001–029 range with gaps); merged PRs landing on master via worktree branch
**Dependencies**: existing scope library + JPS pipeline (production); R5 closed; spec authored

### Phase B: Schema-Aware Output (Week 2–3)

**Objectives**:
1. `outputSchema` field added to `sprk_analysisaction` (action-fixed intrinsic shape)
2. Node config in playbook canvas adds `destination` + `widgetType` (per-playbook routing)
3. `StructuredOutputStreamWidget` renders array + object fields schema-aware
4. CapabilityRouter dedup eliminates duplicate-fire structurally

**Deliverables**:
- D-B-01: `sprk_analysisaction.outputSchema` field (JSON schema describing data shape)
- D-B-02: Node config schema extension for `destination` (enum: chat / workspace / form-prefill / side-effect) + `widgetType`
- D-B-03: Migrate `summarize-document-for-chat@v1` action: populate outputSchema from $choices schema; node destination = chat
- D-B-04: Migrate `summarize-document-for-workspace@v1`: outputSchema populated; node destination = workspace
- D-B-05: Migrate matter-prefill action: outputSchema populated; node destination = form-prefill; pre-fill regression test (NFR-07 binding)
- D-B-06: Migrate project-prefill action: outputSchema populated; node destination = form-prefill; pre-fill regression test (NFR-07 binding)
- D-B-07: `StructuredOutputStreamWidget` reads outputSchema; renders array → bullets (fixes R5 TL;DR bug)
- D-B-08: Widget renders object → labeled key-value blocks (fixes R5 Entities bug)
- D-B-09: CapabilityRouter dedup: one user intent → one route → one playbook → one render (duplicate-fire fix)
- D-B-10: Phase B exit-gate validation (4 exit criteria from spec)

**Inputs**: Phase A complete (Pillar 4 PlaybookExecutionEngine refactor lands)
**Outputs**: ~10 task files (030–049 range with gaps)
**Dependencies**: Pillar 4 (chat `/summarize` routes through engine) for end-to-end dedup test

### Phase C: Tri-directional Workspace + Memory + Visibility (Week 3–6)

**Objectives**:
1. Pillar 6a: Workspace state model + persistence + endpoint + per-turn agent prompt snapshot
2. Pillar 6b: Chat tools for tab mutation + user affordances + conflict resolution
3. Pillar 6c: Execution-trace widget + additive PaneEventBus events
4. Pillar 7: Memory utilization + full Pinned Memory UI (Q7 expansion)
5. Pillar 9: Widget visibility contract (per-widget `getAgentVisibleState`)

**Deliverables (Pillar 6a — gates 6b/6c/7/9)**:
- D-C-01: `WorkspaceTab` canonical TypeScript interface with typed widgetType/widgetData/sessionId/visibleToAssistant/sourceProvenance/matterContext/isPinned/canEdit/lastUserEditAt
- D-C-02: WorkspaceStateService.cs — Redis hot tier (24h TTL) + Cosmos durable on pin
- D-C-03: `GET /api/workspace/state` BFF endpoint (tenant + session scoped; respects getAgentVisibleState)
- D-C-04: `WorkspaceStateService` wired into `SprkChatAgentFactory.CreateAgentAsync` — per-turn snapshot in agent prompt "Workspace State" block

**Deliverables (Pillar 6b)**:
- D-C-05: `send_workspace_artifact` chat tool (creates tab via `sprk_analysistool` row)
- D-C-06: `update_workspace_tab` chat tool (modifies; checks `lastUserEditAt` per Q8)
- D-C-07: `close_workspace_tab` chat tool
- D-C-08: "Send to Workspace" affordance on chat assistant messages
- D-C-09: "Add to Assistant" toggle on user-created tabs (flips `visibleToAssistant: true`)
- D-C-10: "Pin to Matter" persists tab attached to matter record
- D-C-11: Conflict resolution per Q8: agent reads timestamp; refuses on stale write with re-read prompt

**Deliverables (Pillar 6c)**:
- D-C-12: Additive `context.*` PaneEventBus event types: `tool_call_started`, `tool_call_completed`, `knowledge_retrieved`, `playbook_node_executing`, `playbook_node_completed`, `decision_made` (ADR-015 binding: no user message text)
- D-C-13: Additive `workspace.*` PaneEventBus event types: `user_selection`, `tab_edited`, `tab_focused`, `tab_provenance_clicked`
- D-C-14: `ExecutionTraceWidget.tsx` (Context-pane widget; subscribes to context.* events; ordered timeline)
- D-C-15: Trace widget registered with `ContextWidgetRegistry`
- D-C-16: Telemetry from chat agent + playbook execution emits new context.* events

**Deliverables (Pillar 7)**:
- D-C-17: Summarization compression service (replace oldest M turns with LLM summary when sliding window exceeds budget)
- D-C-18: Pinned-context entity in Cosmos `memory` container (pinType: user-preference / system-rule / matter-fact)
- D-C-19: Selective recall via embedding similarity (vectorize each turn at write time; retrieve old turns by similarity)
- D-C-20: Hierarchical memory composition (recent verbatim + compressed mid + retrieved old)
- D-C-21: `MatterMemoryService` wired into chat-agent system prompt assembly (already exists; activation only)
- D-C-22: Shared token budget tracker (factory + document context + knowledge + memory share 8K budget per NFR-10)
- D-C-23: Agent recognition of "remember X" / "forget X" / "always X" via existing CapabilityRouter → pinned-context tool
- D-C-24: **Pinned Memory CRUD UI** (Q7 expansion: list pinned items + create/edit/delete + visualize in Context pane)
- D-C-25: Pinned Memory UI integration with Pillar 6a workspace state

**Deliverables (Pillar 9)**:
- D-C-26: `getAgentVisibleState(): SerializedWidgetState` TypeScript interface
- D-C-27: `WorkspaceWidgetRegistry` extension with optional `getVisibleState?` field
- D-C-28: `getAgentVisibleState()` implemented per widget type: Summary, DocumentViewer, Dashboard, Table
- D-C-29: Per-turn agent prompt builder gathers visible state from each Assistant-visible tab (per ADR-015 + Pillar 6a)
- D-C-30: Privacy default verified: widgets that should NOT expose to LLM don't appear

**Phase C exit gate**: 6 exit criteria from spec (Pillars 6, 7, 9) — agent workspace awareness, tab update flow, user affordances, execution trace, cross-conversation memory, pinned facts persist

**Inputs**: Phase B complete
**Outputs**: ~30 task files (050–079 range)
**Dependencies**: Pillar 6a is the gate for 6b/6c/7/9 (state model interfaces are consumed by all four)

### Phase D: Command Router + Integration + Closeout (Week 6–7) ✅ SIGNED OFF 2026-06-29

> **Phase D exit gate signed off** 2026-06-29 by task 089. Evidence: [`notes/phase-d-exit-checklist.md`](notes/phase-d-exit-checklist.md). All 5 exit criteria green; tasks 080–088 ✅.

**Objectives**:
1. Pillar 8: Slash/hash/at command vocabulary + parser + execution
2. Vertical-slice validation (per spec §6)
3. Lightweight eval baseline captured (Q10)
4. Project wrap-up

**Deliverables**:
- D-D-01: `CommandRouter.ts` parser (Intent { command, references[], rawText })
- D-D-02: Hard slashes (deterministic, bypass LLM): `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter [matterId]`, `/pin`
- D-D-03: Soft slashes (intent shortcuts via agent): `/summarize`, `/draft`, `/extract-entities`, `/analyze`
- D-D-04: References resolver: `#scope` / `@<entity>` / `#<filename>` at parse time
- D-D-05: Composition: `/summarize #engagement-letter.docx`, `/draft response to @opposing-counsel about #motion-to-dismiss`
- D-D-06: `/help` UI affordance discoverable from chat input bar
- D-D-07: Natural language still works alongside slashes (CapabilityRouter unchanged)
- D-D-08: Full vertical-slice integration test (Summarize playbook end-to-end — every pillar exercised)
- D-D-09: Lightweight eval baseline: markdown transcripts per persona + playbook captured (Q10)
- D-D-10: Wrap-up task (090-project-wrap-up.poml per CLAUDE.md §7)

**Inputs**: Phase C complete
**Outputs**: ~10 task files (080–089 range + 090 wrap-up)
**Dependencies**: All pillars complete for vertical-slice validation

### Parallel: 8 Typed Tool Handler Workstream

**Objectives**: Build 8 missing typed handlers the data model anticipates (FR-13–20)

**Deliverables**:
- D-H-00: Handler infra shared task (registration pattern + base class wiring) — gates Wave 1 + Wave 2
- D-H-01 (Wave 1, parallel): `DateExtractorHandler` (pure deterministic)
- D-H-02 (Wave 1, parallel): `FinancialCalculatorHandler` (pure deterministic)
- D-H-03 (Wave 1, parallel): `ClauseComparisonHandler` (pure deterministic, structural diff)
- D-H-04 (Wave 1, parallel): `FinancialCalculationToolHandler` (pure deterministic, currency-aware)
- D-H-05 (Wave 2, parallel): `EntityExtractorHandler` (LLM-assisted NER + validation)
- D-H-06 (Wave 2, parallel): `ClauseAnalyzerHandler` (LLM-assisted contract clause structuring)
- D-H-07 (Wave 2, parallel): `RiskDetectorHandler` (LLM-assisted + code-based severity scoring)
- D-H-08 (Wave 2, parallel): `InvoiceExtractionToolHandler` (LLM extraction + line-item arithmetic)
- D-H-09: Handler dispatch test from playbook context (per FR-13–20 acceptance)
- D-H-10: Handler dispatch test from chat context where applicable (Both context)

**Inputs**: D-A-06 (`IToolHandler` rename) + D-A-09 (adapter) complete
**Outputs**: ~10 task files (100–109 range)
**Dependencies**: Phase A handler infra; spans Phase A–C

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Cosmos DB containers (`sessions`, `prompts`, `audit`, `memory`, `feedback`) | Production | Low | Reused; no new container needed (Pillar 6 reuses `memory`; Pillar 7 reuses `memory`) |
| Azure OpenAI (GPT-4o + GPT-4o-mini) | Production | Low | No new deployments; existing CapabilityRouter Layer 2 reused |
| Azure AI Search indexes | Production | Low | No new indexes; existing session-files + knowledge + RAG indexes reused |
| Insights Engine R2 coordination | External | Medium | Wave F may impact Pillar 3 `invoke_playbook` for `insights.query` capability; coordinate via `notes/insights-r2-coordination.md` |
| Power Apps Dataverse forms | Internal | Low | Q3: auto-generated forms suffice; admin creates persona + outputSchema rows manually in R6 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `IScopeResolverService` | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Production |
| Scope library (Actions, Skills, Knowledge, Tools) | 4 production entities | Production |
| `PlaybookExecutionEngine` + 11 node executors | `src/server/api/Sprk.Bff.Api/Services/Ai/Playbook/` | Production |
| `IAnalysisToolHandler` (rename target) | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs` | Production |
| Auto-discovery registry pattern | DI collection injection | Production |
| `SprkChatAgentFactory` | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Production |
| `CapabilityRouter` (3-tier) | `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` | Production |
| Safety pipeline (PromptShield + Groundedness + Citations + Privilege + Cross-matter) | `SafetyPipelineMiddleware` | Production |
| PublicContracts facades | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` | Production |
| 4-channel PaneEventBus | `src/client/shared/Spaarke.AI.Widgets/src/events/` | Production |
| 4-stage shell lifecycle | `src/solutions/SpaarkeAi/src/state/SessionState.ts` | Production |
| `MatterMemoryService` (activation target) | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/` | Production (unwired) |

---

## 6. Testing Strategy

**Unit Tests** (per FR + per service):
- Handler unit tests (8 new + 10 migrated = 18 handlers)
- Persona resolver tests (most-specific-wins + edge cases)
- Workspace state service tests (Redis + Cosmos)
- Memory composition tests (recent + compressed + retrieved)
- CommandRouter parser tests (hard + soft + references + composition)
- Output schema validation tests
- Adapter tests (`ToolHandlerToAIFunctionAdapter`)

**Integration Tests**:
- Phase A: 10-tool batch migration regression test (every code path)
- Phase B: pre-fill flow before/after migration (matter + project) — NFR-07 binding
- Phase B: duplicate-fire elimination (typing `/summarize` produces ONE output)
- Phase C: workspace state agent prompt snapshot (open tabs visible in prompt)
- Phase C: trace widget event ordering (chat agent + playbook execution events arrive in order)
- Phase C: memory composition end-to-end (yesterday's matter analysis retrievable)
- Phase C: conflict resolution (agent refuses stale write)
- Phase D: vertical-slice validation (Summarize playbook end-to-end — every pillar)

**E2E Tests** (Vertical-Slice Validation per spec §6):
- Summarize playbook: persona resolved from `sprk_aipersona`, tools from `sprk_analysistool`, workspace state in prompt, memory composition, `/summarize` triggers `invoke_playbook`, routes via `PlaybookExecutionEngine` with FK chain, output renders schema-aware, Context pane shows trace, "Send to Workspace" + "Make it shorter" + `/clear` all work

**ADR-015 Audit** (Phase C exit gate):
- Trace event logs reviewed: tool name + decision + timestamp ONLY; no user message content

---

## 7. Acceptance Criteria

### Phase A Exit Criteria (from spec §Success Criteria)
- [ ] Chat-agent tool list driven by `sprk_analysistool` rows with `AvailableInContexts ∋ "Chat"`
- [ ] Persona is a Dataverse-driven scope (`sprk_aipersona`); tenant override changes chat agent voice without code deploy
- [ ] One generic `invoke_playbook` chat tool exists; `InvokeSummarizePlaybookTool` + `InvokeInsightsQueryTool` removed
- [ ] `SessionSummarizeOrchestrator` routes through `PlaybookExecutionEngine` (no alternate-key lookup)

### Phase B Exit Criteria
- [ ] Workspace renderer handles array-typed (`tldr`) fields correctly (bulleted list, not raw JSON)
- [ ] Workspace renderer handles object-typed (`entities`) fields correctly (labeled groups, not raw JSON literal)
- [ ] `outputSchema` declared on action; destination/widgetType on node config
- [ ] Duplicate-fire eliminated at CapabilityRouter (typing `/summarize` produces ONE render)

### Phase C Exit Criteria
- [ ] Agent has accurate workspace awareness ("what's on the workspace?" answered correctly)
- [ ] "Update the summary in Tab 1" dispatches `update_workspace_tab`; targeted tab updates; conflict-detection works
- [ ] "Send to Workspace" + "Add to Assistant" + "Pin to Matter" all functional with persistence verified
- [ ] Context pane shows live execution trace in real time
- [ ] Cross-conversation memory recalls prior-matter context
- [ ] Pinned facts persist as user preferences across sessions
- [ ] **Q7**: Pinned Memory CRUD UI in Context pane functional

### Phase D Exit Criteria — ✅ SIGNED OFF 2026-06-29 ([`notes/phase-d-exit-checklist.md`](notes/phase-d-exit-checklist.md))
- [x] `/help` works and discoverable — tasks 081 + 085
- [x] Hard slashes bypass LLM (<100ms latency; no Azure OpenAI request) — task 081 (43 tests + 087 vertical-slice)
- [x] Soft slashes route via agent with prioritized intent — task 082 (38 tests + 18 BFF tests); successor PR #509 preserves invariant via FR-23 replacement
- [x] References resolve at parse time — tasks 083 + 084 (composition tests)
- [x] All R6 changes have integration test coverage (vertical-slice validation per §6) — tasks 086/087/088

### Cross-cutting
- [ ] BFF publish size ≤+5 MB total across R6 (NFR-02)
- [ ] No new ADRs (NFR-03)
- [ ] Pre-fill flow signatures + 45s timeout + `useAiPrefill` UNCHANGED (NFR-07)
- [ ] All 11 node executors UNMODIFIED (NFR-08)
- [ ] ZERO Microsoft Agent Framework references in code or design (NFR-04)
- [ ] 4-channel PaneEventBus + 4-stage shell preserved (NFR-05, NFR-06)
- [ ] Safety pipeline preserved (NFR-13)
- [ ] All HIGH-severity CVEs resolved (per `dotnet list package --vulnerable --include-transitive`)
- [ ] All FULL-rigor tasks pass `code-review` + `adr-check` at Step 9.5

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Q9 batch 10-tool migration breaks chat path | Medium | High | Comprehensive regression test covering every code path; rollback = git revert + DI flag for hardcoded classes; staging gate before master merge |
| R2 | Pre-fill regression during Pillar 5 schema migration (matter + project) | Low | High | NFR-07 binding; explicit before/after test in every Pillar 5 task; hook signatures + 45s timeout + `useAiPrefill` UNCHANGED |
| R3 | Pillar 6 cross-pillar contract drift (6a state model consumed by 7 + 9) | Medium | Medium | 6a lands first; 7 + 9 reference 6a interfaces; integration test at Phase C exit |
| R4 | Q7 calendar slippage from Memory UI scope expansion | Medium | Medium | Monitor Phase B duration; re-defer Memory UI if Phase B slips >5 days |
| R5 | BFF publish size budget breach (NFR-02 ≤+5 MB) | Low | Medium | Per-task size verification; current baseline ~45.65 MB; ceiling 60 MB hard; weekly delta in TASK-INDEX |
| R6 | ADR-015 trace event accidentally logs user message content | Low | Medium | Explicit constraint in Pillar 6c tasks; reviewer checklist; data-governance audit before Phase C exit |
| R7 | Insights Engine R2 coordination conflicts with Pillar 3 `invoke_playbook` for `insights.query` | Medium | Medium | Coordinate via `notes/insights-r2-coordination.md`; `forceMode` override preserved per `INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md` |
| R8 | Build verification breakage between parallel-agent waves | Medium | Low | Per project-pipeline: build BFF + relevant frontend packages after each wave; STOP if any wave fails build |

---

## 9. Parallel Execution Opportunities

Detailed per-task parallel groups land in `tasks/TASK-INDEX.md`. Key groupings:

| Group | Phase | Tasks | Prerequisite | Notes |
|-------|-------|-------|--------------|-------|
| A-G1 | A | Pillar 1 entity + endpoint + persona seed | none | Parallel after spec.md (3 tasks) |
| A-G2 | A | Pillar 1 agent factory + Pillar 2 IToolHandler infra | A-G1 | Parallel (2 tasks) |
| A-G3 | A | Pillar 2 adapter + Pillar 3 facade + Pillar 4 FK fix | A-G2 | Parallel (3 tasks) |
| A-G4 | A | Pillar 2 batch migration (single big task; Q9) | A-G3 | Sequential (1 task, high-risk gate) |
| A-G5 | A | Pillar 3 specialized bridge removal + Pillar 4 orchestrator refactor + Phase A exit gate | A-G4 | Parallel (3 tasks) |
| B-G1 | B | Action outputSchema field + node config schema | Phase A complete | Parallel (2 tasks) |
| B-G2 | B | 4-action data migration (chat + workspace + matter-prefill + project-prefill) | B-G1 | Parallel (4 tasks; pre-fill tasks include NFR-07 regression test) |
| B-G3 | B | Widget array-rendering + widget object-rendering + CapabilityRouter dedup + Phase B exit gate | B-G2 | Parallel (4 tasks) |
| C-G1 | C-6a | WorkspaceTab interface + WorkspaceStateService + endpoint + agent factory snapshot wiring | Phase B complete | Sequential within (gates 6b/6c/7/9) |
| C-G2 | C-6b/6c/7/9 | Pillar 6b chat tools + Pillar 6c trace widget + Pillar 7 memory infra + Pillar 9 visibility contract | C-G1 | Parallel (4 tracks; each with sub-tasks) |
| C-G3 | C-7 | Pinned Memory UI (Q7 expansion) | C-G2 memory infra | Sequential within track |
| C-G4 | C | Phase C cross-pillar integration test + exit gate | C-G2 + C-G3 | Sequential |
| D-G1 | D | CommandRouter parser + hard slashes + soft slashes + references | Phase C complete | Parallel (4 tasks) |
| D-G2 | D | Composition tests + `/help` UI + vertical-slice integration + eval baseline | D-G1 | Parallel (4 tasks) |
| D-G3 | D | Wrap-up | D-G2 | Sequential (1 task) |
| H-G0 | parallel | Handler infra shared task | A-G2 (IToolHandler rename) | Sequential (gates Wave 1 + 2) |
| H-G1 | parallel | Handler Wave 1 deterministic (4 tasks) | H-G0 | Parallel (4 tasks; max 4 per wave per project-pipeline 6-agent cap) |
| H-G2 | parallel | Handler Wave 2 LLM-assisted (4 tasks) | H-G1 | Parallel (4 tasks) |
| H-G3 | parallel | Handler dispatch tests (playbook + chat contexts) | H-G2 | Parallel (2 tasks) |

**Max concurrency cap**: 6 agents per wave per project-pipeline skill body. Groups H-G1 + B-G2 + D-G1 + D-G2 use 4 parallel each (within cap).

---

## 10. Next Steps

1. **Review this plan.md** + [CLAUDE.md](CLAUDE.md) + [README.md](README.md)
2. **Run task decomposition**: Execute `task-create` Step 3 of `/project-pipeline` to generate ~80 POML task files + TASK-INDEX.md
3. **Step 3.5 artifacts review checkpoint**: Per execution plan, pause for user approval before commit
4. **Step 4 commit**: Push artifacts to `work/spaarke-ai-platform-unification-r6`
5. **Step 5 task execution**: Parallel-agent waves with build verification between waves; confirmation triggers per [CLAUDE.md](CLAUDE.md) §Confirmation Triggers

---

**Status**: Ready for task decomposition (Step 3 of project-pipeline)
**Next Action**: Generate task files via task-create skill

---

*For Claude Code: Load this plan when executing tasks. Decisions in §2 are binding for R6.*
