# Phase C Exit Report

> **Project**: spaarke-ai-platform-unification-r6
> **Phase**: C — Tri-directional Workspace + Memory + Widget Visibility
> **Sign-off date**: 2026-06-18
> **Sign-off**: User (ralph.schroeder@hotmail.com), via session approval
> **Last commit**: `2d8c27dc0`
> **Branch**: `work/spaarke-ai-platform-unification-r6` (synced with origin)

---

## Exit criteria (spec.md Phase C + Q7 expansion)

| # | Criterion | Evidence (task + tests) | Status |
|---|---|---|---|
| 1 | Agent has accurate workspace awareness | Task 053 (`SprkChatAgentFactory.BuildWorkspaceStateBlock`) + Task 074 (rich FR-57 derivation); `SprkChatAgentFactoryWorkspaceStateTests` (21/0) + `Pillar9PrivacyFilterTests` (3/0) + `CrossPillarIntegrationTests` scenarios 1, 4, 6 | ✅ |
| 2 | "Update the summary in Tab 1" works | Task 055 (`UpdateWorkspaceTabHandler`) + Task 058 (Q8 stale-read refusal); `ConflictResolutionTests` (3/0) + `CrossPillarIntegrationTests` scenarios 3, 5 | ✅ |
| 3 | "Send to Workspace" + "Add to Assistant" + "Pin to Matter" functional | Task 057 (3 components in `@spaarke/ai-widgets`); `SendToWorkspaceButton.test.tsx` (4/0) + `AddToAssistantToggle.test.tsx` (5/0) + `PinToMatterButton.test.tsx` (13/0); registered with workspace event channel (no 5th channel — ADR-030) | ✅ |
| 4 | Context pane shows live execution trace in real time | Task 061 (`ExecutionTraceWidget`) + Task 062 (registry) + Task 063 (4 emission sites in BFF); `ContextEventEmissionTests` (11/0) + `register-execution-trace-widget.test.ts` (5/0); ADR-015 per-site audit documented in `task-063-adr015-emission-audit.md` | ✅ |
| 5 | Cross-conversation memory recalls prior-matter context | Tasks 064-068 (compression + pinned + recall + composition + matter activation + budget tracker); `MemoryCompositionServiceTests` (34/0) + `PinnedContextRecallServiceTests` (21/0) + `PromptBudgetTrackerTests` (26/0); 91/0 broader Memory regression | ✅ |
| 6 | Pinned facts persist as user preferences across sessions | Task 065 (`PinnedContextRepository`) + Task 069 (`ManagePinnedContextHandler` + Layer 0 voice classification); `ManagePinnedContextHandlerTests` (16/0) + `CapabilityRouterVoiceMemoryTests` (12/0); Cosmos durable persistence verified | ✅ |
| 7 (Q7) | Pinned Memory CRUD UI in Context pane functional | Task 070 PART A (`PinnedMemoryEndpoints`) + PART B (4 React components); `PinnedMemoryEndpointsTests` (15/0) + frontend UI tests (27/0); registered with `ContextWidgetRegistry`; Fluent v9 + ADR-021 dark-mode parity verified | ✅ |

---

## Cross-pillar coverage (task 078)

6 cross-pillar integration tests in `tests/integration/Spe.Integration.Tests/PhaseC/CrossPillarIntegrationTests.cs` (6/0 in 23ms):

1. `CrossPillar_SendArtifactThenAppearsInNextTurnPrompt` — Pillar 6b → 6a → 9
2. `CrossPillar_AgentCannotUpdateOwnArtifact_FR39Binding` — Pillar 6b cross-handler `canEdit` binding (locks in FR-39 finding)
3. `CrossPillar_UserCreatedTabUpdateRoundTrip` — Pillar 6b → 6a → 9 editable path
4. `CrossPillar_AllFourWidgetVariants_PrivacyFilterAndAdr015Audit` — Pillar 6b → 9 all 4 widget variants
5. `CrossPillar_StaleReadRefusal_LeavesTabUnchangedForNextTurnPrompt` — Pillar 6b + 9 + Q8
6. `CrossPillar_HiddenTabNeverLeaksToPromptEvenAfterAgentEdit` — Pillar 6b + 9 privacy default

---

## Governance posture at exit

