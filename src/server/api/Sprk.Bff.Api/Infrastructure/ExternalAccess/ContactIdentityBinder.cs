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

using System.Security.Claims;
using Microsoft.Extensions.Options;

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

/// <summary>Runs the binding decision and performs its writes. See the file header.</summary>
public sealed class ContactIdentityBinder
{
    private readonly IContactIdentityStore _store;
    private readonly IOptionsMonitor<WorkforceIdentityOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ContactIdentityBinder> _logger;

    public ContactIdentityBinder(
        IContactIdentityStore store,
        IOptionsMonitor<WorkforceIdentityOptions> options,
        TimeProvider time,
        ILogger<ContactIdentityBinder> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// Resolves a workforce caller with no systemuser (Type 2). A MEMBER of a configured customer tenant may be
    /// email-bound on first sign-in, or have a contact created keyed by the oid; everyone else resolves only
    /// through an existing oid binding.
    /// </summary>
    public async Task<ContactBindingResult> ResolveWorkforceCallerAsync(
        ClaimsPrincipal user, Guid callerOid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = WorkforceCallerClaims.From(user);
        var membership = WorkforceMembershipTest.Evaluate(
            claims.Kind, claims.TenantId, claims.Acct, _options.CurrentValue.ParsedCustomerTenantIds());

        var request = new BindingRequest(
            BindingPlane.WorkforceToken,
            callerOid,
            claims.Email,
            membership == WorkforceMembership.Member ? BindingEligibility.EmailBindAndCreate : BindingEligibility.None,
            WorkforceMembershipTest.DenyCodeFor(membership));

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
    /// Resolves a CIAM (external) caller. By oid; the email bind survives ONLY as the repair path for an
    /// invite whose oid write failed after the CIAM account was created. The CIAM plane never creates a
    /// contact — external users arrive by invitation.
    /// </summary>
    public async Task<ContactBindingResult> ResolveCiamCallerAsync(string? oidClaim, string? email, CancellationToken ct)
    {
        var oid = Guid.TryParse(oidClaim?.Trim(), out var parsed) ? parsed : Guid.Empty;
        var request = new BindingRequest(BindingPlane.CiamToken, oid, email, BindingEligibility.EmailBindOnly);
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
            }
            else
            {
                (decision, emailLookup) = await DecideUnlinkedAsync(request, oidLookup, known, ct).ConfigureAwait(false);
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
        var decision = ContactBindingDecision.DecideInvite(emailLookup);
        if (decision.Action == InviteContactAction.NeedReferenceLookup && decision.ContactId is { } candidate)
        {
            var refs = await _store.FindSystemUsersLinkingAsync(new[] { candidate }, ct).ConfigureAwait(false);
            decision = ContactBindingDecision.DecideInvite(emailLookup, refs);
        }

        var row = decision.ContactId is { } id ? emailLookup.Rows.FirstOrDefault(r => r.ContactId == id) : null;

        if (decision.Action == InviteContactAction.Refuse)
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
    /// (a concurrent first sign-in may have bound it in between; that binding is never overwritten).
    /// </summary>
    public async Task<StoreWriteResult> BindInvitedContactAsync(Guid contactId, string? etag, Guid ciamOid, CancellationToken ct)
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

            if (!row.IsActive || row.Binding.Kind != BindingKind.Unbound)
            {
                _logger.LogWarning(
                    "[ID-BIND] Invited contact {ContactId} is no longer an active, unbound contact ({Kind}); its binding "
                    + "is not overwritten", contactId, row.Binding.Kind);
                return new StoreWriteResult(StoreWriteStatus.PreconditionFailed, Error: "the contact was bound or deactivated meanwhile");
            }

            etag = row.ETag;
        }

        return await _store.BindOidAsync(contactId, etag, ciamOid, IdentityPlaneMarker.External, ct).ConfigureAwait(false);
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
        _ => null,
    };

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

            var (decision, _) = await DecideUnlinkedAsync(request, oidLookup, known, ct).ConfigureAwait(false);

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
                    var write = await _store.BindOidAsync(target.ContactId, target.ETag, request.CallerOid,
                        request.CallerPlane, ct).ConfigureAwait(false);
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

                    // A failed bind is NOT "resolved anyway": an unbound contact resolved without its binding
                    // leaves the hijack window open, and the next sign-in retries the bind.
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

                    var create = await _store.CreateContactForOidAsync(request.CallerOid, request.CallerPlane, details, ct)
                        .ConfigureAwait(false);
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
                        default:
                            return ContactBindingResult.Denied(ContactBindingDecision.DenyContactCreateFailed);
                    }
                }

                default:
                    return ContactBindingResult.Denied(decision.DenyCode ?? ContactBindingDecision.DenyContactNotFound);
            }
        }

        return ContactBindingResult.Denied(ContactBindingDecision.DenyContactBindFailed);
    }

    private async Task<(BindingDecision Decision, ContactLookup? EmailLookup)> DecideUnlinkedAsync(
        BindingRequest request, ContactLookup oidLookup, Dictionary<Guid, ContactBindingRow> known, CancellationToken ct)
    {
        var decision = ContactBindingDecision.Decide(request, oidLookup);
        ContactLookup? emailLookup = null;

        if (decision.Action == BindingAction.NeedEmailLookup)
        {
            emailLookup = await _store.FindActiveContactsByEmailAsync(request.Email!, ct).ConfigureAwait(false);
            Remember(known, emailLookup);
            decision = ContactBindingDecision.Decide(request, oidLookup, emailLookup);
        }

        if (decision.Action == BindingAction.NeedReferenceLookup && decision.ContactId is { } candidate)
        {
            var refs = await _store.FindSystemUsersLinkingAsync(new[] { candidate }, ct).ConfigureAwait(false);
            decision = ContactBindingDecision.Decide(request, oidLookup, emailLookup, refs);
        }

        return (decision, emailLookup);
    }

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
                    .Where(id => known.TryGetValue(id, out var r) && ContactBindingDecision.ShouldWriteFlag(r.Flag, party))
                    .ToList();
                foreach (var id in toFlag)
                {
                    var before = known[id].Flag is { } open ? $"flag parties={open.Parties.Count}" : "flag=(none)";
                    changes.Add(new IdentityChange("contact", id, before,
                        $"flag +party reason={decision.Reason} oid={request.CallerOid:D} plane={request.CallerPlane}"));
                }

                if (toFlag.Count == 0)
                {
                    var unreadable = (decision.FlagContactIds ?? none).Any(id => !known.ContainsKey(id));
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
                changes.Add(new IdentityChange("contact", target, "sprk_externalobjectid=(null) sprk_identityplane=(null)",
                    $"sprk_externalobjectid={request.CallerOid:D} sprk_identityplane={IdentityPlaneMarker.Workforce}"));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.BoundLinkedContact, target, null, null, none, changes, false);
                }

                if (await MaskedAsync(ct).ConfigureAwait(false))
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                var etag = known.TryGetValue(target, out var row) ? row.ETag : null;
                var write = await _store.BindOidAsync(target, etag, request.CallerOid, IdentityPlaneMarker.Workforce, ct)
                    .ConfigureAwait(false);
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
                changes.Add(new IdentityChange("contact", target, "sprk_externalobjectid=(null) sprk_identityplane=(null)",
                    $"sprk_externalobjectid={request.CallerOid:D} sprk_identityplane={IdentityPlaneMarker.Workforce}"));
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

                var write = await _store.BindOidAsync(target, known[target].ETag, request.CallerOid,
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
                changes.Add(new IdentityChange("contact", null, "(no contact)",
                    $"create contact lastname={details.LastName} emailaddress1={details.Email ?? "(none)"} "
                    + $"sprk_externalobjectid={request.CallerOid:D} sprk_identityplane={IdentityPlaneMarker.Workforce}"));
                changes.Add(LinkChange(suid, null));
                if (!applyWrites)
                {
                    return Result(suid, SystemUserLinkOutcome.CreatedAndLinked, null, null, null, none, changes, false);
                }

                if (await MaskedAsync(ct).ConfigureAwait(false))
                {
                    return Result(suid, SystemUserLinkOutcome.Denied, null, ContactBindingDecision.DenyBindingColumnMasked,
                        null, none, Array.Empty<IdentityChange>(), false);
                }

                var create = await _store.CreateContactForOidAsync(request.CallerOid, IdentityPlaneMarker.Workforce, details, ct)
                    .ConfigureAwait(false);
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
            if (!ContactBindingDecision.ShouldWriteFlag(current.Flag, party))
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

            // The deny stands regardless; only the operator-visible marker is missing, and the log carries it.
            _logger.LogError(
                "[ID-BIND] Collision on contact {ContactId} ({Reason}) could NOT be flagged ({Status}): {Error}",
                current.ContactId, party.Reason, write.Status, write.Error);
            return false;
        }

        return false;
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
        var pending = holders.Where(h => ContactBindingDecision.ShouldWriteFlag(h.Flag, party)).ToList();
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

    public ContactIdentityBinderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOptionsMonitor<WorkforceIdentityOptions> options,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The store for <paramref name="dataverseBaseUrl"/>, authenticated by <paramref name="getToken"/>.</summary>
    public virtual IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
        => new DataverseContactIdentityStore(
            _httpClientFactory, getToken, dataverseBaseUrl, _loggerFactory.CreateLogger<DataverseContactIdentityStore>());

    /// <summary>A binder writing to <paramref name="dataverseBaseUrl"/>.</summary>
    public ContactIdentityBinder CreateBinder(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
        => new(CreateStore(dataverseBaseUrl, getToken), _options, _time, _loggerFactory.CreateLogger<ContactIdentityBinder>());
}
