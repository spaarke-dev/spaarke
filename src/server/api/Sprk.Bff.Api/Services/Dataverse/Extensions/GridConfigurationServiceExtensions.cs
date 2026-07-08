namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registration extension for the grid-configuration projection service
/// (DEF-002 of <c>spaarke-dataset-grid-framework-r2</c>).
/// </summary>
/// <remarks>
/// <para>
/// Follows the ADR-010 feature-module DI pattern used by
/// <see cref="DataverseServiceExtensions.AddDataverseSavedQueryServices"/> et al.
/// </para>
/// <para>
/// Must be called AFTER <c>AddDataverseSavedQueryServices</c> so the shared privilege
/// checker (<c>IDataversePrivilegeChecker → UserPrivilegeChecker</c>) is already registered.
/// The endpoint's <see cref="Sprk.Bff.Api.Api.Dataverse.EntitySource.FromRouteValue"/> filter
/// reuses that checker; no additional privilege infra is registered here.
/// </para>
/// </remarks>
public static class GridConfigurationServiceExtensions
{
    /// <summary>
    /// Registers <see cref="GridConfigurationService"/> as scoped.
    /// </summary>
    public static IServiceCollection AddDataverseGridConfigurationServices(this IServiceCollection services)
    {
        services.AddScoped<GridConfigurationService>();
        return services;
    }
}
