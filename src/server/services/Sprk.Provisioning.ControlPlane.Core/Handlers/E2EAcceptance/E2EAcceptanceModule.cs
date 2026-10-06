// -----------------------------------------------------------------------------
// E2EAcceptanceModule.cs
//
// L2 CONTROL-PLANE DI composition for the H13 E2E acceptance-gate handler +
// its 5 collaborator seams (task 055, wave C4 Batch 4E).
//
// SCOPE:
//   - Bind E2EAcceptance:{probe timeouts, cost envelope + thresholds,
//     CostDriftFailsRun, TargetSlotName, HonorRegistryStatusReadyShortCircuit}
//     options.
//   - Register the 5 collaborator seams (IE2EValidationRunner,
//     IE2ETrapVerifier, IE2EInvariantVerifier, ICostEnvelopeChecker,
//     IRegistrySetupStatusUpdater) + the H13 handler itself as Scoped.
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
//   IProvisioningRunRepository (task 037) + 5 dedicated seams + reuses
//   IDataverseEnvironmentRegistryClient (task 042 H0.5 seam) for the
//   idempotency-short-circuit registry lookup; no BFF-facade dependencies.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// DI registration for the H13 E2E acceptance-gate handler + its 5
/// collaborator seams. Composed behind a single
/// <see cref="AddH13E2EAcceptanceGateHandler"/> extension method to minimize
/// Program.cs edit surface.
/// </summary>
public static class E2EAcceptanceModule
{
    /// <summary>Configuration section for H13 options.</summary>
    public const string ConfigSection = "E2EAcceptance";

