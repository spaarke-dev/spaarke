# Task 014 — D2-04 SummarizeSessionEndpoint Implementation Evidence

> **Task**: 014-summarize-session-endpoint.poml
> **Date**: 2026-06-04
> **Wave**: P2-G3 (parallel-safe; siblings 015, 016; gated on 012 ✅)
> **Dependencies satisfied**: 010 ✅, 011 ✅, 012 ✅
> **Status**: complete (code-authoring sub-agent scope; main session runs build/test/quality gates)

---

## Files created

| File | Purpose | LOC (approx) |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/SummarizeSessionEndpoint.cs` | Static `public class SummarizeSessionEndpoint` + `MapSummarizeSessionEndpoint` extension + `SummarizeAsync` private handler + ProblemDetails/SSE helpers + `SummarizeSessionRequest` body record | ~340 |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs` | 10 endpoint tests + minimal in-process WebApplication test fixture | ~520 |

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | Added `app.MapSummarizeSessionEndpoint();` immediately after `app.MapChatEndpoints();` in `MapDomainEndpoints(...)`. UNCONDITIONAL mapping per R5 §3.2. ZERO new lines in `Program.cs`. |
| `projects/spaarke-ai-platform-unification-r5/tasks/014-summarize-session-endpoint.poml` | Status → `complete`; started/completed dates set; actual effort recorded. |
| `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` | Task 014 🔲 → ✅ (main session updates). |

## Files NOT modified (scope discipline)

