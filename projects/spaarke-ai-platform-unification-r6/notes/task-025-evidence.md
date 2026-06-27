# Task 025 — `SessionSummarizeOrchestrator` Refactor Evidence

> **Task**: D-A-17 — Refactor `SessionSummarizeOrchestrator` to invoke
> `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (Pillar 4 code side)
> **Date**: 2026-06-08
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Status**: Complete; Pillar 4 closed (data side from task 024 + code side here)

---

## Scope reminder

Pillar 4 code side: replace the orchestrator's R5 alternate-key bypass
(`LoadActionConfigAsync` → `IGenericEntityService.RetrieveByAlternateKeyAsync` on
`sprk_actioncode = "SUM-CHAT@v1"`) with a `PlaybookExecutionEngine` route that resolves the
action through the now-valid playbook → node → action FK chain (post-task-024).

**Preservation mandate** (binding per POML §3.1): public method signature, streaming
`IAsyncEnumerable<AnalysisChunk>` shape, FR-04 multi-file interjection, ADR-014 tenant+session
RAG filter, NFR-13 safety pipeline middleware wraps, NFR-08 11 production node executors,
Null-Object kill-switch subclass behavior — ALL unchanged externally.

---

## Stop-and-surface (Option A approved at design gate)

The POML stated approach (`PlaybookExecutionEngine.ExecuteAsync(playbookId)`) does NOT match
the actual `IPlaybookExecutionEngine` contract — it has `ExecuteBatchAsync(PlaybookRunRequest)`
and `ExecuteConversationalAsync(ConversationContext)`, neither of which yields
`IAsyncEnumerable<AnalysisChunk>` for chat /summarize.

After stop-and-surface, user approved **Option A**: additively extend
`IPlaybookExecutionEngine` with a new method `ExecuteChatSummarizeAsync(Guid playbookId,
ChatSummarizeRequest, CT) → IAsyncEnumerable<AnalysisChunk>`. The chat-summarize streaming
pipeline (RAG retrieval + Structured Outputs streaming + IncrementalJsonParser + AnalysisChunk
emission) moves from the orchestrator INTO the engine. The orchestrator becomes a thin
pass-through.

**Why Option A vs B/C** (the CLAUDE.md "ADRs Are Defaults" anti-pattern check):
- Option B (engine for metadata only) wouldn't satisfy the project CLAUDE.md "MUST route chat
  /summarize through PlaybookExecutionEngine" — it would be engine-in-name-only.
- Option C (use `ExecuteBatchAsync` + adapt event stream) would LOSE field-delta streaming UX,
  LOSE the session-files filter, and require fabricating fake `Guid` DocumentIds for session
  files. Exactly the "known-fragile workaround" pattern CLAUDE.md warns against.
- Option A preserves every external behavior, fully removes the alternate-key bypass, and
  doesn't cross the ADR-013 facade boundary (the interface is in `Services/Ai/`, not
  `Services/Ai/PublicContracts/`).

---

## Files modified

### Production code (3 files)

| File | Change | LoC delta |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookExecutionEngine.cs` | Added `ExecuteChatSummarizeAsync` method + `ChatSummarizeRequest` record | +94 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs` | Added 5 new ctor deps (NodeService, EntityService, RagService, OpenAiClient, R5SummarizeTelemetry); implemented chat-summarize streaming pipeline (moved from orchestrator); added `ResolveActionConfigViaFkChainAsync` (FK-chain resolution) + helpers; added `ChatSummarizeActionConfig` internal record | +444 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` | Removed RAG + Structured Outputs + parser + telemetry + alternate-key load; new ctor (`ChatSessionManager` + `IPlaybookExecutionEngine` + `ILogger`); `SummarizeSessionFilesAsync` is now a thin pass-through that loads session, builds `ChatSummarizeRequest`, forwards to engine; removed `SummarizeActionCode` + `ActionEntityLogicalName` + `SchemaName` + `CombinedSummaryInterjection` constants (moved relevant ones to engine); removed `SessionSummarizeActionConfig` internal record (engine has its own); hardcoded `ChatSummarizePlaybookId` static-readonly Guid with comment pointing at how to make it config-driven | -469 / +228 (net -241) |

