# Track-B Completion Audit — task 050 (FR-P4-01)

> **Executed**: 2026-07-07 · **Rigor**: STANDARD (audit) + FULL (Scope-B code-deletion appendix)
> **Method**: every inventory-§9 item + every OVERLAY-MATRIX DEL verdict + every phase-coupled FR retirement symbol + every wave-accumulated candidate was FRESH-grepped at audit time (`git grep` over `src tests scripts docs infra config .github .claude`, excluding `projects/` audit notes, `.claude/archive`, `knowledge/`). Batch evidence (070–073) cited but never substituted for a fresh grep.
> **Hit classification**: `0 hits` = grep-zero. Non-zero hits are classified per row as **deletion-note** (intentional removal annotation — the established project convention, e.g. `AI-ARCHITECTURE.md` "Deleted estates" line), **historical** (ADR/assessment/researcher-memory describing the past), **doc-drift** (doc presents deleted item as live — registered in §10), or **live** (keep-with-reason).
> **G-P4 criterion**: zero unexplained survivors — **MET**. Every row below is grep-verified-deleted, retired-as-data with old→new evidence, or carries a written keep-with-reason / operator-decision.

## 0. Summary

| Metric | Count |
|---|---|
| Rows audited | 62 |
| Grep-verified DELETED (incl. 39 files deleted by this task) | 44 |
| RETIRE-data executed this task (Dataverse rows, old→new shown) | 15 rows (1 playbook + 4 tools + 10 knowledge) |
| KEEP-with-reason | 9 |
| OPERATOR-decision (registered, not improvised) | 5 |
| Unexplained survivors | **0** |

---

## 1. Inventory §9 — Server

| Item | Responsible task | Status | Evidence (fresh grep 2026-07-07) |
|---|---|---|---|
| `DirectOpenAiAgent` + `ISprkAgent` + `AgentRequest` + `ConversationTurn` + `AgentRole` | 070 | **DELETED** | `\bDirectOpenAiAgent\b: 0` · `\bISprkAgent\b: 0` · `\bAgentRequest\b: 0` · `\bConversationTurn\b: 0` · `\bAgentRole\b: 0` |
| `SseEvent.cs` (agent DTO) | 070 → 050 | **KEEP-with-reason** | Live compile-time consumer `SseOutputGuard.ValidateAndFallback` (`SseOutputGuard.cs:56,62,75`), DI-registered `AiSafetyModule.cs:117`, own test suite. Overlay matrix S7/T-07 verdict = ✅ fills (Output Router). **Flag**: no production *injection* site of `SseOutputGuard` observed (DI + tests only) — operator may retire the pair in r2; retiring now would contradict the ratified matrix ✅. |
| legacy `Chat/Tools/*` (11 classes) | 036 (FR-P2-07) | **DELETED** | dir `Services/Ai/Chat/Tools` **ABSENT**; remaining 5 hits = ADR-033 historical (2), assessment (1), warning-analysis note (1), git-history pointer comment in `AnalysisServicesModule.cs:927` (1) — all historical class |
| `SummarizeInvocationPath.AgentTool` | 070 (whole enum died with `SessionSummarizeOrchestrator` at 044) | **DELETED** | `\bSummarizeInvocationPath\b: 0` |
| `PlaybookDispatcher.RunPhaseBManifestPresentAsync` | 070 (kept) → 035 (file deleted) | **DELETED** | `RunPhaseBManifestPresentAsync: 0` |
| `ScopeGapDetector` | 070 | **DELETED** | code grep 0; 1 doc-drift hit `docs/architecture/scope-architecture.md:28` (presents as live) → §10 |
| `LoadKnowledgeNodeExecutor` | 073 | **KEEP-with-reason** | WIRED to frozen engine: DI `AnalysisServicesModule.cs:1154-1161` (`ExecutorType.LoadKnowledge=142`), Wave-11 template-context integration (`PlaybookTemplateContextBuilder`). Retires with the engine (attrition, OQ-2). |
| `FallbackScopeCatalog` / `FallbackPrompts` | 073 (keep — Builder live) → **050 Scope B** | **DELETED this task** | sole consumers were the AiPlaybookBuilder estate (deleted §9-appendix below); `FallbackScopeCatalog|FallbackPrompts: 0 hits` |
| `LinearConsumersOptions.ActionIds` residue | 070 / 040 | **DELETED** | `\bLinearConsumersOptions\b: 0`; `LinearConsumers:` → 3 explicitly past-tense code/test comments (historical annotations, e.g. `ConsumerTypes.cs:103` "…reverse-lookup config too:", contract-test header "was deleted") |
| `DocumentVectorBackfillService` | 070 | **DELETED** | code grep 0; 7 doc hits — 4 present-as-live (`background-workers-architecture.md:71,105,140`, `sdap-overview.md:287`) → §10; rest historical (ADR-036, assessment) |
| `CompoundIntentDetector` (dead assignment → whole file) | 070 lines / 035 file | **DELETED** | code grep 0; 16 hits = deletion-notes (`AI-ARCHITECTURE.md:33,430`, canonical §7), ADR-039 failure-mode narrative, agent-framework assessment (historical) |

