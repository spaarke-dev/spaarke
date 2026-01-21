using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Office;
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
        var group = app.MapGroup("/office")
            .WithTags("Office")
            .RequireAuthorization();

        // Health check endpoint for Office add-ins
        MapHealthEndpoints(group);

        // Save endpoints (email, attachment, document)
        MapSaveEndpoints(group);

        // Job status endpoints
        MapJobEndpoints(group);

        // Search endpoints (entities, documents)
        MapSearchEndpoints(group);

        // Quick create endpoints
        // TODO: Implement in task 026
        // MapQuickCreateEndpoints(group);

        // Share endpoints (links, attach) - Task 027/028
        MapShareEndpoints(group);

        // Recent locations endpoint
        MapRecentEndpoints(group);

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
            .Produces<OfficeHealthResponse>(StatusCodes.Status200OK);
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
    private static void MapSaveEndpoints(RouteGroupBuilder group)
    {
        // POST /office/save - Submit email, attachment, or document for saving
        // Authorization: OfficeAuthFilter validates user authentication,
        //                EntityAccessFilter validates user has access to target entity
        // Idempotency: IdempotencyFilter prevents duplicate document creation
        // Rate Limit: 10 requests/minute/user (per spec.md)
        group.MapPost("/save", SaveAsync)
            .WithName("OfficeSave")
            .WithDescription("Submit email, attachment, or document for saving to Spaarke DMS")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Save)
            .AddIdempotencyFilter() // Task 030 - Idempotency support per spec.md
            // TODO: Task 033 - .AddOfficeAuthFilter()
            // TODO: Task 033 - .AddEntityAccessFilter()
            .Accepts<SaveRequest>("application/json")
            .Produces<SaveResponse>(StatusCodes.Status202Accepted)
            .Produces<SaveResponse>(StatusCodes.Status200OK) // For duplicate detection
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict) // For idempotency conflicts
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
    /// - Support idempotency via X-Idempotency-Key header
    /// - Return 200 OK with duplicate=true if idempotent request already processed
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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

        // Get idempotency key from header if provided
        var idempotencyKey = context.Request.Headers["X-Idempotency-Key"].FirstOrDefault()
            ?? request.IdempotencyKey;

        logger.LogInformation(
            "Save request received for {ContentType} by user {UserId} with correlation {CorrelationId}",
            request.ContentType,
            userId,
            traceId);

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
            var response = await officeService.SaveAsync(request, userId, cancellationToken);

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
        // Validate association is provided (mandatory per spec - no "Document Only" saves)
        if (request.TargetEntity is null)
        {
            logger.LogWarning(
                "Save request missing required association target, correlation {CorrelationId}",
                correlationId);
            return ProblemDetailsHelper.OfficeAssociationRequired(correlationId);
        }

        // Validate association entity type is valid
        var validEntityTypes = new[] { "account", "contact", "sprk_matter", "sprk_project", "sprk_invoice" };
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
            // TODO: Task 033 - .AddOfficeAuthFilter()
            // TODO: Task 033 - .AddJobOwnershipFilter()
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
            // TODO: Task 033 - .AddOfficeAuthFilter()
            // TODO: Task 033 - .AddJobOwnershipFilter()
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
        // TODO: Task 033 - UserId will be set by OfficeAuthFilter
        // For now, get userId from claims directly
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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

        // TODO: Task 033 - UserId will be set by OfficeAuthFilter
        // For now, get userId from claims directly
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
            // TODO: Task 033 - .AddOfficeAuthFilter()
            .Produces<EntitySearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            // TODO: Task 033 - .AddOfficeAuthFilter()
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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
            .WithDescription("Creates a new Matter, Project, Invoice, Account, or Contact with minimal required fields. Supports inline entity creation from the Office add-in when the user needs a new association target.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)
            .AddIdempotencyFilter() // Task 030 - Idempotency support per spec.md
            // TODO: Task 033 - .AddOfficeAuthFilter()
            .Accepts<QuickCreateRequest>("application/json")
            .Produces<QuickCreateResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // For idempotency conflicts
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Quick Create endpoint handler.
    /// Creates a new entity with minimal required fields for inline creation from the Office add-in.
    /// </summary>
    private static async Task<IResult> QuickCreateAsync(
        string entityType,
        QuickCreateRequest request,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
            // Call service to create entity
            var response = await officeService.QuickCreateAsync(
                parsedEntityType,
                request,
                userId,
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

            // Return 201 Created with location header
            return Results.Created(response.Url ?? $"/office/quickcreate/{entityType}/{response.Id}", response);
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
            // TODO: Task 033 - .AddOfficeAuthFilter()
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
            // TODO: Task 033 - .AddOfficeAuthFilter()
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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
            // TODO: Task 033 - .AddOfficeAuthFilter()
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
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

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
