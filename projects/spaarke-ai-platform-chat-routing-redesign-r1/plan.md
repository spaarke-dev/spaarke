# Project Plan: Spaarke AI Platform — Chat Routing Redesign (R1)

> **Last Updated**: 2026-06-21
> **Status**: Draft — Ready for Tasks
> **Spec**: [spec.md](spec.md) (45 FRs, 19 NFRs, reconciled with architecture v3.3)
> **Architecture (binding for WP5)**: [architecture/stateful-chat-architecture.md](architecture/stateful-chat-architecture.md)

---

## 1. Executive Summary

**Purpose**: Collapse two parallel chat-routing mechanisms onto one matcher; reform playbook identification via stable codes; make destination metadata data-driven; build the 6-tier stateful chat memory subsystem (mostly wire-and-refactor of R6 Pillar 7); author specialized playbooks; retire `CapabilityRouter`.

**Scope** (6 work packages + closeout):
- §1.7 Stable playbook codes — 9 consumer migration (Pattern C cleanup → Pattern A typed-options → Pattern B name→code)
- WP1.5 Index Governance — Dataverse schema additions + Power Apps Send-to-Index UX + drift detection
- WP3 Destination metadata wiring (FR-14a–FR-14f) — `Both` enum, DispatchResult, handler cases
- WP5 6-tier memory — bound by architecture doc §3–§4–§8; 8 new tool handlers; layered context cards; upload enrichment; promotion workflow; Q8 conflict check
- WP2 File-aware classification (Hybrid C primary) — depends on WP5 upload manifest
- WP6 Specialized playbooks + Path 3 JPS `$ref` extension (additive only)
- WP4 CapabilityRouter retirement — single-phase cutover; no backward compat

**Timeline**: Per design §4, rough estimate **6–10 weeks active + 2–3 weeks stabilization**. Highly parallelizable inside phases; critical path through WP3 → WP5 → WP2 → WP4 retirement.

