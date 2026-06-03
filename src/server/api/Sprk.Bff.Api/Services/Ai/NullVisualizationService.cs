using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Visualization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IVisualizationService"/> registered when the
/// compound AI kill-switch is OFF OR when AI Search keys are unconfigured.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 (Tier 1.5 round 4 residual, 2026-06-01). Silently returning
/// an empty <see cref="DocumentGraphResponse"/> would mislead the visualization UI into
/// rendering "no related documents" rather than surfacing the kill-switch state — fail-fast
/// clarifies the state via 503 ProblemDetails.
/// </para>
/// <para>
/// Flushed by the Step 9.5 latent-bug scan after Tier 2/Tier 3 promotions exposed
/// <see cref="VisualizationEndpoints"/> as an unconditional endpoint mapping (registered at
/// <c>EndpointMappingExtensions.cs:159</c>) whose handlers inject the real
/// <see cref="VisualizationService"/>. Same anti-pattern as the prior Tier 1.5 residuals
/// (ChatContextMappingService, DocxExportService, IWorkingDocumentService); absorbed under
/// the D-02 cluster exception per user approval.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 1.5 round 4.</para>
/// </remarks>
public sealed class NullVisualizationService : IVisualizationService
{
    private const string ErrorCode = "ai.visualization.disabled";
    private const string DetailMessage =
        "AI visualization requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true AND configured AI Search.";

    private readonly ILogger<NullVisualizationService> _logger;

    public NullVisualizationService(ILogger<NullVisualizationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
        Guid documentId,
        VisualizationOptions options,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetRelatedDocumentsAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<ContentUploadResult> IndexTemporaryContentAsync(
        Stream fileStream,
        string fileName,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexTemporaryContentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullVisualizationService.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
