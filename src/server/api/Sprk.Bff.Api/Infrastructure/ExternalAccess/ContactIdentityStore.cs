// unified-access-control-r2 task 141 — the Dataverse reads and writes behind the identity binding.
//
// ONE store, Web API, parameterized by environment URL, so the same code serves the BFF's own environment
// (Dataverse:ServiceUrl) AND RegistrationDataverseService's target environment (a demo/customer org that is
// not the default one — the systemuser it creates must get its contact link in the SAME environment).
//
// The interface is the testing seam (ADR-010): the binder, the job and the endpoints are exercised against an
// in-memory store; HTTP doubles are banned (ADR-038 ban B1), so the HTTP shapes are pinned through the PURE
// builders/parsers below instead.
//
// App-only, broker-only (ADR-028): every call uses the BFF's own TokenCredential (managed identity) — never
// the caller's token, no OBO, no Graph, no .WithClientSecret.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>A systemuser as the link decision needs it.</summary>
public sealed record SystemUserIdentityRow(
    Guid SystemUserId,
    Guid? Oid,
    string? InternalEmail,
    string? DomainName,
    Guid? PrimaryContactId,
    string? ETag,
    ContactBindingRow? LinkedContact = null,
    string? FirstName = null,
    string? LastName = null);

/// <summary>A systemuser lookup.</summary>
public sealed record SystemUserLookup(LookupStatus Status, SystemUserIdentityRow? Row)
{
    /// <summary>The systemuser could not be read.</summary>
    public static SystemUserLookup Failed { get; } = new(LookupStatus.Failed, null);
}

/// <summary>Contact details for a contact created keyed by an oid.</summary>
public sealed record NewContactDetails(string? FirstName, string LastName, string? Email)
{
    /// <summary>ADR-028 A9 (r3 task 047): the registration form's optional department.</summary>
    public string? Department { get; init; }

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the home directory (<c>tid</c>), written as <c>sprk_externaltenantid</c>. Required on plane
    /// WorkforceHome and refused on every other plane, the Workforce plane included
    /// (<see cref="DataverseContactIdentityStore.CreatePayload"/>).
    /// </summary>
    public Guid? ExternalTenantId { get; init; }

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the owning team (the default team of <c>ExternalAccess:CustomerBusinessUnitId</c>).
    /// Required on plane WorkforceHome. ISS-005 (#1673): carried by the Workforce plane's creates (first sign-in and the
    /// licensed-user link) too, which the binder resolves from the same setting. Refused on every other plane (the CIAM
    /// create sets no owner).
    /// </summary>
    public Guid? OwningTeamId { get; init; }
}

/// <summary>ADR-028 A9 (r3 task 047): a business unit's default owner teams (two rows, so ambiguity is visible).</summary>
public sealed record OwnerTeamLookup(LookupStatus Status, IReadOnlyList<Guid> TeamIds)
{
    /// <summary>A team read that could not be read.</summary>
    public static OwnerTeamLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<Guid>());
}

/// <summary>ADR-028 A9 (r3 task 047): the business units carrying a name (two rows, so ambiguity is visible).</summary>
public sealed record BusinessUnitLookup(LookupStatus Status, IReadOnlyList<Guid> BusinessUnitIds)
{
    /// <summary>A business-unit read that could not be read.</summary>
    public static BusinessUnitLookup Failed { get; } = new(LookupStatus.Failed, Array.Empty<Guid>());
}

/// <summary>How a store write ended.</summary>
public enum StoreWriteStatus
{
    /// <summary>Written.</summary>
    Written,

    /// <summary>The row changed since it was read (HTTP 412 on <c>If-Match</c>) or already exists (create-only).</summary>
    PreconditionFailed,

    /// <summary>The target row does not exist.</summary>
    NotFound,

    /// <summary>
    /// The create needs the alternate key on the uniqueness mirror (<c>sprk_externalobjectidkey</c>) and it is not
    /// defined in this environment.
    /// </summary>
    KeyMissing,

    /// <summary>Any other failure.</summary>
    Failed,

    /// <summary>
    /// Dataverse refused the write for a missing PRIVILEGE (HTTP 403, or a 400 that names a privilege): the identity's
    /// roles do not allow it (e.g. the owner team holds no role with contact read, or the application user cannot
    /// Assign). A permanent configuration fault — retrying changes nothing. See <see cref="DataverseContactIdentityStore.IsPermissionFault"/>.
    /// </summary>
    Forbidden,

    /// <summary>
    /// A contact CREATE was refused because the OWNER it names may not own the row: Dataverse's
    /// <c>0x80040299</c> "Read Privilege Check For Owner failed", raised when none of the owner team's security roles holds
    /// Read on <c>contact</c> (ISS-005 UAC re-check K1, #1673). A permanent configuration fault of the customer business
    /// unit's default team, so the create routes answer it as <c>sdap.access.registration_owner_unconfigured</c>, not as a
    /// generic failure. See <see cref="DataverseContactIdentityStore.IsOwnerPrivilegeFault"/>. Only a POST returns it.
    /// </summary>
    OwnerPrivilegeMissing,

    /// <summary>
    /// The write would put the oid into the uniqueness mirror while ANOTHER contact already holds it — Dataverse's
    /// duplicate-key fault on the alternate key's unique index (<c>0x80060892</c>, HTTP 412). Not a version race:
    /// re-reading and retrying changes nothing, so the binder denies and flags the holder (owner round 4 item 4, B2).
    /// </summary>
    KeyConflict,

    /// <summary>
    /// A written column is not provisioned in this environment. Returned ONLY by the pending-invite marker write
    /// (<see cref="IContactIdentityStore.MarkInvitedAsync"/>, tasks 184 and 185: the marker OR its invite-code hash
    /// column), whose caller proceeds without the marker on a BFF deployed before the schema; every other write reports a
    /// missing column as <see cref="Failed"/>.
    /// </summary>
    ColumnMissing,
}

/// <summary>A store write's result; <see cref="ContactId"/> is set by a successful create.</summary>
public sealed record StoreWriteResult(StoreWriteStatus Status, Guid? ContactId = null, string? Error = null)
{
    /// <summary>A plain success.</summary>
    public static StoreWriteResult Written { get; } = new(StoreWriteStatus.Written);
}

/// <summary>
/// The invite stamp written with the pending-invite marker: the salted hash of the address the invite was sent to
/// (<see cref="InviteEmailStamp"/>, never the address) and the Entra tenant the invite named, or null for none (which clears
/// an earlier one). Every email bind onto the contact needs it (<see cref="ContactBindingDecision.CheckInviteStamp"/>).
/// </summary>
public sealed record InviteStamp(string InvitedEmailHash, Guid? InvitedTenantId)
{
    /// <summary>Never prints the hash.</summary>
    public override string ToString() => $"InviteStamp {{ InvitedTenantId = {InvitedTenantId?.ToString("D") ?? "(none)"} }}";
}

/// <summary>Whether this identity can READ the binding column (field-level security).</summary>
public enum BindingReadability
{
    /// <summary>At least one marked row came back with its oid: readable.</summary>
    Readable,

    /// <summary>No row carries a plane marker yet, so masking cannot be distinguished — nothing to protect.</summary>
    NoMarkedRows,

    /// <summary>Every marked row came back with no oid: the column is masked. No bind, create or link is safe.</summary>
    Masked,

    /// <summary>The probe itself could not be read (transient: a throttle, a 5xx, a network fault). Retryable.</summary>
    Failed,

    /// <summary>
    /// The home probe selected a column that is not provisioned here (the A9 schema is not applied). A permanent
    /// configuration fault, not a transient read failure; only <see cref="IContactIdentityStore.ProbeHomeReadabilityAsync"/> reports it.
    /// </summary>
    ColumnMissing,
}

/// <summary>One page of a scan, and the continuation for the next page (null when done).</summary>
public sealed record StorePage<T>(LookupStatus Status, IReadOnlyList<T> Rows, string? Continuation, string? Error = null);

/// <summary>Slice C: what the live read of a contact's removed marker says. Only <see cref="NotRemoved"/> composes anything.</summary>
public enum RemovedMarkerRead
{
    /// <summary>The marker, or the row, could not be read, or an empty marker's read is not proven. Composes nothing.</summary>
    Unreadable = 0,

    /// <summary>The marker read empty on a proven read: the person's external access was not removed.</summary>
    NotRemoved = 1,

    /// <summary>The marker is set: an administrator removed the person's external access. Composes nothing.</summary>
    Removed = 2,
}

