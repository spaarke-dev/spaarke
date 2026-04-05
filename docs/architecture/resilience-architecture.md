# Resilience Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Documents the BFF API resilience subsystem — circuit breakers, retry policies, resilient clients, error handling middleware, and ProblemDetails patterns.

---

## Overview

The Sprk.Bff.Api protects against cascading failures across four external service dependencies (Azure OpenAI, Azure AI Search, Microsoft Graph, Document Intelligence) using a layered resilience architecture built on Polly v8. Each service has configurable retry policies with exponential backoff and jitter, circuit breakers with state tracking, and per-request timeouts. A centralized `CircuitBreakerRegistry` provides monitoring and health check data across all protected services.

Error responses follow RFC 7807 ProblemDetails throughout, with domain-specific error codes (e.g., `OFFICE_001`-`OFFICE_015` for Office integration, `ai_unavailable`/`ai_rate_limited` for AI services) and correlation IDs for distributed tracing.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| CircuitBreakerRegistry | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/CircuitBreakerRegistry.cs` | Centralized state tracking for all circuit breakers; exposes monitoring data and .NET Meter metrics |
| ResilientSearchClient | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/ResilientSearchClient.cs` | Wraps Azure AI Search SDK with timeout + retry + circuit breaker pipeline (Polly v8) |
| RetryPolicies | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/RetryPolicies.cs` | Static factory for HTTP retry policies (Azure OpenAI, Dataverse, Document Intelligence) using Polly v7 `IAsyncPolicy<HttpResponseMessage>` |
| StorageRetryPolicy | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/StorageRetryPolicy.cs` | Retry policy for Dataverse storage operations; handles replication lag (404) with exponential backoff |
| ProblemDetailsHelper | `src/server/api/Sprk.Bff.Api/Infrastructure/Errors/ProblemDetailsHelper.cs` | Factory methods for RFC 7807 ProblemDetails responses with Graph error differentiation and domain-specific error codes |
| AiSearchResilienceOptions | `src/server/api/Sprk.Bff.Api/Configuration/AiSearchResilienceOptions.cs` | Configuration for AI Search retry count, backoff, circuit breaker thresholds, timeout |
| GraphResilienceOptions | `src/server/api/Sprk.Bff.Api/Configuration/GraphResilienceOptions.cs` | Configuration for Graph API retry count, backoff, circuit breaker threshold, Retry-After header support |
| DocumentIntelligenceOptions | `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | Contains Doc Intel circuit breaker threshold, break duration, and timeout (embedded in the main options class) |

## Data Flow

### Resilient Search Request

1. Caller invokes `ResilientSearchClient.SearchAsync<T>(client, searchText, options)`
2. **Circuit breaker** (outermost) checks if circuit is open — if open, throws `AiSearchCircuitBrokenException` immediately (no network call)
3. **Retry** (middle) executes the search — on transient failure (429, 503, 504, 5xx), waits with exponential backoff + jitter and retries up to `RetryCount` times
4. **Timeout** (innermost) cancels any single attempt after `TimeoutSeconds`
5. On success, records success with `CircuitBreakerRegistry`; on final failure, records failure
6. Circuit breaker monitors failure ratio over `SamplingDurationSeconds` window — trips to Open when ratio exceeds threshold across `MinimumThroughput` calls

### HTTP Retry Flow (Azure OpenAI, Dataverse, Document Intelligence)

1. `RetryPolicies.GetAzureOpenAiRetryPolicy(logger)` returns Polly v7 `IAsyncPolicy<HttpResponseMessage>` configured on `IHttpClientBuilder`
2. Handles transient HTTP errors (5xx, network failures) plus 429 (Too Many Requests)
3. Exponential backoff: 1s base delay, max 30s, with 50% jitter factor
4. Max 3 retry attempts; each logged with status code and attempt number

### Storage Retry Flow (Dataverse Replication Lag)

1. `StorageRetryPolicy.ExecuteAsync(action)` wraps Dataverse write operations
2. Handles `StorageRetryableException` and `HttpRequestException` with 404 (NotFound) or 503 (ServiceUnavailable)
3. Deterministic exponential backoff: 2s, 4s, 8s (no jitter — predictable for debugging)
4. Used specifically for newly created documents that may not be immediately visible due to Dataverse replication lag

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | RAG search, knowledge retrieval | `IResilientSearchClient` | All AI Search operations go through resilient wrapper |
| Consumed by | AI pipeline (OpenAI, Doc Intel) | `RetryPolicies.GetAzureOpenAiRetryPolicy()`, `GetDocumentIntelligenceRetryPolicy()` | Applied via `IHttpClientBuilder.AddPolicyHandler()` |
| Consumed by | Document Profile storage | `IStorageRetryPolicy` | Protects against Dataverse replication lag |
| Consumed by | All API endpoints | `ProblemDetailsHelper` | Standardized error responses |
| Consumed by | Health check endpoint | `ICircuitBreakerRegistry.GetAllCircuits()` | Exposes circuit breaker states for monitoring |
| Depends on | Configuration | `IOptions<AiSearchResilienceOptions>`, `IOptions<GraphResilienceOptions>` | All thresholds are configurable |
| Depends on | .NET Meter API | `Sprk.Bff.Api.CircuitBreaker` meter | `circuit_breaker.state_transitions`, `circuit_breaker.open_count` gauges |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Polly v8 for AI Search | `ResiliencePipeline` with timeout + retry + circuit breaker | Modern pipeline builder; circuit breaker integrates with registry callbacks | — |
| Polly v7 for HTTP clients | `IAsyncPolicy<HttpResponseMessage>` | `IHttpClientBuilder.AddPolicyHandler()` still requires Polly v7 policy type | — |
| Centralized circuit registry | `CircuitBreakerRegistry` singleton | Unified monitoring across 4 services; exposes health data for `/healthz` | — |
| No jitter on storage retry | Deterministic 2s, 4s, 8s backoff | Predictable timing simplifies debugging replication lag issues | — |
| Domain-specific error codes | `OFFICE_001`-`OFFICE_015`, `ai_unavailable`, `ai_rate_limited` | Enables client-side error handling without parsing error messages | — |
| Custom exception for circuit open | `AiSearchCircuitBrokenException` with `RetryAfter` | Callers can map directly to HTTP 503 with `Retry-After` header | — |

## Constraints

- **MUST**: Wrap all Azure AI Search operations with `IResilientSearchClient` — never call `SearchClient` directly
- **MUST**: Return RFC 7807 ProblemDetails for all error responses — use `ProblemDetailsHelper` factory methods
- **MUST**: Include `correlationId` in all ProblemDetails error extensions for distributed tracing
- **MUST**: Use `StorageRetryPolicy` for Dataverse writes that depend on recently created records
- **MUST NOT**: Catch and swallow `BrokenCircuitException` silently — always propagate as 503 to the caller
- **MUST NOT**: Create ad-hoc retry loops — use `RetryPolicies` factory or `StorageRetryPolicy`
- **MUST NOT**: Hard-code retry counts or backoff durations — use options classes

## Known Pitfalls

- **Two Polly versions**: `ResilientSearchClient` and `StorageRetryPolicy` use Polly v8 `ResiliencePipeline`; `RetryPolicies` uses Polly v7 `IAsyncPolicy`. Both coexist but have different APIs — do not mix them
- **Graph Retry-After header**: `GraphResilienceOptions.HonorRetryAfterHeader` defaults to `true`. When Graph returns 429 with `Retry-After`, the policy should wait that duration instead of the calculated backoff. The current `RetryPolicies` implementation does not parse this header — the option exists for future enhancement
- **Circuit breaker minimum throughput**: The AI Search circuit breaker requires `MinimumThroughput` (default: 10) calls within the sampling window before it can trip. This prevents false opens during low-traffic periods but means the first few failures always pass through
- **StorageRetryableException factory methods**: Use `DocumentNotFound()` and `ServiceUnavailable()` static factories to create properly typed exceptions — throwing raw `HttpRequestException` will bypass the retry handler unless it has the correct `StatusCode`

## Related

- [Configuration Architecture](configuration-architecture.md) — Resilience options classes
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API architecture (no Azure Functions)
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching (rate limit storage)