### §1-appendix — Scope B executed by THIS task (task 053's deferred server leg, FR-P4-04)

**Deleted (33 files, `git rm`)**: `Api/Ai/AiPlaybookBuilderEndpoints.cs` (11 endpoints under `/api/ai/playbook-builder/*` incl. `/process`, `/agentic`, `/test-execution*`, `/executor-config-schemas`) · `Api/Admin/BuilderScopeAdminEndpoints.cs` (3 routes `/api/admin/builder-scopes/*`) · `Services/Ai/{AiPlaybookBuilderService,IAiPlaybookBuilderService,FallbackPrompts,FallbackScopeCatalog,EntityResolutionService,SessionState}.cs` · `Services/Ai/Builder/` (7 files: AiBuilderErrors, BuilderAgentService, BuilderAgentSystemPrompt, BuilderScopeImporter, BuilderToolCall, BuilderToolDefinitions, BuilderToolExecutor) · `Services/Ai/Testing/` (5 files: MockDataGenerator, MockTestExecutor, QuickTestExecutor, ProductionTestExecutor, TempBlobStorageService) · `Models/Ai/{AiIntentClassificationSchema,BuildPlanModels,BuilderSseEvents,TestPlaybookModels,EntityResolutionModels}.cs` · `Infrastructure/Streaming/ServerSentEventWriter.cs` · `builder-scopes/` (9 JSON) · **tests**: `AiPlaybookBuilderServiceTests`, `Builder/ConfigurePromptSchemaTests`, `ClarificationFlowTests`, `ExecutorConfigSchemasEndpointTests` (was on the KNOWN-failing list — dies with subject), `EntityResolutionServiceTests`, `Chat/SessionCleanupSecurityTests` (tested TempBlobStorageService), `Infrastructure/Streaming/ServerSentEventWriterTests`, `Models/Ai/BuilderSseEventsTests`.

**Dependent edits**: `AnalysisServicesModule.cs` (AddBuilderServices + AddTestingServices removed; `IModelSelector` registration retained — `ModelSelectorOptions` is a live config surface for `ClauseAnalyzerHandler`) · `EndpointMappingExtensions.cs` (2 mapping calls removed; **live `/playbook-builder/` SPA static hosting + fallback KEPT** — it serves the task-053 BA catalog editor from `wwwroot/playbook-builder`) · `Sprk.Bff.Api.csproj` (builder-scopes Content group) · **auth**: `BuilderAdminApiKey` scheme + `BuilderAdminApiKey`/`BuilderAdminOrOAuth` policies removed from `AuthorizationModule.cs`/`AuthSchemes.cs`/`AuthPolicies.cs` (sole consumer was the deleted admin endpoints; an orphaned ambient API-key scheme is attack surface — `RagApiKey` + `ApiKeyAuthenticationHandler` untouched; **🔔 auth-touching change — reviewer sign-off requested**) · 5 integration-test fixtures (20 mock-DI lines for deleted interfaces) · 2 stale-comment scrubs (`node-routing-config.schema.json`, `AnalysisChunkFieldDeltaTests` before its own deletion).

**Justification (CLAUDE.md §11)**: zero client callers — `git grep "playbook-builder" -- src/client src/solutions` returns only the code page's own `package.json` name; task 053 deleted the canvas caller grep-zero, and the BA catalog editor saves via Dataverse Web API directly (task 053 decision-criteria citation). ADR-032 note: endpoint mapping AND service registrations both lived inside the same `DocumentIntelligence:Enabled && Analysis:Enabled` compound gate — symmetric conditional deleted together, so **no Null-Object peers are required** (nothing asymmetric remains; §F.1 static scan clean by construction).

**Grep-zero (SHOWN, post-deletion)**:
```
AiPlaybookBuilderService: 0    IAiPlaybookBuilderService: 0    playbook-builder/process: 0
BuilderAgentService: 0         BuilderToolExecutor: 0          BuilderToolDefinitions: 0 (after schema-comment scrub)
BuilderScopeImporter: 0        BuilderAgentSystemPrompt: 0     BuilderScopeIds: 0
FallbackScopeCatalog: 0        FallbackPrompts: 0              IEntityResolutionService: 0
\bEntityResolutionService\b: 0 BuilderSseEvent: 0              AiIntentResult: 0
\bBuildPlan\b: 0               TestPlaybookRequest: 0          IMockTestExecutor: 0
IQuickTestExecutor: 0          IProductionTestExecutor: 0      ITempBlobStorageService: 0
MockDataGenerator: 0           ServerSentEventWriter: 0        useEntityResolver / EntityResolutionResult: 0*
BuilderAdminApiKey / BuilderAdminOrOAuth / builder-scopes: removal-note comments only
```
\* one intentional deletion-note in `Spaarke.AI.Context/src/index.ts` / providers removal note.

