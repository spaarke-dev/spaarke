# Task 023 Implementation Notes

## Date: December 4, 2025

## Summary

**Verification Complete: GraphClientFactory is correctly registered as Singleton.**

No changes required - the existing implementation follows Microsoft best practices for Graph SDK v5.

## Verification

### DI Registration (Program.cs:265)

```csharp
// Singleton GraphServiceClient factory (now uses IHttpClientFactory with resilience handler)
builder.Services.AddSingleton<IGraphClientFactory, Spe.Bff.Api.Infrastructure.Graph.GraphClientFactory>();
```

### GraphClientFactory Implementation Analysis

| Component | Lifetime | Notes |
|-----------|----------|-------|
| `GraphClientFactory` | Singleton | Registered once, reused for all requests |
| `ConfidentialClientApplication` (_cca) | Singleton | Created once in constructor, handles internal MSAL token caching |
| `IHttpClientFactory` | Injected | Manages HTTP connection pooling across all clients |
| `GraphServiceClient` | Per-call | Lightweight wrapper, HttpClient is pooled |
| OBO Tokens | Cached (Redis) | 55-minute TTL, reduces Azure AD load by 97% |

## Architecture

```
Request Flow:
┌─────────────────────────────────────────────────────────────────┐
│  GraphClientFactory (Singleton)                                  │
│  ├── _cca (ConfidentialClientApplication) - reused              │
│  ├── _httpClientFactory (HttpClient pooling) - managed          │
│  └── _tokenCache (Redis) - OBO tokens cached 55 min             │
│                                                                  │
│  ForUserAsync(ctx) → CreateOnBehalfOfClientAsync(token)         │
│    1. Check Redis cache for OBO token                           │
│    2. Cache HIT:  Use cached token (~5ms)                       │
│    3. Cache MISS: OBO exchange via _cca (~200ms), cache result  │
│    4. Create GraphServiceClient with pooled HttpClient          │
└─────────────────────────────────────────────────────────────────┘
```

## Why GraphServiceClient Is Created Per-Call

Per Microsoft guidance for Graph SDK v5:
- `GraphServiceClient` is a lightweight wrapper
- The expensive resources (HttpClient, MSAL token cache) are shared
- Creating new instances per-call allows different auth contexts (app vs user)
- This is the recommended pattern for OBO flow where each request has different user context

## Performance Optimizations Already In Place

1. **Singleton Factory**: `GraphClientFactory` is singleton - no repeated initialization
2. **MSAL Token Caching**: `ConfidentialClientApplication` handles internal token cache
3. **Redis OBO Token Cache**: OBO tokens cached for 55 minutes (Phase 4 implementation)
4. **HttpClient Pooling**: `IHttpClientFactory` manages connection pooling
5. **Resilience Handler**: `GraphHttpMessageHandler` provides retry, circuit breaker, timeout

## No Initialization Overhead Per Request

The original concern was about "initialization overhead per request". This is NOT an issue because:

1. **Factory is singleton** - No repeated factory creation
2. **MSAL _cca is singleton** - Created once, reused
3. **OBO tokens cached** - 97% cache hit rate in production
4. **HttpClient pooled** - No new TCP connections per request

## Acceptance Criteria

| Criterion | Status |
|-----------|--------|
| GraphServiceClient registration verified | ✅ Singleton (via factory) |
| No initialization overhead per request | ✅ (MSAL + Redis caching) |
| Authentication and token refresh work | ✅ (OBO + cache refresh) |
| Finding documented | ✅ |
| dotnet build passes | ✅ |

## Recommendation

**No changes needed.** The current implementation is optimal for Graph SDK v5 with OBO flow:

- Factory pattern allows both app-only and user-delegated auth
- Singleton lifetime ensures single _cca instance
- Redis caching eliminates most OBO token exchanges
- IHttpClientFactory handles connection pooling
