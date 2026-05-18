# TASK-INDEX - Spaarke AI Platform Unification R2

> **Total Tasks**: 86
> **Status**: Ready for Execution
> **Last Updated**: 2026-05-17

## Status Legend
- :black_square_button: Not Started
- :arrows_counterclockwise: In Progress
- :white_check_mark: Complete
- :no_entry: Blocked

## Phase 1: Foundation & Infrastructure (8 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 001 | Amend ADR-015 governed data stores | :black_square_button: | No (.claude/) | none |
| 002 | Cosmos DB infrastructure (Bicep) | :black_square_button: | Yes | none |
| 003 | Azure AI Content Safety resource | :black_square_button: | Yes | none |
| 004 | GPT-4o-mini deployment | :black_square_button: | Yes | none |
| 005 | AI Search privilege_group_ids field | :black_square_button: | Yes | none |
| 006 | DI feature module skeletons | :black_square_button: | Yes | none |
| 007 | SSE event type contracts (JSON Schema) | :black_square_button: | Yes | none |
| 008 | ISprkAgent interface + DirectOpenAiAgent stub | :black_square_button: | Yes | none |

## Phase 2A: Capability Orchestration (7 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 010 | CapabilityManifest singleton | :black_square_button: | Yes | 006 |
| 011 | ManifestRefreshService (polling + webhook) | :black_square_button: | Yes | 010 |
| 012 | CapabilityRouter Layer 1 (keyword) | :black_square_button: | Yes | 010 |
| 013 | CapabilityRouter Layer 2 (GPT-4o-mini) | :black_square_button: | Yes | 004, 012 |
| 014 | CapabilityRouter Layer 3 (superset fallback) | :black_square_button: | Yes | 012 |
| 015 | OrchestratorPromptBuilder | :black_square_button: | Yes | 010 |
| 016 | Capability validation (permissions, kill switch) | :black_square_button: | Yes | 010 |

## Phase 2B: AI Safety Perimeter (9 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 020 | PromptShieldService | :black_square_button: | Yes | 003, 006 |
| 021 | GroundednessCheckService | :black_square_button: | Yes | 003, 006 |
| 022 | CitationVerificationService + IVerificationProvider | :black_square_button: | Yes | 006 |
| 023 | InternalIndexProvider (AI Search lookup) | :black_square_button: | Yes | 022 |
| 024 | VerifyCitationsTool (dual-mode) | :black_square_button: | Yes | 022, 023 |
| 025 | ConfidenceScoringService | :black_square_button: | Yes | 021 |
| 026 | Structured output validation (JSON Schema) | :black_square_button: | Yes | 007 |
| 027 | Privilege-aware retrieval | :black_square_button: | Yes | 005 |
| 028 | Cross-matter conversation safety | :black_square_button: | Yes | 027 |

## Phase 2C: Session Persistence (7 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 030 | SessionPersistenceService (Redis + Cosmos) | :black_square_button: | Yes | 002, 006 |
| 031 | SessionRestoreService | :black_square_button: | Yes | 030 |
| 032 | Session summarization (GPT-4o at 25 msgs) | :black_square_button: | Yes | 030 |
| 033 | AuditLogService (append-only Cosmos) | :black_square_button: | Yes | 002, 006 |
| 034 | MatterMemoryService | :black_square_button: | Yes | 002, 006 |
| 035 | PromptLibraryService (4-tier) | :black_square_button: | Yes | 002, 006 |
| 036 | FeedbackService | :black_square_button: | Yes | 002, 006 |

## Phase 2D: Data & Search (4 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 040 | DataverseQueryTools (OData) | :black_square_button: | Yes | 006 |
| 041 | RecordSyncJob (Dataverse -> AI Search) | :black_square_button: | Yes | 006 |
| 042 | CompareDocumentsTool | :black_square_button: | Yes | 006 |
| 043 | Default "Spaarke AI General" playbook | :black_square_button: | Yes | none |