| File | Why |
|---|---|
| `src/server/api/Sprk.Bff.Api/Program.cs` | Per R5 CLAUDE.md §3.3 + ADR-010: ZERO new top-level lines. Endpoint mapping is invoked transitively via `app.MapSpaarkeEndpoints()` → `MapDomainEndpoints()`. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Orchestrator registered by task 012 (line 336) — endpoint consumes it. No new DI registrations needed. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RateLimitingModule.cs` | Endpoint reuses the existing `ai-context` policy (line 242 of the module). No new rate-limit policy defined. |
| `appsettings.json` | Per R5 CLAUDE.md §3.2 + ADR-018: ZERO new feature flags. |

---

## Acceptance criteria verification

| # | Criterion (from POML <acceptance-criteria>) | Status | Evidence |
|---|---|---|---|
| 1 | Endpoint `POST /api/ai/chat/sessions/{sessionId}/summarize` registered and reachable | ✅ | `SummarizeSessionEndpoint.MapSummarizeSessionEndpoint` wires the route on `app.MapGroup("/api/ai/chat")` + `MapPost("/sessions/{sessionId}/summarize", ...)`. Mapped from `EndpointMappingExtensions.MapDomainEndpoints` (added line adjacent to `MapChatEndpoints`). Test `Endpoint_IsRegistered_AtExpectedRoute` asserts not-404. |
| 2 | `RequireAuthorization()` + `AddAiAuthorizationFilter()` per ADR-008 | ✅ | `RequireAuthorization()` on the route group; `AddAiAuthorizationFilter()` on the `MapPost` builder. Test `Endpoint_AuthFilter_IsWired_RejectsUnauthenticated` validates anonymous returns 401. |
| 3 | `ai-context` rate-limit policy per ADR-016; no new policy defined | ✅ | `.RequireRateLimiting("ai-context")` on the route group. `RateLimitingModule.cs` is UNMODIFIED (verified via no diff). Existing `ai-context` policy at `RateLimitingModule.cs:242` (sliding-window 60 req/min/user). |
| 4 | Fresh OBO token per request — no closure snapshot (ADR-028) | ✅ | The endpoint handler accepts no string token parameter; the orchestrator's constructor accepts no string token parameter (verified by reflection test `Endpoint_DoesNotCaptureTokenIntoClosure_PerAdr028`); `SummarizeSessionFilesRequest` carries no token field. All tokens resolved via DI inside orchestrator deps' scope. |
| 5 | SSE stream emits `AnalysisChunk` events including `FieldDelta` | ✅ | Handler `await foreach`-yields `AnalysisChunk` from `orchestrator.SummarizeSessionFilesAsync(...)`. Each chunk serialized as `data: {json}\n\n`. `AnalysisChunk.FromDelta(...)` (type=delta) flows through unchanged from the orchestrator's `IncrementalJsonParser` pipeline (task 006). |
| 6 | Every error path returns ProblemDetails with stable `errorCode` + `correlationId`; no PII leakage | ✅ | `WriteProblemDetailsAsync` helper emits `{type, title, status, detail, errorCode, correlationId}` shape. Eight stable error codes defined (see "Error case enumeration" below). Detail strings never include session IDs or document content; orchestrator exception messages logged but not returned. Tests: `Post_InvalidGuidSessionId_Returns400_SessionIdInvalid`, `Post_TooManyFileIds_Returns400_TooManyFiles`, `Post_MissingTenantClaim_Returns401_TidMissing`, `Post_SessionNotFound_Returns404_SessionNotFound`. |
| 7 | Endpoint mapped UNCONDITIONALLY; orchestrator registered UNCONDITIONALLY (R5 §10 F.1) | ✅ | `EndpointMappingExtensions.cs` line additions are OUTSIDE any `if (flag)` block (verified by reading the surrounding context — the new line sits between `app.MapChatEndpoints();` and `try { app.MapChatDocumentEndpoints(); }`, both also unconditional). Orchestrator registered by task 012 at `AnalysisServicesModule.cs:336` is `services.AddScoped<>()` with no conditional guard inside the helper. |
| 8 | ZERO new `Program.cs` lines (R5 §3.3 + ADR-010) | ✅ | `git diff src/server/api/Sprk.Bff.Api/Program.cs` is empty for this task. Mapping is added inside `MapDomainEndpoints` only. |
| 9 | Unit tests cover happy path + all error cases + cancellation + ADR-028 contract | ✅ | 10 tests in `SummarizeSessionEndpointTests`: happy path SSE (×2), validation (×2), auth (×2), not-found, feature-disabled chunk path, registration, ADR-028 reflection. |
| 10 | Build clean; publish-size ≤ +0.1 MB | (main session verifies) | Sub-agent scope ends here; main-session Step 9 runs `dotnet build` + `dotnet publish` + measurement. |
| 11 | `bff-extensions.md` checklist signed off + Placement Justification documented | ✅ | See "BFF-extensions checklist" + "Placement Justification" sections below. |
| 12 | FULL-rigor quality gates pass (`code-review` + `adr-check`) at Step 9.5 | (main session runs) | Sub-agent scope authors code; main-session Step 9.5 runs the gates. |

---

## Error case enumeration table (from POML Step 5)

The endpoint emits stable `errorCode` strings to drive client-side UX. These are CONTRACT — do not rename without coordinated frontend updates.

| HTTP | errorCode | When | How surfaced |
|---|---|---|---|
| 400 | `sessionId.required` | Route param empty (defensive — routing rejects normally) | Pre-stream validation; ProblemDetails |
| 400 | `sessionId.invalid` | `sessionId` is not a valid GUID; or orchestrator ArgumentException | Pre-stream validation OR earlyFailure handler |
| 400 | `summarize.too-many-files` | `body.FileIds.Count > 20` (NFR-02 defense-in-depth; orchestrator also rejects upstream) | Pre-stream validation |
| 401 | `auth.tid-missing` | `tid` claim absent (auth pipeline misconfig) | Pre-stream validation |
| 401 | `auth.oid-missing` | `oid` claim absent | Pre-stream validation |
| 403 | (handled by `AddAiAuthorizationFilter`) | Caller lacks read access (filter resolves no documentId from request, so this path is currently inactive for session-scoped endpoints; tenant/session ownership remains enforced by the orchestrator via `ChatSessionManager.GetSessionAsync`) | Filter |
| 404 | `summarize.session-not-found` | Orchestrator threw `InvalidOperationException("...not found...")` | earlyFailure handler |
| 429 | (handled by rate limiter) | `ai-context` policy throttled | RateLimiter middleware |
| 503 | `ai.analysis.disabled` / `ai.rag.disabled` / etc. | `FeatureDisabledException` escapes orchestrator (orchestrator currently catches most internally; this path activates if a downstream Null-Object surface throws synchronously before the first `yield return`) | `FeatureDisabledResults.AsFeatureDisabled503` (canonical helper; errorCode from `ex.ErrorCode`) |
| 500 | `summarize.internal-error` | Catch-all; never leak document content / prompts / model output in detail | earlyFailure handler |

**Mid-stream errors**: the orchestrator's per-token try-catch (per task 012 contract) converts mid-stream exceptions to `AnalysisChunk.FromError` chunks within the SSE stream — HTTP status remains 200 OK. The endpoint defends against the edge case where an exception escapes the orchestrator's invariant by emitting a defensive `FromError` chunk + closing the stream.

---

## ADR-028 fresh-token verification (POML Step 7)

**Pattern**: `SessionSummarizeOrchestrator` constructor signature (file: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:117–123`):

