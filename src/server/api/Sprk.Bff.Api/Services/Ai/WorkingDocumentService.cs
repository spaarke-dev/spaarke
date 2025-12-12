using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages transient working document state during analysis refinement.
/// Handles Dataverse updates and SPE storage operations.
/// </summary>
/// <remarks>
/// Phase 1 Scaffolding: Uses stub implementations until Dataverse and SPE operations are integrated.
/// Full implementation will be completed in Task 032.
/// </remarks>
public class WorkingDocumentService : IWorkingDocumentService
{
    private readonly IDataverseService _dataverseService;
    private readonly AnalysisOptions _options;
    private readonly ILogger<WorkingDocumentService> _logger;

    // Track version numbers per analysis (in-memory, reset on service restart)
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
    public Task UpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating working document for analysis {AnalysisId}, {ContentLength} chars",
            analysisId, content.Length);

        // Phase 1: Log only - actual Dataverse update in Task 032
        _logger.LogDebug("Phase 1: Working document update logged (Dataverse integration in Task 032)");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FinalizeAnalysisAsync(
        Guid analysisId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizing analysis {AnalysisId}: {InputTokens} input, {OutputTokens} output tokens",
            analysisId, inputTokens, outputTokens);

        // Phase 1: Log only - actual Dataverse update in Task 032
        _logger.LogDebug("Phase 1: Analysis finalization logged (Dataverse integration in Task 032)");

        return Task.CompletedTask;
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

        // Phase 1: Return stub result - actual SPE upload in Task 032
        _logger.LogWarning("Phase 1: SPE upload not implemented, returning stub result");

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
        // Get or create version counter
        if (!_versionCounters.TryGetValue(analysisId, out var versionNumber))
        {
            versionNumber = 0;
        }
        versionNumber++;
        _versionCounters[analysisId] = versionNumber;

        // Check if we've exceeded max versions
        if (versionNumber > _options.MaxWorkingVersions)
        {
            _logger.LogDebug("Max versions ({MaxVersions}) reached for analysis {AnalysisId}, skipping version creation",
                _options.MaxWorkingVersions, analysisId);
            return Task.FromResult(Guid.Empty);
        }

        _logger.LogDebug("Creating working version {VersionNumber} for analysis {AnalysisId}",
            versionNumber, analysisId);

        // Phase 1: Return stub version ID - actual Dataverse creation in Task 032
        return Task.FromResult(Guid.NewGuid());
    }
}
