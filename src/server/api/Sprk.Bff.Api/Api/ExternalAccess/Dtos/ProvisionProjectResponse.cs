namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/provision-project.
///
/// Reports what provisioning actually did, so the caller can display confirmation and an operator can
/// reconcile if a later step failed.
/// </summary>
/// <remarks>
/// <para><b>Reshaped by task 021 (2026-08-25).</b> Three members were removed because the things they
/// described are no longer created:</para>
/// <list type="bullet">
///   <item><c>AccountId</c> / <c>AccountName</c> — provisioning created a synthetic
///   "External Access — {project}" <c>account</c> per project. Nothing in the external-access model
///   reads an account (firms are <c>sprk_organization</c>), and the column it was aimed at,
///   <c>sprk_externalaccount</c>, is the project's CLIENT lookup. Both the creation and the write are
///   gone.</item>
///   <item><c>WasUmbrellaBu</c> — there is one canonical Secure Record business unit, so there is no
///   longer a create-vs-reuse distinction to report.</item>
/// </list>
/// <para><b>Task 144 (2026-10-01)</b> added <c>RecordType</c> / <c>RecordId</c> (a matter or work assignment can now
/// be provisioned) and changed what <c>OwnerTeamId</c> names: the business unit's NAMED owner team, never its
/// default team. The JSON contract is additive.</para>
/// </remarks>
/// <param name="BusinessUnitId">
/// The canonical Secure Record business unit the record now belongs to, RESOLVED by name — not created. Reported for
/// operator confirmation; it is not written to the record.
/// </param>
/// <param name="BusinessUnitName">
/// The configured name that resolved (<c>SecureRecord:BusinessUnitName</c>, default <c>Secure Record</c>).
/// </param>
/// <param name="OwnerTeamId">
/// The business unit's NAMED, non-default owner team (<c>SecureRecord:OwnerTeamName</c>, default
/// <c>Secure Record Owners</c>), which now owns the record — proven memberless, in a business unit proven to hold no
/// users, before the assignment. This is the security-relevant outcome: no human holds access through that ownership.
/// </param>
/// <param name="OwnerTeamName">Display name of the owner team.</param>
/// <param name="SpeContainerId">
/// The SPE FileStorageContainer provisioned for this record, recorded on it as <c>sprk_containerid</c>. A response
/// containing this id means the write succeeded — if it could not be written the endpoint returns a non-2xx that
/// carries the id instead (ADR-003).
/// </param>
/// <param name="SharedToCreatorSystemUserId">
/// The creating user the record was explicitly shared to (task 061) — on Make Secure, the caller (task 150). Because the
/// owner team has no members, this share is what makes the record reachable at all — a successful response always carries
/// it, except (task 114, owner round 67 item 3: Restricted wins) when that person is flagged external on a Restricted
/// record: nothing is shared to them, this is <see cref="Guid.Empty"/>, and they are named in <c>SkippedPrincipals</c>
/// with <c>sdap.provision.principal_external_on_restricted</c>.
/// </param>
/// <param name="AdditionalPrincipalsShared">
/// How many of the request's optional <c>SharePrincipalIds</c> were also shared to (best-effort). On Make Secure
/// (<c>transition: "make-secure"</c>, which names no colleagues) it counts the record's creator when they were shared to
/// alongside the caller (task 150, round 33 item 1).
/// </param>
/// <param name="RecordType">The provisioned record's type token: <c>project</c> | <c>matter</c> | <c>workassignment</c>.</param>
/// <param name="RecordId">The provisioned record's id.</param>
/// <param name="Resumed">
/// True when this call FINISHED an earlier run that stopped after the owner move (task 133): the record was already
/// owned by the owner team with no container recorded. <c>SharedToCreatorSystemUserId</c> is then the person who created
/// the record — its <c>createdby</c> user, or for an app-created record the BFF-recorded <c>sprk_createdbyperson</c>
/// (owner round 7 item 2) — not necessarily the caller. On Make Secure (<c>transition: "make-secure"</c>, task 150 round
/// 40) it is the caller, as on the forward path, and the record's creator is counted in
/// <c>AdditionalPrincipalsShared</c> (or named in <c>SkippedPrincipals</c>). Additive to the JSON contract.
/// </param>
/// <param name="SkippedPrincipals">
/// Task 143 (owner N6): named colleagues who were NOT shared to, each with a reason — on the record's No Access list
/// (<c>sdap.provision.principal_no_access</c>), or that list could not be checked
/// (<c>sdap.provision.principal_no_access_unverifiable</c>), or (task 150, round 33 item 5: never silent) the share itself
/// failed (<c>sdap.provision.principal_share_failed</c>). On Make Secure the record's creator is reported here the same
/// way. The other colleagues are still shared. Empty when none was skipped. Additive to the JSON contract.
/// </param>
/// <param name="Children">
/// Task 148: what this call did to the record's EXISTING related records (documents, events, to-dos, communications, memos
/// and the rest) — re-owned into the Secure Record owner team and shared with exactly the record's sharees. A successful
/// response always carries a complete pass; an incomplete one is the <c>sdap.provision.children_incomplete</c> error.
/// Additive to the JSON contract.
/// </param>
/// <param name="ChildrenOnly">
/// Task 148: <c>true</c> when the record was ALREADY provisioned and this call only completed its related records (an earlier
/// call's child pass had not finished). Nothing about the record itself changed and no share was written to it, so
/// <c>SharedToCreatorSystemUserId</c> is <see cref="Guid.Empty"/> — its creator's share was proven by the earlier call. Absent
/// work, such a call still answers 409 <c>already_provisioned</c>. Additive to the JSON contract.
/// </param>
/// <param name="FiledRecords">
/// Task 158 (owner round 6): the work assignments and projects FILED UNDER this matter or project, each made secure itself
/// (its own named-team owner, container and creator share) and given this record's sharees. A successful response always
/// carries a complete pass; one that is not complete is the <c>sdap.provision.children_incomplete</c> error. Null for a
/// work assignment (nothing is filed under one for this rule). Additive to the JSON contract.
/// </param>
public record ProvisionProjectResponse(
    Guid BusinessUnitId,
    string BusinessUnitName,
    Guid OwnerTeamId,
    string OwnerTeamName,
    string SpeContainerId,
    Guid SharedToCreatorSystemUserId,
    int AdditionalPrincipalsShared,
    string RecordType,
    Guid RecordId,
    bool Resumed = false,
    IReadOnlyList<ProvisionSkippedPrincipal>? SkippedPrincipals = null,
    SecureChildPassSummary? Children = null,
    bool ChildrenOnly = false,
    SecureFiledRecordsSummary? FiledRecords = null)
{
    /// <summary>
    /// Round 26 item 3 (wired at the batch-4 integration): what moving the record's existing files into its own container
    /// did — counts, plus what a move stated (a source edited after its move, a truncated history, a source kept for
    /// other records). Null for a call that did not reach the files.
    /// </summary>
    public MakeSecureFilesSummary? Files { get; init; }

    /// <summary>
    /// Task 114 (owner round 67: Restricted wins over the last-reader rule). On a Restricted record the person it would be
    /// shared to was flagged external and NOT shared to (named in <see cref="SkippedPrincipals"/>). <c>true</c>: nobody
    /// internal can open the record now - an administrator must share it with an internal user; <c>false</c>: someone
    /// internal can; <c>null</c>: nobody was skipped, or it could not be read (then <see cref="NoInternalReaderMessage"/>
    /// says so). Additive to the JSON contract.
    /// </summary>
    public bool? NoInternalReader { get; init; }

    /// <summary>Task 114: the plain-language sentence for <see cref="NoInternalReader"/> (null when nothing needs saying).</summary>
    public string? NoInternalReaderMessage { get; init; }
}

/// <summary>A named colleague provisioning did not share to, and why (task 143). The message names no entry or reason text.</summary>
public record ProvisionSkippedPrincipal(Guid SystemUserId, string ReasonCode, string Message);
