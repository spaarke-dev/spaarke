// unified-access-control-r2 task 141 — the ONE binding writer (POML justification item 1).
//
// Runs ContactBindingDecision over IContactIdentityStore and performs the writes it decides: bind an oid,
// create a contact keyed by an oid, set systemuser.sprk_primarycontact, raise a collision flag. Shared by:
//   • WorkforcePrincipalResolver  — the workforce token plane, and the inline link of a licensed user;
//   • CiamContactPrincipalStrategy — the CIAM token plane (repair-only email bind; never creates);
//   • the invite endpoints          — refuse a contact another plane owns;
//   • RegistrationDataverseService — link at systemuser creation, in the TARGET environment (own store);
//   • IdentityLinkReconciliationJob — every licensed user, report-only until the owner switch is thrown.
//
// Why a new class rather than a method on IdentityNormalizationService (CLAUDE.md §11.5): that service is a
// read-only resolver with a 10-minute cache; giving it Dataverse writes adds a second reason to change.
// ExternalParticipationService is the grant-DATA reader. The binding — reads, decision, writes — is one
// reason to change, and it lives here. The CIAM resolution moved here from ExternalParticipationService
// for the same reason: two copies of the rule is how the workforce plane shipped without the CIAM guard.

using System.Collections.Concurrent;
using System.Security.Claims;
using Spaarke.Dataverse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>The outcome of a token-plane binding.</summary>
/// <param name="ContactId">The resolved contact; null on a deny.</param>
/// <param name="DenyCode">The deny code; null when resolved.</param>
/// <param name="Action">What happened: ResolveByOid, BindByEmail, CreateByOid, Deny or Collision.</param>
/// <param name="Reason">The collision reason, for a collision.</param>
/// <param name="Wrote">True when this call wrote a binding, a contact or a flag.</param>
public sealed record ContactBindingResult(
    Guid? ContactId,
    string? DenyCode,
    BindingAction Action,
    IdentityCollisionReason? Reason = null,
    bool Wrote = false)
{
    /// <summary>True when a contact was resolved.</summary>
    public bool IsResolved => ContactId is { } id && id != Guid.Empty && DenyCode is null;

    internal static ContactBindingResult Resolved(Guid contactId, BindingAction action, bool wrote = false)
        => new(contactId, null, action, Wrote: wrote);

    internal static ContactBindingResult Denied(string code, BindingAction action = BindingAction.Deny,
        IdentityCollisionReason? reason = null, bool wrote = false)
        => new(null, code, action, reason, wrote);
}

/// <summary>What a systemuser link attempt did (or, report-only, would do).</summary>
public enum SystemUserLinkOutcome
{
    /// <summary>Already linked to a contact carrying the user's oid. Nothing to do.</summary>
    Verified,

    /// <summary>Linked to the contact already bound to the user's oid.</summary>
    Linked,

    /// <summary>Email-bound an unbound contact and linked it.</summary>
    BoundAndLinked,

    /// <summary>Created a contact keyed by the oid and linked it.</summary>
    CreatedAndLinked,

    /// <summary>Bound the user's oid onto the contact they were already linked to.</summary>
    BoundLinkedContact,

    /// <summary>A collision: refused and flagged.</summary>
    Flagged,

    /// <summary>A collision whose flag was already present — nothing written (idempotent).</summary>
    FlagAlreadyPresent,

    /// <summary>Refused without a flag (lookup failure, masking, missing linked contact, no oid).</summary>
    Denied,

    /// <summary>
    /// A write failed. The reconciliation job re-decides the user on its next run once its writes are enabled — in
    /// this BFF's own environment and in every environment it provisions users into (third fix round; see
    /// IdentityLinkReconciliationJob).
    /// </summary>
    Failed,
}

/// <summary>One row change, recorded before it is made (the job's before-state log).</summary>
public sealed record IdentityChange(string Entity, Guid? RowId, string Before, string After);

/// <summary>A systemuser link attempt's result.</summary>
public sealed record SystemUserLinkResult(
    Guid SystemUserId,
    SystemUserLinkOutcome Outcome,
    Guid? ContactId,
    string? DenyCode,
    IdentityCollisionReason? Reason,
    IReadOnlyList<Guid> FlaggedContactIds,
    IReadOnlyList<IdentityChange> Changes,
    bool WritesApplied)
{
    /// <summary>The user now has (or, report-only, would have) a verified link.</summary>
    public bool IsLinked => Outcome is SystemUserLinkOutcome.Verified or SystemUserLinkOutcome.Linked
        or SystemUserLinkOutcome.BoundAndLinked or SystemUserLinkOutcome.CreatedAndLinked
        or SystemUserLinkOutcome.BoundLinkedContact;
}

/// <summary>The invite path's view of the contact its email resolves to.</summary>
public sealed record InviteContactResolution(
    InviteContactAction Action,
    Guid? ContactId,
    string? ETag,
    string? ReasonCode,
    string? Message);

/// <summary>
/// Task 185 — how the invite's marker write ended (<see cref="ContactIdentityBinder.MarkInviteSentAsync"/>), and, only when
/// it was <see cref="StoreWriteStatus.Written"/>, the new invite code for the invite email's link.
/// </summary>
/// <param name="Write">The marker-and-hash write's result.</param>
public sealed record InviteMarkResult(StoreWriteResult Write)
{
    /// <summary>The write's status.</summary>
    public StoreWriteStatus Status => Write.Status;

    /// <summary>
    /// The plaintext invite code whose hash was just stored, or null when the write did not land. It goes ONLY into the
    /// invite email's link: never stored, never logged, never in an API response. INTERNAL so the compiler-generated
    /// <see cref="object.ToString"/> of this record never prints it (CLAUDE.md §9).
    /// </summary>
    internal string? InviteCode { get; init; }

    /// <summary>
    /// Slice C2b: the invite stamp's stored email hash this write put on the contact (salted per stamp, so it names THIS
    /// invite), or null when the write did not land. <see cref="ContactIdentityBinder.RetractUnsentInviteAsync"/> clears the
    /// invite only while the contact still carries it. INTERNAL: never printed by the record's <see cref="object.ToString"/>.
    /// </summary>
    internal string? WrittenEmailHash { get; init; }

    /// <summary>
    /// #2005 — set only when the contact had no row version (one the invite just created) and its re-read refused the mark:
    /// the invite decision over that row (<see cref="InviteContactAction.Refuse"/>, <see cref="InviteContactAction.Fail"/>,
    /// <see cref="InviteContactAction.AlreadyProvisioned"/>, or any other action that is not an active, unbound contact).
    /// Nothing was written.
    /// </summary>
    public InviteContactResolution? Reread { get; init; }
}

/// <summary>Slice C2b: what <see cref="ContactIdentityBinder.RetractUnsentInviteAsync"/> did.</summary>
public enum InviteRetractOutcome
{
    /// <summary>The invite this call wrote was cleared: no pending invite is left without its email.</summary>
    Retracted,

    /// <summary>The contact no longer carries this invite (a newer invite, or a bind, replaced it): nothing was written.</summary>
    Superseded,

    /// <summary>The invite could not be read or cleared (logged at Error): it may still be pending.</summary>
    Failed,
}

/// <summary>ADR-028 A9 (r3 task 047): a home-directory caller's registration, as the route validated it.</summary>
/// <param name="HomeTenantId">The token's <c>tid</c>.</param>
/// <param name="HomeObjectId">The token's <c>oid</c>.</param>
/// <param name="Email">The token's <c>email</c> claim only (never <c>preferred_username</c>), trimmed.</param>
/// <param name="FirstName">The form's first name, trimmed and validated.</param>
/// <param name="LastName">The form's last name, trimmed and validated.</param>
/// <param name="Department">The form's optional department.</param>
/// <param name="ConfirmExistingRecord">True when the person confirmed binding to an existing contact (409 first).</param>
/// <param name="CustomerBusinessUnitId"><c>ExternalAccess:CustomerBusinessUnitId</c>; null when not configured.</param>
/// <param name="SecureBusinessUnitName">
/// The Secure Record business unit's name (<c>SecureRecordOwnerTeam.BusinessUnitName(configuration)</c>): the create is
/// refused when <paramref name="CustomerBusinessUnitId"/> is that business unit.
/// </param>
/// <param name="EmailDomainOwnerVerified">
/// The home token's <c>xms_edov</c> (<see cref="WorkforceCallerClaims.EmailDomainOwnerVerifiedOf"/>): true only for a boolean
/// true claim. Binding to an existing contact needs it, or the invite stamp's tenant to be <paramref name="HomeTenantId"/>.
/// Absent (the default) is not verified.
/// </param>
public sealed record HomeRegistrationRequest(
    Guid HomeTenantId,
    Guid HomeObjectId,
    string Email,
    string FirstName,
    string LastName,
    string? Department,
    bool ConfirmExistingRecord,
    Guid? CustomerBusinessUnitId,
    string SecureBusinessUnitName,
    bool? EmailDomainOwnerVerified = null);

/// <summary>What a registration did.</summary>
public enum HomeRegistrationOutcome
{
    /// <summary>One WorkforceHome contact was created.</summary>
    Created,

    /// <summary>An existing, unbound contact was bound to the home identity on confirmation.</summary>
    BoundExisting,

    /// <summary>The (tid, oid) already has its WorkforceHome contact (a resubmit, or a concurrent winner): nothing written.</summary>
    AlreadyRegistered,

    /// <summary>The token email is an enabled systemuser's: refused (<c>licensed_user_link_required</c>).</summary>
    LicensedUser,

    /// <summary>One bindable existing contact and no confirmation: the form must ask (409).</summary>
    ExistingRecordFound,

    /// <summary>The email's contacts are ambiguous, bound, linked or unreadable: refused, flagged, nothing bound or created.</summary>
    Conflict,

    /// <summary>No owner team could be resolved from <c>ExternalAccess:CustomerBusinessUnitId</c>: nothing created.</summary>
    OwnerUnconfigured,

    /// <summary>Denied with <see cref="HomeRegistrationResult.DenyCode"/> (a read, write, masking or key fault).</summary>
    Denied,
}

/// <summary>A registration's result.</summary>
public sealed record HomeRegistrationResult(
    HomeRegistrationOutcome Outcome, Guid? ContactId = null, string? DenyCode = null, bool Wrote = false)
{
    /// <summary>
    /// C5: with <see cref="ContactBindingDecision.DenyContactRemoved"/>, <c>true</c> when the removed marker that refused the
    /// registration is not set but its empty read is not proven (the route answers a retryable 503), <c>false</c> for an
    /// administrator's removal (a final 403, answered as not invited).
    /// </summary>
    public bool RemovedMarkerUnproven { get; init; }

    internal static HomeRegistrationResult Denied(string denyCode, bool wrote = false)
        => new(HomeRegistrationOutcome.Denied, null, denyCode, wrote);
}

/// <summary>What the A9 link route did (r3 task 048, design §5).</summary>
public enum HomeLinkWriteOutcome
{
    /// <summary>The link was written onto the guest's systemuser.</summary>
    Linked,

    /// <summary>The guest's systemuser already carried exactly this link: nothing written.</summary>
    AlreadyLinked,

    /// <summary>No systemuser carries the guest's Spaarke-tenant object id: nothing written.</summary>
    NoSystemUser,

    /// <summary>The guest's systemuser is disabled: nothing written.</summary>
    SystemUserDisabled,

    /// <summary>A different link exists, or the pair is held elsewhere: nothing written, nothing re-pointed.</summary>
    Collision,

    /// <summary>Denied with <see cref="HomeLinkWriteResult.DenyCode"/> (a read, masking, schema or write fault).</summary>
    Denied,
}

/// <summary>A link attempt's result.</summary>
public sealed record HomeLinkWriteResult(HomeLinkWriteOutcome Outcome, Guid? SystemUserId = null, string? DenyCode = null)
{
    internal static HomeLinkWriteResult Denied(string denyCode) => new(HomeLinkWriteOutcome.Denied, null, denyCode);
}

/// <summary>Runs the binding decision and performs its writes. See the file header.</summary>
public sealed class ContactIdentityBinder
{
    private readonly IContactIdentityStore _store;
    private readonly IOptionsMonitor<WorkforceIdentityOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ContactIdentityBinder> _logger;
    private readonly ContactCreateOwner _createOwner;

