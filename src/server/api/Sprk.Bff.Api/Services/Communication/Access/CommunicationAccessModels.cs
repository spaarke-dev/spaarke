using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// The caller identity + attributes the <see cref="ICommunicationAccessFilter"/> composes access from
/// (FR-08 / NFR-06). Built by the read endpoint (task 050) from the request's authenticated principal
/// (Azure AD <c>oid</c> → Dataverse <c>systemuserid</c> via <c>azureactivedirectoryobjectid</c>, ADR-028)
/// and passed to the filter. The filter NEVER trusts a client-supplied identity — the endpoint resolves it
/// server-side (D-07: the BFF is the sole policy-enforcement point).
/// </summary>
/// <param name="CallerSystemUserId">
/// The caller's Dataverse <c>systemuserid</c>. Drives ADR-034 open-thread membership and per-record grant
/// matching. <see cref="System.Guid.Empty"/> means "unresolved" — the filter treats it as no membership and
/// no grant (fail closed).
/// </param>
/// <param name="IsInternalUser">
/// The D-05 internal-user attribute. Internal-only messages (<c>sprk_isinternalonly = true</c>) are invisible
/// to non-internal callers (external participants arrive in R2/R3). In R1 the polling timeline is used by
/// internal <c>systemuser</c> callers, so this is <c>true</c>; external (contact) callers get <c>false</c>.
/// </param>
/// <param name="CallerContactId">
/// The caller's <c>contactid</c> when the caller is (also) an external contact (B2B guest). Optional in R1;
/// present so a future R2 contact-scoped grant provider can match grants keyed to a contact principal without
/// a contract change.
/// </param>
public sealed record CommunicationAccessContext(
    Guid CallerSystemUserId,
    bool IsInternalUser,
    Guid? CallerContactId = null);

/// <summary>
/// Thread-level privacy state (<c>sprk_communicationthread.sprk_privacystate</c> — task 004 schema). Integers
/// match the as-built option set exactly. Any UNKNOWN integer maps to <see cref="Private"/> (fail closed).
/// </summary>
public enum CommunicationPrivacyState
{
    /// <summary>Open (100000000) — visibility derives from ADR-034 open-thread membership on the anchor record.</summary>
    Open = 100000000,

    /// <summary>Private (100000001) — visibility requires an explicit per-record overlay grant (point-forward).</summary>
    Private = 100000001,
}

/// <summary>
/// Privilege classification (<c>sprk_communication.sprk_privilegeclassification</c> — task 005 schema). This is
/// classification METADATA a human owns. Per ADR-015 the AI may FLAG a message as potentially privileged, but it
/// NEVER decides access — the filter reads the field and NEVER calls AI at read time. Privilege composes with
/// privacy (it rides along in the <see cref="CommunicationAccessDecision"/>); it does not itself grant or deny a
/// read — privacy + internal-only are the read gate (per design §5).
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
/// The per-request, per-(thread, caller) access basis: everything needed to decide EVERY message on a thread
/// without re-querying Dataverse per row. Resolved ONCE by
/// <see cref="ICommunicationAccessFilter.ResolveThreadAccessBasisAsync"/>. This is the SHARED derivation task
/// 041's ACS-membership reconcile projects onto ACS — both derive from the SAME union
/// (<c>MembershipResolver(anchor) ∪ activeOverlayGrants(thread)</c>); neither may exceed Dataverse-derived access.
/// </summary>
/// <param name="ThreadId">The thread these messages belong to.</param>
/// <param name="ThreadLoaded">
/// <c>false</c> when the thread record could not be loaded (missing / query error). The filter then DENIES every
/// message on the thread — fail closed (NFR-06). Never let a load failure open access.
/// </param>
/// <param name="PrivacyState">The thread's current privacy state (drives point-forward — see the filter).</param>
/// <param name="PrivacyEffectiveFrom">
/// <c>sprk_privacyeffectivefrom</c> — the moment the thread was last flipped to Private (or opened). Drives the
/// point-forward switch. <c>null</c> means "never flipped": a born-Open thread is fully open; a Private thread
/// with a null effective-from is treated as wholly private (fail closed).
/// </param>
/// <param name="CallerHasOpenMembership">
/// <c>true</c> when the caller is in the ADR-034 membership set of the thread's anchor record (i.e., can read the
/// anchor). Gates OPEN messages. <c>false</c> for anchorless (Direct 1:1) threads — those are visible only to
/// grant-holders.
/// </param>
/// <param name="CallerGrants">
/// The caller's ACTIVE overlay grants for this thread (option B, owner-approved 2026-07-16). Each grant carries an
/// <c>EffectiveFrom</c> that gates private messages point-forward (<c>message.createdon &gt;= grant.EffectiveFrom</c>).
/// Empty when the caller has no grant — private messages then fail closed.
/// </param>
public sealed record ThreadAccessBasis(
    Guid ThreadId,
    bool ThreadLoaded,
    CommunicationPrivacyState PrivacyState,
    DateTimeOffset? PrivacyEffectiveFrom,
    bool CallerHasOpenMembership,
    IReadOnlyList<ThreadPrivateGrant> CallerGrants)
{
    /// <summary>A fail-closed basis (thread not loaded) — denies every message.</summary>
    public static ThreadAccessBasis Denied(Guid threadId) => new(
        threadId,
        ThreadLoaded: false,
        PrivacyState: CommunicationPrivacyState.Private,
        PrivacyEffectiveFrom: null,
        CallerHasOpenMembership: false,
        CallerGrants: Array.Empty<ThreadPrivateGrant>());
}

/// <summary>
/// The access decision for a single <c>sprk_communication</c> row. <see cref="IsVisible"/> is the load-bearing
/// bit; <see cref="DenyReason"/> is diagnostic (NEVER leaked to the client as content). <see cref="Privilege"/>
/// is carried as composed metadata for the visible rows (task 050 may surface a privilege badge) — it did NOT
/// gate the read.
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
/// The result of filtering a thread's message set: the visible rows (default-deny — a row is present only if it
/// passed EVERY dimension) plus every per-row decision (task 050 reuses the decisions for the unread count over
/// the SAME access-filtered set).
/// </summary>
/// <param name="VisibleMessages">The subset of input messages the caller may read, input order preserved.</param>
/// <param name="Decisions">Every input message paired with its decision (visible + hidden), input order preserved.</param>
public sealed record CommunicationAccessResult(
    IReadOnlyList<Entity> VisibleMessages,
    IReadOnlyList<(Entity Message, CommunicationAccessDecision Decision)> Decisions);
