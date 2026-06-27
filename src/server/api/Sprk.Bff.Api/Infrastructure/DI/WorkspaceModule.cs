using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the Legal Operations Workspace feature.
/// </summary>
/// <remarks>
/// Follows ADR-010: DI minimalism — concrete type registrations only except where a single-impl + test seam justifies an interface (see IGuidProvider below).
/// Registration count: 11 (TimeProvider, IGuidProvider, WorkspaceLayoutService, PortfolioService, PriorityScoringService, EffortScoringService, WorkspaceAiService, BriefingService, MatterPreFillService, ProjectPreFillService, TodoGenerationOptions, TodoGenerationService).
/// MatterPreFillService now uses IPlaybookOrchestrationService (registered in AiModule) instead of IOpenAiClient.
/// TimeProvider + IGuidProvider added by Phase 4 Track C TestClock PoC (FR-13 / task 042) — TryAdd semantics so production code is not displaced by tests' alternate registrations.
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
        // ── Phase 4 Track C determinism PoC (FR-13 / task 042) ──────────────────
        // TimeProvider: BCL .NET 8 clock abstraction. Registered as Singleton with
        // TryAdd semantics so that callers that already register it (or a custom
        // implementation) elsewhere are not displaced. Production resolves to
        // TimeProvider.System; tests inject a deterministic implementation
        // (e.g., FixedTimeProvider) for time-based assertions on services such as
        // PortfolioService (Phase 4 PoC consumer). See projects/sdap.bff.api-
        // test-suite-repair-r2/design.md §5.5 Track C.
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        // IGuidProvider: bespoke seam (ADR-010 allowed-seam: single-impl + test
        // seam justifies the interface). Greenfield as of task 042; consumer
        // migration (e.g., MatterPreFillService.requestId) is the r3
        // generalization step. Production impl is stateless + thread-safe.
        services.TryAddSingleton<IGuidProvider, DefaultGuidProvider>();

        // WorkspaceLayoutService: Scoped because it depends on per-request Dataverse query context.
        // Handles CRUD for sprk_workspacelayout entity with user-scoped queries.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddScoped<WorkspaceLayoutService>();

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

        // WorkspaceAiService: Scoped because it depends on IPlaybookOrchestrationService (scoped)
        // and will hold per-request Dataverse query context.
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddScoped<WorkspaceAiService>();

        // BriefingService: Scoped because it depends on PortfolioService (also scoped),
        // IDataverseService (Scoped — used for the AAD-oid → systemuserid cross-reference per
        // ADR-028 and for the membership-resolved matter detail query), and
        // IMembershipResolverService (Singleton — registered by MembershipModule; safe to
        // inject into Scoped consumers, no captive-dependency issue). The IBriefingAi
        // facade is resolved as optional (null-safe) — when not registered
        // (DocumentIntelligence:Enabled = false), the service falls back to template narrative.
        // Concrete registration per ADR-010 (no interface seam needed).
        //
        // Top-priority-matter wiring (Wave 28 / GitHub #229 closeout, 2026-06-22):
        //   The prior STUB in BriefingService.GetTopPriorityMatterAsync now routes through
        //   IMembershipResolverService per ADR-034 (canonical user-record membership). The
        //   resolver and IDataverseService are pre-existing BFF registrations — no new DI
        //   binding is required here. See docs/architecture/membership-resolution-pattern.md
        //   "Wiring + Consumer Inventory (AS-BUILT)" for the updated consumer list.
        services.AddScoped<BriefingService>();

        // MatterPreFillService: Scoped to match HttpContext lifetime used for OBO file uploads.
        // Depends on SpeFileStore (singleton), ITextExtractor (singleton),
        // IPlaybookOrchestrationService (scoped) for AI extraction via playbook system (ADR-013).
        // Concrete registration per ADR-010 (no interface seam needed for single implementation).
        services.AddScoped<MatterPreFillService>();

        // ProjectPreFillService: Scoped to match HttpContext lifetime used for OBO file uploads.
        // Same dependency pattern as MatterPreFillService but returns project-specific field names.
        // Concrete registration per ADR-010 (no interface seam needed for single implementation).
        services.AddScoped<ProjectPreFillService>();

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
        // Uses IServiceProvider to lazily resolve IDataverseService after host startup
        // (avoids 500.30 if Dataverse connection fails during cold start).
        // Concrete registration per ADR-010 (no interface seam needed).
        services.AddHostedService<TodoGenerationService>();

        return services;
    }
}
