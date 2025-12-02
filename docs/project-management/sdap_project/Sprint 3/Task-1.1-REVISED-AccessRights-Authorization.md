# Sprint 3 Task 1.1 (REVISED): Granular AccessRights Authorization
## Enable Production-Ready Access Control with UI Integration

**Priority**: 🔴 **CRITICAL - BLOCKING ALL FILE OPERATIONS**
**Estimated Effort**: 8-10 days (increased from 5-8 due to granular requirements)
**Dependencies**: None (foundational)
**Status**: 🔴 IN PROGRESS

---

## Executive Summary

This task replaces the placeholder authorization system with **production-ready, granular access control** that:
1. Maps Dataverse permissions (Read/Write/Delete/Create/Append/AppendTo/Share) to SPE file operations
2. Enforces operation-level security (Preview vs Download vs Upload vs Delete)
3. **Exposes user capabilities to UI** for conditional rendering (PCF controls, Power Apps, React)
4. Provides comprehensive audit logging for compliance

**CRITICAL CHANGE**: This revision adds **granular AccessRights** (replacing binary Grant/Deny) and **UI capability endpoints** to support permission-based user experience.

---

## Problem Statement

### Current State (INSECURE)

```csharp
// ALL authenticated users have FULL access to ALL files!
options.AddPolicy("canreadfiles", p => p.RequireAssertion(_ => true)); // TODO
options.AddPolicy("canwritefiles", p => p.RequireAssertion(_ => true)); // TODO
options.AddPolicy("candeletefiles", p => p.RequireAssertion(_ => true)); // TODO
```

**Security Breach**: Any authenticated user can:
- Download any file from SPE
- Upload/replace any file
- Delete any file
- Access any container

### Business Requirement (NEW - Critical Addition)

**Security Policy**:
- User with **Read** access → Can **preview** file only (no download)
- User with **Write** access → Can **download**, **upload**, **replace** files
- User with **Delete** access → Can **delete** files
- User with **Share** access → Can **share** documents

**UI Integration Requirement**:
- PCF Dataset Control needs to show conditional buttons based on user permissions
- Power Apps needs to know which operations are available
- React frontend needs to render UI based on capabilities

**Example**: Document gallery shows different buttons per user:
- Read-only user sees: [👁️ Preview]
- Write user sees: [👁️ Preview] [⬇️ Download] [⬆️ Upload]
- Full access user sees: [👁️ Preview] [⬇️ Download] [⬆️ Upload] [🗑️ Delete] [🔗 Share]

---

## Context

### Dataverse Native Security Model

Dataverse's `RetrievePrincipalAccess` returns granular permissions:

| Dataverse Right | Numeric Value | Description |
|----------------|---------------|-------------|
| `ReadAccess` | 1 | Can view record |
| `WriteAccess` | 2 | Can update record |
| `AppendAccess` | 4 | Can attach to record |
| `AppendToAccess` | 8 | Can be attached to |
| `CreateAccess` | 16 | Can create new records |
| `DeleteAccess` | 32 | Can delete record |
| `ShareAccess` | 64 | Can share record with others |

**Example Response**:
```
"ReadAccess,WriteAccess,DeleteAccess" → User can read, write, delete (but not share)
```

### SPE File Operations

| SPE Operation | Endpoint | Required Dataverse Right |
|--------------|----------|-------------------------|
| **Preview File** | `GET /api/documents/{id}/preview` | Read |
| **Download File** | `GET /api/documents/{id}/download` | Write* |
| **Upload File** | `POST /api/containers/{id}/files` | Write + Create |
| **Replace File** | `PUT /api/documents/{id}/file` | Write |
| **Delete File** | `DELETE /api/documents/{id}` | Delete |
| **Share Document** | `POST /api/documents/{id}/share` | Share |
| **Read Metadata** | `GET /api/documents/{id}/metadata` | Read |
| **Update Metadata** | `PATCH /api/documents/{id}/metadata` | Write |

\* **Business Rule**: Download requires Write access (not just Read) to enforce secure document handling.

---

## Architecture Overview

### Current Architecture (Sprint 2)

```
User Request → API Endpoint
              ↓
         [Authorize("canreadfiles")] ← Policy always returns TRUE
              ↓
         NO SECURITY CHECK ❌
              ↓
         Access SPE (any user can access any file)
```

### Target Architecture (Sprint 3 - Revised)

