# Unified Access Control (UAC) Architecture

> **Domain**: Authorization, Access Control, Permission Management
> **Status**: Production-Ready (Internal); Design (External)
> **Last Updated**: 2026-03-16
> **Source ADRs**: ADR-003, ADR-008, ADR-009

---

## Overview

The Unified Access Control (UAC) system provides granular, operation-level authorization across Spaarke's three access planes. It is distinct from **authentication** (OAuth, OBO, tokens) which establishes identity.

| Concept | Question | System |
|---------|----------|--------|
| **Authentication** | "Who are you?" | OAuth, MSAL, OBO flow (internal); Entra External ID (external) |
| **Authorization (UAC)** | "What can you do?" | This document |

### Scope

UAC governs authorization for **two caller types** across **three access planes**:

| Caller Type | Identity | Auth Mechanism | Access Planes |
|-------------|----------|----------------|---------------|
| **Internal (Core User)** | Dataverse systemuser (Azure AD OID) | OAuth Bearer + OBO flow | All three planes |
| **External (Contact)** | Power Pages Contact (Entra External ID) | Power Pages session + portal-issued token | All three planes (via different mechanisms) |

### The Three Access Planes

| Plane | What It Controls | Internal Mechanism | External Mechanism |
|-------|-----------------|-------------------|-------------------|
| **Plane 1: Dataverse Records** | CRUD access to Dataverse rows | Security roles, teams, BU, record sharing (POA) | Power Pages table permissions (web role → parent chain) |
| **Plane 2: SPE Files** | Read/write/delete files in SharePoint Embedded containers | BFF AuthorizationService → OperationAccessPolicy (70+ operations) | BFF AuthorizationService (same) + SPE container membership via Graph |
| **Plane 3: AI Search** | Query results from Azure AI Search indexes | BFF constructs filter from user's accessible entities | BFF constructs filter from contact's participation records |

**Key Capabilities**:
- Operation-level access control mapped 1:1 to Graph API operations (70+ operations)
- Dataverse-backed permission resolution via `RetrievePrincipalAccess` (app-only) or direct query (OBO contexts — see [sdap-auth-patterns.md Pattern 5](sdap-auth-patterns.md))
- Fail-closed security (deny on errors or missing data)
- Comprehensive audit logging with correlation IDs
- Redis-backed caching with per-request memoization
- Three-plane orchestration for external access (Dataverse + SPE + AI Search)

---

## Architecture: Internal Access (Production)

```
                    ┌─────────────────────────────────┐
                    │    BFF API Endpoint Filters     │
                    │  (AiAuthorizationFilter, etc.)  │
                    │  12+ domain-specific filters     │
                    └───────────────┬─────────────────┘
                                    │
                                    ↓ AuthorizationContext
                    ┌─────────────────────────────────┐
                    │     AuthorizationService        │
                    │  - Evaluates authorization      │
                    │  - Queries Dataverse for UAC    │
                    │  - Audit logging + telemetry    │
                    └───────────────┬─────────────────┘
                                    │
                    ┌───────────────┴─────────────────┐
                    │    CachedAccessDataSource       │
                    │  (Decorator over Dataverse)     │
                    │  - Redis: roles (2m TTL)        │
                    │  - Redis: teams (2m TTL)        │
                    │  - Redis: access (60s TTL)      │
                    │  - Fail-open on cache errors    │
                    └───────────────┬─────────────────┘
                                    │ cache miss
              ┌─────────────────────┼─────────────────────┐
              ↓                                           ↓
┌─────────────────────────┐               ┌─────────────────────────┐
│   DataverseAccess       │               │  OperationAccessRule    │
│   DataSource            │               │  (Single Rule Model)    │
│                         │               │                         │
│  Dual-mode:             │               │  - Checks AccessRights  │
│  - App-only: calls      │               │  - Uses Operation       │
│    RetrievePrincipalAcc │               │    AccessPolicy         │
│  - OBO: direct query    │               │  - Bitwise check        │
│    GET /sprk_documents  │               │  (user & required)      │
│  - Maps OID → sysuser   │               │    == required          │
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

## Architecture: External Access (Planned)

External access orchestrates all three planes through a single participation grant:

```
External user action (Power Pages SPA)
  → BFF API endpoint (Bearer: portal-issued token)
    → BFF validates token against portal public key
      → BFF resolves Contact from token claims
        → BFF checks sprk_externalrecordaccess for participation
          ├── Plane 1: Dataverse table permissions auto-cascade (no BFF action)
          ├── Plane 2: BFF calls AuthorizationService with Contact context
          └── Plane 3: BFF constructs AI Search filter from participation records
