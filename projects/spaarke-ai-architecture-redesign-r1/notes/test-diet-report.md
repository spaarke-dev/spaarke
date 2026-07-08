# Test diet report — spaarke-ai-architecture-redesign-r1

> **Run date**: 2026-07-08
> **Branch**: `work/spaarke-ai-architecture-redesign-r1`
> **Scope**: test files ADDED or MODIFIED between project start `fc4d448c9` (= `git merge-base HEAD origin/master`; branch-creation merge `1d84678a8`) and HEAD
> **Skill**: `/test-diet` per ADR-038 §7 (17-ban build-vs-maintain classifier B1–B17; 6 KEEP path categories) — invoked by task 090 step 3 (CLAUDE.md §7 project-close gate, spec FR-B09)
> **Read-only**: no deletions executed; commands below are for reviewer judgment
> **Precedent followed**: `projects/spaarkeai-compose-r1/notes/test-diet-report.md` — "MAINTAIN (PATH-NOTE)" convention for the established `tests/unit/Sprk.Bff.Api.Tests/**` layout, and jest suites classified by the same build-vs-maintain spirit (ADR-038's strict scope is `tests/**/*.cs`)

---

## Summary

| Class | Count | Action |
|---|---|---|
| **MAINTAIN** — KEEP path `tests/integration/contract/**` (incl. eval suite) | **14 .cs files** (+2 eval data/doc artifacts) | ✅ confirmed at canonical path |
| **MAINTAIN** — PATH-NOTE (established BFF unit layout, added this project) | **30 .cs files** | ✅ kept; behavioral, 0/17 ban hits on scan |
| **MAINTAIN** — pre-existing suites modified to track the redesign | **42 server .cs files** | ✅ kept; edits are rename/deleted-surface/new-behavior tracking |
| **MAINTAIN** — client jest suites (added 17 + modified 21, same spirit) | **38 files** (+2 helpers) | ✅ kept; behavioral component/hook/service tests |
| **SCAFFOLDING** (DELETE candidate, new) | **0** | n/a — no `git rm` emitted for surviving files |
| **AMBIGUOUS** (reviewer judgment) | **2 files** | see table below (both pre-existing, lean-DELETE as completed-migration scaffolding) |
| **Total files reviewed** | **126 test files** (88 server .cs + 38 client) + 5 non-test artifacts | — |
| **Already reconciled by deletion during the project** | **77 files** (49 server + 28 client) | ✅ no action — see final section |

**Verdict**: 124 MAINTAIN / 0 new SCAFFOLDING-delete candidates / 2 AMBIGUOUS. The project's hard-cutover doctrine (NFR-08 grep-zero retirements) meant scaffolding-class tests were deleted **with their subjects** during execution rather than accumulating to project close — the 17-ban scan of the 55 files ADDED by this project found zero B1–B17 signals (`Mock<HttpMessageHandler>`: 0 real uses; `Assert.NotNull(GetRequiredService)` DI-registration asserts: 0; ctor null-check: 0; `BindingFlags.NonPublic`: 0 in added files).

---

## Classification table

### A. MAINTAIN — KEEP path `tests/integration/contract/**` (endpoint-contract + eval)

ADDED by this project (all names follow `{Method}_{Scenario}_{ExpectedResult}`; in-process host, no transport mocks):

| File | Tests | Basis |
|---|---|---|
| `tests/integration/contract/Api/Ai/AnalysisEndpointsExecuteDispatchContractTests.cs` | 3 | endpoint-contract KEEP; FR dispatch surface |
| `tests/integration/contract/Api/Ai/ChatTurnFailedErrorContractTests.cs` | 2 | endpoint-contract KEEP; `turn_failed` SSE error contract |
| `tests/integration/contract/Api/Ai/DailyBriefingEmailEndpointContractTests.cs` | 5 | endpoint-contract KEEP (`GetRequiredService` hits are host fixture config, not B3) |
| `tests/integration/contract/Api/Ai/DispatchSessionEndpointContractTests.cs` | 15 | endpoint-contract KEEP; session dispatch + gate/resume contract |
| `tests/integration/contract/Api/Ai/WorkProductEnvelopePersistenceContractTests.cs` | 2 | endpoint-contract KEEP; ADR-040 storage-precedes-rendering |
| `tests/integration/contract/Catalog/CatalogInputSchemaContractTests.cs` | 4 | contract KEEP; closed-catalog input-schema contract (FR/NFR-06) |
| `tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs` | 15 (35 running cases) | **explicitly KEEP-class**: ADR-038 + spec NFR-02 (eval green = merge gate) / NFR-06; `Category=GoldenUtteranceEval`; B1 grep hits are negation comments |
| `tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs` | 20 | **explicitly KEEP-class**: NFR-03 untrusted-input injection eval |
| `tests/integration/contract/Eval/golden-utterances.json` + `README.md` | — | eval suite data + doc (artifacts, KEEP with suite) |

MODIFIED pre-existing contract files (updated for redesigned surfaces; stay at KEEP path):

| File | Edit shape |
|---|---|
| `tests/integration/contract/Api/Ai/ChatDocumentEndpointsContractTests.cs` | +75 (new cases) |
| `tests/integration/contract/Api/Ai/RateLimitingContractTests.cs` | mechanical (7 ±) |
| `tests/integration/contract/Api/Ai/SummarizeSessionEndpointContractTests.cs` | rewrite for single-hop dispatch (464 ±) |
| `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` | mechanical (2 ±) |
| `tests/integration/contract/Api/Insights/InsightEndpointsContractTests.cs` | 131 ± |
| `tests/integration/contract/Api/Insights/InsightsAssistantEndpointContractTests.cs` | mechanical (9 ±) |

### B. MAINTAIN (PATH-NOTE) — unit tests ADDED by this project (30 files)

Path `tests/unit/Sprk.Bff.Api.Tests/**` is not one of the six strict KEEP paths (`tests/unit/domain/**`), but is the established BFF unit-test layout hosting ~7,400 pre-existing tests; precedent (compose-r1 diet, W9-071 audit) classifies these MAINTAIN with a path note, no `git mv` recommended (a bulk move is repo-wide policy work, not a project-close action). Scan: 0/17 ban signals in every file; all names carry scenario+expected.

| File | Tests | Behavior protected |
|---|---|---|
| `Infrastructure/DI/AnalysisServicesModuleGatingTests.cs` | 6 | **Note**: despite the `DI/` folder, NOT a B3 DI-registration test — asserts ADR-032 Null-Object peers fail fast with stable 503 error codes when the compound gate is off (behavioral kill-switch contract) |
| `Services/Ai/Chat/AgentTurnLoopContractTests.cs` | 29 | agent turn-loop invariants (FR-P2 loop) |
| `Services/Ai/Chat/ChatSessionLedgerRoundTripTests.cs` | 4 | ADR-040 ledger round-trip |
| `Services/Ai/Chat/ConfirmationGateUnificationTests.cs` | 14 | ONE-gate side-effect classing (spec MUST) |
| `Services/Ai/Chat/LoopElicitationTests.cs` | 17 | elicitation/loop behavior |
| `Services/Ai/Chat/OpenAiFunctionSchemaValidatorTests.cs` | 19 | function-schema validation (closed catalog) |
| `Services/Ai/Chat/RefusalCapabilityToolTests.cs` | 13 | honest-refusal capability (task 033) |
| `Services/Ai/Chat/SessionDispatchManifestProbeTests.cs` | 4 | manifest probe/degrade semantics (bounded retry behavior) |
| `Services/Ai/Chat/SprkChatAgentFactoryInvalidSchemaProjectionTests.cs` | 6 | invalid-schema projection behavior |
| `Services/Ai/Chat/TypedHandlerResumeExecutorTests.cs` | 12 | gate/resume executor |
| `Services/Ai/EngineOutputLedgerAdapterTests.cs` | 6 | frozen-engine → ledger adapter |
| `Services/Ai/EventRules/EventPathUserStateTests.cs` | 5 | Event path user state |
| `Services/Ai/EventRules/EventRulesServiceTests.cs` | 24 | Event entry-path rules |
| `Services/Ai/Handlers/AnalysisExecutionHandlerTests.cs` | 22 | tool handler behavior |
| `Services/Ai/Handlers/Dataverse{Create,Delete,Describe,ReadQuery,SearchData,Update}RecordHandlerTests.cs` (6 files) | 18/8/13/18/15/9 | Dataverse tool handlers — user-OBO + side-effect classing |
| `Services/Ai/Handlers/DataverseToolNameFreezeTests.cs` | 11 | tool-name freeze = closed-catalog contract (ADR-039), not B13 |
| `Services/Ai/Handlers/EmailDraftToolHandlerTests.cs` | 14 | draft-correspondence DRAFT-only contract |
| `Services/Ai/Narrators/DailyBriefingCompositeServiceTests.cs` | 10 | coded-workflow composite |
| `Services/Ai/OutputRouterTests.cs` | 14 | output routing (T-07 fills) |
| `Services/Ai/PublicContracts/ConsumerRoutingServiceBindingContractTests.cs` | 21 | Binding-table routing (single source of routing config) |
| `Services/Ai/PublicContracts/ConsumerRoutingServiceEventBindingsTests.cs` | 5 | Event bindings |
| `Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheckTests.cs` | 18 | FR-P0-04 orphan-handler boot check |
| `Services/Ai/Telemetry/AiMeteringTelemetryTests.cs` | 12 | NFR-07 identifiers-only metering |
| `Services/Ai/Workflows/CodedWorkflowConventionTests.cs` | 3 | `sprk_workflowclass` class-ref resolution convention (behavioral end-to-end, incl. Null-peer path) |
| `Services/Ai/WorkProductRecordPersisterTests.cs` | 8 | work-product persistence |

### C. MAINTAIN — pre-existing suites MODIFIED to track the redesign (42 server files)

Edits are (a) mechanical rename/namespace tracking, (b) removal of test methods whose subjects were deleted (Track B), or (c) new behavioral cases on surviving surfaces. Modification does not change their inherited classification; none acquired ban patterns.

- **`tests/integration/Spe.Integration.Tests/`** (6 files: `AnalysisEndpointsIntegrationTests`, `Api/Ai/ChatEndpointsTests`, `Api/Ai/KnowledgeBaseEndpointsTests`, `Api/Ai/ReAnalysisFlowTests`, `AuthorizationIntegrationTests`, `ToolFrameworkIntegrationTests`) — fixture edits only (mock-DI lines for deleted interfaces removed by task 050; 6–16 ± each).
- **`tests/unit/Sprk.Bff.Api.Tests/`** (36 files) — `Api/Ai/ChatRefineEndpointTests`, `Api/Ai/DailyBriefingEndpointsTests` (−472: cases for deleted narrate flag removed per FR-P3-04), `Api/Ai/DailyBriefingResponseShapeTests`, `Api/Workspace/WorkspaceFileEndpointsTests`, `Models/Ai/Chat/EntityTypeNormalizerTests`, `Services/Ai/AnalysisOrchestrationServiceTests`, `Services/Ai/AnalysisToolDtoTests`, `Services/Ai/AppOnlyAnalysisServiceResolveTests`, `Services/Ai/Chat/ChatHistoryManagerTests` (+249 new behavior), `Services/Ai/Chat/Middleware/{AgentMiddleware,SafetyPipelineMiddleware}Tests`, `Services/Ai/Chat/PlaybookChatContextProvider{Enrichment,EnrichmentIntegration,EntityNameLazyFetch}Tests`, `Services/Ai/Chat/SprkChatAgent{FactoryDedup,Factory,FactoryToolResolution,''}Tests` (4), `Services/Ai/Chat/ToolHandlerToAIFunctionAdapterTests`, `Services/Ai/EmailAnalysisIntegrationTests`, `Services/Ai/Handlers/{CodeInterpreter,DocumentSearch,KnowledgeRetrieval,LegalResearch}HandlerTests`, `Services/Ai/Handlers/SendWorkspaceArtifactHandlerTests` (+272: G-P3 R2-D `widgetType:'Workspace'` leg), `Services/Ai/Insights/{AssistantToolCallHandlerCitationHref,CacheTtlPerTopic,InsightsOrchestrator}Tests`, `Services/Ai/Insights/Playbooks/PredictMatterCostPlaybookTests`, `Services/Ai/Nodes/SummarizeWorkspaceMultinodeMigrationTests`, `Services/Ai/PlaybookOrchestrationServiceSectionStreamingTests`, `Services/Ai/Safety/{ConversationHistorySanitizer,VerifyCitations}Tests`, `Services/Workspace/{MatterPreFillService,ProjectPreFillService,WorkspaceAiService}Tests`.

Non-test artifacts touched (no classification): `tests/.reliability-registry.json` (flake registry), `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`, `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Cache/InMemoryTenantCache.cs` (test double/helper, 2 ±).

### D. MAINTAIN — client jest suites (classified by the same build-vs-maintain spirit)

ADDED (17 — all behavioral component/hook/service tests, no snapshot-of-trivial (B12), no pass-through (B9)):

| File | it() |
|---|---|
| `src/client/code-pages/PlaybookBuilder/src/components/catalog/__tests__/{ActionEditorForm,BindingEditorForm,CatalogEditorShell}.test.tsx` | 6/8/3 — task 053 BA catalog editor |
| `src/client/code-pages/PlaybookBuilder/src/services/__tests__/{catalogService,schemaValidation}.test.ts` | 14/21 — mirror-first input-schema validation |
| `src/client/pcf/ScopeConfigEditor/ScopeConfigEditor/__tests__/BindingConfigEditor.test.tsx` | 14 — Binding editor |
| `src/client/shared/Spaarke.AI.Widgets/src/registry/__tests__/register-context-widgets.test.ts` | 6 — widget registry (replaces deleted R1 registry test) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/useActionHandlers.gateResolve.test.ts` | 6 — gate resolve |
| `src/client/shared/Spaarke.UI.Components/src/services/__tests__/dispatchConsumer.test.ts` | 24 — canonical dispatch client (NFR-08 header documents deleted predecessors) |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConsumerChips.test.tsx` + `ConversationPane.{consumer-chips,event-path,new-session}.test.tsx` | 8/6/17/3 — Click/Event entry paths |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/DocumentUploadedEventStream.test.ts` | 16 — Event path SSE |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/useContextEventBridge.{tool-chain,workspace-open-tab}.test.ts` | 3/4 — ledger tool_chain frame + tab open |
| `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspacePane.tab-restore-race.test.tsx` | 2 — race regression |

MODIFIED (21 + 1 helper `Spaarke.AI.Outputs/src/__tests__/test-utils.tsx`): `AnalysisWorkspace/streaming-e2e.test.ts`, `PlaybookBuilder/adr021-dark-mode-compliance.test.ts`, `ScopeConfigEditorApp.test.tsx`, `AI.Outputs/no-hardcoded-colors.test.ts` (convention guards — kept), `AI.Widgets/widget-serialize-restore.test.ts`, `ExecutionTraceWidget.test.tsx` (rewritten for ledger frames), `StructuredOutputStreamWidget.{'',sections,integration.dispatchSummarizeOnly}.test.tsx` (3, rewritten for section-keyed streaming per amended ADR-037), `WorkspaceLayoutWidget.test.tsx`, `SprkChat/{SprkChat,useChatFileAttachment,useSseStream}.test.*` (canonical SSE parse path, FR-P3-06), `SpaarkeAi CommandRouter.test.ts`, `ConversationPane.{playbook-options,r5}.test.tsx`, `AddToAssistantToggle.test.tsx`, `WorkspacePane.summary-tab.test.tsx`, `WorkspaceTabManager.test.ts`, `WorkspaceTabManagerComponent.hideTabBar.test.tsx`. All MAINTAIN.

---

## Ambiguous — reviewer judgment (2)

| File | Ambiguity | Suggestion |
|---|---|---|
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Phase1StableIdMigrationSuite.cs` (396 lines, ~12 tests; ADDED by `chat-routing-redesign-r1`, MODIFIED here 44 ± to track deleted consumers) | Reflection asserts that migrated consumers structurally depend on `IPlaybookLookupService` (ctor param + private field) + source-string inspection for `/by-id/` URLs — B6 mirror / B8-adjacent shape guarding a **completed** migration ("delete the scaffolding once the building stands"). Not under a strict KEEP path (`Sprk.Bff.Api.IntegrationTests`, not one of the 6). But it self-describes as the by-name→by-id **regression gate**, and by-name resolution must never return. | **Lean DELETE** (whole file) in r2, or convert the 2–3 genuinely behavioral asserts (Consumer08 no-by-name, frontend by-id URL guard) into a slim regression test under `tests/integration/regression/**`. Command staged below — reviewer decides; not counted as a SCAFFOLDING candidate of THIS project since it predates the branch. |
| `tests/unit/Sprk.Bff.Api.Tests/Integration/PhaseAVerticalSliceTests.cs` (254 lines, 6 tests; ADDED by r6 task 028, TRIMMED here −196 by task 044 when Pillar 3/4 subjects died) | Registry-containment / resolvability assertions through the real DI graph (`Pillar2_ToolHandlerRegistry_ContainsR6MigratedHandlers`, `NFR08_NodeExecutorRegistry_ExposesProductionExecutors`) — B3-adjacent, but ADR-038 sanctions NetArchTest-style architecture-contract tests as the B3 **replacement**, and compose-r1 diet precedent cleared this pattern. Phase A exit gate long passed. | Reviewer call: KEEP as architecture-contract test (precedent-consistent) or fold surviving asserts into `RoutingConsumerTypeHealthCheckTests` (which now owns the orphan-handler boot contract) and delete. Default: KEEP. |

---

## Delete commands (DO NOT auto-execute — reviewer judgment required)

No SCAFFOLDING-class files were introduced by this project; nothing is required to close the gate. The only staged command is the AMBIGUOUS lean-DELETE candidate (pre-existing file, reviewer discretion, r2-acceptable):

```bash
# AMBIGUOUS lean-DELETE — completed-migration structural suite (predates this project).
# If deleting, first port Consumer08_ChatContextMappingService_HasNoByNameResolution and the
# frontend /by-id/ URL guards to tests/integration/regression/** in the SAME PR.
git rm tests/integration/Sprk.Bff.Api.IntegrationTests/Phase1StableIdMigrationSuite.cs
```

## Path-move commands

None emitted. `tests/unit/Sprk.Bff.Api.Tests/**` is the established BFF unit layout (precedent: compose-r1 diet report, W9-071 audit); a bulk migration to `tests/unit/domain/**` is repo-wide policy work outside a project-close diet.

---

## Already reconciled by deletion during the project (no action)

Per the hard-cutover doctrine (NFR-08), tests of deleted surfaces were removed **with their subjects** during execution — cited in [`notes/track-b-completion-audit.md`](track-b-completion-audit.md) (§12 ADR-038 test register + §§1–7 grep-zero evidence). 49 server + 28 client test files deleted, all SCAFFOLDING-class relative to the surviving architecture (tests of dead code):

- **Task 050 (§12 register, 9 files)**: `AiPlaybookBuilderServiceTests`, `Builder/ConfigurePromptSchemaTests`, `ClarificationFlowTests`, `ExecutorConfigSchemasEndpointTests`, `EntityResolutionServiceTests`, `Chat/SessionCleanupSecurityTests`, `Infrastructure/Streaming/ServerSentEventWriterTests`, `Models/Ai/BuilderSseEventsTests`, `Models/Ai/AnalysisChunkFieldDeltaTests`.
- **Tasks 034–036 (dispatcher/intent/tools estate)**: `PlaybookDispatcher*Tests` (7), `IntentRerankerServiceTests`, `PlaybookCandidateSelectorTests`, `PlaybookOptionsEventBuilderTests`, `PlaybookOutputHandler*Tests` (4), `Chat/Tools/*Tests` (6), `ChatIntentHintRoundTripTests`, `PlaybookEmbedding/*Tests` (4), `DirectOpenAiAgentTests`.
- **Task 044 (engine shells / F-1 legs)**: `PlaybookExecutionEngineTests`, `SessionSummarizeOrchestrator*` (2), `InvokePlaybookHandlerTests`, `AnalysisQueryHandlerTests`, `WorkingDocumentHandlerTests`, `InvokePlaybookAiTests`, `PlaybookDispatchExecuteRequestTests`, `DispatchResultTests`, `R5SummarizeTelemetryTests`, `InvokePlaybookDescriptionTests`, `StreamingWriteIntegrationTests`, `ChatSessionPlanEndpointTests`, `AnalysisChunkFieldDeltaTests` (also §12), `BuilderSseEventsTests` (also §12), `WorkspaceOptions{,Validator}Tests`.
- **Client (tasks 023/034/045/053/071/072)**: PlaybookBuilder canvas suite (10 incl. `canvasStore`, `canvasValidation`, `TypedConfigForm*`, `testUtils`), AI.Outputs R1 widgets (4: Chart/DataTable/Timeline/DocumentCompare), old `register-context-widgets` location, SpaarkeAi intent/insights estate (13: `intentMatcher`, `executeSummarizeIntent`, `SoftSlashRouter`, `natural-language-regression`, `composition.integration`, `ConversationPane.slash-nl-rewire`, `sseToPaneEventBridge`, `InsightsResponseRenderer*` (2), `LowConfidenceBadge`, `insightsQueryClient`, `PinToMatterButton`, `SendToWorkspaceButton`).

Verification context at close (from track-b audit §9): BFF unit suite 7,444 passed / 4 known failures (all pre-existing, unrelated); eval suite **35/35 green**.

## Count delta

- Server test files added: 38 (.cs) — all MAINTAIN
- Server test files modified (surviving): 50 — 48 MAINTAIN + 2 AMBIGUOUS
- Client test files added/modified: 17 + 21 — all MAINTAIN
- SCAFFOLDING deleted during project (with subjects): 77 files
- New scaffolding surviving at close: **0**
- Net post-diet expected count: unchanged (0 reviewer-mandatory deletions)

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding once the building stands"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17; 6 KEEP path categories; eval suite KEEP-class per ADR-038 + spec NFR-02/NFR-06.
