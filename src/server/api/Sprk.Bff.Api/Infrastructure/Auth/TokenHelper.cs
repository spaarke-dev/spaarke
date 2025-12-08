namespace Sprk.Bff.Api.Infrastructure.Auth;

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