**Kept from the estate (with reason)**: `Models/Ai/CanvasLayoutDto.cs` (live TL consumers: `PlaybookService`/`NodeService` canvas-layout persistence for frozen Insights composites) · `IModelSelector`/`ModelSelector` (options-backed; live config surface; zero injection sites post-estate → r2 trim candidate, registered §11) · `wwwroot/playbook-builder/**` + SPA fallback (live hosting of the BA catalog editor; stale sourcemaps in `assets/` are ADR-029/task-055 territory) · `INodeExecutor.GetConfigSchema()` (frozen-engine interface member; only its serving endpoint died).

---

## 2. Inventory §9 — Client shared

| Item | Task | Status | Evidence |
|---|---|---|---|
| `StandaloneAiProvider` + `useStandaloneAi` (+ `StandaloneAiContext`, standalone types) | 072 | **DELETED** | code grep 0; 7 doc hits — `chat-architecture.md:152` + `spaarkeai-launch-points.md` (×6) present-as-live → §10 |
| AI.Outputs R1 `output-registry` + `source-registry` | 072 | **DELETED** | `outputWidgetRegistry|sourceWidgetRegistry|resolveOutputWidget|resolveSourceWidget: 0`; dir `src/registry` ABSENT |
| 4 unregistered AI.Outputs widgets (Chart/DataTable/Timeline/DocumentCompare) | 072 | **DELETED** | `\bChartWidget\b|\bDataTableWidget\b|\bTimelineWidget\b|\bDocumentCompareWidget\b: 0` |
| AI.Outputs `cross-pane/` (CustomEvent mechanism) | 072 | **DELETED** | all 5 exported symbols: 0; dir ABSENT |
| `SprkChatExportWord` | 072 | **DELETED** | `SprkChatExportWord: 0` |
| `SprkChatBridge` | 072 (keep) | **KEEP-with-reason** | LIVE: SprkChat public API (`types.ts:717` bridge prop), `RichTextEditor/useDocumentStreamConsumer`, AnalysisWorkspace code page (`App.tsx:403-408`), `useSseStream` forwarding; 441 hits. G-P3 048 ruling analog (SseClient keep). Retirement = pane-communication cutover (r2), coupled to DocumentStreamEvent row §8. |
| `useEntityResolver` (batch-3 orphan flag) | 072 flag → **050** | **DELETED this task** | orphaned when the R1 provider died (sole code importer); deleted `providers/useEntityResolver.ts` + emptied `providers/` barrel + `EntityResolutionResult` type + export; comment refs scrubbed in `main.tsx`, `AiSessionProvider.tsx`, `EntityTypeNormalizer(.cs/Tests)`. Grep: 0 (1 deletion-note). AI.Context `tsc` EXIT 0; AI.Widgets `tsc` EXIT 0. |

## 3. Inventory §9 — SpaarkeAi page

| Item | Task | Status | Evidence |
|---|---|---|---|
| Insights renderer cluster (17 files + tests) | 071 | **DELETED** | `InsightsResponseRenderer|RagResponseRenderer|PlaybookResponseRenderer|DeclineResponseRenderer|InsightsErrorRenderer|LowConfidenceBadge|insightsQueryClient|insightsRendererConfig: 0`; dir ABSENT |
| `notificationContextLoader.ts` | 071 | **DELETED** | `notificationContextLoader|loadSpaarkeAiNotificationContext: 0` |
| `SendToWorkspaceButton` / `PinToMatterButton` | 071 | **DELETED** | 0 / 0 |
| `AddToAssistantToggle` (Pillar-6b trio 3rd member) | 071 | **KEEP-with-reason** | inventory stale — LIVE via R6 Pillar 9 (`WorkspaceTabManagerComponent.tsx:44,779`); 27 hits all live code/docs; documented in `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` §3.6 |
| `ChatHistoryPanel.tsx` (SpaarkeAi wrapper) | 071 | **DELETED** | 0 hits in `src/solutions/SpaarkeAi`; 18 remaining hits = the DIFFERENT live shared-lib `@spaarke/ai-outputs` `ChatHistoryPanel` (intentional same-name distinct component) + 1 `.claude` audit-history file |
| Vestigial WorkspacePane/ConversationPane refs | 071 | **DELETED** | per batch-2 §1 row 5; ConversationPane later decomposed at 045 (3,172→300 lines) |

