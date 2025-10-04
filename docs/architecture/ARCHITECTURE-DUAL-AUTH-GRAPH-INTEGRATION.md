# Architecture: Dual Authentication Graph Integration

**Document Type:** Architecture Design Record (ADR)
**Status:** ✅ Implemented & Production-Ready
**Last Updated:** October 3, 2025
**Related ADRs:** ADR-007 (SPE Storage Seam Minimalism)
**Sprint:** 4 (Task 4.4)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [The Dual Authentication Problem](#the-dual-authentication-problem)
3. [Architecture Overview](#architecture-overview)
4. [Core Components](#core-components)
5. [Authentication Flows](#authentication-flows)
6. [Design Patterns](#design-patterns)
7. [Code Examples](#code-examples)
8. [Developer Guidelines](#developer-guidelines)
9. [Testing Strategy](#testing-strategy)
10. [Troubleshooting](#troubleshooting)

---

## Executive Summary

### What This Document Covers

This document explains SDAP's **dual authentication architecture** for integrating with Microsoft Graph and SharePoint Embedded (SPE). It describes how we support both **app-only** (Managed Identity) and **user-context** (On-Behalf-Of) operations through a single unified facade.

### Key Architectural Decisions

1. **Single Facade Pattern** - `SpeFileStore` is the only entry point for all Graph operations
2. **Dual Authentication Modes** - All operation classes support both MI and OBO flows
3. **No Interface Abstractions** - Direct concrete class usage (ADR-007 compliant)
4. **DTO-Only Exposure** - Graph SDK types never leak through public APIs
5. **Modular Operation Classes** - Specialized classes for containers, files, uploads, users

### For Developers

**Quick Start:**
- Use `SpeFileStore` for all Graph/SPE operations
- Call `*Async(...)` methods for app-only operations (admin/platform tasks)
- Call `*AsUserAsync(userToken, ...)` methods for user-context operations
- Always inject `SpeFileStore`, never the underlying operation classes
- Never expose `DriveItem`, `FileStorageContainer`, or other Graph SDK types

---

## The Dual Authentication Problem

### Why Two Authentication Modes?

SDAP needs to perform two fundamentally different types of operations:

#### 1. App-Only Operations (Platform/Admin Tasks)

**Use Case:** Background jobs, system-level container management, administrative operations

**Authentication:** Azure Managed Identity (client credentials flow)

**Security Context:** The application acts as itself, with elevated privileges

**Example Scenarios:**
- Creating containers for new projects
- Processing document events in background workers
- Bulk operations across multiple containers
- System maintenance and cleanup tasks

**Security Characteristic:** ✅ Bypasses user permissions (by design - admin operations)

#### 2. User-Context Operations (OBO - On-Behalf-Of)

**Use Case:** User-initiated file operations through the UI

**Authentication:** Azure AD OBO flow (user token exchange)

**Security Context:** The application acts on behalf of the signed-in user

**Example Scenarios:**
- User uploads their own file
- User downloads a document
- User renames/moves a file
- User checks their permissions

**Security Characteristic:** ✅ Respects SharePoint permissions (enforced by SPE)

### The Security Requirement

**SDAP Requirement:** User operations **must** respect SharePoint Embedded permissions.

#### ❌ Without OBO (Security Breach)

```
Alice's Browser → SDAP API → Graph API (as App/MI)
                              ↓
                         Upload succeeds even if Alice has no permissions!
```

**Problem:** Application bypasses user permissions, creating a security vulnerability.

#### ✅ With OBO (Secure)

```
Alice's Browser → SDAP API → Graph API (as Alice via OBO)
                              ↓
                         Upload fails if Alice lacks permissions ✅
```

**Solution:** SharePoint Embedded enforces Alice's actual permissions.

---

## Architecture Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         API Layer                               │
│                                                                 │
│  ┌──────────────────────┐         ┌──────────────────────┐    │
│  │  DocumentsEndpoints  │         │    OBOEndpoints      │    │
│  │  (Admin/Platform)    │         │    (User Context)    │    │
│  │                      │         │                      │    │
│  │  GET /api/containers │         │  GET /api/obo/...   │    │
│  │  POST /api/files     │         │  PUT /api/obo/...   │    │
│  └──────────┬───────────┘         └──────────┬───────────┘    │
│             │                                │                │
│             └────────────────┬───────────────┘                │
│                              ▼                                 │
│                   ┌─────────────────────┐                      │
│                   │   SpeFileStore      │ ◄─── Single Facade  │
│                   │   (173 lines)       │                      │
│                   └─────────┬───────────┘                      │
│                             │                                  │
│         ┌───────────────────┼──────────────────┬───────────┐  │
│         ▼                   ▼                  ▼           ▼  │
│  ┌──────────────┐  ┌────────────────┐  ┌──────────┐  ┌──────────┐
│  │ Container    │  │  DriveItem     │  │  Upload  │  │  User    │
│  │ Operations   │  │  Operations    │  │  Session │  │  Ops     │
│  │              │  │                │  │  Manager │  │          │
│  │ • List       │  │ • List         │  │ • Small  │  │ • Info   │
│  │ • Create     │  │ • Download     │  │ • Session│  │ • Caps   │
│  │ • Get        │  │ • Update       │  │ • Chunk  │  │          │
│  │              │  │ • Delete       │  │          │  │          │
│  │ App + OBO ✅ │  │ App + OBO ✅   │  │ App+OBO✅│  │ OBO only │
│  └──────┬───────┘  └────────┬───────┘  └────┬─────┘  └────┬─────┘
│         │                   │               │             │     │
│         └───────────────────┼───────────────┼─────────────┘     │
│                             ▼               ▼                   │
│                   ┌──────────────────────────────┐              │
│                   │  IGraphClientFactory         │              │
│                   │  GraphClientFactory          │              │
│                   │                              │              │
│                   │  • CreateAppOnlyClient()     │ ◄─ MI Auth  │
│                   │  • CreateOnBehalfOfClient()  │ ◄─ OBO Auth │
│                   └──────────────┬───────────────┘              │
│                                  │                              │
│                   ┌──────────────┴───────────────┐              │
│                   ▼                              ▼              │
│          ┌─────────────────┐          ┌─────────────────┐      │
│          │ Managed Identity│          │ OBO Token       │      │
│          │ (App Credentials│          │ (User Assertion)│      │
│          └────────┬────────┘          └────────┬────────┘      │
│                   │                            │               │
│                   └────────────┬───────────────┘               │
│                                ▼                               │
│                    Microsoft Graph API                         │
│                    SharePoint Embedded                         │
└─────────────────────────────────────────────────────────────────┘
```

### Request Flow Examples

#### App-Only Request (Admin Operation)

```
1. DocumentsEndpoints.CreateContainer()
   ↓
2. SpeFileStore.CreateContainerAsync(containerTypeId, displayName)
   ↓
3. ContainerOperations.CreateContainerAsync(containerTypeId, displayName)
   ↓
4. GraphClientFactory.CreateAppOnlyClient()
   ↓ (Uses Managed Identity)
5. GraphServiceClient → POST /storage/fileStorage/containers
   ↓
6. Returns ContainerDto (SDAP DTO, not Graph SDK type)
```

#### OBO Request (User Operation)

```
1. OBOEndpoints.UploadFile(containerId, path, content)
   ↓ (Extracts bearer token)
2. TokenHelper.ExtractBearerToken(httpContext)
   ↓
3. SpeFileStore.UploadSmallAsUserAsync(userToken, containerId, path, content)
   ↓
4. UploadSessionManager.UploadSmallAsUserAsync(userToken, containerId, path, content)
   ↓
5. GraphClientFactory.CreateOnBehalfOfClientAsync(userToken)
   ↓ (Exchanges user token for Graph token)
6. ConfidentialClientApplication.AcquireTokenOnBehalfOf(userAssertion)
   ↓
7. GraphServiceClient → PUT /drives/{driveId}/root:/{path}:/content
   ↓ (With user's permissions)
8. Maps DriveItem → FileHandleDto (SDAP DTO)
   ↓
9. Returns FileHandleDto (Graph SDK type never exposed)
```

---

## Core Components

### 1. SpeFileStore (Single Facade)

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Lines:** 173

**Purpose:** Single unified entry point for all Graph/SPE operations

**Responsibilities:**
- Delegates all operations to specialized classes
- Provides consistent API surface
- Enforces DTO-only return types
- Never contains business logic (pure delegation)

**Design Pattern:** Facade Pattern + Dependency Injection

**Key Characteristics:**
- ✅ Single entry point (ADR-007 compliant)
- ✅ No interface abstraction (concrete class)
- ✅ All methods return SDAP DTOs, never Graph SDK types
- ✅ Supports both app-only and OBO operations
- ✅ Thin delegation layer (no business logic)

**Public API:**

```csharp
public class SpeFileStore
{
    // Dependencies (injected)
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadManager;
    private readonly UserOperations _userOps;

    // App-Only Methods (Managed Identity)
    public Task<ContainerDto?> CreateContainerAsync(...)
    public Task<IList<ContainerDto>?> ListContainersAsync(...)
    public Task<FileHandleDto?> UploadSmallAsync(...)
    public Task<IList<FileHandleDto>> ListChildrenAsync(...)
    public Task<Stream?> DownloadFileAsync(...)
    public Task<bool> DeleteFileAsync(...)

    // OBO Methods (User Context) - Note: *AsUserAsync naming
    public Task<IList<ContainerDto>> ListContainersAsUserAsync(string userToken, ...)
    public Task<ListingResponse> ListChildrenAsUserAsync(string userToken, ...)
    public Task<FileHandleDto?> UploadSmallAsUserAsync(string userToken, ...)
    public Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(string userToken, ...)
    public Task<DriveItemDto?> UpdateItemAsUserAsync(string userToken, ...)
    public Task<bool> DeleteItemAsUserAsync(string userToken, ...)
    public Task<UserInfoResponse?> GetUserInfoAsync(string userToken, ...)
    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(string userToken, ...)
}
```

**Naming Convention:**
- App-only methods: `VerbNounAsync(...)`
- OBO methods: `VerbNounAsUserAsync(string userToken, ...)`

### 2. Operation Classes (Specialized Modules)

#### ContainerOperations

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs`

**Lines:** 258

**Purpose:** Container lifecycle management (create, list, get)

**Methods:**
- `CreateContainerAsync()` - App-only (admin creates containers)
- `GetContainerDriveAsync()` - App-only (get drive for container)
- `ListContainersAsync()` - App-only (list all containers)
- `ListContainersAsUserAsync()` - OBO (list containers user can access)

**Key Pattern:**
```csharp
// App-only method
public async Task<IList<ContainerDto>?> ListContainersAsync(
    Guid containerTypeId, CancellationToken ct = default)
{
    var graphClient = _factory.CreateAppOnlyClient();  // ✅ Managed Identity
    // ... Graph API call ...
    return containers.Select(c => new ContainerDto(...)).ToList(); // ✅ Map to DTO
}

// OBO method
public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
    string userToken,
    Guid containerTypeId,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token is required for OBO operations");

    var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken); // ✅ OBO
    // ... Graph API call ...
    return containers.Select(c => new ContainerDto(...)).ToList(); // ✅ Map to DTO
}
```

#### DriveItemOperations

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs`

**Lines:** ~450

**Purpose:** File and folder operations (list, download, update, delete)

**Methods:**
- `ListChildrenAsync()` - App-only
- `DownloadFileAsync()` - App-only
- `DeleteFileAsync()` - App-only
- `GetFileMetadataAsync()` - App-only
- `ListChildrenAsUserAsync()` - OBO (with paging, ordering)
- `DownloadFileWithRangeAsUserAsync()` - OBO (with Range/ETag support)
- `UpdateItemAsUserAsync()` - OBO (rename/move)
- `DeleteItemAsUserAsync()` - OBO

**Advanced Features:**
- HTTP 206 Partial Content (Range requests)
- HTTP 304 Not Modified (ETag caching)
- OData query support (paging, ordering, filtering)

#### UploadSessionManager

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

**Lines:** ~465

**Purpose:** File upload operations (small files and chunked uploads)

**Methods:**
- `UploadSmallAsync()` - App-only (< 4MB files)
- `CreateUploadSessionAsync()` - App-only (large file session)
- `UploadChunkAsync()` - App-only (chunk upload)
- `UploadSmallAsUserAsync()` - OBO (< 4MB files)
- `CreateUploadSessionAsUserAsync()` - OBO (large file session)
- `UploadChunkAsUserAsync()` - OBO (chunk upload with validation)

**Upload Patterns:**
- Small files (< 4MB): Direct PUT to content endpoint
- Large files (≥ 4MB): Chunked upload via upload session
- Chunk size: 8-10 MB (Graph API requirement)

#### UserOperations

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs`

**Lines:** 102

**Purpose:** User-specific operations (identity, permissions)

**Methods:**
- `GetUserInfoAsync()` - OBO only (get current user info)
- `GetUserCapabilitiesAsync()` - OBO only (check container permissions)

**Note:** This class has **no app-only methods** because user operations only make sense in user context.

### 3. GraphClientFactory

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

**Lines:** 132

**Purpose:** Creates authenticated Graph clients for both MI and OBO flows

**Interface:**

```csharp
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates a Graph client using User-Assigned Managed Identity for app-only operations.
    /// </summary>
    GraphServiceClient CreateAppOnlyClient();

    /// <summary>
    /// Creates a Graph client using On-Behalf-Of flow for user context operations.
    /// </summary>
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);
}
```

**Implementation Details:**

#### App-Only Client (Managed Identity)

```csharp
public GraphServiceClient CreateAppOnlyClient()
{
    TokenCredential credential;

    // Local dev: Use Client Secret
    if (!string.IsNullOrWhiteSpace(_clientSecret))
    {
        credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
    }
    // Azure: Use Managed Identity
    else
    {
        credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _uamiClientId,
            ExcludeInteractiveBrowserCredential = true,
            ExcludeSharedTokenCacheCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeVisualStudioCredential = true
        });
    }

    var authProvider = new AzureIdentityAuthenticationProvider(
        credential,
        scopes: new[] { "https://graph.microsoft.com/.default" }
    );

    var httpClient = _httpClientFactory.CreateClient("GraphApiClient");
    return new GraphServiceClient(httpClient, authProvider);
}
```

#### OBO Client (User Token Exchange)

```csharp
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
{
    // Exchange user token for Graph token via MSAL
    var result = await _cca.AcquireTokenOnBehalfOf(
        new[] { "https://graph.microsoft.com/.default" },
        new UserAssertion(userAccessToken)
    ).ExecuteAsync();

    // Create token credential with acquired token
    var tokenCredential = new SimpleTokenCredential(result.AccessToken);

    var authProvider = new AzureIdentityAuthenticationProvider(
        tokenCredential,
        scopes: new[] { "https://graph.microsoft.com/.default" }
    );

    var httpClient = _httpClientFactory.CreateClient("GraphApiClient");
    return new GraphServiceClient(httpClient, authProvider);
}
```

**Key Features:**
- ✅ Centralized authentication logic
- ✅ Environment-aware (dev vs Azure)
- ✅ Uses IHttpClientFactory for resilience (retry, circuit breaker, timeout)
- ✅ MSAL token caching (in-memory for BFF pattern)
- ✅ Tenant-specific authority (not /common)

### 4. TokenHelper Utility

**Location:** `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs`

**Lines:** ~20

**Purpose:** Centralized bearer token extraction from HTTP requests

**Implementation:**

```csharp
public static class TokenHelper
{
    /// <summary>
    /// Extracts bearer token from Authorization header.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown if token missing or malformed</exception>
    public static string ExtractBearerToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader))
            throw new UnauthorizedAccessException("Missing Authorization header");

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid Authorization header format. Expected 'Bearer {token}'");

        return authHeader["Bearer ".Length..].Trim();
    }
}
```

**Usage in Endpoints:**

```csharp
app.MapGet("/api/obo/containers/{id}/children", async (
    string id,
    HttpContext ctx,
    [FromServices] SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    try
    {
        var userToken = TokenHelper.ExtractBearerToken(ctx); // ✅ Centralized extraction
        var result = await speFileStore.ListChildrenAsUserAsync(userToken, id, parameters, ct);
        return TypedResults.Ok(result);
    }
    catch (UnauthorizedAccessException)
    {
        return TypedResults.Unauthorized();
    }
});
```

---

## Authentication Flows

### Flow 1: Managed Identity (App-Only)

**Scenario:** Background worker processes a document event

**Sequence:**

```
1. DocumentEventProcessor receives Service Bus message
   ↓
2. Calls SpeFileStore.GetFileMetadataAsync(driveId, itemId)
   ↓
3. SpeFileStore delegates to DriveItemOperations.GetFileMetadataAsync()
   ↓
4. DriveItemOperations calls GraphClientFactory.CreateAppOnlyClient()
   ↓
5. GraphClientFactory creates client with ManagedIdentityCredential
   ↓
6. Azure AD authenticates using Managed Identity
   ↓ (Returns access token with app permissions)
7. Graph SDK makes API call: GET /drives/{driveId}/items/{itemId}
   ↓
8. SharePoint Embedded returns file metadata (bypassing user permissions)
   ↓
9. DriveItemOperations maps DriveItem → FileHandleDto
   ↓
10. Returns FileHandleDto to caller
```

**Authentication Details:**
- **Credential:** User-Assigned Managed Identity (UAMI)
- **Scope:** `https://graph.microsoft.com/.default`
- **Token Audience:** Microsoft Graph
- **Permissions:** Application permissions (Files.ReadWrite.All, Sites.FullControl.All)
- **User Context:** None (app acts as itself)

### Flow 2: On-Behalf-Of (User Context)

**Scenario:** User Alice uploads a file through the web UI

**Sequence:**

```
1. React SPA: User clicks "Upload File"
   ↓
2. SPA acquires user token from MSAL.js (Azure AD)
   ↓
3. SPA sends request: PUT /api/obo/containers/{id}/files/report.pdf
   ↓ (Authorization: Bearer {user_token})
4. OBOEndpoints receives request
   ↓
5. TokenHelper.ExtractBearerToken(ctx) extracts user token
   ↓
6. Calls SpeFileStore.UploadSmallAsUserAsync(userToken, containerId, path, stream)
   ↓
7. SpeFileStore delegates to UploadSessionManager.UploadSmallAsUserAsync()
   ↓
8. UploadSessionManager validates userToken parameter (not null/empty)
   ↓
9. Calls GraphClientFactory.CreateOnBehalfOfClientAsync(userToken)
   ↓
10. GraphClientFactory calls MSAL: AcquireTokenOnBehalfOf(userAssertion)
    ↓ (MSAL exchanges user token for Graph token)
11. Azure AD performs OBO flow:
    - Validates user token (issued by same tenant)
    - Checks consent (User.Read, Files.ReadWrite)
    - Issues Graph access token in user context
    ↓
12. Graph SDK makes API call: PUT /drives/{driveId}/root:/{path}:/content
    ↓ (With user's permissions - Alice's token)
13. SharePoint Embedded checks permissions:
    - Does Alice have write access to this container?
    - If yes → upload succeeds ✅
    - If no → 403 Forbidden ❌
    ↓
14. UploadSessionManager receives DriveItem from Graph
    ↓
15. Maps DriveItem → FileHandleDto (ADR-007 compliance)
    ↓
16. Returns FileHandleDto to endpoint
    ↓
17. Endpoint returns 200 OK with FileHandleDto JSON
    ↓
18. SPA displays success message to Alice
```

**Authentication Details:**
- **Credential:** On-Behalf-Of (OBO) flow via MSAL
- **Initial Token:** User's access token from SPA (audience: SDAP API)
- **Exchanged Token:** Graph access token (audience: Microsoft Graph)
- **Scope:** `https://graph.microsoft.com/.default`
- **Permissions:** Delegated permissions (Files.ReadWrite, Sites.Read.All)
- **User Context:** Alice's identity and permissions
- **Security:** SharePoint permissions enforced ✅

---

## Design Patterns

### Pattern 1: Facade Pattern (SpeFileStore)

**Intent:** Provide a unified interface to a set of interfaces in a subsystem.

**Implementation:**

```csharp
public class SpeFileStore
{
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadManager;
    private readonly UserOperations _userOps;

    // Facade method - pure delegation
    public Task<FileHandleDto?> UploadSmallAsync(
        string driveId, string path, Stream content, CancellationToken ct = default)
        => _uploadManager.UploadSmallAsync(driveId, path, content, ct);

    // Another facade method - pure delegation
    public Task<FileHandleDto?> UploadSmallAsUserAsync(
        string userToken, string containerId, string path, Stream content, CancellationToken ct = default)
        => _uploadManager.UploadSmallAsUserAsync(userToken, containerId, path, content, ct);
}
```

**Benefits:**
- Single entry point for consumers
- Hides subsystem complexity
- Easy to refactor internals without breaking API
- Enforces consistent DTO exposure

### Pattern 2: Strategy Pattern (Dual Authentication)

**Intent:** Define a family of algorithms (auth methods), encapsulate each one, and make them interchangeable.

**Implementation:**

```csharp
public interface IGraphClientFactory
{
    GraphServiceClient CreateAppOnlyClient();  // ✅ Strategy 1: Managed Identity
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string token); // ✅ Strategy 2: OBO
}

// Operation classes choose strategy based on method called
public class ContainerOperations
{
    private readonly IGraphClientFactory _factory;

    // Uses Strategy 1 (App-only)
    public async Task<IList<ContainerDto>?> ListContainersAsync(...)
    {
        var client = _factory.CreateAppOnlyClient(); // ← Strategy selection
        // ...
    }

    // Uses Strategy 2 (OBO)
    public async Task<IList<ContainerDto>> ListContainersAsUserAsync(string userToken, ...)
    {
        var client = await _factory.CreateOnBehalfOfClientAsync(userToken); // ← Strategy selection
        // ...
    }
}
```

**Benefits:**
- Authentication logic isolated in factory
- Operation classes don't know auth details
- Easy to add new auth strategies (e.g., client certificate)
- Testable via mocking `IGraphClientFactory`

### Pattern 3: DTO Mapping Pattern

**Intent:** Never expose third-party SDK types through public APIs.

**Implementation:**

```csharp
// ❌ WRONG - Exposes Graph SDK type
public async Task<DriveItem?> UploadSmallAsUserAsync(...)
{
    var driveItem = await graphClient.Drives[driveId].Root
        .ItemWithPath(path).Content.PutAsync(content, ct);
    return driveItem; // ❌ Leaks Graph SDK type
}

// ✅ CORRECT - Maps to SDAP DTO
public async Task<FileHandleDto?> UploadSmallAsUserAsync(...)
{
    var driveItem = await graphClient.Drives[driveId].Root
        .ItemWithPath(path).Content.PutAsync(content, ct);

    // Map Graph SDK type → SDAP DTO (ADR-007 compliance)
    return new FileHandleDto(
        driveItem.Id!,
        driveItem.Name!,
        driveItem.ParentReference?.Id,
        driveItem.Size,
        driveItem.CreatedDateTime ?? DateTimeOffset.UtcNow,
        driveItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
        driveItem.ETag,
        driveItem.Folder != null); // ✅ Returns SDAP DTO
}
```

**Benefits:**
- Decouples SDAP from Graph SDK versions
- Smaller payload (only essential properties)
- Consistent API contract
- Easier to test (simple DTOs vs complex SDK objects)

### Pattern 4: Naming Convention Pattern

**Intent:** Clearly distinguish app-only vs OBO methods through naming.

**Convention:**

```csharp
// App-only methods (Managed Identity)
VerbNounAsync(parameters, CancellationToken ct = default)

// OBO methods (User context)
VerbNounAsUserAsync(string userToken, parameters, CancellationToken ct = default)
```

**Examples:**

```csharp
// Container operations
Task<IList<ContainerDto>?> ListContainersAsync(...)          // App-only
Task<IList<ContainerDto>> ListContainersAsUserAsync(string userToken, ...) // OBO

// File operations
Task<FileHandleDto?> UploadSmallAsync(...)                   // App-only
Task<FileHandleDto?> UploadSmallAsUserAsync(string userToken, ...) // OBO

// Download operations
Task<Stream?> DownloadFileAsync(...)                         // App-only
Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(string userToken, ...) // OBO
```

**Benefits:**
- Self-documenting code
- Clear at call site which auth mode is used
- Prevents accidental use of wrong method
- IntelliSense grouping (all `*AsUserAsync` together)

### Pattern 5: Token Validation Pattern

**Intent:** Validate user tokens early and consistently.

**Implementation:**

```csharp
public async Task<FileHandleDto?> UploadSmallAsUserAsync(
    string userToken,
    string containerId,
    string path,
    Stream content,
    CancellationToken ct = default)
{
    // ✅ Validate token early (fail fast)
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token is required for OBO operations", nameof(userToken));

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);
        // ... rest of implementation ...
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied uploading to container {ContainerId}: {Error}", containerId, ex.Message);
        throw new UnauthorizedAccessException($"Access denied to container {containerId}", ex);
    }
}
```

**Validation Points:**
1. **Null/empty check** - Fail fast if token missing
2. **Graph API validation** - Let Azure AD validate token authenticity
3. **Permission check** - Let SharePoint enforce user permissions
4. **Error translation** - Convert 403 ServiceException → UnauthorizedAccessException

### Pattern 6: Resilience Pattern (Centralized)

**Intent:** Apply retry, circuit breaker, and timeout policies to all Graph calls.

**Implementation:**

```csharp
// GraphHttpMessageHandler (registered in DI)
public class GraphHttpMessageHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // ✅ Retry policy (3 attempts, exponential backoff)
        var retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r =>
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                r.StatusCode >= HttpStatusCode.InternalServerError)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // ✅ Circuit breaker (open after 5 consecutive failures)
        var circuitBreakerPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        // ✅ Timeout policy (30 seconds)
        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(30);

        var policyWrap = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy, timeoutPolicy);
        return await policyWrap.ExecuteAsync(() => base.SendAsync(request, ct));
    }
}

// DI Registration
services.AddHttpClient("GraphApiClient")
    .AddHttpMessageHandler<GraphHttpMessageHandler>(); // ✅ Applied to all Graph calls
```

**Benefits:**
- ✅ All Graph API calls automatically resilient
- ✅ No manual retry logic in operation classes
- ✅ Consistent behavior across app-only and OBO flows
- ✅ Configurable policies (appsettings.json)

---

## Code Examples

### Example 1: Adding a New App-Only Operation

**Requirement:** Add a method to get container permissions (app-only).

**Step 1: Add method to operation class**

```csharp
// File: ContainerOperations.cs

/// <summary>
/// Gets permissions for a container (app-only).
/// </summary>
public async Task<IList<PermissionDto>> GetContainerPermissionsAsync(
    string containerId,
    CancellationToken ct = default)
{
    using var activity = Activity.Current;
    activity?.SetTag("operation", "GetContainerPermissions");
    activity?.SetTag("containerId", containerId);

    _logger.LogInformation("Getting permissions for container {ContainerId}", containerId);

    try
    {
        // ✅ Use app-only client (Managed Identity)
        var graphClient = _factory.CreateAppOnlyClient();

        var permissions = await graphClient.Storage.FileStorage
            .Containers[containerId].Permissions
            .GetAsync(cancellationToken: ct);

        if (permissions?.Value == null)
        {
            _logger.LogWarning("No permissions found for container {ContainerId}", containerId);
            return new List<PermissionDto>();
        }

        // ✅ Map to SDAP DTO (never expose Graph SDK types)
        var result = permissions.Value
            .Select(p => new PermissionDto(
                p.Id!,
                p.GrantedToV2?.User?.DisplayName,
                p.Roles?.ToList() ?? new List<string>()))
            .ToList();

        _logger.LogInformation("Retrieved {Count} permissions for container {ContainerId}",
            result.Count, containerId);

        return result;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Container {ContainerId} not found", containerId);
        return new List<PermissionDto>();
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
    {
        _logger.LogWarning("Graph API throttling encountered, retry with backoff");
        throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to get permissions for container {ContainerId}", containerId);
        throw new InvalidOperationException($"Failed to get container permissions: {ex.Message}", ex);
    }
}
```

**Step 2: Add delegation method to SpeFileStore**

```csharp
// File: SpeFileStore.cs

// In the app-only methods section
public Task<IList<PermissionDto>> GetContainerPermissionsAsync(
    string containerId,
    CancellationToken ct = default)
    => _containerOps.GetContainerPermissionsAsync(containerId, ct);
```

**Step 3: Use in endpoint**

```csharp
// File: ContainerEndpoints.cs

app.MapGet("/api/containers/{id}/permissions", async (
    string id,
    [FromServices] SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    try
    {
        var permissions = await speFileStore.GetContainerPermissionsAsync(id, ct);
        return TypedResults.Ok(permissions);
    }
    catch (ServiceException ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
}).RequireRateLimiting("graph-read");
```

### Example 2: Adding a New OBO Operation

**Requirement:** Add a method to copy a file as the user (OBO).

**Step 1: Add method to DriveItemOperations**

```csharp
// File: DriveItemOperations.cs

/// <summary>
/// Copies a file to a new location as the user (OBO flow).
/// </summary>
public async Task<DriveItemDto?> CopyItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    string destinationPath,
    CancellationToken ct = default)
{
    // ✅ Validate user token early (fail fast)
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    using var activity = Activity.Current;
    activity?.SetTag("operation", "CopyItemAsUser");
    activity?.SetTag("driveId", driveId);
    activity?.SetTag("itemId", itemId);

    _logger.LogInformation("Copying item {ItemId} to {DestinationPath} (user context)",
        itemId, destinationPath);

    try
    {
        // ✅ Use OBO client (user context)
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

        // Create copy request
        var copyRequest = new DriveItem
        {
            ParentReference = new ItemReference
            {
                Path = destinationPath
            }
        };

        // Copy item (async operation in Graph)
        var copiedItem = await graphClient.Drives[driveId].Items[itemId]
            .Copy(copyRequest.ParentReference.Path, copyRequest.Name)
            .PostAsync(cancellationToken: ct);

        if (copiedItem == null)
        {
            _logger.LogWarning("Failed to copy item {ItemId}", itemId);
            return null;
        }

        _logger.LogInformation("Copied item {ItemId} to {NewItemId} (user context)",
            itemId, copiedItem.Id);

        // ✅ Map to SDAP DTO (ADR-007 compliance)
        return new DriveItemDto(
            Id: copiedItem.Id!,
            Name: copiedItem.Name!,
            Size: copiedItem.Size,
            ETag: copiedItem.ETag,
            LastModifiedDateTime: copiedItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            ContentType: copiedItem.File?.MimeType,
            Folder: copiedItem.Folder != null ? new FolderDto() : null);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied copying item {ItemId} for user", itemId);
        throw new UnauthorizedAccessException($"Access denied to copy item {itemId}", ex);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Item {ItemId} not found for copy operation", itemId);
        return null;
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to copy item {ItemId} for user", itemId);
        throw;
    }
}
```

**Step 2: Add delegation to SpeFileStore**

```csharp
// File: SpeFileStore.cs

// In the OBO methods section
public Task<DriveItemDto?> CopyItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    string destinationPath,
    CancellationToken ct = default)
    => _driveItemOps.CopyItemAsUserAsync(userToken, driveId, itemId, destinationPath, ct);
```

**Step 3: Create endpoint**

```csharp
// File: OBOEndpoints.cs

app.MapPost("/api/obo/drives/{driveId}/items/{itemId}/copy", async (
    string driveId,
    string itemId,
    CopyFileRequest request,
    HttpContext ctx,
    [FromServices] SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    try
    {
        // ✅ Extract user token
        var userToken = TokenHelper.ExtractBearerToken(ctx);

        // ✅ Call OBO method
        var copiedItem = await speFileStore.CopyItemAsUserAsync(
            userToken, driveId, itemId, request.DestinationPath, ct);

        return copiedItem == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(copiedItem);
    }
    catch (UnauthorizedAccessException)
    {
        return TypedResults.Unauthorized();
    }
    catch (ServiceException ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
}).RequireRateLimiting("graph-write");
```

### Example 3: Testing with Different Auth Modes

**Scenario:** Test file upload with both app-only and OBO flows.

```csharp
// File: UploadSessionManagerTests.cs

public class UploadSessionManagerTests
{
    private readonly Mock<IGraphClientFactory> _mockFactory;
    private readonly Mock<ILogger<UploadSessionManager>> _mockLogger;
    private readonly UploadSessionManager _manager;

    public UploadSessionManagerTests()
    {
        _mockFactory = new Mock<IGraphClientFactory>();
        _mockLogger = new Mock<ILogger<UploadSessionManager>>();
        _manager = new UploadSessionManager(_mockFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task UploadSmallAsync_AppOnly_CreatesCorrectClient()
    {
        // Arrange
        var mockClient = new Mock<GraphServiceClient>();
        _mockFactory.Setup(f => f.CreateAppOnlyClient())
            .Returns(mockClient.Object); // ✅ App-only client

        // Act
        await _manager.UploadSmallAsync("drive-id", "path.txt", Stream.Null);

        // Assert
        _mockFactory.Verify(f => f.CreateAppOnlyClient(), Times.Once);
        _mockFactory.Verify(f => f.CreateOnBehalfOfClientAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadSmallAsUserAsync_OBO_CreatesCorrectClient()
    {
        // Arrange
        var userToken = "user-token-123";
        var mockClient = new Mock<GraphServiceClient>();
        _mockFactory.Setup(f => f.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockClient.Object); // ✅ OBO client

        // Act
        await _manager.UploadSmallAsUserAsync(userToken, "container-id", "path.txt", Stream.Null);

        // Assert
        _mockFactory.Verify(f => f.CreateOnBehalfOfClientAsync(userToken), Times.Once);
        _mockFactory.Verify(f => f.CreateAppOnlyClient(), Times.Never);
    }

    [Fact]
    public async Task UploadSmallAsUserAsync_NullToken_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.UploadSmallAsUserAsync(null!, "container-id", "path.txt", Stream.Null));

        Assert.Contains("User access token is required", exception.Message);
    }
}
```

---

## Developer Guidelines

### DO: Use SpeFileStore for All Graph Operations

```csharp
// ✅ CORRECT - Inject and use SpeFileStore
public class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/containers", async (
            CreateContainerRequest request,
            [FromServices] SpeFileStore speFileStore,  // ✅ Inject SpeFileStore
            CancellationToken ct) =>
        {
            var container = await speFileStore.CreateContainerAsync(
                request.ContainerTypeId, request.DisplayName, ct);
            return TypedResults.Ok(container);
        });
    }
}
```

```csharp
// ❌ WRONG - Don't inject operation classes directly
public class DocumentsEndpoints
{
    app.MapPost("/api/containers", async (
        CreateContainerRequest request,
        [FromServices] ContainerOperations containerOps,  // ❌ Don't do this
        CancellationToken ct) =>
    {
        // ...
    });
}
```

### DO: Use Correct Method for Auth Mode

```csharp
// ✅ CORRECT - Use *AsUserAsync for user operations
app.MapPut("/api/obo/files/{path}", async (
    string path,
    HttpContext ctx,
    [FromServices] SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    var userToken = TokenHelper.ExtractBearerToken(ctx);
    var result = await speFileStore.UploadSmallAsUserAsync(userToken, ...); // ✅ OBO method
    return TypedResults.Ok(result);
});

// ✅ CORRECT - Use regular Async for app-only operations
app.MapPost("/api/background/process", async (
    [FromServices] SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    var result = await speFileStore.UploadSmallAsync(...); // ✅ App-only method
    return TypedResults.Ok(result);
});
```

### DO: Always Map Graph SDK Types to DTOs

```csharp
// ✅ CORRECT - Map DriveItem to FileHandleDto
public async Task<FileHandleDto?> GetFileMetadataAsync(string driveId, string itemId, ...)
{
    var driveItem = await graphClient.Drives[driveId].Items[itemId].GetAsync(...);

    if (driveItem == null) return null;

    // ✅ Map to SDAP DTO
    return new FileHandleDto(
        driveItem.Id!,
        driveItem.Name!,
        driveItem.ParentReference?.Id,
        driveItem.Size,
        driveItem.CreatedDateTime ?? DateTimeOffset.UtcNow,
        driveItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
        driveItem.ETag,
        driveItem.Folder != null);
}
```

```csharp
// ❌ WRONG - Never return Graph SDK types
public async Task<DriveItem?> GetFileMetadataAsync(...)  // ❌ Returns DriveItem
{
    var driveItem = await graphClient.Drives[driveId].Items[itemId].GetAsync(...);
    return driveItem;  // ❌ Leaks Graph SDK type
}
```

### DO: Validate User Tokens Early

```csharp
// ✅ CORRECT - Validate early in OBO methods
public async Task<FileHandleDto?> UploadSmallAsUserAsync(
    string userToken,
    string containerId,
    ...)
{
    // ✅ Fail fast if token missing
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token is required for OBO operations", nameof(userToken));

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);
        // ... rest of implementation ...
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        // ✅ Translate to domain exception
        throw new UnauthorizedAccessException($"Access denied to container {containerId}", ex);
    }
}
```

### DO: Use Structured Logging

```csharp
// ✅ CORRECT - Structured logging with parameters
_logger.LogInformation(
    "Uploaded file {Path} to container {ContainerId}, item ID: {ItemId}, size: {Size} bytes",
    path, containerId, uploadedItem.Id, uploadedItem.Size);

// ✅ CORRECT - Log errors with exception
_logger.LogError(ex,
    "Failed to upload file {Path} to container {ContainerId}",
    path, containerId);
```

```csharp
// ❌ WRONG - String concatenation
_logger.LogInformation("Uploaded file " + path + " to container " + containerId);

// ❌ WRONG - No exception in LogError
_logger.LogError("Failed to upload: " + ex.Message);
```

### DO: Handle ServiceExceptions Properly

```csharp
// ✅ CORRECT - Specific exception handling
try
{
    var result = await graphClient.Drives[driveId].Items[itemId].GetAsync(...);
    return MapToDto(result);
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
{
    _logger.LogWarning("Item {ItemId} not found", itemId);
    return null;  // ✅ Return null for not found
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
{
    _logger.LogWarning("Access denied to item {ItemId}", itemId);
    throw new UnauthorizedAccessException($"Access denied to item {itemId}", ex);
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
{
    _logger.LogWarning("Graph API throttling encountered");
    throw new InvalidOperationException("Service temporarily unavailable", ex);
}
catch (ServiceException ex)
{
    _logger.LogError(ex, "Graph API error retrieving item {ItemId}", itemId);
    throw new InvalidOperationException($"Failed to retrieve item: {ex.Message}", ex);
}
```

### DON'T: Create New Graph Clients Directly

```csharp
// ❌ WRONG - Don't create Graph clients directly
public async Task DoSomething()
{
    var credential = new DefaultAzureCredential();  // ❌ Don't do this
    var graphClient = new GraphServiceClient(credential);
    // ...
}
```

```csharp
// ✅ CORRECT - Use IGraphClientFactory
public async Task DoSomething()
{
    var graphClient = _factory.CreateAppOnlyClient();  // ✅ Use factory
    // ...
}
```

### DON'T: Mix Auth Modes

```csharp
// ❌ WRONG - Don't call OBO method without user token
public async Task ProcessBackgroundJob()
{
    var userToken = ""; // ❌ Empty token in background job
    await speFileStore.UploadSmallAsUserAsync(userToken, ...); // ❌ Will fail
}
```

```csharp
// ✅ CORRECT - Use app-only method in background jobs
public async Task ProcessBackgroundJob()
{
    await speFileStore.UploadSmallAsync(...);  // ✅ No user token needed
}
```

### DON'T: Bypass SpeFileStore

```csharp
// ❌ WRONG - Don't call operation classes directly
public class MyService
{
    private readonly ContainerOperations _containerOps;  // ❌ Don't inject directly

    public async Task CreateContainer(...)
    {
        await _containerOps.CreateContainerAsync(...);  // ❌ Bypasses facade
    }
}
```

```csharp
// ✅ CORRECT - Use SpeFileStore facade
public class MyService
{
    private readonly SpeFileStore _speFileStore;  // ✅ Inject facade

    public async Task CreateContainer(...)
    {
        await _speFileStore.CreateContainerAsync(...);  // ✅ Through facade
    }
}
```

---

## Testing Strategy

### Unit Testing Operation Classes

**Goal:** Test business logic and Graph SDK integration in isolation.

**Pattern:** Mock `IGraphClientFactory` to control Graph client behavior.

```csharp
public class ContainerOperationsTests
{
    private readonly Mock<IGraphClientFactory> _mockFactory;
    private readonly Mock<ILogger<ContainerOperations>> _mockLogger;
    private readonly ContainerOperations _operations;

    public ContainerOperationsTests()
    {
        _mockFactory = new Mock<IGraphClientFactory>();
        _mockLogger = new Mock<ILogger<ContainerOperations>>();
        _operations = new ContainerOperations(_mockFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateContainerAsync_Success_ReturnsContainerDto()
    {
        // Arrange
        var mockClient = CreateMockGraphClient();
        _mockFactory.Setup(f => f.CreateAppOnlyClient()).Returns(mockClient.Object);

        // Act
        var result = await _operations.CreateContainerAsync(
            Guid.NewGuid(), "Test Container", "Description");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Container", result.DisplayName);
        _mockFactory.Verify(f => f.CreateAppOnlyClient(), Times.Once);
    }

    [Fact]
    public async Task ListContainersAsUserAsync_ValidToken_UsesOBOClient()
    {
        // Arrange
        var userToken = "valid-user-token";
        var mockClient = CreateMockGraphClient();
        _mockFactory.Setup(f => f.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockClient.Object);

        // Act
        await _operations.ListContainersAsUserAsync(userToken, Guid.NewGuid());

        // Assert - Verify OBO client was used, not app-only
        _mockFactory.Verify(f => f.CreateOnBehalfOfClientAsync(userToken), Times.Once);
        _mockFactory.Verify(f => f.CreateAppOnlyClient(), Times.Never);
    }
}
```

### Integration Testing with WireMock

**Goal:** Test HTTP-level Graph API integration without real API calls.

```csharp
public class GraphApiIntegrationTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockServer _mockServer;
    private readonly SpeFileStore _speFileStore;

    public GraphApiIntegrationTests(WireMockFixture fixture)
    {
        _mockServer = fixture.Server;
        _speFileStore = CreateSpeFileStore(mockServer.Url);
    }

    [Fact]
    public async Task UploadSmallAsync_Success_Returns201()
    {
        // Arrange - Mock Graph API response
        _mockServer
            .Given(Request.Create()
                .WithPath("/drives/*/root:/test.txt:/content")
                .UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new
                {
                    id = "item-123",
                    name = "test.txt",
                    size = 1024
                }));

        // Act
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        var result = await _speFileStore.UploadSmallAsync("drive-id", "test.txt", content);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("item-123", result.Id);
        Assert.Equal("test.txt", result.Name);
    }

    [Fact]
    public async Task UploadSmallAsUserAsync_403Forbidden_ThrowsUnauthorizedException()
    {
        // Arrange - Mock 403 response
        _mockServer
            .Given(Request.Create()
                .WithPath("/storage/fileStorage/containers/*")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithBodyAsJson(new { error = new { code = "accessDenied" } }));

        // Act & Assert
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _speFileStore.UploadSmallAsUserAsync("user-token", "container-id", "test.txt", content));
    }
}
```

### End-to-End Testing

**Goal:** Test entire flow including authentication, endpoints, and Graph API.

**Note:** Requires real Azure AD and Graph API (dev environment).

```csharp
[Collection("E2E Tests")]
public class OBOEndpointsE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public OBOEndpointsE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadFile_WithValidUserToken_Returns200()
    {
        // Arrange - Get real user token from MSAL
        var userToken = await AcquireUserTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userToken);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test content"));

        // Act
        var response = await _client.PutAsync(
            "/api/obo/containers/test-container-id/files/test.txt", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FileHandleDto>();
        Assert.NotNull(result);
        Assert.Equal("test.txt", result.Name);
    }

    [Fact]
    public async Task UploadFile_WithoutToken_Returns401()
    {
        // Arrange - No authorization header
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));

        // Act
        var response = await _client.PutAsync(
            "/api/obo/containers/test-container-id/files/test.txt", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

---

## Troubleshooting

### Issue 1: "401 Unauthorized" on OBO Endpoint

**Symptoms:**
- OBO endpoint returns 401
- App-only endpoints work fine
- User token appears valid

**Possible Causes:**

1. **Missing Authorization Header**
   ```csharp
   // Check if header is present
   var authHeader = httpContext.Request.Headers.Authorization;
   if (authHeader.Count == 0)
       throw new UnauthorizedAccessException("Missing Authorization header");
   ```

2. **Token for Wrong Audience**
   ```
   User token audience: api://frontend-app-id
   Required audience: api://sdap-api-app-id
   ```
   **Fix:** Ensure SPA requests token with correct scope: `api://{sdap-api-app-id}/.default`

3. **Expired Token**
   - User tokens typically expire after 1 hour
   - **Fix:** Implement token refresh in SPA (MSAL.js handles this)

4. **Missing API Permissions**
   - API app registration needs delegated permissions: `User.Read`, `Files.ReadWrite`
   - **Fix:** Add permissions in Azure Portal → App Registration → API Permissions

### Issue 2: "403 Forbidden" on OBO Operation

**Symptoms:**
- Endpoint returns 403
- User is authenticated (no 401)
- App-only operations work

**Possible Causes:**

1. **User Lacks SharePoint Permissions**
   ```
   User Alice authenticated ✅
   User Alice does NOT have write access to container ❌
   ```
   **Fix:** Grant user appropriate permissions in SharePoint Embedded

2. **Missing Consent**
   - User hasn't consented to delegated permissions
   - **Fix:** Trigger consent prompt in SPA or admin-consent via Azure Portal

3. **Token Missing Required Scopes**
   ```json
   // Token should have scopes:
   {
     "scp": "Files.ReadWrite Sites.Read.All User.Read"
   }
   ```
   **Fix:** Request correct scopes when acquiring token

### Issue 3: "Graph SDK Type Leaked Through API"

**Symptoms:**
- Build succeeds
- But endpoint returns verbose Graph SDK object (20+ properties)

**Diagnosis:**

```bash
# Check for Graph SDK types in SpeFileStore
grep -E "public Task<.*DriveItem[^D].*>" src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs
```

**Fix:**

```csharp
// ❌ Method returns DriveItem
public Task<DriveItem?> SomeMethodAsync(...) => ...;

// ✅ Change to return DTO
public Task<FileHandleDto?> SomeMethodAsync(...) => ...;

// In operation class, add mapping:
var driveItem = await graphClient...;
return new FileHandleDto(
    driveItem.Id!,
    driveItem.Name!,
    // ... map all properties
);
```

### Issue 4: "OBO Token Exchange Fails"

**Symptoms:**
- Exception: "AADSTS500133: Assertion is not within its valid time range"
- Or: "AADSTS50013: Invalid assertion"

**Possible Causes:**

1. **Clock Skew**
   - Server time differs from Azure AD time by > 5 minutes
   - **Fix:** Sync server clock with NTP

2. **Token Reuse**
   - Same user token used multiple times (MSAL caches)
   - **Fix:** This is normal behavior, MSAL handles caching correctly

3. **Wrong Tenant**
   - Using `/common` endpoint instead of tenant-specific
   - **Fix:** Ensure `GraphClientFactory` uses `_tenantId` in authority

### Issue 5: "Managed Identity Not Working Locally"

**Symptoms:**
- Local dev: "ManagedIdentityCredential authentication unavailable"
- Azure: Works fine

**Expected Behavior:**
- Local dev should use Client Secret (not Managed Identity)
- Azure should use Managed Identity

**Fix:**

```csharp
// GraphClientFactory should have fallback logic:
public GraphServiceClient CreateAppOnlyClient()
{
    TokenCredential credential;

    // ✅ Local dev: Use client secret
    if (!string.IsNullOrWhiteSpace(_clientSecret) &&
        !string.IsNullOrWhiteSpace(_tenantId) &&
        !string.IsNullOrWhiteSpace(_clientId))
    {
        credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
        _logger.LogDebug("Creating app-only Graph client with ClientSecretCredential");
    }
    // ✅ Azure: Use Managed Identity
    else
    {
        credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _uamiClientId,
            // ... exclude interactive credentials
        });
        _logger.LogDebug("Creating app-only Graph client with DefaultAzureCredential");
    }

    // ...
}
```

**Configuration:**

```json
// appsettings.Development.json (local dev)
{
  "TENANT_ID": "your-tenant-id",
  "API_APP_ID": "your-api-app-id",
  "API_CLIENT_SECRET": "your-client-secret"  // ✅ Local only
}

// appsettings.Production.json (Azure)
{
  "UAMI_CLIENT_ID": "managed-identity-client-id"  // ✅ Azure only
}
```

---

## Summary

### Key Takeaways

1. **SpeFileStore is the Single Entry Point**
   - All Graph operations go through this facade
   - Never inject operation classes directly
   - Enforces ADR-007 compliance

2. **Two Authentication Modes**
   - App-only (MI): `VerbNounAsync(...)` - Platform/admin operations
   - OBO: `VerbNounAsUserAsync(userToken, ...)` - User operations

3. **Always Map to DTOs**
   - Never expose `DriveItem`, `FileStorageContainer`, or other Graph SDK types
   - Return `FileHandleDto`, `ContainerDto`, or other SDAP DTOs
   - Smaller payloads, cleaner API contracts

4. **Validate User Tokens Early**
   - Check for null/empty in OBO methods
   - Let Graph API validate authenticity
   - Let SharePoint enforce permissions

5. **Use Centralized Patterns**
   - `IGraphClientFactory` for authentication
   - `TokenHelper` for token extraction
   - `GraphHttpMessageHandler` for resilience
   - Structured logging throughout

### Architecture Benefits

✅ **Clean Separation** - MI and OBO flows clearly distinguished
✅ **Security** - SharePoint permissions enforced for user operations
✅ **Maintainability** - Single facade, modular operation classes
✅ **Testability** - Mock `IGraphClientFactory` for unit tests
✅ **Resilience** - Centralized retry, circuit breaker, timeout
✅ **ADR Compliance** - 100% ADR-007 compliant

### Quick Reference

**Adding App-Only Operation:**
1. Add method to operation class (uses `CreateAppOnlyClient()`)
2. Map Graph SDK type → SDAP DTO
3. Add delegation to `SpeFileStore`
4. Create endpoint

**Adding OBO Operation:**
1. Add `*AsUserAsync(string userToken, ...)` method to operation class
2. Validate `userToken` parameter early
3. Use `CreateOnBehalfOfClientAsync(userToken)`
4. Map Graph SDK type → SDAP DTO
5. Add delegation to `SpeFileStore`
6. Create `/api/obo/*` endpoint
7. Use `TokenHelper.ExtractBearerToken(ctx)`

**Common Methods:**
- `SpeFileStore.UploadSmallAsync(...)` - App-only upload
- `SpeFileStore.UploadSmallAsUserAsync(userToken, ...)` - OBO upload
- `SpeFileStore.ListChildrenAsync(...)` - App-only list
- `SpeFileStore.ListChildrenAsUserAsync(userToken, ...)` - OBO list

---

**Document Status:** ✅ Production-Ready
**Last Updated:** October 3, 2025
**Maintainer:** Spaarke Engineering Team
**Related:** ADR-007, Sprint 4 Task 4.4

For questions or clarifications, refer to:
- [ADR-007](../adr/ADR-007-spe-storage-seam-minimalism.md)
- [Task 4.4 Assessment](../../dev/projects/sdap_project/Sprint%204/TASK-4.4-CURRENT-STATE-ASSESSMENT.md)
- [Architecture Diagrams](./diagrams/)
