namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Computes a thread's authorized participant set from Dataverse-derived access (FR-07) — the SINGLE shared
/// access contract for the whole messaging privacy model (design §5). Dataverse is authoritative; ACS
/// membership (task 041 reconcile) and the BFF read-filter (task 042) are both PROJECTIONS of what this
/// service returns, and MUST agree.
/// </summary>
/// <remarks>
/// Derivation by topology + privacy:
/// <list type="bullet">
/// <item><b>Open (record-anchored)</b>: the anchor record's authorized users, derived from
/// <c>MembershipResolverService</c> machinery (ADR-034) applied in the record→users direction. No record
/// access ⇒ no membership.</item>
/// <item><b>Direct (1:1)</b>: the explicit two-participant list the thread declares (task 043), read via
/// <see cref="IThreadExplicitParticipantReader"/>.</item>
/// <item><b>Private</b>: the derived open set PLUS the explicit per-record overlay grants (spike 002
/// option B), read via <see cref="IThreadExplicitParticipantReader"/>.</item>
/// </list>
/// This is a testing seam (ADR-010): consumers get the concrete via DI; unit tests substitute a mock.
/// </remarks>
public interface IThreadMembershipDerivationService
{
    /// <summary>
    /// Derives the authorized participant set for the thread. Returns a non-null set (empty when the thread
    /// is unknown or has no derivable membership). De-duplicated by participant identity.
    /// </summary>
    Task<ThreadAuthorizedSet> DeriveAuthorizedSetAsync(Guid threadId, CancellationToken ct = default);
}
