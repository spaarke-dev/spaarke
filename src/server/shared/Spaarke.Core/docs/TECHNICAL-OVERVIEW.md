# Spaarke.Core - Authorization & Caching

**Purpose**: Core business logic for SharePoint Document Access Platform (SDAP)
**Architecture**: Rule-based authorization engine with distributed caching support
**Status**: Production-Ready

---

## Overview

Spaarke.Core provides two critical subsystems for SDAP:

1. **Authorization**: Granular, operation-level access control for SharePoint Embedded/Microsoft Graph API operations
2. **Caching**: Redis-backed distributed caching with per-request memoization

---

## Architecture

```
┌─────────────────────────────────────────────┐
│         Application Layer (BFF API)         │
│  - Document Endpoints                       │
│  - File Preview/Download Endpoints          │
│  - Container Management Endpoints           │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Inject AuthorizationService
┌─────────────────────────────────────────────┐
│       Spaarke.Core.Auth                     │
│  ┌────────────────────────────────────────┐ │
│  │    AuthorizationService                │ │
│  │  - Evaluates authorization requests    │ │
│  │  - Queries user access from Dataverse  │ │
│  │  - Comprehensive audit logging         │ │
│  └──────┬──────────────┬──────────────────┘ │
│         │              │                     │
│         ↓              ↓                     │
│  ┌─────────────┐  ┌─────────────────────┐  │
│  │ IAccessData │  │ IAuthorizationRule  │  │
│  │ Source      │  │ (Chain of Rules)    │  │
│  └─────────────┘  └─────────────────────┘  │
│         │              │                     │
│         │              ↓                     │
│         │     ┌──────────────────────────┐  │
│         │     │ 1. OperationAccessRule   │  │
│         │     │    - Checks operation-   │  │
│         │     │      specific permissions│  │
│         │     │    - Uses OperationAccess│  │
│         │     │      Policy for mappings │  │
│         │     └──────────────────────────┘  │
│         │              │                     │
│         │              ↓                     │
│         │     ┌──────────────────────────┐  │
│         │     │ 2. TeamMembershipRule    │  │
│         │     │    - Team-based access   │  │
│         │     │    - Fallback rule       │  │
│         │     └──────────────────────────┘  │
└─────────┼──────────────────────────────────┘
          │
          ↓ Queries
┌─────────────────────────────────────────────┐
│    Spaarke.Dataverse (IAccessDataSource)    │
│  - GetUserAccessAsync()                     │
│  - Returns AccessSnapshot with:             │
│    * AccessRights (Read/Write/Delete/etc.)  │
│    * Team Memberships                       │
│    * Explicit Grants/Denies                 │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│       Spaarke.Core.Cache                    │
│  ┌────────────────────────────────────────┐ │
│  │  DistributedCacheExtensions            │ │
│  │  - GetOrCreateAsync<T>()               │ │
│  │  - Versioned cache keys                │ │
│  │  - Standard TTLs (security vs metadata)│ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │  RequestCache (per-request)            │ │
│  │  - GetOrCreateAsync<T>()               │ │
│  │  - Collapses duplicate loads           │ │
│  │  - Scoped lifetime (1 request)         │ │
│  └────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

---

## 1. Authorization System

### Design Principles

**Granular Permissions**: Operation-level access control mapped 1:1 to SharePoint Embedded/Microsoft Graph API operations
**Fail-Closed**: Default deny if no rule grants access or on errors
**Comprehensive Auditing**: All authorization decisions logged with correlation IDs
**Rule Chain Pattern**: Ordered evaluation with Continue/Allow/Deny decisions

### Core Components

#### AuthorizationService

Main entry point for authorization checks.

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
    Operation = "driveitem.content.download",
    CorrelationId = "correlation-guid"
};

var result = await _authorizationService.AuthorizeAsync(context);

if (result.IsAllowed)
{
    // Grant access
    _logger.LogInformation("Access granted: {Reason}", result.ReasonCode);
}
else
{
    // Deny access
    _logger.LogWarning("Access denied: {Reason}", result.ReasonCode);
    return Results.Forbid();
}
```

