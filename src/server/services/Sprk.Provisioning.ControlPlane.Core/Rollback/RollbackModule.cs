// -----------------------------------------------------------------------------
// RollbackModule.cs
//
// DI composition extension for the §4C rollback surface (task 061).
//
// ADR references:
//   - ADR-010: single extension method registers all rollback services —
//              Program.cs stays god-class-ratchet-clean.
//   - ADR-032: all registrations UNCONDITIONAL — no feature-gate branches.
//              QuarantineRequired -> Quarantined is a domain-authoritative
//              transition, not a feature flag.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// DI composition for the §4C rollback surface (IFailureClassifier +
/// IQuarantineClearService).
/// </summary>
public static class RollbackModule
{
    /// <summary>
    /// Registers <see cref="IFailureClassifier"/> +
    /// <see cref="IQuarantineClearService"/> + ensures a <see cref="TimeProvider"/>
    /// is present (TryAdd — parity with ReconcilerModule; production uses
    /// <see cref="TimeProvider.System"/>).
    /// </summary>
    public static IServiceCollection AddRollbackModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Ensure a TimeProvider is registered even when the reconciler module
        // hasn't been added yet (order-independence).
        services.TryAddSingleton(TimeProvider.System);

        // Failure classifier — stateless policy; singleton.
        services.AddSingleton<IFailureClassifier, FailureClassifier>();

        // Clear-quarantine service — Scoped because it consumes the Scoped
        // IProvisioningRunRepository (CosmosProvisioningRunRepository).
        services.AddScoped<IQuarantineClearService, QuarantineClearService>();

        return services;
    }
}
