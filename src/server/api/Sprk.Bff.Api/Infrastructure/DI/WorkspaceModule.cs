using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the Legal Operations Workspace feature.
/// </summary>
/// <remarks>
/// Follows ADR-010: DI minimalism — concrete type registrations only, no unnecessary interfaces.
/// Registration count: 8 (PortfolioService, PriorityScoringService, EffortScoringService, WorkspaceAiService, BriefingService, MatterPreFillService, TodoGenerationOptions, TodoGenerationService).
///
/// Prerequisites (must already be registered before calling AddWorkspaceServices):
/// - <c>IDistributedCache</c> — registered via <c>AddStackExchangeRedisCache</c> in Program.cs
/// - <c>ILogger&lt;T&gt;</c> — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddWorkspaceServices();
/// </code>
///
/// And to register endpoint routes:
/// <code>
/// app.MapWorkspaceEndpoints();
/// app.MapWorkspaceAiEndpoints();
/// </code>
/// </remarks>
public static class WorkspaceModule
{
    /// <summary>
    /// Registers workspace services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (used to bind TodoGeneration options). Optional.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorkspaceServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // PortfolioService: Scoped because it accesses IDistributedCache per-request
        // and will eventually hold per-request Dataverse query context.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddScoped<PortfolioService>();

        // PriorityScoringService: Singleton — stateless, table-driven, thread-safe.
        // Same inputs always produce identical outputs; no per-request state needed.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddSingleton<PriorityScoringService>();

        // EffortScoringService: Singleton — stateless, table-driven, thread-safe.
        // Uses a readonly base-effort dictionary and applies multipliers deterministically.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddSingleton<EffortScoringService>();

        // WorkspaceAiService: Scoped because it will eventually hold per-request Dataverse
        // query context and AI Playbook orchestration state.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddScoped<WorkspaceAiService>();

        // BriefingService: Scoped because it depends on PortfolioService (also scoped).
        // The IOpenAiClient dependency is resolved as optional (null-safe) — when not registered
        // (DocumentIntelligence:Enabled = false), the service falls back to template narrative.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddScoped<BriefingService>();

        // MatterPreFillService: Scoped to match HttpContext lifetime used for OBO file uploads.
        // Depends on SpeFileStore (singleton), ITextExtractor (singleton), IOpenAiClient (singleton).
        // Concrete registration per ADR-010 (no interface seam needed for single implementation).
        services.AddScoped<MatterPreFillService>();

        // TodoGenerationOptions: bind from "TodoGeneration" appsettings section.
        // Defaults apply when the section is absent (IntervalHours=24, StartHourUtc=2).
        if (configuration != null)
        {
            services.Configure<TodoGenerationOptions>(
                configuration.GetSection(TodoGenerationOptions.SectionName));
        }
        else
        {
            services.AddOptions<TodoGenerationOptions>();
        }

        // TodoGenerationService: BackgroundService with 24-hour PeriodicTimer (ADR-001 mandate).
        // Singleton lifetime is safe — IDataverseService is also a Singleton.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddHostedService<TodoGenerationService>();

        return services;
    }
}
