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

        // Authentication & Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // Rate Limiting (must come after Authentication)
        app.UseRateLimiter();
    }
}
