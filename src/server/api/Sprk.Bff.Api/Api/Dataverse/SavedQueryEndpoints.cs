using System.Security.Claims;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Api.Dataverse;

/// <summary>
/// Cached projection endpoints for Dataverse <c>savedquery</c> rows (FR-BFF-01, FR-BFF-02).
/// </summary>
/// <remarks>
/// <para>
/// Two endpoints under <c>/api/dataverse</c>:
/// </para>
/// <list type="bullet">
///   <item><description><c>GET /api/dataverse/savedquery/{savedQueryId}</c> — returns the full saved-query payload (entity + FetchXML + LayoutXML + name).</description></item>
///   <item><description><c>GET /api/dataverse/savedqueries/{entityLogicalName}</c> — returns the user-owned saved queries for a given entity.</description></item>
/// </list>
/// <para>
/// Both endpoints are protected by ADR-008 endpoint-filter authorization:
/// </para>
/// <list type="bullet">
///   <item><description>The list endpoint uses <see cref="DataverseAuthorizationFilter"/> with <see cref="EntitySource.FromRouteValue"/>.</description></item>
///   <item><description>The by-id endpoint defers privilege checking to the handler (since the entity is not known until the savedquery is loaded — see <c>011-deviations.md §1</c>). The handler invokes <see cref="IDataversePrivilegeChecker"/> directly after the cached payload is hydrated.</description></item>
/// </list>
/// <para>
/// Per ADR-019, all errors return <see cref="Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult"/>.
/// Per FR-BFF-01/02, payloads are cached for 1 hour via <see cref="SavedQueryService"/>.
/// </para>
/// </remarks>
public static class SavedQueryEndpoints
{
    /// <summary>
    /// Maps the savedquery endpoints onto the application's endpoint route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapSavedQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .RequireAuthorization()
            .WithTags("Dataverse Projection");

        // FR-BFF-01: GET /api/dataverse/savedquery/{savedQueryId}
        // Handler-side privilege check (entity is unknown until savedquery is loaded).
        group.MapGet("/savedquery/{savedQueryId:guid}", GetSavedQueryByIdAsync)
            .RequireRateLimiting("standard")
            .WithName("GetDataverseSavedQuery")
            .WithSummary("Get a saved query payload by id")
            .WithDescription("Returns the entity, FetchXML, LayoutXML, and name of a saved query. Privilege-checked against the saved query's returnedtypecode entity.")
            .Produces<SavedQueryDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // FR-BFF-02: GET /api/dataverse/savedqueries/{entityLogicalName}
        // Filter-side privilege check via FromRouteValue.
        group.MapGet("/savedqueries/{entityLogicalName}", GetSavedQueriesForEntityAsync)
            .AddDataverseAuthorizationFilter(EntitySource.FromRouteValue, routeKey: "entityLogicalName")
            .RequireRateLimiting("standard")
            .WithName("ListDataverseSavedQueriesForEntity")
            .WithSummary("List saved queries for an entity")
            .WithDescription("Returns user-owned (querytype=0) active saved queries for the specified entity logical name.")
            .Produces<IReadOnlyList<SavedQuerySummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Handler for <c>GET /api/dataverse/savedquery/{savedQueryId}</c>.
    /// Performs handler-side privilege check on the saved query's returnedtypecode entity.
    /// </summary>
    private static async Task<IResult> GetSavedQueryByIdAsync(
        Guid savedQueryId,
        SavedQueryService savedQueryService,
        IDataversePrivilegeChecker privilegeChecker,
        HttpContext httpContext,
        ILogger<SavedQueryService> logger,
        CancellationToken ct)
    {
        // Step 1: Identity.
        var userOidStr = httpContext.User.FindFirst("oid")?.Value
                         ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                         ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userOidStr, out var userOid))
        {
            return Problem(401, "Unauthorized", "User identity not found in authentication token",
                "DV_NO_USER_IDENTITY", httpContext);
        }

        // Step 2: Load the savedquery (cached).
        SavedQueryDto? dto;
        try
        {
            dto = await savedQueryService.GetSavedQueryAsync(savedQueryId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load savedquery {SavedQueryId}", savedQueryId);
            return Problem(500, "Internal Server Error", "Failed to load saved query",
                "DV_INTERNAL_ERROR", httpContext);
        }

        if (dto is null)
        {
            return Problem(404, "Not Found", "Saved query not found",
                "DV_SAVEDQUERY_NOT_FOUND", httpContext);
        }

        if (string.IsNullOrWhiteSpace(dto.EntityName))
        {
            logger.LogWarning(
                "Saved query {SavedQueryId} has no entityName (returnedtypecode); failing closed",
                savedQueryId);
            return Problem(404, "Not Found", "Saved query has no associated entity",
                "DV_SAVEDQUERY_NOT_FOUND", httpContext);
        }

        // Step 3: Privilege check against the saved query's entity.
        var allowed = await privilegeChecker.HasReadPrivilegeAsync(userOid, dto.EntityName, ct);
        if (!allowed)
        {
            logger.LogWarning(
                "Dataverse authorization denied (savedquery handler): user={UserOid}, deniedEntity={Entity}, savedQueryId={SavedQueryId}, correlationId={CorrelationId}",
                userOid, dto.EntityName, savedQueryId, httpContext.TraceIdentifier);
            return Problem(403, "Forbidden", $"Read privilege denied on entity '{dto.EntityName}'",
                "DV_PRIVILEGE_DENIED", httpContext);
        }

        logger.LogInformation(
            "Dataverse authorization granted (savedquery handler): user={UserOid}, entity={Entity}, savedQueryId={SavedQueryId}",
            userOid, dto.EntityName, savedQueryId);

        return Results.Ok(dto);
    }

    /// <summary>
    /// Handler for <c>GET /api/dataverse/savedqueries/{entityLogicalName}</c>.
    /// Privilege check is handled by the <see cref="DataverseAuthorizationFilter"/>.
    /// </summary>
    private static async Task<IResult> GetSavedQueriesForEntityAsync(
        string entityLogicalName,
        SavedQueryService savedQueryService,
        HttpContext httpContext,
        ILogger<SavedQueryService> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Problem(400, "Bad Request", "entityLogicalName is required",
                "DV_NO_TARGET_ENTITY", httpContext);
        }

        try
        {
            var summaries = await savedQueryService.GetSavedQueriesForEntityAsync(entityLogicalName, ct);
            return Results.Ok(summaries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list savedqueries for entity {Entity}", entityLogicalName);
            return Problem(500, "Internal Server Error", "Failed to list saved queries",
                "DV_INTERNAL_ERROR", httpContext);
        }
    }

    private static IResult Problem(int status, string title, string detail, string errorCode, HttpContext httpContext) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = httpContext.TraceIdentifier
            });
}