```
User Request → API Endpoint
              ↓
         [Authorize("candownloadfiles")]
              ↓
         ResourceAccessRequirement("download_file")
              ↓
         ResourceAccessHandler
              ↓
         AuthorizationService.AuthorizeAsync()
              ↓
         DataverseAccessDataSource.GetUserAccessAsync()
              ↓
         Dataverse: RetrievePrincipalAccess
              ↓
         Returns: "ReadAccess,WriteAccess,DeleteAccess"
              ↓
         MapDataverseAccessRights() → AccessRights.Read | Write | Delete
              ↓
         AccessSnapshot { AccessRights = Read | Write | Delete }
              ↓
         OperationAccessRule.EvaluateAsync()
              ↓
         OperationAccessPolicy.GetRequiredRights("download_file") → Requires Write
              ↓
         Check: (User has Read | Write | Delete) & (Needs Write) = Write ✅
              ↓
         ALLOW → 200 OK → Download file from SPE
```

### UI Capability Flow

```
UI Component (PCF Control, Power Apps)
              ↓
         GET /api/documents/{id}/permissions
              ↓
         Returns: { canPreview: true, canDownload: true, canDelete: false, ... }
              ↓
         UI conditionally renders buttons based on capabilities
              ↓
         User clicks [Download] button (only visible if canDownload = true)
              ↓
         API enforces: User actually has Write access
              ↓
         Download succeeds
```

---

## Implementation Steps

### Step 1: Replace AccessLevel with AccessRights Flags

**Problem**: Current `AccessLevel` enum (None/Deny/Grant) doesn't capture granularity.

**Solution**: Use [Flags] enum matching Dataverse permissions.

#### File: `src/shared/Spaarke.Dataverse/IAccessDataSource.cs`

**BEFORE**:
```csharp
public enum AccessLevel
{
    None = 0,
    Deny = 1,
    Grant = 2
}

public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessLevel AccessLevel { get; init; }  // ❌ Too simple
    // ...
}
```

**AFTER**:
```csharp
/// <summary>
/// Granular access rights that mirror Dataverse's permission model.
/// Uses Flags pattern to support bitwise operations.
/// Maps directly to Dataverse RetrievePrincipalAccess response.
/// </summary>
[Flags]
public enum AccessRights
{
    None         = 0,        // 0000000 - No access
    Read         = 1 << 0,   // 0000001 - Can view
    Write        = 1 << 1,   // 0000010 - Can update
    Delete       = 1 << 2,   // 0000100 - Can delete
    Create       = 1 << 3,   // 0001000 - Can create new
    Append       = 1 << 4,   // 0010000 - Can attach to others
    AppendTo     = 1 << 5,   // 0100000 - Others can attach to this
    Share        = 1 << 6    // 1000000 - Can share with others
}

public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessRights AccessRights { get; init; }  // ✅ Granular
    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

**AI Coding Prompt**:
```
Update AccessLevel enum to AccessRights flags enum in IAccessDataSource.cs:

Context:
- Need granular permissions matching Dataverse (Read/Write/Delete/Create/Append/AppendTo/Share)
- Use [Flags] pattern for bitwise operations
- Replace AccessLevel property with AccessRights in AccessSnapshot

Requirements:
1. Create AccessRights enum with [Flags] attribute
2. Use bit-shift for values (1 << 0, 1 << 1, etc.)
3. Update AccessSnapshot to use AccessRights instead of AccessLevel
4. Add XML documentation explaining mapping to Dataverse

Standards:
- Senior C# developer quality
- Follow .NET naming conventions
- Add XML docs for public types
```

---

### Step 2: Update DataverseAccessDataSource to Map Granular Rights

**File**: `src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`

**Update the mapping method**:

```csharp
/// <summary>
/// Maps Dataverse permission string to AccessRights flags.
/// Dataverse returns comma-separated string like "ReadAccess,WriteAccess,DeleteAccess".
/// </summary>
private AccessRights MapDataverseAccessRights(string accessRightsString)
{
    if (string.IsNullOrWhiteSpace(accessRightsString))
        return AccessRights.None;

    var rights = accessRightsString.Split(',', StringSplitOptions.TrimEntries);
    var accessRights = AccessRights.None;

    foreach (var right in rights)
    {
        accessRights |= right switch
        {
            "ReadAccess" => AccessRights.Read,
            "WriteAccess" => AccessRights.Write,
            "DeleteAccess" => AccessRights.Delete,
            "CreateAccess" => AccessRights.Create,
            "AppendAccess" => AccessRights.Append,
            "AppendToAccess" => AccessRights.AppendTo,
            "ShareAccess" => AccessRights.Share,
            _ => AccessRights.None
        };
    }

    _logger.LogDebug("Mapped Dataverse rights '{Rights}' to {AccessRights}",
        accessRightsString, accessRights);

    return accessRights;
}

