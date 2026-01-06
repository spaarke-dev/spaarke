# Unified Access Control (UAC) Architecture

> **Domain**: Authorization, Access Control, Permission Management
> **Status**: Production-Ready
> **Last Updated**: 2026-01-06
> **Source ADRs**: ADR-003, ADR-008, ADR-009

---

## Overview

The Unified Access Control (UAC) system provides granular, operation-level authorization for SharePoint Embedded and Microsoft Graph API operations. It is distinct from **authentication** (OAuth, OBO, tokens) which establishes identity.

| Concept | Question | System |
|---------|----------|--------|
| **Authentication** | "Who are you?" | OAuth, MSAL, OBO flow |
| **Authorization (UAC)** | "What can you do?" | This document |

**Key Capabilities**:
- Operation-level access control mapped 1:1 to Graph API operations (70+ operations)
- Dataverse-backed permission resolution via `RetrievePrincipalAccess`
- Fail-closed security (deny on errors or missing data)
- Comprehensive audit logging with correlation IDs
- Redis-backed caching with per-request memoization

---

## Architecture

```
                    ┌─────────────────────────────────┐
                    │    BFF API Endpoint Filters     │
                    │  (AiAuthorizationFilter, etc.)  │
                    └───────────────┬─────────────────┘
                                    │
                                    ↓ AuthorizationContext
                    ┌─────────────────────────────────┐
                    │     AuthorizationService        │
                    │  - Evaluates authorization      │
                    │  - Queries Dataverse for UAC    │
                    │  - Audit logging                │
                    └───────────────┬─────────────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              ↓                                           ↓
┌─────────────────────────┐               ┌─────────────────────────┐
│   IAccessDataSource     │               │  IAuthorizationRule     │
│   (DataverseAccess      │               │  (OperationAccessRule)  │
│    DataSource)          │               │                         │
│                         │               │  - Checks AccessRights  │
│  - GetUserAccessAsync() │               │  - Uses Operation       │
│  - RetrievePrincipalAcc │               │    AccessPolicy         │
│  - Returns AccessSnapsh │               │  - Single rule model    │
└───────────────┬─────────┘               └─────────────────────────┘
                │
                ↓
┌─────────────────────────────────────────────────────────────────┐
│                     Dataverse                                   │
│  RetrievePrincipalAccess → AccessRights (Read/Write/Delete/...) │
│  Already factors in: Security Roles, Teams, Business Units,    │
│                       Record Sharing, Field-Level Security     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. AuthorizationService

Main entry point for all authorization checks.

**Key Features**:
- Queries user access data from Dataverse via `IAccessDataSource`
- Evaluates rules in order until one returns Allow/Deny
- Comprehensive audit logging (INFO for grants, WARNING for denials)
- OpenTelemetry activity tracking
- Fail-closed on errors (denies access if system failure)

**Usage**:
```csharp
var context = new AuthorizationContext
{
    UserId = "user-azure-ad-oid",
    ResourceId = "document-guid",
    Operation = "read_metadata",  // Must be in OperationAccessPolicy
    CorrelationId = httpContext.TraceIdentifier
};

var result = await _authorizationService.AuthorizeAsync(context);

if (!result.IsAllowed)
{
    return Results.Problem(
        statusCode: 403,
        title: "Forbidden",
        detail: "Access denied",
        extensions: new Dictionary<string, object?>
        {
            ["reasonCode"] = result.ReasonCode
        });
}
```

**Audit Log Examples**:
```
[INFO] AUTHORIZATION GRANTED: User xxx granted read_metadata on doc-123
       by OperationAccessRule - Reason: sdap.access.allow.operation.read_metadata
       (AccessRights: Read, Write, Duration: 43ms)

[WARN] AUTHORIZATION DENIED: User xxx denied driveitem.delete on doc-456
       by OperationAccessRule - Reason: sdap.access.deny.insufficient_rights
       (AccessRights: Read, Duration: 38ms)
