# Task 035 — Dispatcher-stack DELETION (FR-P2-06) — Task Notes

> Date: 2026-07-06 · Wave W-P2-D · task-execute FULL rigor (TEST-MODIFYING override).
> Precondition verified: 034 landed (a90607a9f) — zero live NL-path callers of the stack.
> Ran in parallel with task 036 (PlaybookOutputHandler + Chat/Tools) in the same worktree;
> coordination notes at the bottom.

## Deletion inventory (verify-dead-first: every item caller-censused before deletion)

### Files DELETED — server (git rm)

| File | Caller-census evidence |
|---|---|
| `Services/Ai/Chat/PlaybookDispatcher.cs` | Sole production creator `SprkChatAgentFactory.CreatePlaybookDispatcherAsync`; sole caller of that was dead click endpoint `ExecutePlaybookAsync` (034 severed its trigger event) |
| `Services/Ai/Chat/IntentRerankerService.cs` + `IIntentRerankerService.cs` | Sole consumer `PlaybookOptionsEventBuilder` (deleted); DI registration in AiChatModule (removed) |
| `Services/Ai/Chat/PlaybookCandidateSelector.cs` + `IPlaybookCandidateSelector.cs` | Same — options-builder-only consumer |
| `Services/Ai/Chat/CompoundIntentDetector.cs` | Consumers: SprkChatAgent field (removed — fed only `DetectToolCallsAsync`), factory agent construction (removed), PlaybookOutputHandler (deleted by 036), gate-unification tests §4 (removed) |
| `Configuration/IntentRerankerOptions.cs`, `Configuration/PlaybookSelectorOptions.cs` | Only bound in ConfigurationModule (bindings removed); only read by deleted services; NO appsettings sections existed |
| `Services/Ai/Chat/SseEventTypes/PlaybookOptionsSseEvent.cs` + `PlaybookOptionsEventBuilder.cs` | Sole emitter path deleted in 034; `ChatSseEventFactory.CreatePlaybookOptionsEvent` (removed) was the only other reference |
| `Models/Ai/Chat/DispatchResult.cs` | Consumers: dispatcher, `ExecutePlaybookAsync`, PlaybookOutputHandler (036) — all deleted |
| `Models/Ai/PlaybookEmbeddingDocument.cs`, `Models/Ai/PlaybookSearchResult.cs` | Embeddings-subsystem-only models |
| `Models/Ai/Chat/PendingPlan.cs` (PendingPlan + PendingPlanStep), `Models/Ai/Chat/PlanApprovalRequest.cs` | Plan-shaped gate presentation: producers (detector + deleted 034 pre-pass) and consumer (`ApprovePlanAsync`) all gone |
| `Services/Ai/PlaybookEmbedding/` (whole dir, 7 files: PlaybookEmbeddingService, PlaybookIndexingService, PlaybookIndexingBackgroundService, PlaybookIndexDriftDetectionJob, PlaybookEmbeddingHashCalculator, IPlaybookEmbeddingHashCalculator, PlaybookIndexInputValidator) | READ side of the `spaarke-playbook-embeddings` index was EXCLUSIVELY `PlaybookDispatcher.SearchPlaybooksAsync` calls (verified: 3 call sites, all in the dispatcher). The entire write side (hosted indexer, nightly drift job, trigger endpoint, hash calc, validator) existed only to feed it |
| `Api/Ai/PlaybookEmbeddingEndpoints.cs` | POST `/api/ai/playbooks/{id}/index` fire-and-forget trigger for the deleted hosted indexer |
| `infrastructure/ai-search/spaarke-playbook-embeddings.json` | Index schema — index has no readers or writers left |
| `scripts/Index-ExistingPlaybooks.ps1` | Ingestion script for the retired index |

### Dead click endpoints DELETED from `ChatEndpoints.cs` (~814 lines)