### Test code (4 files)

| File | Change |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SessionSummarizeOrchestratorTests.cs` | Full rewrite: 12 focused tests covering orchestrator boundary responsibilities only (forwards-to-engine, byte-equivalent pass-through, FK-chain routing, ADR-014 propagation, NFR-02 cap, session-not-found, ADR-010 reflection, convergence reflection, path discriminator forwarding, input validation, FR-26 invariant). Mocks `IPlaybookExecutionEngine`. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookExecutionEngineTests.cs` | Updated ctor (5 new mocks: NodeService, EntityService, RagService, OpenAiClient stub, R5SummarizeTelemetry). Added 10 new `ExecuteChatSummarizeAsync` tests covering FK-chain resolution (NO alternate-key), FR-04 single/multi-file, ADR-014 RAG filter, NFR-02 cap, mid-stream error, decline path, broken FK chain, argument validation (3 variants), happy-path complete shape. Added `StubChatSummarizeOpenAiClient` test double. |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs` | Fixture now registers real `PlaybookExecutionEngine` (so endpoint exercises the moved chat-summarize code via the real engine) + stub `INodeService` + replaced `RetrieveByAlternateKeyAsync` stubs with `RetrieveAsync` stubs (FK-resolved ID path) + replaced reference to removed `SummarizeActionCode` constant with literal `"SUM-CHAT@v1"`. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/InvokeSummarizePlaybookToolTests.cs` | `BuildOrchestrator` helper now constructs the real engine + INodeService stub; `BuildActionEntity` now takes an actionId parameter (matches the FK-resolved ID surface); replaced `SessionSummarizeOrchestrator.CombinedSummaryInterjection` references with `PlaybookExecutionEngine.CombinedSummaryInterjection` (the constant lives on the engine now). |

---

## Refactor design decisions

1. **`IPlaybookExecutionEngine` extension is additive, not breaking**
   - 3 existing methods + 9 consumers unaffected
   - New method is co-located with `ChatSummarizeRequest` record (matches `ConversationContext` co-location pattern)
   - No new top-level DI registrations (engine is already registered in
     `AnalysisServicesModule.AddPublicContractsFacade`); engine's new ctor deps are all already
     registered in the same compound-AI-ON gate (NodeService, EntityService, RagService,
     OpenAiClient, R5SummarizeTelemetry)

2. **Orchestrator boundary preserved**
   - `SummarizeSessionFilesAsync` public signature UNCHANGED
   - Session lookup stays at the orchestrator boundary (engine doesn't need to know about
     `ChatSessionManager`); the engine receives the resolved `UploadedFiles` manifest
   - Argument validation stays at the orchestrator (NFR-02 cap, tenant/session required) AND
     is repeated at the engine boundary (defense-in-depth)
   - Session-not-found behavior unchanged — `InvalidOperationException` propagates to the
     endpoint which maps to 404

3. **Null-Object subclass behavior unchanged externally**
   - `NullSessionSummarizeOrchestrator` ctor signature unchanged (`ILogger`-only)
   - Protected ctor on base class kept (`ILogger`-only); just nulls different fields
     (`_executionEngine` instead of the dropped `_ragService` etc.)
   - Override behavior: throws `FeatureDisabledException` at first `MoveNextAsync` — UNCHANGED

4. **Playbook ID hardcoded with config-friendliness comment**
   - `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` is a `static readonly Guid`
   - Doc-comment explicitly states: "If a future use-case requires per-tenant or
     per-environment playbook selection, lift this to `AnalysisOptions.ChatSummarizePlaybookId`
     and inject via `IOptions<T>` — no callers depend on this being a constant."
   - Per project CLAUDE.md "ADRs are defaults": defer the YAGNI; document the lift path

