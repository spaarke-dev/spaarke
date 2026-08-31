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

    /// <summary>
    /// Non-throwing counterpart to <see cref="ExtractBearerToken"/>: returns the bearer token, or
    /// <c>null</c> when the Authorization header is missing or malformed.
    /// </summary>
    /// <remarks>
    /// Added by unified-access-control-r2 task 004 (FR-02). Authorization filters need to make a
    /// *decision* about a missing token, not propagate an exception: <see cref="ExtractBearerToken"/>
    /// throws <see cref="UnauthorizedAccessException"/>, which the global handler maps to a 500 on the
    /// authorization path — a missing credential must fail CLOSED with a deny, never surface as a
    /// server error. Callers that genuinely require a token (OBO downstream calls) should keep using
    /// the throwing overload; callers that must render a decision use this one.
    /// </remarks>
    public static string? ExtractBearerTokenOrNull(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authHeader = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
