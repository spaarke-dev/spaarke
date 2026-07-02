using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Api.Dataverse;

/// <summary>
/// Cached projection endpoint for Dataverse <c>sprk_gridconfiguration</c> rows (DEF-002 of
/// <c>spaarke-dataset-grid-framework-r2</c>).
/// </summary>
/// <remarks>
/// <para>
/// One endpoint under <c>/api/dataverse</c>:
/// </para>
/// <list type="bullet">
///   <item><description><c>GET /api/dataverse/gridconfigurations/{entityLogicalName}</c> — returns
///   active grid-configuration rows targeting the given entity.</description></item>
/// </list>
/// <para>
/// Protected by ADR-008 endpoint-filter authorization — the <see cref="DataverseAuthorizationFilter"/>
/// runs with <see cref="EntitySource.FromRouteValue"/> against the section's target entity, so a
/// caller who cannot read <c>sprk_matter</c> also cannot enumerate its grid configurations. This
/// mirrors the <see cref="SavedQueryEndpoints"/> list-endpoint contract.
/// </para>
/// <para>
/// Per ADR-019, all errors return <see cref="Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult"/>.
/// Cached for 1 hour via <see cref="GridConfigurationService"/>.
/// </para>
/// </remarks>
public static class GridConfigurationEndpoints
{
    /// <summary>
    /// Maps the grid-configuration endpoints onto the application's endpoint route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapGridConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .RequireAuthorization()
            .WithTags("Dataverse Projection");

        // GET /api/dataverse/gridconfigurations/{entityLogicalName}
        // Filter-side privilege check via FromRouteValue.
        group.MapGet("/gridconfigurations/{entityLogicalName}", GetGridConfigurationsForEntityAsync)
            .AddDataverseAuthorizationFilter(EntitySource.FromRouteValue, routeKey: "entityLogicalName")
            .RequireRateLimiting("dataverse-query") // Reuses same policy as SavedQueryEndpoints (task 016 D-016-03).
            .WithName("ListDataverseGridConfigurationsForEntity")
            .WithSummary("List grid configurations for an entity")
            .WithDescription("Returns active sprk_gridconfiguration rows for the specified entity logical name, ordered by sort order. Used by WorkspaceLayoutWizard's per-instance configId picker (DEF-002).")
            .Produces<IReadOnlyList<GridConfigurationSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Handler for <c>GET /api/dataverse/gridconfigurations/{entityLogicalName}</c>.
    /// Privilege check is handled by the <see cref="DataverseAuthorizationFilter"/>.
    /// </summary>
    private static async Task<IResult> GetGridConfigurationsForEntityAsync(
        string entityLogicalName,
        GridConfigurationService gridConfigurationService,
        HttpContext httpContext,
        ILogger<GridConfigurationService> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Problem(400, "Bad Request", "entityLogicalName is required",
                "DV_NO_TARGET_ENTITY", httpContext);
        }

        try
        {
            var summaries = await gridConfigurationService.GetForEntityAsync(entityLogicalName, ct);
            return Results.Ok(summaries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list grid configurations for entity {Entity}", entityLogicalName);
            return Problem(500, "Internal Server Error", "Failed to list grid configurations",
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
