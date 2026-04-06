# Integration Contracts

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Applies To**: All cross-subsystem boundaries in SDAP

---

## Rules

1. **MUST**: All BFF API error responses use RFC 7807 ProblemDetails format via `ProblemDetailsHelper`.
2. **MUST**: All BFF API endpoints require authentication except `/healthz` and `/ping`.
3. **MUST**: Resource authorization uses endpoint filters, not global middleware (ADR-008).
4. **MUST**: Graph API access goes through `SpeFileStore` facade; never inject `GraphServiceClient` directly (ADR-007).
5. **MUST NOT**: Make HTTP or Graph calls from Dataverse plugins (ADR-002).
6. **MUST**: All Service Bus jobs use the `JobContract` schema with `jobType` routing.
7. **SHOULD**: Frontend callers use `authenticatedFetch` wrappers that attach Bearer tokens automatically.
8. **MUST**: AI service endpoints return `503` with `ai_unavailable` error code when circuit breaker is open.
9. **MUST**: All retry policies honor `Retry-After` headers from upstream services (Graph, OpenAI).

---

## Seam 1: Frontend to BFF API (PCF / Code Pages / Power Pages SPA)

### Data Format

- **Protocol**: HTTPS REST (JSON request/response bodies, `application/json`)
- **SSE Streaming**: AI chat and analysis endpoints use `text/event-stream` with `{type:'token',content:'...'} ... {type:'done'}` event shape
- **Serialization**: camelCase JSON (`JsonNamingPolicy.CamelCase`)

### Authentication

- **Internal callers** (PCF controls, Code Pages): Azure AD JWT with scope `api://{bff-client-id}/user_impersonation`
  - PCF controls resolve BFF URL from Dataverse environment variable `sprk_BffApiBaseUrl`
  - Token acquired via MSAL `acquireTokenSilent` with popup fallback
- **External callers** (Power Pages SPA): Azure AD B2B guest JWT validated by ASP.NET Core middleware, then `ExternalCallerAuthorizationFilter` resolves Contact + participation context
- **Header**: `Authorization: Bearer {token}`

### Error Response Shape

All errors return RFC 7807 ProblemDetails. Domain-specific variants add extensions:

| Domain | Extra Fields | Example Status |
|--------|-------------|----------------|
| Standard | `traceId` | 400, 404, 500 |
| Graph | `graphErrorCode`, `graphRequestId` | 401, 403, 429 |
| AI | `errorCode` (`ai_unavailable`, `ai_rate_limited`), `retryAfterSeconds`, `correlationId` | 429, 503 |
| Office | `errorCode` (`OFFICE_XXX`), `type` URI, `correlationId` | 400, 403, 404, 502 |

```json
{ "title": "Bad Request", "status": 400, "detail": "DisplayName is required", "traceId": "00-abc123..." }
```

```json
{ "title": "forbidden", "status": 403, "detail": "missing graph app role...", "graphErrorCode": "Authorization_RequestDenied" }
```

### Retry / Timeout Policy

- **Client-side**: No built-in retry in `authenticatedFetch`; callers handle 401 by clearing MSAL cache and reacquiring token
- **Rate limiting**: BFF applies per-endpoint rate limiting via named policies (`graph-read`, `graph-write`, `ai-stream`, `ai-batch`)
- **429 response**: Clients SHOULD back off; AI endpoints include `retryAfterSeconds` in ProblemDetails

### Key Endpoints (by domain)

| Group | Route Prefix | Auth Filter |
|-------|-------------|-------------|
| OBO file operations | `/api/obo/` | `RequireRateLimiting` (graph-read/write) |
| Container management | `/api/containers` | `RequireAuthorization("canmanagecontainers")` |
| AI Chat | `/api/ai/chat/` | `AddAiAuthorizationFilter()` |
| AI Analysis | `/api/ai/analysis/` | `AddAnalysisExecuteAuthorizationFilter()` |
| AI RAG | `/api/ai/rag/` | `AddTenantAuthorizationFilter()` |
| Email conversion | `/api/v1/emails/` | `RequireAuthorization()` |
| External access | `/api/v1/external/` | `ExternalCallerAuthorizationFilter` |

### Related ADRs

- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) -- Minimal API, no controllers
- [ADR-008](../../docs/adr/ADR-008-authorization-endpoint-filters.md) -- Endpoint filters for auth

---

## Seam 2: BFF API to Microsoft Graph (via SpeFileStore Facade)

### Data Format

- **Protocol**: HTTPS REST via Microsoft Graph SDK v5 (Kiota-based)
- **Base URL**: `https://graph.microsoft.com/v1.0/` (app-only uses beta for some SPE operations)
- **Request serialization**: Handled by Graph SDK

### Authentication

Two authentication strategies via `GraphClientFactory`:

| Method | Factory Call | Use Case | Token Source |
|--------|-------------|----------|-------------|
| **OBO (delegated)** | `ForUserAsync(ctx)` | User-initiated file operations | User JWT exchanged via MSAL `AcquireTokenOnBehalfOf` with scope `https://graph.microsoft.com/.default` |
| **App-only** | `ForApp()` | Background jobs, admin operations | `ClientSecretCredential` with `https://graph.microsoft.com/.default` |

- OBO tokens cached in Redis for 55 minutes (5-minute buffer before 60-minute expiration) per ADR-009
- App-only client is a cached singleton (`Lazy<GraphServiceClient>`)

### Error Handling

Graph SDK throws `ODataError` which is converted to ProblemDetails via `ProblemDetailsHelper.FromGraphException()`:

| Graph Status | ProblemDetails Status | Detail |
|-------------|----------------------|--------|
| 401 | 401 | "unauthorized" |
| 403 (Authorization_RequestDenied) | 403 | "missing graph app role (filestoragecontainer.selected) for the api identity." |
| 403 (other) | 403 | "api identity lacks required container-type permission for this operation." |
| 429 | 429 | Mapped from `activityLimitReached` |
| 5xx | 5xx | Graph error message passed through |

### Retry / Timeout Policy

Centralized via `GraphHttpMessageHandler` (Polly) registered on named `HttpClient("GraphApiClient")`:

| Policy | Default | Configurable Via |
|--------|---------|-----------------|
| **Retry** | 3 attempts, exponential backoff (2^n * 2s) | `GraphResilienceOptions.RetryCount`, `RetryBackoffSeconds` |
| **Retry-After** | Honored from Graph 429 responses | `GraphResilienceOptions.HonorRetryAfterHeader` (default: true) |
| **Circuit Breaker** | Opens after 5 consecutive failures, 30s break | `CircuitBreakerFailureThreshold`, `CircuitBreakerBreakDurationSeconds` |
| **Timeout** | 30s per request (pessimistic) | `GraphResilienceOptions.TimeoutSeconds` |

Policy composition order (inner to outer): Timeout -> Retry -> Circuit Breaker.

### Related ADRs

- [ADR-007](../../docs/adr/ADR-007-spe-storage-seam-minimalism.md) -- SpeFileStore facade, no Graph SDK type leakage
- [ADR-009](../../docs/adr/ADR-009-caching-redis-first.md) -- Redis-first caching for OBO tokens

---

## Seam 3: BFF API to Azure Service Bus (Job Processing)

### Data Format

- **Protocol**: Azure Service Bus (AMQP)
- **Message body**: JSON-serialized `JobContract` (camelCase)
- **Content-Type**: `application/json`
- **Subject**: `job.JobType` string
- **Message ID**: `job.IdempotencyKey` (hashed to 64-char hex if > 128 chars) or `job.JobId`

### JobContract Schema

```json
{
  "jobId": "guid",
  "jobType": "ProcessEmailToDocument",
  "subjectId": "resource-or-user-id",
  "correlationId": "trace-correlation-id",
  "idempotencyKey": "unique-operation-key",
  "attempt": 1,
  "maxAttempts": 3,
  "payload": { /* job-specific JSON */ },
  "createdAt": "2026-04-05T00:00:00Z"
}
```

### Authentication

- **Connection**: Service Bus connection string from configuration (`ServiceBusOptions`)
- **No per-message auth**: Connection-level authentication via Azure SDK

### Error Handling (Processor Side)

`ServiceBusJobProcessor` implements three outcome paths:

| Outcome | Action | Condition |
|---------|--------|-----------|
| **Completed** | `CompleteMessageAsync` | Handler returns `JobStatus.Completed` |
| **Retry** | `AbandonMessageAsync` (redelivered) | Handler fails but `attempt < maxAttempts` and `deliveryCount < 5` |
| **Dead-letter** | `DeadLetterMessageAsync` with reason code | `JobStatus.Poisoned`, `maxAttempts` reached, or `deliveryCount >= 5` |

Dead-letter reason codes: `InvalidFormat`, `NoHandler`, `HandlerResolutionFailed`, `MaxRetriesExceeded`, `Poisoned`, `ProcessingError`.

### Retry / Timeout Policy

| Setting | Value |
|---------|-------|
| **Receive mode** | PeekLock |
| **Auto-complete** | false (explicit complete/abandon/dead-letter) |
| **Max auto-lock renewal** | 10 minutes |
| **Dead-letter threshold** | `deliveryCount >= 3` for unhandled exceptions; `deliveryCount >= 5` or `attempt >= maxAttempts` for handler failures |
| **Duplicate detection** | Via `IdempotencyKey` as `MessageId` |

### Queues

| Queue | Purpose |
|-------|---------|
| Primary (`ServiceBusOptions.QueueName`) | General job processing (AI analysis, RAG indexing, etc.) |
| Communication (`ServiceBusOptions.CommunicationQueueName`) | Email processing, isolated from general queue |

### Related ADRs

- ADR-004 -- Async job contract and uniform processing

---

## Seam 4: BFF API to Azure AI Services (OpenAI, AI Search, Document Intelligence)

All three AI services use API key authentication and share a common resilience pattern.

| Service | SDK / Client | Endpoint (Dev) | Circuit Breaker Wrapper |
|---------|-------------|----------------|------------------------|
| Azure OpenAI | `AzureOpenAIClient` | `https://spaarke-openai-dev.openai.azure.com/` | `OpenAiClient` |
| Azure AI Search | `SearchClient` | `https://spaarke-search-dev.search.windows.net/` | `ResilientSearchClient` |
| Document Intelligence | REST `HttpClient` | `https://westus2.api.cognitive.microsoft.com/` | `RetryPolicies` |

### Error Handling

- SDK exceptions mapped to ProblemDetails; `503` with `ai_unavailable` when circuit breaker is open
- `429` with `ai_rate_limited` and `retryAfterSeconds` for rate-limited callers
- Each wrapper throws a typed exception when circuit is open (`OpenAiCircuitBrokenException`, `AiSearchCircuitBrokenException`)

### Shared Retry Policy (`RetryPolicies`)

All three services use the same base policy (customized per service):

| Setting | Value |
|---------|-------|
| **Max retries** | 3 |
| **Backoff** | Exponential (1s, 2s, 4s) capped at 30s |
| **Jitter** | +/- 50% |
| **Triggers** | 429, 5xx, network failures |

### Circuit Breaker (OpenAI + AI Search)

| Setting | OpenAI | AI Search |
|---------|--------|-----------|
| **Failure threshold** | 5 (50% ratio) | Configurable via `AiSearchResilienceOptions` |
| **Break duration** | 30s | Configurable |
| **Min throughput** | 5 calls | Configurable |
| **Registry key** | `CircuitBreakerRegistry.AzureOpenAI` | `CircuitBreakerRegistry.AzureAISearch` |

### Related ADRs

- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) -- AI architecture: extend BFF, not separate service

---

## Seam 7: BFF API to Redis Cache

### Data Format

- **Protocol**: Redis (StackExchange.Redis)
- **Key prefix**: `sdap:` (configurable via `Redis:InstanceName`)
- **Serialization**: Binary via `IDistributedCache` interface

### Authentication

- Connection string from configuration (`ConnectionStrings:Redis` or `Redis:ConnectionString`)

### Connection Resilience

| Setting | Value |
|---------|-------|
| **AbortOnConnectFail** | false |
| **ConnectTimeout** | 5000ms |
| **SyncTimeout** | 5000ms |
| **ConnectRetry** | 3 |
| **ReconnectRetryPolicy** | Exponential (base 1000ms) |

### Cache Usage

| Cached Item | TTL | Source |
|------------|-----|--------|
| OBO Graph tokens | 55 minutes | `GraphTokenCache` |
| Tenant-scoped data | Varies | ADR-014 tenant-scoped cache keys |