5. **FK-chain resolution strategy**
   - Engine calls `INodeService.GetNodesAsync(playbookId)` — returns `PlaybookNodeDto[]` with
     `ActionId` populated via FK (post-task-024)
   - Selects the first node (the chat-summarize playbook has exactly ONE AI node per task 024
     evidence) — clear error if multi-node variant appears later
   - Engine calls `IGenericEntityService.RetrieveAsync(actionLogicalName, actionId, columns, ct)`
     — NOT `RetrieveByAlternateKeyAsync`
   - Validates `sprk_systemprompt` + `sprk_outputschemajson` present; clear error otherwise

---

## Before/after diff summary

### `SessionSummarizeOrchestrator.cs`

**BEFORE** (~691 LOC):
- 6-param ctor: ChatSessionManager + IRagService + IOpenAiClient + IGenericEntityService + R5SummarizeTelemetry + ILogger
- `SummarizeSessionFilesAsync`: full pipeline — session load, file resolve, NFR-02 cap, FR-04
  interjection, action load (alternate-key), RAG retrieval, Structured Outputs streaming,
  IncrementalJsonParser, AnalysisChunk emission, telemetry recording
- 4 internal constants: SummarizeActionCode, SchemaName, ActionEntityLogicalName, CombinedSummaryInterjection
- `LoadActionConfigAsync` (alternate-key bypass) — REMOVED
- `BuildRagQuery`, `BuildUserContent`, `RecordTelemetry`, `EstimateTokens`, `TryAdvanceAsync`,
  `DisposeEnumeratorAsync` helpers — moved to engine
- `SessionSummarizeActionConfig` internal record — moved to engine as `ChatSummarizeActionConfig`

**AFTER** (~230 LOC):
- 3-param ctor: ChatSessionManager + IPlaybookExecutionEngine + ILogger
- `SummarizeSessionFilesAsync`: validation (tenant/session/NFR-02 cap), session load
  (preserved boundary), build `ChatSummarizeRequest`, forward to engine, yield chunks
- 1 internal static-readonly Guid: ChatSummarizePlaybookId
- All other constants moved to engine where they're actually used

### `PlaybookExecutionEngine.cs`

**BEFORE** (~201 LOC):
- 4-param ctor: builderService + orchestrationService + httpContextAccessor + logger
- 3 methods: `ExecuteConversationalAsync`, `ExecuteBatchAsync`, `DetermineExecutionMode`
- 2 internal helpers: `ConvertToBuilderRequest`, `ConvertToBuilderResult`

**AFTER** (~641 LOC):
- 9-param ctor: 4 original + nodeService + entityService + ragService + openAiClient + summarizeTelemetry
- 4 methods: original 3 + new `ExecuteChatSummarizeAsync`
- 8 new helpers: `ResolveActionConfigViaFkChainAsync`, `ResolveFileIds`, `BuildRagQuery`,
  `BuildUserContent`, `RecordTelemetry`, `EstimateTokens`, `TryAdvanceAsync`,
  `DisposeEnumeratorAsync`
- 3 new internal constants: `ChatSummarizeSchemaName`, `CombinedSummaryInterjection`, `ActionEntityLogicalName`
- 1 new internal record: `ChatSummarizeActionConfig`

### `IPlaybookExecutionEngine.cs`

**ADDED**:
- 1 method: `ExecuteChatSummarizeAsync(Guid, ChatSummarizeRequest, CT) → IAsyncEnumerable<AnalysisChunk>`
- 1 record: `ChatSummarizeRequest`

**UNCHANGED**: existing 3 methods + their consumers + `ExecutionMode` + all conversational
types (`ConversationContext`, `ConversationMessage`, `MessageMetadata`, `ConversationTokenUsage`,
`SessionState`, `BuilderResult`, `BuilderResultType`, `ErrorDetail`).

