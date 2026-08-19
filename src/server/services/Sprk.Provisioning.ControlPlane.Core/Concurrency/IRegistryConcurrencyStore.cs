// -----------------------------------------------------------------------------
// IRegistryConcurrencyStore.cs
//
// L2 CONTROL-PLANE inner seam beneath ICustomerRunGuard (task 059, Wave C5).
//
// PURPOSE:
//   Splits the guard into (a) decision logic in <see cref="CustomerRunGuard"/>
//   and (b) Dataverse-Web-API mechanics in a swappable seam. This lets the
//   unit test suite exercise every decision path — idempotent same-runId
//   acquire, cross-run conflict, ETag race, release-mismatch — against an
//   in-memory store WITHOUT mocking <c>HttpMessageHandler</c>, which is
//   forbidden by ADR-038 §5.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11):
//   Two implementations exist by design:
//     - <see cref="DataverseRegistryConcurrencyStore"/> — production impl
//       (Dataverse Web API against <c>sprk_dataverseenvironments</c>).
//     - Test-only in-memory impl (`InMemoryRegistryConcurrencyStore` in the
//       L2 test project).
//   Two implementations + ADR-038 §5 unit-test mandate satisfies the "genuine
//   seam" bar. This is NOT the null-object kill-switch pattern (ADR-032) — the
//   kill-switch lives on <see cref="CustomerRunGuardOptions.Enabled"/>, above
//   this seam.
//
// STORE-LEVEL CONTRACTS:
//   The store does NOT implement the "idempotent same-runId Success" or "409
//   with reason code" semantics — those live in <see cref="CustomerRunGuard"/>.
//   The store returns primitive Dataverse-shaped outcomes only:
//   present / absent / value + ETag / precondition-failed / not-found /
//   transient failure. All policy is one layer up.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// Inner seam beneath <see cref="ICustomerRunGuard"/>. Owns the Dataverse
/// Web API mechanics for reading <c>sprk_currentrunid</c> and applying
/// If-Match-guarded PATCH writes. Consumed by <see cref="CustomerRunGuard"/>;
/// two impls exist (Dataverse-backed production, in-memory tests).
/// </summary>
public interface IRegistryConcurrencyStore
{
    /// <summary>
    /// Looks up the customer's registry row and returns a snapshot of the
    /// current <c>sprk_currentrunid</c> value plus the row's ETag for
    /// If-Match writes.
    /// </summary>
    /// <returns>
    /// A <see cref="LookupOutcome"/> discriminated union:
    /// <see cref="LookupOutcome.Found"/> (row exists, includes value + ETag),
    /// <see cref="LookupOutcome.NotFound"/> (no registry row for the customer),
    /// or <see cref="LookupOutcome.TransientFailure"/> (transport/auth/etc).
    /// </returns>
    Task<LookupOutcome> LookupAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts an If-Match-guarded PATCH of <c>sprk_currentrunid</c> from
    /// <c>null</c> to <paramref name="newRunId"/> on the row identified by
    /// <paramref name="environmentRowId"/>. The <paramref name="ifMatchEtag"/>
    /// is the ETag returned by the preceding <see cref="LookupAsync"/> call —
    /// a stale ETag surfaces as <see cref="WriteOutcome.PreconditionFailed"/>.
    /// </summary>
    Task<WriteOutcome> TrySetIfNullAsync(
        Guid environmentRowId,
        string newRunId,
        string ifMatchEtag,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts an If-Match-guarded PATCH of <c>sprk_currentrunid</c> from
    /// its current value to <c>null</c> on the row identified by
    /// <paramref name="environmentRowId"/>. The store does NOT enforce
    /// "current-value equality" itself — that's <see cref="CustomerRunGuard"/>'s
    /// job (it performs a Lookup first + compares). This store method's role
    /// is the write-side mechanics.
    /// </summary>
    Task<WriteOutcome> TryClearAsync(
        Guid environmentRowId,
        string ifMatchEtag,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of <see cref="IRegistryConcurrencyStore.LookupAsync"/>.
/// </summary>
public abstract record LookupOutcome
{
    private LookupOutcome() { }

    /// <summary>
    /// The registry row was found. <paramref name="CurrentRunId"/> may be
    /// null (unclaimed) or a non-empty run identifier (currently claimed).
    /// <paramref name="ETag"/> is the row version for If-Match writes;
    /// stored as returned by Dataverse (opaque string, quotes included).
    /// </summary>
    public sealed record Found(
        Guid EnvironmentRowId,
        string? CurrentRunId,
        string ETag) : LookupOutcome;

    /// <summary>No <c>sprk_dataverseenvironment</c> row exists for the customer.</summary>
    public sealed record NotFound(string CustomerId) : LookupOutcome;

    /// <summary>Transport / auth / unexpected-response failure.</summary>
    public sealed record TransientFailure(string CustomerId, string Diagnostic) : LookupOutcome;
}

/// <summary>
/// Result of a store-level PATCH (either <see cref="IRegistryConcurrencyStore.TrySetIfNullAsync"/>
/// or <see cref="IRegistryConcurrencyStore.TryClearAsync"/>).
/// </summary>
public abstract record WriteOutcome
{
    private WriteOutcome() { }

    /// <summary>The PATCH succeeded (Dataverse returned 204 No Content).</summary>
    public sealed record Success : WriteOutcome
    {
        public static readonly Success Instance = new();
    }

    /// <summary>
    /// The If-Match ETag was stale — Dataverse returned 412 Precondition Failed.
    /// The caller should re-read via Lookup and decide whether to retry.
    /// </summary>
    public sealed record PreconditionFailed : WriteOutcome
    {
        public static readonly PreconditionFailed Instance = new();
    }

    /// <summary>The row does not exist (Dataverse returned 404 Not Found).</summary>
    public sealed record NotFound : WriteOutcome
    {
        public static readonly NotFound Instance = new();
    }

    /// <summary>Transport / auth / unexpected-response failure.</summary>
    public sealed record TransientFailure(string Diagnostic) : WriteOutcome;
}