    /// <summary>
    /// Registers <see cref="H13E2EAcceptanceGateHandler"/> + its 5 collaborator
    /// seams with the DI container.
    /// </summary>
    public static IServiceCollection AddH13E2EAcceptanceGateHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<H13AcceptanceOptions>()
            .Bind(configuration.GetSection(ConfigSection))
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "E2EAcceptance options failed validation — see inner exception (Validate throws).")
            .ValidateOnStart();

        // Production seam registrations. All 7 trap + 4 runtime invariant (I2–I5)
        // probes are real as of task 185 (Wave G-7 Batch G-7D). Task 230a deleted
        // the unregistered PlaceholderTrapVerifier / PlaceholderInvariantVerifier.
        //
        // IE2ETrapVerifier -- Wave G-7 Batch G-7D composite migration (task 185).
        // CompositeTrapVerifier dispatches per-TrapKind to registered ITrapProbe
        // impls; un-registered kinds fall back to
        // TrapProbeDeferralMessages.DeferralDiagnostic InfraFault, preserving
        // PlaceholderTrapVerifier's Resumable semantics for any future un-wired
        // kinds. Direct parity with the earlier IE2EInvariantVerifier composite
        // migration (Batch G-7A1 / task 174). Task 185 wires all 6 real trap
        // probes below (task 238 adds the 7th, T7 CustomerIdentityT7Probe):
        //   - task 171 (T1) - KeyVaultReferenceIdentityT1Probe (ArmClient)
        //   - task 177 (T2) - DataverseAppUserPairT2Probe (IDataverseAppUserVerifier)
        //   - task 178 (T3) - GraphAppRoleParityT3Probe (IGraphAppRoleParityVerifier
        //                     + IGraphAppRolesRegistry + typed HttpClient)
        //   - task 180 (T4) - ExchangePolicyCountT4Probe (IExchangePolicyReadClient)
        //   - task 172 (T5) - T5SlotMiKvRbacTrapProbe (ArmClient)
        //   - task 175 (T6) - T6SpeConfidentialClientTrapProbe (IT6GraphAppOnlyProbe
        //                     + SpeContainerOptions; task 248 — owning app via MI-FIC)
        //   - task 238 (T7) - CustomerIdentityT7Probe (ArmClient)
        //
        // IE2EInvariantVerifier — Wave G-7 Batch G-7A1 composite migration
        // (task 174 coordinated with task 173). CompositeInvariantVerifier
        // dispatches per-InvariantKind to registered IInvariantProbe impls;
        // un-registered kinds fall back to
        // InvariantProbeDeferralMessages.DeferralDiagnostic InfraFault,
        // preserving PlaceholderInvariantVerifier's Resumable semantics for
        // un-wired kinds. Sibling wave-G-7 tasks each add ONE
        // AddSingleton<IInvariantProbe, TProbe>() line here:
        //   - task 170 (I1) — packaged-scripts probe DELETED by task 230a: no
        //                     scripts ship with the Worker publish; I1 is
        //                     build-time (the I1 ArchTest).
        //   - task 173 (I2)  — sibling I2 AI Search tenant-filter probe.
        //   - task 174 (I3)  — CosmosPartitionKeyInvariantProbe.
        //   - task 176 (I4)  — a BFF-diagnostic resolver probe, superseded by task 204c's
        //                     SpeContainerTenantDerivationInvariantProbe and retired with the
        //                     diagnostic route by task 227f.
        //   - task 179 (I5)  — I5GraphTokenTenantScopeProbe.
        // Task 181 (Phase C'' Wave G-7 Batch G-7B): pure-C# port replaces the
        // ValidateDeployedEnvironmentScriptRunner shell-out per DS-4 section 6 --
        // ZERO ProcessStartInfo / pwsh dependency; the port issues live HttpClient
        // effect probes (BFF /healthz, /ping, CORS preflight) against the customer's
        // deployed BFF and surfaces the .ps1's Dataverse-env-vars + dev-leakage
        // checks as an explicit ChecksSkipped list per the interim posture.
        // G-8 Batch 11 (2026-08-20, audit Defect #21 / SC #5): the Phase-B
        // extended set (sample analysis / doc upload+index / layout render /
        // wizard field-map) GRADUATED to four real authenticated sample-workload
        // checks inside the same runner (bearer token from the shared UAMI-pinned
        // TokenCredential singleton, scope {bffAuthority}/.default -- parity with
        // the I4 probe). The runner therefore now ALSO injects TokenCredential +
        // IOptions<H13AcceptanceOptions> (both pre-registered; no new lines
        // needed here) -- see E2EValidationRunner.cs file header for the
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
        services.AddSingleton<IE2ETrapVerifier, CompositeTrapVerifier>();
        services.AddSingleton<IE2EInvariantVerifier, CompositeInvariantVerifier>();
        // Task 185 (Wave G-7 Batch G-7D): 7 real ITrapProbe registrations (T7 added by task 238) for
        // the composite trap verifier. Order does not matter (composite
        // dispatches per Kind); each probe's own file header documents its
        // dependencies. IT6GraphAppOnlyProbe (registered below) is the
        // T6-specific Graph seam consumed by T6SpeConfidentialClientTrapProbe.
        // ArmClient (T1 + T5) + SpeConfidentialClientGraphFactory (T6, task 248) come from the shared
        // HandlersModule / Program.cs registrations; IDataverseAppUserVerifier
        // (T2) + IGraphAppRoleParityVerifier + IGraphAppRolesRegistry (T3)
        // come from H10's own module registration (line-parity with H10 wire);
        // IExchangePolicyReadClient (T4) comes from H14's IntegrationWiringModule
        // registration. All UNCONDITIONAL (ADR-032).
        services.AddSingleton<ITrapProbe, KeyVaultReferenceIdentityT1Probe>();       // T1 (task 171)
        services.AddSingleton<ITrapProbe, DataverseAppUserPairT2Probe>();            // T2 (task 177)
        // T3 (task 178) -- has TWO public constructors (production 4-param +
        // testing 5-param with Func<string,TokenCredential>); ActivatorUtilities
        // cannot disambiguate them under AddHttpClient<T>() typed-client factory
        // (both signatures have HttpClient at index 3), so we hand-construct via
        // named HttpClient + explicit ctor call. Pins the production ctor.
        services.AddHttpClient(nameof(GraphAppRoleParityT3Probe));
        services.AddSingleton<ITrapProbe>(sp => new GraphAppRoleParityT3Probe(
            sp.GetRequiredService<IGraphAppRoleParityVerifier>(),
            sp.GetRequiredService<IGraphAppRolesRegistry>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GraphAppRoleParityT3Probe)),
            sp.GetRequiredService<ILogger<GraphAppRoleParityT3Probe>>()));
        services.AddSingleton<ITrapProbe, ExchangePolicyCountT4Probe>();             // T4 (task 180)
        services.AddSingleton<ITrapProbe, T5SlotMiKvRbacTrapProbe>();                // T5 (task 172)
        services.AddSingleton<IT6GraphAppOnlyProbe, GraphContainersListAppOnlyProbe>(); // task 248
        services.AddSingleton<ITrapProbe, T6SpeConfidentialClientTrapProbe>();       // T6 (task 175)
        services.AddSingleton<ITrapProbe, CustomerIdentityT7Probe>();                // T7 (task 238, D-14 — ArmClient)
        // Task 230a: the I1 PackagedScriptTenantLiteralInvariantProbe registration DELETED (I1 is build-time).
        // I2 (task 173) — real AI Search tenant-filter probe. Issues a live
        // /docs/search POST on each canonical index of the stamp's own AI
        // Search service asserting `tenantId eq '{TenantId}'` is enforced
        // server-side (task 225b retired the Model 1 template-artifact read).
        // See AiSearchTenantFilterInvariantProbe.cs file header for the honest
        // can-vs-cannot-detect breakdown. Needs IHttpClientFactory (named
        // HttpClient below) + TokenCredential + AiSearchIndexOptions +
        // ICanonicalIndexCatalog + IProvisioningRunRepository — all
        // pre-registered by Program.cs or by task 045/124's H2b DI.
        services.AddHttpClient(AiSearchTenantFilterInvariantProbe.HttpClientName);
        services.AddSingleton<IInvariantProbe, AiSearchTenantFilterInvariantProbe>();   // I2 (task 173)
        services.AddSingleton<IInvariantProbe, CosmosPartitionKeyInvariantProbe>();     // I3 (task 174)
        // I4 (task 204c B07 — Wave G-7 replacement of task 176, 2026-08-26).
        // INDEPENDENT re-verification variant: reads DEPLOYED App Service
        // config directly via ARM `Microsoft.Web/sites/{name}/config/appsettings/list`
        // and compares the SPE settings with the run's own values (task 227c):
        // container type = the run's; EmailProcessing__DefaultContainerId and
        // Communication__ArchiveContainerId = H8's container (another id →
        // Failed CATASTROPHIC, owner D28). Task 204c dispatch directive: "do NOT trust
        // RunStatus.HandlerReports; re-read the underlying Azure/Cosmos/
        // Graph/SPE surface directly" — task 176's BFF-diagnostic pattern
        // trusts the BFF's own self-report and cannot detect a compromised
        // deploy whose BFF diagnostic echoes plausibly while the app-setting
        // is hardcoded (§4D I4 CATASTROPHIC class). See probe file header
        // § SILENT-FAIL AUDIT for the failure-mode delta. Needs a NAMED
        // HttpClient (registered below) + the shared UAMI-pinned
        // TokenCredential + IOptions<H13AcceptanceOptions>. (Task 176's BFF-diagnostic
        // resolver probe, kept unregistered after this replaced it, was deleted with the
        // diagnostic route by task 227f — nothing could call it.)
        services.AddHttpClient(SpeContainerTenantDerivationInvariantProbe.HttpClientName);
        services.AddSingleton<IInvariantProbe, SpeContainerTenantDerivationInvariantProbe>();   // I4 (task 204c B07; supersedes task 176)
        services.AddSingleton<IInvariantProbe, I5GraphTokenTenantScopeProbe>();         // I5 (task 179)
        // Task 230a: INamingConformanceChecker DELETED — it linted Spaarke repo files absent from the
        // Worker publish; scripts/naming-conformance-check.ps1 runs once as a blocking CI step.
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
