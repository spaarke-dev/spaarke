# Task 015 BFF Publish Delta + FR-05 First Stable-ID Migration

> **Generated**: 2026-06-22
> **Task**: 015 — Migrate `SessionSummarizeOrchestrator` to stable ID (FR-05 first)
> **Wave**: 1-D (FR-05 sequencing prerequisite; unblocks Wave 1-E)
> **Phase 0 baseline**: 44.75 MB compressed
> **Post-Wave 1-B baseline (per `013-bff-publish-delta.md`)**: 44.75 MB compressed (46,927,543 bytes)

## Measurements

| Stage | Compressed (MB) | Compressed (bytes) | Delta vs Wave-1-B baseline |
|---|---|---|---|
| Post-task-013 (Wave 1-B baseline) | 44.75 | 46,927,543 | — |
| Post-task-015 (this task — orchestrator stable-ID migration + tests) | **44.75** | **46,927,919** | **+376 bytes (≈ 0 MB; within measurement noise)** |

## NFR-01 status

- Cumulative delta after Wave 1-A + Wave 1-B + Wave 1-D (task 015): **+376 bytes** vs post-task-013 baseline (≈ **0 MB** vs Phase 0).
- Hard ceiling 60 MB → **44.75 MB measured**, **15.25 MB headroom**.
- Escalation threshold 55 MB → headroom 10.25 MB.
- Single-task escalation threshold (+5 MB) → not approached.

## What changed in this task

FR-05 FIRST stable-ID migration: the hardcoded `internal static readonly Guid ChatSummarizePlaybookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4")` constant at `SessionSummarizeOrchestrator.cs:78-79` was REMOVED. The orchestrator now resolves the playbook at runtime via `IPlaybookLookupService.GetByIdAsync(WorkspaceOptions.ChatSummarizePlaybookId)` per ADR-018 typed options + Pattern A stable-ID resolution. Empty config fails fast (no hardcoded fallback at the chat /summarize convergence point per R6 FR-26).

### Migration shape chosen

**Typed options + IPlaybookLookupService.GetByIdAsync** — same pattern as `InvoiceExtractionJobHandler` (commit `34aef1d01`), adapted to throw `InvalidOperationException` rather than mark-failed because the orchestrator is the chat /summarize convergence point (R6 FR-26).

- Inject `IOptions<WorkspaceOptions>` (already wired in `ConfigurationModule.cs`, populated per task 013).
- Inject `IPlaybookLookupService` (already wired in `FinanceModule.cs` as Scoped, registered prior to this task by R2 invoice work).
- Resolve at runtime via `await _playbookLookup.GetByIdAsync(_workspaceOptions.Value.ChatSummarizePlaybookId, ct)`.
- Forward `playbook.Id` (Guid) to `_executionEngine.ExecuteChatSummarizeAsync(...)` — engine signature unchanged.
- Empty config → fail-fast `InvalidOperationException` with operator-actionable message naming the missing config key.

### Files modified (exactly 3)

1. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs`** (+45 lines, -10 lines net)
   - Removed `internal static readonly Guid ChatSummarizePlaybookId` constant (lines 70-79 of prior version).
   - Added 2 ctor parameters: `IPlaybookLookupService playbookLookup`, `IOptions<WorkspaceOptions> workspaceOptions`.
   - Nulled both fields in the `NullSessionSummarizeOrchestrator` protected-ctor surface (kill-switch contract preserved).
   - Added typed-options read + lookup-service call in `SummarizeSessionFilesAsync` before the engine forward.
   - Added empty-config guard that logs operator-actionable error + throws `InvalidOperationException`.
   - Added FR-05 stable-ID resolution paragraph to class XML doc (`<remarks>`).
   - Public `SummarizeSessionFilesAsync` signature UNCHANGED — convergence invariant preserved.

2. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SessionSummarizeOrchestratorTests.cs`** (+114 lines, -8 lines net)
   - Added `IOptions<WorkspaceOptions>` + `Mock<IPlaybookLookupService>` to fixture.
   - Configured DEV GUID (`44285d15-1360-f111-ab0b-70a8a59455f4`) in test options; lookup stub returns `PlaybookResponse` with matching `Id` so the engine call sees the unchanged Guid (FR-26 convergence preserved).
   - Updated 2 existing tests that referenced the removed `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` constant or compared against the literal GUID — now compare against the configured-then-resolved Guid.
   - Added 3 new tests:
     - `SessionSummarizeOrchestrator_HasNoHardcodedChatSummarizePlaybookIdConstant_FR05` (reflection assert; constant gone).
     - `SummarizeSessionFilesAsync_ResolvesPlaybookViaLookupService_UsingConfiguredOptionValue` (pins typed-options → lookup-service contract).
     - `SummarizeSessionFilesAsync_EmptyConfiguredId_ThrowsInvalidOperationException` (fail-fast verification; lookup + engine MUST NOT be called).