```

---

### 2. OperationAccessPolicy

**Critical Component**: Maps Graph API operations to required Dataverse `AccessRights`.

**Supported Operations (70+)**:

| Category | Examples |
|----------|----------|
| DriveItem Metadata | `driveitem.get`, `driveitem.update`, `read_metadata` |
| DriveItem Content | `driveitem.content.download`, `driveitem.content.upload`, `driveitem.preview` |
| DriveItem File Mgmt | `driveitem.create.folder`, `driveitem.move`, `driveitem.copy`, `driveitem.delete` |
| DriveItem Sharing | `driveitem.createlink`, `driveitem.permissions.add/list/delete` |
| DriveItem Versioning | `driveitem.versions.list`, `driveitem.versions.restore` |
| DriveItem Compliance | `driveitem.sensitivitylabel.*`, `driveitem.retentionlabel.*` |
| Container CRUD | `container.list`, `container.create`, `container.get`, `container.delete` |
| Container Permissions | `container.permissions.list/add/update/delete` |
| Legacy Aliases | `preview_file`, `download_file`, `upload_file`, `delete_file` |

**AccessRights Flags** (from Dataverse):
```csharp
[Flags]
public enum AccessRights
{
    None = 0,
    Read = 1 << 0,      // View metadata, list items, preview
    Write = 1 << 1,     // Modify content/properties, DOWNLOAD
    Delete = 1 << 2,    // Delete items
    Create = 1 << 3,    // Create new items
    Append = 1 << 4,    // Append to existing items
    AppendTo = 1 << 5,  // Allow appending
    Share = 1 << 6      // Share with others
}
```

**Key Business Rules**:
- **Download Requires Write**: `driveitem.content.download` requires `Write` (not just Read) for security compliance
- **Upload Requires Write + Create**: New file uploads need both flags
- **Move Requires Write + Delete**: Moving involves delete from source + write to destination

**API**:
```csharp
// Check if user has required rights
bool hasAccess = OperationAccessPolicy.HasRequiredRights(
    userRights: AccessRights.Read | AccessRights.Write,
    operation: "driveitem.content.download"
);  // → true

// Get missing rights (for error messages)
var missing = OperationAccessPolicy.GetMissingRights(
    userRights: AccessRights.Read,
    operation: "driveitem.delete"
);  // → AccessRights.Delete

// Check if operation is supported
bool isSupported = OperationAccessPolicy.IsOperationSupported("read_metadata");
// → true
```

---

### 3. OperationAccessRule

The single authorization rule that handles all permission checks.

**Flow**:
1. Check if operation is supported (deny if unknown)
2. Get required rights from `OperationAccessPolicy`
3. Bitwise check: `(userRights & required) == required`
4. Allow if all required rights present, deny otherwise

**Why Single Rule?** The `DataverseAccessDataSource.GetUserAccessAsync()` calls Dataverse's `RetrievePrincipalAccess` function, which already computes the user's actual permissions considering:
- Security Roles
- Team Memberships
- Business Units
- Record Sharing (POA)
- Field-Level Security

The rule just checks if those computed permissions satisfy the operation requirements.

---

### 4. DataverseAccessDataSource

Queries Dataverse for user permissions using `RetrievePrincipalAccess`.

**Returns `AccessSnapshot`**:
```csharp
public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessRights AccessRights { get; init; }  // Computed by Dataverse
    public IEnumerable<string> TeamMemberships { get; init; }
    public IEnumerable<string> Roles { get; init; }
    public DateTimeOffset CachedAt { get; init; }
}
```

**Flow**:
1. Map Azure AD OID → Dataverse systemuserid
2. Call `RetrievePrincipalAccess` for the specific document
3. Map Dataverse rights string to `AccessRights` flags
4. Return snapshot (cached per-request)

---

## Integration Patterns

### Endpoint Filter Pattern

```csharp
// In AiAuthorizationFilter.cs
var authContext = new AuthorizationContext
{
    UserId = userId,
    ResourceId = documentId.ToString(),
    Operation = "read_metadata",  // Use OperationAccessPolicy operations
    CorrelationId = httpContext.TraceIdentifier
};

