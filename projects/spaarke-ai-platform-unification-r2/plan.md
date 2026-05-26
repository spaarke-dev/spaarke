# Spaarke AI Platform Unification R2 - Implementation Plan

> **Status**: Active
> **Created**: 2026-05-17
> **Spec**: [spec.md](spec.md)

## Architecture Context

### Discovered Resources

**ADRs (16):** ADR-001, 004, 006, 007, 008, 009, 010, 012, 013, 014, 015, 016, 021, 022
**Patterns:** 4 AI patterns, 9 API patterns
**Constraints:** 14 constraint files (ai, api, auth, data, pcf, testing, theme-consistency)
**Architecture Docs:** AI-ARCHITECTURE, chat-architecture, playbook-architecture, code-pages-architecture, scope-architecture, rag-architecture
**Infrastructure:** No Cosmos DB module exists (new), AI Search schemas exist, GPT-4o-mini deployment needed
**Scripts:** Deploy-BffApi.ps1, Deploy-AnalysisWorkspace.ps1 (no SpaarkeAi deploy script yet)

### Key Architectural Constraints

1. **Frontend is a REBUILD** — new shell, event bus, widget registries; SprkChat + 13 widgets preserved
2. **Backend is an EXTENSION** — new services alongside existing via feature modules
3. **ADR-010**: Feature modules (AddAiSafetyModule, AddAiCapabilitiesModule, AddAiPersistenceModule, AddAiChatModule)
4. **ADR-015 Amendment**: Governed data stores (audit, work history, memory) are explicit exceptions to logging prohibition

## Phase Breakdown

### Phase 1: Foundation & Infrastructure (Serial)
**Objective:** Provision infrastructure, amend ADRs, create DI skeleton

| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 001 | ADR-015 amendment (governed data stores section) | None | No (.claude/) |
| 002 | Cosmos DB Bicep module + provisioning | None | Yes |
| 003 | Azure AI Content Safety resource verification/provisioning | None | Yes |
| 004 | GPT-4o-mini deployment on spaarke-openai-dev | None | Yes |
| 005 | AI Search index update: privilege_group_ids field | None | Yes |
| 006 | DI feature module skeletons (4 modules, empty registrations) | None | Yes |
| 007 | SSE event type contracts (JSON Schema definitions) | None | Yes |
| 008 | ISprkAgent interface + DirectOpenAiAgent stub | None | Yes |

**Parallel Group P1-A:** Tasks 002, 003, 004, 005 (independent infrastructure)
**Parallel Group P1-B:** Tasks 006, 007, 008 (independent code skeletons)
**Serial:** Task 001 (.claude/ path — main session only)

---

### Phase 2: Backend Services (High Parallelism)
**Objective:** Build all new BFF services in parallel streams

#### Phase 2A: Capability Orchestration (FR-300)
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 010 | CapabilityManifest singleton + in-memory catalog | 006 | Yes |
| 011 | ManifestRefreshService (BackgroundService + polling) | 010 | Yes |
| 012 | CapabilityRouter Layer 1 (keyword classifier) | 010 | Yes |
| 013 | CapabilityRouter Layer 2 (GPT-4o-mini pre-check) | 004, 012 | Yes |
| 014 | CapabilityRouter Layer 3 (broad superset fallback) | 012 | Yes |
| 015 | OrchestratorPromptBuilder (stable prefix + per-turn schemas) | 010 | Yes |
| 016 | BFF capability validation (permissions, kill switch, context) | 010 | Yes |

#### Phase 2B: AI Safety Perimeter (FR-400)
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 020 | PromptShieldService (Azure AI Content Safety API) | 003, 006 | Yes |
| 021 | GroundednessCheckService (Content Safety API) | 003, 006 | Yes |
| 022 | CitationVerificationService + IVerificationProvider interface | 006 | Yes |
| 023 | InternalIndexProvider (AI Search spaarke-references lookup) | 022 | Yes |
| 024 | VerifyCitationsTool (dual-mode: safety check + AI tool) | 022, 023 | Yes |
| 025 | ConfidenceScoringService (source passage counting) | 021 | Yes |
| 026 | Structured output validation (JSON Schema for SSE events) | 007 | Yes |
| 027 | Privilege-aware retrieval (privilege_group_ids filter) | 005 | Yes |
| 028 | Cross-matter conversation safety (content stripping) | 027 | Yes |

