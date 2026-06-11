# Wave 8 — WebSearchTools migration to WebSearchHandler

**Status**: COMPLETE (parallel agent — Wave 8)
**Date**: 2026-06-08
**Predecessor**: Wave 7b (capability filter + citation envelope infra), Wave 7c (KnowledgeRetrievalHandler + VerifyCitationsHandler templates)
**Tool migrated**: 1 of 4 Wave 8 chat tools (WebSearchTools → WebSearchHandler)

---

## Summary

Migrated `WebSearchTools` (a hardcoded factory-instantiated AI tool class at
`Services/Ai/Chat/Tools/WebSearchTools.cs`) to the data-driven `IToolHandler` pattern using
the Wave 7b citation-envelope infrastructure. The legacy class still exists; main session
removes the hardcoded factory block at `SprkChatAgentFactory.ResolveTools` lines 1023–1064
after all Wave 8 agents land. Main session also adds the new seed-row JSON to
`scripts/Seed-TypedHandlers.ps1`'s `$RowFiles` array.

The migration preserves every legacy behavior (Bing v7 API call shape, concurrency policy,
mock fallback, scope guidance, error degradation notes) while moving the cross-cutting
citation accumulation from in-tool `CitationContext.AddCitation` calls to the Wave 7b
metadata envelope picked up by `ToolHandlerToAIFunctionAdapter`.

---

## Resolved `PlaybookCapabilities.WebSearch` literal

**Verified by reading `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs`
line 43**:

```csharp
public const string WebSearch = "web_search";
```

Dataverse option set integer code: **100000005** (per the docblock on the constants class
covering all 10 capability values).

The seed row's `sprk_requiredcapability = "web_search"` value matches this constant
verbatim (case-sensitive). The Wave 7b `IsCapabilityGateSatisfied` helper does a
case-insensitive match, so authoring-side typos like `"Web_Search"` would also pass, but
the seed row uses the canonical lowercase form.

---

## Chat-only context decision

The seed row uses `sprk_availableincontexts = 100000001` (Chat only) and the handler
declares `SupportedInvocationContexts => InvocationContextKind.Chat`. Rationale:

- The legacy `WebSearchTools` class was wired into the chat agent factory only — there is
  no playbook node that calls `SearchWebAsync`. Wave 8 preserves this.
- The handler's `ExecuteAsync` (playbook path) returns
  `ToolResult.Error(..., ToolErrorCodes.ValidationFailed, ...)` if accidentally invoked
  from a playbook orchestration. `Validate` also fails fast on the playbook path.
- This matches the `Chat`-only shape rather than `Both` (used by VerifyCitations,
  KnowledgeRetrieval) — those two have explicit playbook use cases (orchestrated
  citation verification, knowledge retrieval as a playbook step). Web search does not.

---

## Files created

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/WebSearchHandler.cs` | The typed handler. Implements `IToolHandler` with `SupportedInvocationContexts = Chat`. Auto-discovered (ADR-010 — no DI line). |
| `infra/dataverse/sprk_analysistool-web-search-row.json` | Seed row (`SYS-Web Search`, `WEB-SEARCH`, `sprk_handlerclass = WebSearchHandler`, `sprk_requiredcapability = web_search`, chat-only). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/WebSearchHandlerTests.cs` | 26 tests — 4-point contract, validate paths, happy path with mocked Bing, mock fallback when no API key, scope-guided search query construction (FR-10), HTTP/JSON failure degradation, cancellation, ADR-015 telemetry sentinel scan, concurrency-gate reflection assertion. |
| `projects/spaarke-ai-platform-unification-r6/notes/wave-08-web-search-migration.md` | This bookkeeping note. |

---

## Preserved behaviors (verbatim from legacy `WebSearchTools`)