/// <summary>
/// Determines access level from permission records.
/// </summary>
private AccessRights DetermineAccessLevel(List<PermissionRecord> permissions)
{
    if (!permissions.Any())
        return AccessRights.None;

    // Aggregate all permissions (could have multiple from different sources)
    var aggregatedRights = AccessRights.None;

    foreach (var permission in permissions)
    {
        aggregatedRights |= MapDataverseAccessRights(permission.AccessRights);
    }

    return aggregatedRights;
}
```

**Update the QueryUserPermissionsAsync call site**:
```csharp
public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
{
    // ... existing code ...

    try
    {
        var permissions = await QueryUserPermissionsAsync(userId, resourceId, ct);
        var teams = await QueryUserTeamMembershipsAsync(userId, ct);
        var roles = await QueryUserRolesAsync(userId, ct);

        var accessRights = DetermineAccessLevel(permissions);  // Returns AccessRights now

        return new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = accessRights,  // Changed from AccessLevel
            TeamMemberships = teams,
            Roles = roles,
            CachedAt = DateTimeOffset.UtcNow
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to fetch access data - fail closed");

        return new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = AccessRights.None,  // Fail-closed
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>(),
            CachedAt = DateTimeOffset.UtcNow
        };
    }
}
```

**AI Coding Prompt**:
```
Update DataverseAccessDataSource.cs to map granular Dataverse permissions:

Context:
- Dataverse RetrievePrincipalAccess returns: "ReadAccess,WriteAccess,DeleteAccess"
- Need to parse this string and map to AccessRights flags
- Must aggregate permissions from multiple sources (user, team, role)

Requirements:
1. Create MapDataverseAccessRights(string) method
2. Parse comma-separated permission string
3. Use bitwise OR to combine multiple rights
4. Update DetermineAccessLevel to return AccessRights (not AccessLevel)
5. Update GetUserAccessAsync to use AccessRights
6. Maintain fail-closed security (return None on errors)

Standards:
- Add comprehensive logging
- Handle null/empty strings gracefully
- Add XML documentation
```

---

### Step 3: Create OperationAccessPolicy

**New File**: `src/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs`

```csharp
using Spaarke.Dataverse;

namespace Spaarke.Core.Auth;

/// <summary>
/// Defines the mapping between operations and required Dataverse access rights.
/// Centralizes business rules for what permissions are needed for each operation.
/// </summary>
public static class OperationAccessPolicy
{
    private static readonly Dictionary<string, AccessRights> _operationRequirements = new()
    {
        // ===== File Operations =====

        // Preview: View file in read-only mode (Office Online viewer)
        // Business Rule: Read access sufficient for preview
        ["preview_file"] = AccessRights.Read,

        // Download: Get full file to local machine
        // Business Rule: Requires Write access (not just Read) for security
        ["download_file"] = AccessRights.Write,

        // Upload: Create new file in container
        // Business Rule: Needs both Create (new record) and Write (file content)
        ["upload_file"] = AccessRights.Write | AccessRights.Create,

        // Replace: Overwrite existing file
        // Business Rule: Write access to modify existing file
        ["replace_file"] = AccessRights.Write,

        // Delete: Remove file permanently
        // Business Rule: Explicit Delete permission required
        ["delete_file"] = AccessRights.Delete,

        // ===== Container Operations =====

        // Manage Containers: Create/delete SPE containers
        // Business Rule: Create permission required
        ["manage_container"] = AccessRights.Create | AccessRights.Write,

        // ===== Metadata Operations =====

        // Read Metadata: View document properties
        // Business Rule: Read access sufficient
        ["read_metadata"] = AccessRights.Read,

        // Update Metadata: Change document properties
        // Business Rule: Write access required
        ["update_metadata"] = AccessRights.Write,

        // ===== Sharing Operations =====

        // Share Document: Grant access to others
        // Business Rule: Explicit Share permission required
        ["share_document"] = AccessRights.Share,

        // ===== Advanced Operations =====

        // Copy File: Duplicate to another location
        // Business Rule: Read source + Create destination
        ["copy_file"] = AccessRights.Read | AccessRights.Create,

        // Move File: Transfer to another location
        // Business Rule: Write source + Delete source + Create destination
        ["move_file"] = AccessRights.Write | AccessRights.Delete | AccessRights.Create
    };

    /// <summary>
    /// Gets the required access rights for a given operation.
    /// </summary>
    /// <param name="operation">The operation name (e.g., "download_file")</param>
    /// <returns>The AccessRights flags required for this operation</returns>
    public static AccessRights GetRequiredRights(string operation)
    {
        return _operationRequirements.TryGetValue(operation, out var rights)
            ? rights
            : AccessRights.None;
    }