**Audit Log Examples**:
```
[INFO] AUTHORIZATION GRANTED: User xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx granted driveitem.content.download on doc-guid-123 by OperationAccessRule - Reason: sdap.access.allow.operation.driveitem.content.download (AccessRights: Read, Write, Duration: 43ms)

[WARN] AUTHORIZATION DENIED: User xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx denied driveitem.delete on doc-guid-456 by OperationAccessRule - Reason: sdap.access.deny.insufficient_rights (AccessRights: Read, Duration: 38ms)
```

---

#### IAuthorizationRule

Interface for implementing custom authorization rules.

**Contract**:
```csharp
public interface IAuthorizationRule
{
    Task<RuleResult> EvaluateAsync(
        AuthorizationContext context,
        AccessSnapshot snapshot,
        CancellationToken ct = default);
}

public class RuleResult
{
    public required AuthorizationDecision Decision { get; init; }  // Continue, Allow, or Deny
    public required string ReasonCode { get; init; }              // Machine-readable reason
}

public enum AuthorizationDecision
{
    Continue = 0,  // Rule didn't make a decision, continue to next rule
    Allow = 1,     // Grant access
    Deny = 2       // Deny access
}
```

**Rule Chain Behavior**:
- Rules are evaluated in order of DI registration
- First rule to return `Allow` or `Deny` wins (short-circuit)
- If all rules return `Continue`, default deny is applied
- Rules should return `Continue` if they cannot make a decision

---

#### OperationAccessPolicy

**Critical Component**: Maps SharePoint Embedded/Graph API operations to required Dataverse `AccessRights`.

**Supported Operations**: 70+ operations covering:
- **DriveItem Metadata**: get, update, list.children
- **DriveItem Content**: download, upload, replace, preview
- **DriveItem File Management**: create.folder, move, copy, delete, permanentdelete
- **DriveItem Sharing**: createlink, permissions.add/list/delete
- **DriveItem Versioning**: versions.list, versions.restore
- **DriveItem Advanced**: search, delta, follow, thumbnails, createuploadsession
- **DriveItem Compliance**: sensitivitylabel, retentionlabel, lock/unlock
- **DriveItem Collaboration**: checkin, checkout
- **Container CRUD**: list, create, get, update, delete
- **Container Lifecycle**: activate, restore, permanentdelete, lock/unlock
- **Container Permissions**: permissions.list/add/update/delete
- **Container Custom Properties**: customproperties.list/add/update/delete
- **Container Additional**: drive.get, columns.list, recyclebin operations
- **Legacy/Compatibility**: Business-friendly aliases (preview_file, download_file, etc.)

**AccessRights Flags** (from Dataverse):
```csharp
[Flags]
public enum AccessRights
{
    None = 0,
    Read = 1 << 0,      // View metadata, list items
    Write = 1 << 1,     // Modify content/properties
    Delete = 1 << 2,    // Delete items
    Create = 1 << 3,    // Create new items
    Append = 1 << 4,    // Append to existing items
    AppendTo = 1 << 5,  // Allow appending
    Share = 1 << 6      // Share with others
}
```

**Key Business Rules**:
- **Download Requires Write**: `driveitem.content.download` requires `AccessRights.Write` (not just Read) for security compliance
- **Upload Requires Write + Create**: New file uploads need both flags
- **Move Requires Write + Delete**: Moving involves deleting from source, writing to destination

**API**:
```csharp
// Get required rights for an operation
var required = OperationAccessPolicy.GetRequiredRights("driveitem.content.download");
// → AccessRights.Write

// Check if user has required rights
var hasAccess = OperationAccessPolicy.HasRequiredRights(
    userRights: AccessRights.Read | AccessRights.Write,
    operation: "driveitem.content.download"
);
// → true

// Get missing rights (for error messages)
var missing = OperationAccessPolicy.GetMissingRights(
    userRights: AccessRights.Read,
    operation: "driveitem.delete"
);
// → AccessRights.Delete

// Get all supported operations grouped by category
var categories = OperationAccessPolicy.GetOperationsByCategory();
// → Dictionary with keys: "DriveItem - Metadata", "DriveItem - Content", "Container - CRUD", etc.

// Check if operation is supported
bool isSupported = OperationAccessPolicy.IsOperationSupported("driveitem.get");
// → true

// Get human-readable description
string description = OperationAccessPolicy.GetRequirementDescription("driveitem.content.upload");
// → "Write, Create"
```

