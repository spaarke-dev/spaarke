# BaseProxyPlugin ADR-002 Violation Assessment

> **Date**: 2026-03-15
> **Assessed By**: Code Quality & Assurance R2 (Task 031)
> **File**: `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs`
> **Status**: Marked `[Obsolete]` -- do not extend or reuse

---

## Summary

`BaseProxyPlugin` is a legacy/experimental abstract plugin class in the `Spaarke.CustomApiProxy` assembly. It was designed as a "proxy" pattern allowing Dataverse plugins to call external HTTP APIs with authentication, retry logic, and audit logging. While architecturally interesting as an experiment, it violates **ADR-002 (Dataverse Plugins Are Not an Execution Runtime)** in 6 distinct ways.

The plugin is **not currently used in production**. Its sole concrete subclass is `GetFilePreviewUrlPlugin`, which is also marked `[Obsolete]`.

Both classes have been marked with `[Obsolete]` attributes to prevent future use or extension.

---

## Violation Detail

| # | Violation | Location (Line) | ADR-002 Rule Violated | Severity |
|---|-----------|-----------------|----------------------|----------|
| 1 | `new HttpClient()` constructed per execution | Line 139 (`CreateAuthenticatedHttpClient`) | MUST NOT make HTTP calls; MUST NOT depend on external services | Critical |
| 2 | Blocking OAuth2 token acquisition via `SimpleAuthHelper.GetClientCredentialsToken` | Lines 181-186 (`GetClientCredentialsToken`) | MUST NOT make HTTP calls; MUST NOT perform long-running execution | Critical |
| 3 | `Thread.Sleep(delay)` in retry loop with exponential backoff | Line 337 (`ExecuteWithRetry`) | MUST NOT use retries, polling, or long-running execution; MUST complete < 50ms p95 | Critical |
| 4 | `new HttpClient` per execution causes socket exhaustion risk | Line 139 (`CreateAuthenticatedHttpClient`) | MUST NOT depend on external state or services | High |
| 5 | `LogRequest` + `LogResponse` perform synchronous Dataverse round-trips (Create, RetrieveMultiple, Update) inside the plugin execution pipeline | Lines 206-275 (`LogRequest`, `LogResponse`) | MUST keep < 50ms p95; MUST limit to in-transaction data inspection/mutation only | High |
| 6 | `GetServiceConfig` performs a Dataverse `RetrieveMultiple` query on every single execution to load configuration | Lines 96-132 (`GetServiceConfig`) | MUST keep < 50ms p95; MUST NOT perform long-running execution | Medium |

---

## Detailed Analysis

### Violation 1 & 4: HttpClient Construction Per Execution (Line 139)

```csharp
var httpClient = new HttpClient
{
    BaseAddress = new Uri(config.BaseUrl),
    Timeout = TimeSpan.FromSeconds(config.Timeout > 0 ? config.Timeout : 300)
};
```

**Problem**: Creating a new `HttpClient` instance per plugin execution causes socket exhaustion under load. The `HttpClient` class is designed to be long-lived and reused. In a plugin context, there is no way to use `IHttpClientFactory` (the correct .NET pattern), making this fundamentally incompatible with ADR-002.

**ADR-002 rules violated**:
- MUST NOT make HTTP calls
- MUST NOT depend on external services

### Violation 2: Blocking OAuth2 Token Acquisition (Lines 181-186)

```csharp
var token = SimpleAuthHelper.GetClientCredentialsToken(
    config.TenantId, config.ClientId, config.ClientSecret, config.Scope
);
```

**Problem**: Acquires an OAuth2 token via client credentials flow during plugin execution. This is a blocking HTTP call to Azure AD's token endpoint. Token acquisition can take 100-500ms, far exceeding the 50ms p95 budget.

**ADR-002 rules violated**:
- MUST NOT make HTTP calls
- MUST NOT perform long-running execution

### Violation 3: Thread.Sleep in Retry Loop (Line 337)

```csharp
var delay = retryDelay * (i + 1);
System.Threading.Thread.Sleep(delay);
```

**Problem**: Blocks the Dataverse execution thread with `Thread.Sleep` during retry backoff. With default config (3 retries, 1000ms delay), worst case blocks for 1000 + 2000 + 3000 = 6000ms. This is 120x the 50ms p95 budget.

