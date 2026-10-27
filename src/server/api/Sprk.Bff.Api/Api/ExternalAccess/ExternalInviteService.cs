using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using static Sprk.Bff.Api.Api.ExternalAccess.InviteExternalUserEndpoint;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 185 slice E (design note §11 option (c), §12 I-5) — the ONE CIAM invite core, shared by
/// <c>/invite</c>, <c>/invite-and-grant</c> and the grant flows that send the invite automatically (<c>/grant</c>).
/// </summary>
/// <remarks>
/// <para><b>CLAUDE.md §11.</b></para>
/// <list type="number">
/// <item><b>Existing:</b> the provision logic in <see cref="InviteExternalUserEndpoint"/> (<c>ProvisionAsync</c>: task 184's
/// marker-first ordering, the ExistingAccount branch and the 503 <c>invite_marker_not_written</c>).</item>
/// <item><b>Extension:</b> it is the same logic moved, not new behaviour; the endpoint becomes a thin caller
/// (<see cref="InviteExternalUserEndpoint.ProvisionAsync"/> forwards here), and the grant flows call the same core.</item>
/// <item><b>Cost of doing nothing:</b> without it the grant flows cannot send the invite the owner required in round 98, short
/// of copying 184's marker-first ordering into four places.</item>
/// </list>
/// <para><b>The grant flows (§12).</b> <see cref="InviteOnGrantAsync"/> runs AFTER a grant was written: an unbound contact
/// with an email is invited (I-1: a failed invite never undoes the grant; it is answered <see cref="GrantInviteStatus.Failed"/>
/// with its code and logged at Warning without the code). A pending invite (a marker under 30 days whose code no bind has
/// consumed yet) sends no new code (<see cref="GrantInviteStatus.AlreadyPending"/>, I-2). A bound contact, or one an
/// internal user links to, needs none (<see cref="GrantInviteStatus.NotNeeded"/>). The grant flows decide the invite contact
/// WITHOUT the binder's collision flagging: a grant is not an invite request, so an email it cannot invite onto (two active
/// contacts, say) is answered <see cref="GrantInviteStatus.Failed"/> with the invite's own reason code, and only an explicit
/// Invite flags it. The one exception is the marker write's re-read when it holds no row version
/// (<see cref="ContactIdentityBinder.MarkInviteSentAsync"/>), which can flag a workforce-bound collision.</para>
/// <para><b>Contact-side grants (task 140, I-4).</b> They call <see cref="AssessAsync"/> only, never
/// <see cref="InviteOnGrantAsync"/> (pinned by <c>ContactGrantGuardTests</c>): a contact never invites.</para>
/// <para>The plaintext code goes only into the invite email's link: never logged, never in a response.</para>
/// </remarks>
public sealed class ExternalInviteService
{
    private const string ContactEntitySet = "contacts";

    private readonly DataverseWebApiClient _dataverse;
    private readonly CiamUserProvisioningService _ciam;
    private readonly RegistrationEmailService _emails;
    private readonly ContactIdentityBinder _binder;
    private readonly InviteOrganizationResolver _organizations;
    private readonly IConfiguration _configuration;
    private readonly CustomerIdentity _customer;
    private readonly TimeProvider _time;
    private readonly ILogger<ExternalInviteService> _logger;

