using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
        // Create, clone, share and unshare are for playbook administrators: the caller's own table privilege, decided
        // before anything is read or written (owner decision 2026-10-30; the services write app-only).
        group.MapPost("/", CreatePlaybook)
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.CreatePlaybookPrivilege)
            .WithName("CreatePlaybook")
            .WithSummary("Create a new analysis playbook")
            .WithDescription("Creates a new playbook with specified actions, skills, knowledge, and tools.")
            .Produces<PlaybookResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesValidationProblem();

        // PUT /api/ai/playbooks/{id} - Update existing playbook
        // Write routes: the caller's own Write privilege first, then ownership. An update that changes IsPublic also
        // needs Share (the stored value is read only after the Write gate).
        group.MapPut("/{id:guid}", UpdatePlaybook)
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.WritePlaybookPrivilege)
            .AddPlaybookOwnerAuthorizationFilter()
            .AddPlaybookPublicFlagShareFilter()
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
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.SharePlaybookPrivilege)
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
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.SharePlaybookPrivilege)
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
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.WritePlaybookPrivilege)
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
            .AddPlaybookTablePrivilegeFilter(PlaybookAuthorizationFilter.CreatePlaybookPrivilege)
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
    /// List playbooks owned by the current user. The rules are <see cref="ListPlaybooksForCallerAsync"/>'s (spaarkeai-mcp-bff-api-r2
    /// task 024); this handler only maps its answer to the route's responses, which did not change.
    /// </summary>
    private static Task<IResult> ListUserPlaybooks(
        HttpContext httpContext,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        Guid? outputTypeId = null,
        string sortBy = "modifiedon",
        bool sortDescending = true) =>
        ListForRouteAsync(httpContext, PlaybookListScope.Owned, page, pageSize, nameFilter, outputTypeId, sortBy, sortDescending);

    /// <summary>
    /// List public playbooks shared by all users. The rules are <see cref="ListPlaybooksForCallerAsync"/>'s (spaarkeai-mcp-bff-api-r2
    /// task 024); this handler only maps its answer to the route's responses, which did not change.
    /// </summary>
    private static Task<IResult> ListPublicPlaybooks(
        HttpContext httpContext,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        Guid? outputTypeId = null,
        string sortBy = "modifiedon",
        bool sortDescending = true) =>
        ListForRouteAsync(httpContext, PlaybookListScope.Public, page, pageSize, nameFilter, outputTypeId, sortBy, sortDescending);

    /// <summary>
    /// The playbook lists of <c>GET /api/ai/playbooks</c> (<see cref="PlaybookListScope.Owned"/>) and <c>GET /api/ai/playbooks/public</c>
    /// (<see cref="PlaybookListScope.Public"/>): THE rules of both routes, for the routes and for a host that is not them (the MCP tool
    /// <c>spaarke_list_playbooks</c>, spaarkeai-mcp-bff-api-r2 task 024, spec FR-04, decision D-024-extract). It reads the caller from
    /// <paramref name="httpContext"/> itself; a caller never passes an identity or a token.
    /// <list type="bullet">
    /// <item><b>No caller.</b> A null context, a principal with no authenticated identity, or no request services is
    /// <see cref="PlaybookListOutcome.UserContextRequired"/> (401) before anything is read. For <see cref="PlaybookListScope.Owned"/> an Entra
    /// <c>oid</c> that is missing or not a GUID is the same refusal (the owned route's 401 "User identity not found"); the public list
    /// never read the <c>oid</c> and still does not.</item>
    /// <item><b>Owned.</b> The owner filter is the caller's Dataverse <c>systemuserid</c>
    /// (<see cref="PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync"/>, WhoAmI on the caller's OBO token), never the Entra
    /// <c>oid</c>. When it cannot be resolved the answer is <see cref="PlaybookListOutcome.OwnerUnresolved"/> with an EMPTY page and no
    /// query (fail closed: no other owner's rows are listed in its place; the route answers that page with 200).</item>
    /// <item><b>Public.</b> <see cref="PlaybookQueryParameters.PublicOnly"/>.</item>
    /// <item><b>Paging and sort.</b> Exactly the route's query (<see cref="PlaybookQueryParameters"/>; the service clamps the page size to
    /// 1-100).</item>
    /// <item><b>A fault</b> in the lookup or the service is <see cref="PlaybookListOutcome.Failed"/> (500), logged as the routes always
    /// logged it. Only a cancellation of <paramref name="cancellationToken"/> propagates.</item>
    /// </list>
    /// <para>Pass the request's own context (<c>IHttpContextAccessor.HttpContext</c>) only: the principal and the bearer token in its
    /// <c>Authorization</c> header are read separately and must come from the same validated token. Never falls back to app-only.</para>
    /// </summary>
    public static async Task<PlaybookListResult> ListPlaybooksForCallerAsync(
        HttpContext? httpContext,
        PlaybookListScope scope,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        Guid? outputTypeId = null,
        string sortBy = "modifiedon",
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        if (httpContext?.User is not { } user || !user.Identities.Any(i => i.IsAuthenticated) || httpContext.RequestServices is not { } services)
        {
            return PlaybookListResult.UserContextRequired;
        }

        var userId = Guid.Empty;
        if (scope == PlaybookListScope.Owned)
        {
            // Get user ID from claims (the owned route's identity test, unchanged).
            var userIdClaim = CallerResolution.ResolveObjectId(user);
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
            {
                return PlaybookListResult.UserContextRequired;
            }
        }

        var logger = (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("PlaybookEndpoints");

        var query = new PlaybookQueryParameters
        {
            Page = page,
            PageSize = pageSize,
            NameFilter = nameFilter,
            OutputTypeId = outputTypeId,
            PublicOnly = scope == PlaybookListScope.Public,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var playbookService = services.GetRequiredService<IPlaybookService>();

            if (scope == PlaybookListScope.Public)
            {
                var publicPage = await playbookService.ListPublicPlaybooksAsync(query, cancellationToken);
                logger.LogDebug("Listed {Count} public playbooks", publicPage.Items.Length);
                return PlaybookListResult.Listed(publicPage);
            }

            // Owner round 12 item 6 (task 164): _ownerid_value holds a Dataverse systemuserid, never the Entra
            // oid, so the filter is the CALLER's systemuserid (WhoAmI over OBO). Unresolvable → an empty page
            // (fail closed: no other owner's rows can be listed in its place).
            var ownerSystemUserId = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, cancellationToken);
            if (ownerSystemUserId is null)
            {
                logger.LogWarning("Listing owned playbooks for user {UserId}: the caller's systemuserid is unresolvable; returning an empty page", userId);
                return PlaybookListResult.OwnerUnresolved(new PlaybookListResponse
                {
                    Items = [],
                    TotalCount = 0,
                    Page = query.Page,
                    PageSize = query.GetNormalizedPageSize()
                });
            }

            var owned = await playbookService.ListUserPlaybooksAsync(ownerSystemUserId.Value, query, cancellationToken);
            logger.LogDebug("Listed {Count} playbooks for user {UserId}", owned.Items.Length, userId);
            return PlaybookListResult.Listed(owned, ownerSystemUserId.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (scope == PlaybookListScope.Public)
            {
                logger.LogError(ex, "Failed to list public playbooks");
            }
            else
            {
                logger.LogError(ex, "Failed to list playbooks for user {UserId}", userId);
            }

            return PlaybookListResult.Failed;
        }
    }

    /// <summary>
    /// One list route: <see cref="ListPlaybooksForCallerAsync"/> with the request's <c>RequestAborted</c>, mapped to the responses the two
    /// handlers always gave (200 with the page, 401 "User identity not found", 500 with the route's own detail). A request the client
    /// aborted is 499, logged at Debug; a cancellation the request did not cause keeps the handlers' 500.
    /// </summary>
    private static async Task<IResult> ListForRouteAsync(
        HttpContext httpContext,
        PlaybookListScope scope,
        int page,
        int pageSize,
        string? nameFilter,
        Guid? outputTypeId,
        string sortBy,
        bool sortDescending)
    {
        var failedDetail = scope == PlaybookListScope.Public ? "Failed to list public playbooks" : "Failed to list playbooks";

        PlaybookListResult result;
        try
        {
            result = await ListPlaybooksForCallerAsync(
                httpContext, scope, page, pageSize, nameFilter, outputTypeId, sortBy, sortDescending, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away (the method is given RequestAborted and lets that cancellation propagate): not a fault. 499 (client
            // closed request), logged at Debug without a stack, as ChatDocumentEndpoints and VisualizationEndpoints answer an abandoned request.
            httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("PlaybookEndpoints")
                .LogDebug("Listing playbooks abandoned by the client; answered 499. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.StatusCode(499);
        }
        catch (OperationCanceledException ex)
        {
            // A cancellation the request did not cause (for example an HttpClient timeout): a fault, the 500 the handlers always gave it.
            httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("PlaybookEndpoints")
                .LogError(ex, "Listing playbooks was cancelled");
            return ListFailed(failedDetail);
        }

        return result.Outcome switch
        {
            PlaybookListOutcome.Listed or PlaybookListOutcome.OwnerUnresolved => Results.Ok(result.Page),
            PlaybookListOutcome.Failed => ListFailed(failedDetail),
            // UserContextRequired. Only the owned route can reach it (RequireAuthorization admits only a signed-in caller); its 401.
            _ => Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found"),
        };
    }

    private static IResult ListFailed(string detail) =>
        Results.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: detail);

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

/// <summary>Which list <see cref="PlaybookEndpoints.ListPlaybooksForCallerAsync"/> reads.</summary>
public enum PlaybookListScope
{
    /// <summary>The caller's own playbooks: <c>GET /api/ai/playbooks</c>.</summary>
    Owned = 0,

    /// <summary>Public playbooks: <c>GET /api/ai/playbooks/public</c>.</summary>
    Public = 1,
}

/// <summary>What <see cref="PlaybookEndpoints.ListPlaybooksForCallerAsync"/> answered.</summary>
/// <remarks><see cref="UserContextRequired"/> is the zero value, so <c>default</c> is never a list.</remarks>
public enum PlaybookListOutcome
{
    /// <summary>No request context or no signed-in caller; for the owned list, no Entra <c>oid</c> GUID. Nothing was read (REST: 401).</summary>
    UserContextRequired = 0,

    /// <summary>The service's page (REST: 200 with it).</summary>
    Listed = 1,

    /// <summary>Owned list only: the caller's <c>systemuserid</c> could not be resolved; an empty page, nothing queried (REST: 200 with it).</summary>
    OwnerUnresolved = 2,

    /// <summary>The lookup or the service faulted (REST: 500).</summary>
    Failed = 3,
}

/// <summary>
/// The answer of <see cref="PlaybookEndpoints.ListPlaybooksForCallerAsync"/>: an outcome, the HTTP status the route answers for it, the
/// machine-readable <c>reasonCode</c> of a refusal and, when listed, the page. It carries no <see cref="IResult"/> and no ProblemDetails,
/// so a non-HTTP host (the MCP tool <c>spaarke_list_playbooks</c>, spaarkeai-mcp-bff-api-r2 task 024) renders it in its own error shape
/// (ADR-019). The page is the route's own body type (<c>Models/Ai</c>, not an AI-internal type; ADR-013).
/// </summary>
public sealed record PlaybookListResult
{
    /// <summary>A list failure's <c>reasonCode</c>. The route's 500 body carries no code; this one is new with task 024 (ADR-019).</summary>
    public const string ListFailedReasonCode = "playbook.list-failed";

    private PlaybookListResult(
        PlaybookListOutcome outcome, int status, string? reasonCode, PlaybookListResponse? page, Guid? ownerSystemUserId = null)
    {
        Outcome = outcome;
        Status = status;
        ReasonCode = reasonCode;
        Page = page;
        OwnerSystemUserId = ownerSystemUserId;
    }

    /// <summary>No request context or no signed-in caller (ADR-028 A10): <see cref="DataverseUserClientErrorCodes.UserContextRequired"/>, 401.</summary>
    public static readonly PlaybookListResult UserContextRequired =
        new(PlaybookListOutcome.UserContextRequired, StatusCodes.Status401Unauthorized, DataverseUserClientErrorCodes.UserContextRequired, null);

    /// <summary>A fault: <see cref="ListFailedReasonCode"/>, 500.</summary>
    public static readonly PlaybookListResult Failed =
        new(PlaybookListOutcome.Failed, StatusCodes.Status500InternalServerError, ListFailedReasonCode, null);

    public PlaybookListOutcome Outcome { get; }

    /// <summary>The HTTP status the route answers for this outcome: 200, 401 or 500.</summary>
    public int Status { get; }

    /// <summary><c>null</c> when a page was returned; otherwise the refusal's or failure's code.</summary>
    public string? ReasonCode { get; }

    /// <summary>The page the route returns (status 200): the service's, or the empty page of <see cref="PlaybookListOutcome.OwnerUnresolved"/>.</summary>
    public PlaybookListResponse? Page { get; }

    /// <summary>
    /// The caller's resolved Dataverse <c>systemuserid</c>: set only on a <see cref="PlaybookListOutcome.Listed"/> OWNED list, <c>null</c>
    /// otherwise. A host that merges the owned and public lists uses it to tell which public rows the caller owns. REST serializes only
    /// <see cref="Page"/>, so the routes' bytes do not carry it.
    /// </summary>
    public Guid? OwnerSystemUserId { get; }

    /// <summary>The page's rows; empty when there is no page.</summary>
    public IReadOnlyList<PlaybookSummary> Items => Page?.Items ?? [];

    internal static PlaybookListResult Listed(PlaybookListResponse page, Guid? ownerSystemUserId = null) =>
        new(PlaybookListOutcome.Listed, StatusCodes.Status200OK, null, page, ownerSystemUserId);

    internal static PlaybookListResult OwnerUnresolved(PlaybookListResponse emptyPage) =>
        new(PlaybookListOutcome.OwnerUnresolved, StatusCodes.Status200OK, null, emptyPage);
}
