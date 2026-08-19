// -----------------------------------------------------------------------------
// HandlersModule.cs
//
// L2 CONTROL-PLANE handler composition (task 041 — first handler wave C4).
//
// SPEC / ADR references:
//   - projects/customer-provisioning-orchestration-r1/spec.md § 5.2 + D3/D8/D12:
//       Provisioning handlers register in L2 control-plane service, NOT the
//       BFF. This module is the L2 handler DI surface.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-01 + NFR-12:
//       H0 preflight owns four quota / readiness checks; each is a distinct
//       IPreflightQuotaProbe registration bound to a script under
//       scripts/preflight/*.ps1 (task 016).
//   - ADR-010: Feature-module extension method keeps Program.cs at ~15
//              non-framework DI lines (NFR-07 god-class ratchet). One
//              AddProvisioningHandlers() call replaces N per-handler /
//              per-probe registrations.
//   - ADR-032: UNCONDITIONAL registration — the L2 REST endpoint layer
//              (wave C5) + the reconciler both need H0PreflightHandler
//              resolvable with no kill switch. If a future wave feature-
//              gates a SPECIFIC handler, apply the Null-Object kill-switch
//              pattern (P1/P2/P3) to THAT handler, never to the module
//              itself.
//
// SCOPE (task 041):
//   - Bind Preflight:{PwshExecutable, ScriptsDirectory, Timeout} options.
//   - Register the four IPreflightQuotaProbe instances (one per PS script).
//   - Register H0PreflightHandler as IProvisioningHandler (Scoped).
//   - Do NOT register any downstream handler (H0.5, H1, ...) — those tasks
//     own their own registrations.
//
// WAVE C5 UPDATE (task 103):
//   H0's keyed + concrete registrations now live alongside the other 18
//   dispatchable handlers' keyed registrations in
//   Handlers/HandlerDispatchRegistrationModule.cs (AddProvisioningHandler-
//   KeyedRegistrations, invoked below). H0's PREVIOUS non-keyed
//   `AddScoped<IProvisioningHandler, H0PreflightHandler>()` line is
//   replaced by a concrete-only `AddScoped<H0PreflightHandler>()` here (the
//   factory-forwarding keyed registration needs the concrete type resolvable)
//   — grep-verified nothing else consumed the non-keyed interface
//   registration (DS-2 §3.2).
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for the L2 provisioning-handler surface. Composes the
/// per-handler + per-probe registrations behind one <see cref="AddProvisioningHandlers"/>
/// extension method to preserve the ~15-line Program.cs budget (ADR-010).
/// </summary>
public static class HandlersModule
{
    /// <summary>Configuration section for preflight-probe options.</summary>
    public const string PreflightConfigSection = "Preflight";

    /// <summary>
    /// Registers <see cref="H0PreflightHandler"/> + its four
    /// <see cref="IPreflightQuotaProbe"/> dependencies with the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration (reads the <c>Preflight</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddProvisioningHandlers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind Preflight:{PwshExecutable, ScriptsDirectory, Timeout} options.
        // Defaults inside PreflightModuleOptions cover the operator running
        // from a workstation with pwsh on PATH + scripts/preflight/ under
        // AppContext.BaseDirectory. Production deployments override via
        // App Service settings.
        services.Configure<PreflightModuleOptions>(configuration.GetSection(PreflightConfigSection));

        // Four probe registrations — one per script under scripts/preflight/.
        // Order does not matter; H0 orchestrates all four in parallel.
        // Kept as an explicit list (rather than reflection / attribute-
        // scanning) so the compile-time diff is visible: a new preflight
        // check is a new line here + a new PreflightCheckNames const.
        RegisterPreflightProbe(
            services,
            checkName: PreflightCheckNames.AzureOpenAiTpmHeadroom,
            scriptFileName: "Test-AzureOpenAiTpmHeadroom.ps1");
        RegisterPreflightProbe(
            services,
            checkName: PreflightCheckNames.DataverseEnvCreationRate,
            scriptFileName: "Test-DataverseEnvCreationRate.ps1");
        RegisterPreflightProbe(
            services,
            checkName: PreflightCheckNames.SubscriptionVCpuQuota,
            scriptFileName: "Test-SubscriptionVCpuQuota.ps1");
        RegisterPreflightProbe(
            services,
            checkName: PreflightCheckNames.SpeCertBootstrap,
            scriptFileName: "Test-SpeCertBootstrap.ps1");

        // H0 handler — Scoped per IProvisioningHandler contract + parity
        // with IHandlerEnqueuer's Scoped registration. Concrete-only: the
        // keyed IProvisioningHandler registration (below) factory-forwards
        // to this same scoped instance (task 103).
        services.AddScoped<H0PreflightHandler>();

        // Keyed IProvisioningHandler resolution surface for all 19
        // dispatchable handlers (task 103 / DS-2 §3.2). Task 102's
        // ProvisioningHandlerDispatcher resolves by envelope HandlerId via
        // GetKeyedService<IProvisioningHandler>(id) — see
        // Handlers/HandlerDispatchRegistrationModule.cs for the full list +
        // rationale for consolidating all 19 lines in one file.
        services.AddProvisioningHandlerKeyedRegistrations();

        return services;
    }

    private static void RegisterPreflightProbe(
        IServiceCollection services,
        string checkName,
        string scriptFileName)
    {
        services.AddScoped<IPreflightQuotaProbe>(sp => new PowerShellPreflightProbe(
            checkName: checkName,
            scriptFileName: scriptFileName,
            options: sp.GetRequiredService<IOptions<PreflightModuleOptions>>(),
            logger: sp.GetRequiredService<ILogger<PowerShellPreflightProbe>>()));
    }
}
