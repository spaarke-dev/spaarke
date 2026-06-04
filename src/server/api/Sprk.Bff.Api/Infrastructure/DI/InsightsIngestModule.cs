using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Mirror;
using Sprk.Bff.Api.Services.Ai.Insights.Nodes;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Spaarke Insights Engine universal ingest pipeline (D-P7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone</b>: A per <c>SPEC §3.5</c> — lives under <c>Services/Ai/Insights/Ingest/</c>
/// + sibling Zone A subnamespaces (<c>Sanitization/</c>, <c>Mirror/</c>). Zone B never
/// imports these registrations; the orchestrator surfaces through
/// <see cref="PublicContracts.IInsightsAi.RunIngestAsync"/>.
/// </para>
/// <para>
/// <b>Wave C-G4 (task 022) retirement</b>: the legacy <c>IIngestOrchestrator</c> +
/// <c>IInsightsPromptLoader</c> registrations have been removed along with their
/// concrete implementations. The universal-ingest pipeline now runs entirely through the
/// <c>universal-ingest@v1</c> JPS playbook — orchestrated by
/// <see cref="IPlaybookOrchestrationService.ExecuteAppOnlyAsync"/> via
/// <see cref="Insights.InsightsOrchestrator"/>. Prompt content lives in
/// <c>sprk_analysisaction.sprk_systemprompt</c> rows, not on-disk <c>.txt</c> files.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>New feature module rather than extending <see cref="AiModule"/> (at 15/15 cap) or
///   <see cref="InsightsExtractionModule"/> (different concern — extraction primitives vs
///   pipeline orchestration) or <see cref="InsightsFacadeModule"/> (Zone A public surface).
///   Keeps the §3.5 boundary visible in DI composition.</item>
///   <item>Four interface seams (<see cref="IInsightsContentSanitizer"/>,
///   <see cref="IObservationMirror"/>, <see cref="IIngestDocumentSource"/>,
///   <see cref="IObservationIndexUpserter"/>). Each is justified per ADR-010 §Exceptions:
///   <list type="bullet">
///     <item><see cref="IInsightsContentSanitizer"/> — Phase 1.5+ LAVERN Sanitizer swap path.</item>
///     <item><see cref="IObservationMirror"/> — Phase 1 NoOp + Phase 1.5+ Dataverse impl (task 051) swap path.</item>
///     <item><see cref="IIngestDocumentSource"/> — testability seam (orchestrator unit-tested without real Azure Search).</item>
///     <item><see cref="IObservationIndexUpserter"/> — testability seam (orchestrator unit-tested without real Azure Search + embeddings).</item>
///   </list>
///   </item>
///   <item>All singletons — concrete impls are stateless wrappers over thread-safe dependencies.</item>
/// </list>
/// </para>
/// <para>
/// <b>Prerequisites</b> (registered upstream):
/// <list type="bullet">
///   <item><see cref="IOpenAiClient"/> — AnalysisServicesModule (constrained-decoding completions + embeddings).</item>
///   <item><see cref="CitationVerification.IGroundingVerifier"/> — AnalysisServicesModule (D-P9 task 020).</item>
///   <item><see cref="Extraction.ILayer1ClassificationEmitter"/> — InsightsExtractionModule (D-P5 task 030).</item>
///   <item><see cref="Extraction.IObservationEmitter"/> — InsightsExtractionModule (D-P10 task 021).</item>
///   <item><see cref="Azure.Search.Documents.Indexes.SearchIndexClient"/> — AnalysisServicesModule.</item>
///   <item><see cref="TimeProvider"/> — Microsoft.Extensions DI default; we register <see cref="TimeProvider.System"/> here if absent.</item>
/// </list>
/// </para>
/// </remarks>
public static class InsightsIngestModule
{
    /// <summary>
    /// Registers the universal ingest pipeline Zone A services. Call from
    /// <c>Program.cs</c> after <c>AddAnalysisServicesModule</c> + <c>AddInsightsExtractionModule</c>.
    /// </summary>
    public static IServiceCollection AddInsightsIngestModule(this IServiceCollection services)
    {
        // TimeProvider — register System default if not already registered (lets tests
        // substitute a FakeTimeProvider for deterministic AsOf timestamps).
        services.TryAddSingleton(TimeProvider.System);

        // D-50 / D-A25 — minimal-viable sanitizer (Phase 1.5+ LAVERN swap path).
        services.AddSingleton<IInsightsContentSanitizer, InsightsContentSanitizer>();

        // D-P11 — mirror seam (Phase 1 no-op; task 051 swaps in DataverseObservationMirror).
        services.AddSingleton<IObservationMirror, NoOpObservationMirror>();

        // D-P7 supporting services. Document fetch is owned by IInsightsAi.RunIngestAsync
        // (the facade pre-fetches and injects documentText + chunksJson into playbook
        // parameters per design-a5 §4 Node 1).
        services.AddSingleton<IIngestDocumentSource, FilesIndexIngestDocumentSource>();
        services.AddSingleton<IObservationIndexUpserter, ObservationIndexUpserter>();

        // Wave C-G4 (task 022): legacy IIngestOrchestrator + IInsightsPromptLoader
        // registrations RETIRED. Universal-ingest is now a JPS playbook (universal-ingest@v1)
        // executed via IPlaybookOrchestrationService; prompts live in
        // sprk_analysisaction.sprk_systemprompt rows, not on-disk .txt files.

        // ====================================================================
        // Wave C1 task 020 — universal-ingest@v1 JPS playbook node executors.
        // ====================================================================
        // Two new INodeExecutor registrations for the universal-ingest@v1 playbook (D-P15-02
        // ONE canonical playbook, parameterized — replaces code-defined IngestOrchestrator.cs
        // on Wave C3). Auto-discovered by NodeExecutorRegistry via SupportedActionTypes:
        //   - SanitizerNodeExecutor → ActionType.Sanitization (130) — wraps IInsightsContentSanitizer
        //   - ObservationEmitterNodeExecutor → ActionType.ObservationEmit (140) — wraps
        //     IObservationEmitter + IObservationIndexUpserter + IObservationMirror
        //
        // ADR-030 §F.1 inspection (Asymmetric-Registration Tier 1.5):
        //   - These are UNCONDITIONAL registrations (no `if (flag)` block).
        //   - Endpoint consumers: none direct — invoked via PlaybookExecutionEngine which is
        //     itself unconditionally registered. No metadata-gen risk.
        //   - Pattern P1 (Promote-to-unconditional) per ADR-030 — appropriate for executors
        //     auto-discovered by NodeExecutorRegistry. Choice rationale: zero feature-gated
        //     transitive deps (IInsightsContentSanitizer, IObservationEmitter, etc. are all
        //     registered above unconditionally in this same module).
        //
        // Singleton: stateless executors per the GroundingVerifyNode pattern (Phase 1).
        services.AddSingleton<INodeExecutor, SanitizerNodeExecutor>();
        services.AddSingleton<INodeExecutor, ObservationEmitterNodeExecutor>();

        return services;
    }
}
