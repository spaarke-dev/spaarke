using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registration for the Spaarke DataGrid Framework R1 metadata-projection services (FR-BFF-03).
/// </summary>
/// <remarks>
/// <para>
/// Registers <see cref="MetadataService"/> as scoped. Scoped (vs. singleton) is chosen because the
/// service depends on <c>IDataverseService</c> which is registered scoped in <c>DataverseModule</c>
/// to align with per-request <c>ServiceClient</c> lifetime semantics. The service has no per-request
/// mutable state beyond the injected dependencies.
/// </para>
/// <para>
/// Main session wires this from <c>Program.cs</c> via
/// <c>builder.Services.AddDataverseMetadataServices();</c> after the BFF wave completes.
/// </para>
/// </remarks>
public static class MetadataServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="MetadataService"/> required by the Spaarke DataGrid Framework R1
    /// metadata endpoint (FR-BFF-03 — <c>GET /api/dataverse/metadata/{entityLogicalName}</c>).
    /// </summary>
    public static IServiceCollection AddDataverseMetadataServices(this IServiceCollection services)
    {
        services.AddScoped<MetadataService>();
        return services;
    }
}