## 4. Inventory §9 — PCF

| Item | Task | Status | Evidence |
|---|---|---|---|
| `AIMetadataExtractor` (empty dir) | 072 | **DELETED** | dir ABSENT; 6 doc hits — `client-resources-inventory.md` says "Delete folder" (consistent); `sdap-pcf-patterns.md:36`, `REPOSITORY-NAVIGATION-GUIDE.md:49`, `testing-and-code-quality.md:1065` list it as an existing control → §10 doc-drift |
| `AnalysisWorkspace` / `AnalysisBuilder` / `PlaybookBuilderHost` / `DrillThroughWorkspace` PCF dirs | 072 | **ALREADY-ABSENT** (never git-tracked) | dirs ABSENT; same-name survivors intentional (live `code-pages/AnalysisWorkspace`, `PlaybookLibrary` compat, launcher fns); doc hits: deployment-guide `sed` recipes are operational for legacy zips (acceptable); `ai-assistant-theming.md:243-246` links PCF paths as live → §10 |

## 5. Inventory §5.4 — Manifest/scripts stale list

| Item | Task | Status | Evidence |
|---|---|---|---|
| 2026-02 ERD docs (×2) | 073 (deleted) + 052 (replacements) | **DELETED** | fresh grep: 1 hit = `docs/data-model/INDEX.md:42` strikethrough deletion note (intentional); 052 authored 3 new + 1 refreshed data-model docs |
| `scope-model-index.json` docs/ai-knowledge twin | pre-existing commit `fb043944c` + 051 regen | **ALREADY-DELETED** | exactly ONE copy at `.claude/catalogs/scope-model-index.json`, regenerated by 051 with live GUIDs |
| `scripts/seed-data/{actions,playbooks}.json` (R4 taxonomy) | 073 → 051/050 | **KEEP-with-reason** | still load-bearing in the documented demo-bootstrap chain (`Deploy-Actions.ps1:17`, `Deploy-Playbooks.ps1:21/63`, `Deploy-All-AI-SeedData.ps1`, `Load-DemoSampleData.ps1:448`, cited by 2 deployment guides + `scripts/README.md`). Correct fix = regeneration from deployed R7 taxonomy (catalog-governance follow-on), not deletion. → operator register §11 |
| `scripts/Seed-JpsActions.ps1` | 051 (**RETIRED**) | **DELETED** | `ls scripts/Seed-JpsActions.ps1` → ABSENT; `.claude` skills updated by 051 with retirement notes. Residuals: `.claude/skills/jps-action-create/SKILL.md:271` stale step ("Add to Seed-JpsActions.ps1") — **MAIN SESSION** (sub-agent write boundary); `docs/guides/ai-guide-playbook-deploy-recipe.md:36,158,224` + `JPS-AUTHORING-GUIDE.md:625,1217` + `ai-architecture-actions-nodes-scopes.md:220` present-as-live → §10 |
| `docs/data-model/INDEX.md` omission (sprk-playbookconsumer) | 052 | **FIXED** | 052 INDEX reconciliation, zero dead links |
| `infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json` blocked-undeployed | 051 | **KEEP-with-note** | `sprk_nodetype` gap ruled OBSOLETE by 051 (schema evolved to `sprk_executortype` incl. DeliverComposite); file carries a stale `PlaybookDispatcher` `$comment` (dispatcher deleted 035) — TL data file, annotate-only |
| No standalone JPS schema / no `sprk_playbooknode` doc | 052 | **FIXED** | 052 data-model docs live-schema-verified |

## 6. Overlay-matrix DEL verdicts (not already covered above)

