# Task 024 (D2-14) — `InvokeInsightsQueryTool` evidence

> **Status**: ✅ complete (sub-agent code authoring; main session owns build/publish/CVE verification)
> **Completed**: 2026-06-04
> **Wave**: Phase 2 P2-G6 (Insights tool integration — first task in suite)
> **Sibling registered alongside**: task 015 `InvokeSummarizePlaybookTool` (NFR-12 audit pair)

---

## Files created / modified

### New code

| File | Purpose | LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/InvokeInsightsQueryTool.cs` | NEW — tool wrapper with `InsightsQueryAsync` method, `InsightsToolException`, Zone B HTTP consumption, ProblemDetails parsing, v1.1 SSE opt-in + 406 graceful fallback | ~470 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/InvokeInsightsQueryToolTests.cs` | NEW — 28 unit tests covering tool catalog, NFR-12 description distinctness, HTTP shape, v1.1 SSE/406, correlation-id propagation, ADR-028 no-token-snapshot, 12 contract error codes, Zone B boundary (static-analysis of source), v1.1 forward-compat | ~580 |

### Extended existing files

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` | Added `InsightsQuery = "insights_query"` capability constant + added to `All` + `CoreCapabilities` sets |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Added `services.AddHttpClient<InvokeInsightsQueryTool>(...)` typed-client registration inside `AddAnalysisOrchestrationServices` (single line + comment block; ZERO new Program.cs lines) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Added capability-gated tool resolution block in `ResolveTools` adjacent to task 015's `InvokeSummarizePlaybookTool` block (per audit guardrail) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs` | Added routing-selection test `CreateAgentAsync_WithInsightsQueryCapability_RegistersInsightsQueryTool` + helper `BuildServiceProviderWithInsightsQueryTool` |

---

## Audit-compliance section (R5 `notes/r5-chat-agent-parallel-build-audit.md`)

### 1. Registered INSIDE existing `SprkChatAgentFactory.ResolveTools` block

✅ **Confirmed**. The new tool block is placed at `SprkChatAgentFactory.cs:898+`, immediately after task 015's `InvokeSummarizePlaybookTool` block and before `WebSearchTools`. It follows the exact pattern:

- `if (capabilities.Contains(PlaybookCapabilities.InsightsQuery))` gate
- `attempted++ / resolved++ / failedTools.Add(...)` bookkeeping (AIPU2-063 per-tool error isolation)
- Uses `AIFunctionFactory.Create` via the tool class's `GetTools()` method
- Logs warning on failure; does NOT throw out of `ResolveTools`

NO parallel chat agent. NO parallel tool registry. NO parallel HTTP client framework.

### 2. NO injection of Insights-internal types (Zone B boundary)

✅ **Confirmed by both static-analysis test AND reflection assertion**.

`InvokeInsightsQueryTool.cs` `using` block:
```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
```

Zero imports from `Sprk.Bff.Api.Services.Ai.Insights` or `Sprk.Bff.Api.Models.Insights`. The request/response DTOs are PRIVATE nested records (`InsightsQueryRequest`, `InsightsConversationContext`, `ProblemDetailsPayload`) local to the tool class.

Constructor dependencies (verified by reflection in Test 6 `ZoneBoundary_SourceFile_ContainsNoInsightsInternalImports`):
- `HttpClient` (System.Net.Http) — typed client
- `IHttpContextAccessor?` (Microsoft.AspNetCore.Http) — for fresh OBO token forwarding
- `ILogger<InvokeInsightsQueryTool>` — logger

NO `IInsightsAi`, `InsightsOrchestrator`, `AssistantToolCallHandler`, `InsightsIntentClassifier`, `IRagService`, `IInsightsPlaybookExecutionCache`, `ISubjectParser`, or any other Insights-internal type.

### 3. NO token snapshots (ADR-028)

✅ **Confirmed by Test 9** (`InsightsQueryAsync_NoTokenSnapshot_ReadsFreshTokenPerCall_AdrL028`).

The tool reads `_httpContextAccessor?.HttpContext?.Request.Headers.Authorization` fresh per HTTP call inside the private helper `ForwardBearerToken(HttpRequestMessage)`. The constructor does NOT capture any token value. Test 9 simulates token rotation mid-session (token-A → token-B between calls) and asserts the second outbound request carries the rotated value.

### 4. Capability manifest allow-list update

