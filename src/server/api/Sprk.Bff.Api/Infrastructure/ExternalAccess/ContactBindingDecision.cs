// unified-access-control-r2 task 141 (defect C7, owner round 2 item 4) — THE identity-binding decision.
//
// One pure decision shared by every path that binds a person to a Dataverse contact:
//   • the workforce token plane (a customer employee with no Power Apps licence — "Type 2"),
//   • the CIAM token plane (an invited external user; the email bind is kept only as the invite-repair path),
//   • the systemuser plane (a licensed user linked without a token: the reconciliation job, registration,
//     and the inline link at first resolution),
//   • the invite path (which never binds, but must refuse a contact another plane already owns).
//
// It grows out of task 013's DecideWorkforceEmailMatch / ReadOidBinding (A-18), which this file supersedes:
// that decision compared a column that does not exist in dev (contact.azureactivedirectoryobjectid) and
// never wrote a binding, so an unbound contact stayed hijackable forever. Here the binding key is the Entra
// oid in contact.sprk_externalobjectid — ONE field for both planes (SPA-r2 FR-11) — and the plane that wrote
// it is recorded in contact.sprk_identityplane.
//
// PUBLIC on purpose. Task 013 exercised its decision through InternalsVisibleTo, which ADR-038 §7 ban B8
// forbids; making the decision a public, side-effect-free contract removes that deviation instead of copying
// it (CLAUDE.md §6.5 path C). Nothing here performs I/O — ContactIdentityBinder does the reads and writes.
//
// The decision table this implements, with every negative case, is in
// projects/unified-access-control-r2/notes/task-141-identity-binding.md §3.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Which plane wrote a contact's <c>sprk_externalobjectid</c> (the <c>sprk_identityplane</c> choice).
/// The integer values are the Dataverse option values.
/// </summary>
public enum IdentityPlaneMarker
{
    /// <summary>An Entra External ID (CIAM) object id — written by the invite or the CIAM first login.</summary>
    External = 100000000,

    /// <summary>A workforce Entra object id — a Type-2 employee or a licensed systemuser.</summary>
    Workforce = 100000001,
}

/// <summary>
/// Why a binding was refused and flagged (the <c>sprk_identitycollisionreason</c> choice). The integer
/// values are the Dataverse option values.
/// </summary>
public enum IdentityCollisionReason
{
    /// <summary>The matched contact is already bound to a different oid.</summary>
    BoundToDifferentOid = 100000000,

    /// <summary>More than one active contact carries the email.</summary>
    EmailAmbiguous = 100000001,

    /// <summary>More than one contact carries the oid.</summary>
    OidOnMultipleContacts = 100000002,

    /// <summary>The contact is another person's <c>sprk_primarycontact</c> (or, on the CIAM plane, anyone's).</summary>
    LinkedToOtherUser = 100000003,

    /// <summary>The contact's binding cannot be read: malformed, all-zero, or a plane marker with no oid.</summary>
    BindingUnreadable = 100000004,

    /// <summary>A B2B-guest systemuser whose email matches an existing contact. Guests are never email-bound.</summary>
    GuestEmailMatch = 100000005,

    /// <summary>A licensed user's existing link points at a contact bound to a different oid.</summary>
    LinkedContactBoundToDifferentOid = 100000006,

    /// <summary>A licensed user's link points at one contact while their oid is bound to another.</summary>
    LinkedContactMismatch = 100000007,

    /// <summary>A licensed user's linked contact is inactive.</summary>
    LinkedContactInactive = 100000008,

    /// <summary>A B2B-guest systemuser linked to an unbound contact — not bound without an oid match.</summary>
    GuestLinkUnverified = 100000009,

    /// <summary>An invite whose email matches a workforce-bound contact or one a systemuser links to.</summary>
    InviteMatchesWorkforceContact = 100000010,

    /// <summary>
    /// The uniqueness mirror (<c>sprk_externalobjectidkey</c>) carries the colliding oid on THIS contact, but its
    /// binding (<c>sprk_externalobjectid</c>) does not — a squatted or half-cleared mirror. The platform's unique
    /// index then refuses to bind or create that oid anywhere else (owner round 4 item 4, B2). Flagged on the
    /// holder so an operator can see it; the caller is denied <c>contact_key_conflict</c>.
    /// </summary>
    KeyMirrorConflict = 100000011,
}

/// <summary>Which caller plane a binding decision is being made for.</summary>
public enum BindingPlane
{
    /// <summary>A workforce Entra token for a caller with no systemuser (Teams / SPA).</summary>
    WorkforceToken,

    /// <summary>An Entra External ID (CIAM) token.</summary>
    CiamToken,

    /// <summary>A licensed systemuser, linked without a token (job, registration, inline link).</summary>
    SystemUser,
}

/// <summary>What a caller is allowed to do beyond resolving through an existing oid binding.</summary>
public enum BindingEligibility
{
    /// <summary>Resolve through an existing oid binding only.</summary>
    None,

    /// <summary>May email-bind an unbound contact; never creates (the CIAM plane).</summary>
    EmailBindOnly,

    /// <summary>May email-bind, and creates a contact keyed by the oid when nothing matches.</summary>
    EmailBindAndCreate,

    /// <summary>Never email-binds; creates only when no active contact carries the email (a guest systemuser).</summary>
    CreateUnlessEmailMatches,
}

/// <summary>The three states of a contact's oid binding.</summary>
public enum BindingKind
{
    /// <summary>Nobody's yet: no oid and no plane marker.</summary>
    Unbound,

    /// <summary>Bound to a readable oid.</summary>
    Bound,

    /// <summary>Somebody may own it but we cannot tell who. Never treated as unbound.</summary>
    Unreadable,
}

/// <summary>A contact's binding as read from one row.</summary>
public readonly record struct BindingState(BindingKind Kind, Guid? Oid, IdentityPlaneMarker? Plane)
{
    /// <summary>The unbound state.</summary>
    public static BindingState Unbound { get; } = new(BindingKind.Unbound, null, null);

    /// <summary>The unreadable state.</summary>
    public static BindingState Unreadable { get; } = new(BindingKind.Unreadable, null, null);
}

