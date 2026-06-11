# Task 020 — IInvokePlaybookAi Facade Evidence

> **Task**: D-A-12 Add New `IInvokePlaybookAi` Facade in `Services/Ai/PublicContracts/` (Pillar 3, Q11)
> **Date**: 2026-06-08
> **Rigor**: FULL (new PublicContracts surface; ADR-013 binding; Confirmation Trigger; downstream blocker for task 021)
> **Status**: Implementation complete; awaiting confirmation gate per project CLAUDE.md §Confirmation Triggers

---

## What was built

### Files created

1. **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs`** (~175 lines)
   - Interface `IInvokePlaybookAi` with single method `InvokePlaybookAsync(playbookId, parameters, context, ct) → PlaybookInvocationResult`
   - DTO `PlaybookInvocationContext` (TenantId, HttpContext, CorrelationId?)
   - DTO `PlaybookInvocationResult` (RunId, Success, TextContent, StructuredData?, Citations, Confidence?, Duration, ErrorMessage?, ErrorCode?)

2. **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs`** (~165 lines)
   - Implementation delegates to `IPlaybookOrchestrationService.ExecuteAsync`
   - Consumes the SSE event stream; aggregates terminal `NodeOutput` text + structured data + Wave-7b citation envelopes from `ToolResult.Metadata` into the domain-shape result
   - Prefers `IsDeliverOutput == true` node when present; falls back to first successful node
   - ADR-015-compliant logging: playbookId + tenantId + runId + decision + parameter COUNT (never values)

3. **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/NullInvokePlaybookAi.cs`** (~60 lines)
   - ADR-032 P3 Fail-fast pattern — throws `FeatureDisabledException("ai.playbook-invocation.disabled", ...)` synchronously
   - Stable error code constant (`NullInvokePlaybookAi.ErrorCode`) exposed for test assertions

### Files modified

4. **`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`** (+18 lines)
   - Real impl registered in `AddPublicContractsFacade` (compound-AI-ON branch, scoped, alongside the other 4 facades)
   - Null peer registered in `AddNullObjectsForCompoundOff` (alongside the other 4 PublicContracts null peers)
   - Symmetric registration verified per the asymmetric-registration anti-pattern guard (CLAUDE.md §10 F.1)
   - ZERO new Program.cs lines (ADR-010 compliant)
   - ZERO new NuGet packages
   - ZERO new feature flags (ADR-018 compliant)

### Tests created

5. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PublicContracts/InvokePlaybookAiTests.cs`** (~360 lines, 14 tests, all passing)
   - **ADR-013 facade boundary**: reflection-based assertion that the public surface (`IInvokePlaybookAi`, `PlaybookInvocationResult`, `PlaybookInvocationContext`) does NOT reference `PlaybookStreamEvent`, `NodeOutput`, `PlaybookRunMetrics`, `PlaybookEventType`, or `IPlaybookOrchestrationService`
   - **Delegation**: facade forwards playbookId + parameters + HttpContext to `IPlaybookOrchestrationService.ExecuteAsync`; documentIds defaults to empty (invoke_playbook is parameters-only)
   - **Aggregation**: terminal `DeliverOutput` node text + structured data + confidence projected into result
   - **Citation accumulation**: `ToolResult.Metadata[ToolResultMetadataKeys.Citations]` envelopes flow into `result.Citations` (Wave 7b shape preserved)
   - **Failure paths**: `RunFailed` → `Success=false` + `ErrorMessage` + `ErrorCode="PLAYBOOK_INVOCATION_FAILED"`
   - **Cancellation**: `OperationCanceledException` propagates from orchestrator
   - **Argument validation**: empty playbookId throws `ArgumentException`; null context throws `ArgumentNullException`; ctor null-guards
   - **ADR-015 telemetry hygiene**: sentinel-string scan — captured log messages MUST NOT contain parameter VALUES (only counts + IDs)
   - **Null peer**: `NullInvokePlaybookAi.InvokePlaybookAsync` throws `FeatureDisabledException` with stable `"ai.playbook-invocation.disabled"` code; integrates with `AsFeatureDisabled503()`

---

## Design decisions

### D1 — Non-streaming return shape (`Task<PlaybookInvocationResult>`, not `IAsyncEnumerable<...>`)

The intended consumers (task 021 `InvokePlaybookHandler` for chat-tool dispatch + future M365 Copilot adapter) need a single typed tool-call result, not progressive UX updates. The facade consumes the underlying SSE stream internally and aggregates outputs. This mirrors `IInsightsAi.AnswerQuestionAsync` (synthesis result) rather than `IWorkspacePrefillAi.ExecutePlaybookAsync` (wizard SSE).

**Trade-off considered**: a streaming `IAsyncEnumerable<PlaybookInvocationStreamEvent>` shape would preserve progressive output. For task 020 + task 021 scope this is not required — the chat-tool surfaces a single function-call response. If a future consumer needs streaming, a sibling streaming method can be added additively (mirroring `IInsightsAi.AssistantQueryStreamAsync` next to the non-streaming `AssistantQueryAsync`).

### D2 — `HttpContext` on the input DTO

`IPlaybookOrchestrationService.ExecuteAsync` requires `HttpContext` for OBO authentication inside playbook node executors. `HttpContext` is an ASP.NET primitive (not an AI-internal type per ADR-013), so it is acceptable on the facade — the existing `IWorkspacePrefillAi.ExecutePlaybookAsync` applies the same pattern. This avoids forcing the facade to discover HttpContext via `IHttpContextAccessor` (which would couple the facade to web-host plumbing the M365 Copilot adapter does not have).

