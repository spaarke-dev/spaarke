// unified-access-control-r2 task 141 (defect C7, owner round 2 item 4) — THE identity-binding decision.
//
// One pure decision shared by every path that binds a person to a Dataverse contact:
//   • the workforce token plane (a customer employee with no Power Apps licence — "Type 2"),
//   • the CIAM token plane (an invited external user; the email bind is kept for the invite-repair path and the
//     existing-CIAM-account invite, and since task 184 binds only on this environment's intent — E11),
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

    /// <summary>
    /// ADR-028 A9 (r3 task 044 schema, task 047 writer): an object id from a CUSTOMER'S home directory, written only by the
    /// registration route (<c>POST /api/v1/external/registration</c>) together with <c>sprk_externaltenantid</c>. Read as a
    /// readable binding everywhere (<see cref="ContactBindingDecision.ReadBinding"/>): the Spaarke-tenant and CIAM planes
    /// treat it as a contact bound to someone else's oid, never as unbound; resolution by (tid, oid) is
    /// <see cref="ContactBindingDecision.DecideHomeContact"/>'s alone. How each binding reader treats it:
    /// <c>projects/spaarke-SPA-external-access-platform-r3/notes/task-047-notes.md</c>.
    /// </summary>
    WorkforceHome = 100000002,
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

    /// <summary>
    /// Registration (r3 task 047, fix round 2; owner 2026-10-15 "Refuse, flag for admin"): no ACTIVE contact carries the
    /// token email but an INACTIVE one does. The registration is refused (<c>registration_contact_conflict</c>), nothing is
    /// created or bound, and the inactive contact is flagged so an operator sees it; reactivating it is the operator's
    /// decision. Held while the flagged contact is still inactive and still carries an email
    /// (<see cref="ContactBindingDecision.CollisionStillHolds"/>) — the one reason that survives the inactive-contact
    /// shortcut, because the inactive state IS the collision.
    /// </summary>
    InactiveContactEmailMatch = 100000012,
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
/// <param name="RawExternalTenantId">
/// ADR-028 A9 (r3 task 046): <c>sprk_externaltenantid</c> exactly as stored, or null — the home directory (<c>tid</c>) of a
/// WorkforceHome contact. Selected only by the home contact lookup (<see cref="ContactBindingDecision.DecideHomeContact"/>);
/// every other read leaves it null.
/// </param>
public sealed record ContactBindingRow(
    Guid ContactId,
    int? StateCode,
    string? RawOid,
    int? RawPlane,
    string? ETag = null,
    string? Email = null,
    CollisionFlag? Flag = null,
    string? RawKeyMirror = null,
    string? RawExternalTenantId = null)
{
    /// <summary>Only <c>statecode = 0</c> resolves or binds (ADR-003).</summary>
    public bool IsActive => StateCode == 0;

    /// <summary>
    /// <c>true</c> when this row was read while the BFF's read of the field-secured identity columns
    /// (<see cref="DataverseContactIdentityStore.IdentityColumns"/>: the binding, the plane and the collision flag) was NOT
    /// proven (<see cref="SecuredColumnReadProof"/>). An empty plane on such a row may be a hidden one, so its binding is
    /// <see cref="BindingKind.Unreadable"/>: never Unbound (no bind onto it) and never a plane-less legacy binding.
    /// </summary>
    public bool IdentityColumnsUnproven { get; init; }

    /// <summary>
    /// The binding, read by <see cref="ContactBindingDecision.ReadBinding"/>; <see cref="BindingKind.Unreadable"/> when the
    /// plane read empty and the identity columns' read is not proven (<see cref="IdentityColumnsUnproven"/>).
    /// </summary>
    public BindingState Binding => IdentityColumnsUnproven && RawPlane is null
        ? BindingState.Unreadable
        : ContactBindingDecision.ReadBinding(RawOid, RawPlane);
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

/// <summary>
/// ADR-028 A9 (r3 task 046): one systemuser as the stored-link read returns it — the FIELD-SECURED home pair
/// (<c>sprk_hometenantid</c>, <c>sprk_homeobjectid</c>) exactly as stored, and <c>isdisabled</c>. The unsecured key mirrors
/// are never selected: nothing resolves by them (design §5). <paramref name="ETag"/> is the row version, read so the link
/// write (r3 task 048) is conditional on it; the stored-link read does not need it.
/// </summary>
public sealed record HomeLinkRow(
    Guid SystemUserId, bool? IsDisabled, string? RawHomeTenantId, string? RawHomeObjectId, string? ETag = null);

/// <summary>The stored-link read: rows, or why there are none to trust.</summary>
public sealed record HomeLinkLookup(LookupStatus Status, IReadOnlyList<HomeLinkRow> Rows)
{
    /// <summary>A link read that could not be read.</summary>
    public static HomeLinkLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<HomeLinkRow>());

    /// <summary>A link read whose columns do not exist here (the A9 schema is not applied).</summary>
    public static HomeLinkLookup ColumnMissing { get; } = new(LookupStatus.ColumnMissing, Array.Empty<HomeLinkRow>());

    /// <summary>A link read that read these rows.</summary>
    public static HomeLinkLookup Of(params HomeLinkRow[] rows) => new(LookupStatus.Read, rows);
}

/// <summary>What the stored link says about a home principal (ADR-028 A9, design §3 step 2).</summary>
public enum HomeLinkOutcome
{
    /// <summary>No systemuser carries the (<c>tid</c>, <c>oid</c>) pair: go on to the contact lookup.</summary>
    NotLinked,

    /// <summary>Exactly one ENABLED systemuser carries the pair: resolve to it.</summary>
    Enabled,

    /// <summary>Only disabled systemusers carry the pair: deny <c>systemuser_disabled</c>; never fall through.</summary>
    Disabled,

