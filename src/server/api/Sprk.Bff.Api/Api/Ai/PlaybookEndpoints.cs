using System.Security.Claims;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Playbook management endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides CRUD operations for analysis playbooks.
/// </summary>
public static class PlaybookEndpoints
{
    public static IEndpointRouteBuilder MapPlaybookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/playbooks")
            .RequireAuthorization()
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

        // GET /api/ai/playbooks/by-name/{name} was REMOVED by unified-access-control-r2 task 164 (owner round 10
        // item 1): no caller in the repo (useAiSummary and DocumentEmailWizard moved to /by-id in FR-03 task 021),
        // not in any published API description, and sweep finding #55 (any signed-in user could read any private
        // playbook's definition by name, app-only, and its 500 echoed exception text).

        // GET /api/ai/playbooks/by-id/{id} - Get playbook by stable-ID alternate key (FR-01)
        // Cached 5 min per ADR-014, tenant-scoped per ADR-008.
        // Per Q&A 2026-06-22 Q1: uses sprk_playbookid (GUID-format stable-ID alt-key).
        // unified-access-control-r2 task 164 (sweep #55): the playbook-use decision (public, or the caller's own
        // Dataverse Read on the row) runs in the filter BEFORE the response cache is read, and an unknown id, a
        // denied id and a decision fault answer one uniform 404 that carries no id.
        group.MapGet("/by-id/{id}", GetPlaybookById)
            .AddPlaybookByIdAuthorizationFilter()
            .WithName("GetPlaybookById")
            .WithSummary("Get a playbook by stable-ID alternate key (sprk_playbookid)")
            .WithDescription(
                "Retrieves a playbook by its sprk_playbookid alternate key (GUID-format opaque ID; " +
                "value mirrors the row's sprk_analysisplaybookid PK per Q&A 2026-06-22 Q1). " +
                "Result is cached for 5 minutes per (tenantId, id) per ADR-014. " +
                "Tenant scoping is enforced via the JWT 'tid' claim per ADR-008.")
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
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(503);

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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
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

        // Gate D-G6-2: the row is OWNED by the caller's Dataverse systemuserid (WhoAmI over OBO — the same resolution
        // OwnerOnly compares with), never left owned by the BFF application user the create runs as, which would lock
        // its creator out of PUT / share / unshare. Unresolvable → refuse; nothing is created (ADR-003).
        var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(
            httpContext, httpContext.RequestAborted);
        if (ownerSystemUserId is null)
        {
            logger.LogWarning("Creating a playbook for user {UserId}: the caller's systemuserid is unresolvable; refusing", userId);
            return OwnerUnresolved();
        }

        try
        {
            var playbook = await playbookService.CreatePlaybookAsync(request, ownerSystemUserId.Value);
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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
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
    /// Get a playbook by stable-ID alternate key (FR-01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per Q&amp;A 2026-06-22 Q1, uses the <c>sprk_playbookid</c> alternate key (GUID-format
    /// opaque ID; value mirrors the row's <c>sprk_analysisplaybookid</c> PK). The admin-facing
    /// <c>sprk_playbookcode</c> slug is NOT consumed here.
    /// </para>
    /// <para>
    /// Tenant-scoped 5-min cache (ADR-014) is layered on top of the service's 1-hour global cache
    /// to ensure tenant isolation per ADR-008. Cache key shape: <c>"playbook-by-id:{tenantId}:{id-upper-invariant}"</c>
    /// (GUID strings are case-insensitive; the upper-invariant call is defensive).
    /// </para>
    /// <para>
    /// unified-access-control-r2 task 164 (sweep #55): the 404 is the ONE uniform playbook-unavailable body
    /// (<see cref="PlaybookAuthorizationFilter.UniformPlaybookNotFound"/>) that the filter also returns for a
    /// denied id and a decision fault, so the status, title, detail and reasonCode do not reveal whether a
    /// playbook the caller may not read exists. It no longer echoes the requested id. The filter runs BEFORE
    /// this handler, so the response cache below is only ever read for a caller the decision has allowed.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetPlaybookById(
        string id,
        IPlaybookLookupService playbookLookup,
        IEndpointResponseCache cache,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PlaybookEndpoints");

        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Playbook id is required");
        }

        // Tenant scoping (ADR-008): resolve tid claim. Falls back to "unknown-tenant" for local-dev
        // schemes that may not emit tid; the cache key still differs from real-tenant entries.
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "unknown-tenant";

        // Cache key: tenant + id (case-insensitive — GUID strings are case-insensitive by convention) per ADR-014.
        var cacheKey = $"playbook-by-id:{tenantId}:{id.ToUpperInvariant()}";

