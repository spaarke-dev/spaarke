# Service Principal Authentication Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (corrected Dataverse SDK auth description)

## When
Implementing app-only (no user context) access to Graph API or Dataverse.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — `ForApp()` method for app-only Graph client
2. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — ClientSecret auth for Dataverse SDK
3. `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` — ManagedIdentity auth for Dataverse REST

## Constraints
- **ADR-004**: App-only auth for background jobs and system operations only
- **ADR-016**: Prefer ManagedIdentity over ClientSecret when available

## Key Rules
- Graph app-only: `GraphClientFactory.ForApp()` with ClientCredential flow
- Dataverse SDK: `ServiceClient` constructed with `ClientSecretCredential` (not a connection string)
- Dataverse REST: `DefaultAzureCredential` with `ManagedIdentityClientId`
- MUST NOT use app-only auth for operations that should respect user permissions
