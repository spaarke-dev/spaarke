using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

// =====================================================================================================================
// unified-access-control-r2 task 193 (#2338) — the ONE answer to "which internal users does this contact represent?".
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: AssignedAccessMaterializer.ResolveTargetAsync, GrantAccessNotifier.NotifyContactGrantAsync and
//     PersonalToDoAssignee each re-implemented the contact -> systemuser rule over AssignedAccessStore.ReadLinkCandidatesAsync
//     and AssignedLinkCandidate.Represents, each slightly differently (only the notifier confirmed an oid-only user through
//     the binder's oid decision; the to-do assignee read the link alone and never read the contact).
//   Extension: this is that rule extracted from the materializer's code, with the notifier's oid confirmation added. The
//     three callers now call it and keep only their own count policy; no new Dataverse read, column or route.
//   Cost of doing nothing: Manage Access's /grant (task 193 step 3) would need a fourth copy to decide between a share and a
//     contact grant, and the copies already disagree on who a contact represents.
// =====================================================================================================================

/// <summary>
/// The ONE read of which systemusers a contact represents (task 141 §2/§5): a user whose link (<c>sprk_primarycontact</c>)
/// names the contact, or a user with NO link whose Entra oid the contact's binding carries — the oid only where the binder's
/// own oid decision (<see cref="ContactBindingDecision.DecideBoundContact"/>) resolves that oid to this contact alone, as the
/// read path does. Each represented user carries its share eligibility (<see cref="InternalShareEndpoints.ClassifyEligibility"/>)
/// for the caller's Restricted input. How many users the caller needs (exactly one, or every eligible one) is the caller's.
/// </summary>
/// <remarks>
/// <para><b>Status-first, fail closed.</b> A read that failed is <see cref="ContactRepresentationStatus.Unreadable"/>, never
/// "represents nobody": a caller must not turn an unreadable link into a contact grant to an internal user.</para>
/// <para><b>Task 193 corrections (deliberate, tested).</b> (1) The Assigned-To materializer now confirms an oid-only user
/// through the binder's oid decision, as the notifier did: a user known only by an oid that two contacts carry, or that an
/// inactive contact carries, is no longer this contact's. (2) The personal To Do assignee now reads the contact too: an
/// inactive contact represents nobody, and an oid-only user confirmed by the binding counts toward "exactly one".</para>
/// <para>Not cached; app-only reads through the injected stores. Exceptions from the contact read itself propagate (the store
/// is status-first and does not throw for a failed read); a fault in the link, oid or state read is reported as
/// <see cref="ContactRepresentationStatus.Unreadable"/> with the exception.</para>
/// </remarks>
public sealed class ContactUserRepresentation
{
    private readonly IContactIdentityStore _identities;
    private readonly AssignedAccessStore _links;
    private readonly ExternalParticipationService _participations;

    public ContactUserRepresentation(
        IContactIdentityStore identities, AssignedAccessStore links, ExternalParticipationService participations)
    {
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
    }

