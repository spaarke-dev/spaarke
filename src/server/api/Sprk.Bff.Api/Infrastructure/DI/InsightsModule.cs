using Sprk.Bff.Api.Services.Insights.Graph;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Spaarke Insights Engine (Zone B per SPEC §3.5 facade boundary).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: Insights Engine domain types and Zone B services living under
/// <c>Services/Insights/</c>, <c>Api/Insights/</c>, <c>Models/Insights/</c>.
/// AI internals (extraction nodes, synthesis orchestration, playbook engine) live
/// in Zone A under <c>Services/Ai/</c> and are wired by <c>AnalysisServicesModule</c>.
/// </para>
/// <para>
/// <b>Phase 1 contents</b>: just the <see cref="IInsightGraph"/> stub (D-P17).
/// As Phase 1 progresses, D-P1 envelope POCOs, D-P11 observation mirror service,
/// and others will register here.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>: this is a new feature module justified by the
/// §3.5 zone boundary — Insights Zone B code MUST NOT live inside the Zone A
/// AnalysisServicesModule (which freely imports AI internals). Keeping the two
/// registration entry points separate makes the boundary visible in DI wiring,
/// which is exactly what ADR-010's "make the composition obvious" goal calls for.
/// Module-level interface seam is still a real seam (D-P17 swap path between
/// <see cref="StubInsightGraph"/> in Phase 1 and <c>CosmosNoSqlInsightGraph</c>
/// in Phase 1.5) — satisfies ADR-010 §Exceptions.
/// </para>
/// </remarks>
public static class InsightsModule
{
    /// <summary>
    /// Registers Insights Engine Zone B services. Call from Program.cs alongside
    /// the other feature modules.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInsightsModule(this IServiceCollection services)
    {
        // IInsightGraph — D-P17 swap-path-preservation seam. Phase 1: StubInsightGraph
        // throws NotImplementedException with a "Phase 1.5" message. Replace this
        // line when CosmosNoSqlInsightGraph lands as the first Phase 1.5 deliverable
        // (SPEC §3.3). Singleton: the future Cosmos impl will hold a CosmosClient
        // which is itself thread-safe and intended to be reused; the stub is
        // stateless so lifetime is irrelevant for Phase 1.
        services.AddSingleton<IInsightGraph, StubInsightGraph>();

        return services;
    }
}
