// -----------------------------------------------------------------------------
// IResourceNameAvailabilityProbe.cs
//
// HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27) — F10 verbatim
// absorption. Checks the globally-namespaced Azure resource names H2a's
// customer.bicep + model1-shared.bicep templates deploy — Storage account,
// Key Vault, App Service, Service Bus, Cosmos, Azure OpenAI account, AI
// Search — BEFORE the ~20 min Bicep deploy fires and burns the window on
// a name collision (F10 verbatim: "burned 16m35s on the Session 2 first
// deploy because a Service Bus `-sb` suffix was already reserved globally").
//
// SCOPE (Wave 2 minimum viable landing):
//   The seam abstracts the check-name-availability calls; the production
//   impl (<see cref="ArmResourceNameAvailabilityProbe"/>) covers the two
//   highest-risk resource kinds — Storage account + Key Vault — where
//   Session 2 actually saw collisions. Additional kinds (Service Bus,
//   Cosmos, App Service, OpenAI account, AI Search) can be added
//   incrementally via the same probe method — the interface is stable.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls from day 1: the production ARM-backed impl + a test-only
//   fake used by H2a handler tests to simulate all four outcome
//   permutations without live ARM calls.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Verifies each globally-namespaced resource name is AVAILABLE (not
/// already taken by another tenant / subscription) BEFORE H2a's Bicep
/// deploy tries to create the resource + fails 90-180s in with a name
/// conflict.
/// </summary>
public interface IResourceNameAvailabilityProbe
{
    /// <summary>
    /// Checks each <paramref name="request"/> entry via the resource-kind's
    /// check-name-availability endpoint. Returns a single aggregated
    /// <see cref="ResourceNameAvailabilityResult"/> — <c>AllAvailable</c>
    /// when every checked name is free; <c>Conflict</c> with the FIRST
    /// unavailable name + its reason string when any collision exists.
    /// Domain outcomes never throw; only unanticipated infrastructure
    /// faults propagate.
    /// </summary>
    Task<ResourceNameAvailabilityResult> CheckAvailabilityAsync(
        ResourceNameAvailabilityRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Input for <see cref="IResourceNameAvailabilityProbe.CheckAvailabilityAsync"/> —
/// the subscription scope + the list of globally-namespaced names to
/// verify. Names are precomputed from the H2a naming convention
/// (customer.bicep line ~140: <c>rg-spaarke-{customerId}-{env}</c>; KV +
/// Storage + Service Bus follow the same pattern) by the caller so the
/// probe stays naming-convention-agnostic.
/// </summary>
/// <param name="SubscriptionId">Target Azure subscription id.</param>
/// <param name="Names">List of (kind, name) pairs to check.</param>
public sealed record ResourceNameAvailabilityRequest(
    string SubscriptionId,
    IReadOnlyList<ResourceNameCheckEntry> Names);

/// <summary>
/// One (resource kind, requested name) tuple in a
/// <see cref="ResourceNameAvailabilityRequest"/>. Kind values are
/// controlled by <see cref="ResourceNameKind"/> so the probe can dispatch
/// to the correct check-name endpoint.
/// </summary>
public sealed record ResourceNameCheckEntry(
    ResourceNameKind Kind,
    string RequestedName);

/// <summary>
/// Globally-namespaced Azure resource kinds Wave 2's HANDLER-05 covers.
/// Extend by adding a new value + a case in
/// <see cref="ArmResourceNameAvailabilityProbe"/>.
/// </summary>
public enum ResourceNameKind
{
    /// <summary>Storage account — Microsoft.Storage/storageAccounts (globally unique in Azure).</summary>
    StorageAccount = 1,

    /// <summary>
    /// Service Bus namespace — Microsoft.ServiceBus/namespaces (globally
    /// unique in Azure). F10 verbatim in the punchlist: SESSION 2 first
    /// deploy burned 16m35s because a Service Bus `-sb` suffix was already
    /// reserved globally.
    /// </summary>
    ServiceBusNamespace = 2,

    /// <summary>
    /// Key Vault — Microsoft.KeyVault/vaults (globally unique in Azure).
    /// NOT covered by the Wave-2 production impl because
    /// Azure.ResourceManager.KeyVault is not currently a project dependency
    /// (adding it inflates publish size). Future incremental add:
    /// reference the package + add a case in
    /// <see cref="ArmResourceNameAvailabilityProbe.CheckAvailabilityAsync"/>.
    /// H2a's precompute currently omits KeyVault entries from the request
    /// list so the probe's "unknown kind" branch never fires in production.
    /// </summary>
    KeyVault = 3,
}

/// <summary>Result of one <see cref="IResourceNameAvailabilityProbe.CheckAvailabilityAsync"/> invocation.</summary>
public abstract record ResourceNameAvailabilityResult
{
    /// <summary>Every checked name is available.</summary>
    public sealed record AllAvailable : ResourceNameAvailabilityResult;

    /// <summary>
    /// At least one name is unavailable. Carries the FIRST-observed
    /// conflict (kind + name + Azure-returned reason) so the operator
    /// diagnostic is specific.
    /// </summary>
    /// <param name="Kind">Resource kind that collided.</param>
    /// <param name="ConflictingName">The unavailable name.</param>
    /// <param name="Reason">Azure's returned unavailability reason (e.g. "AlreadyExists" / "AccountNameInvalid").</param>
    public sealed record Conflict(
        ResourceNameKind Kind,
        string ConflictingName,
        string Reason) : ResourceNameAvailabilityResult;
}
