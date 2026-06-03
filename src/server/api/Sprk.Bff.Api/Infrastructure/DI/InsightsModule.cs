using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Api.Insights;
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

        // ILiveFactResolver — D-P12 swap-path-preservation seam.
        //
        // r2 Wave D5 (task 034) — Multi-entity per-entity resolvers per design-a6 §3 + §6.
        // Per A6-D1 (resolver registration pattern), three concrete resolvers are registered
        // unconditionally:
        //   - MatterLiveFactResolver   (renamed from r1's DataverseLiveFactResolver; behavior 1:1)
        //   - ProjectLiveFactResolver  (NEW; reads sprk_project per design-a6 §6.2)
        //   - InvoiceLiveFactResolver  (NEW; reads sprk_invoice per design-a6 §6.3)
        //
        // Dispatched via IReadOnlyDictionary<string, ILiveFactResolver> keyed by entity-type
        // name (lower-case). LiveFactNode (Zone A) parses subjects via ISubjectParser and
        // looks up the right resolver per-call.
        //
        // ADR-032 (BFF Null-Object Kill-Switch Pattern) / bff-extensions.md §F.1 inspection:
        //   - These 3 resolvers are registered UNCONDITIONALLY (outside any `if (flag)` block)
        //     per A6-D6. Their consumer LiveFactNode is registered in AnalysisServicesModule
        //     unconditionally too. The asymmetric-registration anti-pattern does NOT apply.
        //   - If a future task gates an additional resolver behind a feature flag AND that
        //     resolver is consumed by an unconditionally-mapped endpoint, apply ADR-032 P3
        //     (Fail-fast Null-Object — throws FeatureDisabledException) per design-a6 §7. P2
        //     Quiet no-op is FORBIDDEN for query services like ILiveFactResolver per ADR-032.
        //   - Static-scan recipe (per ADR-032 §10): no new `if (flag) { ... }` blocks added
        //     by this registration; pattern verified compliant.
        //
        // DI minimalism (ADR-010) — net delta vs r1 = +4 registrations (parser + 2 new
        // resolvers + dictionary; matter resolver replaces the existing single ILiveFactResolver
        // registration). Within ADR-010 ≤15 non-framework target per design-a6 §3.4 + spec NFR-05.
        //
        // Lifetime per A6-D8:
        //   - Concrete resolvers: Scoped (matches IGenericEntityService and r1 lifetime)
        //   - Subject parser: Singleton (stateless; reads IOptions snapshot at construction)
        //   - Dictionary: Scoped (carries scoped resolvers; lifetime must match)
        //
        // Zone B per SPEC §3.5 — all three resolvers import IGenericEntityService only; ZERO
        // AI-internal imports. Verified by .github/workflows/insights-eval.yml grep gate.
        //
        // Subject scheme catalog binding — supports adding new schemes (e.g., 'client:',
        // 'contract:') via appsettings.json without a code deploy, PROVIDED a matching
        // resolver registration is also added here per the same pattern (A6-D1 + §2.3).
        services.AddOptions<SubjectSchemeCatalogOptions>()
            .BindConfiguration(SubjectSchemeCatalogOptions.SectionName);

        services.AddSingleton<ISubjectParser, SubjectParser>();

        services.AddScoped<MatterLiveFactResolver>();
        services.AddScoped<ProjectLiveFactResolver>();
        services.AddScoped<InvoiceLiveFactResolver>();

        services.AddScoped<IReadOnlyDictionary<string, ILiveFactResolver>>(sp =>
            new Dictionary<string, ILiveFactResolver>(StringComparer.OrdinalIgnoreCase)
            {
                ["matter"]  = sp.GetRequiredService<MatterLiveFactResolver>(),
                ["project"] = sp.GetRequiredService<ProjectLiveFactResolver>(),
                ["invoice"] = sp.GetRequiredService<InvoiceLiveFactResolver>()
            });

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
        // upstream universal-ingest@v1 ObservationEmitterNodeExecutor (Singleton — a
        // Scoped mirror would trigger a captive-dependency warning). Safe because the
        // only DI dependency is IGenericEntityService, which is registered as Singleton
        // in this codebase (see GraphModule.cs: AddSingleton<IGenericEntityService>(...)
        // bridges to IDataverseService Singleton). InsightsMirrorOptions is bound via
        // IOptions pattern (Singleton-safe).
        //
        // Defense-in-depth: DataverseObservationMirror itself handles the "InsightsObservationActionId
        // unset" case by logging a Warning and skipping the write — so even though we swap to the
        // real impl unconditionally, dev/test environments without the deployment-prerequisite
        // sprk_analysisaction row stay safe (no malformed rows). See InsightsMirrorOptions XML doc.
        services.AddOptions<InsightsMirrorOptions>().BindConfiguration(InsightsMirrorOptions.SectionName);
        services.Replace(ServiceDescriptor.Singleton<IObservationMirror, DataverseObservationMirror>());

        // Name → Guid resolution map for /api/insights/ask. Per-env config holds the
        // env-specific Guids so callers can use stable canonical names like
        // "predict-matter-cost@v1" without coupling to Dataverse Guid generation.
        // See InsightsPlaybookNameMapOptions XML doc for config shape + rationale.
        services.AddOptions<InsightsPlaybookNameMapOptions>()
            .BindConfiguration(InsightsPlaybookNameMapOptions.SectionName);

        return services;
    }
}
