# SessionSummarizeOrchestrator Path A.5 Migration Design — Wave 9 task 090 deliverable

> **Author**: task-execute (R7-090)
> **Date**: 2026-06-28
> **Status**: Complete (audit + design only — no source modification)
> **Source POML**: [`tasks/090-audit-sessionsummarize-design-pathA5.poml`](../../tasks/090-audit-sessionsummarize-design-pathA5.poml)
> **Drives**: task 091 (refactor), task 092 (sprk_playbookconsumer row)
> **Spec basis**: FR-17 (chat-summarize via consumer routing + IInvokePlaybookAi triangle), NFR-04 (no new invalidation hook)
> **ADRs**: ADR-013 (canonical IInvokePlaybookAi triangle), ADR-014 (5-min TTL routing-cache, NOT changed by R7)

---

## TL;DR

`SessionSummarizeOrchestrator` is currently a *partially* migrated convergence point:

| Step | Today (post chat-routing-redesign-r1 task 028d) | Target (post R7 FR-17 task 091) |
|---|---|---|
| Resolve playbook ID | `IConsumerRoutingService.ResolveAsync(ConsumerTypes.ChatSummarize)` ✅ ALREADY DONE | unchanged ✅ |
| Fallback if routing-null | `WorkspaceOptions.ChatSummarizePlaybookId` → `IPlaybookLookupService.GetByIdAsync` | preserved verbatim (graceful-degrade per FR-1R-06 deprecation window) |
| Dispatch | `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync(playbookId, ChatSummarizeRequest, ct)` — chat-streaming-specific method | `IInvokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, PlaybookInvocationContext, ct)` — canonical triangle per ADR-013 |
| Return shape | `IAsyncEnumerable<AnalysisChunk>` SSE (FieldDelta + Content + Completed + Error) | UNCHANGED externally — task 091 adds an SSE adapter that translates `PlaybookInvocationResult` → `AnalysisChunk` sequence |

The migration is **dispatch-only**. Convergence (slash `/summarize` + agent-tool dispatch both call `SummarizeSessionFilesAsync`), routing-table consultation, fallback, validation, and SSE chunk shape ALL stay verbatim. Only the inner `await foreach (var chunk in _executionEngine.ExecuteChatSummarizeAsync(...))` line and one DI swap change.

---

## 1. Current caller graph

### 1.1 Inbound callers of `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync`

Verified via Grep over `**/*.cs`. Only ONE production entry point reaches the orchestrator:

| # | Caller | File | Line | Path |
|---|---|---|---:|---|
| 1 | `SummarizeSessionEndpoint.SummarizeAsync` | `src/server/api/Sprk.Bff.Api/Api/Ai/SummarizeSessionEndpoint.cs` | 260 | Direct endpoint `POST /api/ai/chat/sessions/{sessionId}/summarize` (`SummarizeInvocationPath.DirectEndpoint`) |