**Example Mappings**:
```csharp
["driveitem.preview"] = AccessRights.Read
["driveitem.content.download"] = AccessRights.Write  // CRITICAL: Download requires Write
["driveitem.content.upload"] = AccessRights.Write | AccessRights.Create
["driveitem.delete"] = AccessRights.Delete
["driveitem.createlink"] = AccessRights.Share
["container.create"] = AccessRights.Create | AccessRights.Write
```

---

### Built-In Rules

#### 1. OperationAccessRule (Primary)

**Purpose**: Checks if user has required `AccessRights` for the requested operation
**Priority**: First in chain
**Status**: ✅ Active

**Flow**:
1. Check if operation is supported (deny if unknown)
2. Get required rights from `OperationAccessPolicy`
3. Check if user's `AccessSnapshot.AccessRights` includes all required flags (bitwise AND)
4. Allow if user has all required rights, deny otherwise

**Example**:
```csharp
// User has Read + Write
var userRights = AccessRights.Read | AccessRights.Write;

// Operation: driveitem.content.download (requires Write)
var required = AccessRights.Write;

// Check: (userRights & required) == required
// → (Read | Write) & Write == Write  ✅ ALLOW

// Operation: driveitem.delete (requires Delete)
var required2 = AccessRights.Delete;

// Check: (userRights & required2) == required2
// → (Read | Write) & Delete != Delete  ❌ DENY
```

---

#### 2. TeamMembershipRule (Fallback)

**Purpose**: Grants access based on team membership
**Priority**: Second in chain
**Status**: ✅ Active

**Behavior**:
- If user is member of specific teams, grant access
- Typically used for admin teams or global access groups
- Configurable team list

---

#### 3. ExplicitDenyRule (Deprecated)

**Status**: ⚠️ Obsolete - Use `OperationAccessRule` instead
**Reason**: Legacy from binary Grant/Deny model, superseded by granular `AccessRights`

---

#### 4. ExplicitGrantRule (Deprecated)

**Status**: ⚠️ Obsolete - Use `OperationAccessRule` instead
**Reason**: Legacy from binary Grant/Deny model, superseded by granular `AccessRights`

---

### DI Registration

**In BFF API** ([Infrastructure/DI/SpaarkeCore.cs](../../api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs)):

```csharp
public static class SpaarkeCoreExtensions
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // Authorization service
        services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();

        // Authorization rules (registered in order of execution)
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();  // Primary
        services.AddScoped<IAuthorizationRule, TeamMembershipRule>();   // Fallback

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}
```

**In Program.cs**:
```csharp
// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();
```

---

### Integration with ASP.NET Core Authorization

**Endpoint Filter Pattern**:
```csharp
public class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;

    public DocumentAuthorizationFilter(AuthorizationService authorizationService, string operation)
    {
        _authorizationService = authorizationService;
        _operation = operation;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var documentId = httpContext.Request.RouteValues["id"]?.ToString();

        var authContext = new AuthorizationContext
        {
            UserId = userId!,
            ResourceId = documentId!,
            Operation = _operation,
            CorrelationId = Activity.Current?.Id
        };

        var result = await _authorizationService.AuthorizeAsync(authContext);

        if (!result.IsAllowed)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}
```

**Endpoint Registration**:
```csharp
app.MapGet("/api/documents/{id}/download", async (string id, ...) =>
{
    // Download file logic
})
.AddEndpointFilter<DocumentAuthorizationFilter>()
.RequireAuthorization();
```

---

## 2. Caching System

### Design Principles

