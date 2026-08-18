// -----------------------------------------------------------------------------
// NullSubscriptionReadinessProbe.cs
//
// L2 CONTROL-PLANE placeholder impl of ISubscriptionReadinessProbe (task 043 —
// Wave C4 scaffold). Wave C5 replaces this with a real ARM-backed impl
// (Azure.ResourceManager SDK OR `az` shell-out) once the L2 App Service UAMI
// has Reader RBAC granted on each customer subscription.
//
// SEMANTICS:
//   Every check returns Passed=true — the H1 handler treats "no probe
//   evidence yet" as "readiness assumed" for the Wave C4 scaffold. This is
//   the SAFE default for dev-env exercise:
//     - A stale Pass in a real environment would still be caught downstream:
//       H2a (task 044) attempts real ARM resource creation and will fail
//       fast if the subscription is unreachable OR Lighthouse delegation is
//       missing. H1 is a cheap pre-Bicep gate, not the only gate.
//     - The Wave C5 real impl SHOULD default to Fail if it cannot reach ARM
//       — the semantic flips from "assume ready" (dev scaffold) to "prove
//       ready" (production).
//
// WHY NOT LEAVE THE INTERFACE UNIMPLEMENTED:
//   Following the same rationale as NullDataverseEnvironmentRegistryClient
//   (task 042): landing the interface + a null-object impl now lets H1 be
//   fully unit-testable (probe swaps in mocked results) without creating a
//   phantom dependency that Wave C5 has to backfill. The DI registration
//   is a one-line swap when the real impl ships.
//
// LOGGING:
//   Emits a single Warning-level log per invocation so operators running the
//   scaffold notice the null placeholder is active before Wave C5 replaces
//   it. Suppressed to Debug once the real impl ships (bicep app-setting
//   `SubscriptionReadiness:UseNullFallback = false`).
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>
/// Placeholder <see cref="ISubscriptionReadinessProbe"/> that returns Passed
/// for every check. Registered in Wave C4; replaced by an ARM-backed impl
/// in Wave C5.
/// </summary>
public sealed class NullSubscriptionReadinessProbe : ISubscriptionReadinessProbe
{
    private readonly ILogger<NullSubscriptionReadinessProbe> _logger;

    /// <summary>
    /// Initializes the placeholder. Logger emits a single Warning per check
    /// so scaffold environments surface the "no real probe yet" state.
    /// </summary>
    public NullSubscriptionReadinessProbe(
        ILogger<NullSubscriptionReadinessProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<SubscriptionReadinessCheckResult> CheckSubscriptionReachableAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        _logger.LogWarning(
            "NullSubscriptionReadinessProbe in use — CheckSubscriptionReachableAsync returning " +
            "Passed=true for subscriptionId={SubscriptionId} tenantId={TenantId}. Wave C5 must " +
            "replace this placeholder with a real ARM-backed lookup before H1 can honor its FR-03 " +
            "'az account show succeeds against target sub' acceptance criterion.",
            subscriptionId, tenantId);

        return Task.FromResult(new SubscriptionReadinessCheckResult(
            Passed: true,
            Diagnostic: "Wave C4 placeholder — subscription reachability assumed. " +
                        "Real ARM verification lands in Wave C5.",
            Evidence: null));
    }

    /// <inheritdoc/>
    public Task<SubscriptionReadinessCheckResult> CheckLighthouseDelegationAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        _logger.LogWarning(
            "NullSubscriptionReadinessProbe in use — CheckLighthouseDelegationAsync returning " +
            "Passed=true for subscriptionId={SubscriptionId} tenantId={TenantId}. Wave C5 must " +
            "replace this placeholder with a real ARM Microsoft.ManagedServices/registrationAssignments " +
            "lookup before H1 can honor its FR-03 'Lighthouse RG scope accessible' acceptance for " +
            "CustomerOwned tenancy.",
            subscriptionId, tenantId);

        return Task.FromResult(new SubscriptionReadinessCheckResult(
            Passed: true,
            Diagnostic: "Wave C4 placeholder — Lighthouse delegation assumed present. " +
                        "Real ARM registrationAssignments lookup lands in Wave C5.",
            Evidence: (JsonElement?)null));
    }
}
