# Task 03: Update IGraphClientFactory Interface

**Task ID**: `03-IGraphClientFactory`
**Estimated Time**: 10 minutes
**Status**: Not Started
**Dependencies**: None

---

## 📋 Prompt

Update the `IGraphClientFactory` interface to provide clearer method names that distinguish between user-context (OBO) and app-only authentication patterns. This improves code readability and makes the intent explicit at call sites.

---

## ✅ Todos

- [ ] Open `src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs`
- [ ] Add `ForUserAsync(HttpContext, CancellationToken)` method
- [ ] Add `ForApp()` method
- [ ] Keep existing methods for backward compatibility (deprecated)
- [ ] Add XML documentation
- [ ] Build and verify no compilation errors

---

## 📚 Required Knowledge

### Current Interface (Unclear Intent)
```csharp
public interface IGraphClientFactory
{
    GraphServiceClient CreateAppOnlyClient();
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);
}
```

**Problems**:
- Method names are verbose (`CreateAppOnlyClient`, `CreateOnBehalfOfClientAsync`)
- OBO method requires manual token extraction (error-prone)
- No cancellation token support

### Proposed Interface (Clear Intent)
```csharp
public interface IGraphClientFactory
{
    Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default);
    GraphServiceClient ForApp();
}
```

**Benefits**:
- Clear, concise names (`ForUserAsync`, `ForApp`)
- OBO method extracts token automatically from HttpContext
- Supports cancellation tokens
- Reads naturally at call sites: `await graphFactory.ForUserAsync(context)`

### Use Cases
- **ForUserAsync**: File access endpoints (user permissions enforced)
- **ForApp**: Background jobs, container creation (admin operations)

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs)

**Files That Will Change** (later tasks):
- [src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs) (Task 04)
- [src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs) (Task 05)

---

## 🎯 Implementation

### File: `src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs`

```csharp
using Microsoft.Graph;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory for creating Microsoft Graph clients with different authentication modes.
/// </summary>
/// <remarks>
/// Two authentication patterns:
/// - ForUserAsync: On-Behalf-Of (OBO) flow using user's access token (for user operations)
/// - ForApp: App-only using Managed Identity or Client Secret (for admin operations)
/// </remarks>
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates a Graph client using On-Behalf-Of (OBO) flow for user context operations.
    /// Extracts user token from Authorization header and exchanges it for Graph API token.
    /// </summary>
    /// <param name="ctx">HttpContext containing Authorization header with user's bearer token</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>GraphServiceClient authenticated with user's delegated permissions</returns>
    /// <remarks>
    /// Use this for operations that should enforce user permissions (e.g., file access).
    /// The user token must have audience api://{BFF-AppId}/SDAP.Access.
    /// OBO tokens are cached in Redis for 55 minutes to reduce Azure AD load.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Missing or invalid Authorization header</exception>
    /// <exception cref="Microsoft.Identity.Client.MsalServiceException">OBO token exchange failed</exception>
    Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Creates a Graph client using app-only authentication (Managed Identity or Client Secret).
    /// </summary>
    /// <returns>GraphServiceClient authenticated with application permissions</returns>
    /// <remarks>
    /// Use this for platform/admin operations (e.g., container creation, background jobs).
    /// Requires application permissions in Azure AD (e.g., Sites.FullControl.All).
    /// </remarks>
    GraphServiceClient ForApp();

    // ============================================================================
    // DEPRECATED METHODS - Keep for backward compatibility during transition
    // ============================================================================

    /// <summary>
    /// [DEPRECATED] Use ForApp() instead.
    /// Creates a Graph client using app-only authentication.
    /// </summary>
    [Obsolete("Use ForApp() instead", DiagnosticId = "SDAP001")]
    GraphServiceClient CreateAppOnlyClient();

    /// <summary>
    /// [DEPRECATED] Use ForUserAsync(HttpContext, CancellationToken) instead.
    /// Creates a Graph client using On-Behalf-Of flow.
    /// </summary>
    [Obsolete("Use ForUserAsync(HttpContext, CancellationToken) instead", DiagnosticId = "SDAP002")]
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);
}
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] Deprecated methods show compiler warnings (expected)

### Code Quality
- [ ] New methods have clear, concise names
- [ ] XML documentation explains when to use each method
- [ ] Cancellation token support added
- [ ] HttpContext parameter replaces manual token extraction
- [ ] Obsolete attributes guide developers to new methods

### Verification Steps
1. Open the file and make changes
2. Build: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
3. Verify compilation succeeds
4. Verify existing code using deprecated methods still compiles (with warnings)

---

## 📝 Notes

### Why Keep Deprecated Methods?
The existing codebase (e.g., `OBOEndpoints.cs`, `SpeFileStore.cs`) uses the old method names. Keeping them marked as `[Obsolete]` allows:
1. Gradual migration without breaking existing code
2. Compiler warnings to guide refactoring
3. Clear transition path for developers

### Why ForUserAsync Takes HttpContext?
Instead of requiring manual token extraction:

**Old Pattern** (error-prone):
```csharp
var token = TokenHelper.ExtractBearerToken(context);
var client = await factory.CreateOnBehalfOfClientAsync(token);
```

**New Pattern** (safe):
```csharp
var client = await factory.ForUserAsync(context, ct);
```

Benefits:
- Less boilerplate
- Token extraction handled internally (consistent error handling)
- Easier to test (mock HttpContext, not string tokens)

---

## 🔗 Related Documentation

- [Technical Review](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Section 5.3 (IGraphClientFactory)
- [SDAP Architecture Guide](../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE-10-20-2025.md)

---

**Previous Task**: [02-TASK-GlobalExceptionHandler.md](./02-TASK-GlobalExceptionHandler.md)
**Next Task**: [04-TASK-GraphClientFactory-Implementation.md](./04-TASK-GraphClientFactory-Implementation.md)
