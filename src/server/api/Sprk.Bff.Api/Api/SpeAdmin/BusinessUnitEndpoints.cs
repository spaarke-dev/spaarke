using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoint for listing Dataverse business units.
/// Provides the BU picker data source for the SPE Admin UI when scoping
/// container type configs to specific organizational units.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-001: Minimal API — static method handler, no controllers.
/// Authorization is inherited from the /api/spe route group
/// (RequireAuthorization + SpeAdminAuthorizationFilter applied at group level, task 009).
/// </para>
/// <para>
/// <b>Projected onto the caller's reach</b> (unified-access-control-r2 task 165, follow-up f2). The list used to be every
/// business unit in the environment, so a leaf (customer) admin read every other customer's unit name. It now holds
/// the caller's own unit and its descendants — exactly the units the caller may assign a config to
/// (<c>POST/PUT /api/spe/configs</c> refuse any other) — derived from the caller's token, never a parameter
/// (the <c>ListConfigsAsync</c> precedent). A platform operator (root unit) still sees every unit. A caller who cannot
/// be resolved to a Dataverse user sees none; a hierarchy read fault is the shared 503, never the unprojected list.
/// </para>
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
            .WithDescription("List the Dataverse business units the caller administers (their own unit and its descendants) for BU-scoped container type config assignment.")
            .Produces<BusinessUnitDto[]>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>
    /// Queries the Dataverse businessunit table and returns id, name, and parentBusinessUnitId.
    /// Read-only — no audit logging required for non-mutating operations.
    /// </summary>
    private static async Task<IResult> ListBusinessUnitsAsync(
        DataverseWebApiClient dataverseClient,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        logger.LogInformation("Listing Dataverse business units for SPE Admin UI");

        // The caller's reach FIRST, from the token — fail closed: a fault is the 503, never the whole table.
        IReadOnlyCollection<Guid> accessible;
        try
        {
            accessible = await tenantScope.GetAccessibleBusinessUnitsAsync(context.User, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not read the caller's business-unit scope — refusing the business-unit list (unverifiable).");
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(context.TraceIdentifier);
        }

        if (accessible.Count == 0)
        {
            // An unresolvable caller administers no unit.
            return TypedResults.Ok(Array.Empty<BusinessUnitDto>());
        }

        try
        {
            var rows = await dataverseClient.QueryAsync<BusinessUnitRow>(
                entitySetName: "businessunits",
                select: "businessunitid,name,_parentbusinessunitid_value",
                cancellationToken: ct);

            var dtos = rows
                .Where(r => accessible.Contains(r.BusinessUnitId))
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
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            logger.LogError(ex, "Failed to query Dataverse business units");
            return Results.Problem(
                title: "Failed to retrieve business units",
                detail: ProblemDetailsHelper.Explain("An error occurred querying Dataverse. See server logs for details.", ex),
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
