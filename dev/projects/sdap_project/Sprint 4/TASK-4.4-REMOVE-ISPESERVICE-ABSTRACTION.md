# Task 4.4: Remove ISpeService/IOboSpeService Abstractions (ADR-007 Compliance)

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 2 days (16 hours)
**Status:** Ready for Implementation
**Dependencies:** None

---

## Problem Statement

### Current State (ADR VIOLATION)
The codebase contains `ISpeService` and `IOboSpeService` interface abstractions that **directly violate ADR-007** (SPE Storage Seam Minimalism). These are the exact abstractions the ADR decided to remove.

**Evidence from ADR-007:**
> **Decision:** Remove the ISpeService abstraction layer. Endpoints will use SpeFileStore directly.
> **Rationale:** The ISpeService interface was adding complexity without providing value.

**Current Violation:**
```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs
public interface ISpeService  // ❌ Explicitly forbidden by ADR-007
{
    Task<ContainerListResponse> ListContainersAsync(CancellationToken ct);
    Task<DriveItem?> GetItemAsync(string containerId, string itemId, CancellationToken ct);
    // ... 15 methods
}

// src/api/Spe.Bff.Api/Services/IOboSpeService.cs
public interface IOboSpeService  // ❌ Also violates ADR-007
{
    Task<ContainerListResponse> ListContainersAsync(HttpContext context, CancellationToken ct);
    // ... methods
}
```

**Impact:**
- **Critical ADR non-compliance** (only major violation in codebase)
- Over-abstraction without testing or modularity benefit
- Increased complexity and indirection
- Violates principle of minimalism (ADR-010)

**Affected Files:** 18+ files
- 2 interface definitions
- 2 concrete implementations
- 14+ endpoint and test files consuming interfaces

### Target State (COMPLIANT)
Remove both interfaces. Endpoints use `SpeFileStore` facade directly, with user token passed as parameter for OBO scenarios.

---

## Architecture Context

### ADR-007: SPE Storage Seam Minimalism

**Core Principle:**
> Prefer concrete dependencies over unnecessary abstractions. Interfaces should only exist when there's a **concrete need** for swappability or testing.

**Approved Architecture:**
```
Endpoints → SpeFileStore (facade) → Graph SDK
                ↓
         (No ISpeService layer)
```

**Why Interfaces Were Removed:**
1. **No multiple implementations** - Only one SPE provider (Microsoft)
2. **Testing doesn't require interfaces** - Can use in-memory Graph SDK or WireMock
3. **OBO handled via parameters** - User token passed to methods, not separate interface
4. **Reduced complexity** - Fewer layers, clearer code

---

### Current vs Target Architecture

**Current (INCORRECT):**
```
UserEndpoints → IOboSpeService → OboSpeService → IGraphClientFactory → GraphServiceClient
                      ↓
                (unnecessary interface layer)
```

**Target (CORRECT per ADR-007):**
```
UserEndpoints → SpeFileStore → IGraphClientFactory → GraphServiceClient
                     ↑
              (facade, no interface)
              Accepts userToken parameter for OBO
```

---

## Solution Design

### Step 1: Update SpeFileStore to Support User Tokens

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Current State:** SpeFileStore uses app-only authentication (client credentials).

