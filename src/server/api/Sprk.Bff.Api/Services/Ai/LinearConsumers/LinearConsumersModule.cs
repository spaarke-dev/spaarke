using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// DI registration for the Linear AI Consumer library.
/// </summary>
/// <remarks>
/// R7 Wave 12 (2026-07-02) — see
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// Registers the four shared primitives (<see cref="IActionResolver"/>,
/// <see cref="IDocumentTextSource"/>, <see cref="IActionRunner"/>) + the
/// per-consumer services (<see cref="DocumentProfileService"/> tonight;
/// File Summarize / Prefills / DocCreateProfile in follow-on phases).
/// FR-P3-01 (task 040): the <c>LinearConsumers</c> appsettings options block
/// was retired — routing lives on the <c>sprk_playbookconsumer</c> Binding
/// table (single routing surface, ADR-039).
/// </remarks>
public static class LinearConsumersModule
{
    public static IServiceCollection AddLinearConsumers(
        this IServiceCollection services)
    {
        // Primitives — Singleton where stateless; Scoped where an OBO HttpContext
        // is required transitively (DocumentTextSource → AnalysisDocumentLoader
        // → IHttpContextAccessor).
        services.AddSingleton<IActionResolver, ActionResolver>();
        services.AddScoped<IDocumentTextSource, DocumentTextSource>();
        services.AddScoped<ISessionFileTextSource, SessionFileTextSource>();
        services.AddSingleton<IActionRunner, ActionRunner>();

        // Consumer services.
        services.AddScoped<DocumentProfileService>();
        services.AddScoped<FileSummarizeService>();

        return services;
    }
}