    /// <summary>The read failed, or the answer is ambiguous: deny with <see cref="HomeLinkDecision.DenyCode"/>.</summary>
    Denied,
}

/// <summary>The stored-link decision.</summary>
public sealed record HomeLinkDecision(HomeLinkOutcome Outcome, Guid? SystemUserId = null, string? DenyCode = null);

/// <summary>What the link route does for a proven guest identity and home pair (ADR-028 A9, r3 task 048, design §5).</summary>
public enum HomeLinkWriteAction
{
    /// <summary>The guest's enabled systemuser carries no link and no other systemuser holds the pair: write the link.</summary>
    Write,

    /// <summary>The guest's systemuser already carries exactly this pair: nothing to write.</summary>
    AlreadyLinked,

    /// <summary>No systemuser carries the guest's Spaarke-tenant object id.</summary>
    NoSystemUser,

    /// <summary>Only disabled systemusers carry the guest's object id.</summary>
    SystemUserDisabled,

    /// <summary>
    /// The guest's systemuser carries a DIFFERENT (or unreadable) link, or another systemuser already holds the pair. A link
    /// is never re-pointed (design §5).
    /// </summary>
    Collision,

    /// <summary>A read failed, or the guest's object id is ambiguous: deny with <see cref="HomeLinkWriteDecision.DenyCode"/>.</summary>
    Deny,
}

/// <summary>The link decision: the systemuser to write and its row version, or why not.</summary>
public sealed record HomeLinkWriteDecision(
    HomeLinkWriteAction Action, Guid? SystemUserId = null, string? ETag = null, string? DenyCode = null);

/// <summary>
/// ADR-028 A9 (r3 task 047): one systemuser as the licensed-user email read returns it — <c>isdisabled</c> and the two
/// directory-synced email columns the refusal compares (owner 2026-10-15: <c>internalemailaddress</c> OR <c>domainname</c>).
/// </summary>
public sealed record SystemUserEmailRow(Guid SystemUserId, bool? IsDisabled, string? InternalEmail, string? DomainName);

/// <summary>The licensed-user email read: rows, or why there are none to trust.</summary>
public sealed record SystemUserEmailLookup(LookupStatus Status, IReadOnlyList<SystemUserEmailRow> Rows)
{
    /// <summary>A read that could not be read.</summary>
    public static SystemUserEmailLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<SystemUserEmailRow>());

    /// <summary>A read that returned these rows.</summary>
    public static SystemUserEmailLookup Of(params SystemUserEmailRow[] rows) => new(LookupStatus.Read, rows);
}

/// <summary>What the licensed-user email check (design §6 step 2) says.</summary>
public enum LicensedUserEmailOutcome
{
    /// <summary>No ENABLED systemuser carries the email in either column.</summary>
    NotLicensed,

    /// <summary>An enabled systemuser carries it: refuse with <c>licensed_user_link_required</c> (a refusal, never a grant).</summary>
    LicensedUser,

    /// <summary>The read failed: it cannot be ruled out, so deny (never fall through to a create).</summary>
    Unknown,
}

/// <summary>What the registration email step (design §4, §6 step 3) decided.</summary>
public enum RegistrationEmailAction
{
    /// <summary>No active contact carries the email: create.</summary>
    Create,

    /// <summary>One active, unbound candidate: the systemusers linking it must be read first.</summary>
    NeedReferenceLookup,

    /// <summary>Exactly one bindable contact (active, unbound, not linked to a systemuser): bind it — only on confirmation.</summary>
    BindExisting,

    /// <summary>The one match is already bound to the caller's own oid (a concurrent registration): re-read, never write.</summary>
    AlreadyBoundToCaller,

    /// <summary>Several matches, or a match that is bound, unreadable or linked: refuse and flag; nothing bound or created.</summary>
    Conflict,

    /// <summary>A read failed: deny with <see cref="RegistrationEmailDecision.DenyCode"/>.</summary>
    Deny,
}

/// <summary>The registration email step's answer.</summary>
public sealed record RegistrationEmailDecision(
    RegistrationEmailAction Action,
    Guid? ContactId = null,
    string? DenyCode = null,
    IReadOnlyList<Guid>? FlagContactIds = null,
    IdentityCollisionReason? FlagReason = null);

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