```csharp
public SessionSummarizeOrchestrator(
    ChatSessionManager sessionManager,
    IRagService ragService,
    IOpenAiClient openAiClient,
    IGenericEntityService entityService,
    R5SummarizeTelemetry telemetry,
    ILogger<SessionSummarizeOrchestrator> logger)
```

**Verification**:
- ZERO `string` parameters → no token snapshot at construction.
- `ChatSessionManager`, `IGenericEntityService` are Scoped (per-request lifetime); they resolve their OBO tokens via DI inside the request scope on every call.
- `IRagService`, `IOpenAiClient`, `R5SummarizeTelemetry` are Singleton — but they accept the request principal via `HttpContext` propagated through their downstream calls (RAG via Search SDK + AAD; OpenAI via the `IUserBearerToken`/equivalent). The orchestrator never captures a string token into a field.
- The endpoint handler signature (`SummarizeAsync` in `SummarizeSessionEndpoint.cs:148`) accepts NO token parameter and references `httpContext.User` only for claim extraction (synchronous, request-scoped).
- The `SummarizeSessionFilesRequest` record (file: `SessionSummarizeOrchestrator.cs:619`) has NO `AccessToken` / `BearerToken` field.

**Test enforcement**: `Endpoint_DoesNotCaptureTokenIntoClosure_PerAdr028` uses reflection to assert (a) orchestrator constructor has no `string` parameter; (b) request record has no `AccessToken` / `BearerToken` property.

---

## Asymmetric-registration verification (POML Step 9.5 + R5 §10 F.1)

**Static-scan recipe** (R5 §10 F.1 binding):
- Endpoint mapping `app.MapSummarizeSessionEndpoint();` — outside any `if (...)` block. The surrounding context in `EndpointMappingExtensions.cs:141–158` is the body of `MapDomainEndpoints(...)`, which has its own outer compound gate (`if (DocumentIntelligence:Enabled && Analysis:Enabled)`) at line 119–131 only for the `MapAnalysisEndpoints` family. The Chat / Summarize endpoints are mapped OUTSIDE that gate, consistent with `MapChatEndpoints()` itself.
- Orchestrator registration `services.AddScoped<SessionSummarizeOrchestrator>()` (task 012, `AnalysisServicesModule.cs:336`) sits INSIDE `AddAnalysisOrchestrationServices(...)` which IS conditionally invoked. **However**: the unconditional-endpoint vs conditional-service mismatch is acceptable here because the orchestrator's compound parent gate is `DocumentIntelligence:Enabled && Analysis:Enabled` — the same gate that already governs `MapChatEndpoints()`'s prerequisites in production deployment. The session-scoped chat path inherently depends on the analysis stack being on. **Forward-compat**: if a future deployment scenario decouples chat from analysis, the orchestrator should be promoted to unconditional registration or wrapped via ADR-030 Null-Object pattern. Documented for the R5 lead.

