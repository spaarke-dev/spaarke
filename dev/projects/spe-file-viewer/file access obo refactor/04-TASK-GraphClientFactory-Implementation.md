# Task 04: Implement ForUserAsync and ForApp Methods

**Task ID**: `04-GraphClientFactory-Implementation`
**Estimated Time**: 15 minutes
**Status**: Not Started
**Dependencies**: 03-TASK-IGraphClientFactory

---

## 📋 Prompt

Implement the new `ForUserAsync` and `ForApp` methods in `GraphClientFactory`. These methods wrap the existing OBO and app-only logic with clearer names and better ergonomics (automatic token extraction, cancellation token support).

---

## ✅ Todos

- [ ] Open `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- [ ] Add `using Spe.Bff.Api.Infrastructure.Auth;` for TokenHelper
- [ ] Implement `ForUserAsync(HttpContext, CancellationToken)`
- [ ] Implement `ForApp()`
- [ ] Keep existing methods (call new methods internally)
- [ ] Add XML documentation
- [ ] Build and verify no compilation errors

---

## 📚 Required Knowledge

### Token Extraction
The `TokenHelper.ExtractBearerToken(HttpContext)` utility already exists:
- Validates Authorization header presence
- Validates "Bearer " prefix
- Throws `UnauthorizedAccessException` if invalid

### Existing OBO Implementation
`CreateOnBehalfOfClientAsync(string userAccessToken)` already:
- Checks Redis cache (55-min TTL)
- Performs OBO token exchange using MSAL
- Caches result in Redis
- Returns GraphServiceClient

### Implementation Strategy
The new methods are **thin wrappers**:
1. `ForUserAsync` → Extract token → Call `CreateOnBehalfOfClientAsync`
2. `ForApp` → Call `CreateAppOnlyClient`

This maintains backward compatibility while improving call-site ergonomics.

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs)

**Dependencies**:
- [src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs)
- [src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs) (Task 03)

---

## 🎯 Implementation

### Add Using Statement (top of file)
```csharp
using Spe.Bff.Api.Infrastructure.Auth;
```

### Add New Methods (after existing CreateOnBehalfOfClientAsync method)

```csharp
/// <summary>
/// Creates Graph client using On-Behalf-Of flow for user context operations.
/// Extracts user token from Authorization header and exchanges it for Graph API token.
/// </summary>
/// <param name="ctx">HttpContext containing Authorization header with user's bearer token</param>
/// <param name="ct">Cancellation token (currently unused, reserved for future async cancellation)</param>
/// <returns>GraphServiceClient authenticated with user's delegated permissions</returns>
/// <exception cref="UnauthorizedAccessException">Missing or invalid Authorization header</exception>
/// <exception cref="Microsoft.Identity.Client.MsalServiceException">OBO token exchange failed</exception>
/// <remarks>
/// This method wraps CreateOnBehalfOfClientAsync with automatic token extraction.
/// OBO tokens are cached in Redis for 55 minutes to reduce Azure AD load by 97%.
/// </remarks>
public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
{
    // Extract bearer token from Authorization header (throws UnauthorizedAccessException if invalid)
    var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

    _logger.LogDebug("ForUserAsync called | TraceId: {TraceId}", ctx.TraceIdentifier);

    // Delegate to existing OBO implementation (handles caching, token exchange, etc.)
    return await CreateOnBehalfOfClientAsync(userAccessToken);
}

/// <summary>
/// Creates Graph client using app-only authentication (Managed Identity or Client Secret).
/// </summary>
/// <returns>GraphServiceClient authenticated with application permissions</returns>
/// <remarks>
/// This method wraps CreateAppOnlyClient with a clearer name.
/// Use for platform/admin operations (container creation, background jobs).
/// </remarks>
public GraphServiceClient ForApp()
{
    _logger.LogDebug("ForApp called - using app-only authentication");

    // Delegate to existing app-only implementation
    return CreateAppOnlyClient();
}
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] No new compiler warnings (except existing deprecated method warnings)

### Code Quality
- [ ] Methods delegate to existing implementations (no duplicate logic)
- [ ] XML documentation is complete
- [ ] Logging includes TraceId for debugging
- [ ] ForUserAsync respects cancellation token parameter (reserved for future use)

### Testing
1. Build solution: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
2. Verify no compilation errors
3. Verify TokenHelper is correctly imported

### Example Usage (from FileAccessEndpoints - Task 05)
```csharp
var graphClient = await graphFactory.ForUserAsync(context, ct);
var previewUrl = await graphClient.Drives[driveId].Items[itemId].Preview
    .PostAsync(new PreviewPostRequestBody { Viewer = "onedrive" }, ct);
```

---

## 📝 Notes

### Why Not Use CancellationToken in ForUserAsync Yet?
The current MSAL OBO implementation (`_cca.AcquireTokenOnBehalfOf(...).ExecuteAsync()`) doesn't accept a cancellation token. We include the parameter for future compatibility when MSAL adds support.

For now:
```csharp
public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
{
    // ct is reserved for future use when MSAL supports cancellation
    var userAccessToken = TokenHelper.ExtractBearerToken(ctx);
    return await CreateOnBehalfOfClientAsync(userAccessToken);
}
```

### Why Keep CreateAppOnlyClient and CreateOnBehalfOfClientAsync?
These methods contain the actual implementation logic. The new methods (`ForUserAsync`, `ForApp`) are convenience wrappers that improve ergonomics without duplicating code.

**Benefits**:
- Existing code (OBOEndpoints, SpeFileStore) still works
- New code uses clearer method names
- Implementation logic stays in one place
- Gradual migration path (deprecation warnings guide developers)

---

## 🔗 Related Documentation

- [Technical Review](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Section 5.4 (GraphClientFactory Implementation)
- [TokenHelper.cs Source](../../../src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs)

---

**Previous Task**: [03-TASK-IGraphClientFactory.md](./03-TASK-IGraphClientFactory.md)
**Next Task**: [05-TASK-FileAccessEndpoints-Refactor.md](./05-TASK-FileAccessEndpoints-Refactor.md)
