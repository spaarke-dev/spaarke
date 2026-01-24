using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Dependency injection module for Office add-in services.
/// </summary>
/// <remarks>
/// <para>
/// Registers services required for Office add-in operations:
/// - IOfficeService: Main service for save, share, search operations
/// - IOfficeRateLimitService: Rate limiting for Office endpoints (Task 031)
/// - Authorization filters for Office-specific policies
/// </para>
/// <para>
/// Per ADR-010, this module maintains minimal DI registrations.
/// Services are scoped to match the request lifecycle for proper
/// authorization context handling.
/// </para>
/// </remarks>
public static class OfficeModule
{
    /// <summary>
    /// Adds Office add-in services to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOfficeModule(this IServiceCollection services)
    {
        // ============================================================================
        // Office Add-in Service
        // ============================================================================
        // Scoped lifetime - new instance per request for proper auth context
        services.AddScoped<IOfficeService, OfficeService>();

        // ============================================================================
        // Rate Limiting Configuration and Service (Task 031)
        // ============================================================================
        // Configuration with default values - can be overridden in appsettings.json
        // Rate limits per spec.md:
        //   - Save: 10 requests/minute/user
        //   - QuickCreate: 5 requests/minute/user
        //   - Search: 30 requests/minute/user
        //   - Jobs: 60 requests/minute/user
        //   - Share: 20 requests/minute/user
        //   - Recent: 30 requests/minute/user
        services.AddOptions<OfficeRateLimitOptions>()
            .BindConfiguration(OfficeRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton for efficiency - state is stored in Redis, service is stateless
        // Uses IDistributedCache for distributed rate limit state
        services.AddSingleton<IOfficeRateLimitService, OfficeRateLimitService>();

        // ============================================================================
        // Job Status Service (Task 064)
        // ============================================================================
        // Singleton for efficient Redis pub/sub handling
        // Bridges background workers to SSE clients for real-time job status updates
        // Redis connection is injected via IConnectionMultiplexer (optional - graceful degradation)
        // Use factory to gracefully handle case when Redis is not configured
        services.AddSingleton<IJobStatusService>(sp =>
        {
            var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<JobStatusService>>();
            return new JobStatusService(redis, logger);
        });

        // ============================================================================
        // Authorization Filters (to be added in task 033)
        // ============================================================================
        // TODO: Add Office-specific authorization filters
        // services.AddScoped<OfficeAuthorizationFilter>();

        return services;
    }
}