**Two-Layer Caching**: Distributed (Redis) + Per-Request (in-memory)
**Versioned Keys**: Cache invalidation via version bumping (ADR-009)
**Standard TTLs**: Security data (5 min) vs metadata (15 min)

### Components

#### DistributedCacheExtensions

Extension methods for `IDistributedCache` (Redis).

**Key Features**:
- GetOrCreate pattern with automatic serialization (JSON)
- Versioned cache keys for invalidation
- Standard TTLs for security vs metadata
- Cancellation token support

**Usage**:
```csharp
// Basic GetOrCreate
var document = await _cache.GetOrCreateAsync(
    key: "sdap:document:abc-123",
    factory: async () => await _dataverseService.GetDocumentAsync("abc-123"),
    expiration: DistributedCacheExtensions.MetadataTtl  // 15 minutes
);

// Versioned cache (for invalidation)
var snapshot = await _cache.GetOrCreateAsync(
    key: "sdap:access:user-xyz",
    version: "v2",  // Bump version to invalidate all v1 entries
    factory: async () => await _accessDataSource.GetUserAccessAsync("user-xyz", "doc-123"),
    expiration: DistributedCacheExtensions.SecurityDataTtl  // 5 minutes
);

// With cancellation token
var containers = await _cache.GetOrCreateAsync(
    key: "sdap:containers:all",
    factory: async (ct) => await _dataverseService.GetContainersAsync(ct),
    expiration: TimeSpan.FromMinutes(10),
    ct: cancellationToken
);

// Create standardized cache key
var key = DistributedCacheExtensions.CreateKey(
    category: "document",
    identifier: "abc-123",
    parts: new[] { "metadata", "v1" }
);
// → "sdap:document:abc-123:metadata:v1"
```

**Standard TTLs**:
```csharp
// Security-sensitive data (UAC snapshots, permissions)
public static readonly TimeSpan SecurityDataTtl = TimeSpan.FromMinutes(5);

// Document metadata and other less sensitive data
public static readonly TimeSpan MetadataTtl = TimeSpan.FromMinutes(15);
```

**Versioned Invalidation Pattern**:
```csharp
// V1: Initial cache entry
await _cache.GetOrCreateAsync("user-access", "v1", () => FetchAccessDataAsync(), ...);

// V2: After permission change, bump version
// Old v1 entries expire naturally (TTL), no manual deletion needed
await _cache.GetOrCreateAsync("user-access", "v2", () => FetchAccessDataAsync(), ...);
```

---

#### RequestCache

**Purpose**: Per-request in-memory cache to collapse duplicate loads within a single HTTP request
**Lifetime**: Scoped (one instance per request)
**Use Case**: Avoid multiple Dataverse queries for the same data in a single request

**Usage**:
```csharp
public class DocumentService
{
    private readonly RequestCache _requestCache;
    private readonly IDataverseService _dataverseService;

    public async Task<DocumentEntity> GetDocumentAsync(string id, CancellationToken ct)
    {
        // First call: fetches from Dataverse
        // Subsequent calls in same request: returns cached value
        return await _requestCache.GetOrCreateAsync(
            key: $"document:{id}",
            factory: async (ct) => await _dataverseService.GetDocumentAsync(id, ct),
            ct: ct
        );
    }
}
```

**API**:
```csharp
// Get cached value
var doc = _requestCache.Get<DocumentEntity>("document:123");

// Set cached value
_requestCache.Set("document:123", documentEntity);

// GetOrCreate (sync)
var value = _requestCache.GetOrCreate("key", () => ExpensiveOperation());

// GetOrCreate (async)
var value = await _requestCache.GetOrCreateAsync("key", async () => await ExpensiveOperationAsync());

// GetOrCreate with cancellation token
var value = await _requestCache.GetOrCreateAsync(
    "key",
    async (ct) => await ExpensiveOperationAsync(ct),
    cancellationToken
);
```

---

## Configuration

### DI Registration

```csharp
// In Startup/Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "sdap:";
});

builder.Services.AddSpaarkeCore();  // Registers AuthorizationService, Rules, RequestCache
```

