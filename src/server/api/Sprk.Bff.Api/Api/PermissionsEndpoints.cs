using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for querying user permissions/capabilities on documents.
/// Used by UI (PCF controls, Power Apps, React) to determine which buttons/actions to show.
/// </summary>
public static class PermissionsEndpoints
{
    /// <summary>
    /// Registers permissions endpoints with the application.
    /// </summary>
    public static void MapPermissionsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Permissions")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization(); // All endpoints require authentication

        // GET /api/documents/{documentId}/permissions
        group.MapGet("{documentId}/permissions", GetDocumentPermissionsAsync)
            .WithName("GetDocumentPermissions")
            .WithSummary("Get user capabilities for a single document")
            .WithDescription("Returns what operations the current user can perform on the specified document")
            .Produces<DocumentCapabilities>(200)
            .Produces(401) // Unauthorized
            .Produces(404); // Document not found

        // POST /api/documents/permissions/batch
        group.MapPost("permissions/batch", GetBatchPermissionsAsync)
            .WithName("GetBatchPermissions")
            .WithSummary("Get user capabilities for multiple documents")
            .WithDescription("Batch endpoint to get permissions for multiple documents in one request (performance optimization for galleries)")
            .Produces<BatchPermissionsResponse>(200)
            .Produces(400) // Bad request
            .Produces(401); // Unauthorized
    }

    /// <summary>
    /// Gets user capabilities for a single document.
    /// </summary>
    /// <param name="documentId">Dataverse document ID (sprk_documentid)</param>
    /// <param name="httpContext">HTTP context to extract user identity</param>
    /// <param name="accessDataSource">Data source for querying Dataverse permissions</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>DocumentCapabilities indicating what user can do</returns>
    private static async Task<IResult> GetDocumentPermissionsAsync(
        string documentId,
        HttpContext httpContext,
        IAccessDataSource accessDataSource,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Extract user ID from claims (Azure AD oid claim)
        var userId = httpContext.User.FindFirst("oid")?.Value
                     ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Cannot determine user ID from claims for permissions check");
            return TypedResults.Unauthorized();
        }

        logger.LogInformation("Retrieving permissions for user {UserId} on document {DocumentId}", userId, documentId);

        try
        {
            // Query Dataverse for user's access rights
            // Note: This endpoint uses service principal auth (no user token OBO)
            var snapshot = await accessDataSource.GetUserAccessAsync(userId, documentId, userAccessToken: null, ct);

            // Convert AccessRights to DocumentCapabilities
            var capabilities = MapToDocumentCapabilities(snapshot);

            logger.LogDebug(
                "Permissions retrieved for document {DocumentId}: AccessRights={AccessRights}, CanPreview={CanPreview}, CanDownload={CanDownload}",
                documentId, snapshot.AccessRights, capabilities.CanPreview, capabilities.CanDownload);

            return TypedResults.Ok(capabilities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving permissions for document {DocumentId}", documentId);

            // Fail-closed: Return no permissions on error
            return TypedResults.Ok(new DocumentCapabilities
            {
                DocumentId = documentId,
                UserId = userId,
                AccessRights = "None (Error)",
                CalculatedAt = DateTimeOffset.UtcNow
                // All boolean capabilities default to false
            });
        }
    }

    /// <summary>
    /// Gets user capabilities for multiple documents in one request.
    /// Performance optimization for galleries/lists that display many documents.
    /// </summary>
    /// <param name="request">Batch request with document IDs</param>
    /// <param name="httpContext">HTTP context to extract user identity</param>
    /// <param name="accessDataSource">Data source for querying Dataverse permissions</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BatchPermissionsResponse with capabilities for all documents</returns>
    private static async Task<IResult> GetBatchPermissionsAsync(
        [FromBody] BatchPermissionsRequest request,
        HttpContext httpContext,
        IAccessDataSource accessDataSource,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate request
        if (request.DocumentIds == null || request.DocumentIds.Count == 0)
        {
            return TypedResults.BadRequest(new { error = "DocumentIds cannot be empty" });
        }

        // Limit batch size to prevent abuse
        const int MaxBatchSize = 100;
        if (request.DocumentIds.Count > MaxBatchSize)
        {
            return TypedResults.BadRequest(new { error = $"Maximum batch size is {MaxBatchSize} documents" });
        }

        // Extract user ID
        var userId = request.UserId
                     ?? httpContext.User.FindFirst("oid")?.Value
                     ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Cannot determine user ID from claims for batch permissions check");
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
            "Retrieving batch permissions for user {UserId} on {DocumentCount} documents",
            userId, request.DocumentIds.Count);

        var permissions = new List<DocumentCapabilities>();
        var errors = new List<PermissionError>();
        var successCount = 0;
        var errorCount = 0;

        // Process each document sequentially to avoid Dataverse throttling
        foreach (var documentId in request.DocumentIds)
        {
            try
            {
                // Note: This endpoint uses service principal auth (no user token OBO)
                var snapshot = await accessDataSource.GetUserAccessAsync(userId, documentId, userAccessToken: null, ct);
                var capabilities = MapToDocumentCapabilities(snapshot);
                permissions.Add(capabilities);
                successCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error retrieving permissions for document {DocumentId} in batch", documentId);

                // Add error to response
                errors.Add(new PermissionError
                {
                    DocumentId = documentId,
                    ErrorCode = "permission_check_failed",
                    Message = ex.Message
                });

                // Add empty capabilities (fail-closed)
                permissions.Add(new DocumentCapabilities
                {
                    DocumentId = documentId,
                    UserId = userId,
                    AccessRights = "None (Error)",
                    CalculatedAt = DateTimeOffset.UtcNow
                });

                errorCount++;
            }
        }

        var response = new BatchPermissionsResponse
        {
            Permissions = permissions,
            Errors = errors,
            TotalProcessed = request.DocumentIds.Count,
            SuccessCount = successCount,
            ErrorCount = errorCount
        };

        logger.LogInformation(
            "Batch permissions retrieved: {SuccessCount} successful, {ErrorCount} errors",
            successCount, errorCount);

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Maps Dataverse AccessSnapshot to DocumentCapabilities DTO.
    /// Uses OperationAccessPolicy to determine which operations user can perform.
    /// </summary>
    private static DocumentCapabilities MapToDocumentCapabilities(AccessSnapshot snapshot)
    {
        var rights = snapshot.AccessRights;

        return new DocumentCapabilities
        {
            DocumentId = snapshot.ResourceId,
            UserId = snapshot.UserId,

            // File content operations
            CanPreview = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.preview"),
            CanDownload = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.content.download"),
            CanUpload = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.content.upload"),
            CanReplace = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.content.replace"),
            CanDelete = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.delete"),

            // Metadata operations
            CanReadMetadata = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.get"),
            CanUpdateMetadata = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.update"),

            // Sharing
            CanShare = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.createlink"),

            // Versioning
            CanViewVersions = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.versions.list"),
            CanRestoreVersion = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.versions.restore"),

            // Advanced operations
            CanMove = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.move"),
            CanCopy = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.copy"),
            CanCheckOut = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.checkout"),
            CanCheckIn = OperationAccessPolicy.HasRequiredRights(rights, "driveitem.checkin"),

            // Raw access rights (for debugging/advanced scenarios)
            AccessRights = GetAccessRightsDescription(rights),
            CalculatedAt = snapshot.CachedAt
        };
    }

    /// <summary>
    /// Converts AccessRights flags to human-readable string.
    /// </summary>
    private static string GetAccessRightsDescription(AccessRights rights)
    {
        if (rights == AccessRights.None)
        {
            return "None";
        }

        var parts = new List<string>();

        if (rights.HasFlag(AccessRights.Read)) parts.Add("Read");
        if (rights.HasFlag(AccessRights.Write)) parts.Add("Write");
        if (rights.HasFlag(AccessRights.Delete)) parts.Add("Delete");
        if (rights.HasFlag(AccessRights.Create)) parts.Add("Create");
        if (rights.HasFlag(AccessRights.Append)) parts.Add("Append");
        if (rights.HasFlag(AccessRights.AppendTo)) parts.Add("AppendTo");
        if (rights.HasFlag(AccessRights.Share)) parts.Add("Share");

        return string.Join(", ", parts);
    }
}
