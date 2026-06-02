using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Mirror;
using Sprk.Bff.Api.Services.Ai.Insights.Prompts;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Spaarke Insights Engine universal ingest pipeline (D-P7, task 040).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone</b>: A per <c>SPEC §3.5</c> — lives under <c>Services/Ai/Insights/Ingest/</c>
/// + sibling Zone A subnamespaces (<c>Sanitization/</c>, <c>Mirror/</c>, <c>Prompts/</c>).
/// Zone B never imports these registrations; the orchestrator surfaces through
/// <see cref="PublicContracts.IInsightsAi.RunIngestAsync"/>.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>New feature module rather than extending <see cref="AiModule"/> (at 15/15 cap) or
///   <see cref="InsightsExtractionModule"/> (different concern — extraction primitives vs
///   pipeline orchestration) or <see cref="InsightsFacadeModule"/> (Zone A public surface).
///   Keeps the §3.5 boundary visible in DI composition.</item>
///   <item>Six interface seams (<see cref="IInsightsContentSanitizer"/>,
///   <see cref="IObservationMirror"/>, <see cref="IIngestDocumentSource"/>,
///   <see cref="IObservationIndexUpserter"/>, <see cref="IInsightsPromptLoader"/>,
///   <see cref="IIngestOrchestrator"/>). Each is justified per ADR-010 §Exceptions:
///   <list type="bullet">
///     <item><see cref="IInsightsContentSanitizer"/> — Phase 1.5+ LAVERN Sanitizer swap path.</item>
///     <item><see cref="IObservationMirror"/> — Phase 1 NoOp + Phase 1.5+ Dataverse impl (task 051) swap path.</item>
///     <item><see cref="IIngestDocumentSource"/> — testability seam (orchestrator unit-tested without real Azure Search).</item>
///     <item><see cref="IObservationIndexUpserter"/> — testability seam (orchestrator unit-tested without real Azure Search + embeddings).</item>
///     <item><see cref="IInsightsPromptLoader"/> — testability seam (orchestrator unit-tested without prompt files on disk).</item>
///     <item><see cref="IIngestOrchestrator"/> — facade-level testability seam (<see cref="Insights.InsightsOrchestrator"/> unit-tested without exercising full pipeline).</item>
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

        // D-P7 supporting services.
        services.AddSingleton<IInsightsPromptLoader, InsightsPromptLoader>();
        services.AddSingleton<IIngestDocumentSource, FilesIndexIngestDocumentSource>();
        services.AddSingleton<IObservationIndexUpserter, ObservationIndexUpserter>();

        // D-P7 orchestrator itself — bridges into the InsightsOrchestrator facade
        // (task 042's InsightsOrchestrator.RunIngestAsync delegates here).
        services.AddSingleton<IIngestOrchestrator, IngestOrchestrator>();

        return services;
    }
}