| DEL item | Task | Status | Evidence |
|---|---|---|---|
| `LinearConsumersOptions` maps / `Workspace.*PlaybookId` / `Insights.Playbooks.Map` config | 040 (FR-P3-01) | **DELETED** | `\bLinearConsumersOptions\b: 0`; `Insights:Playbooks`: 11 hits all deletion-notes/guides describing the FR-P3-01 cutover; `Workspace:*PlaybookId`: src hits ZERO — remaining hits are one-shot `scripts/Migrate-*PrefillActionOutputSchema.ps1` + `infra` schema `$comment`s narrating the pre-040 world (historical migration scripts; noted §10-lite) |
| `SessionSummarizeOrchestrator` (+Null) | 044 | **DELETED** | code grep 0; 7 doc hits — `ai-architecture-playbook-consumer-routing.md:249,387` present-as-live → §10; rest carry FROZEN-ENGINE header notes (052) or are ADR-039 deletion-notes |
| `PlaybookExecutionEngine` | 044 | **DELETED** | code grep 0; 26 doc hits — headers carry 052 terminology/deletion notes (`INSIGHTS-ENGINE-ARCHITECTURE.md:4`, `ai-architecture-playbook-runtime.md:3`); residual dead link `INSIGHTS-ENGINE-ARCHITECTURE.md:2226` → §10 |
| `AnalysisOrchestrationService` legacy path | R7 FR-11 (partial) | **KEEP-with-reason / OPERATOR** | R7 removed the legacy direct-invocation entry (file header `:82`); a deprecated no-nodes fallback branch REMAINS live-reachable (`AnalysisOrchestrationService.cs:775-779` "DEPRECATED Legacy mode: No nodes found…"). Full removal requires proving zero node-less playbooks in env data — non-trivial, NOT an improvised deletion. → §11 operator register |
| `/actions/{id}/confirm` HITL store | 031 (FR-P2-02) | **DELETED** | `IPendingActionStore|PendingActionStore|actions/{id}/confirm: 0` |
| r7 `linear_dispatch` event + factory + client case | 025 (FR-P1-06) | **DELETED** | `linear_dispatch|LinearDispatchSseEvent`: 3 deletion-note hits (ADR-039:34 + canonical ×2); `TryDetectExplicitConsumerType: 0`; `executeLinearDispatch: 0` |
| `CompoundIntentDetector` | 035 | **DELETED** | §1 row |
| client dispatch helpers ×3 (`executeSummarizeIntent`, `intentMatcher`, `executeLinearDispatch`) | 023 (FR-P1-04) / 025 | **DELETED** | 3/3/0 hits — non-zero are the ADR-039 deletion-note + `dispatchConsumer.ts` header listing the predecessors it replaced + a test header "were deleted, NFR-08" (annotations) |
| client summarize duplicates (LW `SummarizeFiles` cluster, `executeComposeSummarize`) | 045 (FR-P3-06) | **DELETED** | LW copy gone (`src/solutions/LegalWorkspace/.../summarizeService.ts`: 0); `executeComposeSummarize`: 1 past-tense comment. Same-name survivor: shared-lib `SummarizeFilesWizard/summarizeService.ts` is the LIVE wizard service consuming the canonical `useSseStream` (045 client consolidation) — intentional distinct module |
| R1 client registries / providers / cross-pane | 072 | **DELETED** | §2 rows |
| stale catalogs/seeds/docs | 073/051/052 | see §5 | — |
| `CapabilityRouter` (R2 three-tier classifier, absorbed into deleted-estates) | 030–036 arc | **DELETED** | code: 1 stale client comment fixed opportunity NOT taken (frozen-adjacent seed/infra `$comment`s narrate history); `src/solutions/SpaarkeAi/.../CommandRouter.ts:25` presents it as live → §10 |
| `SprkChatExportWord` / `SprkChatBridge` (C1) · `cross-pane/` (C2) · R1 registries (C3) · 4 widgets (C4) | 072 | **DELETED** (bridge = keep row §2) | §2 |

## 7. Phase-coupled FR retirement symbols (spec FR-P1-04/06, FR-P2-02/05/06/07, FR-P3-01/04/05/06/07)

