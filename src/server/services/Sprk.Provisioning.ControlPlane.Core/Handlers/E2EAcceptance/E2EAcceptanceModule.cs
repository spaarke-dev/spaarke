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
        //   - task 174 (I3)  — CosmosPartitionKeyInvariantProbe.
        //   - task 176 (I4)  — SpeContainerResolverInvariantProbe (real BFF
        //                     diagnostic call; LIVE-verification deferred to
        //                     task 186 per that POML's escalation trigger).
        //   - task 179 (I5)  — I5GraphTokenTenantScopeProbe.
        // PlaceholderInvariantVerifier.cs is retained on disk unregistered
        // per the Wave G-6 retirement convention.
        // Task 181 (Phase C'' Wave G-7 Batch G-7B): pure-C# port replaces the
        // ValidateDeployedEnvironmentScriptRunner shell-out per DS-4 section 6 --
        // ZERO ProcessStartInfo / pwsh dependency; the port issues live HttpClient
        // effect probes (BFF /healthz, /ping, CORS preflight) against the customer's
        // deployed BFF and surfaces the .ps1's Dataverse-env-vars + dev-leakage
        // checks + Phase-B extended set (sample analysis / doc upload+index /
        // layout render / wizard field-map) as an explicit ChecksSkipped list per
        // the interim posture -- see E2EValidationRunner.cs file header for the
        // silent-fail audit (POML premise mismatch: the .ps1 shipped 5 DIFFERENT
        // checks than the POML prompt claimed; ported what actually exists +
        // surfaced the gaps rather than silently mis-implementing the POML's
        // Phase-B list against a script that never contained it). Named HttpClient
        // registered below parity with the sibling I2/I4 probe named-client
        // convention. ValidateDeployedEnvironmentScriptRunner is retained on disk
        // UNREGISTERED per this project's retirement convention (see its retirement
        // banner). Registration remains UNCONDITIONAL (ADR-032).
        services.AddHttpClient(E2EValidationRunner.HttpClientName);
        services.AddSingleton<IE2EValidationRunner, E2EValidationRunner>();
        services.AddSingleton<IE2ETrapVerifier, PlaceholderTrapVerifier>();
        services.AddSingleton<IE2EInvariantVerifier, CompositeInvariantVerifier>();
        // I1 adapter (task 173) — preserves task-170's real packaged-scripts
        // I1 check under the composite pattern by wrapping its internal-static
        // ProbeI1 in an IInvariantProbe (see PackagedScriptTenantLiteralInvariantProbe.cs
        // rationale). Without this adapter, the composite swap would silently
        // regress task-170's shipped Wave G-7 I1 real check to InfraFault.
        services.AddSingleton<IInvariantProbe, PackagedScriptTenantLiteralInvariantProbe>(); // I1 (task 170 IP; task 173 adapter)
        // I2 (task 173) — real AI Search tenant-filter probe. Reads Model 1
        // template artifact from Cosmos + issues a live /docs/search POST
        // asserting `tenantId eq '{TenantId}'` is enforced server-side. See
        // AiSearchTenantFilterInvariantProbe.cs file header for the honest
        // can-vs-cannot-detect breakdown. Needs IHttpClientFactory (named
        // HttpClient below) + TokenCredential + AiSearchIndexOptions +
        // ITenantFilterTemplateStore + ICanonicalIndexCatalog +
        // IProvisioningRunRepository — all pre-registered by Program.cs or by
        // task 045/124's H2b DI.
        services.AddHttpClient(AiSearchTenantFilterInvariantProbe.HttpClientName);
        services.AddSingleton<IInvariantProbe, AiSearchTenantFilterInvariantProbe>();   // I2 (task 173)
        services.AddSingleton<IInvariantProbe, CosmosPartitionKeyInvariantProbe>();     // I3 (task 174)
        // I4 (task 176) — real BFF-diagnostic probe for
        // ITenantContainerResolver-derived SPE container ids. Needs a NAMED
        // HttpClient (registered below) + the shared UAMI-pinned
        // TokenCredential (pre-registered by Worker Program.cs). See
        // SpeContainerResolverInvariantProbe.cs file header for the honest
        // fake-vs-live posture: authored + unit-tested against fake HTTP
        // transport NOW; LIVE verification against a real deployed BFF
        // diagnostic endpoint is deferred to Phase F rerun (task 186) per
        // this task's POML escalation trigger.
        services.AddHttpClient(SpeContainerResolverInvariantProbe.HttpClientName);
        services.AddSingleton<IInvariantProbe, SpeContainerResolverInvariantProbe>();   // I4 (task 176)
        services.AddSingleton<IInvariantProbe, I5GraphTokenTenantScopeProbe>();         // I5 (task 179)
        // Task 182 (Phase C'' Wave G-7 Batch G-7A1): pure-C# port replaces the
        // NamingConformanceScriptRunner shell-out per DS-4 section 6 (this script
        // has 0 az/REST calls -- pure convention checks, so the port is a trivial
        // mechanical translation). NamingConformanceScriptRunner is retained on
        // disk UNREGISTERED per this project's retirement convention (see its
        // retirement banner). Registration remains UNCONDITIONAL (ADR-032).
        services.AddSingleton<INamingConformanceChecker, NamingConformanceChecker>();
        // Task 183 (Phase C'' Wave G-7 Batch G-7A2.2): Azure.ResourceManager.
        // CostManagement SDK port replaces the AzCliCostEnvelopeChecker shell-out
        // per DS-4 section 6 (mechanical REST/SDK swap; threshold arithmetic
        // ported verbatim). AzCliCostEnvelopeChecker is retained on disk
        // UNREGISTERED per this project's retirement convention (see its
        // retirement banner). ArmClient is constructed by the WorkerHost's
        // factory registration in Program.cs (parity with the sibling
        // Wave-G-7 T1/I3/I5 probe registrations that also inject ArmClient
        // -- the shared UAMI-pinned TokenCredential singleton is reused).
        // Registration remains UNCONDITIONAL (ADR-032).
        services.AddSingleton<ICostEnvelopeChecker, ArmCostEnvelopeChecker>();
        // Task 184 (Wave G-7 Batch G-7C): real Ready writer. Delegates to task
        // 112's IDataverseEnvironmentRegistryClient (registered SCOPED by
        // AddDataverseEnvironmentRegistry). Registered SCOPED to avoid
        // singleton-consuming-scoped captive-dependency at DI resolution
        // (parity with the H13 handler itself). The Wave-C4 placeholder was
        // Singleton because it took no dependencies beyond ILogger; now that
        // it delegates to a scoped registry client the lifetime must widen.
        services.AddScoped<IRegistrySetupStatusUpdater, DataverseRegistrySetupStatusUpdater>();

        services.AddScoped<H13E2EAcceptanceGateHandler>();

        return services;
    }
}
