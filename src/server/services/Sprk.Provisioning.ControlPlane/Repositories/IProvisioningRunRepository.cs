// -----------------------------------------------------------------------------
// IProvisioningRunRepository.cs
//
// L2 CONTROL-PLANE state store abstraction for the `spaarke-provisioning/runs`
// Cosmos container (task 037).
//
// DESIGN INVARIANTS enforced at the INTERFACE level (not just implementation):
//
//   1. §4D I3 partition-key predicate discipline (FR-30):
//      EVERY method that touches Cosmos takes `customerId` as its FIRST
//      required parameter. Callers CANNOT accidentally issue a call without
//      the partition key — the compiler prevents it. The paired Wave C6
//      ArchTest scans SDK call sites for missing PartitionKey; this
//      interface shape makes such a violation impossible in code that
//      routes through the repository.
//
//   2. FR-23 I5 same-customer serialization (Cosmos side):
//      <see cref="ReplaceRunAsync"/> takes an explicit `ifMatchEtag` and
//      returns a typed <see cref="ReplaceRunResult"/> discriminated union —
//      Success carries the new stored run + fresh ETag, Conflict carries
//      the CURRENT stored run so the endpoint layer (Wave C5) can return
//      HTTP 409 with the winning state. NO CosmosException leaks past the
//      repository boundary on the concurrency path.
//
//   3. Read-not-found is Nullable, NOT an exception:
//      <see cref="ReadRunAsync"/> returns `null` on 404 (Cosmos SDK's
//      NotFound is expected during initial-provision handshake and during
//      the reconciler's crash-recovery scan). Callers use `is null` /
//      `is not null` — no try/catch discipline required at every call site.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Repositories;

/// <summary>
/// Read/write access to the L2 <c>spaarke-provisioning/runs</c> Cosmos
/// container. All methods require the customer partition key as their first
/// parameter to make cross-partition access a compile-time impossibility
/// (§4D I3 / FR-30).
/// </summary>
public interface IProvisioningRunRepository
{
    /// <summary>
    /// Point-reads a single <see cref="ProvisioningRun"/> document.
    /// </summary>
    /// <param name="customerId">
    /// Partition-key value. Required (throws if null/whitespace) — §4D I3
    /// forbids cross-partition reads.
    /// </param>
    /// <param name="runId">Document id (equals <see cref="ProvisioningRun.RunId"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The stored run with its current server ETag (via
    /// <see cref="ProvisioningRunReadResult"/>), or <c>null</c> when no such
    /// document exists in the customer's partition.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="customerId"/> or <paramref name="runId"/>
    /// is null or whitespace.
    /// </exception>
    Task<ProvisioningRunReadResult?> ReadRunAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new <see cref="ProvisioningRun"/> document. Fails if a
    /// document with the same <see cref="ProvisioningRun.RunId"/> already
    /// exists in the customer's partition (409 -> <see cref="InvalidOperationException"/>).
    /// </summary>
    /// <param name="run">
    /// Run to create. Both <see cref="ProvisioningRun.CustomerId"/> and
    /// <see cref="ProvisioningRun.RunId"/> MUST be non-empty; the caller is
    /// responsible for assigning them (typically <see cref="Guid.NewGuid"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored run with its assigned server ETag.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="run"/>'s <see cref="ProvisioningRun.CustomerId"/>
    /// or <see cref="ProvisioningRun.RunId"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a document with the same id already exists in the
    /// customer's partition (Cosmos 409 Conflict). Callers that want an
    /// upsert-shaped API should call this method inside a try/catch or use
    /// a future <c>UpsertRunAsync</c> extension.
    /// </exception>
    Task<ProvisioningRunReadResult> CreateRunAsync(
        ProvisioningRun run,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing <see cref="ProvisioningRun"/> document using
    /// ETag-based optimistic concurrency (FR-23 I5). On concurrency
    /// conflict, returns a typed <see cref="ReplaceRunResult.Conflict"/>
    /// carrying the CURRENT stored run so the caller (endpoint layer / Wave
    /// C5) can return HTTP 409 with the winning state — no
    /// <c>CosmosException</c> leaks past this method.
    /// </summary>
    /// <param name="run">
    /// Run to persist. <see cref="ProvisioningRun.CustomerId"/> is the
    /// partition key; <see cref="ProvisioningRun.RunId"/> is the document
    /// id.
    /// </param>
    /// <param name="ifMatchEtag">
    /// The ETag returned by the most recent <see cref="ReadRunAsync"/> or
    /// prior successful write. Required (throws if null/whitespace).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="ReplaceRunResult.Success"/> with the new stored run + fresh
    /// ETag on the happy path; <see cref="ReplaceRunResult.Conflict"/> with
    /// the current stored run when the server ETag has advanced;
    /// <see cref="ReplaceRunResult.NotFound"/> when the document has been
    /// deleted between read and write.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ifMatchEtag"/> is null/whitespace, or when
    /// <paramref name="run"/>'s <see cref="ProvisioningRun.CustomerId"/> or
    /// <see cref="ProvisioningRun.RunId"/> is null/whitespace.
    /// </exception>
    Task<ReplaceRunResult> ReplaceRunAsync(
        ProvisioningRun run,
        string ifMatchEtag,
        CancellationToken cancellationToken);
}

/// <summary>
/// A stored <see cref="ProvisioningRun"/> paired with its server ETag. The
/// caller uses the ETag on subsequent writes to enforce optimistic
/// concurrency (FR-23 I5).
/// </summary>
/// <param name="Run">The stored run document (may reflect server-side mutations if any).</param>
/// <param name="ETag">Server-assigned ETag — pass to <see cref="IProvisioningRunRepository.ReplaceRunAsync"/> as <c>ifMatchEtag</c>.</param>
public sealed record ProvisioningRunReadResult(ProvisioningRun Run, string ETag);

/// <summary>
/// Discriminated result of <see cref="IProvisioningRunRepository.ReplaceRunAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="Conflict"/> | <see cref="NotFound"/>.
/// Callers pattern-match to decide the HTTP status the endpoint returns
/// (Wave C5) — the repository never throws on the concurrency path.
/// </summary>
public abstract record ReplaceRunResult
{
    private ReplaceRunResult() { }

    /// <summary>The write succeeded. <paramref name="Run"/> reflects the new stored state; <paramref name="ETag"/> is the fresh server ETag.</summary>
    public sealed record Success(ProvisioningRun Run, string ETag) : ReplaceRunResult;

    /// <summary>
    /// ETag mismatch — a concurrent writer advanced the document between
    /// this caller's most recent read and this write. <paramref name="Current"/>
    /// is the CURRENT stored run (freshly point-read on the server side);
    /// the endpoint layer maps this to HTTP 409 (FR-23 I5).
    /// </summary>
    public sealed record Conflict(ProvisioningRunReadResult Current) : ReplaceRunResult;

    /// <summary>The document was deleted between the caller's read and this write. Rare, but distinguished from Conflict so callers can decide whether to recreate.</summary>
    public sealed record NotFound() : ReplaceRunResult;
}
