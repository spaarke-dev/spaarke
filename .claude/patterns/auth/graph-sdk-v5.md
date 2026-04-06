# Graph SDK v5 Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Creating or modifying Graph API client setup, authentication modes, or request handling.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — Factory with ForApp() and ForUserAsync()
2. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SimpleTokenCredential.cs` — TokenCredential wrapper for pre-acquired OBO tokens
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Http/GraphHttpMessageHandler.cs` — Polly resilience handler (retry + circuit breaker)

## Constraints
- **ADR-007**: Graph SDK types must not leak above the SpeFileStore facade
- **ADR-010**: GraphClientFactory registered as Singleton
- **ADR-004**: Use OBO for user-delegated, ClientCredential for app-only

## Key Rules
- `ForUserAsync(httpContext, ct)` → OBO user-delegated client (caches token)
- `ForApp()` → app-only client (ClientCredential flow)
- Resilience: GraphHttpMessageHandler wraps all requests with retry + circuit breaker
- MUST use `SimpleTokenCredential` to pass pre-acquired tokens to `GraphServiceClient`