/// <summary>The identity-binding reads and writes. Testing seam (ADR-010).</summary>
public interface IContactIdentityStore
{
    /// <summary>Contacts carrying <paramref name="oid"/> — ANY statecode, two rows.</summary>
    Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct);

    /// <summary>
    /// Contacts whose uniqueness MIRROR (<c>sprk_externalobjectidkey</c>) carries <paramref name="oid"/> — any
    /// statecode, two rows. DIAGNOSTIC ONLY: it names the holder of a key conflict so the holder can be flagged.
    /// Nothing resolves or binds by it; who a contact IS is read from the binding column alone.
    /// </summary>
    Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct);

    /// <summary>ACTIVE contacts whose <c>emailaddress1</c> is <paramref name="email"/>, two rows.</summary>
    Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 047, fix round 2): INACTIVE contacts (<c>statecode eq 1</c>) whose <c>emailaddress1</c> is
    /// <paramref name="email"/>, two rows. Read-only; consulted by the registration route only when no ACTIVE contact
    /// carries the email, so a deactivated contact is refused and flagged instead of a second contact being created.
    /// </summary>
    Task<ContactLookup> FindInactiveContactsByEmailAsync(string email, CancellationToken ct);

    /// <summary>One contact by id; a missing row reads as zero rows.</summary>
    Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Slice C: whether an administrator removed <paramref name="contactId"/>'s external access, read LIVE. An empty marker is
    /// <see cref="RemovedMarkerRead.NotRemoved"/> only on a proven read; an unproven or faulted read, or a missing row, is
    /// <see cref="RemovedMarkerRead.Unreadable"/> (fail closed). The composition's check; it never caches an answer.
    /// </summary>
    Task<RemovedMarkerRead> ReadRemovedMarkerAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Slice C (administrator removal and reinstatement): writes the removed marker
    /// (<c>sprk_externalaccessremovedon</c> = <paramref name="removedOn"/>, UTC), or clears it when <paramref name="removedOn"/>
    /// is null. One PATCH naming that column only, <c>If-Match: *</c>, so a missing row is never created. Nothing else on the
    /// row is written: the binding, the statecode and every other column are left as they are.
    /// </summary>
    Task<StoreWriteResult> WriteRemovedMarkerAsync(Guid contactId, DateTimeOffset? removedOn, CancellationToken ct);

    /// <summary>
    /// Slice C5: the removed-marker write of <see cref="WriteRemovedMarkerAsync"/> (set, or cleared when
    /// <paramref name="removedOn"/> is null), made ONLY on the row as it was read: <c>If-Match</c> on <paramref name="etag"/>,
    /// the row version read with the decision. Never <c>*</c>; no row version, no write. A row changed since that read is
    /// <see cref="StoreWriteStatus.PreconditionFailed"/> and nothing is written.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md §11. Existing: <see cref="WriteRemovedMarkerAsync"/> writes the same column with <c>If-Match: *</c>.
    /// Extension: that write serves an administrator's explicit removal, which is meant to land whatever the row holds; a
    /// null-means-<c>*</c> parameter on it would let a missing version turn into an unconditional write. Cost of doing nothing:
    /// the removal sweep marks a contact that was reactivated after its decision read, and a reinstatement clears a marker
    /// on a row whose state it did not read.
    /// </remarks>
    Task<StoreWriteResult> WriteRemovedMarkerIfUnchangedAsync(
        Guid contactId, string? etag, DateTimeOffset? removedOn, CancellationToken ct);

    /// <summary>
    /// Slice C (administrator removal): clears the invite state (<c>sprk_externalinvitedon</c>,
    /// <c>sprk_externalinvitecodehash</c>, <c>sprk_externalinvitedemailhash</c>, <c>sprk_externalinvitedtenantid</c>) and
    /// <c>sprk_accesspermissiongrant</c> in ONE PATCH, <c>If-Match: *</c>, so no outstanding invite link or stamp can bind.
    /// The binding columns (the object id, its plane and its key mirror) are never named, so the person is still found by
    /// their object id and refused.
    /// </summary>
    Task<StoreWriteResult> ClearRemovedPersonAccessStateAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Slice C (administrator removal): clears <c>sprk_standinggrant</c> in a PATCH of its own, <c>If-Match: *</c>. The
    /// application user may update that column only through its direct membership of the standing-grant administrators
    /// profile; without it Dataverse refuses the write and the result says so (never reported as written).
    /// </summary>
    Task<StoreWriteResult> ClearStandingGrantAsync(Guid contactId, CancellationToken ct);

    /// <summary>Systemusers (any state) whose <c>sprk_primarycontact</c> is one of <paramref name="contactIds"/>.</summary>
    Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct);

    /// <summary>One systemuser with its link.</summary>
    Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct);

    /// <summary>The field-level-security masking probe.</summary>
    Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 046): systemusers whose stored home link — the FIELD-SECURED pair <c>sprk_hometenantid</c> /
    /// <c>sprk_homeobjectid</c> — is (<paramref name="homeTenantId"/>, <paramref name="homeObjectId"/>). Any state, two rows.
    /// Read-only. Never filters on the unsecured key mirrors and never on <c>azureactivedirectoryobjectid</c>.
    /// </summary>
    Task<HomeLinkLookup> FindHomeLinkedSystemUsersAsync(Guid homeTenantId, Guid homeObjectId, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 048): the systemusers whose <c>azureactivedirectoryobjectid</c> is <paramref name="guestObjectId"/>
    /// — a SPAARKE-TENANT object id, proven by a validated guest token, never a home oid. Any state, two rows, enabled first,
    /// each with its stored home pair (the SECURED columns) and row version. Read-only; the link route's target.
    /// </summary>
    Task<HomeLinkLookup> FindSystemUsersByGuestObjectIdAsync(Guid guestObjectId, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 048): writes the stored home link — <c>sprk_hometenantid</c>, <c>sprk_homeobjectid</c> and their
    /// key mirrors <c>sprk_hometenantidkey</c>, <c>sprk_homeobjectidkey</c>, the same values — in ONE request, conditional on
    /// <paramref name="etag"/> (no version → no write). A pair another systemuser holds fails on the mirrors' alternate key
    /// (<see cref="StoreWriteStatus.KeyConflict"/>).
    /// </summary>
    Task<StoreWriteResult> WriteHomeLinkAsync(
        Guid systemUserId, string? etag, Guid homeTenantId, Guid homeObjectId, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 046): contacts on plane WorkforceHome whose <c>sprk_externaltenantid</c> is
    /// <paramref name="homeTenantId"/> and whose binding is <paramref name="homeObjectId"/>. Any state, two rows. Read-only.
    /// </summary>
    Task<ContactLookup> FindHomeContactsAsync(Guid homeTenantId, Guid homeObjectId, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 047): ENABLED systemusers whose <c>internalemailaddress</c> OR <c>domainname</c> equals
    /// <paramref name="email"/> (owner 2026-10-15), two rows. Read-only; the registration route's licensed-user refusal.
    /// </summary>
    Task<SystemUserEmailLookup> FindEnabledSystemUsersByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 047, fix round 1): the business units named <paramref name="name"/>, two rows. Read-only. The
    /// registration create's guard that <c>ExternalAccess:CustomerBusinessUnitId</c> is not the Secure Record business unit
    /// (the same name rule and filter as <c>RecordOwnershipResolver</c>: <c>SecureRecordOwnerTeam</c>).
    /// </summary>
    Task<BusinessUnitLookup> FindBusinessUnitsByNameAsync(string name, CancellationToken ct);

    /// <summary>ADR-028 A9 (r3 task 047): the default (owner) team of <paramref name="businessUnitId"/>, two rows. Read-only.</summary>
    Task<OwnerTeamLookup> FindDefaultOwnerTeamsAsync(Guid businessUnitId, CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 047): can this identity READ the A9 field-secured columns (<c>contact.sprk_externaltenantid</c>,
    /// <c>systemuser.sprk_hometenantid</c> / <c>sprk_homeobjectid</c>)? A masked column reads as "no match" in the 046
    /// reads, so the registration route probes this before any create or bind and denies unless it is readable.
    /// </summary>
    Task<BindingReadability> ProbeHomeReadabilityAsync(CancellationToken ct);

    /// <summary>
    /// ADR-028 A9 (r3 task 047): binds an existing, unbound contact to a home identity — the binding, the uniqueness mirror,
    /// plane WorkforceHome and <c>sprk_externaltenantid</c>, in ONE request, conditional on <paramref name="etag"/> (no
    /// version → no write).
    /// </summary>
    Task<StoreWriteResult> BindHomeOidAsync(Guid contactId, string? etag, Guid homeTenantId, Guid oid, CancellationToken ct);

    /// <summary>
    /// Tasks 184 and 185 — this environment's intent for <paramref name="contactId"/>, for E11: its pending-invite marker
    /// (<c>sprk_externalinvitedon</c>), the stored invite-code hash (<c>sprk_externalinvitecodehash</c>) and the invite stamp
    /// (<c>sprk_externalinvitedemailhash</c>, <c>sprk_externalinvitedtenantid</c>) with the contact's current
    /// <c>emailaddress1</c> and row version, read together in ONE query of their own (never in <c>ContactSelect</c>), so a
    /// missing column reads as <see cref="LookupStatus.ColumnMissing"/> here and nowhere else. An email hash read EMPTY while
    /// the BFF's read of the stamp columns is not proven is marked (<see cref="BindIntentLookup.StampColumnsUnproven"/>). No
    /// grant is read (task 185, option (c)): a grant is no bind evidence.
    /// </summary>
    Task<BindIntentLookup> FindBindIntentAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Tasks 184 and 185 — writes the pending-invite marker (<c>sprk_externalinvitedon</c> = <paramref name="invitedOn"/>,
    /// UTC) AND the new invite's stored code hash (<c>sprk_externalinvitecodehash</c> = <paramref name="inviteCodeHash"/>)
    /// onto an existing contact in ONE request, so a re-invite replaces both together. Conditional on
    /// <paramref name="etag"/> (never <c>*</c>; no row version, no write — #2005: a contact bound since it was read must
    /// never receive a marker or a hash). The same request writes the invite stamp (<paramref name="stamp"/>: the email hash
    /// and the invited tenant, null clearing an earlier one). Writes nothing else. <see cref="StoreWriteStatus.ColumnMissing"/>
    /// when any of the four columns is not provisioned here.
    /// <para>A portal-account invite carries a code hash and names no tenant; a work-account invite names its resolved
    /// tenant and carries no code hash (<paramref name="inviteCodeHash"/> null clears an earlier one). Any other pairing
    /// throws before anything is sent (<see cref="DataverseContactIdentityStore.InviteMarkerPayload"/>).</para>
    /// </summary>
    Task<StoreWriteResult> MarkInvitedAsync(
        Guid contactId, string? etag, DateTimeOffset invitedOn, string? inviteCodeHash, InviteStamp stamp, CancellationToken ct);

    /// <summary>
    /// Slice C2b: retracts an invite whose email did not go out. Clears the pending-invite marker
    /// (<c>sprk_externalinvitedon</c>), the invite-code hash and the invite stamp (email hash and invited tenant) in ONE
    /// request, conditional on <paramref name="etag"/> (never <c>*</c>; no row version, no write), so an invite written
    /// after that version is never cleared. Writes nothing else: never a binding column or the access baseline.
    /// </summary>
    Task<StoreWriteResult> ClearPendingInviteAsync(Guid contactId, string? etag, CancellationToken ct);

    /// <summary>
    /// Writes the oid (D format) into the binding AND the uniqueness mirror, with the plane, in ONE request,
    /// conditional on <paramref name="etag"/>. Another contact holding the oid in its mirror makes the platform
    /// refuse the write (<see cref="StoreWriteStatus.KeyConflict"/>). It never names the invite-code hash column, so it
    /// is used only where that column is KNOWN to be absent (the invite's marker-and-hash write answered
    /// <see cref="StoreWriteStatus.ColumnMissing"/>); every other bind is <see cref="BindOidAndConsumeInviteAsync"/>.
    /// </summary>
    Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct);

    /// <summary>
    /// Task 185 — <see cref="BindOidAsync"/>'s bind PLUS <c>sprk_externalinvitecodehash = null</c> and the invite stamp
    /// cleared (<c>sprk_externalinvitedemailhash</c>, <c>sprk_externalinvitedtenantid</c> = null), in ONE request conditional
    /// on <paramref name="etag"/> (never <c>*</c>; no row version, no write). The invite code and stamp are consumed by the
    /// same write that binds the oid, so they are single-use. The marker is not touched.
    /// <para>
    /// #2005 — EVERY bind of an oid onto an existing contact uses this, on every plane (CIAM, workforce, systemuser), so a
    /// bound contact never holds an unused code hash and an operator unbind cannot revive an earlier invite link. Where the
    /// hash column is not provisioned the platform refuses the whole PATCH as a missing property; nothing was written, so
    /// the plain bind is sent under the SAME row version and its result is returned (there is no hash to consume).
    /// </para>
    /// </summary>
    Task<StoreWriteResult> BindOidAndConsumeInviteAsync(
        Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct);

    /// <summary>
    /// Creates a contact for <paramref name="oid"/> — one POST carrying the binding, the uniqueness mirror and the
    /// plane. Another contact holding the oid in its mirror makes the key's unique index refuse the create
    /// (<see cref="StoreWriteStatus.KeyConflict"/>).
    /// </summary>
    Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct);

    /// <summary>Sets <c>systemuser.sprk_primarycontact</c>, conditional on <paramref name="etag"/>.</summary>
    Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct);

    /// <summary>
    /// Writes a collision flag (every party) onto a contact, conditional on <paramref name="etag"/> so a party
    /// another writer appended meanwhile is not overwritten (412 → the caller re-reads and appends again).
    /// </summary>
    Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct);

    /// <summary>
    /// Clears a contact's collision flag, conditional on <paramref name="etag"/> — the version the clearing
    /// decision was made on. Without a version nothing is cleared: a party appended after that read must survive.
    /// </summary>
    Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct);

    /// <summary>One page of enabled interactive systemusers with their linked contact.</summary>
    Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct);

    /// <summary>One page of contacts carrying an open collision flag.</summary>
    Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct);
}

/// <summary>
/// The Web API implementation of <see cref="IContactIdentityStore"/> for one Dataverse environment.
/// </summary>
public sealed class DataverseContactIdentityStore : IContactIdentityStore
{
    /// <summary>The named HttpClient (pooled by <see cref="IHttpClientFactory"/>, safe in a singleton).</summary>
    public const string HttpClientName = "ContactIdentityStore";

    /// <summary>Rows per scan page (<c>Prefer: odata.maxpagesize</c>).</summary>
    public const int ScanPageSize = 500;

    // Column names. The flag columns are this task's schema (scripts/Set-ContactIdentityBindingSchema.ps1).
    public const string FlagOnColumn = "sprk_identitycollisionon";
    public const string FlagOidColumn = "sprk_identitycollisionoid";
    public const string FlagPlaneColumn = "sprk_identitycollisionplane";
    public const string FlagReasonColumn = "sprk_identitycollisionreason";

    /// <summary>Every recorded party of the flag, as JSON (multi-line text). The four columns above are the first.</summary>
    public const string FlagPartiesColumn = "sprk_identitycollisionparties";

    /// <summary>
    /// The contact columns every binding read selects. The uniqueness mirror is selected so a squatted or
    /// half-cleared mirror can be told apart (<see cref="ContactBindingDecision.MirrorHeldWithoutBinding"/>); it is
    /// never what a contact resolves by.
    /// </summary>
    public static readonly string ContactSelect = string.Join(",",
        "contactid", "statecode", "emailaddress1",
        ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
        ContactBindingDecision.KeyMirrorColumn,
        FlagOnColumn, FlagOidColumn, FlagPlaneColumn, FlagReasonColumn, FlagPartiesColumn,
        ContactBindingDecision.ExternalAccessRemovedOnColumn);

    /// <summary>
    /// Slice C: the removed marker's field-secured column (its own readability proof). An EMPTY marker is trusted only while
    /// the BFF's read of it is proven; a row whose marker read empty without that proof is marked
    /// (<see cref="ContactBindingRow.RemovedMarkerUnproven"/>) and counts as removed.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedMarkerColumns = new[]
    {
        ContactBindingDecision.ExternalAccessRemovedOnColumn,
    };

    /// <summary>
    /// The field-secured binding columns: the binding and its plane. An EMPTY plane is trusted only while the BFF's read of
    /// both is proven (<see cref="SecuredColumnReadProof"/>); a row whose plane read empty without that proof is marked
    /// (<see cref="ContactBindingRow.IdentityColumnsUnproven"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> BindingColumns = new[]
    {
        ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
    };

    /// <summary>
    /// The five field-secured collision-flag columns. A flag read while the BFF's read of all five is not proven may be
    /// hidden in whole or in part, so it is never overwritten: the row is marked
    /// (<see cref="ContactBindingRow.CollisionColumnsUnproven"/>) and its flag reads as one whose parties cannot be read.
    /// The proof is per column set: the binding columns and these are proven separately.
    /// </summary>
    public static readonly IReadOnlyList<string> CollisionColumns = new[]
    {
        FlagOnColumn, FlagOidColumn, FlagPlaneColumn, FlagReasonColumn, FlagPartiesColumn,
    };

    /// <summary>
    /// Every field-secured identity column a binding read decides on (<see cref="BindingColumns"/> and
    /// <see cref="CollisionColumns"/>). The masking probe reads as Masked unless the BFF's read of all of them is proven.
    /// </summary>
    public static readonly IReadOnlyList<string> IdentityColumns = BindingColumns.Concat(CollisionColumns).ToArray();

    /// <summary>The table the identity columns live on.</summary>
    public const string ContactTable = "contact";

    /// <summary>The systemuser columns the link decision selects.</summary>
    public const string SystemUserSelect =
        "systemuserid,firstname,lastname,azureactivedirectoryobjectid,internalemailaddress,domainname,_sprk_primarycontact_value";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<CancellationToken, Task<string>> _getToken;
    private readonly string _apiUrl;
    private readonly ILogger _logger;

    // The proofs that this store's identity can READ the binding columns and the collision columns, made over THIS store's
    // environment and token (a store may target another environment than the BFF's own). Each is held for
    // SecuredColumnReadProof.ProofLifetime; a store built by ContactIdentityBinderFactory shares them with every other store
    // the factory builds for the same environment.
    private readonly SecuredColumnReadProof _bindingColumnsProof;
    private readonly SecuredColumnReadProof _collisionColumnsProof;

    // The proof that this store's identity can READ the invite stamp's columns: an email hash read empty without it may be a
    // hidden one. Either way an empty stamp is no stamp (the bind is refused); the proof names the reason for the operator.
    private readonly SecuredColumnReadProof _inviteStampColumnsProof;

    // Slice C: the proof that this store's identity can READ the removed marker. A marker read empty without it may be a
    // hidden one, so the row counts as removed (fail closed).
    private readonly SecuredColumnReadProof _removedMarkerProof;

    /// <summary>The store for an environment, authenticated by <paramref name="getToken"/>.</summary>
    public DataverseContactIdentityStore(
        IHttpClientFactory httpClientFactory,
        Func<CancellationToken, Task<string>> getToken,
        string dataverseBaseUrl,
        ILogger logger,
        TimeProvider? time = null)
        : this(httpClientFactory, getToken, dataverseBaseUrl, logger,
            (source, table, columns) => new SecuredColumnReadProof(source, table, columns, logger, time))
    {
    }

