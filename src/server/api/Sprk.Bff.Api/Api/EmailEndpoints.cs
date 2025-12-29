using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Email;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Email-to-document conversion endpoints following ADR-008.
/// Provides manual email conversion (synchronous) and status queries.
/// </summary>
public static class EmailEndpoints
{
    // Document type choice value for Email
    private const int DocumentTypeEmail = 100000006;

    // Email direction choice values
    private const int DirectionReceived = 100000000;
    private const int DirectionSent = 100000001;

    // Relationship type for email attachments
    private const int RelationshipTypeEmailAttachment = 100000000;

    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/emails")
            .RequireAuthorization()
            .WithTags("Email Conversion");

        // POST /api/v1/emails/{emailId}/save-as-document - Manual save (synchronous)
        group.MapPost("/{emailId:guid}/save-as-document", SaveEmailAsDocumentAsync)
            .WithName("SaveEmailAsDocument")
            .WithDescription("Convert a Dataverse email activity to a document with .eml file")
            .Produces<ConvertEmailToDocumentResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // GET /api/v1/emails/{emailId}/document-status - Check if already saved
        group.MapGet("/{emailId:guid}/document-status", GetEmailDocumentStatusAsync)
            .WithName("GetEmailDocumentStatus")
            .WithDescription("Check if an email has already been saved as a document")
            .Produces<EmailDocumentStatusResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Convert a Dataverse email activity to a document.
    /// This is a synchronous operation for manual user-initiated saves.
    /// </summary>
    private static async Task<IResult> SaveEmailAsDocumentAsync(
        Guid emailId,
        ConvertEmailToDocumentRequest? request,
        IEmailToEmlConverter emlConverter,
        SpeFileStore speFileStore,
        IDataverseService dataverseService,
        IOptions<EmailProcessingOptions> emailOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        request ??= new ConvertEmailToDocumentRequest();

        try
        {
            logger.LogInformation(
                "Starting email-to-document conversion for email {EmailId}, TraceId={TraceId}",
                emailId, traceId);

            // Step 1: Check if document already exists for this email (idempotency)
            var existingDoc = await CheckExistingDocumentAsync(emailId, dataverseService, cancellationToken);
            if (existingDoc != null)
            {
                logger.LogWarning(
                    "Email {EmailId} already has document {DocumentId}, returning 409 Conflict",
                    emailId, existingDoc);

                return Results.Problem(
                    title: "Document Already Exists",
                    detail: $"Email has already been saved as document {existingDoc}",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["existingDocumentId"] = existingDoc,
                        ["traceId"] = traceId
                    });
            }

            // Step 2: Convert email to .eml
            var emlResult = await emlConverter.ConvertToEmlAsync(
                emailId,
                request.IncludeAttachments,
                cancellationToken);

            if (!emlResult.Success)
            {
                logger.LogError("Failed to convert email {EmailId}: {Error}", emailId, emlResult.ErrorMessage);
                return ProblemDetailsHelper.ValidationError(emlResult.ErrorMessage ?? "Conversion failed");
            }

            // Step 3: Generate filename
            var fileName = await emlConverter.GenerateEmlFileNameAsync(emailId, cancellationToken);

            // Step 4: Determine container ID
            var containerId = request.ContainerId ?? emailOptions.Value.DefaultContainerId;
            if (string.IsNullOrEmpty(containerId))
            {
                return ProblemDetailsHelper.ValidationError(
                    "No container ID specified and no default configured");
            }

            // Step 5: Resolve container to drive ID and upload to SPE
            var driveId = await speFileStore.ResolveDriveIdAsync(containerId, cancellationToken);

            var uploadResult = await speFileStore.UploadSmallAsync(
                driveId,
                fileName,
                emlResult.EmlStream!,
                cancellationToken);

            if (uploadResult == null)
            {
                logger.LogError("Failed to upload .eml file for email {EmailId}", emailId);
                return Results.Problem(
                    title: "Upload Failed",
                    detail: "Failed to upload .eml file to SharePoint Embedded",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            logger.LogInformation(
                "Uploaded .eml file {FileName} ({Size} bytes) to SPE, ItemId={ItemId}",
                fileName, emlResult.FileSizeBytes, uploadResult.Id);

            // Step 6: Create sprk_document record in Dataverse
            var metadata = emlResult.Metadata!;
            var createRequest = new CreateDocumentRequest
            {
                Name = metadata.Subject.Length > 0 ? metadata.Subject : fileName,
                ContainerId = containerId,
                Description = $"Email converted to document on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
            };

            var documentIdStr = await dataverseService.CreateDocumentAsync(createRequest, cancellationToken);
            var documentId = Guid.Parse(documentIdStr);

            // Step 7: Update document with file info and email metadata
            var updateRequest = new UpdateDocumentRequest
            {
                FileName = fileName,
                FileSize = emlResult.FileSizeBytes,
                MimeType = "message/rfc822",
                GraphItemId = uploadResult.Id,
                GraphDriveId = driveId,
                HasFile = true,
                DocumentType = DocumentTypeEmail,
                IsEmailArchive = true,
                // Email metadata
                EmailSubject = metadata.Subject,
                EmailFrom = metadata.From,
                EmailTo = metadata.To,
                EmailCc = metadata.Cc,
                EmailDate = metadata.EmailDate,
                EmailBody = metadata.Body,
                EmailMessageId = metadata.MessageId,
                EmailDirection = metadata.Direction,
                EmailTrackingToken = metadata.TrackingToken,
                EmailConversationIndex = metadata.ConversationIndex,
                // Link to original email activity
                EmailLookup = emailId
            };

            await dataverseService.UpdateDocumentAsync(documentIdStr, updateRequest, cancellationToken);

            logger.LogInformation(
                "Created document {DocumentId} for email {EmailId} with {AttachmentCount} attachments",
                documentId, emailId, emlResult.Attachments.Count);

            // Step 8: Create attachment documents if requested
            var attachmentDocs = new List<AttachmentDocumentInfo>();
            if (request.CreateAttachmentDocuments && emlResult.Attachments.Any(a => a.ShouldCreateDocument))
            {
                foreach (var attachment in emlResult.Attachments.Where(a => a.ShouldCreateDocument && a.Content != null))
                {
                    try
                    {
                        var attachDoc = await CreateAttachmentDocumentAsync(
                            attachment,
                            documentId,
                            emailId,
                            driveId,
                            speFileStore,
                            dataverseService,
                            logger,
                            cancellationToken);

                        if (attachDoc != null)
                        {
                            attachmentDocs.Add(attachDoc);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to create attachment document for {FileName}, continuing",
                            attachment.FileName);
                    }
                }
            }

            // Step 9: Queue for AI processing if enabled
            var shouldQueueAi = request.QueueForAiProcessing ?? emailOptions.Value.AutoEnqueueAi;
            if (shouldQueueAi)
            {
                // TODO: Queue AI processing job for documentId and attachment documents
                logger.LogInformation(
                    "AI processing queue for document {DocumentId} (to be implemented in Phase 2)",
                    documentId);
            }

            return Results.Created(
                $"/api/documents/{documentId}",
                ConvertEmailToDocumentResponse.Succeeded(
                    documentId,
                    fileName,
                    emlResult.FileSizeBytes,
                    uploadResult.Id,
                    emlResult.Attachments.Count,
                    attachmentDocs));
        }
        catch (ODataError ex)
        {
            logger.LogError(ex, "Graph API error during email conversion for {EmailId}", emailId);
            return ProblemDetailsHelper.FromGraphException(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error converting email {EmailId}", emailId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred during email conversion",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    /// <summary>
    /// Check if a document already exists for an email.
    /// </summary>
    private static async Task<IResult> GetEmailDocumentStatusAsync(
        Guid emailId,
        IDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var existingDocId = await CheckExistingDocumentAsync(emailId, dataverseService, cancellationToken);

            if (existingDocId == null)
            {
                return Results.Ok(new EmailDocumentStatusResponse
                {
                    DocumentExists = false
                });
            }

            var doc = await dataverseService.GetDocumentAsync(existingDocId.Value.ToString(), cancellationToken);

            return Results.Ok(new EmailDocumentStatusResponse
            {
                DocumentExists = true,
                DocumentId = existingDocId,
                CreatedOn = doc?.CreatedOn
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking document status for email {EmailId}", emailId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "Failed to check document status",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Check if a document already exists for the given email ID.
    /// Returns the document ID if found, null otherwise.
    /// </summary>
    private static async Task<Guid?> CheckExistingDocumentAsync(
        Guid emailId,
        IDataverseService dataverseService,
        CancellationToken cancellationToken)
    {
        // TODO: Implement query to check for existing document with sprk_Email = emailId
        // This will be implemented when the alternate key (Task 003) is in place
        // For now, return null (no duplicate check)
        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// Create a document record for an email attachment.
    /// </summary>
    private static async Task<AttachmentDocumentInfo?> CreateAttachmentDocumentAsync(
        EmailAttachmentInfo attachment,
        Guid parentDocumentId,
        Guid emailId,
        string driveId,
        SpeFileStore speFileStore,
        IDataverseService dataverseService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Upload attachment to SPE
        attachment.Content!.Position = 0;
        var uploadResult = await speFileStore.UploadSmallAsync(
            driveId,
            attachment.FileName,
            attachment.Content,
            cancellationToken);

        if (uploadResult == null)
        {
            logger.LogWarning("Failed to upload attachment {FileName}", attachment.FileName);
            return null;
        }

        // Create document record
        var createRequest = new CreateDocumentRequest
        {
            Name = attachment.FileName,
            ContainerId = driveId, // Using driveId as container reference
            Description = $"Email attachment from email activity"
        };

        var docIdStr = await dataverseService.CreateDocumentAsync(createRequest, cancellationToken);
        var docId = Guid.Parse(docIdStr);

        // Update with file info and parent relationship
        var updateRequest = new UpdateDocumentRequest
        {
            FileName = attachment.FileName,
            FileSize = attachment.SizeBytes,
            MimeType = attachment.MimeType,
            GraphItemId = uploadResult.Id,
            GraphDriveId = driveId,
            HasFile = true,
            ParentDocumentLookup = parentDocumentId,
            RelationshipType = RelationshipTypeEmailAttachment,
            // Link attachment document to same email activity
            EmailLookup = emailId
        };

        await dataverseService.UpdateDocumentAsync(docIdStr, updateRequest, cancellationToken);

        logger.LogInformation(
            "Created attachment document {DocumentId} for {FileName} ({Size} bytes)",
            docId, attachment.FileName, attachment.SizeBytes);

        return new AttachmentDocumentInfo
        {
            DocumentId = docId,
            FileName = attachment.FileName,
            SizeBytes = attachment.SizeBytes,
            GraphItemId = uploadResult.Id
        };
    }
}
