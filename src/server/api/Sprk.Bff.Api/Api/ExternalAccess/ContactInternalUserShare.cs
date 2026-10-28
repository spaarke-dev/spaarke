using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

// =====================================================================================================================
// unified-access-control-r2 task 193 (#2338) steps 3-4 — Manage Access's ONE "add person" entry: when staff add a CONTACT
// that represents an internal user, the SERVER shares the record with that user (model-driven-app access), instead of
// writing a contact grant that gives the internal user SPA-only access.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: ContactUserRepresentation answers who a contact represents; InternalUserShareCore writes an internal-user
//     share. Neither decides between a share and a grant, and /grant and /invite-and-grant each need that decision.
//   Extension: neither can host it — the resolver has no HTTP answer and the share core knows nothing of contacts. This is
//     only their composition plus the decision matrix, in one place both routes call.
//   Cost of doing nothing: a contact representing an internal colleague keeps getting a contact grant (SPA-only access, the
//     training trap the owner asked to remove), or the matrix is copied into both routes and drifts.
// =====================================================================================================================

/// <summary>
/// The decision for a contact named as a grantee (task 193 step 3): a contact that represents exactly one internal user is
/// SHARED through <see cref="InternalUserShareCore"/>; one that represents several is refused; one whose representation
/// cannot be read is refused (never a contact grant — an unreadable link may hide an internal user); one that represents
/// nobody is left to the caller's grant path, unchanged.
/// </summary>
/// <remarks>
/// <para><b>Authorization</b> is the calling route's: <c>/grant</c> and <c>/invite-and-grant</c> run behind the delegation
/// filter (Write + Share as the caller). The share core re-probes the caller's rights (the equalised gate question) only to
/// cap the level.</para>
/// <para><b>Who counts</b>: every user the contact represents (<see cref="ContactUserRepresentation"/>, the link or a confirmed
/// oid binding), eligible or not — "never pick one of two", as the Assigned-To materializer. A single represented user who
/// is disabled, not a person, or flagged external on a Restricted record is refused by the share core with its own code
/// (<c>user_disabled</c>, <c>user_not_a_person</c>, <c>user_not_internal</c>); nothing is written.</para>
/// <para><b>Expiry</b>: a share has no end date, exactly as when the colleague is picked directly as a user (/share-user;
/// internal shares were never under the record Expiration, which <c>set-record-share-expiry</c> applies to external shares
/// only). A request's expiry is therefore not applied to the share, and the calling route says so
/// (<c>expiryNotApplied: true</c>) whenever the request carried one: never refused, never dropped silently.</para>
/// <para>A missing binding column decides on the link alone (<see cref="BindingColumnMissingPolicy.DecideOnLink"/>): with no
/// binding column no oid binding can exist, so the link set is complete.</para>
/// </remarks>
public sealed class ContactInternalUserShare
{
    /// <summary>409: the contact represents more than one internal user, so neither is picked. Nothing written.</summary>
    internal const string RepresentsSeveralUsersReasonCode = "sdap.access.grant.contact_represents_several_users";

    /// <summary>503: who the contact represents could not be read. Nothing written — never a contact grant instead.</summary>
    internal const string RepresentationUnreadableReasonCode = "sdap.access.grant.contact_representation_unreadable";

    private readonly ContactUserRepresentation _people;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly ITenantCache _cache;
    private readonly SecureChildShareSynchronizer _secureChildShares;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly SecureRootInheritance _relatedRoots;
    private readonly IOptionsMonitor<WorkforceIdentityOptions>? _workforceIdentity;