- `ApprovePlanAsync` + `POST /sessions/{sessionId}/plan/approve` mapping + `BuildStepExecutionMessage` — resumed plans that nothing stores post-034.
- `ExecutePlaybookAsync` + the `/api/ai/playbook-dispatch` group (`POST /execute`) + `PlaybookDispatchExecuteRequest` DTO — consumed `playbook_options` picks that nothing emits post-034.
- Chat SSE DTOs: `ChatSsePlanStep`, `ChatSsePlanPreviewData`, `ChatSsePlanStepStartData`, `ChatSsePlanStepCompleteData`; `ChatSseEvent.Type` doc list pruned.
- **Gate-functionality check (task 031/032 caution)**: `ApprovePlanAsync` wrote `GateKindConfirmation` markers only when resuming a stored plan (none can exist); `ExecutePlaybookAsync` resolved `options-` gate markers only when 034's deleted emitter had created them (none can exist). The unified `ResolveGateAsync` (`POST /sessions/{sessionId}/gates/{gateId}/resolve`, task 032) is untouched and remains THE resume surface — elicitation seam (`ResolveElicitationTurnAsync`), `SessionDispatchOrchestrator`, and `BindingCapabilityTool` suspension all intact. **No gate functionality lost.**

### Members DELETED from surviving files