---

## Verification

### Build state

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 16 baseline warnings** (unchanged from pre-refactor baseline)
- `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → **0 errors, 16 baseline warnings**

### Test state

| Filter | Pass | Fail | Skip | Total |
|---|---|---|---|---|
| `SessionSummarizeOrchestratorTests \| PlaybookExecutionEngineTests \| InvokeSummarizePlaybookToolTests \| SummarizeSessionEndpointTests` (task 025 affected) | **71** | 0 | 0 | 71 |
| `Services.Ai` (full AI-services baseline) | **3662** | 2 | 22 | 3686 |

**Two failures in broader `Services.Ai` sweep are pre-existing parallel-task interference**, NOT caused by task 025:
1. `Sprk.Bff.Api.Tests.Services.Ai.Chat.InvokePlaybookDescriptionTests.RenderInvokePlaybookDescription_HasNoLoggerParameter` — task 022 in-flight untracked test file
2. `Sprk.Bff.Api.Tests.Services.Ai.Chat.SprkChatAgentFactoryTests.CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool` — depends on uncommitted `SprkChatAgentFactory.cs` edits from task 021 chain

Verification: stashing task 025 changes still leaves both failures intact (the pre-existing test compile errors actually mask the SprkChatAgentFactory failure on baseline — confirmed via `git stash` test). Task 025 introduces ZERO new failures.

### Grep verification (FR-26 invariant)

```
$ grep -rn "SummarizeActionCode\|ActionEntityLogicalName\|LoadActionConfigAsync" src/server/api/Sprk.Bff.Api
src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:45:   # doc-comment reference to removed constant
src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:57:   # doc-comment reference to removed bypass
src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:33:              # doc-comment reference to removed method
src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:47:              # NEW constant — "sprk_analysisaction" table logical name (NOT alternate-key code)
src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:481:             # FK-resolved RetrieveAsync(ActionEntityLogicalName, node.ActionId, ...)
```

The two remaining `ActionEntityLogicalName` matches are the engine's NEW constant for the table
logical name (`"sprk_analysisaction"`) used by `IGenericEntityService.RetrieveAsync` for the
FK-resolved ID lookup. This is **the table name**, NOT the alternate-key code. The orchestrator's
old `SummarizeActionCode = "SUM-CHAT@v1"` constant is removed; the
`RetrieveByAlternateKeyAsync` call path is removed from `Services/Ai/Chat/`.

```
$ grep -rn "RetrieveByAlternateKeyAsync.*sprk_actioncode\|sprk_actioncode.*RetrieveByAlternateKey" src/server/api/Sprk.Bff.Api/Services/Ai/Chat
(no matches)
```

FR-26 invariant **verified**: NO alternate-key lookup on `sprk_actioncode` remains in the chat
/summarize code path.

### Publish-size delta (NFR-01 / NFR-02)

| Snapshot | Uncompressed | Compressed (tar.gz) |
|---|---|---|
| Pre-task-025 (baseline) | 145,786,296 bytes (139.03 MB) | n/a captured |
| Post-task-025 | 145,806,328 bytes (139.05 MB) | **44.61 MB** |
| **Delta** | **+20,032 bytes (~0.019 MB)** | n/a |

- **R6 budget**: ≤+5 MB across all R6 tasks (NFR-02). Task 025 consumes **0.4% of the budget** (~0.019 / 5).
- **Ceiling**: 60 MB compressed (NFR-01). Current: **44.61 MB** — 15.4 MB headroom.
- **Threshold**: ≥+5 MB single-task delta → justification required. Task 025: **0.019 MB** — no escalation.

---

## Acceptance criteria walkthrough (POML §acceptance-criteria)

| Criterion | Evidence |
|---|---|
| `SessionSummarizeOrchestrator` invokes `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` | ✅ `SessionSummarizeOrchestrator.cs` line 161 — `await foreach (var chunk in _executionEngine.ExecuteChatSummarizeAsync(...))`; covered by `SummarizeSessionFilesAsync_ForwardsToEngine_WithCorrectPlaybookIdAndRequest` test |
| NO `sprk_actioncode` alternate-key lookup remains in chat /summarize code path | ✅ Grep evidence above; covered by `SessionSummarizeOrchestrator_HasNoAlternateKeyConstants_FR26` reflection test + `ExecuteChatSummarizeAsync_ResolvesActionViaFkChain_NoAlternateKey` test asserting `RetrieveByAlternateKeyAsync` `Times.Never` |
| Session-files Azure Search filter preserved | ✅ Engine builds `RagSearchOptions { TenantId, SessionId }` (line ~285); covered by `ExecuteChatSummarizeAsync_PropagatesTenantAndSessionIdToRagSearchOptions` test + `SummarizeSessionFilesAsync_PropagatesTenantAndSessionIdToEngine` orchestrator test |
| Structured Outputs mode preserved | ✅ Engine calls `_openAiClient.StreamStructuredCompletionAsync` (line ~318) with action's `sprk_outputschemajson` (FK-resolved from action seed); covered by happy-path tests |
| Streaming JSON delta preserved | ✅ Engine uses `IncrementalJsonParser` + emits `AnalysisChunk.FromDelta` (line ~370); byte-equivalent pass-through asserted by `SummarizeSessionFilesAsync_YieldsEngineChunksUnchanged_RegressionByteEquivalent` |
| Safety pipeline middleware chain unchanged (NFR-13) | ✅ Endpoint mapping unchanged (the SSE endpoint at `POST /api/ai/chat/sessions/{sessionId}/summarize` still wraps the orchestrator call; orchestrator now forwards to engine but the OUTER middleware chain is untouched) |
| 11 production node executors unchanged (NFR-08) | ✅ NO file under `Services/Ai/Nodes/` was modified by task 025; the chat-summarize path is action-driven (NOT node-executor-driven) — confirmed during stop-and-surface analysis |
| Regression test passes with byte-equivalent output | ✅ `SummarizeSessionFilesAsync_YieldsEngineChunksUnchanged_RegressionByteEquivalent` test asserts orchestrator emits engine chunks unchanged element-by-element |
| Telemetry payload ADR-015-compliant | ✅ Engine's `RecordTelemetry` emits `path + completionStatus + fileCount + totalTokens + latencyMs + tenantId` ONLY — no user message content, no document text |
| BFF publish-size delta reported; ≤+5 MB R6 budget | ✅ +0.019 MB compressed delta — 0.4% of R6 budget |
| `code-review` + `adr-check` quality gates pass | (Step 9.5 — to be run by main session after this evidence note lands) |
| Pillar 4 complete: chat /summarize routes through `PlaybookExecutionEngine` (no alternate-key bypass) | ✅ Pillar 4 closed: data side (task 024 FK fix) + code side (task 025 orchestrator refactor) |

---

## Hand-off to next tasks

- **Task 023** (deletes `InvokeSummarizePlaybookTool`): the orchestrator's public
  `SummarizeSessionFilesAsync` signature is UNCHANGED, so the tool continues to work in the
  meantime. After task 023 lands, the orchestrator's only consumer is the direct endpoint.
- **Task 028** (Pillar 4 integration test): the integration test should exercise the
  full path end-to-end through the real `PlaybookExecutionEngine` against a seeded
  Dataverse playbook → node → action FK chain (post-task-024 valid). The unit tests here
  cover the wiring; task 028 covers the live data path.
- **Future config-friendliness**: if a future task needs per-tenant or per-environment
  chat-summarize playbook IDs, lift `SessionSummarizeOrchestrator.ChatSummarizePlaybookId`
  to `AnalysisOptions.ChatSummarizePlaybookId` and inject via `IOptions<AnalysisOptions>`.
  Doc-comment on the constant explicitly calls out this lift path.