#### Phase 2C: Session Persistence (FR-500)
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 030 | SessionPersistenceService (Redis + Cosmos write-through) | 002, 006 | Yes |
| 031 | SessionRestoreService (load + staleness check + reconstruct) | 030 | Yes |
| 032 | Session summarization (GPT-4o at 25 messages) | 030 | Yes |
| 033 | AuditLogService (append-only Cosmos, immutable policy) | 002, 006 | Yes |
| 034 | MatterMemoryService (per-matter structured facts) | 002, 006 | Yes |
| 035 | PromptLibraryService (4-tier ownership, Cosmos + Dataverse) | 002, 006 | Yes |
| 036 | FeedbackService (thumbs up/down + text, per-response) | 002, 006 | Yes |

#### Phase 2D: Data & Search (FR-600)
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 040 | DataverseQueryTools (QueryEntities + GetEntityDetail OData) | 006 | Yes |
| 041 | RecordSyncJob (BackgroundService, Dataverse -> AI Search) | 006 | Yes |
| 042 | CompareDocumentsTool (fetch 2 docs from SPE, produce diff) | 006 | Yes |
| 043 | Default "Spaarke AI General" playbook record in Dataverse | None | Yes |

**Parallel Group P2-ABCDE:** Tasks 010-016, 020-028, 030-036, 040-043 can ALL run in parallel (independent service domains). Max 6 agents per wave.

**Recommended Wave Sequence:**
- Wave 2.1: Tasks 010, 020, 021, 030, 033, 040 (6 agents — one per service domain foundation)
- Wave 2.2: Tasks 011, 022, 034, 035, 041, 042 (6 agents)
- Wave 2.3: Tasks 012, 015, 023, 036, 043 (5 agents)
- Wave 2.4: Tasks 013, 014, 016, 024, 025, 026 (6 agents — routing + safety completion)
- Wave 2.5: Tasks 027, 028, 031, 032 (4 agents — dependent completions)

---

### Phase 3: Agent Boundary & Integration (Moderate Parallelism)
**Objective:** Wire orchestration into SprkChatAgentFactory; define SSE contract

| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 060 | ISprkAgent full implementation + DirectOpenAiAgent | 008, 015 | Yes |
| 061 | SprkChatAgentFactory extension (per-turn tool injection via router) | 010, 012, 060 | Yes |
| 062 | ChatEndpoints SSE event types (workspace_widget, context_update, safety_annotation) | 007 | Yes |
| 063 | Per-tool error isolation in ResolveTools | 061 | Yes |
| 064 | ChatSessionManager extension (Cosmos write-through integration) | 030 | Yes |
| 065 | Safety pipeline integration (pre-LLM + post-LLM hooks in agent flow) | 020, 021, 022, 060 | Yes |
| 066 | Latency monitoring setup (Azure Monitor metrics: TTFT, TBT, TTLT) | 061 | Yes |

**Parallel Group P3-A:** Tasks 060, 062, 064 (independent interfaces/endpoints)
**Parallel Group P3-B:** Tasks 061, 063, 065, 066 (after P3-A completes)

---

### Phase 4: Frontend Rebuild (High Parallelism)
**Objective:** Rebuild SpaarkeAi shell, create widget library, migrate widgets

#### Phase 4A: Widget Library Foundation
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 070 | @spaarke/ai-widgets package scaffold (package.json, tsconfig, build) | None | Yes |
| 071 | WorkspaceWidget<TData, TActions> interface + base types | 070 | Yes |
| 072 | WorkspaceWidgetRegistry (lazy-load dynamic import) | 071 | Yes |
| 073 | ContextWidgetRegistry (lazy-load dynamic import) | 071 | Yes |
| 074 | Cross-pane event types + PaneEventBus | 070 | Yes |

