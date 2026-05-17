using Sprk.Bff.Api.Services.Ai.Capabilities;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Capabilities services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the multi-provider AI capability services introduced in Spaarke AI Platform Unification R2.
///
/// UNCONDITIONAL registrations (AIPU2-010):
///   1. CapabilityManifest (singleton, also bound as ICapabilityManifest) — in-memory capability catalog
///   2. ICapabilityManifestLoader / DataverseCapabilityManifestLoader (singleton) — Dataverse loader
///   3. CapabilityManifestInitializer (hosted service) — populates manifest at startup
///
/// UNCONDITIONAL registrations (AIPU2-011):
///   4. ManifestRefreshOptions (IOptions) — configures refresh interval + webhook secret
///   5. ManifestRefreshService (singleton hosted service + IManifestRefreshTrigger) —
///      15-minute polling loop + Channel-based webhook wake-up
///
/// Planned registrations (future AIPU2 tasks):
///   6. AiSearchService         — Cross-provider semantic and hybrid search orchestration
///   7. SummarizationService    — Document and conversation summarisation
///   8. CitationService         — Source citation extraction and verification
///   9. MultiProviderAiService  — Provider routing (Azure OpenAI, Anthropic, etc.)
///
/// Prerequisites (must already be registered before calling AddAiCapabilitiesModule):
///   - <c>IConfiguration</c>       — registered by the host
///   - <c>ILogger&lt;T&gt;</c>      — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///   - <c>IChatClient</c>          — registered in AiModule (requires AddAnalysisServicesModule first)
///
/// ADR-009 exception: CapabilityManifest uses singleton in-process cache (not Redis) because
/// capabilities are structural metadata; the sub-millisecond read path must not incur network
/// latency. Runtime refreshes are infrequent (admin-triggered).
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
        // ── AIPU2-010: CapabilityManifest singleton ──────────────────────────
        // Register the concrete singleton and expose it as ICapabilityManifest so
        // consumers only depend on the read interface (no accidental Refresh() calls).
        // The hosted service depends on the concrete type to call Refresh().
        services.AddSingleton<CapabilityManifest>();
        services.AddSingleton<ICapabilityManifest>(sp =>
            sp.GetRequiredService<CapabilityManifest>());

        // Loader: typed HttpClient (one HttpClient per loader instance — ADR-010 pattern).
        // DataverseCapabilityManifestLoader sets its own BaseAddress in the constructor.
        services.AddHttpClient<DataverseCapabilityManifestLoader>();
        services.AddSingleton<ICapabilityManifestLoader>(sp =>
            sp.GetRequiredService<DataverseCapabilityManifestLoader>());

        // Hosted service: populates the manifest before the first HTTP request is served.
        // ADR-001: IHostedService (no Azure Functions).
        services.AddHostedService<CapabilityManifestInitializer>();

        // ── AIPU2-011: ManifestRefreshService ────────────────────────────────
        // Options: bound from "Capabilities" section (RefreshIntervalMinutes, WebhookSecret).
        services
            .AddOptions<ManifestRefreshOptions>()
            .Bind(configuration.GetSection(ManifestRefreshOptions.SectionName));

        // ManifestRefreshService: singleton that also implements IManifestRefreshTrigger.
        // Registered as both IHostedService (for the background loop) and IManifestRefreshTrigger
        // (for the webhook endpoint to inject and call TriggerRefresh()).
        // ADR-010: the concrete type is registered once; the interface forwards to the same instance.
        services.AddSingleton<ManifestRefreshService>();
        services.AddSingleton<IManifestRefreshTrigger>(sp =>
            sp.GetRequiredService<ManifestRefreshService>());
        services.AddHostedService(sp =>
            sp.GetRequiredService<ManifestRefreshService>());

        // TODO AIPU2-xxx: Register AiSearchService, SummarizationService, CitationService, MultiProviderAiService

        return services;
    }
}
