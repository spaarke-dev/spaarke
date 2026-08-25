using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Document checkout/checkin/discard operations endpoints.
/// Implements check-out/check-in version control for document editing.
/// </summary>
public static class DocumentOperationsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents/{documentId:guid}")
            .RequireAuthorization()
            .WithTags("Document Operations");

        // POST /api/documents/{documentId}/checkout
        //
        // Finding H3 (task 022). The comment this replaces claimed "PCF controls button visibility
        // based on Dataverse security profile / actual permissions enforced by Graph API via OBO".
        // Both halves were wrong for this route: client-side button visibility is not enforcement,
        // and the checkout path is app-only — it does not reach Graph under the caller's identity, so
        // nothing downstream ever evaluated the caller. The route returns an EDITABLE url and takes
        // the document's lock, so Write is the requirement.
        group.MapPost("/checkout", CheckoutDocument)
            .WithName("CheckoutDocument")
            .WithDescription("Locks a document for editing and returns the edit URL")
            .Produces<CheckoutResponse>(200)
            .Produces<DocumentLockedError>(409)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("write");

        // POST /api/documents/{documentId}/checkin
        // Finding H3 — creates a new version of the document's content.
        group.MapPost("/checkin", CheckInDocument)
            .WithName("CheckInDocument")
            .WithDescription("Releases the document lock and creates a new version")
            .Produces<CheckInResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("write");

        // POST /api/documents/{documentId}/discard
        //
        // Finding H3 — clears the lock, discarding the checked-out changes. Write, like its siblings.
        // The handler ALSO has its own "you can only discard checkouts you initiated" check
        // (NotAuthorizedDiscardResult), which is a narrower, different question: this filter asks
        // whether the caller may touch the document at all, and runs first.
        group.MapPost("/discard", DiscardCheckout)
            .WithName("DiscardCheckout")
            .WithDescription("Cancels the checkout without saving changes")
            .Produces<DiscardResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("write");

        // DELETE /api/documents/{documentId}
        //
        // Finding C2 (unified-access-control-r2 task 022) — this route destroyed the Dataverse row
        // AND the SPE file with NO per-document authorization: any authenticated caller could delete
        // any document by GUID. It is reachable from a shipped client hook. The delete itself runs
        // app-only, so Dataverse's own row-level security never sees the caller — this filter is the
        // entire boundary (project CLAUDE.md fact 1).
        group.MapDelete("", DeleteDocument)
            .WithName("DeleteDocument")
            .WithDescription("Deletes a document from both Dataverse and SharePoint Embedded")
            .Produces<DeleteDocumentResponse>(200)
            .Produces<DocumentLockedError>(409)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("delete");

        // GET /api/documents/{documentId}/checkout-status
        //
        // Finding H3 — discloses WHO holds the lock and when they took it, for any document by GUID.
        // Read, not Write: it reveals the document's state without changing it.
        group.MapGet("/checkout-status", GetCheckoutStatus)
            .WithName("GetCheckoutStatus")
            .WithDescription("Gets the current checkout status of a document")
            .Produces<CheckoutStatusResponse>(200)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("read");

        // POST /api/documents/{documentId}/analyze
        //
        // Finding H3. Operation is `write`, decided explicitly by the owner 2026-08-24 rather than
        // defaulted. The case for `read` was real: the analysis is written to a DIFFERENT entity
        // than the one this filter authorizes, which is the same reasoning that made
        // "finance.confirm" deliberately NOT require Create (see OperationAccessPolicy). The case
        // for `write` won because this route commits real resources on the caller's say-so — it
        // spends AI credits and enqueues background work — and because the analysis lands back on
        // the document's own profile fields, so it does mutate the authorized record's state.
        group.MapPost("/analyze", TriggerDocumentAnalysis)
            .WithName("TriggerDocumentAnalysis")
            .WithDescription("Queues a document for AI analysis (Document Profile) via Service Bus")
            .Produces(202)
            .ProducesProblem(403)
            .ProducesProblem(401)
            .AddDocumentAuthorizationFilter("write");

        return app;
    }

    /// <summary>
    /// POST /api/documents/{documentId}/checkout
    /// Locks a document for editing by the current user.
    /// </summary>
    private static async Task<IResult> CheckoutDocument(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Checkout endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.CheckoutAsync(documentId, user, correlationId, ct);

            return result switch
            {
                SuccessCheckoutResult success => TypedResults.Ok(success.Response),
                NotFoundCheckoutResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                ConflictCheckoutResult conflict => TypedResults.Json(
                    conflict.ConflictError,
                    statusCode: 409
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during checkout"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checkout failed for document {DocumentId}", documentId);

            // Include detailed error info for debugging (in dev/staging)
            var errorDetail = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetail += $" | Inner: {ex.InnerException.Message}";
            }

            return TypedResults.Problem(
                statusCode: 500,
                title: "Checkout Failed",
                detail: errorDetail,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["exceptionType"] = ex.GetType().Name
                }
            );
        }
    }

    /// <summary>
    /// POST /api/documents/{documentId}/checkin
    /// Releases the lock and creates a new version.
    /// Triggers re-indexing for semantic search if enabled.
    /// </summary>
    private static async Task<IResult> CheckInDocument(
        Guid documentId,
        [FromBody] CheckInRequest? request,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] JobSubmissionService jobSubmissionService,
        [FromServices] IOptions<ReindexingOptions> reindexingOptions,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Check-in endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.CheckInAsync(
                documentId,
                request?.Comment,
                user,
                correlationId,
                ct
            );

            // If check-in successful, trigger re-indexing
            if (result is SuccessCheckInResult success)
            {
                var reindexTriggered = await TryEnqueueReindexJobAsync(
                    success.FileInfo,
                    reindexingOptions.Value,
                    jobSubmissionService,
                    correlationId,
                    logger,
                    ct);

                // Update response with reindex status
                var response = success.Response with { AiAnalysisTriggered = reindexTriggered };
                return TypedResults.Ok(response);
            }

            return result switch
            {
                NotFoundCheckInResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotCheckedOutCheckInResult => TypedResults.Problem(
                    statusCode: 400,
                    title: "Document Not Checked Out",
                    detail: "Cannot check in a document that is not checked out",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during check-in"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Check-in failed for document {DocumentId}", documentId);

            // Include detailed error info for debugging
            var errorDetail = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetail += $" | Inner: {ex.InnerException.Message}";
            }

            return TypedResults.Problem(
                statusCode: 500,
                title: "Check-in Failed",
                detail: errorDetail,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["exceptionType"] = ex.GetType().Name
                }
            );
        }
    }

    /// <summary>
    /// Enqueue a RAG indexing job for re-indexing after document check-in.
    /// Fire-and-forget - does not block check-in response.
    /// </summary>
    private static async Task<bool> TryEnqueueReindexJobAsync(
        DocumentFileInfo? fileInfo,
        ReindexingOptions options,
        JobSubmissionService jobSubmissionService,
        string correlationId,
        ILogger logger,
        CancellationToken ct)
    {
        // Check if reindexing is enabled
        if (!options.Enabled || !options.TriggerOnCheckin)
        {
            logger.LogDebug("Re-indexing disabled or check-in trigger disabled");
            return false;
        }

        // Validate tenant configuration
        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            logger.LogWarning("Re-indexing TenantId not configured - skipping re-index for check-in");
            return false;
        }

        // Validate file info available
        if (fileInfo == null)
        {
            logger.LogWarning("No file info available for re-indexing after check-in");
            return false;
        }

        try
        {
            // Build job payload
            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingJobPayload
            {
                TenantId = options.TenantId,
                DriveId = fileInfo.DriveId,
                ItemId = fileInfo.ItemId,
                FileName = fileInfo.FileName,
                DocumentId = fileInfo.DocumentId.ToString(),
                Source = "CheckinTrigger",
                EnqueuedAt = DateTimeOffset.UtcNow
            }));

            // Create and submit job
            var idempotencyKey = $"checkin-reindex-{fileInfo.DocumentId}-{DateTimeOffset.UtcNow.Ticks}";
            var job = new JobContract
            {
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = fileInfo.ItemId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
                Payload = jobPayload,
                MaxAttempts = 3
            };

            await jobSubmissionService.SubmitJobAsync(job, ct);

            logger.LogInformation(
                "Enqueued re-index job {JobId} for document {DocumentId} after check-in",
                job.JobId, fileInfo.DocumentId);

            return true;
        }
        catch (Exception ex)
        {
            // Log but don't fail check-in - re-indexing is not critical
            logger.LogError(ex,
                "Failed to enqueue re-index job for document {DocumentId} after check-in",
                fileInfo?.DocumentId);
            return false;
        }
    }

    /// <summary>
    /// POST /api/documents/{documentId}/discard
    /// Cancels the checkout without saving changes.
    /// </summary>
    private static async Task<IResult> DiscardCheckout(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Discard endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.DiscardAsync(documentId, user, correlationId, ct);

            return result switch
            {
                SuccessDiscardResult success => TypedResults.Ok(success.Response),
                NotFoundDiscardResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotCheckedOutDiscardResult => TypedResults.Problem(
                    statusCode: 400,
                    title: "Document Not Checked Out",
                    detail: "Cannot discard checkout for a document that is not checked out",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotAuthorizedDiscardResult => TypedResults.Problem(
                    statusCode: 403,
                    title: "Not Authorized",
                    detail: "You can only discard checkouts that you initiated",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during discard"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discard failed for document {DocumentId}", documentId);

            // Include detailed error info for debugging
            var errorDetail = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetail += $" | Inner: {ex.InnerException.Message}";
            }

            return TypedResults.Problem(
                statusCode: 500,
                title: "Discard Failed",
                detail: errorDetail,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["exceptionType"] = ex.GetType().Name
                }
            );
        }
    }

    /// <summary>
    /// DELETE /api/documents/{documentId}
    /// Deletes a document from both Dataverse and SharePoint Embedded.
    /// </summary>
    private static async Task<IResult> DeleteDocument(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Delete endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.DeleteAsync(documentId, correlationId, ct);

            return result switch
            {
                SuccessDeleteResult success => TypedResults.Ok(success.Response),
                NotFoundDeleteResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                CheckedOutDeleteResult checkedOut => TypedResults.Json(
                    checkedOut.CheckedOutError,
                    statusCode: 409
                ),
                FailedDeleteResult failed => TypedResults.Problem(
                    statusCode: 500,
                    title: "Delete Failed",
                    detail: "An error occurred while deleting the document",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during delete"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete failed for document {DocumentId}", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Delete Failed",
                detail: "An error occurred while deleting the document",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
            );
        }
    }

    /// <summary>
    /// GET /api/documents/{documentId}/checkout-status
    /// Returns the current checkout status of a document.
    /// </summary>
    private static async Task<IResult> GetCheckoutStatus(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "GetCheckoutStatus endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var status = await checkoutService.GetCheckoutStatusAsync(documentId, user, ct);

            if (status == null)
            {
                return TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                );
            }

            return TypedResults.Ok(new CheckoutStatusResponse(
                IsCheckedOut: status.IsCheckedOut,
                CheckedOutBy: status.CheckedOutBy,
                CheckedOutAt: status.CheckedOutAt,
                IsCurrentUser: status.IsCurrentUser,
                CorrelationId: correlationId
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetCheckoutStatus failed for document {DocumentId}", documentId);

            return TypedResults.Problem(
                statusCode: 500,
                title: "Get Checkout Status Failed",
                detail: ex.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["exceptionType"] = ex.GetType().Name
                }
            );
        }
    }

    /// <summary>
    /// POST /api/documents/{documentId}/analyze
    /// Queues a document for AI analysis (Document Profile) via Service Bus.
    /// The background handler downloads the file from SPE, runs the AI playbook,
    /// and populates profile fields (summary, keywords, classification, entities).
    /// </summary>
    private static async Task<IResult> TriggerDocumentAnalysis(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] JobSubmissionService jobSubmissionService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "TriggerDocumentAnalysis endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var analysisJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = correlationId,
                IdempotencyKey = $"analysis-{documentId}-documentprofile",
                Attempt = 1,
                MaxAttempts = 3,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    DocumentId = documentId,
                    PlaybookName = "Document Profile",
                    Source = "MatterCreationWizard",
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await jobSubmissionService.SubmitJobAsync(analysisJob, ct);

            logger.LogInformation(
                "Document analysis job {JobId} queued for document {DocumentId} [{CorrelationId}]",
                analysisJob.JobId, documentId, correlationId);

            return TypedResults.Accepted(
                $"/api/documents/{documentId}",
                new { jobId = analysisJob.JobId, documentId, status = "queued" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue analysis for document {DocumentId}", documentId);

            return TypedResults.Problem(
                statusCode: 500,
                title: "Analysis Queue Failed",
                detail: $"Failed to queue document analysis: {ex.Message}",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                }
            );
        }
    }
}
