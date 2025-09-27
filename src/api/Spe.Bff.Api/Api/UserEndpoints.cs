using Spe.Bff.Api.Infrastructure.Errors;
using Services;

namespace Api;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me - Get current user info
        app.MapGet("/api/me", async (
            HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

            try
            {
                var userInfo = await oboSvc.GetUserInfoAsync(bearer, ct);
                return userInfo == null ? Results.Unauthorized() : Results.Ok(userInfo);
            }
            catch (Exception)
            {
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Failed to retrieve user information");
            }
        }).RequireRateLimiting("graph-read");

        // GET /api/me/capabilities?containerId={containerId} - Get user capabilities for container
        app.MapGet("/api/me/capabilities", async (
            string containerId,
            HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(containerId))
            {
                return ProblemDetailsHelper.ValidationError("containerId query parameter is required");
            }

            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

            try
            {
                var capabilities = await oboSvc.GetUserCapabilitiesAsync(bearer, containerId, ct);
                return Results.Ok(capabilities);
            }
            catch (Exception)
            {
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Failed to retrieve user capabilities");
            }
        }).RequireRateLimiting("graph-read");

        return app;
    }

    private static string? GetBearer(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        const string p = "Bearer ";
        return !string.IsNullOrWhiteSpace(h) && h.StartsWith(p, StringComparison.OrdinalIgnoreCase)
            ? h[p.Length..].Trim()
            : null;
    }
}