        try
        {
            // Cache wrapper (ADR-009 / CICD-087) preserves GetOrCreateAsync semantics
            // including the Size=1 cache-entry hint for size-limited MemoryCache configs.
            var playbook = await cache.GetOrCreateAsync<PlaybookResponse>(
                cacheKey,
                TimeSpan.FromMinutes(5),
                async ct => await playbookLookup.GetByIdAsync(id, ct),
                cancellationToken);

            if (playbook is null)
            {
                // Defensive — GetByIdAsync should throw on miss, but guard against null cache entry.
                logger.LogWarning("Playbook by-id lookup returned null for id '{Id}' (tenant {TenantId})", id, tenantId);
                return PlaybookAuthorizationFilter.UniformPlaybookNotFound(httpContext);
            }

            return Results.Ok(playbook);
        }
        catch (PlaybookNotFoundException)
        {
            logger.LogWarning("Playbook not found by id '{Id}' for tenant {TenantId}", id, tenantId);
            // Task 164: the SAME body the filter returns for a denied id (no id echo, ADR-019 reasonCode).
            return PlaybookAuthorizationFilter.UniformPlaybookNotFound(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get playbook by id '{Id}' for tenant {TenantId}", id, tenantId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get playbook by id");
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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
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
            // Owner round 12 item 6 (task 164): _ownerid_value holds a Dataverse systemuserid, never the Entra
            // oid, so the filter is the CALLER's systemuserid (WhoAmI over OBO). Unresolvable → an empty page
            // (fail closed: no other owner's rows can be listed in its place).
            var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(
                httpContext, httpContext.RequestAborted);
            if (ownerSystemUserId is null)
            {
                logger.LogWarning("Listing owned playbooks for user {UserId}: the caller's systemuserid is unresolvable; returning an empty page", userId);
                return Results.Ok(new PlaybookListResponse
                {
                    Items = [],
                    TotalCount = 0,
                    Page = query.Page,
                    PageSize = query.GetNormalizedPageSize()
                });
            }

            var result = await playbookService.ListUserPlaybooksAsync(ownerSystemUserId.Value, query);
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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        try
        {
            // Owner round 12 item 6 (task 164): the sharing service compares this with the row's _ownerid_value,
            // a Dataverse systemuserid — pass the caller's systemuserid, never the Entra oid (which matched no one).
            var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(
                httpContext, httpContext.RequestAborted);
            if (ownerSystemUserId is null)
            {
                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "You do not have permission to modify this playbook");
            }

            var result = await sharingService.SharePlaybookAsync(id, request, ownerSystemUserId.Value);
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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        try
        {
            // Owner round 12 item 6 (task 164): the sharing service compares this with the row's _ownerid_value,
            // a Dataverse systemuserid — pass the caller's systemuserid, never the Entra oid (which matched no one).
            var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(
                httpContext, httpContext.RequestAborted);
            if (ownerSystemUserId is null)
            {
                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "You do not have permission to modify this playbook");
            }

            var result = await sharingService.RevokeShareAsync(id, request, ownerSystemUserId.Value);
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
    internal static async Task<IResult> SaveCanvasLayout(
        Guid id,
        SaveCanvasLayoutRequest request,
        HttpContext httpContext,
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
            // D-97 / PB-08: repo-deployed system playbooks are read-only in the Designer. Check ONCE, before
            // anything is persisted; the permit carries the node snapshot the check read.
            var permit = await nodeService.EnsureCanvasSyncAllowedAsync(id, cancellationToken);

            // Persist the raw canvas JSON to the playbook record
            var result = await playbookService.SaveCanvasLayoutAsync(id, request.Layout);

            // Sync canvas visual design → executable sprk_playbooknode Dataverse records (no re-check, no re-read)
            await nodeService.SyncCanvasToNodesAsync(permit, request.Layout, cancellationToken);

            logger.LogInformation("Saved canvas layout and synced nodes for playbook {PlaybookId}", id);
            return Results.Ok(result);
        }
        catch (ProtectedPlaybookCanvasSyncException ex)
        {
            logger.LogWarning("Canvas save refused for playbook {PlaybookId}: {Reason}", id, ex.Reason);
            // Unverifiable = the guard could not read Dataverse (transient): 503, retryable. Otherwise the
            // playbook is a repo-deployed system playbook: 409 with a stable errorCode for the Designer.
            var unverifiable = ex.Reason == ProtectedPlaybookReason.Unverifiable;
            return Results.Problem(
                statusCode: unverifiable ? 503 : 409,
                title: unverifiable ? "Playbook could not be verified" : "Playbook is read-only",
                detail: ex.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = unverifiable ? "playbook_canvas_unverifiable" : "playbook_read_only",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
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
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        // The clone is the caller's own, by the same owner rule as a create (gate D-G6-2).
        var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(
            httpContext, httpContext.RequestAborted);
        if (ownerSystemUserId is null)
        {
            logger.LogWarning("Cloning playbook {Id} for user {UserId}: the caller's systemuserid is unresolvable; refusing", id, userId);
            return OwnerUnresolved();
        }

        try
        {
            var clonedPlaybook = await playbookService.ClonePlaybookAsync(id, ownerSystemUserId.Value, request?.NewName);
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

    /// <summary>
    /// The refusal for a create or clone whose caller has no resolvable Dataverse user: the playbook would have no person
    /// to own it. The same 403 shape OwnerOnly answers.
    /// </summary>
    private static IResult OwnerUnresolved() =>
        Results.Problem(
            statusCode: 403,
            title: "Forbidden",
            detail: "Your Dataverse user could not be resolved, so the playbook cannot be created for you");
}