/// <summary>
/// One identity's collision with a contact: who collided (oid and plane), why, and when. An invite refusal has
/// no oid (the CIAM account was never created).
/// </summary>
public sealed record CollisionParty(
    Guid? Oid,
    IdentityPlaneMarker? Plane,
    IdentityCollisionReason? Reason,
    DateTimeOffset FlaggedOn)
{
    /// <summary>
    /// The same identity colliding for the same reason. This is the idempotence key: a party already recorded
    /// is never written again, however often that identity retries a denied sign-in.
    /// </summary>
    public bool IsSameCollision(CollisionParty other)
        => other is not null && Oid == other.Oid && Plane == other.Plane && Reason == other.Reason;
}

/// <summary>
/// A durable, operator-visible collision flag on a contact. The four summary columns
/// (<c>sprk_identitycollisionon / oid / plane / reason</c>) carry the FIRST party, which is what the operator's
/// "Contacts with Identity Collisions" view lists; <c>sprk_identitycollisionparties</c> carries EVERY party.
/// </summary>
/// <remarks>
/// <b>Why more than one party</b> (task 141 verifier finding 3): with one slot, the second identity to collide
/// with an already-flagged contact was never recorded. When the first party's collision was resolved the job
/// cleared the flag, and the second collision — still live — vanished from the operator's view until that
/// identity happened to sign in again. Every party is now recorded, and the flag is cleared only when none of
/// them still collides.
/// </remarks>
public sealed record CollisionFlag(
    DateTimeOffset FlaggedOn,
    Guid? CollidingOid,
    IdentityPlaneMarker? CollidingPlane,
    IdentityCollisionReason? Reason)
{
    /// <summary>The parties after the first, oldest first.</summary>
    public IReadOnlyList<CollisionParty> OtherParties { get; init; } = Array.Empty<CollisionParty>();

    /// <summary>
    /// True when the parties column is present but cannot be read (not the shape the BFF writes, or not
    /// starting with the summary's party — someone edited it). Such a flag may record parties we cannot see,
    /// so it is never overwritten and never cleared by the job; an operator resolves it.
    /// </summary>
    public bool HasUnreadableParties { get; init; }

    /// <summary>The first party — the four summary columns.</summary>
    public CollisionParty Primary => new(CollidingOid, CollidingPlane, Reason, FlaggedOn);

    /// <summary>Every recorded party, the first one first.</summary>
    public IReadOnlyList<CollisionParty> Parties => new[] { Primary }.Concat(OtherParties).ToList();

    /// <summary>True when <paramref name="party"/>'s collision is already recorded.</summary>
    public bool Records(CollisionParty party) => Parties.Any(p => p.IsSameCollision(party));

    /// <summary>A flag whose parties are <paramref name="parties"/> (the first becomes the summary); null for none.</summary>
    public static CollisionFlag? FromParties(IReadOnlyList<CollisionParty> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);
        if (parties.Count == 0) return null;
        var first = parties[0];
        return new CollisionFlag(first.FlaggedOn, first.Oid, first.Plane, first.Reason)
        {
            OtherParties = parties.Skip(1).ToList(),
        };
    }
}

/// <summary>What the reconciliation job does with one open flag.</summary>
public enum FlagReconciliationAction
{
    /// <summary>Every recorded party still collides (or the evidence is incomplete): leave the flag as it is.</summary>
    Keep,

    /// <summary>Some parties no longer collide: rewrite the flag with the ones that still do.</summary>
    Prune,

    /// <summary>No party collides any more: clear the flag.</summary>
    Clear,
}

/// <summary>The reconciliation job's answer for one open flag.</summary>
public sealed record FlagReconciliation(FlagReconciliationAction Action, CollisionFlag? Remaining = null);

/// <summary>One contact row as the binding decision needs it. Raw column values, interpreted by the decision.</summary>
/// <param name="ContactId">The contact.</param>
/// <param name="StateCode"><c>statecode</c>; only 0 is active. Absent is NOT active (fail closed).</param>
/// <param name="RawOid"><c>sprk_externalobjectid</c> exactly as stored, or null.</param>
/// <param name="RawPlane"><c>sprk_identityplane</c> option value, or null.</param>
/// <param name="ETag">The row version, used as the <c>If-Match</c> precondition for a bind.</param>
/// <param name="Email"><c>emailaddress1</c>, used by the reconciliation job's flag re-evaluation.</param>
/// <param name="Flag">The open collision flag, or null.</param>
/// <param name="RawKeyMirror">
/// <c>sprk_externalobjectidkey</c> exactly as stored, or null — the UNSECURED uniqueness mirror (owner round 4
/// item 4, B2). It carries no identity: nothing resolves or binds by it. It is read only to tell a squatted or
/// half-cleared mirror apart (<see cref="ContactBindingDecision.MirrorHeldWithoutBinding"/>).
/// </param>
public sealed record ContactBindingRow(
    Guid ContactId,
    int? StateCode,
    string? RawOid,
    int? RawPlane,
    string? ETag = null,
    string? Email = null,
    CollisionFlag? Flag = null,
    string? RawKeyMirror = null)
{
    /// <summary>Only <c>statecode = 0</c> resolves or binds (ADR-003).</summary>
    public bool IsActive => StateCode == 0;

    /// <summary>The binding, read by <see cref="ContactBindingDecision.ReadBinding"/>.</summary>
    public BindingState Binding => ContactBindingDecision.ReadBinding(RawOid, RawPlane);
}

/// <summary>How a lookup ended. Anything but <see cref="Read"/> denies.</summary>
public enum LookupStatus
{
    /// <summary>The query ran; its rows (possibly none) are the answer.</summary>
    Read,

    /// <summary>A non-success status, an exception or a timeout. Never collapsed into "no rows".</summary>
    Failed,

    /// <summary>A binding column is not provisioned in this environment. Its own deny code.</summary>
    ColumnMissing,
}

/// <summary>A contact lookup: rows, or why there are none to trust.</summary>
public sealed record ContactLookup(LookupStatus Status, IReadOnlyList<ContactBindingRow> Rows)
{
    /// <summary>A lookup that could not be read.</summary>
    public static ContactLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<ContactBindingRow>());

    /// <summary>A lookup whose binding column does not exist here.</summary>
    public static ContactLookup ColumnMissing { get; } = new(LookupStatus.ColumnMissing, Array.Empty<ContactBindingRow>());

    /// <summary>A lookup that read these rows.</summary>
    public static ContactLookup Of(params ContactBindingRow[] rows) => new(LookupStatus.Read, rows);
}

