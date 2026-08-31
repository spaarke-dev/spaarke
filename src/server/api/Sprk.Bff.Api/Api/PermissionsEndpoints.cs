using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for querying user permissions/capabilities on documents.
/// Used by UI (PCF controls, Power Apps, React) to determine which buttons/actions to show.
/// </summary>
/// <remarks>
/// <b>Caller-scoped by construction</b> (unified-access-control-r2 task 006, spec FR-05, finding A-4).
/// Both handlers resolve rights through <see cref="AuthorizationService.GetCallerAccessAsync"/>, which
/// is the same snapshot accessor <see cref="AuthorizationService.AuthorizeAsync"/> uses for enforcement.
/// Capabilities therefore cannot drift from what the enforcement path would decide, and a caller with
/// no access to a document receives every capability <c>false</c> rather than the application's own
/// capabilities.
///
/// <para>Before task 006 these handlers called <see cref="IAccessDataSource"/> directly with
/// <c>userAccessToken: null</c>, so the response described what the APPLICATION could do and was
/// returned to anyone who could authenticate.</para>
/// </remarks>
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
            .WithSummary("Get the CALLING user's capabilities for a single document")
            .WithDescription("Returns what operations the current user can perform on the specified document. A caller with no access to the document receives all capabilities false.")
            .Produces<DocumentCapabilities>(200)
            .Produces(401) // Unauthorized
            .Produces(404); // Document not found

        // POST /api/documents/permissions/batch
        group.MapPost("permissions/batch", GetBatchPermissionsAsync)
            .WithName("GetBatchPermissions")
            .WithSummary("Get the CALLING user's capabilities for multiple documents")
            .WithDescription("Batch endpoint to get permissions for multiple documents in one request (performance optimization for galleries). Always scoped to the authenticated caller.")
            .Produces<BatchPermissionsResponse>(200)
            .Produces(400) // Bad request
            .Produces(401); // Unauthorized
    }

    /// <summary>
    /// Gets the calling user's capabilities for a single document.
    /// </summary>
    /// <param name="documentId">Dataverse document ID (sprk_documentid)</param>
    /// <param name="httpContext">HTTP context to extract user identity and bearer token</param>
    /// <param name="authorizationService">Supplies the caller-scoped access snapshot</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>DocumentCapabilities indicating what the CALLER can do</returns>
    private static async Task<IResult> GetDocumentPermissionsAsync(
        string documentId,
        HttpContext httpContext,
        AuthorizationService authorizationService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Extract user ID from claims (Azure AD oid claim)
        var userId = ResolveCallerId(httpContext);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Cannot determine user ID from claims for permissions check");
            return TypedResults.Unauthorized();
        }

        logger.LogInformation("Retrieving permissions for user {UserId} on document {DocumentId}", userId, documentId);

        try
        {
            // Caller-scoped (FR-05): the caller's own bearer token is forwarded, so the snapshot answers
            // "what may THIS CALLER do" rather than "what may the application do". A missing token is
            // handled fail-closed inside GetCallerAccessAsync (AccessRights.None), never as app-only.
            var snapshot = await authorizationService.GetCallerAccessAsync(
                userId,
                documentId,
                TokenHelper.ExtractBearerTokenOrNull(httpContext),
                ct);

            var capabilities = MapToDocumentCapabilities(snapshot);

            logger.LogDebug(
                "Permissions retrieved for document {DocumentId}: AccessRights={AccessRights}, CanPreview={CanPreview}, CanDownload={CanDownload}",
                documentId, snapshot.AccessRights, capabilities.CanPreview, capabilities.CanDownload);

            return TypedResults.Ok(capabilities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving permissions for document {DocumentId}", documentId);

            // Fail-closed: no capabilities on error. Never fall back to an app-scoped answer.
            return TypedResults.Ok(NoCapabilities(documentId, userId, "None (Error)"));
        }
    }

    /// <summary>
    /// Gets the calling user's capabilities for multiple documents in one request.
    /// Performance optimization for galleries/lists that display many documents.
    /// </summary>
    /// <param name="request">Batch request with document IDs</param>
    /// <param name="httpContext">HTTP context to extract user identity and bearer token</param>
    /// <param name="authorizationService">Supplies the caller-scoped access snapshot</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BatchPermissionsResponse with the CALLER's capabilities for all documents</returns>
    private static async Task<IResult> GetBatchPermissionsAsync(
        [FromBody] BatchPermissionsRequest request,
        HttpContext httpContext,
        AuthorizationService authorizationService,
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

        // Identity comes from the validated token ONLY (task 006 / FR-05).
        //
        // This handler previously honoured a `UserId` supplied in the request BODY, which let a caller
        // ask about someone else's capabilities. That is incompatible with a caller-scoped answer, and
        // it is not merely cosmetic: DataverseAccessDataSource.cs:184-199 treats `userId` and
        // `userAccessToken` as INDEPENDENT inputs — `userId` selects whose Dataverse principal is
        // queried while the token selects the auth mode. Honouring a body-supplied id would run the
        // query as the caller (OBO) while asking about a different principal, and task 014's cache key
        // `sdap:auth:access:obo:{userId}:{resourceId}` would then be written under the VICTIM's oid.
        var userId = ResolveCallerId(httpContext);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Cannot determine user ID from claims for batch permissions check");
            return TypedResults.Unauthorized();
        }

        // Read the caller's token once — it is the same credential for every document in the batch.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

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
                var snapshot = await authorizationService.GetCallerAccessAsync(
                    userId, documentId, callerToken, ct);

                permissions.Add(MapToDocumentCapabilities(snapshot));
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
                permissions.Add(NoCapabilities(documentId, userId, "None (Error)"));

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
    /// Resolves the calling user's Entra object id from the validated token's claims.
    /// The ONLY identity source for these endpoints — never a request-supplied value.
    /// </summary>
    private static string? ResolveCallerId(HttpContext httpContext) =>
        CallerResolution.ResolveObjectId(httpContext.User);

    /// <summary>
    /// The single fail-closed capability shape: every capability false.
    /// </summary>
    /// <remarks>
    /// Used for every path that cannot produce a trustworthy caller-scoped answer. Exists as one factory
    /// so a capability added to <see cref="DocumentCapabilities"/> later cannot accidentally default to
    /// <c>true</c> on one error path and <c>false</c> on another — all boolean members are left at their
    /// <c>false</c> default here deliberately.
    /// </remarks>
    private static DocumentCapabilities NoCapabilities(string documentId, string userId, string accessRights) =>
        new()
        {
            DocumentId = documentId,
            UserId = userId,
            AccessRights = accessRights,
            CalculatedAt = DateTimeOffset.UtcNow
            // All boolean capabilities intentionally left at their `false` default.
        };

    /// <summary>
    /// Maps a caller-scoped <see cref="AccessSnapshot"/> to the capability DTO.
    /// </summary>
    /// <remarks>
    /// Every flag is derived from <see cref="OperationAccessPolicy"/> using the SAME operation strings
    /// and the SAME <c>HasRequiredRights</c> comparison the enforcement path uses
    /// (<c>OperationAccessRule</c>). There is deliberately no second capability calculus: if a
    /// capability here disagreed with what the filter would decide, the UI would render an affordance
    /// the server rejects — or, worse, hide one it would have allowed.
    /// </remarks>
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