### Redis Connection String

**appsettings.json**:
```json
{
  "Redis": {
    "ConnectionString": "your-redis.redis.cache.windows.net:6380,password=your-key,ssl=True,abortConnect=False"
  }
}
```

**Azure Configuration** (recommended):
```json
{
  "Redis": {
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/RedisConnectionString)"
  }
}
```

---

## Testing

### Unit Tests

**Test AuthorizationService**:
```csharp
[Fact]
public async Task AuthorizeAsync_UserHasRequiredRights_ReturnsAllowed()
{
    // Arrange
    var mockAccessDataSource = new Mock<IAccessDataSource>();
    mockAccessDataSource
        .Setup(x => x.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AccessSnapshot
        {
            AccessRights = AccessRights.Read | AccessRights.Write,
            TeamMemberships = Array.Empty<string>(),
            ExplicitGrants = Array.Empty<string>(),
            ExplicitDenies = Array.Empty<string>()
        });

    var rules = new List<IAuthorizationRule>
    {
        new OperationAccessRule(Mock.Of<ILogger<OperationAccessRule>>())
    };

    var authService = new AuthorizationService(
        mockAccessDataSource.Object,
        rules,
        Mock.Of<ILogger<AuthorizationService>>()
    );

    var context = new AuthorizationContext
    {
        UserId = "user-123",
        ResourceId = "doc-456",
        Operation = "driveitem.content.download"  // Requires Write
    };

    // Act
    var result = await authService.AuthorizeAsync(context);

    // Assert
    Assert.True(result.IsAllowed);
    Assert.Equal("sdap.access.allow.operation.driveitem.content.download", result.ReasonCode);
    Assert.Equal("OperationAccessRule", result.RuleName);
}
```

**Test OperationAccessPolicy**:
```csharp
[Theory]
[InlineData("driveitem.preview", AccessRights.Read, true)]
[InlineData("driveitem.content.download", AccessRights.Read, false)]  // Download needs Write
[InlineData("driveitem.content.download", AccessRights.Write, true)]
[InlineData("driveitem.delete", AccessRights.Read | AccessRights.Write, false)]  // Delete needs Delete flag
public void HasRequiredRights_VariousScenarios_ReturnsExpected(
    string operation,
    AccessRights userRights,
    bool expected)
{
    // Act
    var result = OperationAccessPolicy.HasRequiredRights(userRights, operation);

    // Assert
    Assert.Equal(expected, result);
}
```

### Integration Tests

**Test End-to-End Authorization**:
```csharp
[Fact]
public async Task DownloadEndpoint_UserLacksWriteAccess_Returns403()
{
    // Arrange
    var client = _factory.CreateClient();

    // Setup: User has only Read access (not Write)
    SetupUserAccessRights(AccessRights.Read);

    // Act
    var response = await client.GetAsync("/api/documents/test-doc-123/download");

    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

---

## Performance Considerations

### 1. Authorization Performance

**Typical Latency**: 30-50ms per authorization check
**Breakdown**:
- Dataverse query: 20-30ms
- Rule evaluation: 5-10ms
- Logging/telemetry: 5ms

**Optimization**:
- RequestCache collapses duplicate access checks in same request
- DistributedCache reduces Dataverse queries (5-min TTL for security data)

### 2. Cache Performance

**Redis Latency**: 1-3ms for local Azure Redis
**Serialization**: JSON (fast for small objects < 100KB)
**RequestCache**: < 1ms (in-memory dictionary lookup)

---

## Security Considerations

### 1. Fail-Closed Design

**Principle**: On errors or missing data, deny access (never grant by default)

**Examples**:
- If Dataverse query fails → Deny access
- If no rule makes a decision → Default deny
- If user has `AccessRights.None` → Deny access
- If operation is unknown → Deny access

### 2. Audit Logging

**All authorization decisions are logged**:
- INFO: Access granted (includes rule name, reason, duration)
- WARNING: Access denied (includes missing rights, reason, duration)
- ERROR: System failures (includes exception, fail-closed to deny)

**Log Format**:
```
[INFO] AUTHORIZATION GRANTED: User {UserId} granted {Operation} on {ResourceId} by {RuleName} - Reason: {Reason} (AccessRights: {AccessRights}, Duration: {DurationMs}ms)

