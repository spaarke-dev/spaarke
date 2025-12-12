using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Document Intelligence endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides document analysis, summarization, and entity extraction via Server-Sent Events (SSE).
/// </summary>
public static class DocumentIntelligenceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapDocumentIntelligenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/document-intelligence")
            .RequireAuthorization()
            .WithTags("AI");

        // POST /api/ai/document-intelligence/analyze - Stream document analysis via SSE
        // Note: AiAuthorizationFilter removed for MVP - Dataverse auth for access checks not yet configured.
        // The user is authenticated (.RequireAuthorization) and typically just uploaded the document.
        // TODO: Re-enable document-level authorization when DataverseAccessDataSource has proper OBO auth.
        group.MapPost("/analyze", StreamAnalyze)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-stream") // 10 requests/minute per user (Task 072)
            .WithName("StreamDocumentAnalysis")
            .WithSummary("Stream document analysis via SSE")
            .WithDescription("Streams AI-generated document analysis (summary, entities, keywords) in real-time using Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/document-intelligence/enqueue - Enqueue single document for background analysis
        // Note: AiAuthorizationFilter disabled for MVP (same as /analyze endpoint)
        group.MapPost("/enqueue", EnqueueAnalysis)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-batch") // 20 requests/minute per user (Task 072)
            .WithName("EnqueueDocumentAnalysis")
            .WithSummary("Enqueue document for background analysis")
            .WithDescription("Submits a document for background AI analysis via Service Bus.")
            .Produces<EnqueueAnalysisResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/document-intelligence/enqueue-batch - Enqueue multiple documents for background analysis
        // Note: AiAuthorizationFilter disabled for MVP (same as /analyze endpoint)
        group.MapPost("/enqueue-batch", EnqueueBatchAnalysis)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-batch") // 20 requests/minute per user (Task 072)
            .WithName("EnqueueBatchDocumentAnalysis")
            .WithSummary("Enqueue multiple documents for background analysis")
            .WithDescription("Submits up to 10 documents for background AI analysis via Service Bus.")
            .Produces<BatchAnalysisResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Stream document analysis endpoint using Server-Sent Events.
    /// Saves the final result to Dataverse after streaming completes.
    /// </summary>
    private static async Task StreamAnalyze(
        DocumentAnalysisRequest request,
        IDocumentIntelligenceService documentIntelligenceService,
        IDataverseService dataverseService,
        HttpContext context,
        ILogger<DocumentIntelligenceService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "Starting SSE stream for document {DocumentId}, TraceId={TraceId}",
            request.DocumentId, context.TraceIdentifier);

        DocumentAnalysisResult? finalResult = null;

        try
        {
            await foreach (var chunk in documentIntelligenceService.AnalyzeStreamAsync(context, request, cancellationToken))
            {
                await WriteSSEAsync(response, chunk, cancellationToken);

                // Capture the final result for Dataverse persistence
                if (chunk.Done && chunk.Result != null)
                {
                    finalResult = chunk.Result;
                    logger.LogInformation(
                        "Captured final result for document {DocumentId}: HasEmailMetadata={HasEmail}, Summary={SummaryLength}",
                        request.DocumentId,
                        chunk.Result.EmailMetadata != null,
                        chunk.Result.Summary?.Length ?? 0);
                }
            }

            logger.LogInformation("SSE stream completed for document {DocumentId}, HasFinalResult={HasResult}",
                request.DocumentId, finalResult != null);

            // Save the result to Dataverse after streaming completes
            if (finalResult != null)
            {
                logger.LogInformation(
                    "Saving to Dataverse for document {DocumentId}: HasEmailMetadata={HasEmail}",
                    request.DocumentId, finalResult.EmailMetadata != null);
                await SaveAnalysisToDataverseAsync(
                    dataverseService, request.DocumentId, finalResult, logger, CancellationToken.None);
            }
            else
            {
                logger.LogWarning("No final result to save for document {DocumentId}", request.DocumentId);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected before streaming completed
            logger.LogInformation(
                "Client disconnected during analysis for document {DocumentId}. " +
                "Background job should be enqueued to complete analysis.",
                request.DocumentId);

            // TODO: Enqueue background job when DocumentAnalysisJobHandler is implemented
            // await jobQueue.EnqueueAsync(new DocumentAnalysisJob(request));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during SSE stream for document {DocumentId}",
                request.DocumentId);

            // Send error chunk to client if connection is still open
            if (!cancellationToken.IsCancellationRequested)
            {
                var errorChunk = AnalysisChunk.FromError("An error occurred during document analysis.");
                await WriteSSEAsync(response, errorChunk, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Write a chunk in SSE format: "data: {json}\n\n"
    /// </summary>
    private static async Task WriteSSEAsync(
        HttpResponse response,
        AnalysisChunk chunk,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single document for background analysis.
    /// </summary>
    private static async Task<IResult> EnqueueAnalysis(
        DocumentAnalysisRequest request,
        JobSubmissionService jobSubmissionService,
        IDataverseService dataverseService,
        HttpContext context,
        ILogger<DocumentIntelligenceService> logger)
    {
        var cancellationToken = context.RequestAborted;

        logger.LogInformation(
            "Enqueuing analysis job for document {DocumentId}, TraceId={TraceId}",
            request.DocumentId, context.TraceIdentifier);

        // Create job contract
        var job = CreateAnalysisJob(request, context.TraceIdentifier);

        // Submit job to Service Bus
        await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

        // Update document status to Pending
        await UpdateDocumentStatusAsync(
            dataverseService, request.DocumentId, AnalysisStatus.Pending, logger, cancellationToken);

        logger.LogInformation(
            "Job {JobId} enqueued for document {DocumentId}",
            job.JobId, request.DocumentId);

        return Results.Accepted(
            value: new EnqueueAnalysisResponse(job.JobId, request.DocumentId));
    }

    /// <summary>
    /// Enqueue multiple documents for background analysis.
    /// </summary>
    private static async Task<IResult> EnqueueBatchAnalysis(
        BatchAnalysisRequest request,
        JobSubmissionService jobSubmissionService,
        IDataverseService dataverseService,
        HttpContext context,
        ILogger<DocumentIntelligenceService> logger)
    {
        var cancellationToken = context.RequestAborted;

        // Validate batch size
        const int maxBatchSize = 10;
        if (request.Documents == null || request.Documents.Count == 0)
        {
            return Results.BadRequest(new { error = "Documents list cannot be empty." });
        }

        if (request.Documents.Count > maxBatchSize)
        {
            return Results.BadRequest(new { error = $"Batch size cannot exceed {maxBatchSize} documents. Received {request.Documents.Count}." });
        }

        logger.LogInformation(
            "Enqueuing batch analysis for {Count} documents, TraceId={TraceId}",
            request.Documents.Count, context.TraceIdentifier);

        var responses = new List<EnqueueAnalysisResponse>();

        // Submit jobs in parallel for efficiency
        var tasks = request.Documents.Select(async doc =>
        {
            var job = CreateAnalysisJob(doc, context.TraceIdentifier);
            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);
            await UpdateDocumentStatusAsync(
                dataverseService, doc.DocumentId, AnalysisStatus.Pending, logger, cancellationToken);
            return new EnqueueAnalysisResponse(job.JobId, doc.DocumentId);
        });

        var results = await Task.WhenAll(tasks);
        responses.AddRange(results);

        logger.LogInformation(
            "Batch enqueue completed: {Count} jobs submitted",
            responses.Count);

        return Results.Accepted(
            value: new BatchAnalysisResponse(responses, responses.Count));
    }

    /// <summary>
    /// Create a job contract for document analysis.
    /// </summary>
    private static JobContract CreateAnalysisJob(DocumentAnalysisRequest request, string correlationId)
    {
        var payload = new DocumentAnalysisJobPayload(request.DocumentId, request.DriveId, request.ItemId);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "ai-analyze",
            SubjectId = request.DocumentId.ToString(),
            CorrelationId = correlationId,
            IdempotencyKey = $"analyze:{request.DocumentId}",
            Payload = JsonDocument.Parse(payloadJson)
        };
    }

    /// <summary>
    /// Update document analysis status in Dataverse.
    /// </summary>
    private static async Task UpdateDocumentStatusAsync(
        IDataverseService dataverseService,
        Guid documentId,
        AnalysisStatus status,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Suppress IDE0060 - parameters will be used when UpdateDocumentRequest is extended
        _ = dataverseService;
        _ = cancellationToken;

        try
        {
            // Note: UpdateDocumentRequest may need to be extended to include summary status field
            // For now, log the status update intent
            logger.LogDebug(
                "Updating document {DocumentId} status to {Status}",
                documentId, status);

            // TODO: Implement actual Dataverse update when UpdateDocumentRequest supports summary fields
            // await dataverseService.UpdateDocumentAsync(documentId.ToString(), new UpdateDocumentRequest { ... }, cancellationToken);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // Log but don't fail the enqueue - the job will still be processed
            logger.LogWarning(ex,
                "Failed to update status for document {DocumentId} to {Status}",
                documentId, status);
        }
    }

    /// <summary>
    /// Save the analysis result to Dataverse after streaming completes.
    /// Replicates the persistence logic from DocumentAnalysisJobHandler for consistency.
    /// </summary>
    private static async Task SaveAnalysisToDataverseAsync(
        IDataverseService dataverseService,
        Guid documentId,
        DocumentAnalysisResult result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var updateRequest = new UpdateDocumentRequest
            {
                Summary = result.Summary,
                SummaryStatus = (int)AnalysisStatus.Completed
            };

            // TL;DR as newline-separated string
            if (result.TlDr.Length > 0)
            {
                updateRequest.TlDr = string.Join("\n", result.TlDr);
            }

            // Keywords (already comma-separated from AI)
            if (!string.IsNullOrWhiteSpace(result.Keywords))
            {
                updateRequest.Keywords = result.Keywords;
            }

            // Extracted entities - convert arrays to newline-separated strings
            var entities = result.Entities;

            if (entities.Organizations.Length > 0)
            {
                updateRequest.ExtractOrganization = string.Join("\n", entities.Organizations);
            }

            if (entities.People.Length > 0)
            {
                updateRequest.ExtractPeople = string.Join("\n", entities.People);
            }

            if (entities.Amounts.Length > 0)
            {
                updateRequest.ExtractFees = string.Join("\n", entities.Amounts);
            }

            if (entities.Dates.Length > 0)
            {
                updateRequest.ExtractDates = string.Join("\n", entities.Dates);
            }

            if (entities.References.Length > 0)
            {
                updateRequest.ExtractReference = string.Join("\n", entities.References);
            }

            // Document type - both raw text and mapped choice value
            if (!string.IsNullOrWhiteSpace(entities.DocumentType))
            {
                updateRequest.ExtractDocumentType = entities.DocumentType;
                updateRequest.DocumentType = DocumentTypeMapper.ToDataverseValue(entities.DocumentType);
            }

            // Email metadata - populate when document is an email file (.eml, .msg)
            logger.LogInformation(
                "Email metadata check for document {DocumentId}: EmailMetadata={HasEmailMetadata}",
                documentId, result.EmailMetadata != null);

            if (result.EmailMetadata != null)
            {
                var email = result.EmailMetadata;

                // Log the SOURCE values before copying
                logger.LogInformation(
                    "Email SOURCE values for {DocumentId}: Subject={Subject}, From={From}, To={To}, Date={Date}, BodyLength={BodyLength}",
                    documentId,
                    email.Subject ?? "(null)",
                    email.From ?? "(null)",
                    email.To ?? "(null)",
                    email.Date?.ToString("o") ?? "(null)",
                    email.Body?.Length ?? 0);

                updateRequest.EmailSubject = email.Subject;
                updateRequest.EmailFrom = email.From;
                updateRequest.EmailTo = email.To;
                updateRequest.EmailDate = email.Date;
                updateRequest.EmailBody = email.Body;

                // Serialize attachments list to JSON for storage
                if (email.HasAttachments)
                {
                    updateRequest.Attachments = JsonSerializer.Serialize(email.Attachments);
                }

                // Log the TARGET values after copying
                logger.LogInformation(
                    "Email TARGET values for {DocumentId}: Subject={Subject}, From={From}, To={To}, Date={Date}, Attachments={Attachments}",
                    documentId,
                    updateRequest.EmailSubject ?? "(null)",
                    updateRequest.EmailFrom ?? "(null)",
                    updateRequest.EmailTo ?? "(null)",
                    updateRequest.EmailDate?.ToString("o") ?? "(null)",
                    updateRequest.Attachments ?? "(null)");
            }

            await dataverseService.UpdateDocumentAsync(documentId.ToString(), updateRequest, cancellationToken);

            logger.LogInformation(
                "Saved analysis to Dataverse for document {DocumentId}: Summary={SummaryLength}, Orgs={Orgs}, People={People}, IsEmail={IsEmail}",
                documentId,
                result.Summary?.Length ?? 0,
                entities.Organizations.Length,
                entities.People.Length,
                result.EmailMetadata != null);
        }
        catch (Exception ex)
        {
            // Log but don't fail the request - the SSE stream already completed successfully
            logger.LogWarning(ex,
                "Failed to save analysis to Dataverse for document {DocumentId}",
                documentId);
        }
    }
}
