using Spe.Bff.Api.Infrastructure.Errors;
using Services;
using Microsoft.AspNetCore.Mvc;

namespace Spe.Bff.Api.Api;

/// <summary>
/// User identity and capabilities endpoints following ADR-008.
/// Groups all user-related operations with consistent error handling.
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me - Get current user info
        app.MapGet("/api/me", async (
            HttpContext ctx,
            [FromServices] IOboSpeService oboSvc,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var traceId = ctx.TraceIdentifier;
            var bearer = GetBearer(ctx);

            if (string.IsNullOrEmpty(bearer))
            {
                return Results.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Bearer token is required",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            try
            {
                logger.LogInformation("Getting user information");
                var userInfo = await oboSvc.GetUserInfoAsync(bearer, ct);

                if (userInfo == null)
                {
                    return Results.Problem(
                        statusCode: 401,
                        title: "Unauthorized",
                        detail: "Invalid or expired token",
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                        extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
                }

                return Results.Ok(userInfo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve user information");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while retrieving user information",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        }); // TODO: .RequireRateLimiting("graph-read");

        // GET /api/me/capabilities?containerId={containerId} - Get user capabilities for container
        app.MapGet("/api/me/capabilities", async (
            string? containerId,
            HttpContext ctx,
            [FromServices] IOboSpeService oboSvc,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var traceId = ctx.TraceIdentifier;

            if (string.IsNullOrWhiteSpace(containerId))
            {
                return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["containerId"] = ["containerId query parameter is required"]
                });
            }

            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer))
            {
                return Results.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Bearer token is required",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            try
            {
                logger.LogInformation("Getting user capabilities for container {ContainerId}", containerId);
                var capabilities = await oboSvc.GetUserCapabilitiesAsync(bearer, containerId, ct);
                return Results.Ok(capabilities);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve user capabilities");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while retrieving user capabilities",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        }); // TODO: .RequireRateLimiting("graph-read");

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