**Add OBO Support:**
```csharp
public class SpeFileStore
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<SpeFileStore> _logger;

    public SpeFileStore(IGraphClientFactory graphClientFactory, ILogger<SpeFileStore> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    // ============================================================================
    // APP-ONLY METHODS (existing, for background jobs)
    // ============================================================================

    /// <summary>
    /// Lists all containers using app-only authentication (client credentials).
    /// Use for background jobs and admin operations.
    /// </summary>
    public async Task<ContainerListResponse> ListContainersAsync(CancellationToken ct)
    {
        var graphClient = _graphClientFactory.CreateClient(); // App-only
        var containers = await graphClient.Storage.FileStorage.Containers.GetAsync(ct);

        return new ContainerListResponse
        {
            Containers = containers?.Value?.Select(c => new ContainerInfo
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                ContainerTypeId = c.ContainerTypeId,
                CreatedDateTime = c.CreatedDateTime
            }).ToList() ?? []
        };
    }

    // ... other app-only methods

    // ============================================================================
    // OBO METHODS (new, for user context operations)
    // ============================================================================

    /// <summary>
    /// Lists containers accessible to the user (OBO flow).
    /// </summary>
    /// <param name="userAccessToken">User's bearer token for OBO flow</param>
    public async Task<ContainerListResponse> ListContainersAsUserAsync(
        string userAccessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            throw new ArgumentException("User access token is required for OBO operations", nameof(userAccessToken));
        }

        var graphClient = _graphClientFactory.CreateClientForUser(userAccessToken);
        var containers = await graphClient.Storage.FileStorage.Containers.GetAsync(ct);

        return new ContainerListResponse
        {
            Containers = containers?.Value?.Select(c => new ContainerInfo
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                ContainerTypeId = c.ContainerTypeId,
                CreatedDateTime = c.CreatedDateTime
            }).ToList() ?? []
        };
    }

    /// <summary>
    /// Downloads a file as the user (OBO flow).
    /// </summary>
    public async Task<FileContentResponse> DownloadFileAsUserAsync(
        string userAccessToken,
        string containerId,
        string itemId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            throw new ArgumentException("User access token is required", nameof(userAccessToken));
        }

        var graphClient = _graphClientFactory.CreateClientForUser(userAccessToken);

        var driveItem = await graphClient.Storage.FileStorage.Containers[containerId]
            .Drive.Items[itemId]
            .GetAsync(ct);

        if (driveItem?.File == null)
        {
            return null; // Not a file
        }

        var contentStream = await graphClient.Storage.FileStorage.Containers[containerId]
            .Drive.Items[itemId]
            .Content.GetAsync(ct);

        return new FileContentResponse
        {
            FileName = driveItem.Name,
            ContentType = driveItem.File.MimeType ?? "application/octet-stream",
            ContentStream = contentStream,
            Size = driveItem.Size ?? 0
        };
    }

    /// <summary>
    /// Uploads a file as the user (OBO flow).
    /// </summary>
    public async Task<DriveItem> UploadFileAsUserAsync(
        string userAccessToken,
        string containerId,
        string fileName,
        Stream contentStream,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            throw new ArgumentException("User access token is required", nameof(userAccessToken));
        }

        var graphClient = _graphClientFactory.CreateClientForUser(userAccessToken);

        // Small file upload (< 4MB)
        if (contentStream.Length < 4 * 1024 * 1024)
        {
            var uploadedItem = await graphClient.Storage.FileStorage.Containers[containerId]
                .Drive.Root
                .ItemWithPath(fileName)
                .Content
                .PutAsync(contentStream, ct);

            return uploadedItem;
        }

        // Large file upload (resumable session)
        var uploadSession = await graphClient.Storage.FileStorage.Containers[containerId]
            .Drive.Root
            .ItemWithPath(fileName)
            .CreateUploadSession
            .PostAsync(ct);

        // Use UploadSessionManager for large file uploads
        throw new NotImplementedException("Large file uploads require UploadSessionManager refactoring");
    }

    // ... additional OBO methods for create, delete, move, copy operations
}
```

**Key Changes:**
1. **Dual API surface:** App-only methods (existing) + OBO methods (new)
2. **Naming convention:** `*AsUserAsync` for OBO methods
3. **Token parameter:** First parameter is `userAccessToken` for OBO methods
4. **No interfaces:** Concrete class used directly

---

### Step 2: Add User Token Extraction Helper

**File:** `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs` (create new)

