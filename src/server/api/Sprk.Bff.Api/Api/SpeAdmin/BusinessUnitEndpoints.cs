using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoint for listing Dataverse business units.
/// Provides the BU picker data source for the SPE Admin UI when scoping
/// container type configs to specific organizational units.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API — static method handler, no controllers.
/// Authorization is inherited from the /api/spe route group
/// (RequireAuthorization + SpeAdminAuthorizationFilter applied at group level, task 009).
/// </remarks>
public static class BusinessUnitEndpoints
{
    /// <summary>
    /// Registers GET /businessunits on the provided /api/spe route group.
    /// </summary>
    public static RouteGroupBuilder MapBusinessUnitEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/businessunits", ListBusinessUnitsAsync)
            .WithName("ListSpeBusinessUnits")
            .WithDescription("List all Dataverse business units for BU-scoped container type config assignment.")
            .Produces<BusinessUnitDto[]>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// Queries the Dataverse businessunit table and returns id, name, and parentBusinessUnitId.
    /// Read-only — no audit logging required for non-mutating operations.
    /// </summary>
    private static async Task<IResult> ListBusinessUnitsAsync(
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Listing Dataverse business units for SPE Admin UI");

        try
        {
            var rows = await dataverseClient.QueryAsync<BusinessUnitRow>(
                entitySetName: "businessunits",
                select: "businessunitid,name,_parentbusinessunitid_value",
                cancellationToken: ct);

            var dtos = rows
                .Select(r => new BusinessUnitDto(
                    Id: r.BusinessUnitId,
                    Name: r.Name ?? string.Empty,
                    IsRootUnit: r.ParentBusinessUnitId == null,
                    ParentBusinessUnitId: r.ParentBusinessUnitId))
                .OrderBy(d => d.Name)
                .ToArray();

            logger.LogDebug("Returned {Count} business units", dtos.Length);

            return TypedResults.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query Dataverse business units");
            return Results.Problem(
                title: "Failed to retrieve business units",
                detail: "An error occurred querying Dataverse. See server logs for details.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ─── Private OData row shape ──────────────────────────────────────────────

    /// <summary>
    /// Internal deserialization shape for the OData businessunit response.
    /// Maps OData property names to typed CLR properties.
    /// Not exposed in the API surface — mapped to BusinessUnitDto before returning.
    /// </summary>
    private sealed class BusinessUnitRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid BusinessUnitId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        /// <summary>
        /// OData lookup column for parent BU.
        /// Null for the root organization business unit.
        /// </summary>
        [JsonPropertyName("_parentbusinessunitid_value")]
        public Guid? ParentBusinessUnitId { get; init; }
    }
}