/// <summary>
/// Task 184 (owner round 96) — whether THIS environment intends an unbound contact to be email-bound on the CIAM plane.
/// The pending-invite marker and its stored invite-code hash, read together in the marker's own query
/// (<c>IContactIdentityStore.FindBindIntentAsync</c>), raw, interpreted by the decision
/// (<see cref="ContactBindingDecision.Decide"/>, E11). Task 185 (option (c)): no grant is read; a grant is no bind evidence.
/// </summary>
/// <remarks>
/// Why it exists: any CIAM user can obtain a token for any customer's audience (the SPA client is pre-authorized for
/// every customer — provisioning spike S1), so an email match alone would bind a person customer Y never invited onto Y's
/// contact. Task 185 (owner round 98): Y's own pending invite, proved by the invite code from Y's own invite email, is the
/// only evidence; a grant alone is not.
/// </remarks>
/// <param name="ContactId">The contact the facts were read for; a lookup for any other contact is never used.</param>
/// <param name="InviteStatus">
/// How the marker read ended. <see cref="LookupStatus.ColumnMissing"/> (the marker or the hash column is not provisioned)
/// is "no invite evidence", NOT a fault (a BFF deployed before the columns denies <c>contact_not_invited</c>). A <see cref="LookupStatus.Failed"/> read denies
/// <c>contact_lookup_failed</c>.
/// </param>
/// <param name="InvitedOn"><c>contact.sprk_externalinvitedon</c> — when the BFF's invite flow last invited the contact.</param>
public sealed record BindIntentLookup(
    Guid ContactId,
    LookupStatus InviteStatus,
    DateTimeOffset? InvitedOn)
{
    /// <summary>
    /// Task 184 (owner round 97) — the decision time the marker's age is measured at: the binder's
    /// <see cref="TimeProvider"/>, stamped when it hands the lookup to the decision, so the decision stays pure. Left
    /// unset (<see cref="DateTimeOffset.MinValue"/>), every marker reads as dated in the future and counts for nothing —
    /// fail closed.
    /// </summary>
    public DateTimeOffset AsOf { get; init; }

    /// <summary>
    /// Task 185: <c>contact.sprk_externalinvitecodehash</c> as stored (<see cref="InviteCode"/>'s <c>v1.</c> value), or
    /// null. The marker leg binds only when the presented code matches it. INTERNAL so the compiler-generated
    /// <see cref="object.ToString"/> of this record never prints it (never log a hash, CLAUDE.md §9).
    /// </summary>
    internal string? InviteCodeHash { get; init; }

    /// <summary>The marker read failed.</summary>
    public static BindIntentLookup Failed(Guid contactId) =>
        new(contactId, LookupStatus.Failed, null);
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
    /// <summary>
    /// Task 185: the invite code the caller presented (CIAM plane only), or null for "no code presented". INTERNAL so the
    /// compiler-generated <see cref="object.ToString"/> of this record never prints it (never log a code, CLAUDE.md §9).
    /// </summary>
    internal string? InviteCode { get; init; }

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

    /// <summary>
    /// The CIAM plane's E11 needs this environment's intent for <see cref="BindingDecision.ContactId"/> (a pending invite and
    /// its stored invite-code hash) before it can bind (tasks 184 and 185).
    /// </summary>
    NeedIntentLookup,
}

