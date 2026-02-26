namespace Sprk.Bff.Api.Services.Ai.RecordSearch;

/// <summary>
/// DI extension methods for registering Record Search services.
/// Follows ADR-010 feature module pattern.
/// </summary>
public static class RecordSearchExtensions
{
    /// <summary>
    /// Adds Record Search services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers the following services:
    /// <list type="bullet">
    /// <item>IRecordSearchService → RecordSearchService (scoped)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Dependencies (must be registered separately):
    /// <list type="bullet">
    /// <item>SearchIndexClient — registered by AI infrastructure setup</item>
    /// <item>IOpenAiClient — registered by AI module setup</item>
    /// <item>IEmbeddingCache — registered by AI module setup</item>
    /// <item>IDistributedCache — registered by Redis configuration</item>
    /// <item>IOptions&lt;DocumentIntelligenceOptions&gt; — registered by configuration binding</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRecordSearch(this IServiceCollection services)
    {
        // Register the record search service
        // Scoped: uses scoped dependencies (IOpenAiClient) and per-request lifetime
        services.AddScoped<IRecordSearchService, RecordSearchService>();

        return services;
    }
}
