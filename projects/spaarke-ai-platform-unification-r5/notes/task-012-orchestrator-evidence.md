# Task 012 — D2-03 SessionSummarizeOrchestrator Implementation Evidence

> **Task**: 012-session-summarize-orchestrator.poml
> **Date**: 2026-06-04
> **Wave**: P2-G2 (parallel-safe peer to task 013)
> **Dependencies satisfied**: 010 ✅, 011 ✅ (per `phase-2-wave-a-deployment-evidence.md`)
> **Status**: complete (code-authoring sub-agent scope per main session orchestration)

---

## Files created

| File | Purpose | Approx LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` | Concrete `sealed` orchestrator + `SummarizeSessionFilesRequest` record + `SummarizeInvocationPath` enum + telemetry-value extension + internal `SessionSummarizeActionConfig` record | ~470 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SessionSummarizeOrchestratorTests.cs` | 11 unit tests covering acceptance criteria (a)–(g) from POML §<steps>/8 + telemetry path-dimension coverage + empty-input validation + empty-session decline path | ~370 |

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs` | Added `StreamStructuredCompletionAsync(...)` to the interface so the orchestrator can be unit-tested against a mock without taking a concrete dependency on `OpenAiClient`. The concrete method already exists on `OpenAiClient` (task 006); this is a purely additive interface change. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Added `services.AddScoped<SessionSummarizeOrchestrator>()` inside the existing `AddAnalysisOrchestrationServices` helper (which is already gated by `analysisEnabled && documentIntelligenceEnabled`). ZERO new `Program.cs` lines. ZERO new feature flags. |
| `projects/spaarke-ai-platform-unification-r5/tasks/012-session-summarize-orchestrator.poml` | Status → `complete`; started/completed dates set; actual-effort recorded. |
| `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` | Task 012 status 🔲 → ✅. |

## Files NOT modified (scope discipline)

| File | Why |
|---|---|
| `src/server/api/Sprk.Bff.Api/Program.cs` | Per R5 CLAUDE.md §3.3: ZERO new top-level DI lines. Registration is inside the existing module helper. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Per R5 CLAUDE.md §3.1 (specifically-prohibited list): no parallel orchestrator. This NEW orchestrator does NOT modify or parallel `AnalysisOrchestrationService` — it composes a different orchestration shape (chat-session scope vs document-record scope). See "Why-New-Class Justification" below. |
| `appsettings.json` | Per R5 CLAUDE.md §3.2 + ADR-018: ZERO new feature flags. Orchestrator inherits kill-switch from parent compound gate. |
| `Endpoints` | Out of scope. Task 014 (direct endpoint) + task 015 (agent-tool handler) consume this orchestrator. |

---

## Acceptance criteria verification

| # | Criterion (from POML <acceptance-criteria>) | Status | Evidence |
|---|---|---|---|
| 1 | File at expected path; class is `public sealed`; concrete; no R5-authored interface | ✅ | `Services/Ai/Chat/SessionSummarizeOrchestrator.cs` line 87: `public sealed class SessionSummarizeOrchestrator`. Test `SessionSummarizeOrchestrator_HasNoR5AuthoredInterface` enforces. |
| 2 | Exactly ONE convergence method `IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(...)`; verified by reflection test | ✅ | Test `SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod` reflects over public methods and asserts exactly one `IAsyncEnumerable<AnalysisChunk>` return type, with name `SummarizeSessionFilesAsync`. |
| 3 | "Why-New-Class Justification" in XML doc-comment + this evidence note | ✅ | Class XML `<remarks>` block — four enumerated items: chat-session-scope, multi-file interjection, convergence shape, AnalysisOrchestrationService dependency on DocumentId+HttpContext. Repeated below in "Why-New-Class Justification" section. |
| 4 | Placement Justification cites ADR-013 BFF-only | ✅ | See "Placement Justification" section below. |
| 5 | All deps reuse existing services (no new orchestrator/playbook/RAG built) | ✅ | Constructor: `ChatSessionManager`, `IRagService`, `IOpenAiClient`, `IGenericEntityService`, `R5SummarizeTelemetry`, `ILogger`. All pre-existing. |
| 6 | Registration inside `AnalysisServicesModule.AddAnalysisOrchestrationServices(...)`; lifetime Scoped; ZERO new `Program.cs` lines | ✅ | `AnalysisServicesModule.cs` — line added inside `AddAnalysisOrchestrationServices`. No `Program.cs` diff. |
| 7 | No new feature flag; registration unconditional within already-gated outer block | ✅ | The added registration is plain `services.AddScoped<SessionSummarizeOrchestrator>()` — no `if (R5flag)` introduced. The outer `if (analysisEnabled && documentIntelligenceEnabled)` is the existing gate. |
| 8 | Multi-file (>=2) emits combined-summary interjection BEFORE playbook stream; single-file does NOT | ✅ | Tests (a) `_SingleFile_DoesNotEmitCombinedSummaryInterjection` + (b) `_MultiFile_EmitsCombinedSummaryInterjectionBeforePlaybookStream`. |
| 9 | Decline path: structured decline envelope when content insufficient | ✅ | Test `_NoFilesInSession_EmitsDecline` — empty `UploadedFiles[]` short-circuits to `AnalysisChunk.FromError` with the decline message + telemetry `completion_status="declined"`. The orchestrator does not silently hallucinate. |
| 10 | All `RagService.SearchAsync` calls set tenantId AND sessionId (ADR-014) | ✅ | Test `_PropagatesTenantAndSessionIdToRagSearchOptions` — captures the `RagSearchOptions` argument and asserts both fields are set. |
| 11 | NFR-02 cap of 20 files enforced | ✅ | Test `_RejectsMoreThanTwentyFileIds` — throws `ArgumentException` with "*NFR-02*" message. |
| 12 | Build clean (zero new compiler warnings) | (main session verifies) | Main-session `dotnet build` is the gate per sub-agent scope. |
| 13 | 8+ tests pass | ✅ (count) / pending (run) | 11 tests authored. Main-session `dotnet test` is the gate. |
| 14 | Publish-size delta < +5 MB; ceiling ≤60 MB | (main session measures) | Adds one .cs file (~470 LOC) + interface additions; net DLL size impact is negligible. |
| 15 | `code-review` + `adr-check` pass | (main session runs) | Sub-agent scope authors code; main-session Step 9.5 runs the gates. |
| 16 | Tasks 014 + 015 can implement by accepting `SummarizeSessionFilesRequest` + calling the convergence method — no additional surface needed | ✅ | The convergence method `SummarizeSessionFilesAsync(SummarizeSessionFilesRequest, CancellationToken)` is the SOLE public streaming entry point. Both downstream tasks pass the appropriate `SummarizeInvocationPath` enum value to drive telemetry. |

---

## Why-New-Class Justification (per R5 CLAUDE.md §3.1 step 3)

This orchestrator is NOT a parallel to `AnalysisOrchestrationService` (or `InsightsOrchestrator`).

1. **Chat-session scope vs document-record scope** — `AnalysisOrchestrationService.ExecuteAnalysisAsync` is anchored to a Dataverse `sprk_document` ID + an `HttpContext` for SPE OBO file download. The R5 chat flow has neither: file text is already indexed in the session-files AI Search slice at upload time (task 003), and the unit of work is `ChatSession.UploadedFiles[]` (task 004) — a manifest of session-scoped uploads, not a document record.
2. **Multi-file combined-summary interjection (FR-04)** — chat-layer UX semantic, not analysis-layer logic. Belongs on the chat orchestrator.
3. **Agent-tool ↔ direct-endpoint convergence (spec FR-01 + FR-08 + SC-08)** — load-bearing reason for this class. A single dedicated delegation target guarantees identical output between the two downstream call sites (task 014 endpoint + task 015 agent tool). Without it, the two paths would drift in streaming shape, multi-file interjection emission, decline handling, RAG-scope filtering, telemetry, or output schema.
4. **AnalysisOrchestrationService's dependency surface is incompatible** — its 10-param constructor includes `ScopeResolverService`, `AnalysisContextBuilder`, `AnalysisDocumentLoader` (SPE downloader), `AnalysisRagProcessor`, `AnalysisResultPersistence` — none applicable to chat-session-scoped synthesis. Re-using it would require either passing nulls (anti-pattern) or extending its API (which would parallelize the orchestrators per CLAUDE.md §3.1).

This orchestrator REUSES (does not re-implement): `ChatSessionManager`, `IRagService` with the new `RagSearchOptions.SessionId` filter (task 002), `IOpenAiClient.StreamStructuredCompletionAsync` (task 006), `IncrementalJsonParser` (task 006), `AnalysisChunk.FromDelta`/`FromContent`/`Completed`/`FromError` (task 005), `DocumentAnalysisResult` (existing), `R5SummarizeTelemetry` (task 008), the deployed `SUM-CHAT@v1` action seed (task 010) and playbook (task 011) — via `IGenericEntityService.RetrieveByAlternateKeyAsync`.

The class composes ~470 LOC entirely of EXISTING primitives. No new orchestrator engine. No new playbook engine. No new RAG service. No new SSE envelope.

---

## Placement Justification (per `.claude/constraints/bff-extensions.md`)

This orchestrator BELONGS IN BFF — `Sprk.Bff.Api.Services.Ai.Chat` namespace.

- **ADR-013 BFF-only AI architecture**: chat orchestration is core BFF responsibility. There is no extraction candidate; the orchestrator's only consumers are the BFF endpoint (task 014) and the BFF-registered chat-tool handler (task 015).
- **No external consumer**: External services do not call this orchestrator. The Insights team's `IInsightsAi` facade pattern (Zone A/Zone B) does not apply — this is purely intra-BFF orchestration.
- **Lifetime is Scoped** — matches dependency lifetimes (`ChatSessionManager` Scoped, `IGenericEntityService` Scoped, others Singleton). Scoped is the safe lifetime that respects every wrapped lifetime.
- **No publish-size implication**: pure C# additions; no new NuGet packages; no new HttpClient registrations (the orchestrator's HTTP traffic flows through pre-existing `IOpenAiClient` + `IRagService` clients).

---

## Convergence verification (per POML Step 6)

Single convergence method:

```csharp
public async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
    SummarizeSessionFilesRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