    /// <summary>
    /// Which systemusers <paramref name="contactId"/> represents, each with its share eligibility on a record whose Restricted
    /// state is <paramref name="rootIsRestricted"/> (<c>null</c> = not read: an external-flagged user is refused, fail closed).
    /// </summary>
    /// <param name="columnMissing">What a contact read whose binding column is not provisioned here means for this caller:
    /// <see cref="BindingColumnMissingPolicy.FailClosed"/> stops at once (status
    /// <see cref="ContactRepresentationStatus.ColumnMissing"/>, no users); <see cref="BindingColumnMissingPolicy.DecideOnLink"/>
    /// reads the contact's state and decides on the link alone — with no binding column no oid binding can exist, so the link
    /// set is complete.</param>
    internal async Task<ContactRepresentation> ResolveAsync(
        Guid contactId, bool? rootIsRestricted, BindingColumnMissingPolicy columnMissing, CancellationToken ct)
    {
        var contact = await _identities.GetContactAsync(contactId, ct).ConfigureAwait(false);

        ContactBindingRow? row = null;
        Guid? boundOid = null;
        switch (contact.Status)
        {
            case LookupStatus.Read:
                row = contact.Rows.FirstOrDefault(r => r.ContactId == contactId);
                if (row is null)
                    return ContactRepresentation.Of(ContactRepresentationStatus.NotFound);
                if (!row.IsActive)
                    return ContactRepresentation.Of(ContactRepresentationStatus.Inactive, row);
                boundOid = Guid.TryParse(row.RawOid?.Trim(), out var oid) && oid != Guid.Empty ? oid : null;
                break;

            case LookupStatus.ColumnMissing when columnMissing == BindingColumnMissingPolicy.DecideOnLink:
                // No binding column here, so no oid — but the contact must still be active. Read its state the read path's
                // way (statecode only): inactive represents nobody; unreadable is "could not tell".
                ContactRecordState state;
                try
                {
                    state = await _participations.ReadContactStateAsync(contactId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    return ContactRepresentation.Failed(ex, $"Contact {contactId}'s state could not be read.");
                }

                switch (state)
                {
                    case ContactRecordState.Active:
                        break;
                    case ContactRecordState.Inactive:
                        return ContactRepresentation.Of(ContactRepresentationStatus.Inactive);
                    default:
                        return ContactRepresentation.Failed(null, $"Contact {contactId}'s state could not be read.");
                }

                break;

            case LookupStatus.ColumnMissing:
                return ContactRepresentation.Of(ContactRepresentationStatus.ColumnMissing);

            default:
                return ContactRepresentation.Failed(null, $"Contact {contactId}'s binding could not be read ({contact.Status}).");
        }

        IReadOnlyList<AssignedLinkCandidate> candidates;
        string? oidUnconfirmed = null;
        var oidResolvesHere = false;
        try
        {
            candidates = await _links.ReadLinkCandidatesAsync(contactId, boundOid, ct).ConfigureAwait(false)
                         ?? Array.Empty<AssignedLinkCandidate>();

            // A user with NO link is represented through the oid binding only where the READ path would resolve it: the
            // binder's own oid decision must answer "exactly one active contact carries the oid, and it is this one". Two
            // contacts on the oid, or an inactive one, resolve to no contact there, so the user is not this contact's here.
            if (boundOid is { } bound && candidates.Any(c => c.PrimaryContactId is null && c.Oid == bound))
            {
                var byOid = await _identities.FindContactsByOidAsync(bound, ct).ConfigureAwait(false);
                if (byOid.Status != LookupStatus.Read)
                    return ContactRepresentation.Failed(null, $"The contacts bound to oid {bound} could not be read ({byOid.Status}).");

                var decision = ContactBindingDecision.DecideBoundContact(byOid);
                oidResolvesHere = decision is { Action: BindingAction.ResolveByOid } && decision.ContactId == contactId;
                if (!oidResolvesHere)
                    oidUnconfirmed = decision?.DenyCode ?? "no contact";
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return ContactRepresentation.Failed(ex, $"The systemusers contact {contactId} represents could not be read.");
        }

        // The link is honoured as it is (task 141 §2/§5: a user linked to ANOTHER contact is not this contact's, whatever the
        // oid says); the oid only where it resolves here alone.
        var confirmedOid = oidResolvesHere ? boundOid : null;
        var users = candidates
            .Where(c => c.SystemUserId != Guid.Empty && c.Represents(contactId, confirmedOid))
            .GroupBy(c => c.SystemUserId)
            .Select(g => g.First())
            .Select(c => new RepresentedUser(
                c,
                InternalShareEndpoints.ClassifyEligibility(c.IsDisabled, c.AccessMode, c.ApplicationId, c.IsExternal, rootIsRestricted),
                InternalShareEndpoints.IsBarredOnRestricted(c.IsExternal, rootIsRestricted)))
            .ToList();

        return new ContactRepresentation(
            contact.Status == LookupStatus.Read ? ContactRepresentationStatus.Read : ContactRepresentationStatus.ColumnMissing,
            row, users, oidUnconfirmed, null, null);
    }

    /// <summary>
    /// Task 193 G1 (owner decision 2): the REVERSE of <see cref="ResolveAsync"/> — the ONE contact that represents
    /// <paramref name="systemUserId"/>, for the paired workforce-app grant of a colleague picked directly (/share-user).
    /// </summary>
    /// <remarks>
    /// <para>Candidates are the user's own link (<c>sprk_primarycontact</c>) and every contact whose binding carries the user's
    /// oid. Each is kept only when the FORWARD rule (<see cref="ResolveAsync"/>) says it represents this user — so the two
    /// directions can never disagree. Exactly one kept candidate pairs; none is
    /// <see cref="UserContactStatus.None"/>; more than one is <see cref="UserContactStatus.Several"/> (never pick one of two).</para>
    /// <para>Status-first, fail closed: any read that fails is <see cref="UserContactStatus.Unreadable"/>, never "no contact".
    /// A missing binding column means no oid binding can exist, so the link alone decides (as <see cref="ResolveAsync"/>).</para>
    /// </remarks>
    internal async Task<UserContactResolution> ResolveContactForUserAsync(Guid systemUserId, CancellationToken ct)
    {
        var candidates = new List<Guid>();
        try
        {
            var user = await _identities.GetSystemUserAsync(systemUserId, ct).ConfigureAwait(false);
            if (user.Status != LookupStatus.Read || user.Row is not { } row || row.SystemUserId != systemUserId)
                return UserContactResolution.Failed(null, $"Systemuser {systemUserId}'s link could not be read ({user.Status}).");

            if (row.PrimaryContactId is { } linked && linked != Guid.Empty)
                candidates.Add(linked);

            if (row.Oid is { } oid && oid != Guid.Empty)
            {
                var byOid = await _identities.FindContactsByOidAsync(oid, ct).ConfigureAwait(false);
                switch (byOid.Status)
                {
                    case LookupStatus.Read:
                        candidates.AddRange(byOid.Rows.Select(r => r.ContactId).Where(id => id != Guid.Empty));
                        break;
                    case LookupStatus.ColumnMissing:
                        break; // no binding column: no contact can carry the oid
                    default:
                        return UserContactResolution.Failed(null, $"The contacts bound to systemuser {systemUserId}'s oid could not be read ({byOid.Status}).");
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return UserContactResolution.Failed(ex, $"The contact systemuser {systemUserId} is linked to could not be read.");
        }

        var kept = new List<(Guid ContactId, int Represents)>();
        foreach (var contactId in candidates.Distinct())
        {
            ContactRepresentation forward;
            try
            {
                forward = await ResolveAsync(contactId, rootIsRestricted: null, BindingColumnMissingPolicy.DecideOnLink, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return UserContactResolution.Failed(ex, $"Who contact {contactId} represents could not be read.");
            }

            switch (forward.Status)
            {
                case ContactRepresentationStatus.Read:
                case ContactRepresentationStatus.ColumnMissing:
                    if (forward.Users.Any(u => u.SystemUserId == systemUserId))
                        kept.Add((contactId, forward.Users.Count));
                    break;
                case ContactRepresentationStatus.NotFound:
                case ContactRepresentationStatus.Inactive:
                    break; // represents nobody
                default:
                    return UserContactResolution.Failed(forward.Fault, forward.FaultDetail ?? $"Who contact {contactId} represents could not be read.");
            }
        }

        return kept.Count switch
        {
            0 => new UserContactResolution(UserContactStatus.None, null, false, null, null),
            1 => new UserContactResolution(UserContactStatus.One, kept[0].ContactId, kept[0].Represents > 1, null, null),
            _ => new UserContactResolution(UserContactStatus.Several, null, false, null, null),
        };
    }
}

/// <summary>How <see cref="ContactUserRepresentation.ResolveContactForUserAsync"/> ended.</summary>
internal enum UserContactStatus
{
    /// <summary>Exactly one contact represents the user: <see cref="UserContactResolution.ContactId"/>.</summary>
    One,

    /// <summary>No contact represents the user.</summary>
    None,

    /// <summary>More than one contact represents the user: none is picked.</summary>
    Several,

    /// <summary>A read failed: which contact represents the user is not known. Never "none".</summary>
    Unreadable,
}

/// <summary>The answer of <see cref="ContactUserRepresentation.ResolveContactForUserAsync"/>.</summary>
/// <param name="ContactId">The one contact (<see cref="UserContactStatus.One"/>), else <c>null</c>.</param>
/// <param name="ContactRepresentsOthers">The one contact ALSO represents another user — the forward rule (/grant) refuses such
/// a contact, so it is not paired either.</param>
internal sealed record UserContactResolution(
    UserContactStatus Status, Guid? ContactId, bool ContactRepresentsOthers, Exception? Fault, string? FaultDetail)
{
    internal static UserContactResolution Failed(Exception? fault, string detail)
        => new(UserContactStatus.Unreadable, null, false, fault, detail);
}

/// <summary>What a contact read with no binding column (<see cref="LookupStatus.ColumnMissing"/>) means for a caller.</summary>
internal enum BindingColumnMissingPolicy
{
    /// <summary>Stop: report <see cref="ContactRepresentationStatus.ColumnMissing"/> and decide nothing (the materializer).</summary>
    FailClosed,

    /// <summary>Read the contact's state and decide on the link alone (the notifier, the to-do assignee, /grant).</summary>
    DecideOnLink,
}

/// <summary>How <see cref="ContactUserRepresentation.ResolveAsync"/> ended.</summary>
internal enum ContactRepresentationStatus
{
    /// <summary>The contact and its users were read; <see cref="ContactRepresentation.Users"/> is the answer.</summary>
    Read,

    /// <summary>The binding column is not provisioned here. Under <see cref="BindingColumnMissingPolicy.DecideOnLink"/> the
    /// contact is active and <see cref="ContactRepresentation.Users"/> was decided on the link alone; under
    /// <see cref="BindingColumnMissingPolicy.FailClosed"/> nothing was decided.</summary>
    ColumnMissing,

    /// <summary>A read failed: who the contact represents is not known. Never "represents nobody".</summary>
    Unreadable,

    /// <summary>No such contact.</summary>
    NotFound,

    /// <summary>The contact is inactive: it represents nobody (it resolves for no one on the read path, ADR-003).</summary>
    Inactive,
}

/// <summary>A systemuser the contact represents, with its share eligibility on the caller's record.</summary>
/// <param name="BarredOnRestricted"><see cref="InternalShareEndpoints.IsBarredOnRestricted"/> for the caller's Restricted input.</param>
internal sealed record RepresentedUser(
    AssignedLinkCandidate User, InternalShareEndpoints.ShareEligibility Eligibility, bool BarredOnRestricted)
{
    public Guid SystemUserId => User.SystemUserId;
}

/// <summary>The answer of <see cref="ContactUserRepresentation.ResolveAsync"/>.</summary>
/// <param name="Contact">The contact row as read (a <see cref="ContactRepresentationStatus.Read"/> answer), else <c>null</c>.</param>
/// <param name="Users">The represented users, one per systemuser; empty unless the status decided them.</param>
/// <param name="OidUnconfirmed">Set when a user with no link carries the contact's bound oid but the binding does not resolve that
/// oid to this contact alone (the binder's deny code): that user is NOT in <paramref name="Users"/>, yet the contact cannot be
/// called external either.</param>
/// <param name="Fault">The exception behind an <see cref="ContactRepresentationStatus.Unreadable"/> answer, when there was one.</param>
/// <param name="FaultDetail">Why an <see cref="ContactRepresentationStatus.Unreadable"/> answer could not tell.</param>
internal sealed record ContactRepresentation(
    ContactRepresentationStatus Status,
    ContactBindingRow? Contact,
    IReadOnlyList<RepresentedUser> Users,
    string? OidUnconfirmed,
    Exception? Fault,
    string? FaultDetail)
{
    internal static ContactRepresentation Of(ContactRepresentationStatus status, ContactBindingRow? contact = null)
        => new(status, contact, Array.Empty<RepresentedUser>(), null, null, null);

    internal static ContactRepresentation Failed(Exception? fault, string detail)
        => new(ContactRepresentationStatus.Unreadable, null, Array.Empty<RepresentedUser>(), null, fault, detail);
}