    public ContactInternalUserShare(
        ContactUserRepresentation people,
        IDataverseRecordShareService recordShare,
        ITenantCache cache,
        SecureChildShareSynchronizer secureChildShares,
        SecureShareNoAccessGuard noAccessGuard,
        SecureRootInheritance relatedRoots,
        IOptionsMonitor<WorkforceIdentityOptions>? workforceIdentity = null)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _recordShare = recordShare ?? throw new ArgumentNullException(nameof(recordShare));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _secureChildShares = secureChildShares ?? throw new ArgumentNullException(nameof(secureChildShares));
        _noAccessGuard = noAccessGuard ?? throw new ArgumentNullException(nameof(noAccessGuard));
        _relatedRoots = relatedRoots ?? throw new ArgumentNullException(nameof(relatedRoots));
        _workforceIdentity = workforceIdentity;
    }

    /// <summary>
    /// Decides <paramref name="contactId"/> on <paramref name="rootType"/> <paramref name="rootId"/> at
    /// <paramref name="level"/>. <see cref="ContactShareDecision.GrantPath"/> means the contact represents nobody: the caller
    /// goes on with its grant, unchanged. Anything else is the answer, and no grant row may be written.
    /// </summary>
    internal async Task<ContactShareDecision> DecideAsync(
        Guid contactId,
        ExternalGrantRootType rootType,
        Guid rootId,
        ExternalAccessLevel level,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        CallerRecordAccessProbe callerAccessProbe,
        AssignedAccessMaterializer assignedAccess,
        GrantAccessNotifier accessNotifier,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        ContactRepresentation representation;
        try
        {
            // rootIsRestricted is not needed to COUNT (every represented user counts); the share core reads the record's
            // flags itself for an external-flagged user.
            representation = await _people.ResolveAsync(
                contactId, rootIsRestricted: null, BindingColumnMissingPolicy.DecideOnLink, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            representation = ContactRepresentation.Failed(ex, $"Contact {contactId}'s binding could not be read.");
        }

        switch (representation.Status)
        {
            case ContactRepresentationStatus.Read:
            case ContactRepresentationStatus.ColumnMissing:
                break;
            case ContactRepresentationStatus.NotFound:
            case ContactRepresentationStatus.Inactive:
                // Not a person anyone is represented by: the grant path answers it (its own not-found / inactive checks).
                return ContactShareDecision.GrantPath;
            default:
                logger.LogError(representation.Fault,
                    "[CONTACT-SHARE] Who contact {ContactId} represents could not be read ({Detail}); nothing was written on " +
                    "{RootType} {RootId}.", contactId, representation.FaultDetail, rootType, rootId);
                return ContactShareDecision.Answered(Refused(httpContext, StatusCodes.Status503ServiceUnavailable,
                    "Access settings unavailable", RepresentationUnreadableReasonCode,
                    "Whether this contact is one of your colleagues could not be checked, so nothing was given. Try again."));
        }

        var users = representation.Users;
        if (users.Count == 0)
            return ContactShareDecision.GrantPath;

        if (users.Count > 1)
        {
            logger.LogWarning(
                "[CONTACT-SHARE] Contact {ContactId} represents {Count} internal users; nothing was written on {RootType} {RootId}.",
                contactId, users.Count, rootType, rootId);
            return ContactShareDecision.Answered(Refused(httpContext, StatusCodes.Status409Conflict,
                "Contact represents several users", RepresentsSeveralUsersReasonCode,
                "This contact is linked to more than one internal user, so nothing was given. Add the colleague you mean " +
                "directly."));
        }

        var userId = users[0].SystemUserId;
        logger.LogInformation(
            "[CONTACT-SHARE] Contact {ContactId} represents internal user {SystemUserId}; sharing {RootType} {RootId} with " +
            "them instead of a contact grant.", contactId, userId, rootType, rootId);

        var shared = await InternalUserShareCore.ShareAsync(
            new ShareRecordWithUserRequest(rootType.ToString(), rootId, userId, level),
            _recordShare, dataverseClient, participations, _cache, callerAccessProbe, _secureChildShares, _noAccessGuard,
            _relatedRoots, assignedAccess, accessNotifier, httpContext, logger, ct, _workforceIdentity).ConfigureAwait(false);

        if (shared.Shared is not { } confirmed)
            return ContactShareDecision.Answered(shared.Result); // the share core's refusal, as /share-user answers it

        // The contact's Assigned-To suggestions on this record are answered by this share: Adopted, stamped with the user,
        // so they are no longer offered (the share core adopts by user only, and a suggestion written before the contact
        // was linked carries none). Ledger-only, after the confirmed share; never thrown, and a failed write keeps the share.
        await assignedAccess.MarkContactShareAdoptedAsync(rootType, rootId, contactId, userId, CancellationToken.None)
            .ConfigureAwait(false);

        // Display only, AFTER the confirmed share: a name that cannot be read is null, never a failure of the share.
        string? fullName = null;
        try
        {
            var names = await InternalShareEndpoints.ReadNamesAsync(dataverseClient, new[] { userId }, logger, ct)
                .ConfigureAwait(false);
            fullName = names.TryGetValue(userId, out var row) ? row.FullName : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[CONTACT-SHARE] {SystemUserId} was shared {RootType} {RootId}; their name could not be read for the answer.",
                userId, rootType, rootId);
        }

        return ContactShareDecision.SharedWith(new SharedWithUser(userId, fullName), confirmed.Response);
    }

    private static IResult Refused(HttpContext httpContext, int statusCode, string title, string reasonCode, string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = reasonCode,
            });
}

/// <summary>What <see cref="ContactInternalUserShare.DecideAsync"/> decided.</summary>
/// <param name="Result">The refusal to answer with (nothing written); <c>null</c> for the grant path or a confirmed share.</param>
/// <param name="Shared">The internal user the record was confirmed shared with; <c>null</c> otherwise.</param>
/// <param name="ShareResponse">The share core's confirmed answer, with <paramref name="Shared"/>.</param>
internal sealed record ContactShareDecision(IResult? Result, SharedWithUser? Shared, ShareRecordWithUserResponse? ShareResponse)
{
    /// <summary>The contact represents nobody: the caller's grant path goes on, unchanged.</summary>
    public static ContactShareDecision GrantPath { get; } = new(null, null, null);

    public bool IsGrantPath => Result is null && Shared is null;

    public static ContactShareDecision Answered(IResult result) => new(result, null, null);

    public static ContactShareDecision SharedWith(SharedWithUser user, ShareRecordWithUserResponse response) =>
        new(null, user, response);
}
