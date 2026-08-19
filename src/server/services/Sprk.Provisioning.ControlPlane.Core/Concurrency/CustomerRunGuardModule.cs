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
        services.PostConfigure<CustomerRunGuardOptions>(o => o.Validate());

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
