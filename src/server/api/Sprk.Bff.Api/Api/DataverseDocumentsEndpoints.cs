using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
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
        documentsGroup.MapPost("/", async (
            [FromBody] CreateDocumentRequest request,
            IDataverseService dataverseService,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                logger.LogInformation("Creating document {DocumentName} in container {ContainerId}",
                    request.Name, request.ContainerId);

                var documentId = await dataverseService.CreateDocumentAsync(request);

                var createdDocument = await dataverseService.GetDocumentAsync(documentId);

                logger.LogInformation("Document created successfully with ID: {DocumentId}", documentId);

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
        })
        .RequireAuthorization();

        // GET /api/v1/documents/{id} - Get document by ID
        documentsGroup.MapGet("/{id}", async (
            string id,
            IDataverseService dataverseService,
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
        .RequireAuthorization();

        // PUT /api/v1/documents/{id} - Update document
        documentsGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateDocumentRequest request,
            IDataverseService dataverseService,
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

                logger.LogInformation("Updating document {DocumentId}", id);

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

                await dataverseService.UpdateDocumentAsync(id, request);

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
        .RequireAuthorization();

        // DELETE /api/v1/documents/{id} - Delete document
        documentsGroup.MapDelete("/{id}", async (
            string id,
            IDataverseService dataverseService,
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
        .RequireAuthorization();

        // GET /api/v1/documents/{id}/download - Download document file (app-only auth)
        // Proxies SPE file downloads for files uploaded by background processing.
        // Users can't download these files directly because they lack SPE container permissions.
        documentsGroup.MapGet("/{id}/download", async (
            string id,
            IDataverseService dataverseService,
            SpeFileStore speFileStore,
            DocumentTelemetry documentTelemetry,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct) =>
        {
            var traceId = context.TraceIdentifier;
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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

                // Step 4: Download file stream from SPE using app-only auth
                var fileStream = await speFileStore.DownloadFileAsync(
                    document.GraphDriveId,
                    document.GraphItemId,
                    ct);

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
            catch (ODataError ex)
            {
                logger.LogError(ex, "Graph API error downloading file for document {DocumentId}", id);
                documentTelemetry.RecordDownloadFailure(stopwatch, id, userId, $"graph_error_{ex.Error?.Code ?? "unknown"}");
                return ProblemDetailsHelper.FromGraphException(ex);
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
        .Produces(StatusCodes.Status500InternalServerError)
        .AddDocumentAuthorizationFilter("read")
        .RequireAuthorization();

        // GET /api/v1/documents - List documents (optionally filtered by container)
        documentsGroup.MapGet("/", async (
            string? containerId,
            int? skip,
            int? take,
            IDataverseService dataverseService,
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
        .RequireAuthorization();

        // GET /api/v1/containers/{containerId}/documents - List documents in a container (alternative endpoint)
        app.MapGet("/api/v1/containers/{containerId}/documents", async (
            string containerId,
            int? skip,
            int? take,
            IDataverseService dataverseService,
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
        .RequireAuthorization();

        return app;
    }
}
