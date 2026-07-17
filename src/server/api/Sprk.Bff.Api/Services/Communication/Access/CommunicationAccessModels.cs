using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// The caller identity + attributes the <see cref="ICommunicationAccessFilter"/> composes access from
/// (FR-08 / NFR-06). Built by the read endpoint (task 050) from the request's authenticated principal
/// (Azure AD <c>oid</c> → Dataverse <c>systemuserid</c> via <c>azureactivedirectoryobjectid</c>, ADR-028)
/// and passed to the filter. The filter NEVER trusts a client-supplied identity — the endpoint resolves it
/// server-side (D-07: the BFF is the sole policy-enforcement point).
/// <para>
/// <b>Access model (owner decision 2026-07-16 — <c>notes/access-model-decision.md</c>):</b> record-level read
/// access is enforced by <b>Dataverse impersonation</b> (the endpoint issues the thread-message query with the
/// <c>MSCRMCallerID</c> header = this caller's <see cref="CallerSystemUserId"/>, so Dataverse returns only the
/// rows the caller may see — honoring ALL access sources: ownership, role depth, BU, teams, sharing, hierarchy).
/// The filter therefore NO LONGER hand-computes membership ∪ grants; it applies only the two Spaarke business
/// rules impersonation does not cover (internal-only + privilege) ON TOP of the already-impersonated rows.
/// </para>
/// </summary>
/// <param name="CallerSystemUserId">
/// The caller's Dataverse <c>systemuserid</c>. This is the value the endpoint sends as the <c>MSCRMCallerID</c>
/// impersonation header on the record-read query (see <c>DataverseImpersonation</c> /
/// <c>DataverseWebApiService.RetrieveMultipleImpersonatedAsync</c>). <see cref="System.Guid.Empty"/> means
/// "unresolved" — the endpoint MUST NOT issue an un-impersonated (app-only) query for that caller (fail closed).
/// </param>
/// <param name="IsInternalUser">
/// The D-05 internal-user attribute. Internal-only messages (<c>sprk_isinternalonly = true</c>) are invisible
/// to non-internal callers (external participants arrive in R2/R3). In R1 the polling timeline is used by
/// internal <c>systemuser</c> callers, so this is <c>true</c>; external (contact) callers get <c>false</c>.
/// </param>
/// <param name="CallerContactId">
/// The caller's <c>contactid</c> when the caller is (also) an external contact (B2B guest). Optional in R1;
/// present so a future R2 contact-scoped path can match a contact principal without a contract change.
/// </param>
public sealed record CommunicationAccessContext(
    Guid CallerSystemUserId,
    bool IsInternalUser,
    Guid? CallerContactId = null);

/// <summary>
/// Privilege classification (<c>sprk_communication.sprk_privilegeclassification</c> — task 005 schema). This is
/// classification METADATA a human owns. Per ADR-015 the AI may FLAG a message as potentially privileged, but it
/// NEVER decides access — the filter reads the field and NEVER calls AI at read time. Per the owner decision
/// (2026-07-16) privilege NEVER gates a read on its own; it rides along in the
/// <see cref="CommunicationAccessDecision"/> as metadata (task 050 may surface a privilege badge / drive review).
/// </summary>
public enum CommunicationPrivilegeClassification
{
    /// <summary>None (100000000) — not privileged.</summary>
    None = 100000000,

    /// <summary>Potentially Privileged (100000001) — an AI/human FLAG for review; never an access decision (ADR-015).</summary>
    PotentiallyPrivileged = 100000001,

    /// <summary>Privileged (100000002) — human-confirmed classification metadata.</summary>
    Privileged = 100000002,
}

/// <summary>
/// The access decision for a single <c>sprk_communication</c> row that Dataverse already returned to the
/// impersonated caller. <see cref="IsVisible"/> is the load-bearing bit; <see cref="DenyReason"/> is diagnostic
/// (NEVER leaked to the client as content). <see cref="Privilege"/> is carried as composed metadata for the
/// visible rows (task 050 may surface a privilege badge) — it did NOT gate the read.
/// <para>
/// The ONLY reason the filter denies a row Dataverse returned is the <c>internal-only</c> Spaarke business rule
/// (D-05): a non-internal caller must not see an <c>sprk_isinternalonly</c> message even if Dataverse record
/// security let them read the row.
/// </para>
/// </summary>
public sealed record CommunicationAccessDecision(
    bool IsVisible,
    string? DenyReason,
    CommunicationPrivilegeClassification Privilege)
{
    public static CommunicationAccessDecision Deny(string reason) =>
        new(false, reason, CommunicationPrivilegeClassification.None);

    public static CommunicationAccessDecision Allow(CommunicationPrivilegeClassification privilege) =>
        new(true, null, privilege);
}

/// <summary>
/// The result of applying the internal-only + privilege business rules to a thread's IMPERSONATED message set
/// (the rows Dataverse already scoped to the caller): the visible rows plus every per-row decision (task 050
/// reuses the decisions for the unread count over the SAME filtered set).
/// </summary>
/// <param name="VisibleMessages">The subset of input messages the caller may read, input order preserved.</param>
/// <param name="Decisions">Every input message paired with its decision (visible + hidden), input order preserved.</param>
public sealed record CommunicationAccessResult(
    IReadOnlyList<Entity> VisibleMessages,
    IReadOnlyList<(Entity Message, CommunicationAccessDecision Decision)> Decisions);
