using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/provision-project
///
/// Makes a <c>sprk_project</c>, <c>sprk_matter</c> or <c>sprk_workassignment</c> secure and provisions what a secure
/// record needs (task 144 widened it from projects only). Called by the Create Project wizard immediately after
/// creating the project with the Secure toggle on, and by a Write holder for any root.
///
/// <para><b>Task 150: this endpoint is the only writer of <c>sprk_issecure = true</c>.</b> The column is field-secured
/// (only the BFF application user may create or update it — <c>scripts/Set-SecureFlagFieldSecurity.ps1</c>), the client
/// no longer writes it, and Step 4.1 sets it as the first write, read back. A record already flagged (an older client,
/// or a row created before task 150) is provisioned exactly like an unflagged one.</para>
///
/// Provisioning sequence (task 133 reordered the share around the owner move — C11):
///   1. Confirm the record exists (flagged or not — task 150)
///   2. Resolve the ONE canonical Secure Record business unit, BY NAME, from configuration
///   3. Resolve that BU's NAMED, non-default owner team (<c>SecureRecord:OwnerTeamName</c>), and prove it has ZERO
///      members and that ZERO systemusers sit in the BU — before any mutation (<see cref="SecureRecordOwnerTeam"/>)
///   4. Read the marker (see <see cref="RootRow"/>): owned by the team WITH a container → 409, nothing written; owned
///      by the team WITHOUT a container → RESUME (below); otherwise continue. Then, still before any mutation: the
///      SPE container type is configured; a container ALREADY recorded on the record is classified (a business unit's
///      or this BFF's configured shared container is replaced, one another root records is refused, the record's own
///      is KEPT — never orphaned); the caller's systemuserid (WhoAmI) — and, for a record NOT yet flagged secure, that
///      the caller created it (owner round 10 item 10, task 150: <c>createdby</c> when a person, else
///      <c>sprk_createdbyperson</c>; an already-flagged record stays on the delegation gate (Write and Share)) — the record's current owner, the
///      OWN owner of each row the owner move cascades to (<see cref="AssignCascadeChildOwners"/> — complete or refused,
///      task 133 c1), and the creator's current share (complete read or nothing — a record that keeps its own container
///      is refused when it cannot be read, task 133 r1)
///   4.1 Set <c>sprk_issecure = true</c> and read it back (task 150) — the FIRST write; skipped when already true
///   4.2 A replaced SHARED container is unlinked from the record (task 133 r1), so the team never owns a record that
///      records shared storage
///   4.5 SHARE-FIRST: give the creator their share while the record is still where it was created
///   5. Assign the record's owner to that team, and verify the assignment by reading it back
///   5.5 Prove the creator holds exactly <see cref="CreatorAccessRights"/> now that the team owns it (re-issue if the
///       move dropped it). If that cannot be proven, COMPENSATE: move the record back to its pre-call owner, read it
///       back, put each row the move cascaded to back on its OWN owner (task 133 c1, owner round 10 item 4), and put the
///       creator's share back to what it was. Then share any named colleagues.
///   6. Create the record's own SPE container — unless it already has its own (kept)
///   7. Record the container on the record — and FAIL if that record cannot be written
///   8. (task 148) Bring the record's EXISTING related records into isolation — re-owned to the team by the ownership
///      rule, then shared with exactly the record's sharees (task 149's synchronizer). Incomplete → children_incomplete;
///      the next call (the record is provisioned) completes them in the already-provisioned branch, which otherwise still
///      answers 409
///
/// <para><b>The one rule this ordering serves</b> (owner, session 27 round 3, S5): a secure record must always keep
/// at least one person who can open it. A memberless team owns it, so after step 5 the creator's share is the only
/// way in. Every single failure therefore ends either with the record back in its pre-call ownership (the creator
/// keeps the access they had, and can retry through the normal delegation gate (Write and Share)) or with the creator's share in place.
/// Only a DOUBLE failure — the share and then the compensating move — can leave a record nobody opens; that state has
/// its own reason code, a CRITICAL log line, and a recovery an administrator (who holds Write and Share) can run: a provisioning
/// call that resumes it — or, for a record that keeps its own container, a Manage Access share (next paragraph).</para>
///
/// <para><b>A record that KEEPS its own container cannot be resumed</b> (task 133 r1, verifier round 4). Once the team
/// owns it, "owned by the team AND a container recorded" is the 409 marker whether or not the run finished — nothing a
/// later call can observe tells the two apart. So such a record is moved only once the creator's share can be set up
/// first (an unreadable share set refuses before any write, rather than moving it with no share), and every failure
/// after its move says the next call answers <c>already_provisioned</c> and names the recovery that works: an
/// administrator shares it through Manage Access. The extension <c>containerKept</c> marks those responses. Making such
/// a record resumable would change what the marker means — decided as shipped by owner round 10 item 5 (task 133 note
/// §14.3).</para>
///
/// <para><b>RESUME.</b> A record owned by the team with no container recorded did not finish: steps 5.5/6/7 did not
/// all complete. A call on it ensures the share for the record's creator — <c>createdby</c> when that is a usable
/// person, otherwise the server-stamped <c>sprk_createdbyperson</c> (owner round 7 item 2: an app-only create records
/// its person there) — never for the caller, unless the caller IS that person (owner decision F8) — then runs steps 6
/// and 7. Every resume step is an "ensure" keyed on
/// observed state, so it re-runs whatever the forward path would. Two rules keep a resume from widening the access
/// list (task 133 verifier round 1): named colleagues are accepted only from the record's creator (anyone else is
/// refused before any write, and adds people through Manage Access); and when neither <c>createdby</c> nor
/// <c>sprk_createdbyperson</c> is a usable person — absent, disabled, an application user or unreadable — the resume
/// is REFUSED with its own reason code, zero grants and zero containers (the closed acceptance criterion; verifier
/// round 2 withdrew a round-1 path that completed when another person already held a share, because it changed that
/// owner-decided contract without the owner). A resume of a record NOT flagged secure is held to the same creator rule
/// as the forward path (owner round 10 item 10; task 150 verifier c1 item 4) — before any write; a flagged one stays on
/// the delegation gate (Write and Share).</para>
///
/// <para><b>RESUME through Make Secure</b> (<c>transition: "make-secure"</c>; task 150, round 40 items 1 and 2). The form's
/// Make Secure command finishes such a record with the forward Make Secure path's rules, minus the move it no longer
/// needs: the CALLER is identified (WhoAmI) and their effective rights read in the same step, checked against the No
/// Access list and shared at the creator's level — never lower than the access they held, by share, ownership or role
/// (round 46 item 1) — and that share is the one proven; the record's creator is shared to beside them, named in
/// <c>skippedPrincipals</c> when they are not. So a Make Secure that failed after its first write is finished by the same
/// caller from the same command, flagged or not, and whoever runs it keeps access on both paths (round 40 item 2 removed
/// the asymmetry; round 46 item 3 confirmed the full forward rule set). Without the transition a resume keeps F8 above:
/// the creator only.</para>
///
/// <para><b>Rollback is for ownership and shares only.</b> Steps 4.5–5.5 are undone on failure, because the undo
/// restores the access state every earlier refusal already leaves (the creator's own). Ownership means the record's AND
/// that of the rows its owner move cascades to (SharePoint document locations and documents on a project or matter):
/// the move back cascades like the move out, so each of those is put back on the owner it had before the call — a
/// child that cannot be is named, with the call that puts it back (task 133 c1). Step 4.2 is not: a shared
/// container stays unlinked from a secure-flagged record, which refuses uploads until it has its own (fail closed —
/// before it, the record's uploads went to shared storage). Steps 6 and 7 are NOT undone:
/// moving a record OUT of the Secure Record business unit because storage failed would turn a storage failure into a
/// disclosure. A container failure leaves a secured, shared record that the next call resumes; the only artifact a
/// failed run can strand is an empty SPE container, which its error body names (ADR-003). <c>sprk_issecure</c> is
/// written once, at Step 4.1, and never cleared here on any path: a record that failed provisioning after that write
/// stays flagged, so uploads to it fail closed (<c>RecordContainerResolver</c>); one refused before it was never
/// flagged, and the client uploads nothing to a secure-requested record whose provisioning did not succeed.</para>
///
/// Authentication: Azure AD JWT (RequireAuthorization via the adminGroup).
/// ADR-001: Minimal API — no controllers.
/// ADR-003: fail closed; a run that cannot record what it created does not return success.
/// ADR-007: SPE container created through the SpeFileStore facade.
/// ADR-008: Authorization applied at route group level in ExternalAccessEndpoints.
/// ADR-010: Concrete DI injections.
/// </summary>
/// <remarks>
/// <para><b>TASK 144 (C10 part 1, #967) — a NAMED owner team, not the business unit's default team.</b> Until
/// 2026-10-01 this endpoint assigned every secure project to the Secure Record BU's DEFAULT owner team and checked only
/// that one such team existed. Dataverse maintains a default team's membership from each user's
/// <c>businessunitid</c> and it cannot be curated, so any user moved into the Secure Record BU silently became a
/// member of the team owning every secure record, and nothing checked. Ownership now goes to a named team that this
/// endpoint proves memberless, in a BU it proves user-free (a user in the BU would read every secure record by DEPTH
/// whatever team owned them). Matters and work assignments, which carry <c>sprk_issecure</c> too but were never
/// re-owned, are provisioned exactly as projects are.</para>
///
/// <para><b>RE-SCOPED 2026-08-25 (task 021).</b> This endpoint previously created a business unit
/// per secure project and an <c>account</c> per secure project, then stamped three references onto
/// the project using three column names that do not exist — and swallowed the resulting 400. All of
/// that is gone. What replaced it, and why, in order of how badly each mattered:</para>
///
/// <para><b>1. BU-per-project contradicted the design and escaped its own guardrail.</b> design.md
/// §5.1 says verbatim "no BU-per-project proliferation" and specifies ONE <c>Secure Record</c>
/// business unit (named <c>Secure Project</c> until task 121 renamed it, 2026-09-29). The old code
/// created a child BU per project and parented it to the ROOT BU — so
/// those BUs sat OUTSIDE the BU that NFR-05's standing assertion guards.</para>
///
/// <para><b>2. Repairing the stamp would have destroyed client data.</b> <c>sprk_externalaccount</c>
/// is the CLIENT lookup — <c>ProjectLiveFactResolver.cs:33</c> and <c>MatterLiveFactResolver.cs:35</c>
/// both map the predicate <c>client</c> to it. The old code created a synthetic
/// "External Access — {project}" account and, in the broken PATCH, aimed it at that column. That column is
/// now never written, and a test fails if it reappears in any payload.</para>
///
/// <para><b>3. The idempotency marker keyed on shared state — a live regression, introduced by this
/// project on 2026-08-23.</b> See <see cref="RootRow"/> for the full account; short version: the
/// wizard then stamped <c>sprk_containerid</c> on every new project from the creating user's BU (removed by task
/// 076), so a marker reading that field ALONE 409'd every secure project as "already provisioned".</para>
///
/// <para><b>4. The nav-property blocker dissolved.</b> The only stamped field is <c>sprk_containerid</c>, an
/// <c>NVARCHAR(100)</c> plain string. The one remaining bind — <c>ownerid</c> — is an out-of-the-box owner field,
/// and it is verified by reading the owner back rather than trusted (see
/// <see cref="AssignOwnerToSecureTeamAsync"/>).</para>
/// </remarks>
public static class ProvisionProjectEndpoint
{
    // ── Configuration (owned by SecureRecordOwnerTeam; aliased here for the pinning tests) ──

    /// <inheritdoc cref="SecureRecordOwnerTeam.BusinessUnitNameConfigKey"/>
    internal const string SecureBusinessUnitNameConfigKey = SecureRecordOwnerTeam.BusinessUnitNameConfigKey;

    /// <inheritdoc cref="SecureRecordOwnerTeam.DefaultBusinessUnitName"/>
    internal const string DefaultSecureBusinessUnitName = SecureRecordOwnerTeam.DefaultBusinessUnitName;

    /// <inheritdoc cref="SecureRecordOwnerTeam.OwnerTeamNameConfigKey"/>
    internal const string SecureOwnerTeamNameConfigKey = SecureRecordOwnerTeam.OwnerTeamNameConfigKey;

    /// <inheritdoc cref="SecureRecordOwnerTeam.DefaultOwnerTeamName"/>
    internal const string DefaultSecureOwnerTeamName = SecureRecordOwnerTeam.DefaultOwnerTeamName;

    // ── Reason codes (ProblemDetails extensions["reasonCode"]) ───────────────
    //
    // Same convention as DelegationRuleFilter (task 008) and ProjectClosureEndpoint: a stable machine
    // -readable code alongside the human-readable detail, so a caller can distinguish "your request
    // named nothing" from "the environment is not set up" without parsing prose. The codes keep the word
    // "provision" for every root type; they are a contract, so they were not renamed when matters and work
    // assignments joined (task 144).

    private const string ReasonKey = "reasonCode";

    internal const string ReasonSecureBuNotFound = "sdap.provision.secure_bu_not_found";
    internal const string ReasonSecureBuAmbiguous = "sdap.provision.secure_bu_ambiguous";
    internal const string ReasonOwnerTeamNotFound = "sdap.provision.secure_owner_team_not_found";
    internal const string ReasonOwnerTeamAmbiguous = "sdap.provision.secure_owner_team_ambiguous";
    internal const string ReasonOwnerAssignmentFailed = "sdap.provision.owner_assignment_failed";
    internal const string ReasonOwnerAssignmentNotApplied = "sdap.provision.owner_assignment_not_applied";
    internal const string ReasonContainerNotRecorded = "sdap.provision.container_not_recorded";
    internal const string ReasonAlreadyProvisioned = "sdap.provision.already_provisioned";
    internal const string ReasonLegacyPerProjectBu = "sdap.provision.legacy_per_project_bu";

    // Task 061 — the share plane. Both are provisioning FAILURES, not warnings: a secure record whose
    // creator was not shared to is a record no human can open.
    internal const string ReasonCreatorUnresolved = "sdap.provision.creator_unresolved";
    internal const string ReasonCreatorShareFailed = "sdap.provision.creator_share_failed";

    // Task 144 — the two invariants a named team needs, each with a distinct "could not tell" code. An unreadable
    // count is a refusal in its own right, never folded into "zero" (ADR-003).
    internal const string ReasonOwnerTeamHasMembers = "sdap.provision.secure_owner_team_has_members";
    internal const string ReasonOwnerTeamMembershipUnreadable = "sdap.provision.secure_owner_team_membership_unreadable";
    internal const string ReasonSecureBuHasUsers = "sdap.provision.secure_bu_has_users";
    internal const string ReasonSecureBuUsersUnreadable = "sdap.provision.secure_bu_users_unreadable";

    // Task 144 — a record already owned INSIDE the Secure Record BU by a team other than the named one: in practice a
    // record provisioned under the retired default team, before the migration script ran. Moving it onto the named team
    // is that script's job — a deliberate, reported operation over every such record — not a side effect of provisioning
    // one, so it is refused before any write. (Until task 133 b2 the stated reason was "a second container"; a recorded
    // container of the record's own is now kept, so that is no longer the reason.)
    internal const string ReasonOwnedByOtherSecureTeam = "sdap.provision.owned_by_other_secure_team";

    // Task 133 (C11) — the creator lock-out. Each names a state the client must tell apart from the others, because
    // what the person in front of the wizard can do next differs.

    /// <summary>
    /// The creator's share failed AND the record could not be shown to be back with its pre-call owner — or the owner
    /// move could not be verified and no creator share could be issued. The record may be owned by the memberless
    /// team with nobody shared: the creator cannot call again (they no longer pass the delegation gate (Write and Share)). An administrator,
    /// who holds Write and Share, recovers it: for a record with no container recorded, by calling provisioning again — it resumes
    /// and shares it to the person who created the record (<c>createdby</c>, or <c>sprk_createdbyperson</c> for an
    /// app-created row); for a record that KEEPS its own container (<c>containerKept: true</c>), that call answers
    /// <c>already_provisioned</c> instead, so the administrator shares it through Manage Access (task 133 r1). The name
    /// says "resumable" for the first case; it is a contract, so it was not renamed.
    /// </summary>
    internal const string ReasonCreatorShareFailedResumable = "sdap.provision.creator_share_failed_resumable";

    /// <summary>
    /// A resume found no usable person to share to: neither <c>createdby</c> nor the server-stamped
    /// <c>sprk_createdbyperson</c> (owner round 7 item 2) is an enabled, non-application user — HTTP 409 with
    /// <c>creatorState</c> <c>absent</c> / <c>disabled</c> / <c>application-user</c> — or one of them could not be read
    /// (HTTP 500, <c>creatorState: unreadable</c>), or Dataverse REFUSED that read — a 401/403 refusing the service's
    /// sign-in or Read privilege, or a 400 (HTTP 500, <c>creatorState: refused</c>, owner round 14 item 3) — or
    /// <c>sprk_createdbyperson</c> does not exist in the environment (HTTP 500, <c>creatorState: column-missing</c>, task
    /// 133 r1: a 400 to the query naming it). The <c>creatorColumn</c> extension names which column the state describes.
    /// Resume never shares to the caller instead (owner decision F8), and nothing is written. Recovery, each of which
    /// works against this code: <c>unreadable</c> — the same caller calls again once the read works; <c>refused</c> —
    /// calling again repeats it: an administrator restores the service's Read privilege (users; this record's table for
    /// <c>sprk_createdbyperson</c>) first, then calls again; <c>column-missing</c> — an administrator applies the schema
    /// (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c>), then calls again;
    /// <c>disabled</c> — an administrator re-enables that user, then calls again (the resume shares to them); any state
    /// — an administrator ASSIGNS the record to the person who should hold it, which takes it out of the owner team, and
    /// that person then calls provisioning, which runs from the start and shares it to them.
    /// </summary>
    internal const string ReasonResumeCreatorUnavailable = "sdap.provision.resume_creator_unavailable";

    /// <summary>
    /// A resume request named colleagues (<c>sharePrincipalIds</c>) but its caller is not the record's creator
    /// (task 133 verifier round 1). Only the creator may add people while finishing an earlier run; anyone else adds
    /// them through Manage Access, which applies its own eligibility and grantor checks. Refused before any write.
    /// </summary>
    internal const string ReasonResumeColleaguesNotPermitted = "sdap.provision.resume_colleagues_not_permitted";

    // Task 143 (owner N6 / Q4) — the No Access list binds internal users on secure records.

    /// <summary>
    /// The calling user is on the No Access list for this record (it names them, a contact that represents them, an
    /// organization that contact belongs to, or an organization the record references). Owner N6: provisioning is
    /// REFUSED before any change, with a clear message — never provisioned and then emptied (403).
    /// </summary>
    internal const string ReasonCreatorNoAccess = "sdap.provision.creator_no_access";

    /// <summary>Whether the creator is on the record's No Access list could not be checked — refused before any change (500).</summary>
    internal const string ReasonCreatorNoAccessUnverifiable = "sdap.provision.creator_no_access_unverifiable";

    /// <summary>
    /// A RESUME's person (<c>createdby</c> / <c>sprk_createdbyperson</c>) is on the record's No Access list, or that list
    /// could not be checked: no share is issued to them and nothing is written (409; 500 when unverifiable).
    /// </summary>
    internal const string ReasonResumeCreatorNoAccess = "sdap.provision.resume_creator_no_access";

    /// <summary>A named colleague on the record's No Access list — skipped with a per-person warning (owner N6).</summary>
    internal const string ReasonPrincipalNoAccess = "sdap.provision.principal_no_access";

    /// <summary>A named colleague whose No Access check could not be completed — skipped (ADR-003).</summary>
    internal const string ReasonPrincipalNoAccessUnverifiable = "sdap.provision.principal_no_access_unverifiable";

    /// <summary>
    /// unified-access-control-r2 task 114 (owner round 67): the record is Restricted (internal use only) and this named
    /// colleague — or, on Make Secure, the record's creator — is flagged <c>sprk_isexternal = true</c>, so
    /// <c>InternalShareEndpoints.ClassifyEligibility</c> refuses them. Skipped with a per-person warning. When the flag or the
    /// record's Restricted state cannot be read, the colleague is skipped as <see cref="ReasonPrincipalNoAccessUnverifiable"/>
    /// ("whether they may access it could not be checked").
    /// </summary>
    internal const string ReasonPrincipalExternalOnRestricted = "sdap.provision.principal_external_on_restricted";

    /// <summary>
    /// Task 150 (round 33 items 1 and 5): a named colleague — or, on Make Secure, the record's creator — whose share could
    /// not be written. Reported per person in <c>skippedPrincipals</c> (never silent), the others still shared; the
    /// record stays secured and shared to the caller, who adds the person through Manage Access.
    /// </summary>
    internal const string ReasonPrincipalShareFailed = "sdap.provision.principal_share_failed";

    /// <summary>
    /// The owner PATCH was sent but its outcome could not be read back. A share to the creator was issued — read back
    /// when the read works (<c>creatorShareConfirmed: true</c>), otherwise issued without confirmation. The next call
    /// resumes a record the team now owns — unless it keeps its own container (<c>containerKept: true</c>), which the
    /// next call answers <c>already_provisioned</c> (task 133 r1).
    /// </summary>
    internal const string ReasonOwnerAssignmentUnverified = "sdap.provision.owner_assignment_unverified";

    /// <summary>Step 6 failed: the record is secured and shared, with no container yet. The same caller may resume.</summary>
    internal const string ReasonContainerCreationFailed = "sdap.provision.container_creation_failed";

    /// <summary><c>SharePointEmbedded:ContainerTypeId</c> is missing or invalid — refused before any mutation.</summary>
    internal const string ReasonContainerTypeNotConfigured = "sdap.provision.container_type_not_configured";

    /// <summary>
    /// The record was read without an owning user or team, so the move could not be undone — refused before any
    /// mutation. Deterministic for that row: calling again repeats the refusal, so it is not a self-service retry.
    /// </summary>
    internal const string ReasonRecordOwnerUnreadable = "sdap.provision.record_owner_unreadable";

    /// <summary>
    /// The container recorded on the record (<c>sprk_containerid</c>) is ALSO recorded on another project, matter or
    /// work assignment, and is not a business unit's or this BFF's configured shared container (task 133, found live
    /// 2026-10-02). Keeping it would put a secure record's files in storage another record reaches; replacing it would
    /// leave this record's existing files behind with nothing secure pointing at them. Refused before any mutation
    /// (409): an administrator decides which record the container belongs to.
    /// </summary>
    internal const string ReasonContainerSharedWithAnotherRecord = "sdap.provision.container_shared_with_another_record";

    /// <summary>
    /// The container recorded on the record could not be checked against the business units and the other secure roots.
    /// Refused before any mutation (500). The <c>containerOwnershipState</c> extension says which: <c>unreadable</c> (a
    /// read failed; the same caller may call again once Dataverse is reachable) or <c>refused</c> (owner round 14 item 3:
    /// Dataverse refused the read — a 401/403 refusing the service's sign-in or Read privilege, or a 400 — so calling again
    /// repeats it: an administrator looks at the service's Read privilege on business units and on the project, matter and
    /// work assignment tables first). Classified by <see cref="AssignCascadeChildOwners.IsRefusedRead"/>, the cascade
    /// reads' rule.
    /// </summary>
    internal const string ReasonContainerOwnershipUnreadable = "sdap.provision.container_ownership_unreadable";

    /// <summary>
    /// The record recorded a SHARED container (a business unit's, or one this BFF uses for many records), which
    /// provisioning replaces with the record's own — and that link could not be removed before the owner move (task 133
    /// r1). Provisioning stops there: the owner was not moved and no share was issued; the link may or may not have been
    /// removed. The same caller may call again. Why the link goes BEFORE the move: a record owned by the secure owner
    /// team with a container recorded is the 409 marker, so a failure after the move would otherwise leave a record that
    /// can never be finished — whose uploads meanwhile go to shared storage.
    /// </summary>
    internal const string ReasonSharedContainerNotCleared = "sdap.provision.shared_container_not_cleared";

    /// <summary>
    /// The rows an owner move of the record cascades to (<see cref="AssignCascadeChildOwners"/>: SharePoint document
    /// locations and documents) could not be read completely before any write, so the move could not be undone child by
    /// child if a later step failed — refused before any mutation (task 133, owner round 10 item 4). The
    /// <c>cascadeChildState</c> extension says which: <c>unreadable</c> (a read failed; the same caller may call again)
    /// or <c>refused</c> (Dataverse refused the read — a 400, or a 401/403 refusing the service's sign-in or Read
    /// privilege — or it came back incomplete; deterministic: an administrator looks at the <c>childTable</c> rows and the
    /// service's Read privilege on that table first). <c>childTable</c> names the table.
    /// </summary>
    internal const string ReasonCascadeChildrenUnreadable = "sdap.provision.cascade_children_unreadable";

    /// <summary>
    /// The creator's share failed and the move WAS undone (the record is back with its pre-call owner, read back), but
    /// rows the owner move cascaded to could not all be put back on their OWN owners (task 133, owner round 10 item 4).
    /// <c>childOwnersNotRestored</c> names each, with its own owner and the call that puts it back; the same is logged
    /// CRITICAL. An administrator makes those calls BEFORE provisioning is called again: another run would snapshot the
    /// wrong owner for them. Not a self-service retry.
    /// </summary>
    internal const string ReasonCascadeChildrenNotRestored = "sdap.provision.cascade_children_not_restored";

    /// <summary>
    /// Task 148 (ADR-003): the record is isolated and shared, but its EXISTING related records were not all brought into
    /// isolation (re-owned to the Secure Record owner team and shared with the record's sharees). 500 with
    /// <c>childrenReowned</c>, <c>childrenRemaining</c> and <c>childTables</c>; nothing done is undone. Calling again completes
    /// the pass (the same caller may), and so does the secure-child reconciliation.
    /// </summary>
    internal const string ReasonChildrenIncomplete = "sdap.provision.children_incomplete";

    /// <summary>
    /// Round 26 item 3 (wired at the batch-4 integration): the record is isolated, shared, has its container and its
    /// related records are secured, but some of its EXISTING files are not yet moved into its own container
    /// (<see cref="DocumentContainerRelocator"/>, purpose <see cref="RelocationPurpose.MakeSecure"/>: copied with their
    /// history, verified, re-pointed, the source deleted). 500 with the counts; the record stays flagged and provisioned,
    /// nothing done is undone, and no file is more exposed than before. Calling again completes it (the already-provisioned
    /// branch relocates again), and so does the secure-child reconciliation, which settles pending Make Secure relocations
    /// every run (round 46 item 2). A row that names no file it can resolve (<see cref="RelocationState.FileMissing"/> — the
    /// item is gone, or the communication-archive path wrote a document id where the item id belongs) is reported in
    /// <c>filesUnresolvable</c>, never moved or deleted, and does not by itself make the result incomplete.
    /// </summary>
    internal const string ReasonFilesIncomplete = "sdap.provision.files_incomplete";

    /// <summary>
    /// Task 150: <c>sprk_issecure</c> could not be set true — the write failed, or the read-back did not show
    /// <c>true</c>. It is the FIRST write, so nothing else was changed (the flag itself may or may not be set). The same
    /// caller may call again. A read-back that comes back without the value usually means this service lost its
    /// field-level-security Read on the column, which an administrator restores
    /// (<c>scripts/Set-SecureFlagFieldSecurity.ps1 -Verify</c>); a refused write, that its application user is not in
    /// the writer profile.
    /// </summary>
    internal const string ReasonSecureFlagNotSet = "sdap.provision.secure_flag_not_set";

    /// <summary>
    /// Task 150 (owner round 10 item 10, 2026-10-03): the record is NOT yet marked secure, and the caller is not the person
    /// who created it. An unflagged record is secured through this call only for its creator — <c>createdby</c> when that
    /// is a person, otherwise the server-stamped <c>sprk_createdbyperson</c> (an app-only create). A record already
    /// flagged (an older client, a row from before task 150) stays on the route's delegation gate (Write and Share). Refused before any write
    /// (403), deterministic for that caller: securing an existing record someone else created, with the content already
    /// filed under it, is task 148's transition, not this call.
    /// </summary>
    internal const string ReasonNotRecordCreator = "sdap.provision.not_record_creator";

