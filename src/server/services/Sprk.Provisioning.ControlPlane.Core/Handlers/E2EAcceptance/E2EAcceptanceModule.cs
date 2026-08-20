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

        // Production seam registrations. PlaceholderTrapVerifier surfaces
        // "not yet wired" as Resumable per POML deferral note.
        //
        // IE2EInvariantVerifier — Wave G-7 Batch G-7A1 composite migration
        // (task 174 coordinated with task 173). CompositeInvariantVerifier
        // dispatches per-InvariantKind to registered IInvariantProbe impls;
        // un-registered kinds fall back to
        // InvariantProbeDeferralMessages.DeferralDiagnostic InfraFault,
        // preserving PlaceholderInvariantVerifier's Resumable semantics for
        // un-wired kinds. Sibling wave-G-7 tasks each add ONE
        // AddSingleton<IInvariantProbe, TProbe>() line here:
        //   - task 170 (I1) — currently ships PackagedScriptTenantLiteralInvariantVerifier
        //                     as a whole-IE2EInvariantVerifier hybrid; task 185
        //                     is expected to refactor its ProbeI1 internal
        //                     static into a proper IInvariantProbe adapter so
        //                     it composes here. Until then, I1 returns the
        //                     deferral-diagnostic InfraFault (called out
        //                     explicitly here rather than silently).
        //   - task 173 (I2)  — sibling I2 AI Search tenant-filter probe.
        //   - task 174 (I3)  — CosmosPartitionKeyInvariantProbe (THIS task).
        //   - task 176 (I4)  — sibling I4 SPE container resolver probe.
        //   - task 179 (I5)  — sibling I5 Graph token tenant probe.
        // PlaceholderInvariantVerifier.cs is retained on disk unregistered
        // per the Wave G-6 retirement convention.
        services.AddSingleton<IE2EValidationRunner, ValidateDeployedEnvironmentScriptRunner>();
        services.AddSingleton<IE2ETrapVerifier, PlaceholderTrapVerifier>();
        services.AddSingleton<IE2EInvariantVerifier, CompositeInvariantVerifier>();
        services.AddSingleton<IInvariantProbe, CosmosPartitionKeyInvariantProbe>();     // I3 (task 174)
        services.AddSingleton<IInvariantProbe, I5GraphTokenTenantScopeProbe>();         // I5 (task 179)
        // Task 182 (Phase C'' Wave G-7 Batch G-7A1): pure-C# port replaces the
        // NamingConformanceScriptRunner shell-out per DS-4 section 6 (this script
        // has 0 az/REST calls -- pure convention checks, so the port is a trivial
        // mechanical translation). NamingConformanceScriptRunner is retained on
        // disk UNREGISTERED per this project's retirement convention (see its
        // retirement banner). Registration remains UNCONDITIONAL (ADR-032).
        services.AddSingleton<INamingConformanceChecker, NamingConformanceChecker>();
        services.AddSingleton<ICostEnvelopeChecker, AzCliCostEnvelopeChecker>();
        services.AddSingleton<IRegistrySetupStatusUpdater, DataverseRegistrySetupStatusUpdater>();

        services.AddScoped<H13E2EAcceptanceGateHandler>();

        return services;
    }
}
