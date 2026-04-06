# Dataverse On-Behalf-Of (OBO) Flow Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Implementing or debugging user-delegated Dataverse access from the BFF API.

## Read These Files
1. `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` — OBO implementation with auth check
2. `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/TokenHelper.cs` — Bearer token extraction from HttpContext

## Constraints
- **ADR-004**: User-delegated Dataverse operations MUST use OBO
- **ADR-009**: Cache Dataverse OBO tokens in Redis

## Key Rules
- Scope: `https://{org}.crm.dynamics.com/.default`
- Uses `IAccessDataSource` interface — implementation does OBO exchange internally
- Separate from Graph OBO (different scope, different token) — see obo-flow.md for Graph
- Token extracted via `TokenHelper` from HttpContext Authorization header
