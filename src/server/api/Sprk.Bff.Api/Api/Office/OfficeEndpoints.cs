using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Api.Office;

/// <summary>
/// Office integration endpoints following ADR-001 Minimal API pattern.
/// Groups all Office Add-in operations (save, share, search, jobs) under /office routes.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints support the Outlook and Word add-ins for saving emails,
/// attachments, and documents to SharePoint Embedded containers.
/// </para>
/// <para>
/// All endpoints use endpoint filters for authorization per ADR-008 and
/// return ProblemDetails for errors per ADR-019.
/// </para>
/// </remarks>
public static class OfficeEndpoints
{
    /// <summary>
    /// Maps all Office-related endpoints to the application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/office")
            .WithTags("Office")
            .RequireAuthorization();

        var env = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        // Health check endpoint for Office add-ins
        MapHealthEndpoints(group);

        // Save endpoints (email, attachment, document)
        MapSaveEndpoints(group, env);

        // Job status endpoints
        MapJobEndpoints(group);

        // Search endpoints (entities, documents)
        MapSearchEndpoints(group);

        // Quick create endpoints — inline "New record" for the add-in "Related to" picker.
        // Implemented for Matter + Project (email-communication-intelligence-r2 Slice 3, #10).
        MapQuickCreateEndpoints(group);

        // Share endpoints (links, attach) - Task 027/028
        MapShareEndpoints(group);

        // Recent locations endpoint
        MapRecentEndpoints(group);

        // Generate Profile trigger (FR-08, spaarkeai-word-add-in-r1 task 022)
        MapDocumentProfileEndpoints(group);

