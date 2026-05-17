namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Capabilities services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the multi-provider AI capability services introduced in Spaarke AI Platform Unification R2.
///
/// UNCONDITIONAL registrations (planned — registered by future AIPU2 tasks):
///   1. AiSearchService         — Cross-provider semantic and hybrid search orchestration
///   2. SummarizationService    — Document and conversation summarisation
///   3. CitationService         — Source citation extraction and verification
///   4. MultiProviderAiService  — Provider routing (Azure OpenAI, Anthropic, etc.)
///
/// Prerequisites (must already be registered before calling AddAiCapabilitiesModule):
///   - <c>IConfiguration</c>       — registered by the host
///   - <c>ILogger&lt;T&gt;</c>      — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///   - <c>IChatClient</c>          — registered in AiModule (requires AddAnalysisServicesModule first)
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddAiCapabilitiesModule(builder.Configuration);
/// </code>
/// </remarks>
public static class AiCapabilitiesModule
{
    /// <summary>
    /// Registers AI Capabilities services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiCapabilitiesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TODO AIPU2-xxx: Register AiSearchService, SummarizationService, CitationService, MultiProviderAiService

        return services;
    }
}
