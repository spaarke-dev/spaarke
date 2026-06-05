using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.Dataverse;

/// <summary>
/// Maps the <c>GET /api/dataverse/metadata/{entityLogicalName}</c> endpoint (FR-BFF-03 of the
/// Spaarke DataGrid Framework R1). Returns the projected <see cref="Services.Dataverse.Models.EntityMetadataDto"/>
/// shape (&lt;50KB per entity) used by filter chips, column rendering, and filter-derivation in the framework.
/// </summary>
/// <remarks>
/// <para>
/// Authorization: <c>DataverseAuthorizationFilter</c> (task 011) is applied with
/// <see cref="EntitySource.FromRouteValue"/> using the <c>entityLogicalName</c> route key. The filter
/// extracts the caller's <c>oid</c> claim, checks Read privilege via the cached
/// <c>IDataversePrivilegeChecker</c>, and returns 403 ProblemDetails with
/// <c>errorCode=DV_PRIVILEGE_DENIED</c> if the caller lacks Read privilege.
/// </para>
/// <para>
/// Caching: <see cref="MetadataService"/> caches the projected DTO for 6 hours in the shared Redis
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>. The 6h TTL matches the
/// FR-BFF-03 specification — metadata only changes on solution import, so longer TTLs are safe.
/// </para>
/// <para>
/// Error handling: ProblemDetails per ADR-019. <c>InvalidOperationException</c> from the service
/// (entity not in metadata) returns 404; other failures return 500 with the trace identifier in the
/// <c>correlationId</c> extension.
/// </para>
/// </remarks>
public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .WithTags("Dataverse")
            .RequireAuthorization();

        group.MapGet("/metadata/{entityLogicalName}", GetEntityMetadataAsync)
            .WithName("GetDataverseEntityMetadata")
            .WithSummary("Returns projected entity metadata for the Spaarke DataGrid Framework.")
            .WithDescription("FR-BFF-03. 6h cache. Payload <50KB per entity. Read-privilege checked by DataverseAuthorizationFilter.")
            .AddDataverseAuthorizationFilter(EntitySource.FromRouteValue);

        return app;
    }

    private static async Task<IResult> GetEntityMetadataAsync(
        string entityLogicalName,
        MetadataService metadataService,
        ILogger<MetadataService> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Entity logical name is required in the route.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_METADATA_MISSING_ENTITY",
                    ["correlationId"] = correlationId
                });
        }

        try
        {
            var dto = await metadataService.GetMetadataAsync(entityLogicalName, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Retrieved Dataverse entity metadata for {EntityLogicalName} ({AttributeCount} attributes)",
                entityLogicalName, dto.Attributes.Count);

            return Results.Ok(dto);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid entity logical name {EntityLogicalName}", entityLogicalName);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_METADATA_INVALID_ENTITY",
                    ["correlationId"] = correlationId
                });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex,
                "Dataverse entity metadata not found for {EntityLogicalName}", entityLogicalName);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Entity '{entityLogicalName}' was not found in Dataverse metadata.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_METADATA_ENTITY_NOT_FOUND",
                    ["correlationId"] = correlationId
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected; let the framework handle it.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error retrieving Dataverse entity metadata for {EntityLogicalName}",
                entityLogicalName);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving entity metadata.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_METADATA_INTERNAL_ERROR",
                    ["correlationId"] = correlationId
                });
        }
    }
}