    /// <summary>Creates the binder over <paramref name="store"/>.</summary>
    /// <param name="createOwner">
    /// ISS-005 (#1673): who owns the contacts this binder creates on the Workforce plane (first sign-in and the licensed-user
    /// link). REQUIRED, with no default, so a binder cannot be built without saying: the DI-registered binder - the one over
    /// THIS BFF's own Dataverse environment - takes <see cref="ContactCreateOwner.FromCustomerBusinessUnit"/>, and an unset
    /// setting refuses the create; only a binder over ANOTHER environment (<see cref="ContactIdentityBinderFactory"/>) takes
    /// <see cref="ContactCreateOwner.NoneForForeignEnvironment"/>.
    /// </param>
    public ContactIdentityBinder(
        IContactIdentityStore store,
        IOptionsMonitor<WorkforceIdentityOptions> options,
        TimeProvider time,
        ILogger<ContactIdentityBinder> logger,
        ContactCreateOwner createOwner)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _createOwner = createOwner ?? throw new ArgumentNullException(nameof(createOwner));
    }

    /// <summary>The store this binder writes through (the job reuses it for its scans).</summary>
    public IContactIdentityStore Store => _store;

    /// <summary>
    /// The owner's rollout switch for LICENSED-USER link writes in this BFF's own environment — the
    /// reconciliation job AND the inline link at first resolution. Absent, empty or unparseable = report-only.
    /// </summary>
    /// <remarks>
    /// Linking a systemuser to an existing contact hands that user the contact's grants (the class of write
    /// ExternalAccessReconciliationJob's R1 is). The switch exists so the dev live gate can review a report-only
    /// run before any such write lands; an ungated inline link would make those writes as soon as a licensed user
    /// signed in after the deploy, ahead of the review (verifier finding 4). The job's writes in the environments
    /// this BFF provisions users into (its provisioning-target pass) are gated by the same switch. Not gated,
    /// deliberately: the Type-2 token plane (a member's own first sign-in, behind the member test — gating it would
    /// deny every Type-2 caller) and registration (an operator-initiated link of a systemuser the BFF itself just
    /// created; a link that does not land there is re-decided by the job's provisioning-target pass on its next run
    /// once this switch is on).
    /// </remarks>
    public const string LinkWritesEnabledConfigKey = "IdentityLink:Reconciliation:WritesEnabled";

    /// <summary>True only when <see cref="LinkWritesEnabledConfigKey"/> parses to <c>true</c>.</summary>
    public static bool LinkWritesEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return bool.TryParse(configuration[LinkWritesEnabledConfigKey], out var enabled) && enabled;
    }

    // ── Workforce token plane ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a workforce caller with no systemuser (Type 2). Only a MEMBER (<c>acct = 0</c>) of a configured customer
    /// tenant resolves: through its existing oid binding, else by an email bind on first sign-in, else a contact created
    /// keyed by the oid. Everyone else is refused with its member-test deny code before any store call — on every
    /// sign-in, so an existing oid binding never admits a caller who fails the member test today (#2128 round 1).
    /// </summary>
    /// <remarks>
    /// #2128 (owner 2026-10-20, option 1) retires the FR-15 guest auto-create. A customer-tenant guest — the member test's
    /// FR-15 conjunction (<c>acct = 1</c>, <c>tid</c> = <paramref name="stampTenantId"/>, home tenant from <c>idp</c> on
    /// the customer list), which still answers <see cref="WorkforceMembership.Member"/> — is refused
    /// <see cref="WorkforceMembershipTest.DenyGuestUseHomeSignin"/> BEFORE any read: no contact is created, none is
    /// email-bound, and a contact already bound to the guest's Spaarke-tenant oid is not resolved either. Under ADR-028 A9
    /// customer staff sign in at their home directory and register there; that is the one door, and one contact shape.
    /// A guest whose home tenant is not listed gets its member-test deny, with or without an existing oid binding.
    /// </remarks>
    /// <param name="stampTenantId">
    /// The stamp's own tenant id (<c>AzureAd:TenantId</c>), never a token value. REQUIRED — there is deliberately no
    /// overload without it, so no caller can skip the guest test by omission; pass <c>null</c> to say "no stamp tenant".
    /// </param>
    public async Task<ContactBindingResult> ResolveWorkforceCallerAsync(
        ClaimsPrincipal user, Guid callerOid, string? stampTenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = WorkforceCallerClaims.From(user);
        var membership = WorkforceMembershipTest.Evaluate(
            claims.Kind, claims.TenantId, claims.Acct, _options.CurrentValue.ParsedCustomerTenantIds(),
            stampTenantId, claims.Idp);

        // #2128: Member with acct = 1 is exactly the FR-15 customer-tenant guest (the member test admits acct = 1 only
        // through that conjunction). Refused here, before the oid lookup, so neither a create, an email bind nor an
        // existing guest binding can admit it.
        if (membership == WorkforceMembership.Member && claims.Acct?.Trim() == "1")
        {
            var refused = ContactBindingResult.Denied(WorkforceMembershipTest.DenyGuestUseHomeSignin);
            var homeTenant = WorkforceMembershipTest.TryParseHomeTenant(claims.Idp, out var home)
                ? home.ToString("D")
                : "unparsed";
            _logger.LogWarning(
                "[ID-BIND] {Plane} caller oid={OidPrefix}… DENIED ({DenyCode}) via {Action} ({Facts})",
                BindingPlane.WorkforceToken, callerOid.ToString("D")[..8], refused.DenyCode, refused.Action,
                $"{membership} (customer-tenant guest, homeTenant={homeTenant}); nothing read, bound or created, "
                + "directed to home sign-in (#2128)");
            return refused;
        }

        var request = new BindingRequest(
            BindingPlane.WorkforceToken,
            callerOid,
            claims.Email,
            membership == WorkforceMembership.Member ? BindingEligibility.EmailBindAndCreate : BindingEligibility.None,
            WorkforceMembershipTest.DenyCodeFor(membership))
        {
            // The invite stamp's tenant leg (E11): the token's tid, and xms_edov only as a boolean true.
            TokenTenantId = Guid.TryParse(claims.TenantId?.Trim(), out var tokenTenant) && tokenTenant != Guid.Empty
                ? tokenTenant
                : null,
            EmailDomainOwnerVerified = claims.EmailDomainOwnerVerified,
        };

        // #2128 round 1: the member test gates EVERY sign-in on this plane, not only the first. A stored binding does not
        // outlive it — a guest whose home tenant is not listed, or a member whose tenant was since removed from the
        // customer list, is refused with its member-test code before the oid lookup (no store call).
        if (membership != WorkforceMembership.Member)
        {
            var notMember = ContactBindingResult.Denied(WorkforceMembershipTest.DenyCodeFor(membership)!);
            Log(request, notMember, $"{membership}; refused before the oid lookup (#2128)");
            return notMember;
        }

        var details = new NewContactDetails(
            claims.GivenName,
            LastNameFor(claims.FamilyName, claims.DisplayName, claims.Email),
            claims.Email);

        var result = await RunUnlinkedAsync(request, details, ct).ConfigureAwait(false);

        Log(request, result, membership.ToString());
        return result;
    }

    // ── CIAM token plane ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a CIAM (external) caller. By oid; the email bind survives for an invited contact that carries no oid yet —
    /// an invite whose oid write failed after the CIAM account was created, or an invite of a person who already had a
    /// CIAM account (task 184, "ExistingAccount") — and binds only on THIS environment's own invite, proved by its code
    /// (E11's marker leg, task 185): the bind then consumes the code in the same write. The CIAM plane never creates a
    /// contact — external users arrive by invitation.
    /// </summary>
    /// <param name="inviteCode">
    /// The invite code the caller presented (task 185), or null for none. Never logged. Slice C reads it from the
    /// <c>X-Spaarke-Invite-Code</c> header.
    /// </param>
    public async Task<ContactBindingResult> ResolveCiamCallerAsync(
        string? oidClaim, string? email, string? inviteCode, CancellationToken ct)
    {
        var oid = Guid.TryParse(oidClaim?.Trim(), out var parsed) ? parsed : Guid.Empty;
        var request = new BindingRequest(BindingPlane.CiamToken, oid, email, BindingEligibility.EmailBindOnly)
        {
            InviteCode = inviteCode,
        };
        var result = await RunUnlinkedAsync(request, details: null, ct).ConfigureAwait(false);
        Log(request, result, "ciam");
        return result;
    }

    // ── Systemuser plane ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads the systemuser and ensures its link (see the row overload).</summary>
    public async Task<SystemUserLinkResult> EnsureSystemUserLinkAsync(
        Guid systemUserId, bool applyWrites, CancellationToken ct)
    {
        var lookup = await _store.GetSystemUserAsync(systemUserId, ct).ConfigureAwait(false);
        if (lookup.Status != LookupStatus.Read || lookup.Row is null)
        {
            return Result(systemUserId, SystemUserLinkOutcome.Denied, null,
                lookup.Status == LookupStatus.ColumnMissing
                    ? ContactBindingDecision.DenyBindingColumnMissing
                    : ContactBindingDecision.DenyContactLookupFailed,
                null, Array.Empty<Guid>(), Array.Empty<IdentityChange>(), false);
        }

        return await EnsureSystemUserLinkAsync(lookup.Row, applyWrites, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures a licensed user's link: <c>contact.sprk_externalobjectid</c> = the user's oid AND
    /// <c>sprk_primarycontact</c> pointing at that contact — or a durable collision flag. The oid is
    /// <c>azureactivedirectoryobjectid</c> and the email <c>internalemailaddress</c>, both directory-synced,
    /// never a client value. An existing link is NEVER re-pointed or cleared. With
    /// <paramref name="applyWrites"/> false nothing is written; the result lists the changes it would make.
    /// </summary>
    public async Task<SystemUserLinkResult> EnsureSystemUserLinkAsync(
        SystemUserIdentityRow user, bool applyWrites, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Oid is not { } oid || oid == Guid.Empty)
        {
            return Result(user.SystemUserId, SystemUserLinkOutcome.Denied, null,
                ContactBindingDecision.DenyUnidentifiableCaller, null, Array.Empty<Guid>(),
                Array.Empty<IdentityChange>(), false);
        }

        // The cheap path the job runs for almost every user once the link exists: the expanded linked contact
        // already carries this user's oid and is active. No further query.
        if (user.PrimaryContactId is { } linkedId && user.LinkedContact is { } expanded
            && expanded.ContactId == linkedId && expanded.IsActive
            && expanded.Binding is { Kind: BindingKind.Bound } b && b.Oid == oid)
        {
            return Result(user.SystemUserId, SystemUserLinkOutcome.Verified, linkedId, null, null,
                Array.Empty<Guid>(), Array.Empty<IdentityChange>(), false);
        }

        var guest = ContactBindingDecision.IsGuestDomainName(user.DomainName);
        var request = new BindingRequest(
            BindingPlane.SystemUser,
            oid,
            user.InternalEmail,
            guest ? BindingEligibility.CreateUnlessEmailMatches : BindingEligibility.EmailBindAndCreate);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var oidLookup = await _store.FindContactsByOidAsync(oid, ct).ConfigureAwait(false);
            var known = new Dictionary<Guid, ContactBindingRow>();
            if (attempt == 0 && user.LinkedContact is { } scannedLink)
            {
                known[scannedLink.ContactId] = scannedLink;
            }

            Remember(known, oidLookup);

            BindingDecision decision;
            ContactLookup? emailLookup = null;

            if (user.PrimaryContactId is { } linked)
            {
                decision = ContactBindingDecision.DecideExistingLink(request, linked, oidLookup);
                ContactLookup? linkedLookup = null;
                if (decision.Action == BindingAction.NeedLinkedContact)
                {
                    linkedLookup = attempt == 0 && user.LinkedContact?.ContactId == linked
                        ? ContactLookup.Of(user.LinkedContact)
                        : await _store.GetContactAsync(linked, ct).ConfigureAwait(false);
                    Remember(known, linkedLookup);
                    decision = ContactBindingDecision.DecideExistingLink(request, linked, oidLookup, linkedLookup);
                }

                if (decision.Action == BindingAction.NeedReferenceLookup)
                {
                    var refs = await _store.FindSystemUsersLinkingAsync(new[] { linked }, ct).ConfigureAwait(false);
                    decision = ContactBindingDecision.DecideExistingLink(request, linked, oidLookup, linkedLookup, refs);
                }

                LogRemovedDeny(decision, request.Plane);
            }
            else
            {
                (decision, emailLookup, _) = await DecideUnlinkedAsync(request, oidLookup, known, ct).ConfigureAwait(false);
            }

            var outcome = await ApplySystemUserDecisionAsync(user, request, decision, known, applyWrites,
                    lastAttempt: attempt == 1, ct)
                .ConfigureAwait(false);
            if (outcome is not null)
            {
                return outcome;
            }

            // null = a write lost a race (412). Re-decide once against fresh rows.
        }

        return Result(user.SystemUserId, SystemUserLinkOutcome.Failed, null, ContactBindingDecision.DenyContactBindFailed,
            null, Array.Empty<Guid>(), Array.Empty<IdentityChange>(), applyWrites);
    }

    /// <summary>
    /// The job's change-log entry for a workforce bind onto <paramref name="contactId"/>. #2005: the bind also clears any
    /// pending invite-code hash, so the entry says so (a dry run must not under-report the write); the hash is never logged.
    /// </summary>
    private static IdentityChange WorkforceBindChange(Guid contactId, Guid oid)
        => new("contact", contactId, "sprk_externalobjectid=(null) sprk_identityplane=(null)",
            $"sprk_externalobjectid={oid:D} sprk_identityplane={IdentityPlaneMarker.Workforce} "
            + $"{ContactBindingDecision.ExternalInviteCodeHashColumn}=(cleared) "
            + $"{ContactBindingDecision.ExternalInvitedEmailHashColumn}=(cleared) "
            + $"{ContactBindingDecision.ExternalInvitedTenantIdColumn}=(cleared)");

    // ── Invite ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the invite email to its contact over ACTIVE rows (two), refusing — and flagging — a contact the
    /// workforce plane owns or an internal user links to. Never binds; the endpoint binds the CIAM oid after it
    /// creates the account (<see cref="BindInvitedContactAsync"/>).
    /// </summary>
    public async Task<InviteContactResolution> ResolveInviteContactAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var emailLookup = await _store.FindActiveContactsByEmailAsync(email, ct).ConfigureAwait(false);
        var resolution = await DecideInviteContactAsync(emailLookup, ct).ConfigureAwait(false);
        if (resolution.Action is not (InviteContactAction.CreateContact or InviteContactAction.ProvisionExisting))
        {
            return resolution;
        }

        // Slice C: no active contact carries the email. A new contact is never created around a removal: an inactive contact
        // for the email whose external access an administrator removed refuses the invite. C5: the same holds for an unbound
        // active contact that shares the email: it is never invited while a removed contact carries that email.
        return await InviteRemovedSameEmailRefusalAsync(email, ct).ConfigureAwait(false) ?? resolution;
    }

    /// <summary>
    /// C5: the invite's answer when an INACTIVE contact for <paramref name="email"/> is one whose external access an
    /// administrator removed (409 <see cref="ContactBindingDecision.InviteContactRemovedAdminReinstate"/>), or whether it is
    /// cannot be established (a retryable failure); <c>null</c> when the invite may go ahead. Runs
    /// <see cref="ContactBindingDecision.DecideCreateAroundRemoval"/>, the one rule, before an invite creates a contact or
    /// provisions an existing unbound one. Read-only; nothing is flagged.
    /// <para>§11. Existing: the same check was inline here and in <see cref="CreateInviteContactGuardedAsync"/>, for a create
    /// only. Extension: it is that check, lifted so the provision of an existing contact runs it too. Cost of doing nothing:
    /// an unbound active contact that shares a removed person's email is invited and bound, so the removal no longer holds
    /// for that email.</para>
    /// </summary>
    internal async Task<InviteContactResolution?> InviteRemovedSameEmailRefusalAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var inactiveLookup = await _store.FindInactiveContactsByEmailAsync(email.Trim(), ct).ConfigureAwait(false);
        var removal = ContactBindingDecision.DecideCreateAroundRemoval(inactiveLookup);
        return removal.MayCreate ? null : InviteRemovalRefusal(removal);
    }

    /// <summary>
    /// Slice C2c: the invite create's ONE guarded contact POST. It runs <see cref="ContactBindingDecision.DecideCreateAroundRemoval"/>
    /// over the inactive contacts for <paramref name="email"/> immediately before the create, so no contact is created around
    /// a removal even when the resolution that answered "create" was read earlier (<c>/invite-and-grant</c> hands its
    /// pre-check's resolution to onboarding). The owner team is the caller's decision and is only written here. The POST's
    /// exceptions propagate unchanged (the caller maps the owner-privilege refusal). Every contact create in server code is
    /// either this method or the store's create seam, reached only through <see cref="CreateContactGuardedAsync"/> (ArchTest
    /// <c>ContactCreateAroundRemovalGuardTests</c>, over the ownership census's create shapes).
    /// </summary>
    /// <returns>The refusal (and no contact id) when the create may not go ahead; else the new contact's id.</returns>
    internal async Task<(InviteContactResolution? Refusal, Guid ContactId)> CreateInviteContactGuardedAsync(
        DataverseWebApiClient dataverseClient, string email, string? firstName, string? lastName, Guid ownerTeamId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverseClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (ownerTeamId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Invite create: refusing to create a contact with no owner team (it would be app-owned in the root business unit).");
        }

        var inactiveLookup = await _store.FindInactiveContactsByEmailAsync(email.Trim(), ct).ConfigureAwait(false);
        var removal = ContactBindingDecision.DecideCreateAroundRemoval(inactiveLookup);
        if (!removal.MayCreate)
        {
            return (InviteRemovalRefusal(removal), Guid.Empty);
        }

        var payload = new Dictionary<string, object?>
        {
            ["emailaddress1"] = email,
            ["firstname"] = firstName ?? string.Empty,
            ["lastname"] = lastName ?? email.Split('@')[0],
            ["ownerid@odata.bind"] = $"/teams({ownerTeamId:D})",
        };

        var created = await dataverseClient.CreateAsync(InviteContactEntitySet, payload, ct).ConfigureAwait(false);
        if (created == Guid.Empty)
        {
            throw new InvalidOperationException("Failed to create a contact for the invite.");
        }

        return (null, created);
    }

    /// <summary>The contact entity set the invite create writes to.</summary>
    private const string InviteContactEntitySet = "contacts";

    /// <summary>
    /// Slice C2c: an invite's answer when a contact may not be created around a removal. Only a SET marker is a refusal
    /// (409); an unproven one, like a read that failed, is a retryable failure.
    /// </summary>
    private InviteContactResolution InviteRemovalRefusal(CreateAroundRemovalDecision removal)
    {
        var action = removal.IsRemoval && !removal.MarkerUnproven ? InviteContactAction.Refuse : InviteContactAction.Fail;
        if (removal.IsRemoval && !removal.MarkerUnproven)
        {
            _logger.LogWarning(
                "[ID-BIND] Invite REFUSED ({ReasonCode}) — contact {ContactId}'s external access was removed by an administrator",
                removal.InviteReasonCode, removal.RemovedContactId);
        }
        else if (removal.MarkerUnproven)
        {
            _logger.LogError(
                "[ID-BIND] Invite NOT SENT ({ReasonCode}) — whether contact {ContactId}'s external access was removed could not " +
                "be established (its removed marker's empty read is not proven). Nothing was created or sent; retryable.",
                removal.InviteReasonCode, removal.RemovedContactId);
        }

        return new InviteContactResolution(action, removal.RemovedContactId, null, removal.InviteReasonCode,
            InviteMessage(removal.InviteReasonCode));
    }

    /// <summary>
    /// The invite decision over <paramref name="emailLookup"/> (the email lookup, or #2005's re-read of one contact): the
    /// reference lookup when it needs one, and the flag and the warning on a refusal.
    /// </summary>
    private async Task<InviteContactResolution> DecideInviteContactAsync(ContactLookup emailLookup, CancellationToken ct)
    {
        var decision = ContactBindingDecision.DecideInvite(emailLookup);
        if (decision.Action == InviteContactAction.NeedReferenceLookup && decision.ContactId is { } candidate)
        {
            var refs = await _store.FindSystemUsersLinkingAsync(new[] { candidate }, ct).ConfigureAwait(false);
            decision = ContactBindingDecision.DecideInvite(emailLookup, refs);
        }

        var row = decision.ContactId is { } id ? emailLookup.Rows.FirstOrDefault(r => r.ContactId == id) : null;

        if (decision.Action == InviteContactAction.Refuse
            && decision.ReasonCode == ContactBindingDecision.InviteContactRemovedAdminReinstate)
        {
            // Slice C: not a collision; nothing is flagged. Only an administrator reinstates the person.
            _logger.LogWarning(
                "[ID-BIND] Invite REFUSED ({ReasonCode}) — contact {ContactId}'s external access was removed by an administrator",
                decision.ReasonCode, decision.ContactId);
        }
        else if (decision.ReasonCode == ContactBindingDecision.InviteContactRemovedStateUnavailable)
        {
            // Slice C2c: not a removal; the marker's empty read is not proven. Nothing is flagged, written or sent.
            _logger.LogError(
                "[ID-BIND] Invite NOT SENT ({ReasonCode}) — whether contact {ContactId}'s external access was removed could not " +
                "be established (its removed marker's empty read is not proven). Retryable.",
                decision.ReasonCode, decision.ContactId);
        }
        else if (decision.Action == InviteContactAction.Refuse)
        {
            var known = new Dictionary<Guid, ContactBindingRow>();
            Remember(known, emailLookup);
            await WriteFlagsAsync(decision.FlagContactIds ?? Array.Empty<Guid>(), known, collidingOid: null,
                IdentityPlaneMarker.External, decision.Reason ?? IdentityCollisionReason.InviteMatchesWorkforceContact, ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "[ID-BIND] Invite REFUSED ({ReasonCode}) — contact(s) {Contacts} flagged for an operator",
                decision.ReasonCode, string.Join(",", decision.FlagContactIds ?? Array.Empty<Guid>()));
        }

        return new InviteContactResolution(decision.Action, decision.ContactId, row?.ETag, decision.ReasonCode,
            InviteMessage(decision.ReasonCode));
    }

    /// <summary>
    /// Writes a newly created CIAM account's oid onto the invited contact, plane External — under the same guards
    /// as every other bind: the masking probe first, and a write conditional on the row version. A contact the
    /// invite just CREATED has no version yet, so it is re-read, and bound only if it is still active and unbound
    /// (a concurrent first sign-in may have bound it in between; that binding is never overwritten). C5: nor if its external
    /// access was removed meanwhile: then <see cref="StoreWriteStatus.PreconditionFailed"/> whose Error is the invite reason
    /// code (<see cref="ContactBindingDecision.InviteContactRemovedAdminReinstate"/>, or
    /// <see cref="ContactBindingDecision.InviteContactRemovedStateUnavailable"/> for a marker whose empty read is not proven).
    /// </summary>
    /// <param name="consumeInviteCode">
    /// Task 185 (owner Q5): true when this invite's marker-and-hash write landed, so the bind PATCH also clears the
    /// now-inert code hash in the same request. False when the columns are not provisioned here: no hash can exist, so the
    /// plain bind is sent directly (the consuming bind would first be refused for the missing column, then fall back to it).
    /// </param>
    public async Task<StoreWriteResult> BindInvitedContactAsync(
        Guid contactId, string? etag, Guid ciamOid, bool consumeInviteCode, CancellationToken ct)
    {
        if (await MaskedAsync(ct).ConfigureAwait(false))
        {
            return new StoreWriteResult(StoreWriteStatus.Failed, Error: ContactBindingDecision.DenyBindingColumnMasked);
        }

        if (string.IsNullOrWhiteSpace(etag))
        {
            var lookup = await _store.GetContactAsync(contactId, ct).ConfigureAwait(false);
            var row = lookup.Status == LookupStatus.Read ? lookup.Rows.FirstOrDefault(r => r.ContactId == contactId) : null;
            if (row is null)
            {
                return new StoreWriteResult(StoreWriteStatus.Failed, Error: "the invited contact could not be re-read");
            }

            // C5: a person whose external access an administrator removed while the invite was in progress is never bound
            // by it. The Error carries the invite's own reason code (a set marker, or one whose empty read is not proven), so
            // the invite answers it as the removed refusal and sends nothing.
            if (row.IsRemoved)
            {
                var removedCode = row.RemovedOn is not null
                    ? ContactBindingDecision.InviteContactRemovedAdminReinstate
                    : ContactBindingDecision.InviteContactRemovedStateUnavailable;
                _logger.LogWarning(
                    "[ID-BIND] Invited contact {ContactId} is not bound ({ReasonCode}): its external access was removed by an "
                    + "administrator, or whether it was cannot be established, while the invite was in progress",
                    contactId, removedCode);
                return new StoreWriteResult(StoreWriteStatus.PreconditionFailed, Error: removedCode);
            }

            if (!row.IsActive || row.Binding.Kind != BindingKind.Unbound)
            {
                _logger.LogWarning(
                    "[ID-BIND] Invited contact {ContactId} is no longer an active, unbound contact ({Kind}); its binding "
                    + "is not overwritten", contactId, row.Binding.Kind);
                return new StoreWriteResult(StoreWriteStatus.PreconditionFailed, Error: "the contact was bound or deactivated meanwhile");
            }

            etag = row.ETag;
        }

        return consumeInviteCode
            ? await _store.BindOidAndConsumeInviteAsync(contactId, etag, ciamOid, IdentityPlaneMarker.External, ct)
                .ConfigureAwait(false)
            : await _store.BindOidAsync(contactId, etag, ciamOid, IdentityPlaneMarker.External, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tasks 184 and 185 — creates a NEW invite code and stamps the pending-invite marker
    /// (<c>contact.sprk_externalinvitedon</c> = now, UTC) together with the code's stored hash
    /// (<c>sprk_externalinvitecodehash</c>) on the invited contact, in ONE write, BEFORE the invite creates the CIAM account.
    /// A first CIAM sign-in that has to email-bind (a failed oid write, or a person who already had a CIAM account) then
    /// binds only by presenting this code. A re-invite replaces both, so every earlier code stops matching (superseded);
    /// nothing clears the marker. Conditional on <paramref name="etag"/> (the version the invite decided on) when there is
    /// one. The same write stamps the invite (<see cref="InviteStamp"/>): the salted hash of <paramref name="email"/> and
    /// the tenant the invite named (or none, clearing an earlier one), which every email bind onto the contact needs. It
    /// writes nothing else and does not depend on how the invite then reaches the person.
    /// </summary>
    /// <param name="email">The address the invite is sent to (hashed here; never stored or logged).</param>
    /// <param name="invitedTenantId">
    /// Null for a portal-account (CIAM) invite: a new code is created and its hash stored. The resolved customer tenant for a
    /// work-account invite: no code is created and the stored code hash is cleared, so only a Workforce or WorkforceHome
    /// sign-in from that tenant can bind the contact (<see cref="ContactBindingDecision.IsWorkAccountInvite"/>).
    /// </param>
    /// <returns>
    /// The write's result and, only when it was written for a portal-account invite, the plaintext code for the invite
    /// email's link. The code is returned to the caller only: it is never stored and never logged.
    /// </returns>
    public async Task<InviteMarkResult> MarkInviteSentAsync(
        Guid contactId, string? etag, string email, Guid? invitedTenantId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (invitedTenantId == Guid.Empty)
        {
            throw new ArgumentException("An invited tenant is a non-empty GUID, or null for none.", nameof(invitedTenantId));
        }

        if (DataverseContactIdentityStore.RowVersionPrecondition(etag) is null)
        {
            // #2005: no row version (a contact this invite just created). A workforce sign-in may have email-bound it since
            // (that plane reads no invite marker), so the marker and hash are never written under "*": the contact is
            // re-read and judged by the invite's own rules, and marked only while it is an active, unbound contact no
            // internal user links to, under that read's version. Anything else, a read fault included, writes nothing.
            var lookup = await _store.GetContactAsync(contactId, ct).ConfigureAwait(false);
            var reread = await DecideInviteContactAsync(lookup, ct).ConfigureAwait(false);
            if (reread.Action != InviteContactAction.ProvisionExisting || reread.ContactId != contactId
                || DataverseContactIdentityStore.RowVersionPrecondition(reread.ETag) is null)
            {
                _logger.LogWarning(
                    "[ID-BIND] Pending-invite marker on contact {ContactId} not written: on re-read it is not an active, "
                    + "unbound contact ({Action}, {ReasonCode})", contactId, reread.Action, reread.ReasonCode);
                return new InviteMarkResult(new StoreWriteResult(
                    reread.Action == InviteContactAction.Fail ? StoreWriteStatus.Failed : StoreWriteStatus.PreconditionFailed,
                    Error: "the invited contact is no longer an active, unbound contact"))
                { Reread = reread };
            }

            etag = reread.ETag;
        }

        // A work-account invite (a resolved tenant) carries no code: no CIAM sign-in may bind it.
        string? code = null, storedValue = null;
        if (invitedTenantId is null)
        {
            (code, storedValue) = InviteCode.Create(contactId);
        }
        var stamp = new InviteStamp(InviteEmailStamp.Create(email), invitedTenantId);
        var write = await _store.MarkInvitedAsync(contactId, etag, _time.GetUtcNow(), storedValue, stamp, ct).ConfigureAwait(false);
        if (write.Status == StoreWriteStatus.ColumnMissing)
        {
            _logger.LogError(
                "[ID-BIND] {Marker}, {Hash}, {StampHash} or {Tenant} is not provisioned in this environment, so contact "
                + "{ContactId} carries no pending invite. A new-account invite still binds its oid in the same request; any "
                + "email bind onto it is denied until it is re-invited. Apply scripts/Set-ExternalInviteMarkerSchema.ps1, "
                + "scripts/Set-ExternalInviteCodeHashSchema.ps1 and scripts/Set-ExternalAccessContactSecuritySchema.ps1.",
                ContactBindingDecision.ExternalInvitedOnColumn, ContactBindingDecision.ExternalInviteCodeHashColumn,
                ContactBindingDecision.ExternalInvitedEmailHashColumn, ContactBindingDecision.ExternalInvitedTenantIdColumn,
                contactId);
        }
        else if (write.Status != StoreWriteStatus.Written)
        {
            _logger.LogWarning("[ID-BIND] Pending-invite marker on contact {ContactId} not written ({Status}): {Error}",
                contactId, write.Status, write.Error);
        }

        return new InviteMarkResult(write)
        {
            InviteCode = write.Status == StoreWriteStatus.Written ? code : null,
            WrittenEmailHash = write.Status == StoreWriteStatus.Written ? stamp.InvitedEmailHash : null,
        };
    }

    /// <summary>
    /// Slice C2b: a pending invite exists only if its email went out. Called when an invite's email (the existing-account
    /// email or the work-account notice) did not send, it clears the invite THIS call's marker write put on the contact (the
    /// marker, the code hash and the stamp) so the next invite, staff or automatic, writes a new one and sends it. The
    /// contact is re-read, and cleared only while it still carries <paramref name="writtenEmailHash"/>, conditional on the
    /// row version read with it: a newer invite (or a bind) written meanwhile is never cleared. A row that changed between
    /// the read and the clear is re-read once. Never throws; a failure is logged at Error.
    /// </summary>
    /// <param name="writtenEmailHash">The stamp's email hash the invite wrote (<see cref="InviteMarkResult.WrittenEmailHash"/>).</param>
    public async Task<InviteRetractOutcome> RetractUnsentInviteAsync(
        Guid contactId, string writtenEmailHash, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writtenEmailHash);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            BindIntentLookup intent;
            try
            {
                intent = await _store.FindBindIntentAsync(contactId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "[ID-BIND] The invite on contact {ContactId} whose email did not send could not be read back to clear it; "
                    + "it may still be pending. Invite the person again.", contactId);
                return InviteRetractOutcome.Failed;
            }

            if (intent.InviteStatus != LookupStatus.Read || intent.ContactId != contactId
                || (string.IsNullOrEmpty(intent.InvitedEmailHash) && intent.StampColumnsUnproven))
            {
                _logger.LogError(
                    "[ID-BIND] The invite on contact {ContactId} whose email did not send could not be read back to clear it "
                    + "({Status}); it may still be pending. Invite the person again.", contactId, intent.InviteStatus);
                return InviteRetractOutcome.Failed;
            }

            if (!string.Equals(intent.InvitedEmailHash, writtenEmailHash, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "[ID-BIND] The invite on contact {ContactId} whose email did not send was already replaced (a newer invite or "
                    + "a bind); nothing cleared.", contactId);
                return InviteRetractOutcome.Superseded;
            }

            var write = await _store.ClearPendingInviteAsync(contactId, intent.ETag, ct).ConfigureAwait(false);
            if (write.Status == StoreWriteStatus.Written)
            {
                _logger.LogInformation(
                    "[ID-BIND] The invite on contact {ContactId} whose email did not send was cleared; the next invite sends a "
                    + "new one.", contactId);
                return InviteRetractOutcome.Retracted;
            }

            if (write.Status != StoreWriteStatus.PreconditionFailed)
            {
                _logger.LogError(
                    "[ID-BIND] The invite on contact {ContactId} whose email did not send could not be cleared ({Status}); it "
                    + "may still be pending. Invite the person again.", contactId, write.Status);
                return InviteRetractOutcome.Failed;
            }
        }

        _logger.LogError(
            "[ID-BIND] The invite on contact {ContactId} whose email did not send could not be cleared: the row kept changing. "
            + "It may still be pending. Invite the person again.", contactId);
        return InviteRetractOutcome.Failed;
    }

    /// <summary>The human message carried in a refusal's ProblemDetails.</summary>
    public static string? InviteMessage(string? reasonCode) => reasonCode switch
    {
        ContactBindingDecision.InviteWorkforceBoundContact =>
            "This email belongs to a contact already bound to an employee's work identity. One contact carries one "
            + "sign-in, so an external account cannot be created for it. An administrator must resolve the identity "
            + "collision on the contact first.",
        ContactBindingDecision.InviteContactLinkedToInternalUser =>
            "This email belongs to the contact of an internal user. An external account cannot be attached to it. "
            + "An administrator must resolve the identity collision on the contact first.",
        ContactBindingDecision.InviteEmailAmbiguous =>
            "More than one active contact carries this email, so the invite cannot tell which person it is for. "
            + "Deactivate or correct the duplicates, then invite again.",
        ContactBindingDecision.InviteContactBindingUnreadable =>
            "The contact for this email carries an identity binding that cannot be read. An administrator must "
            + "correct it before the contact can be invited.",
        ContactBindingDecision.InviteContactLookupFailed =>
            "The contact for this email could not be looked up. Nothing was created; try again.",
        ContactBindingDecision.InviteMarkerNotWritten =>
            "The invite could not be recorded on the contact, so the person could not sign in from it. No email was "
            + "sent; try again. If this environment is still receiving its schema update, try again after it completes.",
        ContactBindingDecision.InviteTenantLookupUnavailable =>
            "The person's organisation could not be checked just now, so nothing was created or sent. Try again in a "
            + "moment.",
        ContactBindingDecision.InviteContactAlreadyBound =>
            "This email's contact already signs in with a portal account, so a work-account invite cannot be added to it. "
            + "Grant the access to that contact instead; they keep signing in as they do now.",
        ContactBindingDecision.InviteContactAlreadyHasAccount =>
            "This person already has an account, so no invite was sent and nothing was changed. They sign in at the link "
            + "below; send it to them if they need it.",
        ContactBindingDecision.InviteContactRemovedAdminReinstate =>
            "An administrator removed this person's external access, so they cannot be invited. Nothing was created or "
            + "sent. Ask an administrator to reinstate them first.",
        ContactBindingDecision.InviteContactRemovedStateUnavailable =>
            "Whether this person's external access was removed could not be checked just now, so nothing was created, "
            + "granted or sent. Try again in a moment.",
        _ => null,
    };

    // ── ADR-028 A9 registration (r3 task 047) ─────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-028 A9 (r3 task 047, design §4 and §6 steps 2–4): registers a home-directory caller whom the 046 resolution
    /// did NOT resolve (the route runs step 1 first). Only <c>ExternalRegistrationEndpoints</c> calls this (ArchTest
    /// <c>AuthenticationSchemeCensusTests</c>). In order: probe that the binding and the A9 columns are readable (else
    /// deny, never create); refuse a licensed user's email; decide over the email's ACTIVE contacts (bind one bindable
    /// contact only on confirmation; refuse and flag an ambiguous one); else create one WorkforceHome contact owned by the
    /// customer business unit's default team. A concurrent or repeated submit re-reads by (tid, oid) and writes nothing.
    /// Logs carry the contact id and HASHED tid/oid only — never the email or the names.
    /// </summary>
    public async Task<HomeRegistrationResult> RegisterHomeContactAsync(HomeRegistrationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HomeTenantId == Guid.Empty || request.HomeObjectId == Guid.Empty)
        {
            throw new ArgumentException("A home registration needs the home tenant and object id.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        var tid = request.HomeTenantId;
        var oid = request.HomeObjectId;
        var who = $"tid#{HashForLog(tid)} oid#{HashForLog(oid)}";

        // 046 review (K, carried): a masked secured column makes a registered contact or link look absent — probe first.
        var binding = await _store.ProbeBindingReadabilityAsync(ct).ConfigureAwait(false);
        var home = await _store.ProbeHomeReadabilityAsync(ct).ConfigureAwait(false);
        if (home == BindingReadability.ColumnMissing)
        {
            // Permanent: the A9 schema is not applied here. An operator fix (500 registration_environment), not a retry.
            _logger.LogError(
                "[ID-BIND] Registration for {Who} DENIED ({DenyCode}): an A9 home-identity column is not provisioned. Nothing "
                + "was bound or created. Apply scripts/Set-WorkforceHomeIdentitySchema.ps1.",
                who, ContactBindingDecision.DenyBindingColumnMissing);
            return HomeRegistrationResult.Denied(ContactBindingDecision.DenyBindingColumnMissing);
        }

        if (binding == BindingReadability.Masked || home == BindingReadability.Masked)
        {
            _logger.LogError(
                "[ID-BIND] Registration for {Who} DENIED ({DenyCode}): binding column {Binding}, A9 columns {Home}. Nothing "
                + "was bound or created. Add the BFF application user to the identity-link write profile (or apply "
                + "scripts/Set-WorkforceHomeIdentitySchema.ps1).",
                who, ContactBindingDecision.DenyBindingColumnMasked, binding, home);
            return HomeRegistrationResult.Denied(ContactBindingDecision.DenyBindingColumnMasked);
        }

        if (binding == BindingReadability.Failed || home == BindingReadability.Failed)
        {
            // Transient (a throttle, a 5xx, a network fault): retryable, and not an FLS fault — so a warning, not an error.
            _logger.LogWarning(
                "[ID-BIND] Registration for {Who} DENIED ({DenyCode}): the masking probe could not be read (binding {Binding}, "
                + "A9 columns {Home}). Nothing was bound or created; retryable.",
                who, ContactBindingDecision.DenyContactLookupFailed, binding, home);
            return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactLookupFailed);
        }

        // Design §6 step 2: a licensed user's email is refused (a refusal only — never a link or a grant).
        var licensed = ContactBindingDecision.DecideLicensedUserEmail(
            await _store.FindEnabledSystemUsersByEmailAsync(request.Email, ct).ConfigureAwait(false), request.Email);
        if (licensed != LicensedUserEmailOutcome.NotLicensed)
        {
            _logger.LogWarning("[ID-BIND] Registration for {Who} refused: licensed-user check {Outcome}", who, licensed);
            return licensed == LicensedUserEmailOutcome.LicensedUser
                ? new HomeRegistrationResult(HomeRegistrationOutcome.LicensedUser)
                : HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactLookupFailed);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Design §6 step 3: the token email's ACTIVE contacts.
            var emailLookup = await _store.FindActiveContactsByEmailAsync(request.Email, ct).ConfigureAwait(false);
            var known = new Dictionary<Guid, ContactBindingRow>();
            Remember(known, emailLookup);
            var decision = ContactBindingDecision.DecideRegistrationEmail(emailLookup, oid);
            if (decision.Action == RegistrationEmailAction.NeedReferenceLookup)
            {
                var references = await _store.FindSystemUsersLinkingAsync(new[] { decision.ContactId!.Value }, ct)
                    .ConfigureAwait(false);
                decision = ContactBindingDecision.DecideRegistrationEmail(emailLookup, oid, references);
            }

            switch (decision.Action)
            {
                case RegistrationEmailAction.Deny:
                    _logger.LogWarning("[ID-BIND] Registration for {Who} DENIED ({DenyCode})", who, decision.DenyCode);
                    return HomeRegistrationResult.Denied(decision.DenyCode ?? ContactBindingDecision.DenyContactLookupFailed)
                        with { RemovedMarkerUnproven = decision.RemovedMarkerUnproven };

                case RegistrationEmailAction.Conflict:
                    return await RefuseAndFlagAsync(decision, known, oid, who, ct).ConfigureAwait(false);

                case RegistrationEmailAction.AlreadyBoundToCaller:
                    return await AlreadyRegisteredAsync(tid, oid, who, wroteNothingElse: true, ct).ConfigureAwait(false);

                case RegistrationEmailAction.BindExisting:
                {
                    var target = known[decision.ContactId!.Value];

                    // C5: never bound while an inactive contact for the same email is one whose external access an
                    // administrator removed. Checked before the confirmation prompt, as the invite stamp is.
                    if (await EmailBindAroundRemovalAsync(request.Email, "Registration", who, target.ContactId, ct)
                            .ConfigureAwait(false) is { } removal)
                    {
                        return HomeRegistrationResult.Denied(removal.DenyCode ?? ContactBindingDecision.DenyContactLookupFailed)
                            with { RemovedMarkerUnproven = removal.MarkerUnproven };
                    }

                    // The email-bind rule (as the Workforce token plane, E11): binding an existing contact needs a current
                    // invite stamp whose recorded address is the token's email and the contact's emailaddress1, and whose
                    // tenant is the token's tid unless xms_edov is boolean true. Checked before the confirmation prompt, so
                    // the person is never asked to confirm a bind that would be refused. No fallback to a create.
                    var stamp = await ReadInviteStampAsync(target.ContactId, ct).ConfigureAwait(false);
                    if (stamp.ContactId != target.ContactId
                        || stamp.InviteStatus is not (LookupStatus.Read or LookupStatus.ColumnMissing))
                    {
                        _logger.LogWarning(
                            "[ID-BIND] Registration for {Who} DENIED ({DenyCode}): the invite stamp of contact {ContactId} could "
                            + "not be read. Nothing was bound or created; retryable.",
                            who, ContactBindingDecision.DenyContactLookupFailed, target.ContactId);
                        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactLookupFailed);
                    }

                    if (ContactBindingDecision.CheckInviteStamp(stamp, request.Email, requireTenantProof: true, tid,
                            request.EmailDomainOwnerVerified) is { } stampFailure)
                    {
                        _logger.LogInformation(
                            "[ID-BIND] Registration for {Who} DENIED ({DenyCode}): existing contact {ContactId} is not invited for "
                            + "this sign-in ({NotInvitedReason}). Nothing was bound or created.",
                            who, ContactBindingDecision.DenyContactNotInvited, target.ContactId,
                            ContactBindingDecision.StampReason(stampFailure));
                        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactNotInvited);
                    }

                    if (!SameRowVersion(stamp.ETag, target.ETag))
                    {
                        // The row changed between the email read and the stamp read: re-read and re-decide (once).
                        if (attempt == 0)
                        {
                            continue;
                        }

                        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactBindFailed);
                    }

                    if (!request.ConfirmExistingRecord)
                    {
                        _logger.LogInformation(
                            "[ID-BIND] Registration for {Who} found existing contact {ContactId}: confirmation required",
                            who, target.ContactId);
                        return new HomeRegistrationResult(HomeRegistrationOutcome.ExistingRecordFound, target.ContactId);
                    }

                    // Conditional on the version the stamp was read at (the same as the email read's, checked above).
                    var write = await _store.BindHomeOidAsync(target.ContactId, stamp.ETag, tid, oid, ct).ConfigureAwait(false);
                    if (write.Status == StoreWriteStatus.Forbidden)
                    {
                        // A missing privilege on the contact PATCH (HTTP 403, or a 400 that names a privilege): permanent, an
                        // operator fix - not retryable. Answered like the create's Forbidden (500 registration_environment).
                        _logger.LogError(
                            "[ID-BIND] Registration bind for {Who} on contact {ContactId} refused ({DenyCode}): Dataverse refused "
                            + "the write for a missing privilege (the BFF application user needs write on the contact and its "
                            + "owner). Nothing was bound.",
                            who, target.ContactId, ContactBindingDecision.DenyContactCreateUnavailable);
                        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactCreateUnavailable);
                    }

                    if (write.Status == StoreWriteStatus.Written)
                    {
                        _logger.LogInformation(
                            "[ID-BIND] Registration bound {Who} (WorkforceHome) to existing contact {ContactId} on confirmation",
                            who, target.ContactId);
                        return new HomeRegistrationResult(HomeRegistrationOutcome.BoundExisting, target.ContactId, Wrote: true);
                    }

                    if (write.Status == StoreWriteStatus.PreconditionFailed && attempt == 0)
                    {
                        continue; // the row changed under us — re-decide against fresh rows
                    }

                    if (write.Status is StoreWriteStatus.KeyConflict)
                    {
                        return await KeyConflictRegistrationAsync(tid, oid, who, ct).ConfigureAwait(false);
                    }

                    _logger.LogWarning("[ID-BIND] Registration bind for {Who} on contact {ContactId} failed ({Status})",
                        who, target.ContactId, write.Status);
                    return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactBindFailed);
                }

                case RegistrationEmailAction.Create:
                {
                    // Fix round 2 (owner 2026-10-15, "Refuse, flag for admin"): no ACTIVE match. An INACTIVE contact carrying
                    // the email refuses the registration and is flagged - never a second contact beside a deactivated one,
                    // and never bound (reactivation is an operator decision). Active matches above decide as they always did.
                    var inactiveLookup = await _store.FindInactiveContactsByEmailAsync(request.Email, ct).ConfigureAwait(false);
                    Remember(known, inactiveLookup);
                    var inactive = ContactBindingDecision.DecideInactiveRegistrationEmail(inactiveLookup);
                    if (inactive.Action == RegistrationEmailAction.Deny)
                    {
                        // The inactive-contact read failed, or (slice C) an inactive match's external access was removed.
                        _logger.LogWarning("[ID-BIND] Registration for {Who} DENIED ({DenyCode}) at the inactive-contact check",
                            who, inactive.DenyCode);
                        return HomeRegistrationResult.Denied(inactive.DenyCode ?? ContactBindingDecision.DenyContactLookupFailed)
                            with { RemovedMarkerUnproven = inactive.RemovedMarkerUnproven };
                    }

                    return inactive.Action == RegistrationEmailAction.Conflict
                        ? await RefuseAndFlagAsync(inactive, known, oid, who, ct).ConfigureAwait(false)
                        : await CreateHomeContactAsync(request, who, ct).ConfigureAwait(false);
                }
            }
        }

        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactBindFailed);
    }

    /// <summary>
    /// Refuses a registration whose email matches contact(s) that cannot be bound, and flags them by CAUSE. Nothing is
    /// bound or created; the refusal stands even if the flag could not be written (the log carries the marker).
    /// </summary>
    private async Task<HomeRegistrationResult> RefuseAndFlagAsync(
        RegistrationEmailDecision decision, Dictionary<Guid, ContactBindingRow> known, Guid oid, string who, CancellationToken ct)
    {
        var flagged = decision.FlagContactIds ?? Array.Empty<Guid>();
        // By cause: EmailAmbiguous only for 2+ matches. A single unbindable match carries its own reason so the
        // reconciliation job's CollisionStillHolds keeps the flag while that cause holds.
        var reason = decision.FlagReason ?? IdentityCollisionReason.EmailAmbiguous;
        var wrote = await WriteFlagsAsync(flagged, known, oid, IdentityPlaneMarker.WorkforceHome, reason, ct).ConfigureAwait(false);
        _logger.LogWarning(
            "[ID-BIND] Registration for {Who} refused: the email matches contact(s) [{Contacts}] that cannot be bound "
            + "(ambiguous, bound, linked, unreadable or inactive); flagged {Reason} (wrote={Wrote})",
            who, string.Join(",", flagged), reason, wrote);
        return new HomeRegistrationResult(HomeRegistrationOutcome.Conflict, Wrote: wrote);
    }

    /// <summary>How <see cref="ResolveCustomerOwnerTeamAsync"/> ended (ISS-005, #1673).</summary>
    internal enum OwnerTeamStatus
    {
        /// <summary>One owner team found: <see cref="OwnerTeamResolution.TeamId"/> owns the new contact.</summary>
        Resolved,

        /// <summary>The setting is unset or empty, names the Secure Record business unit, or has no single default owner team: refuse.</summary>
        Unconfigured,

        /// <summary>A read could not be answered: refuse (retryable).</summary>
        LookupFailed,

        /// <summary>
        /// Only from <see cref="ResolveCreateOwnerAsync"/>: this binder was built with
        /// <see cref="ContactCreateOwner.NoneForForeignEnvironment"/> (it is over another environment, where the setting does
        /// not apply), so the create binds no owner.
        /// </summary>
        NotApplicable,
    }

    internal readonly record struct OwnerTeamResolution(OwnerTeamStatus Status, Guid TeamId = default);

    /// <summary>
    /// The ONE lookup of a contact's owner team (design §6; ISS-005, #1673): the single default owner team of
    /// <paramref name="customerBusinessUnitId"/> (<c>ExternalAccess:CustomerBusinessUnitId</c>). Registration, the first
    /// sign-in create and the licensed-user link create all call it, so they refuse alike: nothing is created when the
    /// setting is unset, when it names the Secure Record business unit, or when the unit's default owner teams are not
    /// exactly one; an unreadable answer is a <see cref="OwnerTeamStatus.LookupFailed"/>.
    /// </summary>
    /// <param name="operation">The create being decided, for the log ("Registration", "First sign-in create", ...).</param>
    private async Task<OwnerTeamResolution> ResolveCustomerOwnerTeamAsync(
        Guid? customerBusinessUnitId, string secureBusinessUnitName, string operation, string who, CancellationToken ct)
    {
        // Design §6 create: owned by the default team of ExternalAccess:CustomerBusinessUnitId. Absent → nothing created.
        if (customerBusinessUnitId is not { } bu || bu == Guid.Empty)
        {
            _logger.LogError(
                "[ID-BIND] {Operation} for {Who} refused: ExternalAccess:CustomerBusinessUnitId is not configured, so the "
                + "new contact would have no owner. Nothing was created.", operation, who);
            return new OwnerTeamResolution(OwnerTeamStatus.Unconfigured);
        }

        // The customer business unit must never be the Secure Record business unit: a registrant's contact owned by a team
        // there would be a record in the secure unit. Decided by ID (as RecordOwnershipResolver does); an unreadable or
        // ambiguous answer refuses (fail closed); only "no such business unit in this environment" lets the create through.
        var secure = await _store.FindBusinessUnitsByNameAsync(secureBusinessUnitName, ct).ConfigureAwait(false);
        if (secure.Status != LookupStatus.Read)
        {
            // K4 (UAC re-check): an unreadable answer refuses the create, so the operator must be able to see why.
            // Business unit id and the read's status only: no email, no secret.
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} refused: the Secure Record business unit could not be read ({Status}) while "
                + "resolving the owner for ExternalAccess:CustomerBusinessUnitId {BusinessUnitId}. Nothing was created; retryable.",
                operation, who, secure.Status, bu);
            return new OwnerTeamResolution(OwnerTeamStatus.LookupFailed);
        }

        if (secure.BusinessUnitIds.Count > 1 || secure.BusinessUnitIds.Contains(bu))
        {
            _logger.LogError(
                "[ID-BIND] {Operation} for {Who} refused: ExternalAccess:CustomerBusinessUnitId {BusinessUnitId} is the Secure "
                + "Record business unit (or that unit is ambiguous: {Count} named '{Name}'). Nothing was created.",
                operation, who, bu, secure.BusinessUnitIds.Count, secureBusinessUnitName);
            return new OwnerTeamResolution(OwnerTeamStatus.Unconfigured);
        }

        var teams = await _store.FindDefaultOwnerTeamsAsync(bu, ct).ConfigureAwait(false);
        if (teams.Status != LookupStatus.Read)
        {
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} refused: the default owner team of business unit {BusinessUnitId} could not be "
                + "read ({Status}). Nothing was created; retryable.",
                operation, who, bu, teams.Status);
            return new OwnerTeamResolution(OwnerTeamStatus.LookupFailed);
        }

        if (teams.TeamIds.Count != 1)
        {
            _logger.LogError(
                "[ID-BIND] {Operation} for {Who} refused: business unit {BusinessUnitId} has {Count} default owner teams "
                + "(exactly one expected). Nothing was created.", operation, who, bu, teams.TeamIds.Count);
            return new OwnerTeamResolution(OwnerTeamStatus.Unconfigured);
        }

        return new OwnerTeamResolution(OwnerTeamStatus.Resolved, teams.TeamIds[0]);
    }

    /// <summary>
    /// K1 (UAC re-check): the log line of a contact create Dataverse refused for its OWNER (0x80040299: the owner team holds no
    /// role with contact Read). No id of a person: <paramref name="who"/> is a hash or a label. The business unit id is
    /// configuration, not personal data. <paramref name="denyCode"/> is the code the caller returns for the refusal, so the log
    /// line and the result carry the same one.
    /// </summary>
    private void LogOwnerPrivilegeMissing(string operation, string who, string denyCode)
        => _logger.LogError(
            "[ID-BIND] {Operation} for {Who} refused ({DenyCode}): Dataverse refused the owner of the new contact (0x80040299, "
            + "Read Privilege Check For Owner). The default owner team of the business unit named by "
            + "ExternalAccess:CustomerBusinessUnitId must hold a security role with Read on contact (and Create/Append as "
            + "needed). Nothing was created.",
            operation, who, denyCode);

    /// <summary>
    /// ISS-005 (#1673): the owner of a Workforce-plane contact create (first sign-in, licensed-user link), decided by the
    /// <see cref="ContactCreateOwner"/> this binder was built with: the customer business unit's default owner team (or a
    /// refusal), or, for <see cref="ContactCreateOwner.NoneForForeignEnvironment"/>,
    /// <see cref="OwnerTeamStatus.NotApplicable"/> with the reason logged at Debug once per create.
    /// </summary>
    private Task<OwnerTeamResolution> ResolveWorkforceCreateOwnerAsync(string operation, Guid oid, CancellationToken ct)
        => ResolveCreateOwnerAsync(operation, $"oid#{HashForLog(oid)}", ct);

    /// <summary>
    /// The ONE owner decision of every contact create outside registration, shared with the invite create
    /// (<c>ExternalInviteService</c>, which has no oid to hash): this binder's <see cref="ContactCreateOwner"/> policy decides,
    /// through <see cref="ResolveCustomerOwnerTeamAsync"/>. A caller treats everything but
    /// <see cref="OwnerTeamStatus.Resolved"/> as a refusal when its create is in this BFF's own environment.
    /// </summary>
    /// <param name="who">A log label that never identifies the person (a hash, not an email).</param>
    internal Task<OwnerTeamResolution> ResolveCreateOwnerAsync(string operation, string who, CancellationToken ct)
    {
        if (_createOwner.IsForeignEnvironment)
        {
            _logger.LogDebug("[ID-BIND] {Operation} for {Who} binds no owner: {Reason}", operation, who, _createOwner.ForeignReason);
            return Task.FromResult(new OwnerTeamResolution(OwnerTeamStatus.NotApplicable));
        }

        return ResolveCustomerOwnerTeamAsync(
            _createOwner.Options.CurrentValue.CustomerBusinessUnitId,
            SecureRecordOwnerTeam.BusinessUnitName(_createOwner.Configuration),
            operation,
            who,
            ct);
    }

    /// <summary>
    /// The create's details with the resolved owner team; unchanged when no owner applies. A positional copy, not a
    /// <c>with</c> expression: the task 146 b2 census flags every <c>with</c> expression that sets an owning team in
    /// server code (it cannot tell this team, resolved from the customer unit, from a forged resolver result). A
    /// property added to <see cref="NewContactDetails"/> must be copied here.
    /// </summary>
    private static NewContactDetails WithOwner(NewContactDetails details, OwnerTeamResolution owner)
        => owner.Status != OwnerTeamStatus.Resolved
            ? details
            : new NewContactDetails(details.FirstName, details.LastName, details.Email)
            {
                Department = details.Department,
                ExternalTenantId = details.ExternalTenantId,
                OwningTeamId = owner.TeamId,
            };

    /// <summary>
    /// Slice C2b: <see cref="CreateContactGuardedAsync"/>'s answer: the refusal's deny code (nothing was written), or the
    /// store's create result.
    /// </summary>
    /// <param name="MarkerUnproven">C5: the refusal is a removed marker whose empty read is not proven (retryable).</param>
    private readonly record struct GuardedCreate(string? DenyCode, StoreWriteResult Write, bool MarkerUnproven = false);

    /// <summary>
    /// Slice C2b: the ONE place this binder creates a contact (registration, the first sign-in create and the licensed-user
    /// link create all come through here; a source-scan test pins that no other code calls the store's create). No contact is
    /// created around a removal: before the create, <see cref="CreateAroundRemovalRefusalAsync"/> reads the inactive contacts
    /// for the new contact's email and refuses when one is removed (or unproven) or the read failed. Then the store's create.
    /// </summary>
    /// <param name="operation">The create, for the log ("Registration", "First sign-in create", ...).</param>
    /// <param name="who">A log label that never identifies the person (a hash, not an email).</param>
    private async Task<GuardedCreate> CreateContactGuardedAsync(
        Guid oid, IdentityPlaneMarker plane, NewContactDetails details, string operation, string who, CancellationToken ct)
    {
        if (await CreateAroundRemovalRefusalDecisionAsync(details.Email, operation, who, ct).ConfigureAwait(false) is
            { DenyCode: { } refused } removal)
        {
            return new GuardedCreate(refused, new StoreWriteResult(StoreWriteStatus.Failed, Error: refused), removal.MarkerUnproven);
        }

        var write = await _store.CreateContactForOidAsync(oid, plane, details, ct).ConfigureAwait(false);
        return new GuardedCreate(null, write);
    }

    /// <summary>
    /// Slice C2b: the deny code that refuses a contact create for <paramref name="email"/>, or <c>null</c> when it may go
    /// ahead (<see cref="ContactBindingDecision.DecideCreateAroundRemoval"/> over the inactive contacts for the email). Reads
    /// only, so the report-only link pass calls it too and reports the refusal the write pass would make.
    /// <para><b>An email-less create</b> has nothing to match on and is allowed here. That is safe because a create is keyed
    /// by the caller's oid, and the oid path already denies a removed carrier: removing a person keeps the contact's binding
    /// (oid, plane and key mirror), so a sign-in with that oid resolves to the removed contact and is denied before any
    /// create is considered.</para>
    /// </summary>
    private async Task<string?> CreateAroundRemovalRefusalAsync(
        string? email, string operation, string who, CancellationToken ct)
        => (await CreateAroundRemovalRefusalDecisionAsync(email, operation, who, ct).ConfigureAwait(false))?.DenyCode;

    /// <summary>
    /// <see cref="CreateAroundRemovalRefusalAsync"/>'s decision: the refusing <see cref="CreateAroundRemovalDecision"/> (logged
    /// here), or <c>null</c> when the create may go ahead. C5: registration answers a removed marker whose empty read is not
    /// proven as retryable, so it needs the decision, not only its code.
    /// </summary>
    private async Task<CreateAroundRemovalDecision?> CreateAroundRemovalRefusalDecisionAsync(
        string? email, string operation, string who, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var inactiveLookup = await _store.FindInactiveContactsByEmailAsync(email.Trim(), ct).ConfigureAwait(false);
        var removal = ContactBindingDecision.DecideCreateAroundRemoval(inactiveLookup);
        if (removal.MayCreate)
        {
            return null;
        }

        if (removal.MarkerUnproven)
        {
            _logger.LogError(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): the removed marker of inactive contact {ContactId} for the " +
                "email could not be proven empty, so a removal cannot be ruled out. No contact was created; retryable.",
                operation, who, removal.DenyCode, removal.RemovedContactId);
        }
        else if (removal.IsRemoval)
        {
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): an inactive contact {ContactId} for the email is one whose "
                + "external access an administrator removed. No contact was created.",
                operation, who, removal.DenyCode, removal.RemovedContactId);
        }
        else
        {
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): the inactive contacts for the email could not be read, so "
                + "a removal cannot be ruled out. No contact was created; retryable.",
                operation, who, removal.DenyCode);
        }

        return removal;
    }

    private async Task<HomeRegistrationResult> CreateHomeContactAsync(HomeRegistrationRequest request, string who, CancellationToken ct)
    {
        var owner = await ResolveCustomerOwnerTeamAsync(
            request.CustomerBusinessUnitId, request.SecureBusinessUnitName, "Registration", who, ct).ConfigureAwait(false);
        switch (owner.Status)
        {
            case OwnerTeamStatus.Unconfigured:
                return new HomeRegistrationResult(HomeRegistrationOutcome.OwnerUnconfigured);
            case OwnerTeamStatus.LookupFailed:
                return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactLookupFailed);
        }

        var details = new NewContactDetails(request.FirstName, request.LastName, request.Email)
        {
            Department = request.Department,
            ExternalTenantId = request.HomeTenantId,
            OwningTeamId = owner.TeamId,
        };
        var guarded = await CreateContactGuardedAsync(
                request.HomeObjectId, IdentityPlaneMarker.WorkforceHome, details, "Registration", who, ct)
            .ConfigureAwait(false);
        if (guarded.DenyCode is { } refused)
        {
            return HomeRegistrationResult.Denied(refused) with { RemovedMarkerUnproven = guarded.MarkerUnproven };
        }

        var create = guarded.Write;
        switch (create.Status)
        {
            case StoreWriteStatus.Written when create.ContactId is { } created:
                _logger.LogInformation("[ID-BIND] Registration created contact {ContactId} for {Who} (WorkforceHome)",
                    created, who);
                return new HomeRegistrationResult(HomeRegistrationOutcome.Created, created, Wrote: true);

            case StoreWriteStatus.Written:
            {
                // Created, but the response named no id: the (tid, oid) read finds it.
                var reread = await AlreadyRegisteredAsync(request.HomeTenantId, request.HomeObjectId, who, false, ct)
                    .ConfigureAwait(false);
                return reread.Outcome == HomeRegistrationOutcome.AlreadyRegistered
                    ? new HomeRegistrationResult(HomeRegistrationOutcome.Created, reread.ContactId, Wrote: true)
                    : HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactCreateFailed);
            }

            case StoreWriteStatus.PreconditionFailed or StoreWriteStatus.KeyConflict:
                // A concurrent submit won the race (the mirror key's 412): it is the contact for this (tid, oid).
                return await KeyConflictRegistrationAsync(request.HomeTenantId, request.HomeObjectId, who, ct)
                    .ConfigureAwait(false);

            case StoreWriteStatus.KeyMissing:
                _logger.LogError(
                    "[ID-BIND] Registration for {Who} refused ({DenyCode}): the alternate key on {Mirror} is not defined. "
                    + "Apply scripts/Set-ContactIdentityBindingSchema.ps1.",
                    who, ContactBindingDecision.DenyContactCreateUnavailable, ContactBindingDecision.KeyMirrorColumn);
                return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactCreateUnavailable);

            case StoreWriteStatus.OwnerPrivilegeMissing:
                // K1 (UAC re-check): Dataverse refused the owner (0x80040299): the customer unit's default team holds no
                // role with contact Read. The same refusal as an unresolved owner, so the operator is told the same thing.
                LogOwnerPrivilegeMissing("Registration", who, ContactBindingDecision.DenyOwnerUnconfigured);
                return new HomeRegistrationResult(HomeRegistrationOutcome.OwnerUnconfigured);

            case StoreWriteStatus.Forbidden:
                // A missing privilege (the application user cannot Create/Assign): permanent, an operator fix - not retryable.
                _logger.LogError(
                    "[ID-BIND] Registration for {Who} refused ({DenyCode}): Dataverse refused the create for a missing "
                    + "privilege (the customer business unit's default owner team needs a role with contact read, and the BFF "
                    + "application user needs Assign). Nothing was created.",
                    who, ContactBindingDecision.DenyContactCreateUnavailable);
                return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactCreateUnavailable);

            default:
                _logger.LogWarning("[ID-BIND] Registration create for {Who} failed ({Status})", who, create.Status);
                return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactCreateFailed);
        }
    }

    /// <summary>
    /// The unique index refused this oid: a concurrent registration holds it (re-read by (tid, oid) → the same contact),
    /// else another contact squats the mirror (flag the holder, deny).
    /// </summary>
    private async Task<HomeRegistrationResult> KeyConflictRegistrationAsync(Guid tid, Guid oid, string who, CancellationToken ct)
    {
        var reread = await AlreadyRegisteredAsync(tid, oid, who, wroteNothingElse: true, ct).ConfigureAwait(false);
        if (reread.Outcome == HomeRegistrationOutcome.AlreadyRegistered)
        {
            return reread;
        }

        var (_, _, wrote) = await FlagKeyMirrorHoldersAsync(oid, IdentityPlaneMarker.WorkforceHome, ct).ConfigureAwait(false);
        return HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactKeyConflict, wrote);
    }

    /// <summary>The 046 home contact read and decision: the caller's own WorkforceHome contact, or a conflict.</summary>
    private async Task<HomeRegistrationResult> AlreadyRegisteredAsync(
        Guid tid, Guid oid, string who, bool wroteNothingElse, CancellationToken ct)
    {
        var lookup = await _store.FindHomeContactsAsync(tid, oid, ct).ConfigureAwait(false);
        var decision = ContactBindingDecision.DecideHomeContact(lookup, tid, oid);
        if (decision is { Action: BindingAction.ResolveByOid, ContactId: { } contactId })
        {
            if (wroteNothingElse)
            {
                _logger.LogInformation(
                    "[ID-BIND] Registration for {Who} is already registered as contact {ContactId}: nothing written", who, contactId);
            }

            return new HomeRegistrationResult(HomeRegistrationOutcome.AlreadyRegistered, contactId);
        }

        // A read that failed is a fault (retryable); anything else (none found, ambiguous, inactive) is a conflict.
        return lookup.Status != LookupStatus.Read && decision?.DenyCode is { } fault
            ? HomeRegistrationResult.Denied(fault)
            : new HomeRegistrationResult(HomeRegistrationOutcome.Conflict, DenyCode: decision?.DenyCode);
    }

    // ── ADR-028 A9 stored link (r3 task 048) ──────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-028 A9 (r3 task 048, design §5): writes the stored home link (<paramref name="homeTenantId"/>,
    /// <paramref name="homeObjectId"/>) onto the ENABLED systemuser whose Spaarke-tenant object id is
    /// <paramref name="guestObjectId"/>. The route has already proven both identities (the home bearer and the guest token);
    /// this decides and performs the write only. Only <c>ExternalWorkforceLinkEndpoints</c> calls it.
    /// <list type="number">
    ///   <item>Probe that the A9 columns are readable: a masked secured pair would read as "no link" and let a write
    ///   re-point an existing one, so anything but readable (or "nothing marked yet") denies before any other read.</item>
    ///   <item>Read the guest's systemusers and the pair's current holders; decide
    ///   (<see cref="ContactBindingDecision.DecideHomeLinkWrite"/>): write once, never re-point.</item>
    ///   <item>Write the pair and its key mirrors in one request, conditional on the row version. A version race re-reads
    ///   and decides again (once); the mirrors' duplicate-key fault is a collision.</item>
    /// </list>
    /// Logs carry the systemuser id and HASHED tid/oid only.
    /// </summary>
    public async Task<HomeLinkWriteResult> LinkHomeSystemUserAsync(
        Guid guestObjectId, Guid homeTenantId, Guid homeObjectId, CancellationToken ct)
    {
        if (guestObjectId == Guid.Empty || homeTenantId == Guid.Empty || homeObjectId == Guid.Empty)
        {
            throw new ArgumentException("A home link needs the guest object id and the home tenant and object id.");
        }

        var who = $"tid#{HashForLog(homeTenantId)} oid#{HashForLog(homeObjectId)}";

        // NoMarkedRows: no systemuser or contact carries an A9 mirror yet, so no link exists that a mask could hide.
        var home = await _store.ProbeHomeReadabilityAsync(ct).ConfigureAwait(false);
        if (home is not (BindingReadability.Readable or BindingReadability.NoMarkedRows))
        {
            var probeCode = home switch
            {
                BindingReadability.ColumnMissing => ContactBindingDecision.DenyBindingColumnMissing,
                BindingReadability.Masked => ContactBindingDecision.DenyBindingColumnMasked,
                _ => ContactBindingDecision.DenyContactLookupFailed,
            };
            _logger.Log(
                home == BindingReadability.Failed ? LogLevel.Warning : LogLevel.Error,
                "[ID-BIND] Home link for {Who} DENIED ({DenyCode}): the A9 columns are {Home}. Nothing was written.",
                who, probeCode, home);
            return HomeLinkWriteResult.Denied(probeCode);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var guestUsers = await _store.FindSystemUsersByGuestObjectIdAsync(guestObjectId, ct).ConfigureAwait(false);
            var holders = await _store.FindHomeLinkedSystemUsersAsync(homeTenantId, homeObjectId, ct).ConfigureAwait(false);
            var decision = ContactBindingDecision.DecideHomeLinkWrite(guestUsers, holders, homeTenantId, homeObjectId);

            switch (decision.Action)
            {
                case HomeLinkWriteAction.AlreadyLinked:
                    _logger.LogInformation(
                        "[ID-BIND] Home link for {Who}: systemuser {SystemUserId} already carries it; nothing written",
                        who, decision.SystemUserId);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.AlreadyLinked, decision.SystemUserId);

                case HomeLinkWriteAction.NoSystemUser:
                    _logger.LogWarning("[ID-BIND] Home link for {Who} refused: the guest identity has no systemuser", who);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.NoSystemUser);

                case HomeLinkWriteAction.SystemUserDisabled:
                    _logger.LogWarning("[ID-BIND] Home link for {Who} refused: the guest identity's systemuser is disabled", who);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.SystemUserDisabled);

                case HomeLinkWriteAction.Collision:
                    _logger.LogWarning(
                        "[ID-BIND] Home link for {Who} refused (collision): systemuser {SystemUserId} carries another link, or "
                        + "another systemuser holds this one. Nothing was re-pointed.",
                        who, decision.SystemUserId);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.Collision, decision.SystemUserId);

                case HomeLinkWriteAction.Deny:
                    _logger.LogWarning("[ID-BIND] Home link for {Who} DENIED ({DenyCode})", who, decision.DenyCode);
                    return HomeLinkWriteResult.Denied(decision.DenyCode ?? ContactBindingDecision.DenyContactLookupFailed);
            }

            var systemUserId = decision.SystemUserId!.Value;
            var write = await _store.WriteHomeLinkAsync(systemUserId, decision.ETag, homeTenantId, homeObjectId, ct)
                .ConfigureAwait(false);
            switch (write.Status)
            {
                case StoreWriteStatus.Written:
                    _logger.LogInformation("[ID-BIND] Home link for {Who} written onto systemuser {SystemUserId}", who, systemUserId);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.Linked, systemUserId);

                case StoreWriteStatus.KeyConflict:
                    _logger.LogWarning(
                        "[ID-BIND] Home link for {Who} refused (collision): the pair's key mirror is held by another systemuser",
                        who);
                    return new HomeLinkWriteResult(HomeLinkWriteOutcome.Collision, systemUserId);

                case StoreWriteStatus.PreconditionFailed:
                    // The row changed since it was read: decide again on a fresh read (once).
                    continue;

                default:
                    var writeCode = write.Status is StoreWriteStatus.Forbidden or StoreWriteStatus.KeyMissing
                        ? ContactBindingDecision.DenyBindingColumnMasked
                        : ContactBindingDecision.DenyContactBindFailed;
                    _logger.LogWarning(
                        "[ID-BIND] Home link for {Who} onto systemuser {SystemUserId} DENIED ({DenyCode}): the write ended {Status}",
                        who, systemUserId, writeCode, write.Status);
                    return HomeLinkWriteResult.Denied(writeCode);
            }
        }

        _logger.LogWarning("[ID-BIND] Home link for {Who} DENIED ({DenyCode}): the systemuser kept changing; retryable",
            who, ContactBindingDecision.DenyContactBindFailed);
        return HomeLinkWriteResult.Denied(ContactBindingDecision.DenyContactBindFailed);
    }

    /// <summary>First 8 hex characters of SHA-256 (as <c>ServiceBusProvisioningEnqueuer.HashForLog</c>): correlates, never identifies.</summary>
    private static string HashForLog(Guid value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value.ToString("D"))))[..8];

    // ── Core ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<ContactBindingResult> RunUnlinkedAsync(
        BindingRequest request, NewContactDetails? details, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var oidLookup = request.CallerOid == Guid.Empty
                ? ContactLookup.Of()
                : await _store.FindContactsByOidAsync(request.CallerOid, ct).ConfigureAwait(false);
            var known = new Dictionary<Guid, ContactBindingRow>();
            Remember(known, oidLookup);

            var (decision, _, intent) = await DecideUnlinkedAsync(request, oidLookup, known, ct).ConfigureAwait(false);

            switch (decision.Action)
            {
                case BindingAction.ResolveByOid:
                    return ContactBindingResult.Resolved(decision.ContactId!.Value, BindingAction.ResolveByOid);

                case BindingAction.Collision:
                {
                    var wrote = await WriteFlagsAsync(decision.FlagContactIds ?? Array.Empty<Guid>(), known,
                        request.CallerOid, request.CallerPlane, decision.Reason!.Value, ct).ConfigureAwait(false);
                    return ContactBindingResult.Denied(decision.DenyCode!, BindingAction.Collision, decision.Reason, wrote);
                }

                case BindingAction.BindByEmail:
                {
                    if (await MaskedAsync(ct).ConfigureAwait(false))
                    {
                        return ContactBindingResult.Denied(ContactBindingDecision.DenyBindingColumnMasked);
                    }

                    var target = known[decision.ContactId!.Value];

                    // An email bind decided on an invite stamp is written conditional on the row version the stamp was read
                    // at, and only when the binding read saw that same version: a row that changed between the two reads (a
                    // re-invite, an emailaddress1 edit, a concurrent bind) is re-read and re-decided, never bound on stale state.
                    if (intent is not null && !SameRowVersion(intent.ETag, target.ETag))
                    {
                        _logger.LogInformation(
                            "[ID-BIND] Contact {ContactId} changed between its binding read and its invite-stamp read; re-deciding",
                            target.ContactId);
                        if (attempt == 0)
                        {
                            continue;
                        }

                        return ContactBindingResult.Denied(ContactBindingDecision.DenyContactBindFailed);
                    }

                    // Task 185 — E11's marker leg binds only with the invite code, and consumes it in the SAME conditional
                    // write as the oid (single-use). #2005: every other email bind (the workforce plane) consumes a pending
                    // hash the same way, so a bound contact never keeps a code an operator unbind could revive. The same write
                    // clears the invite stamp (email hash and tenant).
                    var write = await _store.BindOidAndConsumeInviteAsync(target.ContactId, intent?.ETag ?? target.ETag,
                        request.CallerOid, request.CallerPlane, ct).ConfigureAwait(false);
                    if (write.Status == StoreWriteStatus.Written)
                    {
                        _logger.LogInformation(
                            "[ID-BIND] Bound oid {Oid} ({Plane}) to contact {ContactId} by first-sign-in email match",
                            request.CallerOid, request.CallerPlane, target.ContactId);
                        return ContactBindingResult.Resolved(target.ContactId, BindingAction.BindByEmail, wrote: true);
                    }

                    if (write.Status == StoreWriteStatus.KeyConflict)
                    {
                        // Another contact holds this oid in its uniqueness mirror. Never bound around.
                        return await KeyConflictDeniedAsync(request, ct).ConfigureAwait(false);
                    }

                    if (write.Status == StoreWriteStatus.PreconditionFailed && attempt == 0)
                    {
                        continue; // the row changed under us — re-decide against fresh rows
                    }

                    // A failed bind is NOT "resolved anyway": a contact is resolved only once its binding is
                    // written, so the next sign-in retries the bind.
                    return ContactBindingResult.Denied(ContactBindingDecision.DenyContactBindFailed);
                }

                case BindingAction.CreateByOid:
                {
                    if (details is null)
                    {
                        // Unreachable by construction (only creating planes get here); fail closed if it ever is.
                        return ContactBindingResult.Denied(ContactBindingDecision.DenyContactNotFound);
                    }

                    if (await MaskedAsync(ct).ConfigureAwait(false))
                    {
                        return ContactBindingResult.Denied(ContactBindingDecision.DenyBindingColumnMasked);
                    }

                    // ISS-005 (#1673): the contact is owned by the customer business unit's default team, or it is not created.
                    var owner = await ResolveWorkforceCreateOwnerAsync("First sign-in create", request.CallerOid, ct)
                        .ConfigureAwait(false);
                    switch (owner.Status)
                    {
                        case OwnerTeamStatus.Unconfigured:
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyOwnerUnconfigured);
                        case OwnerTeamStatus.LookupFailed:
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyContactLookupFailed);
                    }

                    var guarded = await CreateContactGuardedAsync(request.CallerOid, request.CallerPlane,
                            WithOwner(details, owner), "First sign-in create", $"oid#{HashForLog(request.CallerOid)}", ct)
                        .ConfigureAwait(false);
                    if (guarded.DenyCode is { } refused)
                    {
                        return ContactBindingResult.Denied(refused);
                    }

                    var create = guarded.Write;
                    switch (create.Status)
                    {
                        case StoreWriteStatus.Written when create.ContactId is { } created:
                            _logger.LogInformation(
                                "[ID-BIND] Created contact {ContactId} keyed by oid {Oid} ({Plane})",
                                created, request.CallerOid, request.CallerPlane);
                            return ContactBindingResult.Resolved(created, BindingAction.CreateByOid, wrote: true);
                        case StoreWriteStatus.Written:
                        case StoreWriteStatus.PreconditionFailed or StoreWriteStatus.KeyConflict when attempt == 0:
                            // Created without an id in the response, or a concurrent first sign-in won the race
                            // (the mirror key's 412 duplicate fault). Either way the oid lookup now finds exactly one contact.
                            continue;
                        case StoreWriteStatus.PreconditionFailed or StoreWriteStatus.KeyConflict:
                            // Refused by the key AGAIN although the binding read found no contact for this oid: no
                            // racer explains it. Another contact holds the oid in its uniqueness mirror.
                            return await KeyConflictDeniedAsync(request, ct).ConfigureAwait(false);
                        case StoreWriteStatus.KeyMissing:
                            _logger.LogError(
                                "[ID-BIND] {DenyCode}: the alternate key on the uniqueness mirror ({Mirror}) is not "
                                + "defined, so exactly-one-contact-per-oid cannot be guaranteed. Apply "
                                + "scripts/Set-ContactIdentityBindingSchema.ps1.",
                                ContactBindingDecision.DenyContactCreateUnavailable, ContactBindingDecision.KeyMirrorColumn);
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyContactCreateUnavailable);
                        case StoreWriteStatus.OwnerPrivilegeMissing:
                            // K1 (UAC re-check): the owner team may not own a contact (no role with contact Read).
                            LogOwnerPrivilegeMissing(
                                "First sign-in create", $"oid#{HashForLog(request.CallerOid)}", ContactBindingDecision.DenyOwnerUnconfigured);
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyOwnerUnconfigured);
                        default:
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyContactCreateFailed);
                    }
                }

                default:
                    // Task 185 (pass 1 F1): the contact row and its version were read BEFORE the marker and the hash. A
                    // concurrent sign-in carrying the same code can bind in between (the SPA's first load sends
                    // /me/entitlements and the bell poll together); this call then sees the hash already cleared and never
                    // reaches the 412 retry. Re-decide ONCE against fresh rows: the oid lookup finds the now-bound contact
                    // (D4), or the email row is bound to another oid (E7). A wrong code fails the match again, so the
                    // re-run never turns it into a bind.
                    if (attempt == 0
                        && decision.DenyCode == ContactBindingDecision.DenyContactNotInvited
                        && request.InviteCode is not null)
                    {
                        continue;
                    }

                    return ContactBindingResult.Denied(decision.DenyCode ?? ContactBindingDecision.DenyContactNotFound);
            }
        }

        return ContactBindingResult.Denied(ContactBindingDecision.DenyContactBindFailed);
    }

    /// <returns>The decision, the email lookup it read (if any) and the invite stamp it read (if any, with its decision time).</returns>
    private async Task<(BindingDecision Decision, ContactLookup? EmailLookup, BindIntentLookup? Intent)> DecideUnlinkedAsync(
        BindingRequest request, ContactLookup oidLookup, Dictionary<Guid, ContactBindingRow> known, CancellationToken ct)
    {
        var decision = ContactBindingDecision.Decide(request, oidLookup);
        ContactLookup? emailLookup = null;
        BindIntentLookup? stamped = null;

        if (decision.Action == BindingAction.NeedEmailLookup)
        {
            emailLookup = await _store.FindActiveContactsByEmailAsync(request.Email!, ct).ConfigureAwait(false);
            Remember(known, emailLookup);
            decision = ContactBindingDecision.Decide(request, oidLookup, emailLookup);
        }

        ReferenceLookup? refs = null;
        if (decision.Action == BindingAction.NeedReferenceLookup && decision.ContactId is { } candidate)
        {
            refs = await _store.FindSystemUsersLinkingAsync(new[] { candidate }, ct).ConfigureAwait(false);
            decision = ContactBindingDecision.Decide(request, oidLookup, emailLookup, refs);
        }

        // Tasks 184 and 185 — E11 binds only on this environment's own invite: on the CIAM plane a pending marker and its
        // code, and on both token planes the invite stamp (no grant is read). A read that throws is a fault, never "no
        // intent" and never intent: the decision denies.
        if (decision.Action == BindingAction.NeedIntentLookup && decision.ContactId is { } invitee)
        {
            var asOf = await ReadInviteStampAsync(invitee, ct).ConfigureAwait(false);
            stamped = asOf;
            decision = ContactBindingDecision.Decide(request, oidLookup, emailLookup, refs, asOf);

            // Design note §5.1: an operator-only, non-secret reason for a not-invited deny (never the code or the hash).
            if (decision.DenyCode == ContactBindingDecision.DenyContactNotInvited
                && ContactBindingDecision.NotInvitedReason(request, asOf, invitee) is { } reason)
            {
                _logger.LogInformation(
                    "[ID-BIND] Contact {ContactId} is not invited for this sign-in ({Plane}): {NotInvitedReason}.",
                    invitee, request.Plane, reason);
            }
        }

        LogRemovedDeny(decision, request.Plane);

        // C5: an email bind onto an existing contact is refused while an inactive contact for the same email is one whose
        // external access an administrator removed (or whether it is cannot be established), as a create is.
        if (decision.Action == BindingAction.BindByEmail
            && await EmailBindAroundRemovalAsync(request.Email, $"{request.Plane} sign-in", $"oid#{HashForLog(request.CallerOid)}",
                    decision.ContactId, ct).ConfigureAwait(false) is { } removal)
        {
            decision = new BindingDecision(BindingAction.Deny, DenyCode: removal.DenyCode)
            {
                RemovedMarkerUnproven = removal.MarkerUnproven,
            };
        }

        return (decision, emailLookup, stamped);
    }

    /// <summary>
    /// C5: the refusing <see cref="CreateAroundRemovalDecision"/> (logged here) when an email bind onto existing contact
    /// <paramref name="contactId"/> may not go ahead because an INACTIVE contact for <paramref name="email"/> is one whose
    /// external access an administrator removed, whether it is cannot be established, or the inactive contacts could not be
    /// read; <c>null</c> when the bind may go ahead. Runs <see cref="ContactBindingDecision.DecideCreateAroundRemoval"/>, the
    /// one rule a create already runs. Read-only; nothing is flagged.
    /// <para>§11. Existing: <see cref="CreateAroundRemovalRefusalDecisionAsync"/> runs the same rule before a create. Extension:
    /// this reuses that rule and its decision; it is separate only so its log names a bind, not a create. Cost of doing
    /// nothing: an unbound active contact that shares a removed person's email is bound at sign-in or registration, so the
    /// removal no longer holds for that email.</para>
    /// </summary>
    private async Task<CreateAroundRemovalDecision?> EmailBindAroundRemovalAsync(
        string? email, string operation, string who, Guid? contactId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var inactiveLookup = await _store.FindInactiveContactsByEmailAsync(email.Trim(), ct).ConfigureAwait(false);
        var removal = ContactBindingDecision.DecideCreateAroundRemoval(inactiveLookup);
        if (removal.MayCreate)
        {
            return null;
        }

        if (removal.MarkerUnproven)
        {
            _logger.LogError(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): the removed marker of inactive contact {RemovedContactId} " +
                "for the email could not be proven empty, so a removal cannot be ruled out. Contact {ContactId} was not bound; " +
                "retryable.",
                operation, who, removal.DenyCode, removal.RemovedContactId, contactId);
        }
        else if (removal.IsRemoval)
        {
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): inactive contact {RemovedContactId} for the email is one " +
                "whose external access an administrator removed. Contact {ContactId} was not bound.",
                operation, who, removal.DenyCode, removal.RemovedContactId, contactId);
        }
        else
        {
            _logger.LogWarning(
                "[ID-BIND] {Operation} for {Who} REFUSED ({DenyCode}): the inactive contacts for the email could not be read, so " +
                "a removal cannot be ruled out. Contact {ContactId} was not bound; retryable.",
                operation, who, removal.DenyCode, contactId);
        }

        return removal;
    }

    /// <summary>
    /// Slice C2c: a removed-person deny is logged with text that tells a SET marker (an administrator's removal, Warning) from
    /// an UNPROVEN one (the marker's empty read is not proven, so the person is refused fail closed: Error, an operator
    /// problem). The decision's code is <see cref="ContactBindingDecision.DenyContactRemoved"/> either way, and the person is
    /// answered as not invited (<see cref="ContactBindingDecision.DenyContactNotInvited"/>).
    /// </summary>
    private void LogRemovedDeny(BindingDecision decision, BindingPlane plane)
    {
        if (decision.DenyCode != ContactBindingDecision.DenyContactRemoved)
        {
            return;
        }

        if (decision.RemovedMarkerUnproven)
        {
            _logger.LogError(
                "[ID-BIND] {Plane} sign-in refused ({DenyCode}): the removed marker of the matched contact could not be proven " +
                "empty, so the person is refused until it can be read. Check the BFF's read of the removed-marker column.",
                plane, decision.DenyCode);
        }
        else
        {
            _logger.LogWarning(
                "[ID-BIND] {Plane} sign-in refused ({DenyCode}): an administrator removed the matched contact's external access.",
                plane, decision.DenyCode);
        }
    }

    /// <summary>
    /// The invite marker, code hash and stamp of <paramref name="contactId"/> (one read), with the decision time set from this
    /// binder's clock (the marker's age is measured then; owner round 97: 30 days). A read that throws is a FAILED read
    /// (the decision denies), never "not invited" and never "invited".
    /// </summary>
    private async Task<BindIntentLookup> ReadInviteStampAsync(Guid contactId, CancellationToken ct)
    {
        BindIntentLookup intent;
        try
        {
            intent = await _store.FindBindIntentAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[ID-BIND] The bind-intent read for contact {ContactId} threw — denied", contactId);
            intent = BindIntentLookup.Failed(contactId);
        }

        return intent with { AsOf = _time.GetUtcNow() };
    }

    /// <summary>True when both row versions are present and equal (ordinal). Pure.</summary>
    private static bool SameRowVersion(string? a, string? b)
        => DataverseContactIdentityStore.RowVersionPrecondition(a) is { } left
           && DataverseContactIdentityStore.RowVersionPrecondition(b) is { } right
           && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Plans — and with <paramref name="applyWrites"/>, performs — a systemuser decision. Returns null when a
    /// conditional write lost a race and the caller should re-decide (never on <paramref name="lastAttempt"/> for a
    /// create the key refused: that is a key-mirror conflict, not a race).
    /// </summary>
    private async Task<SystemUserLinkResult?> ApplySystemUserDecisionAsync(
        SystemUserIdentityRow user,
        BindingRequest request,
        BindingDecision decision,
        Dictionary<Guid, ContactBindingRow> known,
        bool applyWrites,
        bool lastAttempt,
        CancellationToken ct)
    {
        var suid = user.SystemUserId;
        var none = Array.Empty<Guid>();
        var changes = new List<IdentityChange>();

        switch (decision.Action)
        {
            case BindingAction.LinkVerified:
                return Result(suid, SystemUserLinkOutcome.Verified, decision.ContactId, null, null, none, changes, false);

            case BindingAction.Deny:
                return Result(suid, SystemUserLinkOutcome.Denied, null, decision.DenyCode, null, none, changes, false);

            case BindingAction.Collision:
            {
                // Every row to flag must be KNOWN — its current flag decides idempotence. A row the decision
                // named without reading (the linked contact in L5) is read now; one that cannot be read is
                // skipped this run rather than written blind. Idempotence is per PARTY: this user's collision is
                // recorded once, even on a contact another identity already flagged (verifier finding 3).
                await EnsureKnownAsync(decision.FlagContactIds ?? none, known, ct).ConfigureAwait(false);
                var party = Party(request.CallerOid, request.CallerPlane, decision.Reason!.Value);
                var toFlag = (decision.FlagContactIds ?? none)
                    .Where(id => known.TryGetValue(id, out var r) && ShouldRecord(r, party))
                    .ToList();
                foreach (var id in toFlag)
                {
                    var before = known[id].Flag is { } open ? $"flag parties={open.Parties.Count}" : "flag=(none)";
                    changes.Add(new IdentityChange("contact", id, before,
                        $"flag +party reason={decision.Reason} oid={request.CallerOid:D} plane={request.CallerPlane}"));
                }

                if (toFlag.Count == 0)
                {
                    // A row that could not be read, or whose collision columns' read is not proven, is retried next run.
                    var unreadable = (decision.FlagContactIds ?? none)
                        .Any(id => !known.TryGetValue(id, out var r) || r.CollisionColumnsUnproven);
                    return Result(suid,
                        unreadable ? SystemUserLinkOutcome.Failed : SystemUserLinkOutcome.FlagAlreadyPresent,
                        null, decision.DenyCode, decision.Reason, decision.FlagContactIds ?? none, changes, false);
                }

                if (applyWrites)
                {
                    await WriteFlagsAsync(toFlag, known, request.CallerOid, request.CallerPlane, decision.Reason!.Value, ct)
                        .ConfigureAwait(false);
                }

                return Result(suid, SystemUserLinkOutcome.Flagged, null, decision.DenyCode, decision.Reason,
                    decision.FlagContactIds ?? none, changes, applyWrites);
            }

            case BindingAction.BindLinkedContact:
            {
                var target = decision.ContactId!.Value;
                changes.Add(WorkforceBindChange(target, request.CallerOid));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.BoundLinkedContact, target, null, null, none, changes, false);
                }

                if (await MaskedAsync(ct).ConfigureAwait(false))
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                // #2005: the bind consumes a pending invite-code hash in the same conditional write (see the store).
                var etag = known.TryGetValue(target, out var row) ? row.ETag : null;
                var write = await _store.BindOidAndConsumeInviteAsync(target, etag, request.CallerOid,
                    IdentityPlaneMarker.Workforce, ct).ConfigureAwait(false);
                if (write.Status == StoreWriteStatus.KeyConflict)
                {
                    return await KeyConflictLinkResultAsync(user, request, changes, ct).ConfigureAwait(false);
                }

                return write.Status switch
                {
                    StoreWriteStatus.Written => Result(suid, SystemUserLinkOutcome.BoundLinkedContact, target, null, null,
                        none, changes, true),
                    StoreWriteStatus.PreconditionFailed => null,
                    _ => Result(suid, SystemUserLinkOutcome.Failed, null, ContactBindingDecision.DenyContactBindFailed,
                        null, none, changes, true),
                };
            }

            case BindingAction.ResolveByOid:
            {
                // The user's oid is already bound to a contact; only the link is missing.
                var target = decision.ContactId!.Value;
                changes.Add(LinkChange(suid, target));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.Linked, target, null, null, none, changes, false);
                }

                return await LinkAsync(user, target, SystemUserLinkOutcome.Linked, changes, ct).ConfigureAwait(false);
            }

            case BindingAction.BindByEmail:
            {
                var target = decision.ContactId!.Value;
                changes.Add(WorkforceBindChange(target, request.CallerOid));
                changes.Add(LinkChange(suid, target));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.BoundAndLinked, target, null, null, none, changes, false);
                }

                if (await MaskedAsync(ct).ConfigureAwait(false))
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                // #2005: the bind consumes a pending invite-code hash in the same conditional write (see the store).
                var write = await _store.BindOidAndConsumeInviteAsync(target, known[target].ETag, request.CallerOid,
                    IdentityPlaneMarker.Workforce, ct).ConfigureAwait(false);
                if (write.Status == StoreWriteStatus.PreconditionFailed) return null;
                if (write.Status == StoreWriteStatus.KeyConflict)
                {
                    return await KeyConflictLinkResultAsync(user, request, changes, ct).ConfigureAwait(false);
                }

                if (write.Status != StoreWriteStatus.Written)
                {
                    return Result(suid, SystemUserLinkOutcome.Failed, null, ContactBindingDecision.DenyContactBindFailed,
                        null, none, changes, true);
                }

                return await LinkAsync(user, target, SystemUserLinkOutcome.BoundAndLinked, changes, ct).ConfigureAwait(false);
            }

            case BindingAction.CreateByOid:
            {
                var details = new NewContactDetails(
                    user.FirstName,
                    LastNameFor(user.LastName, null, user.InternalEmail),
                    user.InternalEmail);

                // A write pass refuses a masked binding column before anything else is read. A report-only pass never probes it.
                if (applyWrites && await MaskedAsync(ct).ConfigureAwait(false))
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                // ISS-005 (#1673): the contact is owned by the customer business unit's default team, or it is not created.
                // K1: the report-only pass decides the owner too (reads only), so it reports the refusal the write pass would
                // make instead of promising a create (CreatedAndLinked) that the write pass would then refuse.
                var owner = await ResolveWorkforceCreateOwnerAsync(
                        applyWrites ? "Licensed-user link create" : "Licensed-user link create (report-only)", request.CallerOid, ct)
                    .ConfigureAwait(false);
                switch (owner.Status)
                {
                    case OwnerTeamStatus.Unconfigured:
                        return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyOwnerUnconfigured,
                            null, none, Array.Empty<IdentityChange>(), false);
                    case OwnerTeamStatus.LookupFailed:
                        return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyContactLookupFailed,
                            null, none, Array.Empty<IdentityChange>(), false);
                }

                // Slice C2b: no contact is created around a removal. The report-only pass runs the same check (reads only),
                // so it reports the refusal instead of promising a create (CreatedAndLinked) the write pass would refuse.
                var linkCreateWho = $"oid#{HashForLog(request.CallerOid)}";
                if (!applyWrites
                    && await CreateAroundRemovalRefusalAsync(details.Email, "Licensed-user link create (report-only)",
                        linkCreateWho, ct).ConfigureAwait(false) is { } wouldRefuse)
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, wouldRefuse, null, none,
                        Array.Empty<IdentityChange>(), false);
                }

                // #2130: the change log names WHICH columns are written, never a person's values. The email and the last
                // name (which falls back to the email's local part, LastNameFor) are "(set)" / "(none)"; so is the owner
                // (ownerid is "(set)" when a team owns the contact, "(none)" when this binder binds no owner), never the team id.
                changes.Add(new IdentityChange("contact", null, "(no contact)",
                    $"create contact lastname={SetOrNone(details.LastName)} emailaddress1={SetOrNone(details.Email)} "
                    + $"ownerid={(owner.Status == OwnerTeamStatus.Resolved ? "(set)" : "(none)")} "
                    + $"sprk_externalobjectid={request.CallerOid:D} sprk_identityplane={IdentityPlaneMarker.Workforce}"));
                changes.Add(LinkChange(suid, null));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.CreatedAndLinked, null, null, null, none, changes, false);
                }

                var guarded = await CreateContactGuardedAsync(request.CallerOid, IdentityPlaneMarker.Workforce,
                        WithOwner(details, owner), "Licensed-user link create", linkCreateWho, ct)
                    .ConfigureAwait(false);
                if (guarded.DenyCode is { } refused)
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, refused, null, none,
                        Array.Empty<IdentityChange>(), false);
                }

                var create = guarded.Write;
                if (create.Status == StoreWriteStatus.KeyMissing)
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyContactCreateUnavailable,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                if (create.Status is StoreWriteStatus.PreconditionFailed or StoreWriteStatus.KeyConflict)
                {
                    // First refusal: a concurrent writer may have created it — re-decide; the oid lookup finds it.
                    // Refused AGAIN after a fresh read found no contact bound to the oid: a key-mirror conflict.
                    return lastAttempt
                        ? await KeyConflictLinkResultAsync(user, request, changes, ct).ConfigureAwait(false)
                        : null;
                }

                if (create.Status == StoreWriteStatus.Written && create.ContactId is null)
                {
                    return null; // created but the id was not returned: re-decide; the oid lookup now finds it
                }

                if (create.Status == StoreWriteStatus.OwnerPrivilegeMissing)
                {
                    // K1 (UAC re-check): the owner team may not own a contact. A configuration fault that repeats every run,
                    // nothing written. Round 3 F: its own code, so the reconciliation job counts and words it apart from an
                    // unset CustomerBusinessUnitId (the fix is a security role on the team, not the setting).
                    LogOwnerPrivilegeMissing(
                        "Licensed-user link create", $"oid#{HashForLog(request.CallerOid)}", ContactBindingDecision.DenyOwnerPrivilegeMissing);
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyOwnerPrivilegeMissing,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                if (create.Status != StoreWriteStatus.Written)
                {
                    return Result(suid, SystemUserLinkOutcome.Failed, null, ContactBindingDecision.DenyContactCreateFailed,
                        null, none, changes, true);
                }

                return await LinkAsync(user, create.ContactId!.Value, SystemUserLinkOutcome.CreatedAndLinked, changes, ct)
                    .ConfigureAwait(false);
            }

            default:
                return Result(suid, SystemUserLinkOutcome.Denied, null,
                    decision.DenyCode ?? ContactBindingDecision.DenyContactLookupFailed, null, none, changes, false);
        }
    }

    private async Task<SystemUserLinkResult> LinkAsync(
        SystemUserIdentityRow user, Guid contactId, SystemUserLinkOutcome outcome, List<IdentityChange> changes,
        CancellationToken ct)
    {
        if (await MaskedAsync(ct).ConfigureAwait(false))
        {
            return Result(user.SystemUserId, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                null, Array.Empty<Guid>(), changes, true);
        }

        // Conditional on the row version read with the user: if anyone set the link meanwhile, it is NOT
        // overwritten — never re-point an existing link. A row without a version (a systemuser registration just
        // created) is re-read first, and linked only if it still has no link.
        var etag = user.ETag;
        if (string.IsNullOrWhiteSpace(etag))
        {
            var fresh = await _store.GetSystemUserAsync(user.SystemUserId, ct).ConfigureAwait(false);
            if (fresh.Status != LookupStatus.Read || fresh.Row is null)
            {
                return Result(user.SystemUserId, SystemUserLinkOutcome.Failed, contactId,
                    ContactBindingDecision.DenyContactBindFailed, null, Array.Empty<Guid>(), changes, true);
            }

            if (fresh.Row.PrimaryContactId is { } existing)
            {
                // Linked meanwhile. Never re-pointed; the same contact is simply verified.
                return existing == contactId
                    ? Result(user.SystemUserId, outcome, contactId, null, null, Array.Empty<Guid>(), changes, true)
                    : Result(user.SystemUserId, SystemUserLinkOutcome.Failed, contactId,
                        ContactBindingDecision.DenyContactBindFailed, null, Array.Empty<Guid>(), changes, true);
            }

            etag = fresh.Row.ETag;
        }

        var write = await _store.SetPrimaryContactAsync(user.SystemUserId, etag, contactId, ct).ConfigureAwait(false);
        if (write.Status != StoreWriteStatus.Written)
        {
            _logger.LogWarning(
                "[ID-BIND] Link of systemuser {SystemUserId} to contact {ContactId} not written ({Status}). An existing "
                + "link is never overwritten. Only this BFF's own environment is re-decided by its reconciliation job; "
                + "a registration link in another environment is logged by the caller as not retried.",
                user.SystemUserId, contactId, write.Status);
            return Result(user.SystemUserId, SystemUserLinkOutcome.Failed, contactId,
                ContactBindingDecision.DenyContactBindFailed, null, Array.Empty<Guid>(), changes, true);
        }

        _logger.LogInformation("[ID-BIND] Linked systemuser {SystemUserId} to contact {ContactId} ({Outcome})",
            user.SystemUserId, contactId, outcome);
        return Result(user.SystemUserId, outcome, contactId, null, null, Array.Empty<Guid>(), changes, true);
    }

    private async Task<bool> WriteFlagsAsync(
        IReadOnlyList<Guid> contactIds,
        Dictionary<Guid, ContactBindingRow> known,
        Guid? collidingOid,
        IdentityPlaneMarker collidingPlane,
        IdentityCollisionReason reason,
        CancellationToken ct)
    {
        await EnsureKnownAsync(contactIds, known, ct).ConfigureAwait(false);

        var party = Party(collidingOid, collidingPlane, reason);
        var wrote = false;
        foreach (var contactId in contactIds)
        {
            if (!known.TryGetValue(contactId, out var row))
            {
                // Its current flag is unknown, so writing could repeat one. The deny stands; the next attempt retries.
                _logger.LogError(
                    "[ID-BIND] Collision on contact {ContactId} ({Reason}) not flagged: the row could not be read",
                    contactId, reason);
                continue;
            }

            wrote |= await RecordPartyAsync(row, party, ct).ConfigureAwait(false);
        }

        return wrote;
    }

    /// <summary>
    /// Records <paramref name="party"/> on <paramref name="row"/>'s flag: a new flag, or the existing one plus this
    /// party. Idempotent per party (<see cref="ContactBindingDecision.ShouldWriteFlag"/>). The write is conditional
    /// on the row version, so a party another writer appended meanwhile is never overwritten: on 412 the row is
    /// re-read once and the party appended to what is there now.
    /// </summary>
    private async Task<bool> RecordPartyAsync(ContactBindingRow row, CollisionParty party, CancellationToken ct)
    {
        var current = row;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!ShouldRecord(current, party))
            {
                return false; // already recorded (or a flag whose parties cannot be read, which is never overwritten)
            }

            var flag = ContactBindingDecision.FlagWith(current.Flag, party);
            var write = await _store.WriteCollisionFlagAsync(current.ContactId, flag, current.ETag, ct).ConfigureAwait(false);
            if (write.Status == StoreWriteStatus.Written)
            {
                _logger.LogWarning(
                    "[ID-BIND] Collision FLAGGED on contact {ContactId}: reason={Reason} oid={Oid} plane={Plane} parties={Parties}",
                    current.ContactId, party.Reason, party.Oid, party.Plane, flag.Parties.Count);
                return true;
            }

            if (write.Status == StoreWriteStatus.PreconditionFailed && attempt == 0)
            {
                var fresh = await _store.GetContactAsync(current.ContactId, ct).ConfigureAwait(false);
                var freshRow = fresh.Status == LookupStatus.Read
                    ? fresh.Rows.FirstOrDefault(r => r.ContactId == current.ContactId)
                    : null;
                if (freshRow is not null)
                {
                    current = freshRow;
                    continue;
                }
            }

            if (party.Reason == IdentityCollisionReason.InactiveContactEmailMatch
                && write.Status == StoreWriteStatus.Failed
                && DataverseContactIdentityStore.IsOptionValueOutOfRange(write.Error))
            {
                // Fix rounds 2 and 3: the choice option is added by the schema script. Until it is applied the platform
                // refuses the reason value ("outside the valid range"); the registration is still refused (the deny stands)
                // and this line is the operator's marker. Any other failure (a throttle, a 5xx, a 412) is not a schema fault,
                // so it gets no schema hint - the error log below carries its status and text.
                _logger.LogWarning(
                    "[ID-BIND] The InactiveContactEmailMatch flag on contact {ContactId} was not written ({Status}); the "
                    + "'Inactive contact email match' option may not be provisioned in this environment. Apply "
                    + "scripts/Set-ContactIdentityBindingSchema.ps1. The registration is still refused.",
                    current.ContactId, write.Status);
            }

            // The deny stands regardless; only the operator-visible marker is missing, and the log carries it.
            _logger.LogError(
                "[ID-BIND] Collision on contact {ContactId} ({Reason}) could NOT be flagged ({Status}): {Error}",
                current.ContactId, party.Reason, write.Status, write.Error);
            return false;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="party"/> is written onto <paramref name="row"/>'s flag
    /// (<see cref="ContactBindingDecision.ShouldWriteFlag"/>). A row whose collision columns were read without proof is
    /// never written: its flag may be hidden, and a write sets every collision column. The deny stands; this log line is
    /// the operator's marker (the contact and the reason only).
    /// </summary>
    private bool ShouldRecord(ContactBindingRow row, CollisionParty party)
    {
        if (row.CollisionColumnsUnproven)
        {
            _logger.LogError(
                "[ID-BIND] Collision on contact {ContactId} ({Reason}) NOT flagged: the BFF's read of the collision columns "
                + "is not proven, so a flag on the contact may be hidden and is never overwritten. The deny stands.",
                row.ContactId, party.Reason);
            return false;
        }

        return ContactBindingDecision.ShouldWriteFlag(row.Flag, party);
    }

    private CollisionParty Party(Guid? collidingOid, IdentityPlaneMarker plane, IdentityCollisionReason reason)
        => new(collidingOid == Guid.Empty ? null : collidingOid, plane, reason, _time.GetUtcNow());

    /// <summary>A token-plane bind or create the unique index refused: flag the holder(s), deny.</summary>
    private async Task<ContactBindingResult> KeyConflictDeniedAsync(BindingRequest request, CancellationToken ct)
    {
        var (_, _, wrote) = await FlagKeyMirrorHoldersAsync(request.CallerOid, request.CallerPlane, ct).ConfigureAwait(false);
        return ContactBindingResult.Denied(ContactBindingDecision.DenyContactKeyConflict, BindingAction.Collision,
            IdentityCollisionReason.KeyMirrorConflict, wrote);
    }

    /// <summary>A systemuser link the unique index refused: flag the holder(s), report the user as flagged.</summary>
    private async Task<SystemUserLinkResult> KeyConflictLinkResultAsync(
        SystemUserIdentityRow user, BindingRequest request, List<IdentityChange> changes, CancellationToken ct)
    {
        var (holders, pending, wrote) = await FlagKeyMirrorHoldersAsync(request.CallerOid, request.CallerPlane, ct)
            .ConfigureAwait(false);
        var outcome = holders.Count == 0
            ? SystemUserLinkOutcome.Failed          // the holder could not be named: nothing to flag; retried next run
            : pending == 0
                ? SystemUserLinkOutcome.FlagAlreadyPresent
                : wrote ? SystemUserLinkOutcome.Flagged : SystemUserLinkOutcome.Failed;
        return Result(user.SystemUserId, outcome, null, ContactBindingDecision.DenyContactKeyConflict,
            IdentityCollisionReason.KeyMirrorConflict, holders, changes, true);
    }

    /// <summary>
    /// A bind or create the platform's unique index refused although no contact is BOUND to the oid: another contact
    /// holds the oid in its uniqueness mirror (<c>sprk_externalobjectidkey</c>) without the binding to match —
    /// squatted (the mirror is unsecured, so any user with contact Write can set it) or half-cleared by an operator.
    /// It is never bound around. The holder is flagged (<see cref="IdentityCollisionReason.KeyMirrorConflict"/>) so an
    /// operator can see it, and the caller is denied <see cref="ContactBindingDecision.DenyContactKeyConflict"/>.
    /// That is the most a user with contact Write can do through the mirror — deny one identity service, visibly —
    /// because nothing resolves or binds by it (owner round 4 item 4, B2).
    /// </summary>
    /// <returns>The holders, how many of them still needed this party recorded, and whether any flag was written.</returns>
    private async Task<(IReadOnlyList<Guid> Holders, int Pending, bool Wrote)> FlagKeyMirrorHoldersAsync(
        Guid oid, IdentityPlaneMarker plane, CancellationToken ct)
    {
        var lookup = await _store.FindContactsByKeyMirrorAsync(oid, ct).ConfigureAwait(false);
        var holders = lookup.Status == LookupStatus.Read
            ? lookup.Rows.Where(r => ContactBindingDecision.MirrorHeldWithoutBinding(r, oid)).ToList()
            : new List<ContactBindingRow>();

        _logger.LogError(
            "[ID-BIND] {DenyCode}: the unique index refused oid {Oid} ({Plane}) although no contact is bound to it. "
            + "The uniqueness mirror {Mirror} carries it on contact(s) [{Holders}] (lookup {LookupStatus}) — squatted or "
            + "half-cleared. Nothing was bound or created; the holder is flagged for an operator (deployment guide §6.5.3).",
            ContactBindingDecision.DenyContactKeyConflict, oid, plane, ContactBindingDecision.KeyMirrorColumn,
            string.Join(",", holders.Select(h => h.ContactId)), lookup.Status);

        var party = Party(oid, plane, IdentityCollisionReason.KeyMirrorConflict);
        var pending = holders.Where(h => ShouldRecord(h, party)).ToList();
        var wrote = false;
        foreach (var holder in pending)
        {
            wrote |= await RecordPartyAsync(holder, party, ct).ConfigureAwait(false);
        }

        return (holders.Select(h => h.ContactId).ToList(), pending.Count, wrote);
    }

    private async Task<bool> MaskedAsync(CancellationToken ct)
    {
        var readability = await _store.ProbeBindingReadabilityAsync(ct).ConfigureAwait(false);
        if (readability is BindingReadability.Masked or BindingReadability.Failed)
        {
            _logger.LogError(
                "[ID-BIND] {DenyCode}: the binding column reads as {Readability} for this identity. Field-level "
                + "security without Read makes every bound contact look unbound, so no bind, create or link is "
                + "safe. Add the BFF application user to the identity-link write profile.",
                ContactBindingDecision.DenyBindingColumnMasked, readability);
            return true;
        }

        return false;
    }

    private void Log(BindingRequest request, ContactBindingResult result, string facts)
    {
        if (result.IsResolved)
        {
            _logger.LogInformation(
                "[ID-BIND] {Plane} caller oid={Oid} resolved to contact {ContactId} via {Action} ({Facts})",
                request.Plane, request.CallerOid, result.ContactId, result.Action, facts);
        }
        else
        {
            // A deny caused by the ENVIRONMENT (a binding column not provisioned, or masked by field-level
            // security) is an operator problem, not a caller one: it is logged as an error.
            var level = result.DenyCode is ContactBindingDecision.DenyBindingColumnMissing
                or ContactBindingDecision.DenyBindingColumnMasked
                or ContactBindingDecision.DenyContactCreateUnavailable
                or ContactBindingDecision.DenyContactKeyConflict
                ? LogLevel.Error
                : LogLevel.Warning;
            _logger.Log(level,
                "[ID-BIND] {Plane} caller oid={Oid} DENIED ({DenyCode}) via {Action} ({Facts})",
                request.Plane, request.CallerOid, result.DenyCode, result.Action, facts);
        }
    }

    private async Task EnsureKnownAsync(IReadOnlyList<Guid> contactIds, Dictionary<Guid, ContactBindingRow> known,
        CancellationToken ct)
    {
        foreach (var id in contactIds.Where(id => !known.ContainsKey(id)).ToList())
        {
            var lookup = await _store.GetContactAsync(id, ct).ConfigureAwait(false);
            if (lookup.Status == LookupStatus.Read)
            {
                Remember(known, lookup);
            }
        }
    }

    private static void Remember(Dictionary<Guid, ContactBindingRow> known, ContactLookup? lookup)
    {
        if (lookup is null) return;
        foreach (var row in lookup.Rows)
        {
            known[row.ContactId] = row;
        }
    }

    private static IdentityChange LinkChange(Guid systemUserId, Guid? contactId)
        => new("systemuser", systemUserId, "sprk_primarycontact=(null)",
            $"sprk_primarycontact={(contactId is { } c ? c.ToString("D") : "(the created contact)")}");

    /// <summary>
    /// How an <see cref="IdentityChange"/> shows a personal value (#2130): whether one is written, never the value itself.
    /// The change strings are logged as the job's before-state lines.
    /// </summary>
    private static string SetOrNone(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : "(set)";

    /// <summary>A last name for a created contact: the family name, else the display name, else the email's local part.</summary>
    public static string LastNameFor(string? familyName, string? displayName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(familyName)) return familyName.Trim();
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        if (!string.IsNullOrWhiteSpace(email)) return email.Split('@')[0];
        return "Unknown";
    }

    private static SystemUserLinkResult Result(
        Guid systemUserId, SystemUserLinkOutcome outcome, Guid? contactId, string? denyCode,
        IdentityCollisionReason? reason, IReadOnlyList<Guid> flagged, IReadOnlyList<IdentityChange> changes, bool applied)
        => new(systemUserId, outcome, contactId, denyCode, reason, flagged, changes, applied);
}

