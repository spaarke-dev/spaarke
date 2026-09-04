using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
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
        services.AddCoreAncestorResolver();
        return services;
    }

    /// <summary>
    /// Registers <see cref="CoreAncestorResolver"/> — the FR-26 core-ancestor derivation every server-side
    /// writer of a <c>sprk_regarding*</c> lookup on a child record routes through (task 052).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Singleton, with a scope-bridged metadata probe.</b> The resolver's consumers span every lifetime:
    /// <c>CommunicationService</c> and <c>IncomingAssociationResolver</c> are singletons, <c>OfficeService</c>
    /// and the tool handlers are scoped, and <c>TaskActionCore</c> / <c>TodoRegardingBuilder</c> are
    /// constructed inline. Only a singleton can serve all of them. But <see cref="MetadataService"/> is
    /// SCOPED, so capturing one here would be a captive dependency — instead the
    /// <see cref="CoreAncestorResolver.EntityColumnProbe"/> opens a scope per call, the same bridge
    /// <c>UpdateRecordActionCore</c> already uses for the same service. The probe is cheap: metadata is
    /// Redis-cached for 6 hours, so the steady-state cost is a cache read, not a Dataverse round-trip.
    /// </para>
    /// <para>
    /// <b>Idempotent + registered from every entry point.</b> <c>TryAddSingleton</c> and a call from both
    /// <see cref="AddDataverseMetadataServices"/> and <c>AddToolFramework</c> — because
    /// <c>EmailDraftToolHandler</c> is registered by the tool-framework assembly scan and would otherwise
    /// fail to resolve wherever the tool framework is composed without this module. That asymmetry is the
    /// CLAUDE.md §10 F.1 anti-pattern; the same fix is applied here as for <c>TimeProvider</c>.
    /// </para>
    /// <para>
    /// ADR-010: a concrete registered once, no interface — the only test seam
    /// (<see cref="CoreAncestorResolver.EntityColumnProbe"/>) is a delegate.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCoreAncestorResolver(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new CoreAncestorResolver(
            sp.GetRequiredService<IGenericEntityService>(),
            async (entityLogicalName, ct) =>
            {
                using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
                var metadata = scope.ServiceProvider.GetRequiredService<MetadataService>();
                return await CoreAncestorResolver.FromMetadata(metadata)(entityLogicalName, ct)
                    .ConfigureAwait(false);
            },
            sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

        return services;
    }
}