```csharp
using System.Security.Claims;

namespace Spe.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Helper for extracting user tokens from HttpContext.
/// </summary>
public static class TokenHelper
{
    /// <summary>
    /// Extracts the bearer token from the Authorization header.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown if token is missing or malformed</exception>
    public static string ExtractBearerToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader))
        {
            throw new UnauthorizedAccessException("Missing Authorization header");
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid Authorization header format. Expected 'Bearer {token}'");
        }

        return authHeader.Substring("Bearer ".Length).Trim();
    }

    /// <summary>
    /// Gets the current user's Object ID (oid claim) from the ClaimsPrincipal.
    /// </summary>
    public static string GetUserObjectId(ClaimsPrincipal user)
    {
        return user.FindFirst("oid")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User Object ID not found in claims");
    }

    /// <summary>
    /// Gets the current user's UPN (preferred_username or upn claim).
    /// </summary>
    public static string? GetUserPrincipalName(ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst(ClaimTypes.Upn)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }
}
```

---

### Step 3: Update UserEndpoints (Remove IOboSpeService)

**File:** `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

**Current State (lines 20-40):**
```csharp
app.MapGet("/api/user/containers", async (
    [FromServices] IOboSpeService oboService,  // ❌ Remove interface
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var containers = await oboService.ListContainersAsync(httpContext, ct);
    return Results.Ok(containers);
})
.RequireAuthorization("canreadcontainers");
```

**Updated Implementation:**
```csharp
app.MapGet("/api/user/containers", async (
    [FromServices] SpeFileStore speFileStore,  // ✅ Use facade directly
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userToken = TokenHelper.ExtractBearerToken(httpContext);
    var containers = await speFileStore.ListContainersAsUserAsync(userToken, ct);
    return Results.Ok(containers);
})
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");
```

**Apply Same Pattern to All User Endpoints:**
- `GET /api/user/containers` → `ListContainersAsUserAsync`
- `GET /api/user/recent-documents` → `ListRecentDocumentsAsUserAsync`
- `POST /api/user/preferences` → `UpdatePreferencesAsUserAsync`
- etc.

---

### Step 4: Update OBOEndpoints (Remove IOboSpeService)

**File:** `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

**Current State:**
```csharp
app.MapGet("/api/obo/containers/{containerId}/items", async (
    [FromRoute] string containerId,
    [FromServices] IOboSpeService oboService,  // ❌ Remove
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var items = await oboService.GetContainerItemsAsync(httpContext, containerId, ct);
    return Results.Ok(items);
})
.RequireAuthorization("canreadcontainers");
```

**Updated:**
```csharp
app.MapGet("/api/obo/containers/{containerId}/items", async (
    [FromRoute] string containerId,
    [FromServices] SpeFileStore speFileStore,  // ✅ Use facade
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userToken = TokenHelper.ExtractBearerToken(httpContext);
    var items = await speFileStore.GetContainerItemsAsUserAsync(userToken, containerId, ct);
    return Results.Ok(items);
})
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");
```

**Apply to All 9 OBO Endpoints:**
- List containers
- Get container items
- Download file
- Upload file
- Delete file
- Move file
- Copy file
- Create container
- Get item metadata

---

### Step 5: Update DocumentsEndpoints

**File:** `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs`

**Pattern:** If using `IOboSpeService`, replace with `SpeFileStore + userToken`.

**Example:**
```csharp
app.MapPost("/api/containers/{containerId}/documents/{documentId}/download", async (
    [FromRoute] string containerId,
    [FromRoute] string documentId,
    [FromServices] SpeFileStore speFileStore,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userToken = TokenHelper.ExtractBearerToken(httpContext);
    var fileContent = await speFileStore.DownloadFileAsUserAsync(userToken, containerId, documentId, ct);

    if (fileContent == null)
    {
        return Results.NotFound(new { error = "File not found" });
    }

    return Results.File(fileContent.ContentStream, fileContent.ContentType, fileContent.FileName);
})
.RequireAuthorization("canreaddocuments")
.RequireRateLimiting("graph-read");
```

---

### Step 6: Update UploadEndpoints

**File:** `src/api/Spe.Bff.Api/Api/UploadEndpoints.cs`

**Current:** Likely uses `IOboSpeService` for upload session creation.