    /// <summary>
    /// The store for an environment, whose readability proofs come from <paramref name="readProofs"/> (given this store's
    /// own permission source, the table and the column set): the seam through which
    /// <see cref="ContactIdentityBinderFactory"/> shares one proof per environment and column set across its stores.
    /// </summary>
    internal DataverseContactIdentityStore(
        IHttpClientFactory httpClientFactory,
        Func<CancellationToken, Task<string>> getToken,
        string dataverseBaseUrl,
        ILogger logger,
        Func<IColumnReadPermissionSource, string, IReadOnlyList<string>, SecuredColumnReadProof> readProofs)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataverseBaseUrl);
        _apiUrl = $"{dataverseBaseUrl.TrimEnd('/')}/api/data/v9.2";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(readProofs);
        var source = new WebApiColumnReadPermissionSource(this);
        _bindingColumnsProof = readProofs(source, ContactTable, BindingColumns);
        _collisionColumnsProof = readProofs(source, ContactTable, CollisionColumns);
        _inviteStampColumnsProof = readProofs(source, ContactTable, InviteStampColumns);
        _removedMarkerProof = readProofs(source, ContactTable, RemovedMarkerColumns);
    }

    /// <summary>
    /// The store for the BFF's own environment (<c>Dataverse:ServiceUrl</c>) under the BFF's managed identity.
    /// </summary>
    public static DataverseContactIdentityStore ForDefaultEnvironment(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<DataverseContactIdentityStore> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(configuration);
        var baseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required for the identity binding store.");
        var tokens = new CachedTokenSource(credential, $"{baseUrl.TrimEnd('/')}/.default");
        return new DataverseContactIdentityStore(httpClientFactory, tokens.GetAsync, baseUrl, logger);
    }

    // ── Pure builders (asserted directly; ADR-038 bans HTTP doubles) ──────────────────────────────────

    /// <summary>OData string literal: single quotes doubled, then URL-encoded.</summary>
    public static string Literal(string value) => Uri.EscapeDataString(value.Replace("'", "''"));

    /// <summary>Contacts carrying the oid — any state, two rows (an ambiguous oid must be visible).</summary>
    public static string BuildOidLookupPath(Guid oid)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter={ContactBindingDecision.ExternalObjectIdColumn} eq '{oid:D}'&$top=2";

    /// <summary>
    /// Contacts whose uniqueness mirror carries the oid — any state, two rows. Diagnostic: names a key conflict's
    /// holder (<see cref="IContactIdentityStore.FindContactsByKeyMirrorAsync"/>).
    /// </summary>
    public static string BuildKeyMirrorLookupPath(Guid oid)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter={ContactBindingDecision.KeyMirrorColumn} eq '{oid:D}'&$top=2";

    /// <summary>ACTIVE contacts by email, two rows (TopCount 1 made ambiguity invisible — task 013).</summary>
    public static string BuildEmailLookupPath(string email)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter=emailaddress1 eq '{Literal(email)}' and statecode eq 0&$top=2";

    /// <summary>
    /// INACTIVE contacts by email, two rows (r3 task 047, fix round 2). The pair of <see cref="BuildEmailLookupPath"/>:
    /// <c>statecode eq 1</c> instead of <c>eq 0</c>, same email literal and same selected columns. Slice C2b: removed
    /// contacts first (the removed marker descending; an empty marker sorts last), so the two rows read always include a
    /// removed contact for the email when one exists, however many inactive contacts carry it.
    /// </summary>
    public static string BuildInactiveEmailLookupPath(string email)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter=emailaddress1 eq '{Literal(email)}' and statecode eq 1"
           + $"&$orderby={ContactBindingDecision.ExternalAccessRemovedOnColumn} desc&$top=2";

    /// <summary>Systemusers linking any of the contacts — any state, so a disabled user's link still counts.</summary>
    public static string BuildReferenceLookupPath(IReadOnlyCollection<Guid> contactIds)
        => $"systemusers?$select=systemuserid,azureactivedirectoryobjectid,_sprk_primarycontact_value"
           + $"&$filter=({string.Join(" or ", contactIds.Select(id => $"_sprk_primarycontact_value eq {id:D}"))})"
           + "&$top=50";

    /// <summary>
    /// Tasks 184 and 185 — the pending-invite marker of one contact and its stored invite-code hash, in their own query
    /// (never in <see cref="ContactSelect"/>). The contact id is selected too, so the answer is checked to be about this
    /// contact.
    /// </summary>
    public static string BuildInviteMarkerPath(Guid contactId)
        => $"contacts({contactId:D})?$select=contactid,{ContactBindingDecision.ExternalInvitedOnColumn},"
           + $"{ContactBindingDecision.ExternalInviteCodeHashColumn},emailaddress1,"
           + $"{ContactBindingDecision.ExternalInvitedEmailHashColumn},{ContactBindingDecision.ExternalInvitedTenantIdColumn}";

    /// <summary>
    /// The invite stamp's part of the marker read's answer (<see cref="BuildInviteMarkerPath"/>), from a body
    /// <see cref="InterpretMarkerLeg"/> already read as this contact's row: the stored email hash, the invited tenant (raw
    /// text), the contact's current <c>emailaddress1</c> and the row version (<c>@odata.etag</c>). Each is null when absent
    /// or not a string. Pure; the decision interprets them.
    /// </summary>
    public static (string? InvitedEmailHash, string? RawInvitedTenantId, string? CurrentEmail, string? ETag) InterpretStampLeg(
        string? body)
    {
        using var doc = body is null ? null : TryParseJson(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null);
        }

        var row = doc.RootElement;
        return (StringOf(row, ContactBindingDecision.ExternalInvitedEmailHashColumn),
            StringOf(row, ContactBindingDecision.ExternalInvitedTenantIdColumn),
            StringOf(row, "emailaddress1"),
            StringOf(row, "@odata.etag"));
    }

    /// <summary>The invite stamp's two field-secured columns (the readability proof's column set; every bind clears both).</summary>
    public static readonly IReadOnlyList<string> InviteStampColumns = new[]
    {
        ContactBindingDecision.ExternalInvitedEmailHashColumn,
        ContactBindingDecision.ExternalInvitedTenantIdColumn,
    };

    /// <summary>
    /// Tasks 184 and 185 — what the marker leg's answer says. Pure.
    /// <list type="bullet">
    ///   <item>A fault stays that fault: <c>ColumnMissing</c> (the marker or the hash column is not provisioned — no invite
    ///   evidence; the decision denies <c>contact_not_invited</c>) or <c>Failed</c>.</item>
    ///   <item>A 404 is <c>Read</c>, no marker: the contact does not exist, so nothing invited it.</item>
    ///   <item>A body that is absent, not JSON, not an object, or about another contact (its <c>contactid</c> missing or
    ///   different) is <c>Failed</c>: an answer we cannot read is never "not invited", and never "invited".</item>
    ///   <item>Otherwise <c>Read</c>, with the stored time or null (<see cref="ParseInvitedOn"/>) and the stored hash as
    ///   text or null (the decision checks its format; never logged).</item>
    /// </list>
    /// </summary>
    public static (LookupStatus Status, DateTimeOffset? InvitedOn, string? InviteCodeHash) InterpretMarkerLeg(
        LookupStatus status, HttpStatusCode code, string? body, Guid contactId)
    {
        if (status != LookupStatus.Read)
        {
            return (status, null, null);
        }

        if (code == HttpStatusCode.NotFound)
        {
            return (LookupStatus.Read, null, null);
        }

        using var doc = body is null ? null : TryParseJson(body);
        if (doc is null
            || doc.RootElement.ValueKind != JsonValueKind.Object
            || GuidOf(doc.RootElement, "contactid") != contactId)
        {
            return (LookupStatus.Failed, null, null);
        }

        return (LookupStatus.Read, ParseInvitedOn(doc.RootElement),
            StringOf(doc.RootElement, ContactBindingDecision.ExternalInviteCodeHashColumn));
    }

    /// <summary>
    /// Tasks 184 and 185 — the marker write's body: the marker (UTC, second precision), the new invite's stored code hash
    /// AND the invite stamp (the email hash, and the invited tenant as lowercase "D" text or null), together, so a re-invite
    /// replaces all four in one request. Nothing else.
    /// <para>Exactly one of the code hash and the tenant is set. A portal-account invite carries a code hash and no tenant
    /// (it is bound by a CIAM sign-in presenting the code); a work-account invite carries its resolved tenant and no code
    /// hash, which clears an earlier one (it is bound only by a Workforce or WorkforceHome sign-in from that tenant). A
    /// tenant is never written beside a code, and a code is never cleared without a tenant: the tenant column alone says
    /// which kind of sign-in the invite is for (<see cref="ContactBindingDecision.IsWorkAccountInvite"/>). Both or neither
    /// throws.</para>
    /// </summary>
    public static Dictionary<string, object?> InviteMarkerPayload(DateTimeOffset invitedOn, string? inviteCodeHash, InviteStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp.InvitedEmailHash);
        if (stamp.InvitedTenantId == Guid.Empty)
        {
            throw new ArgumentException("An invited tenant is a non-empty GUID, or null for none.", nameof(stamp));
        }

        if (inviteCodeHash is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inviteCodeHash);
        }

        if ((inviteCodeHash is null) != (stamp.InvitedTenantId is not null))
        {
            throw new ArgumentException(
                "An invite stamp carries an invite code (a portal-account invite, no tenant) or a resolved tenant (a "
                + "work-account invite, no code): exactly one of the two.", nameof(inviteCodeHash));
        }

        return new Dictionary<string, object?>
        {
            [ContactBindingDecision.ExternalInvitedOnColumn] =
                invitedOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            [ContactBindingDecision.ExternalInviteCodeHashColumn] = inviteCodeHash,
            [ContactBindingDecision.ExternalInvitedEmailHashColumn] = stamp.InvitedEmailHash,
            [ContactBindingDecision.ExternalInvitedTenantIdColumn] = stamp.InvitedTenantId?.ToString("D"),
        };
    }

    /// <summary>The marker write's columns: a write fault naming ANY is <see cref="StoreWriteStatus.ColumnMissing"/>.</summary>
    public static readonly IReadOnlyList<string> InviteMarkerColumns = new[]
    {
        ContactBindingDecision.ExternalInvitedOnColumn,
        ContactBindingDecision.ExternalInviteCodeHashColumn,
        ContactBindingDecision.ExternalInvitedEmailHashColumn,
        ContactBindingDecision.ExternalInvitedTenantIdColumn,
    };

    /// <summary>
    /// Task 184 — the marker read's answer from a contact row: the stored UTC time, or null when the column is empty or
    /// not a date-time (an unparseable value is no evidence). Pure.
    /// </summary>
    public static DateTimeOffset? ParseInvitedOn(JsonElement row)
        => StringOf(row, ContactBindingDecision.ExternalInvitedOnColumn) is { } raw
           && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on)
            ? on
            : null;

    /// <summary>
    /// True when a WRITE's error says a payload column does not exist on the table. Dataverse words a write differently
    /// from a read (<see cref="ClassifyError"/>): the OData payload deserializer answers "The property '…' does not exist
    /// on type 'Microsoft.Dynamics.CRM.contact'" (0x80048d19); older stacks answer "Invalid property '…' was found". Both
    /// are matched together with a column name, so another column's fault is never read as one of these missing. True when
    /// the error names ANY of <paramref name="columns"/> (task 185: the marker write carries two). Pure.
    /// </summary>
    public static bool IsWriteColumnMissing(string? error, params string[] columns)
        => error is not null
           && columns.Any(column => !string.IsNullOrEmpty(column) && error.Contains(column, StringComparison.OrdinalIgnoreCase))
           && (error.Contains("does not exist on type", StringComparison.OrdinalIgnoreCase)
               || error.Contains("Invalid property", StringComparison.OrdinalIgnoreCase)
               || error.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ADR-028 A9 (r3 task 046): the stored-link read. Filters on the FIELD-SECURED pair only (design §5: a mirror edit must
    /// never redirect a sign-in), any state (a disabled link must be seen so it can deny), two rows (an ambiguous link must
    /// be visible). Selects <c>isdisabled</c> and the secured pair, which the decision re-checks. Ordered
    /// <c>isdisabled asc</c> (as <c>WorkforcePrincipalResolver.ResolveSystemUserAsync</c>): the two rows are an ENABLED-first
    /// sample, so [disabled, enabled, enabled] returns two enabled rows and denies <c>home_link_ambiguous</c> rather than
    /// resolving the second enabled one.
    /// </summary>
    public static string BuildHomeLinkLookupPath(Guid homeTenantId, Guid homeObjectId)
        => $"systemusers?$select=systemuserid,isdisabled,{ContactBindingDecision.HomeTenantIdColumn},"
           + ContactBindingDecision.HomeObjectIdColumn
           + $"&$filter={ContactBindingDecision.HomeTenantIdColumn} eq '{homeTenantId:D}'"
           + $" and {ContactBindingDecision.HomeObjectIdColumn} eq '{homeObjectId:D}'&$top=2&$orderby=isdisabled asc";

    /// <summary>The systemuser home-link uniqueness mirror (unsecured): the home probe's row filter (task 044).</summary>
    public const string HomeObjectIdKeyColumn = "sprk_homeobjectidkey";

    /// <summary>The systemuser home-tenant uniqueness mirror (unsecured), written with the link (task 044). Never read.</summary>
    public const string HomeTenantIdKeyColumn = "sprk_hometenantidkey";

    /// <summary>
    /// ADR-028 A9 (r3 task 048): the link route's target read — systemusers by their Spaarke-tenant object id (the guest
    /// token's <c>oid</c>), any state (a disabled user must be seen so it can refuse), two rows (an ambiguous id must be
    /// visible), enabled first (as <see cref="BuildHomeLinkLookupPath"/>). Selects the SECURED pair the decision re-checks.
    /// </summary>
    public static string BuildGuestSystemUserLookupPath(Guid guestObjectId)
        => $"systemusers?$select=systemuserid,isdisabled,{ContactBindingDecision.HomeTenantIdColumn},"
           + ContactBindingDecision.HomeObjectIdColumn
           // includes disabled: the link must see a disabled guest systemuser in order to refuse it;
           // ContactBindingDecision.DecideHomeLinkWrite keeps only enabled rows and denies when none is left.
           + $"&$filter=azureactivedirectoryobjectid eq {guestObjectId:D}&$top=2&$orderby=isdisabled asc";

    /// <summary>
    /// ADR-028 A9 (r3 task 048): the link write's payload — the SECURED pair and its two unsecured key mirrors, the same
    /// values (design §5). Pure.
    /// </summary>
    public static Dictionary<string, object?> HomeLinkPayload(Guid homeTenantId, Guid homeObjectId)
    {
        if (homeTenantId == Guid.Empty) throw new ArgumentException("A home link needs the home tenant.", nameof(homeTenantId));
        if (homeObjectId == Guid.Empty) throw new ArgumentException("A home link needs the home object id.", nameof(homeObjectId));
        var tenant = homeTenantId.ToString("D");
        var oid = homeObjectId.ToString("D");
        return new Dictionary<string, object?>
        {
            [ContactBindingDecision.HomeTenantIdColumn] = tenant,
            [ContactBindingDecision.HomeObjectIdColumn] = oid,
            [HomeTenantIdKeyColumn] = tenant,
            [HomeObjectIdKeyColumn] = oid,
        };
    }

    /// <summary>The owner lookup bind key on a create.</summary>
    public const string OwnerBindKey = "ownerid@odata.bind";

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the licensed-user read — ENABLED systemusers whose <c>internalemailaddress</c> OR
    /// <c>domainname</c> equals the email (owner 2026-10-15: in dev 2 of 11 enabled users differ between the two), two rows.
    /// Dataverse compares text case-insensitively; <see cref="ContactBindingDecision.DecideLicensedUserEmail"/> re-checks.
    /// </summary>
    public static string BuildLicensedUserEmailLookupPath(string email)
        => "systemusers?$select=systemuserid,isdisabled,internalemailaddress,domainname"
           + $"&$filter=isdisabled eq false and (internalemailaddress eq '{Literal(email)}' or domainname eq '{Literal(email)}')"
           + "&$top=2";

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the business unit's default team (<c>isdefault</c>, owner team type 0), two rows — the
    /// same filter as <c>RecordOwnershipResolver</c>'s default-team read.
    /// </summary>
    public static string BuildDefaultOwnerTeamPath(Guid businessUnitId)
        => $"teams?$select=teamid&$filter=_businessunitid_value eq {businessUnitId:D} and isdefault eq true and teamtype eq 0&$top=2";

    /// <summary>The business units carrying a name, two rows (the filter is <c>SecureRecordOwnerTeam.BusinessUnitFilter</c>).</summary>
    public static string BuildBusinessUnitByNamePath(string name)
        => $"businessunits?$select=businessunitid&$filter={Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.BusinessUnitFilter(name)}&$top=2";

    /// <summary>The A9 contact probe: WorkforceHome rows with their binding and home directory.</summary>
    public static string BuildHomeContactProbePath()
        => $"contacts?$select=contactid,{ContactBindingDecision.ExternalObjectIdColumn},{ContactBindingDecision.IdentityPlaneColumn},"
           + ContactBindingDecision.ExternalTenantIdColumn
           + $"&$filter={ContactBindingDecision.IdentityPlaneColumn} eq {ContactBindingDecision.WorkforceHomePlaneValue}&$top=50";

    /// <summary>
    /// The A9 systemuser probe: rows whose UNSECURED key mirror is set, with the secured pair they mirror. It also selects
    /// the tenant mirror (r3 task 048): the link write sets both mirrors, so an environment missing either column reports
    /// ColumnMissing here, before any write, rather than failing the write as a retryable fault.
    /// </summary>
    public static string BuildHomeLinkProbePath()
        => $"systemusers?$select=systemuserid,isdisabled,{ContactBindingDecision.HomeTenantIdColumn},"
           + $"{ContactBindingDecision.HomeObjectIdColumn},{HomeTenantIdKeyColumn}"
           + $"&$filter={HomeObjectIdKeyColumn} ne null&$top=50";

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the A9 masking verdict. Pure. Every A9 write sets the secured column with its mirror or
    /// plane in one request, so when EVERY marked row comes back without the secured value the column is masked from this
    /// identity. A read that failed (or a missing column) → <see cref="BindingReadability.Failed"/>.
    /// </summary>
    public static BindingReadability ClassifyHomeProbe(ContactLookup contacts, HomeLinkLookup links)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(links);
        // A missing column is a permanent fault (an operator applies the schema); any other unread status is transient.
        if (contacts.Status == LookupStatus.ColumnMissing || links.Status == LookupStatus.ColumnMissing) return BindingReadability.ColumnMissing;
        if (contacts.Status != LookupStatus.Read || links.Status != LookupStatus.Read) return BindingReadability.Failed;

        if (contacts.Rows.Count > 0 && contacts.Rows.All(r => string.IsNullOrWhiteSpace(r.RawExternalTenantId)))
        {
            return BindingReadability.Masked;
        }

        if (links.Rows.Count > 0
            && links.Rows.All(r => string.IsNullOrWhiteSpace(r.RawHomeTenantId) || string.IsNullOrWhiteSpace(r.RawHomeObjectId)))
        {
            return BindingReadability.Masked;
        }

        return contacts.Rows.Count == 0 && links.Rows.Count == 0 ? BindingReadability.NoMarkedRows : BindingReadability.Readable;
    }

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the home bind payload — <see cref="BindPayload"/> on plane WorkforceHome plus the home
    /// directory, lowercase "D" text (the 046 reads match only that form).
    /// </summary>
    public static Dictionary<string, object?> HomeBindPayload(Guid homeTenantId, Guid oid)
    {
        if (homeTenantId == Guid.Empty) throw new ArgumentException("A home bind needs the home tenant.", nameof(homeTenantId));
        var payload = BindPayload(oid, IdentityPlaneMarker.WorkforceHome);
        payload[ContactBindingDecision.ExternalTenantIdColumn] = homeTenantId.ToString("D");
        return payload;
    }

    /// <summary>
    /// ADR-028 A9 (r3 task 047, UAC review F1): the registration's home bind payload — exactly <see cref="HomeBindPayload"/>
    /// plus the invite-code hash set to null, in the one request (task 185's invariant: a bound contact never holds an
    /// unused invite-code hash, or a later unbind would let a forwarded invite link bind the contact through the CIAM
    /// path). The marker (<c>sprk_externalinvitedon</c>) is not touched. Pure.
    /// </summary>
    public static Dictionary<string, object?> HomeBindAndConsumeInvitePayload(Guid homeTenantId, Guid oid)
    {
        var payload = HomeBindPayload(homeTenantId, oid);
        ClearInvite(payload);
        return payload;
    }

    /// <summary>
    /// Sets the consumed invite's columns to null on <paramref name="payload"/>: the invite-code hash and the invite stamp
    /// (the email hash and the invited tenant). The marker is not touched.
    /// </summary>
    private static void ClearInvite(Dictionary<string, object?> payload)
    {
        payload[ContactBindingDecision.ExternalInviteCodeHashColumn] = null;
        foreach (var column in InviteStampColumns)
        {
            payload[column] = null;
        }
    }

    /// <summary>
    /// The one write request every store write sends. Pure, so its headers are pinned directly: OData versions, the
    /// optional precondition, JSON — and never <c>MSCRM.SuppressDuplicateDetection</c> (r3 task 047, owner 2026-10-15:
    /// API writes do not opt in to duplicate-detection rules; email is the canonical duplicate check).
    /// </summary>
    public static HttpRequestMessage BuildWriteRequest(
        HttpMethod method, Uri uri, Dictionary<string, object?> payload, string token, (string Name, string Value)? precondition)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        if (precondition is { } header)
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>The home contact read's columns: every binding column, plus the home directory.</summary>
    public static readonly string HomeContactSelect = $"{ContactSelect},{ContactBindingDecision.ExternalTenantIdColumn}";

    /// <summary>
    /// ADR-028 A9 (r3 task 046): the home contact read — plane WorkforceHome AND the home directory AND the binding, any
    /// state, two rows. Its own select (<see cref="HomeContactSelect"/>), so no other read depends on the A9 column.
    /// </summary>
    public static string BuildHomeContactLookupPath(Guid homeTenantId, Guid homeObjectId)
        => $"contacts?$select={HomeContactSelect}"
           + $"&$filter={ContactBindingDecision.IdentityPlaneColumn} eq {ContactBindingDecision.WorkforceHomePlaneValue}"
           + $" and {ContactBindingDecision.ExternalTenantIdColumn} eq '{homeTenantId:D}'"
           + $" and {ContactBindingDecision.ExternalObjectIdColumn} eq '{homeObjectId:D}'&$top=2";

    /// <summary>Parses one stored-link row from Web API JSON. <c>isdisabled</c> that is not a boolean reads as null.</summary>
    public static HomeLinkRow? ParseHomeLinkRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        var id = GuidOf(row, "systemuserid");
        if (id is null) return null;

        bool? disabled = row.TryGetProperty("isdisabled", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? d.GetBoolean()
            : null;
        return new HomeLinkRow(
            id.Value, disabled,
            StringOf(row, ContactBindingDecision.HomeTenantIdColumn),
            StringOf(row, ContactBindingDecision.HomeObjectIdColumn),
            StringOf(row, "@odata.etag"));
    }

    /// <summary>The masking probe: rows that carry a plane marker.</summary>
    public static string BuildProbePath()
        => $"contacts?$select=contactid,{ContactBindingDecision.ExternalObjectIdColumn},{ContactBindingDecision.IdentityPlaneColumn}"
           + $"&$filter={ContactBindingDecision.IdentityPlaneColumn} ne null&$top=50";

    /// <summary>The readability proof's first read: this store's own Dataverse user.</summary>
    public static string BuildWhoAmIPath() => "WhoAmI";

    /// <summary>
    /// The readability proof's System Administrator leg: a role of the System Administrator template (by role template id,
    /// so a business unit's copy counts and a renamed role still does), joined to <paramref name="user"/> through
    /// <c>systemuserroles</c>. The same query as the SDK source (<see cref="SdkColumnReadPermissionSource"/>), as FetchXML.
    /// </summary>
    public static string BuildSystemAdministratorRolePath(Guid user)
        => "roles?fetchXml=" + Uri.EscapeDataString(
            "<fetch top=\"1\"><entity name=\"role\"><attribute name=\"roleid\" />"
            + $"<filter><condition attribute=\"roletemplateid\" operator=\"eq\" value=\"{SecuredColumnReadProof.SystemAdministratorRoleTemplateId:D}\" /></filter>"
            + "<link-entity name=\"systemuserroles\" from=\"roleid\" to=\"roleid\" link-type=\"inner\">"
            + $"<filter><condition attribute=\"systemuserid\" operator=\"eq\" value=\"{user:D}\" /></filter>"
            + "</link-entity></entity></fetch>");

    /// <summary>
    /// The readability proof's profile legs: the field permissions granting Read on <paramref name="columns"/> of
    /// <paramref name="table"/> through a profile of <paramref name="user"/> — its own (<c>systemuserprofiles</c>) or, with
    /// <paramref name="throughTeam"/>, a team's it is a member of (<c>teamprofiles</c> to <c>teammembership</c>).
    /// </summary>
    public static string BuildFieldPermissionReadPath(Guid user, string table, IEnumerable<string> columns, bool throughTeam)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var values = string.Concat(columns.Select(c => $"<value>{Xml(c)}</value>"));
        var member = $"<filter><condition attribute=\"systemuserid\" operator=\"eq\" value=\"{user:D}\" /></filter>";
        var link = throughTeam
            ? "<link-entity name=\"teamprofiles\" from=\"fieldsecurityprofileid\" to=\"fieldsecurityprofileid\" link-type=\"inner\">"
              + "<link-entity name=\"teammembership\" from=\"teamid\" to=\"teamid\" link-type=\"inner\">" + member
              + "</link-entity></link-entity>"
            : "<link-entity name=\"systemuserprofiles\" from=\"fieldsecurityprofileid\" to=\"fieldsecurityprofileid\" link-type=\"inner\">"
              + member + "</link-entity>";
        return "fieldpermissions?fetchXml=" + Uri.EscapeDataString(
            "<fetch><entity name=\"fieldpermission\"><attribute name=\"attributelogicalname\" /><filter>"
            + $"<condition attribute=\"entityname\" operator=\"eq\" value=\"{Xml(table)}\" />"
            + $"<condition attribute=\"canread\" operator=\"eq\" value=\"{SecuredColumnReadProof.FieldPermissionAllowed}\" />"
            + $"<condition attribute=\"attributelogicalname\" operator=\"in\">{values}</condition>"
            + "</filter>" + link + "</entity></fetch>");
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    /// <summary>
    /// The masking verdict once the identity columns' readability proof has run. Pure. Without proof every marked row may
    /// be hidden (a hidden plane reads as no plane, and the probe then finds no marked rows), so an unproven read is
    /// <see cref="BindingReadability.Masked"/> and a proof that faulted is <see cref="BindingReadability.Failed"/>, whatever
    /// the probe returned. Only a proven read is classified by its rows (<see cref="ClassifyProbe(ContactLookup)"/>).
    /// </summary>
    internal static BindingReadability ClassifyProbe(ContactLookup probe, ColumnReadProofOutcome proof) => proof switch
    {
        ColumnReadProofOutcome.Proven => ClassifyProbe(probe),
        ColumnReadProofOutcome.Unproven => BindingReadability.Masked,
        _ => BindingReadability.Failed,
    };

    /// <summary>The marking below, with one outcome for both column sets. Pure.</summary>
    internal static ContactLookup WithIdentityColumnsProof(ContactLookup lookup, ColumnReadProofOutcome proof)
        => WithIdentityColumnsProof(lookup, proof, proof);

    /// <summary>
    /// Marks every row of a read lookup by the proofs of its two column sets. Pure.
    /// <list type="bullet">
    ///   <item><paramref name="bindingProof"/> not <see cref="ColumnReadProofOutcome.Proven"/>: the row's binding is read
    ///     without proof (<see cref="ContactBindingRow.IdentityColumnsUnproven"/>), so an empty plane is Unreadable.</item>
    ///   <item><paramref name="collisionProof"/> not <see cref="ColumnReadProofOutcome.Proven"/>: the row's collision flag
    ///     may be hidden (<see cref="ContactBindingRow.CollisionColumnsUnproven"/>), so it reads as a flag whose parties
    ///     cannot be read (<see cref="CollisionFlag.ReadWithoutProof"/>), which is never overwritten or cleared.</item>
    /// </list>
    /// </summary>
    internal static ContactLookup WithIdentityColumnsProof(
        ContactLookup lookup, ColumnReadProofOutcome bindingProof, ColumnReadProofOutcome collisionProof)
        => lookup.Status != LookupStatus.Read
           || (bindingProof == ColumnReadProofOutcome.Proven && collisionProof == ColumnReadProofOutcome.Proven)
            ? lookup
            : lookup with { Rows = lookup.Rows.Select(r => Marked(r, bindingProof, collisionProof)).ToList() };

    /// <summary>The systemuser form of the marking above, with one outcome for both column sets. Pure.</summary>
    internal static SystemUserIdentityRow WithIdentityColumnsProof(SystemUserIdentityRow row, ColumnReadProofOutcome proof)
        => WithIdentityColumnsProof(row, proof, proof);

    /// <summary>The systemuser form of the marking above: its linked contact. Pure.</summary>
    internal static SystemUserIdentityRow WithIdentityColumnsProof(
        SystemUserIdentityRow row, ColumnReadProofOutcome bindingProof, ColumnReadProofOutcome collisionProof)
        => row.LinkedContact is null
           || (bindingProof == ColumnReadProofOutcome.Proven && collisionProof == ColumnReadProofOutcome.Proven)
            ? row
            : row with { LinkedContact = Marked(row.LinkedContact, bindingProof, collisionProof) };

    private static ContactBindingRow Marked(
        ContactBindingRow row, ColumnReadProofOutcome bindingProof, ColumnReadProofOutcome collisionProof)
    {
        var marked = bindingProof == ColumnReadProofOutcome.Proven ? row : row with { IdentityColumnsUnproven = true };
        return collisionProof == ColumnReadProofOutcome.Proven
            ? marked
            : marked with { CollisionColumnsUnproven = true, Flag = CollisionFlag.ReadWithoutProof(marked.Flag) };
    }

    /// <summary>
    /// Slice C: marks every row whose removed marker read EMPTY while <paramref name="proof"/> is not
    /// <see cref="ColumnReadProofOutcome.Proven"/> (<see cref="ContactBindingRow.RemovedMarkerUnproven"/>): such a row counts as
    /// removed. A marker that was read is a value and is kept. Pure.
    /// </summary>
    internal static ContactLookup WithRemovedMarkerProof(ContactLookup lookup, ColumnReadProofOutcome proof)
        => lookup.Status != LookupStatus.Read || proof == ColumnReadProofOutcome.Proven
            ? lookup
            : lookup with { Rows = lookup.Rows.Select(RemovedMarked).ToList() };

    /// <summary>The systemuser form of the marking above: its linked contact. Pure.</summary>
    internal static SystemUserIdentityRow WithRemovedMarkerProof(SystemUserIdentityRow row, ColumnReadProofOutcome proof)
        => row.LinkedContact is null || proof == ColumnReadProofOutcome.Proven
            ? row
            : row with { LinkedContact = RemovedMarked(row.LinkedContact) };

    private static ContactBindingRow RemovedMarked(ContactBindingRow row)
        => row.RemovedOn is not null ? row : row with { RemovedMarkerUnproven = true };

    /// <summary>
    /// The interactive-systemuser scan: enabled, not an application user, and a human access mode —
    /// Read-Write (0), Administrative (1) or Read (2). Support User (3), Non-interactive (4) and Delegated
    /// Admin (5) are excluded. The linked contact is expanded so a verified user costs no further query.
    /// </summary>
    public static string BuildSystemUserScanPath()
        => $"systemusers?$select={SystemUserSelect}"
           + "&$filter=isdisabled eq false and applicationid eq null and (accessmode eq 0 or accessmode eq 1 or accessmode eq 2)"
           + $"&$expand=sprk_PrimaryContact($select={ContactSelect})"
           + "&$orderby=systemuserid";

    /// <summary>Contacts with an open collision flag.</summary>
    public static string BuildFlaggedContactScanPath()
        => $"contacts?$select={ContactSelect}&$filter={FlagOnColumn} ne null&$orderby=contactid";

    /// <summary>
    /// The create request: a plain <c>POST contacts</c> whose body carries the binding, the UNSECURED uniqueness
    /// mirror and the plane (owner round 4 item 4, B2: Dataverse refuses an alternate key on the field-secured binding,
    /// so the key lives on the mirror). The key's unique index refuses a second contact for the same oid with HTTP 412
    /// <c>0x80060892</c> (<see cref="IsDuplicateKey"/> → <see cref="StoreWriteStatus.KeyConflict"/>), so two first
    /// sign-ins that race still produce exactly one contact.
    /// </summary>
    /// <remarks>
    /// Not <c>PATCH contacts(sprk_externalobjectidkey='…')</c> with <c>If-None-Match: *</c>: Dataverse answers that
    /// create-only upsert on an alternate key with HTTP 404 <c>0x80060891</c> ("A record with the specified key values
    /// does not exist in contact entity") and creates nothing. Verified live on spaarkedev1 2026-10-02 (task 141 gate
    /// G-6: all seven creates of the first write run failed that way; a POST probe created, and a second POST with the
    /// same mirror value got 412 <c>0x80060892</c>).
    /// </remarks>
    public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(
        Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
        => (HttpMethod.Post, "contacts", CreatePayload(oid, plane, details));

    /// <summary>
    /// The bind payload: the oid in "D" format into the binding AND the uniqueness mirror, and the plane marker —
    /// always together, in one request, so the unique index guards every bind as well as every create.
    /// </summary>
    public static Dictionary<string, object?> BindPayload(Guid oid, IdentityPlaneMarker plane) => new()
    {
        [ContactBindingDecision.ExternalObjectIdColumn] = oid.ToString("D"),
        [ContactBindingDecision.KeyMirrorColumn] = oid.ToString("D"),
        [ContactBindingDecision.IdentityPlaneColumn] = (int)plane,
    };

    /// <summary>
    /// Task 185 — the consuming bind's payload: exactly <see cref="BindPayload"/> plus the invite-code hash set to null, in
    /// the one request, so the code is consumed by the write that binds the oid (never a second write that could be lost).
    /// The marker is not touched.
    /// </summary>
    public static Dictionary<string, object?> BindAndConsumeInvitePayload(Guid oid, IdentityPlaneMarker plane)
    {
        var payload = BindPayload(oid, plane);
        ClearInvite(payload);
        return payload;
    }

    /// <summary>
    /// The create payload: the binding (the field-secured column the BFF's writer profile may create), the uniqueness
    /// mirror (the key column) and the plane — the same oid in both columns, in the one create request
    /// (<see cref="BuildCreateRequest"/>).
    /// </summary>
    /// <remarks>
    /// The owner rule (ISS-005, #1673; DATAVERSE-WRITE-PATH-ARCHITECTURE I-6): every IDENTITY contact this BFF creates in its own
    /// environment - the four external-access creates (registration, first sign-in, licensed-user link, invite) - is owned by the
    /// default owner team of <c>ExternalAccess:CustomerBusinessUnitId</c>, whatever the plane; the payload carries
    /// <c>ownerid@odata.bind</c> on any plane, refuses an empty team, and requires one for WorkforceHome. This is NOT the only
    /// contact create in the BFF: the chat <c>dataverse.create_record</c> tool can create a <c>contact</c> row app-only, owned by the
    /// caller's business-unit team, record-first through the general resolver (<c>OwnedChildWrite</c>), and it does not read
    /// <c>CustomerBusinessUnitId</c> (see <c>ContactCreateOwnerPolicyGuardTests.UndetectableSeams</c>).
    /// </remarks>
    public static Dictionary<string, object?> CreatePayload(Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var payload = new Dictionary<string, object?>
        {
            ["lastname"] = details.LastName,
            [ContactBindingDecision.ExternalObjectIdColumn] = oid.ToString("D"),
            [ContactBindingDecision.KeyMirrorColumn] = oid.ToString("D"),
            [ContactBindingDecision.IdentityPlaneColumn] = (int)plane,
        };
        if (!string.IsNullOrWhiteSpace(details.FirstName)) payload["firstname"] = details.FirstName;
        if (!string.IsNullOrWhiteSpace(details.Email)) payload["emailaddress1"] = details.Email;

        // THE OWNER RULE (ISS-005, #1673; DATAVERSE-WRITE-PATH-ARCHITECTURE I-6): every contact this BFF creates in its OWN
        // environment is owned by the default owner team of ExternalAccess:CustomerBusinessUnitId, whatever the plane; the binder
        // resolves it before the create and refuses the create when it cannot (ContactIdentityBinder). The payload therefore
        // takes an owner on ANY plane (the CIAM plane is email-bind-only today and creates nothing, but a future create there
        // is owned the same way). What it refuses: an EMPTY team on any plane, and a WorkforceHome create with no owner at
        // all. No owner is built only for a binder over ANOTHER environment (ContactCreateOwner.NoneForForeignEnvironment,
        // ContactIdentityBinderFactory only; tracked in #2270). A home directory belongs to WorkforceHome alone.
        var home = plane == IdentityPlaneMarker.WorkforceHome;
        if (home && (details.ExternalTenantId is not { } tenant || tenant == Guid.Empty))
        {
            throw new ArgumentException("A WorkforceHome create needs the home tenant.", nameof(details));
        }

        // The owner-seam refusal (RecordOwnerAssignmentCensusTests.EverySeamRefusesAnOwnerlessCreate): never an app-owned contact.
        if ((home && details.OwningTeamId is null) || details.OwningTeamId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"No owning team was resolved for the {plane} contact; refusing to create an app-owned contact.");
        }

        if (!home && details.ExternalTenantId is not null)
        {
            throw new ArgumentException("Only a WorkforceHome create carries a home tenant.", nameof(details));
        }

        if (home)
        {
            payload[ContactBindingDecision.ExternalTenantIdColumn] = details.ExternalTenantId!.Value.ToString("D");
            if (!string.IsNullOrWhiteSpace(details.Department)) payload["department"] = details.Department;
        }

        if (details.OwningTeamId is { } owner)
        {
            payload[OwnerBindKey] = $"/teams({owner:D})";
        }

        return payload;
    }

    /// <summary>The flag payload: the four summary columns (the first party) and every party as JSON.</summary>
    public static Dictionary<string, object?> FlagPayload(CollisionFlag flag) => new()
    {
        [FlagOnColumn] = flag.FlaggedOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        [FlagOidColumn] = flag.CollidingOid?.ToString("D"),
        [FlagPlaneColumn] = flag.CollidingPlane is { } p ? (int)p : null,
        [FlagReasonColumn] = flag.Reason is { } r ? (int)r : null,
        [FlagPartiesColumn] = SerializeParties(flag.Parties),
    };

    /// <summary>The clear payload (all five flag columns null).</summary>
    public static Dictionary<string, object?> ClearFlagPayload() => new()
    {
        [FlagOnColumn] = null,
        [FlagOidColumn] = null,
        [FlagPlaneColumn] = null,
        [FlagReasonColumn] = null,
        [FlagPartiesColumn] = null,
    };

    /// <summary>
    /// The parties column's JSON: <c>[{"oid":"…","plane":100000001,"reason":100000000,"on":"…Z"}]</c>. Oids in "D"
    /// format, option values as integers, times in UTC. Pure.
    /// </summary>
    public static string SerializeParties(IReadOnlyList<CollisionParty> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);
        var rows = parties.Select(p => new Dictionary<string, object?>
        {
            ["oid"] = p.Oid?.ToString("D"),
            ["plane"] = p.Plane is { } pl ? (int)pl : null,
            ["reason"] = p.Reason is { } r ? (int)r : null,
            ["on"] = p.FlaggedOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
        return JsonSerializer.Serialize(rows);
    }

    /// <summary>
    /// Parses the parties column. Null when the text is not the shape <see cref="SerializeParties"/> writes —
    /// the caller then treats the flag as carrying parties it cannot read (never cleared, never overwritten).
    /// Pure.
    /// </summary>
    public static IReadOnlyList<CollisionParty>? ParseParties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = TryParseJson(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        var parties = new List<CollisionParty>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return null;
            if (!item.TryGetProperty("on", out var onEl) || onEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(onEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on))
            {
                return null;
            }

            Guid? oid = null;
            if (item.TryGetProperty("oid", out var oidEl) && oidEl.ValueKind != JsonValueKind.Null)
            {
                if (oidEl.ValueKind != JsonValueKind.String || !Guid.TryParse(oidEl.GetString(), out var g) || g == Guid.Empty)
                {
                    return null;
                }

                oid = g;
            }

            parties.Add(new CollisionParty(oid, PlaneOf(IntOf(item, "plane")), ReasonOf(IntOf(item, "reason")), on));
        }

        return parties.Count == 0 ? null : parties;
    }

    /// <summary>Parses one contact row from Web API JSON.</summary>
    public static ContactBindingRow? ParseContactRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        var id = GuidOf(row, "contactid");
        if (id is null) return null;

        CollisionFlag? flag = null;
        if (row.TryGetProperty(FlagOnColumn, out var on) && on.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(on.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var flaggedOn))
        {
            flag = new CollisionFlag(
                flaggedOn,
                GuidOf(row, FlagOidColumn),
                PlaneOf(IntOf(row, FlagPlaneColumn)),
                ReasonOf(IntOf(row, FlagReasonColumn)));

            // The summary columns are the first party; the parties column is every party. A parties column that
            // does not parse — or does not start with the summary's party — is a flag whose parties we cannot read:
            // it is kept and never overwritten (ContactBindingDecision.ShouldWriteFlag / ReconcileFlag).
            var rawParties = StringOf(row, FlagPartiesColumn);
            if (!string.IsNullOrWhiteSpace(rawParties))
            {
                var parties = ParseParties(rawParties);
                flag = parties is not null && parties[0].IsSameCollision(flag.Primary)
                    ? flag with { OtherParties = parties.Skip(1).ToList() }
                    : flag with { HasUnreadableParties = true };
            }
        }

        return new ContactBindingRow(
            id.Value,
            IntOf(row, "statecode"),
            StringOf(row, ContactBindingDecision.ExternalObjectIdColumn),
            IntOf(row, ContactBindingDecision.IdentityPlaneColumn),
            StringOf(row, "@odata.etag"),
            StringOf(row, "emailaddress1"),
            flag,
            StringOf(row, ContactBindingDecision.KeyMirrorColumn),
            StringOf(row, ContactBindingDecision.ExternalTenantIdColumn))
        {
            RemovedOn = RemovedOnOf(row),
        };
    }

    /// <summary>
    /// Slice C: the removed marker as read from one contact row. A value that does not parse as a date is still a marker
    /// (something wrote it), so it reads as <see cref="DateTimeOffset.MinValue"/>, never as "not removed".
    /// </summary>
    internal static DateTimeOffset? RemovedOnOf(JsonElement row)
    {
        if (!row.TryGetProperty(ContactBindingDecision.ExternalAccessRemovedOnColumn, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
               && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                   out var removedOn)
            ? removedOn
            : DateTimeOffset.MinValue;
    }

    /// <summary>Parses one systemuser row (with an optional expanded linked contact) from Web API JSON.</summary>
    public static SystemUserIdentityRow? ParseSystemUserRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        var id = GuidOf(row, "systemuserid");
        if (id is null) return null;

        ContactBindingRow? linked = null;
        if (row.TryGetProperty("sprk_PrimaryContact", out var expanded) && expanded.ValueKind == JsonValueKind.Object)
        {
            linked = ParseContactRow(expanded);
        }

        return new SystemUserIdentityRow(
            id.Value,
            GuidOf(row, "azureactivedirectoryobjectid"),
            StringOf(row, "internalemailaddress"),
            StringOf(row, "domainname"),
            GuidOf(row, "_sprk_primarycontact_value"),
            StringOf(row, "@odata.etag"),
            linked,
            StringOf(row, "firstname"),
            StringOf(row, "lastname"));
    }

    /// <summary>
    /// Classifies a Dataverse error body: a column this environment lacks, an alternate key it lacks, or
    /// anything else. Both missing-schema shapes come back as 400 with code <c>0x80060888</c> (verified live
    /// 2026-09-30), told apart by message.
    /// </summary>
    public static LookupStatus ClassifyError(string? body)
        => body is not null && body.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase)
            ? LookupStatus.ColumnMissing
            : LookupStatus.Failed;

    /// <summary>
    /// True when a write refusal is a missing PRIVILEGE, not a fault that a retry can clear: HTTP 403, or a 400 whose body
    /// names a privilege (<c>prv…</c> / "privilege") or the access-check code <c>0x80040220</c>. 429, 5xx and network
    /// faults are NOT permission faults (they stay retryable). Pure.
    /// </summary>
    public static bool IsPermissionFault(HttpStatusCode status, string? body)
        => status == HttpStatusCode.Forbidden
           || (status == HttpStatusCode.BadRequest && body is not null
               && (body.Contains("privilege", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("0x80040220", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// True when a create's refusal is Dataverse's OWNER read-check: HTTP 403 (a 400 is tolerated) whose body carries
    /// <c>0x80040299</c> or "Privilege Check For Owner" - the owner team's roles hold no Read on the table (seen live on
    /// <c>sprk_signal</c> and <c>sprk_decisionrecord</c>). Narrower than <see cref="IsPermissionFault"/> on purpose: a missing
    /// privilege of the BFF's own identity (<c>0x80040220</c>, "missing prvCreateContact") is not an owner fault and keeps its
    /// <see cref="StoreWriteStatus.Forbidden"/> mapping. Pure.
    /// </summary>
    public static bool IsOwnerPrivilegeFault(HttpStatusCode status, string? body)
        => status is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest
           && body is not null
           && (body.Contains("0x80040299", StringComparison.OrdinalIgnoreCase)
               || body.Contains("Privilege Check For Owner", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when a write's error text says a choice value is not an option of its choice - Dataverse's "The value N of 'col'
    /// ... is outside the valid range" (seen live on <c>sprk_event.sprk_priority</c>, task 097). On a flag write that means
    /// the collision-reason option is not provisioned yet (r3 task 047, fix round 3). Pure; the store's write error is
    /// <c>"{status}: {body}"</c>.
    /// </summary>
    public static bool IsOptionValueOutOfRange(string? error)
        => error is not null && error.Contains("outside the valid range", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>error.code</c> of a Dataverse error body (e.g. <c>0x80060888</c>), or null when the body is not that JSON shape.
    /// Never the message: a syntax error's message echoes the request's whole <c>$filter</c> (r3 task 047, UAC re-check). Pure.
    /// </summary>
    public static string? ErrorCodeOf(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when a Dataverse error body says the alternate key in the URL is not defined.</summary>
    public static bool IsKeyMissing(string? body)
        => body is not null && body.Contains("key in the request URI is not valid", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a Dataverse error body is the alternate key's DUPLICATE fault — <c>0x80060892</c>, "Entity Key …
    /// violated", returned as HTTP 412 (captured live 2026-08-06 on <c>sprk_communication</c>,
    /// email-communication-intelligence-r2 task 020). Exact code, never a range. Distinct from the 412 a stale
    /// <c>If-Match</c> version produces, which a re-read can fix.
    /// </summary>
    public static bool IsDuplicateKey(string? body)
        => body is not null && body.Contains("0x80060892", StringComparison.OrdinalIgnoreCase);

    // ── Reads ──────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct)
        => ReadContactsAsync(BuildOidLookupPath(oid), "oid lookup", ct);

    /// <inheritdoc />
    public Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return ReadContactsAsync(BuildEmailLookupPath(email.Trim()), "email lookup", ct, logLabel: "contacts by email");
    }

    /// <inheritdoc />
    public Task<ContactLookup> FindInactiveContactsByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return ReadContactsAsync(BuildInactiveEmailLookupPath(email.Trim()), "inactive email lookup", ct,
            logLabel: "inactive contacts by email");
    }

    /// <inheritdoc />
    public Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct)
        => ReadContactsAsync(BuildKeyMirrorLookupPath(oid), "key-mirror lookup", ct);

    /// <inheritdoc />
    public async Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct)
    {
        var (status, code, body, _) = await GetAsync($"contacts({contactId:D})?$select={ContactSelect}", ct)
            .ConfigureAwait(false);
        if (status == LookupStatus.Read && code == HttpStatusCode.NotFound)
        {
            return ContactLookup.Of();
        }

        if (status != LookupStatus.Read || body is null)
        {
            return status == LookupStatus.ColumnMissing ? ContactLookup.ColumnMissing : ContactLookup.Failed;
        }

        using var doc = TryParseJson(body);
        var row = doc is null ? null : ParseContactRow(doc.RootElement);
        return row is null ? ContactLookup.Failed : await ProvenAsync(ContactLookup.Of(row), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RemovedMarkerRead> ReadRemovedMarkerAsync(Guid contactId, CancellationToken ct)
    {
        var (status, code, body, _) = await GetAsync(BuildRemovedMarkerReadPath(contactId), ct).ConfigureAwait(false);
        var (readable, removedOn) = ParseRemovedMarkerResponse(status, code, body);
        if (!readable)
        {
            return RemovedMarkerRead.Unreadable;
        }

        return removedOn is not null
            ? RemovedMarkerRead.Removed
            : RemovedMarkerFrom(null, await _removedMarkerProof.ProveAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> WriteRemovedMarkerAsync(Guid contactId, DateTimeOffset? removedOn, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", RemovedMarkerPayload(removedOn), ("If-Match", "*"), ct);

    /// <inheritdoc />
    public Task<StoreWriteResult> WriteRemovedMarkerIfUnchangedAsync(
        Guid contactId, string? etag, DateTimeOffset? removedOn, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", RemovedMarkerPayload(removedOn), ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("removed marker", contactId));

    /// <inheritdoc />
    public Task<StoreWriteResult> ClearRemovedPersonAccessStateAsync(Guid contactId, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", RemovedPersonAccessStatePayload(), ("If-Match", "*"), ct);

    /// <inheritdoc />
    public Task<StoreWriteResult> ClearStandingGrantAsync(Guid contactId, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", StandingGrantClearPayload(), ("If-Match", "*"), ct);

    /// <summary>Slice C: the removed-marker write: that one column, set (UTC) or cleared (null). Pure.</summary>
    public static Dictionary<string, object?> RemovedMarkerPayload(DateTimeOffset? removedOn) => new()
    {
        [ContactBindingDecision.ExternalAccessRemovedOnColumn] =
            removedOn?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Slice C: the columns an administrator removal clears in one write: the invite state and the access permission
    /// baseline. Never a binding column (<see cref="BindingColumns"/>, the key mirror) and never the statecode.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedPersonAccessStateColumns = new[]
    {
        ContactBindingDecision.ExternalInvitedOnColumn,
        ContactBindingDecision.ExternalInviteCodeHashColumn,
        ContactBindingDecision.ExternalInvitedEmailHashColumn,
        ContactBindingDecision.ExternalInvitedTenantIdColumn,
        SubjectStandingGrantReader.BaselineAttribute,
    };

    /// <summary>Slice C: every <see cref="RemovedPersonAccessStateColumns"/> column set to null. Pure.</summary>
    public static Dictionary<string, object?> RemovedPersonAccessStatePayload()
        => RemovedPersonAccessStateColumns.ToDictionary(column => column, _ => (object?)null);

    /// <summary>Slice C: the standing grant cleared, in a write of its own. Pure.</summary>
    public static Dictionary<string, object?> StandingGrantClearPayload() => new()
    {
        [SubjectStandingGrantReader.StandingGrantAttribute] = null,
    };

    /// <summary>Slice C: one contact's removed marker, by id, in a query of its own. Any state: a row is never filtered out.</summary>
    public static string BuildRemovedMarkerReadPath(Guid contactId)
        => $"contacts({contactId:D})?$select={ContactBindingDecision.ExternalAccessRemovedOnColumn}";

    /// <summary>
    /// Slice C: the marker read's answer. Not <c>Readable</c> unless the read succeeded and returned the row as an object; a
    /// missing row (404), a failed read or a body that is not JSON is unreadable (fail closed). Pure.
    /// </summary>
    internal static (bool Readable, DateTimeOffset? RemovedOn) ParseRemovedMarkerResponse(
        LookupStatus status, HttpStatusCode code, string? body)
    {
        if (status != LookupStatus.Read || code == HttpStatusCode.NotFound || body is null)
        {
            return (false, null);
        }

        using var doc = TryParseJson(body);
        return doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
            ? (false, null)
            : (true, RemovedOnOf(doc.RootElement));
    }

    /// <summary>
    /// Slice C: the live marker read's answer. A marker that was read is <see cref="RemovedMarkerRead.Removed"/>; an empty one is
    /// <see cref="RemovedMarkerRead.NotRemoved"/> only on a proven read, else <see cref="RemovedMarkerRead.Unreadable"/>. Pure.
    /// </summary>
    internal static RemovedMarkerRead RemovedMarkerFrom(DateTimeOffset? removedOn, ColumnReadProofOutcome proof)
        => removedOn is not null ? RemovedMarkerRead.Removed
            : proof == ColumnReadProofOutcome.Proven ? RemovedMarkerRead.NotRemoved
            : RemovedMarkerRead.Unreadable;

    /// <inheritdoc />
    public async Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        var ids = contactIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return ReferenceLookup.Of();

        var (status, _, body, _) = await GetAsync(BuildReferenceLookupPath(ids), ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            return ReferenceLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return ReferenceLookup.Failed;
        }

        var refs = new List<SystemUserReference>();
        foreach (var row in Values(doc.RootElement))
        {
            var suid = GuidOf(row, "systemuserid");
            var linked = GuidOf(row, "_sprk_primarycontact_value");
            if (suid is { } s && linked is { } l)
            {
                refs.Add(new SystemUserReference(s, GuidOf(row, "azureactivedirectoryobjectid"), l));
            }
        }

        return new ReferenceLookup(LookupStatus.Read, refs);
    }

    /// <inheritdoc />
    public async Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct)
    {
        var (status, code, body, _) = await GetAsync(
                $"systemusers({systemUserId:D})?$select={SystemUserSelect}&$expand=sprk_PrimaryContact($select={ContactSelect})",
                ct)
            .ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null || code == HttpStatusCode.NotFound)
        {
            return new SystemUserLookup(status == LookupStatus.Read ? LookupStatus.Failed : status, null);
        }

        using var doc = TryParseJson(body);
        var row = doc is null ? null : ParseSystemUserRow(doc.RootElement);
        return row is null
            ? SystemUserLookup.Failed
            : new SystemUserLookup(LookupStatus.Read, await ProvenAsync(row, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct)
    {
        // A hidden plane reads as no plane, so without proof the probe would find "no marked rows": nothing to protect.
        // Every identity column's read must be proven: the binding columns, then the collision columns.
        var proof = await _bindingColumnsProof.ProveAsync(ct).ConfigureAwait(false);
        if (proof == ColumnReadProofOutcome.Proven)
        {
            proof = await _collisionColumnsProof.ProveAsync(ct).ConfigureAwait(false);
        }

        if (proof != ColumnReadProofOutcome.Proven)
        {
            return ClassifyProbe(ContactLookup.Failed, proof);
        }

        var lookup = await ReadContactsAsync(BuildProbePath(), "masking probe", ct).ConfigureAwait(false);
        return ClassifyProbe(lookup, proof);
    }

    /// <inheritdoc />
    public async Task<HomeLinkLookup> FindHomeLinkedSystemUsersAsync(Guid homeTenantId, Guid homeObjectId, CancellationToken ct)
    {
        var (status, _, body, _) = await GetAsync(BuildHomeLinkLookupPath(homeTenantId, homeObjectId), ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            if (status == LookupStatus.ColumnMissing)
            {
                LogHomeSchemaMissing("home link lookup");
                return HomeLinkLookup.ColumnMissing;
            }

            return HomeLinkLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return HomeLinkLookup.Failed;
        }

        var rows = Values(doc.RootElement).Select(ParseHomeLinkRow).Where(r => r is not null).Select(r => r!).ToList();
        return new HomeLinkLookup(LookupStatus.Read, rows);
    }

    /// <inheritdoc />
    public async Task<HomeLinkLookup> FindSystemUsersByGuestObjectIdAsync(Guid guestObjectId, CancellationToken ct)
    {
        var (status, _, body, _) = await GetAsync(BuildGuestSystemUserLookupPath(guestObjectId), ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            if (status == LookupStatus.ColumnMissing)
            {
                LogHomeSchemaMissing("guest systemuser lookup");
                return HomeLinkLookup.ColumnMissing;
            }

            return HomeLinkLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return HomeLinkLookup.Failed;
        }

        var rows = Values(doc.RootElement).Select(ParseHomeLinkRow).Where(r => r is not null).Select(r => r!).ToList();
        return new HomeLinkLookup(LookupStatus.Read, rows);
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> WriteHomeLinkAsync(
        Guid systemUserId, string? etag, Guid homeTenantId, Guid homeObjectId, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"systemusers({systemUserId:D})", HomeLinkPayload(homeTenantId, homeObjectId),
                ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("home link", systemUserId));

    /// <inheritdoc />
    public async Task<ContactLookup> FindHomeContactsAsync(Guid homeTenantId, Guid homeObjectId, CancellationToken ct)
    {
        // Not ReadContactsAsync: its missing-column branch logs the binding-schema script, and a missing A9 column must be
        // ONE error naming Set-WorkforceHomeIdentitySchema.ps1 (as the home link read above).
        var (status, _, body, _) = await GetAsync(BuildHomeContactLookupPath(homeTenantId, homeObjectId), ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            if (status == LookupStatus.ColumnMissing)
            {
                LogHomeSchemaMissing("home contact lookup");
                return ContactLookup.ColumnMissing;
            }

            return ContactLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return ContactLookup.Failed;
        }

        var rows = Values(doc.RootElement).Select(ParseContactRow).Where(r => r is not null).Select(r => r!).ToList();
        return await ProvenAsync(new ContactLookup(LookupStatus.Read, rows), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SystemUserEmailLookup> FindEnabledSystemUsersByEmailAsync(string email, CancellationToken ct)
    {
        var (status, _, body, _) = await GetAsync(BuildLicensedUserEmailLookupPath(email), ct, logLabel: "systemusers by email")
            .ConfigureAwait(false);
        using var doc = status == LookupStatus.Read && body is not null ? TryParseJson(body) : null;
        if (doc is null)
        {
            return SystemUserEmailLookup.Failed;
        }

        var rows = new List<SystemUserEmailRow>();
        foreach (var row in Values(doc.RootElement))
        {
            if (row.ValueKind != JsonValueKind.Object || GuidOf(row, "systemuserid") is not { } id) continue;
            bool? disabled = row.TryGetProperty("isdisabled", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? d.GetBoolean()
                : null;
            rows.Add(new SystemUserEmailRow(id, disabled, StringOf(row, "internalemailaddress"), StringOf(row, "domainname")));
        }

        return new SystemUserEmailLookup(LookupStatus.Read, rows);
    }

    /// <inheritdoc />
    public async Task<OwnerTeamLookup> FindDefaultOwnerTeamsAsync(Guid businessUnitId, CancellationToken ct)
    {
        var (status, _, body, _) = await GetAsync(BuildDefaultOwnerTeamPath(businessUnitId), ct).ConfigureAwait(false);
        using var doc = status == LookupStatus.Read && body is not null ? TryParseJson(body) : null;
        if (doc is null)
        {
            return OwnerTeamLookup.Failed;
        }

        var ids = Values(doc.RootElement).Select(r => GuidOf(r, "teamid")).Where(g => g is not null).Select(g => g!.Value).ToList();
        return new OwnerTeamLookup(LookupStatus.Read, ids);
    }

    /// <inheritdoc />
    public async Task<BusinessUnitLookup> FindBusinessUnitsByNameAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (status, _, body, _) = await GetAsync(BuildBusinessUnitByNamePath(name), ct).ConfigureAwait(false);
        using var doc = status == LookupStatus.Read && body is not null ? TryParseJson(body) : null;
        if (doc is null)
        {
            return BusinessUnitLookup.Failed;
        }

        var ids = Values(doc.RootElement).Select(r => GuidOf(r, "businessunitid")).Where(g => g is not null).Select(g => g!.Value).ToList();
        return new BusinessUnitLookup(LookupStatus.Read, ids);
    }

    /// <inheritdoc />
    public async Task<BindingReadability> ProbeHomeReadabilityAsync(CancellationToken ct)
    {
        var (cStatus, _, cBody, _) = await GetAsync(BuildHomeContactProbePath(), ct).ConfigureAwait(false);
        var (lStatus, _, lBody, _) = await GetAsync(BuildHomeLinkProbePath(), ct).ConfigureAwait(false);
        if (cStatus == LookupStatus.ColumnMissing || lStatus == LookupStatus.ColumnMissing)
        {
            LogHomeSchemaMissing("home masking probe");
        }

        using var cDoc = cStatus == LookupStatus.Read && cBody is not null ? TryParseJson(cBody) : null;
        using var lDoc = lStatus == LookupStatus.Read && lBody is not null ? TryParseJson(lBody) : null;
        var contacts = cDoc is null
            ? (cStatus == LookupStatus.ColumnMissing ? ContactLookup.ColumnMissing : ContactLookup.Failed)
            : new ContactLookup(LookupStatus.Read,
                Values(cDoc.RootElement).Select(ParseContactRow).Where(r => r is not null).Select(r => r!).ToList());
        var links = lDoc is null
            ? (lStatus == LookupStatus.ColumnMissing ? HomeLinkLookup.ColumnMissing : HomeLinkLookup.Failed)
            : new HomeLinkLookup(LookupStatus.Read,
                Values(lDoc.RootElement).Select(ParseHomeLinkRow).Where(r => r is not null).Select(r => r!).ToList());
        return ClassifyHomeProbe(contacts, links);
    }

    /// <inheritdoc />
    /// <remarks>
    /// UAC review F1 (#2005): the home bind consumes the invite-code hash in the SAME conditional PATCH, through the one
    /// consuming-bind path every bind shares (<see cref="BindConsumingInviteAsync"/>, PR #2007).
    /// </remarks>
    public Task<StoreWriteResult> BindHomeOidAsync(Guid contactId, string? etag, Guid homeTenantId, Guid oid, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? BindConsumingInviteAsync(contactId, version, HomeBindAndConsumeInvitePayload(homeTenantId, oid),
                HomeBindPayload(homeTenantId, oid), ct)
            : Task.FromResult(NoRowVersion("bind", contactId));

    private void LogHomeSchemaMissing(string what)
        => _logger.LogError(
            "[ID-BIND] {What} failed: an A9 home-identity column is not provisioned in this environment. Every home sign-in is "
            + "denied until scripts/Set-WorkforceHomeIdentitySchema.ps1 is applied.",
            what);

    /// <inheritdoc />
    /// <remarks>
    /// ONE read, reported as it ended: the decision fails closed on a fault. No grant is read (task 185, option (c)), so no
    /// grant fault can deny and no grant can bind.
    /// </remarks>
    public async Task<BindIntentLookup> FindBindIntentAsync(Guid contactId, CancellationToken ct)
    {
        // The pending-invite marker and its code hash, in their OWN query: a BFF deployed before either column gets
        // ColumnMissing here and nowhere else.
        var (inviteRead, code, inviteBody, _) = await GetAsync(BuildInviteMarkerPath(contactId), ct).ConfigureAwait(false);
        var (inviteStatus, invitedOn, inviteCodeHash) = InterpretMarkerLeg(inviteRead, code, inviteBody, contactId);

        if (inviteStatus == LookupStatus.ColumnMissing)
        {
            _logger.LogWarning(
                "[ID-BIND] {Marker}, {Hash}, {StampHash} or {Tenant} is not provisioned in this environment: email binds "
                + "are denied (contact_not_invited). Apply scripts/Set-ExternalInviteMarkerSchema.ps1, "
                + "scripts/Set-ExternalInviteCodeHashSchema.ps1 and scripts/Set-ExternalAccessContactSecuritySchema.ps1.",
                ContactBindingDecision.ExternalInvitedOnColumn, ContactBindingDecision.ExternalInviteCodeHashColumn,
                ContactBindingDecision.ExternalInvitedEmailHashColumn, ContactBindingDecision.ExternalInvitedTenantIdColumn);
        }

        if (inviteStatus != LookupStatus.Read)
        {
            return new BindIntentLookup(contactId, inviteStatus, invitedOn) { InviteCodeHash = inviteCodeHash };
        }

        var (emailHash, tenant, currentEmail, rowVersion) = InterpretStampLeg(inviteBody);

        // An empty email hash is trusted as "no stamp" only on a proven read; unproven, it may be hidden. Both refuse the
        // bind, so the proof is made only for an empty hash, and only to name the reason.
        var unproven = string.IsNullOrWhiteSpace(emailHash)
                       && await _inviteStampColumnsProof.ProveAsync(ct).ConfigureAwait(false) != ColumnReadProofOutcome.Proven;

        return new BindIntentLookup(contactId, inviteStatus, invitedOn)
        {
            InviteCodeHash = inviteCodeHash,
            InvitedEmailHash = emailHash,
            RawInvitedTenantId = tenant,
            CurrentEmail = currentEmail,
            ETag = rowVersion,
            StampColumnsUnproven = unproven,
        };
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> MarkInvitedAsync(
        Guid contactId, string? etag, DateTimeOffset invitedOn, string? inviteCodeHash, InviteStamp stamp, CancellationToken ct)
    {
        // Built first: a code-and-tenant pairing the payload refuses throws whatever the row version.
        var payload = InviteMarkerPayload(invitedOn, inviteCodeHash, stamp);
        return RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", payload,
                ("If-Match", version), ct, missingColumns: InviteMarkerColumns)
            : Task.FromResult(NoRowVersion("invite mark", contactId));
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> ClearPendingInviteAsync(Guid contactId, string? etag, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", PendingInviteClearPayload(),
                ("If-Match", version), ct, missingColumns: InviteMarkerColumns)
            : Task.FromResult(NoRowVersion("invite retract", contactId));

    /// <summary>Slice C2b: every <see cref="InviteMarkerColumns"/> column set to null (the retract of an unsent invite). Pure.</summary>
    public static Dictionary<string, object?> PendingInviteClearPayload()
        => InviteMarkerColumns.ToDictionary(column => column, _ => (object?)null);

    /// <summary>The masking verdict over the probe's rows. Pure.</summary>
    public static BindingReadability ClassifyProbe(ContactLookup probe)
    {
        if (probe.Status != LookupStatus.Read) return BindingReadability.Failed;
        if (probe.Rows.Count == 0) return BindingReadability.NoMarkedRows;

        // Every BFF write sets oid and marker together. If EVERY marked row comes back with no oid, the column
        // is being masked from this identity (FLS without Read) — a few orphaned markers cannot cause this.
        return probe.Rows.All(r => string.IsNullOrWhiteSpace(r.RawOid))
            ? BindingReadability.Masked
            : BindingReadability.Readable;
    }

    // ── Writes ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>If-Match</c> value for a bind or a link: the row version read with the row, or NULL when there is
    /// none. Never <c>*</c> — "the row exists" is not "the row is as I read it", and an unconditional bind or link
    /// could overwrite one a concurrent writer just made (a binding someone else owns; a link that must never be
    /// re-pointed). A caller without a version re-reads the row first. Pure.
    /// </summary>
    public static string? RowVersionPrecondition(string? etag)
        => string.IsNullOrWhiteSpace(etag) || etag.Trim() == "*" ? null : etag;

    /// <inheritdoc />
    public Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", BindPayload(oid, plane), ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("bind", contactId));

    /// <inheritdoc />
    public Task<StoreWriteResult> BindOidAndConsumeInviteAsync(
        Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? BindConsumingInviteAsync(contactId, version, BindAndConsumeInvitePayload(oid, plane), BindPayload(oid, plane), ct)
            : Task.FromResult(NoRowVersion("bind", contactId));

    /// <summary>
    /// #2005: the one consuming bind. Sends <paramref name="consuming"/> (the bind plus the code hash and the invite stamp
    /// cleared) under <paramref name="version"/>; where a cleared column is not provisioned the platform refuses the whole
    /// PATCH (nothing written) and no contact can hold a value there to consume. A missing code-hash column sends
    /// <paramref name="plain"/> (that environment predates the stamp columns too); missing stamp columns alone send the bind
    /// with the code hash cleared, and then the plain bind if that is refused for the code-hash column. Every retry goes
    /// under the SAME row version. Any other status returns as is. Shared by the CIAM/workforce/systemuser binds and the A9
    /// home bind.
    /// </summary>
    private async Task<StoreWriteResult> BindConsumingInviteAsync(
        Guid contactId, string version, Dictionary<string, object?> consuming, Dictionary<string, object?> plain,
        CancellationToken ct)
    {
        var path = $"contacts({contactId:D})";
        var write = await WriteAsync(HttpMethod.Patch, path, consuming, ("If-Match", version),
            ct, missingColumns: ConsumedInviteColumns).ConfigureAwait(false);
        if (write.Status != StoreWriteStatus.ColumnMissing)
        {
            return write;
        }

        if (!IsWriteColumnMissing(write.Error, ContactBindingDecision.ExternalInviteCodeHashColumn))
        {
            // Only the stamp columns are missing: no stamp can exist here. Clear the code hash with the bind.
            _logger.LogWarning(
                "[ID-BIND] {StampHash} or {Tenant} is not provisioned in this environment: contact {ContactId} is bound without "
                + "clearing an invite stamp. Apply scripts/Set-ExternalAccessContactSecuritySchema.ps1.",
                ContactBindingDecision.ExternalInvitedEmailHashColumn, ContactBindingDecision.ExternalInvitedTenantIdColumn,
                contactId);
            var codeOnly = new Dictionary<string, object?>(consuming);
            foreach (var column in InviteStampColumns)
            {
                codeOnly.Remove(column);
            }

            write = await WriteAsync(HttpMethod.Patch, path, codeOnly, ("If-Match", version),
                ct, missingColumns: InviteCodeHashColumn).ConfigureAwait(false);
            if (write.Status != StoreWriteStatus.ColumnMissing)
            {
                return write;
            }
        }

        _logger.LogWarning(
            "[ID-BIND] {Hash} is not provisioned in this environment: contact {ContactId} is bound without consuming an "
            + "invite code. Apply scripts/Set-ExternalInviteCodeHashSchema.ps1.",
            ContactBindingDecision.ExternalInviteCodeHashColumn, contactId);
        return await WriteAsync(HttpMethod.Patch, path, plain, ("If-Match", version), ct).ConfigureAwait(false);
    }

    /// <summary>The consuming bind's only extra column: a write fault naming it is a missing hash column (#2005).</summary>
    private static readonly IReadOnlyList<string> InviteCodeHashColumn = new[]
    {
        ContactBindingDecision.ExternalInviteCodeHashColumn,
    };

    /// <summary>Every column the consuming bind clears: a write fault naming any is a missing column (#2005).</summary>
    private static readonly IReadOnlyList<string> ConsumedInviteColumns = InviteCodeHashColumn.Concat(InviteStampColumns).ToArray();

    /// <inheritdoc />
    public async Task<StoreWriteResult> CreateContactForOidAsync(
        Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct)
    {
        // A POST carrying the mirror: the key's unique index answers 412 0x80060892 (KeyConflict) when a contact
        // already holds that oid in the mirror. That is exactly one contact per oid even when two first sign-ins
        // race — the loser re-reads by the binding. A conflict the re-read cannot explain (no contact BOUND to the
        // oid) is a squatted mirror: the binder denies and flags. See BuildCreateRequest for why not a keyed PATCH.
        var (method, path, payload) = BuildCreateRequest(oid, plane, details);
        var result = await WriteAsync(method, path, payload, precondition: null, ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(
                HttpMethod.Patch,
                $"systemusers({systemUserId:D})",
                new Dictionary<string, object?> { ["sprk_PrimaryContact@odata.bind"] = $"/contacts({contactId:D})" },
                ("If-Match", version),
                ct)
            : Task.FromResult(NoRowVersion("link", systemUserId));

    private StoreWriteResult NoRowVersion(string what, Guid rowId)
    {
        _logger.LogWarning(
            "[ID-BIND] {What} of row {RowId} refused: no row version to make it conditional (never an unconditional write)",
            what, rowId);
        return new StoreWriteResult(StoreWriteStatus.Failed, Error: $"no row version for the {what}");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Conditional on the row version so a concurrent append is never overwritten. A row read without a version
    /// falls back to <c>*</c>: the worst case is a lost party (re-recorded on that identity's next collision),
    /// never a binding or a link — those stay strictly version-conditional (<see cref="RowVersionPrecondition"/>).
    /// </remarks>
    public Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", FlagPayload(flag),
            ("If-Match", RowVersionPrecondition(etag) ?? "*"), ct);

    /// <inheritdoc />
    public Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", ClearFlagPayload(), ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("flag clear", contactId));

    // ── Scans ──────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct)
    {
        var page = await ScanAsync(continuation ?? BuildSystemUserScanPath(), ParseSystemUserRow, ct).ConfigureAwait(false);
        var linked = page.Status == LookupStatus.Read
            ? page.Rows.Where(r => r.LinkedContact is not null).Select(r => r.LinkedContact!).ToList()
            : new List<ContactBindingRow>();
        if (linked.Count == 0)
        {
            return page;
        }

        var (binding, collision) = await ProofsForAsync(linked, ct).ConfigureAwait(false);
        var removed = await RemovedMarkerProofForAsync(linked, ct).ConfigureAwait(false);
        return page with
        {
            Rows = page.Rows.Select(r => WithRemovedMarkerProof(WithIdentityColumnsProof(r, binding, collision), removed)).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct)
    {
        var page = await ScanAsync(continuation ?? BuildFlaggedContactScanPath(), ParseContactRow, ct).ConfigureAwait(false);
        if (page.Status != LookupStatus.Read)
        {
            return page;
        }

        var proven = await ProvenAsync(ContactLookup.Of(page.Rows.ToArray()), ct).ConfigureAwait(false);
        return page with { Rows = proven.Rows };
    }

    /// <summary>
    /// Applies the readability proofs to a read. Every row needs the collision columns' proof: a hidden flag, or a hidden
    /// part of one, reads like no flag whatever the plane holds. The binding columns' proof is needed only when a plane
    /// read empty, since a plane that was read is a value. A read with no rows needs no proof, so none is made for it.
    /// </summary>
    private async Task<ContactLookup> ProvenAsync(ContactLookup lookup, CancellationToken ct)
    {
        if (lookup.Status != LookupStatus.Read || lookup.Rows.Count == 0)
        {
            return lookup;
        }

        var (binding, collision) = await ProofsForAsync(lookup.Rows, ct).ConfigureAwait(false);
        var marked = WithIdentityColumnsProof(lookup, binding, collision);
        return WithRemovedMarkerProof(marked, await RemovedMarkerProofForAsync(marked.Rows, ct).ConfigureAwait(false));
    }

    private async Task<SystemUserIdentityRow> ProvenAsync(SystemUserIdentityRow row, CancellationToken ct)
    {
        if (row.LinkedContact is null)
        {
            return row;
        }

        var (binding, collision) = await ProofsForAsync(new[] { row.LinkedContact }, ct).ConfigureAwait(false);
        var marked = WithIdentityColumnsProof(row, binding, collision);
        return WithRemovedMarkerProof(marked,
            await RemovedMarkerProofForAsync(new[] { marked.LinkedContact! }, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Slice C: the removed marker's proof for <paramref name="rows"/>. Needed only when a marker read empty, since a marker
    /// that was read is a value. A fault or an unproven read is not <see cref="ColumnReadProofOutcome.Proven"/>.
    /// </summary>
    private async Task<ColumnReadProofOutcome> RemovedMarkerProofForAsync(
        IReadOnlyCollection<ContactBindingRow> rows, CancellationToken ct)
        => rows.Any(r => r.RemovedOn is null)
            ? await _removedMarkerProof.ProveAsync(ct).ConfigureAwait(false)
            : ColumnReadProofOutcome.Proven;

    private async Task<(ColumnReadProofOutcome Binding, ColumnReadProofOutcome Collision)> ProofsForAsync(
        IReadOnlyCollection<ContactBindingRow> rows, CancellationToken ct)
    {
        var binding = rows.Any(r => r.RawPlane is null)
            ? await _bindingColumnsProof.ProveAsync(ct).ConfigureAwait(false)
            : ColumnReadProofOutcome.Proven;
        var collision = await _collisionColumnsProof.ProveAsync(ct).ConfigureAwait(false);
        return (binding, collision);
    }

    /// <summary>
    /// The readability proof's reads over this store's Web API channel (its environment, its token). A read that does not
    /// answer throws, which the proof reports as <see cref="ColumnReadProofOutcome.Faulted"/>.
    /// </summary>
    private sealed class WebApiColumnReadPermissionSource(DataverseContactIdentityStore store) : IColumnReadPermissionSource
    {
        public async Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct)
        {
            using var doc = await store.ReadProofAsync(BuildWhoAmIPath(), ct).ConfigureAwait(false);
            return GuidOf(doc.RootElement, "UserId");
        }

        public async Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct)
        {
            using var doc = await store.ReadProofAsync(BuildSystemAdministratorRolePath(user), ct).ConfigureAwait(false);
            return Values(doc.RootElement).Any();
        }

        public async Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
            Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct)
        {
            using var doc = await store.ReadProofAsync(BuildFieldPermissionReadPath(user, table, columns, throughTeam), ct)
                .ConfigureAwait(false);
            return Values(doc.RootElement)
                .Select(r => StringOf(r, "attributelogicalname"))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!)
                .ToArray();
        }
    }

    private async Task<JsonDocument> ReadProofAsync(string path, CancellationToken ct)
    {
        var (status, code, body, error) = await GetAsync(path, ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || code == HttpStatusCode.NotFound || body is null)
        {
            throw new InvalidOperationException($"A readability-proof read did not answer ({status}, {error ?? code.ToString()}).");
        }

        return TryParseJson(body) ?? throw new InvalidOperationException("A readability-proof read did not answer JSON.");
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────────────────────────────

    private async Task<ContactLookup> ReadContactsAsync(string path, string what, CancellationToken ct, string? logLabel = null)
    {
        var (status, _, body, _) = await GetAsync(path, ct, logLabel: logLabel).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            if (status == LookupStatus.ColumnMissing)
            {
                _logger.LogError(
                    "[ID-BIND] {What} failed: a binding column is not provisioned in this environment ({DenyCode}). "
                    + "Apply scripts/Set-ContactIdentityBindingSchema.ps1 before this BFF build.",
                    what, ContactBindingDecision.DenyBindingColumnMissing);
                return ContactLookup.ColumnMissing;
            }

            return ContactLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return ContactLookup.Failed;
        }

        var rows = Values(doc.RootElement).Select(ParseContactRow).Where(r => r is not null).Select(r => r!).ToList();
        return await ProvenAsync(new ContactLookup(LookupStatus.Read, rows), ct).ConfigureAwait(false);
    }

    private async Task<StorePage<T>> ScanAsync<T>(string pathOrNextLink, Func<JsonElement, T?> parse, CancellationToken ct)
        where T : class
    {
        var (status, _, body, error) = await GetAsync(pathOrNextLink, ct,
            prefer: $"odata.maxpagesize={ScanPageSize}").ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            return new StorePage<T>(status == LookupStatus.Read ? LookupStatus.Failed : status, Array.Empty<T>(), null, error);
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return new StorePage<T>(LookupStatus.Failed, Array.Empty<T>(), null, "the response body is not JSON");
        }

        var rows = Values(doc.RootElement).Select(parse).Where(r => r is not null).Select(r => r!).ToList();
        var next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        return new StorePage<T>(LookupStatus.Read, rows, next);
    }

    /// <param name="missingColumns">
    /// Tasks 184 and 185: the columns whose absence this caller handles itself (the pending-invite marker and its code
    /// hash). When the FULL error body says one of them does not exist, the write answers
    /// <see cref="StoreWriteStatus.ColumnMissing"/>; every other caller passes null and keeps reporting a missing column as
    /// <see cref="StoreWriteStatus.Failed"/>.
    /// </param>
    private async Task<StoreWriteResult> WriteAsync(
        HttpMethod method, string path, Dictionary<string, object?> payload, (string Name, string Value)? precondition,
        CancellationToken ct, IReadOnlyList<string>? missingColumns = null)
    {
        try
        {
            var token = await _getToken(ct).ConfigureAwait(false);
            using var request = BuildWriteRequest(method, new Uri(Absolute(path)), payload, token, precondition);

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Guid? created = null;
                if (response.Headers.TryGetValues("OData-EntityId", out var ids))
                {
                    var raw = ids.FirstOrDefault();
                    var open = raw?.LastIndexOf('(') ?? -1;
                    if (raw is not null && open >= 0 && Guid.TryParse(raw[(open + 1)..].TrimEnd(')'), out var g))
                    {
                        created = g;
                    }
                }

                return new StoreWriteResult(StoreWriteStatus.Written, created);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Checked BEFORE the generic 412: the duplicate-key fault is also a 412, but a re-read cannot fix it.
            if (IsDuplicateKey(body))
            {
                return new StoreWriteResult(StoreWriteStatus.KeyConflict, Error: Trim(body));
            }

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return new StoreWriteResult(StoreWriteStatus.PreconditionFailed, Error: Trim(body));
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new StoreWriteResult(StoreWriteStatus.NotFound, Error: Trim(body));
            }

            if (IsKeyMissing(body))
            {
                return new StoreWriteResult(StoreWriteStatus.KeyMissing, Error: Trim(body));
            }

            if (missingColumns is { Count: > 0 } && IsWriteColumnMissing(body, missingColumns.ToArray()))
            {
                return new StoreWriteResult(StoreWriteStatus.ColumnMissing, Error: Trim(body));
            }

            _logger.LogWarning("[ID-BIND] {Method} {Path} failed: {Status} {Body}", method, path, (int)response.StatusCode, Trim(body));
            if (method == HttpMethod.Post && IsOwnerPrivilegeFault(response.StatusCode, body))
            {
                return new StoreWriteResult(StoreWriteStatus.OwnerPrivilegeMissing, Error: $"{(int)response.StatusCode}: {Trim(body)}");
            }

            if (IsPermissionFault(response.StatusCode, body))
            {
                return new StoreWriteResult(StoreWriteStatus.Forbidden, Error: $"{(int)response.StatusCode}: {Trim(body)}");
            }

            return new StoreWriteResult(StoreWriteStatus.Failed, Error: $"{(int)response.StatusCode}: {Trim(body)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timeout surfaces as TaskCanceledException without ct being cancelled — a failure, not a cancel.
            _logger.LogWarning(ex, "[ID-BIND] {Method} {Path} threw", method, path);
            return new StoreWriteResult(StoreWriteStatus.Failed, Error: ex.Message);
        }
    }

    /// <param name="path">The request path (never logged when <paramref name="logLabel"/> is given).</param>
    /// <param name="logLabel">
    /// UAC review K2: a FIXED label logged in place of the path by a read whose OData filter carries the caller's email
    /// (PII, URL-escaped in the path). Null for every other read, which logs its path.
    /// </param>
    private async Task<(LookupStatus Status, HttpStatusCode Code, string? Body, string? Error)> GetAsync(
        string path, CancellationToken ct, string? prefer = null, string? logLabel = null)
    {
        try
        {
            var token = await _getToken(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, Absolute(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (prefer is not null) request.Headers.Add("Prefer", prefer);

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (LookupStatus.Read, response.StatusCode, body, null);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // A retrieve-by-id of a row that does not exist. The caller decides what absence means.
                return (LookupStatus.Read, HttpStatusCode.NotFound, null, null);
            }

            var classified = ClassifyError(body);
            if (logLabel is null)
            {
                _logger.LogWarning("[ID-BIND] GET {Path} failed: {Status} {Body}", path, (int)response.StatusCode, Trim(body));
            }
            else
            {
                // A labelled read carries the caller's email in its $filter, and Dataverse echoes the whole filter in a syntax
                // error's message (UAC 047 re-check, probed on dev). Log the status and the error code only, never the body.
                _logger.LogWarning("[ID-BIND] GET {Path} failed: {Status} {ErrorCode}", logLabel, (int)response.StatusCode,
                    ErrorCodeOf(body) ?? "(no error code)");
            }
            return (classified, response.StatusCode, null, $"{(int)response.StatusCode}: {Trim(body)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ID-BIND] GET {Path} threw — treated as could-not-read (deny)", logLabel ?? path);
            return (LookupStatus.Failed, default, null, ex.Message);
        }
    }

    private string Absolute(string pathOrNextLink)
        => pathOrNextLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? pathOrNextLink
            : $"{_apiUrl}/{pathOrNextLink}";

    /// <summary>
    /// Parses a response body, or null when it is not JSON. A body that cannot be parsed is a could-not-read —
    /// the store's three-state contract (Read / Failed / ColumnMissing) holds even for a malformed response, so a
    /// parse failure denies through the decision instead of escaping as an exception. Pure.
    /// </summary>
    public static JsonDocument? TryParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Values(JsonElement root)
        => root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : Enumerable.Empty<JsonElement>();

    private static string? StringOf(JsonElement row, string name)
        => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? IntOf(JsonElement row, string name)
        => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static Guid? GuidOf(JsonElement row, string name)
        => StringOf(row, name) is { } s && Guid.TryParse(s, out var g) && g != Guid.Empty ? g : null;

    private static IdentityPlaneMarker? PlaneOf(int? value)
        => value is { } v && Enum.IsDefined(typeof(IdentityPlaneMarker), v) ? (IdentityPlaneMarker)v : null;

    private static IdentityCollisionReason? ReasonOf(int? value)
        => value is { } v && Enum.IsDefined(typeof(IdentityCollisionReason), v) ? (IdentityCollisionReason)v : null;

    private static string Trim(string? body) => body is null ? string.Empty : body.Length <= 400 ? body : body[..400];

    /// <summary>A double-checked token cache over a <see cref="TokenCredential"/> (singleton-safe).</summary>
    private sealed class CachedTokenSource(TokenCredential credential, string scope)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private AccessToken? _token;

        public async Task<string> GetAsync(CancellationToken ct)
        {
            if (_token is { } t && t.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) return t.Token;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_token is { } again && again.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) return again.Token;
                _token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
                return _token.Value.Token;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
