using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Handles Service Bus message queuing for Office upload finalization jobs.
/// Extracted from OfficeService to enforce single responsibility.
/// </summary>
public class OfficeJobQueue
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusOptions _serviceBusOptions;
    private readonly ILogger<OfficeJobQueue> _logger;

    // Queue names for Office workers
    private const string UploadFinalizationQueueName = "office-upload-finalization";

    public OfficeJobQueue(
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> serviceBusOptions,
        ILogger<OfficeJobQueue> logger)
    {
        _serviceBusClient = serviceBusClient;
        _serviceBusOptions = serviceBusOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Queues a job to the Service Bus for background processing.
    /// </summary>
    public async Task QueueUploadFinalizationAsync(
        Guid jobId,
        string idempotencyKey,
        string correlationId,
        string userId,
        SaveRequest request,
        string driveId,
        string itemId,
        string fileName,
        long fileSize,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Queueing upload finalization job {JobId} for {ContentType}",
            jobId,
            request.ContentType);

        // Build the payload for the worker
        var payload = new UploadFinalizationPayload
        {
            ContentType = request.ContentType,
            AssociationType = request.TargetEntity?.EntityType,
            AssociationId = request.TargetEntity?.EntityId,
            ContainerId = driveId, // Use resolved drive ID
            FolderPath = request.FolderPath,
            TempFileLocation = $"spe://{driveId}/{itemId}", // Reference the already-uploaded SPE file
            FileName = fileName,
            FileSize = fileSize,
            MimeType = GetMimeType(request),
            TriggerAiProcessing = request.TriggerAiProcessing,
            EmailMetadata = request.ContentType == SaveContentType.Email && request.Email != null
                ? new EmailArtifactPayload
                {
                    InternetMessageId = request.Email.InternetMessageId,
                    ConversationId = request.Email.ConversationId,
                    Subject = request.Email.Subject,
                    SenderEmail = request.Email.SenderEmail,
                    SenderName = request.Email.SenderName,
                    RecipientsJson = request.Email.Recipients != null
                        ? JsonSerializer.Serialize(request.Email.Recipients)
                        : null,
                    SentDate = request.Email.SentDate,
                    ReceivedDate = request.Email.ReceivedDate,
                    BodyPreview = request.Email.Body?[..Math.Min(request.Email.Body.Length, 500)],
                    HasAttachments = request.Email.Attachments?.Count > 0,
                    Importance = 1, // Normal
                    SelectedAttachmentFileNames = request.Email.SelectedAttachmentFileNames
                }
                : null,
            AttachmentMetadata = request.ContentType == SaveContentType.Attachment && request.Attachment != null
                ? new AttachmentArtifactPayload
                {
                    OutlookAttachmentId = request.Attachment.AttachmentId,
                    OriginalFileName = request.Attachment.FileName,
                    ContentType = request.Attachment.ContentType,
                    Size = request.Attachment.Size ?? 0,
                    IsInline = false
                }
                : null,
            AiOptions = request.AiOptions != null
                ? new AiProcessingOptions
                {
                    ProfileSummary = request.AiOptions.ProfileSummary,
                    RagIndex = request.AiOptions.RagIndex,
                    DeepAnalysis = request.AiOptions.DeepAnalysis
                }
                : new AiProcessingOptions
                {
                    ProfileSummary = request.TriggerAiProcessing,
                    RagIndex = request.TriggerAiProcessing,
                    DeepAnalysis = false
                },
            DocumentId = documentId
        };

        // Create the job message
        var message = new OfficeJobMessage
        {
            JobId = jobId,
            JobType = OfficeJobType.UploadFinalization,
            SubjectId = itemId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            Attempt = 1,
            MaxAttempts = 3,
            UserId = userId,
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        // Send to Service Bus
        var sender = _serviceBusClient.CreateSender(UploadFinalizationQueueName);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
        {
            MessageId = jobId.ToString(),
            CorrelationId = correlationId,
            ContentType = "application/json",
            Subject = OfficeJobType.UploadFinalization.ToString(),
            ApplicationProperties =
            {
                ["JobType"] = OfficeJobType.UploadFinalization.ToString(),
                ["Attempt"] = 1,
                ["UserId"] = userId,
                ["ContentType"] = request.ContentType.ToString()
            }
        };

        await sender.SendMessageAsync(sbMessage, cancellationToken);
        await sender.DisposeAsync();

        _logger.LogInformation(
            "Upload finalization job {JobId} queued to Service Bus for {ContentType}",
            jobId,
            request.ContentType);
    }

    /// <summary>
    /// Gets the MIME type for the save request content.
    /// </summary>
    public static string GetMimeType(SaveRequest request) => request.ContentType switch
    {
        SaveContentType.Email => "message/rfc822",
        SaveContentType.Attachment => request.Attachment?.ContentType ?? "application/octet-stream",
        SaveContentType.Document => request.Document?.ContentType ?? "application/octet-stream",
        _ => "application/octet-stream"
    };
}