    /// <summary>
    /// Task 150 (round 33 item 1): <see cref="ProvisionProjectRequest.Transition"/> for the form's Make Secure command —
    /// securing an EXISTING record (task 148's surface). That path is held to the route's delegation gate (Write and Share) only (owner R3b):
    /// the creator rule of owner round 10 item 10 (<see cref="ReasonNotRecordCreator"/>) belongs to the wizards'
    /// create-then-secure path, which sends no transition. On it the record's creator is shared to as well
    /// (<see cref="ResolveMakeSecureCreatorAsync"/>), so the confirmation copy's promise holds (owner round 27); and the
    /// caller is shared at the creator's level on the forward path AND on a resume (round 40 item 2,
    /// <see cref="ResumeMakeSecureAsync"/>), never lower than the access they held before the call — by share, ownership or
    /// role (<see cref="MakeSecureCallerMask"/>, round 46 item 1). Matched exactly (ordinal): the value relaxes a gate, so no
    /// near-miss spelling is read as it.
    /// </summary>
    internal const string TransitionMakeSecure = "make-secure";

    /// <summary>
    /// Task 150 (owner round 10 item 10): whether the caller created the unflagged record could not be checked — its
    /// <c>createdby</c> user or its <c>sprk_createdbyperson</c> could not be read. Refused before any write (500); the
    /// same caller may call again once the read works. A column this environment lacks (<c>sprk_createdbyperson</c>
    /// before its schema script ran — a 400) is this code too (round 17 item 1: unverifiable, not "not the creator"),
    /// but 403 with <c>creatorState: column-missing</c>: deterministic until an administrator applies the schema.
    /// </summary>
    internal const string ReasonRecordCreatorUnverifiable = "sdap.provision.record_creator_unverifiable";

    /// <summary>
    /// Task 150 (round 53 item 1, for round 46 item 1): on Make Secure (<see cref="TransitionMakeSecure"/>) the caller's
    /// EFFECTIVE rights on the record could not be read — the probe threw, or it answered without the Write the route's
    /// gate admitted — so the share they keep could not be floored on them (<see cref="ReadMakeSecureCallerFloorAsync"/>).
    /// Refused before any write (500); the same caller may call again. A provisioning code (round 26 item 1: codes are
    /// namespaced by endpoint) — owner F3's <see cref="SecureDesignationRemoval.PermissionUnverifiableReasonCode"/> is the
    /// unsecure endpoint's. The wizards' create-then-secure path reads no floor, so it never answers this code.
    /// </summary>
    internal const string ReasonCallerRightsUnverifiable = "sdap.provision.caller_rights_unverifiable";

    /// <summary>
    /// The configuration keys naming containers this BFF uses for MANY records — the communication archive, the
    /// email-processing default (task 133; the AI staging container went with its setting in task 227f). A record whose <c>sprk_containerid</c> holds
    /// one of these is pointing at shared storage, not at a container of its own, so provisioning gives it its own
    /// (the shared container stays where the configuration points; nothing is orphaned).
    /// </summary>
    internal static readonly IReadOnlyList<string> SharedContainerConfigKeys = new[]
    {
        "Communication:ArchiveContainerId",
        "EmailProcessing:DefaultContainerId",
        "Email:DefaultContainerId",
    };

    // ── Share rights (task 061) ──────────────────────────────────────────────

    /// <summary>
    /// Rights the creating user receives on their own secure record: the Collaborate level of
    /// <see cref="RecordShareLevels"/> — Read, Write, Append, AppendTo and Share.
    /// </summary>
    /// <remarks>
    /// Read/Write/Append/AppendTo is "can actually work the matter"; <c>ShareAccess</c> lets them bring colleagues
    /// in, through the FR-29 "+ User" surface or the model-driven app's own Share command, without an
    /// administrator. Delete and Assign are deliberately absent — a secure record leaves the secure business unit
    /// only through the explicit unsecure path, not by being reassigned out of it. Before task 139 this constant was
    /// spelled <c>CollaborateRights + ",ShareAccess"</c>; since the owner's 2026-09-30 rule put Share INTO
    /// Collaborate it is the level itself, and the value is unchanged (mask 262167).
    /// </remarks>
    internal const string CreatorAccessRights = RecordShareLevels.CollaborateRights;

    /// <summary>
    /// Rights a named colleague receives at provisioning time: EXACTLY the creator's rights (owner, 2026-09-30, C4).
    /// </summary>
    /// <remarks>
    /// <para>The owner's rule: a person with Write may share (OOB) and use Manage Access; only a View holder may not
    /// pass access on. A colleague named at provisioning is a Collaborate holder, so they carry <c>ShareAccess</c>
    /// like the creator. This supersedes the 2026-09-15 rationale that kept re-sharing with the creator alone; every
    /// grant they make is still capped at their own level (Dataverse's native sharing rule in the model-driven app,
    /// the grantor ceiling on every Spaarke grant route).</para>
    /// <para>The Collaborate level of <see cref="RecordShareLevels"/> (task 063), the one level-to-rights table — so
    /// a colleague shared at provisioning and one shared later at Collaborate through the "+ User" picker hold the
    /// same rights, and the two cannot drift apart. Kept as its own name, beside <see cref="CreatorAccessRights"/>,
    /// because the two shares are written by different steps and a test pins each against the literal.</para>
    /// </remarks>
    internal const string CollaboratorAccessRights = RecordShareLevels.CollaborateRights;

    /// <summary>
    /// The mask Dataverse stores for <see cref="CreatorAccessRights"/> — what the creator's share is confirmed against
    /// (task 133). Derived from the literal, never written as a number, so it follows task 139 if that changes the
    /// rights.
    /// </summary>
    internal static readonly int CreatorAccessMask = RecordShareLevels.MaskForRightsCsv(CreatorAccessRights);

