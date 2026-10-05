using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Dataverse document management endpoints for CRUD operations.
/// Implements Task 1.3 requirements with full validation and error handling.
/// </summary>
public static class DataverseDocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDataverseDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var documentsGroup = app.MapGroup("/api/v1/documents")
            .WithTags("Documents")
            .RequireRateLimiting("dataverse-query");

        // POST /api/v1/documents - Create new document
        // R3 task 082 (2026-06-22): Publishes MembershipChangedEvent for implicit
        // ownerid Lookup per event-source-inventory.md §3B + spec FR-2P2.6.
        // Fire-and-forget per Q2: publisher never throws; mutation succeeds even
        // if publish fails (nightly recon task 085 is the backstop).
        // The handler is CreateDocumentAsync below (extracted so the test assembly runs the real site).
        documentsGroup.MapPost("/", CreateDocumentAsync)
            .RequireAuthorization();

        // GET /api/v1/documents/{id} - Get document by ID
        documentsGroup.MapGet("/{id}", async (
            string id,
            IDocumentDataverseService dataverseService,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
                {
                    return ProblemDetailsHelper.ValidationError("Document ID must be a valid GUID");
                }

                logger.LogDebug("Retrieving document {DocumentId}", id);

                var document = await dataverseService.GetDocumentAsync(id);

                if (document == null)
                {
                    logger.LogWarning("Document not found: {DocumentId}", id);
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "Document Not Found",
                        detail = $"Document with ID {id} was not found",
                        traceId
                    });
                }

                return TypedResults.Ok(new
                {
                    data = document,
                    metadata = new
                    {
                        requestId = traceId,
                        timestamp = DateTime.UtcNow,
                        version = "v1"
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve document {DocumentId}", id);
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while retrieving the document",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // Finding H2 (task 022) — this route returns GraphDriveId and GraphItemId, the exact SPE
        // pointers the destroy and bulk-download paths consume. Ungated, it was the discovery step
        // for the rest of the surface: read the row by GUID, get the pointers, act on them elsewhere.
        .AddDocumentAuthorizationFilter("read")
        .RequireAuthorization();

        // PUT /api/v1/documents/{id} — the document re-file and field update. KEPT at the batch-4 integration although
        // task 166 (sweep finding S-36, owner round 10 item 1) had retired it as caller-less: since task 147 r1 (owner
        // round 28 item 1) the Compose document association re-files through it (SpaarkeAi documentAssociationWrite.ts),
        // so the "no caller" premise of the retirement no longer holds. It is kept under the condition 166 itself set for
        // any body-bound document update: (1) AppendTo on every new parent, asked as the caller (task 146,
        // AuthorizeRefileTargetsAsync below), and (2) the RESOURCE-NAMING fields are refused — GraphDriveId / GraphItemId /
        // ParentGraphItemId / FilePath / HasFile name the drive item GET /{id}/download streams as the application, and
        // only the BFF stamps them (POST /{id}/file; field-level security, scripts/Set-DocumentPointerFieldSecurity.ps1).
        documentsGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateDocumentRequest request,
            IDocumentDataverseService dataverseService,
            [FromServices] Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper restamper,
            Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownershipResolver,
            Spaarke.Core.Auth.AuthorizationService authorization,
            [FromServices] Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe callerAccessProbe,
            [FromServices] Sprk.Bff.Api.Services.Access.SecureChildReconciler children,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            // Task 147 r1c: set when this update re-filed the document (the reparent wrote) — the after-re-file step below.
            Func<Task<bool>>? refiledFromIsolation = null;

            try
            {
                if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
                {
                    return ProblemDetailsHelper.ValidationError("Document ID must be a valid GUID");
                }

                logger.LogInformation("Updating document {DocumentId}", id);
                // Task 166 S-36 (kept route, batch-4 integration): the storage pointer is never caller-chosen.
                var pointerFields = ResourceNamingFieldsIn(request);
                if (pointerFields.Count > 0)
                {
                    logger.LogWarning(
                        "Document update {DocumentId} refused: the body names storage-pointer field(s) {Fields}", id,
                        string.Join(", ", pointerFields));
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Validation Error",
                        detail: "A document's file location is set only by the server. Remove "
                            + string.Join(", ", pointerFields) + " from the request.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["reasonCode"] = PointerFieldRefusedReasonCode,
                            ["fields"] = pointerFields,
                            ["traceId"] = traceId,
                        });
                }


                // Check if document exists
                var existingDocument = await dataverseService.GetDocumentAsync(id);
                if (existingDocument == null)
                {
                    logger.LogWarning("Document not found for update: {DocumentId}", id);
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "Document Not Found",
                        detail = $"Document with ID {id} was not found",
                        traceId
                    });
                }

                // Task 146: an update that FILES the document under a record (a matter, project, parent document, …)
                // is a reparent — its owner is re-derived over every parent it will have (secure-if-any) BEFORE the
                // lookup is written, and reassigned when it moves. A refusal writes nothing (409 + reason code); a
                // Dataverse fault propagates to the 500 below.
                var parentChanges = Sprk.Bff.Api.Services.Dataverse.RecordReparent.ParentChangesOf(request);
                if (parentChanges.Count == 0)
                {
                    await dataverseService.UpdateDocumentAsync(id, request);
                }
                else
                {
                    // r2 (verifier items 7 and 8): filing the document under a record costs AppendTo on THAT record, asked
                    // AS THE CALLER before anything is resolved or written — the Write filter above covers the document
                    // only. Without it a caller could pull their document under a secure record they cannot see (and lose
                    // it to that record's team), and the owner refusal's detail answered questions about such a record.
                    var denial = await AuthorizeRefileTargetsAsync(authorization, context, Guid.Parse(id), parentChanges, logger);
                    if (denial is not null)
                    {
                        return denial;
                    }

                    // Task 147 r1c (owner round 28 item 1 — the Compose document association re-files through this
                    // route): whether the document is isolated BEFORE it moves, so a move OUT of every secure record
                    // also takes the secure record's mirrored shares off it (owner round 22) and releases what is filed
                    // under it, exactly as the other browser re-file routes do. A read fault is carried as "unknown"
                    // (nothing is removed or released; logged).
                    Exception? isolationReadFault = null;
                    var isolatedBefore = false;
                    try
                    {
                        isolatedBefore = await children.IsSecureTeamOwnedAsync("sprk_document", Guid.Parse(id), context.RequestAborted);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        isolationReadFault = ex;
                    }

                    var reparent = await ownershipResolver.ReparentAsync(
                        new Sprk.Bff.Api.Services.Dataverse.RecordReparent
                        {
                            EntityLogicalName = "sprk_document",
                            RecordId = Guid.Parse(id),
                            ParentChanges = parentChanges,
                            CallerObjectId = Guid.TryParse(CallerResolution.ResolveObjectId(context.User), out var callerOid)
                                ? callerOid
                                : null,
                            // Owner round 10 item 7 (task 146 c1): replacing a lookup can move the document OUT of a
                            // secure root — an un-secure. The resolver asks THIS caller's F3 rights (Full Access on the
                            // root, or the document's creator) before anything is written; a refusal is the unsecure
                            // endpoint's 403 ProblemDetails.
                            SecureExitCaller = Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForRequest(callerAccessProbe, context),
                        },
                        token => dataverseService.UpdateDocumentAsync(id, request, token),
                        context.RequestAborted);
                    if (reparent.IsRefused)
                    {
                        return ProblemDetailsHelper.RecordOwnerRefused(reparent, "document", traceId);
                    }

                    refiledFromIsolation = () => isolationReadFault is null
                        ? Task.FromResult(isolatedBefore)
                        : Task.FromException<bool>(isolationReadFault);
                }

                // Task 156 (owner round 4 item 5, option b): a document re-filed to another matter / project / work
                // assignment re-stamps every to-do and analysis filed under it, in this same request. Never thrown: a
                // child that fails is logged and the reconciliation job repairs it; the document's own update stands.
                await restamper.AfterWriteAsync(
                    "sprk_document", Guid.Parse(id),
                    Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper.DocumentColumnsWritten(request),
                    CancellationToken.None);

                // Task 147 r1c (round 36), AFTER the re-stamp (the stamps on the analyses and to-dos under it are lookups the
                // ownership rule reads): the document's mirror — moved under a secure record → shared with its sharees now;
                // moved out of every secure record → the mirror goes — and task 148's pass over everything filed under it.
                if (refiledFromIsolation is not null)
                {
                    await children.AfterRefileAsync("sprk_document", Guid.Parse(id), refiledFromIsolation, CancellationToken.None);
                }

                var updatedDocument = await dataverseService.GetDocumentAsync(id);

                logger.LogInformation("Document updated successfully: {DocumentId}", id);

                return TypedResults.Ok(new
                {
                    data = updatedDocument,
                    metadata = new
                    {
                        requestId = traceId,
                        timestamp = DateTime.UtcNow,
                        version = "v1"
                    }
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid document update request");
                return ProblemDetailsHelper.ValidationError(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update document {DocumentId}", id);
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while updating the document",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // Finding H2 (task 022) — app-only tamper by GUID: any authenticated caller could rewrite any
        // document row's fields.
        .AddDocumentAuthorizationFilter("write")
        .RequireAuthorization();

        // The pointer columns' other doors: the PUT above refuses them, and the MDA form / Xrm.WebApi door is closed by
        // owner round 21 item 1: the client no
        // longer writes them (it calls POST /{id}/file below), field-level security makes them writable by the BFF
        // identity only (scripts/Set-DocumentPointerFieldSecurity.ps1, a main-session live step), and every app-only
        // download verifies the pointer (RecordContainerResolver, interim then strict).

        // POST /api/v1/documents/{id}/file — attach the file a client just uploaded to the document it just created
        // (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i), ADR-002 WP-3). The client creates the row
        // WITHOUT a pointer; the BFF verifies, then stamps sprk_graphdriveid / sprk_graphitemid as the application —
        // the only identity the pointer columns' field-level security lets write them. Write on the row is required
        // as the caller (the route filter); the handler then requires that the row has no file yet (or this same one),
        // that the caller created it, that the file sits in the container DERIVED for the row, and that the caller
        // uploaded it (DocumentContainerRelocator.AttachFileAsync).
        documentsGroup.MapPost("/{id}/file", AttachDocumentFileAsync)
            .WithName("AttachDocumentFile")
            .WithDescription("Attaches the file the caller uploaded to the document the caller created; the BFF verifies "
                + "the file's container and uploader and stamps the document's storage pointer server-side.")
            .Produces<AttachDocumentFileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddDocumentAuthorizationFilter("write")
            .RequireAuthorization();

        // DELETE /api/v1/documents/{id} - Delete document
        documentsGroup.MapDelete("/{id}", async (
            string id,
            IDocumentDataverseService dataverseService,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
                {
                    return ProblemDetailsHelper.ValidationError("Document ID must be a valid GUID");
                }

                logger.LogInformation("Deleting document {DocumentId}", id);

                // Check if document exists
                var existingDocument = await dataverseService.GetDocumentAsync(id);
                if (existingDocument == null)
                {
                    logger.LogWarning("Document not found for deletion: {DocumentId}", id);
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "Document Not Found",
                        detail = $"Document with ID {id} was not found",
                        traceId
                    });
                }

                await dataverseService.DeleteDocumentAsync(id);

                logger.LogInformation("Document deleted successfully: {DocumentId}", id);

                // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event.
                // Per event-source-inventory §3B, document Delete should publish
                // Removed events for every Lookup populated on the deleted row.
                // BUT: DocumentEntity does NOT expose ownerid/systemuserid (the
                // only identity Lookup on sprk_document per inventory) — adding
                // that field requires expanding IDocumentDataverseService, which
                // is out of scope for this task. Per Q2 fire-and-forget + recon
                // backstop (FR-2P2.7 task 085), the nightly reconciliation job
                // scans for orphaned junction rows (rows whose source entity no
                // longer exists) and removes them. This is the load-bearing path
                // per inventory §6.1 + the design contract for Delete cleanup.
                // The 24h max-staleness window applies (recon cadence).
                logger.LogDebug(
                    "Document {DocumentId} deleted — junction row cleanup deferred to nightly recon (FR-2P2.7).",
                    id);

                return TypedResults.NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete document {DocumentId}", id);
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while deleting the document",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // Finding C3 (unified-access-control-r2 task 022) — a second app-only destroy path with no
        // per-document authorization, while its /download sibling below WAS gated by task 002. The
        // asymmetry is the finding: one route on this group checked the caller and the neighbouring
        // destroy route did not.
        .AddDocumentAuthorizationFilter("delete")
        .RequireAuthorization();

        // GET /api/v1/documents/{id}/download - Download document file (app-only auth)
        // Proxies SPE file downloads for files uploaded by background processing.
        // Users can't download these files directly because they lack SPE container permissions.
        documentsGroup.MapGet("/{id}/download", async (
            string id,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            // uac-r2 task 166 r1 (round 21 item 1b): the pointer's container is verified before the app-only download.
            RecordContainerResolver containerResolver,
            DocumentTelemetry documentTelemetry,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct) =>
        {
            var traceId = context.TraceIdentifier;
            // Entra `oid` so audit rows correlate with the Dataverse systemuser (not `sub`).
            var userId = CallerResolution.ResolveObjectId(context.User);

            // Start telemetry tracking (FR-03: audit logging)
            var stopwatch = documentTelemetry.RecordDownloadStart(id, userId);

            try
            {
                // Step 1: Validate document ID format
                if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
                {
                    logger.LogWarning("Invalid document ID format: {DocumentId}", id);
                    documentTelemetry.RecordDownloadFailure(stopwatch, id, userId, "invalid_document_id");
                    return ProblemDetailsHelper.ValidationError("Document ID must be a valid GUID");
                }

                logger.LogInformation("Download requested for document {DocumentId}, TraceId={TraceId}", id, traceId);

                // Step 2: Get document entity from Dataverse (includes SPE pointers)
                var document = await dataverseService.GetDocumentAsync(id, ct);

                if (document == null)
                {
                    logger.LogWarning("Document not found for download: {DocumentId}", id);
                    documentTelemetry.RecordDownloadNotFound(stopwatch, id, userId, "document_not_found");
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "Document Not Found",
                        detail = $"Document with ID {id} was not found",
                        traceId
                    });
                }

                // Step 3: Validate SPE pointers exist (file must be uploaded to SPE)
                if (string.IsNullOrWhiteSpace(document.GraphDriveId))
                {
                    logger.LogWarning("Document {DocumentId} missing GraphDriveId", id);
                    documentTelemetry.RecordDownloadNotFound(stopwatch, id, userId, "missing_drive_id");
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "File Not Available",
                        detail = $"Document {id} does not have an associated file in storage",
                        traceId
                    });
                }

                if (string.IsNullOrWhiteSpace(document.GraphItemId))
                {
                    logger.LogWarning("Document {DocumentId} missing GraphItemId", id);
                    documentTelemetry.RecordDownloadNotFound(stopwatch, id, userId, "missing_item_id");
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "File Not Available",
                        detail = $"Document {id} does not have an associated file in storage",
                        traceId
                    });
                }

                logger.LogDebug(
                    "Downloading file for document {DocumentId}: DriveId={DriveId}, ItemId={ItemId}",
                    id, document.GraphDriveId, document.GraphItemId);

                // Step 3b (uac-r2 task 166 r1, owner round 21 item 1b): the row's pointer is followed AS THE APPLICATION,
                // so it must point into a container this document may use — refused (409) otherwise, before any read.
                await containerResolver.EnsureDocumentPointerContainerAsync(
                    Guid.TryParse(id, out var pointerDocumentId) ? pointerDocumentId : Guid.Empty, document.GraphDriveId,
                    document.GraphItemId, ct);

                // Step 4: Download file stream from SPE using app-only auth
                var fileStream = await GraphCallScope.Run(
                    () => speFileStore.DownloadFileAsync(
                        document.GraphDriveId,
                        document.GraphItemId,
                        ct),
                    "document.download");

                if (fileStream == null)
                {
                    logger.LogWarning("File stream null for document {DocumentId}", id);
                    documentTelemetry.RecordDownloadNotFound(stopwatch, id, userId, "file_stream_null");
                    return TypedResults.NotFound(new
                    {
                        status = 404,
                        title = "File Not Found",
                        detail = $"File content not found in storage for document {id}",
                        traceId
                    });
                }

                // Step 5: Determine content type and filename
                var contentType = document.MimeType ?? "application/octet-stream";
                var fileName = document.FileName ?? $"{id}.bin";

                logger.LogInformation(
                    "Streaming download for document {DocumentId}: FileName={FileName}, ContentType={ContentType}, Size={Size}",
                    id, fileName, contentType, document.FileSize);

                // Record successful download (FR-03: audit logging)
                documentTelemetry.RecordDownloadSuccess(
                    stopwatch,
                    id,
                    userId,
                    fileName,
                    contentType,
                    document.FileSize);

                // Step 6: Return streaming file response with proper headers
                // Using TypedResults.Stream for streaming without full buffering (NFR-06)
                return TypedResults.Stream(
                    fileStream,
                    contentType: contentType,
                    fileDownloadName: fileName,
                    enableRangeProcessing: true); // Support partial downloads for large files
            }
            catch (SdapProblemException ex) when (ex.Code == RecordContainerResolver.DocumentStorageUnverifiedCode)
            {
                documentTelemetry.RecordDownloadFailure(stopwatch, id, userId, "storage_unverified");
                return TypedResults.Problem(
                    statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail,
                    extensions: new Dictionary<string, object?> { ["code"] = ex.Code, ["traceId"] = traceId });
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "Graph API error downloading file for document {DocumentId}", id);
                documentTelemetry.RecordDownloadFailure(stopwatch, id, userId, $"graph_error_{ex.ErrorCode ?? "unknown"}");
                return ex.ToProblemDetails();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to download document {DocumentId}", id);
                documentTelemetry.RecordDownloadFailure(stopwatch, id, userId, "unexpected_error");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while downloading the document",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .WithName("DownloadDocument")
        .WithDescription("Download document file using app-only authentication. " +
            "Proxies SPE file downloads for files uploaded by background processing.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict) // uac-r2 task 166 r1: the file pointer's container is not verified
        .Produces(StatusCodes.Status500InternalServerError)
        .AddDocumentAuthorizationFilter("read")
        .RequireAuthorization();

        // GET /api/v1/documents - List documents (optionally filtered by container)
        documentsGroup.MapGet("/", async (
            string? containerId,
            int? skip,
            int? take,
            IDocumentDataverseService dataverseService,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                // Apply defaults
                var skipValue = skip ?? 0;
                var takeValue = Math.Min(take ?? 50, 100); // Max 100 items per page

                logger.LogDebug("Listing documents - ContainerId: {ContainerId}, Skip: {Skip}, Take: {Take}",
                    containerId, skipValue, takeValue);

                IEnumerable<DocumentEntity> documents;

                if (!string.IsNullOrWhiteSpace(containerId))
                {
                    if (!Guid.TryParse(containerId, out _))
                    {
                        return ProblemDetailsHelper.ValidationError("Container ID must be a valid GUID");
                    }

                    documents = await dataverseService.GetDocumentsByContainerAsync(containerId);
                }
                else
                {
                    // See backlog item SDAP-401 for paging implementation (get all documents with pagination)
                    return ProblemDetailsHelper.ValidationError("ContainerId is required for listing documents");
                }

                var pagedDocuments = documents.Skip(skipValue).Take(takeValue).ToList();
                var totalCount = documents.Count();

                logger.LogDebug("Retrieved {Count} of {TotalCount} documents", pagedDocuments.Count, totalCount);

                return TypedResults.Ok(new
                {
                    data = new
                    {
                        items = pagedDocuments,
                        totalCount,
                        skip = skipValue,
                        take = takeValue,
                        hasNextPage = (skipValue + takeValue) < totalCount,
                        hasPreviousPage = skipValue > 0
                    },
                    metadata = new
                    {
                        requestId = traceId,
                        timestamp = DateTime.UtcNow,
                        version = "v1"
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to list documents");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing documents",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // GATED 2026-10-03 by unified-access-control-r2 task 166 (sweep finding S-66). This is the twin of
        // GET /api/v1/containers/{containerId}/documents below — same app-only GetDocumentsByContainerAsync,
        // same SPE pointers in the result — and it was hidden from task 074's guard by a Permanent
        // "COLLECTION READ … result trimming" waiver, although the caller picks ONE container and no trimming
        // exists. The SAME filter now decides it, reading the container id from the QUERY instead of the route:
        // Read on the container's owning record, as the caller, or the uniform 403. A missing containerId is the
        // filter's 400 (no resolver, no Dataverse call). The handler's Guid.TryParse type bug is deliberately
        // NOT fixed here (task 078 note §4 owns it) — the gate holds whatever that bug's state.
        .AddContainerDocumentAuthorizationFilter(queryParameter: "containerId")
        .RequireAuthorization();

        // GET /api/v1/containers/{containerId}/documents - List documents in a container (alternative endpoint)
        //
        // GATED by unified-access-control-r2 task 078. Until 2026-08-28 this route carried
        // .RequireAuthorization() alone — authentication, not authorization: nothing checked that the
        // caller had any relationship to the container or to the record owning it.
        // AddContainerDocumentAuthorizationFilter resolves the container to its OWNING RECORD (task 075's
        // mapping) and requires the caller's Read on that record, evaluated as the caller. A container with
        // no establishable owner is REFUSED (ADR-003).
        //
        // ⚠️ The gate is deliberately belt-and-braces TODAY, and that is the right order. The data path is
        // separately blocked by a pre-existing type bug: sprk_containerid is NVARCHAR holding an SPE "b!…"
        // id, but the handler below does Guid.TryParse and DataverseServiceClientImpl:874 does
        // Guid.Parse — so a real container id 400s and a GUID-shaped one matches no row. Fixing that type
        // mismatch is one line, and doing it WITHOUT this filter would make the disclosure live. See
        // projects/unified-access-control-r2/notes/task-078-container-document-list-gate.md §4.
        app.MapGet("/api/v1/containers/{containerId}/documents", async (
            string containerId,
            int? skip,
            int? take,
            IDocumentDataverseService dataverseService,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(containerId) || !Guid.TryParse(containerId, out _))
                {
                    return ProblemDetailsHelper.ValidationError("Container ID must be a valid GUID");
                }

                var skipValue = skip ?? 0;
                var takeValue = Math.Min(take ?? 50, 100);

                logger.LogInformation("Listing documents for container {ContainerId}", containerId);

                var documents = await dataverseService.GetDocumentsByContainerAsync(containerId);
                var pagedDocuments = documents.Skip(skipValue).Take(takeValue).ToList();
                var totalCount = documents.Count();

                return TypedResults.Ok(new
                {
                    data = new
                    {
                        containerId,
                        items = pagedDocuments,
                        totalCount,
                        skip = skipValue,
                        take = takeValue,
                        hasNextPage = (skipValue + takeValue) < totalCount,
                        hasPreviousPage = skipValue > 0
                    },
                    metadata = new
                    {
                        requestId = traceId,
                        timestamp = DateTime.UtcNow,
                        version = "v1"
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to list documents for container {ContainerId}", containerId);
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing documents",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .WithTags("Containers")
        .RequireRateLimiting("dataverse-query")
        .AddContainerDocumentAuthorizationFilter()
        .RequireAuthorization();

        return app;
    }

    /// <summary>The right filing a document under a record costs on that record: AppendTo (the key the record-keyed
    /// upload and Office save routes use; the associate-record and event re-file routes that also used it were deleted by
    /// tasks 164 and 159).</summary>
    internal const string RefileTargetOperation = "entity.associate_document";

    /// <summary>
    /// Authorizes a RE-FILE of a document AS THE CALLER on every record the update files it under (task 146 r2, verifier
    /// items 7 and 8): AppendTo (<see cref="RefileTargetOperation"/>) on each new parent, asked of Dataverse through the
    /// caller's own rights before the owner is resolved or anything is written. Write on the document itself is the
    /// route's filter. <c>null</c> when allowed; otherwise a 403 ProblemDetails that names no target and no reason about
    /// it — a caller without AppendTo learns nothing about the record (the owner refusal's detail is reached only by a
    /// caller authorized on every target). Fails closed: an unsupported type, an unanswerable question or a fault denies.
    /// </summary>
    private static async Task<IResult?> AuthorizeRefileTargetsAsync(
        Spaarke.Core.Auth.AuthorizationService authorization,
        HttpContext httpContext,
        Guid documentId,
        IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> parentChanges,
        ILogger logger)
    {
        var userId = CallerResolution.ResolveObjectId(httpContext.User);
        var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var ct = httpContext.RequestAborted;

        foreach (var (column, target) in parentChanges)
        {
            if (target is null)
                continue; // this route's updates only SET lookups (RecordReparent.ParentChangesOf)

            var entitySet = EntityAccessFilter.TryResolveEntitySet(target.LogicalName, out var resolved)
                ? resolved
                : string.Equals(target.LogicalName, "sprk_document", StringComparison.OrdinalIgnoreCase)
                    ? FinanceAuthorizationFilter.DocumentEntitySet
                    : null;

            string? denyReason;
            if (entitySet is null || string.IsNullOrEmpty(userId))
            {
                denyReason = FinanceAuthorizationFilter.NoTargetReasonCode;
            }
            else
            {
                try
                {
                    var snapshot = await authorization.GetCallerRecordAccessAsync(userId, entitySet, target.Id, token, ct);
                    denyReason = Spaarke.Core.Auth.OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, RefileTargetOperation)
                        ? null
                        : FinanceAuthorizationFilter.InsufficientRightsReasonCode;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Document re-file authorization faulted on {EntitySet}({RecordId}); denying",
                        entitySet, target.Id);
                    denyReason = FinanceAuthorizationFilter.SystemFailureReasonCode;
                }
            }

            if (denyReason is not null)
            {
                logger.LogWarning(
                    "Document re-file DENIED: caller may not file document {DocumentId} under {Column} → {Entity}({RecordId}) ({Reason})",
                    documentId, column, target.LogicalName, target.Id, denyReason);
                return ProblemDetailsHelper.Forbidden(
                    denyReason, "You do not have permission to file this document under that record.", httpContext.TraceIdentifier);
            }
        }

        return null;
    }

    /// <summary>
    /// POST /api/v1/documents — creates a document row (app-only, owned by the caller's business-unit default owner
    /// team, task 080) and publishes the owner <see cref="MembershipChangedEvent"/> for it.
    /// </summary>
    /// <remarks>
    /// Internal (not private) so the test assembly (InternalsVisibleTo) runs the real handler and observes the owner
    /// event it publishes (unified-access-control-r2 task 152 verifier round 1, item 7) — the wiring is pinned by
    /// executing this site, not by reading its source.
    /// </remarks>
    internal static async Task<IResult> CreateDocumentAsync(
        [FromBody] CreateDocumentRequest request,
        IDocumentDataverseService dataverseService,
        IMembershipEventPublisher membershipEventPublisher,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownershipResolver,
        Spaarke.Dataverse.IGenericEntityService genericEntityService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = CallerResolution.ResolveObjectId(context.User);

        try
        {
            logger.LogInformation("Creating document {DocumentName} in container {ContainerId}",
                request.Name, request.ContainerId);

            // Task 080 (write-path invariant I-6): this create is APP-ONLY, so without an explicit owner
            // Dataverse makes the BFF application user the owner — in the ROOT business unit, where no child-BU
            // user (the caller included) can read it. The body names no record, so the caller's business-unit
            // default owner team decides. OwningTeamId and Id are [JsonIgnore]d on the request, so the body can
            // no longer set either; the owner is decided here, server-side, or the create is refused.
            //
            // Task 146 c1-r1 (owner round 13 item 9): the caller is also recorded as the person who asked
            // (sprk_createdbyperson) — this create is app-only, so createdby is the application user.
            var owner = await ownershipResolver.ResolveOwnerAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
                {
                    CallerObjectId = Guid.TryParse(userId, out var callerObjectId) ? callerObjectId : null,
                    RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfObjectId(userId),
                },
                ct);
            var owningTeamId = owner.IsOwned ? owner.OwningTeamId : null;

            if (owningTeamId is null)
            {
                logger.LogWarning(
                    "Refusing document create for caller {UserId}: no owner team resolved (task 080)", userId);
                // The same code every Office create uses for this refusal (ADR-019: one condition, one code).
                const string ownerUnresolved = Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.RecordOwnerUnresolved;
                return TypedResults.Problem(
                    statusCode: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetStatusCode(ownerUnresolved),
                    type: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetTypeUri(ownerUnresolved),
                    title: Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.GetTitle(ownerUnresolved),
                    detail: "The document could not be assigned to your business unit's team, so it was not "
                            + "created. Ask an administrator to check your user record's business unit.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = ownerUnresolved,
                        ["traceId"] = traceId
                    });
            }

            request.OwningTeamId = owningTeamId;
            request.CreatedByPersonId = owner.CreatedByPerson;

            var documentId = await dataverseService.CreateDocumentAsync(request);

            var createdDocument = await dataverseService.GetDocumentAsync(documentId);

            logger.LogInformation("Document created successfully with ID: {DocumentId}", documentId);

            // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event, describing the row's REAL owner
            // (UAC-r2 task 152, ADR-034 A3): the create is app-only and the row is owned by the business-unit
            // default owner TEAM set above, so the event is PersonIdType=Team, PersonId=that team — the same key
            // MembershipReconciliationJob builds for this row. (Before task 152 it published the caller's AAD oid
            // as a User owner — false, and in an identity space reconciliation never writes.)
            // When MembershipEventPublisherOptions.Enabled=false (default), the NullMembershipEventPublisher peer
            // logs + returns (ADR-032 P2).
            if (Guid.TryParse(documentId, out var documentGuid))
            {
                _ = MembershipOwnerEvents.PublishOwnerAddedAsync(
                    membershipEventPublisher,
                    genericEntityService,
                    "sprk_document",
                    documentGuid,
                    new Microsoft.Xrm.Sdk.EntityReference("team", owningTeamId.Value),
                    traceId,
                    logger,
                    ct);
            }

            return TypedResults.Created($"/api/v1/documents/{documentId}", new
            {
                data = createdDocument,
                metadata = new
                {
                    requestId = traceId,
                    timestamp = DateTime.UtcNow,
                    version = "v1"
                }
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid document creation request");
            return ProblemDetailsHelper.ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create document");
            return TypedResults.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while creating the document",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    /// <summary>The reason code of every refused file attach (one code; the detail says which check refused).</summary>
    internal const string AttachRefusedCode = "document_file_attach_refused";

    /// <summary>
    /// POST /api/v1/documents/{id}/file — see the route's comment. Internal so the test assembly runs the real handler.
    /// </summary>
    /// <summary>
    /// The reason code of PUT /api/v1/documents/{id}'s refusal of a body that names a storage-pointer field (task 166
    /// S-36's condition for keeping a body-bound document update; batch-4 integration).
    /// </summary>
    internal const string PointerFieldRefusedReasonCode = "sdap.documents.pointer_field_refused";

    /// <summary>
    /// The resource-naming fields a caller set on a document update body: the columns that name the drive item the BFF
    /// follows AS THE APPLICATION (sprk_graphdriveid / sprk_graphitemid / sprk_parentgraphitemid / sprk_filepath) and
    /// sprk_hasfile, which the BFF writes WITH the pointer. Empty when none is set.
    /// </summary>
    internal static IReadOnlyList<string> ResourceNamingFieldsIn(UpdateDocumentRequest request)
    {
        var named = new List<string>();
        if (request.GraphDriveId is not null) named.Add(nameof(UpdateDocumentRequest.GraphDriveId));
        if (request.GraphItemId is not null) named.Add(nameof(UpdateDocumentRequest.GraphItemId));
        if (request.ParentGraphItemId is not null) named.Add(nameof(UpdateDocumentRequest.ParentGraphItemId));
        if (request.FilePath is not null) named.Add(nameof(UpdateDocumentRequest.FilePath));
        if (request.HasFile is not null) named.Add(nameof(UpdateDocumentRequest.HasFile));
        return named;
    }

    internal static async Task<IResult> AttachDocumentFileAsync(
        string id,
        [FromBody] AttachDocumentFileRequest? request,
        [FromServices] Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator relocator,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        if (!Guid.TryParse(id, out var documentId) || documentId == Guid.Empty)
        {
            return ProblemDetailsHelper.ValidationError("Document ID must be a valid GUID");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.DriveId) || string.IsNullOrWhiteSpace(request.ItemId))
        {
            return ProblemDetailsHelper.ValidationError("driveId and itemId (the uploaded file's) are required");
        }

        try
        {
            var result = await relocator.AttachFileAsync(
                documentId, CallerResolution.ResolveObjectId(context.User), request.DriveId, request.ItemId, ct);

            if (result.Outcome == Sprk.Bff.Api.Services.Documents.PointerAttachOutcome.Attached)
            {
                return TypedResults.Ok(new AttachDocumentFileResponse(
                    documentId, result.DriveId!, result.ItemId!, result.AlreadyAttached));
            }

            var status = result.Outcome switch
            {
                Sprk.Bff.Api.Services.Documents.PointerAttachOutcome.InvalidRequest => StatusCodes.Status400BadRequest,
                Sprk.Bff.Api.Services.Documents.PointerAttachOutcome.NotTheCreator => StatusCodes.Status403Forbidden,
                Sprk.Bff.Api.Services.Documents.PointerAttachOutcome.NotTheUploader => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict,
            };

            return TypedResults.Problem(
                statusCode: status,
                title: "File Not Attached",
                detail: result.Detail,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = AttachRefusedCode,
                    ["reasonCode"] = result.Outcome.ToString(),
                    ["traceId"] = traceId,
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed: nothing was attached unless the final write succeeded.
            logger.LogError(ex, "Attaching a file to document {DocumentId} failed", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "The file could not be attached to the document.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }
}

/// <summary>The file a client uploaded (the drive and item ids the upload route returned). Task 166 f1.</summary>
public sealed record AttachDocumentFileRequest(string? DriveId, string? ItemId);

/// <summary>The attached pointer. <paramref name="AlreadyAttached"/>: this same file was attached before (idempotent).</summary>
public sealed record AttachDocumentFileResponse(Guid DocumentId, string DriveId, string ItemId, bool AlreadyAttached);