var result = await _authorizationService.AuthorizeAsync(authContext);

if (!result.IsAllowed)
{
    return ProblemDetailsHelper.Forbidden(result.ReasonCode);
}

return await next(context);
```

### DI Registration

```csharp
// In SpaarkeCore.cs
services.AddScoped<IAuthorizationRule, OperationAccessRule>();
services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
services.AddScoped<Spaarke.Core.Auth.IAuthorizationService>(
    sp => sp.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>());
```

---

## Caching

### Two-Layer Caching

| Layer | Scope | TTL | Purpose |
|-------|-------|-----|---------|
| Redis (Distributed) | Cross-request | 5 min (security) | Reduce Dataverse queries |
| RequestCache | Single request | Request lifetime | Collapse duplicate loads |

### Versioned Invalidation

```csharp
// V1: Initial cache entry
await _cache.GetOrCreateAsync("user-access", "v1", () => FetchAccessAsync(), ...);

// V2: After permission change, bump version (old entries expire via TTL)
await _cache.GetOrCreateAsync("user-access", "v2", () => FetchAccessAsync(), ...);
```

---

## Security Principles

### Fail-Closed Design

| Scenario | Result |
|----------|--------|
| Dataverse query fails | **Deny** |
| No rule makes a decision | **Deny** |
| User has `AccessRights.None` | **Deny** |
| Unknown operation | **Deny** |
| Any exception | **Deny** |

### Deny Code Format

Pattern: `{domain}.{area}.{action}.{reason}`

Examples:
- `sdap.access.deny.insufficient_rights`
- `sdap.access.deny.unknown_operation`
- `sdap.access.deny.no_rule`
- `sdap.access.error.system_failure`

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Access Denied" despite permissions | Cache staleness | Wait 5 min TTL or bump cache version |
| High latency (>100ms) | Dataverse slow or cache miss | Check Dataverse perf, Redis health |
| "Unknown operation" error | Operation not in policy | Use valid operation from `OperationAccessPolicy` |

---

## Future Enhancements

The UAC architecture is designed to support future capabilities:

- **Conditional Access Policies**: Time-based, location-based, device-based restrictions
- **Delegated Permissions**: User-to-user delegation with scope limits
- **External Sharing Controls**: Guest access with limited operations
- **Audit Trail Queries**: Historical permission lookups
- **Real-time Permission Sync**: Webhook-based cache invalidation

---

## Related Resources

### Patterns
- [UAC Access Control Pattern](../../.claude/patterns/auth/uac-access-control.md) - Concise implementation guide

### Constraints
- [Auth Constraints](../../.claude/constraints/auth.md) - MUST/MUST NOT rules

### ADRs
- [ADR-003](../adr/ADR-003-lean-authorization-seams.md) - Authorization seams architecture
- [ADR-008](../adr/ADR-008-endpoint-filters.md) - Endpoint filter authorization
- [ADR-009](../adr/ADR-009-redis-first-caching.md) - Caching strategy

### Source Files
```
src/server/shared/Spaarke.Core/Auth/
├── AuthorizationService.cs       # Main authorization engine
├── IAuthorizationService.cs      # Service interface
├── IAuthorizationRule.cs         # Rule interface & enums
├── OperationAccessPolicy.cs      # Operation-to-rights mappings
└── Rules/
    └── OperationAccessRule.cs    # Single authorization rule

src/server/shared/Spaarke.Dataverse/
├── DataverseAccessDataSource.cs  # RetrievePrincipalAccess integration
├── IAccessDataSource.cs          # Interface & AccessSnapshot
└── AccessRights.cs               # Flags enum
```

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2026-01-06 | Simplified to single rule model | Removed redundant TeamMembershipRule |
| 2025-09-30 | Added `OperationAccessPolicy` | Granular permissions for SPE/Graph operations |
| 2025-09-15 | Initial `AuthorizationService` | Resource-level access control |