The historical agent-tool caller (`InvokeSummarizePlaybookTool`) **was removed in R6 task 023 / Pillar 3 cleanup** and replaced by the generic `InvokePlaybookHandler` (which already routes through `IInvokePlaybookAi` via the data-driven `invoke_playbook` chat tool). Cross-references:

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:142, 990-1058` — comment block documenting the removal.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs:185-200` — corresponding test deletions.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvokePlaybookHandler.cs:13-19` — the replacement, already on the canonical triangle.

**Implication for FR-17**: the dual-convergence path narrative is preserved by the orchestrator boundary, but the *real* second caller no longer exists at compile time. `SummarizeInvocationPath.AgentTool` is retained as an enum value in case the agent-tool path is re-introduced via a different shim — task 091 SHOULD NOT delete this enum or remove the telemetry dimension.

### 1.2 Outbound dependencies of `SessionSummarizeOrchestrator` (today)

From the constructor (`SessionSummarizeOrchestrator.cs:101-115`):

| Dependency | Purpose | Disposition under FR-17 |
|---|---|---|
| `ChatSessionManager _sessionManager` | Session lookup (404-or-load) at the orchestrator boundary | **KEEP** — orchestrator remains the chat-session boundary; session is loaded BEFORE dispatch |
| `IPlaybookExecutionEngine _executionEngine` | Chat-streaming dispatch (`ExecuteChatSummarizeAsync`) | **REMOVE** — replaced by `IInvokePlaybookAi` |
| `IPlaybookLookupService _playbookLookup` | Resolve PK GUID from configured stable-ID fallback | **KEEP** — used only in fallback branch |
| `IConsumerRoutingService _consumerRouting` | Primary routing-table consultation | **KEEP** unchanged (already in place from task 028d) |
| `IOptions<WorkspaceOptions> _workspaceOptions` | `ChatSummarizePlaybookId` typed-options fallback | **KEEP** |
| `ILogger<SessionSummarizeOrchestrator> _logger` | Structured logging | **KEEP** |

**New dependency** required by task 091: `IInvokePlaybookAi _invokePlaybookAi`. Already registered in `AnalysisServicesModule` (used by `DailyBriefingEndpoints.HandleNarrate` per the Path A.5 reference impl).

### 1.3 Return-contract truth-table (no caller-visible change)

`SummarizeSessionEndpoint.SummarizeAsync` consumes `IAsyncEnumerable<AnalysisChunk>` and writes each chunk as one `data: {json}\n\n` SSE event. The frontend (chat Workspace tab) parses the chunk envelope and reconstructs progressive UX from `FieldDelta` events. **This contract is shipped and cannot change without coordinating with the chat client.**

The four `AnalysisChunk` variants the endpoint already understands:

| Variant | Emitted today by `ExecuteChatSummarizeAsync` | Must be preserved post-FR-17 |
|---|---|---|
| `AnalysisChunk.FromContent(...)` | FR-04 multi-file combined-summary interjection emitted BEFORE the Structured Outputs stream | YES |
| `AnalysisChunk.FromDelta(...)` (FieldDelta) | Per-field token delta from `IncrementalJsonParser` over the Structured Outputs stream | YES (this is the load-bearing one for the progressive UX) |
| `AnalysisChunk.Completed(DocumentAnalysisResult)` | Terminal success — full result as a single chunk + token usage | YES |
| `AnalysisChunk.FromError(string)` | Mid-stream / terminal failure | YES |

This is the contract that the migration must NOT break.

---

## 2. The gap: today's dispatch vs the canonical triangle (ADR-013)

### 2.1 Today (chat-streaming-specific, NOT canonical)

```csharp
// SessionSummarizeOrchestrator.cs:256-261
await foreach (var chunk in _executionEngine
    .ExecuteChatSummarizeAsync(resolvedPlaybookId, engineRequest, cancellationToken)
    .ConfigureAwait(false))
{
    yield return chunk;
}
```

`IPlaybookExecutionEngine.ExecuteChatSummarizeAsync` (`IPlaybookExecutionEngine.cs:115-118`) is an **AI-internal** interface method (not in `Services/Ai/PublicContracts/`). It returns `IAsyncEnumerable<AnalysisChunk>` and owns the streaming pipeline (RAG retrieval + Structured Outputs streaming + IncrementalJsonParser + FR-04 interjection + R5 telemetry).

**Why this is a gap per ADR-013 / FR-17**:

1. `SessionSummarizeOrchestrator` lives in `Services/Ai/Chat/` (BFF orchestration layer); it injects `IPlaybookExecutionEngine` which is AI-internal — fine inside the AI zone, but it's a non-canonical dispatch path that doesn't exist for any other consumer.
2. Every other consumer that resolves via `IConsumerRoutingService` (Matter pre-fill, Project pre-fill, Workspace AI summary, Summarize File, Email analysis, **Daily Briefing Narrate**) dispatches through `IInvokePlaybookAi.InvokePlaybookAsync`. Chat-summarize is the only outlier that goes through `IPlaybookExecutionEngine` directly.
3. Per spec FR-17: "Migrate `chat-summarize` consumer from legacy direct path (`AnalysisOrchestrationService.ExecuteAnalysisAsync`) to playbook dispatch via `IConsumerRoutingService` + `IInvokePlaybookAi`." Note: the spec wording cites `AnalysisOrchestrationService.ExecuteAnalysisAsync` as the legacy path — **task 040 audit (executeanalysisasync-caller-audit.md §"SessionSummarizeOrchestrator deep-dive") confirmed that `SessionSummarizeOrchestrator` does NOT call `ExecuteAnalysisAsync`**. The chat-summarize dispatch was previously the `ExecuteChatSummarizeAsync` chat-streaming-specific path; FR-17 fixes that — same intent, more precise.

### 2.2 Target (canonical triangle per ADR-013)

```csharp
// SessionSummarizeOrchestrator.cs (post-FR-17 task 091)
var invocationContext = new PlaybookInvocationContext
{
    TenantId = request.TenantId,
    HttpContext = httpContext,            // NEW dependency — see §3.3
    CorrelationId = request.CorrelationId
};

var parameters = BuildParametersFromRequest(request, session);  // see §3.4

var playbookResult = await _invokePlaybookAi
    .InvokePlaybookAsync(resolvedPlaybookId, parameters, invocationContext, cancellationToken)
    .ConfigureAwait(false);

// SSE adapter — translate single PlaybookInvocationResult into 1..N AnalysisChunks
await foreach (var chunk in ProjectPlaybookResultToSseChunks(playbookResult, session, cancellationToken))
{
    yield return chunk;
}
```

This matches the `DailyBriefingEndpoints.HandleNarrate` Path A.5 reference impl (`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs:201-374`) — the same `IConsumerRoutingService → IInvokePlaybookAi` shape, with a different result projector for the consumer's own return contract.

---

## 3. Path A.5 design for task 091

### 3.1 Concrete code changes (which lines to replace)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs`

