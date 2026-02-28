using System.Security.Claims;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Playbook management endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides CRUD operations for analysis playbooks.
/// </summary>
public static class PlaybookEndpoints
{
    public static IEndpointRouteBuilder MapPlaybookEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Re-enable authorization once MSAL auth is implemented in PlaybookBuilderHost PCF
        // For development/testing, endpoints are temporarily accessible without authentication.
        // Production deployment MUST restore .RequireAuthorization() and implement proper auth.
        var group = app.MapGroup("/api/ai/playbooks")
            .AllowAnonymous()  // TEMPORARY: Allow anonymous for development (was: .RequireAuthorization())
            .WithTags("AI Playbooks");

        // POST /api/ai/playbooks - Create new playbook
        group.MapPost("/", CreatePlaybook)
            .WithName("CreatePlaybook")
            .WithSummary("Create a new analysis playbook")
            .WithDescription("Creates a new playbook with specified actions, skills, knowledge, and tools.")
            .Produces<PlaybookResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesValidationProblem();

        // PUT /api/ai/playbooks/{id} - Update existing playbook
        group.MapPut("/{id:guid}", UpdatePlaybook)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("UpdatePlaybook")
            .WithSummary("Update an existing playbook")
            .WithDescription("Updates playbook configuration. User must own the playbook.")
            .Produces<PlaybookResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesValidationProblem();

        // GET /api/ai/playbooks/{id} - Get playbook by ID
        group.MapGet("/{id:guid}", GetPlaybook)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("GetPlaybook")
            .WithSummary("Get a playbook by ID")
            .WithDescription("Retrieves playbook details. User must own the playbook or it must be public.")
            .Produces<PlaybookResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // GET /api/ai/playbooks/by-name/{name} - Get playbook by name
        group.MapGet("/by-name/{name}", GetPlaybookByName)
            .WithName("GetPlaybookByName")
            .WithSummary("Get a playbook by name")
            .WithDescription("Retrieves playbook details by name. Used by PCF for resolving system playbooks like 'Document Profile'.")
            .Produces<PlaybookResponse>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        // GET /api/ai/playbooks - List user's playbooks
        group.MapGet("/", ListUserPlaybooks)
            .WithName("ListUserPlaybooks")
            .WithSummary("List playbooks owned by the current user")
            .WithDescription("Returns a paginated list of playbooks owned by the authenticated user. Supports filtering by name and output type.")
            .Produces<PlaybookListResponse>()
            .ProducesProblem(401);

        // GET /api/ai/playbooks/public - List public playbooks
        group.MapGet("/public", ListPublicPlaybooks)
            .WithName("ListPublicPlaybooks")
            .WithSummary("List public playbooks")
            .WithDescription("Returns a paginated list of public playbooks shared by all users. Supports filtering by name and output type.")
            .Produces<PlaybookListResponse>()
            .ProducesProblem(401);

        // POST /api/ai/playbooks/{id}/share - Share a playbook
        group.MapPost("/{id:guid}/share", SharePlaybook)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("SharePlaybook")
            .WithSummary("Share a playbook with teams or organization")
            .WithDescription("Shares the playbook with specified teams or makes it organization-wide. Only the owner can share.")
            .Produces<ShareOperationResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/playbooks/{id}/unshare - Revoke sharing
        group.MapPost("/{id:guid}/unshare", RevokeShare)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("RevokePlaybookShare")
            .WithSummary("Revoke sharing from a playbook")
            .WithDescription("Revokes access from specified teams or removes organization-wide access. Only the owner can revoke.")
            .Produces<ShareOperationResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // GET /api/ai/playbooks/{id}/sharing - Get sharing info
        group.MapGet("/{id:guid}/sharing", GetSharingInfo)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("GetPlaybookSharingInfo")
            .WithSummary("Get playbook sharing information")
            .WithDescription("Returns information about who the playbook is shared with.")
            .Produces<PlaybookSharingInfo>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // GET /api/ai/playbooks/{id}/canvas - Get canvas layout
        group.MapGet("/{id:guid}/canvas", GetCanvasLayout)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("GetCanvasLayout")
            .WithSummary("Get canvas layout for playbook builder")
            .WithDescription("Returns the visual canvas layout (node positions, edges, viewport) for the playbook builder.")
            .Produces<CanvasLayoutResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // PUT /api/ai/playbooks/{id}/canvas - Save canvas layout
        group.MapPut("/{id:guid}/canvas", SaveCanvasLayout)
            .AddPlaybookOwnerAuthorizationFilter()
            .WithName("SaveCanvasLayout")
            .WithSummary("Save canvas layout for playbook builder")
            .WithDescription("Saves the visual canvas layout (node positions, edges, viewport). User must own the playbook.")
            .Produces<CanvasLayoutResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // GET /api/ai/playbooks/templates - List template playbooks
        group.MapGet("/templates", ListTemplates)
            .WithName("ListTemplates")
            .WithSummary("List template playbooks available for cloning")
            .WithDescription("Returns a paginated list of template playbooks that can be cloned as starting points for new playbooks.")
            .Produces<PlaybookListResponse>()
            .ProducesProblem(401);

