using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Endpoints.SpeAdmin;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Root route group registration for all SPE (SharePoint Embedded) Admin API endpoints.
/// Establishes the /api/spe route prefix with shared authorization requirements.
/// All child endpoint groups (environments, configs, business units, audit log) are registered
/// on this group so they inherit RequireAuthorization() and SpeAdminAuthorizationFilter.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API — MapGroup() for route organization, not controllers.
/// Follows ADR-008: Endpoint filters for authorization — SpeAdminAuthorizationFilter applied
/// at the route group level so all child endpoints inherit admin-only access.
/// </remarks>
public static class SpeAdminEndpoints
{
    /// <summary>
    /// Registers the /api/spe route group with RequireAuthorization() and the
    /// SpeAdminAuthorizationFilter, then registers all child endpoint groups on the group.
    /// </summary>
    /// <param name="app">The endpoint route builder (WebApplication).</param>
    /// <returns>The WebApplication for chaining.</returns>
    public static IEndpointRouteBuilder MapSpeAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/spe")
            .RequireAuthorization()
            .AddSpeAdminAuthorizationFilter()
            .WithTags("SpeAdmin");

        // Child endpoint groups registered on the shared /api/spe group.
        // All children inherit RequireAuthorization() + SpeAdminAuthorizationFilter (ADR-008).
        group.MapAuditLogEndpoints();   // GET /api/spe/audit  (SPE-023)

        group.MapEnvironmentEndpoints();              // SPE-010
        group.MapConfigEndpoints();                   // SPE-011
        group.MapBusinessUnitEndpoints();             // SPE-012
        ContainerEndpoints.MapContainerEndpoints(group); // SPE-013
        ContainerColumnEndpoints.MapContainerColumnEndpoints(group); // SPE-055
        ContainerCustomPropertyEndpoints.MapContainerCustomPropertyEndpoints(group); // SPE-056
        ContainerPermissionEndpoints.MapContainerPermissionEndpoints(group); // SPE-016
        // group.MapItemEndpoints();                 // SPE-017
        group.MapDashboardEndpoints();               // SPE-022
        RecycleBinEndpoints.MapRecycleBinEndpoints(group); // SPE-059
        group.MapContainerTypeEndpoints();           // SPE-050
        group.MapContainerTypeSettingsEndpoints();  // SPE-052
        group.MapContainerTypePermissionEndpoints(); // SPE-054
        group.MapSearchContainersEndpoints();        // SPE-057
        group.MapSearchItemsEndpoints();             // SPE-058
        group.MapSecurityEndpoints();               // SPE-060
        group.MapConsumingTenantEndpoints();        // SPE-082
        BulkOperationEndpoints.MapBulkOperationEndpoints(group); // SPE-083

        return app;
    }
}
