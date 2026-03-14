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
                    if (allowedOrigins.Contains(origin))
                        return true;

                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        if (uri.Host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase) ||
                            uri.Host == "dynamics.com")
                            return true;

                        if (uri.Host.EndsWith(".powerapps.com", StringComparison.OrdinalIgnoreCase) ||
                            uri.Host == "powerapps.com")
                            return true;

                        if (uri.Host.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase))
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
                          "X-Requested-With",
                          "X-Correlation-Id",
                          "X-Idempotency-Key")
                      .WithExposedHeaders(
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