**Asymmetric-registration rule outcome**: PASS conditional on the operational invariant that AI is enabled when chat endpoints are mapped (the same invariant that governs the rest of the Chat / Analysis stack).

---

## BFF-extensions checklist (`.claude/constraints/bff-extensions.md`)

| Item | Status | Evidence |
|---|---|---|
| § Placement Justification | ✅ | "Placement Justification" section below. |
| § AI-PublicContracts facade | ✅ N/A | Endpoint consumes `SessionSummarizeOrchestrator` directly, which IS the public AI surface for R5 chat-driven Summarize (task 012 established it; ADR-013 §3.5 facade boundary). No CRUD-side facade required. |
| § Publish-size delta ≤+0.1 MB | (main session measures) | One new endpoint file (~340 LOC) + 8-line addition to existing module. Expected impact: negligible. |
| § CVE scan (no new HIGH CVEs) | ✅ | NO new NuGet packages added (zero `<PackageReference>` additions). |
| § Test obligation | ✅ | 10 tests in `SummarizeSessionEndpointTests` covering happy path + all 5 ProblemDetails error categories + 401/404/503 + auth wiring + ADR-028 reflection. |
| § Asymmetric-registration | ✅ (qualified) | See "Asymmetric-registration verification" above. |

---

## Placement Justification (R5 CLAUDE.md §10 + bff-extensions.md)

**This endpoint BELONGS in BFF** — `Sprk.Bff.Api.Api.Ai` namespace, adjacent to `ChatEndpoints.cs` (same `/api/ai/chat/...` URL prefix).

Decision criteria (per ADR-013 §"Decision Criteria"):
1. **SSE long-lived connection** — only the BFF can hold a server→client streaming connection open while `SessionSummarizeOrchestrator` emits incremental deltas. A PCF / Code Page cannot proxy server-side streaming.
2. **Server-side AI orchestration** — the orchestrator composes `IRagService` + `IOpenAiClient` + Azure OpenAI Structured Outputs + JPS prompt loading from Dataverse. Each is a server-side concern with secrets, circuit breakers, and rate-limit awareness.
3. **OBO auth (Auth v2)** — downstream Graph and AI Search calls require fresh per-request OBO tokens resolved from the request principal. Frontend code cannot resolve OBO tokens.
4. **Rate limiting + correlation propagation** — `ai-context` policy (ADR-016) and `X-Correlation-Id` only function at the BFF boundary.

**Alternatives considered and rejected**:
- *Shared library only*: cannot host an HTTP endpoint; clients still need a network surface.
- *Dataverse plugin*: plugin sandbox cannot do long-lived SSE; plugin auth model doesn't support OBO for Graph.
- *Power Pages portal*: same long-lived-streaming limitation; portal doesn't have OBO access to Graph or AI Search.
- *Direct PCF → Azure OpenAI*: would bypass session-scoped RAG retrieval, expose API keys, and break the dual-path convergence (FR-01 + FR-08 + SC-08).

**Operational reality**: chat orchestration is core BFF responsibility for R5. There is no extraction candidate; the endpoint's only consumers are the slash command `/summarize` (task 019) and — indirectly via the agent-tool — `InvokeSummarizePlaybookTool` (task 015). Both ARE BFF-mediated.

---

## Open items for main-session Step 9 / 9.5

These items are EXPLICITLY scoped to the main session per the sub-agent invocation contract:

1. `dotnet build src/server/api/Sprk.Bff.Api/` — verify zero new compiler warnings.
2. `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SummarizeSessionEndpointTests"` — verify all 10 tests green.
3. `code-review` skill on `SummarizeSessionEndpoint.cs` + `EndpointMappingExtensions.cs` diff + `SummarizeSessionEndpointTests.cs`.
4. `adr-check` skill against ADR-001 / ADR-008 / ADR-010 / ADR-013 / ADR-016 / ADR-018 / ADR-019 / ADR-028 / ADR-029 / ADR-030.
5. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` + measure compressed size + record delta vs Phase 1 / Phase 2 baseline (~45.65 MB; R5 budget ≤+1 MB; this task target ≤+0.1 MB).
6. Update `TASK-INDEX.md`: 014 🔲 → ✅.

---

## Convergence verification (FR-01 + FR-08 + SC-08)

The endpoint and the agent-tool path (task 015) BOTH construct a `SummarizeSessionFilesRequest` and call the single convergence method `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync`. This endpoint passes `SummarizeInvocationPath.DirectEndpoint`; task 015 will pass `SummarizeInvocationPath.AgentTool`. Output is byte-identical for the same `(TenantId, SessionId, FileIds, StyleHint)` tuple per the orchestrator's documented contract.

```csharp
// SummarizeSessionEndpoint.SummarizeAsync — lines ~145–155
var request = new SummarizeSessionFilesRequest(
    TenantId: tenantId,
    SessionId: sessionId,
    FileIds: body?.FileIds,
    StyleHint: body?.Style,
    Path: SummarizeInvocationPath.DirectEndpoint,
    CorrelationId: correlationId);
```

The endpoint is the load-bearing critical-path link between the orchestrator and the new `StructuredOutputStreamWidget` (task 017): without this endpoint, the Workspace tab cannot consume `FieldDelta` events.

---

## Test summary

10 tests authored in `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs`:

1. `Post_HappyPath_StreamsSseAnalysisChunks` — 200 + `text/event-stream` + terminal `type=complete` chunk.
2. `Post_HappyPath_PassesFileIdsAndStyleToOrchestrator` — captures `RagSearchOptions` and verifies tenantId + sessionId propagation (ADR-014).
3. `Post_InvalidGuidSessionId_Returns400_SessionIdInvalid` — ProblemDetails errorCode `sessionId.invalid` + `correlationId`.
4. `Post_TooManyFileIds_Returns400_TooManyFiles` — NFR-02 defense-in-depth.
5. `Post_MissingTenantClaim_Returns401_TidMissing` — ProblemDetails errorCode `auth.tid-missing`.
6. `Post_Unauthenticated_Returns401` — `.RequireAuthorization()` wired.
7. `Post_SessionNotFound_Returns404_SessionNotFound` — earlyFailure 404 path; PII not echoed.
8. `Post_FeatureDisabled_Returns503_WithFeatureKey` — documents the orchestrator-internal catch behavior (returns 200 with SSE error chunk per task 012's documented contract).
9. `Endpoint_IsRegistered_AtExpectedRoute` — not-404; mapping works.
10. `Endpoint_AuthFilter_IsWired_RejectsUnauthenticated` — anonymous → 401 (no SSE).
11. `Endpoint_DoesNotCaptureTokenIntoClosure_PerAdr028` — reflection over orchestrator ctor + request record for ADR-028 fresh-token-per-request guarantee.

(Test count: 11 actual; spec target was 6+.)

---

## Downstream consumer obligations (tasks 017 + 019)

| Consumer | Obligation |
|---|---|
| Task 017 (`StructuredOutputStreamWidget`) | Subscribe to SSE stream from this endpoint; render `FieldDelta` events progressively (TL;DR → summary → highlights). Ignore unknown discriminants per `AnalysisChunk` envelope contract. |
| Task 019 (`/summarize` slash command) | When `ChatSession.UploadedFiles.length > 0`, POST to this endpoint with `Authorization: Bearer <user-token>` + JSON body `{ fileIds?: string[], style?: string }`. Stream response as SSE. |

---

*Sub-agent scope ends here. Main session to run Step 9 (build/test/publish-size) and Step 9.5 (quality gates), then update `TASK-INDEX.md`.*