| Surviving file | Removed |
|---|---|
| `SprkChatAgent.cs` | `_rawChatClient` + `_intentDetector` fields, ctor params, `DetectToolCallsAsync` (zero live callers — only middleware pass-throughs + test fakes) |
| `ISprkChatAgent.cs` | `DetectToolCallsAsync` member |
| 5 middlewares (ContentSafety, CostControl, Telemetry, ServiceRouting, SafetyPipeline) | `DetectToolCallsAsync` pass-throughs |
| `SprkChatAgentFactory.cs` | `CreatePlaybookDispatcherAsync`, `CreatePlaybookOutputHandler` (sole caller = deleted execute endpoint; deleting it does not touch 036's files), keyed-`"raw"` `IChatClient` ctor dependency, detector construction |
| `NullSprkChatAgentFactory.cs` | Both corresponding overrides (ADR-032 Null peers die in the same commit) |
| `AiModule.cs` | Keyed `"raw"` `IChatClient` registration (factory was sole consumer); `PlaybookIndexInputValidator`, `IPlaybookEmbeddingHashCalculator`, `IJobHandler` drift-job registrations; conditional `AddHostedService<indexer>`; DI-audit comment 15→11 unconditional |
| `AiChatModule.cs` | Selector + reranker + options-builder registrations (6→4... final 4: prompt-builder ×2 + latency ×2) |
| `ConfigurationModule.cs` | Both classifier option bindings |
| `EndpointMappingExtensions.cs` | `MapPlaybookEmbeddingEndpoints()` call |
| `RateLimitingModule.cs` | `ai-indexing` policy (sole consumer was the deleted trigger endpoint) |
| `PendingPlanManager.cs` | Plan-shaped members (`StoreAsync`/`GetAsync`/`GetAndDeleteAsync`/`DeleteAsync` over the plan record), `BuildPendingPlanKey`, `pending-plan` cache resource. **Generalized invocation gate store 100% untouched** (Suspend/Resume/Reject/Close/WriteGateMarker/FindPendingGate/ResolveElicitationOnDispatch) |
| `NullPendingPlanManager.cs` | Plan-shaped Null overrides (invocation-gate overrides untouched) |
| `IPlaybookService.cs` / `PlaybookService.cs` / `NullPlaybookService.cs` | `ListAllActivePlaybooksAsync` (FR-13 drift-scan enumerator) + `UpdateIndexStatusAsync` (FR-13 index-state writer) — only callers were the deleted embeddings jobs. `sprk_indexstatus`/`sprk_indexhash` Dataverse columns + passive DTO fields kept (read-only data-model surface; P4/Track-B candidate) |
| `ChatSseEventFactory.cs` | `CreatePlaybookOptionsEvent` |
| `appsettings.template.json` | `spaarke-playbook-embeddings` removed from `AllowedIndexes` |
| `scripts/ai-search/Deploy-AllIndexes.ps1` | `playbook-embeddings` catalog entry + key lists |

### Tests DELETED (TEST-MODIFYING override → Step 9.5 gates run)

- `PlaybookDispatcher{PhaseB,Integration,DirectExecute,Destination,Attachments}Tests.cs` (5 files — **incl. the flaky PhaseB latency test, which dies with its subject**; its `.reliability-registry.json` entry removed)
- `PlaybookCandidateSelectorTests.cs`, `IntentRerankerServiceTests.cs`, `PlaybookOptionsEventBuilderTests.cs`, `DispatchResultTests.cs`, `PlaybookDispatchExecuteRequestTests.cs`
- `tests/unit/.../Services/Ai/PlaybookEmbedding/` (4 files: DriftDetectionJob, ComposeContentText, HashCalculator, InputValidator tests)
- `Api/Ai/ChatSessionPlanEndpointTests.cs` (tested the deleted plan-shaped store + plan/approve surface)
- KEEP-path note: the only KEEP-path file touched is `tests/integration/contract/Api/Ai/RateLimitingContractTests.cs` — NOT deleted; only the `ai-indexing` rows were removed because that policy + its endpoint no longer exist (scenario itself retired; contract coverage of the surviving 6 policies unchanged).

### Tests EDITED (subject-preserving)

- `ConfirmationGateUnificationTests.cs` — detector section (4 tests) removed; anchors 1–3 (ADR-039 declared-metadata gating, unified store suspend/resume, ADR-040 ledger markers) intact.
- `SprkChatAgentTests.cs`, `AgentTurnLoopContractTests.cs` — ctor updated (raw client + detector args gone).
- `SprkChatAgentFactoryTests.cs`, `SprkChatAgentFactoryDedupTests.cs` — keyed-`"raw"` registrations removed.
- `AgentMiddlewareTests.cs` (2 fakes), `SafetyPipelineMiddlewareTests.cs` (1 fake) — `DetectToolCallsAsync` fake members removed.
- `ReAnalysisFlowTests.cs`, `ChatEndpointsTests.cs` (integration) + `InvokePlaybookDescriptionTests.cs`, `SummarizeWorkspaceMultinodeMigrationTests.cs`, `InMemoryTenantCache.cs` — comment-only rewording for grep-zero.

### Client (scope-bounded per 034 notes + parent directive)

- P3 FR-P3-06 owns the client `plan_preview`/`playbook_options` handler consolidation (ConversationPane 117b handlers, PlanPreviewCard, ActionConfirmationDialog leftovers) — NOT deleted here; they provably receive no events but are documented P3 scope.
- Comment-only rewording for server-symbol grep-zero: `SprkChat/types.ts` (2), `useSseStream.ts` (1), `PlaybookBuilder/types/playbook.ts` (4).

### Docs + contracts updated

- `docs/architecture/chat-architecture.md` — rewritten to post-cutover reality (loop = ONE dispatch protocol; unified gate; classifier stack marked deleted; dispatch-flow section removed).
- `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` — index #7 retired (8→7 active); restoration matrix row removed; naming examples swapped.
- `docs/guides/ai-search-azure-setup.md` — bootstrap/verification rows updated (8→7 rows); live-index deletion flagged for P4 sweep.
- `infrastructure/contracts/sse-events/manifest.json` — `workspace_action.emittedBy` corrected to `R2SseEventEmitter` (former emitters deleted).

## Grep-zero evidence (NFR-08) — SHOWN

`git grep -l "<symbol>" -- src tests scripts infrastructure` per symbol, 2026-07-06:

```
PlaybookDispatcher: ZERO            IntentRerankerService: ZERO
IIntentRerankerService: ZERO        PlaybookCandidateSelector: ZERO
IPlaybookCandidateSelector: ZERO    CompoundIntentDetector: ZERO
IntentRerankerOptions: ZERO         PlaybookSelectorOptions: ZERO
DispatchResult: ZERO                PlaybookEmbeddingDocument: ZERO
PlaybookSearchResult: ZERO          PlaybookEmbeddingService: ZERO
PlaybookIndexingService: ZERO       PlaybookIndexingBackgroundService: ZERO
PlaybookIndexDriftDetectionJob: ZERO  PlaybookEmbeddingHashCalculator: ZERO
PlaybookIndexInputValidator: ZERO   PlaybookEmbeddingEndpoints: ZERO
DetectToolCallsAsync: ZERO          PendingPlanStep: ZERO
PlanApprovalRequest: ZERO           ChatSsePlanPreviewData: ZERO
ChatSsePlanStepStartData: ZERO      ChatSsePlanStepCompleteData: ZERO
PlaybookOptionsEventBuilder: ZERO   PlaybookOptionsSseEvent: ZERO
PlaybookDispatchExecuteRequest: ZERO
```

**docs/ scope note**: two dated decision-record files retain symbol mentions by design —
`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` (the governing redesign
doc whose §7 ten-mechanism disposition table + changelog PRESCRIBE these very deletions —
erasing them would erase the decision record) and
`docs/assessments/agent-framework-fit-assessment-2026-06-03.md` (dated point-in-time
assessment snapshot). Treated as the paper analogue of git history. All LIVING reference
docs (chat-architecture, index catalog, setup guide) are zero.

## Frozen-engine scope check (constraint)

`git status` diff scope contains ZERO files under `Services/Ai/Nodes/` engine internals or
`PlaybookOrchestrationService` — the frozen engine is untouched (the only Nodes/ test edit is a
comment rewording in `SummarizeWorkspaceMultinodeMigrationTests.cs`).

## Telemetry check (NFR-09)

Deleted components carried classifier-local logging only (rerank latency, dispatch stage logs) —
all dead with their subjects since 034. Budget telemetry (ADR-016) lives in `BudgetedAIFunction`
/ `AiLatencyTelemetry` / `AgentCostControlMiddleware` on the loop path — untouched. No live
telemetry removed. Ledger writers (ADR-040): none of the deleted components wrote ledger
entries that lacked a replacement — gate markers write via surviving `PendingPlanManager`.

## Builds / tests / eval / publish

- `dotnet build src/server/api/Sprk.Bff.Api/` — **Build succeeded** (0 errors) post-deletion.
- Test projects: Spe.Integration.Tests **0 errors** (after fixing 6 factory-ctor call sites),
  Sprk.Bff.Api.IntegrationTests **0 errors**, Spaarke.ArchTests **0 errors**.
- **Full unit suite** (run after 036's in-flight edits landed — shared worktree):
  **7698 total — 7591 passed, 101 skipped, 6 failed.** All 6 failures are on the KNOWN
  pre-existing list (ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector
  resolver, PlaybookTemplateContextBuilder TextOnly, SessionFilesCleanup, AuditLogService
  flake). **Zero failures attributable to 035.** The flaky PlaybookDispatcherPhaseBTests
  latency test is GONE — it died with its subject (correct), and its
  `.reliability-registry.json` entry was removed.
- **Eval suite (NFR-02)**: `--filter Category=GoldenUtteranceEval` = **12/12 green**.
- **ArchTests**: 18/23 — the 5 failures are exactly the KNOWN "NetArchTest 5".
- **Client**: `@spaarke/ui-components` typecheck clean (my client edits were comment-only).
- Final grep-zero re-run on the settled tree (post-036): **ALL 27 SYMBOLS ZERO** across
  src/ tests/ scripts/ infrastructure/.

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release -o deploy/api-publish` → **46.83 MB compressed**
(PowerShell `Compress-Archive -CompressionLevel Optimal`, incl. PDBs — same convention +
tree lineage as the 034 baseline) / 141.53 MB uncompressed / 270 files.
Baseline (task 034): **46.95 MB** → **NET REDUCTION −0.12 MB** ✅ (tree also contains 036's
concurrent deletions; the delta is the combined W-P2-D wave effect at measurement time).
ZERO NuGet changes → no new CVE surface by construction. Far below the 60 MB ceiling.

## Step 9.5 quality gates (FULL rigor — TEST-MODIFYING override)

**adr-check (self-run, findings):**
- ADR-039 ✅ — removes the LAST second intent-detection mechanism; nothing new added; no
  tool-name gating introduced; grounded outputs untouched.
- ADR-040 ✅ — no deleted component was a ledger writer without replacement; gate markers
  flow through surviving PendingPlanManager; ledger-before-render contract untouched.
- ADR-032 ✅ — every deleted registration's Null-Object peer deleted in the SAME change
  (NullSprkChatAgentFactory overrides, NullPendingPlanManager plan overrides,
  NullPlaybookService FR-13 members). bff-extensions §F.1 scan: no new conditional
  registrations added; the one conditional block touched (hosted indexer) was removed whole.
- ADR-010 ✅ — AiModule 15→11 unconditional, AiChatModule 6→4; DI-audit comments updated.
- ADR-029 ✅ — publish verified, net reduction, ≤60 MB.
- ADR-015 / NFR-07 ✅ — deletion-only for logging surfaces; no content logging added.
- ADR-038 ✅ — deleted tests are subject-death class; KEEP-path rule respected (the only
  KEEP-path file touched, RateLimitingContractTests, lost ONLY the rows for the deleted
  `ai-indexing` policy; the file + remaining 6-policy contract coverage intact).
- ADR-013 ✅ — no CRUD→AI injection introduced.
- NFR-08 ✅ — grep-zero SHOWN above; no shims, no [Obsolete] stubs, no commented-out code.
- NFR-09 ✅ — budget telemetry (ADR-016) lives on the loop path, untouched.
- §6.5 protocol: no ADR conflicts required paths A/B; everything resolved as compliant
  deletion (path C-equivalent: comply by construction).

**code-review (self-run, notable calls):**
- `PendingPlanManager` plan-shaped member deletion verified against the "do not disturb the
  unified gate" constraint — invocation-store members + gate vocabulary + FindPendingGate +
  elicitation resolution all untouched; only the dead plan-record trio + its cache resource
  + key helper removed (zero callers post-034, shown).
- `IPlaybookService` member deletions (drift-scan enumerator + index-state writer) censused
  to zero callers outside the deleted embeddings jobs before removal.
- `ai-indexing` rate-limit policy removed with its sole consumer (kept policies unchanged).
- Comment tombstones reworded to avoid naming deleted symbols (NFR-08 grep-zero holds even
  for explanatory comments).
- 6 integration-test factory constructions re-pointed to the 3-arg ctor.
- Client edits are comment-only; no client behavior change (P3 owns handler consolidation).

## Coordination with task 036 (parallel, same worktree)

- 036 deleted `PlaybookOutputHandler.cs`, `Services/Ai/Chat/Tools/*` (11 files), and the
  `PlaybookOutputHandler*Tests` before my coupled cluster (CompoundIntentDetector,
  DispatchResult, PendingPlan) was due — unblocking full deletion with no leftover shims.
- I did NOT touch 036's files. The `dialog_open`/`navigate` SSE DTO records at the tail of
  `ChatEndpoints.cs` (PlaybookOutputHandler's wire events) were left for 036's sweep; their
  doc comments no longer name deleted 035 symbols.
- `tests/.reliability-registry.json`: I removed the PhaseB dispatcher entry; 036 removed the
  CompareDocumentsTool entry.