/// <summary>A systemuser whose <c>sprk_primarycontact</c> points at a contact under consideration.</summary>
public sealed record SystemUserReference(Guid SystemUserId, Guid? Oid, Guid LinkedContactId);

/// <summary>The systemusers linking to a set of contacts.</summary>
public sealed record ReferenceLookup(LookupStatus Status, IReadOnlyList<SystemUserReference> References)
{
    /// <summary>A reference lookup that could not be read.</summary>
    public static ReferenceLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<SystemUserReference>());

    /// <summary>A reference lookup that read these rows.</summary>
    public static ReferenceLookup Of(params SystemUserReference[] references) => new(LookupStatus.Read, references);
}

/// <summary>The facts of one binding request, as the calling plane establishes them.</summary>
/// <param name="Plane">Which plane is asking.</param>
/// <param name="CallerOid">The caller's oid, already parsed. <see cref="Guid.Empty"/> = unusable.</param>
/// <param name="Email">The plane's email: the token claim, or <c>systemuser.internalemailaddress</c>.</param>
/// <param name="Eligibility">What the caller may do beyond resolving by oid.</param>
/// <param name="IneligibleDenyCode">The deny code when eligibility is <see cref="BindingEligibility.None"/>.</param>
public sealed record BindingRequest(
    BindingPlane Plane,
    Guid CallerOid,
    string? Email,
    BindingEligibility Eligibility,
    string? IneligibleDenyCode = null)
{
    /// <summary>The plane marker this caller's oid belongs to.</summary>
    public IdentityPlaneMarker CallerPlane =>
        Plane == BindingPlane.CiamToken ? IdentityPlaneMarker.External : IdentityPlaneMarker.Workforce;
}

/// <summary>The outcome of one decision step.</summary>
public enum BindingAction
{
    /// <summary>Resolve to <see cref="BindingDecision.ContactId"/>. Email was never consulted, or confirms it.</summary>
    ResolveByOid,

    /// <summary>Write the caller's oid onto the unbound <see cref="BindingDecision.ContactId"/>, then resolve.</summary>
    BindByEmail,

    /// <summary>Write the systemuser's oid onto its already-linked, unbound contact. The link is unchanged.</summary>
    BindLinkedContact,

    /// <summary>Create one contact keyed by the oid, then resolve.</summary>
    CreateByOid,

    /// <summary>The systemuser's existing link is verified: the linked contact carries their oid.</summary>
    LinkVerified,

    /// <summary>Refused, with a deny code. Nothing is written.</summary>
    Deny,

    /// <summary>Refused, with a deny code, and the listed contacts get a durable collision flag.</summary>
    Collision,

    /// <summary>The decision needs the email lookup before it can finish.</summary>
    NeedEmailLookup,

    /// <summary>The decision needs the systemusers linking the candidate before it can finish.</summary>
    NeedReferenceLookup,

    /// <summary>The decision needs the systemuser's linked contact row before it can finish.</summary>
    NeedLinkedContact,
}

/// <summary>One decision step's answer.</summary>
public sealed record BindingDecision(
    BindingAction Action,
    Guid? ContactId = null,
    string? DenyCode = null,
    IdentityCollisionReason? Reason = null,
    IReadOnlyList<Guid>? FlagContactIds = null)
{
    /// <summary>True for an outcome that ends the decision.</summary>
    public bool IsTerminal => Action is not (BindingAction.NeedEmailLookup or BindingAction.NeedReferenceLookup
        or BindingAction.NeedLinkedContact);
}

/// <summary>How the invite path must treat the contact its email resolves to.</summary>
public enum InviteContactAction
{
    /// <summary>No active contact carries the email: create one, then provision.</summary>
    CreateContact,

    /// <summary>One unbound active contact: provision onto it.</summary>
    ProvisionExisting,

    /// <summary>One contact already bound on the CIAM plane: today's idempotent "AlreadyProvisioned".</summary>
    AlreadyProvisioned,

    /// <summary>Refused with HTTP 409 and a reason code; the listed contacts are flagged.</summary>
    Refuse,

    /// <summary>The lookup could not be read. Not a refusal — a failure (ProblemDetails, never a bare 500).</summary>
    Fail,

    /// <summary>The decision needs the systemusers linking the candidate.</summary>
    NeedReferenceLookup,
}

/// <summary>The invite path's answer.</summary>
public sealed record InviteDecision(
    InviteContactAction Action,
    Guid? ContactId = null,
    string? ReasonCode = null,
    IdentityCollisionReason? Reason = null,
    IReadOnlyList<Guid>? FlagContactIds = null);

/// <summary>
/// The single identity-binding decision. Pure: every input is a value, every output is a value.
/// </summary>
public static class ContactBindingDecision
{
    // ── Deny codes (auth.md: {domain}.{area}.{action}.{reason}) ─────────────────────────────────────
    // Each deny has its OWN code: on every plane the HTTP answer is the same 403, so the code is the only
    // thing in the audit trail that tells an outage from a hijack attempt from an ordinary non-contact.

    /// <summary>The caller's own oid is unusable.</summary>
    public const string DenyUnidentifiableCaller = "sdap.access.deny.unidentifiable_caller";

    /// <summary>A binding lookup could not be read (status, exception, timeout).</summary>
    public const string DenyContactLookupFailed = "sdap.access.deny.contact_lookup_failed";

    /// <summary>A binding column is not provisioned in this environment.</summary>
    public const string DenyBindingColumnMissing = "sdap.access.deny.binding_column_missing";

    /// <summary>The oid's only contact is inactive. Deactivation is how an operator removes a person.</summary>
    public const string DenyContactInactive = "sdap.access.deny.contact_inactive";

    /// <summary>More than one contact carries the oid.</summary>
    public const string DenyContactOidAmbiguous = "sdap.access.deny.contact_oid_ambiguous";

    /// <summary>More than one active contact carries the email.</summary>
    public const string DenyContactEmailAmbiguous = "sdap.access.deny.contact_email_ambiguous";

    /// <summary>The email matched a contact bound to a different oid (the A-18 hijack).</summary>
    public const string DenyContactBoundToDifferentOid = "sdap.access.deny.contact_bound_to_different_oid";

    /// <summary>The matched contact's binding cannot be read.</summary>
    public const string DenyContactBindingUnreadable = "sdap.access.deny.contact_binding_unreadable";

    /// <summary>The matched contact is another person's <c>sprk_primarycontact</c>.</summary>
    public const string DenyContactLinkedToOtherUser = "sdap.access.deny.contact_linked_to_other_user";