| Symbol set | FR / task | Status | Evidence |
|---|---|---|---|
| `intentHint` (end-to-end incl. SoftSlashRouter internals) | FR-P2-05 / 034 | **DELETED** | `\bintentHint\b|\bIntentHint\b`: 3 deletion-note hits only. Note: client `SoftSlashRouter` module itself remains LIVE by design (deterministic soft-slash dispatch — 2 live comment refs); 034 retired only its `intentHint` leg. `AI-ARCHITECTURE.md:33` "intentHint/SoftSlashRouter" phrasing slightly overstates → §10-lite |
| `PlaybookDispatcher`, `IntentRerankerService`, `PlaybookCandidateSelector`, PlaybookEmbedding subsystem | FR-P2-06 / 035 | **DELETED** | code grep 0 for all four (`PlaybookEmbedding` dir ABSENT); hits = deletion-notes + `.claude/agent-memory/researcher/*` (historical memory, main-session-only surface) + 1 stale infra `$comment` (§5 multinode row) + `background-workers-architecture.md:63` presents `PlaybookIndexingBackgroundService` as live → §10 |
| legacy `Chat/Tools` + `PlaybookOutputHandler` | FR-P2-07 / 036 | **DELETED** | dir ABSENT; `\bPlaybookOutputHandler\b`: 5 hits = researcher memory (3) + canonical T-07 "vocabulary" heritage note (1) + memory index (1) — historical |
| `NarrateUseCodeBasedNarrator` flag + `/narrate` engine default | FR-P3-04 / 043 | **DELETED** | `NarrateUseCodeBasedNarrator|UseCodeBasedNarrator: 0` |
| Engine shells + F-1 legs: `FileSummarizeService`, `DocumentProfileService` wrappers, `InvokePlaybook`/`AnalysisQuery`/`WorkingDocument` handlers, facade triangle (`IInvokePlaybookAi`) | FR-P3-05 / 044 | **DELETED** | `\bFileSummarizeService\b: 0`; `DocumentProfileService`: 2 doc-drift hits (code sample in `SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md:145-151`) → §10; `InvokePlaybookHandler`: 1 canonical-matrix heritage row; `AnalysisQuery(Tool)?Handler`: 1 PCF test fixture string (mock data, harmless); `InvokePlaybookAi`: 3 stale comments INSIDE frozen-engine files (`INodeExecutor.cs:301`, `ReturnResponseNodeExecutor.cs:29,215`) — left untouched deliberately (044 frozen-diff-empty constraint outweighs comment hygiene) |
| ONE SSE parse path / ConversationPane decomposition | FR-P3-06 / 045 | **DELETED/DONE** | 045 evidence + keep-with-reason: office-addins `SseClient` (048 operator ruling "SseClient keep") |
| FieldDelta dual-render (widget layer, bus discriminant, dispatchConsumer case) | FR-P3-07 / 046 | **DELETED** | client `field_delta|FieldDelta` in `src/client` + `src/solutions`: **0 hits** |

## 8. Wave-accumulated candidates (TASK-INDEX ✅ annotations + G-P3 rounds) — adjudicated by THIS task

