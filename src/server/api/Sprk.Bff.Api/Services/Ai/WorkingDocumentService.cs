using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages working document persistence to Dataverse during and after analysis.
/// </summary>
public class WorkingDocumentService : IWorkingDocumentService
{
    private readonly IDataverseService _dataverseService;
    private readonly AnalysisOptions _options;
    private readonly ILogger<WorkingDocumentService> _logger;

    private readonly Dictionary<Guid, int> _versionCounters = new();

    public WorkingDocumentService(
        IDataverseService dataverseService,
        IOptions<AnalysisOptions> options,
        ILogger<WorkingDocumentService> logger)
    {
        _dataverseService = dataverseService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task UpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Persisting working document for analysis {AnalysisId}, {ContentLength} chars",
            analysisId, content.Length);

        try
        {
            var fields = new Dictionary<string, object>
            {
                ["sprk_workingdocument"] = content
            };

            await _dataverseService.UpdateAsync("sprk_analysis", analysisId, fields, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't throw — streaming should continue even if a periodic save fails.
            // The final save in FinalizeAnalysisAsync is the critical one.
            _logger.LogWarning(ex,
                "Failed to persist working document for analysis {AnalysisId} ({ContentLength} chars)",
                analysisId, content.Length);
        }
    }

    /// <inheritdoc />
    public async Task FinalizeAnalysisAsync(
        Guid analysisId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizing analysis {AnalysisId}: {InputTokens} input, {OutputTokens} output tokens",
            analysisId, inputTokens, outputTokens);

        try
        {
            var fields = new Dictionary<string, object>
            {
                ["statuscode"] = 2  // Completed
            };

            await _dataverseService.UpdateAsync("sprk_analysis", analysisId, fields, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update analysis status to Completed for {AnalysisId}", analysisId);
        }
    }

    /// <inheritdoc />
    public Task<SavedDocumentResult> SaveToSpeAsync(
        Guid analysisId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving analysis {AnalysisId} to SPE: {FileName} ({ContentLength} bytes)",
            analysisId, fileName, content.Length);

        // SPE upload not yet implemented — returns stub result
        _logger.LogWarning("SPE upload not implemented, returning stub result");

        return Task.FromResult(new SavedDocumentResult
        {
            DocumentId = Guid.NewGuid(),
            DriveId = "stub-drive-id",
            ItemId = "stub-item-id",
            WebUrl = $"https://stub.sharepoint.com/documents/{fileName}"
        });
    }

    /// <inheritdoc />
    public Task<Guid> CreateWorkingVersionAsync(
        Guid analysisId,
        string content,
        int tokenDelta,
        CancellationToken cancellationToken)
    {
        if (!_versionCounters.TryGetValue(analysisId, out var versionNumber))
        {
            versionNumber = 0;
        }
        versionNumber++;
        _versionCounters[analysisId] = versionNumber;

        if (versionNumber > _options.MaxWorkingVersions)
        {
            _logger.LogDebug("Max versions ({MaxVersions}) reached for analysis {AnalysisId}, skipping version creation",
                _options.MaxWorkingVersions, analysisId);
            return Task.FromResult(Guid.Empty);
        }

        _logger.LogDebug("Creating working version {VersionNumber} for analysis {AnalysisId}",
            versionNumber, analysisId);

        return Task.FromResult(Guid.NewGuid());
    }
}
