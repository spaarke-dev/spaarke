# BFF Dataverse HTTP Unification Assessment

**Date**: 2026-06-23
**Author**: researcher subagent
**Context**: PR #417 fixed 3 playbook executors with orphan-named-client `"DataverseApi"` bug by refactoring to `Spaarke.Dataverse.IGenericEntityService`. Five additional BFF services suffer related bugs (each with a different orphan name) plus adjacent issues. This document recommends the design for a unification project.
**Scope**: Sprk.Bff.Api Dataverse outbound HTTP access patterns only. Graph access (`GraphApiClient`, `GraphUploadSession`) is well-governed and out of scope.

---

## 1. Pattern fitness assessment per service

For each broken service: recommended tier (A=`IGenericEntityService`, B=new `IDataverseHttpClient`, C=bespoke named client, D=delete) and the HTTP semantic driving the choice.

### 1.1 `EmailTemplateService` (`Services/Ai/Delivery/EmailTemplateService.cs:22`) — **Tier D (delete) → fallback Tier A**

- **Orphan name**: `"Dataverse"` (line 136, 161).
- **HTTP shape**: GET `templates(id)?$select=...` and `templates?$filter=title eq '...'&$top=1` — pure entity CRUD on the OOB Power Apps `template` entity.
- **Antipattern**: mutates `client.BaseAddress` and `client.DefaultRequestHeaders.Authorization` on a shared `HttpClient` instance per call — concurrency hazard if Singleton.
- **Caller graph**: `IEmailTemplateService` is registered (`AnalysisServicesModule.cs:852`) but grep shows **zero injection sites** outside its own file. Dead code.
- **Recommendation**: **Tier D** — delete the service, the interface, and the DI line. If a future delivery channel needs it, re-introduce on Tier A (`IGenericEntityService.RetrieveByAlternateKeyAsync` for name lookup + `RetrieveAsync` for id lookup; the `template` entity is plain CRUD with no special semantics).

### 1.2 `EmailAssociationService` (`Services/Email/EmailAssociationService.cs:14`) — **Tier A** (refactor to `IGenericEntityService` + composable helpers) with one **Tier B caveat**

- **Orphan name**: `"DataverseAssociation"` (line 730).
- **HTTP shape**: OData `$filter` queries with `contains()`, `startswith()`, `eq` against `emails`, `sprk_matters`, `accounts`, `contacts`. Uses `Prefer: odata.include-annotations=*` to get `@OData.Community.Display.V1.FormattedValue` and `@Microsoft.Dynamics.CRM.lookuplogicalname` annotations on lookups (line 736). Reads `_regardingobjectid_value@…` annotations from JSON.
- **Antipattern**: same client-mutation bug. Also manually builds `ConfidentialClientApplication` with `ClientSecret` (line 757–765) — direct ADR-028 violation; should use the DI-injected `TokenCredential` (per `RecordSyncJob` pattern).
- **Driving semantic**: It IS queryable through `IGenericEntityService.RetrieveMultipleAsync(QueryExpression)` — the SDK preserves formatted-value annotations through `Entity.FormattedValues[]` and `LookupLogicalName` on `EntityReference`. The OData `Prefer` header is redundant when using the SDK.
- **Recommendation**: **Tier A**. Convert each `Find*Async`/`Evaluate*Async` to a `QueryExpression`, use `Entity.FormattedValues["sprk_regardingobjectid"]`, and drop the bespoke MSAL token acquisition entirely (DI-inject `TokenCredential`). One subtlety: the `conversationindex startswith` filter (line 317) → use `ConditionOperator.BeginsWith` (supported by QueryExpression).

### 1.3 `SessionRestoreService` (`Services/Ai/Sessions/SessionRestoreService.cs:27`) — **Tier B (new `IDataverseHttpClient`)**

- **Orphan name**: `"DataverseETagCheck"` (line 280).
- **HTTP shape**: GET `entities(id)?$select=pk` to inspect the response `ETag` HTTP header (line 311–314). Falls back to body `@odata.etag` only when header is absent.
- **Driving semantic**: **ETag header inspection** — `IGenericEntityService` returns an `Entity` and the SDK does not expose response headers. Using `ServiceClient` here would require either parsing the entity's `RowVersion` attribute (different semantics, may not match the `W/"…"` literal saved at session-write) or wrapping every retrieve in a `RetrieveResponse.Results["@odata.etag"]` read — uglier than the Web API.
- **Critical-path perf**: <500ms p95 target with `Task.WhenAll` parallel fan-out. The new abstraction MUST preserve concurrent reuse of a single token (currently does — token acquired once at line 246, reused across N parallel `CheckSingleEntityAsync` calls).
- **Recommendation**: **Tier B**. This is the canonical use case for a new `IDataverseHttpClient` abstraction with `Task<HttpResponseMessage> SendAsync(HttpRequestMessage, CancellationToken)` exposed for ETag/header-aware paths.