**Estimated Effort**: **120 task files** across 8 phases (0 through 7); mix of FULL / STANDARD / MINIMAL rigor. See [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for the audited task graph.

---

## 2. Architecture Context

### Design Constraints (From ADRs — binding)

- **ADR-001**: Minimal API pattern for new endpoints (`/by-code/{code}`, `/api/memory/promotions/{id}/{approve|reject}`)
- **ADR-008**: Endpoint filters for tenant scoping on new endpoints
- **ADR-010**: DI minimalism — no `IServiceProvider.GetService<T>()` in endpoint signatures; concrete-class Null-Object pattern
- **ADR-013**: AI Public Contracts facade — chat-memory tools cross facade boundary cleanly; NO direct `IOpenAiClient` / `IPlaybookService` injection into CRUD code; in-process coupling justifies BFF placement
- **ADR-014**: AI caching — `/by-code/` 5-min TTL per-tenant; new memory tool reads use Redis hot-tier patterns
- **ADR-015**: AI Data Governance — Tier-1 logging only carries deterministic IDs + sizes + timings; NEVER user message content, file content, recall results, memory facts. Memory event payloads tier-1 safe (80-char `factSummary` preview cap)
- **ADR-018**: Feature flags / Typed options — fix `Workspace:SummarizePlaybookId` raw `IConfiguration[...]` indexer; promote to typed `WorkspaceOptions.SummarizePlaybookCode`
- **ADR-019**: ProblemDetails error model on new endpoints (404 shape for `/by-code/` not found)
- **ADR-029**: BFF Publish Hygiene — measure publish size per task; ≤60 MB ceiling; expect net reduction from WP4 deletion; `<PublishTrimmed>`/`<PublishAot>` prohibited (reflection-hostile stack)
- **ADR-030 v2** (2026-06-21 amendment): PaneEventBus 5-channel union with new `memory` channel; `MemoryPaneEvent` initial 5 discriminants (`promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`)
- **ADR-032**: BFF Null-Object Kill-Switch — any feature-gated memory tool registration uses P1/P2/P3 pattern; asymmetric registration anti-pattern banned
- **ADR-033**: Streaming Chat-Tool Side Channel — Path 3 streaming preservation via context-injected `DocumentStreamWriter`; new memory tools return single `ToolResult` (no streaming)

### Architecture Constraints (From spec & architecture doc — binding)

- **NFR-A1–A7** (architecture §2 P1–P7): six-tier separation with explicit promotion; JIT retrieval over stuffing; citation-bearing trust model; layered context cards (~150–250 tok); wire-not-build; privacy by default; ADR-015 audit hygiene
- **Insights reuse = PATTERN-LEVEL ONLY** (architecture §5): MUST NOT reuse `spaarke-insights-index`, `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `sprk_matter.sprk_performancesummary` for chat memory
- **NFR-07 pre-fill flow** preserved: signatures, 45s timeout, `useAiPrefill`, `$choices`-constrained output untouched throughout migration
- **NFR-08** 11 production node executors preserved
- **6 production-bound playbooks** (spec §1.5) migrated in place via stable code only — no delete, no rename, no output-schema change
- **Stable `text-embedding-3-large`** — no new external API surface (no `voyage-law-2`, no Document Intelligence classifier)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Hybrid (C) for WP2 multi-file routing | Lowest routing latency; manifest precomputed at upload | WP2 (Phase 5) depends on WP5 (Phase 4) upload manifest shipping first |
| Reuse `sprk_actioncode` (no new field) | Existing `NVARCHAR(64)` per Dataverse describe; FR-06 confirmed | No schema change for actions; backward-compat layer for `@v1` suffix until cutover |
| `sprk_aichatmessage` retire to write-only | Cosmos `audit` is canonical reader; placeholder methods removed | FR-25; interface rename `IChatDataverseRepository` → `IChatAuditRepository` |
| Chat-summarize migrates first | Lowest blast radius; no NFR-07 risk | FR-05 sequencing; Phase 1 task ordering |
| ADR-030 v2 `memory` channel | Memory events get dedicated semantic channel | Already amended; ContextPane wires via `usePaneEvent('memory', ...)` |
| Single-phase WP4 cutover (no parallel run) | Q8 — `CapabilityRouter` is duplicate machinery | Phase 7 cutover atomic; coordinate with R6 PR #401 merge |
| BFF monolith (no microservice extraction) | ADR-013 in-process coupling justified for chat memory | Wire-not-build (P5) mitigates publish-size pressure |
| Insights reuse = PATTERN-LEVEL only | Architecture §5 critical scrutiny | New chat-memory components do not depend on Insights code paths |

### Discovered Resources

**Applicable Skills** (auto-discovered via `.claude/skills/INDEX.md`):
- `.claude/skills/task-execute/` — every task uses this
- `.claude/skills/adr-check/`, `.claude/skills/code-review/` — quality gates at Step 9.5 of FULL-rigor tasks
- `.claude/skills/dataverse-create-schema/` — `sprk_analysisplaybook` 5 field additions (Phase 2)
- `.claude/skills/dataverse-deploy/` — playbook/skill/knowledge JSON updates (Phase 6)
- `.claude/skills/dataverse-mcp-usage/` — schema validation reads at planning time
- `.claude/skills/bff-deploy/` — BFF redeploy at phase exits
- `.claude/skills/code-page-deploy/` — SpaarkeAi frontend (ADR-030 v2 PaneEventBus + new memory subscribers)
- `.claude/skills/merge-to-master/` — after project complete
- `.claude/skills/context-handoff/` — every 3 steps during execution
- `.claude/skills/script-aware/` — find existing PowerShell scripts
- `.claude/skills/ui-test/` — promotion approval UI + memory channel subscriber tests

**Knowledge / pattern files**:
- `.claude/patterns/api/` — endpoint patterns
- `.claude/patterns/auth/` — Spaarke Auth v2 tenant scoping
- `.claude/constraints/bff-extensions.md` — binding placement criteria EVERY BFF task
- `.claude/constraints/azure-deployment.md` — publish-size verification rule (NFR-01)
- `docs/standards/CODING-STANDARDS.md`
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — Xrm.WebApi vs BFF
- `docs/architecture/AI-ARCHITECTURE.md`
- `docs/architecture/auth-azure-resources.md`
- `docs/procedures/testing-and-code-quality.md` — test update obligation
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` — binding for WP5

**Existing components leveraged** (architecture §11.1; wire-not-build per P5):

| Component | Purpose | File |
|---|---|---|
| `PlaybookLookupService` (alternate-key `by-code` lookup) | Already supports §1.7 resolution | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs:70-115` |
| `MatterMemoryService` + `IMatterMemoryService` | T3 substrate; FR-45 wired | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` + `PlaybookChatContextProvider.cs:627` |
| `MemoryCompositionService` | T1 4-layer composition; FR-42 pinned-never-drops | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs` |
| `PinnedContextRepository` + `PinnedContextRecallService` | T4 pins; embedding similarity ranker | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/Pinned*.cs` |
| `SummarizationCompressionService`, `PromptBudgetTracker` | T2 history compression; 8K + 5K budget | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/` |
| `SessionPersistenceService` `SaveTabsAsync` pattern | T2 Cosmos write-through extension precedent | `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs` |
| `AuditLogService` | T6 write target | `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditLogService.cs` |
| `RagService` | T5 wrapper for `spaarke-session-files` | `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` |
| `WorkspaceStateService` (R6 task 051) | T2 workspace tabs hybrid persistence | `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs` |
| `JpsRefResolver` (canonical at Path 1) | Extend into Path 3 per FR-38 | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:442-499` |
| `NodeRoutingConfig` (correct schema; needs `Both` enum) | WP3 destination metadata | `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs:30-274` |
| `PaneEventBus.ts` (channel-string-agnostic Set-keyed registry) | Extend with `memory` channel | `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` |
| `Pillar 6b workspace handlers` | Already registered `sprk_analysistool` rows | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/*WorkspaceTab*.cs` |
| `sseToPaneEventBridge.ts:174-256` | Workspace destination coordination point | `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` |

**Existing scripts**:
- `scripts/dataverse/Deploy-Playbook.ps1` — FR-14e JSON-schema validation gate
- `scripts/dataverse/Update-PlaybookEmbeddings.ps1` — WP1.5 send-to-index
- `scripts/Seed-TypedHandlers.ps1` — new memory tools registration
- `scripts/api/Test-SdapBffApi.ps1` — integration tests

**Dataverse schema validation (MCP, 2026-06-21)**:
- `sprk_analysisplaybook` confirmed: `sprk_playbookcode NVARCHAR(10)`, `sprk_playbookid NVARCHAR(100)` (Text on this table; the LOOKUP of same name lives on related entities), `sprk_analysisplaybookid GUID PK`, `sprk_configjson MULTILINE TEXT`. **5 fields to add per FR-08**: `sprk_lastindexedat`, `sprk_indexstatus`, `sprk_lastindexerror`, `sprk_indexhash`, `sprk_jpsmatchingmetadata`.
- `sprk_analysisaction` confirmed: `sprk_actioncode NVARCHAR(64)` ✅ FR-06 reuse decision validated; `sprk_systemprompt MULTILINE TEXT` (Path 3 JPS `$ref` target).
- `sprk_aichatmessage` confirmed broken-stub shape (FR-25 retire-to-write-only viable).
- `sprk_userpreferences` confirmed — `sprk_preferencetype` CHOICE does NOT include writingStyle/summaryLength/citationStyle; Q13 defer correct.

---

## 3. Implementation Approach

### Phase Structure (post-audit numbering — 120 tasks total)

```
Phase 0 (Tasks 000–004)  — R6 readiness check + §1.7 Pattern C cleanup
                          └─ 000 (NEW) verifies R6 closeout complete before any work begins

Phase 1 (Tasks 010–027)  — §1.7 Stable codes migration (Patterns A then B)
                          └─ Chat-summarize first per FR-05.
                          └─ NEW task 013 extends WorkspaceOptions.cs with 4 typed code options FIRST,
                             so Pattern A migrations (016–019) are truly file-isolated and parallel-safe.

Phase 2 (Tasks 030–040)  — WP1.5 Index Governance
                          └─ Dataverse schema (5 fields) + Power Apps Send-to-Index UX + nightly drift job

Phase 3 (Tasks 045–055)  — WP3 Destination metadata wiring (FR-14a–FR-14f)
                          └─ Additive; backward-compat preserved via NodeRoutingConfig.Parse(null) default.
                          └─ Can begin during Phase 2 (wave 3-A prereq is 1-J, not 2-E).

Phase 4 (Tasks 060–105)  — WP5 6-tier memory (LARGEST PHASE — 42 tasks across 21 waves)
                          └─ NEW task 070: DI registration for upload pipeline services.
                          └─ NEW task 091: DI registration for 8 tool handlers.
                          └─ Wave 4-A split into 4-A1 (060 sequential) + 4-A2 (061–064 parallel).
                          └─ Cross-wave dependency: task 098 (ContextPane subscriber) needs 060.
                          └─ Bound by architecture/stateful-chat-architecture.md §3–§11.

Phase 5 (Tasks 110–119)  — WP2 File-aware classification (Hybrid C primary)
                          └─ DEPENDS ON Phase 4 upload manifest (FR-17).

Phase 6 (Tasks 120–132)  — WP6 Specialized playbooks + Path 3 JPS $ref extension
                          └─ PB-009/PB-012/PB-015/PB-017 Dataverse audit FIRST; then author NDA/Patent/Invoice.

Phase 7 (Tasks 140–148, 150)  — WP4 CapabilityRouter retirement + project closeout
                          └─ Task 141 expanded to cover FR-23 tool filtering replacement (single atomic commit).
                          └─ Quality gates REORDERED: 147 (code-review) + 148 (adr-check) run BEFORE 146 (UAT).
                          └─ Hard prerequisite: R6 PR #401 merged to master before wave 7-B.
```

### Critical Path

**Blocking dependencies**:
- Phase 0 cleanup BLOCKS Phase 1 (avoid migrating dead code consumers)
- Phase 1 Pattern A typed-options BLOCKS Pattern B (resolver infrastructure proven first)
- Phase 2 Dataverse schema BLOCKS WP1.5 indexing tasks within Phase 2 (need fields to populate)
- Phase 3 destination wiring BLOCKS Phase 4 workspace-write tools (Workspace case must exist in PlaybookOutputHandler)
- Phase 4 upload-time enrichment (SessionFileEnrichmentService) BLOCKS Phase 5 Hybrid C primary path
- Phase 5 file-aware classification BLOCKS Phase 6 specialized playbook routing validation
- Phase 4 ADR-030 v2 PaneEventBus implementation BLOCKS Phase 4 promotion workflow consumer
- Phase 7 R6 PR #401 merge to master BLOCKS WP4 CapabilityRouter retirement (need clean diff)

**Critical path (longest chain)**:
```
Phase 0 cleanup → Phase 1 chat-summarize migration → Phase 3 destination wiring →
Phase 4 SessionFileEnrichmentService → Phase 5 Hybrid C routing → Phase 6 specialized playbook
authoring → Phase 7 WP4 retirement → Phase 7 wrap-up
```

**High-risk items**:
- Production-bound playbook accidental break during Pattern B name→code migration
  - Mitigation: integration test per consumer surface BEFORE and AFTER migration
- BFF publish size pushes past 60 MB during Phase 4 (8 new tool handlers + LayeredContextCardBuilder + SessionFileEnrichmentService)
  - Mitigation: NFR-01 per-task `dotnet publish` measurement; expect WP4 deletion offset in Phase 7
- Path 3 JPS `$ref` extension regresses streaming UX
  - Mitigation: FR-38 acceptance criterion — streaming integration test continues emitting `FieldDelta` per top-level field

---

## 4. Phase Breakdown

### Phase 0 — R6 Readiness Check + §1.7 Pattern C Cleanup (Tasks 000–004; 5 tasks)

**Objectives**:
1. Delete LegalWorkspace dead code (OC-R4-05 retired components)
2. Migrate or delete PCF `UniversalQuickCreate/useAiSummary.ts` duplicate
3. Fix stale GUID comments in `WorkspaceOptions.cs:35` + `ProjectPreFillService.cs:40`

**Deliverables**:
- [ ] `src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` (+ Project + WorkAssignment siblings) deleted
- [ ] PCF duplicate `useAiSummary.ts` migrated or deleted
- [ ] Stale `3f21cec1-...` GUID comments scrubbed

**Inputs**: spec.md §1.7.3 Pattern C; design WP4 inventory
**Outputs**: Clean baseline blast radius for Patterns A + B
**Dependencies**: None

### Phase 1 — §1.7 Stable Codes Migration (Tasks 010–027; 18 tasks)

**Objectives**:
1. Stand up `/api/ai/playbooks/by-code/{code}` endpoint (5-min ADR-014 cache; tenant-scoped; ADR-019 404 shape)
2. Backfill `sprk_playbookcode` on 6 production-bound playbooks (codes per spec §1.7.3)
3. Pattern A migration (chat-summarize → pre-fill → workspace AI)
4. Pattern B migration (4 name-resolve consumers)
5. Action codes — REUSE existing `sprk_actioncode` field; drop `@v1` suffix on new entries
6. Frontend `SoftSlashRouter` wire-format rename per Q5

**Deliverables**:
- [ ] `GET /api/ai/playbooks/by-code/{code}` endpoint live + integration tested
- [ ] `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` hardcoded GUID removed (FR-05 first)
- [ ] `MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService`, `WorkspaceFileEndpoints` migrated to `*PlaybookCode` typed options
- [ ] `WorkspaceOptions.SummarizePlaybookCode` added; ADR-018 violation at `WorkspaceFileEndpoints.cs:30,254` resolved
- [ ] `AppOnlyAnalysisService:46,1068` + `useAiSummary.ts:285` + `DocumentEmailWizard.tsx:628` + `ChatContextMappingService` migrated to code lookup
- [ ] `/by-name/` endpoint emits deprecation warning per call
- [ ] `commandIntent` wire-format renamed (FE + BE coordinated)

**Inputs**: Phase 0 clean baseline; `PlaybookLookupService.cs:70-115` (already supports lookup)
**Outputs**: 9 consumers resolve playbooks by stable code; zero hardcoded playbook GUIDs in `Services/Ai/`
**Dependencies**: Phase 0 complete

### Phase 2 — WP1.5 Index Governance (Tasks 030–040; 11 tasks)

**Objectives**:
1. Add 5 tracking fields to `sprk_analysisplaybook` (Dataverse schema)
2. Add `sprk_jpsmatchingmetadata` JSON field with schema
3. Extend `PlaybookEmbeddingService` embed-input composition
4. Power Apps "Send to Index" UX (button + status + admin view)
5. Nightly drift-detection job
6. Validation gate (description / documentTypes / destinationHint required)

**Deliverables**:
- [ ] `sprk_lastindexedat`, `sprk_indexstatus`, `sprk_lastindexerror`, `sprk_indexhash`, `sprk_jpsmatchingmetadata` added via `dataverse-create-schema`
- [ ] JSON schema for `sprk_jpsmatchingmetadata` documented (documentTypes, intents, triggerPhrases, preferredOver, outputDestination, scopeHints, exclusionHints)
- [ ] `PlaybookEmbeddingService.cs:28-30` extended to embed `documentTypes + intents + triggerPhrases`
- [ ] Power Apps form button "Send to Index" wired
- [ ] Nightly drift-detection job deployed
- [ ] Admin view "Playbook Index Drift" published
- [ ] Vector match for "summarize this NDA" returns Summarize-NDA top-1 on 100-doc Spaarke corpus benchmark

**Inputs**: Phase 1 stable codes (resolver infrastructure stable)
**Outputs**: Index drift detectable; routing precision improved via structured signal
**Dependencies**: Phase 1 chat-summarize migrated (for benchmark integrity)

### Phase 3 — WP3 Destination Metadata Wiring (Tasks 045–055; 11 tasks)

**Objectives**:
1. Add `NodeDestination.Both` enum + JSON converter
2. Extend `DispatchResult` with `NodeDestination` + `WidgetType` properties
3. `PlaybookDispatcher` populates new DispatchResult properties
4. `PlaybookOutputHandler` adds Workspace / Both / FormPrefill / SideEffect cases
5. `Deploy-Playbook.ps1` JSON-schema validation gate
6. Preserve backward compat via `NodeRoutingConfig.Parse(null)` default

**Deliverables** (FR-14a–FR-14f):
- [ ] `NodeRoutingConfig.cs:31-64,247-272` extended; roundtrip preserves `Both`
- [ ] `DispatchResult.cs:37-46` extended; `dotnet build` passes without modifying any caller
- [ ] `PlaybookDispatcher` integration test returns `DispatchResult { NodeDestination: Workspace, WidgetType: "structured-output-stream" }` for workspace playbook
- [ ] `PlaybookOutputHandler` end-to-end test opens Workspace tab via handler path (NOT implicit streaming)
- [ ] Deploy script catches malformed configJson before publish
- [ ] Empty configJson playbook still produces `Chat` destination (regression preserved)

**Inputs**: Phase 1 (stable codes available for new test fixtures)
**Outputs**: Workspace destination is data-driven; implicit-streaming behavior at `sseToPaneEventBridge.ts:174-256` is replaced
**Dependencies**: Phase 1 complete

### Phase 4 — WP5 6-Tier Memory Subsystem (Tasks 060–105; 42 tasks; 21 waves)

**LARGEST PHASE — 42 tasks across 6 sub-WPs. Architecture doc is binding.**

**Sub-WP organization**:

**4a — ADR-030 v2 PaneEventBus extension** (060–065):
- [ ] `PaneEventTypes.ts` — add `MemoryPaneEvent` interface; extend `PaneChannel` union + `PaneChannelEventMap`
- [ ] `PaneEventBus.ts` — verify channel-string switches accept `memory` (likely no code change; Set-keyed registry is channel-string-agnostic)
- [ ] Frontend type-check + lint pass
- [ ] Verification grep commands per ADR-030 v2 amendment

**4b — Upload-time enrichment pipeline** (066–075):
- [ ] `SessionFileEnrichmentService` (NEW) orchestrating classify + summarize + manifest
- [ ] `FileClassificationService` (NEW) — gpt-4o-mini documentType + confidence
- [ ] `FileSummarizationService` (NEW) — gpt-4o-mini 1-paragraph precomputed summary (marked NOT authoritative)
- [ ] `FileManifestExtractor` (NEW) — sections, tables, pageCount, language
- [ ] `ChatSession.UploadedFiles` shape extension via `SaveTabsAsync` pattern
- [ ] `SessionPersistenceService.UpdateUploadedFilesAsync` (NEW method)
- [ ] Integration test: 50-page PDF upload completes in ≤4s total; enriched fields persist Redis hot + Cosmos warm

**4c — Per-turn prompt assembly refactor** (076–082):
- [ ] `LayeredContextCardBuilder` (NEW) — structured per-file card (~150–250 tok)
- [ ] `TrustFrameInstructionInjector` (NEW) — adds canonical persona text per architecture §8.3
- [ ] Unify `MemoryCompositionService` with `PlaybookChatContextProvider` (no parallel pipelines)
- [ ] Static prefix ~6K cacheable + dynamic suffix ~5K (per WP5.4)
- [ ] FR-45 regression test asserts `PlaybookChatContextProvider.cs:627` invocation continues to fire

**4d — 8 new tool handlers** (083–090):
- [ ] `ListSessionFilesHandler` (T2)
- [ ] `GetFileManifestHandler` (T2)
- [ ] `RecallSessionFileHandler` (T2 + T5) — 6 purpose enum values, 5 scope enum values, requireCitations default true
- [ ] `WriteSessionMemoryHandler` (T2)
- [ ] `RetrieveMatterMemoryHandler` (T3)
- [ ] `PromoteToMatterMemoryHandler` (T2 → T3 pending-approval)
- [ ] `GetUserPreferencesHandler` (T4 read-only)
- [ ] `GetOrgTemplatesHandler` (T4 read-only)
- [ ] Seed via `Seed-TypedHandlers.ps1`; integration test invokes each successfully

**4e — Promotion workflow + Q8 conflict check** (091–096):
- [ ] `MatterMemoryPromotionService` (NEW) — pending-approval Cosmos doc-type
- [ ] `POST /api/memory/promotions/{id}/approve` + `POST /api/memory/promotions/{id}/reject` endpoints
- [ ] `memory.promotion_pending` + `memory.promotion_resolved` + `memory.fact_promoted` dispatch sites (via new channel)
- [ ] ContextPane subscriber + approval UI (Accept/Reject buttons)
- [ ] FR-35 Q8 workspace tab conflict check — refuse agent write when `lastUserEditAt` > agent's last read

**4f — Tier 5 retrieval + audit** (097–099):
- [ ] T5 wrapper handlers over `spaarke-session-files` + `spaarke-files-index` + `spaarke-rag-references` ONLY (NOT `spaarke-insights-index`)
- [ ] `ChatDataverseRepository` → `IChatAuditRepository` rename + retire placeholder methods (FR-25)
- [ ] `sprk_aichatmessage` confirmed write-only via grep
- [ ] T6 audit-write integration tests

**Deliverables**: All 25+ FRs in WP5 (FR-26 through FR-37) satisfied; architecture §8.1 tool surface complete; layered context cards render per architecture §6.2.

**Inputs**: Phase 3 destination metadata (Workspace handler case exists)
**Outputs**: Stateful chat memory works end-to-end; promotion workflow complete; FR-45 invariant guarded
**Dependencies**: Phase 3 complete

### Phase 5 — WP2 File-Aware Classification (Tasks 110–119; 10 tasks)

**Hybrid (C) primary** per owner clarification.

**Objectives**:
1. `PlaybookDispatcher.DispatchAsync` accepts `attachments` parameter
2. Phase A per-file fingerprint (filename tokens + content type + textLength + textPrefix 2000 chars + sha256) <50ms
3. Phase B per-file vector match using manifest `documentType` as structured pre-filter against `sprk_jpsmatchingmetadata.documentTypes`
4. Fallback to parallel per-file vector match when manifest absent
5. Phase C reconciliation; LLM decider only on disagreement; gpt-4o-mini structured output
6. `commandIntent` integrated as vector-query bias (NOT separate routing layer)

**Deliverables**:
- [ ] Existing tests pass without modification (backward-compat `null`/empty attachments)
- [ ] Telemetry logs `decidersInvoked` count; multi-file all-agree case shows 0 decider calls
- [ ] Load test: p95 ≤1.5s for 1–3 file scenarios; ≤2s worst case
- [ ] `SoftSlashIntentToCapabilityName` dict removed (slash + NL flows produce identical routing)
- [ ] Acceptance test: "summarize this NDA" + NDA upload routes to Summarize-NDA top-1

**Inputs**: Phase 4 upload manifest available; Phase 2 `sprk_jpsmatchingmetadata` filter ready
**Outputs**: File-aware routing live; specialization can be properly matched
**Dependencies**: Phase 2 + Phase 4 complete

### Phase 6 — WP6 Specialized Playbooks + Path 3 Extension (Tasks 120–132; 13 tasks)

**Additive only per audit correction.**

**Objectives**:
1. Dataverse audit (NOT just repo grep) for PB-009 / PB-012 / PB-015 / PB-017
2. Extend `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (Path 3) to invoke `JpsRefResolver` BEFORE LLM call
3. Author `summarize-nda` playbook + action (uses SKL-003 + KNW-006 + KNW-005 via `$ref`)
4. Author `summarize-patent` (NEW SKL Patent Review + NEW KNW Patent Standards needed)
5. Author `extract-invoice` (uses SKL-002 Invoice Processing)
6. PB-009 "Summarize NDA" inspect + rewrite description + extend via JPS `$ref` + populate `sprk_jpsmatchingmetadata` + verify `sprk_playbookcode`
7. Path 3 Persona binding (R6 Pillar 1) + evidence-sufficiency precheck

**Deliverables**:
- [ ] Dataverse audit document for each candidate playbook
- [ ] Streaming integration test confirms `FieldDelta` SSE events per top-level field unchanged after JPS `$ref` extension
- [ ] 3 new playbooks indexed; vector match returns each as top-1 for representative queries
- [ ] PB-009 inspection report; post-rewrite vector match for "summarize this NDA" returns PB-009 as top-1

**Inputs**: Phase 5 file-aware routing live; Phase 2 index governance ready
**Outputs**: Specialized routing works end-to-end
**Dependencies**: Phase 5 complete

### Phase 7 — WP4 Retirement + Project Closeout (Tasks 140–148, 150; 10 tasks)

**Single-phase cutover per Q8. Quality gates REORDERED per audit CRIT-7: code-review (147) + adr-check (148) run BEFORE UAT regression (146).**

**Objectives**:
1. Coordinate with R6 PR #401 merge to master (prerequisite)
2. Delete `CapabilityRouter.cs` + 10 supporting files atomically
3. Tool filtering migrated to per-playbook scopes + always-on conversational tools
4. R6 FR-30 CapabilityRouter dedup semantics preserved through new dispatcher path (binding test per Q20)
5. Frontend `SoftSlashRouter.SOFT_SLASH_TO_INTENT` removed
6. Project wrap-up (task 150)

**Deliverables**:
- [ ] `grep CapabilityRouter src/` returns zero hits
- [ ] CI green
- [ ] Dedup test suite remains green
- [ ] Net BFF publish-size reduction reported
- [ ] All 20 success criteria from spec verified
- [ ] `code-review` + `adr-check` clean
- [ ] `repo-cleanup` clean (no orphan files)
- [ ] `README.md` status flipped to Complete; `lessons-learned.md` authored
- [ ] R7 backlog seeded (auto-deploy gap, Pillar 9 closed-union, tool-name normalization)

**Inputs**: All prior phases complete; R6 PR #401 merged
**Outputs**: Clean R6 successor; project closure
**Dependencies**: All prior phases + R6 PR #401 merged

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure OpenAI `text-embedding-3-large` + `gpt-4o-mini` | GA | Low | Existing; no new external API surface |
| Azure AI Search `playbook-embeddings`, `spaarke-session-files`, `spaarke-files-index`, `spaarke-rag-references`, etc. | GA | Low | Existing |
| Cosmos containers `sessions`, `memory`, `audit`, `prompts`, `feedback` | Provisioned | Low | Already in Bicep |
| Dataverse production access for 5 field additions | Available | Med | Schema-add request gated by owner approval per FR-08 |
| Power Apps admin role for "Send to Index" UX | Available | Low | Admin role exists |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R6 closeout tasks 089 + 090 | `projects/spaarke-ai-platform-unification-r6/` | Pending |
| R6 UAT hotfix series PR #401 | `work/spaarke-ai-platform-unification-r6` branch | Open (blocks Phase 7) |
| R6 Pillar 7 memory services | `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/` | Shipped |
| FR-45 wiring at `PlaybookChatContextProvider.cs:627` | Same | Verified — do not regress |
| Pillar 6b workspace handlers + tool registry | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/` | Shipped |
| ADR-030 v2 `memory` channel | `.claude/adr/ADR-030-pane-event-bus.md` + `docs/adr/ADR-030-pane-event-bus.md` | Amended 2026-06-21 |
| 5 active worktrees (potential coordination): #406 SmartTodo, #402 CI, #401 R6 hotfix, #399 nightly-health, #398 SmartTodo header | various | Informational; Phase 7 awaits #401 merge |

---

## 6. Testing Strategy

**Unit Tests** (target ≥80% coverage on new code):
- New tool handlers — happy path + failure mode per architecture §9.1
- `MatterMemoryPromotionService` workflow states
- `SessionFileEnrichmentService` orchestration
- `LayeredContextCardBuilder` output shape
- `MemoryPaneEvent` payload typing

**Integration Tests** (BFF + Dataverse + Cosmos):
- `/by-code/{code}` resolution per consumer (9 consumers × pre/post)
- Promotion workflow end-to-end: pending → approval → durable T3 write
- Q8 conflict check: user-edit-during-agent-call refused
- File upload enrichment: 50-page PDF in ≤4s
- Path 3 streaming: `FieldDelta` events preserved after `$ref` extension
- Workspace tab open via `PlaybookOutputHandler.HandleOutputAsync` Workspace case (NOT implicit streaming)

**E2E / UAT Scenarios**:
- T-001: `/summarize` slash + NL "summarize" produce same destination
- T-002: "Do you have the document?" → agent answers from session memory, no re-upload prompt
- T-003: NDA upload + "summarize this NDA" → Summarize-NDA returned; Workspace tab + 7-section summary
- T-004: "What was the term?" → agent calls `recall_session_file({purpose: 'answer_question', requireCitations: true})` with cited answer
- T-005: "Remember that user prefers concise summaries" → write_session_memory + promote prompt
- T-006: Multi-file (NDA + Invoice) upload → Phase C reconciliation; user disambiguation prompt OR fan-out
- T-007: 6 production-bound playbooks invoked from their consumer surfaces — all functional
- T-008: NFR-07 pre-fill flow — Matter / Project / WorkAssignment / SummarizeFiles wizards work end-to-end with 45s timeout intact
- T-009: ADR-030 v2 `memory` channel — ContextPane subscriber receives `memory.promotion_pending` and renders approval UI

**Insights regression suite** (NFR-06):
- Existing Insights playbooks execute unchanged (matter-health-single, etc.)
- `sprk_matter.sprk_performancesummary` field reads/writes unchanged
- `MultiIndexComposer.Merge` behavior unchanged
- `spaarke-insights-index` query results unchanged

---

## 7. Acceptance Criteria

See [README.md](README.md) Graduation Criteria — 16 items.

### Per-phase technical acceptance

**Phase 0**: Dead code deleted; PCF duplicate resolved; stale comments scrubbed; build green.
**Phase 1**: All 9 consumers resolve by stable code; zero hardcoded GUIDs; `/by-name/` deprecation warnings emitting.
**Phase 2**: 5 schema fields live; vector match returns Summarize-NDA top-1 for representative query; admin view discoverable.
**Phase 3**: `DispatchResult.NodeDestination = Workspace` test passes; Workspace tab opens via handler path (NOT implicit streaming).
**Phase 4**: All 25+ WP5 FRs satisfied; 8 new tool handlers registered; promotion workflow end-to-end; FR-45 regression-tested.
**Phase 5**: p95 ≤1.5s for 1–3 file scenarios; "summarize this NDA" + NDA upload routes to Summarize-NDA.
**Phase 6**: 3 new playbooks indexed + matchable; Path 3 streaming preserved; PB-009/PB-012/PB-015/PB-017 Dataverse audit document delivered.
**Phase 7**: `CapabilityRouter` deleted; CI green; Insights regression suite passes; project artifacts archived.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Production-bound playbook accidentally broken during Pattern B name→code migration | Med | High | Integration test per consumer surface before + after migration (FR-05 sequencing chat-summarize first) |
| R2 | BFF publish size pushes past 60 MB during Phase 4 (8 new tool handlers + enrichment services) | Med | High | NFR-01 per-task `dotnet publish` measurement; WP4 deletion in Phase 7 expected to offset |
| R3 | Path 3 JPS `$ref` extension breaks streaming UX | Low | High | FR-38 acceptance criterion — streaming integration test continues to emit `FieldDelta`; resolve `$ref` BEFORE LLM call |
| R4 | Phase 7 WP4 cutover lands before R6 PR #401 merged → conflict diff | Med | Med | Block Phase 7 task 140 on PR #401 merge check; coordinate timing |
| R5 | Architecture §5 "no Insights reuse" misinterpreted as "no coordination" | Low | Med | NFR-06 explicitly framed binding-NEGATIVE; MUST NOT rules cite specific component names; Insights regression suite required |
| R6 | `voyage-law-2` 6–10% precision gap surfaces as UAT routing accuracy issue | Low | Med | Compensate via WP1.5 structured `sprk_jpsmatchingmetadata` filter + Phase 5 file-aware classification; monitor telemetry; argue ADR amendment if UAT fails |
| R7 | Dataverse schema confirmation delays Phase 2 | Med | Med | Submit schema-add request early (Phase 0 / Phase 1); Phase 2 work parallelizable into UX + drift job pieces while schema waits |
| R8 | Promotion approval UI never wireframed → backend stub merges without consumer | Med | Med | FR-32 acceptance criterion forces end-to-end test through ContextPane subscriber |
| R9 | `spaarke-insights-index` accidentally consumed by chat memory tool | Low | High | Code review must grep handler implementations; architecture §5.2.1 binding violation |
| R10 | Path 3 chat-Summarize call site fails to bind Persona/Knowledge after `$ref` extension | Low | Med | FR-44 + FR-45 acceptance criteria; trace logs verify scope resolution |

---

## 9. Next Steps

1. **Review this plan.md** (and README, CLAUDE.md, TASK-INDEX once generated) before executing any task.
2. **Run** `/task-create projects/spaarke-ai-platform-chat-routing-redesign-r1` to decompose this plan into ~80–110 POML task files + `TASK-INDEX.md`. (Project-pipeline does this in Step 3.)
3. **Coordinate** R6 PR #401 merge timeline before Phase 7 begins.
4. **Schedule** Dataverse schema confirmation for the 5 additive fields (FR-08).
5. **Begin** Phase 0 cleanup in a clean session via `/task-execute 001`.

---

**Status**: Draft — Ready for Tasks
**Next Action**: Run `/task-create` (or continue project-pipeline) to generate task files.

---

*For Claude Code: This plan provides implementation context. Load architecture/stateful-chat-architecture.md alongside any Phase 4 task. Load spec.md as the canonical FR/NFR source for every task. Load `.claude/constraints/bff-extensions.md` + `.claude/constraints/azure-deployment.md` for every BFF-touching task.*
