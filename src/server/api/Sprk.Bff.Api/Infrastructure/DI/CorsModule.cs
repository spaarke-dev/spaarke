namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for CORS configuration (ADR-010).
/// Configures secure, fail-closed CORS with Dataverse/PowerApps/Office Add-in wildcard support.
/// </summary>
public static class CorsModule
{
    /// <summary>
    /// Adds CORS services with secure origin validation and fail-closed behavior in production.
    /// </summary>
    public static IServiceCollection AddCorsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        var isTestOrDevelopment = environment.IsDevelopment() ||
                                  environment.EnvironmentName == "Testing";

        if (allowedOrigins == null || allowedOrigins.Length == 0)
        {
            if (isTestOrDevelopment)
            {
                var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
                logger.LogWarning(
                    "CORS: No allowed origins configured. Falling back to localhost (development only).");

                allowedOrigins = new[]
                {
                    "http://localhost:3000",
                    "http://localhost:3001",
                    "http://127.0.0.1:3000"
                };
            }
            else
            {
                throw new InvalidOperationException(
                    $"CORS configuration is missing or empty in {environment.EnvironmentName} environment. " +
                    "Configure 'Cors:AllowedOrigins' with explicit origin URLs. " +
                    "CORS will NOT fall back to AllowAnyOrigin for security reasons.");
            }
        }

        if (allowedOrigins.Contains("*"))
        {
            throw new InvalidOperationException(
                "CORS: Wildcard origin '*' is not allowed. " +
                "Configure explicit origin URLs in 'Cors:AllowedOrigins'.");
        }

        foreach (var origin in allowedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException(
                    $"CORS: Invalid origin URL '{origin}'. Must be absolute URL (e.g., https://example.com).");
            }

            if (uri.Scheme != "https" && !isTestOrDevelopment)
            {
                throw new InvalidOperationException(
                    $"CORS: Non-HTTPS origin '{origin}' is not allowed in {environment.EnvironmentName} environment. " +
                    "Use HTTPS URLs for security.");
            }
        }

        {
            var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
            logger.LogInformation(
                "CORS: Configured with {OriginCount} allowed origins: {Origins}",
                allowedOrigins.Length,
                string.Join(", ", allowedOrigins));
        }

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    // Explicit, config-driven allow-list (exact origin match). This is the single
                    // source for our Azure Static Web Apps SPA origins (external-access SPA + Office
                    // Add-ins) — enumerate each in 'Cors:AllowedOrigins'. See appsettings.template.json.
                    if (allowedOrigins.Contains(origin))
                        return true;

                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        // First-party Microsoft app-host domains (Dataverse model-driven app + Power
                        // Apps) that embed our web resources. Per-tenant, Microsoft-provisioned.
                        if (uri.Host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase) ||
                            uri.Host == "dynamics.com")
                            return true;

                        if (uri.Host.EndsWith(".powerapps.com", StringComparison.OrdinalIgnoreCase) ||
                            uri.Host == "powerapps.com")
                            return true;

                        // SECURITY (r3 task 030 / FR-17, D3-03): the former blanket
                        // ".azurestaticapps.net" suffix rule was REMOVED. azurestaticapps.net is a
                        // shared, third-party-registrable domain; combined with AllowCredentials it
                        // let ANY attacker-owned SWA make credentialed cross-origin calls. Our own
                        // SWA origins are now allowed only via the explicit 'Cors:AllowedOrigins'
                        // config list above (fail-closed). NOTE: ".powerappsportals.com" below is the
                        // same shared-domain risk class (D3-03) and should be migrated to the explicit
                        // list too, once the live Power Pages origin is verified (Tranche B).
                        if (uri.Host.EndsWith(".powerappsportals.com", StringComparison.OrdinalIgnoreCase) ||
                            uri.Host == "powerappsportals.com")
                            return true;
                    }

                    return false;
                })
                      .AllowCredentials()
                      .AllowAnyMethod()
                      .WithHeaders(
                          "Authorization",
                          "Content-Type",
                          "Accept",
                          "If-Match",
                          "If-None-Match",
                          "X-Requested-With",
                          "X-Correlation-Id",
                          "X-Idempotency-Key",
                          // KEEP. Task 059 stopped the BFF from READING this header, but the browser
                          // SSE path (useSseStream.ts readSseStream) still SENDS it. Dropping it here
                          // would make the CORS preflight reject the request outright — turning an
                          // ignored header into a broken chat stream. Remove only together with the
                          // client-side send.
                          "X-Tenant-Id",
                          "request-id",
                          "client-request-id",
                          "traceparent",
                          "tracestate",
                          // SSE job-progress streams (Office add-in SaveFlow → SseClient.ts).
                          // Neither is a CORS-safelisted request header, so both MUST be listed
                          // explicitly or the preflight fails with "Request header field
                          // cache-control is not allowed by Access-Control-Allow-Headers".
                          // Last-Event-ID is only sent on RECONNECT, so omitting it fails later
                          // and more confusingly than Cache-Control does.
                          "Cache-Control",
                          "Last-Event-ID")
                      .WithExposedHeaders(
                          "ETag",
                          "request-id",
                          "client-request-id",
                          "traceparent",
                          "X-Correlation-Id",
                          "X-Pagination-TotalCount",
                          "X-Pagination-HasMore")
                      .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        return services;
    }
}
