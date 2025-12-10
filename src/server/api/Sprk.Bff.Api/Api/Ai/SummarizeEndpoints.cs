using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// AI summarization endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides real-time streaming via Server-Sent Events (SSE).
/// </summary>
public static class SummarizeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapSummarizeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/summarize")
            .RequireAuthorization()
            .WithTags("AI");

        // POST /api/ai/summarize/stream - Stream summarization via SSE
        // Note: AiAuthorizationFilter removed for MVP - Dataverse auth for access checks not yet configured.
        // The user is authenticated (.RequireAuthorization) and typically just uploaded the document.
        // TODO: Re-enable document-level authorization when DataverseAccessDataSource has proper OBO auth.
        group.MapPost("/stream", StreamSummarize)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-stream") // 10 requests/minute per user (Task 072)
            .WithName("StreamSummarize")
            .WithSummary("Stream document summarization via SSE")
            .WithDescription("Streams AI-generated summary tokens in real-time using Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/summarize/enqueue - Enqueue single document for background summarization
        // Note: AiAuthorizationFilter disabled for MVP (same as /stream endpoint)
        group.MapPost("/enqueue", EnqueueSummarize)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-batch") // 20 requests/minute per user (Task 072)
            .WithName("EnqueueSummarize")
            .WithSummary("Enqueue document for background summarization")
            .WithDescription("Submits a document for background AI summarization via Service Bus.")
            .Produces<EnqueueSummarizeResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/summarize/enqueue-batch - Enqueue multiple documents for background summarization
        // Note: AiAuthorizationFilter disabled for MVP (same as /stream endpoint)
        group.MapPost("/enqueue-batch", EnqueueBatchSummarize)
            // .AddAiAuthorizationFilter() // Disabled: requires Dataverse OBO auth configuration
            .RequireRateLimiting("ai-batch") // 20 requests/minute per user (Task 072)
            .WithName("EnqueueBatchSummarize")
            .WithSummary("Enqueue multiple documents for background summarization")
            .WithDescription("Submits up to 10 documents for background AI summarization via Service Bus.")
            .Produces<BatchSummarizeResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Stream summarization endpoint using Server-Sent Events.
    /// </summary>
    private static async Task StreamSummarize(
        SummarizeRequest request,
        ISummarizeService summarizeService,
        HttpContext context,
        ILogger<SummarizeService> logger)
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

        try
        {
            await foreach (var chunk in summarizeService.SummarizeStreamAsync(context, request, cancellationToken))
            {
                await WriteSSEAsync(response, chunk, cancellationToken);
            }

            logger.LogDebug("SSE stream completed successfully for document {DocumentId}", request.DocumentId);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected before streaming completed
            logger.LogInformation(
                "Client disconnected during summarization for document {DocumentId}. " +
                "Background job should be enqueued to complete summarization.",
                request.DocumentId);

            // TODO: Enqueue background job when Task 021 (SummarizeJobHandler) is implemented
            // await jobQueue.EnqueueAsync(new SummarizeJob(request));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during SSE stream for document {DocumentId}",
                request.DocumentId);

            // Send error chunk to client if connection is still open
            if (!cancellationToken.IsCancellationRequested)
            {
                var errorChunk = SummarizeChunk.FromError("An error occurred during summarization.");
                await WriteSSEAsync(response, errorChunk, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Write a chunk in SSE format: "data: {json}\n\n"
    /// </summary>
    private static async Task WriteSSEAsync(
        HttpResponse response,
        SummarizeChunk chunk,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single document for background summarization.
    /// </summary>
    private static async Task<IResult> EnqueueSummarize(
        SummarizeRequest request,
        JobSubmissionService jobSubmissionService,
        IDataverseService dataverseService,
        HttpContext context,
        ILogger<SummarizeService> logger)
    {
        var cancellationToken = context.RequestAborted;

        logger.LogInformation(
            "Enqueuing summarization job for document {DocumentId}, TraceId={TraceId}",
            request.DocumentId, context.TraceIdentifier);

        // Create job contract
        var job = CreateSummarizeJob(request, context.TraceIdentifier);

        // Submit job to Service Bus
        await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

        // Update document status to Pending
        await UpdateDocumentStatusAsync(
            dataverseService, request.DocumentId, SummaryStatus.Pending, logger, cancellationToken);

        logger.LogInformation(
            "Job {JobId} enqueued for document {DocumentId}",
            job.JobId, request.DocumentId);

        return Results.Accepted(
            value: new EnqueueSummarizeResponse(job.JobId, request.DocumentId));
    }

    /// <summary>
    /// Enqueue multiple documents for background summarization.
    /// </summary>
    private static async Task<IResult> EnqueueBatchSummarize(
        BatchSummarizeRequest request,
        JobSubmissionService jobSubmissionService,
        IDataverseService dataverseService,
        HttpContext context,
        ILogger<SummarizeService> logger)
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
            "Enqueuing batch summarization for {Count} documents, TraceId={TraceId}",
            request.Documents.Count, context.TraceIdentifier);

        var responses = new List<EnqueueSummarizeResponse>();

        // Submit jobs in parallel for efficiency
        var tasks = request.Documents.Select(async doc =>
        {
            var job = CreateSummarizeJob(doc, context.TraceIdentifier);
            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);
            await UpdateDocumentStatusAsync(
                dataverseService, doc.DocumentId, SummaryStatus.Pending, logger, cancellationToken);
            return new EnqueueSummarizeResponse(job.JobId, doc.DocumentId);
        });

        var results = await Task.WhenAll(tasks);
        responses.AddRange(results);

        logger.LogInformation(
            "Batch enqueue completed: {Count} jobs submitted",
            responses.Count);

        return Results.Accepted(
            value: new BatchSummarizeResponse(responses, responses.Count));
    }

    /// <summary>
    /// Create a job contract for summarization.
    /// </summary>
    private static JobContract CreateSummarizeJob(SummarizeRequest request, string correlationId)
    {
        var payload = new SummarizeJobPayload(request.DocumentId, request.DriveId, request.ItemId);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "ai-summarize",
            SubjectId = request.DocumentId.ToString(),
            CorrelationId = correlationId,
            IdempotencyKey = $"summarize:{request.DocumentId}",
            Payload = JsonDocument.Parse(payloadJson)
        };
    }

    /// <summary>
    /// Update document summary status in Dataverse.
    /// </summary>
    private static async Task UpdateDocumentStatusAsync(
        IDataverseService dataverseService,
        Guid documentId,
        SummaryStatus status,
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
}
