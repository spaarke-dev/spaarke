# Spaarke Public-Contracts Facade DI Fascia

> **Last Reviewed**: 2026-06-05
> **Reviewed By**: bff-ai-architecture-audit-r1 Migration PR #9
> **Status**: Verified
> **Source**: [DR-003](../../../projects/bff-ai-architecture-audit-r1/decisions/DR-003-public-contracts-facade.md) · [canonical-architecture-decisions.md §2.3](../../../projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md)

## When
Use when adding any AI capability to `Sprk.Bff.Api` that must be consumed by code outside `Services/Ai/` (Workspace, Finance, Insights endpoints, background jobs, Service Bus consumers).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IBriefingAi.cs` — canonical facade interface shape
2. `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/NullBriefingAi.cs` — canonical P3 Fail-Fast Null peer
3. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (`AddPublicContractsFacade` + `AddNullObjectsForCompoundOff`) — symmetric registration pattern

## Constraints
- **ADR-013** (refined 2026-05-20): external CRUD code MUST consume AI through `PublicContracts/` facade, NOT via `IOpenAiClient` / `IPlaybookService` directly
- **ADR-032 §F.1**: every facade MUST have a Null peer registered in the compound-OFF branches with matching `errorCode` (`ai.<feature>.disabled`)
- **`bff-extensions.md` §F**: any new facade requires symmetric DI registration per Endpoint↔DI Symmetry Rule

## Key Rules
- Facade interface = SDAP-domain DTOs only — never leak `ChatMessage`, OpenAI types, or `Microsoft.Extensions.AI.*` types
- Real impl registered in `AddPublicContractsFacade` (compound-AI-ON gate)
- Null peer registered in `AddNullObjectsForCompoundOff` (both DocIntel-off and Analysis-off branches)
- Null peer throws `FeatureDisabledException(errorCode, detail)` — endpoint catches → 503 ProblemDetails per ADR-018 + ADR-019
- For `IAsyncEnumerable<T>` methods (SSE streams): throw synchronously BEFORE returning the iterator so endpoint sees the exception before negotiating SSE headers
- Defensive-nullable injection (`IBriefingAi? = null`) is PROHIBITED at facade boundary (per ADR-032 §Anti-patterns)
