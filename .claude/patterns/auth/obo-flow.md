# On-Behalf-Of (OBO) Flow Pattern — Graph API

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Implementing or debugging user-delegated Graph API access from the BFF API.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — `ForUserAsync()` OBO implementation
2. `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` — Redis token caching with SHA256 hash keys
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/TokenHelper.cs` — Bearer token extraction from HttpContext

## Constraints
- **ADR-004**: User-delegated operations MUST use OBO — never app-only for user data
- **ADR-009**: Cache OBO tokens in Redis with 55-min TTL

## Key Rules
- Flow: extract bearer → hash → check cache → if miss, MSAL `AcquireTokenOnBehalfOf` → cache result
- `ForUserAsync(httpContext, ct)` returns a `GraphServiceClient` scoped to the calling user
- Dataverse OBO is separate — see dataverse-obo.md
- Token failures throw `MsalServiceException` → global handler returns 401