        // POST /api/ai/playbooks/{id}/clone - Clone a playbook
        group.MapPost("/{id:guid}/clone", ClonePlaybook)
            .AddPlaybookAccessAuthorizationFilter()
            .WithName("ClonePlaybook")
            .WithSummary("Clone a playbook")
            .WithDescription("Creates a copy of the playbook owned by the current user. Useful for customizing templates.")
            .Produces<PlaybookResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    /// <summary>
    /// Create a new playbook.
    /// </summary>
    private static async Task<IResult> CreatePlaybook(
        SavePlaybookRequest request,
        IPlaybookService playbookService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        // Validate request
        var validationResult = await playbookService.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Playbook"] = validationResult.Errors
                });
        }

        try
        {
            var playbook = await playbookService.CreatePlaybookAsync(request, userId);
            logger.LogInformation("Created playbook {Id}: {Name}", playbook.Id, playbook.Name);

            return Results.Created($"/api/ai/playbooks/{playbook.Id}", playbook);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create playbook: {Name}", request.Name);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to create playbook");
        }
    }

    /// <summary>
    /// Update an existing playbook.
    /// </summary>
    private static async Task<IResult> UpdatePlaybook(
        Guid id,
        SavePlaybookRequest request,
        IPlaybookService playbookService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        // Validate request
        var validationResult = await playbookService.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Playbook"] = validationResult.Errors
                });
        }

        try
        {
            var playbook = await playbookService.UpdatePlaybookAsync(id, request, userId);
            logger.LogInformation("Updated playbook {Id}: {Name}", playbook.Id, playbook.Name);

            return Results.Ok(playbook);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update playbook {Id}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to update playbook");
        }
    }

    /// <summary>
    /// Get a playbook by ID.
    /// </summary>
    private static async Task<IResult> GetPlaybook(
        Guid id,
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        try
        {
            var playbook = await playbookService.GetPlaybookAsync(id);
            if (playbook == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(playbook);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get playbook {Id}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get playbook");
        }
    }

    /// <summary>
    /// Get a playbook by name.
    /// </summary>
    private static async Task<IResult> GetPlaybookByName(
        string name,
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        try
        {
            var playbook = await playbookService.GetByNameAsync(name);
            return Results.Ok(playbook);
        }
        catch (PlaybookNotFoundException ex)
        {
            logger.LogWarning("Playbook not found: {Name}", name);
            return Results.Problem(
                statusCode: 404,
                title: "Playbook Not Found",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get playbook by name: {Name}. Exception: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}",
                name, ex.GetType().Name, ex.Message, ex.StackTrace);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: $"Failed to get playbook: {ex.Message}");
        }
    }

    /// <summary>
    /// List playbooks owned by the current user.
    /// </summary>
    private static async Task<IResult> ListUserPlaybooks(
        IPlaybookService playbookService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        Guid? outputTypeId = null,
        string sortBy = "modifiedon",
        bool sortDescending = true)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        var query = new PlaybookQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            NameFilter = nameFilter,
            OutputTypeId = outputTypeId,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await playbookService.ListUserPlaybooksAsync(userId, query);
            logger.LogDebug("Listed {Count} playbooks for user {UserId}", result.Items.Length, userId);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list playbooks for user {UserId}", userId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list playbooks");
        }
    }

    /// <summary>
    /// List public playbooks shared by all users.
    /// </summary>
    private static async Task<IResult> ListPublicPlaybooks(
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        Guid? outputTypeId = null,
        string sortBy = "modifiedon",
        bool sortDescending = true)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        var query = new PlaybookQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            NameFilter = nameFilter,
            OutputTypeId = outputTypeId,
            PublicOnly = true,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await playbookService.ListPublicPlaybooksAsync(query);
            logger.LogDebug("Listed {Count} public playbooks", result.Items.Length);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list public playbooks");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list public playbooks");
        }
    }

    /// <summary>
    /// Share a playbook with teams or organization.
    /// </summary>
    private static async Task<IResult> SharePlaybook(
        Guid id,
        SharePlaybookRequest request,
        IPlaybookSharingService sharingService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        try
        {
            var result = await sharingService.SharePlaybookAsync(id, request, userId);
            if (!result.Success)
            {
                return Results.Problem(
                    statusCode: 400,
                    title: "Sharing Failed",
                    detail: result.ErrorMessage);
            }

            logger.LogInformation("Shared playbook {PlaybookId} by user {UserId}", id, userId);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to share playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to share playbook");
        }
    }

    /// <summary>
    /// Revoke sharing from a playbook.
    /// </summary>
    private static async Task<IResult> RevokeShare(
        Guid id,
        RevokeShareRequest request,
        IPlaybookSharingService sharingService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        try
        {
            var result = await sharingService.RevokeShareAsync(id, request, userId);
            if (!result.Success)
            {
                return Results.Problem(
                    statusCode: 400,
                    title: "Revoke Failed",
                    detail: result.ErrorMessage);
            }

            logger.LogInformation("Revoked sharing for playbook {PlaybookId} by user {UserId}", id, userId);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to revoke sharing for playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to revoke sharing");
        }
    }

    /// <summary>
    /// Get sharing information for a playbook.
    /// </summary>
    private static async Task<IResult> GetSharingInfo(
        Guid id,
        IPlaybookSharingService sharingService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        try
        {
            var sharingInfo = await sharingService.GetSharingInfoAsync(id);
            if (sharingInfo == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(sharingInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sharing info for playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get sharing info");
        }
    }

    /// <summary>
    /// Get canvas layout for a playbook.
    /// </summary>
    private static async Task<IResult> GetCanvasLayout(
        Guid id,
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        try
        {
            var result = await playbookService.GetCanvasLayoutAsync(id);
            if (result == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get canvas layout for playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get canvas layout");
        }
    }

    /// <summary>
    /// Save canvas layout for a playbook.
    /// </summary>
    private static async Task<IResult> SaveCanvasLayout(
        Guid id,
        SaveCanvasLayoutRequest request,
        IPlaybookService playbookService,
        INodeService nodeService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        if (request.Layout == null)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Layout is required");
        }

        try
        {
            // Persist the raw canvas JSON to the playbook record
            var result = await playbookService.SaveCanvasLayoutAsync(id, request.Layout);

            // Sync canvas visual design → executable sprk_playbooknode Dataverse records
            await nodeService.SyncCanvasToNodesAsync(id, request.Layout, cancellationToken);

            logger.LogInformation("Saved canvas layout and synced nodes for playbook {PlaybookId}", id);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save canvas layout for playbook {PlaybookId}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to save canvas layout");
        }
    }

    /// <summary>
    /// List template playbooks available for cloning.
    /// </summary>
    private static async Task<IResult> ListTemplates(
        IPlaybookService playbookService,
        ILoggerFactory loggerFactory,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        string sortBy = "modifiedon",
        bool sortDescending = true)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        var query = new PlaybookQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            NameFilter = nameFilter,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await playbookService.ListTemplatesAsync(query);
            logger.LogDebug("Listed {Count} template playbooks", result.Items.Length);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list template playbooks");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list template playbooks");
        }
    }

    /// <summary>
    /// Clone a playbook to create a new copy owned by the current user.
    /// </summary>
    private static async Task<IResult> ClonePlaybook(
        Guid id,
        ClonePlaybookRequest? request,
        IPlaybookService playbookService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        // Get user ID from claims
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        try
        {
            var clonedPlaybook = await playbookService.ClonePlaybookAsync(id, userId, request?.NewName);
            logger.LogInformation("Cloned playbook {SourceId} to {CloneId} for user {UserId}",
                id, clonedPlaybook.Id, userId);

            return Results.Created($"/api/ai/playbooks/{clonedPlaybook.Id}", clonedPlaybook);
        }
        catch (PlaybookNotFoundException ex)
        {
            logger.LogWarning("Source playbook not found for cloning: {Id}", id);
            return Results.Problem(
                statusCode: 404,
                title: "Playbook Not Found",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clone playbook {Id}", id);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to clone playbook");
        }
    }
}
