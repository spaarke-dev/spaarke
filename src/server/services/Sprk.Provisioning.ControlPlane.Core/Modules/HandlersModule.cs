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
//       IPreflightQuotaProbe registration.
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
//   - Register the four IPreflightQuotaProbe instances.
//   - Register H0PreflightHandler as IProvisioningHandler (Scoped).
//   - Do NOT register any downstream handler (H0.5, H1, ...) — those tasks
//     own their own registrations.
//
// TASK 120 UPDATE (Wave G-2, Option D hybrid per DS-1b §1 H0 row):
//   The four probes are now pure .NET SDK/REST implementations
//   (ArmCognitiveServicesTpmProbe, BapRestEnvironmentRateProbe,
//   ArmComputeVCpuProbe, KeyVaultCertBootstrapProbe) — the shell-out
//   PowerShellPreflightProbe + its Preflight:{PwshExecutable,
//   ScriptsDirectory, Timeout} options binding are RETIRED (grep-verified
//   zero remaining callers). The TPM + vCPU probes share ONE platform
//   ArmClient singleton (built here from the CosmosModule TokenCredential,
//   TryAddSingleton so task 121's ArmSubscriptionReadinessProbe can reuse
//   the same instance rather than constructing a second one — CLAUDE.md
//   §11); the KV probe reuses the TokenCredential directly (SecretClient is
//   constructed per-call since the vault name is a per-run parameter); the
//   BAP REST probe is a typed HttpClient (AddHttpClient<IPreflightQuotaProbe,
//   BapRestEnvironmentRateProbe>) since it scopes DefaultAzureCredential
//   per-tenant internally (§4D I5).
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

using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for the L2 provisioning-handler surface. Composes the
/// per-handler + per-probe registrations behind one <see cref="AddProvisioningHandlers"/>
/// extension method to preserve the ~15-line Program.cs budget (ADR-010).
/// </summary>
public static class HandlersModule
{
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

        // Shared platform ArmClient singleton (task 120) — built from the
        // CosmosModule TokenCredential (UAMI-pinned, ADR-028). TryAdd so a
        // sibling module (e.g. task 121's SubscriptionReadiness registration)
        // reuses this same instance instead of constructing a second one
        // (CLAUDE.md §11 — one ArmClient, not N).
        services.TryAddSingleton(sp => new ArmClient(sp.GetRequiredService<TokenCredential>()));

        // Typed HttpClient for the BAP REST probe (task 120) — matches the
        // GraphRestB2BConsentVerifier / GraphRestSubscriptionCreator
        // AddHttpClient<TInterface, TImplementation>() convention. Additive:
        // IPreflightQuotaProbe already has 3 other registrations below: all 4
        // resolve via the IEnumerable<IPreflightQuotaProbe> H0 injects.
        services.AddHttpClient<IPreflightQuotaProbe, BapRestEnvironmentRateProbe>();

        // Remaining three probe registrations (task 120 — SDK ports; Option D
        // hybrid per DS-1b §1 H0 row). Order does not matter; H0 orchestrates
        // all four in parallel.
        services.AddScoped<IPreflightQuotaProbe>(sp => new ArmCognitiveServicesTpmProbe(
            sp.GetRequiredService<ArmClient>(),
            sp.GetRequiredService<ILogger<ArmCognitiveServicesTpmProbe>>()));
        services.AddScoped<IPreflightQuotaProbe>(sp => new ArmComputeVCpuProbe(
            sp.GetRequiredService<ArmClient>(),
            sp.GetRequiredService<ILogger<ArmComputeVCpuProbe>>()));
        services.AddScoped<IPreflightQuotaProbe>(sp => new KeyVaultCertBootstrapProbe(
            sp.GetRequiredService<TokenCredential>(),
            sp.GetRequiredService<ILogger<KeyVaultCertBootstrapProbe>>()));

        // HANDLER-03 (Wave 2 pre-dispatch remediation 2026-08-27) — F1
        // verbatim absorption: pinned Azure OpenAI model freshness probe.
        // Fails H0 fast if any ADR-020 pin (PinnedModelCatalog.Models) is
        // Deprecating / already-Deprecated / not-reported in the target
        // region, sparing the operator the ~20-30 min wait for H2a to fail
        // with ServiceModelDeprecated. Reuses the shared platform ArmClient
        // singleton (TryAddSingleton above); reuses the canonical ADR-020
        // catalog (no second source of truth); reuses the ambient
        // TimeProvider (test-injectable per docs/standards/TEST-ARCHITECTURE.md).
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPreflightQuotaProbe>(sp => new ArmOpenAiPinFreshnessProbe(
            sp.GetRequiredService<ArmClient>(),
            PinnedModelCatalog.Models,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ArmOpenAiPinFreshnessProbe>>()));

        // FR-34 version-compat matrix (Wave G-8 Batch 10, defect #24) —
        // singleton: the parsed matrix is immutable per process lifetime
        // (matrix edits ship with a release-tag → new deploy). Default source
        // is the embedded version-compat-matrix.json; operators can override
        // with an on-disk file via Preflight:VersionCompatMatrixPath (hotfix
        // path — no rebuild). Queried by H0 in upgrade mode ONLY.
        services.TryAddSingleton<IVersionCompatMatrix>(sp => new JsonFileVersionCompatMatrix(
            configuration["Preflight:VersionCompatMatrixPath"],
            sp.GetRequiredService<ILogger<JsonFileVersionCompatMatrix>>()));

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
}
