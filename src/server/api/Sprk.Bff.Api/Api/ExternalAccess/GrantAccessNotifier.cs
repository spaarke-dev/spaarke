using System.Security.Claims;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>What one grant-notification attempt did (task 181).</summary>
public enum GrantNotificationOutcome
{
    /// <summary>Nobody is to be told: no internal user behind the grantee, only the granter, or a walled user.</summary>
    NotApplicable,

    /// <summary>Every person to be told was sent the in-app notification.</summary>
    Sent,

    /// <summary>At least one person to be told could not be sent it, or who they are could not be read.</summary>
    Failed,
}

/// <summary>
/// Tells an internal user, in the model-driven app's notification bell, that they were just given access to a record,
/// with a link to it (unified-access-control-r2 task 181, owner round 89 item 3). Called by <c>/grant</c> (a contact that
/// represents an internal user) and <c>/share-user</c> (an internal user's share), ONLY after the write succeeded and gave
/// access the person did not already hold. Task 185 slice E (§12 I-4): <c>/contact-grants</c> calls
/// <see cref="NotifyInviteNeededAsync"/> to tell the record's internal owner that a colleague a partner contact granted needs
/// an internal invite.
/// </summary>
/// <remarks>
/// <para><b>Reuse (CLAUDE.md §11).</b> The channel is task 100's: <see cref="NotificationService"/> writing a Dataverse
/// <c>appnotification</c> owned by the recipient, with the same record link (<c>/main.aspx?etn=…&amp;id=…&amp;pagetype=entityrecord</c>).
/// Who a contact represents is the Assigned-To materializer's rule (task 142 over task 141): the users whose
/// <c>sprk_primarycontact</c> names it, or who have no link and whose oid the contact's binding carries
/// (<see cref="AssignedLinkCandidate.Represents"/>, read through <see cref="AssignedAccessStore.ReadLinkCandidatesAsync"/>
/// and the status-first <see cref="IContactIdentityStore"/>) — the same users the read path gives the contact's grants to.
/// Who is a person is <see cref="InternalShareEndpoints.ClassifyEligibility"/>, the one share-eligibility rule; whether a
/// user is walled off a secure record is <see cref="SecureShareNoAccessGuard"/>, the check <c>/share-user</c> runs.</para>
/// <para><b>Best effort.</b> Nothing here throws. A failed read or write is logged and answered
/// <see cref="GrantNotificationOutcome.Failed"/>, which the route reports as <c>notificationFailed</c>; the grant stands.
/// Every Dataverse call uses <see cref="CancellationToken.None"/>: the grant is already written, so the person it was given
/// to is told even if the granter's browser has gone.</para>
/// <para><b>What it names, to whom.</b> Only the people the write just gave access to, never the granter, and never a
/// user the secure record's No Access list walls off (the contact grant does not check users; the read path vetoes them,
/// so naming the record to them would be the leak). The record's name is read app-only here, after the write.</para>
/// <para><b>External contacts (task 036, FR-07).</b> A grant to an already-onboarded external contact (an active contact
/// with a readable identity binding, an email address, and no internal user it represents) is told by email instead, with
/// one deep link into the portal. The email goes through <see cref="RegistrationEmailService"/> — the sender
/// <c>SendCiamOnboardingEmailAsync</c> uses (the shared mailbox, app-only; never the user's mailbox, no OBO). Best effort,
/// like everything here: a send that is refused or fails is logged and the grant stands.</para>
/// </remarks>
public sealed class GrantAccessNotifier
{
    /// <summary>The <c>appnotification</c> category (the Daily Briefing groups by it).</summary>
    internal const string Category = "access";

    /// <summary>A record name longer than this is cut, so the title stays inside <c>appnotification.title</c>'s 256.</summary>
    internal const int MaxNameLength = 180;

    /// <summary>
    /// How long the external email send may take before it is given up as <see cref="GrantNotificationOutcome.Failed"/>
    /// (task 036 review K2): the grant is already written, so a hung shared-mailbox call must not hold the caller's
    /// request open for long.
    /// </summary>
    internal static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(15);

    private readonly NotificationService _notifications;
    private readonly DataverseWebApiClient _dataverse;
    private readonly IContactIdentityStore _identities;
    private readonly AssignedAccessStore _links;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly ExternalParticipationService _participations;
    private readonly RegistrationEmailService _emails;
    private readonly CustomerIdentity _customer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrantAccessNotifier> _logger;