**Update to:**
```csharp
app.MapPost("/api/upload/session", async (
    [FromBody] CreateUploadSessionRequest request,
    [FromServices] SpeFileStore speFileStore,
    [FromServices] UploadSessionManager uploadManager,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userToken = TokenHelper.ExtractBearerToken(httpContext);

    // Create upload session using user's permissions
    var uploadSession = await speFileStore.CreateUploadSessionAsUserAsync(
        userToken,
        request.ContainerId,
        request.FileName,
        ct);

    // Track session in manager
    var sessionId = uploadManager.RegisterSession(uploadSession, httpContext.User);

    return Results.Created($"/api/upload/session/{sessionId}", new
    {
        sessionId,
        uploadUrl = uploadSession.UploadUrl,
        expiresAt = uploadSession.ExpirationDateTime
    });
})
.RequireAuthorization("canuploadfiles")
.RequireRateLimiting("upload-heavy");
```

---

### Step 7: Update DI Registration (Remove Interfaces)

**File:** `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs`

**Current Registration:**
```csharp
public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
{
    // Remove these registrations:
    services.AddScoped<ISpeService, SpeService>();  // ❌ Delete
    services.AddScoped<IOboSpeService, OboSpeService>();  // ❌ Delete

    // Keep these:
    services.AddScoped<SpeFileStore>();  // ✅ Keep (concrete class)
    services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
    services.AddScoped<UploadSessionManager>();

    return services;
}
```

**Updated Registration:**
```csharp
public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
{
    // Register concrete services only
    services.AddScoped<SpeFileStore>();
    services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
    services.AddScoped<UploadSessionManager>();

    return services;
}
```

---

### Step 8: Delete Interface Files

**Files to DELETE:**
```
src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs
src/api/Spe.Bff.Api/Services/IOboSpeService.cs
src/api/Spe.Bff.Api/Services/OboSpeService.cs (implementation)
```

**Verification:**
```bash
# Search for remaining references (should return 0 results)
grep -r "IOboSpeService" src/
grep -r "ISpeService" src/
```

---

### Step 9: Update Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/UserEndpointsTests.cs`

**Current Test Pattern:**
```csharp
[Fact]
public async Task GetContainers_CallsOboService()
{
    // Arrange
    var mockOboService = new Mock<IOboSpeService>();  // ❌ Remove mock
    mockOboService.Setup(s => s.ListContainersAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ContainerListResponse { Containers = [] });

    // ...
}
```

**Updated Test Pattern (Option A - Mock SpeFileStore):**
```csharp
[Fact]
public async Task GetContainers_UsesSpeFileStore()
{
    // Arrange
    var mockSpeFileStore = new Mock<SpeFileStore>(
        Mock.Of<IGraphClientFactory>(),
        Mock.Of<ILogger<SpeFileStore>>());

    mockSpeFileStore.Setup(s => s.ListContainersAsUserAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ContainerListResponse { Containers = [] });

    // ... rest of test
}
```

**Updated Test Pattern (Option B - Use Real SpeFileStore with Mocked Factory):**
```csharp
[Fact]
public async Task GetContainers_WithRealSpeFileStore()
{
    // Arrange
    var mockGraphClient = new Mock<GraphServiceClient>();
    // ... setup mock Graph responses

    var mockFactory = new Mock<IGraphClientFactory>();
    mockFactory.Setup(f => f.CreateClientForUser(It.IsAny<string>()))
        .Returns(mockGraphClient.Object);

    var speFileStore = new SpeFileStore(mockFactory.Object, Mock.Of<ILogger<SpeFileStore>>());

    // Act
    var result = await speFileStore.ListContainersAsUserAsync("fake-token", CancellationToken.None);

    // Assert
    Assert.NotNull(result);
}
```

**Recommended:** Option B (use real SpeFileStore) to test actual logic.

---

### Step 10: Update Integration Tests

**File:** `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs`

**Ensure SpeFileStore is registered:**
```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        // Ensure SpeFileStore is registered (should be automatic via DocumentsModule)
        // No IOboSpeService or ISpeService registrations needed
    });
}
```

