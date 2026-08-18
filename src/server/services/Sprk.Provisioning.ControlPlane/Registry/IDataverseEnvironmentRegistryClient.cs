// -----------------------------------------------------------------------------
// IDataverseEnvironmentRegistryClient.cs
//
// L2 CONTROL-PLANE Dataverse registry lookup abstraction (task 042 — H0.5).
//
// PURPOSE:
//   Read-only lookup against the `sprk_dataverseenvironment` registry table
//   used by H0.5 to answer "is there already an environment for this tid?"
//   (spec.md FR-02 re-consent semantics + design.md §4.1 H0.5 row).
//
//   H0.5 is the FIRST handler that needs a registry lookup — Wave C5 will
//   replace the placeholder impl (NullDataverseEnvironmentRegistryClient)
//   with a real Dataverse call. H0.5 depends only on the tid-keyed lookup
//   shape; the registry-write path (H5 output) is a separate concern.
//
// PLACEHOLDER SEMANTICS (Wave C4):
//   NullDataverseEnvironmentRegistryClient returns null for every lookup —
//   H0.5 branches on null == "no existing environment" == treat as first
//   consent (fresh start via H0 enqueue). This is safe: a stale null would
//   result in a duplicate H0 attempt which the ServiceBus MessageId dedup
//   (level-1 idempotency) drops within the dedup window.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist by design:
//     - NullDataverseEnvironmentRegistryClient (placeholder Wave C4)
//     - Real Dataverse-backed impl (Wave C5)
//   Plus test-only stub implementations per unit test. That satisfies the
//   "genuine seam" bar in ADR-010.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Read-only lookup against the <c>sprk_dataverseenvironment</c> registry
/// table. Consumed by H0.5 (consent-capture) for re-consent branching per
/// spec.md FR-02 + design.md §4.1 H0.5 row.
/// </summary>
public interface IDataverseEnvironmentRegistryClient
{
    /// <summary>
    /// Looks up an environment row by customer tenant id (<c>sprk_tenantid</c>).
    /// </summary>
    /// <param name="tenantId">
    /// The customer's Entra tenant id (from the consent callback's <c>tid</c>
    /// claim). MUST be non-empty (§4D I1 no-hardcoded-tenant); the caller is
    /// expected to have validated non-empty before invoking.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The matching registry snapshot, or <c>null</c> when no environment
    /// exists for the given tenant. Callers treat null as "first consent,
    /// fresh start" — see <c>H05ConsentCaptureHandler</c>.
    /// </returns>
    Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Read-only snapshot of a <c>sprk_dataverseenvironment</c> row for
/// re-consent branching. Only the fields H0.5 needs are surfaced —
/// downstream handlers use their own richer projections.
/// </summary>
/// <param name="EnvironmentId">Dataverse row id (as GUID string) for correlation.</param>
/// <param name="CustomerId">Customer short-id (partition key value for Cosmos runs).</param>
/// <param name="TenantId">Customer Entra tenant id (matches the lookup key).</param>
/// <param name="SetupStatus">
/// Registry setup-status value at read time. Values per spec.md FR-02:
/// <c>Ready</c> | <c>Running</c> | <c>WaitingOnGate</c> | <c>Failed</c> |
/// <c>Cancelled</c> | <c>NotStarted</c>. Serialized as the string form so
/// this projection does not compile-depend on a shared enum.
/// </param>
/// <param name="CurrentRunId">
/// The active run id (Cosmos <c>runs</c>-container document id) associated
/// with this environment, if any. Populated when SetupStatus is
/// <c>Running</c> or <c>WaitingOnGate</c>; may be null for terminal states.
/// </param>
public sealed record DataverseEnvironmentRegistrySnapshot(
    string EnvironmentId,
    string CustomerId,
    string TenantId,
    string SetupStatus,
    string? CurrentRunId);
