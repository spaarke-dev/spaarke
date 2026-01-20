namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// DI extension methods for registering Semantic Search services.
/// Follows ADR-010 feature module pattern.
/// </summary>
public static class SemanticSearchExtensions
{
    /// <summary>
    /// Adds Semantic Search services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers the following services:
    /// <list type="bullet">
    /// <item>ISemanticSearchService → SemanticSearchService (scoped)</item>
    /// <item>IQueryPreprocessor → NoOpQueryPreprocessor (singleton - R1)</item>
    /// <item>IResultPostprocessor → NoOpResultPostprocessor (singleton - R1)</item>
    /// </list>
    /// </para>
    /// <para>
    /// For R1, no-op implementations are used for preprocessor and postprocessor.
    /// These are extensibility hooks for future agentic RAG capabilities.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSemanticSearch(this IServiceCollection services)
    {
        // Register no-op extensibility hooks (R1)
        // Singleton: stateless, no overhead
        services.AddSingleton<IQueryPreprocessor, NoOpQueryPreprocessor>();
        services.AddSingleton<IResultPostprocessor, NoOpResultPostprocessor>();

        // Register the semantic search service
        // Scoped: uses scoped dependencies (IKnowledgeDeploymentService, IOpenAiClient)
        services.AddScoped<ISemanticSearchService, SemanticSearchService>();

        return services;
    }
}
