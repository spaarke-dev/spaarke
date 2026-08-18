# Service Principal Authentication Pattern

> **Last Reviewed**: 2026-08-17
> **Reviewed By**: `spaarke-auth-v4-dataverse-MI` (ADR-028 Amendment A4)
> **Status**: Current — updated for A4 (secret-free confidential credential)

## When
Implementing app-only (no user context) access to Graph API or Dataverse.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — `ForApp()` method for app-only Graph client
2. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — ClientSecret auth for Dataverse SDK
3. `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` — ManagedIdentity auth for Dataverse REST

## Constraints
- **ADR-004**: App-only auth for background jobs and system operations only
- **ADR-016**: Prefer ManagedIdentity over ClientSecret when available
- **ADR-028 A4**: two classes, two credentials — **app-only** uses the UAMI-pinned `TokenCredential` from DI;
  a **confidential client acting as the BFF identity** uses a **MI-FIC client assertion** (default) or a
  **Key Vault certificate**. `.WithClientSecret(...)` is prohibited outside transitional exception **E-3**.

## Key Rules
- Graph app-only: `GraphClientFactory.ForApp()` — UAMI `DefaultAzureCredential` when `Graph:ManagedIdentity:Enabled`
- Dataverse SDK: `ServiceClient` with a `tokenProviderFunction` backed by the DI `TokenCredential` (**not** a
  connection string, and **not** `ClientSecretCredential` — corrected 2026-08-17; `DataverseServiceClientImpl`
  was migrated to MI by `code-quality-and-assurance-r3` #3b)
- Dataverse REST: `DefaultAzureCredential` with `ManagedIdentityClientId`
- MSAL confidential clients: obtain the credential from the **shared provider**, never per call site; **cache the
  CCA at singleton scope** keyed `(tenant|client)` — assertions require shared clients (per-request construction
  discards the MSAL token cache). Reference impl: `DataverseUserClient`'s static CCA cache
- **`DefaultAzureCredential` cannot perform an OBO exchange** — it is not a substitute on delegated paths
- MUST NOT use app-only auth for operations that should respect user permissions
