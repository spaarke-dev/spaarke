// -----------------------------------------------------------------------------
// E2EAcceptanceModule.cs
//
// L2 CONTROL-PLANE DI composition for the H13 E2E acceptance-gate handler +
// its 6 collaborator seams (task 055, wave C4 Batch 4E).
//
// SCOPE:
//   - Bind E2EAcceptance:{PwshExecutable, AzCliExecutable, ValidateDeployed
//     EnvironmentScriptPath, NamingConformanceScriptPath, various timeouts,
//     cost envelope thresholds, CostDriftFailsRun, TargetSlotName,
//     HonorRegistryStatusReadyShortCircuit} options.
//   - Register the 6 collaborator seams (IE2EValidationRunner,
//     IE2ETrapVerifier, IE2EInvariantVerifier, INamingConformanceChecker,
//     ICostEnvelopeChecker, IRegistrySetupStatusUpdater) + the H13 handler
//     itself as Scoped.
//
// UNCONDITIONAL REGISTRATION (ADR-032): every registration below is
// UNCONDITIONAL — no feature-gate branch.
//
// PATTERN PARITY:
//   Mirrors AppConfigSeed/AppConfigSeedModule.cs (single AddH{X}...() extension
//   method) so Program.cs additions stay to ONE new line (NFR-07 god-class
//   ratchet + ADR-010 DI minimalism).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H13 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule). H13 uses
//   IProvisioningRunRepository (task 037) + 6 dedicated seams + reuses
//   IDataverseEnvironmentRegistryClient (task 042 H0.5 seam) for the
//   idempotency-short-circuit registry lookup; no BFF-facade dependencies.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// DI registration for the H13 E2E acceptance-gate handler + its 6
/// collaborator seams. Composed behind a single
/// <see cref="AddH13E2EAcceptanceGateHandler"/> extension method to minimize
/// Program.cs edit surface.
/// </summary>
public static class E2EAcceptanceModule
{
    /// <summary>Configuration section for H13 options.</summary>
    public const string ConfigSection = "E2EAcceptance";

    /// <summary>
    /// Registers <see cref="H13E2EAcceptanceGateHandler"/> + its 6 collaborator
    /// seams with the DI container.
    /// </summary>
    public static IServiceCollection AddH13E2EAcceptanceGateHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<H13AcceptanceOptions>(configuration.GetSection(ConfigSection));

        // Production seam registrations. Placeholder impls for TrapVerifier /
        // RegistrySetupStatusUpdater surface "not yet wired" as Resumable per
        // POML deferral note (real Wave-G7 sibling probes swap via DI change
        // only — the H13 handler + tests are unchanged).
        //
        // I1 branch (task 170, Wave G-7 Batch G-7A1): swapped from
        // PlaceholderInvariantVerifier to PackagedScriptTenantLiteralInvariantVerifier.
        // The real class returns real Pass/Fail for I1 (packaged-scripts on-disk
        // grep for tenant-shaped GUID defaults on [string]$*Tenant* Params) and
        // preserves InfraFault for I2-I5 (their own sibling tasks 173/174/176/179
        // land those one-by-one). The placeholder file is retained on disk
        // (unregistered) as reversibility scaffolding — see
        // PlaceholderInvariantVerifier.cs retirement banner.
        services.AddSingleton<IE2EValidationRunner, ValidateDeployedEnvironmentScriptRunner>();
        services.AddSingleton<IE2ETrapVerifier, PlaceholderTrapVerifier>();
        services.AddSingleton<IE2EInvariantVerifier, PackagedScriptTenantLiteralInvariantVerifier>();
        services.AddSingleton<INamingConformanceChecker, NamingConformanceScriptRunner>();
        services.AddSingleton<ICostEnvelopeChecker, AzCliCostEnvelopeChecker>();
        services.AddSingleton<IRegistrySetupStatusUpdater, DataverseRegistrySetupStatusUpdater>();

        services.AddScoped<H13E2EAcceptanceGateHandler>();

        return services;
    }
}