```

### External Access Orchestration Flow

**Granting access** (triggered by Core User via wizard/dialog → BFF API):

```
Step 1: Create sprk_externalrecordaccess record
        (Contact → Project, AccessLevel, GrantedBy, GrantedDate)

Step 2: BFF API orchestrates:
  ├── 2a: Ensure Contact has web role "Secure Project Participant"
  │       (mspp_webrole N:N relationship)
  ├── 2b: Add Contact's Entra External ID to SPE container
  │       (Graph API: container membership as Reader/Writer)
  └── 2c: If new Contact → create adx_invitation
          (built-in Power Pages invitation, auto-assigns web role)

RESULT: All three planes active
  Plane 1: Table permission parent chain auto-cascades
  Plane 2: SPE container membership granted
  Plane 3: Next AI Search query includes this project in filter
```

**Revoking access** (triggered by Core User → BFF API):

```
Step 1: Deactivate sprk_externalrecordaccess record

Step 2: BFF API orchestrates:
  ├── 2a: Remove Contact from SPE container membership
  ├── 2b: If no other active participation → remove web role
  └── 2c: Deactivate pending adx_invitation

RESULT: All three planes revoked
  Plane 1: Inactive record breaks parent chain
  Plane 2: SPE container membership removed
  Plane 3: Project excluded from search filter
```

---

## Core Components

### 1. AuthorizationService

Main entry point for all authorization checks.

**Key Features**:
- Ordered rule evaluation (currently 1 rule: OperationAccessRule)
- Default-deny on no decision or errors (fail-closed)
- Comprehensive audit logging:
  - INFO: "AUTHORIZATION GRANTED" with duration, access rights
  - WARNING: "AUTHORIZATION DENIED" with reason code, missing rights
- OpenTelemetry activity tracking (tags: userId, resourceId, operation, correlationId)
- Performance: ~40ms p50 (Dataverse), ~5ms p50 on cache hit

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
| DriveItem Collaboration | `driveitem.checkin`, `driveitem.checkout` |
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

**Why Single Rule?** Dataverse's `RetrievePrincipalAccess` (app-only) or direct query (OBO) already computes permissions considering:
- Security Roles
- Team Memberships
- Business Units
- Record Sharing (POA)
- Field-Level Security

The rule just checks if those computed permissions satisfy the operation requirements.

---

### 4. DataverseAccessDataSource

Queries Dataverse for user permissions using a **dual-mode approach**:

| Auth Mode | When Used | Method | Why |
|-----------|-----------|--------|-----|
| **App-only** (service principal) | Background jobs, webhooks, no user context | `RetrievePrincipalAccess` API | Full access to Dataverse admin operations |
| **OBO** (user-delegated) | User-initiated BFF requests | Direct query: `GET /sprk_documents({id})` | `RetrievePrincipalAccess` does NOT work with OBO tokens |

**OBO Direct Query Pattern**: The BFF exchanges the user's bearer token for a Dataverse-scoped token via MSAL OBO flow. It then queries `GET /sprk_documents({resourceId})` — a 200 response means the user has at least Read access (Dataverse applies all row-level security). A 403/404 means access denied.

```csharp
if (!string.IsNullOrEmpty(userAccessToken))
{
    // OBO: Call Dataverse as the user
    dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", dataverseToken);
}
else
{
    // App-only: Service principal authentication
    await EnsureAuthenticatedAsync(ct);
}
```

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

---

### 5. CachedAccessDataSource

**Decorator** over `DataverseAccessDataSource` providing two-layer caching (ADR-009 compliance).

**Strategy**: Cache permission **data**, not decisions.

| Data | Cache Key Pattern | TTL | Scope |
|------|-------------------|-----|-------|
| User Roles | `sdap:auth:roles:{userOid}` | 2 min | User-level, reusable across resources |
| Team Memberships | `sdap:auth:teams:{userOid}` | 2 min | User-level, reusable across resources |
| Resource Access | `sdap:auth:access:{userOid}:{resourceId}` | 60 sec | Resource-specific, most sensitive |

**Performance**: 50-200ms (Dataverse direct) → <10ms on cache hit (97% improvement)

**Fail-open on cache errors**: If Redis is unavailable, falls through to `DataverseAccessDataSource` — no blocked requests due to cache failures.

**Versioned Invalidation**:
```csharp
// V1: Initial cache entry
await _cache.GetOrCreateAsync("user-access", "v1", () => FetchAccessAsync(), ...);