    /// <summary>A guest systemuser's email matched an existing contact.</summary>
    public const string DenyGuestEmailMatch = "sdap.access.deny.contact_guest_email_match";

    /// <summary>No contact, and this plane may not create one (the CIAM plane's long-standing code).</summary>
    public const string DenyContactNotFound = "sdap.access.deny.contact_not_found";

    /// <summary>A licensed user's link points at a contact bound to another oid.</summary>
    public const string DenyLinkedContactBoundToDifferentOid = "sdap.access.deny.linked_contact_bound_to_different_oid";

    /// <summary>A licensed user's link and their oid binding name different contacts.</summary>
    public const string DenyLinkedContactMismatch = "sdap.access.deny.linked_contact_mismatch";

    /// <summary>A licensed user's linked contact is inactive.</summary>
    public const string DenyLinkedContactInactive = "sdap.access.deny.linked_contact_inactive";

    /// <summary>A licensed user's link points at a contact that no longer exists.</summary>
    public const string DenyLinkedContactMissing = "sdap.access.deny.linked_contact_missing";

    /// <summary>A guest systemuser is linked to an unbound contact.</summary>
    public const string DenyGuestLinkUnverified = "sdap.access.deny.guest_link_unverified";

    /// <summary>The bind write failed and a re-read did not show the caller bound.</summary>
    public const string DenyContactBindFailed = "sdap.access.deny.contact_bind_failed";

    /// <summary>
    /// Creation needs the alternate key on the uniqueness mirror (<c>sprk_externalobjectidkey</c>), which is not
    /// defined here — so exactly-one-contact-per-oid cannot be guaranteed and nothing is created.
    /// </summary>
    public const string DenyContactCreateUnavailable = "sdap.access.deny.contact_create_unavailable";

    /// <summary>The create write failed.</summary>
    public const string DenyContactCreateFailed = "sdap.access.deny.contact_create_failed";

    /// <summary>The binding column reads as masked (field-level security without Read): no write is safe.</summary>
    public const string DenyBindingColumnMasked = "sdap.access.deny.binding_column_masked";

    /// <summary>
    /// The uniqueness mirror carries the caller's oid on a contact whose binding does not: the platform's unique
    /// index refuses the bind or the create. A squatted or half-cleared mirror — flagged on the holder, never bound
    /// around (owner round 4 item 4, B2).
    /// </summary>
    public const string DenyContactKeyConflict = "sdap.access.deny.contact_key_conflict";

    // ── Invite reason codes ─────────────────────────────────────────────────────────────────────────

    /// <summary>The invite email matches a contact bound on the workforce plane.</summary>
    public const string InviteWorkforceBoundContact = "sdap.access.invite.workforce_bound_contact";

    /// <summary>The invite email matches a contact a systemuser links to.</summary>
    public const string InviteContactLinkedToInternalUser = "sdap.access.invite.contact_linked_to_internal_user";

    /// <summary>More than one active contact carries the invite email.</summary>
    public const string InviteEmailAmbiguous = "sdap.access.invite.email_ambiguous";

    /// <summary>The matched contact's binding cannot be read.</summary>
    public const string InviteContactBindingUnreadable = "sdap.access.invite.contact_binding_unreadable";

    /// <summary>The invite's contact lookup could not be read.</summary>
    public const string InviteContactLookupFailed = "sdap.access.invite.contact_lookup_failed";

    /// <summary>
    /// Reads a contact's binding as Unbound / Bound / Unreadable from its raw column values.
    /// </summary>
    /// <remarks>
    /// <para><b>A plane marker with no oid is UNREADABLE, never unbound.</b> Once
    /// <c>sprk_externalobjectid</c> is field-secured, a reader without FLS Read gets it back as null — in a
    /// returned row AND inside a filter, where Dataverse substitutes null. A bound contact would then look
    /// unbound and be handed to whoever's email matched. <c>sprk_identityplane</c> is not secured, and every
    /// BFF write sets both columns together, so "marker present, oid absent" is the detectable signature of
    /// masking (or of an operator who cleared one column and not the other). Either way it denies.</para>
    /// <para><b>An oid with no marker is a pre-141 CIAM binding.</b> Every writer before this task was the
    /// CIAM path (verified 2026-09-30: 6 of 6), and the schema step backfills them as External. Reading it as
    /// External keeps today's invite idempotency for those rows if the backfill has not run yet.</para>
    /// <para>Oids are compared as parsed <see cref="Guid"/>s, never as strings; an all-zero oid is anomalous
    /// data, not "not yet bound".</para>
    /// </remarks>
    public static BindingState ReadBinding(string? rawOid, int? rawPlane)
    {
        IdentityPlaneMarker? plane;
        if (rawPlane is null)
        {
            plane = null;
        }
        else if (Enum.IsDefined(typeof(IdentityPlaneMarker), rawPlane.Value))
        {
            plane = (IdentityPlaneMarker)rawPlane.Value;
        }
        else
        {
            // A marker value this code does not know. Somebody wrote it; we cannot say who owns the row.
            return BindingState.Unreadable;
        }

        if (string.IsNullOrWhiteSpace(rawOid))
        {
            return plane is null ? BindingState.Unbound : BindingState.Unreadable;
        }

        if (!Guid.TryParse(rawOid.Trim(), out var oid) || oid == Guid.Empty)
        {
            return BindingState.Unreadable;
        }

        return new BindingState(BindingKind.Bound, oid, plane ?? IdentityPlaneMarker.External);
    }