## Phase 3: Agent Boundary & Integration (7 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 060 | DirectOpenAiAgent full implementation | :black_square_button: | Yes | 008, 015 |
| 061 | SprkChatAgentFactory extension (per-turn tools) | :black_square_button: | Yes | 010, 012, 060 |
| 062 | ChatEndpoints SSE event types | :black_square_button: | Yes | 007 |
| 063 | Per-tool error isolation | :black_square_button: | Yes | 061 |
| 064 | ChatSessionManager Cosmos integration | :black_square_button: | Yes | 030 |
| 065 | Safety pipeline integration (pre/post-LLM) | :black_square_button: | Yes | 020, 021, 022, 060 |
| 066 | Latency monitoring (Azure Monitor) | :black_square_button: | Yes | 061 |

## Phase 4A: Widget Library Foundation (5 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 070 | @spaarke/ai-widgets package scaffold | :black_square_button: | Yes | none |
| 071 | WorkspaceWidget interface + base types | :black_square_button: | Yes | 070 |
| 072 | WorkspaceWidgetRegistry | :black_square_button: | Yes | 071 |
| 073 | ContextWidgetRegistry | :black_square_button: | Yes | 071 |
| 074 | PaneEventBus (unified cross-pane events) | :black_square_button: | Yes | 070 |

## Phase 4B: Shell Rebuild (5 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 075 | ThreePaneShell root component | :black_square_button: | Yes | 074 |
| 076 | AiSessionProvider (replaces StandaloneAiProvider) | :black_square_button: | Yes | 074 |
| 077 | WorkspacePane + WorkspaceTabManager | :black_square_button: | Yes | 072, 075 |
| 078 | ContextPaneController (adaptive rendering) | :black_square_button: | Yes | 073, 075 |
| 079 | ConversationPane (SprkChat wrapper) | :black_square_button: | Yes | 075, 076 |

## Phase 4C: Widget Migration (3 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 080 | Migrate 7 output widgets + serialize/restore | :black_square_button: | Yes | 072 |
| 081 | Migrate 6 source widgets | :black_square_button: | Yes | 073 |
| 082 | Consolidate useSseStream (merge 2 impls) | :black_square_button: | Yes | 076 |

## Phase 4D: New Widgets (5 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 085 | RedlineViewerWidget | :black_square_button: | Yes | 071, 042 |
| 086 | PlaybookGalleryWidget | :black_square_button: | Yes | 073 |
| 087 | EntityInfoWidget | :black_square_button: | Yes | 073 |
| 088 | ProgressTrackerWidget | :black_square_button: | Yes | 073 |
| 089 | FindingsWidget | :black_square_button: | Yes | 073 |

## Phase 4E: Safety & Feedback UI (3 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 090 | Safety annotation UI | :black_square_button: | Yes | 074, 062 |
| 091 | Confidence indicator UI | :black_square_button: | Yes | 074 |
| 092 | Feedback collection UI | :black_square_button: | Yes | 074, 036 |

## Phase 5: Integration & End-to-End (8 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 100 | Cross-pane: citation click -> source highlight | :white_check_mark: | Yes | 074, 080, 081 |
| 101 | Cross-pane: text selection -> chat refinement | :white_check_mark: | Yes | 074, 079 |
| 102 | Cross-pane: playbook selection -> chat specialize | :white_check_mark: | Yes | 074, 086, 079 |
| 103 | Cross-pane: tab change -> context adaptation | :white_check_mark: | Yes | 077, 078 |
| 104 | Embedded wizards in Workspace | :white_check_mark: | Yes | 077 |
| 105 | Stage lifecycle (4 stages) | :white_check_mark: | Yes | 075, 079, 077, 078 |
| 106 | Session restore end-to-end | :white_check_mark: | Yes | 031, 080, 081 |
| 107 | Welcome panel redesign (Stage 1) | :white_check_mark: | Yes | 079, 086 |

