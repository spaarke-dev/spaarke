using Microsoft.AspNetCore.Diagnostics;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Extension methods for configuring the middleware pipeline (post-Build phase).
/// Extracts exception handler, static files, and auth middleware setup from Program.cs.
/// </summary>
public static class MiddlewarePipelineExtensions
{
    /// <summary>
    /// Configures the full middleware pipeline: CORS, static files, exception handler,
    /// authentication, authorization, and rate limiting.
    /// </summary>
    public static void UseSpaarkeMiddleware(this WebApplication app)
    {
        // CORS
        app.UseCors();
        app.UseMiddleware<Sprk.Bff.Api.Api.SecurityHeadersMiddleware>();

        // Static Files - Serve playbook-builder SPA from wwwroot
        app.UseStaticFiles();
        var nestedWwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        if (Directory.Exists(nestedWwwroot))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(nestedWwwroot)
            });
        }

        // Global Exception Handler - RFC 7807 Problem Details
        app.UseExceptionHandler(exceptionHandlerApp =>
        {
            exceptionHandlerApp.Run(async ctx =>
            {
                var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
                var traceId = ctx.TraceIdentifier;

                var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

                (int status, string code, string title, string detail) = exception switch
                {
                    SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail ?? sp.Message),

                    Microsoft.Identity.Client.MsalServiceException ms => (
                        401, "obo_failed", "OBO Token Acquisition Failed",
                        $"Failed to exchange user token for Graph API token: {ms.Message}"
                    ),

                    Microsoft.Graph.Models.ODataErrors.ODataError gs => (
                        (int?)gs.ResponseStatusCode ?? 500, "graph_error", "Graph API Error",
                        gs.Error?.Message ?? gs.Message
                    ),

                    _ => (
                        500, "server_error", "Internal Server Error",
                        $"An unexpected error occurred ({exception?.GetType().Name}). Please check correlation ID in logs."
                    )
                };

                logger.LogError(exception,
                    "Request failed | Status: {StatusCode} | Code: {Code} | Detail: {Detail} | ExceptionType: {ExceptionType} | Path: {Path} | CorrelationId: {CorrelationId}",
                    status, code, detail, exception?.GetType().FullName, ctx.Request.Path, traceId);

                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/problem+json";

                var origin = ctx.Request.Headers.Origin.FirstOrDefault();
                if (!string.IsNullOrEmpty(origin) && !ctx.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
                {
                    ctx.Response.Headers.Append("Access-Control-Allow-Origin", origin);
                    ctx.Response.Headers.Append("Access-Control-Allow-Credentials", "false");
                    logger.LogWarning("Re-applied CORS headers in exception handler for origin: {Origin}", origin);
                }

                await ctx.Response.WriteAsJsonAsync(new
                {
                    type = $"https://spaarke.com/errors/{code}",
                    title,
                    detail,
                    status,
                    correlationId = traceId,
                    extensions = new Dictionary<string, object?>
                    {
                        ["code"] = code,
                        ["correlationId"] = traceId
                    }
                });
            });
        });

        // DEBUG: Log auth header presence for Copilot API plugin diagnostics
        // TODO: Remove before production
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CopilotAuth");
                var authHeader = ctx.Request.Headers.Authorization.ToString();
                if (string.IsNullOrEmpty(authHeader))
                {
                    logger.LogWarning("No Authorization header on {Method} {Path}",
                        ctx.Request.Method, ctx.Request.Path);
                }
                else
                {
                    try
                    {
                        var token = authHeader["Bearer ".Length..].Trim();
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(token);
                        logger.LogWarning(
                            "Token on {Method} {Path}: aud={Audience} iss={Issuer} appid={AppId} scp={Scope}",
                            ctx.Request.Method, ctx.Request.Path,
                            string.Join(",", jwt.Audiences), jwt.Issuer,
                            jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value
                                ?? jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value,
                            jwt.Claims.FirstOrDefault(c => c.Type == "scp")?.Value);
                    }
                    catch
                    {
                        logger.LogWarning("Bearer token present but unreadable on {Method} {Path}",
                            ctx.Request.Method, ctx.Request.Path);
                    }
                }
            }
            await next();
        });

        // Authentication & Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // Rate Limiting (must come after Authentication)
        app.UseRateLimiter();
    }
}