**Update Integration Tests:**
```csharp
[Fact]
public async Task UserContainersEndpoint_ReturnsContainers()
{
    // Arrange
    var client = _fixture.CreateAuthenticatedClient(); // Includes bearer token

    // Act
    var response = await client.GetAsync("/api/user/containers");

    // Assert
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadFromJsonAsync<ContainerListResponse>();
    Assert.NotNull(content);
}
```

---

## Testing Strategy

### Unit Tests

**Test Categories:**
1. **SpeFileStore OBO Methods** - Verify user token is passed correctly
2. **TokenHelper** - Verify token extraction logic
3. **Endpoint Handlers** - Verify endpoints call SpeFileStore correctly

**Example Test:**
```csharp
[Fact]
public async Task ListContainersAsUser_WithValidToken_CallsGraphWithUserContext()
{
    // Arrange
    var userToken = "fake-user-token";
    var mockGraphClient = new Mock<GraphServiceClient>();
    var mockFactory = new Mock<IGraphClientFactory>();

    mockFactory.Setup(f => f.CreateClientForUser(userToken))
        .Returns(mockGraphClient.Object);

    var speFileStore = new SpeFileStore(mockFactory.Object, Mock.Of<ILogger<SpeFileStore>>());

    // Act
    await speFileStore.ListContainersAsUserAsync(userToken, CancellationToken.None);

    // Assert
    mockFactory.Verify(f => f.CreateClientForUser(userToken), Times.Once);
}

[Fact]
public async Task ListContainersAsUser_WithNullToken_ThrowsException()
{
    // Arrange
    var speFileStore = new SpeFileStore(Mock.Of<IGraphClientFactory>(), Mock.Of<ILogger<SpeFileStore>>());

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() =>
        speFileStore.ListContainersAsUserAsync(null, CancellationToken.None));
}
```

---

### Integration Tests

**Test Real OBO Flow:**
```csharp
[Fact]
public async Task OBOEndpoint_WithRealUserToken_AccessesUserFiles()
{
    // Arrange - Acquire real user token via Azure AD
    var userToken = await AcquireUserTokenAsync();
    var client = _fixture.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

    // Act
    var response = await client.GetAsync("/api/user/containers");

    // Assert
    response.EnsureSuccessStatusCode();
    var containers = await response.Content.ReadFromJsonAsync<ContainerListResponse>();
    Assert.NotNull(containers);
    Assert.NotEmpty(containers.Containers);
}
```

---

### Smoke Tests

**After Deployment:**
1. **User Endpoints:** Call `/api/user/containers` with user token → 200 OK
2. **OBO Endpoints:** Call `/api/obo/containers/{id}/items` → 200 OK
3. **File Download:** Download a file as user → File content returned
4. **File Upload:** Upload a file as user → 201 Created

---

## Validation & Verification

### Success Criteria

✅ **Code Cleanup:**
- [ ] `ISpeService.cs` deleted
- [ ] `IOboSpeService.cs` deleted
- [ ] `OboSpeService.cs` deleted
- [ ] No references to these interfaces remain in codebase

✅ **Build & Compilation:**
- [ ] `dotnet build Spaarke.sln` completes with 0 errors
- [ ] No missing dependencies

✅ **Unit Tests:**
- [ ] All existing tests updated and passing
- [ ] New SpeFileStore OBO method tests passing

✅ **Integration Tests:**
- [ ] All integration tests passing
- [ ] Real OBO flow works end-to-end

✅ **ADR Compliance:**
- [ ] ADR-007 fully compliant (no ISpeService/IOboSpeService)
- [ ] Architectural review passes

---

## Impact Analysis

### Files Requiring Changes

