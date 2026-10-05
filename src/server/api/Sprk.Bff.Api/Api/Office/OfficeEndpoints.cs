using System.IO;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Infrastructure.Auth;
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

        // Search endpoints (entities, matter types)
        MapSearchEndpoints(group);

        // Quick create endpoints — inline "New record" for the add-in "Related to" picker.
        // Implemented for Matter + Project (email-communication-intelligence-r2 Slice 3, #10).
        MapQuickCreateEndpoints(group);

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

                // LENGTH ONLY, NEVER THE BODY (task 120, GitHub #1015). This previously logged the entire
                // raw body at Information and echoed 500 characters back on a parse failure. The body is
                // an Office SaveRequest: it carries email content, attachment payloads and document bytes.
                // The route is .AllowAnonymous() and, although it only registers under IsDevelopment(),
                // development environments hold real customer mail often enough that "it's only dev" is
                // not a property worth relying on. The diagnostic value here is the SHAPE of the failure —
                // which the structural fields below give in full — not its contents.
                logger.LogInformation("DEBUG /office/save-debug: Received request body of {Length} bytes",
                    body.Length);

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
                        bytePositionInLine = ex.BytePositionInLine,
                        bodyLength = body.Length
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
        // Task 055 (#1005): the collision refusal's display fields are gated on the caller's Read access to
        // the colliding document. The gate lives HERE, not in OfficeService — ADR-008 puts resource
        // authorization at the endpoint, OfficeService has no authorization concern among its fifteen
        // dependencies, and every caller-scoped check in this codebase is handler-side
        // (ChatDocumentEndpoints, RecordSearchEndpoints). See notes/055-collision-names-its-target.md §2.
        AuthorizationService authorizationService,
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

                // Task 055 (#1005): withhold the colliding document's identity from a caller who cannot read
                // it. The service resolved it in-process (one lookup, no second round trip); this is the only
                // code that can put it on the wire, so this is where the authorization decision belongs.
                var error = await WithholdCollisionIdentityIfUnauthorizedAsync(
                    response.Error, userId, context, authorizationService, logger, cancellationToken);

                // Map service errors to ProblemDetails
                return MapSaveErrorToProblem(error, traceId);
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
            // Every type here MUST have a real sprk_document lookup column in DocumentAssociationMap,
            // or this endpoint authorizes a save that can only land unassociated — the user believes
            // the file is filed and it is not.
            //
            // 2026-09-04 (unified-access-control-r2): "account" REMOVED — sprk_document has no account
            // lookup in either column family, so every account-filed save was persisted unassociated.
            // "todo" ADDED — sprk_relatedtodo exists and always did; the earlier record calling a
            // to-do "unmappable" came from checking only the bare sprk_{type} family.
            var validEntityTypes = new[]
            {
                "matter", "project", "invoice", "workassignment", "event", "todo", "contact"
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
    /// Task 055 (#1005 / ISS-006): strips a name-collision refusal's <c>ExistingDocumentName</c> and
    /// <c>ExistingDocumentId</c> unless the caller holds <see cref="AccessRights.Read"/> on that document.
    /// Task 088 (UAT-5): <c>CanSaveAsVersion</c> is forced to <c>false</c> in the same breath, so a caller who
    /// cannot read the other document receives exactly the payload it received before task 088.
    /// Every other error passes through untouched.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the id goes too.</b> It is already returned today with no authorization of any kind —
    /// <c>FindCollisionTargetByLocationAsync</c> queries by drive + file name through the app-only generic
    /// seam. Withholding the name while still handing back the id would close the new disclosure and leave
    /// the pre-existing one. It also costs the caller nothing: without <c>Write</c> they would be refused the
    /// version retry by <c>OfficeVersionSaveAuthorizationFilter</c> anyway, and the pane's "Keep both only"
    /// state is already shipped and tested.</para>
    /// <para><b>Fail closed.</b> An access check that throws withholds rather than reveals —
    /// <c>AuthorizationService</c> is itself fail-closed (it denies outright when the caller's bearer token
    /// is absent rather than degrading to app-only), and this mirrors that posture instead of assuming the
    /// happy path.</para>
    /// </remarks>
    private static async Task<SaveError?> WithholdCollisionIdentityIfUnauthorizedAsync(
        SaveError? error,
        string userId,
        HttpContext context,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (error is null
            || error.Code != OfficeErrorCodes.NameCollision
            || error.ExistingDocumentId is not { } collidingDocumentId)
        {
            return error;
        }

        AccessRights rights;
        try
        {
            var snapshot = await authorizationService.GetCallerAccessAsync(
                userId,
                collidingDocumentId.ToString("D"),
                TokenHelper.ExtractBearerTokenOrNull(context),
                cancellationToken);
            rights = snapshot.AccessRights;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Could not evaluate the caller's access to collision target {DocumentId}; withholding its "
                + "identity from the refusal (fail closed).",
                collidingDocumentId);
            rights = AccessRights.None;
        }

        if (rights.HasFlag(AccessRights.Read))
        {
            return error;
        }

        logger.LogInformation(
            "Collision refusal for {FileName}: the caller holds no Read on the owning document, so its name "
            + "and id are withheld. The pane offers \"Keep both\" only.",
            error.FileName);

        return error with { ExistingDocumentId = null, ExistingDocumentName = null, CanSaveAsVersion = false };
    }

    /// <summary>
    /// The ProblemDetails extensions of an <c>OFFICE_020</c> name-collision refusal (task 025, extended by tasks
    /// 055 and 088).
    /// </summary>
    /// <remarks>
    /// <c>existingDocumentId</c> + <c>existingDocumentName</c> identify the document that already holds the
    /// name — present only when the caller holds Read on it (stripped upstream by
    /// <see cref="WithholdCollisionIdentityIfUnauthorizedAsync"/>); the pane offers "Open" from them.
    /// <c>canSaveAsVersion</c> (task 088) is the ONLY signal for "Save as new version", and is written only
    /// alongside an id: a refusal whose identity was withheld carries exactly the keys it carried before task
    /// 088, so a caller who cannot read the other document cannot tell from the payload's shape anything it
    /// could not tell before.
    /// </remarks>
    private static Dictionary<string, object?> NameCollisionExtensions(SaveError error, string correlationId)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = error.Code,
            ["correlationId"] = correlationId,
            ["retryable"] = error.Retryable,
            ["fileName"] = error.FileName,
            ["existingDocumentId"] = error.ExistingDocumentId,
            // Task 055 (#1005): names the document. Null whenever the id is also null.
            ["existingDocumentName"] = error.ExistingDocumentName
        };

        if (error.ExistingDocumentId is not null)
        {
            extensions["canSaveAsVersion"] = error.CanSaveAsVersion;
        }

        return extensions;
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
            // FR-02 (word-add-in-r1 task 014) — the uploaded bytes claim to be an Office package but cannot be
            // opened as one. Refused before any SPE write, so nothing partial was stored. Same validation-error
            // shape as OFFICE_018 above; it is the request's content that is wrong, not the server's state.
            OfficeErrorCodes.CorruptDocumentPackage => ProblemDetailsHelper.OfficeValidationError(
                error.Code, OfficeErrorCodes.GetTitle(error.Code), error.Message, correlationId),
            // Task 080 — OFFICE_022 (no owner could be determined) joins the two version refusals: a refusal that
            // wrote nothing, rendered with its own status and title from OfficeErrorCodes. Task 060 — OFFICE_014 (the
            // save's job row could not be created) too: a retryable 502, not the default 400, which would blame the request.
            // Task 075 — OFFICE_INTERNAL (an unexpected server exception) too: a 500 with a generic message, not the
            // default 400 below, which blamed the request and carried the exception's message.
            OfficeErrorCodes.VersionTargetHasNoFile
                or OfficeErrorCodes.VersionTargetLocked
                or OfficeErrorCodes.RecordOwnerUnresolved
                or OfficeErrorCodes.DataverseError
                or OfficeErrorCodes.InternalError => Results.Problem(
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
            // ExistingDocumentId (when resolvable and readable) let the pane offer its choices without
            // re-parsing the message text.
            OfficeErrorCodes.NameCollision => Results.Problem(
                type: OfficeErrorCodes.GetTypeUri(error.Code),
                title: OfficeErrorCodes.GetTitle(error.Code),
                detail: error.Message,
                statusCode: OfficeErrorCodes.GetStatusCode(error.Code),
                extensions: NameCollisionExtensions(error, correlationId)),
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
            // GitHub #975 (ADR-019 / RFC 7807), task 052: WriteAsJsonAsync's convenience overload
            // always sets Content-Type to "application/json; charset=utf-8", unconditionally
            // overwriting the "application/problem+json" set immediately above — it never consults
            // the response's existing header. This return happens before ANY SSE framing begins
            // (context.Response.ContentType is not set to "text/event-stream" until further below),
            // so it is a plain error response, not a frame inside a started stream. Passing
            // contentType explicitly (the framework's own 4-arg overload, same fix task 050 applied
            // to the global exception handler) fixes the header without changing the serialized body.
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                    title = "Unauthorized",
                    status = 401,
                    detail = "User identity could not be determined",
                    errorCode = "OFFICE_009",
                    correlationId = traceId
                },
                options: (JsonSerializerOptions?)null,
                contentType: "application/problem+json",
                cancellationToken);
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
            // GitHub #975 (ADR-019 / RFC 7807), task 052: WriteAsJsonAsync's convenience overload
            // always sets Content-Type to "application/json; charset=utf-8", unconditionally
            // overwriting the "application/problem+json" set immediately above — it never consults
            // the response's existing header. This return happens before ANY SSE framing begins
            // (context.Response.ContentType is not set to "text/event-stream" until further below),
            // so it is a plain error response, not a frame inside a started stream. Passing
            // contentType explicitly (the framework's own 4-arg overload, same fix task 050 applied
            // to the global exception handler) fixes the header without changing the serialized body.
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    title = "Not Found",
                    status = 404,
                    detail = $"No processing job found with ID '{jobId}'",
                    errorCode = "OFFICE_008",
                    jobId = jobId,
                    correlationId = traceId
                },
                options: (JsonSerializerOptions?)null,
                contentType: "application/problem+json",
                cancellationToken);
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
        // Authorization: OfficeAuthFilter validates user authentication; per-RECORD authorization is
        // enforced INSIDE the query by Dataverse row-level security, because the handler resolves the
        // caller's systemuserid and OfficeSearchService issues the search IMPERSONATED as that user
        // (task 062, finding F1). Per ADR-008 a per-resource check belongs in a filter — but a filter
        // runs before the handler and there are no rows yet to authorize, and the subject here is a
        // whole result set rather than one route-addressed resource. The trim therefore lives in the
        // query itself, which is the case ADR-008's own constraint carves out ("where trimming must
        // happen inside the query, document why in the code"). See OfficeSearchService.QuerySearchEntityAsync.
        // Rate Limit: 30 requests/minute/user (per spec.md)
        search.MapGet("/entities", SearchEntitiesAsync)
            .WithName("SearchOfficeEntities")
            .WithSummary("Search for association target entities")
            .WithDescription("Searches for Matters, Projects, Invoices, Accounts, and Contacts the CALLER may read (impersonated Dataverse read). Supports typeahead (min 2 chars). Returns results within 500ms.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<EntitySearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /office/search/{list} - List the active rows of one create-form reference list:
        // matter-types (task 038), practice-areas and project-types (task 100).
        // ONE parameterized route rather than three near-identical ones (CLAUDE.md §11): task 100 generalized the
        // task-038 /matter-types route, whose URL and wire shape are unchanged. {list} is looked up in the CLOSED
        // OfficeSearchService.ReferenceLists table — any other value is a 404, so the route cannot read an arbitrary
        // table. The literal /entities route above takes precedence over this parameter (route precedence).
        // Small, load-once reference lists — siblings of /entities, not filters on it: they are reference tables (not
        // association-target entities) and the caller loads each once, so the 2-character typeahead contract does not fit.
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: 30 requests/minute/user (reuses the Search category — same low-risk read shape)
        search.MapGet("/{list}", GetReferenceListAsync)
            .WithName("GetOfficeReferenceList")
            .WithSummary("List the active rows of a create-form reference list")
            .WithDescription("Returns the active rows of one reference list for the pane's \"+ New\" form: matter-types (sprk_mattertype_ref, task 038), practice-areas (sprk_practicearea_ref) or project-types (sprk_projecttype_ref) (task 100). A small, load-once list ordered by name, not a typeahead search. Any other list name is 404.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter() // Task 073 - baseline Office-caller authentication
            .Produces<ReferenceListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
    /// <para>
    /// The last of those was a comment rather than a behaviour until task 062 (finding F1): the search
    /// ran app-only, so any authenticated caller could enumerate every Matter, Project, Invoice,
    /// Account and Contact in the tenant from a two-character substring. The handler now resolves the
    /// caller's Dataverse <c>systemuserid</c> and the service issues the query IMPERSONATED as that
    /// user, so Dataverse applies row-level security inside the query. A caller who cannot be resolved
    /// to a Dataverse user is refused (403) — there is no app-only fallback.
    /// </para>
    /// </remarks>
    /// <param name="q">Search query string (min 2 chars).</param>
    /// <param name="type">Comma-separated entity types to filter (Matter, Project, Invoice, Account, Contact).</param>
    /// <param name="skip">Number of results to skip for pagination (default: 0).</param>
    /// <param name="top">Maximum results to return (default: 20, max: 50).</param>
    /// <param name="access">
    /// <c>file</c> asks for each result's <c>canFile</c>: whether <c>POST /api/office/save</c> would accept it as
    /// the target (task 084, #1037). Any other value, or none, leaves <c>canFile</c> unset and costs nothing.
    /// </param>
    /// <param name="officeService">Office service for search operations.</param>
    /// <param name="searchService">Evaluates <c>canFile</c> with the save's own rights check (task 084).</param>
    /// <param name="callerResolver">Resolves the caller's Dataverse systemuserid for the impersonated read (task 062).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response with matched entities.</returns>
    private static async Task<IResult> SearchEntitiesAsync(
        string? q,
        string? type,
        int? skip,
        int? top,
        string? access,
        IOfficeService officeService,
        OfficeSearchService searchService,
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

        // Task 062 / finding F1 — the caller identity the search runs AS. Fail-closed: a caller with
        // no Dataverse systemuser cross-reference has no row-level security context, so there is no
        // trimmed answer to give them. Refusing is the only alternative to the app-only enumeration
        // this task closes, and a caller with no Dataverse user has no Dataverse rights to lose.
        var callerResolution = await callerResolver.ResolveAsync(context.User, cancellationToken);
        if (!callerResolution.IsResolved
            || !Guid.TryParse(callerResolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            logger.LogWarning(
                "Entity search refused: caller {UserId} has no resolvable Dataverse systemuserid ({Reason}) — "
                + "refusing rather than serving a security-untrimmed app-only search (fail closed).",
                userId,
                callerResolution.UnresolvedReason ?? "unresolved");

            return Results.Problem(
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so search results cannot be scoped to their access.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_SEARCH_FORBIDDEN",
                    ["correlationId"] = traceId
                });
        }

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
            var response = await officeService.SearchEntitiesAsync(
                request, userId, callerSystemUserId, cancellationToken);

            // Task 084 (#1037): "pickable equals savable". The Save tab's picker asks with access=file, and each
            // row then says whether the save would accept it, decided by the save's own rights check. The To Do
            // assignee search does not ask, and pays nothing.
            if (string.Equals(access, "file", StringComparison.OrdinalIgnoreCase))
            {
                response = await searchService.ApplyFilingAccessAsync(
                    response, TokenHelper.ExtractBearerTokenOrNull(context), cancellationToken);
            }

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
    /// Reference-list endpoint handler (task 038 for matter types; generalized by task 100). No query, no pagination —
    /// the whole active set is returned in one call for a dropdown loaded once.
    /// </summary>
    /// <param name="list">The list name — a key of <see cref="OfficeSearchService.ReferenceLists"/>.</param>
    /// <param name="officeService">Office service for the reference read.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">HTTP context for user claims.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<IResult> GetReferenceListAsync(
        string list,
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
            logger.LogWarning("Reference list requested without valid user identity");
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

        // A CLOSED table: an unknown list name is a 404, never a read of a table the caller named.
        if (!OfficeSearchService.TryGetReferenceList(list, out var referenceList))
        {
            return Results.Problem(
                title: "Not Found",
                detail: "There is no reference list with that name.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId,
                    ["parameter"] = "list"
                });
        }

        try
        {
            var response = await officeService.GetReferenceListAsync(referenceList, cancellationToken);

            logger.LogInformation(
                "Reference list {List} returned {ResultCount} results for user {UserId}",
                list,
                response.Results.Count,
                userId);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing reference list {List} for user {UserId}", list, userId);

            return Results.Problem(
                title: "Reference List Unavailable",
                detail: "An error occurred while listing the reference values",
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
            .WithDescription("Creates a new Matter, Project, or Invoice with minimal required fields, for inline creation from the Office add-in. Matter and Project are created server-side (spaarkeai-word-add-in-r1 FR-13) with a load-bearing owner — the caller's business-unit default owner team (an unresolved caller or team is refused with 403 and no row is written), business-unit defaults, the Field Mapping Framework applied from the optional record context, and for Matter the matter-type lookup when supplied. The matter and project numbers (sprk_matternumber, sprk_projectnumber — each entity's primary name attribute) are assigned by Dataverse's platform autonumber on create (MAT-###### / PRJ-######; interim until a numbering function, task 076); the request never carries one. A create the number's alternate key refuses is retried with the next number; after 3 refusals it is refused with 409 record_number_unavailable and no row is written. A record that comes back without a number is still returned, with a warning. Task 100 adds the create form's fields: practiceAreaId (Matter) and projectTypeId (Project) are set when they resolve and dropped with a warning when they do not (never a rejection, like matterTypeId); assignedToContactId names the Assigned To contact (sprk_assignedtointernal on a Matter or Project, sprk_assignedto1 on an Invoice) and requires the caller to hold Read on that contact — otherwise 403 OFFICE_009 assignee_inaccessible, with one body for an unreadable and a nonexistent contact, and no row written. Without it a Matter or Project is assigned to the maker's linked contact and an Invoice is left unassigned. Invoice is written on the minimal path: name, description and Assigned To. Every record created here is owned by the caller's business-unit default owner team (task 080); when no team resolves the create is refused with 403 OFFICE_022 and no row is written.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)
            .AddIdempotencyFilter() // Task 030 - Idempotency support per spec.md
            .AddOfficeAuthFilter()  // Task 073 - baseline Office-caller authentication
            .AddQuickCreateSourceAccessFilter() // word-add-in-r1 task 030 - caller must hold Read on the record context
            .Accepts<QuickCreateRequest>("application/json")
            .Produces<QuickCreateResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict) // Idempotency conflicts; record_number_unavailable (task 076)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /office/quickcreate/defaults - What the "+ New" form prefills (task 100, owner decision B)
        // The caller's OWN linked contact for Assigned To — the same contact RecordCreationService assigns a Matter or
        // Project when the request names none, so the prefill and the server's default are one answer
        // (RecordCreationService.ResolveDefaultAssigneeAsync). Takes no id: there is no resource to authorize beyond
        // the caller themself (waived Permanent in RouteAuthorizationGuardTests).
        // Authorization: OfficeAuthFilter validates user authentication
        // Rate Limit: Search category (30/minute/user) — a read loaded once per pane, not a create
        group.MapGet("/quickcreate/defaults", GetQuickCreateDefaultsAsync)
            .WithName("OfficeQuickCreateDefaults")
            .WithSummary("Defaults for the quick-create form")
            .WithDescription("Returns what the pane's \"+ New\" form prefills: assignedTo = the caller's own linked contact (task 141's user-contact link, never an email match), or null when the caller has none, cannot be resolved, or it could not be read. For a Matter or Project it is the contact the create assigns when the request names none (spaarkeai-word-add-in-r1 task 100).")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)
            .AddOfficeAuthFilter()
            .Produces<QuickCreateDefaultsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // POST /office/todo - Create a first-class sprk_todo from the add-in inline "Create To Do"
        // (email-communication-intelligence-r2 #3). Regarding = the record the email was filed to.
        // Authorization: OfficeAuthFilter validates user authentication, then TodoSourceAccessFilter
        // (word-add-in-r1 task 064, ADR-008) read-gates ALL FOUR caller-supplied record ids —
        // regardingRecordId, documentId, communicationId, assignedToContactId — before the handler runs.
        // Every one of them is written onto a row the CALLER owns, and the regarding target is read
        // app-only by CoreAncestorResolver to stamp its parent matter/project onto that row, so an
        // ungated id let a caller both attach a To Do to a record they cannot read and harvest that
        // record's core ancestor. The filter's single constant deny body is also what closes the
        // 403-vs-201 record-existence oracle — see TodoSourceAccessFilter's remarks.
        group.MapPost("/todo", CreateTodoAsync)
            .WithName("OfficeCreateTodo")
            .WithSummary("Create a To Do (sprk_todo)")
            .WithDescription("Creates a first-class sprk_todo regarding the filed record, mirroring the CreateTodoWizard field set (name, description, contact assignee, due date, priority/effort). NOT a sprk_event.")
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)
            .AddIdempotencyFilter()
            .AddOfficeAuthFilter()
            .AddTodoSourceAccessFilter() // word-add-in-r1 task 064 - caller must hold Read on every source id
            .Accepts<CreateTodoRequest>("application/json")
            .Produces<CreateTodoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Quick-create defaults handler (task 100): the caller's own linked contact for the form's Assigned To prefill.
    /// A caller who cannot be resolved to a Dataverse user gets <c>assignedTo: null</c> (200), not a refusal — the
    /// form simply has no prefill, and the create itself refuses such a caller with its own message.
    /// </summary>
    private static async Task<IResult> GetQuickCreateDefaultsAsync(
        RecordCreationService recordCreation,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.ResolveAsync(context.User, cancellationToken);
        if (!caller.IsResolved
            || !Guid.TryParse(caller.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            return TypedResults.Ok(new QuickCreateDefaultsResponse());
        }

        var assignedTo = await recordCreation.ResolveDefaultAssigneeAsync(callerSystemUserId, cancellationToken);
        return TypedResults.Ok(new QuickCreateDefaultsResponse { AssignedTo = assignedTo });
    }

    /// <summary>
    /// Create To Do endpoint handler. Creates a first-class <c>sprk_todo</c> regarding the filed record
    /// (email-communication-intelligence-r2 #3). Owned by a business-unit default owner team, record-first; refused
    /// with 403 OFFICE_022 when none resolves (task 080).
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
            // The caller's systemuserid — one input to the owner-team resolution (task 080). The To Do is owned by a
            // business-unit default owner TEAM, never the caller; see OfficeService.ResolveTodoOwnerTeamAsync.
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
        catch (SdapProblemException problem)
        {
            // Task 080: a structured refusal — today only OFFICE_022, no owner team could be resolved. No row was
            // written. Same rendering as the quick-create endpoint, so the generic catch below cannot turn a
            // deliberate refusal into a 500, and it stays distinguishable from OFFICE_010's generic failure.
            logger.LogWarning(
                "Create To Do refused: {Code} ({Status}), CorrelationId={CorrelationId}",
                problem.Code, problem.StatusCode, traceId);

            return Results.Problem(
                type: OfficeErrorCodes.GetTypeUri(problem.Code),
                title: problem.Title,
                detail: problem.Detail,
                statusCode: problem.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = problem.Code,
                    ["correlationId"] = traceId
                });
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
    /// FR-08 (spaarkeai-word-add-in-r1 task 022): the "Generate Profile" trigger. 202 Accepted, no wait for the
    /// profile to complete, unconditional overwrite with no confirmation. Since task 068 (#1086) the 202 means the
    /// request is on the job queue, so it survives a restart.
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
            .WithDescription("Queues a fresh Document Profile for the given sprk_document: 202 Accepted once the request is on the job queue, unconditional overwrite of any existing profile, no confirmation prompt.")
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
            // 503: the compound AI gate is off, so the queued job could never run (OFFICE_PROFILE_002), or the job
            // queue refused the request (OFFICE_PROFILE_004).
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
    /// Only a request that is on the job queue produces 202. With the compound AI gate off the job could never run, so
    /// that is a 503, never a 202 (root CLAUDE.md §10 asymmetric-registration rule / §F.1 / ADR-032); so is a request
    /// the job queue refused. The 202 carries the job's id, and its <c>Location</c> is the document read, where this job
    /// type records its status (<c>sprk_filesummarystatus</c>, ADR-017).
    /// </remarks>
    private static async Task<IResult> GenerateProfileAsync(
        Guid documentId,
        IOfficeService officeService,
        ILogger<Program> logger,
        HttpContext context)
    {
        var traceId = context.TraceIdentifier;

        // Awaits the queue submit, not the profile, which runs on the job queue.
        var result = await officeService.GenerateProfileAsync(documentId, context, context.RequestAborted)
            .ConfigureAwait(false);
        var outcome = result.Outcome;

        switch (outcome)
        {
            case GenerateProfileDispatchOutcome.Dispatched:
                logger.LogInformation(
                    "Office Generate Profile: document {DocumentId} requested by user, queued as job {JobId} TraceId={TraceId}",
                    documentId, result.JobId, traceId);
                return Results.Accepted(
                    $"/api/v1/documents/{documentId}",
                    new { documentId, jobId = result.JobId, correlationId = traceId });

            case GenerateProfileDispatchOutcome.QueueUnavailable:
                return Results.Problem(
                    type: "https://spaarke.com/errors/office/office_profile_004",
                    title: "Service Unavailable",
                    detail: "The profile request could not be queued. Try again in a moment.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_PROFILE_004",
                        ["retryable"] = true,
                        ["correlationId"] = traceId,
                    });

            case GenerateProfileDispatchOutcome.FacadeUnavailable:
                logger.LogWarning(
                    "Office Generate Profile: document {DocumentId} — document profiling unavailable, refusing to claim success. TraceId={TraceId}",
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
    /// Internal (not private) so the test assembly (InternalsVisibleTo) runs the real handler and observes the owner
    /// event it publishes (UAC-r2 task 152 verifier round 1, item 7).
    /// </remarks>
    internal static async Task<IResult> QuickCreateAsync(
        string entityType,
        QuickCreateRequest request,
        IOfficeService officeService,
        IMembershipEventPublisher membershipEventPublisher,
        Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver callerResolver,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
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
            // Resolve the caller's systemuserid. For MATTER (task 030) and PROJECT (task 031) it is load-bearing: the
            // creation service refuses an unresolved caller (403 owner_unresolved, no row written). For every type it
            // decides the owner TEAM (task 080) — the caller's business-unit default owner team — and an unresolvable
            // team is refused with 403 OFFICE_022. No quick-created record is app-owned any more.
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
            // UAC-r2 task 152 (ADR-034 A3): the event describes the row's REAL owner — the business-unit default
            // owner TEAM RecordCreationService wrote (read back here; the service does not return it) — as
            // PersonIdType=Team, PersonId=teamid: the key MembershipReconciliationJob builds for the same row. Before
            // task 152 it carried the caller's AAD oid as a User under a comment claiming the junction updater
            // resolves oid → systemuserid; it never did (it writes PersonId verbatim). When disabled (default
            // Membership:EventPublisher:Enabled=false), the Null peer logs + returns (ADR-032 P2). Discarded Task =
            // fire-and-forget; the 201 never waits on Service Bus.
            if (parsedEntityType == QuickCreateEntityType.Matter)
            {
                _ = MembershipOwnerEvents.PublishOwnerAddedAsync(
                    membershipEventPublisher,
                    genericEntityService,
                    "sprk_matter",
                    response.Id,
                    knownOwner: null,
                    traceId,
                    logger,
                    cancellationToken);
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
}
