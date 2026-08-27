// -----------------------------------------------------------------------------
// SubscriptionReadinessOptions.cs
//
// HANDLER-04 (Wave 2 pre-dispatch remediation 2026-08-27) — F6 verbatim
// absorption. Bound options for <see cref="H1SubscriptionReadinessHandler"/>
// carrying the canonical required-resource-provider list H1 registers +
// polls on the target subscription BEFORE H2a's ~20 min Bicep deploy
// would otherwise fail with `MissingSubscriptionRegistration`.
//
// PUNCHLIST QUOTE (HANDLER-04): "Extend ArmSubscriptionReadinessProbe to
// accept a required-providers list from BicepInfraDeployOptions (canonical
// list of ~10 RPs derived from the Bicep templates)." Wave 2 landing
// deviates from the exact wording: a dedicated SubscriptionReadinessOptions
// is a better home (H1 lives in Preflight/SubscriptionReadiness, not
// BicepInfraDeploy; H1 does NOT depend on BicepInfraDeployOptions
// elsewhere and adding that dependency would leak infra-deploy config
// into the readiness gate). The provider list content matches the punchlist
// intent exactly.
//
// DEFAULT PROVIDER LIST — derived from the customer.bicep / model1-shared.bicep
// template composition (task 044 IBicepTemplateInspector's scan surface):
//   - Microsoft.KeyVault       (task 044 kv module, spec §7.9)
//   - Microsoft.Storage        (task 044 storage module)
//   - Microsoft.ServiceBus     (task 044 service-bus module, spec §11 R1)
//   - Microsoft.DocumentDB     (task 044 cosmos module, spec §7.4)
//   - Microsoft.CognitiveServices  (openai + docintel modules — H2a task 128)
//   - Microsoft.Search         (H2b handler — separate module family)
//   - Microsoft.Web            (task 044 app-service module — H4 T1 target)
//   - Microsoft.ManagedIdentity (task 028 uami module — T1 identity)
//   - Microsoft.ManagedServices (Lighthouse — used by existing
//                                CheckLighthouseDelegationAsync for Model 2)
//   - Microsoft.Insights       (App Insights baseline)
//   Redis (Microsoft.Cache) is NOT included — per Q-E FR-12 Redis is
//   per-environment, not per-customer; H2a's template inspector explicitly
//   rejects a per-customer Redis. SignalR (Microsoft.SignalRService) is
//   conditional (ADR-032 feature flag) and NOT part of the always-required
//   list — its Bicep module will register on-demand if a customer's
//   signalrEnabled flag flips true.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>
/// Bound options for <see cref="H1SubscriptionReadinessHandler"/>.
/// Configuration key: <c>SubscriptionReadiness</c>. Sensible defaults
/// mirror the H2a Bicep template composition — an operator override is
/// only needed for out-of-band experimentation.
/// </summary>
public sealed class SubscriptionReadinessOptions
{
    /// <summary>The canonical namespaces H1 registers + polls on the target subscription BEFORE H2a would fail with MissingSubscriptionRegistration.</summary>
    public IList<string> RequiredResourceProviders { get; set; } = new List<string>
    {
        "Microsoft.KeyVault",
        "Microsoft.Storage",
        "Microsoft.ServiceBus",
        "Microsoft.DocumentDB",
        "Microsoft.CognitiveServices",
        "Microsoft.Search",
        "Microsoft.Web",
        "Microsoft.ManagedIdentity",
        "Microsoft.ManagedServices",
        "Microsoft.Insights",
    };

    /// <summary>
    /// Total time budget for the register-and-poll cycle. Default 5 min per
    /// punchlist (`polls until registrationState == Registered or 5min
    /// timeout`). ARM's own registration steady-state is typically
    /// under 30 s per RP on a fresh subscription; the 5 min ceiling
    /// absorbs cross-region propagation lag.
    /// </summary>
    public TimeSpan PollTotalTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Interval between successive polls. Default 5 s — small enough to
    /// return quickly on the common case where registration completes in
    /// under 30 s; large enough to avoid ARM throttling on the poll loop.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