### Fallback

- Local development: in-memory cache via `AddDistributedMemoryCache()` when `Redis:Enabled` is false
- Production: Redis is required

### Related ADRs

- [ADR-009](../../docs/adr/ADR-009-caching-redis-first.md) -- Redis-first caching; no hybrid L1 unless profiling proves need

---

## Seam 8: BFF API to Dataverse (SDK + Web API)

### Data Format

- **SDK path**: `DataverseServiceClientImpl` via `Microsoft.PowerPlatform.Dataverse.Client` (SOAP/Organization Service)
- **Web API path**: `DataverseWebApiService` via `HttpClient("DataverseWebApi")` for OData REST
- **Endpoint**: `https://spaarkedev1.crm.dynamics.com`

### Authentication

- **SDK path**: Connection string authentication via `ServiceClient` (singleton)
- **Web API path**: Bearer token (OBO or app-only depending on operation)

### Interface Segregation

`IDataverseService` is a composite of 9 narrow interfaces (ISP). Consumers inject the narrowest applicable interface (e.g., `IDocumentDataverseService`, `IEventDataverseService`). `IEventDataverseService` routes to `DataverseWebApiService` (REST); all others route to `DataverseServiceClientImpl` (SDK).

### Retry / Timeout Policy

Via `RetryPolicies.GetDataverseRetryPolicy()`:

| Setting | Value |
|---------|-------|
| **Max retries** | 3 |
| **Initial delay** | 1 second |
| **Max delay** | 30 seconds |
| **Jitter** | +/- 50% |
| **Triggers** | 429, 503, 5xx, network failures |

---

## Seam 9: PCF Controls to Dataverse (Client-Side)

### Data Format

- **Protocol**: Xrm.WebApi (Dataverse client SDK provided by the platform)
- **Operations**: `retrieveRecord`, `retrieveMultipleRecords`, `createRecord`, `updateRecord`, `deleteRecord`
- **Query format**: OData `$select`, `$filter`, `$expand` or FetchXML

### Authentication

- **Implicit**: Xrm.WebApi runs in the authenticated Dataverse session; no token management needed
- **No BFF involvement**: Direct client-to-Dataverse for entity CRUD

### Error Handling

- Xrm.WebApi returns promise rejections with Dataverse error objects
- PCF controls catch and display user-friendly messages

### Retry / Timeout Policy

- Platform-managed; no custom retry in PCF controls
- PCF controls handle transient errors with user-facing retry prompts

---

## Anti-Pattern Reference

| Anti-Pattern | Why It's Wrong | Correct Approach | Reference |
|-------------|---------------|-----------------|-----------|
| Injecting `GraphServiceClient` in endpoints | Leaks Graph SDK types above facade boundary | Use `SpeFileStore` facade | ADR-007 |
| Global authorization middleware | Prevents per-resource auth logic | Use endpoint filters per route | ADR-008 |
| HTTP calls from Dataverse plugins | Plugins must complete in <50ms; network calls are unreliable | Submit job to Service Bus; process in BFF | ADR-002 |
| Hardcoded BFF URL in PCF controls | Breaks multi-tenant deployment | Read from `sprk_BffApiBaseUrl` environment variable | ADR-010 |
| Per-request `ServiceClient` creation | Connection pooling lost; expensive handshake per call | Singleton `ServiceClient` registration | ADR-010 |
| Skipping circuit breaker on AI calls | Cascading failures when OpenAI/Search is down | Use `OpenAiClient`, `ResilientSearchClient` wrappers | ADR-013 |

---

## Related

- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) -- Minimal API + BackgroundService
- [ADR-002](../../docs/adr/ADR-002-no-heavy-plugins.md) -- Thin Dataverse plugins
- [ADR-007](../../docs/adr/ADR-007-spe-storage-seam-minimalism.md) -- SpeFileStore facade
- [ADR-008](../../docs/adr/ADR-008-authorization-endpoint-filters.md) -- Endpoint filters for auth
- [ADR-009](../../docs/adr/ADR-009-caching-redis-first.md) -- Redis-first caching
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) -- AI architecture
- [Component Interactions Guide](../architecture/sdap-component-interactions.md) -- Cross-component impact analysis