    /// <summary>
    /// Checks if user has the required rights for an operation.
    /// Uses bitwise AND to verify all required rights are present.
    /// </summary>
    /// <param name="userRights">The access rights the user has</param>
    /// <param name="operation">The operation to check</param>
    /// <returns>True if user has all required rights</returns>
    public static bool HasRequiredRights(AccessRights userRights, string operation)
    {
        var required = GetRequiredRights(operation);

        // Bitwise check: User must have ALL required rights
        // Example: User has (Read | Write | Delete), operation needs (Write)
        //          (Read | Write | Delete) & Write = Write ✅
        // Example: User has (Read), operation needs (Write)
        //          Read & Write = None ❌
        return (userRights & required) == required;
    }

    /// <summary>
    /// Gets all supported operations (for documentation/tooling).
    /// </summary>
    public static IEnumerable<string> GetSupportedOperations() => _operationRequirements.Keys;

    /// <summary>
    /// Gets user's missing rights for an operation (for error messages).
    /// </summary>
    public static AccessRights GetMissingRights(AccessRights userRights, string operation)
    {
        var required = GetRequiredRights(operation);
        return required & ~userRights;  // Bitwise: required AND NOT user = missing
    }
}
```

**AI Coding Prompt**:
```
Create OperationAccessPolicy.cs with operation → required rights mapping:

Context:
- Central policy defining what Dataverse permissions are needed for each SPE operation
- Business Rule: Download requires Write (not just Read) for security
- Must support bitwise checking of multiple rights (e.g., Upload needs Write | Create)

Requirements:
1. Static class with Dictionary<string, AccessRights> mapping
2. GetRequiredRights(string operation) method
3. HasRequiredRights(AccessRights user, string operation) with bitwise check
4. GetMissingRights(AccessRights user, string operation) for error messages
5. GetSupportedOperations() for introspection

Operations to support:
- preview_file, download_file, upload_file, replace_file, delete_file
- manage_container
- read_metadata, update_metadata
- share_document

Standards:
- Comprehensive XML documentation
- Business rule comments explaining each mapping
- Senior C# quality code
```

---

### Step 4: Create OperationAccessRule

**New File**: `src/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs`

```csharp
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

/// <summary>
/// Authorization rule that checks if user has required AccessRights for the requested operation.
/// Uses OperationAccessPolicy to determine required rights.
/// Primary rule for operation-level authorization.
/// </summary>
public class OperationAccessRule : IAuthorizationRule
{
    private readonly ILogger<OperationAccessRule> _logger;

    public OperationAccessRule(ILogger<OperationAccessRule> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<RuleResult> EvaluateAsync(
        AuthorizationContext context,
        AccessSnapshot snapshot,
        CancellationToken ct = default)
    {
        // Get required rights for this operation
        var required = OperationAccessPolicy.GetRequiredRights(context.Operation);

        if (required == AccessRights.None)
        {
            // Unknown operation - Continue to next rule (might be handled elsewhere)
            _logger.LogDebug("Operation '{Operation}' not defined in OperationAccessPolicy - continuing",
                context.Operation);
            return Task.FromResult(RuleResult.Continue());
        }

        // Check if user has all required rights (bitwise AND check)
        if (OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, context.Operation))
        {
            _logger.LogDebug(
                "User {UserId} has required rights for operation '{Operation}' - User has: {UserRights}, Required: {Required}",
                context.UserId, context.Operation, snapshot.AccessRights, required);

            return Task.FromResult(RuleResult.Allow(
                $"sdap.access.allow.operation.{context.Operation}",
                $"User has required access rights: {required}"));
        }

        // User lacks required rights - DENY
        var missing = OperationAccessPolicy.GetMissingRights(snapshot.AccessRights, context.Operation);

        _logger.LogWarning(
            "User {UserId} DENIED for operation '{Operation}' - User has: {UserRights}, Required: {Required}, Missing: {Missing}",
            context.UserId, context.Operation, snapshot.AccessRights, required, missing);

        return Task.FromResult(RuleResult.Deny(
            $"sdap.access.deny.operation.insufficient_rights",
            $"Operation '{context.Operation}' requires {required} but user only has {snapshot.AccessRights}. Missing: {missing}"));
    }
}
```

**AI Coding Prompt**:
```
Create OperationAccessRule.cs that checks user's AccessRights against operation requirements:

Context:
- Primary authorization rule for operation-level security
- Uses OperationAccessPolicy to get required rights
- Must perform bitwise check for multiple rights (e.g., Upload needs Write AND Create)