    /// <summary>The Full Access level's mask (Collaborate + Delete) — the highest a Make Secure caller's share is kept at.</summary>
    private static readonly int FullAccessMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.FullAccessRights);

    /// <summary>
    /// Task 150 (round 40 item 2; round 46 item 1): the share a Make Secure caller ends with, given what they held — the
    /// creator's level (<see cref="CreatorAccessMask"/>), and never LOWER than a level they already held: a Full Access
    /// holder keeps Delete (and so stays one of the people owner F3 lets remove the designation). <paramref name="heldMask"/>
    /// is what they held BEFORE the call by any route — their explicit share, and their EFFECTIVE rights
    /// (<see cref="MakeSecureHeldMask"/>: ownership or a security role too, as F3 itself decides Full Access). Nothing beyond
    /// Full Access is kept — a secure record's sharee never holds Assign (it leaves the Secure Record business unit only
    /// through the unsecure endpoint), which is why the wizards' creator share is EXACTLY the creator's level.
    /// </summary>
    internal static int MakeSecureCallerMask(int heldMask) => (heldMask & FullAccessMask) | CreatorAccessMask;

    /// <summary>
    /// Task 150 (round 46 item 1): the Full Access rights (Collaborate plus Delete) a caller holds EFFECTIVELY — Dataverse's
    /// own answer (<c>RetrievePrincipalAccess</c>, asked as the caller), so held through an explicit share, through owning
    /// the record, or through a security role alike — as the share mask those rights would be. Owner F3 decides "Full
    /// Access" from exactly this answer (<see cref="SecureDesignationRemoval.FullAccess"/>), so a caller who held Delete by
    /// ownership or by role before Make Secure keeps it, through an explicit Full Access share, exactly as one who held it
    /// by share does. Rights no share level carries (Create, Assign) are never in the result.
    /// </summary>
    internal static int MakeSecureHeldMask(AccessRights effectiveRights) =>
        RecordShareLevels.Intersect(
            new RecordShareRights(RecordShareLevels.FullAccessRights, FullAccessMask), effectiveRights).AccessRightsMask;

    /// <summary>
    /// The columns Step 1 reads from <c>sprk_project</c> — the project row of <see cref="SecureRecordRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para>Every name here is verified against live <c>sprk_project</c> metadata (2026-08-25, re-checked for all
    /// three roots 2026-10-01). <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c> and
    /// <c>sprk_externalaccountid</c> — the three names the old stamping PATCH used — do not exist on this table, which
    /// is why that PATCH 400'd for five months.</para>
    ///
    /// <para>Internal (not private) so a test can pin the exact names against the live column set.</para>
    /// </remarks>
    internal static readonly string ProjectProvisioningSelect = SecureRecordRoot.Project.ProvisioningSelect;

    /// <summary>
    /// Registers the provision-project endpoint on the external-access management group.
    /// </summary>
    public static RouteGroupBuilder MapProvisionProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/provision-project", ProvisionProjectAsync)
            .WithName("ProvisionSecureProject")
            .WithSummary("Provision infrastructure for a new secure project, matter or work assignment")
            .WithDescription(
                "Assigns the record to the canonical Secure Record business unit's NAMED owner team — after " +
                "proving that team has no members and that no user sits in the business unit — and provisions " +
                "the record's own SPE container, recording it on sprk_containerid. Accepts recordType + recordId " +
                "(project | matter | workassignment) or the legacy projectId. Creates no business unit and no " +
                "account.")
            .Produces<ProvisionProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409: already provisioned (owned by the secure owner team with a container recorded) — nothing is
            // written; or a refusal before any write (another record's container, a legacy BU, a retired owner team).
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// The record this request targets. The delegation filter calls this too, so the record whose Write was
    /// checked is the record re-owned (task 144; the <c>FromGrantRoot</c> pattern).
    /// </summary>
    internal static GrantExternalAccessEndpoint.GrantRootResolution ResolveRoot(ProvisionProjectRequest request)
        => SecureRecordRoot.ResolveTarget(request.ProjectId, request.RecordType, request.RecordId);

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> ProvisionProjectAsync(
        ProvisionProjectRequest request,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        SecureChildReconciler secureChildren,
        DocumentContainerRelocator fileRelocator,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        SecureShareNoAccessGuard noAccessGuard,
        IMembershipCacheInvalidator accessCacheInvalidator,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var result = await ProvisionCoreAsync(
            request,
            ProvisioningCreator.Caller(callerAccessProbe, TokenHelper.ExtractBearerTokenOrNull(httpContext)),
            httpContext.TraceIdentifier,
            dataverseClient, speFileStore, recordShare, secureChildren, relatedRoots, configuration, noAccessGuard,
            accessCacheInvalidator, fileRelocator, callerAccessProbe, httpContext, logger, ct);

        // ── Task 175 (owner round 87): a work assignment or project made secure BY HAND keeps it as its own ──
        //
        // Making a record stricter is never refused (a child may be stricter than its parent). Its access record now says the
        // secure designation was set on it, so a parent that is later secured and then un-secured does not take it away (round
        // 87 item 2). Best effort: a failure here is logged, and the job reads the same fact from the record within 5 minutes.
        var target = ResolveRoot(request);
        if (result is Microsoft.AspNetCore.Http.HttpResults.Ok<ProvisionProjectResponse> && target.Ok
            && SecureRootInheritance.Inherits(ExternalGrantRoot.LogicalNameFor(target.Type)))
        {
            try
            {
                await relatedRoots.FollowParentsAsync(ExternalGrantRoot.LogicalNameFor(target.Type), target.Id,
                    httpContext.TraceIdentifier, CancellationToken.None, ownSecureSetNow: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[PROVISION] Recording that {RecordId} was made secure by hand failed; the job records it.", target.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// unified-access-control-r2 task 158 (owner round 6): provisions a work assignment or project that is FILED UNDER a
    /// secure matter or project — through THIS endpoint's own steps, never a second "make secure" implementation. There is
    /// no caller: the person the record is secured for is the one who created it (<c>createdby</c> when that is a usable
    /// person, otherwise the BFF-stamped <c>sprk_createdbyperson</c> — the RESUME rule, owner round 7 item 2), no colleague
    /// is named, and the record need not arrive flagged: <c>sprk_issecure</c> is set as the first write (task 150's
    /// Step 4.1), read back. Every other step, refusal and compensation is the endpoint's own.
    /// </summary>
    /// <returns>The endpoint's own result: 200 (provisioned, or its related records completed), 409
    /// <c>already_provisioned</c> (nothing to do), or the refusal / failure it would answer a caller with.</returns>
    internal static Task<IResult> ProvisionInheritedAsync(
        SecureRecordRoot root,
        Guid recordId,
        string traceId,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        SecureShareNoAccessGuard noAccessGuard,
        IMembershipCacheInvalidator accessCacheInvalidator,
        DocumentContainerRelocator? fileRelocator,
        ILogger logger,
        CancellationToken ct)
        => ProvisionCoreAsync(
            new ProvisionProjectRequest(Guid.Empty, null, null, root.WireToken, recordId),
            ProvisioningCreator.RecordedCreator,
            traceId,
            dataverseClient, speFileStore, recordShare, secureChildren, relatedRoots, configuration, noAccessGuard,
            accessCacheInvalidator, fileRelocator, callerAccessProbe: null, httpContext: null, logger, ct);

    private static async Task<IResult> ProvisionCoreAsync(
        ProvisionProjectRequest request,
        ProvisioningCreator creator,
        string traceId,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        SecureShareNoAccessGuard noAccessGuard,
        IMembershipCacheInvalidator accessCacheInvalidator,
        DocumentContainerRelocator? fileRelocator,
        // The HTTP caller's probe and request (null on task 158's inherited provisioning, which has no caller and sends no
        // transition: the paths that ask the caller — Make Secure's floor and resume, the unflagged-resume creator rule —
        // are never reached on it).
        CallerRecordAccessProbe? callerAccessProbe,
        HttpContext? httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        var target = ResolveRoot(request);
        if (!target.Ok)
            return ProblemDetailsHelper.ValidationError(target.Error ?? "A record to provision is required.");

        // Task 150 (round 33 item 1): which surface asks. Omitted = the wizards' create-then-secure path (creator rule);
        // make-secure = the form's Make Secure command (delegation gate (Write and Share)). Anything else is refused before any read or write: an
        // unrecognised value never falls back to either rule, and only the exact token relaxes the creator rule.
        bool makeSecure;
        if (request.Transition is null)
            makeSecure = false;
        else if (string.Equals(request.Transition, TransitionMakeSecure, StringComparison.Ordinal))
            makeSecure = true;
        else
            return ProblemDetailsHelper.ValidationError($"Transition must be '{TransitionMakeSecure}' or omitted.");

        // Make Secure names no colleagues: it shares to the caller and to the record's creator, nobody else. A Write holder
        // who did not create the record (round 33 item 1 admits them) would otherwise widen its explicit access list through
        // the application identity, skipping the eligibility and grantor checks Manage Access applies — the reason a
        // resume refuses colleagues from anyone but the creator (task 133 verifier round 1). People are added through
        // Manage Access. Refused before any read or write.
        if (makeSecure && request.SharePrincipalIds is { Count: > 0 })
        {
            return ProblemDetailsHelper.ValidationError(
                "Make Secure shares the record to you and to the person who created it, and names nobody else: add other " +
                "people through Manage Access once it is secure. Nothing was changed.");
        }

        var root = SecureRecordRoot.For(target.Type);
        var recordId = target.Id;

        logger.LogInformation(
            "[PROVISION] Starting secure provisioning: RecordType={RecordType}, RecordId={RecordId}, " +
            "Ref={Ref}, TraceId={TraceId}",
            root.WireToken, recordId, request.ProjectRef, traceId);

        // ── Step 1: Confirm the record exists (task 150: the flag is set later, not required here) ──
        RootRow? row;
        try
        {
            var rows = await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: root.ProvisioningSelect,
                top: 1,
                cancellationToken: ct);

            row = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to query {RecordType} {RecordId} from Dataverse", root.WireToken, recordId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Failed to retrieve the {root.DisplayLabel.ToLowerInvariant()} record from Dataverse.", traceId);
        }

        if (row == null)
        {
            return Problem(StatusCodes.Status404NotFound, "Not Found",
                $"{root.DisplayLabel} {recordId} not found.", traceId);
        }

        // Task 150: the record no longer has to arrive flagged. sprk_issecure is field-secured and ONLY this endpoint
        // sets it (EnsureSecureFlagAsync, the first write, after every pre-mutation refusal below), so a record from the
        // new client arrives unflagged. One from the old client, or created before task 150, arrives flagged and not
        // provisioned; both take exactly the same path from here (rollout constraint), the flagged one skipping the write.
        var alreadyFlagged = row.sprk_issecure == true;

        // Task 114 (owner round 67, item 3 decided 2026-10-06: RESTRICTED WINS over the last-reader rule): on a Restricted
        // record the person this run would share it to — the creator / Make Secure caller — is not shared to when flagged
        // sprk_isexternal = true, exactly as a named colleague is not. A secure record left with nobody internal is then
        // provisioned anyway (an administrator still sees it) and the response says so.
        // #1478 (task 175): Restricted THROUGH a parent counts as Restricted (owner round 84; task 174's effective rule) — a
        // work assignment or project filed under a Restricted matter is Restricted before its own column catches up. A
        // chain that cannot be read counts as Restricted (fail closed: it only bars a person flagged external).
        var restrictedThroughFiling = row.sprk_accesspermission != ExternalParticipationService.AccessPermissionRestricted
                                      && (await relatedRoots.IsRestrictedThroughFilingAsync(root.LogicalName, recordId, ct) ?? true);
        var creatorRule = new RestrictedCreatorRule(
            dataverseClient,
            row.sprk_accesspermission == ExternalParticipationService.AccessPermissionRestricted || restrictedThroughFiling);

        var recordName = row.NameFrom(root.NameColumn) ?? request.ProjectRef ?? recordId.ToString();

        // ── Steps 2 + 3: the Secure Record BU, its NAMED owner team, and the two invariants ──
        //
        // FAIL CLOSED, and never substitute. An absent or ambiguous BU or team, a team with any member, a BU with any
        // user, or an unreadable answer to any of those stops provisioning BEFORE anything is written. Never the
        // root BU, never the caller's BU, and never the BU's default team: any of those would put a secure record
        // where people can reach it, and a silent substitution is not detectable from the outside at all.
        var topology = await SecureRecordOwnerTeam.ResolveAsync(dataverseClient, configuration, ct);
        if (!topology.IsResolved)
            return TopologyRefusal(topology, logger, traceId);

        var secureBuId = topology.BusinessUnitId!.Value;
        var secureBuName = topology.BusinessUnitName;
        var ownerTeamId = topology.OwnerTeamId!.Value;
        var ownerTeamName = topology.OwnerTeamName;

        logger.LogInformation(
            "[PROVISION] Resolved Secure Record BU '{BuName}' ({BuId}) and its named owner team " +
            "'{TeamName}' ({TeamId}); the team has no members and the BU holds no users",
            secureBuName, secureBuId, ownerTeamName, ownerTeamId);

        // ── Step 4: The marker ───────────────────────────────────────────────
        if (row.HasLegacyPerProjectBusinessUnit)
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} carries a legacy per-record security BU " +
                "({LegacyBuId}). Refusing: migrating it onto the canonical BU is a deliberate act, " +
                "not something a provisioning retry should do. TraceId={TraceId}",
                root.WireToken, recordId, row._sprk_securitybu_value, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"{root.DisplayLabel} {recordId} was provisioned by the retired BU-per-project mechanism " +
                "and still references its own security business unit. Migrating it to the canonical " +
                "Secure Record business unit is a manual operation — provisioning will not do it as " +
                "a side effect, because it would leave the old business unit and its container behind " +
                "with nothing pointing at them.",
                traceId,
                (ReasonKey, ReasonLegacyPerProjectBu),
                ("legacyBusinessUnitId", row._sprk_securitybu_value),
                ("speContainerId", row.sprk_containerid));
        }

        var resume = false;
        if (row.IsOwnedBy(ownerTeamId))
        {
            if (row.IsProvisioned(ownerTeamId))
            {
                // ── Task 148: a provisioned record whose related records are not all secured yet ──
                //
                // An earlier call's child pass (Step 8, after the container) may not have finished, and a record secured
                // before task 148 never had one. So the pass runs here too: with nothing to do it writes nothing and the 409
                // below is unchanged; with work to do it COMPLETES the pass (constraint "re-invoking provisioning on a root
                // whose child pass is incomplete completes the pass instead of returning 409") — nothing about the record
                // itself is changed, and no share is written to it.
                var pending = await secureChildren.ReconcileAsync(
                    root.LogicalName, recordId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, ct);
                if (!pending.IsComplete)
                    return ChildrenIncomplete(pending, root, recordId, logger, traceId);

                // ── Task 158: the work assignments and projects filed under it are secured too (owner round 6) ──
                var pendingRoots = await relatedRoots.SecureFiledRootsUnderAsync(root.LogicalName, recordId, traceId, ct);
                if (!pendingRoots.IsComplete)
                    return RelatedRootsIncomplete(pendingRoots, root, recordId, logger, traceId);

                // Round 26 item 3: the re-entry relocates the record's files again — a repeat call completes a
                // files_incomplete (the relocator is idempotent: a moved file is InPlace, an owed step is settled first).
                var pendingFiles = await FilesFollowAsync(
                    fileRelocator, pending, row.sprk_containerid!, root, recordId, logger, traceId, ct);
                if (pendingFiles.Error is not null)
                    return pendingFiles.Error;

                if (pending.WroteAnything || pendingRoots.WroteAnything || pendingFiles.Summary.Moved > 0)
                {
                    logger.LogInformation(
                        "[PROVISION] {RecordType} {RecordId} was already provisioned; this call completed its related records " +
                        "(reowned={Reowned}, filed records secured={Secured}). TraceId={TraceId}",
                        root.WireToken, recordId, pending.ChildrenReowned, pendingRoots.Secured, traceId);

                    return TypedResults.Ok(new ProvisionProjectResponse(
                        BusinessUnitId: secureBuId,
                        BusinessUnitName: secureBuName,
                        OwnerTeamId: ownerTeamId,
                        OwnerTeamName: ownerTeamName,
                        SpeContainerId: row.sprk_containerid!,
                        SharedToCreatorSystemUserId: Guid.Empty,
                        AdditionalPrincipalsShared: 0,
                        RecordType: root.WireToken,
                        RecordId: recordId,
                        Resumed: true,
                        Children: SecureChildPassSummary.From(pending),
                        ChildrenOnly: true,
                        FiledRecords: pendingRoots.Summary())
                    {
                        Files = pendingFiles.Summary,
                    });
                }

                // Provisioned, and nothing is written. The recorded container is one of two things (see RootRow): one
                // Step 7 recorded after Step 5.5 proved the creator's share, or the record's OWN container, kept (task
                // 133 b2). A record that keeps its container is indistinguishable here from a finished one even when a
                // step after its move failed — which is why it moves only once the creator's share can be set up first,
                // and why each failure after its move names a recovery that does not need this call (task 133 r1). A
                // SHARED container never reaches here: it is unlinked before the move (Step 4.2).
                logger.LogWarning(
                    "[PROVISION] {RecordType} {RecordId} is already provisioned: owned by the Secure Record owner " +
                    "team {TeamId} with container {ContainerId} recorded. Refusing. TraceId={TraceId}",
                    root.WireToken, recordId, ownerTeamId, row.sprk_containerid, traceId);

                return Problem(StatusCodes.Status409Conflict, "Conflict",
                    $"{root.DisplayLabel} {recordId} has already been provisioned: it is owned by the Secure Record " +
                    "owner team and its own SPE container is recorded on it. Nothing was changed. Calling provisioning " +
                    "again does not change who can open it: that is managed through Manage Access, where an " +
                    "administrator shares it to anyone who should hold it but cannot open it.",
                    traceId,
                    (ReasonKey, ReasonAlreadyProvisioned),
                    ("businessUnitId", secureBuId),
                    ("ownerTeamId", ownerTeamId),
                    ("speContainerId", row.sprk_containerid));
            }

            // Owned by the team, no container recorded: an earlier run stopped after the owner move.
            resume = true;
        }

        if (row.IsOwnedInBusinessUnitByAnotherTeam(secureBuId, ownerTeamId))
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} is already owned inside the Secure Record BU by team " +
                "{OwningTeamId}, not by the named owner team {TeamId}. Refusing: it was provisioned under the " +
                "retired default team and must be migrated, not re-provisioned. TraceId={TraceId}",
                root.WireToken, recordId, row._owningteam_value, ownerTeamId, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"{root.DisplayLabel} {recordId} is already owned inside the Secure Record business unit, by a " +
                "team other than the named secure owner team — most likely the business unit's default team, " +
                "which owned secure records before task 144. Nothing was changed: moving it onto the named team is a " +
                "deliberate migration, not a side effect of provisioning — move it with " +
                "scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 instead.",
                traceId,
                (ReasonKey, ReasonOwnedByOtherSecureTeam),
                ("businessUnitId", secureBuId),
                ("ownerTeamId", ownerTeamId),
                ("owningTeamId", row._owningteam_value),
                ("speContainerId", row.sprk_containerid));
        }

        // ── Still before any mutation: the container type (task 133) ─────────
        //
        // Checked here rather than at Step 6 because a fault that is certain to stop Step 6 must stop the run while
        // nothing has moved, not after the record is owned by a memberless team.
        var containerTypeIdStr = configuration["SharePointEmbedded:ContainerTypeId"];
        if (!Guid.TryParse(containerTypeIdStr, out var containerTypeId))
        {
            logger.LogError(
                "[PROVISION] SharePointEmbedded:ContainerTypeId is not configured or invalid: '{Value}'. Refusing " +
                "before any change. TraceId={TraceId}", containerTypeIdStr, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The SPE container type is not configured on the BFF API (SharePointEmbedded:ContainerTypeId), so " +
                "the record could not be given its own container. Nothing was changed.",
                traceId, (ReasonKey, ReasonContainerTypeNotConfigured));
        }

        // ── Still before any mutation: a container ALREADY recorded on the record (task 133, found live 2026-10-02) ──
        //
        // A record reaching here with sprk_containerid set is not owned by the secure owner team (that is the 409
        // above). Provisioning used to create a new container and overwrite the value — on the 2026-10-02 live gate
        // that orphaned the record's OWN container (65a3fab2), and would have orphaned anything stored there. Now the
        // value is classified first, read-only: a business unit's or this BFF's configured shared container is replaced
        // by the record's own (the business unit / configuration keeps pointing at it, so nothing is orphaned) — and is
        // unlinked BEFORE the owner move (task 133 r1), because a team-owned record with any container recorded is the
        // 409 marker; one recorded on ANOTHER root is refused; anything else is this record's own and is KEPT. A failed
        // read refuses, never guesses.
        string? keptContainerId = null;
        string? sharedContainerToUnlink = null;
        if (!resume && !string.IsNullOrWhiteSpace(row.sprk_containerid))
        {
            var recorded = row.sprk_containerid.Trim();
            var holder = await ClassifyRecordedContainerAsync(dataverseClient, configuration, root, recordId, recorded, ct);
            switch (holder.Kind)
            {
                case RecordedContainerKind.Unreadable:
                    {
                        // Owner round 14 item 3: a read Dataverse REFUSES (400/401/403 — the cascade reads' rule) repeats on
                        // every call, so it is an administrator's job, never offered to the same caller as a retry.
                        var ownershipRefused = holder.Fault is not null && AssignCascadeChildOwners.IsRefusedRead(holder.Fault);
                        var ownershipState = ownershipRefused ? ContainerOwnershipStateRefused : ContainerOwnershipStateUnreadable;
                        logger.LogError(holder.Fault,
                            "[PROVISION] Could not tell whether container {ContainerId} recorded on {RecordType} {RecordId} " +
                            "belongs to it alone ({State}). Refusing before any change. TraceId={TraceId}",
                            recorded, root.WireToken, recordId, ownershipState, traceId);
                        return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                            $"The {root.DisplayLabel.ToLowerInvariant()} already records an SPE container, and whether that " +
                            "container belongs to it alone could not be checked " + (ownershipRefused
                                ? "(Dataverse refused the read of the business units or of the other secure records, or the " +
                                  "service's permission to read them). Nothing was changed. Calling again repeats this " +
                                  "refusal: an administrator looks at the service's Read privilege on business units and on " +
                                  "the project, matter and work assignment tables first."
                                : "(a read of the business units or of the other secure records failed). Nothing was changed; " +
                                  "calling again once Dataverse is reachable repeats the check (the same caller may)."),
                            traceId, (ReasonKey, ReasonContainerOwnershipUnreadable), ("speContainerId", recorded),
                            ("containerOwnershipState", ownershipState));
                    }

                case RecordedContainerKind.AnotherRecord:
                    // WHICH record holds it goes to the operator log, not to the caller (the TopologyRefusal precedent):
                    // the caller holds Write on THIS record, not necessarily on the other, and the remedy is an
                    // administrator's. The response names only the kind of record.
                    logger.LogWarning(
                        "[PROVISION] Container {ContainerId} recorded on {RecordType} {RecordId} is also recorded on " +
                        "{OtherType} {OtherId}. Refusing before any change. TraceId={TraceId}",
                        recorded, root.WireToken, recordId, holder.OtherRoot!.WireToken, holder.OtherRecordId, traceId);
                    return Problem(StatusCodes.Status409Conflict, "Conflict",
                        $"The {root.DisplayLabel.ToLowerInvariant()} records an SPE container that another " +
                        $"{holder.OtherRoot.DisplayLabel.ToLowerInvariant()} also records. Keeping it would put a secure " +
                        "record's files in storage the other record reaches, and replacing it would leave this record's " +
                        "existing files there. Nothing was changed: an administrator finds the other record (logged under " +
                        "this traceId, or by querying sprk_containerid), decides which record the container belongs to " +
                        "and clears it from the other, then calls provisioning again.",
                        traceId, (ReasonKey, ReasonContainerSharedWithAnotherRecord), ("speContainerId", recorded),
                        ("otherRecordType", holder.OtherRoot.WireToken));

                case RecordedContainerKind.BusinessUnit:
                    sharedContainerToUnlink = recorded;
                    logger.LogInformation(
                        "[PROVISION] {RecordType} {RecordId} records business unit {BusinessUnitId}'s shared container " +
                        "{ContainerId}; it is unlinked before the owner move and the record gets its own container. The " +
                        "business unit keeps the shared one.",
                        root.WireToken, recordId, holder.BusinessUnitId, recorded);
                    break;

                case RecordedContainerKind.Configured:
                    sharedContainerToUnlink = recorded;
                    logger.LogInformation(
                        "[PROVISION] {RecordType} {RecordId} records the shared container {ContainerId} configured as " +
                        "{ConfigKey}; it is unlinked before the owner move and the record gets its own container. The " +
                        "configuration keeps the shared one.",
                        root.WireToken, recordId, recorded, holder.ConfigKey);
                    break;

                case RecordedContainerKind.Own:
                    keptContainerId = recorded;
                    logger.LogInformation(
                        "[PROVISION] {RecordType} {RecordId} already records its own container {ContainerId} (no business " +
                        "unit, configured shared container or other record holds it). It is kept: no container is " +
                        "created and the value is not rewritten.", root.WireToken, recordId, recorded);
                    break;

                default:
                    // Exhaustive above. An unknown classification is never read as "its own".
                    throw new InvalidOperationException($"Unknown recorded-container classification '{holder.Kind}'.");
            }
        }

        Guid creatorId;
        Guid? recordCreatorToShare = null;
        if (resume && makeSecure)
        {
            // ── RESUME through Make Secure (round 40 items 1 and 2): the forward Make Secure path's rules ──
            //
            // The caller is identified, checked against the No Access list and shared at the creator's level (never lower
            // than they hold) — the share this run proves; the record's creator joins the colleague step, exactly as on the
            // forward path. Flagged or not: a Make Secure that failed after its first write is finished by the same call.
            var finish = await ResumeMakeSecureAsync(
                creatorRule, dataverseClient, recordShare, callerAccessProbe!, noAccessGuard, httpContext!, root, recordId, row,
                ownerTeamId, alreadyFlagged, logger, traceId, ct);

            if (finish.Error != null)
                return finish.Error;

            creatorId = finish.CallerId;
            recordCreatorToShare = finish.RecordCreator;
        }
        else if (resume)
        {
            // ── RESUME of an UNFLAGGED record: only its creator (owner round 10 item 10; task 150 verifier c1 item 4) ──
            //
            // The round-10 rule names an unflagged record, not the forward path: a resume would mark it secure too (the
            // flag write below). Every documented resume meets a FLAGGED row — since task 150 the flag is the forward
            // path's first write and nothing clears it on failure; before task 150 provisioning required it; and the
            // unsecure endpoint moves the owner away before clearing it — so this gate costs that recovery nothing and
            // refuses only anomalous rows (e.g. a manual Assign to the secure team). Before any write; a flagged row stays
            // on the route's delegation gate (Write and Share). (Make Secure — round 33 item 1 — never reaches here: the branch above.)
            // Task 158's inherited provisioning has no caller: the person is the recorded creator by construction.
            if (!alreadyFlagged && !creator.IsRecordedCreator)
            {
                var unflaggedResumeRefusal = await RefuseUnflaggedResumeUnlessCreatorAsync(
                    dataverseClient, callerAccessProbe!, httpContext!, root, recordId, row, ownerTeamId, logger, traceId, ct);

                if (unflaggedResumeRefusal != null)
                    return unflaggedResumeRefusal;
            }

            // ── RESUME: the person a resume shares to — createdby when a person, else sprk_createdbyperson ──
            var person = await ResolveResumeCreatorAsync(
                dataverseClient, root, recordId, row, ownerTeamId, logger, traceId, ct);

            if (person.Error != null)
                return person.Error;

            // ── RESUME: only the record's creator may name colleagues (task 133 verifier round 1) ──
            var colleagueRefusal = await RefuseResumeColleaguesUnlessCreatorAsync(
                request, creator, root, recordId, person.CreatorId, ownerTeamId, logger, traceId, ct);

            if (colleagueRefusal != null)
                return colleagueRefusal;

            // ── RESUME: that person must not be on the record's No Access list (task 143), nor on that of any secure
            // record it is filed under (owner round 31 item 1, task 158 r1) ──
            // Before the share (and before the flag write below), whoever ResolveResumeCreatorAsync named (createdby or
            // sprk_createdbyperson), asked about the record AS a secure record whatever its flag reads. Nothing is written
            // for a walled or unverifiable person; the record stays as the earlier run left it.
            var resumeWall = await CheckCreatorWallsAsync(noAccessGuard, root, recordId, person.CreatorId, ct);
            if (resumeWall.RefusesShare)
            {
                var walled = resumeWall.Outcome == SecureShareWallOutcome.Walled;
                logger.LogWarning(
                    "[PROVISION] RESUME of {RecordType} {RecordId} refused: its creator {CreatorId} is {State} the No " +
                    "Access list of {Where} ({Detail}). Nothing was written. TraceId={TraceId}",
                    root.WireToken, recordId, person.CreatorId, walled ? "on" : "not provably off", resumeWall.Where,
                    walled ? string.Join(",", resumeWall.EntryIds) : resumeWall.Fault, traceId);
                var record = root.DisplayLabel.ToLowerInvariant();
                var list = resumeWall.ParentTable is { } parentTable
                    ? $"the No Access list of the secure {SecureRootInheritance.WireTokenFor(parentTable)} it is filed under"
                    : resumeWall.FilingUnreadable ? "the No Access list of a secure record it is filed under" : "its No Access list";
                return Problem(
                    walled ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError,
                    walled ? "Conflict" : "Internal Server Error",
                    walled
                        ? $"The person who created this {record} is on {list}, so provisioning will not share it to them, " +
                          "and nothing was changed. An administrator assigns the record to the person who should hold it; " +
                          "that person then provisions it."
                        : $"Whether the person who created this {record} is on {list} could not be checked, so nothing was " +
                          "changed. The same caller may try again.",
                    traceId, (ReasonKey, ReasonResumeCreatorNoAccess));
            }

            // ── RESUME: the flag is the first write here too (task 150) ──
            var resumeFlag = await EnsureSecureFlagAsync(
                dataverseClient, root, recordId, alreadyFlagged, logger, traceId, ct);

            if (resumeFlag != null)
                return resumeFlag;

            // ── RESUME: ensure that person's share ──
            var resumed = await EnsureResumeCreatorShareAsync(
                creatorRule, recordShare, root, recordId, person.CreatorId, ownerTeamId, logger, traceId, ct);

            if (resumed.Error != null)
                return resumed.Error;

            creatorId = resumed.CreatorId;
        }
        else
        {
            // ── FORWARD, Make Secure (round 33 item 1): who created the record, read BEFORE any write ──
            // The caller is shared to as on every forward run; the creator, when a usable person other than the caller,
            // is shared to as well (owner round 27: "the person who created this record … will keep access"). A creator
            // that cannot be read refuses — never secured with its creator possibly locked out (ADR-003).
            if (makeSecure)
            {
                var creatorRead = await ResolveMakeSecureCreatorAsync(dataverseClient, root, recordId, row, logger, traceId, ct);
                if (creatorRead.Error != null)
                    return creatorRead.Error;

                recordCreatorToShare = creatorRead.CreatorId;
            }

            // ── FORWARD: share-first, move, prove, compensate ──
            var forward = await MoveWithCreatorShareAsync(
                creatorRule, dataverseClient, recordShare, creator, noAccessGuard, accessCacheInvalidator, relatedRoots, root, recordId, row,
                ownerTeamId, keepsOwnContainer: keptContainerId is not null, sharedContainerToUnlink, makeSecure, logger,
                traceId, ct);

            if (forward.Error != null)
                return forward.Error;

            creatorId = forward.CreatorId;
        }

        // ── Named colleagues: only once the share this run proves is in place ─────────
        // That share is creatorId's: the caller's on the forward path and on a Make Secure resume, the record creator's on
        // a resume without the transition (F8).
        int additionalShared;
        IReadOnlyList<ProvisionSkippedPrincipal> skippedPrincipals;
        try
        {
            (additionalShared, skippedPrincipals) = await ShareToColleaguesAsync(restrictedThroughFiling,
                dataverseClient, recordShare, noAccessGuard, request, root, recordId, creatorId, recordCreatorToShare, logger,
                traceId, ct);
        }
        finally
        {
            // ── Step 5.6 (task 132 · C12): ownership and shares changed — evict, before any return ──────────
            // Forward: the record left its business-unit owner for the memberless secure team (verified, Step 5) and the
            // creator and colleagues got explicit shares. Resume: the creator's share was ensured and colleagues shared.
            // Without this, a BU colleague whose cached membership contained the record through ownership keeps it on
            // Teams/SPA for the identity + membership TTLs, and every user's cached impersonated root set and access
            // snapshot for it stay stale. Runs once per successful run (the failure branches of the forward path evict
            // where their own owner change happens), on success or failure of the colleague step, before any later
            // return, and never fails provisioning: the hook does not throw and is not bound to the request's token.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);
        }

        // ── Task 114 (owner round 67, item 3: Restricted wins): the person not shared to, and whether anyone internal remains ──
        // The creator / Make Secure caller flagged external on a Restricted record was NOT shared to (above). They are named
        // like a skipped colleague, and when nobody internal can open the record now, the response says so plainly — an
        // administrator still sees it and shares it with an internal user. Never a failure: the record is provisioned.
        bool? noInternalReader = null;
        string? noInternalReaderMessage = null;
        // A person this run did not share to is never reported as shared to (task 114 verifier round 3): the empty GUID,
        // and they are named in skippedPrincipals instead.
        var sharedToCreator = creatorRule.SkippedCreator == creatorId ? Guid.Empty : creatorId;
        if (creatorRule.SkippedCreator is { } skippedCreator)
        {
            skippedPrincipals = skippedPrincipals
                .Append(new ProvisionSkippedPrincipal(skippedCreator, ReasonPrincipalExternalOnRestricted,
                    "This record is Restricted to internal users, and this person is flagged as external, so it was not " +
                    "shared with them."))
                .ToList();
            noInternalReader = await NoInternalReaderAsync(dataverseClient, recordShare, root, recordId, logger, traceId, ct);
            var label = root.DisplayLabel.ToLowerInvariant();
            noInternalReaderMessage = noInternalReader switch
            {
                true => "Nobody internal can open this " + label + " now: it is Restricted, and the people it would have " +
                        "been shared with are flagged as external. An administrator must share it with an internal user.",
                null => "Whether anyone internal can open this " + label + " could not be confirmed. Check its Manage " +
                        "Access, and have an administrator share it with an internal user if nobody is listed.",
                _ => null,
            };
        }

        // ── Step 8 (task 148): the record's EXISTING related records follow it into isolation ──
        //
        // Run once the record is isolated (Step 5), shared (Step 5.5 + colleagues) and has its container (Steps 6 + 7, or
        // the container it keeps). Every child it ALREADY has — documents, events, to-dos, communications, memos, and their
        // own children — is re-owned to the Secure Record owner team by the ownership rule (task 146), then the record's
        // sharees are mirrored onto them (task 149's synchronizer — task 149's former Step 8 —, which mirrors Secure-team-owned
        // rows only, so the mirror follows the re-own: the transient is an under-share of the sharees, never an over-share).
        // After the container, so a related record the rule cannot place never keeps the record from storing documents.
        // A pass that does not complete is not a success (ADR-003): children_incomplete, and calling again completes it —
        // the record is then provisioned, so the already-provisioned branch above runs the pass (one resume path, 133's).
        //
        // ── Then (round 26 item 3, wired at the batch-4 integration) the record's existing FILES follow it ──
        // Every document the pass leaves isolated moves into the record's own container through the ONE
        // DocumentContainerRelocator (purpose MakeSecure; copy with history, verify, re-point, delete the source). After the
        // child pass, because the relocator derives each document's container from its (now secure) parents. Incomplete →
        // files_incomplete; the record stays flagged and provisioned and the re-entry branch above relocates again.
        //
        // Task 158 (owner round 6): then the work assignments and projects FILED UNDER it (a matter or project) are made
        // secure themselves — each through this endpoint's own steps (ProvisionInheritedAsync), for the person who created
        // it — and this record's sharees are mirrored onto them (task 149's mechanism). They are roots of their own, so the
        // child pass above never touches them. A filed record that cannot be secured is the same children_incomplete error.
        async Task<(SecureChildPassSummary? Summary, MakeSecureFilesSummary? Files, SecureFiledRecordsSummary? Filed, IResult? Error)>
            ChildrenFollowAsync(string containerId)
        {
            var pass = await secureChildren.ReconcileAsync(
                root.LogicalName, recordId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, ct);
            if (!pass.IsComplete)
                return (null, null, null, ChildrenIncomplete(pass, root, recordId, logger, traceId));

            var filed = await relatedRoots.SecureFiledRootsUnderAsync(root.LogicalName, recordId, traceId, ct);
            if (!filed.IsComplete)
                return (null, null, null, RelatedRootsIncomplete(filed, root, recordId, logger, traceId));

            var files = await FilesFollowAsync(fileRelocator, pass, containerId, root, recordId, logger, traceId, ct);
            return files.Error is not null
                ? (null, null, null, files.Error)
                : (SecureChildPassSummary.From(pass), files.Summary, filed.Summary(), null);
        }

        // ── Steps 6 + 7: the record's own SPE container — kept when it already has one ──
        if (keptContainerId is not null)
        {
            var keptChildren = await ChildrenFollowAsync(keptContainerId);
            if (keptChildren.Error is not null)
                return keptChildren.Error;
            var childSummary = keptChildren.Summary;

            logger.LogInformation(
                "[PROVISION] Provisioning complete for {RecordType} {RecordId}: BU={BuId} ({BuName}), " +
                "OwnerTeam={TeamId}, Container={ContainerId} (its own, kept), Resumed={Resumed}",
                root.WireToken, recordId, secureBuId, secureBuName, ownerTeamId, keptContainerId, resume);

            return TypedResults.Ok(new ProvisionProjectResponse(
                BusinessUnitId: secureBuId,
                BusinessUnitName: secureBuName,
                OwnerTeamId: ownerTeamId,
                OwnerTeamName: ownerTeamName,
                SpeContainerId: keptContainerId,
                SharedToCreatorSystemUserId: sharedToCreator,
                AdditionalPrincipalsShared: additionalShared,
                RecordType: root.WireToken,
                RecordId: recordId,
                Resumed: resume,
                SkippedPrincipals: skippedPrincipals,
                Children: childSummary,
                FiledRecords: keptChildren.Filed)
            {
                Files = keptChildren.Files,
                NoInternalReader = noInternalReader,
                NoInternalReaderMessage = noInternalReaderMessage,
            });
        }

        // ── Step 6: Create the record's own SPE container ────────────────────
        // Bound to the Secure Record business unit — the unit that now owns the record (step 5) — so the SPE admin
        // plane's per-container rule (task 165, owner round 20) keeps it out of every customer administrator's reach.
        var containerResult = await CreateSpeContainerAsync(
            speFileStore, containerTypeId, root, recordName, recordId, ownerTeamId, secureBuId, logger, traceId, ct);
        if (containerResult.Error != null)
            return containerResult.Error;

        var speContainerId = containerResult.ContainerId!;

        // ── Step 7: Record the container on the record — FAIL if it cannot be written ──
        try
        {
            await RecordContainerAsync(dataverseClient, root, recordId, speContainerId, logger, ct);
        }
        catch (Exception ex)
        {
            // ADR-003. This used to be a catch + LogWarning + return 200, and that swallow is the
            // single reason the broken column names survived five months: provisioning created real
            // infrastructure, failed to record any of it, and reported success. A container nobody
            // recorded is invisible — so the error carries its id, which is the only thing that makes
            // the orphan reconcilable.
            logger.LogError(ex,
                "[PROVISION] Container {ContainerId} was created for {RecordType} {RecordId} but could " +
                "NOT be recorded on the record. The container is orphaned until an operator " +
                "reconciles it. TraceId={TraceId}",
                speContainerId, root.WireToken, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"An SPE container was created for {root.DisplayLabel.ToLowerInvariant()} {recordId} but could " +
                "not be recorded on the record. The record is owned by the Secure Record owner team and shared " +
                "to its creator, with no container recorded, so calling provisioning again (the same caller may) " +
                "resumes and creates and records a new container. The container named here holds nothing — " +
                "uploads to a secure record with no container of its own are refused — and can be deleted.",
                traceId,
                (ReasonKey, ReasonContainerNotRecorded),
                ("speContainerId", speContainerId),
                ("ownerTeamId", ownerTeamId));
        }

        logger.LogInformation(
            "[PROVISION] Provisioning complete for {RecordType} {RecordId}: BU={BuId} ({BuName}), " +
            "OwnerTeam={TeamId}, Container={ContainerId}, Resumed={Resumed}",
            root.WireToken, recordId, secureBuId, secureBuName, ownerTeamId, speContainerId, resume);

        // ── Step 8 (task 148) — see ChildrenFollowAsync above. A fan-out that does not complete is the children_incomplete
        // error now, no longer a logged warning (ADR-003).
        var children = await ChildrenFollowAsync(speContainerId);
        if (children.Error is not null)
            return children.Error;

        return TypedResults.Ok(new ProvisionProjectResponse(
            BusinessUnitId: secureBuId,
            BusinessUnitName: secureBuName,
            OwnerTeamId: ownerTeamId,
            OwnerTeamName: ownerTeamName,
            SpeContainerId: speContainerId,
            SharedToCreatorSystemUserId: sharedToCreator,
            AdditionalPrincipalsShared: additionalShared,
            RecordType: root.WireToken,
            RecordId: recordId,
            Resumed: resume,
            SkippedPrincipals: skippedPrincipals,
            Children: children.Summary,
            FiledRecords: children.Filed)
        {
            Files = children.Files,
            NoInternalReader = noInternalReader,
            NoInternalReaderMessage = noInternalReaderMessage,
        });
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Task 114 (owner round 67): the one share-eligibility rule for the person a provisioning run shares the record to
    /// (the Restricted branch of <see cref="InternalShareEndpoints.ClassifyEligibility"/>): on a Restricted record a user
    /// flagged <c>sprk_isexternal = true</c> is not shared to. Asked at most once per user per request (cached), so the
    /// share-first step and the steps after the move always agree.
    /// </summary>
    private sealed class RestrictedCreatorRule(DataverseWebApiClient client, bool recordIsRestricted)
    {
        private readonly Dictionary<Guid, bool?> _answers = new();

        /// <summary>The person this run did not share to (flagged external on a Restricted record), if any.</summary>
        public Guid? SkippedCreator { get; private set; }

        /// <summary><c>true</c> = not shared to; <c>false</c> = shared to; <c>null</c> = could not be read (fail closed).</summary>
        /// <param name="fresh">Read the flag again rather than answer from this request's cache (task 114 verifier V1: the
        /// last-resort grant after an unverifiable owner move asks a FRESH answer before writing a share).</param>
        public async Task<bool?> IsBarredAsync(Guid userId, ILogger logger, CancellationToken ct, bool fresh = false)
        {
            if (!recordIsRestricted)
                return false;
            if (!fresh && _answers.TryGetValue(userId, out var known))
                return known;

            bool? answer;
            try
            {
                var rows = await client.QueryAsync<InternalShareEndpoints.SystemUserRow>(
                    InternalShareEndpoints.SystemUserEntitySet, filter: "systemuserid eq " + userId,
                    select: "systemuserid,sprk_isexternal", top: 1, cancellationToken: ct);
                answer = InternalShareEndpoints.IsBarredOnRestricted(
                    rows.FirstOrDefault(r => r.Id == userId)?.IsExternal, rootIsRestricted: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "[PROVISION] Whether {UserId} is flagged external could not be read on a Restricted record; their share " +
                    "is not proven.", userId);
                answer = null;
            }

            _answers[userId] = answer;
            if (answer == true)
                SkippedCreator = userId;
            return answer;
        }
    }

    /// <summary>
    /// Task 114: whether the record now has NO enabled internal user (not flagged external) with a direct share that can
    /// read it — <c>null</c> when that could not be read. Display-only: it never fails provisioning.
    /// </summary>
    private static async Task<bool?> NoInternalReaderAsync(
        DataverseWebApiClient dataverseClient, IDataverseRecordShareService recordShare, SecureRecordRoot root, Guid recordId,
        ILogger logger, string traceId, CancellationToken ct)
    {
        try
        {
            var readers = (await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct))
                .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && RecordShareLevels.CanRead(s.AccessRightsMask))
                .Select(s => s.Principal.Id)
                .Distinct()
                .ToList();
            foreach (var batch in readers.Chunk(InternalShareEndpoints.NameBatchSize))
            {
                var users = await dataverseClient.QueryAsync<InternalShareEndpoints.SystemUserRow>(
                    InternalShareEndpoints.SystemUserEntitySet,
                    filter: string.Join(" or ", batch.Select(id => "systemuserid eq " + id)),
                    select: "systemuserid,isdisabled,sprk_isexternal",
                    top: batch.Length,
                    cancellationToken: ct);
                if (users.Any(u => batch.Contains(u.Id) && u.IsDisabled is false && u.IsExternal != true))
                    return false;
            }

            logger.LogWarning(
                "[PROVISION] Restricted {RecordType} {RecordId} was provisioned with NO internal reader (its people are flagged " +
                "external): an administrator must share it with an internal user. TraceId={TraceId}",
                root.WireToken, recordId, traceId);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[PROVISION] Whether anyone internal can open Restricted {RecordType} {RecordId} could not be read. " +
                "TraceId={TraceId}", root.WireToken, recordId, traceId);
            return null;
        }
    }

    /// <summary>
    /// Task 148 — a child pass that did not complete (ADR-003: never reported as success, never a bare 500). The record's own
    /// steps are NOT undone: it is isolated, shared and has its container, and a related record left behind is exactly as
    /// exposed as it was before this call, never more. The detail and extensions carry per-table counts; calling again
    /// completes it (the record is provisioned, so the already-provisioned branch runs the pass).
    /// </summary>
    private static async Task<(MakeSecureFilesSummary Summary, IResult? Error)> FilesFollowAsync(
        DocumentContainerRelocator? relocator, SecureChildReconcileReport pass, string containerId, SecureRecordRoot root,
        Guid recordId, ILogger logger, string traceId, CancellationToken ct)
    {
        if (pass.IsolatedDocumentIds.Count == 0)
            return (MakeSecureFilesSummary.None, null);
        if (relocator is null)
        {
            // Only a test composition of task 158's inherited provisioning omits it; production always registers it.
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId}: no file relocator in this host, so its {Count} file(s) were not moved. " +
                "TraceId={TraceId}", root.WireToken, recordId, pass.IsolatedDocumentIds.Count, traceId);
            return (MakeSecureFilesSummary.None, null);
        }

        var batch = await relocator.RelocateDocumentsAsync(
            pass.IsolatedDocumentIds, containerId, RelocationPurpose.MakeSecure, apply: true, ct);
        var summary = MakeSecureFilesSummary.From(batch);
        if (summary.Incomplete == 0)
        {
            logger.LogInformation(
                "[PROVISION] {RecordType} {RecordId}: its files are in its own container (moved={Moved}, examined={Examined}, " +
                "unresolvable={Unresolvable}). TraceId={TraceId}",
                root.WireToken, recordId, summary.Moved, summary.Examined, summary.Unresolvable, traceId);
            return (summary, null);
        }

        logger.LogError(
            "[PROVISION] {RecordType} {RecordId} is secured and shared and its related records are secured, but its files are " +
            "not all in its own container: moved={Moved} incomplete={Incomplete} unresolvable={Unresolvable} " +
            "counts={Counts}. TraceId={TraceId}",
            root.WireToken, recordId, summary.Moved, summary.Incomplete, summary.Unresolvable,
            JsonSerializer.Serialize(summary.Counts), traceId);

        return (summary, Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} is secured, shared and has its document container, and its related " +
            $"records are secured, but {summary.Incomplete} of its existing files are not moved into its own container yet " +
            $"({summary.Moved} moved). No file is more exposed than before this call. Calling provisioning again (the same " +
            "caller may) completes them; the secure-child reconciliation completes them regardless.",
            traceId,
            (ReasonKey, ReasonFilesIncomplete),
            ("filesExamined", summary.Examined),
            ("filesMoved", summary.Moved),
            ("filesIncomplete", summary.Incomplete),
            ("filesUnresolvable", summary.Unresolvable),
            ("fileCounts", summary.Counts),
            ("incompleteDocuments", summary.IncompleteDocuments),
            ("unresolvableDocuments", summary.UnresolvableDocuments),
            ("sourceChangedAfterMove", summary.SourceChangedAfterMove),
            ("versionsTruncated", summary.VersionsTruncated),
            ("sourceKeptForOtherRecords", summary.SourceKeptForOtherRecords)));
    }

    private static IResult ChildrenIncomplete(
        SecureChildReconcileReport pass, SecureRecordRoot root, Guid recordId, ILogger logger, string traceId)
    {
        var summary = SecureChildPassSummary.From(pass);
        var perTable = string.Join(", ", summary.Tables
            .Where(t => t.Reowned + t.Refused + t.Failed > 0)
            .Select(t => $"{t.Table}: {t.Reowned} re-owned, {t.Refused + t.Failed} not"));

        logger.LogError(
            "[PROVISION] {RecordType} {RecordId} is secured and shared, but its related records are not all secured: " +
            "status={Status} reowned={Reowned} remaining={Remaining} ({PerTable}) detail={Detail}. TraceId={TraceId}",
            root.WireToken, recordId, pass.Status, summary.Reowned, summary.Remaining, perTable, pass.Detail, traceId);

        return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} is secured, shared and has its document container, but its existing " +
            "related records (documents, events, to-dos, communications, memos) are not all secured yet: " +
            $"{summary.Reowned} re-owned, {summary.Remaining} remaining" + (perTable.Length > 0 ? $" ({perTable})" : "") +
            (pass.Detail is null ? "" : $" — {pass.Detail}") +
            ". No related record is more exposed than before this call. Calling provisioning again (the same caller may) " +
            "completes them; the secure-child reconciliation completes them regardless.",
            traceId,
            (ReasonKey, ReasonChildrenIncomplete),
            ("childrenReowned", summary.Reowned),
            ("childrenRemaining", summary.Remaining),
            ("childTables", summary.Tables));
    }

    /// <summary>
    /// Task 158 — the work assignments and projects filed under the record (a matter or project) were not all made secure
    /// themselves. Answered as task 148's <see cref="ReasonChildrenIncomplete"/> (the client already classifies it: the
    /// record is provisioned, calling again completes its related records), with <c>filedRecordsSecured</c>,
    /// <c>filedRecordsRemaining</c> and <c>filedRecords</c> (each record not secured, with the reason code its own
    /// provisioning answered) as extensions. Nothing done is undone; the secure-root inheritance job completes them too.
    /// </summary>
    private static IResult RelatedRootsIncomplete(
        SecureFiledRootsPass pass, SecureRecordRoot root, Guid recordId, ILogger logger, string traceId)
    {
        var summary = pass.Summary();
        var remaining = summary.Records.Where(r => r.IsOutstanding).ToList();

        logger.LogError(
            "[PROVISION] {RecordType} {RecordId} is secured, shared and its related records are isolated, but the work " +
            "assignments / projects filed under it are not all secured: status={Status} secured={Secured} remaining={Remaining} " +
            "({Records}) detail={Detail}. TraceId={TraceId}",
            root.WireToken, recordId, pass.Status, summary.Secured, remaining.Count,
            string.Join(", ", remaining.Select(r => $"{r.RecordType}:{r.RecordId:D}={r.Outcome}/{r.ReasonCode}")), pass.Detail,
            traceId);

        return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} is secured, shared and has its document container, but the work " +
            $"assignments and projects filed under it are not all secure yet: {summary.Secured} secured, {remaining.Count} " +
            "remaining" + (pass.Detail is null ? "" : $" — {pass.Detail}") +
            ". Each one is secured for the person who created it; one that cannot be (its creator is unknown or disabled, " +
            "or a read failed) is listed with its own reason. No record is more exposed than before this call. Calling " +
            "provisioning again (the same caller may) retries them; the secure-root inheritance job retries them regardless.",
            traceId,
            (ReasonKey, ReasonChildrenIncomplete),
            ("filedRecordsSecured", summary.Secured),
            ("filedRecordsRemaining", remaining.Count),
            ("filedRecords", remaining));
    }

    /// <summary>
    /// The refusal for every state in which the Secure Record topology is not provably safe to assign into. Each
    /// is returned BEFORE any mutation (task 144), so "nothing was written" is true of every one.
    /// </summary>
    /// <remarks>
    /// The unreadable BU and team reads keep the codes they always had (<c>secure_bu_not_found</c>,
    /// <c>secure_owner_team_not_found</c>), because the wizard already classifies those as "environment not set
    /// up". The two invariants task 144 adds each get a distinct "could not tell" code — an unreadable member count
    /// must never be reported as a clean one. A team with members, or a BU with users, is logged at CRITICAL: it is
    /// not only a refusal here but a live exposure of every secure record that already exists.
    /// </remarks>
    private static IResult TopologyRefusal(SecureOwnerTeamResolution topology, ILogger logger, string traceId)
    {
        var buName = topology.BusinessUnitName;
        var teamName = topology.OwnerTeamName;
        var buId = topology.BusinessUnitId;

        switch (topology.Status)
        {
            case SecureOwnerTeamStatus.BusinessUnitUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] Failed to resolve the Secure Record business unit by name '{BuName}'", buName);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to resolve the Secure Record business unit from Dataverse.", traceId,
                    (ReasonKey, ReasonSecureBuNotFound));

            case SecureOwnerTeamStatus.BusinessUnitNotFound:
                logger.LogError(
                    "[PROVISION] No business unit named '{BuName}' exists. Provisioning refused — a secure " +
                    "record must not be placed in any other business unit. TraceId={TraceId}", buName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"No business unit named '{buName}' exists in this environment. The canonical " +
                    "Secure Record business unit is created during environment setup; provisioning will " +
                    "not create one, and will not fall back to another business unit.",
                    traceId, (ReasonKey, ReasonSecureBuNotFound), ("businessUnitName", buName));

            case SecureOwnerTeamStatus.BusinessUnitAmbiguous:
                logger.LogError(
                    "[PROVISION] More than one business unit is named '{BuName}'. Provisioning refused — " +
                    "picking one would be arbitrary. TraceId={TraceId}", buName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"More than one business unit is named '{buName}'. Provisioning cannot choose " +
                    "between them. Resolve the duplicate, or set " +
                    $"'{SecureBusinessUnitNameConfigKey}' to an unambiguous name.",
                    traceId, (ReasonKey, ReasonSecureBuAmbiguous), ("businessUnitName", buName));

            case SecureOwnerTeamStatus.OwnerTeamUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] Failed to resolve the named owner team '{TeamName}' in business unit {BuId}",
                    teamName, buId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to resolve the Secure Record owner team from Dataverse.", traceId,
                    (ReasonKey, ReasonOwnerTeamNotFound));

            case SecureOwnerTeamStatus.OwnerTeamNotFound:
                logger.LogError(
                    "[PROVISION] Business unit '{BuName}' has no non-default owner team named '{TeamName}'. " +
                    "Provisioning refused — it will NOT fall back to the business unit's default team. " +
                    "TraceId={TraceId}", buName, teamName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"Business unit '{buName}' has no owner team named '{teamName}' (an Owner team that is " +
                    "not the business unit's default team). Create it per docs/guides/" +
                    "SECURE-PROJECT-ENVIRONMENT-SETUP.md §4, or set " +
                    $"'{SecureOwnerTeamNameConfigKey}'. Provisioning will not use the default team: its " +
                    "membership follows every user placed in the business unit and cannot be curated.",
                    traceId, (ReasonKey, ReasonOwnerTeamNotFound),
                    ("businessUnitId", buId), ("ownerTeamName", teamName));

            case SecureOwnerTeamStatus.OwnerTeamAmbiguous:
                logger.LogError(
                    "[PROVISION] More than one non-default owner team in '{BuName}' is named '{TeamName}'. " +
                    "Provisioning refused — picking one would be arbitrary. TraceId={TraceId}",
                    buName, teamName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"Business unit '{buName}' has more than one owner team named '{teamName}'. " +
                    "Provisioning cannot choose between them.",
                    traceId, (ReasonKey, ReasonOwnerTeamAmbiguous),
                    ("businessUnitId", buId), ("ownerTeamName", teamName));

            case SecureOwnerTeamStatus.OwnerTeamHasMembers:
                logger.LogCritical(
                    "[PROVISION] The secure owner team '{TeamName}' ({TeamId}) has member(s) {MemberIds}. Every " +
                    "secure record it owns is readable by each of them. Provisioning refused. TraceId={TraceId}",
                    teamName, topology.OwnerTeamId, topology.MemberIds, traceId);
                // WHO the members are goes to the operator log above, not to the caller: any Write holder on any
                // secure record can reach this response, and the remedy is an administrator's, not theirs.
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The secure owner team '{teamName}' has members. It owns every secure record, so each " +
                    "member reads all of them. An administrator must remove every member (human or application " +
                    "user) before provisioning; nothing was changed.",
                    traceId, (ReasonKey, ReasonOwnerTeamHasMembers),
                    ("ownerTeamId", topology.OwnerTeamId), ("memberCount", topology.MemberIds.Count));

            case SecureOwnerTeamStatus.OwnerTeamMembershipUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] The membership of the secure owner team {TeamId} could not be read. Refusing — " +
                    "an unreadable count is not zero. TraceId={TraceId}", topology.OwnerTeamId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The secure owner team's membership could not be read, so it cannot be shown to be empty. " +
                    "Nothing was changed; retry when Dataverse is reachable.",
                    traceId, (ReasonKey, ReasonOwnerTeamMembershipUnreadable),
                    ("ownerTeamId", topology.OwnerTeamId));

            case SecureOwnerTeamStatus.BusinessUnitHasUsers:
                logger.LogCritical(
                    "[PROVISION] User(s) {UserIds} sit in the Secure Record business unit '{BuName}' ({BuId}). " +
                    "With Business Unit or Deep depth on a secure table they read every secure record. " +
                    "Provisioning refused. TraceId={TraceId}", topology.BusinessUnitUserIds, buName, buId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The Secure Record business unit '{buName}' contains users. It must hold none — a user " +
                    "there reads every secure record by business-unit depth, whoever owns them. Nothing was " +
                    "changed; an administrator must move them out (this is an owner decision, not something " +
                    "provisioning does).",
                    traceId, (ReasonKey, ReasonSecureBuHasUsers),
                    ("businessUnitId", buId), ("userCount", topology.BusinessUnitUserIds.Count));

            case SecureOwnerTeamStatus.BusinessUnitUsersUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] The users of the Secure Record business unit {BuId} could not be read. " +
                    "Refusing — an unreadable count is not zero. TraceId={TraceId}", buId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The Secure Record business unit's users could not be read, so it cannot be shown to hold " +
                    "none. Nothing was changed; retry when Dataverse is reachable.",
                    traceId, (ReasonKey, ReasonSecureBuUsersUnreadable), ("businessUnitId", buId));

            default:
                // Exhaustive above. An unknown status is refused, not treated as resolved.
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The Secure Record owner team could not be resolved.", traceId,
                    (ReasonKey, ReasonOwnerTeamNotFound));
        }
    }

    /// <summary>
    /// What the container ALREADY recorded on a record that is not yet owned by the secure owner team is — read-only,
    /// before any write (task 133, found live 2026-10-02: provisioning 65a3fab2, which already carried its own
    /// container, created a second one and overwrote <c>sprk_containerid</c>, orphaning the first and anything in it).
    /// </summary>
    /// <remarks>
    /// <para><b>The order is load-bearing.</b> Shared containers are recognised FIRST: a business unit's container
    /// (<c>businessunit.sprk_containerid</c>, the pre-task-076 create-time cascade) or one this BFF is configured to use
    /// for many records (<see cref="SharedContainerConfigKeys"/>). Many records legitimately carry such a value — three
    /// live projects share the root business unit's — so it is replaced by the record's own and the shared container
    /// stays where the business unit or configuration points (nothing is orphaned) — unlinked from the record before the
    /// owner move (task 133 r1). Only then is "another project, matter or work assignment records the same container" a
    /// REFUSAL: a container two records claim, which is not shared storage, belongs to one of them, and provisioning
    /// cannot tell which. Anything else is this record's own and is KEPT (and moved only once its creator's share can be
    /// set up first — see <see cref="MoveWithCreatorShareAsync"/>).</para>
    ///
    /// <para><b>Fail closed.</b> Any failed read is <see cref="RecordedContainerKind.Unreadable"/> — never "its own"
    /// (which would keep a container another record may hold) and never "shared" (which would orphan the record's own).
    /// Every query is bounded to one row: one match is enough to decide.</para>
    /// </remarks>
    private static async Task<RecordedContainer> ClassifyRecordedContainerAsync(
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        SecureRecordRoot root,
        Guid recordId,
        string containerId,
        CancellationToken ct)
    {
        foreach (var key in SharedContainerConfigKeys)
        {
            if (string.Equals(configuration[key]?.Trim(), containerId, StringComparison.Ordinal))
                return new RecordedContainer(RecordedContainerKind.Configured, ConfigKey: key);
        }

        var literal = containerId.Replace("'", "''", StringComparison.Ordinal);
        try
        {
            var businessUnits = await dataverseClient.QueryAsync<BusinessUnitContainerRow>(
                "businessunits",
                filter: $"sprk_containerid eq '{literal}'",
                select: "businessunitid",
                top: 1,
                cancellationToken: ct);

            if (businessUnits.FirstOrDefault() is { } businessUnit)
                return new RecordedContainer(RecordedContainerKind.BusinessUnit, BusinessUnitId: businessUnit.businessunitid);

            foreach (var other in SecureRecordRoot.All)
            {
                var filter = other == root
                    ? $"sprk_containerid eq '{literal}' and {other.IdColumn} ne {recordId}"
                    : $"sprk_containerid eq '{literal}'";

                var holders = await dataverseClient.QueryAsync<RootRow>(
                    other.EntitySet, filter: filter, select: other.IdColumn, top: 1, cancellationToken: ct);

                if (holders.FirstOrDefault() is { } holder)
                {
                    return new RecordedContainer(
                        RecordedContainerKind.AnotherRecord, OtherRoot: other, OtherRecordId: holder.IdFrom(other.IdColumn));
                }
            }
        }
        catch (Exception ex)
        {
            return new RecordedContainer(RecordedContainerKind.Unreadable, Fault: ex);
        }

        return new RecordedContainer(RecordedContainerKind.Own);
    }

    /// <summary>
    /// The forward path (task 133, C11): resolve the creator, share to them BEFORE the owner move where the platform
    /// allows it, move the record to the team, prove the creator's share on the moved record — and if that cannot be
    /// proven, move the record back to its pre-call owner and put the creator's share back the way it was.
    /// </summary>
    /// <remarks>
    /// <para><b>Why share-first.</b> Before task 133 the share was issued only AFTER the move. When it failed, the
    /// record was already owned by a memberless team, so its creator could not open it, could not retry (the
    /// delegation filter wants Write, which they no longer held), and anyone else who retried hit the 409. Sharing
    /// while the record still sits where it was created means the creator's access never depends on a write made after
    /// they lost it.</para>
    ///
    /// <para><b>Two platform behaviours are proven live, not assumed</b> (task 133 manual gate): (a) a share to the
    /// record's CURRENT owner is accepted; (b) that share survives the reassignment. This method does not depend on
    /// either: if a share to a creator who owns the record is refused, it falls back to the post-move grant; if the
    /// move drops the share, Step 5.5 re-issues it. Either way the post-move read is what decides.</para>
    ///
    /// <para><b>Compensation restores ownership and shares, never <c>sprk_issecure</c></b> — the access state every
    /// earlier refusal already leaves. No COMPENSATED branch gives anyone access they lacked before the call: a share
    /// this call issued is removed when the move is undone (ADR-003 path C, recorded in the task 133 note).</para>
    ///
    /// <para><b>The rows the move cascades to</b> (task 133 c1, owner round 10 item 4). An owner move of a project or a
    /// matter re-owns its SharePoint document locations and documents too (Assign cascade; accepted for the move OUT by
    /// owner round 4 item 3). The move BACK does the same, to the RECORD's pre-call owner — not each child's own. So each
    /// child's own owner is snapshotted before any write (<see cref="AssignCascadeChildOwners"/>; a snapshot that cannot
    /// be taken refuses the run, nothing written), and after a VERIFIED move back each child that does not read as its own
    /// owner is assigned back and read back. A child that cannot be is named in the response
    /// (<c>cascade_children_not_restored</c>) and a CRITICAL log line, with the call that puts it back. After an
    /// UNVERIFIED move back nothing is restored (whether the record moved is unknown); the children it would have left on
    /// the wrong owner are named instead. On success the children stay with the team, as the owner accepted.</para>
    ///
    /// <para><b>The one residual widening — the UNVERIFIED owner move.</b> When the owner cannot be read back, the
    /// creator's share (<see cref="CreatorAccessRights"/>, which carries <c>ShareAccess</c>) is kept or issued and NOT
    /// undone, because whether the move landed is unknown and removing the share risks a record nobody can open (S5).
    /// If the PATCH had in fact not landed and the pre-call owner was a team, the creator now holds an explicit share
    /// they did not hold before, on a record they already had Write on. Stated in the task 133 note §4 and accepted in
    /// its code review; nothing else on this path widens.</para>
    ///
    /// <para><b>A record that keeps its own container</b> (<paramref name="keepsOwnContainer"/>, task 133 r1). Once the
    /// team owns it, the 409 marker answers every later call, so it can never be resumed. Two rules follow. (1) It is
    /// moved only once the creator's share can be set up FIRST: when the pre-call share set cannot be read, share-first is
    /// impossible and the run is refused before any write (a transient read failure — the same caller retries), instead
    /// of moving it with no share. The fallback to a post-move grant for a creator who OWNS the record (live gate (a))
    /// stays, because refusing there would refuse every re-securing for good if Dataverse never accepts a share to a
    /// record's owner. (2) Every failure after its move tells the truth about the next call — <c>already_provisioned</c>,
    /// not a resume — and names the recovery that works without it: an administrator's Manage Access share
    /// (<c>containerKept: true</c>).</para>
    ///
    /// <para><b>A shared container is unlinked before the move</b> (<paramref name="sharedContainerToUnlink"/>, task 133
    /// r1). Otherwise a failure after the move (the share and the undo, or Step 6 / Step 7) would leave the team owning a
    /// record with a container recorded — the 409 marker — that was never finished, and whose uploads go to the shared
    /// container. Unlinked, every such failure leaves "owned by the team, no container", which the next call resumes.
    /// The unlink is the first write, after every read: a failure of it changes nothing else.</para>
    /// </remarks>
    private static async Task<CreatorShareStep> MoveWithCreatorShareAsync(
        RestrictedCreatorRule creatorRule,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        ProvisioningCreator creator,
        SecureShareNoAccessGuard noAccessGuard,
        IMembershipCacheInvalidator accessCacheInvalidator,
        SecureRootInheritance relatedRoots,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        bool keepsOwnContainer,
        string? sharedContainerToUnlink,
        bool makeSecure,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        Guid? resolved;
        if (creator.IsRecordedCreator)
        {
            // Task 158: an INHERITED provisioning has no caller. The person the record is secured for is the one who
            // created it, decided by the RESUME rule (createdby when it is a usable person, else the BFF-stamped
            // sprk_createdbyperson) — read-only, before anything is written; a refusal changes nothing.
            var recorded = await ResolveResumeCreatorAsync(
                dataverseClient, root, recordId, row, ownerTeamId, logger, traceId, ct, resuming: false);
            if (recorded.Error != null)
                return recorded;
            resolved = recorded.CreatorId;
        }
        else
        {
            // The creator is identified from their own token via WhoAmI (task 061) — never from the request body, so a
            // caller cannot nominate someone else. Resolved before anything is written.
            resolved = await creator.CallerSystemUserIdAsync(ct);
        }

        if (resolved is not { } creatorId || creatorId == Guid.Empty)
            return CreatorShareStep.Failed(CallerUnresolved(root, recordId, logger, traceId));

        // ── Make Secure (round 46 item 1): the caller's EFFECTIVE rights before the call, in the same step as WhoAmI ──
        // The floor their share is kept at (a Full Access holder by share, ownership or role keeps Delete). Read before any
        // write; a read that fails refuses — never a floor of "nothing", which would narrow them (ADR-003). The wizards'
        // path makes no such read: their creator's share is EXACTLY the creator's level.
        var callerHeldMask = 0;
        if (makeSecure)
        {
            // Make Secure comes only from an HTTP caller (the inherited provisioning sends no transition).
            var floor = await ReadMakeSecureCallerFloorAsync(
                creator.Probe!, creator.CallerToken, root, recordId, creatorId, logger, traceId, ct);
            if (floor.Error != null)
                return CreatorShareStep.Failed(floor.Error);

            callerHeldMask = floor.HeldMask;
        }

        // ── Owner round 10 item 10 (task 150): an UNFLAGGED record is secured only for the person who created it ──
        //
        // Before any write, like every refusal on this path. A record already flagged true stays on the route's Write
        // gate (rollout constraint: an older client flags at create time, and rows from before task 150 arrive flagged).
        // Make Secure (round 33 item 1) is the exception: securing an EXISTING record is held to the delegation gate (Write and Share) (owner R3b).
        if (row.sprk_issecure != true && !makeSecure)
        {
            var notCreator = await RefuseUnlessRecordCreatorAsync(
                dataverseClient, root, recordId, row, creatorId, logger, traceId, ct);

            if (notCreator != null)
                return CreatorShareStep.Failed(notCreator);
        }

        // ── Owner N6 (task 143) + owner round 31 item 1 (task 158 r1): a creator walled off the record — by its OWN No
        // Access list or by that of any secure record it is filed under — is refused BEFORE any change ──
        // In task 133's order this is before Step 4.1 (the first write) and the share-first step, so nothing is flagged,
        // moved, unlinked or shared. It never depends on the flag: on the inherited path the record is still unflagged
        // here, and the record is asked about AS the secure record it is becoming (CheckForSecuringAsync). The message
        // names no entry and no reason (the refusal contract).
        var wall = await CheckCreatorWallsAsync(noAccessGuard, root, recordId, creatorId, ct);
        if (wall.RefusesShare)
            return CreatorShareStep.Failed(
                CallerWallRefusal(root, recordId, creatorId, wall, logger, traceId, creator.IsRecordedCreator));

        // The owner compensation would restore. Every Dataverse row has one; a row read without it is not one this
        // endpoint can safely move, because the move could not be undone.
        if (row.Owner is not { } preOwner)
        {
            logger.LogError(
                "[PROVISION] {RecordType} {RecordId} was read without an owning user or team. Refusing before any " +
                "change: a move that cannot be undone is not attempted. TraceId={TraceId}",
                root.WireToken, recordId, traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()}'s current owner could not be read, so provisioning could " +
                "not guarantee it could put the record back if a later step failed. Nothing was changed. The record was " +
                "read without an owning user or team, so calling again repeats this refusal: an administrator checks " +
                "the record's owner in Dataverse first.",
                traceId, (ReasonKey, ReasonRecordOwnerUnreadable)));
        }

        // The rows the owner move cascades to, each with its OWN owner (task 133, owner round 10 item 4). The move to the
        // team re-owns them — accepted (owner round 4 item 3) — and an undo re-owns them again, to the RECORD's pre-call
        // owner, which is not each child's own when they differed. Read before any write so compensation can put each
        // back. Fail closed: a move whose cascade could not be undone child by child is not attempted.
        var cascade = await AssignCascadeChildOwners.SnapshotAsync(dataverseClient, root.LogicalName, recordId, ct);
        if (cascade.Snapshot is not { } cascadeSnapshot)
            return CreatorShareStep.Failed(CascadeChildrenUnreadable(root, recordId, cascade, logger, traceId));

        // The creator's share BEFORE the call — complete, or not at all. An unreadable list is never "no share"
        // (ADR-003): without it, share-first is not used, and compensation's only safe target is "no share".
        int? preCreatorMask;
        try
        {
            var preShares = await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct);
            preCreatorMask = MaskOf(preShares, creatorId);
        }
        catch (Exception ex)
        {
            preCreatorMask = null;
            logger.LogWarning(ex,
                "[PROVISION] The shares on {RecordType} {RecordId} could not be read completely before any change. " +
                "Share-first is not used for this call: the creator's share is issued after the owner move, and if " +
                "the move is undone the creator's explicit share is removed entirely — including any share they held " +
                "before the call, which cannot be told apart. TraceId={TraceId}",
                root.WireToken, recordId, traceId);
        }

        // Round 40 item 2 / round 46 item 1: a Make Secure caller is shared at the creator's level, never LOWER than what they
        // held before the call — their effective rights read above (share, ownership or role), the pre-call share when it
        // could be read, and what a later read shows — so a Full Access holder who runs Make Secure keeps Full Access. The
        // wizards' creator gets EXACTLY the creator's level (null: the default target).
        Func<int, int>? shareTarget = makeSecure
            ? held => MakeSecureCallerMask(held | (preCreatorMask ?? 0) | callerHeldMask)
            : null;

        // ── A record that keeps its own container moves only after share-first (task 133 r1) ──
        if (keepsOwnContainer && preCreatorMask is null)
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} keeps its own container, and its shares could not be read before " +
                "any change, so its creator's share cannot be set up before the owner move. Refusing before any change: " +
                "once the team owns a record that records a container, no provisioning call can finish it. " +
                "TraceId={TraceId}", root.WireToken, recordId, traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()} keeps its own SPE container, so it is moved to the Secure " +
                "Record owner team only once its creator's share is set up first — once the team owns it, a later call " +
                "answers already_provisioned and cannot finish it. The record's shares could not be read, so that share " +
                "could not be set up, and provisioning stopped BEFORE changing anything: its ownership and shares are as " +
                "they were. The same caller may retry once Dataverse is reachable.",
                traceId, (ReasonKey, ReasonCreatorShareFailed),
                ("ownershipRestored", true), ("sharesRestored", true), ("containerKept", true)));
        }

        // ── Step 4.1: the secure flag — the FIRST write of the forward path (task 150) ──
        //
        // After every read and every refusal above, so each of those still leaves "nothing changed" true — and before
        // anything else is written, so from here on every failure leaves a record that is FLAGGED: its uploads are
        // refused (RecordContainerResolver fails closed on a secure record with no container of its own), which is the
        // state a secure-requested record must be in whenever provisioning did not finish.
        var flagRefusal = await EnsureSecureFlagAsync(
            dataverseClient, root, recordId, row.sprk_issecure == true, logger, traceId, ct);

        if (flagRefusal != null)
            return CreatorShareStep.Failed(flagRefusal);

        // ── Step 4.2: unlink a SHARED container before the owner move (task 133 r1) ──
        var unlinkedNote = string.Empty;
        if (sharedContainerToUnlink is not null)
        {
            try
            {
                await dataverseClient.UpdateAsync(
                    root.EntitySet,
                    recordId,
                    new Dictionary<string, object?> { ["sprk_containerid"] = null },
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[PROVISION] Could not unlink shared container {ContainerId} from {RecordType} {RecordId} before the " +
                    "owner move. Stopped: nothing else was written. TraceId={TraceId}",
                    sharedContainerToUnlink, root.WireToken, recordId, traceId);

                return CreatorShareStep.Failed(Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The {root.DisplayLabel.ToLowerInvariant()} records a shared SPE container (a business unit's, or " +
                    "one this service uses for many records), which provisioning replaces with a container of its own. " +
                    "That link is removed before the record is moved to the Secure Record owner team, and removing it " +
                    "failed, so provisioning stopped: the record's ownership and shares were not changed (the link may or " +
                    "may not have been removed). The same caller may retry.",
                    traceId, (ReasonKey, ReasonSharedContainerNotCleared), ("speContainerId", sharedContainerToUnlink)));
            }

            logger.LogInformation(
                "[PROVISION] Unlinked shared container {ContainerId} from {RecordType} {RecordId} before the owner move " +
                "(its business unit or configuration keeps it); the record gets its own container.",
                sharedContainerToUnlink, root.WireToken, recordId);

            unlinkedNote = "The shared SPE container it recorded was unlinked from it before the move (its business " +
                           "unit or configuration keeps it), so uploads to the record are refused until it has a container " +
                           "of its own. ";
        }

        var wroteCreatorShare = false;   // this call wrote, or tried to write, the creator's share
        var creatorShareProven = false;  // a read has shown the creator holding exactly CreatorAccessMask

        // ── Step 4.5: SHARE-FIRST ────────────────────────────────────────────
        if (preCreatorMask is { } knownPreMask)
        {
            var first = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct, shareTarget, creatorRule);
            wroteCreatorShare = first.WriteAttempted;
            creatorShareProven = first.Proven && !first.Barred; // a barred person holds no share (verifier V2)

            if (!first.Proven)
            {
                var restored = !wroteCreatorShare
                               || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, knownPreMask, preOwner, logger, ct);

                if (restored && preOwner == DataversePrincipalRef.User(creatorId))
                {
                    // The creator still owns the record, so ownership gives them access until the move. Whether
                    // Dataverse accepts a share to a record's CURRENT owner is live gate (a); if it does not, share-first
                    // can never succeed for a creator-owned record. Carry on with the post-move grant, which
                    // compensation covers — nothing is left that this call added.
                    logger.LogWarning(
                        "[PROVISION] The pre-move share to creator {CreatorId}, who owns {RecordType} {RecordId}, was not " +
                        "confirmed. Falling back to the share after the owner move. TraceId={TraceId}",
                        creatorId, root.WireToken, recordId, traceId);
                    wroteCreatorShare = false;
                }
                else
                {
                    logger.LogError(
                        "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be " +
                        "confirmed before the owner move. Stopped with ownership unchanged (share restored: {Restored}). " +
                        "TraceId={TraceId}", root.WireToken, recordId, creatorId, restored, traceId);

                    return CreatorShareStep.Failed(Problem(
                        StatusCodes.Status500InternalServerError, "Internal Server Error",
                        "The record could not be shared to its creator, so provisioning stopped BEFORE moving it. Its " +
                        "ownership was not changed. " + (restored
                            ? "The creator's share is as it was before the call (read back). "
                            : "A share this call may have issued to the creator could not be confirmed removed; it " +
                              "shows under Manage Access. ") +
                        unlinkedNote + "The same caller may retry.",
                        traceId, (ReasonKey, ReasonCreatorShareFailed),
                        ("ownershipRestored", true), ("sharesRestored", restored)));
                }
            }
        }

        // ── Step 5: the owner move, read back ────────────────────────────────
        //
        // Ownership is the SECURITY step and the container the storage step, so the move comes before the container:
        // a storage failure then leaves the record inside the Secure Record business unit, never outside it.
        var move = await MoveOwnerAsync(
            dataverseClient, root, recordId, DataversePrincipalRef.Team(ownerTeamId), logger, ct);

        if (move.Outcome == OwnerMoveOutcome.NotMoved)
        {
            // Read back and NOT moved: nothing moved, so undo the only other write — the share-first grant.
            var restored = !wroteCreatorShare
                           || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, preCreatorMask ?? 0, preOwner, logger, ct);

            var cause = move.PatchRefused
                ? "Dataverse refused the assignment to the Secure Record owner team. If this is a privilege error, " +
                  "the owner team lacks the entity privileges an assignment target must hold — see the Secure Record " +
                  "Owner role in docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §5."
                : "Dataverse accepted the ownership assignment but the record is still not owned by the Secure Record " +
                  "owner team. This is the silent-navigation-property failure mode: an unrecognised @odata.bind " +
                  "property is accepted and ignored rather than rejected.";

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                (cause + " The owner was read back and is unchanged, so the record was not moved. " + (restored
                    ? "The creator's share is as it was before the call. "
                    : "A share this call issued to the creator could not be confirmed removed; it shows under Manage " +
                      "Access. ") + unlinkedNote).TrimEnd(),
                traceId,
                (ReasonKey, move.PatchRefused ? ReasonOwnerAssignmentFailed : ReasonOwnerAssignmentNotApplied),
                ("ownerTeamId", ownerTeamId), ("sharesRestored", restored)));
        }

        if (move.Outcome == OwnerMoveOutcome.Unverified)
        {
            // The PATCH may have landed. Whatever happened, the creator's share must be in place (S5). It is PROVEN by
            // the same complete read every other share write on this path uses (task 133 verifier round 1) — a share
            // proven before the move is not assumed to have survived it (live gate (b)).
            var ensured = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct, shareTarget, creatorRule);
            // Task 114 verifier V2: a person barred on a Restricted record holds NO share — "proven" only that none is owed —
            // so it is never reported as a share in place.
            var creatorBarred = ensured.Barred;
            var shareConfirmed = ensured.Proven && !ensured.Barred;
            var shareIssued = shareConfirmed;

            if (!ensured.Proven)
            {
                // The read or the write failed. Issue the share without a read to confirm it: leaving no share risks a
                // record nobody can open if the move DID land. The response then says the share is NOT confirmed. A Make
                // Secure caller's is issued at their floor (round 46 item 1: never lower than they held), not below it.
                // Task 114 verifier V1: never to a person flagged external on a Restricted record — a FRESH read of the
                // flag must answer "not barred" before this unconfirmed write; barred or unreadable, nothing is written.
                var barredNow = await creatorRule.IsBarredAsync(creatorId, logger, ct, fresh: true);
                if (barredNow == false)
                {
                    var fallbackMask = shareTarget?.Invoke(0) ?? CreatorAccessMask;
                    var fallbackRights = fallbackMask == CreatorAccessMask
                        ? CreatorAccessRights
                        : RecordShareLevels.RightsCsvForMask(fallbackMask);
                    try
                    {
                        await recordShare.GrantAccessAsync(
                            root.EntitySet, recordId, DataversePrincipalRef.User(creatorId), fallbackRights, ct);
                        shareIssued = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "[PROVISION] The creator's share on {RecordType} {RecordId} could not be issued after an " +
                            "unverifiable owner move. TraceId={TraceId}", root.WireToken, recordId, traceId);
                    }
                }
                else
                {
                    creatorBarred = barredNow == true;
                    logger.LogError(
                        "[PROVISION] No last-resort share to {CreatorId} on {RecordType} {RecordId} after an unverifiable " +
                        "owner move: {Why}. TraceId={TraceId}", creatorId, root.WireToken, recordId,
                        creatorBarred
                            ? "the record is Restricted and they are flagged external"
                            : "whether they are flagged external on this Restricted record could not be read",
                        traceId);
                }

                // A share proven before the move was issued too, even if it cannot be confirmed now.
                shareIssued |= creatorShareProven;
            }

            // Task 132 (C12): the PATCH may have re-owned the record, and the creator's share may have been written —
            // evict (always safe, never fails the request) before either answer below.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);

            if (creatorBarred)
            {
                logger.LogCritical(
                    "[PROVISION] {RecordType} {RecordId}: the owner move could not be verified, and the record is Restricted " +
                    "while {CreatorId} is flagged external, so it was NOT shared to them. If the Secure Record owner team now " +
                    "owns it, no internal user may be able to open it. TraceId={TraceId}",
                    root.WireToken, recordId, creatorId, traceId);

                return CreatorShareStep.Failed(Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The record's owner could not be read back after the assignment, so whether it is now owned by the " +
                    "Secure Record owner team is not known. " + unlinkedNote +
                    "This record is Restricted to internal users and the person it would be shared to is flagged as " +
                    "external, so it was NOT shared to them. If the team now owns it, no internal user may be able to " +
                    "open it: an administrator shares it with an internal user and calls provisioning again. If the " +
                    "assignment did not take effect, the record is where it was and provisioning can be called again.",
                    traceId, (ReasonKey, ReasonOwnerAssignmentUnverified), ("ownerTeamId", ownerTeamId),
                    ("creatorShareConfirmed", false),
                    ("creatorShareSkippedReason", ReasonPrincipalExternalOnRestricted),
                    ("containerKept", keepsOwnContainer)));
            }

            if (shareIssued)
            {
                string afterText;
                if (shareConfirmed)
                {
                    afterText = "The creator's share is in place (read back), so the creator can open the record either " +
                                "way, and the same caller may retry: " + (keepsOwnContainer
                                    ? "the record keeps its own SPE container, so if the team now owns it provisioning " +
                                      "is complete and that call answers already_provisioned; if the team does not own " +
                                      "it, it is provisioned from the start."
                                    : "a record the team now owns is resumed, and one it does not own is provisioned " +
                                      "from the start.");
                }
                else if (keepsOwnContainer)
                {
                    afterText = "A share to the creator was issued but could not be read back, so it is NOT confirmed. " +
                                "If the team does not own the record, it is where it was and the creator may call again: " +
                                "it is provisioned from the start. If the team owns it, " + KeptContainerRecovery;
                }
                else
                {
                    afterText = "A share to the creator was issued but could not be read back, so it is NOT confirmed. If " +
                                "the creator can open the record they may call again: a record the team now owns is " +
                                "resumed, and one it does not own is provisioned from the start. If they cannot, an " +
                                "administrator (who holds Write and Share on it) calls provisioning again: it resumes and ensures " +
                                "the share for the person who created the record.";
                }

                return CreatorShareStep.Failed(Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The record's owner could not be read back after the assignment, so whether it is now owned by the " +
                    "Secure Record owner team is not known. " + unlinkedNote + afterText,
                    traceId, (ReasonKey, ReasonOwnerAssignmentUnverified), ("ownerTeamId", ownerTeamId),
                    ("creatorShareConfirmed", shareConfirmed), ("containerKept", keepsOwnContainer)));
            }

            logger.LogCritical(
                "[PROVISION] {RecordType} {RecordId}: the owner move could not be verified AND no share to creator " +
                "{CreatorId} could be issued. If the Secure Record owner team now owns it, NOBODY can open it. " +
                "{Recovery} TraceId={TraceId}",
                root.WireToken, recordId, creatorId, AdministratorRecoveryForLog(keepsOwnContainer), traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The record's owner could not be read back after the assignment, and no share to its creator could be " +
                "issued. " + unlinkedNote + "If the record is now owned by the Secure Record owner team, nobody can open " +
                "it, and its creator no longer passes the Write and Share check this endpoint requires: " + (keepsOwnContainer
                    ? KeptContainerRecovery + " "
                    : "an administrator (who holds Write and Share on it) calls provisioning again, which resumes and shares the " +
                      "record to the person who created it. ") +
                "If the assignment did not take effect, the record is where it was and its creator calls provisioning " +
                "again.",
                traceId, (ReasonKey, ReasonCreatorShareFailedResumable), ("ownerTeamId", ownerTeamId),
                ("containerKept", keepsOwnContainer)));
        }

        // ── Step 5.5: prove the creator's share on the moved record ───────────
        var proof = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct, shareTarget, creatorRule);
        if (proof.Proven)
            return CreatorShareStep.Ok(creatorId);

        wroteCreatorShare |= proof.WriteAttempted;

        // Task 132 (C12): the verified move to the team (Step 5) changed who can read the record, and Step 5.5 may have
        // written the creator's share. Evict now — the compensation below is a second, separate owner change and evicts
        // for its own outcome. (On the proven path the caller evicts once after the colleagues' shares.)
        await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);

        // ── COMPENSATE: back to the pre-call owner, read back ────────────────
        logger.LogError(
            "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be proven after " +
            "the owner move. Moving the record back to its pre-call owner {OwnerKind} {OwnerId}. TraceId={TraceId}",
            root.WireToken, recordId, creatorId, preOwner.Kind, preOwner.Id, traceId);

        var back = await MoveOwnerAsync(dataverseClient, root, recordId, preOwner, logger, ct);
        if (back.Outcome == OwnerMoveOutcome.Moved)
        {
            // The move back cascaded like the move out, giving every child the RECORD's pre-call owner: each is put back
            // on its own (owner round 10 item 4), read back child by child.
            var children = await AssignCascadeChildOwners.RestoreAsync(dataverseClient, cascadeSnapshot, logger, ct);

            var restored = !wroteCreatorShare
                           || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, preCreatorMask ?? 0, preOwner, logger, ct);

            // Task 132 (C12): the move back (verified) and the creator-share restore changed who can read the record, and
            // each child put back on its own owner is an owner change of its own — evict each, once, before any return.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);
            await EvictRestoredChildrenAsync(accessCacheInvalidator, children, traceId);

            // What the response may claim about the creator's shares (task 133 verifier round 1). When the pre-call share
            // set could not be read, the only safe restore target is "no share" — which also removes any explicit share
            // the creator held BEFORE the call. That is a narrowing, never a widening, but it is not "as it was", so it
            // is reported as such: sharesRestored false, creatorShareRemoved true.
            string sharesText;
            var sharesRestored = restored;
            var creatorShareRemoved = false;
            if (!wroteCreatorShare)
            {
                sharesText = "This call wrote no share for the creator, so the creator's shares are as they were before " +
                             "the call. ";
            }
            else if (!restored)
            {
                sharesText = "A share this call issued to the creator could not be confirmed removed; it shows under " +
                             "Manage Access. ";
            }
            else if (preCreatorMask is null)
            {
                sharesRestored = false;
                creatorShareRemoved = true;
                sharesText = "The record's shares could not be read before the call, so the creator's explicit share was " +
                             "removed entirely (read back) — including any share the creator held before this call. If " +
                             "the creator had one and still needs it, an administrator re-adds it through Manage Access. ";
            }
            else
            {
                sharesText = "The creator's share is as it was before the call (read back). ";
            }

            var undoneText =
                "The record could not be shared to its creator once it was owned by the Secure Record owner team, so " +
                "the move was undone: its owner is back to the owner it had before the call (read back). " + sharesText +
                unlinkedNote;

            if (!children.AllRestored)
            {
                return CreatorShareStep.Failed(CascadeChildrenNotRestored(
                    root, recordId, children, undoneText, sharesRestored, creatorShareRemoved, logger, traceId));
            }

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                undoneText + "The same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorShareFailed),
                ("ownershipRestored", true), ("sharesRestored", sharesRestored),
                ("creatorShareRemoved", creatorShareRemoved), ("childOwnersRestored", true)));
        }

        // The undo did not land, or could not be verified (ADR-003: an unverifiable compensation is a failure, never
        // "reverted"). The record may be owned by the memberless team with no confirmed creator share — the one state
        // that can leave nobody able to open it — so it is CRITICAL, and only an administrator can finish it.
        var undoUnverified = back.Outcome == OwnerMoveOutcome.Unverified;
        if (undoUnverified)
        {
            // Task 132 (C12): the move back may have re-owned the record (and cascaded) — evict (always safe). A move back
            // read back as NOT taken effect changed nothing: the record is still the team's, already evicted above.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);
        }

        logger.LogCritical(
            "[PROVISION] {RecordType} {RecordId}: the creator's share could not be proven AND the move back to " +
            "{OwnerKind} {OwnerId} {UndoState} (outcome {Outcome}). It may be owned by the memberless Secure Record " +
            "owner team without a confirmed creator share — possibly NOBODY can open it. {Recovery} TraceId={TraceId}",
            root.WireToken, recordId, preOwner.Kind, preOwner.Id,
            undoUnverified ? "could not be verified" : "did not take effect", back.Outcome,
            AdministratorRecoveryForLog(keepsOwnContainer), traceId);

        // What the creator can do now. A share-first grant that was PROVEN before the move has not been removed by this
        // call (compensation removes it only after a verified move back), so — unless the move itself dropped it (live
        // gate (b)) — the creator still opens the record; otherwise they may not, and may no longer pass the delegation gate (Write and Share).
        var creatorText = keepsOwnContainer && creatorShareProven
            ? "The creator's share was confirmed before the move and this call has not removed it, so unless the move " +
              "itself dropped it the creator can still open the record. "
            : "Its creator may not be able to open it and may no longer pass the Write and Share check this endpoint requires. ";

        // The recovery that works against the marker: a record that keeps its own container is never resumed (task 133
        // r1), so an administrator restores access directly; one with no container is resumed by their call.
        var recoveryText = keepsOwnContainer
            ? "While the team owns it, " + KeptContainerRecovery + " "
            : "While the team owns it, an administrator (who holds Write and Share on it) calls provisioning again: it resumes and " +
              "shares the record to the person who created it. ";

        // If an UNVERIFIED move back did land, it cascaded like the move out: every child now has the record's pre-call
        // owner, and those whose own owner differed are on the wrong one (owner round 10 item 4). They are named, with the
        // call that puts each back, but not restored here: whether the record moved is unknown, and putting a child on its
        // own owner while the team may still own the record would take it out of the team the move out put it in. A move
        // back that did NOT take effect (read back) left the record and its children with the team, as the move out did.
        var childrenAtRisk = undoUnverified ? cascadeSnapshot.NotOwnedBy(preOwner) : Array.Empty<CascadeChild>();
        if (childrenAtRisk.Count > 0)
        {
            logger.LogCritical(
                "[PROVISION] {RecordType} {RecordId}: if the unverified move back to {OwnerKind} {OwnerId} took effect, it " +
                "also gave {Count} related record(s) that owner, which is not their own. An administrator puts each back " +
                "BEFORE provisioning is called again: {Calls} TraceId={TraceId}",
                root.WireToken, recordId, preOwner.Kind, preOwner.Id, childrenAtRisk.Count,
                string.Join(" ; ", childrenAtRisk.Select(c => c.RestoreCall)), traceId);
        }

        var moveBackText = !undoUnverified
            ? string.Empty
            : childrenAtRisk.Count == 0
                ? "If the move back did take effect, the record is where it was before the call and its creator calls " +
                  "provisioning again."
                : "If the move back did take effect, the record is where it was before the call, and Dataverse moved " +
                  $"{childrenAtRisk.Count} related record(s) with it onto its previous owner, which is not their own: " +
                  DescribeChildren(childrenAtRisk) + ". An administrator puts each back on the owner named " +
                  "(childOwnersAtRisk names the call) and then its creator calls provisioning again.";

        var extensions = new List<(string Key, object? Value)>
        {
            (ReasonKey, ReasonCreatorShareFailedResumable),
            ("ownerTeamId", ownerTeamId), ("ownershipRestored", false), ("ownershipVerified", !undoUnverified),
            ("containerKept", keepsOwnContainer)
        };
        if (childrenAtRisk.Count > 0)
            extensions.Add(("childOwnersAtRisk", childrenAtRisk.Select(c => ChildPayload(c)).ToList()));

        return CreatorShareStep.Failed(Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            ("The record could not be shared to its creator once it was owned by the Secure Record owner team, and " +
             (undoUnverified
                 ? "the move back to its previous owner could not be verified: its owner could not be read back, so it may " +
                   "still be owned by that memberless team without a confirmed creator share. "
                 : "the move back to its previous owner did not take effect (read back). It is owned by that memberless " +
                   "team without a confirmed creator share. ") +
             unlinkedNote + creatorText + recoveryText + moveBackText).TrimEnd(),
            traceId, extensions.ToArray()));
    }

    /// <summary>
    /// Refusal before any write: the rows the owner move cascades to could not be snapshotted (task 133, owner round 10
    /// item 4), so a failure after the move could not put each back on its own owner.
    /// </summary>
    private static IResult CascadeChildrenUnreadable(
        SecureRecordRoot root, Guid recordId, CascadeSnapshotResult cascade, ILogger logger, string traceId)
    {
        var table = cascade.FailedTable!.LogicalName;
        var deterministic = cascade.Failure == CascadeReadFailure.Refused;

        logger.LogError(cascade.Fault,
            "[PROVISION] The {Table} rows an owner move of {RecordType} {RecordId} cascades to could not be read " +
            "completely ({State}). Refusing before any change. TraceId={TraceId}",
            table, root.WireToken, recordId, deterministic ? CascadeChildRefused : CascadeChildUnreadable, traceId);

        return Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"Moving the {root.DisplayLabel.ToLowerInvariant()} to the Secure Record owner team also moves the related " +
            $"{table} rows Dataverse re-owns with it, so provisioning records each one's own owner first, to put it back " +
            "if a later step fails. " + (deterministic
                ? $"Dataverse refused the read of those rows, or the service's permission to read them (or answered " +
                  "it incompletely), so provisioning stopped BEFORE changing anything: the record's ownership and shares " +
                  "are as they were. Calling again repeats this refusal: an administrator looks at the record's " +
                  $"{table} rows, and the service's Read privilege on that table, first."
                : "Those rows could not be read, so provisioning stopped BEFORE changing anything: the record's ownership " +
                  "and shares are as they were. The same caller may retry once Dataverse is reachable."),
            traceId, (ReasonKey, ReasonCascadeChildrenUnreadable), ("childTable", table),
            ("cascadeChildState", deterministic ? CascadeChildRefused : CascadeChildUnreadable));
    }

    /// <summary>
    /// The move was undone (read back) but cascaded rows are not all back on their own owners (task 133, owner round 10
    /// item 4): each is named in the response and in a CRITICAL log line, with the call that puts it back.
    /// </summary>
    private static IResult CascadeChildrenNotRestored(
        SecureRecordRoot root,
        Guid recordId,
        CascadeRestoreReport children,
        string undoneText,
        bool sharesRestored,
        bool creatorShareRemoved,
        ILogger logger,
        string traceId)
    {
        var failed = children.NotRestored;

        logger.LogCritical(
            "[PROVISION] {RecordType} {RecordId}: the move was undone, but {Count} related record(s) Dataverse moved with " +
            "it could not be put back on their own owners: {Children}. An administrator puts each back BEFORE provisioning " +
            "is called again (another run would record the wrong owner for them): {Calls} TraceId={TraceId}",
            root.WireToken, recordId, failed.Count,
            string.Join(" ; ", failed.Select(f => $"{f.Child.LogicalName} {f.Child.Id} ({f.Outcome})")),
            string.Join(" ; ", failed.Select(f => f.Child.RestoreCall)), traceId);

        return Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            undoneText +
            $"Dataverse moves related records together with it, and {failed.Count} of them could not be put back on their " +
            "own owners: " + DescribeChildren(failed.Select(f => f.Child)) + ". An administrator puts each back on the " +
            "owner named (childOwnersNotRestored names the call) before provisioning is called again: another run would " +
            "record the owner they have now as their own.",
            traceId, (ReasonKey, ReasonCascadeChildrenNotRestored),
            ("ownershipRestored", true), ("sharesRestored", sharesRestored),
            ("creatorShareRemoved", creatorShareRemoved), ("childOwnersRestored", false),
            ("childOwnersNotRestored", failed.Select(f => ChildPayload(f.Child, f)).ToList()));
    }

    /// <summary>"sharepointdocumentlocation {id} (own owner systemuser {id})", comma-separated.</summary>
    private static string DescribeChildren(IEnumerable<CascadeChild> children) =>
        string.Join(", ", children.Select(c =>
            $"{c.LogicalName} {c.Id} (own owner {c.Owner.Kind.ToEntitySet().TrimEnd('s')} {c.Owner.Id})"));

    /// <summary>One cascaded child as a ProblemDetails extension entry: what it is, its own owner, the call that restores it.</summary>
    private static object ChildPayload(CascadeChild child, CascadeChildRestore? restore = null) => new
    {
        table = child.LogicalName,
        id = child.Id,
        ownerType = child.Owner.Kind.ToEntitySet().TrimEnd('s'),
        ownerId = child.Owner.Id,
        outcome = restore?.Outcome.ToString(),
        nextCall = child.RestoreCall
    };

    // The cascadeChildState values (task 133 c1): the client tells a retry from an administrator's job on them.
    private const string CascadeChildUnreadable = "unreadable";
    private const string CascadeChildRefused = "refused";

    // The containerOwnershipState values (owner round 14 item 3, task 133 c1-r4): the same split for the check of a
    // container already recorded on the record.
    private const string ContainerOwnershipStateUnreadable = "unreadable";
    private const string ContainerOwnershipStateRefused = "refused";

    /// <summary>
    /// The recovery for a record that KEEPS its own container when a failure after its move may have left it with no
    /// confirmed creator share (task 133 r1): it reads as provisioned to every later call, so the recovery is a direct
    /// share, not a provisioning call. Starts lower-case: it completes a sentence.
    /// </summary>
    private const string KeptContainerRecovery =
        "a later provisioning call answers already_provisioned and does not resume it, because the record keeps its own " +
        "SPE container; if the creator cannot open it, an administrator shares it to them directly — through Manage " +
        "Access, or Share in the model-driven app.";

    /// <summary>The administrator's recovery as the CRITICAL log line states it (task 133 r1).</summary>
    private static string AdministratorRecoveryForLog(bool keepsOwnContainer) => keepsOwnContainer
        ? "It keeps its own container, so a provisioning call answers already_provisioned: an administrator must share " +
          "it to its creator through Manage Access (or Share in the model-driven app)."
        : "An administrator must call provisioning again to resume.";

    /// <summary>
    /// On a record NOT yet flagged secure (owner round 10 item 10, 2026-10-03; task 150 note §11.6) — the forward path, and
    /// a resume (<see cref="RefuseUnflaggedResumeUnlessCreatorAsync"/>, verifier c1 item 4): refuses — read-only, before
    /// any write — unless the caller is the person who created the record. Returns the refusal to send, or <c>null</c>
    /// when the caller is that person.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> After the <c>sprk_issecure</c> lock this call is the one way to mark a record secure, and the route
    /// admits any Write holder — including a colleague who reaches someone else's ordinary record through business-unit
    /// depth. Securing a record that already holds content, filed by other people, is task 148's transition (it carries the
    /// children and files); this call secures what its creator just made — the only use the two wizards have of it.</para>
    ///
    /// <para><b>The creator</b> (the owner's rule: "<c>createdby</c> when human, else <c>sprk_createdbyperson</c>", the same
    /// order the resume uses): the caller is admitted when they are <c>createdby</c> (they made the create — a delegated
    /// caller is a person, so no read is needed); otherwise <c>createdby</c> is read, and when it is a PERSON (enabled or
    /// not) that person is the creator and the caller is refused; when it is an application user (an app-only create, e.g.
    /// Office quick-create — enabled or not: an application user is never a person, verifier c1 item 7) or absent, the
    /// BFF-stamped <c>sprk_createdbyperson</c> (field-secured, written only by the BFF — task 133) names the creator.</para>
    ///
    /// <para><b>Fail closed.</b> A read that fails refuses with <see cref="ReasonRecordCreatorUnverifiable"/> (500, the same
    /// caller may retry) — never folded into "the caller is the creator". A <c>sprk_createdbyperson</c> column this
    /// environment lacks (400) admits nobody and is UNVERIFIABLE, not "not the creator" (round 17 item 1, aligned with
    /// task 146's F3 helper): 403 <see cref="ReasonRecordCreatorUnverifiable"/> with <c>creatorState: column-missing</c>
    /// (no retry; an administrator applies the schema).</para>
    /// </remarks>
    private static async Task<IResult?> RefuseUnlessRecordCreatorAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid callerId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var createdBy = row._createdby_value is { } cb && cb != Guid.Empty ? cb : (Guid?)null;
        if (createdBy == callerId)
            return null;

        string createdByState;
        if (createdBy is { } createdById)
        {
            string? state;
            try
            {
                state = await UnusablePersonStateAsync(dataverseClient, createdById, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "[PROVISION] The creator (createdby {CreatorId}) of unflagged {RecordType} {RecordId} could not be read, " +
                    "so whether caller {CallerId} created it is unknown. Refusing before any change. TraceId={TraceId}",
                    createdById, root.WireToken, recordId, callerId, traceId);
                return RecordCreatorUnverifiable(root, traceId);
            }

            // A PERSON created it — enabled or not, that person is the creator, and it is not the caller.
            if (state is null or UnusableDisabled)
                return NotRecordCreator(root, recordId, callerId, "createdby", logger, traceId);

            createdByState = state;
        }
        else
        {
            createdByState = UnusableAbsent;
        }

        // createdby is an application user (an app-only create) or absent: the person the BFF stamped decides.
        Guid? person;
        try
        {
            person = await ReadCreatorPersonAsync(dataverseClient, root, recordId, ct);
        }
        catch (Exception ex) when (IsColumnMissing(ex))
        {
            // Round 17 item 1 (2026-10-03): a missing creator-person column is UNVERIFIABLE, not "not the creator" —
            // the same answer task 146's F3 helper gives for the same environment fact (CreatorColumnAbsent → 403
            // permission_unverifiable). Nobody is known to be the creator, and nobody is known not to be.
            logger.LogWarning(
                "[PROVISION] {Column} is not in this environment (400), so who created unflagged {RecordType} " +
                "{RecordId} (createdby is {CreatedByState}) cannot be checked. Refusing before any change: an " +
                "administrator applies the schema (scripts/Set-RecordCreatorPersonSchema.ps1). TraceId={TraceId}",
                RecordCreatorPerson.Column, root.WireToken, recordId, createdByState, traceId);
            return RecordCreatorColumnMissing(root, traceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[PROVISION] The person recorded as creating unflagged {RecordType} {RecordId} ({Column}) could not be " +
                "read. Refusing before any change. TraceId={TraceId}",
                root.WireToken, recordId, RecordCreatorPerson.Column, traceId);
            return RecordCreatorUnverifiable(root, traceId);
        }

        return person == callerId
            ? null
            : NotRecordCreator(root, recordId, callerId, RecordCreatorPerson.Column, logger, traceId);
    }

    /// <summary>
    /// Make Secure (task 150, round 33 item 1): the person who created the record — shared to alongside the caller, so the
    /// confirmation copy's "the person who created this record … will keep access" holds. Read-only, before any write.
    /// </summary>
    /// <remarks>
    /// <para><b>Which person</b> — the creator rule's columns: <c>createdby</c> when it is a PERSON; when it is an
    /// application user (an app-only create) or absent, the BFF-stamped <c>sprk_createdbyperson</c>. A person who cannot
    /// use the record (disabled, an application user, none recorded) is not shared to: nobody is left to keep access
    /// through that clause.</para>
    /// <para><b>Fail closed</b> (ADR-003). A read that fails refuses <see cref="ReasonRecordCreatorUnverifiable"/> (500,
    /// the same caller may retry); a <c>sprk_createdbyperson</c> column this environment lacks, needed because
    /// <c>createdby</c> names no person, refuses as the creator rule does (403, <c>creatorState: column-missing</c>) —
    /// never secured with its creator possibly locked out. The same codes, and so the same client copy, as the creator
    /// rule.</para>
    /// </remarks>
    private static async Task<(Guid? CreatorId, IResult? Error)> ResolveMakeSecureCreatorAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var createdBy = row._createdby_value is { } cb && cb != Guid.Empty ? cb : (Guid?)null;
        if (createdBy is { } createdById)
        {
            string? state;
            try
            {
                state = await UnusablePersonStateAsync(dataverseClient, createdById, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "[PROVISION] Make Secure: the creator (createdby {CreatorId}) of {RecordType} {RecordId} could not be " +
                    "read. Refusing before any change. TraceId={TraceId}", createdById, root.WireToken, recordId, traceId);
                return (null, RecordCreatorUnverifiable(root, traceId));
            }

            if (state is null)
                return (createdById, null);         // a usable person created it

            if (state == UnusableDisabled)
                return (null, null);                // a person created it and cannot use it now: no share
        }

        // createdby is an application user (an app-only create) or absent: the person the BFF stamped.
        Guid? person;
        try
        {
            person = await ReadCreatorPersonAsync(dataverseClient, root, recordId, ct);
        }
        catch (Exception ex) when (IsColumnMissing(ex))
        {
            logger.LogWarning(
                "[PROVISION] Make Secure: {Column} is not in this environment (400), so who created {RecordType} " +
                "{RecordId} cannot be checked. Refusing before any change. TraceId={TraceId}",
                RecordCreatorPerson.Column, root.WireToken, recordId, traceId);
            return (null, RecordCreatorColumnMissing(root, traceId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[PROVISION] Make Secure: the person recorded as creating {RecordType} {RecordId} ({Column}) could not be " +
                "read. Refusing before any change. TraceId={TraceId}",
                root.WireToken, recordId, RecordCreatorPerson.Column, traceId);
            return (null, RecordCreatorUnverifiable(root, traceId));
        }

        if (person is not { } personId)
            return (null, null);                    // nobody recorded

        string? personState;
        try
        {
            personState = await UnusablePersonStateAsync(dataverseClient, personId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[PROVISION] Make Secure: the person recorded as creating {RecordType} {RecordId} ({PersonId}) could not " +
                "be read. Refusing before any change. TraceId={TraceId}", root.WireToken, recordId, personId, traceId);
            return (null, RecordCreatorUnverifiable(root, traceId));
        }

        return personState is null ? (personId, null) : (null, null);
    }

    /// <summary>
    /// RESUME through Make Secure (task 150, round 40 items 1 and 2): a record owned by the Secure Record owner team with no
    /// container recorded — an earlier run stopped after the owner move — finished with the forward Make Secure path's
    /// rules. Every refusal comes before any write. Returns the caller (the share this run proves) and the record's
    /// creator (shared to beside them in the colleague step), or the refusal to send.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the forward path's rules.</b> Round 40 item 2: a non-creator who runs Make Secure keeps access on BOTH
    /// paths. And round 40 item 1: a Make Secure that fails after its first write can always be finished — by the same
    /// caller, from the same command. The resume's own rules (F8: share to the record's creator only; refuse when no usable
    /// creator, or a walled one) would answer a retry with 409 for exactly the records the forward path secured anyway
    /// (a disabled creator; a creator on the No Access list, named in <c>skippedPrincipals</c>), so the retry could never
    /// finish them. Here the caller's share is proven, as on the forward path, so a record with no usable creator still has
    /// a reader (S5); the creator is shared to, skipped with the per-person reason, or not shared to (unusable) — the
    /// forward rules exactly.</para>
    /// <para><b>Order</b> (ADR-003, nothing written before every check): the caller by WhoAmI (unresolved: 403
    /// <see cref="ReasonCreatorUnresolved"/>) and, in the same step, their effective rights (round 46 item 1:
    /// <see cref="ReadMakeSecureCallerFloorAsync"/>; unreadable: 500 <see cref="ReasonCallerRightsUnverifiable"/>); the
    /// caller against the No Access list (403 <see cref="ReasonCreatorNoAccess"/> / 500
    /// <see cref="ReasonCreatorNoAccessUnverifiable"/>); the record's creator (<see cref="ResolveMakeSecureCreatorAsync"/>:
    /// an unreadable creator refuses); then the flag (the first write, skipped when already set); then the caller's share —
    /// <see cref="MakeSecureCallerMask"/> of what they held (share now, and effective rights), read back. A share that
    /// cannot be proven answers 500
    /// <see cref="ReasonCreatorShareFailed"/> (<c>resumed: true</c>): nothing about ownership changed, and the same caller
    /// may call again.</para>
    /// <para><b>F8 still holds without the transition.</b> A resume the wizards or an administrator's API call make (no
    /// transition) shares to the record's creator only — an administrator who finishes a record that way is not added to
    /// its explicit access list. One who uses the form's Make Secure command is shared to like any caller of it.</para>
    /// </remarks>
    private static async Task<(Guid CallerId, Guid? RecordCreator, IResult? Error)> ResumeMakeSecureAsync(
        RestrictedCreatorRule creatorRule,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        SecureShareNoAccessGuard noAccessGuard,
        HttpContext httpContext,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        bool alreadyFlagged,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        logger.LogWarning(
            "[PROVISION] Make Secure on {RecordType} {RecordId}: owned by the Secure Record owner team {TeamId} with no " +
            "container recorded — an earlier run stopped after the owner move. Finishing it for the caller (round 40). " +
            "TraceId={TraceId}", root.WireToken, recordId, ownerTeamId, traceId);

        // The caller — from their own token (WhoAmI), never the body — as on the forward path.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var resolved = await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
        if (resolved is not { } callerId || callerId == Guid.Empty)
            return (Guid.Empty, null, CallerUnresolved(root, recordId, logger, traceId));

        // Their effective rights before the call, in the same step (round 46 item 1) — the floor their share is kept at.
        var floor = await ReadMakeSecureCallerFloorAsync(
            callerAccessProbe, callerToken, root, recordId, callerId, logger, traceId, ct);
        if (floor.Error != null)
            return (Guid.Empty, null, floor.Error);

        // A caller the No Access list walls off is refused before any change (owner N6), as on the forward path — the
        // record's own list AND every secure record it is filed under (task 158 r1, owner round 31 item 1; batch-4
        // integration: the resume asks the same ONE check the forward path does).
        var wall = await CheckCreatorWallsAsync(noAccessGuard, root, recordId, callerId, ct);
        if (wall.RefusesShare)
            return (Guid.Empty, null, CallerWallRefusal(root, recordId, callerId, wall, logger, traceId));

        // Who created the record — read before any write; a read that fails refuses (never secured with them locked out).
        var creatorRead = await ResolveMakeSecureCreatorAsync(dataverseClient, root, recordId, row, logger, traceId, ct);
        if (creatorRead.Error != null)
            return (Guid.Empty, null, creatorRead.Error);

        // The flag — the first write here too (task 150); skipped when the earlier run already set it.
        var flag = await EnsureSecureFlagAsync(dataverseClient, root, recordId, alreadyFlagged, logger, traceId, ct);
        if (flag != null)
            return (Guid.Empty, null, flag);

        // The caller's share at the creator's level, never lower than they held (their share now, and the effective rights
        // read above) — proven by a read (the forward path's proof; the record is already the team's, so there is no move to
        // undo).
        var ensured = await EnsureCreatorShareAsync(
            recordShare, root, recordId, callerId, logger, ct, held => MakeSecureCallerMask(held | floor.HeldMask), creatorRule);
        if (!ensured.Proven)
        {
            logger.LogError(
                "[PROVISION] Make Secure resuming {RecordType} {RecordId}: the caller's share ({CallerId}) could not be " +
                "proven. Nothing about ownership changed. TraceId={TraceId}", root.WireToken, recordId, callerId, traceId);

            return (Guid.Empty, null, Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"This call was finishing securing {root.DisplayLabel.ToLowerInvariant()} {recordId}, which is owned by the " +
                "Secure Record owner team with no container recorded, and could not confirm your share on it. This call did " +
                "not change the record's ownership. The same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorShareFailed), ("resumed", true), ("ownerTeamId", ownerTeamId)));
        }

        return (callerId, creatorRead.CreatorId, null);
    }

    /// <summary>
    /// The caller's Dataverse identity could not be established (WhoAmI): refused before any change — on the forward path
    /// and on a Make Secure resume, whose proven share is the caller's.
    /// </summary>
    private static IResult CallerUnresolved(SecureRecordRoot root, Guid recordId, ILogger logger, string traceId)
    {
        logger.LogError(
            "[PROVISION] Could not resolve the calling user's systemuserid for {RecordType} {RecordId}. Refusing " +
            "before any change: the record could not be shared back to its creator. TraceId={TraceId}",
            root.WireToken, recordId, traceId);

        return Problem(
            StatusCodes.Status403Forbidden, "Forbidden",
            "The calling user's Dataverse identity could not be established, so the record could not be shared " +
            "back to them. Provisioning stopped before changing anything: the record's ownership and shares are " +
            "as they were, and the same caller may retry.",
            traceId, (ReasonKey, ReasonCreatorUnresolved));
    }

    /// <summary>
    /// Owner N6 (task 143): the caller — the person this run shares to and proves — is on the record's No Access list,
    /// or that could not be checked. Refused before any change; the message names no entry and no reason.
    /// </summary>
    private static IResult CallerWallRefusal(
        SecureRecordRoot root, Guid recordId, Guid callerId, CreatorWallDecision wall, ILogger logger, string traceId,
        bool recordedCreator = false)
    {
        var walled = wall.Outcome == SecureShareWallOutcome.Walled;
        logger.LogWarning(
            "[PROVISION] Refused to provision {RecordType} {RecordId}: its creator {CreatorId} is {State} the No Access " +
            "list of {Where} ({Detail}). Nothing was changed. TraceId={TraceId}",
            root.WireToken, recordId, callerId, walled ? "on" : "not provably off", wall.Where,
            walled ? string.Join(",", wall.EntryIds) : wall.Fault, traceId);

        // Task 158 r1 (owner round 31 item 1): the list may be a secure parent's; on the inherited provisioning (no caller)
        // the person is the record's creator, never "you".
        var record = root.DisplayLabel.ToLowerInvariant();
        var who = recordedCreator ? $"The person who created this {record} is" : "You are";
        var whose = recordedCreator ? $"the person who created this {record} is" : "you are";
        var list = wall.ParentTable is { } parentTable
            ? $"the No Access list of the secure {SecureRootInheritance.WireTokenFor(parentTable)} it is filed under"
            : wall.FilingUnreadable ? "the No Access list of a secure record it is filed under" : $"the No Access list for this {record}";
        return walled
            ? Problem(StatusCodes.Status403Forbidden, "Forbidden",
                $"{who} on {list} — directly, through an organization, or through an organization it references — so " +
                $"it cannot be made a secure record shared to {(recordedCreator ? "them" : "you")}. Nothing was changed.",
                traceId, (ReasonKey, ReasonCreatorNoAccess))
            : Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Whether {whose} on {list} could not be checked, so provisioning stopped before changing anything. " +
                "Try again.",
                traceId, (ReasonKey, ReasonCreatorNoAccessUnverifiable));
    }

    /// <summary>
    /// Make Secure (task 150, round 46 item 1): the caller's EFFECTIVE rights on the record before any write — Dataverse's
    /// own answer, asked as the caller (<see cref="CallerRecordAccessProbe.GetCallerRightsAsync"/>, the question owner F3
    /// asks) — as the share mask their Make Secure share may not fall below (<see cref="MakeSecureHeldMask"/>). Read in the
    /// same pre-write step as WhoAmI, on the forward path and on a Make Secure resume.
    /// </summary>
    /// <remarks>
    /// <para><b>Fail closed</b> (ADR-003). A probe that throws, or an answer without Write — the route's gate admitted
    /// Write on this record moments ago, so an answer lacking it is the probe's "could not answer" (it answers
    /// <see cref="AccessRights.None"/> for that, deliberately indistinguishable from "no rights") or a change mid-call —
    /// refuses before any write with <see cref="ReasonCallerRightsUnverifiable"/> (500; the same caller may retry). Never a
    /// floor of "nothing": that would narrow a Full Access holder and take away their F3 right to remove the designation.</para>
    /// <para><b>Why its own code</b> (round 53 item 1). Codes are namespaced by endpoint (round 26 item 1): owner F3's
    /// <see cref="SecureDesignationRemoval.PermissionUnverifiableReasonCode"/> names the same unreadable fact on the
    /// unsecure endpoint, and every other provisioning "unverifiable" code names a different fact (the No Access list, the
    /// record's creator). The wizards' create-then-secure path never reads the floor, so it never answers this code.</para>
    /// </remarks>
    private static async Task<(int HeldMask, IResult? Error)> ReadMakeSecureCallerFloorAsync(
        CallerRecordAccessProbe callerAccessProbe,
        string? callerToken,
        SecureRecordRoot root,
        Guid recordId,
        Guid callerId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        AccessRights effective;
        try
        {
            effective = await callerAccessProbe.GetCallerRightsAsync(callerToken, root.EntitySet, recordId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[PROVISION] Make Secure: the effective rights of caller {CallerId} on {RecordType} {RecordId} could not be " +
                "read, so the share they keep could not be set. Refusing before any change. TraceId={TraceId}",
                callerId, root.WireToken, recordId, traceId);
            return (0, CallerAccessUnverifiable(root, traceId));
        }

        if ((effective & AccessRights.Write) != AccessRights.Write)
        {
            logger.LogWarning(
                "[PROVISION] Make Secure: the effective rights of caller {CallerId} on {RecordType} {RecordId} read as " +
                "{Rights}, without the Write the route admitted them on — an unanswered probe or a change mid-call. Refusing " +
                "before any change. TraceId={TraceId}", callerId, root.WireToken, recordId, effective, traceId);
            return (0, CallerAccessUnverifiable(root, traceId));
        }

        var held = MakeSecureHeldMask(effective);
        logger.LogInformation(
            "[PROVISION] Make Secure: caller {CallerId} holds {Rights} on {RecordType} {RecordId}; their share is kept at " +
            "no less than mask {HeldMask} (floored at the creator's level). TraceId={TraceId}",
            callerId, effective, root.WireToken, recordId, held, traceId);
        return (held, null);
    }

    /// <summary>
    /// The caller's effective rights could not be read (round 46 item 1) — refused before any change with
    /// <see cref="ReasonCallerRightsUnverifiable"/> (round 53 item 1; <see cref="ReadMakeSecureCallerFloorAsync"/>). The
    /// detail is the copy round 53 ratified; the Access ribbon and the wizard's client carry the same words.
    /// </summary>
    private static IResult CallerAccessUnverifiable(SecureRecordRoot root, string traceId) =>
        Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"Which access you hold on this {root.DisplayLabel.ToLowerInvariant()} could not be read, so securing it could " +
            "not make sure you keep that access. Nothing was changed; you may try again.",
            traceId, (ReasonKey, ReasonCallerRightsUnverifiable));

    // F6 row 9 — owner round 13 item 10 (2026-10-03): option B, verbatim (notes/task-150-issecure-lock.md §6).
    private static IResult NotRecordCreator(
        SecureRecordRoot root, Guid recordId, Guid callerId, string decidingColumn, ILogger logger, string traceId)
    {
        logger.LogWarning(
            "[PROVISION] Caller {CallerId} did not create unflagged {RecordType} {RecordId} (decided by {Column}). Refusing " +
            "before any change: an ordinary record is secured through this call only for its creator (owner round 10 " +
            "item 10). TraceId={TraceId}",
            callerId, root.WireToken, recordId, decidingColumn, traceId);

        return Problem(StatusCodes.Status403Forbidden, "Forbidden",
            $"Only the person who created this {root.DisplayLabel.ToLowerInvariant()} can secure it this way, and you did " +
            "not create it. Nothing was changed.",
            traceId, (ReasonKey, ReasonNotRecordCreator), ("creatorColumn", decidingColumn));
    }

    // F6 row 10 — owner round 13 item 10 (2026-10-03): option B, verbatim (notes/task-150-issecure-lock.md §6).
    private static IResult RecordCreatorUnverifiable(SecureRecordRoot root, string traceId) =>
        Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"Who created this {root.DisplayLabel.ToLowerInvariant()} could not be looked up, so whether you may secure it " +
            "could not be checked. Nothing was changed; you may try again.",
            traceId, (ReasonKey, ReasonRecordCreatorUnverifiable));

    /// <summary>
    /// <c>sprk_createdbyperson</c> is not in this environment, and <c>createdby</c> does not name a person: who created
    /// the record cannot be checked (round 17 item 1). The same reason code as a failed read
    /// (<see cref="ReasonRecordCreatorUnverifiable"/>), but 403 — a deterministic environment fact, not a fault, as
    /// task 146's F3 helper answers it — with <c>creatorState: column-missing</c> (the extension the resume refusal
    /// already uses for the same fact), so the client offers no retry and names the administrator.
    /// </summary>
    private static IResult RecordCreatorColumnMissing(SecureRecordRoot root, string traceId) =>
        Problem(StatusCodes.Status403Forbidden, "Forbidden",
            $"Who created this {root.DisplayLabel.ToLowerInvariant()} is not recorded in this environment, so whether " +
            "you may secure it could not be checked. Nothing was changed; an administrator needs to finish setting " +
            "up the environment.",
            traceId, (ReasonKey, ReasonRecordCreatorUnverifiable), ("creatorState", UnusableColumnMissing),
            ("creatorColumn", RecordCreatorPerson.Column));

    /// <summary>
    /// RESUME of a record NOT flagged secure (owned by the Secure Record owner team, no container recorded): the same
    /// creator rule as the forward path (owner round 10 item 10 — "may secure an UNFLAGGED record only for its creator";
    /// task 150 verifier c1 item 4). Read-only, before any write. Returns the refusal to send, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The caller is identified by WhoAmI, as on the forward path; an unresolved caller is refused
    /// <see cref="ReasonCreatorUnresolved"/> (never read as "the creator"). Then
    /// <see cref="RefuseUnlessRecordCreatorAsync"/> decides, with the same columns and the same fail-closed reads. A
    /// System Administrator who is not the creator and needs to finish such a row sets the flag first (F4: the platform
    /// lets that role write it) — the row is then on the delegation gate (Write and Share) like every documented resume.
    /// </remarks>
    private static async Task<IResult?> RefuseUnflaggedResumeUnlessCreatorAsync(
        DataverseWebApiClient dataverseClient,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var caller = await callerAccessProbe.GetCallerSystemUserIdAsync(
            TokenHelper.ExtractBearerTokenOrNull(httpContext), ct);

        if (caller is not { } callerId || callerId == Guid.Empty)
        {
            logger.LogWarning(
                "[PROVISION] Resume of unflagged {RecordType} {RecordId} refused: the caller's Dataverse identity could not " +
                "be established, so it could not be shown to be the record's creator. Nothing was changed. TraceId={TraceId}",
                root.WireToken, recordId, traceId);

            // F6 row 11 — owner round 13 item 10 (2026-10-03): option B, verbatim (notes/task-150-issecure-lock.md §6).
            return Problem(
                StatusCodes.Status403Forbidden, "Forbidden",
                "Your account could not be confirmed, so whether you created this " +
                $"{root.DisplayLabel.ToLowerInvariant()} could not be checked. Nothing was changed; you may try again.",
                traceId, (ReasonKey, ReasonCreatorUnresolved), ("ownerTeamId", ownerTeamId));
        }

        return await RefuseUnlessRecordCreatorAsync(dataverseClient, root, recordId, row, callerId, logger, traceId, ct);
    }

    /// <summary>
    /// On RESUME, refuses a request that names colleagues unless its caller IS the record's creator — before any write
    /// (task 133 verifier round 1).
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> On the forward path the caller becomes the creator and holds the creator's <c>ShareAccess</c>,
    /// so the colleagues they name are theirs to add. A resume is different: its caller is often an administrator, or
    /// any Write holder — including a Collaborate colleague after a container failure. Honouring their
    /// <c>sharePrincipalIds</c> would widen the record's explicit access list through the app identity as a side effect
    /// of recovery (owner decision F8 / C4), skipping the eligibility and grantor checks Manage Access applies — and a
    /// caller could name themselves. So only the creator may name colleagues while finishing a run; anyone else adds
    /// people through Manage Access.</para>
    ///
    /// <para><b>Refused, not ignored.</b> Silently dropping the list would answer 200 with
    /// <c>additionalPrincipalsShared: 0</c> to a caller who asked for shares — a success that hides what did not
    /// happen. The refusal names the reason and changes nothing; the same call without the list completes the
    /// resume. The record's creator named in the list is not a colleague (they receive the creator share anyway).</para>
    ///
    /// <para><b>An unresolvable caller is not "not the creator"</b> (task 133 verifier round 3). When WhoAmI cannot
    /// establish who is calling, the refusal is <c>creator_unresolved</c> and says exactly that — it does not claim the
    /// caller is someone other than the creator, which nothing showed.</para>
    /// </remarks>
    private static async Task<IResult?> RefuseResumeColleaguesUnlessCreatorAsync(
        ProvisionProjectRequest request,
        ProvisioningCreator creator,
        SecureRecordRoot root,
        Guid recordId,
        Guid createdBy,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var named = (request.SharePrincipalIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty && id != createdBy)
            .Distinct()
            .ToList();

        if (named.Count == 0)
            return null;

        // Task 158: an inherited provisioning has no caller (and names nobody) — an unknown caller, refused below.
        var caller = await creator.CallerSystemUserIdAsync(ct);

        if (caller is not { } callerId || callerId == Guid.Empty)
        {
            logger.LogWarning(
                "[PROVISION] Resume of {RecordType} {RecordId} refused: the request names {Count} principal(s) to share " +
                "with, and the caller's Dataverse identity could not be established, so it could not be shown to be " +
                "the record's creator ({CreatorId}). Nothing was changed. TraceId={TraceId}",
                root.WireToken, recordId, named.Count, createdBy, traceId);

            return Problem(
                StatusCodes.Status403Forbidden, "Forbidden",
                $"{root.DisplayLabel} {recordId} is owned by the Secure Record owner team with no container recorded, so " +
                "this call would finish securing it. The request names people to share it with, which only the person " +
                "who created the record may do at this stage — and the calling user's Dataverse identity could not be " +
                "established, so it could not be checked. Nothing was changed. Calling again without sharePrincipalIds " +
                "finishes securing the record; people are added through Manage Access.",
                traceId, (ReasonKey, ReasonCreatorUnresolved), ("ownerTeamId", ownerTeamId));
        }

        if (callerId == createdBy)
            return null;

        logger.LogWarning(
            "[PROVISION] Resume of {RecordType} {RecordId} refused: the request names {Count} principal(s) to share " +
            "with, and its caller ({CallerId}) is not the record's creator ({CreatorId}). Nothing was " +
            "changed. TraceId={TraceId}",
            root.WireToken, recordId, named.Count, callerId, createdBy, traceId);

        return Problem(
            StatusCodes.Status403Forbidden, "Forbidden",
            $"{root.DisplayLabel} {recordId} is owned by the Secure Record owner team with no container recorded, so " +
            "this call would finish securing it. The request names people to share it with, but only the person who " +
            "created the record may do that while finishing it; anyone else adds people through Manage Access, which " +
            "applies its own checks. Nothing was changed. Calling again without sharePrincipalIds finishes securing " +
            "the record.",
            traceId, (ReasonKey, ReasonResumeColleaguesNotPermitted), ("ownerTeamId", ownerTeamId));
    }

    /// <summary>
    /// RESUME (task 133): the record is owned by the team with no container recorded, so an earlier run stopped after
    /// the move. Decides — read-only, before any write — the ONE person the resume shares to: <c>createdby</c> when that
    /// is a usable person; otherwise the server-stamped <c>sprk_createdbyperson</c> when THAT is a usable person;
    /// otherwise nobody (refused).
    /// </summary>
    /// <remarks>
    /// <para><b>Never the caller as a substitute</b> (owner decision F8). A resume is often run by an administrator,
    /// who reaches the record through their role; adding them to its explicit access list would be a C4 decision made
    /// as a recovery side effect. When the caller IS the creator, it is the same share.</para>
    ///
    /// <para><b>Which person</b> (owner round 7 item 2, option (a), 2026-10-02: "shares to createdby when it is a human,
    /// else to this column; still refuses when neither is a usable human"). <c>createdby</c> is the identity that sent
    /// the create. For a row the BFF created APP-ONLY (Office quick-create; before task 166 deleted it, <c>POST /api/v1/work-assignments</c>) that
    /// is the BFF application user, and the person who asked for it is the BFF-stamped <c>sprk_createdbyperson</c>
    /// (<see cref="RecordCreatorPerson"/> — field-secured, writable only by the BFF). So a usable <c>createdby</c>
    /// wins; when it is absent, disabled or an application user the column is read, in its OWN query, so a BFF deployed
    /// before the schema still provisions forward and only a resume that needs the column reports it — as
    /// <c>column-missing</c> when Dataverse answers 400 (the column is not there: an administrator applies the schema;
    /// calling again cannot help), as <c>refused</c> for a 401/403 (owner round 14 item 3), as <c>unreadable</c> for any
    /// other failure (task 133 r1).</para>
    ///
    /// <para><b>An unreadable <c>createdby</c> stops the decision</b> rather than falling through to the column: it may
    /// well be a usable person, and the rule names it first. A failed read is a 500, never folded into "unusable for good"
    /// (ADR-003): <c>refused</c> when Dataverse refused it (<see cref="AssignCascadeChildOwners.IsRefusedRead"/> — a
    /// 400/401/403, deterministic: an administrator acts first; owner round 14 item 3), otherwise <c>unreadable</c>
    /// (transient — the same caller may call again).</para>
    ///
    /// <para><b>A person must be usable</b>: present, enabled (an <c>isdisabled</c> read as anything but false is
    /// disabled), not an application user. When neither column names one, the resume is REFUSED —
    /// <c>resume_creator_unavailable</c>, the deciding state and the column it describes named, zero grants, zero
    /// containers — whoever else may hold a share (the closed acceptance criterion). Every recovery the detail names
    /// works against this code (task 133 verifier round 2): call again once the read works; re-enable a disabled person;
    /// or ASSIGN the record to the person who should hold it — it then leaves the owner team, so the next call by that
    /// person is a forward run that shares it to them.</para>
    ///
    /// <para>No Access (task 143): the caller checks the person this returns against the record's No Access list before
    /// the share, and refuses (<see cref="ReasonResumeCreatorNoAccess"/>) with nothing written.</para>
    /// </remarks>
    private static async Task<CreatorShareStep> ResolveResumeCreatorAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct,
        bool resuming = true)
    {
        // Task 158: the same decision names the person an INHERITED forward provisioning secures the record for
        // (resuming: false) — there is no caller to share to.
        if (resuming)
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} is owned by the Secure Record owner team {TeamId} with no container " +
                "recorded: an earlier run stopped after the owner move. RESUMING. TraceId={TraceId}",
                root.WireToken, recordId, ownerTeamId, traceId);
        }
        else
        {
            logger.LogInformation(
                "[PROVISION] {RecordType} {RecordId} is filed under a secure record and is secured with no caller (task " +
                "158): it is shared to the person who created it. TraceId={TraceId}", root.WireToken, recordId, traceId);
        }

        // ── 1. createdby ──
        var createdBy = row._createdby_value is { } cb && cb != Guid.Empty ? cb : (Guid?)null;
        string createdByState;
        if (createdBy is not { } createdById)
        {
            createdByState = UnusableAbsent;
        }
        else
        {
            string? state;
            try
            {
                state = await UnusablePersonStateAsync(dataverseClient, createdById, ct);
            }
            catch (Exception ex)
            {
                var readState = ReadFailureState(ex);
                logger.LogError(ex,
                    "[PROVISION] The creator (createdby {CreatorId}) of {RecordType} {RecordId} could not be read " +
                    "({State}). TraceId={TraceId}", createdById, root.WireToken, recordId, readState, traceId);
                return ResumeCreatorRefused(root, recordId, ownerTeamId, logger, traceId,
                    decidingState: readState, decidingColumn: CreatedByColumn,
                    createdByState: readState, personState: null);
            }

            if (state is null)
                return CreatorShareStep.Ok(createdById);

            createdByState = state;
        }

        // ── 2. createdby is not a usable person: the person the BFF stamped (owner round 7 item 2) ──
        Guid? person;
        try
        {
            person = await ReadCreatorPersonAsync(dataverseClient, root, recordId, ct);
        }
        catch (Exception ex)
        {
            // A 400 to this one-column read is how Dataverse answers a column the environment lacks (the schema has not
            // run there): deterministic, so it is NOT the transient "unreadable" a caller may retry (task 133 r1,
            // verifier round 4 finding 10 — the wizard's retry would fail until the schema is applied). A 401/403 is
            // deterministic too: "refused" (owner round 14 item 3).
            var columnState = IsColumnMissing(ex) ? UnusableColumnMissing : ReadFailureState(ex);
            logger.LogError(ex,
                "[PROVISION] The creator person ({Column}) of {RecordType} {RecordId} could not be read ({State}); " +
                "createdby is {CreatedByState}. TraceId={TraceId}", RecordCreatorPerson.Column, root.WireToken, recordId,
                columnState, createdByState, traceId);
            return ResumeCreatorRefused(root, recordId, ownerTeamId, logger, traceId,
                decidingState: columnState, decidingColumn: RecordCreatorPerson.Column,
                createdByState: createdByState, personState: columnState);
        }

        if (person is not { } personId)
        {
            // Nobody recorded: the refusal describes createdby, the column that named someone (or no one).
            return ResumeCreatorRefused(root, recordId, ownerTeamId, logger, traceId,
                decidingState: createdByState, decidingColumn: CreatedByColumn,
                createdByState: createdByState, personState: null);
        }

        string? personState;
        try
        {
            personState = await UnusablePersonStateAsync(dataverseClient, personId, ct);
        }
        catch (Exception ex)
        {
            var readState = ReadFailureState(ex);
            logger.LogError(ex,
                "[PROVISION] The creator person ({Column} {PersonId}) of {RecordType} {RecordId} could not be read " +
                "({State}). TraceId={TraceId}", RecordCreatorPerson.Column, personId, root.WireToken, recordId, readState,
                traceId);
            return ResumeCreatorRefused(root, recordId, ownerTeamId, logger, traceId,
                decidingState: readState, decidingColumn: RecordCreatorPerson.Column,
                createdByState: createdByState, personState: readState);
        }

        if (personState is not null)
        {
            return ResumeCreatorRefused(root, recordId, ownerTeamId, logger, traceId,
                decidingState: personState, decidingColumn: RecordCreatorPerson.Column,
                createdByState: createdByState, personState: personState);
        }

        logger.LogInformation(
            "[PROVISION] {RecordType} {RecordId}: createdby is {CreatedByState}; resuming for the person recorded as its " +
            "creator ({Column} {PersonId}).",
            root.WireToken, recordId, createdByState, RecordCreatorPerson.Column, personId);
        return CreatorShareStep.Ok(personId);
    }

    /// <summary>The column a <c>resume_creator_unavailable</c> refusal describes when it is <c>createdby</c>.</summary>
    private const string CreatedByColumn = "createdby";

    // The states a person can be refused in (the creatorState extension; the client classifies on them).
    private const string UnusableAbsent = "absent";
    private const string UnusableDisabled = "disabled";
    private const string UnusableApplicationUser = "application-user";
    private const string UnusableUnreadable = "unreadable";

    /// <summary>
    /// Dataverse REFUSED a read the decision needs — a 401/403 (the service's sign-in or Read privilege) or a 400
    /// (owner round 14 item 3). Deterministic: calling again repeats it until an administrator acts, so the client does
    /// not offer the same caller a retry.
    /// </summary>
    private const string UnusableRefused = "refused";

    /// <summary>
    /// The state a failed creator read reports: <c>refused</c> when Dataverse refused it
    /// (<see cref="AssignCascadeChildOwners.IsRefusedRead"/>, the one rule for provisioning's reads), else the transient
    /// <c>unreadable</c>.
    /// </summary>
    private static string ReadFailureState(Exception ex) =>
        AssignCascadeChildOwners.IsRefusedRead(ex) ? UnusableRefused : UnusableUnreadable;

    /// <summary>
    /// <c>sprk_createdbyperson</c> does not exist in this environment — its read answered 400 (task 133 r1). Deterministic
    /// until an administrator applies the schema, so the client does not offer the same caller a retry.
    /// </summary>
    private const string UnusableColumnMissing = "column-missing";

    /// <summary>
    /// True when a read failed with HTTP 400 — <c>DataverseWebApiClient</c> raises <see cref="HttpRequestException"/>
    /// carrying the status (<c>EnsureSuccessStatusCode</c>). For the one-column, by-id creator-person read, a 400 means the
    /// column is not in the environment (Dataverse: "Could not find a property named …").
    /// </summary>
    private static bool IsColumnMissing(Exception ex) =>
        ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest };

    /// <summary>
    /// The <c>resume_creator_unavailable</c> refusal: nothing written; the deciding state and the column it describes
    /// named (<c>creatorState</c>, <c>creatorColumn</c>), with both columns' states for the operator
    /// (<c>createdByState</c>, <c>creatorPersonState</c>); and only recoveries that work against this code (task 133
    /// verifier round 2).
    /// </summary>
    private static CreatorShareStep ResumeCreatorRefused(
        SecureRecordRoot root,
        Guid recordId,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        string decidingState,
        string decidingColumn,
        string createdByState,
        string? personState)
    {
        // A failed or refused read and a missing column are environment faults (500); an unusable person is a state of the
        // data (409).
        var serverFault = decidingState is UnusableUnreadable or UnusableRefused or UnusableColumnMissing;

        logger.LogWarning(
            "[PROVISION] Resume of {RecordType} {RecordId} refused: no usable person created it (createdby is " +
            "{CreatedByState}; {Column} is {PersonState}). It is not shared to anyone instead. TraceId={TraceId}",
            root.WireToken, recordId, createdByState, RecordCreatorPerson.Column, personState ?? "empty", traceId);

        var createdByText = createdByState switch
        {
            UnusableUnreadable => "Its creator (createdby) could not be read.",
            UnusableRefused =>
                "Its creator (createdby) could not be read: Dataverse refused the read, or the service's permission to " +
                "read users.",
            UnusableDisabled => "The person who created it (createdby) has a disabled user record.",
            UnusableApplicationUser => "It was created by an application (createdby is an application user).",
            _ => "It records no creator (createdby)."
        };

        var personText = createdByState is UnusableUnreadable or UnusableRefused
            ? string.Empty
            : personState switch
            {
                null => " No person is recorded as its creator either (sprk_createdbyperson is empty — the record was " +
                        "created before that column existed, or outside the BFF).",
                UnusableUnreadable =>
                    " The person recorded as its creator (sprk_createdbyperson) could not be read — if this repeats, an " +
                    "administrator checks that the column exists in this environment " +
                    "(scripts/Set-RecordCreatorPersonSchema.ps1 -Verify).",
                UnusableRefused =>
                    " The person recorded as its creator (sprk_createdbyperson) could not be read: Dataverse refused the " +
                    "read, or the service's permission to read it.",
                UnusableColumnMissing =>
                    " The person recorded as its creator cannot be looked up: Dataverse refused the query naming " +
                    "sprk_createdbyperson (400 Bad Request), which is how it answers when that column does not exist in " +
                    "this environment.",
                UnusableDisabled => " The person recorded as its creator (sprk_createdbyperson) has a disabled user record.",
                UnusableApplicationUser => " The creator recorded in sprk_createdbyperson is an application user.",
                _ => " The person recorded as its creator (sprk_createdbyperson) no longer exists."
            };

        var recovery = decidingState switch
        {
            UnusableUnreadable =>
                "A read failed, so this refusal is not final: calling again once Dataverse is reachable repeats the check " +
                "(the same caller may). ",
            UnusableRefused =>
                "Calling again repeats this refusal until an administrator restores the service's Read privilege — on " +
                "users (systemuser) and on this record's table — or its sign-in; then calling again repeats the check. ",
            UnusableColumnMissing =>
                "Calling again repeats this refusal until an administrator applies the column's schema " +
                "(scripts/Set-RecordCreatorPersonSchema.ps1 -Apply, then -Verify must pass); then the resume can share it " +
                "to the person recorded there, if one is. ",
            UnusableDisabled =>
                "If that person should keep the record, an administrator re-enables their user and calls again: the " +
                "resume then shares it to them. ",
            _ => string.Empty
        };

        return CreatorShareStep.Failed(Problem(
            serverFault ? StatusCodes.Status500InternalServerError : StatusCodes.Status409Conflict,
            serverFault ? "Internal Server Error" : "Conflict",
            $"{root.DisplayLabel} {recordId} is owned by the Secure Record owner team with no container recorded, so " +
            "provisioning would resume and share it to the person who created it. " + createdByText + personText +
            " It is never shared to the caller or anyone else instead. Nothing was changed. " + recovery +
            "Otherwise an administrator assigns the record to the person who should hold it: it then leaves the Secure " +
            "Record owner team (flagged secure, no container, uploads refused — as before it was first provisioned), and " +
            "that person calls provisioning, which runs from the start and shares it to them.",
            traceId, (ReasonKey, ReasonResumeCreatorUnavailable), ("creatorState", decidingState),
            ("creatorColumn", decidingColumn), ("createdByState", createdByState),
            ("creatorPersonState", personState), ("ownerTeamId", ownerTeamId)));
    }

    /// <summary>
    /// The record's <c>sprk_createdbyperson</c> (task 133, owner round 7 item 2), in a query of its own: provisioning's
    /// Step 1 select never names it, so a BFF deployed before the schema still provisions forward. Throws when the read
    /// fails (including the 400 an environment without the column answers) or the record is not found.
    /// </summary>
    private static async Task<Guid?> ReadCreatorPersonAsync(
        DataverseWebApiClient dataverseClient, SecureRecordRoot root, Guid recordId, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<CreatorPersonRow>(
            root.EntitySet,
            filter: $"{root.IdColumn} eq {recordId}",
            select: root.CreatorPersonSelect,
            top: 1,
            cancellationToken: ct);

        var row = rows.FirstOrDefault()
                  ?? throw new InvalidOperationException(
                      $"{root.WireToken} {recordId} was not found when its creator person was read.");

        return row.Person is { } person && person != Guid.Empty ? person : null;
    }

    /// <summary>
    /// Ensures the resume's creator share (task 133): exactly <see cref="CreatorAccessRights"/> for the person
    /// <see cref="ResolveResumeCreatorAsync"/> decided, proven by a read. Nothing else is written here.
    /// </summary>
    private static async Task<CreatorShareStep> EnsureResumeCreatorShareAsync(
        RestrictedCreatorRule creatorRule,
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var ensured = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct, creatorRule: creatorRule);
        if (!ensured.Proven)
        {
            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"This call was resuming {root.DisplayLabel.ToLowerInvariant()} {recordId}, which is owned by the " +
                "Secure Record owner team with no container recorded, and could not confirm the share to the person who " +
                "created it. This call did not change the record's ownership. The same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorShareFailed), ("resumed", true), ("ownerTeamId", ownerTeamId)));
        }

        return CreatorShareStep.Ok(creatorId);
    }

    /// <summary>
    /// Why <paramref name="systemUserId"/> cannot be the person a secure record is kept open for — <c>absent</c>,
    /// <c>disabled</c> or <c>application-user</c> — or <c>null</c> when it can. Throws when the user cannot be read:
    /// an unreadable user is never "usable". An application user is <c>application-user</c> whether enabled or not — it
    /// is never a person, so a DISABLED application user is not read as a disabled person (task 150 verifier c1 item 7:
    /// "createdby when human"). Otherwise a row whose <c>isdisabled</c> is not read as <c>false</c> (including null) is
    /// treated as disabled — only a proven-enabled user counts.
    /// </summary>
    private static async Task<string?> UnusablePersonStateAsync(
        DataverseWebApiClient dataverseClient, Guid systemUserId, CancellationToken ct)
    {
        var users = await dataverseClient.QueryAsync<CreatorRow>(
            "systemusers",
            filter: $"systemuserid eq {systemUserId}",
            select: "systemuserid,isdisabled,applicationid",
            top: 1,
            cancellationToken: ct);

        var user = users.FirstOrDefault();
        if (user is null)
            return UnusableAbsent;
        if (user.applicationid is { } app && app != Guid.Empty)
            return UnusableApplicationUser;
        if (user.isdisabled != false)
            return UnusableDisabled;
        return null;
    }

    /// <summary>
    /// Makes the creator hold EXACTLY <see cref="CreatorAccessRights"/> on the record — or, for a Make Secure caller, the
    /// mask <paramref name="targetFor"/> names for what they hold now and held before the call (round 40 item 2, round 46
    /// item 1: never lower) — and proves it by reading the shares back. Idempotent: a share already holding exactly the
    /// target is not written again.
    /// </summary>
    /// <remarks>
    /// A creator with no share gets GrantAccess; one whose share holds other rights gets ModifyAccess, because
    /// GrantAccess is not documented to set the rights of an existing share (task 063). Every read is the
    /// complete-or-throw read: "the read failed" is never "the share is there".
    /// </remarks>
    private static async Task<ShareEnsureResult> EnsureCreatorShareAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        ILogger logger,
        CancellationToken ct,
        Func<int, int>? targetFor = null,
        RestrictedCreatorRule? creatorRule = null)
    {
        var principal = DataversePrincipalRef.User(creatorId);

        // Task 114: on a Restricted record a person flagged external is not shared to — the share is deliberately absent,
        // which is this step's answer ("proven" that nothing is owed). A flag that cannot be read is no proof (ADR-003).
        if (creatorRule is not null)
        {
            var barred = await creatorRule.IsBarredAsync(creatorId, logger, ct);
            if (barred is null)
                return new ShareEnsureResult(Proven: false, WriteAttempted: false);
            if (barred == true)
            {
                logger.LogWarning(
                    "[PROVISION] Restricted {RecordType} {RecordId}: {CreatorId} is flagged external, so the record is NOT " +
                    "shared to them (owner round 67: Restricted wins over the last-reader rule).", root.WireToken, recordId, creatorId);
                return new ShareEnsureResult(Proven: true, WriteAttempted: false, Barred: true);
            }
        }

        int current;
        try
        {
            current = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The shares on {RecordType} {RecordId} could not be read completely, so the creator's " +
                "share cannot be confirmed.", root.WireToken, recordId);
            return new ShareEnsureResult(Proven: false, WriteAttempted: false);
        }

        // The mask to hold: the creator's level exactly, unless a Make Secure caller already holds more (round 40 item 2).
        var target = targetFor?.Invoke(current) ?? CreatorAccessMask;
        if (current == target)
            return new ShareEnsureResult(Proven: true, WriteAttempted: false);

        var targetRights = target == CreatorAccessMask ? CreatorAccessRights : RecordShareLevels.RightsCsvForMask(target);
        try
        {
            if (current == 0)
                await recordShare.GrantAccessAsync(root.EntitySet, recordId, principal, targetRights, ct);
            else
                await recordShare.ModifyAccessAsync(root.EntitySet, recordId, principal, targetRights, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be written.",
                root.WireToken, recordId, creatorId);
            return new ShareEnsureResult(Proven: false, WriteAttempted: true);
        }

        try
        {
            var after = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (after == target)
                return new ShareEnsureResult(Proven: true, WriteAttempted: true);

            logger.LogError(
                "[PROVISION] The creator's share on {RecordType} {RecordId} reads back as mask {Mask}, not {Expected}.",
                root.WireToken, recordId, after, target);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} could not be read back.",
                root.WireToken, recordId);
        }

        return new ShareEnsureResult(Proven: false, WriteAttempted: true);
    }

    /// <summary>
    /// Puts the creator's share back to <paramref name="targetMask"/> (0 = no share) and proves it by reading back.
    /// Returns false when that cannot be proven — the caller then says so rather than claiming a clean undo.
    /// </summary>
    /// <param name="recordOwner">
    /// The record's owner at the moment of the restore — every caller restores with the record on its pre-call owner (the
    /// move was never made, was read back as not made, or was moved back and read back). When that owner is the creator, a
    /// revoke of the creator's share runs AS the creator: Dataverse refuses an app-only revoke of the owning user's own
    /// share ("Only owner can revoke access to the owner", 0x80040223), which left the undo of a creator-owned record
    /// <c>sharesRestored: false</c> with the share in place. The read-back below still decides the answer.
    /// </param>
    private static async Task<bool> RestoreCreatorShareAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        int targetMask,
        DataversePrincipalRef recordOwner,
        ILogger logger,
        CancellationToken ct)
    {
        var principal = DataversePrincipalRef.User(creatorId);
        try
        {
            var current = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (current == targetMask)
                return true;

            if (targetMask == 0)
                await recordShare.RevokeAccessAsync(root.EntitySet, recordId, principal, recordOwner, ct);
            else if (current == 0)
                await recordShare.GrantAccessAsync(root.EntitySet, recordId, principal, RecordShareLevels.RightsCsvForMask(targetMask), ct);
            else
                await recordShare.ModifyAccessAsync(root.EntitySet, recordId, principal, RecordShareLevels.RightsCsvForMask(targetMask), ct);

            var after = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (after == targetMask)
                return true;

            logger.LogError(
                "[PROVISION] Restoring the creator's share on {RecordType} {RecordId}: reads back as mask {Mask}, not " +
                "{Expected}.", root.WireToken, recordId, after, targetMask);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} could not be restored to mask {Expected}.",
                root.WireToken, recordId, targetMask);
            return false;
        }
    }

    /// <summary>The rights mask <paramref name="systemUserId"/>'s own share carries (0 = no share).</summary>
    private static int MaskOf(IReadOnlyList<DataversePrincipalAccess> shares, Guid systemUserId) =>
        shares
            .Where(s => s.Principal == DataversePrincipalRef.User(systemUserId))
            .Aggregate(0, (mask, s) => mask | s.AccessRightsMask);

    /// <summary>
    /// Evicts the caches an owner change makes stale, through the ONE hook every owner-changing writer must call
    /// (<see cref="IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync"/>, task 132). Never fails the request:
    /// the hook does not throw, and it is not bound to the request's token — a client that disconnects must not leave
    /// the clean-up half done.
    /// </summary>
    private static Task EvictAfterOwnerChangeAsync(
        IMembershipCacheInvalidator accessCacheInvalidator, SecureRecordRoot root, Guid recordId, string traceId)
        => accessCacheInvalidator.InvalidateRecordOwnerChangeAsync(
            root.LogicalName, root.EntitySet, recordId, traceId, CancellationToken.None);

    /// <summary>
    /// Task 132 x task 133 (batch 4 integration): each cascaded child the compensation tried to put back on its own owner
    /// is an owner change — evicted once, whatever its read-back said (a PATCH that reports failure can have committed).
    /// A child read as already on its owner, gone, or unreadable before any write was not written: nothing to evict.
    /// <para>The children an Assign cascades to (<c>sharepointdocumentlocation</c>, <c>sharepointdocument</c>) are held by
    /// no access cache (<c>AssignCascadeChildOwners.IsReownedByCascade</c>), so for them the hook builds no pattern and
    /// touches Redis not at all (task 132 integration residual) — the call stays because the HOOK, not each writer, decides
    /// what an owner change can have made stale.</para>
    /// </summary>
    private static async Task EvictRestoredChildrenAsync(
        IMembershipCacheInvalidator accessCacheInvalidator, CascadeRestoreReport children, string traceId)
    {
        foreach (var restore in children.Children)
        {
            if (restore.Outcome is CascadeChildRestoreOutcome.Restored or CascadeChildRestoreOutcome.Refused
                or CascadeChildRestoreOutcome.NotApplied or CascadeChildRestoreOutcome.Unverified)
            {
                await accessCacheInvalidator.InvalidateRecordOwnerChangeAsync(
                    restore.Child.LogicalName, restore.Child.EntitySet, restore.Child.Id, traceId, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Assigns the record's owner to <paramref name="target"/>, then reads the owner back. Used for the move to the
    /// team AND for the compensating move back.
    /// </summary>
    /// <remarks>
    /// <para><b>The read-back is the point, not belt-and-braces.</b> <c>ownerid</c> is written through
    /// <c>@odata.bind</c>, and Dataverse's behaviour on an unrecognised <c>@odata.bind</c> property is to accept the
    /// request and ignore the property — no error, no write. So the outcome is what the read shows: moved, not moved,
    /// or (the read failed) unverified. The read runs even when the PATCH threw, because a PATCH that reports failure
    /// can still have committed, and only the read tells the caller which state to describe.</para>
    ///
    /// <para>Ownership is assigned on its own, not folded into another PATCH: Dataverse treats an owner change as a
    /// distinct operation from a field update, and combining them is a documented way to have one of the two quietly
    /// not happen.</para>
    /// </remarks>
    private static async Task<OwnerMove> MoveOwnerAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        DataversePrincipalRef target,
        ILogger logger,
        CancellationToken ct)
    {
        var patchRefused = false;
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/{target.Kind.ToEntitySet()}({target.Id})"
                },
                ct);
        }
        catch (Exception ex)
        {
            patchRefused = true;
            logger.LogError(ex,
                "[PROVISION] Dataverse refused the ownership assignment of {RecordType} {RecordId} to {OwnerKind} " +
                "{OwnerId}. If this is a privilege error, the owner team lacks the entity rights an assignment target " +
                "must hold (Secure Record Owner role, setup guide §5).", root.WireToken, recordId, target.Kind, target.Id);
        }

        try
        {
            var rows = await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},_owningteam_value,_owninguser_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread?.Owner == target)
            {
                logger.LogInformation(
                    "[PROVISION] {RecordType} {RecordId} is now owned by {OwnerKind} {OwnerId} (verified by read-back)",
                    root.WireToken, recordId, target.Kind, target.Id);
                return new OwnerMove(OwnerMoveOutcome.Moved, patchRefused);
            }

            logger.LogError(
                "[PROVISION] Ownership read-back for {RecordType} {RecordId}: expected {OwnerKind} {OwnerId}, found " +
                "team {ActualTeamId} / user {ActualUserId}. The record was NOT moved.",
                root.WireToken, recordId, target.Kind, target.Id, reread?._owningteam_value, reread?._owninguser_value);
            return new OwnerMove(OwnerMoveOutcome.NotMoved, patchRefused);
        }
        catch (Exception ex)
        {
            // An unverifiable assignment is never reported as done, and never as "nothing moved" either.
            logger.LogError(ex,
                "[PROVISION] Could not read back the owner of {RecordType} {RecordId} after assigning it to " +
                "{OwnerKind} {OwnerId}. The outcome is UNVERIFIED.", root.WireToken, recordId, target.Kind, target.Id);
            return new OwnerMove(OwnerMoveOutcome.Unverified, patchRefused);
        }
    }

    /// <summary>
    /// Shares the record with the request's named colleagues — and, on Make Secure, with the record's creator
    /// (<paramref name="recordCreator"/>) — best-effort (task 061), and only once the share this run proves is in place
    /// (<paramref name="creatorId"/>'s: the caller's on the forward path and on a Make Secure resume, the record creator's
    /// on a resume without the transition), so no colleague outcome changes the result of any branch.
    /// </summary>
    /// <remarks>
    /// <para>A colleague who already holds a share (a resumed run that got this far before) is not shared to again. If the
    /// shares cannot be read, every colleague is shared to, as before task 133 — their absence is visible and fixable
    /// through the FR-29 "+ User" path, the creator's is not.</para>
    /// <para><b>Never silent</b> (task 150, round 33 items 1 and 5). Every person NOT shared to is named in the returned
    /// list with the reason: on the No Access list, that list unverifiable (task 143), or the share itself failed
    /// (<see cref="ReasonPrincipalShareFailed"/>) — so the client tells the caller who did not get access, and the
    /// confirmation's "the person who created this record … will keep access" is never broken without saying so.</para>
    /// </remarks>
    /// <summary>
    /// Task 114: <paramref name="colleagues"/> without anyone a Restricted record may not be shared with
    /// (<c>ClassifyEligibility</c> = <c>ExternalOnRestricted</c>); each one left out is added to <paramref name="skipped"/>.
    /// </summary>
    private static async Task<List<Guid>> WithoutExternalOnRestrictedAsync(
        DataverseWebApiClient dataverseClient, SecureRecordRoot root, Guid recordId, bool restrictedThroughFiling, List<Guid> colleagues,
        List<ProvisionSkippedPrincipal> skipped, ILogger logger, string traceId, CancellationToken ct)
    {
        const string couldNotCheck =
            "Whether this person may access this record could not be checked (whether they are flagged as external on a " +
            "Restricted record), so it was not shared with them. Add them through Manage Access once it can be checked.";

        List<InternalShareEndpoints.SystemUserRow> users;
        bool restricted;
        try
        {
            users = await dataverseClient.QueryAsync<InternalShareEndpoints.SystemUserRow>(
                InternalShareEndpoints.SystemUserEntitySet,
                filter: string.Join(" or ", colleagues.Select(id => $"systemuserid eq {id}")),
                select: "systemuserid,sprk_isexternal",
                top: colleagues.Count,
                cancellationToken: ct);

            restricted = false;
            if (users.Any(u => colleagues.Contains(u.Id) && u.IsExternal == true) && restrictedThroughFiling)
            {
                restricted = true; // #1478: Restricted through a parent (task 175)
            }
            else if (users.Any(u => colleagues.Contains(u.Id) && u.IsExternal == true))
            {
                var rows = await dataverseClient.QueryAsync<RootRow>(
                    root.EntitySet, filter: $"{root.IdColumn} eq {recordId}", select: "sprk_accesspermission", top: 1,
                    cancellationToken: ct);
                restricted = rows.FirstOrDefault() is { } rootRow
                    ? rootRow.sprk_accesspermission == ExternalParticipationService.AccessPermissionRestricted
                    : throw new InvalidOperationException("The record did not come back, so whether it is Restricted is unknown.");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[PROVISION] Whether the named colleagues of {RecordType} {RecordId} are flagged external on a Restricted record " +
                "could not be read; none is shared. TraceId={TraceId}", root.WireToken, recordId, traceId);
            skipped.AddRange(colleagues.Select(id => new ProvisionSkippedPrincipal(id, ReasonPrincipalNoAccessUnverifiable, couldNotCheck)));
            return new List<Guid>();
        }

        if (!restricted)
            return colleagues;

        var kept = new List<Guid>(colleagues.Count);
        foreach (var id in colleagues)
        {
            var row = users.FirstOrDefault(u => u.Id == id);
            if (InternalShareEndpoints.IsBarredOnRestricted(row?.IsExternal, rootIsRestricted: true))
            {
                logger.LogWarning(
                    "[PROVISION] Not sharing Restricted {RecordType} {RecordId} with named principal {PrincipalId}: flagged " +
                    "external (owner round 67). TraceId={TraceId}", root.WireToken, recordId, id, traceId);
                skipped.Add(new ProvisionSkippedPrincipal(id, ReasonPrincipalExternalOnRestricted,
                    "This record is Restricted to internal users, and this person is flagged as external, so it was not " +
                    "shared with them."));
                continue;
            }

            kept.Add(id);
        }

        return kept;
    }

    private static async Task<(int Shared, IReadOnlyList<ProvisionSkippedPrincipal> Skipped)> ShareToColleaguesAsync(
        bool restrictedThroughFiling,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        SecureShareNoAccessGuard noAccessGuard,
        ProvisionProjectRequest request,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        Guid? recordCreator,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        // Make Secure (round 33 item 1): the record's creator joins the colleagues — the same No Access check, the same
        // Collaborate level, the same per-person warning when skipped.
        var colleagues = (request.SharePrincipalIds ?? Array.Empty<Guid>())
            .Concat(recordCreator is { } person ? new[] { person } : Array.Empty<Guid>())
            .Where(id => id != Guid.Empty && id != creatorId)
            .Distinct()
            .ToList();

        var skipped = new List<ProvisionSkippedPrincipal>();
        if (colleagues.Count == 0)
            return (0, skipped);

        // ── Owner N6 (task 143): a walled colleague is SKIPPED with a per-person warning; the others are shared ──
        // Asked before any colleague share is written. An unanswerable check skips that colleague too (ADR-003). Task 158
        // r1c-v2 (round 39 item 2): on a work assignment or project filed under secure records, the colleague honours every
        // secure parent's No Access list too — the guard's ONE entry point, the record asked about as the secure record it
        // is becoming.
        var allowed = new List<Guid>(colleagues.Count);
        foreach (var principalId in colleagues)
        {
            var wall = await noAccessGuard.CheckRecordAndSecureParentsAsync(
                root.LogicalName, recordId, principalId, SecureWallRecordScope.BeingSecured, ct);
            if (!wall.RefusesShare)
            {
                allowed.Add(principalId);
                continue;
            }

            var walled = wall.Outcome == SecureShareWallOutcome.Walled;
            logger.LogWarning(
                "[PROVISION] Not sharing {RecordType} {RecordId} with named principal {PrincipalId}: {State} the No Access " +
                "list of {Where} ({Detail}). TraceId={TraceId}",
                root.WireToken, recordId, principalId, walled ? "on" : "not provably off",
                wall.ParentTable is { } parentTable ? $"{parentTable}:{wall.ParentId:D}" : $"{root.LogicalName}:{recordId:D}",
                walled ? string.Join(",", wall.EntryIds) : wall.Fault, traceId);
            var list = wall.ParentTable is { } walledParent
                ? $"the No Access list of the secure {SecureRootInheritance.WireTokenFor(walledParent)} this record is filed under"
                : wall.FilingUnreadable
                    ? "the No Access list of a secure record this record is filed under"
                    : "the No Access list for this record";
            skipped.Add(walled
                ? new ProvisionSkippedPrincipal(principalId, ReasonPrincipalNoAccess,
                    $"This person is on {list}, so it was not shared with them.")
                : new ProvisionSkippedPrincipal(principalId, ReasonPrincipalNoAccessUnverifiable,
                    $"Whether this person is on {list} could not be checked, so it was not " +
                    "shared with them. Add them through Manage Access once it can be checked."));
        }

        colleagues = allowed;
        if (colleagues.Count == 0)
            return (0, skipped);

        // ── Task 114 (owner round 67): the ONE share-eligibility rule — a Restricted record is never shared with a person
        // flagged external. The flags are read for the colleagues; the record's Restricted state only when one of them is
        // flagged. A read that fails skips the colleague (ADR-003), as an unverifiable No Access check does.
        colleagues = await WithoutExternalOnRestrictedAsync(
            dataverseClient, root, recordId, restrictedThroughFiling, colleagues, skipped, logger, traceId, ct);
        if (colleagues.Count == 0)
            return (0, skipped);

        IReadOnlyList<DataversePrincipalAccess> existing;
        try
        {
            existing = await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct);
        }
        catch (Exception)
        {
            existing = Array.Empty<DataversePrincipalAccess>();
        }

        var shared = 0;
        foreach (var principalId in colleagues)
        {
            if (MaskOf(existing, principalId) != 0)
            {
                shared++;
                continue;
            }

            try
            {
                await recordShare.GrantAccessAsync(
                    root.EntitySet, recordId, DataversePrincipalRef.User(principalId), CollaboratorAccessRights, ct);
                shared++;
            }
            catch (Exception ex)
            {
                // Logged with the id so an operator can see exactly who was missed — and named in the response, so the
                // caller is told too (round 33 item 5: never silent).
                logger.LogWarning(ex,
                    "[PROVISION] Could not share {RecordType} {RecordId} with named principal {PrincipalId}. " +
                    "Provisioning continues; add them via the Manage Access surface. TraceId={TraceId}",
                    root.WireToken, recordId, principalId, traceId);
                skipped.Add(new ProvisionSkippedPrincipal(principalId, ReasonPrincipalShareFailed,
                    "Sharing this record with this person failed, so it was not shared with them. Add them through " +
                    "Manage Access."));
            }
        }

        logger.LogInformation(
            "[PROVISION] {RecordType} {RecordId} shared to creator {CreatorId} and {Count} named principal(s).",
            root.WireToken, recordId, creatorId, shared);

        return (shared, skipped);
    }

    /// <summary>
    /// Creates the SPE container via the SpeFileStore facade (ADR-007).
    /// </summary>
    /// <remarks>
    /// There is no rollback of the ownership assignment if this fails. That is deliberate: ownership
    /// inside the Secure Record business unit is the safer state to be left in, so undoing it on a
    /// container failure would move the record back OUT of the secure business unit — turning a
    /// storage failure into a disclosure. The record is left secured and shared to its creator with no container
    /// recorded, which the next call resumes (task 133).
    /// </remarks>
    private static async Task<SpeContainerCreationResult> CreateSpeContainerAsync(
        SpeFileStore speFileStore,
        Guid containerTypeId,
        SecureRecordRoot root,
        string recordName,
        Guid recordId,
        Guid ownerTeamId,
        Guid owningBusinessUnitId,        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        logger.LogInformation("[PROVISION] Creating SPE container for {RecordType} {RecordId}", root.WireToken, recordId);

        string? failure;
        try
        {
            var containerDisplayName = root.ContainerDisplayName(recordName);
            var containerDescription = root.ContainerDescription(recordName);

            var container = await speFileStore.CreateContainerAsync(
                containerTypeId, containerDisplayName, owningBusinessUnitId, containerDescription, ct);

            if (container != null)
            {
                logger.LogInformation(
                    "[PROVISION] Created SPE container {ContainerId} ('{DisplayName}') for {RecordType} {RecordId}",
                    container.Id, containerDisplayName, root.WireToken, recordId);

                return new SpeContainerCreationResult(container.Id, null);
            }

            failure = "Graph API returned no container";
            logger.LogError(
                "[PROVISION] SpeFileStore.CreateContainerAsync returned null for {RecordType} {RecordId}",
                root.WireToken, recordId);
        }
        catch (Exception ex)
        {
            failure = "the container request failed";
            logger.LogError(ex,
                "[PROVISION] Failed to create SPE container for {RecordType} {RecordId}", root.WireToken, recordId);
        }

        return new SpeContainerCreationResult(null, Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} is owned by the Secure Record owner team and shared to its " +
            $"creator, but its own SPE container could not be created ({failure}). Nothing needs undoing: calling " +
            "provisioning again (the same caller may) resumes from here.",
            traceId, (ReasonKey, ReasonContainerCreationFailed), ("ownerTeamId", ownerTeamId)));
    }

    /// <summary>
    /// Sets <c>sprk_issecure = true</c> and proves it by reading it back — or, when the record already reads
    /// <c>true</c>, writes nothing (task 150). Returns the refusal to send, or <c>null</c> when the flag is proven set.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the server sets it.</b> The column is field-secured: only this service's application user (the
    /// "Spaarke BFF-Managed Field Writers" profile) may create or update it, so a client can no longer mark a record
    /// secure without provisioning it, nor clear the flag on a secure one. The client asks for a secure record by
    /// calling this endpoint.</para>
    ///
    /// <para><b>Why FIRST, and why it is never undone.</b> Every refusal before it changes nothing, so the record is
    /// still not flagged and the client — which skips every upload and child create for a secure-requested record whose
    /// provisioning did not succeed — has put nothing in shared storage. From this write on, every failure leaves the
    /// record FLAGGED with no container of its own, whose uploads are refused (fail closed). Compensation restores
    /// ownership and shares, never the flag: un-flagging a record the user asked to secure would route its next upload
    /// to shared storage, which cannot be retracted.</para>
    ///
    /// <para><b>The read-back is the proof</b> (ADR-003): an accepted PATCH is not evidence the value is stored, and a
    /// read that comes back without the value — a field-secured column this identity cannot read is returned EMPTY,
    /// not refused — must not be taken for success.</para>
    /// </remarks>
    private static async Task<IResult?> EnsureSecureFlagAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        bool alreadyFlagged,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        if (alreadyFlagged)
        {
            logger.LogInformation(
                "[PROVISION] {RecordType} {RecordId} already reads sprk_issecure = true (created before task 150, or by an " +
                "older client); the flag is not written again.", root.WireToken, recordId);
            return null;
        }

        string failure;
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?> { ["sprk_issecure"] = true },
                ct);

            var reread = (await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},sprk_issecure",
                top: 1,
                cancellationToken: ct)).FirstOrDefault();

            if (reread?.sprk_issecure == true)
            {
                logger.LogInformation(
                    "[PROVISION] Set sprk_issecure = true on {RecordType} {RecordId} (read back).", root.WireToken, recordId);
                return null;
            }

            failure = reread is null
                ? "the record could not be read back"
                : "the value read back was not true — if it came back empty, this service has likely lost its " +
                  "field-level-security Read on sprk_issecure";

            logger.LogError(
                "[PROVISION] sprk_issecure on {RecordType} {RecordId} did not read back true after the write " +
                "(read back: {Value}). Stopped: nothing else was written. TraceId={TraceId}",
                root.WireToken, recordId, reread?.sprk_issecure?.ToString() ?? "(empty)", traceId);
        }
        catch (Exception ex)
        {
            failure = "the write or its read-back failed — if Dataverse refused the write, this service's application " +
                      "user is not in the field security profile that may update sprk_issecure";

            logger.LogError(ex,
                "[PROVISION] Could not set sprk_issecure on {RecordType} {RecordId}. Stopped: nothing else was written. " +
                "TraceId={TraceId}", root.WireToken, recordId, traceId);
        }

        return Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} could not be marked secure ({failure}). That is the first change " +
            "provisioning makes, so nothing else was changed: its ownership, shares and storage are as they were (the " +
            "secure flag itself may or may not be set — while it is set, uploads to the record are refused). The same " +
            "caller may retry; an administrator checks the field security setup with " +
            "scripts/Set-SecureFlagFieldSecurity.ps1 -Verify.",
            traceId, (ReasonKey, ReasonSecureFlagNotSet));
    }

    /// <summary>
    /// Records the provisioned container on the record. Throws on failure — the caller turns that
    /// into a non-2xx carrying the container id (ADR-003).
    /// </summary>
    /// <remarks>
    /// <para><c>sprk_containerid</c> is <c>NVARCHAR(100)</c> on all three roots (live metadata) — a PLAIN STRING
    /// write. No <c>@odata.bind</c>, no navigation property, nothing case-sensitive.</para>
    ///
    /// <para>Provisioning is the ONLY writer of <c>sprk_containerid</c> on a provisionable root (task 076; see
    /// <see cref="RootRow.IsProvisioned"/>), in exactly two places: this step, which records the record's own container,
    /// and Step 4.2, which UNLINKS a shared container — a business unit's (stamped by a client before task 076 removed
    /// that write) or one this BFF is configured to use for many records — before the owner move (task 133 r1; the
    /// business unit or configuration keeps pointing at it, and the unlinked value is logged there). So this step never
    /// overwrites a value: a record that records a container of its OWN never reaches it (it is kept, task 133 b2), and
    /// one whose container another root also records is refused before any write.</para>
    /// </remarks>
    private static async Task RecordContainerAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        string speContainerId,
        ILogger logger,
        CancellationToken ct)
    {
        await dataverseClient.UpdateAsync(
            root.EntitySet,
            recordId,
            new Dictionary<string, object?>
            {
                ["sprk_containerid"] = speContainerId
            },
            ct);

        logger.LogInformation(
            "[PROVISION] Recorded container {ContainerId} on {RecordType} {RecordId}",
            speContainerId, root.WireToken, recordId);
    }

    /// <summary>Builds a ProblemDetails result with a traceId and any extra extension members.</summary>
    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        string traceId,
        params (string Key, object? Value)[] extensions)
    {
        var members = new Dictionary<string, object?> { ["traceId"] = traceId };
        foreach (var (key, value) in extensions)
            members[key] = value;

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: members);
    }

    // =========================================================================
    // Private types
    // =========================================================================

    /// <summary>
    /// The No Access decision for the person a record is being secured for (task 158 r1): the outcome, the list that
    /// decided it (<see cref="Where"/>, for the log), the parent table when it was a filed-under record's list
    /// (<see cref="ParentTable"/>, for the message — which never names an entry), the matching entries and the fault.
    /// </summary>
    internal sealed record CreatorWallDecision(
        SecureShareWallOutcome Outcome, string Where, string? ParentTable, IReadOnlyList<Guid> EntryIds, string? Fault)
    {
        /// <summary>Walled, or could not tell: the share — and so the provisioning — is refused.</summary>
        public bool RefusesShare => Outcome is SecureShareWallOutcome.Walled or SecureShareWallOutcome.Unverifiable;

        /// <summary>Task 158 r1c-v2: could not tell because what the record is filed under could not be read.</summary>
        public bool FilingUnreadable { get; init; }
    }

    /// <summary>
    /// unified-access-control-r2 task 158 r1 (owner round 31 item 1): is the person a record is being secured for walled
    /// off it — by the record's OWN No Access list, or by that of ANY secure matter or project it is filed under (the rule
    /// the sharee mirror already applies)? Read-only; asked before provisioning's first write, on the caller's path and the
    /// inherited path alike, and never dependent on the record's flag (the record is asked about as the secure record it is
    /// becoming). Fail closed: a record whose parents cannot all be decided, or any guard answer that is not "not walled",
    /// refuses. A <see cref="SecureShareWallOutcome.Walled"/> answer anywhere wins over an unverifiable one (it is final).
    /// </summary>
    internal static async Task<CreatorWallDecision> CheckCreatorWallsAsync(
        SecureShareNoAccessGuard noAccessGuard,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        CancellationToken ct)
    {
        // Task 158 r1c-v2 (round 39 item 2): the guard's ONE entry point for "the record's list AND every secure parent's" —
        // the record asked about as the secure record it is becoming; never a second copy of the parent walk here.
        var decision = await noAccessGuard.CheckRecordAndSecureParentsAsync(
            root.LogicalName, recordId, creatorId, SecureWallRecordScope.BeingSecured, ct);
        return new CreatorWallDecision(
            decision.RefusesShare ? decision.Outcome : SecureShareWallOutcome.NotWalled,
            decision.ParentTable is { } parentTable ? $"{parentTable}:{decision.ParentId:D}"
                : decision.FilingUnreadable ? "a record it is filed under"
                : decision.RefusesShare ? $"{root.LogicalName}:{recordId:D}" : "every list",
            decision.ParentTable, decision.EntryIds, decision.Fault)
        {
            FilingUnreadable = decision.FilingUnreadable,
        };
    }

    /// <summary>
    /// unified-access-control-r2 task 158 r1: the person an INHERITED provisioning of <paramref name="recordId"/> would
    /// secure it for — the resume rule (<c>createdby</c> when a usable person, else <c>sprk_createdbyperson</c>) — read-only,
    /// for a writer that must decide BEFORE its own write whether the record it files under a secure record can be secured
    /// (round 31 item 1). <c>CreatorId</c> when one is named; otherwise the provisioning's own refusal code and detail.
    /// </summary>
    internal static async Task<(Guid? CreatorId, string? RefusalCode, string? Detail)> ResolveRecordedCreatorAsync(
        DataverseWebApiClient dataverseClient, SecureRecordRoot root, Guid recordId, ILogger logger, string traceId,
        CancellationToken ct)
    {
        RootRow? row;
        try
        {
            row = (await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet, filter: $"{root.IdColumn} eq {recordId}", select: root.ProvisioningSelect, top: 1,
                cancellationToken: ct)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[PROVISION] {RecordType} {RecordId} could not be read to name its creator.", root.WireToken, recordId);
            return (null, ReasonCreatorNoAccessUnverifiable, "the record could not be read to name the person who created it");
        }

        if (row is null)
            return (null, ReasonResumeCreatorUnavailable, "the record was not found");

        var person = await ResolveResumeCreatorAsync(dataverseClient, root, recordId, row, Guid.Empty, logger, traceId, ct, resuming: false);
        if (person.Error is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem)
        {
            var code = problem.ProblemDetails.Extensions.TryGetValue(ReasonKey, out var value) ? value?.ToString() : null;
            return (null, code ?? ReasonResumeCreatorUnavailable, problem.ProblemDetails.Detail);
        }

        return person.Error is null
            ? (person.CreatorId, null, null)
            : (null, ReasonResumeCreatorUnavailable, "the person who created it could not be determined");
    }

    /// <summary>
    /// WHO a provisioning secures the record for (task 158). <see cref="Caller"/>: the calling user, by WhoAmI on their
    /// own token (task 061) — every HTTP call. <see cref="RecordedCreator"/>: the person who created the record, by the
    /// RESUME rule (<c>createdby</c> when usable, else <c>sprk_createdbyperson</c>) — the inherited provisioning of a work
    /// assignment or project filed under a secure record, which has no caller. Never a value from a request body.
    /// </summary>
    internal sealed class ProvisioningCreator
    {
        private readonly CallerRecordAccessProbe? _probe;
        private readonly string? _callerToken;

        private ProvisioningCreator(CallerRecordAccessProbe? probe, string? callerToken, bool isRecordedCreator)
        {
            _probe = probe;
            _callerToken = callerToken;
            IsRecordedCreator = isRecordedCreator;
        }

        /// <summary>True for the inherited provisioning: no caller; the record's creator is the person.</summary>
        public bool IsRecordedCreator { get; }

        /// <summary>The caller's probe (task 150's Make Secure floor read); <c>null</c> for the inherited provisioning.</summary>
        public CallerRecordAccessProbe? Probe => _probe;

        /// <summary>The caller's bearer token; <c>null</c> for the inherited provisioning.</summary>
        public string? CallerToken => _callerToken;

        /// <summary>The caller of an HTTP request, through their own bearer token.</summary>
        public static ProvisioningCreator Caller(CallerRecordAccessProbe probe, string? callerToken) =>
            new(probe ?? throw new ArgumentNullException(nameof(probe)), callerToken, isRecordedCreator: false);

        /// <summary>No caller: the person who created the record (task 158).</summary>
        public static ProvisioningCreator RecordedCreator { get; } = new(null, null, isRecordedCreator: true);

        /// <summary>The caller's <c>systemuserid</c> — <c>null</c> when it cannot be established, or there is no caller.</summary>
        public Task<Guid?> CallerSystemUserIdAsync(CancellationToken ct) =>
            _probe is null ? Task.FromResult<Guid?>(null) : _probe.GetCallerSystemUserIdAsync(_callerToken, ct);
    }

    /// <summary>What a read-back of the owner showed after an assignment.</summary>
    private enum OwnerMoveOutcome
    {
        /// <summary>The owner reads back as the target.</summary>
        Moved,

        /// <summary>The owner reads back as something else: the assignment did not take effect.</summary>
        NotMoved,

        /// <summary>The owner could not be read back: the assignment may or may not have taken effect.</summary>
        Unverified
    }

    /// <summary>An owner assignment's observed outcome, and whether the PATCH itself reported failure.</summary>
    private readonly record struct OwnerMove(OwnerMoveOutcome Outcome, bool PatchRefused);

    /// <summary>
    /// Whether a read proved the creator's share exact, and whether this call wrote (or tried to write) it — the
    /// second decides whether an undo has anything to undo.
    /// </summary>
    /// <param name="Barred">Task 114 verifier V2: the person is flagged external on a Restricted record, so NO share was written
    /// and none is owed — <see cref="Proven"/> is <c>true</c> (nothing to prove), but the person holds no share, and a caller
    /// that would otherwise say "the share is in place" must say it was not given.</param>
    private readonly record struct ShareEnsureResult(bool Proven, bool WriteAttempted, bool Barred = false);

    /// <summary>The creator whose share is proven, or the response that stopped provisioning.</summary>
    private sealed record CreatorShareStep(Guid CreatorId, IResult? Error)
    {
        public static CreatorShareStep Ok(Guid creatorId) => new(creatorId, null);

        public static CreatorShareStep Failed(IResult error) => new(Guid.Empty, error);
    }

    /// <summary>Internal result wrapper for SPE container creation with optional error result.</summary>
    private sealed record SpeContainerCreationResult(string? ContainerId, IResult? Error);

    /// <summary>What a container already recorded on a not-yet-provisioned record turned out to be (task 133).</summary>
    private enum RecordedContainerKind
    {
        /// <summary>No business unit, configured shared container or other root holds it: the record's own — KEPT.</summary>
        Own,

        /// <summary>
        /// A business unit's shared container (the pre-task-076 cascade): unlinked before the move, then replaced; the
        /// business unit keeps it.
        /// </summary>
        BusinessUnit,

        /// <summary>
        /// A container this BFF is configured to use for many records: unlinked before the move, then replaced; the
        /// configuration keeps it.
        /// </summary>
        Configured,

        /// <summary>Recorded on another project, matter or work assignment too: REFUSED before any write.</summary>
        AnotherRecord,

        /// <summary>A read failed: REFUSED before any write, never guessed.</summary>
        Unreadable
    }

    /// <summary>A classified recorded container, with whatever names its holder.</summary>
    private sealed record RecordedContainer(
        RecordedContainerKind Kind,
        Guid? BusinessUnitId = null,
        string? ConfigKey = null,
        SecureRecordRoot? OtherRoot = null,
        Guid? OtherRecordId = null,
        Exception? Fault = null);

    // ── Dataverse row DTOs ────────────────────────────────────────────────

    /// <summary>A business unit holding a container (task 133: the container classification).</summary>
    private sealed class BusinessUnitContainerRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid businessunitid { get; set; }
    }

    /// <summary>The server-stamped creator person (task 133, owner round 7 item 2), read in its own query.</summary>
    private sealed class CreatorPersonRow
    {
        [JsonPropertyName(RecordCreatorPerson.ValueColumn)]
        public Guid? Person { get; set; }
    }

    /// <summary>
    /// The <c>systemuser</c> columns a resume needs to decide whether <c>createdby</c> (or the stamped creator person) is
    /// a usable person.
    /// </summary>
    private sealed class CreatorRow
    {
        [JsonPropertyName("systemuserid")]
        public Guid systemuserid { get; set; }

        [JsonPropertyName("isdisabled")]
        public bool? isdisabled { get; set; }

        [JsonPropertyName("applicationid")]
        public Guid? applicationid { get; set; }
    }

    /// <summary>
    /// The columns Step 1 reads, common to all three roots. The id and name columns differ per table, so the name
    /// is read from <see cref="Extra"/> by the column <see cref="SecureRecordRoot.NameColumn"/> names.
    /// </summary>
    private sealed class RootRow
    {
        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        /// <summary>Task 114: Access Permission (Restricted = 100000002), read only by the colleague step's rule.</summary>
        [JsonPropertyName("sprk_accesspermission")]
        public int? sprk_accesspermission { get; set; }

        /// <summary>
        /// The container recorded on the record. Half of the marker — see <see cref="IsProvisioned"/>; never a marker
        /// on its own.
        /// </summary>
        /// <remarks>Its value is whatever Step 1 read; a shared value unlinked at Step 4.2 is not re-read.</remarks>
        [JsonPropertyName("sprk_containerid")]
        public string? sprk_containerid { get; set; }

        /// <summary>
        /// A per-record security business unit stamped by the RETIRED BU-per-project mechanism.
        /// </summary>
        [JsonPropertyName("_sprk_securitybu_value")]
        public Guid? _sprk_securitybu_value { get; set; }

        /// <summary>The team that owns this record, if it is team-owned.</summary>
        [JsonPropertyName("_owningteam_value")]
        public Guid? _owningteam_value { get; set; }

        /// <summary>The user that owns this record, if it is user-owned (task 133: what compensation restores).</summary>
        [JsonPropertyName("_owninguser_value")]
        public Guid? _owninguser_value { get; set; }

        /// <summary>The business unit the record's owner places it in.</summary>
        [JsonPropertyName("_owningbusinessunit_value")]
        public Guid? _owningbusinessunit_value { get; set; }

        /// <summary>
        /// Who created the record — stamped by Dataverse, not writable by a client. The person a RESUME shares to when
        /// it is a usable person; otherwise the BFF-stamped <c>sprk_createdbyperson</c> (task 133; owner decision F8,
        /// owner round 7 item 2).
        /// </summary>
        [JsonPropertyName("_createdby_value")]
        public Guid? _createdby_value { get; set; }

        /// <summary>Every other column the read returned — the table-specific id and name.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>The record's display name, read from the table's own name column.</summary>
        public string? NameFrom(string nameColumn) =>
            Extra is not null
            && Extra.TryGetValue(nameColumn, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

        /// <summary>The record's id, read from the table's own id column (task 133: the container classification).</summary>
        public Guid? IdFrom(string idColumn) =>
            Extra is not null
            && Extra.TryGetValue(idColumn, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id)
                ? id
                : null;

        /// <summary>The record's owner as a principal: its owning user, else its owning team, else null.</summary>
        public DataversePrincipalRef? Owner =>
            _owninguser_value is { } user && user != Guid.Empty ? DataversePrincipalRef.User(user)
            : _owningteam_value is { } team && team != Guid.Empty ? DataversePrincipalRef.Team(team)
            : null;

        /// <summary>True when this record still references a retired per-record security BU.</summary>
        public bool HasLegacyPerProjectBusinessUnit =>
            _sprk_securitybu_value is { } bu && bu != Guid.Empty;

        /// <summary>Whether this record is owned by <paramref name="ownerTeamId"/> — provisioning reached Step 5.</summary>
        public bool IsOwnedBy(Guid ownerTeamId) =>
            _owningteam_value is { } team && team == ownerTeamId;

        /// <summary>
        /// The idempotency marker: owned by the named owner team AND a container recorded — provisioning reached
        /// Step 7. Owned by the team with NO container means an earlier run stopped after the move, and is resumed.
        /// </summary>
        /// <remarks>
        /// <para><b>Why both halves, and why the container half is now sound.</b> On 2026-08-23 a guard keyed on
        /// <c>sprk_containerid</c> ALONE. But the Create Project wizard then stamped that field on every new project from
        /// the creating user's business unit, so every secure project answered 409 "already provisioned" and none was
        /// ever provisioned — a guard against double-provisioning had become a guard against provisioning. Ownership
        /// replaced it: owned by the named team is state only this endpoint (and the one-time migration script) writes.
        /// Ownership alone, though, cannot tell a finished run from one that stopped after the move, and refusing the
        /// second locked its creator out (C11). Task 076 then made provisioning the ONLY writer of <c>sprk_containerid</c>
        /// on these tables (<c>EntityCreationService</c> deleted the client stamp; <c>RecordCreationService</c> never
        /// writes it; live 2026-10-01: no field-mapping rule targets it) — Step 7 records the record's own container, and
        /// Step 4.2 unlinks a shared one BEFORE the move (task 133 r1). A container Step 6 created without Step 7 is
        /// unrecorded and could never receive content (uploads to a secure record with no container of its own are
        /// refused), so a resume that creates a fresh one orphans at most an empty container its failed run named.</para>
        ///
        /// <para><b>What a recorded container on a team-owned record can be</b> — and why none of them is resumable
        /// here. (1) One Step 7 recorded, after Step 5.5 proved the creator's share: finished. (2) The record's OWN
        /// container, KEPT (task 133 b2: a provisioned record later reassigned away from the team, or unsecured and
        /// secured again, re-provisions without a second container — live 2026-10-02, 65a3fab2 was orphaned before
        /// that). Such a record reaches the team exactly like (1) when the run succeeds; when a step after its move fails,
        /// nothing on the row tells it from a finished one, so the run that failed says so and names the recovery that
        /// works without this call — an administrator's Manage Access share (task 133 r1). It is also moved only once its
        /// creator's share can be set up first, which removes the commonest such failure. Resuming it instead would make
        /// this marker read more than ownership and the container — decided as shipped by owner round 10 item 5 (task 133
        /// note §14.3). (3) A SHARED container never reaches the team: it is unlinked before the move, so every failure
        /// after the move leaves "owned, no container", which is resumed.</para>
        /// </remarks>
        public bool IsProvisioned(Guid ownerTeamId) =>
            IsOwnedBy(ownerTeamId) && !string.IsNullOrWhiteSpace(sprk_containerid);

        /// <summary>
        /// Owned INSIDE the Secure Record BU, but by a team other than the named one (task 144) — the retired
        /// default team, before the migration. Refused before any write: the migration script, not a provisioning call,
        /// moves it onto the named team. "Inside" is <see cref="SecureRecordOwnerTeam.IsInSecureBusinessUnit"/>, the rule the
        /// <c>can-manage-access</c> owner answer uses too (round 53 item 2: the ribbon hides Make Secure on such a record).
        /// </summary>
        public bool IsOwnedInBusinessUnitByAnotherTeam(Guid secureBusinessUnitId, Guid ownerTeamId) =>
            SecureRecordOwnerTeam.IsInSecureBusinessUnit(_owningbusinessunit_value, secureBusinessUnitId) == true
            && !IsOwnedBy(ownerTeamId);
    }
}