// V2: After permission change, bump version (old entries expire via TTL)
await _cache.GetOrCreateAsync("user-access", "v2", () => FetchAccessAsync(), ...);
```

---

### 6. Endpoint Filters

Authorization is enforced at the endpoint level via domain-specific filters (ADR-008: no global middleware).

**Current Filters (12+)**:

| Filter | Domain | Notes |
|--------|--------|-------|
| `DocumentAuthorizationFilter` | General document access | Operation specified as parameter |
| `AiAuthorizationFilter` | AI analysis access | Full UAC via `IAiAuthorizationService` |
| `AnalysisAuthorizationFilter` | Document analysis | Delegation & role-based |
| `EntityAccessFilter` | Dataverse entity association | Read + write access required |
| `CommunicationAuthorizationFilter` | Email operations | |
| `FinanceAuthorizationFilter` | Finance module | |
| + 6 more | Various domains | |

**Pattern**:
```csharp
// In any endpoint filter
var authContext = new AuthorizationContext
{
    UserId = userId,           // Azure AD OID from 'oid' claim
    ResourceId = documentId,
    Operation = "read_metadata", // From OperationAccessPolicy
    CorrelationId = httpContext.TraceIdentifier
};

var result = await _authorizationService.AuthorizeAsync(authContext);

if (!result.IsAllowed)
    return ProblemDetailsHelper.Forbidden(result.ReasonCode);

return await next(context);
```

---

## External Caller Authorization

### Contact-Based vs. SystemUser-Based Callers

The current system is designed to handle both caller types:

| Caller Type | Token Source | Identity Resolution | Dataverse Mode |
|-------------|-------------|--------------------|-|
| **Internal (systemuser)** | Azure AD Bearer (OBO) | Azure AD OID → Dataverse systemuserid | OBO direct query |
| **External (Contact)** | Portal-issued OAuth token | Portal token claims → Contact record | App-only (service principal) |
| **Background jobs** | No user context | Service principal | App-only |

### External Caller Flow (Planned)

For external callers authenticated via Power Pages:

1. SPA obtains portal-issued token from `/_services/auth/token` (OAuth Implicit Grant)
2. SPA calls BFF API with `Authorization: Bearer <portal-token>`
3. BFF validates token against portal's public key (`/_services/auth/publickey`)
4. BFF extracts Contact identity from token claims
5. BFF queries `sprk_externalrecordaccess` to determine accessible projects
6. BFF creates `AuthorizationContext` with Contact's effective permissions
7. Standard `AuthorizationService` evaluates access

**Key difference from internal**: External callers use **app-only** Dataverse mode (service principal), not OBO. The BFF acts as a trusted intermediary that enforces access based on the Contact's participation records, not Dataverse's native security (which doesn't apply to Contacts).

### Access Level Mapping

External access levels map to SPE operations:

| Access Level | Dataverse Records | SPE Files | AI Search |
|-------------|-------------------|-----------|-----------|
| **View Only** | Read (via table permissions) | Reader (container membership) | Query with project filter |
| **Collaborate** | Read + Create (via table permissions) | Writer (container membership) | Query with project filter |
| **Full Access** | Read + Create + Write (via table permissions) | Writer (container membership) | Query with project filter |

---

## DI Registration

Current production registration in `SpaarkeCore.cs`:

```csharp
// Core auth service (scoped)
services.AddScoped<AuthorizationService>();
services.AddScoped<IAuthorizationService>(
    sp => sp.GetRequiredService<AuthorizationService>());

// AI authorization (OBO mode, FullUAC)
services.AddScoped<IAiAuthorizationService, AiAuthorizationService>();

// HttpClient for Dataverse (with BaseAddress setup)
services.AddHttpClient<DataverseAccessDataSource>((sp, client) =>
{
    var dataverseUrl = configuration["Dataverse:ServiceUrl"];
    client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
});

// Decorator pattern: Dataverse → Cached
services.AddScoped<IAccessDataSource>(sp =>
{
    var inner = sp.GetRequiredService<DataverseAccessDataSource>();
    var cache = sp.GetRequiredService<IDistributedCache>();
    return new CachedAccessDataSource(inner, cache, ...);
});

// Single authorization rule
services.AddScoped<IAuthorizationRule, OperationAccessRule>();

// Per-request memoization
services.AddScoped<RequestCache>();
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
| Contact → systemuser mapping fails | **Deny** (with diagnostic log) |
| External caller with no participation records | **Deny** |

### Deny Code Format

