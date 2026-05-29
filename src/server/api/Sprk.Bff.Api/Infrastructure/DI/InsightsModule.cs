using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Insights.Graph;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Sprk.Bff.Api.Services.Insights.Observations;
using Sprk.Bff.Api.Services.Insights.Precedents;

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

        // IPrecedentBoard — D-P3 manual SME authoring of Precedents (task 012).
        // Scoped because the underlying IGenericEntityService is resolved per-request
        // and the board carries no state of its own. The implementation consumes only
        // IGenericEntityService (Zone B) — no AI internals are imported.
        services.AddScoped<IPrecedentBoard, DataversePrecedentBoard>();

        // IPrecedentProjectionSync — D-P4 Precedent → spaarke-insights-index projection (task 041).
        // Scoped because IPrecedentBoard above is Scoped (consistent lifetime — avoids the captive
        // dependency warning that would arise from a Singleton consuming a Scoped). Zone B per
        // SPEC §3.5: imports IInsightsAi (the §3.5 facade, the ONLY Zone-A type permitted in
        // Zone B per project CLAUDE.md §3.5.4) for embedding generation, plus the standalone
        // Azure.Search.Documents.Indexes.SearchIndexClient already registered in Program.cs.
        services.AddScoped<IPrecedentProjectionSync, PrecedentProjectionSync>();

        // ILiveFactResolver — D-P12 task 022 swap-path-preservation seam.
        // Task 071 (Wave 8.5 pre-deploy gap fix, 2026-05-29): swapped from StubLiveFactResolver
        // to DataverseLiveFactResolver. The stub threw LiveFactNotSupportedException on every
        // call, which broke the predict-matter-cost playbook (D-P14 task 060) — the playbook's
        // first node is a LiveFactNode that reads matter facts from sprk_matter. Stub stays in
        // place at Services/Insights/LiveFacts/StubLiveFactResolver.cs as a test-only fallback
        // (tests inject their own ILiveFactResolver; the stub is not consumed in production).
        //
        // Scoped lifetime: matches DataversePrecedentBoard (Zone B Dataverse-read pattern) and
        // matches IGenericEntityService which is typically resolved per-request. The previous
        // Singleton lifetime worked only because the stub was stateless and never invoked.
        //
        // Zone B per SPEC §3.5 — DataverseLiveFactResolver imports IGenericEntityService only;
        // ZERO AI-internal imports. Verified by .github/workflows/insights-eval.yml grep gate.
        services.AddScoped<ILiveFactResolver, DataverseLiveFactResolver>();

        // IObservationMirror — D-P11 task 051. SWAP OUT the NoOp registered by
        // InsightsIngestModule (Zone A) with the real DataverseObservationMirror impl
        // (Zone B). The IObservationMirror interface lives in Services/Ai/PublicContracts/
        // (the canonical cross-zone seam per §3.5.4 — same pattern as IInsightsAi).
        //
        // Why Replace rather than overwrite-by-last-wins: explicit intent. AddInsightsModule
        // is registered AFTER AddInsightsIngestModule in Program.cs, so the last-wins behavior
        // would already work — but Replace makes the swap a visible architectural decision
        // and would fail loudly if a future refactor changed the registration order.
        //
        // Singleton lifetime: matches the upstream NoOp registration AND matches the
        // upstream IIngestOrchestrator (Singleton, consumes IObservationMirror — a Scoped
        // mirror would trigger a captive-dependency warning). Safe because the only DI
        // dependency is IGenericEntityService, which is registered as Singleton in this
        // codebase (see GraphModule.cs: AddSingleton<IGenericEntityService>(...) bridges
        // to IDataverseService Singleton). InsightsMirrorOptions is bound via IOptions
        // pattern (Singleton-safe).
        //
        // Defense-in-depth: DataverseObservationMirror itself handles the "InsightsObservationActionId
        // unset" case by logging a Warning and skipping the write — so even though we swap to the
        // real impl unconditionally, dev/test environments without the deployment-prerequisite
        // sprk_analysisaction row stay safe (no malformed rows). See InsightsMirrorOptions XML doc.
        services.AddOptions<InsightsMirrorOptions>().BindConfiguration(InsightsMirrorOptions.SectionName);
        services.Replace(ServiceDescriptor.Singleton<IObservationMirror, DataverseObservationMirror>());

        return services;
    }
}