#### Phase 4B: Shell Rebuild
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 075 | ThreePaneShell root component (replaces App.tsx provider tree) | 074 | Yes |
| 076 | AiSessionProvider (replaces StandaloneAiProvider, multi-subscriber) | 074 | Yes |
| 077 | WorkspacePane + WorkspaceTabManager (max 3 tabs) | 072, 075 | Yes |
| 078 | ContextPaneController (adaptive stage-based rendering) | 073, 075 | Yes |
| 079 | ConversationPane (wraps SprkChat as child, stage transitions) | 075, 076 | Yes |

#### Phase 4C: Widget Migration (R1 -> R2)
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 080 | Migrate 7 output widgets to WorkspaceWidgetRegistry + serialize/restore | 072 | Yes |
| 081 | Migrate 6 source widgets to ContextWidgetRegistry | 073 | Yes |
| 082 | useSseStream consolidation (merge 2 implementations into 1) | 076 | Yes |

#### Phase 4D: New Widgets
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 085 | RedlineViewerWidget (side-by-side diff + change tracking) | 071, 042 | Yes |
| 086 | PlaybookGalleryWidget (Context pane, playbook selection) | 073 | Yes |
| 087 | EntityInfoWidget (Context pane, entity detail display) | 073 | Yes |
| 088 | ProgressTrackerWidget (Context pane, workflow progress) | 073 | Yes |
| 089 | FindingsWidget (Context pane, analysis findings display) | 073 | Yes |

#### Phase 4E: Safety & Feedback UI
| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 090 | Safety annotation UI (groundedness highlights, citation badges) | 074, 062 | Yes |
| 091 | Confidence indicator UI (high/medium/low bars per response) | 074 | Yes |
| 092 | Feedback collection UI (thumbs up/down + optional text) | 074, 036 | Yes |

**Parallel Group P4-Foundation:** Tasks 070, 074 (package + events — run first)
**Parallel Group P4-Interfaces:** Tasks 071, 072, 073 (after P4-Foundation)
**Parallel Group P4-Shell:** Tasks 075, 076 (after 074)
**Parallel Group P4-Panes:** Tasks 077, 078, 079 (after P4-Shell)
**Parallel Group P4-Migration:** Tasks 080, 081, 082 (after registries)
**Parallel Group P4-Widgets:** Tasks 085, 086, 087, 088, 089 (after 071/073)
**Parallel Group P4-UI:** Tasks 090, 091, 092 (after 074)

---

### Phase 5: Integration & End-to-End (Moderate Parallelism)
**Objective:** Wire frontend to backend; implement cross-pane interactions; end-to-end flows

| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 100 | Cross-pane interaction: citation click -> source highlight | 074, 080, 081 | Yes |
| 101 | Cross-pane interaction: text selection -> chat refinement | 074, 079 | Yes |
| 102 | Cross-pane interaction: playbook selection -> chat specialization | 074, 086, 079 | Yes |
| 103 | Cross-pane interaction: tab change -> context adaptation | 077, 078 | Yes |
| 104 | Embedded wizards in Workspace (CreateMatter, DocUpload, SearchSelect) | 077 | Yes |
| 105 | Stage lifecycle: Landing -> Playbook Selected -> Active Work -> Multi-Task | 075, 079, 077, 078 | Yes |
| 106 | Session restore end-to-end (Cosmos -> widgets -> panes) | 031, 080, 081 | Yes |
| 107 | Welcome panel redesign (Stage 1 of three-pane lifecycle) | 079, 086 | Yes |

**Parallel Group P5-A:** Tasks 100, 101, 102, 103 (independent cross-pane interactions)
**Parallel Group P5-B:** Tasks 104, 105, 106, 107 (independent integration flows)

---

### Phase 6: Testing & Hardening (High Parallelism)
**Objective:** Validate all systems; benchmark; harden

| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 110 | Safety perimeter end-to-end test (injection, groundedness, citations) | 065, 090 | Yes |
| 111 | Widget serialize/restore test (all 18 widgets) | 080, 081, 085-089 | Yes |
| 112 | Capability routing benchmark (Layer 1/2/3 distribution, latency) | 061, 066 | Yes |
| 113 | Session restore load test (<500ms target with 25+ messages) | 106 | Yes |
| 114 | Prompt token budget validation (~9000 target) | 015, 061 | Yes |
| 115 | Audit log immutability verification | 033 | Yes |
| 116 | Cross-matter privilege leakage test | 028, 027 | Yes |
| 117 | SprkChat regression test (all 23 SSE event types) | 079, 062 | Yes |
| 118 | Dark mode / accessibility compliance (ADR-021) | 090, 091, 092 | Yes |

**Parallel Group P6-ALL:** Tasks 110-118 (all independent test domains)

---

### Phase 7: Deployment & Wrap-up (Serial)
**Objective:** Deploy, verify, document, close

| Task | Deliverable | Dependencies | Parallel Safe |
|------|------------|--------------|---------------|
| 120 | Deploy Cosmos DB infrastructure to dev | 002 | Yes |
| 121 | Deploy BFF API with all new services | Phase 3 complete | Yes |
| 122 | Deploy SpaarkeAi web resource (create deploy script if needed) | Phase 4 complete | Yes |
| 123 | Production verification (safety, routing, persistence) | 121, 122 | Yes |
| 124 | Documentation updates (architecture docs, guides) | 123 | Yes |
| 199 | Project wrap-up (README status, lessons-learned, repo-cleanup) | All | No |

---

## Parallel Execution Summary

| Wave | Tasks | Agents | Phase | Prerequisites |
|------|-------|--------|-------|---------------|
| W1 | 001 (serial) + 002,003,004,005 + 006,007,008 | 1+4+3 | Phase 1 | None |
| W2.1 | 010,020,021,030,033,040 | 6 | Phase 2 | Phase 1 |
| W2.2 | 011,022,034,035,041,042 | 6 | Phase 2 | W2.1 |
| W2.3 | 012,015,023,036,043 | 5 | Phase 2 | W2.2 |
| W2.4 | 013,014,016,024,025,026 | 6 | Phase 2 | W2.3 |
| W2.5 | 027,028,031,032 | 4 | Phase 2 | W2.4 |
| W3.1 | 060,062,064 | 3 | Phase 3 | Phase 2 |
| W3.2 | 061,063,065,066 | 4 | Phase 3 | W3.1 |
| W4.1 | 070,074 | 2 | Phase 4A | None (can overlap Phase 2/3) |
| W4.2 | 071,072,073 | 3 | Phase 4A | W4.1 |
| W4.3 | 075,076 | 2 | Phase 4B | W4.2 |
| W4.4 | 077,078,079,082 | 4 | Phase 4B/C | W4.3 |
| W4.5 | 080,081,085,086,087,088 | 6 | Phase 4C/D | W4.4 |
| W4.6 | 089,090,091,092 | 4 | Phase 4D/E | W4.5 |
| W5 | 100,101,102,103,104,105 | 6 | Phase 5 | Phase 3+4 |
| W5.2 | 106,107 | 2 | Phase 5 | W5 |
| W6 | 110-118 | 6+3 (2 waves) | Phase 6 | Phase 5 |
| W7 | 120,121,122,123,124,199 | Serial | Phase 7 | Phase 6 |

**Total tasks: ~80**
**Max concurrent agents per wave: 6**
**Frontend (Phase 4) can start at W4.1 independently of backend Phases 2-3**

## References

- [Specification](spec.md) — 43 functional requirements, 15 NFRs
- [Design Document](design.md) — detailed design with architecture diagrams
- [Architecture Companion](architecture.md) — component inventory and data flows
- `.claude/adr/` — all applicable ADRs
- `.claude/patterns/ai/` — AI implementation patterns
- `.claude/patterns/api/` — API endpoint patterns
- `.claude/constraints/` — MUST/MUST NOT rules by domain
- `docs/architecture/AI-ARCHITECTURE.md` — current AI architecture
- `docs/architecture/chat-architecture.md` — chat system architecture