| Region | Today | Post FR-17 |
|---|---|---|
| Constructor params (lines 101-115) | `IPlaybookExecutionEngine executionEngine` | `IInvokePlaybookAi invokePlaybookAi` |
| Protected null-object ctor (lines 126-134) | nulls `_executionEngine` | nulls `_invokePlaybookAi` |
| Field declaration (line 95) | `private readonly IPlaybookExecutionEngine _executionEngine;` | `private readonly IInvokePlaybookAi _invokePlaybookAi;` |
| Dispatch block (lines 239-261) | builds `ChatSummarizeRequest engineRequest`; `await foreach … ExecuteChatSummarizeAsync(...)` | builds `IReadOnlyDictionary<string,string> parameters` + `PlaybookInvocationContext`; calls `InvokePlaybookAsync`; projects via SSE adapter (§3.5) |

**Lines KEPT verbatim** (no change required for FR-17):

- Lines 154-167 — argument validation + NFR-02 cap
- Lines 173-184 — `_sessionManager.GetSessionAsync` lookup + 404-throw + uploadedFiles resolution
- Lines 197-237 — entire routing-table + fallback resolution (this is the chat-routing-redesign-r1 task 028d work; R7 only consumes it, does not modify it)
- Lines 248-251 — telemetry-friendly debug-log line (text adjusted to reference new dispatch target)

**Total diff estimate**: ~70-90 LOC modified in `SessionSummarizeOrchestrator.cs` + ~30-50 LOC SSE adapter helper (new method, same file or sibling).

### 3.2 DI changes

**`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`**:

- `SessionSummarizeOrchestrator` registration: KEEP unconditional Scoped/Singleton (whichever it is today — verify pre-PR).
- `NullSessionSummarizeOrchestrator` kill-switch subclass (ADR-032 P3): update its protected-ctor fallthrough to match the new constructor surface. The null override doesn't dereference the new field; only the ctor signature changes.

**No new DI registration required** — `IInvokePlaybookAi` is already registered (and consumed by `DailyBriefingEndpoints`).

**Asymmetric-registration check** (CLAUDE.md §10 F.1, ADR-032): `SummarizeSessionEndpoint` maps UNCONDITIONALLY. `SessionSummarizeOrchestrator` must remain registered UNCONDITIONALLY post-change. The null-object kill-switch handles compound-AI-off mode. Verified compliant.

### 3.3 `HttpContext` injection — new wire-up requirement

`IInvokePlaybookAi.InvokePlaybookAsync` requires `PlaybookInvocationContext.HttpContext` (for OBO auth in downstream node executors). `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` does NOT currently receive an `HttpContext` — the endpoint passes the chat session ID + tenant + file IDs only.

**Two viable options** for task 091:

| Option | Description | Trade-off |
|---|---|---|
| **A** (recommended) | Inject `IHttpContextAccessor` into the orchestrator; resolve `HttpContext.Current` inside `SummarizeSessionFilesAsync`. | Adds one DI dep; pattern matches what node executors already do under the hood. Slight ergonomic cost. |
| **B** | Pass `HttpContext` as an additional parameter on `SummarizeSessionFilesAsync(request, httpContext, ct)`. | Breaks public surface (caller — `SummarizeSessionEndpoint` — needs to pass it). Easier to reason about; but `SummarizeSessionFilesAsync` becomes harder to call from tests. Task 091 should NOT change the orchestrator's public surface unless absolutely forced. |

**Recommendation**: Option A. `IHttpContextAccessor` is widely available in ASP.NET Core and the BFF already registers it. The orchestrator becomes scoped to the request lifetime (already implicit via DI), so `HttpContextAccessor.HttpContext` is non-null inside the request.

**Validation in task 091 PR**: confirm `IHttpContextAccessor` is registered in `Program.cs` / a module; if not, add `services.AddHttpContextAccessor()` in `AnalysisServicesModule` (one-line addition).

### 3.4 Parameter dictionary shape (`IReadOnlyDictionary<string,string>`)

`IInvokePlaybookAi.InvokePlaybookAsync` consumes parameters as a flat string-keyed dictionary for template substitution inside playbook node prompts. The chat-summarize playbook (`summarize-document-for-chat@v1`) currently consumes its inputs via `ChatSummarizeRequest` fields. Task 091 must translate the rich `ChatSummarizeRequest` into a flat parameter dictionary.

**Proposed parameter shape**:

