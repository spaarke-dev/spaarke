using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// SPE Admin endpoint group for file/folder item operations within a container.
///
/// Provides folder navigation via an optional <c>folderId</c> query parameter:
///   - Omitted: lists items at the container drive root.
///   - Provided: lists children of the specified folder (by DriveItem ID).
///
/// Also exposes file metadata sub-resources:
///   - GET .../versions    — version history
///   - GET .../thumbnails  — thumbnail URLs
///   - POST .../share      — create sharing link
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API — no controllers.
/// Follows ADR-007: No Graph SDK types in the public response.
/// Follows ADR-008: RequireAuthorization() applied to the route.
/// </remarks>
public static class ContainerItemEndpoints
{
    /// <summary>
    /// Registers all container item endpoints on the provided route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapContainerItemEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/spe/containers/{id}/items?configId={guid}&folderId={itemId}
        // Lists files and folders at the container root or within a specified subfolder.
        app.MapGet("/api/spe/containers/{id}/items", ListContainerItems)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("ListContainerItems")
            .WithSummary("List files and folders in an SPE container or subfolder")
            .Produces<IReadOnlyList<SpeAdminGraphService.SpeContainerItemSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containers/{id}/items/{itemId}/versions?configId={guid}
        // Returns chronological version history for a DriveItem.
        app.MapGet("/api/spe/containers/{id}/items/{itemId}/versions", GetFileVersions)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("GetFileVersions")
            .WithSummary("Get version history for a file in an SPE container")
            .Produces<IReadOnlyList<SpeAdminGraphService.SpeFileVersionSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containers/{id}/items/{itemId}/thumbnails?configId={guid}
        // Returns thumbnail URLs for a DriveItem.
        app.MapGet("/api/spe/containers/{id}/items/{itemId}/thumbnails", GetFileThumbnails)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("GetFileThumbnails")
            .WithSummary("Get thumbnail URLs for a file in an SPE container")
            .Produces<IReadOnlyList<SpeAdminGraphService.SpeThumbnailSet>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{id}/items/{itemId}/share?configId={guid}
        // Creates a sharing link for a DriveItem.
        app.MapPost("/api/spe/containers/{id}/items/{itemId}/share", CreateSharingLink)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("CreateSharingLink")
            .WithSummary("Create a sharing link for a file in an SPE container")
            .Accepts<CreateSharingLinkRequest>("application/json")
            .Produces<SpeAdminGraphService.SpeSharingLink>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containers/{id}/items/{itemId}/content?configId={guid}
        // Streams file content from Graph with correct Content-Type and Content-Disposition headers.
        app.MapGet("/api/spe/containers/{id}/items/{itemId}/content", DownloadItem)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("DownloadContainerItem")
            .WithSummary("Download a file from an SPE container")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containers/{id}/items/{itemId}/preview?configId={guid}
        // Returns a temporary preview URL for browser-based document viewing.
        app.MapGet("/api/spe/containers/{id}/items/{itemId}/preview", PreviewItem)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("PreviewContainerItem")
            .WithSummary("Get a temporary preview URL for a file in an SPE container")
            .Produces<PreviewUrlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/containers/{id}/items/{itemId}?configId={guid}
        // Deletes the item via Graph and writes an audit log entry.
        app.MapDelete("/api/spe/containers/{id}/items/{itemId}", DeleteItem)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("DeleteContainerItem")
            .WithSummary("Delete a file or folder from an SPE container")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{id}/folders?configId={guid}
        // Creates a new folder at the container root or within a specified parent folder.
        app.MapPost("/api/spe/containers/{id}/folders", CreateFolder)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("CreateFolder")
            .WithSummary("Create a new folder in an SPE container")
            .Accepts<CreateFolderRequest>("application/json")
            .Produces<SpeAdminGraphService.SpeContainerItemSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{id}/items/upload?configId={guid}&folderId={itemId}
        // Uploads a file into an SPE container. Accepts multipart/form-data with a "file" field.
        // Routes to direct PUT (< 4 MB) or resumable upload session (>= 4 MB) automatically.
        app.MapPost("/api/spe/containers/{id}/items/upload", UploadContainerItem)
            .RequireAuthorization()
            .WithTags("SpeAdmin")
            .WithName("UploadContainerItem")
            .WithSummary("Upload a file into an SPE container or subfolder")
            .Produces<SpeAdminGraphService.SpeContainerItemSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .DisableAntiforgery(); // multipart form uploads require antiforgery to be disabled (or handled separately)