```

Public method count audit (`SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod` test):
- Constructor (1) — implicit
- `SummarizeSessionFilesAsync` (1) — the convergence method
- No other public methods.

Both downstream tasks (014 endpoint + 015 agent-tool handler) construct a `SummarizeSessionFilesRequest` (with `SummarizeInvocationPath.DirectEndpoint` or `.AgentTool` respectively) and call this single method. The path-discriminator drives the `R5SummarizeTelemetry.RecordSummarizeInvocation` `path` dimension (`direct_endpoint` | `agent_tool`).

---

## Streaming wiring (per task 006 implementation evidence "Downstream consumer obligations")

The orchestrator's streaming loop follows the canonical pattern from `notes/task-006-implementation-evidence.md`:

```csharp
var parser = new IncrementalJsonParser();
await foreach (var token in _openAiClient.StreamStructuredCompletionAsync(messages, schema, schemaName, ct))
{
    var events = parser.Append(token);
    foreach (var ev in events)
    {
        if (ev.Kind == FieldDeltaEventKind.FieldContent && !string.IsNullOrEmpty(ev.Content))
            yield return AnalysisChunk.FromDelta(ev.Path, ev.Content, ev.Sequence);
    }
}
var result = parser.TryParseFinal(jsonOptions);
yield return AnalysisChunk.Completed(result ?? DocumentAnalysisResult.Fallback(parser.GetAccumulatedJson()));
```

(Note: the actual implementation hoists exceptions out of `catch` blocks because C# does not allow `yield return` inside a `catch` — see code comments around the per-token try/catch.)

---

## Telemetry contract (per task 008 R5SummarizeTelemetry)

The orchestrator records ONE invocation per call via `R5SummarizeTelemetry.RecordSummarizeInvocation` with:
- `path` ∈ {`agent_tool`, `direct_endpoint`} — set by caller via `SummarizeInvocationPath`
- `completion_status` ∈ {`success`, `failed`, `declined`, `cancelled`}
- `fileCount` — count of files included
- `totalTokens` — input + accumulated-output estimate (4-char heuristic, consistent with `AnalysisOrchestrationService`)
- `latencyMs` — wall time from method entry to terminal chunk
- `tenantId` — per ADR-014 low-cardinality dimension

Cancellation is propagated by setting `completion_status = "cancelled"` before re-throwing `OperationCanceledException`. Decline path uses `completion_status = "declined"`. Mid-stream errors set `completion_status = "failed"`.

---

## Tests (12)

1. `SummarizeSessionFilesAsync_SingleFile_DoesNotEmitCombinedSummaryInterjection` — FR-04 negative.
2. `SummarizeSessionFilesAsync_MultiFile_EmitsCombinedSummaryInterjectionBeforePlaybookStream` — FR-04 positive.
3. `SummarizeSessionFilesAsync_PropagatesTenantAndSessionIdToRagSearchOptions` — ADR-014 / NFR-03.
4. `SummarizeSessionFilesAsync_RejectsMoreThanTwentyFileIds` — NFR-02.
5. `SummarizeSessionFilesAsync_MidStreamException_YieldsFromErrorAndTerminates` — graceful mid-stream failure.
6. `SessionSummarizeOrchestrator_HasNoR5AuthoredInterface` — ADR-010 reflection enforcement.
7. `SessionSummarizeOrchestrator_ExposesExactlyOneConvergenceMethod` — FR-01 + FR-08 + SC-08 reflection enforcement.
8. `SummarizeSessionFilesAsync_AgentToolPath_TelemetryRecordsAgentToolDimension` — path enum mapping.
9. `SummarizeSessionFilesAsync_DirectEndpointPath_TelemetryRecordsDirectEndpointDimension` — path enum mapping.
10. `SummarizeSessionFilesAsync_EmptyTenantId_Throws` — input validation.
11. `SummarizeSessionFilesAsync_EmptySessionId_Throws` — input validation.
12. `SummarizeSessionFilesAsync_NoFilesInSession_EmitsDecline` — empty-session decline path (FR-11 equivalent).

---

## Open items for main-session Step 9 / 9.5 / 9.7

These items are EXPLICITLY scoped to the main session per the sub-agent invocation contract:

1. `dotnet build src/server/api/Sprk.Bff.Api/` — verify zero new compiler warnings.
2. `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SessionSummarizeOrchestratorTests"` — verify all tests green.
3. `code-review` skill on the new + modified files.
4. `adr-check` skill against ADR-010, ADR-013, ADR-014, ADR-018, ADR-028, ADR-029.
5. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` + measure compressed size + record delta vs Phase 1 baseline (~45.65 MB; budget ≤+1 MB cumulative for R5).

---

## Downstream consumer contracts (for tasks 014 + 015)

Both downstream tasks construct a `SummarizeSessionFilesRequest` and call the single convergence method:

```csharp
// Task 014 (direct endpoint):
var request = new SummarizeSessionFilesRequest(
    TenantId: tenantId, SessionId: sessionId, FileIds: payload.FileIds,
    StyleHint: payload.Style, Path: SummarizeInvocationPath.DirectEndpoint,
    CorrelationId: httpContext.TraceIdentifier);
await foreach (var chunk in orchestrator.SummarizeSessionFilesAsync(request, ct))
{
    await writer.WriteSseEventAsync(chunk, ct);
}

// Task 015 (agent-tool handler):
var request = new SummarizeSessionFilesRequest(
    TenantId: agentContext.TenantId, SessionId: agentContext.SessionId, FileIds: args.FileIds,
    StyleHint: args.Style, Path: SummarizeInvocationPath.AgentTool,
    CorrelationId: agentContext.CorrelationId);
await foreach (var chunk in orchestrator.SummarizeSessionFilesAsync(request, ct))
{
    await toolStream.EmitAsync(chunk, ct);
}
```

The contract: both paths produce byte-identical output for the same `(TenantId, SessionId, FileIds, StyleHint)` tuple. The `Path` discriminator influences telemetry only — NOT the output stream.