| Candidate | Origin | Verdict | Evidence / action |
|---|---|---|---|
| `DAILY-BRIEFING-NARRATE` playbook orphaned on spaarkedev1 | 043 | **RETIRE-data — DONE** | `sprk_analysisplaybook` `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` "Daily Briefing Narrate": read Active(0/1) → deactivated → re-read **Inactive(1/2)** (old→new shown in transcript) |
| Live `spaarke-playbook-embeddings` Azure AI Search index | 035 | **OPERATOR — document only (per task boundary)** | zero code consumers since 035 (writers/readers/drift-job all deleted, grep-zero above); docs already annotate orphan status (`AI-ARCHITECTURE.md:340`, `rag-architecture.md:37,104`, `ai-search-azure-setup.md:424`). **Operator action**: delete index `spaarke-playbook-embeddings` on the dev (and any other env) Azure AI Search service; no code change required; safe immediately. |
| `DocumentStreamEvent` plumbing | 046 | **OPERATOR-decision** | ZERO server emitters remain (`DocumentStreamWriter` invocations in handlers: 0 — the sole emitter `WorkingDocumentHandler` died at 044); plumbing survives as ADR-033's documented side-channel contract (`ChatInvocationContext.DocumentStreamWriter`, adapter pass-through, ChatEndpoints writer, model, client `useSseStream`→`SprkChatBridge`→RichTextEditor/AnalysisWorkspace consumers). Deleting = retiring ADR-033 Path (needs Path-B ADR decision) + touches the live SprkChatBridge keep. → r2 pane-communication cutover |
| `playbook_options` client leg | 046 | **OPERATOR-decision** | server emitters: `git grep "playbook_options|PlaybookOptionsEventBuilder" -- src/server` → **0 hits** (builder + dispatcher deleted); client leg (~67 hits: SprkChat card rendering, callbacks, types, useSseStream case) is dead-on-wire but entangled in the SprkChat public API — NOT a "small remainder" per task constraint. → r2 SprkChat API trim |
| legacy `ContextEventEmitter` trace events | 046 | **KEEP-with-reason** | the six `context.tool_call_*` events are LIVE ADR-015-audited telemetry counters (binding per `infra/dataverse/sprk_analysistool-recall-session-file-row.json` `_comment_telemetry`); only their trace-WIDGET rendering was superseded by the ledger `tool_chain` frame (046). Any relay-trim lives in `Services/Ai/Telemetry` = **task 054's hands-off zone** — coordination note left in the 050 report |
| `AnalysisChunk.FieldDelta` server model | 046 | **DELETED this task** | authority: ADR-037 as amended ("FieldDelta dual-render deletable at cutover"); `FromDelta` call sites: 0; `.Delta` reads: 0; client renderer deleted at 046. Removed `FieldDelta` record + `Delta` property + `FromDelta` factory from `Models/Ai/AnalysisChunk.cs`; deleted `AnalysisChunkFieldDeltaTests.cs`; updated `SectionStreamSseEvents.cs` invariant docs. Post-grep: server `FieldDelta` hits = deletion-note + frozen TL playbook-JSON `$comment`s + frozen-engine comment (`PlaybookOrchestrationService.cs:1297`, untouchable) + the DISTINCT live `Streaming.FieldDeltaEvent` parser type (frozen engine internal — intentionally untouched) |
| 4 legacy workspace-tab tools (SYS-Get/Update/Close Workspace Tab + legacy artifact variants of Send) | G-P3 / 052 note | **OPERATOR-decision — rows left Active on purpose** | rows exist Active (`GET-WORKSPACE-TAB-CONTENT` cb930271, `CLOSE-WORKSPACE-TAB` cc930271, `UPDATE-WORKSPACE-TAB` 806162a3; read output shown). Deactivating them NOW would orphan their still-registered handlers (`GetWorkspaceTabContentHandler`/`UpdateWorkspaceTabHandler`/`CloseWorkspaceTabHandler`) and trip the FR-P0-04 orphan-handler → `/healthz` **Unhealthy** boot check. ALSO the store is not fully orphaned: `SprkChatAgentFactory:427-431` reads `GetTabsAsync` for the prompt-snapshot workspace block, and the client restores via `GET /api/workspace/state` (`WorkspaceTab.ts:280`, WorkspacePane reconciliation). Retirement must be a coordinated code+row change (handlers + rows + `SendWorkspaceArtifactHandler` legacy-variant surgery, keeping the LIVE `widgetType:'Workspace'` leg from G-P3 R2-D). → r2 |
| `WorkingDocumentService` | 044 note | **KEEP-with-reason** | LIVE consumer: `ChatEndpoints.cs:343` injects + `:882` calls `UpdateChatHistoryAsync(analysisGuid, chatHistoryJson)` (sprk_analysis chat-history write-back, `json-field-schemas.md:82`). Only the tool-handler legs died at 044. Service-level trim (dead methods) = r2 candidate |
| `TL-004/006/008/010` no-mirror tool rows | 051 | **RETIRE-data — DONE** | read: all 4 Active (Document Classifier 68518925 / Summary Generator 69518925 / Search Documents 16956329 / General Analysis c01ef382) → deactivated → re-read: all 4 **Inactive** (shown). No handler mirror existed (051 verification) → no orphan-handler risk |
| KNW duplicate knowledge rows (10 coded + 10 null-code) | 051 | **RETIRE-data — DONE (10 null-code rows)** | canonical coded rows KNW-001..010 (sprk_knowledgecode set) KEPT Active; the 10 null-code duplicates (`KNW-001-contract-terms-glossary`…`KNW-010-legal-red-flags-catalog`, `*-8271-f111-ab0d-*` seed batch) are unreachable via the canonical `sprk_knowledgecode` lookup → deactivated → re-read: all 10 **Inactive** (shown) |
| `ACT-BUILDER-*` / `TL-BUILDER-*` catalog rows | 050 Scope B discovery | **VERIFIED ABSENT** | `SELECT … WHERE sprk_actioncode LIKE 'ACT-BUILDER%'` → `[]`; `sprk_toolcode LIKE 'TL-BUILDER%'` → no rows. The builder scopes existed only as code fallbacks + embedded JSON (both deleted) |

## 9. Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ GREEN — `0 Error(s)`, 18 warnings (all pre-existing families: CS1998 Null* peers, CS0618 DemoExpiration, CS8766) |
| Full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | First run (contended with parallel task-054 test host): 18 failed / 7430 passed / 101 skipped. Stable re-run: **4 failed / 7444 passed / 101 skipped**. Every failure maps to the KNOWN list: `KnowledgeDeploymentConfigTests`, `DailyBriefingCollectorTests`, `PlaybookTemplateContextBuilderTests.Build_TextOnlyOutput`, `SessionFilesCleanupJobTests` (+ `AuditLogService` flake and NetArchTest 5+1 appeared only in the contended run). `ExecutorConfigSchemasEndpointTests` (KNOWN) no longer exists — deleted with its subject. **Zero NEW failures from task 050.** |
| Eval suite (`--filter Category=GoldenUtteranceEval`) | ✅ **35/35 passed** (`Failed: 0, Passed: 35, Total: 35`) |
| Client builds (touched packages) | ✅ `Spaarke.AI.Context` tsc EXIT 0 (after removing the emptied `providers/` barrel) · `Spaarke.AI.Widgets` tsc EXIT 0 |
| Publish size (ADR-029) | `dotnet publish -c Release … deploy/api-publish/` → **45.25 MB compressed** = **−0.22 MB** vs the 45.47 MB task-046 baseline (NET REDUCTION as expected; well under the 60 MB ceiling). Measured on the shared worktree, i.e. INCLUSIVE of task-054's in-flight telemetry additions — the 050-attributable delta is ≥ −0.22 MB |
| Dataverse old→new evidence | shown per row in §8 (read → update → re-read for all 15 deactivated rows) |