### D3 — ADR-032 P3 Fail-fast pattern (not P2 Quiet)

`IInvokePlaybookAi` is a command surface — callers expect playbook execution, not a no-op. A quiet success would silently route every LLM `invoke_playbook` tool call to a fake "no output" response and break trust in the tool ecosystem. Same reasoning applied across `NullPlaybookOrchestrationService`, `NullBriefingAi`, `NullInsightsAi`.

### D4 — `documentIds` set to empty in the orchestration request

The facade's `InvokePlaybookAsync` signature does NOT take documentIds — invoke_playbook callers (chat tool, M365 Copilot) pass parameters only. The orchestration service interprets empty documentIds as "no document context" (consistent with the existing M365 Copilot adapter path). If a future consumer needs to pass documents, a separate facade method or input-DTO field can be added additively.

### D5 — Citation accumulation reads typed envelopes only

`AccumulateCitationsFromToolResults` accepts `IEnumerable<ToolResultCitation>` from `ToolResult.Metadata`. The JSON-shape pass-through documented on `ToolResultMetadataKeys.Citations` is intentionally NOT silently handled — the only producer upstream of this facade today is the chat-tool adapter (Wave 7b) which uses the typed envelope. If a future producer emits JSON-shape citations, extend with explicit deserialization rather than silent best-effort.

---

## Stop-and-surface items

**None.** The orchestration service's public surface is clean for facade wrapping:
- `ExecuteAsync` returns `IAsyncEnumerable<PlaybookStreamEvent>` — internally consumed, not surfaced
- `NodeOutput.IsDeliverOutput` discriminates the terminal output node cleanly
- `ToolResult.Metadata` (Wave 7b) already carries citations in a domain-friendly shape
- `FeatureDisabledException` + `AsFeatureDisabled503()` integration works the same as for the other 4 facades

No ADR conflicts surfaced. No DI module changes required beyond adding 2 lines to existing methods.

---

## Verification results

### Build
```
dotnet build src/server/api/Sprk.Bff.Api/ --nologo -v q
    16 Warning(s)
    0 Error(s)
```
Same baseline as task start (16 warnings, all pre-existing per the master sweep).

### Tests
```
dotnet test ... --filter "FullyQualifiedName~InvokePlaybookAiTests"
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 72 ms
```
All 14 new tests pass.

```
dotnet test ... --filter "FullyQualifiedName~Services.Ai"
Passed!  - Failed:     0, Passed:  3608, Skipped:    22, Total:  3630, Duration: 16 s
```
Broader AI services regression: 3608 passed (vs 3594 baseline; +14 from new tests), 22 skipped (pre-existing), 0 failed.

### Publish size
```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o /tmp/r6-task020-publish
Compressed publish (tgz): 44.60 MB
```
Δ vs post-Wave-9 baseline (~45.65 MB): **~-1.05 MB** (minor decrease, normal variance from incremental rebuild). Well within ≤+5 MB R6 budget and far below the 60 MB ceiling. No new NuGet packages added.

---

## Acceptance criteria verification (POML §acceptance-criteria)

- [x] `IInvokePlaybookAi.cs` exists in `Services/Ai/PublicContracts/` with `InvokePlaybookAsync` method.
- [x] Implementation delegates to `IPlaybookOrchestrationService` without exposing AI internals through facade surface (ADR-013) — verified by reflection-based test `Facade_PublicSurface_DoesNotLeakAiInternalTypes`.
- [x] Registered inside `AnalysisServicesModule`; ZERO new Program.cs lines (ADR-010).
- [x] Telemetry: playbookId + decision + timestamp only; no user content (ADR-015) — verified by `InvokePlaybookAsync_TelemetryHygiene_NoParameterValuesInLogs` sentinel-string scan.
- [ ] **User confirmation** recorded for public contract surface change — **PENDING main session per Confirmation Trigger gate** (task POML §step 6).
- [x] BFF publish-size delta reported (~-1.05 MB; well within budget); ≤+5 MB R6 budget compliant.
- [ ] `code-review` + `adr-check` quality gates — **DEFERRED to main session per task-execute Step 9.5** (sub-agent scope does not include skill invocation).

---

## ADR-032 pattern choice: P3 Fail-fast

Same pattern as the other 4 PublicContracts null peers (`NullBriefingAi`, `NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`). Rationale: `IInvokePlaybookAi` is a command surface where silent no-op would mislead operators and break LLM tool-call trust.

---

## What this unblocks

- **Task 021** — `InvokePlaybookHandler` chat-tool handler can now inject `IInvokePlaybookAi` and translate `invoke_playbook(playbookId, parameters)` tool calls into a single facade call, projecting the result into a chat-tool `ToolResult` (Summary, Data, Metadata.Citations).
- **Future R7+** — M365 Copilot agent gateway can consume the same facade without retrofitting the Zone A boundary.

---

## What this does NOT change

- `IPlaybookOrchestrationService` — surface unchanged
- `IPlaybookExecutionEngine` — surface unchanged
- `IOpenAiClient` — surface unchanged
- 11 node executors — unchanged (NFR-08 compliant)
- Pre-fill flow (`MatterPreFillService` / `ProjectPreFillService`) — unchanged (NFR-07 compliant)
- Program.cs — unchanged (ADR-010 compliant)
- Existing `sprk_analysistool` rows — none modified
- No new feature flags (ADR-018 compliant)
- No new ADRs (NFR-03 compliant)
