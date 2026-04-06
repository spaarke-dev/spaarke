# Resilience Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Adding HTTP resilience (retry, circuit breaker, timeout) to external service calls.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Http/GraphHttpMessageHandler.cs` — Production Polly handler for Graph API (retry + circuit breaker + timeout)
2. `src/server/api/Sprk.Bff.Api/Program.cs` — HttpClient registration with resilience policies

## Constraints
- **ADR-017**: All external HTTP calls must have retry + circuit breaker + timeout

## Key Rules
- Policy wrap order: Timeout -> Retry -> Circuit Breaker
- Honor `Retry-After` header (Graph 429 throttling) — don't use fixed delays
- Exponential backoff with jitter: `2^attempt + random(0-1000ms)`
- Circuit breaker: 5 failures -> open for 30s
- Graph API: 3 retries, 60s timeout | OpenAI: 3 retries, 120s timeout | Redis: 2 retries, 5s timeout
- MUST NOT retry 4xx errors (except 429) — only transient 5xx and network errors
