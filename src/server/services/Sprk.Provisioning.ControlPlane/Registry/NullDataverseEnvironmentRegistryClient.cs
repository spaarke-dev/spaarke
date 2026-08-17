// -----------------------------------------------------------------------------
// NullDataverseEnvironmentRegistryClient.cs
//
// L2 CONTROL-PLANE placeholder impl of IDataverseEnvironmentRegistryClient
// (task 042 — H0.5 scaffold). Wave C5 replaces this with a real Dataverse-
// backed impl once the L2 Dataverse OBO/app-only client wiring lands.
//
// SEMANTICS:
//   Every lookup returns null — H0.5's re-consent branch treats null as
//   "no existing environment; treat this callback as first consent for the
//   tenant". This is the SAFE default: a stale null results in a fresh H0
//   enqueue whose Service Bus MessageId dedup (level-1 idempotency) drops
//   duplicates within the dedup window.
//
// WHY NOT DEFER THE ABSTRACTION:
//   The alternative — inject a Func<string, Task<...>> or a plain marker
//   value — would obscure the intent and defer the interface debate to
//   Wave C5 anyway. Landing the interface now with a null-object impl lets
//   H0.5 be fully unit-testable (probes swap in mocked snapshots) without
//   creating a phantom dependency the reader has to hunt for.
//
// LOGGING:
//   Emits a single Warning-level log per lookup so operators running the
//   scaffold notice the null placeholder is active before Wave C5 replaces
//   it. Suppressed to Debug once the real impl ships (bicep app-setting
//   `Registry:UseNullFallback = false`).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Placeholder <see cref="IDataverseEnvironmentRegistryClient"/> that always
/// returns null. Registered in Wave C4; replaced by a Dataverse-backed impl
/// in Wave C5.
/// </summary>
public sealed class NullDataverseEnvironmentRegistryClient : IDataverseEnvironmentRegistryClient
{
    private readonly ILogger<NullDataverseEnvironmentRegistryClient> _logger;

    /// <summary>
    /// Initializes the placeholder. Logger emits a single Warning per lookup
    /// so scaffold environments surface the "no real registry yet" state.
    /// </summary>
    public NullDataverseEnvironmentRegistryClient(
        ILogger<NullDataverseEnvironmentRegistryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — LookupByTenantIdAsync returning null " +
            "for tenantId={TenantId}. Wave C5 must replace this placeholder with a real Dataverse " +
            "lookup before H0.5 can honor the re-consent no-op contract for existing environments.",
            tenantId);

        return Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);
    }
}
