using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages working document persistence to Dataverse during and after analysis.
/// Uses SpeFileStore (ADR-007) for SharePoint Embedded uploads.
/// SpeFileStore is resolved via IServiceProvider at call time (not constructor-injected)
/// because SpeFileStore is scoped and this service may be consumed transitively by singletons.
/// </summary>
public class WorkingDocumentService : IWorkingDocumentService
{
    private readonly IGenericEntityService _genericEntityService;
    private readonly IServiceProvider _serviceProvider;
    private readonly AnalysisOptions _options;
    private readonly ILogger<WorkingDocumentService> _logger;

    private readonly Dictionary<Guid, int> _versionCounters = new();

    public WorkingDocumentService(
        IGenericEntityService genericEntityService,
        IServiceProvider serviceProvider,
        IOptions<AnalysisOptions> options,
        ILogger<WorkingDocumentService> logger)
    {
        _genericEntityService = genericEntityService;
        _serviceProvider = serviceProvider;
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

            await _genericEntityService.UpdateAsync("sprk_analysis", analysisId, fields, cancellationToken);
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

            await _genericEntityService.UpdateAsync("sprk_analysis", analysisId, fields, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update analysis status to Completed for {AnalysisId}", analysisId);
        }
    }

    /// <inheritdoc />
    public async Task<SavedDocumentResult> SaveToSpeAsync(
        Guid analysisId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving analysis {AnalysisId} to SPE: {FileName} ({ContentLength} bytes)",
            analysisId, fileName, content.Length);

        // Resolve the SPE container from the analysis's parent matter.
        // The analysis record has a sprk_matterid lookup that points to a matter,
        // which has a sprk_containerid field (the SPE container drive ID).
        string? driveId = null;
        try
        {
            var analysisEntity = await _genericEntityService.RetrieveAsync(
                "sprk_analysis", analysisId,
                ["sprk_matterid"],
                cancellationToken);

            var matterRef = analysisEntity.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_matterid");
            if (matterRef is not null)
            {
                var matterEntity = await _genericEntityService.RetrieveAsync(
                    "sprk_matter", matterRef.Id,
                    ["sprk_containerid"],
                    cancellationToken);

                var containerId = matterEntity.GetAttributeValue<string>("sprk_containerid");
                if (!string.IsNullOrWhiteSpace(containerId))
                {
                    driveId = containerId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve SPE container for analysis {AnalysisId} — document will be saved to Dataverse only",
                analysisId);
        }

        if (string.IsNullOrWhiteSpace(driveId))
        {
            _logger.LogWarning(
                "No SPE container resolved for analysis {AnalysisId} — persisting to Dataverse field only",
                analysisId);

            // Fallback: save content to the analysis record's sprk_workingdocument field
            await UpdateWorkingDocumentAsync(analysisId, System.Text.Encoding.UTF8.GetString(content), cancellationToken);

            return new SavedDocumentResult
            {
                DocumentId = analysisId,
                DriveId = string.Empty,
                ItemId = string.Empty,
                WebUrl = string.Empty
            };
        }

        // Upload to SPE via SpeFileStore (ADR-007).
        // Resolved at call time via IServiceProvider because SpeFileStore is scoped
        // and WorkingDocumentService may be consumed transitively by singleton services.
        var speFileStore = _serviceProvider.GetService<SpeFileStore>();
        if (speFileStore is null)
        {
            _logger.LogWarning("SpeFileStore not available — saving to Dataverse field only");
            await UpdateWorkingDocumentAsync(analysisId, System.Text.Encoding.UTF8.GetString(content), cancellationToken);
            return new SavedDocumentResult
            {
                DocumentId = analysisId,
                DriveId = driveId,
                ItemId = string.Empty,
                WebUrl = string.Empty
            };
        }

        var path = $"/analysis-outputs/{analysisId}/{fileName}";
        using var stream = new MemoryStream(content);

        var uploadResult = await speFileStore.UploadSmallAsync(driveId, path, stream, cancellationToken);

        if (uploadResult is null)
        {
            _logger.LogWarning(
                "SPE upload returned null for analysis {AnalysisId}, file {FileName}",
                analysisId, fileName);

            return new SavedDocumentResult
            {
                DocumentId = analysisId,
                DriveId = driveId,
                ItemId = string.Empty,
                WebUrl = string.Empty
            };
        }

        _logger.LogInformation(
            "SPE upload succeeded for analysis {AnalysisId}: DriveId={DriveId}, ItemId={ItemId}",
            analysisId, driveId, uploadResult.Id);

        return new SavedDocumentResult
        {
            DocumentId = analysisId,
            DriveId = driveId,
            ItemId = uploadResult.Id,
            WebUrl = uploadResult.WebUrl ?? string.Empty
        };
    }

    /// <inheritdoc />
    public async Task UpdateChatHistoryAsync(
        Guid analysisId,
        string chatHistoryJson,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Persisting chat history for analysis {AnalysisId}, {JsonLength} chars",
            analysisId, chatHistoryJson.Length);

        try
        {
            var fields = new Dictionary<string, object>
            {
                ["sprk_chathistory"] = chatHistoryJson
            };

            await _genericEntityService.UpdateAsync("sprk_analysis", analysisId, fields, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist chat history for analysis {AnalysisId}", analysisId);
        }
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