Requirements:
1. Implement IAuthorizationRule interface
2. Call OperationAccessPolicy.GetRequiredRights(operation)
3. Check if user has all required rights using bitwise AND
4. Return Allow if user has rights
5. Return Deny with detailed message showing what's missing
6. Return Continue if operation not recognized
7. Add comprehensive logging (Debug for allow, Warning for deny)

Standards:
- Inject ILogger for logging
- Use structured logging with parameters
- Include user rights, required rights, missing rights in deny message
- Senior C# quality
```

---

### Step 5: Update Authorization Rules Registration

**File**: `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`

**BEFORE**:
```csharp
services.AddScoped<IAuthorizationRule, ExplicitDenyRule>();
services.AddScoped<IAuthorizationRule, ExplicitGrantRule>();
services.AddScoped<IAuthorizationRule, TeamMembershipRule>();
```

**AFTER**:
```csharp
// Authorization rules evaluated in order
// Rule chain stops at first Allow or Deny (Continue proceeds to next rule)

// 1. OperationAccessRule: Primary check - does user have required rights?
services.AddScoped<IAuthorizationRule, OperationAccessRule>();

// 2. TeamMembershipRule: Team-based access (can grant additional rights)
services.AddScoped<IAuthorizationRule, TeamMembershipRule>();

// 3. ExplicitDenyRule: Explicit denials override everything (if needed)
// services.AddScoped<IAuthorizationRule, ExplicitDenyRule>(); // Keep if using explicit deny feature

// Note: ExplicitGrantRule removed - replaced by OperationAccessRule
```

**Rationale**: `OperationAccessRule` replaces both `ExplicitGrantRule` and `ExplicitDenyRule` since it directly evaluates Dataverse permissions.

---

### Step 6: Update Authorization Policies

**File**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
builder.Services.AddAuthorization(options =>
{
    // ===== File Operations =====
    options.AddPolicy("canpreviewfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("preview_file")));

    options.AddPolicy("candownloadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("download_file")));

    options.AddPolicy("canuploadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("upload_file")));

    options.AddPolicy("canreplacefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("replace_file")));

    options.AddPolicy("candeletefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("delete_file")));

    // ===== Container Operations =====
    options.AddPolicy("canmanagecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("manage_container")));

    // ===== Metadata Operations =====
    options.AddPolicy("canreadmetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("read_metadata")));

    options.AddPolicy("canupdatemetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("update_metadata")));

    // ===== Sharing Operations =====
    options.AddPolicy("cansharedocuments", p =>
        p.Requirements.Add(new ResourceAccessRequirement("share_document")));
});
```

---

### Step 7: Create User Capabilities Endpoint (NEW - Critical for UI)

**New File**: `src/api/Spe.Bff.Api/Models/DocumentCapabilities.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Represents what operations a user can perform on a specific document.
/// Used by UI (PCF controls, Power Apps, React) for conditional rendering.
/// </summary>
public class DocumentCapabilities
{
    /// <summary>
    /// The document ID these capabilities apply to
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// The user ID these capabilities apply to
    /// </summary>
    public required string UserId { get; init; }

    // ===== File Operations =====

    /// <summary>
    /// Can user preview file in read-only mode? (Office Online viewer)
    /// </summary>
    public bool CanPreview { get; init; }

    /// <summary>
    /// Can user download file to local machine? (requires Write access)
    /// </summary>
    public bool CanDownload { get; init; }

    /// <summary>
    /// Can user upload new file to container? (requires Write + Create)
    /// </summary>
    public bool CanUpload { get; init; }

    /// <summary>
    /// Can user replace existing file? (requires Write)
    /// </summary>
    public bool CanReplace { get; init; }

    /// <summary>
    /// Can user delete file permanently? (requires Delete)
    /// </summary>
    public bool CanDelete { get; init; }

    // ===== Metadata Operations =====

    /// <summary>
    /// Can user view document metadata? (requires Read)
    /// </summary>
    public bool CanReadMetadata { get; init; }

    /// <summary>
    /// Can user update document metadata? (requires Write)
    /// </summary>
    public bool CanUpdateMetadata { get; init; }

    // ===== Sharing Operations =====

    /// <summary>
    /// Can user share document with others? (requires Share)
    /// </summary>
    public bool CanShare { get; init; }

    // ===== Raw Access Rights (for advanced use) =====

    /// <summary>
    /// Raw access rights string (e.g., "Read, Write, Delete") for debugging
    /// </summary>
    public string AccessRights { get; init; } = string.Empty;
}
```