**DELETE (3 files):**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs`
- `src/api/Spe.Bff.Api/Services/IOboSpeService.cs`
- `src/api/Spe.Bff.Api/Services/OboSpeService.cs`

**MODIFY (15+ files):**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` (add OBO methods)
- `src/api/Spe.Bff.Api/Api/UserEndpoints.cs` (replace IOboSpeService)
- `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs` (replace IOboSpeService)
- `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs` (replace IOboSpeService)
- `src/api/Spe.Bff.Api/Api/UploadEndpoints.cs` (replace IOboSpeService)
- `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs` (remove registrations)
- `tests/unit/Spe.Bff.Api.Tests/UserEndpointsTests.cs` (update mocks)
- `tests/unit/Spe.Bff.Api.Tests/OBOEndpointsTests.cs` (update mocks)
- `tests/unit/Spe.Bff.Api.Tests/FileOperationsTests.cs` (update mocks)
- `tests/integration/Spe.Integration.Tests/*.cs` (update to use SpeFileStore)

**CREATE (1 file):**
- `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs` (new utility)

---

## Rollback Plan

**If OBO Flow Breaks:**

1. **Revert Changes:** Use Git to revert all changes from this task
   ```bash
   git revert <commit-hash>
   ```

2. **Temporary Workaround:** Keep interfaces but mark as deprecated
   ```csharp
   [Obsolete("Use SpeFileStore directly. This interface will be removed in next release.")]
   public interface IOboSpeService { }
   ```

3. **Impact:** ADR-007 remains violated (technical debt carries forward)

---

## Known Issues & Limitations

### Issue #1: Large File Upload Refactoring
**Current State:** `UploadSessionManager` tightly coupled to old interface

**Resolution:** Refactor `UploadSessionManager` to accept `SpeFileStore` directly

---

### Issue #2: Mocking Concrete Classes in Tests
**Challenge:** Mocking `SpeFileStore` (concrete class) requires `virtual` methods

**Options:**
- **Option A:** Make key methods `virtual` for testing
- **Option B:** Use real `SpeFileStore` with mocked `IGraphClientFactory`
- **Option C:** Use integration tests instead of unit tests

**Recommendation:** Option B (mock factory, use real facade)

---

## References

### ADRs
- **ADR-007:** SPE Storage Seam Minimalism (primary driver)
- **ADR-010:** DI Minimalism (supports removing interfaces)

### Related Files
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs` (keep - has multiple implementations)
- All endpoint files

---

## AI Implementation Prompt

**Copy this prompt to your AI coding assistant:**

```
Remove ISpeService and IOboSpeService interface abstractions to comply with ADR-007.

CONTEXT:
- ADR-007 explicitly forbids ISpeService abstraction layer
- Current codebase has ISpeService and IOboSpeService interfaces (violation)
- Need to use SpeFileStore facade directly per ADR decision

TASKS:
1. Update src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs:
   - Add OBO methods: ListContainersAsUserAsync, DownloadFileAsUserAsync, etc.
   - Accept userAccessToken as first parameter for OBO methods
   - Keep existing app-only methods for background jobs
2. Create src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs:
   - Add ExtractBearerToken method
   - Add GetUserObjectId method
3. Update all endpoint files (UserEndpoints, OBOEndpoints, DocumentsEndpoints, UploadEndpoints):
   - Replace IOboSpeService with SpeFileStore
   - Extract user token using TokenHelper
   - Call *AsUserAsync methods with token
4. Update src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs:
   - Remove ISpeService and IOboSpeService registrations
   - Keep SpeFileStore registration (concrete class)
5. Delete files:
   - src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs
   - src/api/Spe.Bff.Api/Services/IOboSpeService.cs
   - src/api/Spe.Bff.Api/Services/OboSpeService.cs
6. Update test files to mock SpeFileStore or IGraphClientFactory
7. Run dotnet build and verify no errors

VALIDATION:
- Build succeeds with 0 errors
- No references to ISpeService or IOboSpeService remain
- Tests pass with updated mocks

Reference Steps 1-9 of this task document for detailed code examples.
```

---

**Task Owner:** [Assign to developer]
**Reviewer:** [Assign to senior developer/architect]
**Created:** 2025-10-02
**Updated:** 2025-10-02
