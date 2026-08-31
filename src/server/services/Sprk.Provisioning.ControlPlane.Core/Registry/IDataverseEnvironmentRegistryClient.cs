// -----------------------------------------------------------------------------
// IDataverseEnvironmentRegistryClient.cs
//
// L2 CONTROL-PLANE Dataverse registry client abstraction (task 042 H0.5 seam;
// task 112 Wave G-1 extended to include the WRITE-path used by task 184's
// H13 Ready-writer swap).
//
// PURPOSE:
//   ONE seam covering both the READ path used by H0.5's re-consent branch
//   (spec.md FR-02 + design.md §4.1 H0.5 row) AND the WRITE path used by
//   H13's E2E acceptance-gate Ready transition (spec.md FR-18 + §15 north-star).
//
//   Both paths target the ADMIN Dataverse env's `sprk_dataverseenvironment`
//   registry table. Path X (task 112 / DS-8): a real impl acquires tokens via
//   `DefaultAzureCredential(ManagedIdentityClientId)` pinned to the L2 UAMI —
//   NO client secret, NO cross-tenant token flow (the admin env lives in
//   Spaarke's own tenant, so plain UAMI suffices).
//
// TWO-METHOD SEAM SHAPE (task 112):
//   1. LookupByTenantIdAsync(tenantId, ct) — READ. Preserved verbatim from
//      task 042's original interface so H0.5's existing tests and DI wiring
//      keep binding without churn (parent-task instruction: "task 112 builds
//      the client; task 122 performs H0.5's swap"). H0.5 flips from the
//      NullDataverseEnvironmentRegistryClient placeholder to the real impl
//      via DI change only.
//   2. UpdateSetupStatusAsync(update, ct) — WRITE. New on task 112. Task 184's
//      real IRegistrySetupStatusUpdater implementation delegates to this
//      method (H13's Ready-transition PATCH) so ALL registry HTTP wiring
//      (auth, URI shape, error taxonomy) lives in ONE place.
//
// NAMING vs POML step 1 wording:
//   POML step 1 wrote `ReadByCustomerAsync(customerId, tenantId)`. Preserved
//   the existing `LookupByTenantIdAsync(tenantId, ct)` shape instead — a
//   Path C pivot (per CLAUDE.md §6.5) — because renaming the method would
//   churn H0.5's handler + 3 test stubs (StubRegistry, ThrowingRegistry,
//   FakeRegistryClient) without adding value: the row is uniquely keyed by
//   `sprk_tenantid` (customerId is not the alt-key). CustomerId only flows
//   as log-correlation metadata on the WRITE path via RegistrySetupStatusUpdate.
//
// PLACEHOLDER SEMANTICS (Wave G-1 rollout ordering):
//   NullDataverseEnvironmentRegistryClient stays as the Null-Object kill-switch
//   fallback per ADR-032 P2 (read returns null / write returns Success no-op
//   with WARN log). Task 122 DI-swaps H0.5 onto the real client; task 184
//   swaps H13 onto its real registry-status updater which in turn calls this
//   client's UpdateSetupStatusAsync.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls exist by design:
//     - NullDataverseEnvironmentRegistryClient (kill-switch fallback per ADR-032)
//     - DataverseEnvironmentRegistryClient (task 112 real impl, MI-native)
//     - Test-only fakes per unit test (StubRegistry, ThrowingRegistry,
//       FakeRegistryClient) — updated same-PR to satisfy the extended contract.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Read + write seam over the <c>sprk_dataverseenvironment</c> admin-tenant
/// registry table. Consumed by H0.5 (READ — re-consent branching per spec.md
/// FR-02 + design.md §4.1 H0.5 row) and by H13's Ready-writer (WRITE — via
/// task 184's <c>IRegistrySetupStatusUpdater</c> real impl delegating to
/// <see cref="UpdateSetupStatusAsync"/>).
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

    /// <summary>
    /// PATCHes a <c>sprk_dataverseenvironment</c> row's <c>sprk_setupstatus</c>
    /// (and optionally clears <c>sprk_currentrunid</c>) by row id. Consumed
    /// by task 184's <c>IRegistrySetupStatusUpdater</c> real impl to land
    /// H13's Ready transition (spec.md FR-18 / §15 north-star).
    /// </summary>
    /// <param name="update">The PATCH request payload — see <see cref="RegistrySetupStatusUpdate"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A discriminated outcome:
    /// <see cref="RegistryUpdateOutcome.Success"/> when the PATCH landed;
    /// <see cref="RegistryUpdateOutcome.NotFound"/> when the target row does
    /// not exist (401 vs 404 disambiguation lives in the impl);
    /// <see cref="RegistryUpdateOutcome.Failure"/> for any other Dataverse
    /// domain error carrying an operator-facing diagnostic.
    /// </returns>
    Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
        RegistrySetupStatusUpdate update,
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

/// <summary>
/// Inputs to a single <see cref="IDataverseEnvironmentRegistryClient.UpdateSetupStatusAsync"/>
/// PATCH. Immutable record. Mirrors the shape of task 055's
/// <c>RegistrySetupStatusUpdateRequest</c> (H13's status-updater seam) so
/// task 184's IRegistrySetupStatusUpdater impl can map field-for-field.
/// </summary>
/// <param name="EnvironmentId">
/// The Dataverse row id (as GUID string) — target of the PATCH. MUST parse
/// as a well-formed GUID; the impl rejects non-GUID input to avoid emitting
/// a malformed OData URI. Sourced from a prior LookupByTenantIdAsync result
/// or an InterStepState carry-over.
/// </param>
/// <param name="SetupStatus">
/// New value for <c>sprk_setupstatus</c> — one of the FR-02 status strings
/// (<c>Ready</c>, <c>Running</c>, <c>WaitingOnGate</c>, <c>Failed</c>,
/// <c>Cancelled</c>, <c>NotStarted</c>). MUST be non-empty.
/// </param>
/// <param name="ClearCurrentRunId">
/// When true, the PATCH also sets <c>sprk_currentrunid</c> to null — the H13
/// green-path behaviour (see task 055 header § "COMPANION UPDATE"). When
/// false, <c>sprk_currentrunid</c> is left untouched.
/// </param>
/// <param name="CustomerIdForLog">Customer id — flows into log lines for correlation. Not sent to Dataverse.</param>
/// <param name="RunIdForLog">Run id — flows into log lines for correlation. Not sent to Dataverse.</param>
public sealed record RegistrySetupStatusUpdate(
    string EnvironmentId,
    string SetupStatus,
    bool ClearCurrentRunId,
    string CustomerIdForLog,
    string RunIdForLog);

/// <summary>
/// Typed outcome of a registry PATCH. Discriminated union: mirror-shape of
/// task 055's <c>RegistrySetupStatusUpdateOutcome</c> to keep task 184's
/// IRegistrySetupStatusUpdater mapping trivial.
/// </summary>
public abstract record RegistryUpdateOutcome
{
    private RegistryUpdateOutcome() { }

    /// <summary>PATCH landed — the row now reflects the requested state.</summary>
    public sealed record Success() : RegistryUpdateOutcome;

    /// <summary>
    /// The target row does not exist. Distinct from Failure so callers can
    /// distinguish "operator pointed at the wrong environmentId" from
    /// "Dataverse rejected the write for a different reason".
    /// </summary>
    public sealed record NotFound(string Diagnostic) : RegistryUpdateOutcome;

    /// <summary>PATCH rejected — Web API domain error carrying an operator-facing diagnostic (HTTP status + response body summary).</summary>
    public sealed record Failure(string Diagnostic) : RegistryUpdateOutcome;
}