/// <summary>
/// Builds a <see cref="ContactIdentityBinder"/> over ANOTHER Dataverse environment — the registration path's
/// target environment, where a newly created systemuser's contact link must be written (task 141). The BFF's
/// own environment uses the DI-registered binder.
/// </summary>
/// <remarks>
/// A concrete class with a virtual seam (the convention of <c>ExternalParticipationService</c>): a test
/// overrides <see cref="CreateStore"/> to record the environment URL and return an in-memory store, which is
/// how "the link lands in the TARGET environment, never the default one" is asserted without an HTTP double.
/// </remarks>
public class ContactIdentityBinderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<WorkforceIdentityOptions> _options;
    private readonly TimeProvider _time;
    private readonly IConfiguration _configuration;

    // One readability proof per (environment, table, column set), shared by every store this factory builds, so a proof
    // made by one binder holds for the next one (SecuredColumnReadProof.ProofLifetime, on the injected TimeProvider). Only
    // a proven outcome is held, inside the proof; a failed proof is made again at the next use.
    private readonly ConcurrentDictionary<string, SecuredColumnReadProof> _readProofs = new(StringComparer.Ordinal);

    /// <param name="configuration">
    /// Read for <c>Dataverse:ServiceUrl</c>, this BFF's own environment, which <see cref="CreateBinder"/> refuses (ISS-005,
    /// #1673 review K2). Read on each call, so a changed setting applies without a restart.
    /// </param>
    public ContactIdentityBinderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOptionsMonitor<WorkforceIdentityOptions> options,
        TimeProvider time,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>The store for <paramref name="dataverseBaseUrl"/>, authenticated by <paramref name="getToken"/>.</summary>
    public virtual IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
    {
        var logger = _loggerFactory.CreateLogger<DataverseContactIdentityStore>();
        return new DataverseContactIdentityStore(_httpClientFactory, getToken, dataverseBaseUrl, logger,
            (source, table, columns) => SharedReadProof(dataverseBaseUrl, source, table, columns, logger));
    }

    /// <summary>
    /// The one readability proof for <paramref name="columns"/> of <paramref name="table"/> in
    /// <paramref name="dataverseBaseUrl"/>'s environment, made the first time through <paramref name="source"/> (the
    /// requesting store's channel). Every store of one environment reads as the same application identity, so the proof
    /// holds for each of them. The column set is keyed in a stable order, so the same columns share one proof.
    /// </summary>
    internal SecuredColumnReadProof SharedReadProof(
        string dataverseBaseUrl, IColumnReadPermissionSource source, string table, IReadOnlyCollection<string> columns,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataverseBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(columns);
        var key = string.Join("|",
            dataverseBaseUrl.Trim().TrimEnd('/').ToLowerInvariant(),
            table.Trim().ToLowerInvariant(),
            string.Join(",", columns.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        return _readProofs.GetOrAdd(key, _ => new SecuredColumnReadProof(source, table, columns, logger, _time));
    }

    /// <summary>
    /// A binder writing to <paramref name="dataverseBaseUrl"/>, an environment other than this BFF's own: contacts it creates
    /// carry no owner (<c>ExternalAccess:CustomerBusinessUnitId</c> names a unit of THIS environment). The owner-aware binder of
    /// the own environment is the DI-registered one, which <c>RegistrationDataverseService.ContactBinderFor</c> returns for it.
    /// The ownerless creates here are a known gap, tracked in #2270.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="dataverseBaseUrl"/> is this BFF's own environment (<c>Dataverse:ServiceUrl</c>, compared by
    /// <see cref="RegistrationDataverseService.IsSameEnvironment"/>, so the <c>.api.</c> spelling is the same environment):
    /// an ownerless binder there would create app-owned contacts in root. Use the DI-registered binder.
    /// </exception>
    public ContactIdentityBinder CreateBinder(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
    {
        if (RegistrationDataverseService.IsSameEnvironment(_configuration["Dataverse:ServiceUrl"], dataverseBaseUrl))
        {
            throw new InvalidOperationException(
                $"A factory binder is ownerless and may only target a foreign Dataverse environment, but {HostOf(dataverseBaseUrl)} "
                + "is this BFF's own (Dataverse:ServiceUrl). Use the DI-registered ContactIdentityBinder.");
        }

        return new(
            CreateStore(dataverseBaseUrl, getToken),
            _options,
            _time,
            _loggerFactory.CreateLogger<ContactIdentityBinder>(),
            ContactCreateOwner.NoneForForeignEnvironment(
                $"foreign Dataverse environment {HostOf(dataverseBaseUrl)}; see #2270"));
    }

    private static string HostOf(string dataverseBaseUrl)
        => Uri.TryCreate(dataverseBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "(unparseable url)";
}