    /// <summary>
    /// True when <paramref name="domainName"/> marks a B2B guest (<c>#EXT#</c> in the user principal name).
    /// Used ONLY on the systemuser plane, where it is directory-synced; never to infer membership from a token.
    /// </summary>
    public static bool IsGuestDomainName(string? domainName)
        => !string.IsNullOrEmpty(domainName) && domainName.Contains("#EXT#", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The unlinked flow (Workforce token, CIAM token, unlinked systemuser). Returns a <c>Need…</c> action when
    /// a further lookup is required; call again with it supplied.
    /// </summary>
    public static BindingDecision Decide(
        BindingRequest request,
        ContactLookup oidLookup,
        ContactLookup? emailLookup = null,
        ReferenceLookup? references = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(oidLookup);

        // D0 — a caller we cannot name cannot be shown to own anything.
        if (request.CallerOid == Guid.Empty)
        {
            return Deny(DenyUnidentifiableCaller);
        }

        // D1..D4 — the oid binding is authoritative. Could-not-read DENIES; it never falls through.
        var oidOutcome = DecideOnOid(oidLookup);
        if (oidOutcome is not null)
        {
            return oidOutcome;
        }

        // E0 — no oid binding. Only an eligible caller goes any further.
        if (request.Eligibility == BindingEligibility.None)
        {
            return Deny(request.IneligibleDenyCode ?? DenyContactNotFound);
        }

        var mayCreate = request.Eligibility is BindingEligibility.EmailBindAndCreate
            or BindingEligibility.CreateUnlessEmailMatches;

        // E1 — no email to match on. Never emit an empty-email filter: it would match every contact with none.
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return mayCreate ? new BindingDecision(BindingAction.CreateByOid) : Deny(DenyContactNotFound);
        }

        if (emailLookup is null)
        {
            return new BindingDecision(BindingAction.NeedEmailLookup);
        }

        // E2
        if (emailLookup.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(emailLookup.Status));
        }

        // Only ACTIVE rows count; the store filters server-side and this re-checks, because the filter is a
        // bound on volume and the decision is what is applied.
        var rows = emailLookup.Rows.Where(r => r.IsActive && r.ContactId != Guid.Empty).ToList();

        // E3 — picking one of several is a coin-flip over whose grants the caller inherits.
        if (rows.Count > 1)
        {
            return Collide(IdentityCollisionReason.EmailAmbiguous, DenyContactEmailAmbiguous,
                rows.Select(r => r.ContactId));
        }

        // E4
        if (rows.Count == 0)
        {
            return mayCreate ? new BindingDecision(BindingAction.CreateByOid) : Deny(DenyContactNotFound);
        }

        var row = rows[0];
        var binding = row.Binding;

        // E5
        if (binding.Kind == BindingKind.Unreadable)
        {
            return Collide(IdentityCollisionReason.BindingUnreadable, DenyContactBindingUnreadable, row.ContactId);
        }

        if (binding.Kind == BindingKind.Bound)
        {
            // E6 — bound to this caller between our two reads. The oid is the identity; resolve.
            if (binding.Oid == request.CallerOid)
            {
                return new BindingDecision(BindingAction.ResolveByOid, row.ContactId);
            }

            // E7 — the A-18 hijack, on either plane.
            return Collide(IdentityCollisionReason.BoundToDifferentOid, DenyContactBoundToDifferentOid, row.ContactId);
        }

        // Unbound from here on.

        // E8 — a guest's email is controlled by its home tenant; a guest is never email-bound.
        if (request.Eligibility == BindingEligibility.CreateUnlessEmailMatches)
        {
            return Collide(IdentityCollisionReason.GuestEmailMatch, DenyGuestEmailMatch, row.ContactId);
        }

        if (references is null)
        {
            return new BindingDecision(BindingAction.NeedReferenceLookup, row.ContactId);
        }

