using System.Security.Claims;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>What one grant-notification attempt did (task 181).</summary>
internal enum GrantNotificationOutcome
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
/// access the person did not already hold.
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
/// </remarks>
internal sealed class GrantAccessNotifier
{
    /// <summary>The <c>appnotification</c> category (the Daily Briefing groups by it).</summary>
    internal const string Category = "access";

    /// <summary>A record name longer than this is cut, so the title stays inside <c>appnotification.title</c>'s 256.</summary>
    internal const int MaxNameLength = 180;

    private readonly NotificationService _notifications;
    private readonly DataverseWebApiClient _dataverse;
    private readonly IContactIdentityStore _identities;
    private readonly AssignedAccessStore _links;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly ExternalParticipationService _participations;
    private readonly ILogger<GrantAccessNotifier> _logger;

    public GrantAccessNotifier(
        NotificationService notifications,
        DataverseWebApiClient dataverse,
        IContactIdentityStore identities,
        AssignedAccessStore links,
        SecureShareNoAccessGuard noAccessGuard,
        ExternalParticipationService participations,
        ILogger<GrantAccessNotifier> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _noAccessGuard = noAccessGuard ?? throw new ArgumentNullException(nameof(noAccessGuard));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// After a grant to <paramref name="contactId"/> gave it access: tells every enabled person the contact represents.
    /// An external contact represents nobody, so nothing is sent (its CIAM invitation is the external path's).
    /// </summary>
    public async Task<GrantNotificationOutcome> NotifyContactGrantAsync(
        ExternalGrantRootType rootType, Guid rootId, Guid contactId, ExternalAccessLevel? level, ClaimsPrincipal caller)
    {
        IReadOnlyList<AssignedLinkCandidate> candidates;
        Guid? boundOid = null;
        var oidResolvesHere = false;
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
            return GrantNotificationOutcome.NotApplicable;

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
        var granterId = await GrantExternalAccessEndpoint.ResolveGrantedBySystemUserIdAsync(
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

    /// <summary>The granter as their sign-in names them; "Someone" when the token carries no name.</summary>
    internal static string GranterName(ClaimsPrincipal? caller)
    {
        var name = caller?.FindFirst("name")?.Value ?? caller?.FindFirst("preferred_username")?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Someone" : name.Trim();
    }
}
