# Streaming Endpoints Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Use when implementing SSE (Server-Sent Events) endpoints for AI analysis features that stream results to the client in real-time.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — SSE endpoint structure, headers, and chunk writing
2. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — stream orchestration and cancellation handling
3. `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` — circuit breaker configuration (5 failures, 30s break)

## Constraints
- **ADR-013**: AI features extend BFF, not a separate service; use AI Tool Framework

## Key Rules
- SSE format: `data: {json}\n\n` with explicit flush after each chunk
- Four chunk types: `metadata`, `chunk`, `done`, `error` — never add others without updating client
- Pass `HttpContext` to all orchestration methods — required for OBO token exchange to access SPE files
- `OperationCanceledException` = client disconnected; swallow silently, do not log as error
- Client must buffer partial chunks across reads (split on `\n\n`, keep remainder)