        return app;
    }

    // -------------------------------------------------------------------------
    // Request/response models
    // -------------------------------------------------------------------------

    /// <summary>
    /// Request body for POST .../share — specifies link type, scope, and optional expiration.
    /// </summary>
    /// <param name="LinkType">Required. One of: "view", "edit", "embed".</param>
    /// <param name="Scope">Required. One of: "anonymous", "organization", "users".</param>
    /// <param name="ExpirationDateTime">Optional. UTC expiration for the link. Null means no expiry.</param>
    public sealed record CreateSharingLinkRequest(
        string LinkType,
        string Scope,
        DateTimeOffset? ExpirationDateTime);

    /// <summary>
    /// Response body for GET .../preview — returns the temporary preview URL.
    /// </summary>
    /// <param name="PreviewUrl">
    /// Time-limited URL that can be embedded in an iframe for in-browser document preview.
    /// </param>
    public sealed record PreviewUrlResponse(string PreviewUrl);

    /// <summary>
    /// Request body for POST /api/spe/containers/{id}/folders — specifies the folder name and
    /// optional parent folder location.
    /// </summary>
    /// <param name="Name">
    /// Required. Name of the folder to create.
    /// Must not be empty and must not contain characters invalid in SharePoint paths:
    /// <c>" * : &lt; &gt; ? / \ |</c>
    /// </param>
    /// <param name="ParentFolderId">
    /// Optional DriveItem ID of the parent folder. When <c>null</c> or omitted, the folder
    /// is created at the container drive root.
    /// </param>
    public sealed record CreateFolderRequest(string Name, string? ParentFolderId);

    // -------------------------------------------------------------------------
    // Allowed values for request validation
    // -------------------------------------------------------------------------

    private static readonly HashSet<string> AllowedLinkTypes =
        new(StringComparer.OrdinalIgnoreCase) { "view", "edit", "embed" };

    private static readonly HashSet<string> AllowedScopes =
        new(StringComparer.OrdinalIgnoreCase) { "anonymous", "organization", "users" };

    /// <summary>
    /// Characters invalid in SharePoint / OneDrive folder names.
    /// Matches: " * : &lt; &gt; ? / \ |
    /// See: https://support.microsoft.com/office/restrictions-and-limitations-in-onedrive-64883a5d-228e-48f5-b3d2-eb39e07630fa
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex InvalidFolderNameChars =
        new(@"[""*:<>?/\\|]",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Handles GET /api/spe/containers/{id}/items.
    ///
    /// <paramref name="id"/> — the SPE FileStorageContainer ID.
    /// <paramref name="configId"/> — Dataverse primary key of sprk_specontainertypeconfig.
    ///   The config provides app registration credentials for the Graph client.
    /// <paramref name="folderId"/> — optional DriveItem ID; when omitted root children are returned.
    /// </summary>
    private static async Task<IResult> ListContainerItems(
        string id,
        Guid configId,
        string? folderId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");
        }

        logger.LogInformation(
            "SPE Admin list items — Container: {ContainerId}, ConfigId: {ConfigId}, FolderId: {FolderId}, " +
            "TraceId: {TraceId}",
            id, configId, folderId ?? "(root)", context.TraceIdentifier);

        // Resolve the container type config from Dataverse via SpeAdminGraphService.
        // The graph service reads sprk_specontainertypeconfig to get app registration credentials,
        // then caches the GraphServiceClient per configId (30-min TTL).
        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var items = await graphService.ListContainerItemsAsync(graphClient, id, folderId, ct);

            logger.LogInformation(
                "SPE Admin list items succeeded — Container: {ContainerId}, FolderId: {FolderId}, " +
                "ItemCount: {Count}, TraceId: {TraceId}",
                id, folderId ?? "(root)", items.Count, context.TraceIdentifier);

            return TypedResults.Ok(items);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error listing items — Container: {ContainerId}, FolderId: {FolderId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, folderId ?? "(root)", odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            // Surfaced when Key Vault secret resolution fails in GetClientForConfigAsync
            logger.LogError(ex,
                "Configuration error listing items — Container: {ContainerId}, ConfigId: {ConfigId}, " +
                "TraceId: {TraceId}",
                id, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // GET /api/spe/containers/{id}/items/{itemId}/versions
    // =========================================================================

    /// <summary>
    /// Handles GET /api/spe/containers/{id}/items/{itemId}/versions.
    ///
    /// Returns the chronological version history for the specified DriveItem.
    /// Versions are ordered oldest-first (v1.0 at index 0).
    /// </summary>
    private static async Task<IResult> GetFileVersions(
        string id,
        string itemId,
        Guid configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        logger.LogInformation(
            "SPE Admin get file versions — Container: {ContainerId}, ItemId: {ItemId}, " +
            "ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var versions = await graphService.GetFileVersionsAsync(graphClient, id, itemId, ct);

            logger.LogInformation(
                "SPE Admin get file versions succeeded — Container: {ContainerId}, ItemId: {ItemId}, " +
                "VersionCount: {Count}, TraceId: {TraceId}",
                id, itemId, versions.Count, context.TraceIdentifier);

            return TypedResults.Ok(versions);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error getting file versions — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error getting file versions — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // GET /api/spe/containers/{id}/items/{itemId}/thumbnails
    // =========================================================================

    /// <summary>
    /// Handles GET /api/spe/containers/{id}/items/{itemId}/thumbnails.
    ///
    /// Returns thumbnail URLs for the specified DriveItem.
    /// Returns an empty array for folders or items that do not support thumbnails.
    /// </summary>
    private static async Task<IResult> GetFileThumbnails(
        string id,
        string itemId,
        Guid configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        logger.LogInformation(
            "SPE Admin get thumbnails — Container: {ContainerId}, ItemId: {ItemId}, " +
            "ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var thumbnails = await graphService.GetFileThumbnailsAsync(graphClient, id, itemId, ct);

            logger.LogInformation(
                "SPE Admin get thumbnails succeeded — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ThumbnailSets: {Count}, TraceId: {TraceId}",
                id, itemId, thumbnails.Count, context.TraceIdentifier);

            return TypedResults.Ok(thumbnails);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error getting thumbnails — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error getting thumbnails — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // POST /api/spe/containers/{id}/items/{itemId}/share
    // =========================================================================

    /// <summary>
    /// Handles POST /api/spe/containers/{id}/items/{itemId}/share.
    ///
    /// Creates a sharing link via Graph createLink action. The request body specifies
    /// the link type ("view", "edit", "embed"), scope ("anonymous", "organization", "users"),
    /// and an optional expiration date.
    /// </summary>
    private static async Task<IResult> CreateSharingLink(
        string id,
        string itemId,
        Guid configId,
        CreateSharingLinkRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        if (string.IsNullOrWhiteSpace(request.LinkType) || !AllowedLinkTypes.Contains(request.LinkType))
        {
            return ProblemDetailsHelper.ValidationError(
                $"Invalid linkType '{request.LinkType}'. Allowed values: view, edit, embed.");
        }

        if (string.IsNullOrWhiteSpace(request.Scope) || !AllowedScopes.Contains(request.Scope))
        {
            return ProblemDetailsHelper.ValidationError(
                $"Invalid scope '{request.Scope}'. Allowed values: anonymous, organization, users.");
        }

        logger.LogInformation(
            "SPE Admin create sharing link — Container: {ContainerId}, ItemId: {ItemId}, " +
            "Type: {LinkType}, Scope: {Scope}, ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, request.LinkType, request.Scope, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var sharingLink = await graphService.CreateSharingLinkAsync(
                graphClient, id, itemId,
                request.LinkType, request.Scope, request.ExpirationDateTime,
                ct);

            logger.LogInformation(
                "SPE Admin create sharing link succeeded — Container: {ContainerId}, ItemId: {ItemId}, " +
                "Type: {LinkType}, TraceId: {TraceId}",
                id, itemId, request.LinkType, context.TraceIdentifier);

            return TypedResults.Ok(sharingLink);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error creating sharing link — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error creating sharing link — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // POST /api/spe/containers/{id}/folders
    // =========================================================================

    /// <summary>
    /// Handles POST /api/spe/containers/{id}/folders.
    ///
    /// Creates a new folder inside the specified SPE container. The folder is created at
    /// the drive root when <c>parentFolderId</c> is omitted, or as a child of the specified
    /// DriveItem when provided.
    ///
    /// Folder names are validated before calling Graph:
    ///   - Must not be empty or whitespace.
    ///   - Must not contain characters invalid in SharePoint paths: " * : &lt; &gt; ? / \ |
    ///
    /// Graph uses <c>@microsoft.graph.conflictBehavior: fail</c>, so a 409 Conflict is returned
    /// when a folder with the same name already exists at the target location.
    ///
    /// Returns 201 Created with the created folder's metadata including ID, name, and timestamps.
    /// </summary>
    private static async Task<IResult> CreateFolder(
        string id,
        Guid configId,
        CreateFolderRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");
        }

        // Validate folder name is not empty.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ProblemDetailsHelper.ValidationError("Folder name must not be empty.");
        }

        // Validate folder name does not contain SharePoint-invalid characters.
        if (InvalidFolderNameChars.IsMatch(request.Name))
        {
            return ProblemDetailsHelper.ValidationError(
                $"Folder name '{request.Name}' contains invalid characters. " +
                "The following characters are not allowed: \" * : < > ? / \\ |");
        }

        logger.LogInformation(
            "SPE Admin create folder — Container: {ContainerId}, FolderName: {FolderName}, " +
            "ParentFolderId: {ParentFolderId}, ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, request.Name, request.ParentFolderId ?? "(root)", configId, context.TraceIdentifier);

        // Resolve the container type config from Dataverse to get app registration credentials.
        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var folder = await graphService.CreateFolderAsync(
                graphClient, id, request.Name, request.ParentFolderId, ct);

            logger.LogInformation(
                "SPE Admin create folder succeeded — Container: {ContainerId}, FolderName: {FolderName}, " +
                "DriveItemId: {DriveItemId}, TraceId: {TraceId}",
                id, request.Name, folder.Id, context.TraceIdentifier);

            // 201 Created with Location header pointing to the item in the items list.
            return TypedResults.Created(
                $"/api/spe/containers/{id}/items/{folder.Id}?configId={configId}",
                folder);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status409Conflict)
        {
            logger.LogWarning(
                "Folder name conflict — Container: {ContainerId}, FolderName: {FolderName}, " +
                "ParentFolderId: {ParentFolderId}, TraceId: {TraceId}",
                id, request.Name, request.ParentFolderId ?? "(root)", context.TraceIdentifier);

            return Results.Problem(
                title: "Conflict",
                detail: $"A folder named '{request.Name}' already exists at the specified location.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error creating folder — Container: {ContainerId}, FolderName: {FolderName}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, request.Name, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            // Surfaced when Key Vault secret resolution or drive ID resolution fails.
            logger.LogError(ex,
                "Configuration error creating folder — Container: {ContainerId}, FolderName: {FolderName}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, request.Name, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // GET /api/spe/containers/{id}/items/{itemId}/content
    // =========================================================================

    /// <summary>
    /// Handles GET /api/spe/containers/{id}/items/{itemId}/content.
    ///
    /// Streams the file content from Graph with appropriate Content-Type and
    /// Content-Disposition headers. The stream is piped directly without
    /// buffering the entire file in memory (suited for large files).
    ///
    /// Returns 404 when the item does not exist.
    /// </summary>
    private static async Task<IResult> DownloadItem(
        string id,
        string itemId,
        Guid configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        logger.LogInformation(
            "SPE Admin download item — Container: {ContainerId}, ItemId: {ItemId}, " +
            "ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var result = await graphService.DownloadDriveItemAsync(graphClient, id, itemId, ct);

            if (result is null)
            {
                logger.LogInformation(
                    "Item {ItemId} not found in container {ContainerId}. TraceId: {TraceId}",
                    itemId, id, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Item '{itemId}' not found in container '{id}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var (contentStream, mimeType, fileName) = result.Value;

            logger.LogInformation(
                "SPE Admin download succeeded — Container: {ContainerId}, ItemId: {ItemId}, " +
                "FileName: {FileName}, MimeType: {MimeType}, TraceId: {TraceId}",
                id, itemId, fileName, mimeType, context.TraceIdentifier);

            // RFC 6266: set Content-Disposition header so browsers prompt a file download.
            // filename*=UTF-8'' encoding handles non-ASCII characters in file names.
            var encodedFileName = Uri.EscapeDataString(fileName);
            context.Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"{fileName}\"; filename*=UTF-8''{encodedFileName}";

            return Results.Stream(contentStream, contentType: mimeType);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error downloading item — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error downloading item — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // GET /api/spe/containers/{id}/items/{itemId}/preview
    // =========================================================================

    /// <summary>
    /// Handles GET /api/spe/containers/{id}/items/{itemId}/preview.
    ///
    /// Returns a temporary preview URL suitable for browser-based viewing (e.g., iframe embed).
    /// Supported for Office files (Word, Excel, PowerPoint), PDFs, and images.
    ///
    /// Returns 404 when the item does not exist.
    /// </summary>
    private static async Task<IResult> PreviewItem(
        string id,
        string itemId,
        Guid configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        logger.LogInformation(
            "SPE Admin preview item — Container: {ContainerId}, ItemId: {ItemId}, " +
            "ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var previewUrl = await graphService.GetPreviewUrlAsync(graphClient, id, itemId, ct);

            if (previewUrl is null)
            {
                logger.LogInformation(
                    "Item {ItemId} not found (or preview unavailable) in container {ContainerId}. TraceId: {TraceId}",
                    itemId, id, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Item '{itemId}' not found in container '{id}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            logger.LogInformation(
                "SPE Admin preview URL obtained — Container: {ContainerId}, ItemId: {ItemId}, TraceId: {TraceId}",
                id, itemId, context.TraceIdentifier);

            return TypedResults.Ok(new PreviewUrlResponse(previewUrl));
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error getting preview URL — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error getting preview URL — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // =========================================================================
    // DELETE /api/spe/containers/{id}/items/{itemId}
    // =========================================================================

    /// <summary>
    /// Handles DELETE /api/spe/containers/{id}/items/{itemId}.
    ///
    /// Deletes the specified DriveItem from the SPE container via Graph.
    /// Writes an audit log entry with category "FileDeleted" after successful deletion.
    /// Audit logging is fire-and-forget — a logging failure does not cause a 500 response.
    ///
    /// Returns 204 No Content on success.
    /// Returns 404 when the item does not exist.
    /// </summary>
    private static async Task<IResult> DeleteItem(
        string id,
        string itemId,
        Guid configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");

        if (string.IsNullOrWhiteSpace(itemId))
            return ProblemDetailsHelper.ValidationError("Item ID must not be empty.");

        logger.LogInformation(
            "SPE Admin delete item — Container: {ContainerId}, ItemId: {ItemId}, " +
            "ConfigId: {ConfigId}, TraceId: {TraceId}",
            id, itemId, configId, context.TraceIdentifier);

        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var deleted = await graphService.DeleteDriveItemAsync(graphClient, id, itemId, ct);

            if (!deleted)
            {
                logger.LogInformation(
                    "Item {ItemId} not found in container {ContainerId} (delete 404). TraceId: {TraceId}",
                    itemId, id, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Item '{itemId}' not found in container '{id}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            logger.LogInformation(
                "SPE Admin delete succeeded — Container: {ContainerId}, ItemId: {ItemId}, TraceId: {TraceId}",
                id, itemId, context.TraceIdentifier);

            // Audit log: "FileDeleted" category, fire-and-forget (audit failure must not block response).
            // Target resource format "container/{containerId}/item/{itemId}" for clear audit trail.
            // Use CancellationToken.None: audit must complete even if the HTTP connection is closed.
            _ = auditService.LogOperationAsync(
                operation: "DeleteDriveItem",
                category: "FileDeleted",
                targetResource: $"container/{id}/item/{itemId}",
                responseStatus: StatusCodes.Status204NoContent,
                configId: configId,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error deleting item — Container: {ContainerId}, ItemId: {ItemId}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, itemId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Configuration error deleting item — Container: {ContainerId}, ItemId: {ItemId}, " +
                "ConfigId: {ConfigId}, TraceId: {TraceId}",
                id, itemId, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Handles POST /api/spe/containers/{id}/items/upload.
    ///
    /// Accepts multipart/form-data with a required "file" form field.
    /// Optional "folderId" query parameter places the file inside a specific subfolder.
    ///
    /// Upload strategy is selected automatically by SpeAdminGraphService.UploadFileToContainerAsync:
    ///   - Files under 4 MB: direct PUT to drive item content endpoint.
    ///   - Files 4 MB and above: Graph resumable upload session (chunked).
    ///
    /// On success: 201 Created with the uploaded DriveItem summary.
    /// On failure: ProblemDetails (400/404/429/500) per ADR-019.
    ///
    /// Audit log: writes "FileUploaded" category entry to sprk_speauditlog (fire-and-forget).
    /// </summary>
    private static async Task<IResult> UploadContainerItem(
        string id,
        Guid configId,
        string? folderId,
        HttpRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ProblemDetailsHelper.ValidationError("Container ID must not be empty.");
        }

        // Validate multipart content
        if (!request.HasFormContentType)
        {
            return ProblemDetailsHelper.ValidationError(
                "Request must be multipart/form-data. Include a 'file' field with the file to upload.");
        }

        IFormFile? formFile;
        try
        {
            var form = await request.ReadFormAsync(ct);
            formFile = form.Files["file"];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to read multipart form for upload — Container: {ContainerId}, TraceId: {TraceId}",
                id, context.TraceIdentifier);
            return ProblemDetailsHelper.ValidationError("Failed to read the uploaded file from the request.");
        }

        if (formFile is null || formFile.Length == 0)
        {
            return ProblemDetailsHelper.ValidationError(
                "The 'file' form field is required and must not be empty.");
        }

        var fileName = Path.GetFileName(formFile.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ProblemDetailsHelper.ValidationError("The uploaded file must have a non-empty file name.");
        }

        logger.LogInformation(
            "SPE Admin upload — Container: {ContainerId}, ConfigId: {ConfigId}, File: {FileName}, " +
            "Size: {FileSize}, FolderId: {FolderId}, TraceId: {TraceId}",
            id, configId, fileName, formFile.Length, folderId ?? "(root)", context.TraceIdentifier);

        // Resolve container type config from Dataverse.
        var config = await graphService.ResolveConfigAsync(configId, ct);
        if (config is null)
        {
            logger.LogWarning(
                "Container type config {ConfigId} not found. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container type configuration '{configId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            SpeAdminGraphService.SpeContainerItemSummary uploadedItem;
            await using (var fileStream = formFile.OpenReadStream())
            {
                uploadedItem = await graphService.UploadFileToContainerAsync(
                    graphClient, id, fileName, fileStream, formFile.Length, folderId, ct);
            }

            logger.LogInformation(
                "SPE Admin upload succeeded — Container: {ContainerId}, DriveItemId: {ItemId}, " +
                "FileName: {FileName}, TraceId: {TraceId}",
                id, uploadedItem.Id, uploadedItem.Name, context.TraceIdentifier);

            // Audit log: fire-and-forget — audit failures must never block the primary response.
            var auditTarget = folderId is null
                ? $"container:{id}/file:{fileName}"
                : $"container:{id}/folder:{folderId}/file:{fileName}";

            _ = auditService.LogOperationAsync(
                operation: "UploadFile",
                category: "FileUploaded",
                targetResource: auditTarget,
                responseStatus: StatusCodes.Status201Created,
                configId: configId,
                cancellationToken: default);

            return TypedResults.Created(
                $"/api/spe/containers/{id}/items/{uploadedItem.Id}",
                uploadedItem);
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "Graph API error uploading file — Container: {ContainerId}, File: {FileName}, " +
                "GraphStatus: {Status}, TraceId: {TraceId}",
                id, fileName, odataError.ResponseStatusCode, context.TraceIdentifier);

            return ProblemDetailsHelper.FromGraphException(odataError);
        }
        catch (InvalidOperationException ex)
        {
            // Surfaced when Key Vault secret resolution or upload session creation fails.
            logger.LogError(ex,
                "Configuration error uploading file — Container: {ContainerId}, ConfigId: {ConfigId}, " +
                "TraceId: {TraceId}",
                id, configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Configuration Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