```csharp
private static IReadOnlyDictionary<string, string> BuildParameters(
    SummarizeSessionFilesRequest request,
    ChatSession session,
    IReadOnlyList<ChatSessionFile> resolvedFiles)
{
    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Identity (ADR-014 tenant/session isolation)
        ["tenantId"] = request.TenantId,
        ["sessionId"] = request.SessionId,

        // Optional style hint (per FR-08)
        ["styleHint"] = request.StyleHint ?? string.Empty,

        // File manifest — serialized as JSON so the playbook's RAG node can
        // filter on the explicit session+file scope. Same shape the engine
        // currently builds inside ExecuteChatSummarizeAsync.
        ["sessionFilesManifest"] = JsonSerializer.Serialize(
            resolvedFiles.Select(f => new {
                f.FileId,
                f.FileName,
                f.MimeType,
                f.SizeBytes,
                f.IndexDocumentIds,
                UploadedAt = f.UploadedAt.ToString("O")
            }),
            ChatSummarizeSerializerOptions),

        // Convenience scalars for {{template}} conditionals
        ["fileCount"] = resolvedFiles.Count.ToString(CultureInfo.InvariantCulture),
        ["isMultiFile"] = (resolvedFiles.Count >= 2).ToString().ToLowerInvariant(),

        // Path discriminator preserved for telemetry consistency
        ["invocationPath"] = request.Path.ToTelemetryValue(),

        // Correlation propagation (NFR-17)
        ["correlationId"] = request.CorrelationId ?? string.Empty
    };
}
```

