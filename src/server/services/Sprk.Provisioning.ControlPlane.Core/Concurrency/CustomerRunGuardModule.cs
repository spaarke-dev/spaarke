// -----------------------------------------------------------------------------
// CustomerRunGuardModule.cs
//
// L2 CONTROL-PLANE I5 concurrency-guard DI composition (task 059, Wave C5;
// REG-02 + REG-05 Path X migration, 2026-08-27, Wave 2 pre-dispatch
// remediation).
//
// Single extension method registers:
//   - CustomerRunGuardOptions (bound from IConfiguration section
//     "CustomerRunGuard", with fail-fast Validate() at startup).
//   - Named HttpClient for DataverseRegistryConcurrencyStore.
//   - IRegistryConcurrencyStore -> DataverseRegistryConcurrencyStore (Singleton).
//   - ICustomerRunGuard -> CustomerRunGuard (Singleton — stateless over the store).
//
// PATH X CREDENTIAL WIRING (REG-02, 2026-08-27):
//   The store authenticates via DefaultAzureCredential pinned to the L2 UAMI
//   (see DataverseRegistryConcurrencyStore.AcquireTokenAsync). This module's
//   PostConfigure supplies the two fallbacks that make Enabled=true safe on
//   every deployment shape:
//     1. `TargetDataverseUrl` fallback ← `DataverseEnvironmentRegistry:AdminEnvironmentUrl`
//        (REG-05 URL collapse — one setting drives both admin-env clients).
//     2. `ManagedIdentityClientId` fallback ← `ManagedIdentity:ClientId`
//        (parity with CosmosModule + DataverseEnvironmentRegistryModule so a
//        single property pins every module's UAMI).
//
// REG-05 CROSS-CHECK:
//   When both `CustomerRunGuard:TargetDataverseUrl` AND
//   `DataverseEnvironmentRegistry:AdminEnvironmentUrl` are set, PostConfigure
//   asserts they resolve to the same host (case-insensitive). Different hosts
//   would mean the guard writes sprk_currentrunid to env A while the registry
//   status-updater clears it from env B — a silent lock-forever bug (both
//   PATCHes return 2xx against their own env; see REG-05 punchlist entry).
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only. Consumes NO AI-internal types
// (ADR-013 forcing-function rule). Uses IHttpClientFactory (already registered
// via other handler modules) — no duplicate client instantiation.
//
// ADR-032 UNCONDITIONAL: registered even when CustomerRunGuardOptions.Enabled
// is false. The kill-switch lives INSIDE CustomerRunGuard (returns
// AcquireResult.Success unconditionally when disabled) so the DI graph is a
// stable shape across staged rollout — no branch on Enabled here.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// DI registration for the L2 same-customer serialization guard — options +
/// HTTP client + store + guard.
/// </summary>
public static class CustomerRunGuardModule
{
    /// <summary>
    /// Registers the I5 concurrency-guard DI graph. Fails fast at startup on
    /// invalid <see cref="CustomerRunGuardOptions"/> when Enabled=true (NFR-05
    /// parity with ReconcilerModule / CosmosModule / ServiceBusModule).
    /// </summary>
    public static IServiceCollection AddCustomerRunGuard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<CustomerRunGuardOptions>(
            configuration.GetSection(CustomerRunGuardOptions.SectionName));
        services.PostConfigure<CustomerRunGuardOptions>(o =>
        {
            // REG-05 URL collapse (2026-08-27): fall back to
            // DataverseEnvironmentRegistry:AdminEnvironmentUrl when
            // CustomerRunGuard:TargetDataverseUrl is not set explicitly. The
            // two settings target the SAME admin env (both host the
            // sprk_dataverseenvironment registry table); a single value
            // authored in one place is the ergonomic choice.
            var registryAdminUrl = configuration[DataverseEnvironmentRegistryConfigKeys.AdminEnvironmentUrl];
            if (string.IsNullOrWhiteSpace(o.TargetDataverseUrl))
            {
                o.TargetDataverseUrl = registryAdminUrl;
            }
            else if (!string.IsNullOrWhiteSpace(registryAdminUrl))
            {
                // REG-05 cross-check: BOTH set → hosts MUST match. Divergent
                // hosts would silently lock sprk_currentrunid on one env
                // while the registry status-updater clears it on the other,
                // creating a permanent 409 loop on the next POST /api/runs
                // for that customerId.
                if (!Uri.TryCreate(o.TargetDataverseUrl, UriKind.Absolute, out var guardUri))
                {
                    // Let Validate() emit the shaped error for absolute-URI
                    // parse failure — this branch only fires when both are
                    // set, so a malformed guard URL is the caller's bug and
                    // the standard validator already covers it.
                }
                else if (Uri.TryCreate(registryAdminUrl, UriKind.Absolute, out var registryUri)
                    && !string.Equals(guardUri.Host, registryUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Configuration mismatch — '{CustomerRunGuardOptions.SectionName}:TargetDataverseUrl' " +
                        $"host '{guardUri.Host}' does not match " +
                        $"'{DataverseEnvironmentRegistryConfigKeys.AdminEnvironmentUrl}' host " +
                        $"'{registryUri.Host}'. Both settings MUST point at the SAME admin Dataverse env " +
                        "(they read/write the same sprk_dataverseenvironment rows). See REG-05.");
                }
            }

            // REG-02 (2026-08-27) — ManagedIdentity:ClientId fallback for the
            // UAMI pin, parity with CosmosModule + DataverseEnvironmentRegistryModule.
            if (string.IsNullOrWhiteSpace(o.ManagedIdentityClientId))
            {
                o.ManagedIdentityClientId = configuration["ManagedIdentity:ClientId"];
            }

            o.Validate();
        });

        // Named HttpClient — the store owns per-request Timeout + auth header
        // application, but IHttpClientFactory manages the connection pool.
        services.AddHttpClient(DataverseRegistryConcurrencyStore.HttpClientName);

        // Singleton: both store + guard are stateless over their injected
        // collaborators (options, IHttpClientFactory, ILogger). Same lifetime
        // choice as ISolutionCatalog / ICanonicalIndexCatalog / other pure
        // registration collaborators in L2.
        services.TryAddSingleton<IRegistryConcurrencyStore, DataverseRegistryConcurrencyStore>();
        services.TryAddSingleton<ICustomerRunGuard, CustomerRunGuard>();

        return services;
    }
}