**New File**: `src/api/Spe.Bff.Api/Api/PermissionsEndpoints.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Spe.Bff.Api.Models;
using System.Security.Claims;

namespace Spe.Bff.Api.Api;

/// <summary>
/// Endpoints for querying user permissions and capabilities.
/// Used by UI to determine which buttons/actions to show.
/// </summary>
public static class PermissionsEndpoints
{
    public static void MapPermissionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Permissions")
            .RequireAuthorization();

        // Get user's capabilities for a single document
        group.MapGet("/{documentId}/permissions", GetDocumentPermissionsAsync)
            .WithName("GetDocumentPermissions")
            .WithSummary("Get user's capabilities for a specific document")
            .WithDescription("Returns which operations (preview, download, delete, etc.) the user can perform on this document. Used by UI for conditional rendering.")
            .Produces<DocumentCapabilities>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // Get user's capabilities for multiple documents (batch)
        group.MapPost("/permissions/batch", GetBatchPermissionsAsync)
            .WithName("GetBatchPermissions")
            .WithSummary("Get user's capabilities for multiple documents")
            .WithDescription("Returns capabilities for multiple documents in one request. Useful for document galleries/lists.")
            .Produces<BatchPermissionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Gets user's capabilities for a specific document
    /// </summary>
    private static async Task<IResult> GetDocumentPermissionsAsync(
        [FromRoute] string documentId,
        HttpContext httpContext,
        IAccessDataSource accessDataSource,
        CancellationToken ct)
    {
        // Extract user ID from JWT token
        var userId = httpContext.User.FindFirst("oid")?.Value
                     ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? httpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Get user's access rights from Dataverse
        var snapshot = await accessDataSource.GetUserAccessAsync(userId, documentId, ct);

        // Map to capabilities
        var capabilities = new DocumentCapabilities
        {
            DocumentId = documentId,
            UserId = userId,

            // Check each operation
            CanPreview = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "preview_file"),
            CanDownload = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "download_file"),
            CanUpload = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "upload_file"),
            CanReplace = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "replace_file"),
            CanDelete = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "delete_file"),
            CanReadMetadata = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "read_metadata"),
            CanUpdateMetadata = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "update_metadata"),
            CanShare = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "share_document"),

            // Raw rights for debugging
            AccessRights = snapshot.AccessRights.ToString()
        };

        return Results.Ok(capabilities);
    }

    /// <summary>
    /// Gets user's capabilities for multiple documents (batch operation)
    /// </summary>
    private static async Task<IResult> GetBatchPermissionsAsync(
        [FromBody] BatchPermissionsRequest request,
        HttpContext httpContext,
        IAccessDataSource accessDataSource,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirst("oid")?.Value
                     ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var results = new List<DocumentCapabilities>();

        foreach (var documentId in request.DocumentIds)
        {
            var snapshot = await accessDataSource.GetUserAccessAsync(userId, documentId, ct);

            results.Add(new DocumentCapabilities
            {
                DocumentId = documentId,
                UserId = userId,
                CanPreview = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "preview_file"),
                CanDownload = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "download_file"),
                CanUpload = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "upload_file"),
                CanReplace = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "replace_file"),
                CanDelete = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "delete_file"),
                CanReadMetadata = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "read_metadata"),
                CanUpdateMetadata = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "update_metadata"),
                CanShare = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "share_document"),
                AccessRights = snapshot.AccessRights.ToString()
            });
        }

        return Results.Ok(new BatchPermissionsResponse { Permissions = results });
    }
}

public class BatchPermissionsRequest
{
    public List<string> DocumentIds { get; set; } = new();
}

public class BatchPermissionsResponse
{
    public List<DocumentCapabilities> Permissions { get; set; } = new();
}
```

**Register in Program.cs**:
```csharp
// In Program.cs, after app is built:
app.MapPermissionsEndpoints();  // NEW
```

**AI Coding Prompt**:
```
Create PermissionsEndpoints.cs with user capability endpoints for UI integration:

Context:
- UI needs to know which buttons to show (Preview, Download, Delete, etc.)
- PCF Dataset Control will call these endpoints to get user's capabilities
- Must support both single document and batch queries for performance

Requirements:
1. Create DocumentCapabilities DTO with bool fields for each operation
2. Create GET /api/documents/{id}/permissions endpoint
3. Create POST /api/documents/permissions/batch endpoint for multiple documents
4. Use OperationAccessPolicy.HasRequiredRights to check each operation
5. Return 401 if user not authenticated
6. Return 200 with capabilities object

Standards:
- Use minimal API pattern
- Add OpenAPI documentation (WithSummary, WithDescription)
- Inject IAccessDataSource
- Extract userId from JWT claims (oid, sub, NameIdentifier)
- Senior C# quality
```

