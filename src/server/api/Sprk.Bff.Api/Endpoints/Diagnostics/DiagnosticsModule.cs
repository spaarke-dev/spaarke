using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Bff.Api.Endpoints.Diagnostics;

/// <summary>
/// DI composition for the BFF Diagnostics surface (G-8 Batch 6 — customer-
/// provisioning-orchestration-r1, closes fix #18). Registers:
/// <list type="bullet">
/// <item><see cref="ITenantContainerResolver"/> → <see cref="OptionsTenantContainerResolver"/>
/// (singleton — stateless over <c>IOptionsMonitor</c> bags that are themselves singletons).</item>
/// </list>
///
/// <para>
/// Registered UNCONDITIONALLY per ADR-032 / bff-extensions.md §F.1: the
/// <c>/api/diagnostics/tenant-container-resolver</c> endpoint maps unconditionally in
/// <c>EndpointMappingExtensions.MapDomainEndpoints</c>, so its sole ctor dependency MUST
/// be registered unconditionally too (asymmetric-registration anti-pattern guard).
/// Transitive deps (<c>IOptionsMonitor&lt;GraphOptions&gt;</c>,
/// <c>IOptionsMonitor&lt;SharePointEmbeddedOptions&gt;</c>) are bound unconditionally in
/// <c>ConfigurationModule</c> — the §F.1 transitive scan holds.
/// </para>
///
/// <para>
/// Placement Justification (CLAUDE.md §10): new top-level Endpoints/Diagnostics/ folder
/// mirroring Endpoints/Onboarding/ (same project, task 042). One interface + one impl +
/// one endpoint; ONE DI registration; zero new packages; zero AI-internal types (ADR-013).
/// </para>
/// </summary>
public static class DiagnosticsModule
{
    /// <summary>Registers the Diagnostics surface with the DI container.</summary>
    public static IServiceCollection AddDiagnosticsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITenantContainerResolver, OptionsTenantContainerResolver>();

        return services;
    }
}
