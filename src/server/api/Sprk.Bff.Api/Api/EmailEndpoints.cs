using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Email-to-document conversion endpoints following ADR-008.
/// Provides manual email conversion (synchronous), status queries, and webhook trigger.
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

    // Job type for email processing
    private const string JobTypeProcessEmail = "ProcessEmailToDocument";

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

        // GET /api/v1/emails/{emailId}/association-preview - Preview automatic associations
        group.MapGet("/{emailId:guid}/association-preview", GetAssociationPreviewAsync)
            .WithName("GetEmailAssociationPreview")
            .WithDescription("Preview automatic associations for an email before saving, showing all detected signals and confidence scores")
            .Produces<AssociationSignalsResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // POST /api/v1/emails/webhook-trigger - Dataverse webhook receiver (AllowAnonymous with secret validation)
        app.MapPost("/api/v1/emails/webhook-trigger", HandleWebhookTriggerAsync)
            .AllowAnonymous()
            .WithName("EmailWebhookTrigger")
            .WithTags("Email Conversion")
            .WithDescription("Receive Dataverse webhook notifications for new email activities")
            .Produces<WebhookTriggerResponse>(StatusCodes.Status202Accepted)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // Admin endpoints for email processing management
        var adminGroup = app.MapGroup("/api/v1/emails/admin")
            .RequireAuthorization()
            .WithTags("Email Admin");

        // POST /api/v1/emails/admin/seed-rules - Seed default email processing rules
        adminGroup.MapPost("/seed-rules", SeedDefaultRulesAsync)
            .WithName("SeedEmailProcessingRules")
            .WithDescription("Seed default email processing rules to Dataverse (idempotent)")
            .Produces<SeedRulesResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // GET /api/v1/emails/admin/rules - Get all active rules (for debugging/admin)
        adminGroup.MapGet("/rules", GetActiveRulesAsync)
            .WithName("GetActiveEmailRules")
            .WithDescription("Get all active email processing rules from Dataverse")
            .Produces<IReadOnlyList<EmailFilterRule>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // POST /api/v1/emails/admin/refresh-rules-cache - Force refresh rules cache
        adminGroup.MapPost("/refresh-rules-cache", RefreshRulesCacheAsync)
            .WithName("RefreshEmailRulesCache")
            .WithDescription("Force refresh the email processing rules cache")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Handle Dataverse webhook notification for new email activities.
    /// Validates webhook signature and enqueues a processing job.
    /// </summary>
    private static async Task<IResult> HandleWebhookTriggerAsync(
        HttpRequest request,
        JobSubmissionService jobSubmissionService,
        IOptions<EmailProcessingOptions> emailOptions,
        Telemetry.EmailTelemetry telemetry,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var traceId = request.HttpContext.TraceIdentifier;
        var correlationId = request.Headers["X-Correlation-Id"].FirstOrDefault() ?? traceId;
        var stopwatch = telemetry.RecordWebhookReceived();

        try
        {
            // Step 1: Validate webhook is enabled
            if (!emailOptions.Value.EnableWebhook)
            {
                logger.LogWarning("Webhook trigger received but webhooks are disabled");
                telemetry.RecordWebhookRejected(stopwatch, "disabled");
                return Results.Problem(
                    title: "Webhook Disabled",
                    detail: "Email webhook processing is currently disabled",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Step 2: Read and validate request body
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Empty webhook payload received");
                telemetry.RecordWebhookRejected(stopwatch, "empty_payload");
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Webhook payload is empty",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Step 3: Validate webhook signature
            var signatureValid = await ValidateWebhookSignatureAsync(
                request,
                requestBody,
                emailOptions.Value.WebhookSecret,
                logger);

            if (!signatureValid)
            {
                logger.LogWarning("Invalid webhook signature for request {TraceId}", traceId);
                telemetry.RecordWebhookRejected(stopwatch, "invalid_signature");
                return Results.Problem(
                    title: "Unauthorized",
                    detail: "Invalid webhook signature",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // Step 4: Parse webhook payload
            DataverseWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<DataverseWebhookPayload>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse webhook payload");
                telemetry.RecordWebhookRejected(stopwatch, "invalid_json");
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Failed to parse webhook payload as JSON",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (payload == null || payload.PrimaryEntityId == Guid.Empty)
            {
                logger.LogWarning("Webhook payload missing PrimaryEntityId");
                telemetry.RecordWebhookRejected(stopwatch, "missing_entity_id");
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Webhook payload missing required PrimaryEntityId",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Step 5: Validate entity type
            if (!string.Equals(payload.PrimaryEntityName, "email", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Webhook received for non-email entity: {EntityName}",
                    payload.PrimaryEntityName);
                telemetry.RecordWebhookRejected(stopwatch, "wrong_entity_type");
                return Results.Problem(
                    title: "Invalid Entity",
                    detail: $"Webhook is for entity '{payload.PrimaryEntityName}', expected 'email'",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var emailId = payload.PrimaryEntityId;

            logger.LogInformation(
                "Received webhook for email {EmailId}, Message={Message}, CorrelationId={CorrelationId}",
                emailId, payload.MessageName, correlationId);

            // Step 6: Create and submit job
            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                EmailId = emailId,
                TriggerSource = "Webhook",
                MessageName = payload.MessageName,
                WebhookCorrelationId = payload.CorrelationId
            }));

            var job = new JobContract
            {
                JobType = JobTypeProcessEmail,
                SubjectId = emailId.ToString(),
                CorrelationId = correlationId,
                IdempotencyKey = $"Email:{emailId}:Archive",
                Payload = jobPayload,
                MaxAttempts = 3
            };

            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Submitted job {JobId} for email {EmailId} with IdempotencyKey={IdempotencyKey}",
                job.JobId, emailId, job.IdempotencyKey);

            telemetry.RecordWebhookEnqueued(stopwatch, emailId);

            return Results.Accepted(
                value: new WebhookTriggerResponse
                {
                    Accepted = true,
                    JobId = job.JobId,
                    CorrelationId = correlationId,
                    Message = $"Email {emailId} queued for processing"
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing webhook trigger, TraceId={TraceId}", traceId);
            telemetry.RecordWebhookRejected(stopwatch, "internal_error");
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred processing the webhook",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    /// <summary>
    /// Validate the webhook signature from Dataverse.
    /// Dataverse webhooks can be validated using either:
    /// 1. X-Dataverse-Signature header (HMAC-SHA256 of request body with shared secret)
    /// 2. HttpHeader authentication type with custom header
    /// </summary>
    private static Task<bool> ValidateWebhookSignatureAsync(
        HttpRequest request,
        string requestBody,
        string? webhookSecret,
        ILogger logger)
    {
        // If no secret configured, skip validation (development mode)
        if (string.IsNullOrEmpty(webhookSecret))
        {
            logger.LogWarning("Webhook secret not configured - skipping signature validation (DEVELOPMENT MODE)");
            return Task.FromResult(true);
        }

        // Check for X-Dataverse-Signature header
        var signature = request.Headers["X-Dataverse-Signature"].FirstOrDefault();

        // Also check for custom authorization header (HttpHeader auth type)
        if (string.IsNullOrEmpty(signature))
        {
            signature = request.Headers["X-Webhook-Secret"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("No webhook signature header found");
            return Task.FromResult(false);
        }

        // For HttpHeader auth type, just compare secrets directly
        if (signature == webhookSecret)
        {
            return Task.FromResult(true);
        }

        // For HMAC signature, compute and compare
        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(requestBody));
            var computedSignature = Convert.ToBase64String(hash);

            // Dataverse may send with or without "sha256=" prefix
            var signatureToCompare = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
                ? signature[7..]
                : signature;

            var isValid = string.Equals(computedSignature, signatureToCompare, StringComparison.Ordinal);

            if (!isValid)
            {
                logger.LogWarning("Webhook signature mismatch");
            }

            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating webhook signature");
            return Task.FromResult(false);
        }
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
        catch (Exception ex) when (ex.GetType().FullName?.StartsWith("Microsoft.Graph") == true)
        {
            // Graph API error - log and return generic error (ADR-007: don't expose Graph types in endpoints)
            logger.LogError(ex, "Graph API error during email conversion for {EmailId}", emailId);
            return Results.Problem(
                title: "Storage Service Error",
                detail: "An error occurred while accessing the storage service",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
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
    /// Preview automatic associations for an email before saving.
    /// Returns all detected signals with confidence scores and the recommended association.
    /// </summary>
    private static async Task<IResult> GetAssociationPreviewAsync(
        Guid emailId,
        IEmailAssociationService associationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Getting association preview for email {EmailId}", emailId);

            var signals = await associationService.GetAssociationSignalsAsync(emailId, cancellationToken);

            logger.LogInformation(
                "Association preview for email {EmailId}: {SignalCount} signals, recommended={HasRecommendation}",
                emailId,
                signals.Signals.Count,
                signals.RecommendedAssociation != null);

            return Results.Ok(signals);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting association preview for email {EmailId}", emailId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "Failed to get association preview",
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Admin Endpoint Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seed default email processing rules to Dataverse.
    /// Idempotent - skips rules that already exist.
    /// </summary>
    private static async Task<IResult> SeedDefaultRulesAsync(
        EmailRuleSeedService seedService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting email processing rules seed operation");

            var result = await seedService.SeedDefaultRulesAsync(forceUpdate: false, cancellationToken);

            return Results.Ok(new SeedRulesResponse
            {
                Created = result.Created,
                Skipped = result.Skipped,
                Errors = result.Errors,
                TotalRulesAvailable = EmailRuleSeedService.DefaultRules.Count,
                Success = result.IsSuccess
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding email processing rules");
            return Results.Problem(
                title: "Seed Operation Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get all active email processing rules (for debugging/admin).
    /// </summary>
    private static async Task<IResult> GetActiveRulesAsync(
        IEmailFilterService filterService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var rules = await filterService.GetActiveRulesAsync(cancellationToken);
            return Results.Ok(rules);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving active email processing rules");
            return Results.Problem(
                title: "Failed to Retrieve Rules",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Force refresh the email processing rules cache.
    /// </summary>
    private static async Task<IResult> RefreshRulesCacheAsync(
        IEmailFilterService filterService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Force refreshing email processing rules cache");
            await filterService.RefreshRulesAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing email processing rules cache");
            return Results.Problem(
                title: "Cache Refresh Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>
/// Response from seeding default email processing rules.
/// </summary>
public record SeedRulesResponse
{
    /// <summary>Number of rules created.</summary>
    public int Created { get; init; }

    /// <summary>Number of rules skipped (already existed).</summary>
    public int Skipped { get; init; }

    /// <summary>List of error messages if any operations failed.</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>Total number of default rules available to seed.</summary>
    public int TotalRulesAvailable { get; init; }

    /// <summary>Whether the operation completed successfully (no errors).</summary>
    public bool Success { get; init; }
}