## 10. Doc-drift register (deleted items still presented as LIVE in docs — follow-on for 052-addendum / task 090)

Small, itemized; none block G-P4 (the gate criterion is code estate + explained survivors):

1. `docs/architecture/scope-architecture.md:28` — ScopeGapDetector row presented as live; `:76` — AiPlaybookBuilderService consumer row (now also stale post-Scope-B).
2. `docs/architecture/background-workers-architecture.md:63,71,105,140` + `docs/architecture/sdap-overview.md:287` — DocumentVectorBackfillService / PlaybookIndexingBackgroundService as live.
3. `docs/architecture/chat-architecture.md:152` + `docs/guides/spaarkeai-launch-points.md` (6 refs) — StandaloneAiProvider as live.
4. `docs/architecture/ai-architecture-playbook-consumer-routing.md:249,387` — SessionSummarizeOrchestrator as canonical.
5. `docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md:145-151` — DocumentProfileService code sample.
6. `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md:2226` — dead file link to PlaybookExecutionEngine.cs (header note exists; link itself dangling).
7. PCF list drift: `sdap-pcf-patterns.md:36`, `REPOSITORY-NAVIGATION-GUIDE.md:49`, `testing-and-code-quality.md:1065`, `ai-assistant-theming.md:243-246` (AIMetadataExtractor / DrillThroughWorkspace / PlaybookBuilderHost).
8. Seed-JpsActions presented-as-live: `ai-guide-playbook-deploy-recipe.md:36,158,224`, `JPS-AUTHORING-GUIDE.md:625,1217`, `ai-architecture-actions-nodes-scopes.md:220`.
9. `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts:25` — comment claims fall-through to "existing CapabilityRouter" (deleted).
10. `AI-ARCHITECTURE.md:33` "intentHint/SoftSlashRouter" phrasing overstates — SoftSlashRouter module is live; only its intentHint leg died.
11. **MAIN SESSION ONLY** (`.claude` write boundary): `.claude/skills/jps-action-create/SKILL.md:271` stale "Add to Seed-JpsActions.ps1 for deployment" step; `.claude/constraints/bff-extensions.md:260` + `.claude/skills/jps-validate/SKILL.md:184,333,352` reference the deleted `GET /api/ai/playbook-builder/executor-config-schemas` endpoint (Scope B).

## 11. Operator-decision register (consolidated)

| # | Item | Recommended action |
|---|---|---|
| O-1 | `spaarke-playbook-embeddings` Azure index | Delete on dev AI Search service (zero consumers; boundary = operator executes) |
| O-2 | Workspace-tab tool cluster (3 rows + 3 handlers + Send legacy variants + `WorkspaceStateEndpoints`/`WorkspaceStateService` write path) | Coordinated code+row retirement in r2; keep `send_workspace_artifact` `widgetType:'Workspace'` leg + `GetTabsAsync` prompt block + `GET /api/workspace/state` restore |
| O-3 | `DocumentStreamEvent` plumbing + `SprkChatBridge` + `playbook_options` client leg | One r2 "client wire diet" cutover (all three are dead-on-wire or bridge-coupled; each requires SprkChat public-API changes; ADR-033 Path-B amendment for the doc-stream side-channel) |
| O-4 | `AnalysisOrchestrationService` deprecated no-nodes legacy branch (`:775`) | Verify zero node-less playbooks in env data, then remove branch (r2) |
| O-5 | `SseOutputGuard`+`SseEvent` pair · `IModelSelector`/`ModelSelector` · one-shot `Migrate-*Prefill*.ps1` scripts · `scripts/seed-data` R4 JSONs | Registered/unused-but-ratified or load-bearing-but-stale items — batch into r2 catalog-governance / test-diet review |

## 12. ADR-038 test register (for task-090 /test-diet)

Deleted this task, all SCAFFOLDING-class (tests of deleted dead surface): `AiPlaybookBuilderServiceTests.cs`, `Builder/ConfigurePromptSchemaTests.cs`, `ClarificationFlowTests.cs`, `ExecutorConfigSchemasEndpointTests.cs`, `EntityResolutionServiceTests.cs`, `Chat/SessionCleanupSecurityTests.cs`, `Infrastructure/Streaming/ServerSentEventWriterTests.cs`, `Models/Ai/BuilderSseEventsTests.cs`, `Models/Ai/AnalysisChunkFieldDeltaTests.cs`. Edited fixtures (mock-DI line removal only): 5 `Spe.Integration.Tests` files.