3. **`tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs`** (+38 lines)
   - Added `Mock<IPlaybookLookupService> PlaybookLookupMock` fixture property + reset hook.
   - Added in-process DI registration: `IPlaybookLookupService` (singleton mock) + `services.Configure<WorkspaceOptions>(o => o.ChatSummarizePlaybookId = ConfiguredChatSummarizePlaybookId)`.
   - Added default stub in `ConfigureDefaults` that returns `PlaybookResponse { Id = ChatSummarizePlaybookId }` so the in-process happy-path SSE test keeps yielding the same engine FK-chain Guid (matches the pre-existing fixture's `ChatSummarizePlaybookId` internal constant — preserved verbatim because the engine's FK-chain stub needs the same Guid).
   - Added `using Sprk.Bff.Api.Models.Ai;` for `PlaybookResponse`.

## Scope check

- No DI module changes — `IPlaybookLookupService` is already Scoped (`FinanceModule.cs:115`); `WorkspaceOptions` is already bound (`ConfigurationModule.cs:111`). The two `AddScoped<SessionSummarizeOrchestrator>()` registrations (in `AnalysisServicesModule.AddAnalysisOrchestrationServices` and the Null mirror) auto-resolve the new constructor dependencies.
- No `appsettings.template.json` change required — `Workspace:ChatSummarizePlaybookId` was pre-seated by task 013 (empty default + comment); orchestrator's empty-config fail-fast logs the missing key on misconfigured environments.
- `NullSessionSummarizeOrchestrator` (kill-switch P3) UNCHANGED — uses the protected `ILogger`-only ctor, never resolves AI deps.

## Test outcome

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 16 warnings (all pre-existing)**
- `dotnet test ... --filter "FullyQualifiedName~SessionSummarize"` → **17/17 passed, 0 failed, 0 skipped, 132 ms** (12 pre-existing + 5 updated/added test methods; the FR-26 reflection invariant + the new FR-05 hardcoded-constant-removal + lookup-contract + fail-fast tests).
- `dotnet test ... --filter "FullyQualifiedName~SummarizeSessionEndpoint"` → **11/11 passed, 0 failed, 0 skipped, 78 ms** (all in-process SSE / 400 / 404 / 503 / ProblemDetails / fresh-token-per-request tests).
- Broader regression sweep (`Chat | WorkspaceOptions | PlaybookLookup` filter) → **1099/1099 passed, 12 skipped (pre-existing), 0 failed, ~5 s**.

## Acceptance criteria

