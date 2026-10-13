// -----------------------------------------------------------------------------
// CustomerRunGuardModule.cs
//
// L2 CONTROL-PLANE I5 concurrency-guard DI composition (task 059, Wave C5).
//
// Single extension method registers:
//   - CustomerRunGuardOptions (bound from IConfiguration section
//     "CustomerRunGuard", with fail-fast Validate() at startup).
//   - Named HttpClient for DataverseRegistryConcurrencyStore.
//   - IRegistryConcurrencyStore -> DataverseRegistryConcurrencyStore (Singleton).
//   - ICustomerRunGuard -> CustomerRunGuard (Singleton — stateless over the store).
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
            // REG-05: the guard and the registry client write the same sprk_dataverseenvironment rows, so one
            // setting drives both — fall back to the registry's admin URL, and refuse two settings that disagree.
            var registryAdminUrl = configuration[CustomerRunGuardOptions.RegistryAdminEnvironmentUrlKey];
            if (string.IsNullOrWhiteSpace(o.TargetDataverseUrl))
            {
                o.TargetDataverseUrl = registryAdminUrl;
            }
            else if (o.Enabled
                && !string.IsNullOrWhiteSpace(registryAdminUrl)
                && Uri.TryCreate(o.TargetDataverseUrl, UriKind.Absolute, out var guardUri)
                && Uri.TryCreate(registryAdminUrl, UriKind.Absolute, out var registryUri)
                && !string.Equals(guardUri.Host, registryUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                // Only an enabled guard writes rows, so the kill-switch (Enabled=false) skips this check like
                // the rest of Validate(). A relative/invalid TargetDataverseUrl is left to Validate(), which names it.
                throw new InvalidOperationException(
                    $"Configuration mismatch — '{CustomerRunGuardOptions.SectionName}:TargetDataverseUrl' host " +
                    $"'{guardUri.Host}' does not match '{CustomerRunGuardOptions.RegistryAdminEnvironmentUrlKey}' host " +
                    $"'{registryUri.Host}'. Both MUST point at the same admin Dataverse environment (they read and write " +
                    "the same sprk_dataverseenvironment rows). See REG-05.");
            }

            // REG-02: the guard signs in as the L2 UAMI, the same identity the rest of the host uses.
            if (string.IsNullOrWhiteSpace(o.ManagedIdentityClientId))
            {
                o.ManagedIdentityClientId = configuration["ManagedIdentity:ClientId"];
            }

            o.Validate();
        });

        // Run the PostConfigure + Validate() above at host start, not at the first resolve (on the Api that
        // would be the first POST /api/runs) — the fail-fast this module's summary promises.
        services.AddOptions<CustomerRunGuardOptions>().ValidateOnStart();

        // Named HttpClient — the store owns per-request Timeout + auth header
        // application, but IHttpClientFactory manages the connection pool.
        services.AddHttpClient(DataverseRegistryConcurrencyStore.HttpClientName);

        // Singleton: both store + guard are stateless over their injected
        // collaborators (options, IHttpClientFactory, ILogger). Same lifetime
        // choice as ICanonicalIndexCatalog / other pure
        // registration collaborators in L2.
        services.TryAddSingleton<IRegistryConcurrencyStore, DataverseRegistryConcurrencyStore>();
        services.TryAddSingleton<ICustomerRunGuard, CustomerRunGuard>();

        return services;
    }
}
