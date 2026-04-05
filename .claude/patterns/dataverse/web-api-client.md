# Web API Client Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Accessing Dataverse from the BFF API (REST or SDK client).

## Read These Files
1. `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` — Pure REST client (ManagedIdentity auth)
2. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — ServiceClient wrapper (ClientSecret auth)
3. `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` — Service interface
4. `src/server/shared/Spaarke.Dataverse/Models.cs` — DTOs and request models

## Constraints
- **ADR-007**: No Graph SDK types leak above facade
- **ADR-010**: DI minimalism — register via feature module

## Key Rules
- Two approaches: `DataverseWebApiClient` (pure REST, .NET 8 compatible) vs `DataverseServiceClientImpl` (SDK)
- Token refresh with 5-minute buffer before expiry
- Extract created record GUID from `OData-EntityId` header (REST) or return value (SDK)
- OData query: use `$filter`, `$select`, `$top` — handle `@odata.nextLink` for pagination
- Async all the way with CancellationToken