/// <summary>One decision step's answer.</summary>
public sealed record BindingDecision(
    BindingAction Action,
    Guid? ContactId = null,
    string? DenyCode = null,
    IdentityCollisionReason? Reason = null,
    IReadOnlyList<Guid>? FlagContactIds = null)
{
    /// <summary>
    /// Task 185: set on a CIAM-plane <see cref="BindingAction.BindByEmail"/> made through E11's marker leg: the bind was
    /// proven by a presented invite code, which the bind's SAME conditional write clears, so the code is single-use. #2005:
    /// the binder clears a pending hash on EVERY bind onto an existing contact, whatever the plane, so this flag records
    /// the E11 proof; it no longer selects the write.
    /// </summary>
    public bool ConsumeInviteCode { get; init; }

    /// <summary>True for an outcome that ends the decision.</summary>
    public bool IsTerminal => Action is not (BindingAction.NeedEmailLookup or BindingAction.NeedReferenceLookup
        or BindingAction.NeedLinkedContact or BindingAction.NeedIntentLookup);
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

    /// <summary>
    /// ISS-005 (#1673): no owner team could be resolved from <c>ExternalAccess:CustomerBusinessUnitId</c> (unset, the Secure
    /// Record business unit, or not exactly one default owner team), so the create is refused and nothing is written. ONE
    /// code for every contact create: the registration route answers it as 500 (<c>ExternalRegistrationEndpoints.ReasonOwnerUnconfigured</c>
    /// is this value) and the first-sign-in create answers it the same way (<c>CallerPrincipalResolver</c>).
    /// </summary>
    public const string DenyOwnerUnconfigured = "sdap.access.registration_owner_unconfigured";

    /// <summary>
    /// ISS-005 (#1673 UAC round 3, F): the licensed-user link's contact create was refused by Dataverse because the owner team
    /// cannot own a contact (error 0x80040299, "Privilege Check For Owner": the customer business unit's default owner team
    /// holds no security role with Read on contact). Its own code so the reconciliation run can tell a role fault from an
    /// unset <c>ExternalAccess:CustomerBusinessUnitId</c>. Never an API response code: the link route does not surface it.
    /// </summary>
    public const string DenyOwnerPrivilegeMissing = "sdap.access.deny.owner_privilege_missing";

    /// <summary>The binding column reads as masked (field-level security without Read): no write is safe.</summary>
    public const string DenyBindingColumnMasked = "sdap.access.deny.binding_column_masked";

    /// <summary>
    /// The uniqueness mirror carries the caller's oid on a contact whose binding does not: the platform's unique
    /// index refuses the bind or the create. A squatted or half-cleared mirror — flagged on the holder, never bound
    /// around (owner round 4 item 4, B2).
    /// </summary>
    public const string DenyContactKeyConflict = "sdap.access.deny.contact_key_conflict";

    /// <summary>
    /// Registration (r3 task 047, fix round 2): the token email matches no active contact but an INACTIVE one. Refused and
    /// flagged for an administrator; never created around and never bound to the inactive contact.
    /// </summary>
    public const string DenyContactInactiveEmailMatch = "sdap.access.deny.contact_inactive_email_match";

    /// <summary>
    /// CIAM plane, E11 (task 184, owner round 96; task 185, owner round 98): the email matches one unbound, active, unlinked
    /// contact, but the caller did not prove THIS environment's invite: a pending invite (<c>sprk_externalinvitedon</c> set
    /// within the last 30 days, owner round 97) together with its matching invite code. A grant alone is not an invite.
    /// Nothing is written and nothing is flagged: no other person is involved.
    /// </summary>
    public const string DenyContactNotInvited = "sdap.access.deny.contact_not_invited";

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
    /// Task 184 — the invite needed the pending-invite marker (<see cref="ExternalInvitedOnColumn"/>) as the contact's ONLY
    /// evidence of this environment's intent, and the marker did not land (the column is not provisioned yet, or the write
    /// failed). Retryable (503): nothing was reported as invited and no email was sent.
    /// </summary>
    public const string InviteMarkerNotWritten = "sdap.access.invite.invite_marker_not_written";

    /// <summary>
    /// ADR-028 A9 (r3 task 046): more than one ENABLED systemuser carries the same stored home link (<c>tid</c>, <c>oid</c>).
    /// Resolving to one of them would merge two people, so the caller is denied; an operator resolves it.
    /// </summary>
    public const string DenyHomeLinkAmbiguous = "sdap.access.deny.home_link_ambiguous";

    /// <summary>
    /// ADR-028 A9 (r3 task 048): more than one ENABLED systemuser carries the guest token's Spaarke-tenant object id. Linking
    /// one of them could link the wrong person, so the link is refused; an operator resolves it.
    /// </summary>
    public const string DenyGuestSystemUserAmbiguous = "sdap.access.deny.link_guest_systemuser_ambiguous";

    /// <summary>
    /// Reads a contact's binding as Unbound / Bound / Unreadable from its raw column values.
    /// </summary>
    /// <remarks>
    /// <para><b>A plane marker with no oid is UNREADABLE, never unbound.</b> Once
    /// <c>sprk_externalobjectid</c> is field-secured, a reader without FLS Read gets it back as null — in a
    /// returned row AND inside a filter, where Dataverse substitutes null. A bound contact would then look
    /// unbound and be handed to whoever's email matched. Every BFF write sets both columns together, so "marker
    /// present, oid absent" is the detectable signature of masking (or of an operator who cleared one column and not
    /// the other). Either way it denies.</para>
    /// <para><b>An EMPTY plane is trusted only on a proven read.</b> <c>sprk_identityplane</c> is field-secured too, so
    /// a hidden plane reads empty, like no plane. This parse cannot tell them apart: a row read while the identity
    /// columns' read is not proven is <see cref="BindingKind.Unreadable"/> whenever its plane is empty
    /// (<see cref="ContactBindingRow.IdentityColumnsUnproven"/>, set by the store from <see cref="SecuredColumnReadProof"/>).</para>
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
        ReferenceLookup? references = null,
        BindIntentLookup? intent = null)
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

        // E11 — the CIAM plane binds only on THIS environment's intent (task 184, owner round 96). A CIAM token for this
        // customer's audience is obtainable by any CIAM user, so an email match alone is no evidence this customer means
        // this person. The workforce and systemuser planes are unchanged: their callers are admitted by the member test
        // or are directory-synced licensed users (Model 1 workforce sign-in is under separate owner discussion).
        if (request.Plane == BindingPlane.CiamToken)
        {
            if (intent is null)
            {
                return new BindingDecision(BindingAction.NeedIntentLookup, row.ContactId);
            }

            return DecideIntent(request, intent, row.ContactId);
        }

        return new BindingDecision(BindingAction.BindByEmail, row.ContactId);
    }

    /// <summary>
    /// E11's marker leg on the CIAM plane (task 185, owner round 98). It binds, and tells the binder to consume the code,
    /// only when the marker is current (<see cref="IsInviteMarkerCurrent"/>: at most 30 days old, at most 5 minutes in the
    /// future) AND the presented code matches the contact's stored hash (<see cref="InviteCode.Matches"/>, constant time).
    /// FAIL CLOSED: a lookup for another contact, or a marker read that FAILED, denies <see cref="DenyContactLookupFailed"/>.
    /// Every other non-match (no code, a wrong or superseded code, no stored hash, an expired or far-future marker, a marker
    /// column not provisioned yet) denies <see cref="DenyContactNotInvited"/>, and the answer does not tell them apart. A
    /// grant is NOT evidence (option (c)): a grant alone never binds.
    /// </summary>
    private static BindingDecision DecideIntent(BindingRequest request, BindIntentLookup intent, Guid contactId)
    {
        if (intent.ContactId != contactId
            || intent.InviteStatus is not (LookupStatus.Read or LookupStatus.ColumnMissing))
        {
            return Deny(DenyContactLookupFailed);
        }

        var invited = intent.InviteStatus == LookupStatus.Read
            && IsInviteMarkerCurrent(intent.InvitedOn, intent.AsOf)
            && InviteCode.Matches(intent.InviteCodeHash, request.InviteCode, contactId);
        return invited
            ? new BindingDecision(BindingAction.BindByEmail, contactId) { ConsumeInviteCode = true }
            : Deny(DenyContactNotInvited);
    }

    /// <summary>
    /// Task 185 (design note §5.1): the OPERATOR-only reason E11's marker leg answered <see cref="DenyContactNotInvited"/> —
    /// <c>no-marker</c>, <c>marker-expired</c>, <c>no-stored-hash</c>, <c>no-code-presented</c> or <c>code-mismatch</c>, in
    /// the order <see cref="DecideIntent"/> tests them. For the binder's log line only: it names no code and no hash, and
    /// the HTTP answer never tells the reasons apart (no oracle). <c>null</c> when the leg would not deny not-invited. Pure.
    /// </summary>
    internal static string? NotInvitedReason(BindingRequest request, BindIntentLookup intent, Guid contactId)
    {
        if (intent.ContactId != contactId || intent.InviteStatus is not (LookupStatus.Read or LookupStatus.ColumnMissing))
            return null;
        if (intent.InviteStatus == LookupStatus.ColumnMissing || intent.InvitedOn is null)
            return "no-marker";
        if (!IsInviteMarkerCurrent(intent.InvitedOn, intent.AsOf))
            return "marker-expired";
        if (string.IsNullOrEmpty(intent.InviteCodeHash))
            return "no-stored-hash";
        if (string.IsNullOrEmpty(request.InviteCode))
            return "no-code-presented";
        return InviteCode.Matches(intent.InviteCodeHash, request.InviteCode, contactId) ? null : "code-mismatch";
    }

    /// <summary>
    /// Task 184 (owner round 97) — a pending-invite marker counts for <see cref="InviteMarkerWindow"/> (30 days) after the
    /// invite: an invite nobody acted on for a month is no longer this environment's intent. A re-invite overwrites the
    /// marker and so restarts the window.
    /// </summary>
    public static readonly TimeSpan InviteMarkerWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Task 184 (owner round 97) — how far in the future a marker may be dated and still count: clock skew between the
    /// BFF instance that stamped it and the one deciding. Anything later is not an invite this BFF wrote "now" (only the
    /// BFF writes the column, from its own clock), so it is treated as tampering and counts for nothing.
    /// </summary>
    public static readonly TimeSpan InviteMarkerClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True when <paramref name="invitedOn"/> is evidence at <paramref name="asOf"/>: no more than
    /// <see cref="InviteMarkerWindow"/> old (exactly 30 days counts) and no more than <see cref="InviteMarkerClockSkew"/> in
    /// the future (exactly 5 minutes counts). An expired or far-future marker is "no invite evidence", not a fault. Pure.
    /// </summary>
    public static bool IsInviteMarkerCurrent(DateTimeOffset? invitedOn, DateTimeOffset asOf)
        => invitedOn is { } on
           && on - asOf <= InviteMarkerClockSkew
           && asOf - on <= InviteMarkerWindow;

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

        // A WorkforceHome binding (task 047) is a workforce contact's home-directory sign-in: refused exactly like the
        // Workforce plane, never "AlreadyProvisioned" as if it were a CIAM account.
        if (binding.Kind == BindingKind.Bound && IsWorkforcePlane(binding.Plane))
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

        // A registration refused because the email belongs to a DEACTIVATED contact: the inactive state is the collision, so
        // this is decided BEFORE the inactive shortcut below. It holds while the contact stays inactive and still carries an
        // email; reactivating it (an operator decision) or removing its email clears the flag. (The party records no email,
        // so a change to ANOTHER address cannot be told apart here.)
        if (party.Reason == IdentityCollisionReason.InactiveContactEmailMatch)
        {
            return !flagged.IsActive && !string.IsNullOrWhiteSpace(flagged.Email);
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
                => (binding.Kind == BindingKind.Bound && IsWorkforcePlane(binding.Plane))
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
        IdentityCollisionReason.InactiveContactEmailMatch => DenyContactInactiveEmailMatch,
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

    /// <summary>
    /// <c>contact.sprk_externalinvitedon</c> — the pending-invite marker (task 184, owner round 96: the one column the
    /// no-schema rule was lifted for). A UTC date-and-time the BFF's invite flow writes BEFORE it creates the CIAM account,
    /// overwritten by a re-invite, never cleared. "Pending" = current (<see cref="IsInviteMarkerCurrent"/>) AND its code
    /// hash (<see cref="ExternalInviteCodeHashColumn"/>) not yet consumed by a bind (#2005). FIELD-SECURED so only the BFF can
    /// write it (<c>scripts/Set-ExternalInviteMarkerSchema.ps1</c>): a user who could set it could open E11's email bind.
    /// Read in its OWN query, never in <c>ContactSelect</c>, so a BFF deployed before the column keeps every binding read.
    /// It is NOT a binding and never touches <see cref="IdentityPlaneColumn"/>: "plane marker with no oid" stays Unreadable.
    /// </summary>
    public const string ExternalInvitedOnColumn = "sprk_externalinvitedon";

    /// <summary>
    /// <c>contact.sprk_externalinvitecodehash</c> — task 185: the stored value of the current invite's code
    /// (<see cref="InviteCode"/>'s <c>v1.</c> salted SHA-256, never the code). Written in the SAME request as
    /// <see cref="ExternalInvitedOnColumn"/> (a re-invite replaces both), cleared in the SAME request as the oid bind that
    /// consumes it. FIELD-SECURED with no Reader access (<c>scripts/Set-ExternalInviteCodeHashSchema.ps1</c>). Read only in
    /// the marker's own query, never in <c>ContactSelect</c>. Never logged.
    /// </summary>
    public const string ExternalInviteCodeHashColumn = "sprk_externalinvitecodehash";

    /// <summary>
    /// <c>contact.sprk_externalinvitedemailhash</c> — the salted hash of the email address the latest invite was sent to
    /// (never the address). FIELD-SECURED: only the BFF reads or writes it, no reader profile
    /// (<c>scripts/Set-ExternalAccessContactSecuritySchema.ps1</c>). Created by the schema step; no BFF read or write yet.
    /// </summary>
    public const string ExternalInvitedEmailHashColumn = "sprk_externalinvitedemailhash";

    /// <summary>
    /// <c>contact.sprk_externalinvitedtenantid</c> — the Entra tenant id (GUID text) the latest invite named, when it named
    /// one. FIELD-SECURED: only the BFF reads or writes it, no reader profile
    /// (<c>scripts/Set-ExternalAccessContactSecuritySchema.ps1</c>). Created by the schema step; no BFF read or write yet.
    /// </summary>
    public const string ExternalInvitedTenantIdColumn = "sprk_externalinvitedtenantid";

    /// <summary>
    /// <c>contact.sprk_externalaccessremovedon</c> — when an administrator removed all external access for the person (UTC).
    /// FIELD-SECURED: every user reads it, only the BFF writes it
    /// (<c>scripts/Set-ExternalAccessContactSecuritySchema.ps1</c>). Created by the schema step; no BFF read or write yet.
    /// </summary>
    public const string ExternalAccessRemovedOnColumn = "sprk_externalaccessremovedon";

    // ── ADR-028 A9 — home principals (r3 task 046; design notes/a9-bff-design.md §3, §4, §5) ────────────────────

    /// <summary>
    /// The <c>sprk_identityplane</c> option value WorkforceHome (task 044): a contact bound to an oid from a customer's
    /// home directory. Task 047 made it the <see cref="IdentityPlaneMarker.WorkforceHome"/> member (046 matched it as a raw
    /// value only); <see cref="DecideHomeContact"/> still matches on the raw value.
    /// </summary>
    public const int WorkforceHomePlaneValue = (int)IdentityPlaneMarker.WorkforceHome;

    /// <summary><c>contact.sprk_externaltenantid</c> — the home directory of a WorkforceHome contact. FIELD-SECURED (task 044).</summary>
    public const string ExternalTenantIdColumn = "sprk_externaltenantid";

    /// <summary>
    /// <c>systemuser.sprk_hometenantid</c> — the stored link's home directory. FIELD-SECURED (task 044). The link is looked
    /// up by this column and <see cref="HomeObjectIdColumn"/> only, never by their unsecured key mirrors
    /// (<c>sprk_hometenantidkey</c>, <c>sprk_homeobjectidkey</c>), so editing a mirror can block a link but never redirect
    /// a sign-in (design §5, owner 2026-10-14).
    /// </summary>
    public const string HomeTenantIdColumn = "sprk_hometenantid";

    /// <summary><c>systemuser.sprk_homeobjectid</c> — the stored link's home object id. FIELD-SECURED (task 044).</summary>
    public const string HomeObjectIdColumn = "sprk_homeobjectid";

    /// <summary>
    /// Design §3 step 2: what the stored link says about the home principal (<paramref name="homeTenantId"/>,
    /// <paramref name="homeObjectId"/>). Pure. The store's filter bounds the volume; this re-checks every row's SECURED pair,
    /// so a row the filter should not have returned (a mirror-only match, another directory) never counts.
    /// <list type="bullet">
    ///   <item>A read that failed, or a column that is missing, denies (fail closed: a link cannot be ruled out).</item>
    ///   <item>Exactly one enabled match → <see cref="HomeLinkOutcome.Enabled"/>. Two or more → denied
    ///   <see cref="DenyHomeLinkAmbiguous"/>.</item>
    ///   <item>Only disabled matches (a row whose <c>isdisabled</c> is absent counts as disabled) →
    ///   <see cref="HomeLinkOutcome.Disabled"/>.</item>
    ///   <item>No match → <see cref="HomeLinkOutcome.NotLinked"/>.</item>
    /// </list>
    /// </summary>
    public static HomeLinkDecision DecideHomeLink(HomeLinkLookup lookup, Guid homeTenantId, Guid homeObjectId)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        if (lookup.Status != LookupStatus.Read)
        {
            return new HomeLinkDecision(HomeLinkOutcome.Denied, DenyCode: LookupDenyCode(lookup.Status));
        }

        var matches = lookup.Rows
            .Where(r => r.SystemUserId != Guid.Empty
                        && IsGuid(r.RawHomeTenantId, homeTenantId)
                        && IsGuid(r.RawHomeObjectId, homeObjectId))
            .ToList();
        var enabled = matches.Where(r => r.IsDisabled == false).Select(r => r.SystemUserId).Distinct().ToList();

        if (enabled.Count > 1)
        {
            return new HomeLinkDecision(HomeLinkOutcome.Denied, DenyCode: DenyHomeLinkAmbiguous);
        }

        if (enabled.Count == 1)
        {
            return new HomeLinkDecision(HomeLinkOutcome.Enabled, enabled[0]);
        }

        return new HomeLinkDecision(matches.Count > 0 ? HomeLinkOutcome.Disabled : HomeLinkOutcome.NotLinked);
    }

    /// <summary>
    /// ADR-028 A9 (r3 task 048, design §5 "Outcome"): may the home pair (<paramref name="homeTenantId"/>,
    /// <paramref name="homeObjectId"/>) be written onto the guest's systemuser? Pure. Both identities are already proven by
    /// the caller; this decides only the write, and never re-points a link.
    /// <list type="number">
    ///   <item><paramref name="guestSystemUsers"/> (the systemusers carrying the guest's Spaarke-tenant object id, any state):
    ///   a failed read denies; two ENABLED rows deny <see cref="DenyGuestSystemUserAmbiguous"/>; none enabled →
    ///   <see cref="HomeLinkWriteAction.SystemUserDisabled"/> when a disabled row exists, else
    ///   <see cref="HomeLinkWriteAction.NoSystemUser"/>.</item>
    ///   <item>The enabled systemuser's own SECURED pair: exactly this pair → <see cref="HomeLinkWriteAction.AlreadyLinked"/>;
    ///   empty → go on; anything else (another pair, half a pair, an unparseable value) →
    ///   <see cref="HomeLinkWriteAction.Collision"/>.</item>
    ///   <item><paramref name="pairHolders"/> (the systemusers whose SECURED pair is this pair, any state): a failed read
    ///   denies; any OTHER systemuser holding it → <see cref="HomeLinkWriteAction.Collision"/>.</item>
    ///   <item>Otherwise <see cref="HomeLinkWriteAction.Write"/>, conditional on the row's version.</item>
    /// </list>
    /// The pair's key mirrors are never read: their alternate key refuses a duplicate at the write (412), which the caller
    /// maps to a collision too.
    /// </summary>
    public static HomeLinkWriteDecision DecideHomeLinkWrite(
        HomeLinkLookup guestSystemUsers, HomeLinkLookup pairHolders, Guid homeTenantId, Guid homeObjectId)
    {
        ArgumentNullException.ThrowIfNull(guestSystemUsers);
        ArgumentNullException.ThrowIfNull(pairHolders);

        if (guestSystemUsers.Status != LookupStatus.Read)
        {
            return new HomeLinkWriteDecision(HomeLinkWriteAction.Deny, DenyCode: LookupDenyCode(guestSystemUsers.Status));
        }

        var rows = guestSystemUsers.Rows.Where(r => r.SystemUserId != Guid.Empty).ToList();
        var enabled = rows.Where(r => r.IsDisabled == false).GroupBy(r => r.SystemUserId).Select(g => g.First()).ToList();
        if (enabled.Count > 1)
        {
            return new HomeLinkWriteDecision(HomeLinkWriteAction.Deny, DenyCode: DenyGuestSystemUserAmbiguous);
        }

        if (enabled.Count == 0)
        {
            return new HomeLinkWriteDecision(rows.Count > 0 ? HomeLinkWriteAction.SystemUserDisabled : HomeLinkWriteAction.NoSystemUser);
        }

        var target = enabled[0];
        var targetLinked = !string.IsNullOrWhiteSpace(target.RawHomeTenantId) || !string.IsNullOrWhiteSpace(target.RawHomeObjectId);

        // "Already linked" only in the exact form the 046 resolution reads (its filter compares the D-form text), so a
        // hand-entered pair in another format is not reported as working while sign-in still fails.
        if (targetLinked
            && !(IsCanonicalGuid(target.RawHomeTenantId, homeTenantId) && IsCanonicalGuid(target.RawHomeObjectId, homeObjectId)))
        {
            return new HomeLinkWriteDecision(HomeLinkWriteAction.Collision, target.SystemUserId);
        }

        if (pairHolders.Status != LookupStatus.Read)
        {
            return new HomeLinkWriteDecision(HomeLinkWriteAction.Deny, DenyCode: LookupDenyCode(pairHolders.Status));
        }

        if (pairHolders.Rows.Any(r => r.SystemUserId != Guid.Empty && r.SystemUserId != target.SystemUserId
                                      && IsGuid(r.RawHomeTenantId, homeTenantId) && IsGuid(r.RawHomeObjectId, homeObjectId)))
        {
            // Another holder: a new link would collide, and an existing one is ambiguous to the 046 resolution.
            return new HomeLinkWriteDecision(HomeLinkWriteAction.Collision, target.SystemUserId);
        }

        return targetLinked
            ? new HomeLinkWriteDecision(HomeLinkWriteAction.AlreadyLinked, target.SystemUserId)
            : new HomeLinkWriteDecision(HomeLinkWriteAction.Write, target.SystemUserId, target.ETag);
    }

    private static bool IsCanonicalGuid(string? raw, Guid expected)
        => expected != Guid.Empty && Guid.TryParseExact(raw, "D", out var value) && value == expected;

    /// <summary>
    /// Design §3 step 3 / §4: the contact a home principal resolves to — plane WorkforceHome AND
    /// <c>sprk_externaltenantid</c> = <paramref name="homeTenantId"/> AND <c>sprk_externalobjectid</c> =
    /// <paramref name="homeObjectId"/>. Pure. The store's filter bounds the volume; this applies all three conditions to every
    /// row, so a contact on plane External, on the Spaarke-tenant workforce plane, without a tenant, or in another directory
    /// is never matched, whatever the read returned.
    /// <list type="bullet">
    ///   <item><c>null</c> — no contact matches (the caller is then told to register).</item>
    ///   <item><see cref="BindingAction.ResolveByOid"/> — exactly one matches, and it is active.</item>
    ///   <item><see cref="BindingAction.Deny"/> — the read failed; the one match is inactive (<see cref="DenyContactInactive"/>,
    ///   as D3: deactivating a contact is how an operator removes a person); or several match
    ///   (<see cref="DenyContactOidAmbiguous"/>). Nothing is flagged or written here.</item>
    /// </list>
    /// </summary>
    public static BindingDecision? DecideHomeContact(ContactLookup lookup, Guid homeTenantId, Guid homeObjectId)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        if (lookup.Status != LookupStatus.Read)
        {
            return Deny(LookupDenyCode(lookup.Status));
        }

        var matches = lookup.Rows
            .Where(r => r.ContactId != Guid.Empty
                        && r.RawPlane == WorkforceHomePlaneValue
                        && IsGuid(r.RawExternalTenantId, homeTenantId)
                        && IsGuid(r.RawOid, homeObjectId))
            .ToList();

        if (matches.Count > 1)
        {
            return Deny(DenyContactOidAmbiguous);
        }

        if (matches.Count == 1)
        {
            return matches[0].IsActive
                ? new BindingDecision(BindingAction.ResolveByOid, matches[0].ContactId)
                : Deny(DenyContactInactive);
        }

        return null;
    }

    /// <summary>
    /// Design §6 step 2 (owner 2026-10-15): is <paramref name="email"/> an ENABLED systemuser's <c>internalemailaddress</c> OR
    /// <c>domainname</c>, compared case-insensitively? Pure. The store's filter bounds the volume; this re-checks every row,
    /// so a disabled user, or a row the filter should not have returned, never counts. A failed read is
    /// <see cref="LicensedUserEmailOutcome.Unknown"/> (deny): a licensed user must never be registered as a contact because a
    /// read failed.
    /// </summary>
    public static LicensedUserEmailOutcome DecideLicensedUserEmail(SystemUserEmailLookup lookup, string email)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        if (lookup.Status != LookupStatus.Read)
        {
            return LicensedUserEmailOutcome.Unknown;
        }

        var wanted = email.Trim();
        return lookup.Rows.Any(r => r.SystemUserId != Guid.Empty
                                    && r.IsDisabled == false
                                    && (SameEmail(r.InternalEmail, wanted) || SameEmail(r.DomainName, wanted)))
            ? LicensedUserEmailOutcome.LicensedUser
            : LicensedUserEmailOutcome.NotLicensed;
    }

    /// <summary>
    /// Design §4 "Registering to an existing contact" / §6 step 3: what the token email's ACTIVE contacts allow. Pure.
    /// <list type="bullet">
    ///   <item>No match → <see cref="RegistrationEmailAction.Create"/>.</item>
    ///   <item>Several matches → <see cref="RegistrationEmailAction.Conflict"/> over all of them, reason
    ///   <see cref="IdentityCollisionReason.EmailAmbiguous"/>.</item>
    ///   <item>One match bound to <paramref name="callerOid"/> → <see cref="RegistrationEmailAction.AlreadyBoundToCaller"/>
    ///   (a concurrent registration bound or created it; the caller re-reads and never writes).</item>
    ///   <item>One match that is bound to anyone else, on any plane (External included), or whose binding cannot be read →
    ///   <see cref="RegistrationEmailAction.Conflict"/> by its cause: <see cref="IdentityCollisionReason.BoundToDifferentOid"/>
    ///   or <see cref="IdentityCollisionReason.BindingUnreadable"/>.</item>
    ///   <item>One unbound match → <see cref="RegistrationEmailAction.NeedReferenceLookup"/>, then
    ///   <see cref="RegistrationEmailAction.Conflict"/> when ANY systemuser links it (E10: binding would merge two people),
    ///   (<see cref="IdentityCollisionReason.LinkedToOtherUser"/>), else <see cref="RegistrationEmailAction.BindExisting"/>.</item>
    ///   <item>A read that failed → <see cref="RegistrationEmailAction.Deny"/>.</item>
    /// </list>
    /// </summary>
    public static RegistrationEmailDecision DecideRegistrationEmail(
        ContactLookup emailLookup, Guid callerOid, ReferenceLookup? references = null)
    {
        ArgumentNullException.ThrowIfNull(emailLookup);

        if (emailLookup.Status != LookupStatus.Read)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.Deny, DenyCode: LookupDenyCode(emailLookup.Status));
        }

        var rows = emailLookup.Rows.Where(r => r.IsActive && r.ContactId != Guid.Empty).ToList();
        if (rows.Count == 0)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.Create);
        }

        if (rows.Count > 1)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.Conflict,
                FlagContactIds: rows.Select(r => r.ContactId).Distinct().ToList(),
                FlagReason: IdentityCollisionReason.EmailAmbiguous);
        }

        var row = rows[0];
        var binding = row.Binding;
        if (binding.Kind == BindingKind.Bound && callerOid != Guid.Empty && binding.Oid == callerOid)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.AlreadyBoundToCaller, row.ContactId);
        }

        if (binding.Kind != BindingKind.Unbound)
        {
            // Flagged by CAUSE (not EmailAmbiguous): CollisionStillHolds keeps EmailAmbiguous only while 2+ active
            // contacts carry the email, so a single match flagged EmailAmbiguous would be cleared by the next
            // reconciliation run although nothing changed.
            return new RegistrationEmailDecision(RegistrationEmailAction.Conflict, row.ContactId,
                FlagContactIds: new[] { row.ContactId },
                FlagReason: binding.Kind == BindingKind.Bound
                    ? IdentityCollisionReason.BoundToDifferentOid
                    : IdentityCollisionReason.BindingUnreadable);
        }

        if (references is null)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.NeedReferenceLookup, row.ContactId);
        }

        if (references.Status != LookupStatus.Read)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.Deny, row.ContactId,
                DenyCode: LookupDenyCode(references.Status));
        }

        return references.References.Any(r => r.LinkedContactId == row.ContactId)
            ? new RegistrationEmailDecision(RegistrationEmailAction.Conflict, row.ContactId,
                FlagContactIds: new[] { row.ContactId }, FlagReason: IdentityCollisionReason.LinkedToOtherUser)
            : new RegistrationEmailDecision(RegistrationEmailAction.BindExisting, row.ContactId);
    }

    /// <summary>
    /// Registration (r3 task 047, fix round 2; owner 2026-10-15 "Refuse, flag for admin"): consulted ONLY when
    /// <see cref="DecideRegistrationEmail"/> found NO active match (<see cref="RegistrationEmailAction.Create"/>). An INACTIVE
    /// contact carrying the email refuses the registration: <see cref="RegistrationEmailAction.Conflict"/> over every
    /// inactive match, reason <see cref="IdentityCollisionReason.InactiveContactEmailMatch"/>. None → create as before.
    /// A read that failed denies (a create is never reached when an inactive match cannot be ruled out). Pure.
    /// </summary>
    public static RegistrationEmailDecision DecideInactiveRegistrationEmail(ContactLookup inactiveLookup)
    {
        ArgumentNullException.ThrowIfNull(inactiveLookup);

        if (inactiveLookup.Status != LookupStatus.Read)
        {
            return new RegistrationEmailDecision(RegistrationEmailAction.Deny, DenyCode: LookupDenyCode(inactiveLookup.Status));
        }

        var rows = inactiveLookup.Rows.Where(r => !r.IsActive && r.ContactId != Guid.Empty).ToList();
        return rows.Count == 0
            ? new RegistrationEmailDecision(RegistrationEmailAction.Create)
            : new RegistrationEmailDecision(RegistrationEmailAction.Conflict,
                FlagContactIds: rows.Select(r => r.ContactId).Distinct().ToList(),
                FlagReason: IdentityCollisionReason.InactiveContactEmailMatch);
    }

    /// <summary>The two workforce planes (Spaarke-tenant and customer home directory): an employee's sign-in, not a CIAM account.</summary>
    private static bool IsWorkforcePlane(IdentityPlaneMarker? plane)
        => plane is IdentityPlaneMarker.Workforce or IdentityPlaneMarker.WorkforceHome;

    private static bool SameEmail(string? stored, string wanted)
        => !string.IsNullOrWhiteSpace(stored) && string.Equals(stored.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="raw"/> is a non-zero GUID equal to <paramref name="expected"/> (compared parsed, not as text).</summary>
    private static bool IsGuid(string? raw, Guid expected)
        => expected != Guid.Empty && Guid.TryParse(raw?.Trim(), out var value) && value == expected;

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