        return app;
    }

    /// <summary>
    /// Maps health check endpoints for Office add-in connectivity testing.
    /// </summary>
    private static void MapHealthEndpoints(RouteGroupBuilder group)
    {
        // GET /office/health - Health check for Office add-ins
        group.MapGet("/health", GetHealthAsync)
            .WithName("GetOfficeHealth")
            .WithDescription("Health check endpoint for Office add-in connectivity testing")
            .AllowAnonymous()
            .RequireRateLimiting("anonymous") // Task AUTHV2-049 — anonymous health check; 10/min per IP
            .Produces<OfficeHealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Health check endpoint handler.
    /// </summary>
    private static Ok<OfficeHealthResponse> GetHealthAsync(
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context)
    {
        logger.LogInformation("Office health check requested from {RemoteIp}",
            context.Connection.RemoteIpAddress);

        var response = new OfficeHealthResponse
        {
            Status = "healthy",
            Service = "SDAP Office Integration",
            Version = "1.0.0",
            Timestamp = DateTimeOffset.UtcNow
        };

        return TypedResults.Ok(response);
    }

    #region Save Endpoints

    /// <summary>
    /// Maps save endpoints for Office add-in content saving.
    /// Applies OfficeAuthFilter for authentication, EntityAccessFilter for target entity access,
    /// and OfficeRateLimitFilter for rate limiting (10 requests/minute/user per spec.md).
    /// </summary>
    private static void MapSaveEndpoints(RouteGroupBuilder group, IWebHostEnvironment env)
    {
        // DEBUG: Raw body capture endpoint to diagnose 400 errors (Development only)
        if (env.IsDevelopment())
        {
            group.MapPost("/save-debug", async (HttpContext context, ILogger<Program> logger) =>
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                logger.LogInformation("DEBUG /office/save-debug: Raw request body ({Length} bytes): {Body}",
                    body.Length, body);

                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var request = System.Text.Json.JsonSerializer.Deserialize<SaveRequest>(body, options);
                    logger.LogInformation("DEBUG /office/save-debug: Deserialization succeeded. ContentType={ContentType}",
                        request?.ContentType);
                    return Results.Ok(new
                    {
                        success = true,
                        contentType = request?.ContentType.ToString(),
                        hasEmail = request?.Email != null,
                        hasAttachment = request?.Attachment != null,
                        hasDocument = request?.Document != null,
                        hasTargetEntity = request?.TargetEntity != null
                    });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    logger.LogError(ex, "DEBUG /office/save-debug: JSON deserialization failed");
                    return Results.Ok(new
                    {
                        success = false,
                        error = ex.Message,
                        innerError = ex.InnerException?.Message,
                        path = ex.Path,
                        lineNumber = ex.LineNumber,
                        bodyPreview = body.Length > 500 ? body[..500] + "..." : body
                    });
                }
            })
                .WithName("OfficeSaveDebug")
                .WithDescription("DEBUG: Diagnostic endpoint to test request body parsing (Development only)")
                .AllowAnonymous();
        }

        // POST /office/save - Submit email, attachment, or document for saving
        // Authorization: OfficeAuthFilter validates user authentication,
        //                EntityAccessFilter validates user has access to target entity
        // Idempotency (task 039 — see SaveAsync's remarks for the contract): the persistent job de-dupe keys on the
        //   BODY idempotencyKey, else on the server's own key (content-aware for Document saves). IdempotencyFilter's
        //   X-Idempotency-Key response cache is bound to that same key for Document saves
        //   (DocumentSaveIdempotencyBinding), so a reused header can never replay a response for a different document.
        //   A VERSION save is never replayed from that cache (task 047, SaveResponseMayBeReplayed): whether it repeats
        //   a completed save depends on what the document holds now, which only the service can check.
        // Rate Limit: 10 requests/minute/user (per spec.md)
        group.MapPost("/save", SaveAsync)
            .WithName("OfficeSave")
            .WithDescription("Submit email, attachment, or document for saving to Spaarke DMS")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Save)
            .AddIdempotencyFilter( // Task 030 + task 039 (finding 4) + task 047
                bindClientKeyTo: DocumentSaveIdempotencyBinding,
                mayReplayResponse: SaveResponseMayBeReplayed)
            .AddOfficeAuthFilter()   // Task 073 - baseline Office-caller authentication (sets HttpContext.Items[UserIdKey])
            .AddEntityAccessFilter() // Task 073 - entity-scoped: caller must have access to SaveRequest.TargetEntity
            .AddOfficeVersionSaveAuthorizationFilter() // word-add-in-r1 task 023 - FR-11 version save: "write" on the existing sprk_document
            .Accepts<SaveRequest>("application/json")
            .Produces<SaveResponse>(StatusCodes.Status202Accepted)
            .Produces<SaveResponse>(StatusCodes.Status200OK) // For duplicate detection
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict) // For idempotency conflicts
            .ProducesProblem(StatusCodes.Status423Locked) // FR-11 version save: target item locked (OFFICE_019)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Save endpoint handler.
    /// Validates the request, creates a ProcessingJob, queues work for background processing,
    /// and returns 202 Accepted with job tracking information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Return 202 Accepted with jobId within 3 seconds (heavy processing is async)
    /// - Validate that association target is provided (OFFICE_003 if missing)
    /// - Validate that association target exists (OFFICE_006/OFFICE_007 if invalid/not found)
    /// - Support idempotency (contract below)
    /// - Return 200 OK with duplicate=true if idempotent request already processed
    /// </para>
    /// <para>
    /// <b>Idempotency contract (spaarkeai-word-add-in-r1 task 039, finding 1).</b> ONE source decides whether a
    /// save runs: the request BODY's <c>idempotencyKey</c> when present, else the server's own key
    /// (<c>OfficeService.GenerateIdempotencyKey</c>, content-aware for every Document save). A ProcessingJob with
    /// that key that did not fail makes the request a duplicate (200, <c>duplicate: true</c>); a Failed or Cancelled
    /// one does not (finding 2). The <c>X-Idempotency-Key</c> header is NOT that key — it only names
    /// <c>IdempotencyFilter</c>'s 24-hour response-replay cache, and for a Document save the cache entry is bound
    /// to that same authoritative key (<see cref="DocumentSaveIdempotencyBinding"/>), so a reused header replays only
    /// a request the key would also de-duplicate. Email and Attachment keep the header-keyed replay unchanged: their
    /// header names the same immutable message/attachment the server key does.
    /// </para>
    /// <para>
    /// <b>Version saves (task 047).</b> A version key names a document and its content, so it cannot tell a retry from
    /// a later save of the same content (B, then A, then B again). A version save is therefore never replayed from the
    /// response cache (<see cref="SaveResponseMayBeReplayed"/>; the in-flight lock still applies). A Completed job under
    /// its key is its duplicate only while the document still holds exactly its content
    /// (<c>OfficeService.IsStillTheSameOperationAsync</c>).
    /// </para>
    /// </remarks>
    /// <param name="request">The save request with content metadata.</param>
    /// <param name="officeService">Office service for save operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims and headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Save response with job tracking information or duplicate result.</returns>
    private static async Task<IResult> SaveAsync(
        SaveRequest request,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // This value is stamped as the job's CreatedBy (OfficeService.SaveAsync) and is later
        // compared against JobOwnershipFilter's userId, which comes from
        // OfficeAuthFilter.ExtractUserId — and that resolves 'oid' FIRST.
        // Reading NameIdentifier ('sub') first here stamped a DIFFERENT claim than the filter
        // compares, so every subsequent job poll returned 403 OFFICE_009
        // ("You do not have access to this job") for every user. Both sides must resolve
        // identity identically; this now matches the sibling handlers at GetJobStatusAsync
        // and StreamJobAsync, which already read UserIdKey first.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Task 039 (finding 1): a dead read of X-Idempotency-Key used to sit here — assigned, never used — which
        // made the header look like an input to the save's de-duplication. It is not, by decision: see the remarks
        // above. The header is consumed only by IdempotencyFilter's response cache.

        // Diagnostic logging for debugging 400 errors
        logger.LogInformation(
            "Save request received: ContentType={ContentType}, UserId={UserId}, CorrelationId={CorrelationId}, " +
            "HasEmail={HasEmail}, HasAttachment={HasAttachment}, HasDocument={HasDocument}, HasTargetEntity={HasTargetEntity}",
            request.ContentType,
            userId,
            traceId,
            request.Email != null,
            request.Attachment != null,
            request.Document != null,
            request.TargetEntity != null);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Save requested without valid user identity");
            return ProblemDetailsHelper.OfficeAccessDenied(traceId);
        }

        // Validate mandatory association (spec constraint: no "Document Only" saves)
        var validationError = ValidateSaveRequest(request, traceId, logger);
        if (validationError is not null)
        {
            return validationError;
        }

        try
        {
            // Call service to process save request
            var response = await officeService.SaveAsync(request, userId, context, cancellationToken);

            if (response.Success)
            {
                // Check if this was a duplicate detection
                if (response.Duplicate)
                {
                    logger.LogInformation(
                        "Duplicate save detected for {ContentType} with job {JobId}",
                        request.ContentType,
                        response.JobId);

                    // Return 200 OK for duplicates per spec
                    return TypedResults.Ok(response);
                }

                logger.LogInformation(
                    "Save job created successfully: {JobId} for {ContentType}",
                    response.JobId,
                    request.ContentType);

                // Return 202 Accepted for new saves
                return TypedResults.Accepted(response.StatusUrl, response);
            }
            else
            {
                logger.LogWarning(
                    "Save failed for {ContentType}: {ErrorCode} - {ErrorMessage}",
                    request.ContentType,
                    response.Error?.Code,
                    response.Error?.Message);

                // Map service errors to ProblemDetails
                return MapSaveErrorToProblem(response.Error, traceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error during save for {ContentType} by user {UserId}",
                request.ContentType,
                userId);

            return Results.Problem(
                type: "https://spaarke.com/errors/office/internal_error",
                title: "Internal Server Error",
                detail: "An unexpected error occurred while processing the save request.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Task 039 (findings 1 + 4): the value a Document save's <c>X-Idempotency-Key</c> response-cache entry is bound
    /// to — the save's AUTHORITATIVE idempotency key (<see cref="OfficeService.ResolveIdempotencyKey"/>: the body key,
    /// else the server's content-aware key). <c>null</c> (the default, header-only cache key) for Email and
    /// Attachment.
    /// </summary>
    /// <remarks>
    /// <para>Why bind at all. The Word pane's create-save header names the record and the document URL, not the
    /// content — so the second save of an EDITED document to the same record reused the first save's header and was
    /// answered with its cached 202, never written. Bound to the authoritative key, the response cache can replay only
    /// a request that key would ALSO de-duplicate: the header can split two saves, never merge them.</para>
    /// <para>Why Document only. A Document is editable, so one header can legitimately carry different bytes. An
    /// Outlook header names an immutable message/attachment — the same identity the server key names — so its replay
    /// is left exactly as it was.</para>
    /// <para>Read from the BOUND <see cref="SaveRequest"/> argument: endpoint filters run after parameter binding, so
    /// the raw JSON body has already been consumed by the time <c>IdempotencyFilter</c> runs.</para>
    /// </remarks>
    internal static string? DocumentSaveIdempotencyBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<SaveRequest>().FirstOrDefault() is { ContentType: SaveContentType.Document } request
            ? OfficeService.ResolveIdempotencyKey(request)
            : null;

    /// <summary>
    /// Task 047: may <c>IdempotencyFilter</c> answer this save from its response cache? Not a VERSION save
    /// (<see cref="OfficeService.IsVersionSave"/>). Everything else may, exactly as before.
    /// </summary>
    /// <remarks>
    /// A version save's key names a document and its content. Whether a request under that key repeats a completed save
    /// depends on what the document holds NOW: after B, then A, the content key of B names a save that no longer
    /// describes the document. The cache cannot see that, so it replayed the first B save's 202 for 24 hours. The save
    /// still takes the in-flight lock (a concurrent double submit gets 409). A sequential retry reaches
    /// <c>OfficeService.SaveAsync</c>, which answers it with the first save's job when the document still holds these
    /// bytes (<c>200</c>, <c>duplicate: true</c>).
    /// </remarks>
    internal static bool SaveResponseMayBeReplayed(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<SaveRequest>().FirstOrDefault() is not { } request
        || !OfficeService.IsVersionSave(request);

    /// <summary>
    /// Validates a save request for required fields and constraints.
    /// </summary>
    /// <param name="request">The save request to validate.</param>
    /// <param name="correlationId">Correlation ID for error responses.</param>
    /// <param name="logger">Logger for validation warnings.</param>
    /// <returns>IResult with validation error, or null if valid.</returns>
    private static IResult? ValidateSaveRequest(
        SaveRequest request,
        string correlationId,
        ILogger logger)
    {
        // Association (TargetEntity) is optional - users can save documents without association
        // If provided, validate entity type and ID
        if (request.TargetEntity is not null)
        {
            // Validate association entity type is valid. The client sends the friendly
            // AssociationEntityType name ("Matter"/"Project"/…), and the finalization worker's
            // association switch (UploadFinalizationWorker) matches on the lowercased friendly name.
            // This list previously mixed logical names (sprk_matter/sprk_project/sprk_invoice) with
            // friendly ones (account/contact), so every Matter/Project/Invoice association was
            // rejected with OFFICE_002 while its own error text listed them as valid. Pre-existing on
            // master — surfaced once real entity search (task 026) returned real typed records.
            // ⚠️ `account` and `contact` are accepted here but sprk_document has NO account/contact
            // lookup column (verified against live Dataverse metadata 2026-09-03), so a save filed to
            // one is persisted UNASSOCIATED — the user believes it is filed and it is not. Left
            // accepted rather than silently rejected because that is a user-visible flow change and
            // an owner decision (add the columns, or reject the type). The drop is now logged loudly
            // at both persistence sites. See Spaarke.Dataverse.DocumentAssociationMap.
            //
            // `workassignment` + `event` added 2026-09-03 — both DO have lookup columns.
            var validEntityTypes = new[]
            {
                "matter", "project", "invoice", "workassignment", "event", "account", "contact"
            };
            if (!validEntityTypes.Contains(request.TargetEntity.EntityType.ToLowerInvariant()))
            {
                logger.LogWarning(
                    "Save request has invalid association type {EntityType}, correlation {CorrelationId}",
                    request.TargetEntity.EntityType,
                    correlationId);
                return ProblemDetailsHelper.OfficeInvalidAssociationType(correlationId);
            }

            // Validate association entity ID is not empty
            if (request.TargetEntity.EntityId == Guid.Empty)
            {
                logger.LogWarning(
                    "Save request has empty association ID, correlation {CorrelationId}",
                    correlationId);
                return ProblemDetailsHelper.OfficeInvalidAssociationTarget(
                    request.TargetEntity.EntityType,
                    correlationId);
            }
        }
        else
        {
            logger.LogInformation(
                "Save request without association (document-only), correlation {CorrelationId}",
                correlationId);
        }

        // Validate content type is valid
        if (!Enum.IsDefined(typeof(SaveContentType), request.ContentType))
        {
            logger.LogWarning(
                "Save request has invalid content type {ContentType}, correlation {CorrelationId}",
                request.ContentType,
                correlationId);
            return ProblemDetailsHelper.OfficeInvalidSourceType(correlationId);
        }

        // Validate content-type-specific required fields
        switch (request.ContentType)
        {
            case SaveContentType.Email:
                if (request.Email is null)
                {
                    logger.LogWarning(
                        "Save request for Email missing email metadata, correlation {CorrelationId}",
                        correlationId);
                    return ProblemDetailsHelper.OfficeValidationError(
                        "OFFICE_VALIDATION",
                        "Validation Error",
                        "Email metadata is required when ContentType is Email.",
                        correlationId);
                }
                break;

            case SaveContentType.Attachment:
                if (request.Attachment is null)
                {
                    logger.LogWarning(
                        "Save request for Attachment missing attachment metadata, correlation {CorrelationId}",
                        correlationId);
                    return ProblemDetailsHelper.OfficeValidationError(
                        "OFFICE_VALIDATION",
                        "Validation Error",
                        "Attachment metadata is required when ContentType is Attachment.",
                        correlationId);
                }
                break;

            case SaveContentType.Document:
                if (request.Document is null)
                {
                    logger.LogWarning(
                        "Save request for Document missing document metadata, correlation {CorrelationId}",
                        correlationId);
                    return ProblemDetailsHelper.OfficeValidationError(
                        "OFFICE_VALIDATION",
                        "Validation Error",
                        "Document metadata is required when ContentType is Document.",
                        correlationId);
                }
                break;
        }

        // All validations passed
        return null;
    }

    /// <summary>
    /// Maps a SaveError to an appropriate ProblemDetails response.
    /// </summary>
    private static IResult MapSaveErrorToProblem(SaveError? error, string correlationId)
    {
        if (error is null)
        {
            return Results.Problem(
                title: "Unknown Error",
                detail: "An unknown error occurred during save.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }

        // Map known error codes to appropriate ProblemDetails responses
        return error.Code switch
        {
            "OFFICE_003" => ProblemDetailsHelper.OfficeAssociationRequired(correlationId),
            "OFFICE_006" => ProblemDetailsHelper.OfficeInvalidAssociationTarget("entity", correlationId),
            "OFFICE_007" => ProblemDetailsHelper.OfficeAssociationTargetNotFound("entity", Guid.Empty, correlationId),
            "OFFICE_009" => ProblemDetailsHelper.OfficeAccessDenied(correlationId),
            "OFFICE_012" => ProblemDetailsHelper.OfficeSpeUploadFailed(error.Message, correlationId),
            // FR-11 version save (word-add-in-r1 task 023) — refusals that wrote nothing; see OfficeErrorCodes.
            OfficeErrorCodes.VersionTargetNotFound => ProblemDetailsHelper.OfficeNotFound(
                error.Code, OfficeErrorCodes.GetTitle(error.Code), error.Message, correlationId),
            OfficeErrorCodes.VersionIntentMismatch => ProblemDetailsHelper.OfficeValidationError(
                error.Code, OfficeErrorCodes.GetTitle(error.Code), error.Message, correlationId),
            OfficeErrorCodes.VersionTargetHasNoFile or OfficeErrorCodes.VersionTargetLocked => Results.Problem(
                type: OfficeErrorCodes.GetTypeUri(error.Code),
                title: OfficeErrorCodes.GetTitle(error.Code),
                detail: error.Message,
                statusCode: OfficeErrorCodes.GetStatusCode(error.Code),
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = error.Code,
                    ["correlationId"] = correlationId,
                    ["retryable"] = error.Retryable
                }),
            // Task 025 (word-add-in-r1) — a same-name collision refused BEFORE any bytes moved. FileName +
            // ExistingDocumentId (Document saves only, when resolvable) let the pane offer the two-option
            // choice ("Keep both" / "Save as new version") without re-parsing the message text.
            OfficeErrorCodes.NameCollision => Results.Problem(
                type: OfficeErrorCodes.GetTypeUri(error.Code),
                title: OfficeErrorCodes.GetTitle(error.Code),
                detail: error.Message,
                statusCode: OfficeErrorCodes.GetStatusCode(error.Code),
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = error.Code,
                    ["correlationId"] = correlationId,
                    ["retryable"] = error.Retryable,
                    ["fileName"] = error.FileName,
                    ["existingDocumentId"] = error.ExistingDocumentId
                }),
            _ => Results.Problem(
                title: "Save Failed",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = error.Code,
                    ["correlationId"] = correlationId,
                    ["retryable"] = error.Retryable
                })
        };
    }

    #endregion

    #region Job Status Endpoints

    /// <summary>
    /// Maps job status endpoints for processing job tracking.
    /// Applies OfficeAuthFilter for authentication, JobOwnershipFilter for ownership verification,
    /// and OfficeRateLimitFilter for rate limiting (60 requests/minute/user per spec.md).
    /// </summary>
    private static void MapJobEndpoints(RouteGroupBuilder group)
    {
        var jobs = group.MapGroup("/jobs");

        // GET /office/jobs/{id} - Get job status (polling)
        // Authorization: OfficeAuthFilter validates user authentication,
        //                JobOwnershipFilter validates user owns the job
        // Rate Limit: 60 requests/minute/user (per spec.md)
        jobs.MapGet("/{jobId:guid}", GetJobStatusAsync)
            .WithName("GetOfficeJobStatus")
            .WithDescription("Get the status of a processing job for polling-based updates")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Jobs)
            .AddOfficeAuthFilter()   // Task 073 - baseline authentication (must precede JobOwnershipFilter)
            .AddJobOwnershipFilter() // Task 073 - job-scoped: caller must own the job (403 otherwise)
            .Produces<JobStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /office/jobs/{id}/stream - SSE stream for real-time updates
        // Authorization: OfficeAuthFilter validates user authentication,
        //                JobOwnershipFilter validates user owns the job
        // Rate Limit: 60 requests/minute/user (per spec.md)
        // Supports reconnection via Last-Event-ID header
        jobs.MapGet("/{jobId:guid}/stream", GetJobStatusStreamAsync)
            .WithName("GetOfficeJobStatusStream")
            .WithDescription("Server-Sent Events (SSE) stream for real-time job status updates. " +
                "Supports reconnection via Last-Event-ID header. " +
                "Events: connected, progress, stage-update, job-complete, job-failed, heartbeat, error.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Jobs)
            .AddOfficeAuthFilter()   // Task 073 - baseline authentication (must precede JobOwnershipFilter)
            .AddJobOwnershipFilter() // Task 073 - job-scoped: caller must own the job (403 otherwise)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Get job status endpoint handler.
    /// Returns the current status of a processing job including stage progress.
    /// </summary>
    /// <remarks>
    /// Authorization is handled by OfficeAuthFilter and JobOwnershipFilter.
    /// When this handler is called, the user has already been verified to own the job.
    /// </remarks>
    /// <param name="jobId">The job ID (validated by JobOwnershipFilter).</param>
    /// <param name="officeService">Office service for job operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job status response or error.</returns>
    private static async Task<IResult> GetJobStatusAsync(
        Guid jobId,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Task 073 - OfficeAuthFilter has already set the userId in
        // HttpContext.Items[OfficeAuthFilter.UserIdKey]; direct claim extraction is
        // retained as a defensive fallback so the handler is safe if reused elsewhere.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        logger.LogInformation(
            "Job status requested for {JobId} by user {UserId}",
            jobId,
            userId);

        // Note: JobOwnershipFilter has already verified ownership and job existence
        // If we reach this point, the job exists and user has access
        var jobStatus = await officeService.GetJobStatusAsync(jobId, userId, cancellationToken);

        if (jobStatus is null)
        {
            // This shouldn't happen if filters worked correctly, but handle defensively
            logger.LogWarning("Job {JobId} not found (unexpected - filters should have caught this)", jobId);
            return Results.Problem(
                title: "Job not found",
                detail: $"No processing job found with ID '{jobId}'",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_008",
                    ["jobId"] = jobId
                });
        }

        return TypedResults.Ok(jobStatus);
    }

    /// <summary>
    /// SSE streaming endpoint for real-time job status updates.
    /// Implements Server-Sent Events protocol per W3C specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Use text/event-stream content type
    /// - Send initial connection and status events
    /// - Send heartbeat events every 15 seconds to keep connection alive
    /// - Support reconnection via Last-Event-ID header
    /// - Send terminal event (job-complete or job-failed) and close connection
    /// - Handle client disconnection gracefully
    /// </para>
    /// <para>
    /// SSE Event Format (per W3C):
    /// <code>
    /// event: {event-type}
    /// id: {job-id}:{sequence}
    /// data: {json-payload}
    ///
    /// </code>
    /// </para>
    /// <para>
    /// Event types:
    /// - connected: Initial connection established
    /// - progress: Progress percentage updated
    /// - stage-update: Job phase changed
    /// - job-complete: Job finished successfully
    /// - job-failed: Job encountered an error
    /// - heartbeat: Keep-alive signal (every 15 seconds)
    /// - error: Terminal error event per ADR-019
    /// </para>
    /// </remarks>
    /// <param name="jobId">The job ID to stream updates for.</param>
    /// <param name="officeService">Office service for job operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims and headers.</param>
    /// <param name="cancellationToken">Cancellation token triggered by client disconnect.</param>
    /// <returns>SSE stream of job status updates.</returns>
    private static async Task GetJobStatusStreamAsync(
        Guid jobId,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;

        // Task 073 - OfficeAuthFilter has already set the userId in
        // HttpContext.Items[OfficeAuthFilter.UserIdKey]; direct claim extraction is
        // retained as a defensive fallback so the handler is safe if reused elsewhere.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Get Last-Event-ID header for reconnection support
        var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();

        logger.LogInformation(
            "SSE stream requested for job {JobId} by user {UserId}, LastEventId={LastEventId}, CorrelationId={CorrelationId}",
            jobId,
            userId,
            lastEventId ?? "none",
            traceId);

        // Validate user identity (defensive - filters should handle this)
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "SSE stream denied: No user identity for job {JobId}, CorrelationId={CorrelationId}",
                jobId,
                traceId);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail = "User identity could not be determined",
                errorCode = "OFFICE_009",
                correlationId = traceId
            }, cancellationToken);
            return;
        }

        // Verify job exists and user has access before starting stream
        // Note: JobOwnershipFilter has already verified this, but verify again for defense in depth
        var initialStatus = await officeService.GetJobStatusAsync(jobId, userId, cancellationToken);
        if (initialStatus is null)
        {
            logger.LogWarning(
                "SSE stream denied: Job {JobId} not found for user {UserId}, CorrelationId={CorrelationId}",
                jobId,
                userId,
                traceId);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                title = "Not Found",
                status = 404,
                detail = $"No processing job found with ID '{jobId}'",
                errorCode = "OFFICE_008",
                jobId = jobId,
                correlationId = traceId
            }, cancellationToken);
            return;
        }

        // Set SSE response headers per W3C specification
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        // Disable response buffering for real-time streaming
        var responseBodyFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        responseBodyFeature?.DisableBuffering();

        try
        {
            // Stream job status updates as SSE events
            await foreach (var eventData in officeService.StreamJobStatusAsync(jobId, lastEventId, cancellationToken))
            {
                // Check if client is still connected before writing
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "SSE stream client disconnected for job {JobId}, CorrelationId={CorrelationId}",
                        jobId,
                        traceId);
                    break;
                }

                // Write SSE event data directly to response stream
                await context.Response.Body.WriteAsync(eventData, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }

            logger.LogInformation(
                "SSE stream completed normally for job {JobId}, CorrelationId={CorrelationId}",
                jobId,
                traceId);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - this is expected behavior
            logger.LogInformation(
                "SSE stream cancelled (client disconnect) for job {JobId}, CorrelationId={CorrelationId}",
                jobId,
                traceId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SSE stream error for job {JobId}, CorrelationId={CorrelationId}",
                jobId,
                traceId);

            // Try to send terminal error event if response hasn't been completed
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
            else
            {
                // Response already started - try to send error event in SSE format
                try
                {
                    var errorEvent = Services.Office.SseHelper.FormatError(
                        "OFFICE_INTERNAL",
                        "An error occurred during streaming",
                        traceId);
                    await context.Response.Body.WriteAsync(errorEvent, CancellationToken.None);
                    await context.Response.Body.FlushAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore errors when writing final error event
                }
            }
        }
    }

    #endregion

    #region Search Endpoints

    /// <summary>
    /// Maps search endpoints for finding entities and documents.
    /// Applies OfficeAuthFilter for authentication and OfficeRateLimitFilter for rate limiting
    /// (30 requests/minute/user per spec.md) on all search endpoints.
    /// </summary>
    private static void MapSearchEndpoints(RouteGroupBuilder group)
    {
        var search = group.MapGroup("/search");

        // GET /office/search/entities - Search for association targets
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 30 requests/minute/user (per spec.md)
        search.MapGet("/entities", SearchEntitiesAsync)
            .WithName("SearchOfficeEntities")
            .WithSummary("Search for association target entities")
            .WithDescription("Searches for Matters, Projects, Invoices, Accounts, and Contacts. Supports typeahead (min 2 chars). Returns results within 500ms.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<EntitySearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /office/search/matter-types - List active Matter Type reference values (task 038)
        // A small, load-once reference list (5 rows in dev) — sibling of /entities, not a filter on it:
        // sprk_mattertype_ref is a reference table (not an association-target entity) and the caller
        // loads it once, so the 2-character-minimum typeahead contract below does not fit.
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 30 requests/minute/user (reuses the Search category — same low-risk read shape)
        search.MapGet("/matter-types", GetMatterTypesAsync)
            .WithName("GetOfficeMatterTypes")
            .WithSummary("List active Matter Type reference values")
            .WithDescription("Returns the active sprk_mattertype_ref rows for the pane's required Matter Type field (spaarkeai-word-add-in-r1 task 038). A small, load-once reference list, not a typeahead search.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<MatterTypeListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /office/search/documents - Search for documents to share
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 30 requests/minute/user (per spec.md)
        search.MapGet("/documents", SearchDocumentsAsync)
            .WithName("SearchOfficeDocuments")
            .WithSummary("Search for documents to share")
            .WithDescription("Search for documents to share from Outlook compose mode. Supports filtering by entity association, content type, date range, and container/folder. Only returns documents the user has permission to share.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<DocumentSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Search entities endpoint handler.
    /// Returns matching entities for association target selection in the save flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Return results within 500ms for typical queries
    /// - Require minimum 2 character query (OFFICE_VALIDATION if shorter)
    /// - Support filtering by entity type via 'type' parameter
    /// - Support pagination via 'skip' and 'top' parameters
    /// - Only return entities the user has access to (Dataverse security roles)
    /// </para>
    /// </remarks>
    /// <param name="q">Search query string (min 2 chars).</param>
    /// <param name="type">Comma-separated entity types to filter (Matter, Project, Invoice, Account, Contact).</param>
    /// <param name="skip">Number of results to skip for pagination (default: 0).</param>
    /// <param name="top">Maximum results to return (default: 20, max: 50).</param>
    /// <param name="officeService">Office service for search operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched entities.</returns>
    private static async Task<IResult> SearchEntitiesAsync(
        string? q,
        string? type,
        int? skip,
        int? top,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Entity search requested without valid user identity");
            return Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["correlationId"] = traceId
                });
        }

        // Validate query parameter
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            logger.LogWarning(
                "Entity search requested with invalid query '{Query}' by user {UserId}",
                q,
                userId);
            return Results.Problem(
                title: "Invalid Query",
                detail: "Search query must be at least 2 characters",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "q"
                });
        }

        // Parse entity types from comma-separated string
        string[]? entityTypes = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            entityTypes = type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Constrain pagination parameters
        var skipValue = Math.Max(skip ?? 0, 0);
        var topValue = Math.Clamp(top ?? 20, 1, 50);

        var request = new EntitySearchRequest
        {
            Query = q,
            EntityTypes = entityTypes,
            Skip = skipValue,
            Top = topValue
        };

        logger.LogInformation(
            "Entity search: Query='{Query}', Types={Types}, Skip={Skip}, Top={Top}, User={UserId}",
            q,
            type ?? "all",
            skipValue,
            topValue,
            userId);

        try
        {
            var response = await officeService.SearchEntitiesAsync(request, userId, cancellationToken);

            // Add correlation ID to response
            response = response with { CorrelationId = traceId };

            logger.LogInformation(
                "Entity search returned {ResultCount} results (total: {TotalCount}) for query '{Query}'",
                response.Results.Count,
                response.TotalCount,
                q);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during entity search for query '{Query}' by user {UserId}",
                q,
                userId);

            return Results.Problem(
                title: "Search Failed",
                detail: "An error occurred while searching for entities",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Matter-types list endpoint handler (task 038). No query, no pagination — the whole active set is
    /// returned in one call for a dropdown loaded once.
    /// </summary>
    /// <param name="officeService">Office service for the matter-type reference read.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<IResult> GetMatterTypesAsync(
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Matter-types list requested without valid user identity");
            return Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["correlationId"] = traceId
                });
        }

        try
        {
            var response = await officeService.GetMatterTypesAsync(cancellationToken);

            logger.LogInformation(
                "Matter-types list returned {ResultCount} results for user {UserId}",
                response.Results.Count,
                userId);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing matter types for user {UserId}", userId);

            return Results.Problem(
                title: "Matter Types Unavailable",
                detail: "An error occurred while listing matter types",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Search documents endpoint handler.
    /// Returns matching documents that the user has permission to share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Return results within 500ms for typical queries
    /// - Require minimum 2 character query (OFFICE_VALIDATION if shorter)
    /// - Support filtering by entity type/ID, content type, date range, container/folder
    /// - Support pagination via 'skip' and 'top' parameters
    /// - Only return documents the user has permission to share
    /// - Include metadata for preview (name, size, modified date, thumbnail URL)
    /// </para>
    /// </remarks>
    /// <param name="q">Search query string (min 2 chars).</param>
    /// <param name="entityType">Filter by associated entity type (Matter, Project, Invoice, Account, Contact).</param>
    /// <param name="entityId">Filter by specific entity association ID.</param>
    /// <param name="containerId">Filter by SPE container ID.</param>
    /// <param name="folderPath">Filter by folder path within the container.</param>
    /// <param name="contentType">Filter by content type/MIME type (partial match supported).</param>
    /// <param name="modifiedAfter">Filter by modification date range start.</param>
    /// <param name="modifiedBefore">Filter by modification date range end.</param>
    /// <param name="skip">Number of results to skip for pagination (default: 0).</param>
    /// <param name="top">Maximum results to return (default: 20, max: 50).</param>
    /// <param name="officeService">Office service for search operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched documents.</returns>
    private static async Task<IResult> SearchDocumentsAsync(
        string? q,
        string? entityType,
        Guid? entityId,
        Guid? containerId,
        string? folderPath,
        string? contentType,
        DateTimeOffset? modifiedAfter,
        DateTimeOffset? modifiedBefore,
        int? skip,
        int? top,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Document search requested without valid user identity");
            return Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["correlationId"] = traceId
                });
        }

        // Validate query parameter
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            logger.LogWarning(
                "Document search requested with invalid query '{Query}' by user {UserId}",
                q,
                userId);
            return Results.Problem(
                title: "Invalid Query",
                detail: "Search query must be at least 2 characters",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "q"
                });
        }

        // Parse entity type if provided
        AssociationEntityType? parsedEntityType = null;
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            if (Enum.TryParse<AssociationEntityType>(entityType, ignoreCase: true, out var parsed))
            {
                parsedEntityType = parsed;
            }
            else
            {
                logger.LogWarning(
                    "Document search requested with invalid entityType '{EntityType}' by user {UserId}",
                    entityType,
                    userId);
                return Results.Problem(
                    title: "Invalid Entity Type",
                    detail: $"Invalid entity type '{entityType}'. Valid values are: Matter, Project, Invoice, Account, Contact.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_VALIDATION",
                        ["correlationId"] = traceId,
                        ["parameter"] = "entityType"
                    });
            }
        }

        // Validate date range
        if (modifiedAfter.HasValue && modifiedBefore.HasValue && modifiedAfter > modifiedBefore)
        {
            logger.LogWarning(
                "Document search has invalid date range: modifiedAfter={After} > modifiedBefore={Before}",
                modifiedAfter,
                modifiedBefore);
            return Results.Problem(
                title: "Invalid Date Range",
                detail: "modifiedAfter cannot be later than modifiedBefore.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "modifiedAfter,modifiedBefore"
                });
        }

        // Constrain pagination parameters
        var skipValue = Math.Max(skip ?? 0, 0);
        var topValue = Math.Clamp(top ?? 20, 1, 50);

        var request = new DocumentSearchRequest
        {
            Query = q,
            EntityType = parsedEntityType,
            EntityId = entityId,
            ContainerId = containerId,
            FolderPath = folderPath,
            ContentType = contentType,
            ModifiedAfter = modifiedAfter,
            ModifiedBefore = modifiedBefore,
            Skip = skipValue,
            Top = topValue
        };

        logger.LogInformation(
            "Document search: Query='{Query}', EntityType={EntityType}, EntityId={EntityId}, ContentType={ContentType}, Skip={Skip}, Top={Top}, User={UserId}",
            q,
            entityType ?? "any",
            entityId?.ToString() ?? "none",
            contentType ?? "any",
            skipValue,
            topValue,
            userId);

        try
        {
            var response = await officeService.SearchDocumentsAsync(request, userId, cancellationToken);

            logger.LogInformation(
                "Document search returned {ResultCount} results (total: {TotalCount}) for query '{Query}'",
                response.Results.Count,
                response.TotalCount,
                q);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during document search for query '{Query}' by user {UserId}",
                q,
                userId);

            return Results.Problem(
                title: "Search Failed",
                detail: "An error occurred while searching for documents",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    #endregion

    #region Quick Create Endpoints

    /// <summary>
    /// Maps Quick Create endpoints for inline entity creation.
    /// Applies OfficeAuthFilter for authentication and OfficeRateLimitFilter for rate limiting
    /// (5 requests/minute/user per spec.md).
    /// </summary>
    private static void MapQuickCreateEndpoints(RouteGroupBuilder group)
    {
        // POST /office/quickcreate/{entityType} - Create a new entity with minimal fields
        // Authorization: OfficeAuthFilter validates user authentication
        // Idempotency: IdempotencyFilter prevents duplicate entity creation
        // Rate Limit: 5 requests/minute/user (per spec.md)
        group.MapPost("/quickcreate/{entityType}", QuickCreateAsync)
            .WithName("OfficeQuickCreate")
            .WithSummary("Create a new entity with minimal fields")
            .WithDescription("Creates a new Matter, Project, or Invoice with minimal required fields, for inline creation from the Office add-in. Matter and Project are created server-side (spaarkeai-word-add-in-r1 FR-13) with the caller as a load-bearing owner (an unresolved caller is refused with 403 and no row is written), business-unit defaults, the Field Mapping Framework applied from the optional record context, and for Matter the matter-type lookup when supplied. This endpoint assigns NEITHER a matter number nor a project number: both will be set by a planned separate server-side numbering component that triggers on create. Because sprk_matternumber and sprk_projectnumber are their entities' primary name attributes, records created here show a blank name in lookups and grids until that component exists. Invoice keeps the minimal name-only path with best-effort ownership.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)
            .AddIdempotencyFilter() // Task 030 - Idempotency support per spec.md
            .AddOfficeAuthFilter()  // Task 073 - baseline Office-caller authentication
            .AddQuickCreateSourceAccessFilter() // word-add-in-r1 task 030 - caller must hold Read on the record context
            .Accepts<QuickCreateRequest>("application/json")
            .Produces<QuickCreateResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // For idempotency conflicts
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // POST /office/todo - Create a first-class sprk_todo from the add-in inline "Create To Do"
        // (email-communication-intelligence-r2 #3). Regarding = the record the email was filed to.
        // Authorization: OfficeAuthFilter validates user authentication.
        // Rate Limit: reuses the QuickCreate category (both are low-frequency inline creates).
        group.MapPost("/todo", CreateTodoAsync)
            .WithName("OfficeCreateTodo")
            .WithSummary("Create a To Do (sprk_todo)")
            .WithDescription("Creates a first-class sprk_todo regarding the filed record, mirroring the CreateTodoWizard field set (name, description, contact assignee, due date, priority/effort). NOT a sprk_event.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)
            .AddIdempotencyFilter()
            .AddOfficeAuthFilter()
            .Accepts<CreateTodoRequest>("application/json")
            .Produces<CreateTodoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Create To Do endpoint handler. Creates a first-class <c>sprk_todo</c> regarding the filed record
    /// (email-communication-intelligence-r2 #3). Owner attribution is best-effort (ADR-024).
    /// </summary>
    private static async Task<IResult> CreateTodoAsync(
        CreateTodoRequest request,
        IOfficeService officeService,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        logger.LogInformation(
            "Create To Do requested by user {UserId}, CorrelationId={CorrelationId}", userId, traceId);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Create To Do requested without valid user identity");
            return ProblemDetailsHelper.OfficeAccessDenied(traceId);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = ["Name is required"] },
                title: "Validation Error",
                detail: "A To Do requires a name.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_007",
                    ["correlationId"] = traceId
                });
        }

        try
        {
            // Attribute ownership to the caller (ADR-024) — best-effort; unresolved → app-owned.
            var ownerResolution = await callerResolver.ResolveAsync(context.User, cancellationToken);
            var ownerSystemUserId = ownerResolution.IsResolved ? ownerResolution.SystemUserId : null;

            var response = await officeService.CreateTodoAsync(request, userId, ownerSystemUserId, cancellationToken);

            if (response is null)
            {
                logger.LogWarning(
                    "Create To Do failed: service returned null, CorrelationId={CorrelationId}", traceId);
                return Results.Problem(
                    type: "https://spaarke.com/errors/office/create-failed",
                    title: "Create Failed",
                    detail: "Failed to create the To Do.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_010",
                        ["correlationId"] = traceId
                    });
            }

            logger.LogInformation(
                "Create To Do succeeded: TodoId={TodoId}, CorrelationId={CorrelationId}", response.TodoId, traceId);

            return Results.Created($"/office/todo/{response.TodoId}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Create To Do by user {UserId}, CorrelationId={CorrelationId}", userId, traceId);
            return Results.Problem(
                type: "https://spaarke.com/errors/office/internal_error",
                title: "Internal Server Error",
                detail: "An unexpected error occurred while creating the To Do.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    #endregion

    #region Document Profile Endpoints

    /// <summary>
    /// FR-08 (spaarkeai-word-add-in-r1 task 022): the "Generate Profile" trigger. Mirrors
    /// <c>ComposeDocumentEndpoints.RefreshProfileAsync</c> exactly — fire-and-forget, 202 Accepted, no
    /// synchronous wait for the profile to complete, unconditional overwrite with no confirmation.
    /// </summary>
    private static void MapDocumentProfileEndpoints(RouteGroupBuilder group)
    {
        var documents = group.MapGroup("/documents");

        // POST /api/office/documents/{documentId}/generate-profile — re-run the Document Profile for an
        // identified document on user request (task 021's Profile section reflects the resulting status
        // transition). ADR-008: resource-level authorization ("write" — the trigger overwrites the
        // record's profile fields) via the canonical DocumentAuthorizationFilter, the SAME filter and
        // operation PUT /api/v1/documents/{id} already uses — reused, not duplicated (CLAUDE.md §11).
        // Filter order matters: the Guid.Empty VALIDATION filter runs BEFORE the AUTHORIZATION filter so
        // an obviously-invalid id 400s without ever probing the access data source for a record that
        // cannot exist — validation and authorization stay two separate decisions, neither one inline
        // in the handler.
        documents.MapPost("/{documentId:guid}/generate-profile", GenerateProfileAsync)
            .WithName("OfficeGenerateDocumentProfile")
            .WithSummary("Re-run the Document Profile for an identified document (FR-08)")
            .WithDescription("Fire-and-forget best-effort re-dispatch of the Document Profile for the given sprk_document, mirroring Compose's shipped refresh-profile semantics: 202 Accepted, unconditional overwrite of any existing profile, no confirmation prompt.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate) // low-frequency inline action — same category as /todo
            .AddOfficeAuthFilter()
            .AddEndpointFilter(async (context, next) =>
            {
                // Guid.Empty is the one malformed shape that still matches the {documentId:guid} route
                // constraint (a syntactically valid GUID, but never a real record) — mirrors
                // ComposeDocumentEndpoints.RefreshProfileAsync's own Guid.Empty check. A non-GUID route
                // segment never reaches this filter at all; the :guid constraint 404s it at the routing
                // layer, the same shape Compose's own refresh-profile route accepts.
                var raw = context.HttpContext.Request.RouteValues["documentId"] as string;
                if (!Guid.TryParse(raw, out var routeDocumentId) || routeDocumentId == Guid.Empty)
                {
                    return ProblemDetailsHelper.OfficeValidationError(
                        "OFFICE_PROFILE_001",
                        "Bad Request",
                        "documentId is required.",
                        context.HttpContext.TraceIdentifier);
                }

                return await next(context);
            })
            .AddDocumentAuthorizationFilter("write")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            // 503: the compound AI gate is off (IDocumentProfileAi unavailable) — coordinator-review fix,
            // see GenerateProfileDispatchOutcome.FacadeUnavailable.
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Generate Profile endpoint handler. See <see cref="IOfficeService.GenerateProfileAsync"/> for the
    /// dispatch contract. By the time this handler runs, the endpoint filter chain has already validated
    /// <paramref name="documentId"/> is non-empty and <see cref="Api.Filters.DocumentAuthorizationFilter"/>
    /// has already authorized the caller for <c>write</c> on it (ADR-008 — no inline authorization here).
    /// </summary>
    /// <remarks>
    /// Coordinator-review fix: this handler used to fire-and-forget
    /// <c>officeService.GenerateProfileAsync</c> WITHOUT awaiting or inspecting its result, so it
    /// returned 202 unconditionally — including when the AI profile facade was unavailable (compound AI
    /// gate off) or no usable bearer token reached the dispatcher. That let an unconditionally-mapped
    /// endpoint claim success for a feature-gated dependency that would never run the job (root
    /// CLAUDE.md §10 asymmetric-registration rule / §F.1 / the ADR-032 Null-Object kill-switch
    /// principle). The fix AWAITS the dispatch DECISION (fast — it does not wait for the background
    /// profile itself, only for <c>OfficeProfileDispatcher.Dispatch</c>'s synchronous branch) and
    /// switches on the outcome, so only a genuine dispatch produces 202.
    /// </remarks>
    private static async Task<IResult> GenerateProfileAsync(
        Guid documentId,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context)
    {
        var traceId = context.TraceIdentifier;

        // Awaits only the DISPATCH DECISION, not the background profile — see the XML remarks above and
        // OfficeProfileDispatcher.Dispatch, whose synchronous branch (facade-availability check, bearer
        // check, Task.Run scheduling) completes immediately; the profile itself continues detached.
        var outcome = await officeService.GenerateProfileAsync(documentId, context, context.RequestAborted)
            .ConfigureAwait(false);

        switch (outcome)
        {
            case GenerateProfileDispatchOutcome.Dispatched:
                logger.LogInformation(
                    "Office Generate Profile: document {DocumentId} requested by user, dispatched (best-effort) TraceId={TraceId}",
                    documentId, traceId);
                return Results.Accepted(value: new { documentId, correlationId = traceId });

            case GenerateProfileDispatchOutcome.FacadeUnavailable:
                logger.LogWarning(
                    "Office Generate Profile: document {DocumentId} — AI profile facade unavailable, refusing to claim success. TraceId={TraceId}",
                    documentId, traceId);
                return Results.Problem(
                    type: "https://spaarke.com/errors/office/office_profile_002",
                    title: "Service Unavailable",
                    detail: "Document profiling is currently unavailable. Try again later.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_PROFILE_002",
                        ["correlationId"] = traceId,
                    });

            case GenerateProfileDispatchOutcome.NoBearer:
                // Defensive-only branch — unreachable via this route's filter chain (see the XML doc on
                // GenerateProfileDispatchOutcome.NoBearer and OfficeProfileDispatcher.Dispatch for the
                // proof). Mapped honestly rather than assumed away.
                logger.LogWarning(
                    "Office Generate Profile: document {DocumentId} — no usable bearer token reached the dispatcher. TraceId={TraceId}",
                    documentId, traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "A caller bearer token is required to generate a document profile.",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_PROFILE_003",
                        ["correlationId"] = traceId,
                    });

            default:
                // Exhaustive-switch safety net — new enum members must be handled explicitly, not fall
                // through to an implicit 202.
                logger.LogError(
                    "Office Generate Profile: document {DocumentId} — unrecognized dispatch outcome {Outcome}. TraceId={TraceId}",
                    documentId, outcome, traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while starting document profiling.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_PROFILE_INTERNAL",
                        ["correlationId"] = traceId,
                    });
        }
    }

    #endregion

    #region Quick Create Endpoints — handlers

    /// <summary>
    /// Quick Create endpoint handler.
    /// Creates a new entity with minimal required fields for inline creation from the Office add-in.
    /// </summary>
    /// <remarks>
    /// R3 task 081 (2026-06-22): On successful Matter creation, fires a
    /// <see cref="MembershipChangedEvent"/> via
    /// <see cref="IMembershipEventPublisher"/> for the implicit
    /// <c>ownerid</c> Lookup (matter cluster — only BFF-side mutation site
    /// for sprk_matter per event-source-inventory §3A). Fire-and-forget
    /// per FR-2P2.6 + Q2: publisher never throws; mutation succeeds even
    /// if publish fails (nightly recon job task 085 is the backstop).
    /// </remarks>
    private static async Task<IResult> QuickCreateAsync(
        string entityType,
        QuickCreateRequest request,
        IOfficeService officeService,
        IMembershipEventPublisher membershipEventPublisher,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        logger.LogInformation(
            "Quick create requested for {EntityType} by user {UserId}, CorrelationId={CorrelationId}",
            entityType,
            userId,
            traceId);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Quick create requested without valid user identity");
            return ProblemDetailsHelper.OfficeAccessDenied(traceId);
        }

        // Parse and validate entity type
        if (!QuickCreateFieldRequirements.TryParse(entityType, out var parsedEntityType))
        {
            logger.LogWarning(
                "Quick create requested with invalid entity type '{EntityType}', CorrelationId={CorrelationId}",
                entityType,
                traceId);
            return Results.Problem(
                type: "https://spaarke.com/errors/office/invalid-entity-type",
                title: "Invalid Entity Type",
                detail: $"Entity type '{entityType}' is not valid. Valid types are: matter, project, invoice, account, contact.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_002",
                    ["correlationId"] = traceId,
                    ["validTypes"] = new[] { "matter", "project", "invoice", "account", "contact" }
                });
        }

        // Validate required fields per entity type
        var validationErrors = QuickCreateFieldRequirements.Validate(parsedEntityType, request);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "Quick create validation failed for {EntityType}: {Errors}, CorrelationId={CorrelationId}",
                entityType,
                string.Join(", ", validationErrors.Keys),
                traceId);
            return Results.ValidationProblem(
                validationErrors,
                title: "Validation Error",
                detail: "One or more required fields are missing.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_007",
                    ["correlationId"] = traceId,
                    ["entityType"] = entityType
                });
        }

        try
        {
            // Resolve the caller's systemuserid for ownerid. For MATTER (task 030) and PROJECT (task 031) it is
            // load-bearing: the creation service refuses an unresolved caller (403 owner_unresolved, no row written).
            // For Invoice it stays best-effort: unresolved leaves ownerid to the Dataverse default (app user).
            var ownerResolution = await callerResolver.ResolveAsync(context.User, cancellationToken);
            var ownerSystemUserId = ownerResolution.IsResolved ? ownerResolution.SystemUserId : null;

            // Call service to create entity
            var response = await officeService.QuickCreateAsync(
                parsedEntityType,
                request,
                userId,
                ownerSystemUserId,
                cancellationToken);

            if (response is null)
            {
                logger.LogWarning(
                    "Quick create failed: entity creation returned null for {EntityType}, CorrelationId={CorrelationId}",
                    entityType,
                    traceId);
                return Results.Problem(
                    type: "https://spaarke.com/errors/office/create-failed",
                    title: "Create Failed",
                    detail: $"Failed to create {entityType}. User may not have permission to create this entity type.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_010",
                        ["correlationId"] = traceId,
                        ["entityType"] = entityType
                    });
            }

            logger.LogInformation(
                "Quick create succeeded: EntityType={EntityType}, Id={Id}, Name={Name}, CorrelationId={CorrelationId}",
                response.EntityType,
                response.Id,
                response.Name,
                traceId);

            // R3 task 081 — FR-2P2.6 + Q2 fire-and-forget membership event.
            // Per event-source-inventory.md §3A + §6.3, the QuickCreate
            // matter endpoint is the ONLY BFF-side write path for sprk_matter.
            // The implicit ownerid Lookup is defaulted by Dataverse to the
            // OBO caller; publish an Added event for that implicit mutation
            // so the junction-updater (task 084) + nightly recon (task 085)
            // observe the new association in real time when the topic is
            // provisioned and the publisher flag is on. When disabled
            // (default Membership:EventPublisher:Enabled=false), the
            // NullMembershipEventPublisher peer logs + returns immediately
            // (ADR-032 P2). Publisher contract guarantees no exceptions
            // propagate to this site — but discard the Task explicitly to
            // signal the fire-and-forget semantics + avoid blocking the
            // 201 Created response on Service Bus latency.
            if (parsedEntityType == QuickCreateEntityType.Matter
                && Guid.TryParse(userId, out var callerOid))
            {
                var membershipEvent = new MembershipChangedEvent
                {
                    // PersonId here is the AAD oid (object id) of the OBO
                    // caller — Dataverse exposes this as
                    // `systemuser.azureactivedirectoryobjectid`. Downstream
                    // consumers (task 084 MembershipJunctionUpdater)
                    // resolve oid → systemuserid via Dataverse lookup. The
                    // PersonIdType is User to flag that resolution path.
                    PersonId = callerOid,
                    PersonIdType = PersonIdentityType.User,
                    EntityLogicalName = "sprk_matter",
                    EntityRecordId = response.Id,
                    SourceField = "ownerid",
                    Role = "owner",
                    MutationType = MembershipMutationType.Added,
                    CorrelationId = traceId,
                    OccurredOnUtc = DateTime.UtcNow,
                };

                _ = membershipEventPublisher.PublishAsync(membershipEvent, cancellationToken);
            }

            // Return 201 Created with location header
            return Results.Created(response.Url ?? $"/office/quickcreate/{entityType}/{response.Id}", response);
        }
        catch (SdapProblemException problem)
        {
            // Task 030: a structured creation refusal (owner unresolved → 403, invalid input → 400). No row was
            // written. Rendered in this endpoint's ProblemDetails shape rather than letting the generic catch below
            // turn a deliberate refusal into a 500.
            logger.LogWarning(
                "Quick create refused for {EntityType}: {Code} ({Status}), CorrelationId={CorrelationId}",
                entityType, problem.Code, problem.StatusCode, traceId);

            return Results.Problem(
                type: $"https://spaarke.com/errors/office/{problem.Code}",
                title: problem.Title,
                detail: problem.Detail,
                statusCode: problem.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = problem.Code,
                    ["correlationId"] = traceId,
                    ["entityType"] = entityType
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during quick create for {EntityType} by user {UserId}, CorrelationId={CorrelationId}",
                entityType,
                userId,
                traceId);

            return Results.Problem(
                type: "https://spaarke.com/errors/office/internal_error",
                title: "Internal Server Error",
                detail: "An unexpected error occurred while creating the entity.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    #endregion

    #region Share Endpoints

    /// <summary>
    /// Maps share endpoints for generating shareable document links and attachments.
    /// Applies OfficeAuthFilter for authentication and OfficeRateLimitFilter for rate limiting
    /// (20 requests/minute/user per spec.md).
    /// </summary>
    private static void MapShareEndpoints(RouteGroupBuilder group)
    {
        var share = group.MapGroup("/share");

        // POST /office/share/links - Generate shareable links for documents
        // Authorization: OfficeAuthFilter validates user authentication
        // Idempotency: IdempotencyFilter prevents duplicate link generation
        // Rate Limit: 20 requests/minute/user (per spec.md)
        share.MapPost("/links", CreateShareLinksAsync)
            .WithName("CreateOfficeShareLinks")
            .WithSummary("Create shareable links for documents")
            .WithDescription("Generates shareable URLs for selected documents that resolve through Spaarke access controls. Optionally creates invitations for external recipients.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Share)
            .AddIdempotencyFilter() // Task 030 - Idempotency support per spec.md
            .AddOfficeAuthFilter()  // Task 073 - baseline Office-caller authentication
            .Accepts<ShareLinksRequest>("application/json")
            .Produces<ShareLinksResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // For idempotency conflicts
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // POST /office/share/attach - Package documents for email attachment
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 20 requests/minute/user (per spec.md)
        share.MapPost("/attach", ShareAttachAsync)
            .WithName("OfficeShareAttach")
            .WithSummary("Package documents for email attachment")
            .WithDescription("Retrieves documents and packages them for attachment to Outlook compose emails. Returns download URLs (primary) or base64 content (fallback). Validates user share permission and size limits (25MB/file, 100MB total).")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Share)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Accepts<ShareAttachRequest>("application/json")
            .Produces<ShareAttachResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Create share links endpoint handler.
    /// Generates shareable URLs for documents that the user has permission to share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Accept array of document IDs (max 50 per request)
    /// - Generate shareable links for accessible documents
    /// - Support partial success (errors for inaccessible docs)
    /// - Optionally create external invitations when grantAccess=true
    /// - Support idempotency via IdempotencyKey
    /// </para>
    /// </remarks>
    /// <param name="request">Share links request with document IDs and options.</param>
    /// <param name="officeService">Office service for share operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Share links response with URLs and any invitations created.</returns>
    private static async Task<IResult> CreateShareLinksAsync(
        ShareLinksRequest request,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Share links requested without valid user identity");
            return Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["correlationId"] = traceId
                });
        }

        // Validate request
        if (request.DocumentIds == null || request.DocumentIds.Count == 0)
        {
            logger.LogWarning(
                "Share links requested with no document IDs by user {UserId}",
                userId);
            return Results.Problem(
                title: "Invalid Request",
                detail: "At least one document ID is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "documentIds"
                });
        }

        if (request.DocumentIds.Count > 50)
        {
            logger.LogWarning(
                "Share links requested with too many documents ({Count}) by user {UserId}",
                request.DocumentIds.Count,
                userId);
            return Results.Problem(
                title: "Too Many Documents",
                detail: "Maximum 50 documents per request.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "documentIds",
                    ["maxAllowed"] = 50,
                    ["requested"] = request.DocumentIds.Count
                });
        }

        logger.LogInformation(
            "Share links requested for {DocumentCount} documents by user {UserId}, GrantAccess={GrantAccess}",
            request.DocumentIds.Count,
            userId,
            request.GrantAccess);

        try
        {
            var response = await officeService.CreateShareLinksAsync(request, userId, cancellationToken);

            logger.LogInformation(
                "Share links created: {LinkCount} links, {ErrorCount} errors, {InvitationCount} invitations for user {UserId}",
                response.Links.Count,
                response.Errors?.Count ?? 0,
                response.Invitations?.Count ?? 0,
                userId);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating share links for {DocumentCount} documents by user {UserId}",
                request.DocumentIds.Count,
                userId);

            return Results.Problem(
                title: "Share Links Failed",
                detail: "An error occurred while creating share links.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Share attach endpoint handler.
    /// Packages documents for attachment to Outlook compose emails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per spec.md, this endpoint MUST:
    /// - Accept array of document IDs
    /// - Validate user has share permission for each document
    /// - Enforce size limits (25MB/file, 100MB total per NFR-03)
    /// - Return download URLs (primary) or base64 content (fallback)
    /// - Support partial success (errors for inaccessible/oversized docs)
    /// - URLs contain cryptographic token with 5-minute TTL
    /// </para>
    /// </remarks>
    /// <param name="request">Share attach request with document IDs and delivery mode.</param>
    /// <param name="officeService">Office service for share operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Share attach response with packaged attachments and any errors.</returns>
    private static async Task<IResult> ShareAttachAsync(
        ShareAttachRequest request,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        // Validate user identity
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Share attach requested without valid user identity");
            return Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["correlationId"] = traceId
                });
        }

        // Validate request - documentIds is required with at least 1 item
        if (request.DocumentIds == null || request.DocumentIds.Length == 0)
        {
            logger.LogWarning(
                "Share attach requested with no document IDs by user {UserId}",
                userId);
            return Results.Problem(
                title: "Invalid Request",
                detail: "At least one document ID is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "documentIds"
                });
        }

        // Validate max documents per request (reasonable limit)
        if (request.DocumentIds.Length > 20)
        {
            logger.LogWarning(
                "Share attach requested with too many documents ({Count}) by user {UserId}",
                request.DocumentIds.Length,
                userId);
            return Results.Problem(
                title: "Too Many Documents",
                detail: "Maximum 20 documents per attachment request.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "documentIds",
                    ["maxAllowed"] = 20,
                    ["requested"] = request.DocumentIds.Length
                });
        }

        logger.LogInformation(
            "Share attach requested for {DocumentCount} documents by user {UserId}, DeliveryMode={DeliveryMode}",
            request.DocumentIds.Length,
            userId,
            request.DeliveryMode);

        try
        {
            var response = await officeService.GetAttachmentsAsync(
                request,
                userId,
                traceId,
                cancellationToken);

            logger.LogInformation(
                "Share attach completed: {AttachmentCount} attachments, {ErrorCount} errors, TotalSize={TotalSize} bytes for user {UserId}",
                response.Attachments.Length,
                response.Errors?.Length ?? 0,
                response.TotalSize,
                userId);

            // Check if total size exceeds Outlook limit (warn but still return)
            const long maxTotalAttachmentSizeBytes = 100 * 1024 * 1024; // 100MB
            if (response.TotalSize > maxTotalAttachmentSizeBytes)
            {
                logger.LogWarning(
                    "Total attachment size {TotalSize} exceeds limit {Limit} for user {UserId}",
                    response.TotalSize,
                    maxTotalAttachmentSizeBytes,
                    userId);
            }

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error packaging attachments for {DocumentCount} documents by user {UserId}",
                request.DocumentIds.Length,
                userId);

            return Results.Problem(
                title: "Attachment Packaging Failed",
                detail: "An error occurred while packaging documents for attachment.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_012",
                    ["correlationId"] = traceId
                });
        }
    }

    #endregion

    #region Recent Endpoints

    /// <summary>
    /// Maps recent items endpoints for quick access to recently used entities and documents.
    /// Applies OfficeAuthFilter for authentication and OfficeRateLimitFilter for rate limiting
    /// (30 requests/minute/user per spec.md).
    /// </summary>
    private static void MapRecentEndpoints(RouteGroupBuilder group)
    {
        // GET /office/recent - Get recently used entities and documents
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 30 requests/minute/user (per spec.md)
        group.MapGet("/recent", GetRecentAsync)
            .WithName("GetOfficeRecent")
            .WithDescription("Get recently used association targets and documents for quick selection")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Recent)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<RecentDocumentsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Get recent items endpoint handler.
    /// Returns recently used association targets and documents for the authenticated user.
    /// </summary>
    /// <param name="top">Maximum number of items to return per category (default: 10, max: 50).</param>
    /// <param name="officeService">Office service for recent items operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent items response.</returns>
    private static async Task<Results<Ok<RecentDocumentsResponse>, ProblemHttpResult>> GetRecentAsync(
        int? top,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Resolve identity the SAME way OfficeAuthFilter.ExtractUserId does ('oid' first). Reading
        // NameIdentifier ('sub') first here diverges from every filter that compares against it —
        // the defect that made `SaveAsync` stamp one claim and JobOwnershipFilter check another,
        // 403-ing every job poll. No handler below currently persists this value for later comparison,
        // so none was reachable by that bug; they are aligned anyway so the next one cannot be.
        var userId = context.Items[OfficeAuthFilter.UserIdKey] as string
            ?? CallerResolution.ResolveObjectId(context.User);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Recent items requested without valid user identity");
            return TypedResults.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009"
                });
        }

        // Validate and constrain top parameter
        var limit = Math.Clamp(top ?? 10, 1, 50);

        logger.LogInformation(
            "Recent items requested by user {UserId} with limit {Limit}",
            userId,
            limit);

        var response = await officeService.GetRecentDocumentsAsync(
            userId,
            limit,
            cancellationToken);

        logger.LogInformation(
            "Returning {AssociationCount} recent associations, {DocumentCount} recent documents, {FavoriteCount} favorites for user {UserId}",
            response.RecentAssociations.Count,
            response.RecentDocuments.Count,
            response.Favorites.Count,
            userId);

        return TypedResults.Ok(response);
    }

    #endregion
}
