# SDAP SPE File Access — Technical Review & Implementation Guide

**Date**: 2025-01-25
**Author**: Claude
**Status**: Ready for Implementation
**Related Issue**: FileAccessEndpoints returning 403 Access Denied

---

## Table of Contents
1. [Goals](#goals)
2. [Executive Summary](#executive-summary)
3. [Current State Analysis](#current-state-analysis)
4. [Existing OBO Implementation](#existing-obo-implementation)
5. [Why OBO Will Solve the Issue](#why-obo-will-solve-the-issue)
6. [Required Changes by File](#required-changes-by-file)
7. [Configuration & Permissions Checklist](#configuration--permissions-checklist)
8. [Testing Strategy](#testing-strategy)
9. [Risk Analysis & Rollback](#risk-analysis--rollback)

---

## Goals

### Primary Objectives

1. **Use delegated OBO for all user-initiated file operations**
   - User context authentication for preview, download, Office viewer
   - Preserve user identity for audit trails
   - Enforce user-level permissions (no manual container grants)

2. **Keep app-only authentication for background/admin tasks only**
   - Container creation
   - Background jobs
   - Webhook processors
   - Administrative operations

3. **Return actionable errors (RFC 7807 Problem Details)**
   - Structured error responses with `type`, `title`, `detail`, `status`
   - Include `correlationId` for debugging
   - Specific error codes (`invalid_id`, `mapping_missing_drive`, `obo_failed`, etc.)

4. **Validate SPE pointers before Graph API calls**
   - `driveId` must start with `"b!"` (SharePoint Embedded format)
   - `itemId` must be at least 20 characters (SharePoint Item ID format)
   - Return 409 Conflict if pointers missing or invalid

5. **Ensure PCF passes BFF audience token, not Graph token**
   - Token audience: `api://{BFF-AppId}/SDAP.Access` ✓
   - PCF uses MSAL.js to acquire BFF token
   - BFF performs OBO exchange to get Graph token

6. **Confirm Dataverse has both SPE pointer fields**
   - `sprk_graphdriveid` (e.g., `"b!abc123..."`)
   - `sprk_graphitemid` (e.g., `"01LBYCMX..."`)
   - Throw structured exception if either is missing

### Success Criteria

- ✅ User can preview/download files in containers they have access to
- ✅ User gets 403 Forbidden for containers they lack access to (expected behavior)
- ✅ Zero manual container permission grants required
- ✅ All errors return RFC 7807 Problem Details with correlation IDs
- ✅ No CS1593 compilation errors (method groups work correctly)
- ✅ PCF integration works end-to-end

---

## Executive Summary

### Problem

FileAccessEndpoints.cs is using **app-only (service principal) authentication** to access SharePoint Embedded containers, causing "Access denied" (403) errors.

**Error Details:**
- **Exception**: `Microsoft.Graph.Models.ODataErrors.ODataError: Access denied`
- **Location**: DriveItemOperations.cs:684
- **Correlation ID**: `947113c6-c645-4d32-aa81-8afcb1ae056d`
- **Document**: `ab1b0c34-52a5-f011-bbd3-7c1e5215b8b5`
- **SPE Item ID**: `01LBYCMX5QI2GQJSIJVBGYSRJILMDIITCI` ✓ (Retrieved successfully)
- **SPE Drive ID**: Unknown (but resolver is working)

### Root Cause

**Two Issues:**

1. **Authentication Pattern**: App-only auth requires manual container grants
2. **Missing Validation**: No SPE pointer validation before Graph calls

The endpoints use service principal authentication instead of user context. With `FileStorageContainer.Selected` permission, the service principal must be **explicitly granted access to each container** via PowerShell, which is not scalable.

### Solution

**Switch to On-Behalf-Of (OBO) flow** with **SPE pointer validation**:

1. ✅ **Extract user token** from `Authorization` header
2. ✅ **Validate SPE pointers** (driveId format, itemId length)
3. ✅ **Exchange for Graph token** using OBO
4. ✅ **Make Graph API call** with user's permissions
5. ✅ **Return structured errors** (RFC 7807)

**Benefits:**
- ✅ **Scalable** - No manual container permission grants
- ✅ **Secure** - Users only access their containers
- ✅ **Consistent** - Matches OBOEndpoints.cs pattern
- ✅ **Maintainable** - Zero admin overhead
- ✅ **Observable** - Correlation IDs in all errors

---

## Current State Analysis

### Current Error Flow

```
PCF Control (with user token: api://bff-api)
    ↓ Authorization: Bearer {user_token}
FileAccessEndpoints.GetPreviewUrl()
    ↓ IGNORES user token ❌
    ↓ No SPE pointer validation ❌
SpeFileStore.GetPreviewUrlAsync()
    ↓
DriveItemOperations.GetPreviewUrlAsync()
    ↓ var graphClient = _factory.CreateAppOnlyClient(); ❌
Graph API /drives/{driveId}/items/{itemId}/preview
    ↓ Uses service principal credentials
    ❌ Access denied - service principal not granted access to container
```

### Issues Identified

| Issue | Description | Impact |
|-------|-------------|--------|
| **No user context** | Using app-only auth instead of OBO | 403 Access Denied |
| **No pointer validation** | Missing driveId/itemId format checks | Confusing Graph API errors |
| **Generic errors** | Returning 500 with no correlation ID | Hard to debug |
| **Wrong token audience** | PCF might be requesting Graph token | OBO exchange fails |
| **Missing exception handling** | No global exception → problem mapping | Inconsistent error format |

---

## Existing OBO Implementation

### How OBO Works in SDAP

**Reference**: [SDAP-ARCHITECTURE-GUIDE-10-20-2025.md](c:\code_files\spaarke\docs\architecture\SDAP-ARCHITECTURE-GUIDE-10-20-2025.md)

The SDAP architecture **already implements OBO successfully** in OBOEndpoints.cs. We're applying the same proven pattern to FileAccessEndpoints.

### Complete OBO Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. PCF AUTHENTICATION (MSAL.js)                                │
└─────────────────────────────────────────────────────────────────┘
User logs into Dataverse/PowerApps
    ↓
MSAL.js acquires token:
    Scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access
    Audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c (BFF API)
    ✓ NOT Graph token - this is critical!

┌─────────────────────────────────────────────────────────────────┐
│ 2. API REQUEST                                                  │
└─────────────────────────────────────────────────────────────────┘
POST /api/documents/{documentId}/preview-url
Headers:
    Authorization: Bearer eyJ0eXAiOiJKV1QiLC... (BFF token, not Graph!)
    Content-Type: application/json

┌─────────────────────────────────────────────────────────────────┐
│ 3. ASP.NET CORE AUTHENTICATION MIDDLEWARE                      │
└─────────────────────────────────────────────────────────────────┘
Validates JWT:
    ✓ Audience = api://1e40baad-e065-4aea-a8d4-4b7ab273458c (BFF)
    ✓ Issuer = https://login.microsoftonline.com/{tenant}/v2.0
    ✓ Signature valid
    ✓ Not expired
    → Proceed to endpoint

┌─────────────────────────────────────────────────────────────────┐
│ 4. VALIDATE SPE POINTERS                                       │
└─────────────────────────────────────────────────────────────────┘
var (driveId, itemId) = await resolver.GetSpePointersAsync(docId, ct);

Validation:
    IF driveId is null or !driveId.StartsWith("b!")
        → Throw SdapProblemException("mapping_missing_drive", 409)
    IF itemId is null or itemId.Length < 20
        → Throw SdapProblemException("mapping_missing_item", 409)

┌─────────────────────────────────────────────────────────────────┐
│ 5. OBO TOKEN EXCHANGE (GraphClientFactory.ForUserAsync)        │
└─────────────────────────────────────────────────────────────────┘
var graph = await _factory.ForUserAsync(ctx, ct);

Internal:
    ├─ Extract user token from ctx.Request.Headers.Authorization
    ├─ Check Redis cache (SHA256 hash of user token)
    │   IF found → return cached GraphServiceClient (5ms)
    │
    └─ IF NOT found → Perform OBO exchange (200ms)
        POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
        Body:
            grant_type=urn:ietf:params:oauth2:grant-type:jwt-bearer
            client_id={BFF-AppId}
            client_secret={from KeyVault}
            assertion={user BFF token}
            requested_token_use=on_behalf_of
            scope=https://graph.microsoft.com/.default

        Response:
            access_token={Graph token with user context}

        Cache in Redis (TTL: 55 min)

┌─────────────────────────────────────────────────────────────────┐
│ 6. GRAPH API CALL (with user's permissions)                    │
└─────────────────────────────────────────────────────────────────┘
var preview = await graph.Drives[driveId].Items[itemId].Preview.PostAsync(...);

Graph API checks:
    ✓ Valid Graph token?
    ✓ What user? (from OBO token)
    ✓ Does THIS USER have access to THIS CONTAINER?
        → Check Azure AD group memberships
        → Check container permission grants
        → IF user has access → Return preview URL
        → IF user lacks access → 403 Forbidden (expected!)

┌─────────────────────────────────────────────────────────────────┐
│ 7. ERROR HANDLING (Global Exception Handler)                   │
└─────────────────────────────────────────────────────────────────┘
IF exception thrown:
    SdapProblemException → Return RFC 7807 with custom code/status
    MsalServiceException → Return 401 with "obo_failed" code
    ServiceException (Graph) → Return status from Graph API
    Other → Return 500 "server_error"

ALL errors include correlationId for tracing
```

### Existing Working Example: OBOEndpoints.cs

**Source**: [OBOEndpoints.cs:52-93](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\OBOEndpoints.cs#L52-L93)

```csharp
app.MapPut("/api/obo/containers/{id}/files/{*path}", async (
    string id, string path, HttpRequest req, HttpContext ctx,
    [FromServices] SpeFileStore speFileStore,
    [FromServices] ILogger<Program> logger,
    CancellationToken ct) =>
{
    try
    {
        // ✅ Extract user token from HttpContext
        var userToken = TokenHelper.ExtractBearerToken(ctx);

        // ✅ Pass to service layer (OBO happens internally)
        var item = await speFileStore.UploadSmallAsUserAsync(userToken, id, path, req.Body, ct);

        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }
    catch (UnauthorizedAccessException ex)
    {
        return TypedResults.Unauthorized();
    }
    catch (ODataError ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
}
```

**Why This Works:**
- Extracts user token from Authorization header
- Service layer performs OBO exchange
- User can only access containers they have permission to
- Returns structured errors

---

## Why OBO Will Solve the Issue

### Permission Model Comparison

#### App-Only (Current - Fails)

```
Service Principal:
    ├─ App Registration: FileStorageContainer.Selected ✓
    ├─ Admin Consent: Granted ✓
    │
    └─ Container Access: MANUAL grants required ❌
        For EACH container:
            Set-SPOFileStorageContainerPermission `
                -ContainerId {container-id} `
                -ApplicationId {service-principal-id} `
                -Role "write"

        Result: Access denied until admin grants access
        Scalability: POOR - manual intervention per container
```

#### OBO (Proposed - Will Work)

```
User Context:
    ├─ User: john.doe@contoso.com
    ├─ Azure AD Groups: ["Project Alpha Team", "All Employees"]
    │
    ├─ Container Permissions (Automatic):
    │   ├─ Container A → "Project Alpha Team" has Write ✓
    │   │   → User HAS ACCESS (via group membership)
    │   │
    │   └─ Container B → "Project Beta Team" has Write
    │       → User NO ACCESS (403 - expected behavior)
    │
    └─ OBO Exchange:
        BFF token → Exchange → Graph token (user context)
        Graph API checks: Does THIS USER have access?
        Result: User accesses containers they have permission to
        Scalability: EXCELLENT - automatic via Azure AD
```

---

## Required Changes by File

### 1. Program.cs

#### Change 1.1: Add Global Exception Handler

**Location**: After `app.UseAuthentication()` and before `app.MapEndpoints()`

```csharp
// Global exception handler - converts exceptions to RFC 7807 Problem Details
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exceptionFeature = ctx.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;
        var traceId = ctx.TraceIdentifier;

        var (status, code, title, detail) = exception switch
        {
            // Custom SDAP exceptions with specific codes
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail),

            // MSAL OBO failures
            MsalServiceException ms => (401, "obo_failed", "OBO token acquisition failed", ms.Message),

            // Graph API errors
            ServiceException gs when gs.ResponseStatusCode > 0
                => (gs.ResponseStatusCode, "graph_error", "Graph API error", gs.Message),

            // Unauthorized access
            UnauthorizedAccessException => (401, "unauthorized", "Unauthorized", "Missing or invalid authorization"),

            // Argument validation
            ArgumentException arg => (400, "invalid_argument", "Invalid argument", arg.Message),

            // Not found
            KeyNotFoundException => (404, "not_found", "Resource not found", exception.Message),

            // Fallback for unknown errors
            _ => (500, "server_error", "Internal Server Error", "An unexpected error occurred")
        };

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://tools.ietf.org/html/rfc7231#section-6.{(status / 100)}.{(status % 100)}",
            title,
            detail,
            status,
            extensions = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = traceId
            }
        });

        // Log the exception
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "[{CorrelationId}] Unhandled exception: {Code} - {Message}",
            traceId, code, exception?.Message);
    });
});
```

#### Change 1.2: Update DI Registration

**Current:**
```csharp
// Register GraphServiceClient for minimal API endpoint injection (app-only authentication)
builder.Services.AddScoped<Microsoft.Graph.GraphServiceClient>(sp =>
{
    var factory = sp.GetRequiredService<IGraphClientFactory>();
    return factory.CreateAppOnlyClient();  // ❌ Remove this
});
```

**New:**
```csharp
// Register only the factory - endpoints create clients per-request with appropriate auth
builder.Services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

// Note: No scoped GraphServiceClient registration
// Endpoints use factory.ForUserAsync(ctx) for OBO or factory.ForApp() for admin ops
```

---

### 2. IGraphClientFactory Interface

**File**: `src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs`

**Current:**
```csharp
public interface IGraphClientFactory
{
    GraphServiceClient CreateAppOnlyClient();
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);
}
```

**New (with HttpContext parameter):**
```csharp
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow for user context operations.
    /// Extracts user token from HttpContext.Request.Headers.Authorization.
    /// Uses Redis caching to reduce Azure AD load.
    /// </summary>
    /// <param name="ctx">HttpContext containing Authorization header with user token</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>GraphServiceClient authenticated with user's permissions</returns>
    Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Creates Graph client using app-only authentication (service principal).
    /// For background tasks, admin operations, and container management only.
    /// </summary>
    /// <returns>GraphServiceClient authenticated with application permissions</returns>
    GraphServiceClient ForApp();
}
```

**Implementation in GraphClientFactory.cs:**

```csharp
public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
{
    // Extract user token from Authorization header
    var userToken = TokenHelper.ExtractBearerToken(ctx);

    // Delegate to existing OBO implementation
    return await CreateOnBehalfOfClientAsync(userToken);
}

public GraphServiceClient ForApp()
{
    // Delegate to existing app-only implementation
    return CreateAppOnlyClient();
}
```

**Rationale**: Simpler API for endpoint handlers - they just pass HttpContext instead of manually extracting tokens.

---

### 3. SdapProblemException Class (New)

**File**: `src/api/Spe.Bff.Api/Infrastructure/Errors/SdapProblemException.cs` (new file)

```csharp
namespace Spe.Bff.Api.Infrastructure.Errors;

/// <summary>
/// Custom exception that maps to RFC 7807 Problem Details.
/// Thrown when validation fails or business rules are violated.
/// </summary>
public class SdapProblemException : Exception
{
    public string Code { get; }
    public string Title { get; }
    public string Detail { get; }
    public int StatusCode { get; }

    public SdapProblemException(
        string code,
        string title,
        string detail,
        int statusCode = 400)
        : base(detail)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        StatusCode = statusCode;
    }
}
```

**Usage Examples:**

```csharp
// Invalid document ID format
if (!Guid.TryParse(documentId, out var docId))
    throw new SdapProblemException(
        code: "invalid_id",
        title: "Invalid document ID",
        detail: "Document ID must be a valid GUID (Dataverse primary key). Do not use SharePoint Item IDs.",
        statusCode: 400);

// Missing SPE pointer - driveId
if (string.IsNullOrWhiteSpace(driveId) || !driveId.StartsWith("b!"))
    throw new SdapProblemException(
        code: "mapping_missing_drive",
        title: "Storage mapping incomplete",
        detail: "DriveId is missing or invalid. Document record must have sprk_graphdriveid field populated with SharePoint Embedded drive ID (format: b!...).",
        statusCode: 409);

// Missing SPE pointer - itemId
if (string.IsNullOrWhiteSpace(itemId) || itemId.Length < 20)
    throw new SdapProblemException(
        code: "mapping_missing_item",
        title: "Storage mapping incomplete",
        detail: "ItemId is missing or invalid. Document record must have sprk_graphitemid field populated with SharePoint item ID.",
        statusCode: 409);

// Preview unavailable
if (string.IsNullOrEmpty(previewUrl))
    throw new SdapProblemException(
        code: "preview_unavailable",
        title: "Preview unavailable",
        detail: "Graph API did not return a preview URL. File may not support preview or may be corrupted.",
        statusCode: 404);
```

---

### 4. FileAccessEndpoints.cs

**File**: `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`

#### Change 4.1: Update MapFileAccessEndpoints

**Current:**
```csharp
public static IEndpointRouteBuilder MapFileAccessEndpoints(this IEndpointRouteBuilder app)
{
    var docs = app.MapGroup("/api/documents")
        .RequireAuthorization();

    // Endpoints map directly (method groups - already correct)
    docs.MapGet("/{documentId}/preview-url", GetPreviewUrl)
        .WithName("GetDocumentPreviewUrl");

    docs.MapGet("/{documentId}/preview", GetPreview)
        .WithName("GetDocumentPreview");

    docs.MapGet("/{documentId}/content", GetContent)
        .WithName("GetDocumentContent");

    docs.MapGet("/{documentId}/office", GetOffice)
        .WithName("GetDocumentOfficeViewer");

    return app;
}
```

**Add Optional Diagnostics Endpoint:**

```csharp
#if DEBUG
    // Diagnostics endpoint for checking SPE pointer mapping
    docs.MapGet("/{documentId}/_diagnostics/pointers", async (
        string documentId,
        IDocumentStorageResolver resolver,
        CancellationToken ct) =>
    {
        if (!Guid.TryParse(documentId, out var id))
            return Results.BadRequest(new { code = "invalid_id", message = "Invalid document ID format" });

        var (driveId, itemId) = await resolver.GetSpePointersAsync(id, ct);

        return Results.Ok(new
        {
            documentId,
            driveId,
            itemId,
            driveIdValid = !string.IsNullOrWhiteSpace(driveId) && driveId.StartsWith("b!"),
            itemIdValid = !string.IsNullOrWhiteSpace(itemId) && itemId.Length >= 20
        });
    })
    .WithName("DiagnosticsGetSpePointers")
    .WithTags("Diagnostics");
#endif
```

#### Change 4.2: Refactor GetPreviewUrl Endpoint

**Current:**
```csharp
static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    SpeFileStore speFileStore,  // ❌ Uses app-only internally
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    // Missing: user token extraction
    // Missing: SPE pointer validation
    // Uses: SpeFileStore (app-only)
}
```

**New:**
```csharp
/// <summary>
/// GET /api/documents/{documentId}/preview-url
/// Server-side proxy for Custom API: Returns ephemeral preview URL with UAC validation.
/// This endpoint is called by the GetFilePreviewUrlPlugin via Custom API.
/// </summary>
static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver resolver,
    SpeFileStore store,
    ILogger<FileAccessEndpoints> log,  // ✅ Changed from ILogger<Program>
    HttpContext ctx,
    CancellationToken ct)
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? ctx.TraceIdentifier;

    log.LogInformation("[{CorrelationId}] Getting preview URL for document {DocumentId}",
        correlationId, documentId);

    // STEP 1: Validate GUID format
    if (!Guid.TryParse(documentId, out var docId))
    {
        throw new SdapProblemException(
            code: "invalid_id",
            title: "Invalid document ID",
            detail: "Document ID must be a valid GUID (Dataverse primary key). Do not use SharePoint Item IDs.",
            statusCode: 400);
    }

    // STEP 2: Resolve Document GUID → SPE pointers (DriveId, ItemId)
    var (driveId, itemId) = await resolver.GetSpePointersAsync(docId, ct);

    // STEP 3: Validate SPE pointers
    if (string.IsNullOrWhiteSpace(driveId) || !driveId.StartsWith("b!"))
    {
        log.LogWarning("[{CorrelationId}] Document {DocumentId} has missing or invalid DriveId: '{DriveId}'",
            correlationId, documentId, driveId);

        throw new SdapProblemException(
            code: "mapping_missing_drive",
            title: "Storage mapping incomplete",
            detail: "DriveId is missing or invalid. Document record must have sprk_graphdriveid field populated with SharePoint Embedded drive ID (format: b!...).",
            statusCode: 409);
    }

    if (string.IsNullOrWhiteSpace(itemId) || itemId.Length < 20)
    {
        log.LogWarning("[{CorrelationId}] Document {DocumentId} has missing or invalid ItemId: '{ItemId}'",
            correlationId, documentId, itemId);

        throw new SdapProblemException(
            code: "mapping_missing_item",
            title: "Storage mapping incomplete",
            detail: "ItemId is missing or invalid. Document record must have sprk_graphitemid field populated with SharePoint item ID.",
            statusCode: 409);
    }

    log.LogDebug("[{CorrelationId}] SPE pointers validated - DriveId: {DriveId}, ItemId: {ItemId}",
        correlationId, driveId, itemId);

    // STEP 4: Get preview URL using OBO (SpeFileStore extracts user token from HttpContext)
    var preview = await store.GetPreviewUrlAsync(driveId, itemId, ctx, ct);

    log.LogInformation("[{CorrelationId}] Preview URL retrieved successfully for {DocumentId}",
        correlationId, documentId);

    // STEP 5: Return structured response
    return TypedResults.Ok(new
    {
        data = preview,
        metadata = new
        {
            correlationId,
            timestamp = DateTimeOffset.UtcNow
        }
    });
}
```

**Key Changes:**
1. ✅ Validates document ID is GUID
2. ✅ Validates `driveId` starts with `"b!"`
3. ✅ Validates `itemId` is at least 20 characters
4. ✅ Throws `SdapProblemException` with specific codes
5. ✅ Passes `HttpContext` to SpeFileStore (for OBO)
6. ✅ Returns structured response with correlationId

#### Change 4.3: Apply Same Pattern to Other Endpoints

**GetPreview, GetContent, GetOffice** - Apply same validation pattern:

```csharp
static async Task<IResult> GetPreview(
    string documentId,
    IDocumentStorageResolver resolver,
    SpeFileStore store,
    ILogger<FileAccessEndpoints> log,
    HttpContext ctx,
    CancellationToken ct)
{
    // STEP 1: Validate GUID
    if (!Guid.TryParse(documentId, out var docId))
        throw new SdapProblemException("invalid_id", "Invalid document ID", "Document ID must be a GUID.", 400);

    // STEP 2: Resolve SPE pointers
    var (driveId, itemId) = await resolver.GetSpePointersAsync(docId, ct);

    // STEP 3: Validate SPE pointers
    if (string.IsNullOrWhiteSpace(driveId) || !driveId.StartsWith("b!"))
        throw new SdapProblemException("mapping_missing_drive", "Storage mapping incomplete", "DriveId missing or invalid.", 409);

    if (string.IsNullOrWhiteSpace(itemId) || itemId.Length < 20)
        throw new SdapProblemException("mapping_missing_item", "Storage mapping incomplete", "ItemId missing or invalid.", 409);

    // STEP 4: Get preview URL using OBO
    var preview = await store.GetPreviewAsync(driveId, itemId, ctx, ct);

    // STEP 5: Return response
    return TypedResults.Ok(new
    {
        data = preview,
        metadata = new { correlationId = ctx.TraceIdentifier }
    });
}
```

**GetOffice** keeps the `mode` parameter:

```csharp
static async Task<IResult> GetOffice(
    string documentId,
    string? mode,  // "view" or "edit"
    IDocumentStorageResolver resolver,
    SpeFileStore store,
    ILogger<FileAccessEndpoints> log,
    HttpContext ctx,
    CancellationToken ct)
{
    // Same validation pattern...
    // ...

    // STEP 4: Get Office viewer URL
    var officeUrl = await store.GetOfficeViewerUrlAsync(driveId, itemId, mode, ctx, ct);

    return TypedResults.Ok(new
    {
        data = officeUrl,
        metadata = new { correlationId = ctx.TraceIdentifier }
    });
}
```

---

### 5. SpeFileStore.cs

**File**: `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

#### Change 5.1: Update GetPreviewUrlAsync Method Signature

**Current:**
```csharp
public async Task<FilePreviewDto> GetPreviewUrlAsync(
    string driveId,
    string itemId,
    string? correlationId = null,
    CancellationToken ct = default)
{
    // Uses app-only client ❌
}
```

**New:**
```csharp
/// <summary>
/// Get preview URL for a SharePoint Embedded file using user context (OBO).
/// </summary>
/// <param name="driveId">SharePoint Embedded drive ID (format: b!...)</param>
/// <param name="itemId">SharePoint item ID</param>
/// <param name="ctx">HttpContext for extracting user token</param>
/// <param name="ct">Cancellation token</param>
/// <returns>Preview URL and metadata</returns>
public async Task<PreviewHandle> GetPreviewUrlAsync(
    string driveId,
    string itemId,
    HttpContext ctx,
    CancellationToken ct = default)
{
    // Extract correlation ID from context
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? ctx.TraceIdentifier;

    _logger.LogInformation("[{CorrelationId}] Getting preview URL for {DriveId}/{ItemId} (OBO)",
        correlationId, driveId, itemId);

    // ✅ Create Graph client with user context (OBO)
    var graph = await _factory.ForUserAsync(ctx, ct);

    // Call Graph API preview endpoint
    var previewRequest = new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody();
    var resp = await graph.Drives[driveId]
        .Items[itemId]
        .Preview
        .PostAsync(previewRequest, cancellationToken: ct);

    // Extract preview URL from response
    var url = resp?.GetAdditionalData()?["getUrl"] as string
           ?? resp?.GetAdditionalData()?["@microsoft.graph.downloadUrl"] as string
           ?? resp?.GetUrl;

    if (string.IsNullOrEmpty(url))
    {
        _logger.LogWarning("[{CorrelationId}] Preview URL not returned for {DriveId}/{ItemId}",
            correlationId, driveId, itemId);

        throw new SdapProblemException(
            code: "preview_unavailable",
            title: "Preview unavailable",
            detail: "Graph API did not return a preview URL. File may not support preview or may be corrupted.",
            statusCode: 404);
    }

    _logger.LogInformation("[{CorrelationId}] Preview URL retrieved successfully",
        correlationId);

    return new PreviewHandle(
        Url: url,
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10), // Preview URLs typically expire in 10 min
        ContentType: null
    );
}
```

**Key Changes:**
1. ✅ Parameter changed from `string? correlationId` to `HttpContext ctx`
2. ✅ Uses `_factory.ForUserAsync(ctx, ct)` for OBO
3. ✅ Extracts correlation ID from HttpContext
4. ✅ Throws `SdapProblemException` if preview URL not returned

#### Change 5.2: Update Other Methods (Same Pattern)

Apply same pattern to:
- `GetPreviewAsync(...)` → Add `HttpContext ctx` parameter, use OBO
- `GetContentAsync(...)` → Add `HttpContext ctx` parameter, use OBO
- `GetOfficeViewerUrlAsync(...)` → Add `HttpContext ctx` parameter, use OBO

#### Change 5.3: Keep App-Only Methods for Background Tasks

**No changes needed** - keep existing app-only methods for:
- Container creation (`CreateContainerAsync`)
- Background jobs
- Webhook processors

**Pattern**: Separate method names for clarity:
- `GetPreviewUrlAsync(HttpContext ctx, ...)` → OBO (user context)
- `GetPreviewUrlAppOnlyAsync(string correlationId, ...)` → App-only (admin/background)

---

### 6. IDocumentStorageResolver.cs

**File**: `src/api/Spe.Bff.Api/Infrastructure/Dataverse/IDocumentStorageResolver.cs`

**Current:**
```csharp
public interface IDocumentStorageResolver
{
    Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
```

**Implementation Must:**

1. ✅ Query Dataverse for `sprk_graphdriveid` and `sprk_graphitemid`
2. ✅ Throw `SdapProblemException` if either field is missing or invalid
3. ✅ Return tuple `(driveId, itemId)`

**Implementation Example:**

```csharp
public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(
    Guid documentId,
    CancellationToken cancellationToken = default)
{
    // Query Dataverse for document record
    var document = await _dataverseService.GetDocumentAsync(documentId, cancellationToken);

    if (document == null)
    {
        throw new KeyNotFoundException($"Document {documentId} not found in Dataverse");
    }

    var driveId = document.GraphDriveId;
    var itemId = document.GraphItemId;

    // Validate driveId
    if (string.IsNullOrWhiteSpace(driveId) || !driveId.StartsWith("b!"))
    {
        _logger.LogWarning("Document {DocumentId} has missing or invalid sprk_graphdriveid: '{DriveId}'",
            documentId, driveId);

        throw new SdapProblemException(
            code: "mapping_missing_drive",
            title: "Storage mapping incomplete",
            detail: $"Document {documentId} does not have sprk_graphdriveid field populated. Ensure file upload completed successfully.",
            statusCode: 409);
    }

    // Validate itemId
    if (string.IsNullOrWhiteSpace(itemId) || itemId.Length < 20)
    {
        _logger.LogWarning("Document {DocumentId} has missing or invalid sprk_graphitemid: '{ItemId}'",
            documentId, itemId);

        throw new SdapProblemException(
            code: "mapping_missing_item",
            title: "Storage mapping incomplete",
            detail: $"Document {documentId} does not have sprk_graphitemid field populated. Ensure file upload completed successfully.",
            statusCode: 409);
    }

    return (driveId, itemId);
}
```

---

## Configuration & Permissions Checklist

### Azure AD App Registration (BFF API)

#### Delegated Permissions (For OBO)

- [ ] **Files.ReadWrite.All** (Delegated)
- [ ] **Sites.FullControl.All** (Delegated)
- [ ] **FileStorageContainer.Selected** (Delegated)
- [ ] **Admin consent granted** for all delegated permissions

**Verify:**
```bash
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
    --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[?type=='Scope']"
```

Expected:
```json
[
  { "id": "df85f4d6-205c-4ac5-a5ea-6bf408dba283", "type": "Scope" },  // Files.ReadWrite.All
  { "id": "8e6ec84c-5fcd-4cc7-ac8a-2296efc0ed9b", "type": "Scope" },  // Sites.FullControl.All
  { "id": "527b6d64-cdf5-4b8b-b336-4aa0b8ca2ce5", "type": "Scope" }   // FileStorageContainer.Selected
]
```

#### Application Permissions (For Background Tasks)

- [ ] **Files.ReadWrite.All** (Application) - For background jobs only
- [ ] **Sites.FullControl.All** (Application) - For container creation only
- [ ] **Admin consent granted** for all application permissions

**Important**: Use certificate-based credentials for app-only operations (not client secret).

### PCF Control Configuration

#### Token Acquisition

- [ ] **MSAL.js configured** to request BFF token, **NOT** Graph token
- [ ] **Scope**: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access`
- [ ] **Audience validation**: Token has `aud = api://1e40baad-e065-4aea-a8d4-4b7ab273458c`

**PCF Code to Verify:**

```typescript
// ✅ CORRECT - Request BFF token
const request = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access"]
};
const authResult = await msalInstance.acquireTokenSilent(request);
const bffToken = authResult.accessToken;

// Send to BFF API
const response = await fetch(`${bffApiUrl}/api/documents/${documentId}/preview-url`, {
    headers: {
        "Authorization": `Bearer ${bffToken}`  // BFF token, not Graph!
    }
});
```

```typescript
// ❌ WRONG - Do NOT request Graph token
const wrongRequest = {
    scopes: ["https://graph.microsoft.com/.default"]  // ❌ This is wrong!
};
```

### Dataverse Configuration

#### Document Entity Schema

- [ ] **Field**: `sprk_graphdriveid` (Text, required)
  - Format: `"b!abc123..."`
  - Example: `"b!dGVzdC1kcml2ZS1pZA"`

- [ ] **Field**: `sprk_graphitemid` (Text, required)
  - Format: SharePoint item ID (>= 20 chars)
  - Example: `"01LBYCMX5QI2GQJSIJVBGYSRJILMDIITCI"`

**Verify Test Record:**
```bash
# Query test document
curl -X GET "https://{org}.crm.dynamics.com/api/data/v9.2/sprk_documents(ab1b0c34-52a5-f011-bbd3-7c1e5215b8b5)" \
  -H "Authorization: Bearer {token}" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0"
```

Expected response:
```json
{
    "sprk_documentid": "ab1b0c34-52a5-f011-bbd3-7c1e5215b8b5",
    "sprk_graphdriveid": "b!dGVzdC1kcml2ZS1pZA",
    "sprk_graphitemid": "01LBYCMX5QI2GQJSIJVBGYSRJILMDIITCI",
    ...
}
```

---

## Testing Strategy

### Unit Tests

**File**: `FileAccessEndpoints.Tests.cs` (new)

```csharp
[Fact]
public async Task GetPreviewUrl_WithValidPointers_ReturnsPreviewUrl()
{
    // Arrange
    var mockFactory = new Mock<IGraphClientFactory>();
    var mockResolver = new Mock<IDocumentStorageResolver>();
    var mockStore = new Mock<SpeFileStore>();

    var documentId = Guid.NewGuid();
    var driveId = "b!abc123";
    var itemId = "01LBYCMX5QI2GQJSIJVBGYSRJILMDIITCI";

    mockResolver
        .Setup(r => r.GetSpePointersAsync(documentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync((driveId, itemId));

    var previewUrl = "https://contoso.sharepoint.com/preview/...";
    mockStore
        .Setup(s => s.GetPreviewUrlAsync(driveId, itemId, It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PreviewHandle(previewUrl, DateTimeOffset.UtcNow.AddMinutes(10), null));

    var httpContext = new DefaultHttpContext();
    httpContext.Request.Headers["Authorization"] = "Bearer valid-token";

    // Act
    var result = await FileAccessEndpoints.GetPreviewUrl(
        documentId.ToString(),
        mockResolver.Object,
        mockStore.Object,
        Mock.Of<ILogger<FileAccessEndpoints>>(),
        httpContext,
        CancellationToken.None);

    // Assert
    var okResult = Assert.IsType<Ok<object>>(result);
    // Verify structure...
}

[Fact]
public async Task GetPreviewUrl_WithInvalidDriveId_ThrowsSdapProblemException()
{
    // Arrange
    var mockResolver = new Mock<IDocumentStorageResolver>();
    mockResolver
        .Setup(r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(("invalid-drive-id", "01LBYCMX..."));  // ❌ Doesn't start with "b!"

    var httpContext = new DefaultHttpContext();

    // Act & Assert
    var ex = await Assert.ThrowsAsync<SdapProblemException>(() =>
        FileAccessEndpoints.GetPreviewUrl(
            Guid.NewGuid().ToString(),
            mockResolver.Object,
            Mock.Of<SpeFileStore>(),
            Mock.Of<ILogger<FileAccessEndpoints>>(),
            httpContext,
            CancellationToken.None));

    Assert.Equal("mapping_missing_drive", ex.Code);
    Assert.Equal(409, ex.StatusCode);
}

[Fact]
public async Task GetPreviewUrl_WithMissingItemId_ThrowsSdapProblemException()
{
    // Arrange
    var mockResolver = new Mock<IDocumentStorageResolver>();
    mockResolver
        .Setup(r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(("b!abc123", "short"));  // ❌ ItemId too short

    var httpContext = new DefaultHttpContext();

    // Act & Assert
    var ex = await Assert.ThrowsAsync<SdapProblemException>(() =>
        FileAccessEndpoints.GetPreviewUrl(
            Guid.NewGuid().ToString(),
            mockResolver.Object,
            Mock.Of<SpeFileStore>(),
            Mock.Of<ILogger<FileAccessEndpoints>>(),
            httpContext,
            CancellationToken.None));

    Assert.Equal("mapping_missing_item", ex.Code);
    Assert.Equal(409, ex.StatusCode);
}
```

### Integration Tests

**Prerequisites:**
- Real Azure AD tenant
- Test user with access to test SPE container
- Valid BFF token (not Graph token!)

```csharp
[Fact]
public async Task GetPreviewUrl_WithRealUserToken_ReturnsPreviewUrl()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();

    // Get REAL user token from test configuration
    var bffToken = TestConfiguration.GetBffUserToken();  // api://bff-api/SDAP.Access
    var testDocumentId = TestConfiguration.GetTestDocumentId();

    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", bffToken);

    // Act
    var response = await client.GetAsync($"/api/documents/{testDocumentId}/preview-url");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var content = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<JsonElement>(content);

    Assert.True(result.TryGetProperty("data", out var data));
    Assert.True(data.TryGetProperty("url", out var url));
    Assert.StartsWith("https://", url.GetString());

    Assert.True(result.TryGetProperty("metadata", out var metadata));
    Assert.True(metadata.TryGetProperty("correlationId", out _));
}
```

### Manual Testing Checklist

**Pre-deployment:**

- [ ] Build succeeds: `dotnet build`
- [ ] Unit tests pass: `dotnet test`
- [ ] No compilation errors

**Post-deployment:**

- [ ] Health endpoint: `GET /health` returns 200 OK
- [ ] Diagnostics endpoint (DEBUG): `GET /api/documents/{id}/_diagnostics/pointers` shows valid pointers
- [ ] Preview URL endpoint: `GET /api/documents/{id}/preview-url` returns 200 with previewUrl
- [ ] Preview endpoint: `GET /api/documents/{id}/preview` returns iframe-embeddable URL
- [ ] Content endpoint: `GET /api/documents/{id}/content` returns download URL
- [ ] Office endpoint: `GET /api/documents/{id}/office?mode=view` returns Office web viewer URL

**Error Scenarios:**

- [ ] Missing token → 401 Unauthorized with `code: "unauthorized"`
- [ ] Invalid document ID → 400 Bad Request with `code: "invalid_id"`
- [ ] Missing driveId → 409 Conflict with `code: "mapping_missing_drive"`
- [ ] Missing itemId → 409 Conflict with `code: "mapping_missing_item"`
- [ ] User lacks access → 403 Forbidden with `code: "graph_error"`

**Example Curl Commands:**

```bash
# Get BFF token (from PCF or test harness)
BFF_TOKEN="Bearer eyJ0eXAiOiJKV1QiLC..."  # api://bff-api/SDAP.Access
DOC_ID="ab1b0c34-52a5-f011-bbd3-7c1e5215b8b5"

# Test preview URL
curl -v -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/$DOC_ID/preview-url" \
  -H "Authorization: $BFF_TOKEN"

# Expected: 200 OK with JSON:
# {
#   "data": { "url": "https://...", "expiresAt": "2025-..." },
#   "metadata": { "correlationId": "abc123..." }
# }

# Test diagnostics
curl -v -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/$DOC_ID/_diagnostics/pointers" \
  -H "Authorization: $BFF_TOKEN"

# Expected: 200 OK with:
# {
#   "documentId": "ab1b0c34-...",
#   "driveId": "b!abc123...",
#   "itemId": "01LBYCMX...",
#   "driveIdValid": true,
#   "itemIdValid": true
# }

# Test error - invalid ID
curl -v -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/not-a-guid/preview-url" \
  -H "Authorization: $BFF_TOKEN"

# Expected: 400 Bad Request with:
# {
#   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
#   "title": "Invalid document ID",
#   "detail": "Document ID must be a valid GUID...",
#   "status": 400,
#   "extensions": {
#     "code": "invalid_id",
#     "correlationId": "xyz789..."
#   }
# }
```

---

## Risk Analysis & Rollback

### High Risk Areas

#### Risk 1: PCF Sending Wrong Token Audience

**Description**: If PCF requests Graph token instead of BFF token, OBO exchange will fail.

**Detection**:
```bash
# Check token in browser dev tools
# Decode JWT at https://jwt.ms
# Verify: "aud": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" (BFF)
# NOT: "aud": "https://graph.microsoft.com" (Graph)
```

**Mitigation**:
1. Verify PCF MSAL configuration requests BFF scope
2. Add logging to decode token audience in TokenHelper
3. Return clear error message if audience is Graph

**Fix**:
```typescript
// PCF - src/services/MsalAuthProvider.ts
const request = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access"]  // BFF, not Graph!
};
```

#### Risk 2: Missing SPE Pointers in Dataverse

**Description**: If test documents don't have `sprk_graphdriveid` or `sprk_graphitemid`, endpoints will return 409.

**Detection**: Use diagnostics endpoint to check pointers before testing.

**Mitigation**:
1. Use diagnostics endpoint: `GET /api/documents/{id}/_diagnostics/pointers`
2. Verify test document has both fields populated
3. Upload new file if needed

**Fix**: Upload file via PCF control to ensure pointers are populated.

### Medium Risk Areas

#### Risk 3: OBO Token Exchange Failures

**Description**: MSAL OBO exchange could fail due to misconfiguration.

**Detection**: Check logs for `MsalServiceException`.

**Mitigation**:
1. Verify delegated permissions in Azure AD
2. Verify admin consent granted
3. Test with existing OBOEndpoints first

#### Risk 4: Performance Impact from Validation

**Description**: SPE pointer validation adds minimal overhead (~1ms).

**Mitigation**: Acceptable overhead for improved error messages.

**Baseline**: OBO exchange already adds 5ms (cached) or 200ms (uncached).

### Rollback Plan

**Immediate Rollback:**

1. Revert code changes via Git:
   ```bash
   cd c:/code_files/spaarke
   git log --oneline -5
   git revert {commit-hash}
   ```

2. Rebuild and redeploy:
   ```bash
   cd src/api/Spe.Bff.Api
   dotnet publish -c Release -o ./publish
   tar -czf rollback.zip -C publish .
   az webapp deploy --resource-group spe-infrastructure-westus2 \
     --name spe-api-dev-67e2xz --src-path rollback.zip --type zip
   ```

3. Temporary workaround: Manually grant container access (if needed for urgent fix).

**Time to rollback**: ~10 minutes

---

## Success Criteria

### Deployment Success

- [ ] Build completes without errors
- [ ] All unit tests pass
- [ ] Deployment to Azure succeeds
- [ ] Health endpoint returns 200 OK
- [ ] No unhandled exceptions in Application Insights

### Functional Success

- [ ] User with container access gets preview URL (200 OK)
- [ ] User without container access gets 403 Forbidden (expected)
- [ ] Invalid document ID returns 400 with `code: "invalid_id"`
- [ ] Missing driveId returns 409 with `code: "mapping_missing_drive"`
- [ ] Missing itemId returns 409 with `code: "mapping_missing_item"`
- [ ] All errors include `correlationId` for debugging

### Performance Success

- [ ] Average request latency < 300ms (with Redis cache)
- [ ] 95th percentile latency < 500ms
- [ ] OBO token cache hit rate > 90%

### Business Success

- [ ] PCF control file preview works end-to-end
- [ ] Zero manual container permission grants needed
- [ ] Users can view files immediately after upload
- [ ] Clear error messages help users/admins troubleshoot issues

---

## Conclusion

This refactor addresses all the goals:

1. ✅ **Delegated OBO for user operations** - Scalable, secure
2. ✅ **App-only for background tasks** - Preserved where appropriate
3. ✅ **RFC 7807 Problem Details** - Structured errors with correlation IDs
4. ✅ **SPE pointer validation** - Catch mapping issues early
5. ✅ **PCF token audience verified** - BFF token, not Graph
6. ✅ **Dataverse field requirements** - Both pointer fields validated

**Estimated Implementation Time**: 3-4 hours (coding + testing + deployment)

**Next Steps**:
1. ✅ Create SdapProblemException class
2. ✅ Add global exception handler to Program.cs
3. ✅ Update IGraphClientFactory interface
4. ✅ Refactor FileAccessEndpoints.cs (4 endpoints)
5. ✅ Update SpeFileStore.cs methods
6. ✅ Add diagnostics endpoint (DEBUG only)
7. ✅ Build and test locally
8. ✅ Deploy to dev environment
9. ✅ Validate with PCF integration test
10. ✅ Monitor Application Insights for errors

---

**Document Version**: 2.0
**Last Updated**: 2025-01-25
**Approval Status**: Ready for Implementation
**Reviewed By**: [Pending]
