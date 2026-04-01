namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// DI registration and endpoint mapping for the Reporting module.
///
/// Follows ADR-010 (DI minimalism — exactly 2 non-framework registrations):
///   1. <see cref="ReportingEmbedService"/>  — MSAL + Power BI embed token generation.
///   2. <see cref="ReportingProfileManager"/> — service principal profile cache.
///
/// <see cref="ReportingAuthorizationFilter"/> is an endpoint filter applied at route-group
/// level via <see cref="ReportingAuthorizationFilterExtensions"/>; it is NOT registered in DI.
///
/// PowerBiOptions is registered by task 001 (ConfigurationModule.AddConfigurationModule).
/// Do NOT re-register it here.
/// </summary>
public static class ReportingModule
{
    /// <summary>
    /// Registers Reporting module services into the DI container.
    /// Call this from <c>Program.cs</c> during the service-registration phase.
    /// </summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    /// <param name="configuration">The application's <see cref="IConfiguration"/> (reserved for future use).</param>
    /// <returns><paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1 of 2 — ADR-010: holds MSAL ConfidentialClientApplication (stateful, thread-safe singleton).
        services.AddSingleton<ReportingEmbedService>();

        // 2 of 2 — ADR-010: holds in-memory service principal profile cache (ConcurrentDictionary).
        services.AddSingleton<ReportingProfileManager>();

        return services;
    }

    /// <summary>
    /// Maps all Reporting API endpoint groups onto the application.
    /// Call this from <c>EndpointMappingExtensions.MapDomainEndpoints</c> after the app is built.
    ///
    /// Implementation is added by task 005 (ReportingEndpoints.cs). This stub ensures that
    /// <c>Program.cs</c> and <c>EndpointMappingExtensions</c> compile cleanly before task 005
    /// lands, and that wiring is already in place when endpoints are added.
    /// </summary>
    /// <param name="app">The built <see cref="WebApplication"/>.</param>
    public static void MapReportingEndpoints(this WebApplication app)
    {
        // TODO (task 005): map /api/reporting/* endpoint groups here.
        // Example:
        //   var group = app.MapGroup("/api/reporting")
        //       .AddReportingAuthorizationFilter()
        //       .RequireAuthorization();
        //   group.MapGet("/embed-config", ReportingEndpoints.GetEmbedConfig);
    }
}