        // E9
        if (references.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(references.Status));
        }

        // E10 — a contact another person is linked to carries that person's Assigned-To and No-Access
        // standing; binding a different identity onto it would merge two people. On the CIAM plane ANY
        // link counts: an internal user's contact is never the repair target of an external sign-in.
        var linkingUsers = references.References.Where(r => r.LinkedContactId == row.ContactId);
        var otherUserLinks = request.Plane == BindingPlane.CiamToken
            ? linkingUsers.Any()
            : linkingUsers.Any(r => r.Oid != request.CallerOid);
        if (otherUserLinks)
        {
            return Collide(IdentityCollisionReason.LinkedToOtherUser, DenyContactLinkedToOtherUser, row.ContactId);
        }

        // E11
        return new BindingDecision(BindingAction.BindByEmail, row.ContactId);
    }

    /// <summary>
    /// The existing-link flow: a systemuser whose <c>sprk_primarycontact</c> is <paramref name="linkedContactId"/>.
    /// Never re-points and never clears the link — a link that does not verify is FLAGGED and left exactly as it
    /// is (owner decision (a), 2026-09-30: clearing Ralph's link would silently remove his Assigned-To access).
    /// </summary>
    public static BindingDecision DecideExistingLink(
        BindingRequest request,
        Guid linkedContactId,
        ContactLookup oidLookup,
        ContactLookup? linkedContact = null,
        ReferenceLookup? references = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(oidLookup);

        if (request.CallerOid == Guid.Empty)
        {
            return Deny(DenyUnidentifiableCaller);
        }

        // L1
        if (oidLookup.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(oidLookup.Status));
        }

        var oidRows = oidLookup.Rows.Where(r => r.ContactId != Guid.Empty).ToList();

        // L2
        if (oidRows.Count > 1)
        {
            return Collide(IdentityCollisionReason.OidOnMultipleContacts, DenyContactOidAmbiguous,
                oidRows.Select(r => r.ContactId));
        }

        if (oidRows.Count == 1)
        {
            var carrier = oidRows[0];
            if (carrier.ContactId == linkedContactId)
            {
                // L3 / L4
                return carrier.IsActive
                    ? new BindingDecision(BindingAction.LinkVerified, linkedContactId)
                    : Collide(IdentityCollisionReason.LinkedContactInactive, DenyLinkedContactInactive, linkedContactId);
            }

            // L5 — the link names one contact, the binding another. Neither is re-pointed.
            return Collide(IdentityCollisionReason.LinkedContactMismatch, DenyLinkedContactMismatch, linkedContactId);
        }

        if (linkedContact is null)
        {
            return new BindingDecision(BindingAction.NeedLinkedContact, linkedContactId);
        }

        if (linkedContact.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(linkedContact.Status));
        }

        // L6 — the link points at nothing. There is no row to flag; the job reports it.
        var linked = linkedContact.Rows.FirstOrDefault(r => r.ContactId == linkedContactId);
        if (linked is null)
        {
            return Deny(DenyLinkedContactMissing);
        }

        var binding = linked.Binding;

        // L7
        if (binding.Kind == BindingKind.Unreadable)
        {
            return Collide(IdentityCollisionReason.BindingUnreadable, DenyContactBindingUnreadable, linkedContactId);
        }

        if (binding.Kind == BindingKind.Bound)
        {
            // Bound to this user although the oid lookup missed it: a concurrent write. Verified.
            if (binding.Oid == request.CallerOid)
            {
                return linked.IsActive
                    ? new BindingDecision(BindingAction.LinkVerified, linkedContactId)
                    : Collide(IdentityCollisionReason.LinkedContactInactive, DenyLinkedContactInactive, linkedContactId);
            }

            // L8 — Ralph's dev row: linked to a contact a CIAM identity owns. Flag, keep the link.
            return Collide(IdentityCollisionReason.LinkedContactBoundToDifferentOid,
                DenyLinkedContactBoundToDifferentOid, linkedContactId);
        }

        // Unbound linked contact.

        // L9
        if (!linked.IsActive)
        {
            return Collide(IdentityCollisionReason.LinkedContactInactive, DenyLinkedContactInactive, linkedContactId);
        }

        // L10 — the same reason a guest is never email-bound: nothing ties this contact to the guest's oid.
        if (request.Eligibility == BindingEligibility.CreateUnlessEmailMatches)
        {
            return Collide(IdentityCollisionReason.GuestLinkUnverified, DenyGuestLinkUnverified, linkedContactId);
        }

        if (references is null)
        {
            return new BindingDecision(BindingAction.NeedReferenceLookup, linkedContactId);
        }

        if (references.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(references.Status));
        }

        // L11
        if (references.References.Any(r => r.LinkedContactId == linkedContactId && r.Oid != request.CallerOid))
        {
            return Collide(IdentityCollisionReason.LinkedToOtherUser, DenyContactLinkedToOtherUser, linkedContactId);
        }

        // L12
        return new BindingDecision(BindingAction.BindLinkedContact, linkedContactId);
    }

    /// <summary>
    /// The invite path. Resolves by email over ACTIVE contacts (two rows); refuses a contact another plane
    /// owns instead of answering "AlreadyProvisioned" for it.
    /// </summary>
    public static InviteDecision DecideInvite(ContactLookup emailLookup, ReferenceLookup? references = null)
    {
        ArgumentNullException.ThrowIfNull(emailLookup);

        if (emailLookup.Status != LookupStatus.Read)
        {
            return new InviteDecision(InviteContactAction.Fail, ReasonCode: InviteContactLookupFailed);
        }

        var rows = emailLookup.Rows.Where(r => r.IsActive && r.ContactId != Guid.Empty).ToList();
        if (rows.Count > 1)
        {
            return new InviteDecision(InviteContactAction.Refuse, ReasonCode: InviteEmailAmbiguous,
                Reason: IdentityCollisionReason.EmailAmbiguous, FlagContactIds: rows.Select(r => r.ContactId).ToList());
        }

        if (rows.Count == 0)
        {
            return new InviteDecision(InviteContactAction.CreateContact);
        }

        var row = rows[0];
        var binding = row.Binding;
        if (binding.Kind == BindingKind.Unreadable)
        {
            return new InviteDecision(InviteContactAction.Refuse, row.ContactId, InviteContactBindingUnreadable,
                IdentityCollisionReason.BindingUnreadable, new[] { row.ContactId });
        }

        if (binding.Kind == BindingKind.Bound && binding.Plane == IdentityPlaneMarker.Workforce)
        {
            return new InviteDecision(InviteContactAction.Refuse, row.ContactId, InviteWorkforceBoundContact,
                IdentityCollisionReason.InviteMatchesWorkforceContact, new[] { row.ContactId });
        }

        if (references is null)
        {
            return new InviteDecision(InviteContactAction.NeedReferenceLookup, row.ContactId);
        }

        if (references.Status != LookupStatus.Read)
        {
            return new InviteDecision(InviteContactAction.Fail, row.ContactId, InviteContactLookupFailed);
        }

        // Even a CIAM-bound contact is refused when an internal user links to it: inviting that email would
        // hand an internal person's contact (and its Assigned-To standing) to an external login.
        if (references.References.Any(r => r.LinkedContactId == row.ContactId))
        {
            return new InviteDecision(InviteContactAction.Refuse, row.ContactId, InviteContactLinkedToInternalUser,
                IdentityCollisionReason.InviteMatchesWorkforceContact, new[] { row.ContactId });
        }

        return binding.Kind == BindingKind.Bound
            ? new InviteDecision(InviteContactAction.AlreadyProvisioned, row.ContactId)
            : new InviteDecision(InviteContactAction.ProvisionExisting, row.ContactId);
    }

    /// <summary>
    /// The most parties one flag records. Past it a new party is NOT recorded, so a flag at capacity is never
    /// cleared or pruned by the job (an unrecorded party may still collide) — an operator clears it.
    /// </summary>
    public const int MaxCollisionParties = 20;

    /// <summary>
    /// Whether <paramref name="party"/>'s collision should be written onto a row. Idempotent per collision: a
    /// party already recorded is never written again (a caller retrying a denied sign-in must not turn the deny
    /// path into a stream of writes), so each distinct identity costs at most one write per contact. A
    /// DIFFERENT identity colliding with an already-flagged contact IS recorded — otherwise its collision would
    /// disappear the moment the first party's was resolved (verifier finding 3).
    /// </summary>
    public static bool ShouldWriteFlag(CollisionFlag? existing, CollisionParty party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return existing is null
            || (!existing.HasUnreadableParties && !existing.Records(party) && existing.Parties.Count < MaxCollisionParties);
    }

    /// <summary>The flag to write: a new one for the first party, else <paramref name="existing"/> plus the party.</summary>
    public static CollisionFlag FlagWith(CollisionFlag? existing, CollisionParty party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return existing is null
            ? CollisionFlag.FromParties(new[] { party })!
            : existing with { OtherParties = existing.OtherParties.Append(party).ToList() };
    }

    /// <summary>
    /// What the reconciliation job does with one open flag, given the recorded parties whose collision still
    /// holds (<paramref name="stillHolding"/>) and whether any systemuser decided THIS run collided with the
    /// contact (<paramref name="collidesThisRun"/>). Pure.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>A flag at <see cref="MaxCollisionParties"/> is KEPT: a party past the cap was never recorded, so
    ///     no evidence about the recorded ones can show the contact is clear.</item>
    ///   <item>A flag is CLEARED only when no recorded party still collides AND no systemuser collided with the
    ///     contact this run. The second condition covers a collision the run saw but has not recorded (report-only
    ///     mode, or an append that lost a race): it is still a live collision, so the flag stays.</item>
    ///   <item>Otherwise a flag some of whose parties no longer collide is PRUNED to the ones that do, so the
    ///     operator's view names a party that still collides.</item>
    /// </list>
    /// </remarks>
    public static FlagReconciliation ReconcileFlag(
        CollisionFlag flag, IReadOnlyCollection<CollisionParty> stillHolding, bool collidesThisRun)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(stillHolding);

        var parties = flag.Parties;
        if (flag.HasUnreadableParties || parties.Count >= MaxCollisionParties)
        {
            return new FlagReconciliation(FlagReconciliationAction.Keep);
        }

        var remaining = parties.Where(p => stillHolding.Any(h => h.IsSameCollision(p))).ToList();
        if (remaining.Count == 0)
        {
            return collidesThisRun
                ? new FlagReconciliation(FlagReconciliationAction.Keep)
                : new FlagReconciliation(FlagReconciliationAction.Clear);
        }

        return remaining.Count < parties.Count
            ? new FlagReconciliation(FlagReconciliationAction.Prune, CollisionFlag.FromParties(remaining))
            : new FlagReconciliation(FlagReconciliationAction.Keep);
    }

    /// <summary>
    /// Whether one recorded party's collision still holds, re-evaluated by the reconciliation job for a party
    /// that is not a systemuser the run re-decided. A party is dropped only when this is false.
    /// </summary>
    /// <param name="party">The recorded party.</param>
    /// <param name="flagged">The flagged contact, freshly read.</param>
    /// <param name="emailCarriers">Active contacts carrying the flagged contact's email.</param>
    /// <param name="oidCarriers">Contacts carrying the party's oid.</param>
    /// <param name="references">Systemusers linking the flagged contact.</param>
    public static bool CollisionStillHolds(
        CollisionParty party,
        ContactBindingRow flagged,
        ContactLookup emailCarriers,
        ContactLookup oidCarriers,
        ReferenceLookup references)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(flagged);
        ArgumentNullException.ThrowIfNull(emailCarriers);
        ArgumentNullException.ThrowIfNull(oidCarriers);
        ArgumentNullException.ThrowIfNull(references);

        // A key-mirror conflict is a fact about the UNIQUE INDEX, which counts inactive rows too: deactivating the
        // holder does not free the oid. So it is decided from the holder row alone, before the inactive shortcut —
        // it holds while the holder's mirror still carries the party's oid without the binding to match.
        if (party.Reason == IdentityCollisionReason.KeyMirrorConflict)
        {
            return party.Oid is { } mirrored && MirrorHeldWithoutBinding(flagged, mirrored);
        }

        // Anything we could not read keeps the party: dropping it on a failed read is the fail-open direction.
        if (emailCarriers.Status != LookupStatus.Read || oidCarriers.Status != LookupStatus.Read
            || references.Status != LookupStatus.Read)
        {
            return true;
        }

        // An operator deactivated the contact: the person was removed, the collision is moot.
        if (!flagged.IsActive)
        {
            return false;
        }

        var binding = flagged.Binding;
        var links = references.References.Where(r => r.LinkedContactId == flagged.ContactId).ToList();

        return party.Reason switch
        {
            IdentityCollisionReason.BoundToDifferentOid or IdentityCollisionReason.LinkedContactBoundToDifferentOid
                => binding.Kind == BindingKind.Unreadable
                   || (binding.Kind == BindingKind.Bound && binding.Oid != party.Oid),
            IdentityCollisionReason.EmailAmbiguous
                => emailCarriers.Rows.Count(r => r.IsActive) > 1,
            IdentityCollisionReason.OidOnMultipleContacts
                => oidCarriers.Rows.Count > 1,
            IdentityCollisionReason.BindingUnreadable
                => binding.Kind == BindingKind.Unreadable,
            IdentityCollisionReason.LinkedToOtherUser
                => party.Plane == IdentityPlaneMarker.External
                    ? links.Count > 0
                    : links.Any(r => r.Oid != party.Oid),
            IdentityCollisionReason.InviteMatchesWorkforceContact
                => (binding.Kind == BindingKind.Bound && binding.Plane == IdentityPlaneMarker.Workforce)
                   || links.Count > 0,
            // Re-decided per systemuser by the job's first pass. Reaching here means that systemuser was not
            // re-decided this run (disabled, removed, or out of scope) — the collision has no live party.
            IdentityCollisionReason.GuestEmailMatch or IdentityCollisionReason.LinkedContactMismatch
                or IdentityCollisionReason.LinkedContactInactive or IdentityCollisionReason.GuestLinkUnverified
                => false,
            // No reason, or one this code does not know: keep it. Clearing what we cannot evaluate is fail-open.
            _ => true,
        };
    }

    /// <summary>The deny code for a collision reason on the binding planes.</summary>
    public static string DenyCodeFor(IdentityCollisionReason reason) => reason switch
    {
        IdentityCollisionReason.BoundToDifferentOid => DenyContactBoundToDifferentOid,
        IdentityCollisionReason.EmailAmbiguous => DenyContactEmailAmbiguous,
        IdentityCollisionReason.OidOnMultipleContacts => DenyContactOidAmbiguous,
        IdentityCollisionReason.LinkedToOtherUser => DenyContactLinkedToOtherUser,
        IdentityCollisionReason.BindingUnreadable => DenyContactBindingUnreadable,
        IdentityCollisionReason.GuestEmailMatch => DenyGuestEmailMatch,
        IdentityCollisionReason.LinkedContactBoundToDifferentOid => DenyLinkedContactBoundToDifferentOid,
        IdentityCollisionReason.LinkedContactMismatch => DenyLinkedContactMismatch,
        IdentityCollisionReason.LinkedContactInactive => DenyLinkedContactInactive,
        IdentityCollisionReason.GuestLinkUnverified => DenyGuestLinkUnverified,
        IdentityCollisionReason.InviteMatchesWorkforceContact => InviteWorkforceBoundContact,
        IdentityCollisionReason.KeyMirrorConflict => DenyContactKeyConflict,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Every collision reason needs its own code."),
    };

    /// <summary>
    /// True when <paramref name="row"/>'s uniqueness mirror carries <paramref name="oid"/> while its binding does
    /// not (unbound, unreadable, or bound to another oid) — the contact holds the oid's unique-index slot without
    /// owning the oid. Every BFF write sets binding and mirror together, so this is a squatted mirror (any user
    /// with contact Write can set the unsecured column) or one an operator half-cleared. Pure.
    /// </summary>
    /// <remarks>
    /// Owner round 4 item 4 (B2): field-level security stays on <c>sprk_externalobjectid</c>, and the platform's
    /// "exactly one contact per oid" moves to the unsecured mirror. What a user with contact Write can do to the
    /// mirror is therefore limited to DENYING SERVICE to one identity: the bind or create that needs the slot is
    /// refused by the index, and the BFF denies (<see cref="DenyContactKeyConflict"/>) and flags the holder. It can
    /// never make a contact resolve as someone else — nothing resolves or binds by the mirror.
    /// </remarks>
    public static bool MirrorHeldWithoutBinding(ContactBindingRow row, Guid oid)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (oid == Guid.Empty
            || string.IsNullOrWhiteSpace(row.RawKeyMirror)
            || !Guid.TryParse(row.RawKeyMirror.Trim(), out var mirrored)
            || mirrored != oid)
        {
            return false;
        }

        var binding = row.Binding;
        return !(binding.Kind == BindingKind.Bound && binding.Oid == oid);
    }

    /// <summary>
    /// The read-only SDK query for the contacts bound to <paramref name="oid"/>: EVERY statecode, two rows, with
    /// the statecode selected — the same question the binder's oid lookup asks
    /// (<c>IContactIdentityStore.FindContactsByOidAsync</c>), so an ambiguous binding is visible even when one of
    /// the two contacts is inactive. Shared by the read-only consumers (identity normalization, "assign it to
    /// me") so the binding column is named in exactly one SDK query. Answer it with
    /// <see cref="DecideBoundContact"/>.
    /// </summary>
    public static QueryExpression ContactsBoundToQuery(Guid oid)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "statecode", ExternalObjectIdColumn, IdentityPlaneColumn),
            TopCount = 2,
            NoLock = true,
        };
        query.Criteria.AddCondition(ExternalObjectIdColumn, ConditionOperator.Equal, oid.ToString("D"));
        return query;
    }

    /// <summary>
    /// The rows of a <see cref="ContactsBoundToQuery"/> result as the lookup <see cref="DecideBoundContact"/>
    /// reads. A row whose <c>statecode</c> is absent maps to an unknown state, which is not active (ADR-003: only
    /// <c>statecode = 0</c> resolves).
    /// </summary>
    public static ContactLookup BoundContactLookup(IEnumerable<Entity> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new ContactLookup(LookupStatus.Read, rows
            .Select(e => new ContactBindingRow(
                e.Id,
                e.GetAttributeValue<OptionSetValue>("statecode")?.Value,
                e.GetAttributeValue<string>(ExternalObjectIdColumn),
                e.GetAttributeValue<OptionSetValue>(IdentityPlaneColumn)?.Value))
            .ToList());
    }

    /// <summary>
    /// The oid step (D1–D4) on its own, for READ-ONLY consumers: the SAME answer the binder gives to "which
    /// contact is bound to this oid?", so a reader never resolves a contact the binder would deny.
    /// <list type="bullet">
    /// <item><c>null</c> — no contact carries the oid.</item>
    /// <item><see cref="BindingAction.ResolveByOid"/> — exactly one contact carries it, and it is active.</item>
    /// <item><see cref="BindingAction.Collision"/> (<see cref="DenyContactOidAmbiguous"/>) — two or more contacts
    /// carry it, in ANY state: one active plus one inactive is ambiguous here exactly as it is in the binder.</item>
    /// <item><see cref="BindingAction.Deny"/> — the only contact is inactive (<see cref="DenyContactInactive"/>),
    /// or the lookup could not be read.</item>
    /// </list>
    /// A reader writes nothing: flagging the collision is the binder's job (sign-in or the reconciliation job).
    /// </summary>
    public static BindingDecision? DecideBoundContact(ContactLookup oidLookup)
    {
        ArgumentNullException.ThrowIfNull(oidLookup);
        return DecideOnOid(oidLookup);
    }

    /// <summary>
    /// <c>contact.sprk_externalobjectid</c> — the binding, both planes. FIELD-SECURED (only the BFF writes it).
    /// Every read that decides who a contact IS uses this column, and only this column.
    /// </summary>
    public const string ExternalObjectIdColumn = "sprk_externalobjectid";

    /// <summary>
    /// <c>contact.sprk_externalobjectidkey</c> — the UNSECURED uniqueness mirror that carries the platform's
    /// alternate key (owner round 4 item 4, B2: Dataverse refuses an alternate key on a field-secured column). The
    /// BFF writes it with the same oid, in the same request, as every bind and create; nothing reads an identity
    /// from it (<see cref="MirrorHeldWithoutBinding"/>).
    /// </summary>
    public const string KeyMirrorColumn = "sprk_externalobjectidkey";

    /// <summary><c>contact.sprk_identityplane</c> — which plane wrote the binding.</summary>
    public const string IdentityPlaneColumn = "sprk_identityplane";

    private static BindingDecision? DecideOnOid(ContactLookup oidLookup)
    {
        if (oidLookup.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(oidLookup.Status));
        }

        var rows = oidLookup.Rows.Where(r => r.ContactId != Guid.Empty).ToList();
        if (rows.Count > 1)
        {
            return Collide(IdentityCollisionReason.OidOnMultipleContacts, DenyContactOidAmbiguous,
                rows.Select(r => r.ContactId));
        }

        if (rows.Count == 1)
        {
            // D3 — never falls through to the email bind and never creates a replacement: deactivating a
            // contact is how an operator removes a person, and auto-creation must not undo that.
            return rows[0].IsActive
                ? new BindingDecision(BindingAction.ResolveByOid, rows[0].ContactId)
                : Deny(DenyContactInactive);
        }

        return null;
    }

    private static string LookupDenyCode(LookupStatus status)
        => status == LookupStatus.ColumnMissing ? DenyBindingColumnMissing : DenyContactLookupFailed;

    private static BindingDecision Deny(string code) => new(BindingAction.Deny, DenyCode: code);

    private static BindingDecision Collide(IdentityCollisionReason reason, string code, params Guid[] contactIds)
        => Collide(reason, code, (IEnumerable<Guid>)contactIds);

    private static BindingDecision Collide(IdentityCollisionReason reason, string code, IEnumerable<Guid> contactIds)
        => new(BindingAction.Collision, DenyCode: code, Reason: reason,
            FlagContactIds: contactIds.Distinct().ToList());
}
