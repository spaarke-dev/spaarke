# Task 4.4 - Phase 3: Create TokenHelper Utility

**Sprint:** 4
**Phase:** 3 of 7
**Estimated Effort:** 30 minutes
**Dependencies:** None (can run in parallel)
**Status:** Ready

---

## Objective

Create utility class to extract bearer tokens from HttpContext, consolidating duplicated code across OBO endpoints.

---

## Create TokenHelper.cs

**NEW FILE:** `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Helper for extracting bearer tokens from HttpContext.
/// Consolidates token extraction logic used across OBO endpoints.
/// </summary>
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
        {
            throw new UnauthorizedAccessException("Missing Authorization header");
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid Authorization header format. Expected 'Bearer {token}'");
        }

        return authHeader["Bearer ".Length..].Trim();
    }
}
```

---

## Acceptance Criteria

- [ ] New file created: `Infrastructure/Auth/TokenHelper.cs`
- [ ] Static class with single method
- [ ] Throws `UnauthorizedAccessException` for invalid/missing tokens
- [ ] Build succeeds with 0 errors

---

## Next Phase

**Phase 4:** Update endpoints to use SpeFileStore + TokenHelper