    /// <param name="ciam">
    /// The CIAM provisioner, injected directly (task 185 slice F, ADR-010 path C: lazy validation instead of a service
    /// locator). It constructs on a host without the <c>Ciam:*</c> configuration and validates at first use, so <c>/grant</c>
    /// and <c>/contact-grants</c> resolve this service there; an invite on such a host is answered
    /// <see cref="GrantInviteStatus.Failed"/> with <see cref="GrantInviteStatus.CiamNotConfiguredReasonCode"/> and the grant
    /// stands (I-1).
    /// </param>
    /// <param name="organizations">
    /// Slice B3: the ONE work-account / portal-account decision <c>/invite</c> makes, so an automatic invite after a grant is
    /// decided from the email's domain exactly as a staff invite is.
    /// </param>
    public ExternalInviteService(
        DataverseWebApiClient dataverse,
        CiamUserProvisioningService ciam,
        RegistrationEmailService emails,
        ContactIdentityBinder binder,
        InviteOrganizationResolver organizations,
        IConfiguration configuration,
        CustomerIdentity customer,
        TimeProvider time,
        ILogger<ExternalInviteService> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _ciam = ciam ?? throw new ArgumentNullException(nameof(ciam));
        _emails = emails ?? throw new ArgumentNullException(nameof(emails));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _organizations = organizations ?? throw new ArgumentNullException(nameof(organizations));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _customer = customer ?? throw new ArgumentNullException(nameof(customer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Slice C3: the identity store for THIS environment, the one the invite reads and writes through. <c>/grant</c> hands it
    /// to the grant core, whose removed-marker check reads it.
    /// </summary>
    internal IContactIdentityStore IdentityStore => _binder.Store;

    // =========================================================================
    // The grant flows
    // =========================================================================

    /// <summary>
    /// What the grant to <paramref name="contactId"/> needs, read only (nothing is written, nothing flagged). <c>Final</c> is
    /// the answer when no invite is to be sent; <c>null</c> when the contact is an active, unbound contact with an email that
    /// no internal user links to and that holds no current invite — one an invite would be sent to. Never throws for a
    /// read that fails: that is <see cref="GrantInviteStatus.Failed"/> with the invite's lookup code.
    /// </summary>
    internal async Task<GrantInviteAssessment> AssessAsync(Guid contactId, CancellationToken ct)
    {
        var store = _binder.Store;

        var contact = await store.GetContactAsync(contactId, ct).ConfigureAwait(false);
        if (contact.Status != LookupStatus.Read)
            return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactLookupFailed));

        if (contact.Rows.FirstOrDefault(r => r.ContactId == contactId) is not { } row)
            return GrantInviteAssessment.Done(Failed(GrantInviteStatus.ContactNotActiveReasonCode));
        // Slice C: a contact whose external access an administrator removed (or whose marker cannot be read) is never
        // invited, whatever its statecode or binding. Only an administrator reinstates the person.
        // Slice C2c: a set marker is a removal; an unproven one is a retryable failure, never reported as a removal.
        if (row.RemovedOn is not null)
            return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactRemovedAdminReinstate));
        if (row.IsRemoved)
            return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactRemovedStateUnavailable));
        if (!row.IsActive)
            return GrantInviteAssessment.Done(Failed(GrantInviteStatus.ContactNotActiveReasonCode));

        switch (row.Binding.Kind)
        {
            case BindingKind.Bound:
                return GrantInviteAssessment.Done(new GrantInviteResult(GrantInviteStatus.NotNeeded));
            case BindingKind.Unreadable:
                return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactBindingUnreadable));
        }

        if (string.IsNullOrWhiteSpace(row.Email))
            return GrantInviteAssessment.Done(new GrantInviteResult(GrantInviteStatus.NoEmail));

        // A contact an internal user links to is that user's: they sign in on the workforce plane (and are told in-app by
        // the grant notifier). No external account is ever attached to it (the invite core refuses it).
        var references = await store.FindSystemUsersLinkingAsync(new[] { contactId }, ct).ConfigureAwait(false);
        if (references.Status != LookupStatus.Read)
            return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactLookupFailed));
        if (references.References.Any(r => r.LinkedContactId == contactId))
            return GrantInviteAssessment.Done(new GrantInviteResult(GrantInviteStatus.NotNeeded));

        // §12 I-2: a pending invite is kept — a new code would break a link the person may be about to open. Pending means a
        // current marker (under 30 days) AND an unconsumed code: every bind clears the hash and leaves the marker (#2005),
        // so a contact unbound after a bind holds a current marker with no code anyone can use, and needs a new one. A
        // marker column not provisioned here is "no invite yet", as E11 reads it.
        var intent = await store.FindBindIntentAsync(contactId, ct).ConfigureAwait(false);
        if (intent.InviteStatus == LookupStatus.Failed || intent.ContactId != contactId)
            return GrantInviteAssessment.Done(Failed(ContactBindingDecision.InviteContactLookupFailed));
        // A pending invite also carries its invite stamp: one written before the stamp existed binds nobody, so it is
        // replaced (a new invite) rather than kept. Slice B2: a current work-account invite carries no code (only a
        // Workforce or WorkforceHome sign-in from its tenant binds it) and is pending too, so a grant never replaces it with
        // a portal-account invite.
        if (intent.InviteStatus == LookupStatus.Read
            && (!string.IsNullOrEmpty(intent.InviteCodeHash) || ContactBindingDecision.IsWorkAccountInvite(intent))
            && !string.IsNullOrEmpty(intent.InvitedEmailHash)
            && ContactBindingDecision.IsInviteMarkerCurrent(intent.InvitedOn, _time.GetUtcNow()))
            return GrantInviteAssessment.Done(new GrantInviteResult(GrantInviteStatus.AlreadyPending));

        return GrantInviteAssessment.Invite(row, references);
    }

    /// <summary>
    /// After an internal user's grant to <paramref name="contactId"/> was written: sends the CIAM invite (with its code) when
    /// the contact is unbound and has an email (§11 option (c)). Never throws and never undoes the grant (I-1): a failure is
    /// logged at Warning (never the code) and answered <see cref="GrantInviteStatus.Failed"/> with its reason code. Runs on
    /// <see cref="CancellationToken.None"/>, as the grant notifier does: the grant is written, so the person is invited even
    /// if the granter's browser has gone.
    /// </summary>
    /// <param name="rootType">Slice B3: the granted record's type, named in a work-account notice; null names no record.</param>
    /// <param name="rootId">Slice B3: the granted record's id (its name is read for the notice); null names no record.</param>
    public async Task<GrantInviteResult> InviteOnGrantAsync(
        Guid contactId, ExternalGrantRootType? rootType = null, Guid? rootId = null)
    {
        var ct = CancellationToken.None;
        try
        {
            var assessment = await AssessAsync(contactId, ct).ConfigureAwait(false);
            if (assessment.Final is { } final)
                return Logged(contactId, final);

            var row = assessment.Contact!;
            var email = row.Email!.Trim();

            // The invite core's own decision over the email, WITHOUT the binder's collision flagging (a grant is not an
            // invite request). The grantee must be the contact the email resolves to.
            var emailLookup = await _binder.Store.FindActiveContactsByEmailAsync(email, ct).ConfigureAwait(false);
            var decision = ContactBindingDecision.DecideInvite(emailLookup, assessment.References);
            switch (decision.Action)
            {
                case InviteContactAction.Refuse:
                case InviteContactAction.Fail:
                    return Logged(contactId, Failed(decision.ReasonCode ?? ContactBindingDecision.InviteContactLookupFailed));
                case InviteContactAction.AlreadyProvisioned when decision.ContactId == contactId:
                    return Logged(contactId, new GrantInviteResult(GrantInviteStatus.NotNeeded));
                case InviteContactAction.ProvisionExisting when decision.ContactId == contactId:
                    break;
                default:
                    return Logged(contactId, Failed(GrantInviteStatus.ContactMismatchReasonCode));
            }

            var etag = emailLookup.Rows.FirstOrDefault(r => r.ContactId == contactId)?.ETag;
            var resolution = new InviteContactResolution(InviteContactAction.ProvisionExisting, contactId, etag, null, null);

            var portalUrl = _configuration["ExternalAccess:PortalUrl"];
            if (string.IsNullOrWhiteSpace(portalUrl))
                return Logged(contactId, Failed(GrantInviteStatus.InviteFailedReasonCode));

            // Slice B3: the same organisation decision /invite makes (InviteOrganizationResolver.DecideAsync), before anything
            // is written. A customer-tenant domain gets a work-account stamp, never a portal-account invite. A lookup with no
            // definite answer writes nothing and is answered "not sent: tenant_lookup_unavailable" in this result, so staff
            // see it and retry with Invite; the grant stands (I-1). The cache is scoped to this deployment's tenant.
            var organization = await _organizations.DecideAsync(
                email, ImpersonatedRootSetSource.DeploymentCacheTenant(_configuration), ct).ConfigureAwait(false);
            switch (organization.Status)
            {
                case InviteOrganizationStatus.LookupUnavailable:
                    return Logged(contactId, Failed(ContactBindingDecision.InviteTenantLookupUnavailable));
                case InviteOrganizationStatus.MalformedDomain:
                    return Logged(contactId, Failed(GrantInviteStatus.InviteFailedReasonCode));
            }

            // Task 185 slice F: a host without Ciam:* constructs the provisioner but cannot create an account. Answered here,
            // before anything is written (no marker, no code), as the invite core would refuse it. Slice B3: only a
            // portal-account invite needs CIAM.
            if (!organization.IsWorkAccount && !_ciam.IsConfigured)
                return Logged(contactId, Failed(GrantInviteStatus.CiamNotConfiguredReasonCode));

            var (firstName, lastName) = await ReadNamesAsync(contactId).ConfigureAwait(false);
            var request = new InviteExternalUserRequest(
                email, Guid.Empty, AccessLevel: 0, firstName, lastName, ExpiryDate: null, OrganizationId: null);

            var outcome = await ProvisionAsync(
                request, _dataverse, _ciam, _emails, _binder, portalUrl, InviteLinkCustomerKey(_customer), _logger,
                resolution, ct, organization.WorkAccountTenantId).ConfigureAwait(false);

            if (outcome.Refusal is { } refusal)
                return Logged(contactId, Failed(refusal.ReasonCode));
            if (outcome.Failure is { } failure)
                return Logged(contactId, Failed(failure.ReasonCode));
            if (outcome.EmailFailed)
                return Logged(contactId, Failed(GrantInviteStatus.EmailNotSentReasonCode));

            // Slice B3: the work-account stamp landed; the notice goes to the stamped address. The grant exists, so it may
            // name the record. Slice C2b: a notice that did not send retracts the stamp and is reported as the invite email
            // is, so the next invite stamps and sends again.
            if (outcome.Status == WorkAccountInvitedStatus)
            {
                WorkAccountNoticeRecord? record = null;
                if (rootType is { } type && rootId is { } id && id != Guid.Empty)
                {
                    record = new WorkAccountNoticeRecord(ExternalGrantRoot.LabelFor(type),
                        await GrantAccessNotifier.ReadRecordNameAsync(_dataverse, _logger, type, id).ConfigureAwait(false));
                }

                if (!await SendWorkAccountNoticeAsync(
                        _emails, email, portalUrl, InviteLinkCustomerKey(_customer), record, contactId, _logger, _binder,
                        outcome.WrittenInviteStamp).ConfigureAwait(false))
                    return Logged(contactId, Failed(GrantInviteStatus.EmailNotSentReasonCode));
            }

            if (outcome.Status == ExistingAccountStatus)
            {
                // §12 I-10: the operator log keeps the branch; the inviter is answered Sent, as for a new account.
                _logger.LogInformation(
                    "[EXT-INVITE] Grant to contact {ContactId}: the person already has a CIAM account ({InviteOutcome}); " +
                    "the existing-account email was sent. Operator log only: the grant response says {InviteStatus}.",
                    contactId, ExistingAccountStatus, GrantInviteStatus.Sent);
            }

            return Logged(contactId, outcome.Status switch
            {
                ExistingAccountStatus => new GrantInviteResult(GrantInviteStatus.Sent),
                AlreadyProvisionedStatus => new GrantInviteResult(GrantInviteStatus.NotNeeded),
                ProvisionedStatus => new GrantInviteResult(GrantInviteStatus.Sent),
                WorkAccountInvitedStatus => new GrantInviteResult(GrantInviteStatus.WorkAccountInvited),
                _ => Failed(GrantInviteStatus.InviteFailedReasonCode),
            });
        }
        catch (Exception ex)
        {
            // The exception never carries the code (it exists only inside the core, until the email is sent).
            _logger.LogWarning(ex,
                "[EXT-INVITE] The automatic invite for contact {ContactId} after a grant failed. The grant stands; retry with Invite.",
                contactId);
            return Failed(GrantInviteStatus.InviteFailedReasonCode);
        }
    }

    private GrantInviteResult Logged(Guid contactId, GrantInviteResult result)
    {
        if (result.Status == GrantInviteStatus.Failed)
        {
            _logger.LogWarning(
                "[EXT-INVITE] The automatic invite for contact {ContactId} after a grant was not sent ({ReasonCode}). The grant " +
                "stands; retry with Invite.", contactId, result.ErrorCode);
        }
        else
        {
            _logger.LogInformation(
                "[EXT-INVITE] Grant to contact {ContactId}: invite {InviteStatus}.", contactId, result.Status);
        }

        return result;
    }

    private static GrantInviteResult Failed(string reasonCode) => new(GrantInviteStatus.Failed, reasonCode);

    /// <summary>The contact's first and last name for the CIAM account and the email greeting; best effort (nulls).</summary>
    private async Task<(string? FirstName, string? LastName)> ReadNamesAsync(Guid contactId)
    {
        try
        {
            var row = await _dataverse.RetrieveAsync<Dictionary<string, JsonElement>>(
                ContactEntitySet, contactId, "firstname,lastname", CancellationToken.None).ConfigureAwait(false);
            return (StringOf(row, "firstname"), StringOf(row, "lastname"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EXT-INVITE] The name of contact {ContactId} could not be read; the invite is sent without it.", contactId);
            return (null, null);
        }

        static string? StringOf(Dictionary<string, JsonElement>? row, string column)
            => row is not null && row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.String
               && value.GetString() is { } s && !string.IsNullOrWhiteSpace(s)
                ? s.Trim()
                : null;
    }

    // =========================================================================
    // Slice B3: the work-account notice
    // =========================================================================

    /// <summary>The record a work-account notice names: only when a grant to it was written for the person.</summary>
    /// <param name="Label">The record type's label (<see cref="ExternalGrantRoot.LabelFor"/>).</param>
    /// <param name="Name">The record's display name, or null when it has none or could not be read.</param>
    internal sealed record WorkAccountNoticeRecord(string Label, string? Name);

    /// <summary>What a work-account notice says, and where its one link goes.</summary>
    internal sealed record WorkAccountNotice(string Subject, string Headline, string Message, string LinkUrl, string LinkText);

    /// <summary>
    /// The work-account notice for <paramref name="record"/> (null: fixed text, for <c>/invite</c>, which writes no grant),
    /// or <c>null</c> when the portal link cannot be built. Fixed text plus, when a grant exists, the record's label and
    /// name; the link is the portal with this deployment's customer key (<see cref="BuildInviteLink"/> with no code). It never
    /// carries a code, a contact id or a tenant id. The name is a plain-text value: the template HTML-encodes it in the
    /// body, and the subject has every control character removed. Pure.
    /// </summary>
    internal static WorkAccountNotice? BuildWorkAccountNotice(
        string? portalUrl, string? customerKey, WorkAccountNoticeRecord? record)
    {
        if (BuildInviteLink(portalUrl, customerKey, inviteCode: null) is not { } link)
            return null;

        const string Message =
            "Sign in with your work account: on the Spaarke sign-in page, choose \"Sign in with your company account\".";
        if (record is null)
        {
            return new WorkAccountNotice(
                "You've been invited to Spaarke", "You've been invited to Spaarke", Message, link, "Open Spaarke");
        }

        var label = WithoutControlCharacters(record.Label);
        var name = record.Name is { } raw ? WithoutControlCharacters(raw).Trim() : null;
        var headline = string.IsNullOrEmpty(name)
            ? $"You've been given access to a {label} in Spaarke"
            : $"You've been given access to the {label} \"{name}\" in Spaarke";
        return new WorkAccountNotice(headline, headline, Message, link, "Open Spaarke");
    }

    /// <summary><paramref name="value"/> with every control character (and line or paragraph separator) removed. Pure.</summary>
    internal static string WithoutControlCharacters(string value)
        => string.Concat(value.Where(c => !char.IsControl(c)
                                           && char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.LineSeparator
                                               or System.Globalization.UnicodeCategory.ParagraphSeparator)));

    /// <summary>
    /// Sends the work-account notice (<see cref="BuildWorkAccountNotice"/>) to <paramref name="stampedEmail"/>, the address the
    /// invite stamp was written for, through the shared mailbox's portal notification (the existing template and send path).
    /// Called only after a work-account stamp landed. <c>true</c> when it was sent; <c>false</c> (logged at Warning, never
    /// with the address) when it could not be built or sent, and the caller reports "email not sent". Slice C2b: a pending
    /// invite exists only if its email went out, so a notice that did not send (a timeout included) retracts the stamp this
    /// invite wrote (<see cref="ContactIdentityBinder.RetractUnsentInviteAsync"/>; a newer invite is never cleared, and a
    /// clear that fails is logged at Error). Never throws.
    /// </summary>
    /// <param name="binder">The binder that wrote the stamp; it retracts the stamp when the notice does not send.</param>
    /// <param name="writtenInviteStamp">The stamp's email hash the invite wrote (<c>ProvisionOutcome.WrittenInviteStamp</c>).</param>
    internal static async Task<bool> SendWorkAccountNoticeAsync(
        RegistrationEmailService emails, string stampedEmail, string? portalUrl, string? customerKey,
        WorkAccountNoticeRecord? record, Guid contactId, ILogger logger, ContactIdentityBinder binder,
        string? writtenInviteStamp)
    {
        if (await TrySendWorkAccountNoticeAsync(emails, stampedEmail, portalUrl, customerKey, record, contactId, logger)
                .ConfigureAwait(false))
        {
            return true;
        }

        await RetractUnsentInviteAsync(binder, contactId, writtenInviteStamp, logger).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Slice C2b: retracts the invite this request wrote on <paramref name="contactId"/>, whose email did not send
    /// (<see cref="ContactIdentityBinder.RetractUnsentInviteAsync"/>). Runs on <see cref="CancellationToken.None"/>: the email
    /// is already lost, so a cancelled request still clears what it wrote. No stamp written (null) is nothing to retract.
    /// Never throws.
    /// </summary>
    internal static async Task RetractUnsentInviteAsync(
        ContactIdentityBinder binder, Guid contactId, string? writtenInviteStamp, ILogger logger)
    {
        if (string.IsNullOrEmpty(writtenInviteStamp))
        {
            return;
        }

        try
        {
            await binder.RetractUnsentInviteAsync(contactId, writtenInviteStamp, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-INVITE] The invite on contact {ContactId} whose email did not send could not be cleared; it may still be "
                + "pending. Invite the person again.", contactId);
        }
    }

    private static async Task<bool> TrySendWorkAccountNoticeAsync(
        RegistrationEmailService emails, string stampedEmail, string? portalUrl, string? customerKey,
        WorkAccountNoticeRecord? record, Guid contactId, ILogger logger)
    {
        var notice = BuildWorkAccountNotice(portalUrl, customerKey, record);
        if (notice is null)
        {
            logger.LogWarning(
                "[EXT-INVITE] Contact {ContactId} is invited for a work-account sign-in, but the notice was not sent: the portal "
                + "link could not be built from ExternalAccess:PortalUrl.", contactId);
            return false;
        }

        using var timeout = new CancellationTokenSource(GrantAccessNotifier.DefaultSendTimeout);
        try
        {
            await emails.SendPortalNotificationEmailAsync(
                stampedEmail.Trim(), notice.Subject, notice.Headline, notice.Message, notice.LinkUrl, notice.LinkText,
                correlationId: $"work-account-invite-{Guid.NewGuid():N}", timeout.Token).ConfigureAwait(false);
            logger.LogInformation("[EXT-INVITE] Contact {ContactId}: the work-account notice was sent.", contactId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-INVITE] Contact {ContactId} is invited for a work-account sign-in, but the notice was not sent; invite "
                + "again to resend it.", contactId);
            return false;
        }
    }

    // =========================================================================
    // The invite core (moved from InviteExternalUserEndpoint, task 185 §12 I-5; behaviour unchanged)
    // =========================================================================

    /// <summary>
    /// Resolves-or-creates the Dataverse Contact by email and — if not already provisioned — creates a
    /// CIAM local account, persists the returned oid to <c>Contact.sprk_externalobjectid</c> (plane External),
    /// and sends the onboarding email. <b>Idempotent</b>: a Contact already bound on the CIAM plane creates NO
    /// second account ("AlreadyProvisioned"). <b>Refuses</b> (task 141) a contact bound on the WORKFORCE plane,
    /// one an internal user links to, an ambiguous email, or an unreadable binding — BEFORE any CIAM account is
    /// created. Returns a <see cref="InviteLookupFailure"/> (HTTP 503, its own reason code) when the contact
    /// lookup could not be read. Stamps the pending-invite marker before the CIAM account is created (task 184), and
    /// answers "ExistingAccount" — no account, the contact kept pending, the sign-in email sent — when the person already
    /// has a CIAM account. Answers a retryable 503 (<see cref="ContactBindingDecision.InviteMarkerNotWritten"/>, no email)
    /// when the marker write failed, or when the contact is left with no oid (ExistingAccount, a failed oid write) and the
    /// marker did not land. Throws on any other hard failure (Contact create, CIAM create, oid bind). Shared
    /// by <c>/invite</c>, <c>/invite-and-grant</c> (task 029) and the grant flows (task 185).
    /// </summary>
    /// <param name="resolution">
    /// The invite resolution the caller ALREADY made with <see cref="ContactIdentityBinder.ResolveInviteContactAsync"/>,
    /// or <c>null</c> to resolve here. <c>/invite-and-grant</c> resolves once, BEFORE onboarding, judges that contact
    /// with the grant checks (task 139), and passes the same resolution in — so the contact the checks judged IS the
    /// contact onboarding provisions: one email→contact answer, never a second, possibly different, lookup.
    /// </param>
    /// <param name="workAccountTenantId">
    /// Slice B2: null for a portal-account (CIAM) invite, everything above. The customer tenant the invited email's
    /// organisation resolved to (<see cref="InviteOrganizationResolver"/>) for a work-account invite: the contact is resolved
    /// or created as above, a contact that already carries a sign-in is refused
    /// (<see cref="InviteExternalUserEndpoint.WorkAccountNotProvisionable"/>), and the invite stamp (email hash, that tenant,
    /// the marker; no code) is written. No CIAM account, no oid, no plane and no email: only a Workforce or WorkforceHome
    /// sign-in from that tenant binds the contact. A stamp that did not land is the retryable 503
    /// <see cref="ContactBindingDecision.InviteMarkerNotWritten"/>.
    /// </param>
    internal static async Task<ProvisionOutcome> ProvisionAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ContactIdentityBinder binder,
        string portalUrl,
        string? customerKey,
        ILogger logger,
        InviteContactResolution? resolution,
        CancellationToken ct,
        Guid? workAccountTenantId = null)
    {
        if (workAccountTenantId == Guid.Empty)
        {
            throw new ArgumentException("A work-account tenant is a non-empty GUID, or null for a portal account.",
                nameof(workAccountTenantId));
        }

        // Task 185: the invite link is built from trusted config only. A portal URL it cannot use (not https, credentials in
        // it) stops the invite here, before anything is resolved, written or created.
        if (BuildInviteLink(portalUrl, customerKey, inviteCode: null) is null)
        {
            throw new InvalidOperationException(
                "ExternalAccess:PortalUrl must be an absolute https URL with no credentials to build the invite link.");
        }

        // Task 185 slice F: the CIAM configuration is validated at first use. On a host without Ciam:* this throws
        // InvalidOperationException HERE, before any contact is resolved, created or marked. Slice B3: only a portal-account
        // invite needs CIAM; a work-account invite creates no account.
        if (workAccountTenantId is null)
        {
            ciamProvisioner.EnsureConfigured();
        }

        resolution ??= await binder.ResolveInviteContactAsync(request.Email, ct);

        if (NotProvisionable(resolution, request.Email, logger) is { } stopped)
        {
            return stopped;
        }

        if (workAccountTenantId is not null
            && InviteExternalUserEndpoint.WorkAccountNotProvisionable(resolution, request.Email, logger) is { } bound)
        {
            return bound;
        }

        Guid contactId;
        string? etag = null;
        switch (resolution.Action)
        {
            case InviteContactAction.AlreadyProvisioned:
                // Idempotency gate: an oid bound on the CIAM plane means the CIAM account already exists — do NOT
                // create a second account (and do not re-send the onboarding email).
                logger.LogInformation(
                    "[EXT-INVITE] Contact {ContactId} ({Email}) already has a CIAM oid bound — skipping account creation (idempotent).",
                    resolution.ContactId, request.Email);
                return new ProvisionOutcome(resolution.ContactId!.Value, AlreadyProvisionedStatus);

            case InviteContactAction.ProvisionExisting:
                contactId = resolution.ContactId!.Value;
                etag = resolution.ETag;
                break;

            case InviteContactAction.CreateContact:
                {
                    // ISS-005 (#1673): the new contact is owned by the customer business unit's default owner team, resolved by the
                    // binder's one owner decision (the same refusal as registration and first sign-in). Decided BEFORE anything is
                    // written: an unconfigured or unreadable owner creates no contact, marker or CIAM account. Only this create
                    // sets an owner; a ProvisionExisting contact keeps the owner it has.
                    var owner = await binder.ResolveCreateOwnerAsync("Invite create", "the invitee", ct);
                    if (owner.Status != ContactIdentityBinder.OwnerTeamStatus.Resolved)
                    {
                        return OwnerNotResolved(owner.Status, request.Email, logger);
                    }

                    try
                    {
                        contactId = await CreateContactAsync(dataverseClient, request, owner.TeamId, logger, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested && IsOwnerPrivilegeRefusal(ex))
                    {
                        // K1 (UAC re-check, round 3): Dataverse refused the owned create for the OWNER's privilege (0x80040299: the owner
                        // team holds no role with contact Read). The same refusal as an unresolved owner: 500 + reason code, nothing
                        // created. Only that exact Dataverse error code counts (read by DataverseWebApiClient.CreateAsync, which never
                        // reads the message); any other 403, including the BFF identity's own 0x80040220, is NOT an owner fault.
                        logger.LogError(ex,
                            "[EXT-INVITE] Invite create refused ({ReasonCode}): Dataverse refused the owner of the new contact " +
                            "(0x80040299, Read Privilege Check For Owner). The default owner team of the business unit named by " +
                            "ExternalAccess:CustomerBusinessUnitId must hold a security role with Read on contact " +
                            "(and Create/Append as needed). Nothing was created.",
                            ContactBindingDecision.DenyOwnerUnconfigured);
                        return OwnerUnconfiguredOutcome();
                    }
                    catch (HttpRequestException ex) when (!ct.IsCancellationRequested
                                                          && ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        // A 403 that is not the owner fault keeps its previous, generic failure path (it propagates). Name the
                        // Dataverse error code (never its message) so the operator can tell it from the owner fault.
                        logger.LogError(ex,
                            "[EXT-INVITE] Invite create refused with HTTP 403, Dataverse error code {DataverseErrorCode}: not the " +
                            "owner-privilege fault (0x80040299), reported as a generic failure. Nothing was created.",
                            DataverseWebApiClient.ErrorCodeOf(ex) ?? "(none)");
                        throw;
                    }

                    break;
                }

            default:
                // NeedReferenceLookup (or an action this code does not know) cannot reach here: the binder
                // completes the reference lookup itself. Fail closed — nothing is created.
                throw new InvalidOperationException(
                    $"The invite decision for '{request.Email}' ended in an unexpected state ({resolution.Action}).");
        }

        // Task 184 — the pending-invite marker, BEFORE the CIAM account exists: a first CIAM sign-in that has to email-bind
        // this contact finds THIS environment's intent. Which paths NEED it:
        //   - the normal new-account path does NOT: the CIAM create and the oid bind happen in this request, so the
        //     contact is bound before anyone signs in. It proceeds when the column is not provisioned yet (a customer
        //     stamp between Deploy-Release's Phase 2 BFF deploy and its Phase 3 SpaarkeMaster import).
        //   - "ExistingAccount" and the oid-write-failed repair path DO: the contact is left with no oid, and the marker
        //     is the only evidence E11 can bind it on (a later grant is the other). If the marker did not land there,
        //     the invite answers a retryable 503 (InviteMarkerNotWritten) and sends no email — never a success that
        //     leaves a contact no sign-in can bind.
        // A marker write that FAILED (anything but Written / ColumnMissing) stops the invite here: 503, nothing created.
        // The same write stamps the invite (the invited address, hashed, and the tenant it names): every email bind onto the
        // contact needs that stamp. It does not depend on the CIAM account branch below.
        var mark = await binder.MarkInviteSentAsync(contactId, etag, request.Email, workAccountTenantId, ct);
        var markerLanded = mark.Status == StoreWriteStatus.Written;

        // #2005: the contact had no row version (this invite created it) and its re-read found it bound, linked to an
        // internal user, deactivated or unreadable meanwhile (or could not be read), so nothing was marked. It is answered as
        // an invite that resolved to that contact is: the same refusal, flag and reason code, or the same lookup-failure 503.
        // Anything else falls to the marker-not-written 503 below.
        if (mark.Reread is { } reread)
        {
            if (NotProvisionable(reread, request.Email, logger) is { } refused)
                return refused;
            if (workAccountTenantId is not null
                && InviteExternalUserEndpoint.WorkAccountNotProvisionable(reread, request.Email, logger) is { } boundSince)
                return boundSince;
            if (reread.Action == InviteContactAction.AlreadyProvisioned)
                return new ProvisionOutcome(contactId, AlreadyProvisionedStatus);
        }

        // Slice B2: a work-account invite ends here. The stamp is the contact's only evidence, so a stamp that did not land
        // (a write fault, or a column not provisioned) is the retryable 503. No CIAM account, no oid, no email.
        if (workAccountTenantId is { } workTenant)
        {
            if (!markerLanded)
            {
                logger.LogWarning(
                    "[EXT-INVITE] Contact {ContactId} could not be stamped for a work-account invite ({Status}) — answered 503, "
                    + "nothing sent.", contactId, mark.Status);
                return MarkerNotWritten(contactId);
            }

            logger.LogInformation(
                "[EXT-INVITE] Contact {ContactId} invited for a work-account sign-in from customer tenant {TenantId}; no CIAM "
                + "account and no invite email.", contactId, workTenant);
            return new ProvisionOutcome(contactId, WorkAccountInvitedStatus) { WrittenInviteStamp = mark.WrittenEmailHash };
        }

        // Task 185: the link both invite emails open. It carries the one-time code only when the marker and its hash landed
        // (a new-account invite on a stamp without the columns binds in this request and needs none). Never logged.
        var inviteUrl = BuildInviteLink(portalUrl, customerKey, mark.InviteCode)!;
        switch (mark.Status)
        {
            case StoreWriteStatus.Written:
                // The marker moved the row version. The bind below re-reads the contact and binds it only while it is
                // still active and unbound, under its fresh version (the path a just-created contact already takes).
                etag = null;
                break;
            case StoreWriteStatus.ColumnMissing:
                break;
            default:
                logger.LogWarning(
                    "[EXT-INVITE] Contact {ContactId} for {Email} could not be marked invited ({Status}) — no CIAM account created.",
                    contactId, request.Email, mark.Status);
                return MarkerNotWritten(contactId);
        }

        // Create the CIAM local account (broker-only; no B2B guest).
        string rawOid;
        try
        {
            rawOid = await ciamProvisioner.CreateCiamUserAsync(request.Email, request.FirstName, request.LastName, ct);
        }
        catch (CiamAccountAlreadyExistsException)
        {
            // Task 184 — the person already has a CIAM account (Graph 400 ObjectConflict on proxyAddresses). The invite
            // succeeds: the contact stays PENDING (marker set, no oid), and the first sign-in email-binds it under E11's
            // intent gate. The existing account's oid is deliberately NOT looked up here (no User.Read.All on any stamp).
            // The marker is this contact's only evidence, so without it there is no success to report: 503, no email.
            if (!markerLanded)
            {
                logger.LogWarning(
                    "[EXT-INVITE] {Email} already has a CIAM account, but contact {ContactId} carries no pending-invite marker "
                    + "({Status}) — answered 503, no email sent. Retry once the column is provisioned.",
                    request.Email, contactId, mark.Status);
                return MarkerNotWritten(contactId);
            }

            // §12 I-10: this operator log line is the ONLY place the branch is named; the inviter's response never says it.
            logger.LogInformation(
                "[EXT-INVITE] {Email} already has a CIAM account ({InviteOutcome}) — contact {ContactId} kept pending; the first "
                + "sign-in binds it.",
                request.Email, ExistingAccountStatus, contactId);
            var existingAccountEmailFailed = false;
            try
            {
                await emailService.SendCiamExistingAccountEmailAsync(
                    request.Email, request.FirstName ?? string.Empty, portalUrl, inviteUrl, ct);
            }
            catch (Exception ex)
            {
                existingAccountEmailFailed = true;
                logger.LogWarning(ex, "[EXT-INVITE] Contact {ContactId} for {Email}: the sign-in email failed to send.",
                    contactId, request.Email);
            }

            // Slice C2b: a pending invite exists only if its email went out. Nobody holds this code, so the invite this
            // request wrote is retracted (never a newer one): the next invite, staff or automatic, writes and sends a new one.
            if (existingAccountEmailFailed)
            {
                await RetractUnsentInviteAsync(binder, contactId, mark.WrittenEmailHash, logger).ConfigureAwait(false);
            }

            return new ProvisionOutcome(contactId, ExistingAccountStatus, EmailFailed: existingAccountEmailFailed);
        }

        if (!Guid.TryParse(rawOid, out var ciamOid) || ciamOid == Guid.Empty)
        {
            throw new InvalidOperationException($"CIAM returned an object id that is not a GUID for '{request.Email}'.");
        }

        // Persist the oid (D format) + plane External, conditional on the row version read with it. If the bind
        // does not land, no email is sent, so nobody holds a code: a retried invite (ExistingAccount, a new code) repairs it.
        // Task 185 (owner Q5): when the marker and its code hash landed, the same bind PATCH clears the now-inert hash.
        var bind = await binder.BindInvitedContactAsync(contactId, etag, ciamOid, consumeInviteCode: markerLanded, ct);
        if (bind.Status != StoreWriteStatus.Written && !markerLanded)
        {
            // Task 184 — the repair path needs the marker: without it the first sign-in finds no intent (unless a grant
            // follows, which this failure prevents). Retryable: a retry finds the account and answers "ExistingAccount"
            // once the marker can land. No email.
            logger.LogWarning(
                "[EXT-INVITE] CIAM account {Oid} was created for {Email} but neither its oid ({BindStatus}) nor the pending-invite "
                + "marker landed on contact {ContactId} — answered 503, no email sent.",
                ciamOid, request.Email, bind.Status, contactId);
            return MarkerNotWritten(contactId);
        }

        if (bind.Status != StoreWriteStatus.Written)
        {
            throw new InvalidOperationException(
                $"CIAM account {ciamOid:D} was created for '{request.Email}' but its oid could not be bound to contact " +
                $"{contactId:D} ({bind.Status}). No email was sent: retry the invite, which sends a new invite code that binds " +
                "the contact at its first sign-in.");
        }

        // Send the onboarding email (task 024; task 185: its links open the invite link).
        // Non-fatal: the account + oid are persisted. But the link carries the invite code, which exists only in this email,
        // so a failed send leaves nobody holding the code: sending the portal URL out of band does not help. The person needs
        // a re-invite, which sends a new code. EmailFailed reports it (the routes' emailSent, the grant flows' email_not_sent).
        var onboardingEmailFailed = false;
        try
        {
            await emailService.SendCiamOnboardingEmailAsync(
                request.Email, request.FirstName ?? string.Empty, portalUrl, inviteUrl, ct);
        }
        catch (Exception ex)
        {
            onboardingEmailFailed = true;
            logger.LogWarning(ex, "[EXT-INVITE] CIAM account created for {Email} but onboarding email failed to send.", request.Email);
        }

        logger.LogInformation(
            "[EXT-INVITE] Provisioned CIAM user {Oid} for {Email} — Contact: {ContactId}",
            ciamOid, request.Email, contactId);

        return new ProvisionOutcome(contactId, ProvisionedStatus, EmailFailed: onboardingEmailFailed);
    }

    /// <summary>
    /// Task 184 — the retryable 503 of an invite whose contact needed the pending-invite marker as its only evidence and
    /// did not get it. Reported through the existing lookup-failure shape (503 + reason code), so /invite-and-grant writes
    /// no grant and neither route sends an email.
    /// </summary>
    private static ProvisionOutcome MarkerNotWritten(Guid contactId)
        => new(contactId, "MarkerNotWritten",
            Failure: new InviteLookupFailure(
                ContactBindingDecision.InviteMarkerNotWritten,
                ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteMarkerNotWritten)!,
                Title: "Invite could not be recorded"));

    /// <summary>
    /// ISS-005 (#1673): the invite's answer when no owner team could be resolved for the contact it would create: nothing was
    /// created. An unset or unusable <c>ExternalAccess:CustomerBusinessUnitId</c> is a 500 with
    /// <see cref="ContactBindingDecision.DenyOwnerUnconfigured"/> (the code registration and first sign-in answer); an owner
    /// lookup that could not be read is the retryable 503 every invite lookup failure is. No id is in either message.
    /// </summary>
    private static ProvisionOutcome OwnerNotResolved(
        ContactIdentityBinder.OwnerTeamStatus status, string email, ILogger logger)
    {
        if (status == ContactIdentityBinder.OwnerTeamStatus.LookupFailed)
        {
            logger.LogWarning(
                "[EXT-INVITE] The owner team for the new contact of {Email} could not be read — nothing created", email);
            return new ProvisionOutcome(Guid.Empty, "LookupFailed",
                Failure: new InviteLookupFailure(
                    ContactBindingDecision.InviteContactLookupFailed,
                    ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactLookupFailed)
                        ?? "The invite could not be completed. Nothing was created; try again."));
        }

        logger.LogError(
            "[EXT-INVITE] Invite for {Email} refused: no owner team could be resolved for the new contact " +
            "(ExternalAccess:CustomerBusinessUnitId) — nothing created", email);
        return OwnerUnconfiguredOutcome();
    }

    /// <summary>The Dataverse error code of "Read Privilege Check For Owner failed" (the owner team cannot own the row).</summary>
    private const string OwnerPrivilegeErrorCode = "0x80040299";

    /// <summary>
    /// True only when the create failed with a 403 whose Dataverse <c>error.code</c> is exactly 0x80040299. NOT
    /// <c>SignalWriter.IsDataverseAccessDenied</c>: that also accepts 0x80040220 (the BFF identity's own missing privilege)
    /// and a bare 403, and neither is an owner fault. Pure.
    /// </summary>
    internal static bool IsOwnerPrivilegeRefusal(Exception ex)
        => ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden }
           && string.Equals(DataverseWebApiClient.ErrorCodeOf(ex), OwnerPrivilegeErrorCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>The 500 <see cref="ContactBindingDecision.DenyOwnerUnconfigured"/> answer, whichever way the owner failed.</summary>
    private static ProvisionOutcome OwnerUnconfiguredOutcome()
    {
        return new ProvisionOutcome(Guid.Empty, "OwnerUnconfigured",
            Failure: new InviteLookupFailure(
                ContactBindingDecision.DenyOwnerUnconfigured,
                "Invitations are not configured for this organization. Nothing was created; contact your administrator.",
                Title: "Invitations are not configured",
                StatusCode: StatusCodes.Status500InternalServerError));
    }

    /// <summary>
    /// Creates a new, unbound Contact for the invite email, owned by <paramref name="ownerTeamId"/> (the customer business
    /// unit's default owner team; ISS-005, #1673). Throws on failure.
    /// </summary>
    private static async Task<Guid> CreateContactAsync(
        DataverseWebApiClient dataverseClient,
        InviteExternalUserRequest request,
        Guid ownerTeamId,
        ILogger logger,
        CancellationToken ct)
    {
        if (ownerTeamId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Invite create: refusing to create a contact with no owner team (it would be app-owned in the root business unit).");
        }

        var payload = new Dictionary<string, object?>
        {
            ["emailaddress1"] = request.Email,
            ["firstname"] = request.FirstName ?? string.Empty,
            ["lastname"] = request.LastName ?? request.Email.Split('@')[0],
            ["ownerid@odata.bind"] = $"/teams({ownerTeamId:D})",
        };

        var newContactId = await dataverseClient.CreateAsync(ContactEntitySet, payload, ct);
        if (newContactId == Guid.Empty)
        {
            throw new InvalidOperationException($"Failed to create a Contact for '{request.Email}'.");
        }

        logger.LogInformation("[EXT-INVITE] Created new Contact {ContactId} for email {Email}",
            newContactId, request.Email);
        return newContactId;
    }
}

/// <summary>
/// Task 185 slice E — <see cref="ExternalInviteService.AssessAsync"/>'s answer: a final <see cref="GrantInviteResult"/>, or
/// the unbound contact (with its reference read) an invite would be sent to.
/// </summary>
internal sealed record GrantInviteAssessment(GrantInviteResult? Final, ContactBindingRow? Contact, ReferenceLookup? References)
{
    /// <summary>No invite is to be sent; <paramref name="result"/> is the answer.</summary>
    public static GrantInviteAssessment Done(GrantInviteResult result) => new(result, null, null);

    /// <summary>An invite would be sent to <paramref name="contact"/>.</summary>
    public static GrantInviteAssessment Invite(ContactBindingRow contact, ReferenceLookup references) => new(null, contact, references);
}