---

### Step 8: Update Integration Tests

**File**: `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs`

Update tests to use `AccessRights` instead of `AccessLevel`:

```csharp
// Update MockAccessDataSource
internal class MockAccessDataSource : IAccessDataSource
{
    private readonly AccessRights _accessRights;  // Changed from AccessLevel

    public MockAccessDataSource(AccessRights accessRights)
    {
        _accessRights = accessRights;
    }

    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = _accessRights,  // Changed
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        });
    }
}

// Update test scenarios
[Theory]
[InlineData(AccessRights.None, HttpStatusCode.Forbidden)]
[InlineData(AccessRights.Read, HttpStatusCode.Forbidden)]  // Read not enough for download!
[InlineData(AccessRights.Write, HttpStatusCode.OK)]        // Write needed for download
[InlineData(AccessRights.Read | AccessRights.Write | AccessRights.Delete, HttpStatusCode.OK)]
public async Task Authorization_EnforcesAccessRights(AccessRights accessRights, HttpStatusCode expectedStatus)
{
    // Arrange
    var client = _fixture.CreateClientWithMockedAccess(accessRights);
    var token = GenerateMockJwt("test-user");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Act - Try to download (requires Write)
    var response = await client.GetAsync("/api/documents/test-doc/download");

    // Assert
    if (expectedStatus == HttpStatusCode.OK)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    else
        response.StatusCode.Should().Be(expectedStatus);
}

// Add test for permissions endpoint
[Fact]
public async Task GetPermissions_ReturnsUserCapabilities()
{
    // Arrange - User has Read and Write (but not Delete)
    var client = _fixture.CreateClientWithMockedAccess(AccessRights.Read | AccessRights.Write);
    var token = GenerateMockJwt("test-user");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Act
    var response = await client.GetAsync("/api/documents/test-doc/permissions");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var capabilities = await response.Content.ReadFromJsonAsync<DocumentCapabilities>();
    capabilities.Should().NotBeNull();
    capabilities.CanPreview.Should().BeTrue();   // Has Read
    capabilities.CanDownload.Should().BeTrue();  // Has Write
    capabilities.CanDelete.Should().BeFalse();   // Missing Delete
    capabilities.CanShare.Should().BeFalse();    // Missing Share
}
```

---

## PCF Dataset Control Integration

### Control Specification

**File**: Create new document `Sprint 3/Task-1.1-PCF-Control-Specification.md`

**Overview**: PCF Dataset Control for Document entity with permission-based UI.

**Key Features**:
1. Calls `/api/documents/{id}/permissions` on record select
2. Conditionally renders command bar buttons based on capabilities
3. Supports both single and batch permission queries

**Implementation**:
```typescript
// In PCF control's updateView method
async updateView(context: ComponentFramework.Context<IInputs>): Promise<void> {
    const selectedRecords = context.parameters.dataset.getSelectedRecordIds();

    if (selectedRecords.length === 1) {
        // Get permissions for selected document
        const documentId = selectedRecords[0];
        const capabilities = await this.getDocumentPermissions(documentId);

        // Update command bar visibility
        this.updateCommandBar(capabilities);
    }
}

private async getDocumentPermissions(documentId: string): Promise<DocumentCapabilities> {
    const token = await this.getAuthToken();
    const response = await fetch(`/api/documents/${documentId}/permissions`, {
        headers: { 'Authorization': `Bearer ${token}` }
    });
    return await response.json();
}

private updateCommandBar(capabilities: DocumentCapabilities): void {
    // Show/hide buttons based on capabilities
    this.previewButton.visible = capabilities.canPreview;
    this.downloadButton.visible = capabilities.canDownload;
    this.uploadButton.visible = capabilities.canUpload;
    this.deleteButton.visible = capabilities.canDelete;
    this.shareButton.visible = capabilities.canShare;
}
```

**See full specification**: [Task-1.1-PCF-Control-Specification.md](Task-1.1-PCF-Control-Specification.md) (to be created)

---

## Testing Strategy

### Unit Tests
1. **OperationAccessPolicy Tests**:
   - Test each operation mapping (preview_file → Read, download_file → Write, etc.)
   - Test HasRequiredRights with various combinations
   - Test GetMissingRights calculates correctly

2. **OperationAccessRule Tests**:
   - Mock AccessSnapshot with different AccessRights
   - Test Allow when user has required rights
   - Test Deny when user lacks required rights
   - Test Continue when operation unknown

