// -----------------------------------------------------------------------------
// DispatchModule.cs
//
// L2 CONTROL-PLANE dispatcher shared-infra DI composition (task 102, Phase C''
// Wave G-1).
//
// PURPOSE:
//   Registers the dispatcher's SHARED-INFRA pieces that live in .Core:
//     - DispatcherOptions        (bound from "Dispatcher" section + validated)
//     - IDispatchIdempotencyService -> NoOpDispatchIdempotencyService default
//
//   The ProvisioningHandlerDispatcher BackgroundService itself lives in
//   Sprk.Provisioning.ControlPlane.Worker/Dispatch/ (per task 102 POML
//   file-layout constraint + design intent that dispatcher CODE is a
//   .Worker-only concern). .Worker/Program.cs registers the hosted service
//   directly next to its call to AddDispatchModule -- EXACT parity with
//   how CrashRecoveryStartupService (in .Core/Reconciler/) is registered
//   in Program.cs today:
//
//     builder.Services.AddDispatchModule(builder.Configuration);
//     builder.Services.AddHostedService<ProvisioningHandlerDispatcher>();
//
//   This split is a MECHANICAL consequence of the .Core/.Worker project
//   boundary (a .Core module extension cannot reference a .Worker type).
//   It does not weaken the ADR-010 god-class-ratchet posture: the two
//   dispatcher-related lines in Program.cs are the same 2-liner shape
//   CrashRecovery uses, plus one addressable module call.
//
// LIFETIME NOTES:
//   - DispatcherOptions             : Options<T> pattern (bound + validated).
//   - IDispatchIdempotencyService   : Singleton (NoOp today; task 105
//                                     swaps for Singleton Redis-backed
//                                     impl consuming Singleton
//                                     IDistributedCache from the same
//                                     Microsoft.Extensions.Caching.
//                                     StackExchangeRedis package this
//                                     task added to .Core.csproj).
//
// PLACEMENT (CLAUDE.md §10 / §11):
//   Extension method LIVES IN .Core because both .Worker (production
//   host) and .Api (future in-process integration test host) may want to
//   compose the shared-infra. NO reference to Sprk.Bff.Api types (project
//   MUST rule -- L2 is a PEER service).
//
// KILL-SWITCH POSTURE (ADR-032):
//   Registration is UNCONDITIONAL. The runtime kill switch is
//   DispatcherOptions.Enabled -- when false, ExecuteAsync exits cleanly
//   before creating the session processor (parity with
//   StateReconcilerService's ReconcilerOptions.Enabled pattern). This is
//   the HostedService-shaped Null-Object kill switch:
//     - Registration always present (no if/else in DI).
//     - Runtime opts out inside ExecuteAsync.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// DI registration for the L2 dispatcher shared-infra (options + Level-2
/// idempotency seam). The <c>ProvisioningHandlerDispatcher</c> hosted service
/// itself lives in <c>Sprk.Provisioning.ControlPlane.Worker/Dispatch/</c> and
/// is registered directly by <c>Sprk.Provisioning.ControlPlane.Worker/Program.cs</c>
/// via <see cref="Microsoft.Extensions.Hosting.ServiceCollectionHostedServiceExtensions.AddHostedService{THostedService}(IServiceCollection)"/>
/// — see file header for the mechanical rationale.
/// </summary>
public static class DispatchModule
{
    /// <summary>
    /// Registers dispatcher-scope shared-infra: <see cref="DispatcherOptions"/>
    /// (bound from configuration + fail-fast validated at startup) +
    /// <see cref="IDispatchIdempotencyService"/> (no-op default, replaced by
    /// task 105's Redis impl). Fails fast at startup on invalid options
    /// (NFR-05 parity with ReconcilerModule / CrashRecoveryOptions).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound configuration (reads the <c>Dispatcher</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDispatchModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind + validate. Validate runs at PostConfigure so it fires during
        // host build, before ExecuteAsync starts -- fail-fast per NFR-05.
        services.Configure<DispatcherOptions>(configuration.GetSection(DispatcherOptions.SectionName));
        services.PostConfigure<DispatcherOptions>(o => o.Validate());

        // Level-2 idempotency seam. TryAdd so a test host / task 105's Redis
        // impl can pre-register a different implementation and win. Singleton
        // because both the NoOp default and the Redis impl are stateless
        // over the injected cache client (no per-request state).
        services.TryAddSingleton<IDispatchIdempotencyService, NoOpDispatchIdempotencyService>();

        return services;
    }
}