    /// <summary>The bound on one external email send; <see cref="DefaultSendTimeout"/> unless a test shortens it.</summary>
    internal TimeSpan SendTimeout { get; init; } = DefaultSendTimeout;

    public GrantAccessNotifier(
        NotificationService notifications,
        DataverseWebApiClient dataverse,
        IContactIdentityStore identities,
        AssignedAccessStore links,
        SecureShareNoAccessGuard noAccessGuard,
        ExternalParticipationService participations,
        RegistrationEmailService emails,
        CustomerIdentity customer,
        IConfiguration configuration,
        ILogger<GrantAccessNotifier> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _noAccessGuard = noAccessGuard ?? throw new ArgumentNullException(nameof(noAccessGuard));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _emails = emails ?? throw new ArgumentNullException(nameof(emails));
        _customer = customer ?? throw new ArgumentNullException(nameof(customer));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// After a grant to <paramref name="contactId"/> gave it access: tells every enabled person the contact represents.
    /// A contact that represents nobody is an external contact (task 036): when it is already onboarded and has an email
    /// address it is emailed a portal deep link to the record; one not yet onboarded is told nothing here (its CIAM
    /// invitation is the invite path's).
    /// </summary>
    public async Task<GrantNotificationOutcome> NotifyContactGrantAsync(
        ExternalGrantRootType rootType, Guid rootId, Guid contactId, ExternalAccessLevel? level, ClaimsPrincipal caller)
    {
        IReadOnlyList<AssignedLinkCandidate> candidates;
        Guid? boundOid = null;
        var oidResolvesHere = false;
        ContactBindingRow? readContact = null;
        try
        {
            // Status-first: a contact read that failed is "could not tell", never "represents nobody". A binding column
            // not provisioned here means no contact carries an oid, so only the link counts.
            var contact = await _identities.GetContactAsync(contactId, CancellationToken.None);
            if (contact.Status == LookupStatus.Read)
            {
                // An inactive (or missing) contact is nobody's: it resolves for no one on the read path (ADR-003).
                if (contact.Rows.FirstOrDefault(r => r.ContactId == contactId) is not { IsActive: true } row)
                {
                    _logger.LogInformation(
                        "[GRANT-NOTIFY] Contact {ContactId} is not active; nobody is told about the grant on {RootType} {RootId}.",
                        contactId, rootType, rootId);
                    return GrantNotificationOutcome.NotApplicable;
                }

                readContact = row;
                var raw = row.RawOid?.Trim();
                boundOid = Guid.TryParse(raw, out var oid) && oid != Guid.Empty ? oid : null;
            }
            else if (contact.Status == LookupStatus.ColumnMissing)
            {
                // No binding column here, so no oid — but the contact must still be active. Read its state the read
                // path's way (statecode only): inactive tells nobody; unreadable is a failure (could not tell).
                switch (await _participations.ReadContactStateAsync(contactId, CancellationToken.None))
                {
                    case ContactRecordState.Active:
                        break;
                    case ContactRecordState.Inactive:
                        _logger.LogInformation(
                            "[GRANT-NOTIFY] Contact {ContactId} is not active; nobody is told about the grant on {RootType} {RootId}.",
                            contactId, rootType, rootId);
                        return GrantNotificationOutcome.NotApplicable;
                    default:
                        throw new InvalidOperationException($"Contact {contactId}'s state could not be read.");
                }
            }
            else
            {
                throw new InvalidOperationException($"Contact {contactId}'s binding could not be read ({contact.Status}).");
            }

            candidates = await _links.ReadLinkCandidatesAsync(contactId, boundOid, CancellationToken.None)
                         ?? Array.Empty<AssignedLinkCandidate>();

            // A user with NO link is represented through the oid binding only where the READ path would resolve it:
            // the binder's own oid decision (ContactBindingDecision.DecideBoundContact, as IdentityNormalizationService
            // asks it) must answer "exactly one active contact carries the oid, and it is this one". Two contacts on
            // the oid, or an inactive one, resolve to no contact there, so nobody is told here either.
            if (boundOid is { } bound && candidates.Any(c => c.PrimaryContactId is null && c.Oid == bound))
            {
                var byOid = await _identities.FindContactsByOidAsync(bound, CancellationToken.None);
                if (byOid.Status != LookupStatus.Read)
                    throw new InvalidOperationException($"The contacts bound to oid {bound} could not be read ({byOid.Status}).");

                var decision = ContactBindingDecision.DecideBoundContact(byOid);
                oidResolvesHere = decision is { Action: BindingAction.ResolveByOid } && decision.ContactId == contactId;
                if (!oidResolvesHere)
                {
                    _logger.LogInformation(
                        "[GRANT-NOTIFY] Contact {ContactId}'s oid binding does not resolve to it alone ({DenyCode}); a user " +
                        "known only by that oid is not told.", contactId, decision?.DenyCode ?? "no contact");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] Could not read who contact {ContactId} represents after a grant on {RootType} {RootId}; " +
                "nobody was notified. The grant stands.", contactId, rootType, rootId);
            return GrantNotificationOutcome.Failed;
        }

        // A contact grant reaches only a record that is not Restricted (the grant policy refuses it otherwise), so the
        // external flag bars nobody here; the person test is the share rule's.
        // The link is honoured as it is (the read path's primary rule); the oid only where it resolves here alone.
        var people = candidates
            .Where(u => u.SystemUserId != Guid.Empty
                        && u.Represents(contactId, oidResolvesHere ? boundOid : null)
                        && InternalShareEndpoints.ClassifyEligibility(
                               u.IsDisabled, u.AccessMode, u.ApplicationId, u.IsExternal, rootIsRestricted: false)
                           == InternalShareEndpoints.ShareEligibility.Eligible)
            .Select(u => u.SystemUserId)
            .Distinct()
            .ToList();

        if (people.Count == 0)
        {
            // Task 036: a contact that no internal user can be said to represent (not by link, not by its oid, even where the
            // oid does not resolve to it alone) is an external contact. Nobody else is told, so the email is the only notice.
            if (readContact is not null
                && !candidates.Any(u => u.SystemUserId != Guid.Empty && u.Represents(contactId, boundOid)))
            {
                return await EmailExternalContactGrantAsync(rootType, rootId, readContact, caller);
            }

            return GrantNotificationOutcome.NotApplicable;
        }

        // The contact grant checked the contact's No Access entries, not the user's. On a secure record a walled user is
        // vetoed at read, so they must not be told the record's name either; an unanswerable check tells nobody (ADR-003).
        var recipients = new List<Guid>();
        var failed = false;
        foreach (var userId in people)
        {
            SecureShareWallOutcome wall;
            try
            {
                wall = (await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                    ExternalGrantRoot.LogicalNameFor(rootType), rootId, userId, SecureWallRecordScope.AsFlagged,
                    CancellationToken.None)).Outcome;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[GRANT-NOTIFY] The No Access check for user {UserId} on {RootType} {RootId} threw; they were not notified.",
                    userId, rootType, rootId);
                wall = SecureShareWallOutcome.Unverifiable;
            }

            switch (wall)
            {
                case SecureShareWallOutcome.NotSecure:
                case SecureShareWallOutcome.NotWalled:
                    recipients.Add(userId);
                    break;
                case SecureShareWallOutcome.Walled:
                    _logger.LogInformation(
                        "[GRANT-NOTIFY] User {UserId} is on the No Access list of {RootType} {RootId}; not notified.",
                        userId, rootType, rootId);
                    break;
                default:
                    _logger.LogWarning(
                        "[GRANT-NOTIFY] Whether user {UserId} is on the No Access list of {RootType} {RootId} could not be " +
                        "checked; not notified.", userId, rootType, rootId);
                    failed = true;
                    break;
            }
        }

