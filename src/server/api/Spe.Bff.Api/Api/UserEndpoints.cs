using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Spe.Bff.Api.Infrastructure.Auth;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;

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
        app.MapGet("/api/me", GetCurrentUserAsync)
            .RequireRateLimiting("graph-read");

        // GET /api/me/capabilities?containerId={containerId} - Get user capabilities for container
        app.MapGet("/api/me/capabilities", async (
            string? containerId,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
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

            try
            {
                logger.LogInformation("Getting user capabilities for container {ContainerId}", containerId);
                var capabilities = await speFileStore.GetUserCapabilitiesAsync(ctx, containerId, ct);
                return TypedResults.Ok(capabilities);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Bearer token is required",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve user capabilities");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while retrieving user capabilities",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read");

        return app;
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext ctx,
        SpeFileStore speFileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var traceId = ctx.TraceIdentifier;

        try
        {
            logger.LogInformation("Getting user information");
            var userInfo = await speFileStore.GetUserInfoAsync(ctx, ct);

            if (userInfo == null)
            {
                return TypedResults.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Invalid or expired token",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            return TypedResults.Ok(userInfo);
        }
        catch (UnauthorizedAccessException)
        {
            return TypedResults.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Bearer token is required",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve user information");
            return TypedResults.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving user information",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }
}
