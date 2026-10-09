namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/unsecure-project.
/// </summary>
/// <param name="ProjectId">
/// The project whose secure designation was removed — <see cref="Guid.Empty"/> when the record is a matter or work
/// assignment (task 144), so a client reading the legacy field is never told a matter id is a project id. Read
/// <paramref name="RecordType"/> / <paramref name="RecordId"/> instead.
/// </param>
/// <param name="NewOwnerSystemUserId">
/// The <c>systemuser</c> that now owns the record. Ownership moving off the Secure Record business
/// unit's owner team is what actually ends the isolation — the <c>sprk_issecure</c> flag is a label,
/// the ownership is the mechanism.
/// </param>
/// <param name="SharesRevoked">
/// How many POA shares were removed from the record. A secure project's access came entirely from
/// explicit shares; once normal ownership and business-unit access apply, leaving them behind would
/// keep a second, invisible access path alive.
/// </param>
/// <param name="AlreadyUnsecure">
/// <c>true</c> when the project was not secure to begin with. The call is idempotent: a repeat is a
/// 200 that changed nothing, not a 409.
/// </param>
/// <param name="SweepComplete">
/// Whether <paramref name="SharesRevoked"/> is the WHOLE story. Three states, and the distinction
/// between the last two is the point:
/// <list type="bullet">
/// <item><c>true</c> — every share on the record was enumerated and removed.</item>
/// <item><c>false</c> — a sweep RAN but could not account for every row; one or more shares may survive.</item>
/// <item><c>null</c> — NO sweep was attempted (the idempotent already-unsecure path). This response
/// makes no claim either way, and the record may still carry POA rows from another surface.</item>
/// </list>
/// A caller must treat anything other than <c>true</c> as "not vouched for".
/// </param>
/// <param name="RecordType">The record's type token: <c>project</c> | <c>matter</c> | <c>workassignment</c> (task 144).</param>
/// <param name="RecordId">The record whose secure designation was removed (task 144).</param>
/// <remarks>
/// <para><b>Why this exists</b> (ISS-018 / #995, task 108): <c>SharesRevoked = 0</c> was ambiguous in
/// the worst direction — EITHER "no shares" OR "the shares could not be read", because the soft POA
/// read answers an empty list when it fails. A count is only meaningful next to a statement about
/// whether the count is complete. The full argument lives on
/// <c>UnsecureProjectEndpoint.RevokeAllSharesAsync</c>; this summary points there rather than
/// restating it, so the two cannot drift apart.</para>
///
/// <para><b>No default, deliberately.</b> An earlier draft defaulted this to <c>true</c>, and review
/// caught the consequence: the idempotent early-return omitted the argument and so silently claimed a
/// complete sweep for a sweep it never ran — reinstating ISS-018 on the retry that a
/// <c>sweepComplete: false</c> response invites. A completeness flag that defaults to the optimistic
/// answer is fail-OPEN. With no default the compiler forces every construction site to say what it
/// actually knows. The JSON contract is still purely additive; only in-assembly source is affected,
/// and both call sites live in the endpoint.</para>
/// </remarks>
/// <param name="Children">
/// Task 148: what this call did to the record's EXISTING related records — each re-owned OUT of the Secure Record owner team
/// to the owner the ownership rule gives a child of an ordinary record (its business unit's team), its mirrored shares
/// removed — BEFORE the record's own shares were revoked and its flag cleared. On an already-unsecure record it is the pass
/// that completes an earlier unsecure's related records (owner round 11 item 3). A successful response always carries a
/// complete pass; an incomplete one is the <c>sdap.unsecure.children_incomplete</c> error, which leaves the flag set.
/// Additive to the JSON contract.
/// </param>
/// <param name="RelatedSecureRecords">
/// unified-access-control-r2 task 158 (owner round 6): the work assignments and projects FILED UNDER this record that are
/// still secure after the call. A record filed under a secure record became secure itself, and it STAYS secure when that
/// record is unsecured (never auto-unsecure) — the UI offers each one, with a checkbox, to unsecure through
/// <c>alsoUnsecure</c> (or later, on its own). <c>null</c> for a work assignment (nothing is filed under one for this
/// rule) or when the list could not be read (<paramref name="RelatedSecureRecordsUnreadable"/>). Additive.
/// </param>
/// <param name="RelatedRecordsUnsecured">
/// Task 158: one entry per <c>alsoUnsecure</c> record — <c>unsecured</c>, or <c>refused</c> / <c>failed</c> with the reason
/// code (F3's <c>sdap.unsecure.not_permitted</c> / <c>sdap.unsecure.permission_unverifiable</c>,
/// <c>sdap.unsecure.not_related</c>, or the code its own unsecure answered). <c>null</c> when none was asked. Additive.
/// </param>
/// <param name="RelatedSecureRecordsUnreadable">
/// Task 158: <c>true</c> when the records filed under this one could not be read, so <paramref name="RelatedSecureRecords"/>
/// is not known (never reported as "none"). Additive.
/// </param>
public record UnsecureProjectResponse(
    Guid ProjectId,
    Guid NewOwnerSystemUserId,
    int SharesRevoked,
    bool AlreadyUnsecure,
    bool? SweepComplete,
    string RecordType,
    Guid RecordId,
    SecureChildPassSummary? Children = null,
    IReadOnlyList<RelatedSecureRecord>? RelatedSecureRecords = null,
    IReadOnlyList<RelatedUnsecureOutcome>? RelatedRecordsUnsecured = null,
    bool RelatedSecureRecordsUnreadable = false)
{
    /// <summary>
    /// Task 175 fix round 3 (K1): the work assignments and projects below a matter or project, with no access record yet, that
    /// were NOT recorded as inherited before its flag was cleared (past the inline cap of 50, or not written). They stay secure
    /// and the secure-root inheritance job records them. <c>null</c> for a work assignment, or when the records below could not
    /// all be read. Additive.
    /// </summary>
    public int? AccessRecordsNotRecorded { get; init; }
}

/// <summary>A work assignment or project filed under the record, still secure (task 158).</summary>
public record RelatedSecureRecord(string RecordType, Guid RecordId, string? Name);

/// <summary>What happened to one <c>alsoUnsecure</c> record (task 158).</summary>
/// <param name="Outcome"><c>unsecured</c>, <c>refused</c> or <c>failed</c>.</param>
public record RelatedUnsecureOutcome(string RecordType, Guid RecordId, string Outcome, string? ReasonCode, string? Detail);