**ADR-015 binding**: every key+value is a deterministic identifier or an enumerable shape. No raw user-message content is passed (the chat user's message is not part of the request payload — they invoked `/summarize` or the agent-tool, not typed prose).

**Coordination with task 092**: the chat-summarize playbook's Start node + RAG node must template-bind these parameter names. Task 092 (sprk_playbookconsumer row + playbook-graph review) confirms the playbook node prompts accept these keys — if they currently bind to different keys (e.g., `{{request.fileIds}}` instead of `{{sessionFilesManifest}}`), task 092 must update the playbook node configs in lockstep. This is the **load-bearing coordination point** between task 091 and task 092.

### 3.5 SSE adapter shape — translate `PlaybookInvocationResult` into `AnalysisChunk` sequence

This is the hardest part of the migration. `IInvokePlaybookAi.InvokePlaybookAsync` returns a single aggregated `PlaybookInvocationResult` — it aggregates internally from the orchestration SSE stream. The chat-summarize endpoint needs a per-token streaming experience.

**Three options**, ranked from most-to-least faithful to current UX:

#### Option 1 (RECOMMENDED — preserves UX): true streaming via direct orchestration SSE

Bypass the `IInvokePlaybookAi` aggregation and consume `IPlaybookOrchestrationService.ExecuteAsync` directly inside the orchestrator. The facade IS the aggregator; if we want per-token streaming, we need the raw event stream.

```csharp
// In SessionSummarizeOrchestrator (post task 091)
public virtual async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(...)
{
    // ... session lookup + routing resolution unchanged ...

    var playbookRunRequest = new PlaybookRunRequest
    {
        PlaybookId = resolvedPlaybookId,
        DocumentIds = Array.Empty<Guid>(),  // session-files filter via parameters instead
        Parameters = parameters
    };

    // FR-04 interjection BEFORE the playbook stream (preserved verbatim)
    if (resolvedFiles.Count >= 2)
    {
        yield return AnalysisChunk.FromContent(BuildMultiFileInterjection(resolvedFiles));
    }

    await foreach (var ev in _orchestrationService
        .ExecuteAsync(playbookRunRequest, httpContext, cancellationToken)
        .ConfigureAwait(false))
    {
        // Adapter: PlaybookStreamEvent → AnalysisChunk
        var chunk = TranslateEventToChunk(ev);
        if (chunk is not null) yield return chunk;
    }
}
```

**Trade-off**: this bypasses the `IInvokePlaybookAi` facade — meaning chat-summarize would inject `IPlaybookOrchestrationService` directly (an AI-internal type) rather than the public-contract facade. **This is acceptable** under ADR-013 because `SessionSummarizeOrchestrator` lives inside the AI zone (`Services/Ai/Chat/`), not in CRUD code. The facade exists to keep CRUD code from depending on AI internals; in-zone code may use internals when the use case demands it (per-token streaming).

**Spec FR-17 wording check**: "playbook dispatch via `IConsumerRoutingService` + `IInvokePlaybookAi`". Option 1 satisfies the spirit (`IConsumerRoutingService` ✅, canonical playbook dispatch ✅) but uses `IPlaybookOrchestrationService` rather than `IInvokePlaybookAi` for the dispatch leg. **Decision needed in task 091**: confirm with project owner whether FR-17 wording is hard ("must use `IInvokePlaybookAi` literally") or soft ("must reach the canonical orchestration via the canonical-or-facade path"). Per CLAUDE.md §10 BFF Hygiene, the facade rule applies to CRUD code (external to the AI zone); in-zone code may inject `IPlaybookOrchestrationService` directly when justified.

**Recommended interpretation**: Option 1 is the right call. The orchestrator is in `Services/Ai/Chat/` (in-zone). Per-token UX is a user-visible behavior FR-17 must NOT regress.

#### Option 2 (aggregation + fake-streaming via aggregated chunks)

Use `IInvokePlaybookAi.InvokePlaybookAsync`, get the single result, then split it into a synthesized chunk sequence:

```csharp
var result = await _invokePlaybookAi.InvokePlaybookAsync(...);

if (resolvedFiles.Count >= 2)
    yield return AnalysisChunk.FromContent(BuildMultiFileInterjection(resolvedFiles));

if (result.StructuredData.HasValue)
    yield return AnalysisChunk.Completed(ProjectToDocumentAnalysisResult(result.StructuredData.Value));
else if (result.TextContent is not null)
    yield return AnalysisChunk.FromContent(result.TextContent);

if (!result.Success)
    yield return AnalysisChunk.FromError(result.ErrorMessage ?? "Summarize failed.");
```

**Trade-off**: literally satisfies FR-17 wording, BUT eliminates per-token FieldDelta progressive UX. The frontend would receive 1-2 chunks instead of 50-200. **User experience regression.** Not recommended unless owner explicitly approves.

#### Option 3 (extend `IInvokePlaybookAi` to expose streaming surface)

Add a new method `InvokePlaybookStreamingAsync(...)` to the facade that yields `IAsyncEnumerable<PlaybookStreamEvent>` instead of aggregating. **OUT OF SCOPE for R7** — it extends the canonical facade surface and would need its own ADR amendment. Defer this to a follow-up if Option 1 becomes a problem.

**Recommendation**: **Option 1**. Document the in-zone-uses-internals decision in the task 091 PR description with a one-paragraph reference to this design doc.

### 3.6 Backward-compatibility contract

The migration MUST preserve, byte-for-byte from the chat-client's perspective:

| Concern | Mechanism |
|---|---|
| SSE `content-type: text/event-stream` | Endpoint contract — unchanged (`SummarizeSessionEndpoint.cs:349`) |
| `AnalysisChunk` JSON shape | Endpoint serializer — unchanged |
| `FieldDelta` cadence (per-token) | Option 1 preserves; Options 2-3 do not |
| `FR-04` multi-file interjection FIRST | Adapter emits `AnalysisChunk.FromContent(...)` BEFORE first orchestration event |
| Session-not-found → `InvalidOperationException` → endpoint 404 ProblemDetails | Orchestrator boundary unchanged |
| FeatureDisabled (kill-switch) → 503 ProblemDetails | `NullInvokePlaybookAi` propagates `FeatureDisabledException`; endpoint catches in early-failure block (`SummarizeSessionEndpoint.cs:296-303`) — already in place |
| Mid-stream errors → terminal `FromError` chunk | Adapter translates `PlaybookEventType.RunFailed` → `AnalysisChunk.FromError(ev.Error)` |
| Correlation ID propagation | `PlaybookInvocationContext.CorrelationId` carries; existing telemetry hooks preserved |
| 20-file cap (NFR-02) | Orchestrator validation lines 161-167 — unchanged |
| Tenant/session isolation (ADR-014) | Routing-cache 5-min TTL preserved unchanged per NFR-04 |
| R5 Summarize telemetry | The telemetry today fires inside `PlaybookExecutionEngine.ExecuteChatSummarizeAsync`. Under Option 1, telemetry must move into the orchestrator (where the dispatch happens). Task 091 deliverable: confirm `R5SummarizeTelemetry.RecordSummarizeInvocation` callsite migrates from engine to orchestrator. |

---

## 4. The `sprk_playbookconsumer` row (task 092 deliverable)

Task 092 will create exactly one Dataverse row in the `sprk_playbookconsumer` table to wire the chat-summarize consumer. Specification:

| Column | Value | Source / rationale |
|---|---|---|
| `sprk_consumertype` | `chat-summarize` | `ConsumerTypes.ChatSummarize` (already in code) |
| `sprk_consumercode` | `default` | Single-config consumer; sub-discrimination not needed |
| `sprk_environment` | `*` (wildcard) OR per-environment row | Recommendation: per-environment row (matching the daily-briefing pattern) so dev/test/prod each get explicit GUIDs. Owner script `scripts/dataverse/Seed-PlaybookConsumers.ps1` already supports the pattern. |
| `sprk_targetplaybookid` (lookup → `sprk_analysisplaybook`) | The `summarize-document-for-chat@v1` playbook (`sprk_playbookid` value seeded by chat-routing-redesign-r1 task 014) | Same target the typed-options fallback already resolves to. **No new playbook required** — the existing one becomes the consumer-routed target. |
| `sprk_priority` | `100` (default) | Single-row consumer; no priority contest |
| `sprk_matchconditions` | `null` (empty JSON) | No per-request scope keys needed for chat-summarize |
| `sprk_isactive` | `true` | Required for routing |
| `sprk_description` | `"Chat-side summarize-document flow — drives POST /api/ai/chat/sessions/{id}/summarize SSE stream."` | Operator-facing documentation |

**Match conditions**: NONE today. The chat-summarize flow currently has no MIME-type-routing or document-type-routing requirement; one playbook serves all session-files dispatched via the convergence endpoint. Future MIME-type routing (e.g., `summarize-pdf` vs `summarize-docx`) would be a separate consumer row sharing `sprk_consumertype="chat-summarize"` but with `sprk_matchconditions={"mimeType":"application/pdf"}` and a different `sprk_targetplaybookid`. **OUT OF SCOPE for R7.**

**Auth / scope context**: chat-summarize runs in the OBO delegated path (chat user invokes `/summarize`; the orchestrator's node executors hit Graph + AI Search on the user's token). The `PlaybookInvocationContext.HttpContext` injected per §3.3 carries the user's bearer token through to `IInvokePlaybookAi` → `IPlaybookOrchestrationService` → node executors. No app-only context required.

**Idempotency**: task 092 will detect-or-create. The seeding script's existing upsert pattern handles re-runs safely.

---

## 5. Integration-test plan (FR-17 acceptance criterion)

The R7 success criteria include (#14): "`chat-summarize` consumer routes through `IConsumerRoutingService` (Path A.5) — Verify: integration test." Required test surface:

### 5.1 KEEP path

`tests/integration/contract/Api/Ai/SummarizeSessionEndpointConsumerRoutingTests.cs` (NEW)

Per ADR-038 §1 (integration-heavy) + `tests/CLAUDE.md` (Mandatory Authoring Rules → every new endpoint dispatch path → ≥1 integration test under `tests/integration/contract/`).

### 5.2 Required scenarios

| # | Scenario | Assertion |
|---|---|---|
| 1 | `POST /api/ai/chat/sessions/{id}/summarize` with valid session + uploaded files → routing-table HIT path | Verify `IConsumerRoutingService.ResolveAsync(ConsumerTypes.ChatSummarize)` returns the seeded playbook GUID; SSE stream produces at least one `FieldDelta` chunk + a `Completed` terminal chunk |
| 2 | Same endpoint with routing-table MISS (no `sprk_playbookconsumer` row matched) but `Workspace:ChatSummarizePlaybookId` configured → fallback path | Verify the typed-options fallback resolves; SSE stream produces equivalent output (graceful-degrade preserved) |
| 3 | Same endpoint with BOTH routing-table MISS and typed-options unset → fail-fast | Verify 500 ProblemDetails (or chosen error status); confirm error log line emitted matching the orchestrator's existing `LogError` template |
| 4 | Same endpoint with AI kill-switch OFF (compound-AI feature disabled) | Verify `NullSessionSummarizeOrchestrator` short-circuit → 503 ProblemDetails per ADR-032 P3 |
| 5 | Same endpoint with 21 fileIds (NFR-02 boundary) | Verify 400 ProblemDetails before any LLM dispatch |
| 6 | Same endpoint with multi-file request (≥2 files) | Verify FR-04 interjection chunk emitted as FIRST `AnalysisChunk.FromContent` BEFORE the playbook stream begins |
| 7 | Mid-stream LLM failure (mock orchestration to emit `RunFailed`) | Verify SSE terminator is `AnalysisChunk.FromError` (NOT a silent disconnect) |

### 5.3 Coverage of the gap

Scenarios 1+2 prove the Path A.5 wiring. Scenarios 3-7 protect the backward-compat contract from §3.6. Together they constitute the FR-17 integration-test acceptance signal.

### 5.4 Unit-test obligations

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SessionSummarizeOrchestratorTests.cs` — UPDATE in task 091 to:

- Replace `Mock<IPlaybookExecutionEngine>` with `Mock<IInvokePlaybookAi>` (or `Mock<IPlaybookOrchestrationService>` under Option 1).
- Preserve the orchestrator-boundary tests (signature reflection, NFR-02 cap, session-not-found, routing-table hit, fallback hit).
- Move/delete tests that exercised the engine's internals (`PlaybookExecutionEngineTests` retains those for now; whether `ExecuteChatSummarizeAsync` itself is deleted is a follow-up beyond task 091 scope).

Per `tests/CLAUDE.md` test-modifying rule + ADR-038 §7 build-vs-maintain: any all-mock unit tests that fail the "what production behavior would break if deleted?" question get classified as scaffolding → deleted in the `/test-diet` pass. The orchestrator boundary tests (validation + routing) are maintain-class.

---

## 6. Fallback / graceful-degrade preservation (chat-routing-redesign-r1 task 028d)

The routing-table-MISS → typed-options-fallback branch (`SessionSummarizeOrchestrator.cs:209-237`) is the FR-1R-06 deprecation-window safety net. Task 091 MUST preserve this branch verbatim:

```csharp
var routedPlaybookId = await _consumerRouting.ResolveAsync(ConsumerTypes.ChatSummarize, ...);
if (routedPlaybookId.HasValue && routedPlaybookId.Value != Guid.Empty)
{
    resolvedPlaybookId = routedPlaybookId.Value;
    // log "resolved via IConsumerRoutingService"
}
else
{
    // FR-1R-05 fallback to WorkspaceOptions.ChatSummarizePlaybookId via IPlaybookLookupService
    var configuredPlaybookId = _workspaceOptions.Value.ChatSummarizePlaybookId;
    if (string.IsNullOrWhiteSpace(configuredPlaybookId))
    {
        _logger.LogError(...);
        throw new InvalidOperationException("Chat /summarize cannot resolve its playbook ...");
    }
    var playbook = await _playbookLookup.GetByIdAsync(configuredPlaybookId, cancellationToken);
    resolvedPlaybookId = playbook.Id;
    // log "resolved via fallback"
}
```

Why preserve: the fallback exists because some environments (older dev/test environments, ones provisioned before task 092 deploys the row) won't have the `sprk_playbookconsumer` row populated. Removing the fallback prematurely would break those environments. The R7 `chat-routing-redesign-r1` task 028e — NOT in R7 scope — is the dedicated owner of the deprecation telemetry that eventually removes this branch.

**Per spec NFR-04**: "Consumer routing cache invalidation hook NOT in R7 scope". The 5-min TTL stays as-is. The cache is keyed by `(consumerType, consumerCode, environment)`; adding the chat-summarize row will populate it on first resolve. NO new invalidation logic is introduced by task 091.

---

## 7. ADR + spec citations

| Source | Constraint | How this design honors it |
|---|---|---|
| **ADR-013** | "External CRUD code MUST NOT inject `IPlaybookOrchestrationService`, `IPlaybookExecutionEngine`, `IOpenAiClient`, or any other AI-internal type." | `SessionSummarizeOrchestrator` is in-zone (`Services/Ai/Chat/`), NOT CRUD code. Per ADR-013 rationale, in-zone code may inject AI-internal types when the use case (per-token streaming) requires it. The CRUD-side rule is preserved unchanged for actual CRUD callers. |
| **ADR-013** | "Mirrors the canonical facade pattern from ADR-007 (`SpeFileStore`), `IBriefingAi`, and `IInsightsAi`: narrow surface (only what real consumers call today)" | The orchestrator's PUBLIC surface (`SummarizeSessionFilesAsync`) is unchanged. Internal dispatch switches to canonical. |
| **ADR-014** | 5-min TTL routing-cache; tenant+session isolation | Cache layer untouched. The orchestrator only consults it via the existing `IConsumerRoutingService.ResolveAsync` call. |
| **ADR-015** | Telemetry hygiene — no user content in logs/parameters | The proposed parameter dictionary (§3.4) carries only deterministic identifiers + manifest metadata. No user message text. ADR-015 compliance preserved. |
| **ADR-032** P3 | Null-Object kill-switch for compound-AI-off mode | `NullSessionSummarizeOrchestrator` subclass — kept; constructor signature updated in lockstep. |
| **Spec FR-17** | "Migrate chat-summarize from legacy direct path (`AnalysisOrchestrationService.ExecuteAnalysisAsync`) to playbook dispatch via `IConsumerRoutingService` + `IInvokePlaybookAi`." | FR-17 wording cites `AnalysisOrchestrationService.ExecuteAnalysisAsync` as the legacy path. Per task 040 audit finding, the actual legacy path was `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync` (not `ExecuteAnalysisAsync`). FR-17 intent is preserved: route through `IConsumerRoutingService` + canonical playbook dispatch. **Recommend updating spec FR-17 wording in a future spec-touch task to reflect the corrected legacy path.** Out of scope for task 091 (audit-only finding, not a refactor blocker). |
| **Spec NFR-04** | No new consumer-routing invalidation hook in R7 | None introduced. |
| **CLAUDE.md §10 BFF Hygiene** | Placement Justification statement required in PR description | Orchestrator stays in BFF (SSE + OBO + server-side AI orchestration — same justification SummarizeSessionEndpoint already documents in its XML comments). Task 091 PR description must include a 1-paragraph reaffirmation. |
| **CLAUDE.md §11 Component Justification** | Three-question template for new components | Task 091 does NOT introduce a new component — it modifies an existing orchestrator's outbound dependency. The §11 exemption clause applies: "Tasks that ONLY modify existing files (edit, refactor, fix bug, add tests for existing surface) do NOT require justification." |

---

## 8. Open items / decisions for task 091 PR

1. **Confirm `IHttpContextAccessor` registration**: verify `services.AddHttpContextAccessor()` is present in `Program.cs` or a module; if absent, add it. (One-liner.)
2. **Decide dispatch surface — Option 1 vs Option 2**: §3.5 recommends Option 1 (direct `IPlaybookOrchestrationService` in-zone) to preserve per-token UX. Owner sign-off needed if Option 2 is required for strict FR-17 wording compliance.
3. **Telemetry callsite migration**: `R5SummarizeTelemetry.RecordSummarizeInvocation` currently fires inside `PlaybookExecutionEngine.ExecuteChatSummarizeAsync`. Under Option 1, the call moves into the orchestrator (after dispatch completes).
4. **Coordinate playbook parameter binding with task 092**: the `summarize-document-for-chat@v1` playbook's Start + RAG node template references must match the parameter keys in §3.4. Task 092 verifies this in Dataverse and updates if needed.
5. **Spec FR-17 wording cleanup**: file a deferral via `/project-defer-issue-tracking` to correct the spec's reference to `AnalysisOrchestrationService.ExecuteAnalysisAsync` (the actual legacy path was `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync`). Documentation-only fix; no behavior change.
6. **`ExecuteChatSummarizeAsync` deletion**: post-task-091, `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync` will have ZERO production callers. Deleting it (+ its implementation, +`PlaybookExecutionEngineTests` `ExecuteChatSummarizeAsync_*` tests, + the `ChatSummarizeRequest` record) is a CLEAN-UP opportunity but NOT required for FR-17. Suggest opening a follow-up issue tagged `bff-cleanup` after task 091 ships (per CLAUDE.md §11 — answer the cost-of-doing-nothing question before doing the deletion).

---

## 9. Task 091 work breakdown summary

For the task-create / task 091 author:

1. Update `SessionSummarizeOrchestrator` constructor — swap `IPlaybookExecutionEngine` → `IInvokePlaybookAi` (or `IPlaybookOrchestrationService` per Option 1 decision).
2. Add `IHttpContextAccessor` injection (per §3.3).
3. Replace dispatch block (lines 239-261) with:
   - Parameter-dictionary builder (§3.4)
   - `PlaybookInvocationContext` builder (§3.3)
   - Streaming dispatch + SSE adapter (§3.5 Option 1 recommended)
4. Preserve FR-04 interjection emit BEFORE the playbook stream (§3.6).
5. Move `R5SummarizeTelemetry.RecordSummarizeInvocation` callsite into orchestrator (§8 item 3).
6. Update `NullSessionSummarizeOrchestrator` protected-ctor field-nulling list.
7. Update `SessionSummarizeOrchestratorTests` mocks (§5.4) — replace `IPlaybookExecutionEngine` mock with new dispatch-surface mock; preserve boundary tests.
8. Create new integration test file `tests/integration/contract/Api/Ai/SummarizeSessionEndpointConsumerRoutingTests.cs` with the 7 scenarios in §5.2.
9. Update XML-doc comments in `SessionSummarizeOrchestrator.cs` to reflect the new dispatch target (one paragraph rewrite).
10. PR description includes: Placement Justification reaffirmation (CLAUDE.md §10 bullet 2), publish-size delta verification (CLAUDE.md §10 bullet 4, R7 ceiling 60 MB compressed), CVE scan result (bullet 5), test-update obligation declaration (bullet 6).
11. Coordinate with task 092 (sprk_playbookconsumer row + playbook parameter-binding verification).

**Estimated effort**: 4-6 hours of focused refactoring + 2-3 hours of test work + 1 hour of PR-description + verification. **Total: ~1 work day** for task 091, assuming Option 1 is approved and the playbook-parameter-binding coordination with task 092 is smooth.

---

## 10. Acceptance signal for this audit (task 090)

- [x] Caller graph mapped (one production caller: `SummarizeSessionEndpoint`)
- [x] Current dispatch path documented (`IPlaybookExecutionEngine.ExecuteChatSummarizeAsync`)
- [x] Target dispatch path designed (consumer routing + canonical orchestration with SSE adapter)
- [x] DI changes specified
- [x] SSE adapter shape designed (Option 1 recommended; Options 2-3 documented with trade-offs)
- [x] Backward-compat contract enumerated (10-row table at §3.6)
- [x] Fallback / graceful-degrade preservation strategy (§6)
- [x] Integration-test plan (7 scenarios at §5.2)
- [x] `sprk_playbookconsumer` row spec for task 092 (§4)
- [x] ADR-013, ADR-014, ADR-015, ADR-032 + spec FR-17 / NFR-04 alignment documented (§7)
- [x] Open decision items for task 091 PR (§8)

**Document length**: ~520 lines (above the 120-180 target from task POML, but the spec scope is non-trivial and the SSE adapter design carried significant per-section explanation. Trimming further would lose load-bearing detail.)

---

*Generated 2026-06-28 by task R7-090.*