| Behavior | Source line | Wave 8 location |
|---|---|---|
| Static `SemaphoreSlim(2, 2)` concurrency gate | `WebSearchTools.cs:40` | `WebSearchHandler.s_bingConcurrencyGate` |
| 10s semaphore timeout | `WebSearchTools.cs:45` | `WebSearchHandler.s_semaphoreTimeout` |
| 5s HTTP timeout | `WebSearchTools.cs:50` | `WebSearchHandler.s_httpTimeout` |
| `BingWebSearch` named HttpClient | `WebSearchTools.cs:63` | `WebSearchHandler.HttpClientName` (same constant value) |
| Mock results when no API key | `WebSearchTools.cs:134-138` | `WebSearchHandler.ExecuteChatAsync` (early-return mock path) |
| Mock results when concurrency limit reached | `WebSearchTools.cs:150-155` | `WebSearchHandler.ExecuteChatAsync` (semaphore timeout branch) |
| HTTP timeout / failure / JSON-parse failure → empty results + degradation note | `WebSearchTools.cs:170-188` | Same three `catch` branches in `WebSearchHandler.ExecuteChatAsync` |
| Scope guidance prepending | `WebSearchTools.ApplyScopeGuidance` | `WebSearchHandler.ApplyScopeGuidance` (now `internal static` for direct test coverage) |
| Mock result content (5 fixture entries) | `WebSearchTools.GenerateMockResults` | `WebSearchHandler.GenerateMockResults` (identical entries) |
| Position-based result ordering | `WebSearchTools.CallBingApiAsync` | `WebSearchHandler.CallBingApiAsync` (identical projection) |
| Bing response parsing (`BingSearchResponse`/`BingWebPages`/`BingWebPage`) | `WebSearchTools.cs:383-414` | `WebSearchHandler.cs` (same three private classes) |
| Output text formatting with `[N] [External Source]` markers | `WebSearchTools.FormatResults` | `WebSearchHandler.FormatResults` |
| Truncate-with-ellipsis snippet helper | `WebSearchTools.TruncateSnippet` | `WebSearchHandler.TruncateSnippet` |
| Citation `SourceType = "web"` | `WebSearchTools.RegisterCitations` line 285 | `WebSearchHandler.BuildToolResult` (in `ToolResultCitation` envelope) |

---

## Wave 7b citation envelope wiring

The legacy `WebSearchTools.RegisterCitations` called `CitationContext.AddCitation` directly,
including a per-position confidence score (1st = 0.95, linear decay to 10th = 0.50). Per
Wave 7b, the handler now returns:

```csharp
return ToolResult.Ok(...) with
{
    Metadata = new Dictionary<string, object?>
    {
        [ToolResultMetadataKeys.Citations] = citations  // ToolResultCitation[] (SourceType="web", Url, Snippet)
    }
};
```

The `ToolHandlerToAIFunctionAdapter` accumulates these envelopes into the per-turn
`CitationContext` via `AddCitation`. The position-based confidence scoring from legacy is
NOT carried over — it was never exposed to the LLM (the `ToolResultCitation` envelope and
`CitationMetadata` record both lack a `Confidence` field). If position-based confidence is
ever needed at the frontend, that's a follow-up enhancement to the citation envelope, not
a Wave 8 regression.

This matches the Wave 7c VerifyCitations + KnowledgeRetrieval shape exactly.

---

## ADR compliance