## Phase 6: Testing & Hardening (9 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 110 | Safety perimeter E2E test | :white_check_mark: | Yes | 065, 090 |
| 111 | Widget serialize/restore test (18 widgets) | :white_check_mark: | Yes | 080, 081, 085-089 |
| 112 | Capability routing benchmark | :white_check_mark: | Yes | 061, 066 |
| 113 | Session restore load test (<500ms) | :white_check_mark: | Yes | 106 |
| 114 | Prompt token budget validation (~9000) | :white_check_mark: | Yes | 015, 061 |
| 115 | Audit log immutability verification | :white_check_mark: | Yes | 033 |
| 116 | Cross-matter privilege leakage test | :black_square_button: | Yes | 028, 027 |
| 117 | SprkChat regression test (23 SSE types) | :black_square_button: | Yes | 079, 062 |
| 118 | Dark mode / accessibility (ADR-021) | :black_square_button: | Yes | 090, 091, 092 |

## Phase 7: Deployment & Wrap-up (6 tasks)

| # | Task | Status | Parallel Safe | Dependencies |
|---|------|--------|--------------|--------------|
| 120 | Deploy Cosmos DB to dev | :black_square_button: | Yes | 002 |
| 121 | Deploy BFF API with R2 services | :black_square_button: | Yes | Phase 3 |
| 122 | Deploy SpaarkeAi web resource | :black_square_button: | Yes | Phase 4 |
| 123 | Production verification | :black_square_button: | Yes | 121, 122 |
| 124 | Documentation updates | :black_square_button: | Yes | 123 |
| 199 | Project wrap-up | :black_square_button: | No | all |

---

## Parallel Execution Groups

| Wave | Tasks | Max Agents | Phase | Prerequisites |
|------|-------|-----------|-------|---------------|
| W1-serial | 001 | 1 | 1 | None (.claude/ path) |
| W1-parallel | 002, 003, 004, 005, 006, 007 | 6 | 1 | None |
| W1-B | 008 | 1 | 1 | 006, 007 |
| W2.1 | 010, 020, 021, 030, 033, 040 | 6 | 2 | Phase 1 |
| W2.2 | 011, 022, 034, 035, 041, 042 | 6 | 2 | W2.1 partial |
| W2.3 | 012, 015, 023, 036, 043 | 5 | 2 | W2.2 partial |
| W2.4 | 013, 014, 016, 024, 025, 026 | 6 | 2 | W2.3 partial |
| W2.5 | 027, 028, 031, 032 | 4 | 2 | W2.4 partial |
| W3.1 | 060, 062, 064 | 3 | 3 | Phase 2 |
| W3.2 | 061, 063, 065, 066 | 4 | 3 | W3.1 |
| W4.1 | 070, 074 | 2 | 4A | None (parallel with Phase 2/3) |
| W4.2 | 071, 072, 073 | 3 | 4A | W4.1 |
| W4.3 | 075, 076, 082 | 3 | 4B | W4.2 |
| W4.4 | 077, 078, 079 | 3 | 4B | W4.3 |
| W4.5 | 080, 081, 085, 086, 087, 088 | 6 | 4C/D | W4.4 |
| W4.6 | 089, 090, 091, 092 | 4 | 4D/E | W4.5 partial |
| W5.1 | 100, 101, 102, 103, 104, 105 | 6 | 5 | Phase 3+4 |
| W5.2 | 106, 107 | 2 | 5 | W5.1 |
| W6.1 | 110, 111, 112, 113, 114, 115 | 6 | 6 | Phase 5 |
| W6.2 | 116, 117, 118 | 3 | 6 | W6.1 |
| W7 | 120, 121, 122, 123, 124, 199 | serial | 7 | Phase 6 |

**Key optimization**: Phase 4 (frontend) can start at W4.1 independently of backend Phases 2-3. This means backend and frontend streams run in parallel, significantly reducing total elapsed time.

## Critical Path

```
001 -> 006 -> 010 -> 012 -> 013 -> 061 -> 065 (backend safety integration)
                                      |
070 -> 074 -> 075 -> 077 -> 080 -> 100 -> 105 -> 110 (frontend through testing)
```

Longest chain: ~18 tasks. With parallel execution, effective depth is ~10 waves.
