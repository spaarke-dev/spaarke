using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Processes email attachments by filtering signature images and small files,
/// uploading valid attachments to SPE, and creating Dataverse document records.
/// </summary>
public class EmailAttachmentProcessor : IEmailAttachmentProcessor
{
    private readonly SpeFileStore _speFileStore;
    private readonly IDataverseService _dataverseService;
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailAttachmentProcessor> _logger;

    // Document type for email attachment (Dataverse optionset value)
    private const int DocumentTypeEmailAttachment = 100000007;

    // Relationship type for email attachment
    private const int RelationshipTypeEmailAttachment = 100000000;

    private readonly Regex[] _signaturePatterns;

    // Blocked file extensions (security)
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse",
        ".wsf", ".wsh", ".msc", ".scr", ".pif", ".com", ".hta"
    };

    // Image MIME types for size filtering
    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/gif", "image/jpeg", "image/jpg", "image/bmp", "image/webp"
    };

    public EmailAttachmentProcessor(
        SpeFileStore speFileStore,
        IDataverseService dataverseService,
        IOptions<EmailProcessingOptions> options,
        ILogger<EmailAttachmentProcessor> logger)
    {
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Pre-compile signature patterns for performance
        _signaturePatterns = _options.SignatureImagePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)))
            .ToArray();
    }

    public async Task<AttachmentProcessingResult> ProcessAttachmentsAsync(
        ProcessAttachmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Attachments.Count == 0)
        {
            _logger.LogDebug("No attachments to process for email {EmailId}", request.EmailId);
            return AttachmentProcessingResult.Succeeded(0, 0, [], []);
        }

        _logger.LogInformation(
            "Processing {Count} attachments for email {EmailId}",
            request.Attachments.Count, request.EmailId);

        var processedDocuments = new List<AttachmentDocumentRecord>();
        var filteredAttachments = new List<FilteredAttachmentInfo>();
        var failedCount = 0;

        foreach (var attachment in request.Attachments)
        {
            try
            {
                // Check if attachment should be filtered
                if (ShouldFilterAttachment(attachment.FileName, attachment.SizeBytes, attachment.ContentType))
                {
                    var reason = GetFilterReason(attachment.FileName, attachment.SizeBytes, attachment.ContentType);
                    filteredAttachments.Add(new FilteredAttachmentInfo
                    {
                        FileName = attachment.FileName,
                        Reason = reason,
                        SizeBytes = attachment.SizeBytes
                    });

                    _logger.LogDebug(
                        "Filtered attachment '{FileName}' for email {EmailId}: {Reason}",
                        attachment.FileName, request.EmailId, reason);
                    continue;
                }

                // Process the attachment
                var document = await ProcessSingleAttachmentAsync(
                    request, attachment, cancellationToken);

                if (document != null)
                {
                    processedDocuments.Add(document);
                }
                else
                {
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process attachment '{FileName}' for email {EmailId}",
                    attachment.FileName, request.EmailId);
                failedCount++;
            }
        }

        _logger.LogInformation(
            "Attachment processing complete for email {EmailId}: " +
            "{Processed} processed, {Filtered} filtered, {Failed} failed",
            request.EmailId, processedDocuments.Count, filteredAttachments.Count, failedCount);

        return AttachmentProcessingResult.Succeeded(
            request.Attachments.Count,
            filteredAttachments.Count,
            processedDocuments,
            filteredAttachments);
    }

    public bool ShouldFilterAttachment(string fileName, long sizeBytes, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return true;

        // Check blocked extensions
        var extension = Path.GetExtension(fileName);
        if (BlockedExtensions.Contains(extension))
            return true;

        // Check if it's a signature image pattern
        if (IsSignatureImage(fileName))
            return true;

        // Check if it's a small image (likely signature/spacer)
        if (IsSmallImage(fileName, sizeBytes, contentType))
            return true;

        return false;
    }

    private bool IsSignatureImage(string fileName)
    {
        foreach (var pattern in _signaturePatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning(
                    "Regex timeout checking signature pattern for '{FileName}'", fileName);
            }
        }
        return false;
    }

    private bool IsSmallImage(string fileName, long sizeBytes, string? contentType)
    {
        // Check if it's an image by content type or extension
        var isImage = false;

        if (!string.IsNullOrEmpty(contentType) && ImageMimeTypes.Contains(contentType))
        {
            isImage = true;
        }
        else
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            isImage = extension is ".png" or ".gif" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
        }

        if (!isImage)
            return false;

        // Check size threshold (convert KB to bytes)
        var minSizeBytes = _options.MinImageSizeKB * 1024;
        return sizeBytes < minSizeBytes;
    }

    private string GetFilterReason(string fileName, long sizeBytes, string? contentType)
    {
        var extension = Path.GetExtension(fileName);
        if (BlockedExtensions.Contains(extension))
            return $"Blocked file extension: {extension}";

        if (IsSignatureImage(fileName))
            return "Matches signature image pattern";

        if (IsSmallImage(fileName, sizeBytes, contentType))
            return $"Small image ({sizeBytes} bytes < {_options.MinImageSizeKB}KB threshold)";

        return "Unknown filter reason";
    }

    private async Task<AttachmentDocumentRecord?> ProcessSingleAttachmentAsync(
        ProcessAttachmentsRequest request,
        EmailAttachmentDto attachment,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Processing attachment '{FileName}' ({Size} bytes) for email {EmailId}",
            attachment.FileName, attachment.SizeBytes, request.EmailId);

        // Generate unique filename to avoid conflicts
        var uniqueFileName = GenerateUniqueFileName(attachment.FileName);

        // Upload to SPE
        string? graphItemId;
        try
        {
            // Reset stream position if possible
            if (attachment.Content.CanSeek)
            {
                attachment.Content.Position = 0;
            }

            var uploadResult = await _speFileStore.UploadSmallAsync(
                request.DriveId,
                uniqueFileName,
                attachment.Content,
                cancellationToken);

            graphItemId = uploadResult?.Id;

            if (string.IsNullOrEmpty(graphItemId))
            {
                _logger.LogWarning(
                    "SPE upload returned no item ID for attachment '{FileName}'",
                    attachment.FileName);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upload attachment '{FileName}' to SPE",
                attachment.FileName);
            return null;
        }

        // Create Dataverse document record
        string documentIdStr;
        Guid documentId;
        try
        {
            // Create initial document record
            var createRequest = new CreateDocumentRequest
            {
                Name = attachment.FileName,
                ContainerId = request.ContainerId,
                Description = $"Email attachment from email {request.EmailId}"
            };

            documentIdStr = await _dataverseService.CreateDocumentAsync(createRequest, cancellationToken);

            if (!Guid.TryParse(documentIdStr, out documentId))
            {
                _logger.LogError(
                    "Invalid document ID returned from Dataverse: {DocumentId}",
                    documentIdStr);
                return null;
            }

            // Update with file info, parent relationship, and metadata
            var updateRequest = new UpdateDocumentRequest
            {
                FileName = uniqueFileName,
                FileSize = attachment.SizeBytes,
                MimeType = attachment.ContentType,
                GraphItemId = graphItemId,
                GraphDriveId = request.DriveId,
                HasFile = true,
                DocumentType = DocumentTypeEmailAttachment,
                RelationshipType = RelationshipTypeEmailAttachment,
                ParentDocumentLookup = request.ParentDocumentId
            };

            // Set entity association if provided
            if (request.AssociatedEntityId.HasValue && !string.IsNullOrEmpty(request.AssociatedEntityType))
            {
                // Map entity type to specific lookup field
                switch (request.AssociatedEntityType.ToLowerInvariant())
                {
                    case "sprk_matter":
                        updateRequest.MatterLookup = request.AssociatedEntityId;
                        break;
                    case "sprk_project":
                        updateRequest.ProjectLookup = request.AssociatedEntityId;
                        break;
                    case "sprk_invoice":
                        updateRequest.InvoiceLookup = request.AssociatedEntityId;
                        break;
                    default:
                        _logger.LogWarning(
                            "Unknown entity type for association: {EntityType}",
                            request.AssociatedEntityType);
                        break;
                }
            }

            await _dataverseService.UpdateDocumentAsync(documentIdStr, updateRequest, cancellationToken);

            _logger.LogInformation(
                "Created document record {DocumentId} for attachment '{FileName}'",
                documentId, attachment.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create Dataverse record for attachment '{FileName}'",
                attachment.FileName);
            // Note: File is already uploaded to SPE but we couldn't create the record
            // Could implement cleanup here if needed
            return null;
        }

        // Note: AI processing is triggered from PCF on document upload, not as background job
        // The AnalysisOrchestrationService requires user context (OBO authentication)
        if (request.QueueForAiProcessing)
        {
            _logger.LogDebug(
                "Document {DocumentId} marked for AI processing (will be triggered from PCF)",
                documentId);
        }

        return new AttachmentDocumentRecord
        {
            DocumentId = documentId,
            FileName = attachment.FileName,
            SizeBytes = attachment.SizeBytes,
            GraphItemId = graphItemId,
            ContentType = attachment.ContentType
        };
    }

    private static string GenerateUniqueFileName(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);

        // Add timestamp to ensure uniqueness
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        return $"{nameWithoutExtension}_{timestamp}{extension}";
    }
}
