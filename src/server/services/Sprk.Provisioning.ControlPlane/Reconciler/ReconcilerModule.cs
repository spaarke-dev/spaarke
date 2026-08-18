// -----------------------------------------------------------------------------
// ReconcilerModule.cs
//
// L2 CONTROL-PLANE state-reconciler DI composition (task 058, Wave C5).
//
// Single extension method registers:
//   - ReconcilerOptions (bound from IConfiguration section "Reconciler",
//     with fail-fast Validate() at startup).
//   - TimeProvider.System (once) — production clock. Tests may override in
//     WebApplicationFactory ConfigureServices with FakeTimeProvider-style
//     doubles. Wave-C4 handler DI never touched TimeProvider so this is the
//     first registration in the L2 project; guarded with TryAddSingleton so
//     future wave additions don't double-register.
//   - IDagAdvancer -> DagAdvancer (Singleton — pure function).
//   - IActiveRunScanner -> CosmosActiveRunScanner (Scoped — mirrors
//     CosmosProvisioningRunRepository's lifetime; scanner is stateless over
//     the singleton CosmosClient, but Scoped is the DI-minimalism default).
//   - StateReconcilerService (HostedService).
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only. Consumes NO AI-internal types
// (ADR-013 forcing-function rule). Reuses the Cosmos client + database +
// container names registered by CosmosModule (task 037) — no duplicate
// client instantiation.
// -----------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Provisioning.ControlPlane.Modules;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// DI registration for the L2 state-reconciler stack — options + scanner +
/// DAG advancer + hosted service.
/// </summary>
public static class ReconcilerModule
{
    /// <summary>
    /// Registers the state-reconciler DI graph + hosted service. Fails fast at
    /// startup on invalid <see cref="ReconcilerOptions"/> (NFR-05 parity with
    /// CosmosModule / ServiceBusModule).
    /// </summary>
    public static IServiceCollection AddReconcilerModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind + validate options. Validate runs at PostConfigure so it fires
        // during host build, before the reconciler starts.
        services.Configure<ReconcilerOptions>(configuration.GetSection(ReconcilerOptions.SectionName));
        services.PostConfigure<ReconcilerOptions>(o => o.Validate());

        // TimeProvider.System is the production clock. TryAddSingleton so a
        // later wave (or a test host) that registers its own TimeProvider
        // (e.g. FakeTimeProvider) wins first-writer semantics.
        services.TryAddSingleton(TimeProvider.System);

        // DAG advancer is a pure function — no state, no I/O. Singleton is safe.
        services.TryAddSingleton<IDagAdvancer, DagAdvancer>();

        // Cross-partition scanner — Scoped, mirroring the repository lifetime.
        // Reuses the SAME Cosmos database + container names CosmosModule
        // registers (defaults spaarke-provisioning/runs) so both services
        // target the identical physical container.
        services.AddScoped<IActiveRunScanner>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var databaseName = configuration[$"{CosmosModule.ConfigSection}:DatabaseName"]
                ?? CosmosModule.DefaultDatabaseName;
            var containerName = configuration[$"{CosmosModule.ConfigSection}:ContainerName"]
                ?? CosmosModule.DefaultContainerName;
            var logger = sp.GetRequiredService<ILogger<CosmosActiveRunScanner>>();
            return new CosmosActiveRunScanner(cosmosClient, databaseName, containerName, logger);
        });

        // BackgroundService — one instance per L2 process (per App Service
        // instance under scale-out). Concurrent-safe: Service Bus MessageId
        // dedup + BFF-side Redis idempotency lock catch duplicate dispatches
        // from parallel reconcilers.
        services.AddHostedService<StateReconcilerService>();

        return services;
    }
}
