// -----------------------------------------------------------------------------
// SubscriptionDedication.cs — is this subscription this customer's alone?
//
// T228 (owner D4; ADR-027: one subscription per customer, MUST NOT put two customers in one). The operator creates the
// customer's subscription and types its id at intake, so H1 must not trust it: a subscription already holding another
// customer's stamp resource group (rg-spaarke-{otherId}-{env}), or Spaarke's own (rg-spaarke-platform-*, -shared-*,
// -byok-*), is refused before H1 writes anything (provider registration) and long before H2a deploys. Pure: the ARM
// listing lives in ArmSubscriptionReadinessProbe.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>The resource-group rule behind <see cref="ISubscriptionReadinessProbe.CheckSubscriptionDedicatedAsync"/>.</summary>
public static class SubscriptionDedication
{
    /// <summary>
    /// A Spaarke stamp resource group, <c>rg-spaarke-{id}-{env}</c> (customer.bicep). The id position takes the customerId
    /// standard's shape, so the reserved non-customer ids (platform, shared, byok) match too — and are foreign to any
    /// customer.
    /// </summary>
    private static readonly Regex StampGroup = new(
        @"^rg-spaarke-(?<id>[a-z][a-z0-9]{2,7})-(?<env>[a-z0-9]+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The resource groups in <paramref name="resourceGroupNames"/> that belong to a Spaarke stamp other than
    /// <paramref name="customerId"/>'s, in input order. Other resource groups (not named rg-spaarke-*) are ignored: a
    /// customer subscription may hold the customer's own unrelated resources.
    /// </summary>
    public static IReadOnlyList<string> FindForeignStampGroups(IEnumerable<string> resourceGroupNames, string customerId)
    {
        ArgumentNullException.ThrowIfNull(resourceGroupNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return resourceGroupNames
            .Where(name => StampGroup.Match(name) is { Success: true } m
                           && !string.Equals(m.Groups["id"].Value, customerId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
