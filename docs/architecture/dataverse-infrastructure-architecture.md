# Dataverse Infrastructure Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Describes how the BFF API interacts with Dataverse for document storage resolution, user access control, and data retrieval.

---

## Overview

The Dataverse infrastructure layer mediates between the BFF API and Microsoft Dataverse (the platform data store). It provides three capabilities: resolving document storage pointers (mapping Dataverse document GUIDs to SharePoint Embedded DriveId/ItemId pairs), querying user access permissions with fail-closed security, and performing CRUD operations against Dataverse entities via a composite service interface.

The key design decision is the **separation of data access from authorization decisions**. The `IAccessDataSource` provides raw permission data, while `AuthorizationService` (in `Spaarke.Core`) computes authorization decisions. This split ensures cached data never bypasses fresh decision logic (ADR-003).

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| `IDocumentStorageResolver` | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/IDocumentStorageResolver.cs` | Abstracts Document GUID to (DriveId, ItemId) resolution |
| `DocumentStorageResolver` | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/DocumentStorageResolver.cs` | Dataverse-backed implementation with heuristic validation |
| `IAccessDataSource` | `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs` | Abstracts user permission queries; returns `AccessSnapshot` |
| `DataverseAccessDataSource` | `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | Queries Dataverse for permissions, teams, roles via OData |
| `CachedAccessDataSource` | `src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` | Redis decorator over `IAccessDataSource` with short TTLs |
| `IDataverseService` | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | Composite interface (ISP) combining 9 domain-focused interfaces |
| `IDocumentDataverseService` | `src/server/shared/Spaarke.Dataverse/IDocumentDataverseService.cs` | Document CRUD, relationship queries, email-to-document lookups |
| `AccessSnapshot` / `AccessRights` | `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs` | Permission model with `[Flags]` enum mapping Dataverse rights |
| Repositories (placeholder) | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/Repositories/` | Reserved for future repository pattern implementations |
| Security (placeholder) | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/Security/` | Reserved for future security layer implementations |

## Data Flow

### Document Storage Resolution

1. API endpoint receives a Document GUID (Dataverse primary key)
2. `DocumentStorageResolver.GetSpePointersAsync()` calls `IDocumentDataverseService.GetDocumentAsync()`
3. Resolver extracts `GraphDriveId` and `GraphItemId` from the document entity
4. Heuristic validation: DriveId must start with `b!` and be 20+ chars; ItemId must be alphanumeric, 20+ chars
5. On validation failure: throws `SdapProblemException` with stable error codes (`document_not_found`, `mapping_missing_drive`, `mapping_missing_item`)
6. On success: returns `(DriveId, ItemId)` tuple for Graph API calls

### User Access Resolution

1. API endpoint calls `IAccessDataSource.GetUserAccessAsync(userId, resourceId, userAccessToken?)`
2. `CachedAccessDataSource` checks Redis (`sdap:auth:access:{userId}:{resourceId}`, TTL 60s)
3. On cache miss: delegates to `DataverseAccessDataSource`
4. `DataverseAccessDataSource` determines auth mode:
   - If `userAccessToken` is provided: performs OBO token exchange via MSAL to get Dataverse token
   - If null: uses service principal (ClientSecretCredential or DefaultAzureCredential)
5. Maps Azure AD Object ID to Dataverse `systemuserid` via OData query
6. Queries document access by attempting direct record retrieval (success = Read access)
7. Queries team memberships and security roles via OData association endpoints
8. Aggregates permissions using bitwise OR on `AccessRights` flags
9. On any error: **fail-closed** -- returns `AccessRights.None`
10. Result cached in Redis (resource: 60s, roles: 2min, teams: 2min)

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Dataverse OData API | `{ServiceUrl}/api/data/v9.2` | Direct HTTP calls via HttpClient |
| Depends on | Azure AD / MSAL | `IConfidentialClientApplication` | OBO token exchange for delegated access |
| Depends on | Redis | `IDistributedCache` | Authorization data caching (ADR-009) |
| Consumed by | BFF API endpoints | `IDocumentStorageResolver` | File access, viewer, AI pipeline |
| Consumed by | `AuthorizationService` | `IAccessDataSource` | Authorization decision computation |
| Consumed by | AI pipeline services | `IDocumentDataverseService` | Document context loading for analysis |
| Consumed by | Background job handlers | `IDataverseService` | Processing jobs use composite interface |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Separate data from decisions | `IAccessDataSource` returns data; `AuthorizationService` decides | Cached data never bypasses fresh decision logic | ADR-003 |
| Redis-first caching | `CachedAccessDataSource` decorator with 60s/2min TTLs | Reduce Dataverse round-trips from 50-200ms to <10ms | ADR-009 |
| Fail-closed security | Returns `AccessRights.None` on any error | Security-sensitive: errors deny access, never grant | -- |
| Interface segregation | `IDataverseService` composes 9 narrower interfaces | New consumers depend on narrowest needed interface | ADR-010 |
| Direct record query for access | Retrieve document with OBO token instead of RetrievePrincipalAccess | RetrievePrincipalAccess unreliable with delegated tokens | -- |
| Heuristic pointer validation | DriveId `b!` prefix check, length checks | Catches incomplete uploads before Graph API returns cryptic errors | -- |

## Constraints

- **MUST**: Return `AccessRights.None` on any error (fail-closed security)
- **MUST**: Use OBO token when `userAccessToken` is provided; service principal when null
- **MUST**: Cache authorization data in Redis with short TTLs (60s resource, 2min roles/teams)
- **MUST NOT**: Cache authorization decisions -- only cache raw data
- **MUST NOT**: Make direct Dataverse calls from API endpoints -- always go through service interfaces
- **MUST**: Validate DriveId/ItemId format before returning from `DocumentStorageResolver`

## Known Pitfalls

- **OBO token exchange requires client credentials**: If `TENANT_ID`, `API_APP_ID`, or `API_CLIENT_SECRET` are not configured, OBO is unavailable and `InvalidOperationException` is thrown
- **Azure AD OID to Dataverse user mapping**: If a user exists in Azure AD but not in Dataverse (no systemuser record), access is denied silently
- **DriveId format change**: The `b!` prefix heuristic is based on current SharePoint behavior; if Microsoft changes the format, the validation will reject valid IDs
- **Cache failure is non-blocking**: Redis errors log warnings but fall through to Dataverse (cache is optimization, not requirement)

## Related

- [ADR-003](../../.claude/adr/ADR-003-authorization-seams.md) -- Cache data not decisions
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) -- Redis-first caching
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) -- DI minimalism
- [Auth & BFF URL Pattern](./AUTH-AND-BFF-URL-PATTERN.md) -- OBO flow architecture