| Criterion | Status |
|---|---|
| `grep "44285d15" src/` returns zero hits OR only in migration-history comments | ✅ 2 hits, both in XML doc comments documenting the migration (POML explicitly allows this) |
| `grep "ChatSummarizePlaybookId" src/` returns zero hits OR only as typed-options property name | ✅ All hits are `WorkspaceOptions.ChatSummarizePlaybookId` (property/config key) — never as a hardcoded orchestrator constant |
| Unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/` exit 0 | ✅ 17/17 pass |
| Endpoint tests for `/api/ai/chat/sessions/{id}/summarize` exit 0 | ✅ 11/11 pass (in-process SSE end-to-end via real engine FK-chain) |
| Telemetry "path" dimension on `R5SummarizeTelemetry` retains existing dimensions | ✅ `SummarizeInvocationPath` enum + `ToTelemetryValue` extension UNCHANGED; engine emits same `path=direct_endpoint` / `agent_tool` (tests `_AgentToolPath_` + `_DirectEndpointPath_` still pass) |
| FR-26 convergence invariant (slash + NL → same playbook) preserved | ✅ tests `_ForwardsToEngine_WithCorrectPlaybookIdAndRequest`, `_UsesFkChainPlaybookId_NotAlternateKeyCode`, `_HasNoAlternateKeyConstants_FR26`, `_PropagatesTenantAndSessionIdToEngine` all green; orchestrator's single `SummarizeSessionFilesAsync` convergence method preserved (test `_ExposesExactlyOneConvergenceMethod` green) |
| FR-30 dedup invariant preserved | ✅ orchestrator is a thin pass-through to engine; chunk shape + ordering unchanged (test `_YieldsEngineChunksUnchanged_RegressionByteEquivalent` green); chat + workspace SUM-CHAT siblings still dedup to a single render via the engine's chunk stream (FR-30 lives downstream in the chat-render layer and was not in this task's blast radius) |
| code-review + adr-check exit 0 | ⏳ executed below |

## R6 FR-26 + FR-30 Invariant Preservation Evidence

- **FR-26 convergence**: the orchestrator's public `SummarizeSessionFilesAsync` signature is BYTE-IDENTICAL to the prior version (same `SummarizeSessionFilesRequest` shape, same `IAsyncEnumerable<AnalysisChunk>` return). Both the direct endpoint (`SummarizeSessionEndpoint`) AND the agent-tool dispatcher (`InvokeSummarizePlaybookTool`) still converge on this single method. Per-test:
  - `SummarizeSessionFilesAsync_ForwardsToEngine_WithCorrectPlaybookIdAndRequest` — pins the engine call.
  - `SummarizeSessionFilesAsync_UsesFkChainPlaybookId_NotAlternateKeyCode` — pins the resolved Guid (no alternate-key path).
  - `SessionSummarizeOrchestrator_HasNoAlternateKeyConstants_FR26` — reflection assert that pre-R6 constants stayed removed.
  - `SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod` — reflection assert on the public surface; FR-01 + FR-08 + SC-08 contract.
- **FR-30 dedup**: chunk shape + ordering preserved (test `_YieldsEngineChunksUnchanged_RegressionByteEquivalent` asserts byte-equivalent pass-through). FR-30 dedup logic lives in the workspace chat-render layer (not modified by this task). No regression possible from this orchestrator change.

## Notes / Unexpected findings

- The POML stated the constant was at lines 78-79; actual location was 78-79 confirmed.
- The POML estimated lookup signature `GetByCodeAsync` — already corrected at task creation per the Q1 refactor; the actual API is `GetByIdAsync(string playbookId, CancellationToken ct)` as documented in the project CLAUDE.md and consumed identically in `InvoiceExtractionJobHandler.cs:324`.
- The orchestrator was NOT `sealed` (it's the base class of `NullSessionSummarizeOrchestrator`). The protected `ILogger`-only ctor mode for the Null kill-switch is preserved — nulled `_playbookLookup` + `_workspaceOptions` fields, never dereferenced (Null override throws `FeatureDisabledException` first).
- `IPlaybookLookupService` is Scoped (Singleton would have caused captive-dependency warnings via `IGenericEntityService` Scoped); orchestrator is Scoped → matches.
- No InvoiceExtractionJobHandler-style "mark failed" fallback adopted: the orchestrator throws because the chat /summarize convergence point is too important to silently downgrade. Error is mapped by `SummarizeSessionEndpoint` to a 503/500 ProblemDetails per its existing exception-handler.