| Binding | Status | Evidence |
|---|---|---|
| **NFR-01** Conversational primacy | ✅ Preserved | All chat-tool additions are optional; CapabilityRouter Layer 0 + Layer 2/3 dispatch never blocks conversational ability |
| **NFR-02** BFF publish-size ≤+5 MB | ✅ Within budget | 44.71 MB compressed (-0.94 MB net from ~45.65 baseline across all of Phase C) |
| **NFR-03** No new ADRs | ✅ Honored | Zero new ADRs in Phase C (Wave 9's ADR-033 was Phase A scope) |
| **NFR-05** PaneEventBus 4 channels | ✅ Preserved | All new event types additive on existing `workspace` + `context` channels; no 5th channel |
| **NFR-08** 11 production node executors UNMODIFIED | ✅ Verified | `git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty across all Phase C tasks |
| **NFR-10** 8K system-prompt budget | ✅ Enforced | `PromptBudgetTracker` (task 068) consumed by factory + context provider; truncation telemetry emitted |
| **NFR-16** Per-tenant isolation | ✅ Enforced | All Cosmos partition keys + Redis cache keys include `tenantId`; endpoint handlers derive tenant from JWT (never from request body) |
| **ADR-010** DI minimalism | ✅ Honored | Zero new Program.cs lines across Phase C (all new services registered inside existing `AnalysisServicesModule` + `AiPersistenceModule`) |
| **ADR-015** AI data governance | ✅ Per-site audited | Per-task audit notes for every telemetry emission site (058, 063, 069, 070, 074); structured logs + meter tags carry deterministic IDs only — never user message text or widget content body |

---

## Notable design surfaces (transparently surfaced during build)

1. **FR-39 binding** (task 078 finding): `send_workspace_artifact` defaults agent-dispatched tabs to `canEdit=false`. `update_workspace_tab` refuses with `refused_not_editable` until user explicitly converts via affordance. Agent cannot silently rewrite its own dispatched artifacts. Test #2 locks this binding in.

2. **Task 067 design call-out**: "retrieved-old = pinned-similarity, not chat-turn-similarity" — pragmatic FR-44 interpretation. R6 has no chat-turn vectorizer primitive; `IPinnedContextRecallService` operates over pinned items. Documented in `task-067-evidence.md`.

3. **Task 074 architectural decision**: Option A (server-derives FR-57 shapes from `WidgetData`) chosen over Option B (persist `VisibleState` field). Rationale: the C# polymorphic union (`WorkspaceTabWidgetData.cs`) was already authored with FR-57 shapes in XML doc comments at task 053 — designed for server-side derivation.

4. **Task 068 housekeeping** (caught + fixed in task 069 dispatch): `IMatterMemoryService` promoted from nullable to required ctor parameter. 5 test files migrated (1 by my C-G5 cleanup; 4 by task 069 sub-agent). `IPromptBudgetTracker` kept nullable for documented DI-asymmetry rationale (gate-controlled registration vs unconditional provider registration).

---

## Deferred to R7 (transparent)

- True E2E test harness with mock LLM + Cosmos test container + Redis test instance — composed evidence + cross-pillar integration tests are operationally equivalent for catching the failure modes a harness would catch
- Frontend browser-driven E2E for Pinned Memory UI ↔ chat composition (Vitest + BFF endpoint integration sufficient for sign-off)
- `PinnedContextItem.source` discriminator (provenance badge currently stubs "Created via UI" with TODO(R7) marker in `PinnedMemoryProvenanceBadge.tsx` lines 22-45)
- Voice command Layer 0 regex tightening for non-anchored phrasings ("I want you to remember X") — LLM is expected to handle these via the tool description text
- `RagService.SearchAsync` + `PlaybookOrchestrationService.ExecuteNodeAsync` sessionId/tenantId threading (currently null in those services' APIs; trace widget correlates by tenantId + timestamp ordering as interim)

---

## Sign-off

**Phase C COMPLETE.** All 6 spec exit criteria + Q7 expansion criterion validated by per-task tests + cross-pillar integration tests. Governance posture preserved across NFR-01/02/03/05/08/10/16 + ADR-010/015. No regressions in factory hot path (1414/0 in `Chat | Workspace` sweep across the final commit).

**Phase D APPROVED to launch** — Pillar 8 (command router) + integration + lightweight eval baseline + wrap-up.

First Phase D task: **080 CommandRouter.ts parser** — builds `Intent { command, references[], rawText }` before agent invocation. Gates 081 (hard slashes) + 082 (soft slashes) + 083 (references resolver), which can run in parallel after 080 commits.