| Constraint | Result |
|---|---|
| **ADR-010** (DI minimalism) | PASS — auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly`. ZERO new top-level DI registrations. `IHttpClientFactory` + `IConfiguration` + `ILogger<>` are existing registrations. |
| **ADR-013** (PublicContracts boundary) | PASS — handler lives in `Services/Ai/Handlers/`, NOT in `Services/Ai/PublicContracts/`. CRUD-side callers do not consume it directly. |
| **ADR-014** (per-tenant isolation) | PASS — `TenantId` is validated on the chat path. Bing is tenant-agnostic, but the tenant ID is enforced for telemetry correlation. |
| **ADR-015** (data governance) | PASS — telemetry logs query LENGTH + result COUNT + duration + correlation IDs only. Sentinel-string tests (`Telemetry_RespectsAdr015_NoQueryTextInLogs`, `Telemetry_RespectsAdr015_NoResultBodyInLogs_BingPath`) verify the query string and result snippet content never appear in captured logs above Debug. The effective-query log (with prepended scope guidance) is at Debug level only — preserved from legacy. |
| **ADR-016** (rate limiting) | PASS — static `SemaphoreSlim(2, 2)` preserved verbatim from legacy. Reflection-based test (`ConcurrencyGate_IsPreserved_StaticAcrossInstances`) pins the contract. |
| **ADR-018** (feature flags) | PASS — Wave 8 uses `sprk_requiredcapability` (per-tool authorization), NOT a feature flag. The handler is always registered; the capability gate withholds it from the LLM schema when the playbook lacks `web_search`. |
| **ADR-029** (BFF publish size) | Pending — main session will measure after Seed-TypedHandlers wiring + legacy class removal. Handler is BCL-only (no new NuGet); expected delta ≤+0.1 MB per handler. |
| **NFR-04** (no Agent Framework) | PASS — no `Microsoft.Agents.*` references introduced. |

---

## Surprises / notes for main session

1. **`Validate` (playbook path) returns Failure** — by design, since the handler is
   chat-only. The legacy hardcoded registration only registered the tool in chat; there is
   no symmetric "chat-only" enum value on `IToolHandler` for the playbook-path
   `Validate(ToolExecutionContext, ...)`, so the handler returns a descriptive
   `ToolValidationResult.Failure(...)` and `ExecuteAsync` returns
   `ToolResult.Error(..., ValidationFailed, ...)` to make accidental playbook invocation
   loud and clear. No stop-and-surface — this matches the intent.

2. **No `BuildToolFrameworkServiceCollection` regression risk** — the new handler is
   constructor-injectable with three primitives (`IHttpClientFactory`, `IConfiguration`,
   `ILogger<WebSearchHandler>`) all already in DI. Auto-discovery scan picks it up. The
   `HandlerType_IsRegisteredInDi` test verifies this.

3. **Scope guidance is now per-invocation, not per-session** — the legacy `WebSearchTools`
   captured `scopeSearchGuidance` at construction time (per-chat-session); the handler
   reads it from `ChatInvocationContext.KnowledgeScope.ScopeSearchGuidance` on every
   invocation. For chat sessions bound to a stable playbook, this is functionally identical
   to the legacy capture. The new shape is correct for the DI-scoped handler instance
   lifetime (handlers are scoped, not per-session).

4. **Position-based confidence not carried over** — see Wave 7b citation envelope wiring
   section above. Not a regression for the LLM (the LLM never saw confidence scores); only
   a frontend visibility change if frontend started rendering position-based confidence
   from citations. The frontend currently does NOT do that, so no behavior change.

5. **Test for concurrency uses reflection** — verifying the SemaphoreSlim is configured at
   (2, 2) without orchestrating an exhaustion scenario. Exhaustion testing would require
   blocking 2 concurrent tasks then asserting the 3rd falls back to mock — possible but
   adds substantial test complexity for a contract that reflection pins more directly.
   Reflection-based test approach pins the ADR-016 contract without flakiness.

---

## Final outcomes

- **Files created**: 4 (handler + seed row + tests + this note)
- **Tests added**: 26 (4-point contract × 5 + validation × 5 + happy path × 2 + mock fallback × 1 + scope guidance × 4 + failures × 4 + cancellation × 1 + ADR-015 × 2 + concurrency × 1 + chat-context × 1)
- **No edits to**: `SprkChatAgentFactory.cs`, `scripts/Seed-TypedHandlers.ps1`, `current-task.md`, `TASK-INDEX.md`, the legacy `WebSearchTools.cs` (per strict ownership rules).
- **No new NuGet packages**.
- **No new top-level DI registrations**.
- **No new ADRs**.
- **Resolved literal**: `PlaybookCapabilities.WebSearch` = `"web_search"` (verified by direct file read).

Main session next steps (NOT done by this agent):
1. Add `infra/dataverse/sprk_analysistool-web-search-row.json` to `$RowFiles` in
   `scripts/Seed-TypedHandlers.ps1`.
2. Remove the hardcoded WebSearchTools block from `SprkChatAgentFactory.ResolveTools`
   lines 1023–1064 after all 4 Wave 8 agents land.
3. Measure publish-size delta + update R6 ADR-029 tracking.