Pattern: `{domain}.{area}.{action}.{reason}`

Examples:
- `sdap.access.deny.insufficient_rights`
- `sdap.access.deny.unknown_operation`
- `sdap.access.deny.no_rule`
- `sdap.access.deny.no_participation` (external)
- `sdap.access.error.system_failure`

---

## Integration Patterns

### Internal Caller Pattern (Production)

Standard endpoint filter authorization — see Endpoint Filters section above.

### External Caller Pattern (Planned)

```csharp
// In ExternalAccessFilter.cs (planned)
// 1. Validate portal token
var contactId = await _portalTokenValidator.ValidateAndGetContactId(
    httpContext.Request.Headers.Authorization);

// 2. Check participation
var participations = await _participationService
    .GetActiveParticipationsAsync(contactId, resourceId);

if (!participations.Any())
    return ProblemDetailsHelper.Forbidden("sdap.access.deny.no_participation");

// 3. Check operation against access level
var accessLevel = participations.Max(p => p.AccessLevel);
var effectiveRights = MapAccessLevelToRights(accessLevel);

var authContext = new AuthorizationContext
{
    UserId = contactId.ToString(),
    ResourceId = resourceId,
    Operation = operation,
    CorrelationId = httpContext.TraceIdentifier,
    EffectiveRights = effectiveRights  // Override: don't query Dataverse
};

var result = await _authorizationService.AuthorizeAsync(authContext);
```

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Access Denied" despite permissions | Cache staleness | Wait 60s-2m TTL or bump cache version |
| High latency (>100ms) | Dataverse slow or cache miss | Check Dataverse perf, Redis health |
| "Unknown operation" error | Operation not in policy | Use valid operation from `OperationAccessPolicy` |
| External user "Access Denied" | No active `sprk_externalrecordaccess` | Verify participation record is Active |
| External user can see records but not files | SPE container membership missing | BFF must add Contact to container via Graph API |
| AI Search returns no results for external | Missing project_id in search filter | BFF must include project IDs from participation records |

---

## Future Enhancements

| Enhancement | Status | Notes |
|-------------|--------|-------|
| External sharing controls (three-plane orchestration) | **Planned** | Secure Project Module — see design doc |
| Conditional access policies (time/location/device) | Future | ADR-003 mentions as enhancement |
| Delegated permissions (user-to-user) | Future | Manager approval scenarios |
| Audit trail queries | Future | Historical permission lookups |
| Real-time permission sync (webhook-based) | Future | Cache invalidation on permission change |
| Power Pages web role + security role merge | Monitoring | 2025 Wave 2 preview — when GA, could simplify external access |
| Azure AI Search native Entra ACL | Monitoring | 2025 preview — could replace manual search filters |

---

## Related Resources

### Architecture Docs
- [Power Pages Access Control & UAC Configuration](power-pages-access-control.md) - External access setup guide
- [Power Pages SPA Technical Guide](power-pages-spa-guide.md) - SPA development standards
- [Auth Patterns](sdap-auth-patterns.md) - OBO, service principal, hybrid patterns

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
├── OperationAccessPolicy.cs      # Operation-to-rights mappings (70+)
└── Rules/
    └── OperationAccessRule.cs    # Single authorization rule

src/server/shared/Spaarke.Dataverse/
├── DataverseAccessDataSource.cs  # Dual-mode: RetrievePrincipalAccess + OBO direct query
├── IAccessDataSource.cs          # Interface & AccessSnapshot
└── AccessRights.cs               # Flags enum

src/server/api/Sprk.Bff.Api/
├── Infrastructure/
│   ├── Caching/CachedAccessDataSource.cs  # Redis-backed caching decorator
│   └── DI/SpaarkeCore.cs                  # Auth DI registration
└── Api/Filters/
    ├── DocumentAuthorizationFilter.cs
    ├── AiAuthorizationFilter.cs
    ├── EntityAccessFilter.cs
    ├── AnalysisAuthorizationFilter.cs
    ├── CommunicationAuthorizationFilter.cs
    ├── FinanceAuthorizationFilter.cs
    └── ... (12+ domain-specific filters)
```

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2026-03-16 | Major update: three-plane model, external access architecture, CachedAccessDataSource, DI details | Secure Project Module design + code audit |
| 2026-01-06 | Simplified to single rule model | Removed redundant TeamMembershipRule |
| 2025-09-30 | Added `OperationAccessPolicy` | Granular permissions for SPE/Graph operations |
| 2025-09-15 | Initial `AuthorizationService` | Resource-level access control |
