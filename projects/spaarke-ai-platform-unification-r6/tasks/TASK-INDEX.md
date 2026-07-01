# R6 Task Index

> **Project**: spaarke-ai-platform-unification-r6
> **Generated**: 2026-06-07 via `/project-pipeline` Step 3
> **Last status sync**: 2026-06-25 (post-UAT)
> **Total tasks**: 80 original + 9 closeout audit (091-098, ~~094~~ withdrawn) + 1 TIER-C diagnostic = 89 + 1 withdrawn
> **Driver**: All tasks executed via `task-execute` skill (FULL rigor for code tasks; STANDARD for tests/exit gates; MINIMAL for docs)
> **Calendar estimate**: 6–7 weeks (Q7 expansion adds 1–2 weeks vs spec's 5–7)

---

## Current Status Roll-up (2026-06-25)

| Status | Count | Notes |
|---|---|---|
| ✅ Completed | 80 (original) + 2 (097a, 097c) = 82 | All Phase A/B/C/D + handler workstream + 2 audit hotfixes |
| 🔲 Pending — closeout (R6 admin) | 089, 090 | Phase D exit-gate + project wrap-up; gated on UAT pass |
| 🔲 Pending — surface completion | 095, 097b, 098, TIER-C (+optional 096) | **UAT-blocking per 2026-06-25 walkthrough** |
| 🔲 Pending — Builder UI | 091, 093 (+092 cleanup) | Without these, persona + Q5 routing aren't configurable from maker portal |
| ❌ Withdrawn | 094 | Audit found already wired |

**R6 closure criteria**: surface completion (95/97b/98/TIER-C) → UAT Tiers A–G pass → 089 + 090 close. Builder UI (091, 093) may carry to R7 per audit recommendation.

**Production state**: PR #401 merged 2026-06-24 (commit `8579d6536`); deployed via Environment Promotion at 19:06:26.

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| 🚧 | Blocked |
| ⏭️ | Deferred |
| ⚠️ | Completed with limitations |

---

## Phase Summary

| Phase | Pillars | Task IDs | Count | Calendar |
|-------|---------|----------|-------|----------|
| **A** | 1 (persona), 2 (tool registry + Q9 batch), 3 (invoke_playbook), 4 (PlaybookExecutionEngine FK fix) | 001–029 (with gaps) | 22 | Week 1–2 |
| **B** | 5 (outputSchema on action; destination on node; widget schema-aware; CapabilityRouter dedup) | 030–049 (with gaps) | 11 | Week 2–3 |
| **C** | 6a + 6b + 6c (workspace state + tools + trace), 7 (memory + Q7 UI), 9 (visibility contract) | 050–079 (with gaps) | 27 | Week 3–6 (Q7 expansion +1–2 weeks) |
| **D** | 8 (command router), vertical-slice integration, eval baseline | 080–089 | 10 | Week 6–7 |
| **Parallel** | 8 typed tool handlers | 100–109 | 10 | Spans Phase A–C |
| **Wrap-up** | code-review + adr-check + repo-cleanup + lessons-learned | 090 | 1 | End of Phase D |

---

## Tasks by Phase

### Phase A — Data-driven Foundation (Week 1–2, 22 tasks)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 001 | A-G0 | Create `sprk_aipersona` Dataverse entity (D-A-01) | ✅ | FULL | true | none |
| 002 | A-G1 | `GET /api/ai/scopes/personas` endpoint (D-A-02) | ✅ | FULL | true | 001 |
| 003 | A-G1 | Persona resolver methods in `IScopeResolverService` (D-A-03) | ✅ | FULL | true | 001 |
| 004 | A-G1 | Seed default SYS- persona row (D-A-04) | ✅ | STANDARD | true | 001 |
| 005 | A-G2 | Wire `SprkChatAgentFactory.CreateAgentAsync` to scope persona (D-A-05) | ✅ | FULL | false | 002, 003, 004 |
| 006 | A-G0' | Rename `IAnalysisToolHandler` → `IToolHandler` (D-A-06) | ✅ | FULL | true | none |
| 007 | A-G3 | Add `AvailableInContexts` enum + Dataverse column (D-A-07) | ✅ | FULL | true | 006 |
| 008 | A-G3 | Add `JsonSchema` field to `AnalysisTool` DTO + Dataverse column (D-A-08) | ✅ | FULL | false (same DTO as 007) | 006, 007 |
| 009 | A-G3 | Split execution context (Tool/ChatInvocation) (D-A-09) | ✅ | FULL | true | 006 |
| 010 | A-G4 | Build `ToolHandlerToAIFunctionAdapter` (D-A-10) | ✅ | FULL | false | 007, 008, 009 |
| 011 | A-G5 | Wire `ResolveTools()` to read `sprk_analysistool` rows (D-A-11) | ✅ | FULL | false | 010 |
| 012 | A-G6 | **Q9 BIG-BANG: Migrate 10 pre-R5 chat tools** (D-A-11) | ✅ | FULL | false | 011 |
| 013 | A-G7 | Q9 migration regression test gate (D-A-11) | ✅ | STANDARD | false | 012 |
| 020 | A-G8 | Add `IInvokePlaybookAi` facade (D-A-12) | ✅ | FULL | true | 011 |
| 021 | A-G9 | Build generic `invoke_playbook` chat tool (D-A-13) | ✅ | FULL | true | 020 |
| 022 | A-G10 | Dynamic playbook list in tool description (D-A-14) | ✅ | FULL | true | 021 |
| 023 | A-G11 | Remove `InvokeSummarizePlaybookTool`/`InvokeInsightsQueryTool` bridges (D-A-15) | ✅ | FULL | true | 022 |
| 024 | A-G8 | Playbook FK fix: summarize-document-for-chat → SUM-CHAT (D-A-16) | ✅ | STANDARD | true | 011 |
| 025 | A-G12 | Refactor `SessionSummarizeOrchestrator` to use `PlaybookExecutionEngine` (D-A-17) | ✅ | FULL | false | 024 |
| 028 | A-G13 | Phase A integration test | ✅ | STANDARD | false | 005, 013, 023, 025 |
| 029 | A-G14 | Phase A exit-gate validation | ✅ | MINIMAL | false | 028 |

### Phase B — Schema-Aware Output (Week 2–3, 11 tasks)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 030 | B-G1 | Add `outputSchema` field to `sprk_analysisaction` (D-B-01) — Option A: verify existing `sprk_outputschemajson` column (no deploy) | ✅ | FULL | true | 029 |
| 031 | B-G1 | Extend node config with `destination` + `widgetType` (D-B-02) | ✅ | FULL | true | 029 |
| 032 | B-G2 | Migrate `summarize-document-for-chat@v1` action outputSchema (D-B-03) | ✅ | STANDARD | true | 030, 031 |
| 033 | B-G2 | Migrate `summarize-document-for-workspace@v1` action outputSchema (D-B-04) — REVISED: CREATE new playbook referencing shared SUM-CHAT@v1 (Option A) | ✅ | STANDARD | true | 030, 031 |
| 034 | B-G2 | **Migrate matter-prefill action outputSchema (NFR-07 regression test)** (D-B-05) — data work via timed-out sub-agent; closeout by main session 2026-06-09 | ✅ | FULL | true | 030, 031 |
| 035 | B-G2 | **Migrate project-prefill action outputSchema (NFR-07 regression test)** (D-B-06) | ✅ | FULL | true | 030, 031 |
| 040 | B-G3 | `StructuredOutputStreamWidget` schema-aware array rendering (D-B-07) | ✅ | FULL | true | 032, 033 |
| 041 | B-G3 | `StructuredOutputStreamWidget` schema-aware object rendering (D-B-08) | ✅ | FULL | false (same file as 040) | 040 |
| 042 | B-G4 | CapabilityRouter dedup (one intent → one route) (D-B-09) | ✅ | FULL | false | 041, 025 |
| 048 | B-G5 | Phase B integration test (TL;DR + Entities + dedup + pre-fill regression) | ✅ | STANDARD | false | 042, 034, 035 |
| 049 | B-G6 | Phase B exit-gate validation — 5/5 GREEN; 4 YELLOW flags; programmatically exit-ready; pending user manual UI walkthrough sign-off | ✅ | MINIMAL | false | 048 |

### Phase C — Tri-directional Workspace + Memory + Visibility (Week 3–6, 27 tasks)

#### Sub-phase 6a — Workspace state model (gates 6b/6c/7/9)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 050 | C-G1 | `WorkspaceTab` canonical TypeScript interface (D-C-01) | ✅ | FULL | true | 049 |
| 051 | C-G2 | `WorkspaceStateService.cs` (Redis hot + Cosmos durable per Q4) (D-C-02) | ✅ | FULL | false | 050 |
| 052 | C-G3 | `GET /api/workspace/state` endpoint (D-C-03) | ✅ | FULL | false | 051 |
| 053 | C-G4 | Wire `WorkspaceStateService` into `SprkChatAgentFactory` (D-C-04) | ✅ | FULL | false | 052 |

#### Sub-phase 6b — Chat tools + affordances + conflict (parallel with 6c/7/9 after 6a)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 054 | C-G5 | `send_workspace_artifact` chat tool (D-C-05) | ✅ | FULL | true | 053 |
| 055 | C-G5 | `update_workspace_tab` chat tool (Q8 conflict-check) (D-C-06) | ✅ | FULL | true | 053 |
| 056 | C-G5 | `close_workspace_tab` chat tool (D-C-07) | ✅ | FULL | true | 053 |
| 057 | C-G6 | User affordances (Send to Workspace + Add to Assistant + Pin to Matter) (D-C-08/09/10) | ✅ | FULL | false | 054, 055, 056 |
| 058 | C-G7 | Conflict resolution implementation (Q8 user wins) (D-C-11) | ✅ | FULL | false | 055 |

#### Sub-phase 6c — Trace widget + PaneEventBus events (parallel with 6b/7/9)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 059 | C-G5 | Additive `context.*` PaneEventBus event types (ADR-015 binding) (D-C-12) | ✅ | FULL | true | 053 |
| 060 | C-G5 | Additive `workspace.*` PaneEventBus event types (D-C-13) — TS via timed-out sub-agent; closeout 2026-06-09 main session | ✅ | FULL | true | 053 |
| 061 | C-G8 | `ExecutionTraceWidget.tsx` (Context-pane; ordered timeline) (D-C-14) | ✅ | FULL | false | 059 |
| 062 | C-G9 | Register trace widget with `ContextWidgetRegistry` (D-C-15) | ✅ | STANDARD | false | 061 |
| 063 | C-G10 | Emit `context.*` events from chat agent + playbook execution (D-C-16) | ✅ | FULL | false | 059 |

#### Sub-phase 7 — Memory + Q7 expansion

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 064 | C-G5 | Summarization compression service (D-C-17) | ✅ | FULL | true | 053 |
| 065 | C-G5 | Pinned-context entity in Cosmos `memory` container (D-C-18) | ✅ | FULL | true | 053 |
| 066 | C-G11 | Selective recall via embedding similarity (D-C-19) | ✅ | FULL | false | 064 |
| 067 | C-G12 | Hierarchical memory composition (D-C-20) | ✅ | FULL | false | 064, 065, 066 |
| 068 | C-G13 | Activate `MatterMemoryService` + shared token budget tracker (D-C-21/22) | ✅ | FULL | false | 067 |
| 069 | C-G14 | "Remember/forget/always" recognition via CapabilityRouter (D-C-23) | ✅ | FULL | false | 065, 068 |
| 070 | C-G15 | **Q7 EXPANSION: Pinned Memory CRUD + Visualization UI** (D-C-24/25) — PART A (BFF endpoints) ✅ + PART B (frontend components + registry + 27 tests) ✅ | ✅ | FULL | false | 065, 069 |

#### Sub-phase 9 — Widget visibility contract (parallel with 6b/6c/7)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 071 | C-G5 | `getAgentVisibleState()` TypeScript interface (D-C-26) | ✅ | FULL | true | 053 |
| 072 | C-G16 | Extend `WorkspaceWidgetRegistry` with `getVisibleState?` (D-C-27) | ✅ | STANDARD | false | 071 |
| 073 | C-G17 | Implement `getAgentVisibleState()` per widget type (D-C-28) | ✅ | FULL | false | 072 |
| 074 | C-G18 | Per-turn agent prompt builder gathers visible state (D-C-29/30) | ✅ | FULL | false | 053, 073 |

#### Integration + exit

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 078 | C-G19 | Phase C cross-pillar integration test — composed evidence (per-task tests) + 6 NEW cross-pillar tests in `tests/integration/Spe.Integration.Tests/PhaseC/`; see `notes/phase-c-integration-results.md` | ✅ | STANDARD | false | 057, 058, 062, 063, 070, 074 |
| 079 | C-G20 | Phase C exit-gate validation | ✅ | MINIMAL | false | 078 |

### Phase D — Command Router + Integration + Closeout (Week 6–7, 10 tasks)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 080 | D-G1 | `CommandRouter.ts` parser (D-D-01) — Pillar 8 closed vocabulary (6 hard + 4 soft + 3 ref); pure parser; 36 tests green; wired into `ConversationPane.handleBeforeSendMessage` (capture-only); NFR-11 regression locked; `notes/task-080-evidence.md` | ✅ | FULL | true | 079 |
| 081 | D-G1 | Hard slashes (6: /clear, /new-session, /help, /export, /save-to-matter, /pin) (D-D-02) — `HardSlashExecutor.ts` + `CommandHelpPanel.tsx` + 53 tests green (43 executor + 10 help panel); all 6 commands measured <100ms in mocked-BFF; ADR-015 telemetry audit PASS (no user text in payloads); ADR-021 dark-mode parity PASS (renders under webLightTheme + webDarkTheme); BFF publish-size delta = 0 MB (frontend-only; reuses existing endpoints `/api/ai/chat/sessions/{id}` DELETE, `/api/ai/chat/sessions` POST, `/api/memory/pins` POST, `/api/ai/chat/sessions/{id}/tabs` PATCH); ConversationPane.tsx integration deferred to main session (parallel coordination with 082/083); `notes/task-081-evidence.md` | ✅ | FULL | true | 080 |
| 082 | D-G1 | Soft slashes (4: /summarize, /draft, /extract-entities, /analyze) (D-D-03) — `SoftSlashRouter.ts` (38 tests green) + CapabilityRouter Layer 0.5 pre-pass (18 BFF tests green); BFF publish-size delta ≈ 0 MB; ConversationPane.tsx integration deferred to main session (parallel coordination); `notes/task-082-evidence.md` | ✅ | FULL | true | 080 |
| 083 | D-G1 | References resolver (#scope/@entity/#filename) (D-D-04) — `ReferenceResolver.ts` + 27 tests green; 3 resolver types wired (scope/entity/file); ADR-014 tenantId cache keys; NFR-01 non-blocking; in-flight de-dup; integration handoff in `notes/task-083-evidence.md`; ConversationPane.tsx integration deferred to main session (parallel coordination with 081/082) | ✅ | FULL | true | 080 |
| 084 | D-G2 | Composition integration tests (D-D-05) — `composition.integration.test.ts` with 12 tests green; FR-52 `/summarize #engagement-letter.docx` (5 tests) + `/draft response to @opposing-counsel about #motion-to-dismiss` (4 tests) + NFR-11 NL backward-compat (3 tests); end-to-end parse → resolve → decorate → send chain with stubbed adapters (fileLookup/scopeFetch); ADR-014 tenantId cache keys + NFR-01 non-blocking degradation both exercised; BFF publish-size delta = 0 MB (frontend-only, test-only); `notes/task-084-evidence.md` | ✅ | STANDARD | true | 081, 082, 083 |
| 085 | D-G2 | `/help` UI affordance (D-D-06) — `HelpAffordance.tsx` (subtle Fluent v9 Button + Tooltip + `QuestionCircleRegular`); `HelpAffordance.test.tsx` 10/10 green (aria-label, tooltip, click, Enter/Space keyboard, light/dark theme, disabled state); ConversationPane.tsx minimal additive change (import + render + wire `setHelpPanelOpen(true)` + `sprkChatFlex` gets `position: relative` for absolute anchor); Option A absolute-positioned overlay top-right of chat region; ADR-021 semantic tokens only; NFR-11 additive UX (SprkChat input bar untouched); BFF publish-size delta = 0 MB (frontend-only); `notes/task-085-evidence.md` | ✅ | STANDARD | true | 081 |
| 086 | D-G2 | Natural language regression test (NFR-11 backward compat) (D-D-07) — `natural-language-regression.test.ts` + 50 tests green; 4 NL inputs verified (summarize-this-document / draft-a-reply / what's-the-matter-status / make-it-shorter); each verified for (a) `Intent.command === null` (b) `decorateBody` no-op no `commandIntent` field (c) input body purity (d) NFR-01 conversational primacy preserved for refinement / follow-up; positive anchor (`/summarize` decorates) + negative anchor (`/clear` no decoration) confirm suite integrity; BFF publish-size delta = 0 MB (test-only); `notes/task-086-evidence.md` | ✅ | STANDARD | true | 080 |
| 087 | D-G3 | **Vertical-slice integration test (all 9 pillars per spec §6) (D-D-08)** — **COMPOSED EVIDENCE** framing per task 078 precedent: 9-pillar evidence map at `notes/vertical-slice-evidence.md` + new `Pillar8ToPlaybookEngineTests.cs` (13 tests PASSED in 23 ms) at the Pillar 8 → Pillar 3 → Pillar 4 → Pillar 5 → Pillar 6c BFF chain; ADR-015 audit, NFR-11 fall-through, voice-memory vs soft-slash ordering, FR-30 playbook ID propagation, Q6 closed-vocabulary integrity; BFF publish-size 46.06 MB compressed (+0.41 MB cumulative R6 — well within ≤+5 MB NFR-02 and 60 MB ADR-029 hard limit); NFR-08 invariant verified (`git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty); `notes/task-087-evidence.md` | ✅ | STANDARD | false | 084, 085, 086, 029, 049, 079 |
| 088 | D-G4 | Lightweight eval baseline (Q10 markdown transcripts) (D-D-09) — 4 markdown transcripts at `notes/eval-baseline/`: SYS-DEFAULT × summarize-chat (vertical-slice snapshot) + summarize-workspace (Pillar 5 shared-action) + matter-prefill (NFR-07 evidence) + project-prefill (NFR-07 evidence); each cites Q10 + synthetic user messages per ADR-015; full eval harness deferred R7 per spec Owner Clarifications | ✅ | MINIMAL | false | 087 |
| 089 | D-G5 | Phase D exit-gate validation — 5 spec criteria signed off; evidence at `notes/phase-d-exit-checklist.md`; plan.md Phase D marked ✅; BFF publish-size delta = 0 MB | ✅ | MINIMAL | false | 088 |

### Parallel — 8 Typed Tool Handlers (spans Phase A–C, 10 tasks)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 100 | H-G0 | Handler infra + registration pattern (gate) (D-H-00) | ✅ | FULL | false | 006, 009 |
| 101 | H-G1 | `DateExtractorHandler` (pure deterministic) (D-H-01) | ✅ | FULL | true | 100 |
| 102 | H-G1 | `FinancialCalculatorHandler` (pure deterministic) (D-H-02) | ✅ | FULL | true | 100 |
| 103 | H-G1 | `ClauseComparisonHandler` (pure deterministic) (D-H-03) | ✅ | FULL | true | 100 |
| 104 | H-G1 | `FinancialCalculationToolHandler` (pure deterministic) (D-H-04) | ✅ | FULL | true | 100 |
| 105 | H-G2 | `EntityExtractorHandler` (LLM-assisted NER) (D-H-05) | ✅ | FULL | true | 104 |
| 106 | H-G2 | `ClauseAnalyzerHandler` (LLM-assisted clause structuring) (D-H-06) | ✅ | FULL | true | 104 |
| 107 | H-G2 | `RiskDetectorHandler` (LLM-assisted + severity scoring) (D-H-07) | ✅ | FULL | true | 104 |
| 108 | H-G2 | `InvoiceExtractionToolHandler` (LLM-assisted + line-item arithmetic) (D-H-08) | ✅ | FULL | true | 104 |
| 109 | H-G3 | Handler dispatch tests (playbook + chat) (D-H-09/10) | ✅ | STANDARD | false | 105, 106, 107, 108 |

### Wrap-up (1 task)

| ID | Wave | Title | Status | Rigor | Parallel-safe | Dependencies |
|----|------|-------|--------|-------|---------------|--------------|
| 090 | END | Project wrap-up — lessons-learned.md + r7-backlog.md authored; README/plan/CLAUDE.md flipped to Complete; 4 phase exit gates signed off; BFF final publish-size 46.71 MB compressed (+0.41 MB cumulative R6 — well within NFR-02 +5 MB); 9 GitHub issues filed for carry-forward (#470-#476, #510, #511) | ✅ | FULL | false | 089 |

### Closeout Audit — Surface Completion (added 2026-06-21 per `r6-deliverables-audit.md`)

Tasks identified by the R6 deliverables audit after R6 was declared "feature-complete". These close the gap between SHIPPED code and USABLE functionality. UAT on 2026-06-25 confirmed these are blocking real-world use.

| ID | Pillar | Title | Status | Effort | Notes |
|----|--------|-------|--------|--------|-------|
| 091 | 1 | Builder UI: persona dropdown on playbook properties form — DESCOPED 2026-06-29; DEF-002 (e16677beb) shipped the BFF wiring fix for `sprk_playbookpersona` FK; user added the field to the maker-portal main form. Dedicated Builder dropdown is nice-to-have, not need-to-have. Tracked in r7-backlog.md if Q3 admin UI project picks it up. | ✅ | STANDARD | false | 089 |
| 092 | 2 | Verify + remove vestigial `sprk_capabilities` field on playbook entity — DEF-004 (#476 closed 2026-06-29). User removed the column via maker portal after verification confirmed zero production-code references on `sprk_analysisplaybook`. Sister-project bug ISS-003 (#510) surfaced and filed urgent. | ✅ | MINIMAL | false | 089 |
| 093 | 5 | Builder UI: `destination` + `widgetType` fields on node properties form — DEF-003 (c98fe7f85) shipped 2026-06-29. `DeliverOutputForm.tsx` extended with 5-option destination dropdown + conditional widgetType input; persists `NodeRoutingConfig` JSON to existing `sprk_configjson`. PlaybookBuilder code page built; deployable HTML at `out/sprk_playbookbuilder.html` (2,977 KB) ready for maker portal upload. | ✅ | FULL | false | 089 |
| ~~094~~ | 6a | ~~Frontend workspace state restore~~ | ❌ | — | **WITHDRAWN** — audit found already wired |
| 095 | 6c | Server SSE bridge for `context.*` events + client mount for `ExecutionTraceWidget` — Phase 1+2 (frontend SSE contract + widget mount) shipped pre-checkpoint commit `0a5bc7e05`; Phase 3 BFF emission shipped as DEF-001 (commit 229f30ef9) via `IContextSseRelay` per-request scoped service. Full pipeline now live on master. | ✅ | FULL | false | 089 |
| 096 | 7 | Mount `PinnedMemoryListWidget` in SpaarkeAi shell — shipped pre-checkpoint (added `pinned-memory` ContextToolId + render branch in `ContextPaneController` + export from `@spaarke/ai-widgets`). | ✅ | STANDARD | false | 089 |
| 097a | 8 | Wire `createNewSession` callback to POST `/api/ai/chat/sessions` | ✅ | ~30m | Done 2026-06-21 (commit `869bdd777`) |
| 097b | 8 | Wire `getConversationHistory` to expose SprkChat message-list — shipped pre-checkpoint. Added `onMessagesChange` prop to `ISprkChatProps`; `ConversationPane` snapshots messages into `messagesRef`; `getConversationHistory` returns real chat history for `/export` markdown. | ✅ | FULL | false | 089 |
| 097c | 8 | Wire `getFocusedTabId` via PaneEventBus `tab_change` subscription | ✅ | ~1h | Done 2026-06-21 (commit `869bdd777`) |
| 098 | 9 | Render `AddToAssistantToggle` per-tab in tab header + wire PATCH — shipped pre-checkpoint. `WorkspaceTabManagerComponent` renders the toggle above the active widget; `WorkspacePane` handles PATCH to `/api/ai/chat/sessions/{id}/tabs/{tabId}` with `visibleToAssistant` body. | ✅ | FULL | false | 089 |
| **TIER-C** | 6 | **Diagnose + fix LLM workspace visibility** — RESOLVED 2026-06-26. Diagnostic confirmed C# wiring was correct (`SprkChatAgentFactory` snapshot path intact); root cause was Dataverse data gap: 2 of 4 workspace handler rows (CLOSE-WORKSPACE-TAB + GET-WORKSPACE-TAB-CONTENT) were missing from `Seed-TypedHandlers.ps1`. Added CLOSE-WORKSPACE-TAB seed entry (commit 738b59de6); deployed both missing rows to spaarke-dev via MCP. LLM now has full workspace visibility via the 4 registered handlers. See `notes/tier-c-diagnostic.md`. | ✅ | DIAG | false | n/a |

**Closeout audit total**: ~12-15 hours surface (095, 097b, 098, TIER-C, optionally 096) + ~2 days Builder UI (091, 093) + ~1h cleanup (092).

---

## Parallel Execution Plan (max 6 agents per wave per project-pipeline cap)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| **A-G0** | 001, 006 | none | Phase A starter — both touch different surfaces (Dataverse entity vs C# interface rename); parallel-safe |
| **A-G1** | 002, 003, 004 | 001 | Pillar 1 endpoint + resolver + seed (separate files) |
| **A-G3** | 007, 009 | 006 | Tool registry infra extensions (007 + 008 same DTO — serialize: 008 after 007) |
| **A-G2** | 005 | 002, 003, 004 | Agent factory wiring (sequential gate) |
| **A-G4** | 010 | 007, 008, 009 | Adapter (depends on DTO + context split) |
| **A-G5** | 011 | 010 | Wire ResolveTools to data (sequential) |
| **A-G6** | 012 | 011 | **Q9 BIG-BANG MIGRATION (high-risk single task)** |
| **A-G7** | 013 | 012 | Regression test gate (must pass before downstream) |
| **A-G8** | 020, 024 | 011 | Pillar 3 facade + Pillar 4 FK fix (separate areas, parallel) |
| **A-G9** | 021 | 020 | Generic invoke_playbook tool (sequential) |
| **A-G10** | 022 | 021 | Dynamic playbook list in tool description |
| **A-G11** | 023 | 022 | Specialized bridge removal |
| **A-G12** | 025 | 024 | Orchestrator refactor |
| **A-G13** | 028 | 005, 013, 023, 025 | Phase A integration test (sequential gate) |
| **A-G14** | 029 | 028 | Phase A exit-gate (sign-off pause point per CLAUDE.md) |
| **B-G1** | 030, 031 | 029 | Schema additions (different entities/configs, parallel) |
| **B-G2** | 032, 033, 034, 035 | 030, 031 | 4-action migration (parallel; 034+035 include NFR-07 regression test) |
| **B-G3** | 040 | 032, 033 | Widget array rendering (gates 041) |
| **B-G3'** | 041 | 040 | Widget object rendering (same file as 040, sequential) |
| **B-G4** | 042 | 041, 025 | CapabilityRouter dedup |
| **B-G5** | 048 | 042, 034, 035 | Phase B integration test |
| **B-G6** | 049 | 048 | Phase B exit-gate (sign-off pause) |
| **C-G1** | 050 | 049 | WorkspaceTab interface |
| **C-G2** | 051 | 050 | WorkspaceStateService (Redis + Cosmos) |
| **C-G3** | 052 | 051 | Endpoint |
| **C-G4** | 053 | 052 | Wire into chat factory (gates 6b/6c/7/9) |
| **C-G5** | 054, 055, 056, 059, 060, 064, 065, 071 | 053 | **Big parallel wave (8 tasks, max 6/wave: split into C-G5a + C-G5b)**: C-G5a = 054, 055, 056, 059, 060, 064 (chat tools + events + memory start) ; C-G5b = 065, 071 (memory + visibility) |
| **C-G6** | 057 | 054, 055, 056 | User affordances |
| **C-G7** | 058 | 055 | Conflict resolution |
| **C-G8** | 061 | 059 | Trace widget |
| **C-G9** | 062 | 061 | Register trace widget |
| **C-G10** | 063 | 059 | Emit events from agent + playbook |
| **C-G11** | 066 | 064 | Selective recall |
| **C-G12** | 067 | 064, 065, 066 | Hierarchical composition |
| **C-G13** | 068 | 067 | MatterMemoryService activation |
| **C-G14** | 069 | 065, 068 | Remember/forget/always recognition |
| **C-G15** | 070 | 065, 069 | **Q7: Pinned Memory UI (1–2 week task)** |
| **C-G16** | 072 | 071 | Registry extension |
| **C-G17** | 073 | 072 | Per-widget visible state |
| **C-G18** | 074 | 053, 073 | Per-turn prompt builder |
| **C-G19** | 078 | 057, 058, 062, 063, 070, 074 | Phase C integration test |
| **C-G20** | 079 | 078 | Phase C exit-gate (sign-off pause) |
| **D-G1** | 080, 081, 082, 083 | 079 | Command router parser + 4 components (parallel — separate code areas) |
| **D-G2** | 084, 085, 086 | 081, 082, 083 | Composition test + help UI + NL regression (parallel) |
| **D-G3** | 087 | 084, 085, 086, 029, 049, 079 | Vertical-slice (all pillars) |
| **D-G4** | 088 | 087 | Eval baseline |
| **D-G5** | 089 | 088 | Phase D exit-gate |
| **H-G0** | 100 | 006, 009 | Handler infra gate (can start during Phase A after task 009) |
| **H-G1** | 101, 102, 103, 104 | 100 | Wave 1 deterministic (4 parallel) |
| **H-G2** | 105, 106, 107, 108 | 104 | Wave 2 LLM-assisted (4 parallel) |
| **H-G3** | 109 | 105, 106, 107, 108 | Dispatch tests |
| **END** | 090 | 089 | Wrap-up (mandatory; FULL rigor) |

---

## Critical Path

The longest dependency chain through R6 (15 sequential gates):

```
001 → 002+003+004 (A-G1) → 005 → 006 → 007 → 008 → 009 → 010 → 011 → 012 → 013 → 028 → 029
(Phase A core: persona + tool registry + Q9 batch migration + integration → exit)
→ 030+031 → 032+033+034+035 → 040 → 041 → 042 → 048 → 049
(Phase B: schema + 4-action migration + widget + dedup → exit)
→ 050 → 051 → 052 → 053 → (6a gates) → 070 (Q7 Pinned Memory UI is C-phase critical longest sub-track) → 078 → 079
(Phase C: 6a → 7 longest sub-track → integration → exit)
→ 080 → 081/082/083 → 084 → 087 → 088 → 089 → 090
(Phase D: parser + components + composition + vertical-slice + eval + exit → wrap-up)
```

**Notable**: The Q9 big-bang migration (task 012 + 013 regression gate) is the highest-risk single sequential point in Phase A. Q7 Pinned Memory UI (task 070) is the longest Phase C sub-track.

---

## Pillar Interaction Dependency Matrix

| Pillar | Depends On | Unblocks | Notes |
|--------|------------|----------|-------|
| 1 (persona) | none | 5 wiring (depends on `IScopeResolverService`) | Phase A foundation |
| 2-infra (IToolHandler rename + adapter) | none (006 standalone) | 3 (uses adapter), 6b chat tools (register as analysistool rows), Handler workstream (100) | Phase A foundation |
| 2-migration (Q9 batch) | 2-infra (011) | Phase A exit | Single-task high-risk gate |
| 3 (invoke_playbook) | 2-infra (011), 1 (resolver) | Phase A exit + Pillar 5 (CapabilityRouter dedup) | Facade per ADR-013 |
| 4 (PlaybookExecutionEngine FK) | 2-infra (011) | Phase A exit + Pillar 5 (dedup test) | Data fix + orchestrator refactor |
| 5 (outputSchema/destination/widget/dedup) | Phase A complete | Phase B exit | Q5 re-shape applied |
| 6a (workspace state) | Phase B complete | 6b + 6c + 7 + 9 | Internal Phase C gate |
| 6b (chat tools + affordances + conflict) | 6a (053) | Phase C integration | Parallel with 6c/7/9 |
| 6c (trace widget + events) | 6a (053) | Phase C integration | Parallel with 6b/7/9 ; ADR-015 binding |
| 7 (memory + Q7 UI) | 6a (053) | Phase C integration | Longest C-phase sub-track per Q7 expansion |
| 9 (visibility contract) | 6a (053) | Phase C integration + Pillar 6a prompt builder (074) | Parallel with 6b/6c/7 |
| 8 (command router) | Phase C complete | Phase D vertical-slice (087) | Phase D leading work |
| Vertical-slice (087) | All pillars (Phase A + B + C + D exit gates) | Wrap-up (090) | Per spec §6 |
| Wrap-up (090) | 089 | (project complete) | Mandatory per Step 3.7 |
| 8 typed handlers (parallel workstream) | 006 + 009 (Phase A early infra) | Phase B/C (handlers usable inside playbooks once registered) | Spans Phase A–C |

---

## High-Risk Items

| ID | Risk | Mitigation |
|----|------|------------|
| **012** | Q9 big-bang 10-tool migration — single high-blast-radius task | Comprehensive regression test (013); rollback = git revert + DI flag to reactivate hardcoded classes; staging gate before merge to master |
| **034, 035** | Pre-fill flow regression (NFR-07 binding) | Explicit before/after regression test in each task; hook signatures + 45s timeout + `useAiPrefill` UNCHANGED |
| **042** | CapabilityRouter dedup — must not break existing single-fire flows | Test matrix covers chat-only, workspace-only, both-from-same-intent |
| **051, 052** | Workspace state persistence — schema drift could break 6b/6c/7/9 | 6a interfaces locked before C-G5 wave starts; cross-pillar integration test (078) validates contracts |
| **053** | Wiring workspace state into agent factory — exceeds 8K system prompt budget (NFR-10)? | Token budget tracker (068) accounts for snapshot; per-tab compact `getAgentVisibleState` (Pillar 9) shrinks payload |
| **059, 061, 063** | ADR-015 leak — trace events accidentally log user message text | Constraint cited in all three tasks; data-governance audit at Phase C exit (079) |
| **070** | Q7 scope expansion — calendar risk | Phase B duration monitored; re-defer to R7 if Phase B slips >5 days |
| **087** | Vertical-slice integration test — all-pillar coverage hard to define | Spec §6 acceptance criteria provide concrete checklist |
| **all BFF tasks** | NFR-02 ≤+5 MB R6 budget breach | Per-task verification; running total tracked in this index after each task |

---

## Confirmation Triggers (project-specific; main session must pause)

Per CLAUDE.md "Confirmation Triggers", main session MUST pause + confirm with user when:

- Any task touches an ADR file (`.claude/adr/*.md` or `docs/adr/*.md`)
- Any task proposes a new top-level DI registration (vs existing modules per ADR-010)
- Any task modifies a public contract surface in `Services/Ai/PublicContracts/`
- Any task proposes a schema change to a production Dataverse entity (e.g., 001, 007, 008, 030, 031)
- Any task finds the pre-fill flow needs modification beyond data migration (034, 035)
- Any task touches the 11 production node executors (should be NONE in R6)
- Any wave fails build verification
- Any task surfaces a need for a new ADR (R6 does not allow new ADRs)
- Phase exit gates (029, 049, 079, 089) require user sign-off
- Q9 batch migration task 012 — before kickoff and after regression gate 013

---

## Phase A First Wave Recommendation

When Step 5 begins, dispatch this wave (no dependencies, parallel-safe):

**Wave 1**: Tasks **001** + **006** (2 parallel agents)
- 001 creates `sprk_aipersona` Dataverse entity (foundation for Pillar 1)
- 006 renames `IAnalysisToolHandler` → `IToolHandler` (foundation for Pillar 2 + parallel handler workstream)
- Both touch different surfaces (Dataverse vs C#); fully parallel-safe.
- Build verification: after Wave 1, run `dotnet build src/server/api/Sprk.Bff.Api/` since 006 modifies C#.
- Wave 2 candidates after Wave 1: 002, 003, 004 (Pillar 1 endpoint + resolver + seed — all depend on 001) + 007, 009 (Pillar 2 tool registry extensions — depend on 006) + 100 (handler infra gate, depends on 006 + 009).

---

*Maintained by: project-pipeline at Step 3; updated by task-execute as tasks complete (🔲 → 🔄 → ✅).*