[WARN] AUTHORIZATION DENIED: User {UserId} denied {Operation} on {ResourceId} by {RuleName} - Reason: {Reason} (AccessRights: {AccessRights}, Duration: {DurationMs}ms)

[ERROR] AUTHORIZATION ERROR: Failed to evaluate authorization for user {UserId} on {ResourceId} operation {Operation} - Fail-closed: DENY (Duration: {DurationMs}ms)
```

### 3. Cache Security

**Sensitive Data TTL**: 5 minutes (reduces staleness risk for permissions)
**Versioned Keys**: Allows instant invalidation on permission changes
**Per-Request Cache**: Scoped lifetime prevents cross-request leakage

---

## Troubleshooting

### Issue: "Access Denied" Despite User Having Permissions

**Cause**: Cache staleness - permissions changed but cache not invalidated

**Solution**:
1. Wait 5 minutes for `SecurityDataTtl` to expire
2. Or: Bump cache version in code to force refresh
3. Check audit logs for actual `AccessRights` returned from Dataverse

### Issue: High Authorization Latency (> 100ms)

**Cause**: Dataverse query slow or cache miss

**Solution**:
1. Check Dataverse query performance
2. Verify Redis cache is healthy (`az redis show`)
3. Check RequestCache is registered as Scoped (not Transient)

### Issue: "Unknown operation" Error

**Cause**: Operation name not in `OperationAccessPolicy`

**Solution**:
1. Check operation name spelling (case-insensitive)
2. Use Graph API operation name (e.g., "driveitem.content.download")
3. Or use legacy alias (e.g., "download_file")
4. Add new operation to `OperationAccessPolicy` if needed

---

## References

- **ADR-008**: Authorization & Security - Resource-level access control
- **ADR-009**: Caching Strategy - Redis-first with versioned keys
- **SharePoint Embedded API**: [Microsoft Graph DriveItem Resource](https://learn.microsoft.com/en-us/graph/api/resources/driveitem)
- **Dataverse Security**: [Security roles and privileges](https://learn.microsoft.com/en-us/power-platform/admin/security-roles-privileges)

---

## Dependencies

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
```

**Project References**:
- `Spaarke.Dataverse` - For `IAccessDataSource`, `AccessSnapshot`, `AccessRights`

---

## File Structure

```
Spaarke.Core/
├── Auth/
│   ├── AuthorizationService.cs           # Main authorization engine
│   ├── IAuthorizationRule.cs             # Rule interface & enums
│   ├── OperationAccessPolicy.cs          # SPE/Graph operation mappings (70+ operations)
│   └── Rules/
│       ├── OperationAccessRule.cs        # Primary rule: checks AccessRights
│       ├── TeamMembershipRule.cs         # Fallback rule: team-based access
│       ├── ExplicitGrantRule.cs          # [DEPRECATED] Legacy grant rule
│       └── ExplicitDenyRule.cs           # [DEPRECATED] Legacy deny rule
│
├── Cache/
│   ├── DistributedCacheExtensions.cs     # Redis cache helpers (GetOrCreate, versioned keys)
│   └── RequestCache.cs                   # Per-request in-memory cache
│
├── Spaarke.Core.csproj                   # Project file
└── README.md                             # This file
```

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2025-09-30 | Added `OperationAccessPolicy` | Sprint 3 - Granular permissions for SPE/Graph operations |
| 2025-09-26 | Deprecated `ExplicitGrantRule` and `ExplicitDenyRule` | Migrated to granular `AccessRights` model |
| 2025-09-20 | Added `RequestCache` | Performance optimization - reduce duplicate queries |
| 2025-09-15 | Initial `AuthorizationService` | Sprint 2 - Resource-level access control |