3. **MapDataverseAccessRights Tests**:
   - Test parsing "ReadAccess,WriteAccess,DeleteAccess"
   - Test bitwise aggregation of multiple rights
   - Test empty/null handling

### Integration Tests
1. **Authorization Flow**:
   - Test 403 when user has Read but tries to Download (needs Write)
   - Test 200 when user has Write and tries to Download
   - Test 403 when user has Write but tries to Delete (needs Delete)
   - Test 200 when user has full rights

2. **Permissions Endpoint**:
   - Test GET /api/documents/{id}/permissions returns correct capabilities
   - Test batch endpoint with multiple documents
   - Test 401 when not authenticated

3. **PCF Control Integration** (manual):
   - Select document in gallery
   - Verify correct buttons shown/hidden
   - Test with different users (admin, read-only, editor)

---

## Validation Checklist

Before marking complete:

### Code Implementation ✅
- [ ] AccessLevel enum replaced with AccessRights flags
- [ ] DataverseAccessDataSource maps granular rights
- [ ] OperationAccessPolicy created with all operations
- [ ] OperationAccessRule implemented and registered
- [ ] Authorization policies updated (preview, download, upload, delete, share)
- [ ] DocumentCapabilities DTO created
- [ ] GET /api/documents/{id}/permissions endpoint working
- [ ] POST /api/documents/permissions/batch endpoint working
- [ ] ResourceAccessHandler unchanged (still works)

### Testing ✅
- [ ] Unit tests for OperationAccessPolicy pass
- [ ] Unit tests for OperationAccessRule pass
- [ ] Integration tests updated for AccessRights
- [ ] Integration tests for permissions endpoint pass
- [ ] Manual test: User with Read only cannot download
- [ ] Manual test: User with Write can download
- [ ] Manual test: User with Delete can delete
- [ ] Manual test: Permissions endpoint returns correct capabilities

### Documentation ✅
- [ ] OperationAccessPolicy has XML docs
- [ ] OperationAccessRule has XML docs
- [ ] DocumentCapabilities has XML docs
- [ ] Permissions endpoints have OpenAPI docs
- [ ] PCF control specification created
- [ ] This task document updated

### UI Integration ✅
- [ ] PCF control can call permissions endpoint
- [ ] PCF control conditionally renders buttons
- [ ] Power Apps can consume capabilities API
- [ ] React frontend example documented

---

## Rollout Plan

### Phase 1: API Authorization (Week 1-2)
1. Implement AccessRights and OperationAccessPolicy
2. Update DataverseAccessDataSource
3. Deploy to dev environment
4. Test with Postman/curl

### Phase 2: UI Capability Endpoints (Week 2)
1. Create permissions endpoints
2. Deploy to dev environment
3. Test with Postman
4. Document API for frontend team

### Phase 3: PCF Control Integration (Week 3)
1. Create PCF control specification
2. Implement PCF control (separate task or different dev)
3. Test with real users
4. Deploy to staging

### Phase 4: Production (Week 4)
1. Code review and QA
2. Load testing
3. Deploy to production
4. Monitor audit logs

---

## Dependencies & Blockers

### Upstream Dependencies
- None (foundational task)

### Downstream Dependencies
- **Task 2.1** (OboSpeService): Must apply correct authorization policies to file endpoints
- **Task 3.2** (SpeFileStore): Refactored operations must respect authorization
- **PCF Control Development**: Needs permissions API
- **All UI Development**: Needs capabilities API

### Potential Blockers
- Dataverse environment access required for testing
- Azure AD token generation for integration tests
- PCF control development may be separate sprint/team

---

## Completion Criteria

Task complete when:
1. ✅ All code implementation checked off
2. ✅ All tests passing (unit + integration)
3. ✅ Build succeeds with 0 errors
4. ✅ Manual validation completed (Task-1.1-Manual-Validation-Guide.md)
5. ✅ Code reviewed by senior developer
6. ✅ Permissions API documented in OpenAPI spec
7. ✅ PCF control specification created
8. ✅ Validation report generated

**Estimated Completion**: 8-10 days (increased from 5-8 days due to UI integration requirements)

---

**Task Owner**: [Assign developer]
**Reviewer**: [Assign senior developer]
**Started**: [Date]
**Completed**: [Date]

---

## Related Documents

- [Task 1.1 Validation Report](Task-1.1-Validation-Report.md)
- [Task 1.1 Manual Validation Guide](Task-1.1-Manual-Validation-Guide.md)
- [Contact Access Extension Analysis](../Contact-Access-Extension-Analysis.md)
- [ADR-003: Lean Authorization Seams](../../../docs/adr/ADR-003-lean-authorization-seams.md)