### 1.4 `BulkRagIndexingJobHandler` (`Services/Ai/Jobs/BulkRagIndexingJobHandler.cs:31`) — **Tier B** (currently single-page; later $batch)

- **Orphan name**: `"DataverseBatch"` (line 76).
- **HTTP shape**: today, a single `$select`/`$expand`/`$filter`/`$top` GET on `sprk_documents` (`QueryDocumentsAsync` line 373). Field name is "Batch" but it's NOT yet using OData `$batch` — that's aspirational naming.
- **Antipattern**: stores `HttpClient` in instance field at construction (line 76) and mutates `DefaultRequestHeaders.Authorization` in `EnsureAuthenticatedAsync` (line 496). Token refresh happens on instance state — works because `IJobHandler` is per-job-scoped, but still fragile.
- **Driving semantic**: COULD work with `IGenericEntityService.RetrieveMultipleAsync(QueryExpression)` since `$expand` is `LinkEntity` and the filter is straightforward. BUT the spec intent (per class name) is `$batch` — and SDK `ExecuteMultipleRequest` is semantically different from OData `$batch` (different size limits, different error handling).
- **Recommendation**: **Tier B**. Today the handler can be Tier A, but if the future plan is real `$batch` (preferred for bulk indexing of 1000+ docs to avoid the N×token-validation round-trips), `IDataverseHttpClient` is the right abstraction. Spec the new abstraction with `Task<HttpResponseMessage> SendBatchAsync(IEnumerable<HttpRequestMessage>)` from day one.

### 1.5 `RecordSyncJob` (`Services/Jobs/RecordSyncJob.cs:140`) — **Tier B**

