using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IFileIndexingService"/> registered when the
/// compound AI kill-switch is OFF OR when AI Search keys are unconfigured.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 (Tier 1.5 round 4 residual, 2026-06-01). Silently returning
/// a <see cref="FileIndexingResult.Failed"/> sentinel would conflate kill-switch state with
/// genuine indexing failure (e.g., text-extraction error, transient AI Search outage) — fail-fast
/// surfaces the kill-switch state distinctly via 503 ProblemDetails.
/// </para>
/// <para>
/// Flushed by the Step 9.5 latent-bug scan after Tier 2/Tier 3 promotions exposed
/// <see cref="RagEndpoints"/> handlers (<c>IndexFile</c>, <c>SendToIndex</c>) as unconditional
/// endpoint consumers (registered at <c>EndpointMappingExtensions.cs:133</c>). Same anti-pattern
/// as the prior Tier 1.5 residuals (ChatContextMappingService, DocxExportService,
/// IWorkingDocumentService); absorbed under the D-02 cluster exception per user approval.
/// </para>
/// <para>
/// Background workers (<see cref="Workers.Office.IndexingWorkerHostedService"/>,
/// <see cref="Services.Jobs.Handlers.RagIndexingJobHandler"/>,
/// <see cref="Services.Ai.Jobs.BulkRagIndexingJobHandler"/>) also depend on
/// <see cref="IFileIndexingService"/>; when the compound gate is OFF, these jobs will fail
/// fast on dequeue and propagate the FeatureDisabledException through the existing job
/// retry/dead-letter infrastructure — this is the intended behavior under the kill switch.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 1.5 round 4.</para>
/// </remarks>
public sealed class NullFileIndexingService : IFileIndexingService
{
    private const string ErrorCode = "ai.file-indexing.disabled";
    private const string DetailMessage =
        "AI file indexing requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true AND configured AI Search.";

    private readonly ILogger<NullFileIndexingService> _logger;

    public NullFileIndexingService(ILogger<NullFileIndexingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexFileAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<FileIndexingResult> IndexFileAppOnlyAsync(
        FileIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexFileAppOnlyAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<FileIndexingResult> IndexContentAsync(
        ContentIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexContentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullFileIndexingService.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