**ADR-002 rules violated**:
- MUST NOT use retries, polling, or long-running execution
- MUST complete < 50ms p95

### Violation 5: Synchronous Audit Logging via Dataverse (Lines 206-275)

**LogRequest** (lines 206-232):
```csharp
OrganizationService.Create(auditLog);  // Synchronous Dataverse Create
```

**LogResponse** (lines 234-275):
```csharp
var results = OrganizationService.RetrieveMultiple(query);  // Synchronous query
OrganizationService.Update(auditLog);  // Synchronous update
```

**Problem**: Three synchronous Dataverse operations (Create + RetrieveMultiple + Update) execute within the plugin pipeline. Each operation adds 10-50ms latency. Combined with the HTTP proxy call, total execution time far exceeds acceptable bounds.

**ADR-002 rules violated**:
- MUST keep < 50ms p95
- MUST limit to in-transaction data inspection/mutation only

### Violation 6: Configuration Query Per Execution (Lines 96-132)

```csharp
var query = new QueryExpression("sprk_externalserviceconfig") { ColumnSet = new ColumnSet(true) };
var results = OrganizationService.RetrieveMultiple(query);
```

**Problem**: Queries the `sprk_externalserviceconfig` table on every plugin execution to load service configuration. Uses `ColumnSet(true)` (all columns) which is a Dataverse anti-pattern. Configuration is static and should be cached or externalized.

**ADR-002 rules violated**:
- MUST keep < 50ms p95
- MUST NOT perform long-running execution

---

## Recommended Remediation

**Do NOT attempt to fix BaseProxyPlugin in place.** The violations are architectural, not just code quality issues. The plugin sandbox environment fundamentally cannot support:
- `IHttpClientFactory` (no DI container)
- Proper async/await patterns (synchronous-only execution)
- Token caching (no persistent state across executions)
- Background processing (no `BackgroundService` equivalent)

### Correct Architecture: BFF API + Service Bus Worker

If the proxy functionality is needed in production, implement it using the established Spaarke patterns:

1. **BFF API Endpoint** (`src/server/api/Sprk.Bff.Api/`)
   - Receives the request from the Dataverse form (via JavaScript/PCF)
   - Uses `IHttpClientFactory` with named/typed clients
   - Handles authentication via endpoint filters (ADR-008)
   - Returns the result directly for synchronous operations

2. **Service Bus Worker** (for async operations)
   - Plugin enqueues a message to Azure Service Bus
   - `BackgroundService` processes the message
   - Updates Dataverse via the API when complete

3. **Client-Side Integration**
   - PCF control or Code Page calls the BFF API directly
   - No plugin involvement needed for HTTP proxy operations

### Why This Is Better

| Concern | Plugin Approach (Current) | BFF API Approach (Recommended) |
|---------|--------------------------|-------------------------------|
| HttpClient lifecycle | New instance per call (socket exhaustion) | IHttpClientFactory (pooled, reused) |
| Authentication | Blocking token acquisition per call | Cached tokens via MSAL.NET |
| Retry logic | Thread.Sleep blocking (6s worst case) | Polly policies (async, configurable) |
| Audit logging | Synchronous Dataverse writes | Application Insights + async logging |
| Configuration | Query per execution | Cached in memory, refreshed on change |
| Execution time | 2-10 seconds typical | < 200ms typical |
| Testability | Requires plugin sandbox | Standard .NET DI + unit tests |

---

## Action Taken

| Action | Date | Details |
|--------|------|---------|
| Marked `BaseProxyPlugin` as `[Obsolete]` | 2026-03-15 | Attribute added with ADR-002 reference and this assessment path |
| Marked `GetFilePreviewUrlPlugin` as `[Obsolete]` | 2026-03-15 | Attribute added referencing BaseProxyPlugin violation |
| Added plugin assembly to arch test scope | 2026-03-14 | Task 003: `Spaarke.ArchTests/ADR002_PluginTests.cs` covers this assembly |
| Assessment documented | 2026-03-15 | This file |

---

## References

- [ADR-002: Dataverse Plugins Are Not an Execution Runtime](../../../.claude/adr/ADR-002-thin-plugins.md)
- [ADR-001: Minimal API + BackgroundService](../../../.claude/adr/ADR-001-minimal-api.md)
- [Task 003: Fix No-Op Arch Tests](../tasks/003-fix-noop-arch-tests.poml)
