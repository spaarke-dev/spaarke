using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// DI registration for the Linear AI Consumer library.
/// </summary>
/// <remarks>
/// R7 Wave 12 (2026-07-02) — see
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// Registers the shared executor primitives (<see cref="IActionResolver"/>,
/// <see cref="IDocumentTextSource"/>, <see cref="ISessionFileTextSource"/>,
/// <see cref="IActionRunner"/>).
/// FR-P3-01 (task 040): the <c>LinearConsumers</c> appsettings options block
/// was retired — routing lives on the <c>sprk_playbookconsumer</c> Binding
/// table (single routing surface, ADR-039).
/// FR-P3-05 (task 044): the consumer-specific wrapper classes were absorbed
/// into their call sites — endpoints compose the primitives directly.
/// </remarks>
public static class LinearConsumersModule
{
    public static IServiceCollection AddLinearConsumers(
        this IServiceCollection services)
    {
        // Primitives — Singleton where stateless; Scoped where an OBO HttpContext
        // is required transitively (DocumentTextSource → AnalysisDocumentLoader
        // → IHttpContextAccessor).
        // ActionResolver is Scoped (dotnet-10-upgrade task 020, R5): it captures the
        // Scoped IConsumerRoutingService and is consumed EXCLUSIVELY by Scoped services
        // (CommunicationTriage/Propose/CreateTaskAi, AssistantSuggestionService) and
        // per-request endpoint params — never by a singleton — so demotion is the
        // behavior-preserving fix under ValidateScopes. The compound-OFF NullActionResolver
        // peer (AnalysisServicesModule) is demoted symmetrically per ADR-032.
        services.AddScoped<IActionResolver, ActionResolver>();
        services.AddScoped<IDocumentTextSource, DocumentTextSource>();
        services.AddScoped<ISessionFileTextSource, SessionFileTextSource>();
        services.AddSingleton<IActionRunner, ActionRunner>();

        return services;
    }
}
