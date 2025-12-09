using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;

#pragma warning disable IDE0060 // Remove unused parameter - many placeholder methods in this handler await future implementation

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Handles document events from the Dataverse plugin.
/// Implements business logic for document operations.
/// </summary>
public class DocumentEventHandler : IDocumentEventHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly SpeFileStore _speFileStore;
    private readonly ILogger<DocumentEventHandler> _logger;

    public DocumentEventHandler(
        IDataverseService dataverseService,
        SpeFileStore speFileStore,
        ILogger<DocumentEventHandler> logger)
    {
        _dataverseService = dataverseService;
        _speFileStore = speFileStore;
        _logger = logger;
    }

    public async Task HandleEventAsync(DocumentEvent documentEvent, CancellationToken cancellationToken = default)
    {
        using var activity = DocumentEventTelemetry.StartActivity($"HandleEvent.{documentEvent.Operation}", documentEvent.CorrelationId);
        activity?.SetTag("document_id", documentEvent.DocumentId);
        activity?.SetTag("event_id", documentEvent.EventId);

        _logger.LogInformation("Handling {Operation} event for document {DocumentId}",
            documentEvent.Operation, documentEvent.DocumentId);

        try
        {
            var result = documentEvent.Operation switch
            {
                "Create" => await HandleDocumentCreatedAsync(documentEvent, cancellationToken),
                "Update" => await HandleDocumentUpdatedAsync(documentEvent, cancellationToken),
                "Delete" => await HandleDocumentDeletedAsync(documentEvent, cancellationToken),
                _ => throw new NotSupportedException($"Operation '{documentEvent.Operation}' is not supported")
            };

            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);

            _logger.LogInformation("Successfully handled {Operation} event for document {DocumentId}",
                documentEvent.Operation, documentEvent.DocumentId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to handle {Operation} event for document {DocumentId}: {Error}",
                documentEvent.Operation, documentEvent.DocumentId, ex.Message);
            throw;
        }
    }

    private async Task<bool> HandleDocumentCreatedAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing document creation for {DocumentId}", documentEvent.DocumentId);

        // Initialize document state for file operations
        await InitializeDocumentForFileOperationsAsync(documentEvent, cancellationToken);

        // If initial file was provided, process it
        if (documentEvent.EntityData.TryGetValue("sprk_hasfile", out var hasFileObj) &&
            hasFileObj is bool hasFile && hasFile)
        {
            await ProcessInitialFileUploadAsync(documentEvent, cancellationToken);
        }

        // Update document status to Active if initialization successful
        await UpdateDocumentStatusAsync(documentEvent.DocumentId, DocumentStatus.Active, cancellationToken);

        return true;
    }

    private async Task<bool> HandleDocumentUpdatedAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing document update for {DocumentId}", documentEvent.DocumentId);

        // Check what fields were updated
        var changedFields = DetermineChangedFields(documentEvent);

        foreach (var changedField in changedFields)
        {
            switch (changedField)
            {
                case "sprk_documentname":
                    await SyncDocumentNameToSpeAsync(documentEvent, cancellationToken);
                    break;

                case "sprk_containerid":
                    await HandleContainerChangeAsync(documentEvent, cancellationToken);
                    break;

                case "sprk_hasfile":
                    await HandleFileStatusChangeAsync(documentEvent, cancellationToken);
                    break;

                case "statuscode":
                case "statecode":
                    await HandleStatusChangeAsync(documentEvent, cancellationToken);
                    break;
            }
        }

        return true;
    }

    private async Task<bool> HandleDocumentDeletedAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing document deletion for {DocumentId}", documentEvent.DocumentId);

        // Check if document had an associated file
        if (documentEvent.EntityData.TryGetValue("sprk_hasfile", out var hasFileObj) &&
            hasFileObj is bool hasFile && hasFile)
        {
            await DeleteAssociatedFileAsync(documentEvent, cancellationToken);
        }

        // Update container document count
        if (documentEvent.EntityData.ContainsKey("sprk_containerid"))
        {
            await UpdateContainerDocumentCountAsync(documentEvent, cancellationToken);
        }

        // Clean up any pending file operations
        await CleanupPendingOperationsAsync(documentEvent.DocumentId, cancellationToken);

        return true;
    }

    // File operation methods
    private async Task InitializeDocumentForFileOperationsAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var containerId = ExtractContainerId(documentEvent);
        if (string.IsNullOrEmpty(containerId))
        {
            _logger.LogWarning("Document {DocumentId} created without container reference", documentEvent.DocumentId);
            return;
        }

        _logger.LogDebug("Initialized file operations context for document {DocumentId} in container {ContainerId}",
            documentEvent.DocumentId, containerId);

        await Task.CompletedTask;
    }

    private async Task ProcessInitialFileUploadAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var fileName = ExtractFileName(documentEvent);
        var fileSize = ExtractFileSize(documentEvent);

        if (!string.IsNullOrEmpty(fileName))
        {
            _logger.LogInformation("Processing initial file upload for document {DocumentId}: {FileName}",
                documentEvent.DocumentId, fileName);

            await UpdateDocumentFileMetadataAsync(documentEvent.DocumentId, fileName, fileSize, cancellationToken);
        }
    }

    private async Task SyncDocumentNameToSpeAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var newName = ExtractNewValue<string>(documentEvent, "sprk_documentname");
        var oldName = ExtractOldValue<string>(documentEvent, "sprk_documentname");

        if (newName != oldName && HasAssociatedFile(documentEvent))
        {
            _logger.LogInformation("Syncing document name change to SPE for document {DocumentId}: {OldName} -> {NewName}",
                documentEvent.DocumentId, oldName, newName);

            // SPE metadata update would go here
            await Task.CompletedTask;
        }
    }

    private async Task HandleContainerChangeAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var newContainerId = ExtractNewValue<string>(documentEvent, "sprk_containerid");
        var oldContainerId = ExtractOldValue<string>(documentEvent, "sprk_containerid");

        if (newContainerId != oldContainerId)
        {
            _logger.LogInformation("Document {DocumentId} moved from container {OldContainer} to {NewContainer}",
                documentEvent.DocumentId, oldContainerId, newContainerId);

            // Update container document counts
            if (!string.IsNullOrEmpty(oldContainerId))
            {
                await DecrementContainerDocumentCountAsync(oldContainerId, cancellationToken);
            }

            if (!string.IsNullOrEmpty(newContainerId))
            {
                await IncrementContainerDocumentCountAsync(newContainerId, cancellationToken);
            }

            // Handle file movement if document has file
            if (HasAssociatedFile(documentEvent))
            {
                await HandleFileContainerMovementAsync(documentEvent, oldContainerId, newContainerId, cancellationToken);
            }
        }
    }

    private async Task HandleFileStatusChangeAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var newHasFile = ExtractNewValue<bool>(documentEvent, "sprk_hasfile");
        var oldHasFile = ExtractOldValue<bool>(documentEvent, "sprk_hasfile");

        if (newHasFile != oldHasFile)
        {
            if (newHasFile && !oldHasFile)
            {
                _logger.LogInformation("File added to document {DocumentId}", documentEvent.DocumentId);
                await HandleFileAddedAsync(documentEvent, cancellationToken);
            }
            else if (!newHasFile && oldHasFile)
            {
                _logger.LogInformation("File removed from document {DocumentId}", documentEvent.DocumentId);
                await HandleFileRemovedAsync(documentEvent, cancellationToken);
            }
        }
    }

    private async Task HandleStatusChangeAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var newStatus = ExtractNewValue<int>(documentEvent, "statuscode");
        var oldStatus = ExtractOldValue<int>(documentEvent, "statuscode");

        if (newStatus != oldStatus)
        {
            _logger.LogInformation("Document {DocumentId} status changed from {OldStatus} to {NewStatus}",
                documentEvent.DocumentId, oldStatus, newStatus);

            switch ((DocumentStatus?)newStatus)
            {
                case DocumentStatus.Active:
                    await HandleDocumentActivationAsync(documentEvent, cancellationToken);
                    break;

                case DocumentStatus.Processing:
                    await HandleDocumentProcessingAsync(documentEvent, cancellationToken);
                    break;

                case DocumentStatus.Error:
                    await HandleDocumentErrorAsync(documentEvent, cancellationToken);
                    break;

                case DocumentStatus.Draft:
                    await HandleDocumentDraftAsync(documentEvent, cancellationToken);
                    break;
            }
        }
    }

    // Helper methods for data extraction
    private string? ExtractContainerId(DocumentEvent documentEvent)
    {
        if (documentEvent.EntityData.TryGetValue("sprk_containerid", out var value))
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
        }
        return null;
    }

    private string? ExtractFileName(DocumentEvent documentEvent)
    {
        return documentEvent.EntityData.TryGetValue("sprk_filename", out var fileName) ? fileName?.ToString() : null;
    }

    private long? ExtractFileSize(DocumentEvent documentEvent)
    {
        if (documentEvent.EntityData.TryGetValue("sprk_filesize", out var fileSize))
        {
            if (fileSize is JsonElement element && element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt64();
            }
            if (long.TryParse(fileSize?.ToString(), out var size))
            {
                return size;
            }
        }
        return null;
    }

    private bool HasAssociatedFile(DocumentEvent documentEvent)
    {
        return documentEvent.EntityData.TryGetValue("sprk_hasfile", out var hasFile) && hasFile is bool hasFileFlag && hasFileFlag;
    }

    private T? ExtractNewValue<T>(DocumentEvent documentEvent, string fieldName)
    {
        if (!documentEvent.EntityData.TryGetValue(fieldName, out var value))
            return default;

        if (value is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }

        if (value is T typedValue)
            return typedValue;

        return default;
    }

    private T? ExtractOldValue<T>(DocumentEvent documentEvent, string fieldName)
    {
        if (documentEvent.PreEntityData == null || !documentEvent.PreEntityData.TryGetValue(fieldName, out var value))
            return default;

        if (value is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }

        if (value is T typedValue)
            return typedValue;

        return default;
    }

    private List<string> DetermineChangedFields(DocumentEvent documentEvent)
    {
        var changedFields = new List<string>();

        if (documentEvent.PreEntityData == null)
            return changedFields;

        foreach (var currentField in documentEvent.EntityData)
        {
            if (!documentEvent.PreEntityData.TryGetValue(currentField.Key, out var oldValue) ||
                !Equals(currentField.Value, oldValue))
            {
                changedFields.Add(currentField.Key);
            }
        }

        return changedFields;
    }

    // Container management methods
    private async Task IncrementContainerDocumentCountAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Incremented document count for container {ContainerId}", containerId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment document count for container {ContainerId}", containerId);
        }
    }

    private async Task DecrementContainerDocumentCountAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Decremented document count for container {ContainerId}", containerId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrement document count for container {ContainerId}", containerId);
        }
    }

    // Additional business logic methods
    private async Task DeleteAssociatedFileAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting associated file for document {DocumentId}", documentEvent.DocumentId);
        await Task.CompletedTask;
    }

    private async Task UpdateContainerDocumentCountAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        var containerId = ExtractContainerId(documentEvent);
        if (!string.IsNullOrEmpty(containerId))
        {
            await DecrementContainerDocumentCountAsync(containerId, cancellationToken);
        }
    }

    private async Task CleanupPendingOperationsAsync(string documentId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up pending operations for document {DocumentId}", documentId);
        await Task.CompletedTask;
    }

    private async Task UpdateDocumentStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken)
    {
        try
        {
            var updateRequest = new UpdateDocumentRequest { Status = status };
            await _dataverseService.UpdateDocumentAsync(documentId, updateRequest, cancellationToken);
            _logger.LogInformation("Updated document {DocumentId} status to {Status}", documentId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update document status for {DocumentId}", documentId);
            throw;
        }
    }

    private async Task UpdateDocumentFileMetadataAsync(string documentId, string fileName, long? fileSize, CancellationToken cancellationToken)
    {
        try
        {
            var updateRequest = new UpdateDocumentRequest
            {
                FileName = fileName,
                FileSize = fileSize,
                HasFile = true
            };
            await _dataverseService.UpdateDocumentAsync(documentId, updateRequest, cancellationToken);
            _logger.LogInformation("Updated file metadata for document {DocumentId}: {FileName}, {FileSize} bytes",
                documentId, fileName, fileSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file metadata for {DocumentId}", documentId);
            throw;
        }
    }

    // Status-specific handlers
    private async Task HandleDocumentActivationAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling document activation for {DocumentId}", documentEvent.DocumentId);
        await Task.CompletedTask;
    }

    private async Task HandleDocumentProcessingAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling document processing state for {DocumentId}", documentEvent.DocumentId);
        // Implement processing logic - e.g., file validation, metadata extraction, etc.
        await Task.CompletedTask;
    }

    private async Task HandleDocumentErrorAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogError("Handling document error state for {DocumentId}", documentEvent.DocumentId);
        // Implement error handling logic - e.g., notifications, cleanup, etc.
        await Task.CompletedTask;
    }

    private async Task HandleDocumentDraftAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling document draft state for {DocumentId}", documentEvent.DocumentId);
        await Task.CompletedTask;
    }

    private async Task HandleFileAddedAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling file addition for document {DocumentId}", documentEvent.DocumentId);
        await Task.CompletedTask;
    }

    private async Task HandleFileRemovedAsync(DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling file removal for document {DocumentId}", documentEvent.DocumentId);
        await Task.CompletedTask;
    }

    private async Task HandleFileContainerMovementAsync(DocumentEvent documentEvent, string? oldContainerId, string? newContainerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling file container movement for document {DocumentId} from {OldContainer} to {NewContainer}",
            documentEvent.DocumentId, oldContainerId, newContainerId);
        await Task.CompletedTask;
    }
}