- **Orphan name**: `"RecordSyncDataverse"` (line 424).
- **HTTP shape**: server-paged OData query with `Prefer: odata.maxpagesize=200` and `@odata.nextLink` follow (line 445–487). Uses `HttpRequestMessage` per request (CORRECT pattern — no `DefaultRequestHeaders` mutation).
- **Driving semantic**: **OData server-driven paging via `@odata.nextLink`**. SDK `RetrieveMultipleAsync` returns `EntityCollection.MoreRecords` + `PagingCookie` — different mechanism. Both work; the Web API is closer to the existing PowerShell `Sync-RecordsToIndex.ps1` that this code mirrors. Also uses `OData.Community.Display.V1.FormattedValue` annotations.
- **Auth posture**: ALREADY correct per ADR-028 — uses DI-injected `TokenCredential` (line 196), cached token with semaphore (line 233). This is the gold-standard pattern other services should converge to.
- **Recommendation**: **Tier B**. Migrate to `IDataverseHttpClient` and DELETE the orphan name; otherwise keep all the auth logic as-is (it's the reference impl for the new abstraction).

---

## 2. Shared abstraction design — `IDataverseHttpClient`

Three services (Tiers B above) need response-header/paging/batch semantics that `IGenericEntityService` does not expose. Recommended design:

### Interface (lives in `Spaarke.Dataverse` shared lib)

```csharp
namespace Spaarke.Dataverse;

/// <summary>
/// Low-level Dataverse Web API access for callers needing HTTP-level semantics
/// (response headers, ETag, OData $batch, server-driven paging via @odata.nextLink)
/// that the SDK-based <see cref="IGenericEntityService"/> cannot ergonomically expose.
///
/// For pure entity CRUD, prefer <see cref="IGenericEntityService"/>.
/// </summary>
public interface IDataverseHttpClient
{
    /// <summary>Base URL: {Dataverse:ServiceUrl}/api/data/v9.2/</summary>
    Uri BaseAddress { get; }

    /// <summary>
    /// Sends a pre-built request. Authorization header is auto-injected;
    /// caller must NOT set Authorization.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct);

    /// <summary>
    /// Convenience: GET an OData query string (relative to BaseAddress),
    /// deserialize the @value array to T, follow @odata.nextLink to completion.
    /// </summary>
    IAsyncEnumerable<T> QueryPagedAsync<T>(string relativeQuery, CancellationToken ct);

    /// <summary>
    /// Convenience: GET with ETag-aware semantics. Returns (entity, etag) or null if NotFound.
    /// </summary>
    Task<(JsonElement Body, string? ETag)?> GetWithETagAsync(
        string relativeUrl, CancellationToken ct);

    /// <summary>
    /// OData $batch wrapper. Returns per-operation responses in order.
    /// </summary>
    Task<IReadOnlyList<HttpResponseMessage>> SendBatchAsync(
        IEnumerable<HttpRequestMessage> operations, CancellationToken ct);
}
```

### Location

`Spaarke.Dataverse` shared lib — peer with `IGenericEntityService`, `DataverseServiceClientImpl`, `DataverseWebApiClient`. **Do NOT put it in `Sprk.Bff.Api/Infrastructure/`** — the shared lib already has the right dependencies and patterns, and at least one external caller path (the existing `DataverseWebApiClient` already lives there).

### Auth handler

A `DataverseAuthDelegatingHandler : DelegatingHandler` (in `Spaarke.Dataverse`) that:
1. Injects `TokenCredential` (per ADR-028 — MI canonical; `ClientSecretCredential` fallback only via existing `DataverseWebApiClient.cs:42-54` cascade — `API_APP_ID` + `API_CLIENT_SECRET` + `TENANT_ID` present → ClientSecret; else `DefaultAzureCredential`).
2. Caches `AccessToken` until 5 min before expiry (mirror `RecordSyncJob:233-251` and `DataverseWebApiClient:69-103` — semaphore + double-check).
3. Sets `Authorization: Bearer …` on every outbound request (NOT on `DefaultRequestHeaders` — per-request only).

### Resilience handler

Mirror `GraphHttpMessageHandler`:
- Retry on transient (HTTP 408/429/5xx) with exponential backoff capped at 3 attempts.
- Honor `Retry-After` (Dataverse returns it on 429 / 503).
- Circuit-breaker open after 5 consecutive failures, 30s break.
- Timeout per attempt: 30s (longer than Graph since Dataverse $batch can be slow).

### DI registration shape

Add a new `DataverseHttpModule` (peer of `GraphModule`):

```csharp
public static class DataverseHttpModule
{
    public static IServiceCollection AddDataverseHttpModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<DataverseAuthDelegatingHandler>();
        services.AddTransient<DataverseResilienceHandler>();

        services.AddHttpClient<IDataverseHttpClient, DataverseHttpClient>((sp, client) =>
        {
            var url = configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl required");
            client.BaseAddress = new Uri($"{url.TrimEnd('/')}/api/data/v9.2/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        })
        .AddHttpMessageHandler<DataverseAuthDelegatingHandler>()
        .AddHttpMessageHandler<DataverseResilienceHandler>();

        return services;
    }
}
```

### Should `IGenericEntityService` use `IDataverseHttpClient` internally?

**No.** Keep them independent. The runtime impl `DataverseServiceClientImpl` wraps `ServiceClient` (SDK), which has its own connection pool, batching, and metadata cache. Routing SDK calls through the Web API would lose those optimizations and break the `OrganizationService` escape hatch (`DataverseServiceClientImpl.cs:28`) used by `SpendSnapshotService` and others. The two abstractions serve different needs: SDK for ergonomic typed CRUD; Web API for header-aware/paged/batch HTTP.

---

## 3. Deeper search — additional findings

| # | Severity | File:line | Issue | Tier |
|---|----------|-----------|-------|------|
| 3.1 | **BROKEN** | `Services/Ai/Chat/Tools/WebSearchTools.cs:63,203` + `Services/Ai/Handlers/WebSearchHandler.cs:108,501` | `"BingWebSearch"` named-client referenced via `IHttpClientFactory.CreateClient(HttpClientName)` but **NEVER registered** with `AddHttpClient("BingWebSearch")` in any DI module. Mirrors the original PR #417 bug exactly. The handler is wired into `SprkChatAgentFactory:1226` (live for chat flows). Likely currently masked by graceful mock fallback when API key is null (line 108 of WebSearchTools), but if a tenant configures `Bing:ApiKey`, the call will use an unconfigured `HttpClient` with no `BaseAddress`, no resilience, no proper handler chain — request will issue from the BFF but with no retry/circuit-breaker. | **C** — register `AddHttpClient("BingWebSearch")` with timeout + resilience handler; out of scope for Dataverse unification BUT same bug class so include in the project. |
| 3.2 | **DEAD** | `Infrastructure/DI/EmailServicesModule.cs:50` | `AddHttpClient("DataversePolling")` registered with no callers. Adds 1 DI line (registry pollution per ADR-010 ≤15 principle). | **D** — delete the registration. |
| 3.3 | **RISKY** | `Services/Registration/RegistrationDataverseService.cs:375,431,467,522,552` | Five `using var client = new HttpClient()` sites in cross-environment paths. Bypasses `IHttpClientFactory` lifecycle — risks socket exhaustion if invoked at scale. Currently demo/registration paths only (low traffic), so works in practice. | **B** — refactor to use `IDataverseHttpClient` with a `WithBaseUri(targetDataverseUrl)` factory method, or simpler: keep `RegistrationDataverseService` as a typed `HttpClient` (it already injects one for the default env) and add a SECOND typed client class `CrossEnvironmentRegistrationDataverseService` for the targeted-URL path. |
| 3.4 | **RISKY** | `Services/Registration/DataverseEnvironmentService.cs:41-44` | Same pattern: `_httpClient = new HttpClient { BaseAddress = … }` in constructor, not from `IHttpClientFactory`. Token logic is fine (uses injected `TokenCredential` per ADR-028). | **B** — convert to use `IDataverseHttpClient`. |
| 3.5 | **MINOR** | `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs:42-54` | Manual `ClientSecretCredential` fallback inside the lib. Pattern is correct per ADR-028 ("ClientSecret is fallback for local dev"), but the fallback cascade duplicates the SAME logic in `DataverseWebApiService.cs:56`, `DataverseServiceClientImpl.cs:43`, `EmailAssociationService.cs:757`. Five different credential-construction paths is a smell — should consolidate into one `CredentialResolver` in `Spaarke.Dataverse`. | Address as part of the new auth handler (§2). |
| 3.6 | **TEST DEBT (HIGH)** | `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/MigratedPlaybookFixture.cs:99`; `tests/unit/Sprk.Bff.Api.Tests/Services/Email/EmailAssociationServiceTests.cs:281`; `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs:40`; `tests/unit/Sprk.Bff.Api.Tests/Integration/PlaybookExecutionTests.cs:459` | Test fixtures stub `httpFactoryMock.Setup(f => f.CreateClient("DataverseApi"/"DataverseAssociation"))` — these tests PASSED while the prod code was broken because the test DI registered what prod did not. Exactly the "fixture-lies-about-DI" anti-pattern called out in `.claude/constraints/bff-extensions.md § F.2`. The PR #417 fixes deleted some; `EmailAssociationServiceTests` still has it. | Update during the unification project: tests MUST verify DI registration matches prod (one approach: build the real DI container in a fixture, resolve `IHttpClientFactory`, assert `CreateClient(name).BaseAddress != null`). |
| 3.7 | **MINOR** | `src/server/api/Sprk.Bff.Api/Services/Communication/Dataverse/` | Brief mentioned `DataverseWebApiService` in this path — it does NOT exist there. The class lives in `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (registered for `IEventDataverseService` per `GraphModule.cs:73`). Just a doc/path correction. | N/A |

**Did NOT find**:
- Other unregistered named clients beyond the 6 in §1.1–§1.5 + §3.1.
- Other `GraphServiceClient` direct injections beyond `GraphUserService` parameters (lines 356, 377), which are FACTORY METHOD parameters not constructor injection — likely intentional. Verify in code review.
- Audit-middleware bypass: no middleware exists at `src/server/api/Sprk.Bff.Api/Middleware/` (the path in `CLAUDE.md` § "Auth-related infrastructure files" pointing to `Middleware/AuditEnrichmentMiddleware.cs` doesn't resolve — separate doc-drift issue, out of scope here).

---

## 4. Migration sequencing

Recommended order (low-risk first, critical-path last; each PR independently mergeable):

1. **Pre-work (no behavior change)**: Add `IDataverseHttpClient` + `DataverseHttpModule` + `DataverseAuthDelegatingHandler` to `Spaarke.Dataverse`. Wire `AddDataverseHttpModule` in `Program.cs`. Add unit tests that prove `CreateClient` returns a properly-configured client (defeats the §3.6 fixture-lies pattern). NO migrations yet.
2. **`EmailTemplateService` — DELETE** (§1.1). Zero risk, removes 1 orphan, removes 1 DI line, removes 1 dead-code file. PR sized small.
3. **`DataversePolling` orphan — DELETE** (§3.2). Same scope as #2.
4. **`EmailAssociationService` — refactor to Tier A** (§1.2). Update the test (§3.6) to use real `IGenericEntityService`. Tests cover the regression surface.
5. **`BulkRagIndexingJobHandler` — refactor to Tier B** (§1.4). Background-job → tolerant of latency change. Verify `EnsureAuthenticatedAsync` (line 496) and the field-storage antipattern (line 76) both disappear.
6. **`RecordSyncJob` — refactor to Tier B** (§1.5). Already has correct auth pattern, so just swap the HTTP call sites; small diff.
7. **`SessionRestoreService` — refactor to Tier B LAST** (§1.3). Critical <500ms p95 path. Benchmark before merge.
8. **`RegistrationDataverseService` + `DataverseEnvironmentService`** (§3.3, §3.4). Move to Tier B; lowest priority because no caller traffic to speak of.
9. **`BingWebSearch` orphan — fix** (§3.1). Out of Dataverse scope strictly but same bug class, so include in the project plan as Tier C (add `AddHttpClient("BingWebSearch")` with timeout + resilience).

**Feature flag**: NOT needed — `IDataverseHttpClient` is purely additive. Each call-site refactor either works (tests pass) or doesn't (tests fail at registration time). No traffic-splitting / fallback complexity. This avoids the ADR-032 Null-Object kill-switch pattern.

**Test-update obligation** (per `.claude/constraints/bff-extensions.md § F`): every Tier A/B refactor must update the corresponding test in `tests/unit/Sprk.Bff.Api.Tests/Services/.../...Tests.cs`, including replacing `Mock<IHttpClientFactory>` setups with either real DI container or `Mock<IDataverseHttpClient>`. Add at least one DI-registration smoke test (fixture: bootstrap real Program.cs DI, call `sp.GetRequiredService<IDataverseHttpClient>()`).

---

## 5. Open design questions for the follow-up project spec

1. **Typed-class vs named-client pattern for `IDataverseHttpClient`?** Recommendation: typed-class (`services.AddHttpClient<IDataverseHttpClient, DataverseHttpClient>()`) — gives compile-time injection, eliminates the "string-name" orphan bug class by construction. The original incident was a STRING typo; typed-class makes the bug impossible. Decision needed: ADR amendment to ADR-010 "MUST use single typed `HttpClient` per upstream service" — clarify that strongly-typed > named for any non-trivial client.
2. **Should `IGenericEntityService` and `IDataverseHttpClient` share auth state?** Today `DataverseServiceClientImpl` (SDK) and the new `DataverseAuthDelegatingHandler` (Web API) will each cache their own `AccessToken`. Two refreshes per token-lifetime is wasteful. Consolidate via a shared `IDataverseTokenProvider` singleton? Spec needs to weigh the perf benefit vs. interface bloat.
3. **`ClientSecretCredential` fallback removal target date?** ADR-028 marks it as "local-dev only", but FIVE production code paths still construct it manually (§3.5). Should the unification project DELETE the fallback (forcing all envs to MI via `DefaultAzureCredential`'s dev-credential chain) or just centralize it? Recommendation: centralize now, schedule a separate project to delete after monitoring confirms MI works everywhere.
4. **`DataverseWebApiClient` vs `DataverseWebApiService` vs new `IDataverseHttpClient` — keep all three?** `DataverseWebApiClient` (line 17, `Spaarke.Dataverse`) has its own `HttpClient`-via-`new` and its own auth — it's an older twin of what we're building. `DataverseWebApiService` is the `IDataverseService` Web API implementation used as fallback (per `IGenericEntityService.RetrieveMultipleAsync(FetchExpression)` doc). Recommendation: deprecate `DataverseWebApiClient` (mark `[Obsolete]`, list callers, plan removal); keep `DataverseWebApiService` since it implements the SDK contract.
5. **Scope: include `BingWebSearch` fix or split it out?** §3.1 is the same bug class but a different external service. Recommendation: include in the same project (one PR, one set of DI-smoke-tests) — splitting forces two rounds of `.claude/constraints/bff-extensions.md` review for the same root-cause fix.

---

## Sources

Code paths (all `c:\code_files\spaarke-wt-spaarke-daily-update-service-r2`):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Delivery/EmailTemplateService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Email/EmailAssociationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionRestoreService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/RecordSyncJob.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WebSearchTools.cs` (NEW finding)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/WebSearchHandler.cs` (NEW finding)
- `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentService.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/GraphModule.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EmailServicesModule.cs`
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs`
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`
- `src/server/shared/Spaarke.Dataverse/IGenericEntityService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Email/EmailAssociationServiceTests.cs:281` (masking-fixture)
- `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/MigratedPlaybookFixture.cs:99` (masking-fixture)
- `.claude/adr/ADR-007-spefilestore.md`, `ADR-010-di-minimalism.md`, `.claude/constraints/bff-extensions.md`

ADR cross-references: ADR-007 (SpeFileStore facade analog for the proposed `IDataverseHttpClient`), ADR-010 (DI minimalism — typed-class > named-client per §5.1), ADR-028 (MI canonical / ClientSecret fallback — §3.5), ADR-032 (Null-Object kill-switch NOT needed for this migration — §4 rationale).