        return await SendAsync(rootType, rootId, recipients, level, caller, failed);
    }

    // =========================================================================
    // External contacts (task 036, FR-07): email with a portal deep link
    // =========================================================================

    /// <summary>
    /// The grant gave the already-read, active, ONLY-external contact <paramref name="contact"/> access to a record: emails
    /// it, once, when it is already onboarded (a readable identity binding) and has an email address. Never the granter
    /// (a grantee whose sign-in oid is the caller's own is skipped). Best effort: nothing throws.
    /// </summary>
    private async Task<GrantNotificationOutcome> EmailExternalContactGrantAsync(
        ExternalGrantRootType rootType, Guid rootId, ContactBindingRow contact, ClaimsPrincipal caller)
    {
        try
        {
            if (!IsOnboardedWithEmail(contact))
            {
                _logger.LogInformation(
                    "[GRANT-NOTIFY] Contact {ContactId} is not onboarded (or has no email address); no email for the grant on " +
                    "{RootType} {RootId}. The invite path emails a contact it onboards.", contact.ContactId, rootType, rootId);
                return GrantNotificationOutcome.NotApplicable;
            }

            // Never the person who gave the access: a self-grant, or a caller whose sign-in is the grantee's.
            var callerOid = CallerResolution.ResolveObjectId(caller);
            if (contact.Binding.Oid is { } boundOid
                && Guid.TryParse(callerOid, out var granterOid) && granterOid == boundOid)
            {
                _logger.LogInformation(
                    "[GRANT-NOTIFY] Contact {ContactId} is the caller; no email for the grant on {RootType} {RootId}.",
                    contact.ContactId, rootType, rootId);
                return GrantNotificationOutcome.NotApplicable;
            }

            var link = TryBuildLink(rootType, rootId);
            if (link is null)
                return GrantNotificationOutcome.Failed;

            var label = ExternalGrantRoot.LabelFor(rootType);
            var name = await ReadRecordNameAsync(rootType, rootId);
            var subject = name is null
                ? $"You were given access to a {label} in the Spaarke portal"
                : $"You were given access to the {label} \"{name}\" in the Spaarke portal";
            var headline = name is null
                ? $"You were given access to a {label}"
                : $"You were given access to the {label} \"{name}\"";
            // The granter is named only by the token's display `name` claim: an external party is never shown a UPN or an
            // email address (task 036 review K3). With no usable name the sentence simply has no subject.
            var message = ExternalGranterName(caller) is { } granter
                ? $"{granter} gave you access in the Spaarke portal. Open it with the button below."
                : "You were given access in the Spaarke portal. Open it with the button below.";

            return await TrySendAsync(
                contact, subject, headline, message, link, $"Open the {label}",
                correlationId: $"portal-grant-{rootId:N}-{contact.ContactId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                what: $"the grant on {rootType} {rootId}")
                ? GrantNotificationOutcome.Sent
                : GrantNotificationOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] Could not email contact {ContactId} about the grant on {RootType} {RootId}. The grant stands.",
                contact.ContactId, rootType, rootId);
            return GrantNotificationOutcome.Failed;
        }
    }

    /// <summary>An already-onboarded contact: a readable identity binding, and an email address to send to.</summary>
    /// <remarks>
    /// A binding on the WORKFORCE plane is an employee's work identity, not an external contact's CIAM account: it is never
    /// emailed as one (task 036 review K4). An oid with no plane marker is a pre-141 CIAM binding (<see cref="ContactBindingDecision.ReadBinding"/>).
    /// A WorkforceHome binding (ADR-028 A9, r3 task 047) IS emailed: that contact registered through, and signs in to, the
    /// same external portal the email links to.
    /// </remarks>
    private static bool IsOnboardedWithEmail(ContactBindingRow contact)
    {
        var binding = contact.Binding;
        return binding.Kind == BindingKind.Bound
               && binding.Plane != IdentityPlaneMarker.Workforce
               && !string.IsNullOrWhiteSpace(contact.Email);
    }

    /// <summary>
    /// One send. <c>false</c> (logged, never thrown) when the shared mailbox refuses the sender or the send fails — an
    /// unconfigured or unapproved sender is skipped, and the mail is never sent as the user.
    /// </summary>
    private async Task<bool> TrySendAsync(
        ContactBindingRow contact, string subject, string headline, string message, string link, string linkText,
        string correlationId, string what)
    {
        // Bounded (task 036 review K2). The notifier has no request token by design (the person is told even if the granter's
        // browser has gone), so the only cancellation here is this timeout.
        using var timeout = new CancellationTokenSource(SendTimeout);
        try
        {
            await _emails.SendPortalNotificationEmailAsync(
                contact.Email!.Trim(), subject, headline, message, link, linkText, correlationId, timeout.Token);
            _logger.LogInformation(
                "[GRANT-NOTIFY] Emailed contact {ContactId} about {What} ({CorrelationId}).", contact.ContactId, what, correlationId);
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[GRANT-NOTIFY] The email to contact {ContactId} about {What} did not finish within {Seconds}s; given up. The " +
                "grant stands.", contact.ContactId, what, SendTimeout.TotalSeconds);
            return false;
        }
        catch (SdapProblemException ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] The email to contact {ContactId} about {What} was refused ({Code}); skipped. A stamp whose " +
                "sender is not configured or approved sends nothing.", contact.ContactId, what, ex.Code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] Could not email contact {ContactId} about {What}; skipped.", contact.ContactId, what);
            return false;
        }
    }

    /// <summary>
    /// The portal deep link for a record from server configuration, or <c>null</c> (logged) when the portal URL or this
    /// stamp's customer key is not configured. See <see cref="BuildPortalLink"/>.
    /// </summary>
    private string? TryBuildLink(ExternalGrantRootType rootType, Guid rootId)
    {
        var customerKey = _customer.IsResolved ? _customer.Id : null;
        var link = BuildPortalLink(_configuration["ExternalAccess:PortalUrl"], customerKey, rootType, rootId);
        if (link is null)
        {
            _logger.LogWarning(
                "[GRANT-NOTIFY] The portal link for {RootType} {RootId} could not be built: ExternalAccess:PortalUrl must be an " +
                "https URL and this stamp's customer id (Customer:Id) must be resolved. No email was sent.", rootType, rootId);
        }

        return link;
    }

    /// <summary>
    /// <c>{PortalUrl}/?customer={key}&amp;record={type}:{id}</c> (spec FR-07). Built ONLY from the trusted portal URL, this
    /// stamp's customer key and the record's type token and id; each query value is URL-encoded, and the link carries no
    /// BFF URL, scope or token (NFR-02: a link names a customer key and a record, never a backend). The portal URL must be
    /// an absolute https URL (http only on loopback) with no credentials; its own query and fragment are dropped.
    /// <c>null</c> when the portal URL or the key is unusable.
    /// </summary>
    internal static string? BuildPortalLink(string? portalUrl, string? customerKey, ExternalGrantRootType rootType, Guid rootId)
    {
        if (PortalBase(portalUrl, customerKey, rootId) is not { } baseUrl)
        {
            return null;
        }

        var token = rootType switch
        {
            ExternalGrantRootType.Project => "project",
            ExternalGrantRootType.Matter => "matter",
            ExternalGrantRootType.WorkAssignment => "workassignment",
            _ => throw new ArgumentOutOfRangeException(nameof(rootType), rootType, "Unknown external grant root type."),
        };

        return ComposePortalLink(baseUrl, customerKey!, token, rootId);
    }

    /// <summary>
    /// The portal's own <c>?record={type}</c> tokens: exactly <c>RECORD_LINK_TOKENS</c> in the external SPA's
    /// <c>src/components/detail/record-targets.ts</c> (r3 task 055), whose parser is the authority. Any other token opens nothing there.
    /// </summary>
    internal static readonly IReadOnlySet<string> PortalRecordTokens =
        new HashSet<string>(StringComparer.Ordinal) { "project", "matter", "workassignment", "document", "invoice", "servicerequest" };

    /// <summary>
    /// The same link as the enum overload for a record the portal names by its type token (ontology task 160, D-181: the budget
    /// inquiry's portal link). The same portal URL and key rules apply; a token outside <see cref="PortalRecordTokens"/> (the
    /// portal would ignore it) gives <c>null</c>, never a link that opens nothing.
    /// </summary>
    internal static string? BuildPortalLink(string? portalUrl, string? customerKey, string recordToken, Guid rootId) =>
        recordToken is not null && PortalRecordTokens.Contains(recordToken) && PortalBase(portalUrl, customerKey, rootId) is { } baseUrl
            ? ComposePortalLink(baseUrl, customerKey!, recordToken, rootId)
            : null;

    /// <summary>The portal URL's origin and path when it, the key and the id are usable (the rules above); otherwise <c>null</c>.</summary>
    private static string? PortalBase(string? portalUrl, string? customerKey, Guid rootId)
    {
        if (string.IsNullOrWhiteSpace(customerKey) || rootId == Guid.Empty
            || !Uri.TryCreate(portalUrl?.Trim(), UriKind.Absolute, out var portal)
            || !string.IsNullOrEmpty(portal.UserInfo)
            || !(portal.Scheme == Uri.UriSchemeHttps || (portal.Scheme == Uri.UriSchemeHttp && portal.IsLoopback)))
        {
            return null;
        }

        return portal.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string ComposePortalLink(string baseUrl, string customerKey, string token, Guid rootId) =>
        $"{baseUrl}/?customer={Uri.EscapeDataString(customerKey.Trim())}" +
        $"&record={Uri.EscapeDataString(token)}:{Uri.EscapeDataString(rootId.ToString("D"))}";

    // =========================================================================
    // Task 185 slice E (§12 I-4, owner round 98b): a contact-side grant whose colleague needs an INTERNAL invite
    // =========================================================================

    /// <summary>
    /// A contact-side grant (task 140) gave <paramref name="contactId"/> — an unbound colleague with an email — access to a
    /// record. Contacts never invite, so the colleague cannot sign in until an internal user sends the invite: one internal
    /// user is told so, in-app, with a link to the record. Who (§12 I-4):
    /// <list type="number">
    /// <item>a user-owned record: its OWNING user, as before;</item>
    /// <item>a team-owned record (task 185 slice F; secure records are team-owned): the first of
    /// <paramref name="granters"/> — the internal users who granted the granting partner contact its own access to the
    /// record (<c>sprk_grantedby</c> on its grant rows) — that is an enabled person not walled off the record;</item>
    /// <item>else the record's <c>createdby</c>, when that is an enabled person (not an application user) not walled off;</item>
    /// <item>else nobody: logged at Warning.</item>
    /// </list>
    /// Never a user the record's No Access list walls off. Best effort, like everything here: nothing throws.
    /// </summary>
    public async Task<GrantNotificationOutcome> NotifyInviteNeededAsync(
        ExternalGrantRootType rootType, Guid rootId, Guid contactId, ClaimsPrincipal caller,
        IReadOnlyList<Guid>? granters = null)
    {
        try
        {
            var logical = ExternalGrantRoot.LogicalNameFor(rootType);
            var rows = await _dataverse.QueryAsync<OwnerRow>(
                ExternalGrantRoot.BindFor(rootType).EntitySet,
                filter: $"{logical}id eq {rootId}",
                select: "_owninguser_value,_createdby_value",
                top: 1,
                cancellationToken: CancellationToken.None);
            var record = rows.FirstOrDefault();

            Guid owner;
            string role;
            if (record?.OwningUser is { } owningUser && owningUser != Guid.Empty)
            {
                // A user-owned record: its owner, as before.
                switch (await CheckWallAsync(logical, rootType, rootId, owningUser, "owner"))
                {
                    case WallCheck.Clear:
                        break;
                    case WallCheck.Walled:
                        return GrantNotificationOutcome.NotApplicable;
                    default:
                        return GrantNotificationOutcome.Failed;
                }

                owner = owningUser;
                role = "owner";
            }
            else
            {
                // A team-owned record (task 185 slice F): the partner contact's own granter, else the record's creator.
                var candidates = (granters ?? Array.Empty<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Select(id => (Id: id, Role: "granter"))
                    .ToList();
                if (record?.CreatedBy is { } creator && creator != Guid.Empty)
                    candidates.Add((creator, "creator"));

                (Guid Id, string Role)? chosen = null;
                foreach (var candidate in candidates.DistinctBy(c => c.Id))
                {
                    if (!await IsPersonAsync(candidate.Id, candidate.Role, rootType, rootId))
                        continue;
                    if (await CheckWallAsync(logical, rootType, rootId, candidate.Id, candidate.Role) != WallCheck.Clear)
                        continue;
                    chosen = candidate;
                    break;
                }

                if (chosen is not { } found)
                {
                    _logger.LogWarning(
                        "[GRANT-NOTIFY] {RootType} {RootId} is team-owned and neither the partner contact's granter nor the " +
                        "record's creator is an internal person who can be told; nobody is told that contact {ContactId} needs " +
                        "an invite. The grant stands.", rootType, rootId, contactId);
                    return GrantNotificationOutcome.NotApplicable;
                }

                (owner, role) = found;
            }

            var label = ExternalGrantRoot.LabelFor(rootType);
            var name = await ReadRecordNameAsync(rootType, rootId);
            var who = await ReadContactNameAsync(contactId) ?? "A contact";
            var title = name is null
                ? $"{who} needs an invite to a {label}"
                : $"{who} needs an invite to the {label} \"{name}\"";
            var body = "A partner contact gave them access in the Spaarke portal. They cannot sign in until someone here " +
                       "sends them an invite: open Manage Access on the record and choose Invite.";

            await _notifications.CreateNotificationAsync(
                userId: owner,
                title: Sprk.Bff.Api.Infrastructure.Text.TextTruncation.TruncateSurrogateSafe(title, 250),
                body: body,
                category: Category,
                actionUrl: $"/main.aspx?etn={logical}&id={rootId:D}&pagetype=entityrecord",
                regardingId: rootId,
                cancellationToken: CancellationToken.None,
                actionTitle: $"Open {label}");

            _logger.LogInformation(
                "[GRANT-NOTIFY] Told the {Role} {UserId} of {RootType} {RootId} that contact {ContactId} needs an invite.",
                role, owner, rootType, rootId, contactId);
            return GrantNotificationOutcome.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] Could not tell anyone at {RootType} {RootId} that contact {ContactId} needs an invite. The " +
                "grant stands.", rootType, rootId, contactId);
            return GrantNotificationOutcome.Failed;
        }
    }

    private enum WallCheck
    {
        Clear,
        Walled,
        Unreadable,
    }

    /// <summary>Whether <paramref name="userId"/> may be named the record: not walled off it. Never throws.</summary>
    private async Task<WallCheck> CheckWallAsync(
        string logical, ExternalGrantRootType rootType, Guid rootId, Guid userId, string role)
    {
        SecureShareWallOutcome wall;
        try
        {
            wall = (await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                logical, rootId, userId, SecureWallRecordScope.AsFlagged, CancellationToken.None)).Outcome;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] The No Access check for {Role} {UserId} on {RootType} {RootId} threw; they were not told.",
                role, userId, rootType, rootId);
            return WallCheck.Unreadable;
        }

        switch (wall)
        {
            case SecureShareWallOutcome.NotSecure:
            case SecureShareWallOutcome.NotWalled:
                return WallCheck.Clear;
            case SecureShareWallOutcome.Walled:
                _logger.LogInformation(
                    "[GRANT-NOTIFY] {Role} {UserId} is on the No Access list of {RootType} {RootId}; not told.",
                    role, userId, rootType, rootId);
                return WallCheck.Walled;
            default:
                _logger.LogWarning(
                    "[GRANT-NOTIFY] Whether {Role} {UserId} is on the No Access list of {RootType} {RootId} could not be checked; " +
                    "not told.", role, userId, rootType, rootId);
                return WallCheck.Unreadable;
        }
    }

    /// <summary>
    /// Task 185 slice F: whether <paramref name="userId"/> is an enabled, interactive person — not an application user — by
    /// the one share-eligibility rule (<see cref="InternalShareEndpoints.ClassifyEligibility"/>; a contact grant reaches only
    /// a record that is not Restricted). A user that cannot be read is not one. Never throws.
    /// </summary>
    private async Task<bool> IsPersonAsync(Guid userId, string role, ExternalGrantRootType rootType, Guid rootId)
    {
        try
        {
            var user = await InternalShareEndpoints.ReadSystemUserAsync(_dataverse, userId, CancellationToken.None);
            if (user is not null
                && InternalShareEndpoints.ClassifyEligibility(
                       user.IsDisabled, user.AccessMode, user.ApplicationId, user.IsExternal, rootIsRestricted: false)
                   == InternalShareEndpoints.ShareEligibility.Eligible)
            {
                return true;
            }

            _logger.LogInformation(
                "[GRANT-NOTIFY] The {Role} {UserId} of {RootType} {RootId} is not an enabled internal person; not told.",
                role, userId, rootType, rootId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] The {Role} {UserId} of {RootType} {RootId} could not be read; not told.",
                role, userId, rootType, rootId);
            return false;
        }
    }

    /// <summary>The contact's full name, read app-only, for the owner's notification; <c>null</c> when empty or unreadable.</summary>
    private async Task<string?> ReadContactNameAsync(Guid contactId)
    {
        try
        {
            var row = await _dataverse.RetrieveAsync<Dictionary<string, JsonElement>>(
                "contacts", contactId, "fullname", CancellationToken.None);
            if (row is not null && row.TryGetValue("fullname", out var value) && value.ValueKind == JsonValueKind.String)
            {
                var full = value.GetString()?.Trim();
                if (!string.IsNullOrEmpty(full))
                    return Sprk.Bff.Api.Infrastructure.Text.TextTruncation.TruncateSurrogateSafe(full, MaxNameLength / 3);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GRANT-NOTIFY] Could not read the name of contact {ContactId}.", contactId);
        }

        return null;
    }

    /// <summary>The owner projection (a LOOKUP is <c>_x_value</c> in <c>$select</c> — FAILURE-MODES G-13).</summary>
    private sealed class OwnerRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("_owninguser_value")]
        public Guid? OwningUser { get; set; }

        /// <summary>Task 185 slice F: the record's creator, the last fallback for a team-owned record.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("_createdby_value")]
        public Guid? CreatedBy { get; set; }
    }

    /// <summary>
    /// After a share gave <paramref name="systemUserId"/> access: tells them. <c>/share-user</c> has already established
    /// that they are an enabled person and not walled off the record.
    /// </summary>
    public Task<GrantNotificationOutcome> NotifyUserShareAsync(
        ExternalGrantRootType rootType, Guid rootId, Guid systemUserId, ExternalAccessLevel? level, ClaimsPrincipal caller)
        => SendAsync(rootType, rootId, new[] { systemUserId }, level, caller, alreadyFailed: false);

    private async Task<GrantNotificationOutcome> SendAsync(
        ExternalGrantRootType rootType,
        Guid rootId,
        IReadOnlyCollection<Guid> candidates,
        ExternalAccessLevel? level,
        ClaimsPrincipal caller,
        bool alreadyFailed)
    {
        // Never the person who gave the access. When the caller's systemuser cannot be resolved nobody is excluded: the
        // worst case is a granter told about their own share.
        // ADR-028 A9 / task 046 guard (d): a WorkforceHome principal's oid is the CUSTOMER directory's, so it is never
        // compared with systemuser.azureactivedirectoryobjectid. No lookup, nobody excluded: a home caller is not one of
        // the systemuser recipients by oid.
        var granterId = WorkforcePrincipalResolver.IsWorkforceHomePrincipal(caller)
            ? null
            : await GrantExternalAccessEndpoint.ResolveGrantedBySystemUserIdAsync(
                _dataverse, CallerResolution.ResolveObjectId(caller), _logger, CancellationToken.None);
        var recipients = candidates
            .Where(id => id != Guid.Empty && !string.Equals(id.ToString(), granterId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (recipients.Count == 0)
            return alreadyFailed ? GrantNotificationOutcome.Failed : GrantNotificationOutcome.NotApplicable;

        var label = ExternalGrantRoot.LabelFor(rootType);
        var name = await ReadRecordNameAsync(rootType, rootId);
        var title = name is null
            ? $"You were given access to a {label}"
            : $"You were given access to the {label} \"{name}\"";
        var levelText = level is { } l ? $" {GrantPolicyDecision.DisplayName(l)}" : string.Empty;
        var body = $"{GranterName(caller)} gave you{levelText} access.";
        var logicalName = ExternalGrantRoot.LogicalNameFor(rootType);

        var failed = alreadyFailed;
        foreach (var userId in recipients)
        {
            try
            {
                await _notifications.CreateNotificationAsync(
                    userId: userId,
                    title: title,
                    body: body,
                    category: Category,
                    actionUrl: $"/main.aspx?etn={logicalName}&id={rootId:D}&pagetype=entityrecord",
                    regardingId: rootId,
                    cancellationToken: CancellationToken.None,
                    actionTitle: $"Open {label}");

                _logger.LogInformation(
                    "[GRANT-NOTIFY] Told user {UserId} they were given access to {RootType} {RootId}.", userId, rootType, rootId);
            }
            catch (Exception ex)
            {
                // NotificationService has logged the fault; this line says what it cost.
                _logger.LogWarning(ex,
                    "[GRANT-NOTIFY] Could not tell user {UserId} they were given access to {RootType} {RootId}. The grant stands.",
                    userId, rootType, rootId);
                failed = true;
            }
        }

        return failed ? GrantNotificationOutcome.Failed : GrantNotificationOutcome.Sent;
    }

    /// <summary>
    /// The record's display name, read app-only AFTER the write gave the recipient access to it. <c>null</c> when it is
    /// empty or cannot be read; the notification then says "a matter" and still links to it.
    /// </summary>
    private async Task<string?> ReadRecordNameAsync(ExternalGrantRootType rootType, Guid rootId)
    {
        var column = ExternalGrantRoot.NameColumnFor(rootType);
        try
        {
            var row = await _dataverse.RetrieveAsync<Dictionary<string, JsonElement>>(
                ExternalGrantRoot.BindFor(rootType).EntitySet, rootId, column, CancellationToken.None);
            if (row is not null && row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var name = value.GetString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                    return Sprk.Bff.Api.Infrastructure.Text.TextTruncation.TruncateSurrogateSafe(name, MaxNameLength);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GRANT-NOTIFY] Could not read the name of {RootType} {RootId}; the notification names the record type only.",
                rootType, rootId);
        }

        return null;
    }

    /// <summary>
    /// The granter as the EXTERNAL email may name them (task 036 review K3): the token's display <c>name</c> claim only —
    /// never <c>preferred_username</c>, the UPN or an email address, even when the name itself is shaped like one. <c>null</c>
    /// when there is none.
    /// </summary>
    internal static string? ExternalGranterName(ClaimsPrincipal? caller)
    {
        var name = caller?.FindFirst("name")?.Value?.Trim();
        return string.IsNullOrEmpty(name) || name.Contains('@') ? null : name;
    }

    /// <summary>The granter as their sign-in names them; "Someone" when the token carries no name. Internal recipients only.</summary>
    internal static string GranterName(ClaimsPrincipal? caller)
    {
        var name = caller?.FindFirst("name")?.Value ?? caller?.FindFirst("preferred_username")?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Someone" : name.Trim();
    }
}
