# Shared Libraries Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Documents the two shared .NET libraries — Spaarke.Core and Spaarke.Dataverse — that provide cross-cutting utilities, authorization, caching, and Dataverse client abstraction for the BFF API.

---

## Overview

The Spaarke backend uses two shared libraries to avoid duplication and enforce separation of concerns. **Spaarke.Core** provides authorization, caching, and utilities that are independent of any specific data store. **Spaarke.Dataverse** provides the Dataverse Web API client, entity models, and domain-specific service interfaces following the Interface Segregation Principle (ISP).

The dependency direction is strictly one-way: `Spaarke.Core` depends on `Spaarke.Dataverse` (for `IAccessDataSource` and `AccessSnapshot`), and the BFF API depends on both. There are no circular references. Both target .NET 8.0.

## Component Structure

### Spaarke.Core

| Component | Path | Responsibility |
|-----------|------|---------------|
| AuthorizationService | `src/server/shared/Spaarke.Core/Auth/AuthorizationService.cs` | Evaluates authorization using a rule chain against Dataverse access snapshots; fail-closed on errors |
| IAuthorizationService | `src/server/shared/Spaarke.Core/Auth/IAuthorizationService.cs` | Interface for authorization evaluation (testing seam) |
| IAuthorizationRule | `src/server/shared/Spaarke.Core/Auth/IAuthorizationRule.cs` | Rule interface with `Continue`/`Allow`/`Deny` decision model |
| OperationAccessPolicy | `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs` | Static mapping of 50+ SPE/Graph operations to required `AccessRights` flags |
| OperationAccessRule | `src/server/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs` | Primary authorization rule — checks user `AccessRights` against `OperationAccessPolicy` |
| DistributedCacheExtensions | `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | `GetOrCreateAsync<T>` with versioned keys and standard TTL constants (ADR-009) |
| RequestCache | `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` | Per-request in-memory cache (scoped lifetime) to collapse duplicate loads within a single HTTP request |
| DesktopUrlBuilder | `src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs` | Generates `ms-word:`/`ms-excel:`/`ms-powerpoint:` protocol URLs from SPE web URLs and MIME types |

### Spaarke.Dataverse

| Component | Path | Responsibility |
|-----------|------|---------------|
| DataverseWebApiClient | `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` | REST client for Dataverse Web API v9.2; thread-safe token refresh, CRUD, OData queries, N:N associations |
| DataverseAccessDataSource | `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | Queries user access permissions and team memberships from Dataverse; supports both OBO and service principal auth |
| IDataverseService | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | Composite interface inheriting 9 ISP-segregated interfaces |
| IDocumentDataverseService | `src/server/shared/Spaarke.Dataverse/IDocumentDataverseService.cs` | Document CRUD, relationship queries, email-to-document lookups |
| IAnalysisDataverseService | `src/server/shared/Spaarke.Dataverse/IAnalysisDataverseService.cs` | Analysis records, outputs, and scope associations |
| ICommunicationDataverseService | `src/server/shared/Spaarke.Dataverse/ICommunicationDataverseService.cs` | Communication accounts, email lookups, contact/account matching |
| IGenericEntityService | `src/server/shared/Spaarke.Dataverse/IGenericEntityService.cs` | Generic CRUD, metadata queries, navigation property discovery |
| IAccessDataSource | `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs` | Interface for user access permission queries; defines `AccessSnapshot` and `AccessRights` flags |
| Models | `src/server/shared/Spaarke.Dataverse/Models.cs` | Entity models: `DocumentEntity`, `AnalysisEntity`, `EventEntity`, `FieldMappingProfileEntity`, request/response DTOs |
| DataverseWebApiService | `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | Implementation of `IDataverseService` composite using `DataverseWebApiClient` |

## Data Flow

### Authorization Check

1. Endpoint filter calls `IAuthorizationService.AuthorizeAsync(context)` with userId, resourceId, and operation name
2. `AuthorizationService` fetches `AccessSnapshot` from `IAccessDataSource.GetUserAccessAsync()` — contains `AccessRights` flags, team memberships, and roles
3. Rules are evaluated in order: `OperationAccessRule` checks `OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, operation)` using bitwise AND
4. First rule to return `Allow` or `Deny` wins; if no rule decides, default is **Deny** (fail-closed)
5. Decision is logged as `AUTHORIZATION GRANTED` or `AUTHORIZATION DENIED` with full context for audit compliance

### Dataverse Access Query (OBO vs Service Principal)

1. If `userAccessToken` is provided: `DataverseAccessDataSource` performs OBO token exchange via MSAL to get a Dataverse token as the user
2. If `userAccessToken` is null: uses service principal (app-only) `ClientSecretCredential` or `DefaultAzureCredential`
3. Maps Azure AD Object ID to Dataverse `systemuserid` via OData query
4. Queries document access by attempting direct record retrieval — if the OBO-authenticated call succeeds, user has at least Read access (Dataverse enforces row-level security)
5. Returns `AccessSnapshot` with combined `AccessRights` from all permission sources

### Distributed Cache GetOrCreate

1. `DistributedCacheExtensions.GetOrCreateAsync<T>(cache, key, factory, expiration)` checks Redis for existing entry
2. On cache hit: deserializes JSON and returns immediately
3. On cache miss: calls `factory`, serializes result to JSON, stores in Redis with TTL, returns value
4. Versioned variant appends `:v:{version}` to key — enables cache invalidation by incrementing version

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | BFF API endpoint filters | `IAuthorizationService` | Per-request authorization via `DocumentAuthorizationFilter` |
| Consumed by | BFF API services | `IDataverseService` (or narrower ISP interface) | All Dataverse operations |
| Consumed by | AI Authorization | `IAccessDataSource` | OBO-based access checks for AI features |
| Consumed by | BFF API caching | `DistributedCacheExtensions`, `RequestCache` | Redis + per-request cache layers |
| Consumed by | Office Add-ins | `DesktopUrlBuilder` | Desktop protocol URL generation |
| Depends on | Azure AD / Entra ID | `ClientSecretCredential`, `DefaultAzureCredential` | Token acquisition for Dataverse |
| Depends on | Dataverse Web API | REST API v9.2 | CRUD operations, metadata queries |
| Depends on | Redis | `IDistributedCache` | Distributed caching (ADR-009) |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| REST client over ServiceClient | `DataverseWebApiClient` (raw HTTP) | Avoids `System.ServiceModel` dependency that conflicts with .NET 8 minimal hosting | — |
| ISP for Dataverse interfaces | 9 focused interfaces composed into `IDataverseService` | Consumers depend on narrowest interface needed; reduces coupling | — |
| Fail-closed authorization | Default Deny on errors and no-rule-match | Security compliance: access is never accidentally granted | — |
| Static OperationAccessPolicy | Pure static class, no DI | Performance (no allocation per request); thread-safe; operations are compile-time constants | ADR-010 |
| AccessRights as Flags enum | Bitwise OR/AND for permission combinations | Dataverse uses composite permissions (Read+Write+Delete); flags enable efficient bitwise checks | — |
| Two cache layers | `DistributedCacheExtensions` (Redis) + `RequestCache` (in-memory per request) | `RequestCache` collapses duplicate loads within a request; Redis provides cross-instance sharing | ADR-009 |

## Constraints

- **MUST**: Use `IDataverseService` or narrower ISP interface for Dataverse access — never call `DataverseWebApiClient` directly from endpoints
- **MUST**: Register `AuthorizationService` with ordered `IAuthorizationRule` chain — rule evaluation order matters
- **MUST**: Follow fail-closed pattern — return `AccessRights.None` on any authorization error
- **MUST**: Use `DistributedCacheExtensions.CreateKey()` for cache key construction — ensures `sdap:` prefix consistency
- **MUST NOT**: Add circular dependencies — `Spaarke.Core` depends on `Spaarke.Dataverse`, never the reverse at the project level
- **MUST NOT**: Create per-request `DataverseWebApiClient` instances — it is designed as a singleton with thread-safe token refresh
- **MUST NOT**: Use `ServiceClient` from `Microsoft.PowerPlatform.Dataverse.Client` for new code — `DataverseWebApiClient` is the replacement

## Known Pitfalls

- **Spaarke.Core depends on Spaarke.Dataverse**: Despite the naming suggesting Core is lower-level, it has a project reference to Dataverse for `IAccessDataSource` and `AccessSnapshot`. This is intentional — authorization needs access data
- **DataverseWebApiClient token refresh**: Uses `SemaphoreSlim` with 30s timeout for thread-safe token refresh. Under extreme concurrency, the semaphore wait can timeout — throwing `TimeoutException`
- **OBO vs service principal in DataverseAccessDataSource**: When `userAccessToken` is provided, subsequent calls use the OBO token. The `_httpClient.DefaultRequestHeaders.Authorization` is mutated — this is safe only because the class uses per-request `HttpRequestMessage` with explicit `Authorization` headers for the actual queries
- **OperationAccessPolicy download quirk**: `driveitem.content.download` requires `AccessRights.Write` (not just `Read`) — this is a deliberate security policy decision, not a bug
- **RequestCache scope**: `RequestCache` is registered as Scoped — it works correctly in HTTP contexts but will throw in singleton services that try to resolve it

## Related

- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism and concrete registrations
- [Configuration Architecture](configuration-architecture.md) — `DataverseOptions`, `RedisOptions` consumed by these libraries
- [Resilience Architecture](resilience-architecture.md) — `StorageRetryPolicy` protects Dataverse writes