For now: `insights.query` is added to the LOCAL `PlaybookCapabilities.CoreCapabilities` allow-list (mirrors task 015's approach for `invoke_summarize_playbook`). When `playbookId: null` (standalone chat mode), the chat agent uses `CoreCapabilities` which now includes `InsightsQuery`, so the tool is discoverable.

**Note for R6 follow-up** (matches task 015 evidence note): the Dataverse capability manifest's `sprk_playbookcapabilities` global multi-select choice does NOT yet include an integer code for `insights_query`. Insights r3 F-4 backfill is scheduled to add both `invoke_summarize_playbook` AND `insights.query` rows to the manifest. Until then, AIPU2-061 Layer-2 narrow routing falls back to Layer-3 full-capability-set return, which is still correct (tool IS discoverable; just not narrowly routed).

---

## NFR-12 audit pair (description text cross-reference with task 015)

### Task 015 — `invoke_summarize_playbook` description (verbatim from `InvokeSummarizePlaybookTool.cs`)

> "Summarize the user's currently-uploaded chat session files (or a specific subset by fileIds) using the Summarize playbook. Returns a streamed structured summary with a TL;DR and per-file highlights. Use for ANY natural-language request to summarize, recap, distill, TL;DR, or produce an executive overview of the files attached to the current chat session (e.g., 'summarize the attached document', 'TL;DR these files', 'give me a bullet recap of what I uploaded'). Do NOT use this tool for analytical questions about a matter, project, or invoice entity — those are handled by insights.query."

### Task 024 — `insights.query` description (verbatim from `InvokeInsightsQueryTool.cs`)

> "Answer matter/project/invoice-scoped analytical questions about entities. Use for questions about a specific matter, project, or invoice (predicted cost, key dates, closing conditions, outstanding amounts, status updates, comparable outcomes, etc.). The tool routes server-side between a predictive playbook path (structured answer) and a citation-grounded RAG path (cited prose). Do NOT use this tool for summarizing files uploaded to the current chat session (use invoke_summarize_playbook for that). Do NOT use for free-form web research or text refinement."

### Cross-reference assertions (UR-01 mitigation)

| Aspect | Task 015 (`invoke_summarize_playbook`) | Task 024 (`insights.query`) |
|---|---|---|
| Scope | session-uploaded files | matter/project/invoice entities |
| Primary verbs | "summarize", "recap", "distill", "TL;DR", "executive overview" | "predicted cost", "key dates", "closing conditions", "outstanding amounts", "status updates", "comparable outcomes" |
| Negative-routing guard | "Do NOT use ... for analytical questions about a matter, project, or invoice entity — those are handled by insights.query" | "Do NOT use ... for summarizing files uploaded to the current chat session (use invoke_summarize_playbook for that)" |
| Path-awareness | streamed structured summary (TL;DR + per-file highlights) | server-side routes between playbook (structured) and RAG (cited prose) |

Both descriptions explicitly name the OTHER tool as the alternative for the negative-routing case. Test 2 (`ToolDescription_IsSemanticallyDistinctFrom_InvokeSummarizePlaybook_NFR12`) asserts:
- `insights.query` description contains "matter", "project", "invoice"
- `insights.query` description contains the verbatim string `"invoke_summarize_playbook"` for differentiation
- Length is in the 100..800 range (LLM tool-schema token-budget discipline)
- Description is NOT character-for-character identical to Summarize description

---

## Tool description and parameter description (recorded for NFR-12 audit)

| Field | Description |
|---|---|
| **Method-level (tool description)** | "Answer matter/project/invoice-scoped analytical questions about entities. Use for questions about a specific matter, project, or invoice (predicted cost, key dates, closing conditions, outstanding amounts, status updates, comparable outcomes, etc.). The tool routes server-side between a predictive playbook path (structured answer) and a citation-grounded RAG path (cited prose). Do NOT use this tool for summarizing files uploaded to the current chat session (use invoke_summarize_playbook for that). Do NOT use for free-form web research or text refinement." (727 chars) |
| **Parameter `query`** | "The user's natural-language question (1..500 chars). Required." |
| **Parameter `subject`** | "The scope entity in the format '<scheme>:<guid>' where scheme is 'matter', 'project', or 'invoice'. Resolved from the active chat host context. Required." |
| **Parameter `forceMode`** | "Optional intent override. Set to 'playbook' or 'rag' when invoking via an explicit slash command. Omit for natural-language tool-calls — the BFF intent classifier will route automatically." |

---

## Capability and config decisions

| Decision | Value |
|---|---|
| Gating capability constant | `PlaybookCapabilities.InsightsQuery = "insights_query"` (NEW; added to `All` + `CoreCapabilities`) |
| Tool name (AIFunction) | `insights.query` (matches binding contract v1.0 §3.1) |
| HttpClient registration | Typed client `services.AddHttpClient<InvokeInsightsQueryTool>(...)` inside `AnalysisServicesModule.AddAnalysisOrchestrationServices` — ZERO new Program.cs lines |
| Config key | `Bff:BaseAddress` (existing convention; fallback `https://localhost:7001` for local dev) |
| HTTP timeout | 60s (Insights playbook path p95 ~2s; RAG path can spike) |
| Default Accept (registration time) | `application/json` |
| Per-call Accept (v1.1 opt-in) | `text/event-stream` + `application/json` fallback |
| Correlation ID source | `Activity.Current?.Id` (ambient ASP.NET diagnostic source) → fallback `Guid.NewGuid().ToString("N")` |

---

## Test coverage (28 new tests in `InvokeInsightsQueryToolTests` + 1 in `SprkChatAgentFactoryTests`)

### Coverage matrix vs task 024 §3.7 obligation

| Sub-section | Test(s) | Status |
|---|---|---|
| (a) Tool registered with correct name + description | `GetTools_YieldsExactlyOneFunction_WithCanonicalNameAndDescription` (Tools) + `CreateAgentAsync_WithInsightsQueryCapability_RegistersInsightsQueryTool` (Factory) | ✅ |
| (b) Zone B boundary (no Insights internals imported) | `ZoneBoundary_SourceFile_ContainsNoInsightsInternalImports` — static-analysis (using-line scan) + reflection on private fields | ✅ |
| (c) NFR-12 description distinct from `invoke_summarize_playbook` | `ToolDescription_IsSemanticallyDistinctFrom_InvokeSummarizePlaybook_NFR12` | ✅ |
| (d) HTTP POST to `/api/insights/assistant/query` with correct payload | `InsightsQueryAsync_PostsToCorrectEndpoint_WithExpectedJsonBody` | ✅ |
| (e) v1.1 SSE opt-in via `Accept: text/event-stream` | `InsightsQueryAsync_SendsSseAcceptHeader_PerV11OptIn` | ✅ |
| (f) 406 fallback to JSON | `InsightsQueryAsync_OnHttp406_FallsBackToJsonOnlyAndSucceeds` | ✅ |
| (g) `forceMode` parameter passes through correctly | `InsightsQueryAsync_NullForceMode_OmittedOrNullInRequestBody` + `InsightsQueryAsync_ExplicitForceMode_PropagatesToBffWithoutValidation` (Theory: playbook/rag/garbage) | ✅ |
| (h) correlation-id propagated end-to-end | `InsightsQueryAsync_SetsCorrelationIdHeader_OnEveryOutboundRequest` + `InsightsQueryAsync_TwoCalls_GenerateDistinctCorrelationIds` | ✅ |
| (i) no token snapshots (token re-fetched per call) | `InsightsQueryAsync_NoTokenSnapshot_ReadsFreshTokenPerCall_AdrL028` (token-A → token-B rotation) | ✅ |
| (j) 12 error codes from contract surfaced via `InsightsToolException` | `InsightsQueryAsync_OnProblemDetailsError_SurfacesContractErrorCodeViaException` Theory (10 explicit codes) + `InsightsQueryAsync_On401WithNoErrorCode_SurfacesSyntheticAuth401` + `InsightsQueryAsync_On429WithRetryAfter_SurfacesRateLimit429WithRetryAfter` | ✅ |
| BONUS — v1.1 forward-compat | `InsightsQueryAsync_OnSuccess_ForwardsUnknownFieldsVerbatim_V11ForwardCompat` (citations[].href, streamingSupported, structuredResult.envelope.extraField pass through unchanged) | ✅ |
| BONUS — constructor null-guards | 2 tests (`Constructor_ThrowsArgumentNullException_WhenHttpClientIsNull`, `Constructor_ThrowsArgumentNullException_WhenLoggerIsNull`) | ✅ |

### 12 contract error codes coverage (integration brief §5.1)

| HTTP | errorCode | Test mechanism |
|---|---|---|
| 400 | `query.required` | Theory row |
| 400 | `subject.required` | Theory row |
| 400 | `subject.invalid` | Theory row |
| 400 | `forceMode.invalid` | Theory row |
| 400 | `conversationContext.invalid` | Theory row |
| 401 | `auth.401` (synthetic) | Dedicated test |
| 429 | `rate-limit.429` (synthetic + Retry-After) | Dedicated test |
| 503 | `ai.insights.disabled` | Theory row |
| 503 | `ai.rag.disabled` | Theory row |
| 503 | `ai.intent-classification.disabled` | Theory row |
| 503 | `ai.assistant-default-playbook.unconfigured` | Theory row |
| 500 | `INSIGHTS_ASSISTANT_INTERNAL_ERROR` | Theory row |

All 12 codes verified to surface verbatim via `InsightsToolException.ErrorCode`. `correlationId`, `status`, `title`, `detail` preserved on every code.

---

## Test run summary

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~InvokeInsightsQueryTool"
Passed!  - Failed: 0, Passed: 28, Skipped: 0, Total: 28
```

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SprkChatAgentFactoryTests"
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11
```

Full BFF test suite:
```
dotnet test tests/unit/Sprk.Bff.Api.Tests/
Passed!  - Failed: 0, Passed: 6198, Skipped: 111, Total: 6309
```

Delta vs Wave C baseline (6169): **+29 new tests** (28 InvokeInsightsQueryTool + 1 routing-selection). 0 regressions.

---

## Deferred to main session (per task brief — sub-agent scope: CODE AUTHORING)

The following verification belongs to the main session post-merge:

1. **BFF publish-size measurement** (R5 §3.6 + CLAUDE.md §10):
   ```
   dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
   ```
   Expected delta ≤ +0.05 MB (single new tool class + one HTTP-client registration line). Baseline as of Wave C: ~45 MB. Hard ceiling: 60 MB.

2. **CVE scan** (`dotnet list package --vulnerable --include-transitive`) — no new HIGH expected (no new NuGet refs).

3. **Smoke test against Spaarke Dev BFF** (task POML Step 9) — positive prompts ("what will this matter cost?"), negative prompts (Summarize prompts MUST NOT route here), slash command `forceMode` path. Reserved for downstream task 030 (D2-20 smoke tests).

4. **code-review + adr-check skill invocations** (Step 9.5 quality gates).

---

## Constraints satisfied

| Constraint | Status |
|---|---|
| R5 CLAUDE.md §3.1 reuse mandate (no parallel chat agent / no parallel tool framework / no parallel HTTP client) | ✅ — registered inside existing `ResolveTools` block; existing `IHttpClientFactory` typed client; existing `AIFunctionFactory.Create` pattern |
| R5 CLAUDE.md §3.2 no new feature flags | ✅ — gated by existing capability check; kill-switch inherits via Insights endpoint's own 503s |
| R5 CLAUDE.md §3.3 DI minimalism (zero new Program.cs lines) | ✅ — typed HttpClient registered inside `AnalysisServicesModule.AddAnalysisOrchestrationServices` |
| R5 CLAUDE.md §3.5 / §10 Insights tool governance (Zone B; HTTP only; no Insights internals) | ✅ — verified by static-analysis test + reflection field-type check |
| R5 CLAUDE.md §3.7 test obligation (tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/`) | ✅ — 28 new tool tests + 1 factory routing test |
| ADR-013 §3.5 (BFF-only AI; agents via factory; R5 is Zone B HTTP consumer) | ✅ |
| ADR-010 (DI minimalism; concrete classes; constructor ≤10 params) | ✅ — concrete `sealed class`; 3-param constructor; ZERO new top-level DI lines |
| ADR-018 (no new feature flags) | ✅ |
| ADR-019 (ProblemDetails error parsing) | ✅ — `ProblemDetailsPayload` record + private `TryParseProblemDetails` |
| ADR-028 (no token snapshots; fresh OBO per request) | ✅ — `ForwardBearerToken` reads `HttpContext.Request.Headers.Authorization` per call; Test 9 confirms |
| ADR-016 (rate-limit honoring) | ✅ — 429 surfaced structurally with `Retry-After` preserved; no in-tool auto-retry |

---

*Authored 2026-06-04 by R5 task 024 sub-agent (code authoring scope). Main session handles build verification + publish-size + CVE + Spaarke Dev smoke + quality-gate skills.*
