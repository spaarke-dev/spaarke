using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
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

    // Task 193 G1: why a shared colleague's workforce-app grant was NOT written (WorkforceAppAccess.Reason). The share stands
    // in every case; the grant core's own refusal codes (record_restricted, policy_unreadable, ...) are passed on as they are.

    /// <summary>No contact represents the colleague (/share-user), so there is no contact to grant.</summary>
    internal const string WorkforceNoLinkedContactReasonCode = "sdap.access.share.workforce_no_linked_contact";

    /// <summary>More than one contact represents the colleague (/share-user): none is picked.</summary>
    internal const string WorkforceSeveralContactsReasonCode = "sdap.access.share.workforce_several_linked_contacts";

    /// <summary>Which contact represents the colleague could not be read (/share-user).</summary>
    internal const string WorkforceLinkUnreadableReasonCode = "sdap.access.share.workforce_link_unreadable";

    /// <summary><c>sprk_pairedsystemuser</c> is not provisioned in this environment: no grant is written unpaired.</summary>
    internal const string WorkforcePairingColumnMissingReasonCode = "sdap.access.share.workforce_pairing_column_missing";

    /// <summary>The existing grants' pairing could not be read: no grant is written.</summary>
    internal const string WorkforcePairingUnreadableReasonCode = "sdap.access.share.workforce_pairing_unreadable";

    /// <summary>The grant write failed (or confers nothing); the share stands.</summary>
    internal const string WorkforceGrantFailedReasonCode = "sdap.access.share.workforce_grant_failed";

    private readonly ContactUserRepresentation _people;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly ITenantCache _cache;
    private readonly SecureChildShareSynchronizer _secureChildShares;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly SecureRootInheritance _relatedRoots;
    private readonly IOptionsMonitor<WorkforceIdentityOptions>? _workforceIdentity;
    private readonly IContactIdentityStore _identities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IDataversePrivilegeChecker _ownerPrivileges;
    private readonly TimeProvider _time;

    public ContactInternalUserShare(
        ContactUserRepresentation people,
        IDataverseRecordShareService recordShare,
        ITenantCache cache,
        SecureChildShareSynchronizer secureChildShares,
        SecureShareNoAccessGuard noAccessGuard,
        SecureRootInheritance relatedRoots,
        IContactIdentityStore identities,
        IRecordOwnershipResolver ownership,
        IDataversePrivilegeChecker ownerPrivileges,
        TimeProvider time,
        IOptionsMonitor<WorkforceIdentityOptions>? workforceIdentity = null)
    {
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _ownerPrivileges = ownerPrivileges ?? throw new ArgumentNullException(nameof(ownerPrivileges));
        _time = time ?? throw new ArgumentNullException(nameof(time));
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
        IAccessibleRecordSetService accessibleRecords,
        Guid? firmOrganizationId,
        DateOnly? expiryDate,
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

        // The record's No Access list, on EVERY record (owner ruling, V-fix item 3): staff named this contact, so an entry
        // naming the contact, its organizations or the firm the request names refuses exactly as /grant did before task
        // 193 (422 grantee_denied; a check that cannot be completed refuses 503), and nothing is written. A user picked
        // directly keeps the share core's rule (the wall applies on secure records only).
        if (await NoAccessRefusalAsync(accessibleRecords, contactId, firmOrganizationId, rootType, rootId, httpContext, logger, ct)
                .ConfigureAwait(false) is { } refused)
            return ContactShareDecision.Answered(refused);

        logger.LogInformation(
            "[CONTACT-SHARE] Contact {ContactId} represents internal user {SystemUserId}; sharing {RootType} {RootId} with " +
            "them instead of a contact grant.", contactId, userId, rootType, rootId);

        var shared = await InternalUserShareCore.ShareAsync(
            new ShareRecordWithUserRequest(rootType.ToString(), rootId, userId, level),
            _recordShare, dataverseClient, participations, _cache, callerAccessProbe, _secureChildShares, _noAccessGuard,
            _relatedRoots, assignedAccess, accessNotifier, httpContext, logger, ct, _workforceIdentity).ConfigureAwait(false);

        if (shared.Shared is not { } confirmed)
            return ContactShareDecision.Answered(shared.Result); // the share core's refusal, as /share-user answers it

        // From here the record's own share is in place: the 200, or children_incomplete (some related records are not
        // updated yet), which is answered below with who was shared with, as the 200 is.

        // The contact's Assigned-To suggestions on this record are answered by this share: Adopted, stamped with the user,
        // so they are no longer offered (the share core adopts by user only, and a suggestion written before the contact
        // was linked carries none). Ledger-only, after the confirmed share; never thrown, and a failed write keeps the share.
        await assignedAccess.MarkContactShareAdoptedAsync(rootType, rootId, contactId, userId, CancellationToken.None)
            .ConfigureAwait(false);

        // Task 193 G1 (owner F1-b): beside the share, the contact is granted (the colleague's workforce-app access, paired with
        // the share) through the grant core with all its checks. After the confirmed share; a grant that is refused or fails
        // leaves the share standing and is reported (workforceApp), never undoes it.
        var workforceApp = await GrantPairedAsync(
                contactId, userId, rootType, rootId, level, expiryDate, firmOrganizationId, dataverseClient, participations,
                accessibleRecords, callerAccessProbe, httpContext, logger, ct)
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

        var sharedWith = new SharedWithUser(userId, fullName);
        if (shared.ChildrenIncomplete)
        {
            // The children_incomplete ProblemDetails is the answer (the share stands); it says who was shared with, and that a
            // requested expiry was not applied, exactly as the 200 does.
            if (shared.Result is ProblemHttpResult { ProblemDetails: { } problem })
            {
                problem.Extensions["sharedWithUser"] = sharedWith;
                if (expiryDate is not null)
                    problem.Extensions["expiryNotApplied"] = true;
                problem.Extensions["workforceApp"] = workforceApp;
            }

            return ContactShareDecision.Answered(shared.Result);
        }

        return ContactShareDecision.SharedWith(sharedWith, confirmed.Response, workforceApp);
    }

    /// <summary>
    /// Task 193 G1 (owner decision 2): the paired workforce-app grant for a colleague picked DIRECTLY (/share-user), after
    /// <see cref="InternalUserShareCore"/> confirmed the share: the ONE contact that represents the user
    /// (<see cref="ContactUserRepresentation.ResolveContactForUserAsync"/>, kept only where the forward rule agrees) is granted
    /// through the grant core. No contact, several, one that also represents someone else, or a read that fails: only the
    /// share stands, and the answer says why. Never throws (but for cancellation).
    /// </summary>
    internal async Task<WorkforceAppAccess> GrantPairedForUserAsync(
        Guid systemUserId,
        ExternalGrantRootType rootType,
        Guid rootId,
        ExternalAccessLevel level,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        UserContactResolution contact;
        try
        {
            contact = await _people.ResolveContactForUserAsync(systemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            contact = UserContactResolution.Failed(ex, $"The contact systemuser {systemUserId} is linked to could not be read.");
        }

        switch (contact.Status)
        {
            case UserContactStatus.One when contact.ContactRepresentsOthers:
                logger.LogWarning(
                    "[CONTACT-SHARE] {SystemUserId} was shared {RootType} {RootId}; their contact {ContactId} represents other " +
                    "users too, so no workforce-app grant was written.", systemUserId, rootType, rootId, contact.ContactId);
                return WorkforceAppAccess.NotGiven(RepresentsSeveralUsersReasonCode);
            case UserContactStatus.One:
                return await GrantPairedAsync(
                        contact.ContactId!.Value, systemUserId, rootType, rootId, level, expiryDate: null, firmOrganizationId: null,
                        dataverseClient, participations, accessibleRecords, callerAccessProbe, httpContext, logger, ct)
                    .ConfigureAwait(false);
            case UserContactStatus.None:
                logger.LogInformation(
                    "[CONTACT-SHARE] {SystemUserId} was shared {RootType} {RootId}; no contact represents them, so no " +
                    "workforce-app grant was written.", systemUserId, rootType, rootId);
                return WorkforceAppAccess.NotGiven(WorkforceNoLinkedContactReasonCode);
            case UserContactStatus.Several:
                logger.LogWarning(
                    "[CONTACT-SHARE] {SystemUserId} was shared {RootType} {RootId}; more than one contact represents them, so " +
                    "no workforce-app grant was written.", systemUserId, rootType, rootId);
                return WorkforceAppAccess.NotGiven(WorkforceSeveralContactsReasonCode);
            default:
                logger.LogError(contact.Fault,
                    "[CONTACT-SHARE] {SystemUserId} was shared {RootType} {RootId}; which contact represents them could not be " +
                    "read ({Detail}), so no workforce-app grant was written.", systemUserId, rootType, rootId, contact.FaultDetail);
                return WorkforceAppAccess.NotGiven(WorkforceLinkUnreadableReasonCode);
        }
    }

    /// <summary>
    /// The paired contact grant (task 193 G1, owner F1-b and decisions (a) and (iii)): <paramref name="contactId"/> granted on the
    /// record at <paramref name="level"/> through <see cref="GrantExternalAccessEndpoint.CreateGrantAsync"/> with all its checks
    /// (the policy, so Restricted refuses; the grantor ceiling; never-lower; the No Access list; the owner team), dated as any
    /// grant (the request's date, else today + 90; a lapsed key is restored as a re-add), with <c>sprk_pairedsystemuser</c> set
    /// to <paramref name="systemUserId"/>. An existing grant is ADOPTED: stamped, its level and date handled by the core as any
    /// re-add. No invite and no contact notification: the share core told the colleague.
    /// </summary>
    /// <remarks>
    /// FAIL CLOSED on the column: it is read first (<see cref="ExternalGrantLifecycle.ReadPairingsAsync"/>). When the
    /// environment lacks it, or the read fails, NO grant is written (never an unpaired one, which the share's later removal
    /// could not find) and the answer says so. Never throws (but for cancellation).
    /// </remarks>
    private async Task<WorkforceAppAccess> GrantPairedAsync(
        Guid contactId,
        Guid systemUserId,
        ExternalGrantRootType rootType,
        Guid rootId,
        ExternalAccessLevel level,
        DateOnly? expiryDate,
        Guid? firmOrganizationId,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        var key = ExternalGrantKey.ForContact(rootType, rootId, contactId);
        var pairings = await ExternalGrantLifecycle.ReadPairingsAsync(dataverseClient, key, ct).ConfigureAwait(false);
        switch (pairings.Status)
        {
            case GrantPairingReadStatus.Read:
                break;
            case GrantPairingReadStatus.ColumnMissing:
                logger.LogError(pairings.Fault,
                    "[CONTACT-SHARE] PAIRING-COLUMN-MISSING: {GrantTable}.{PairingColumn} is not provisioned here, so no " +
                    "workforce-app grant was written for contact {ContactId} on {RootType} {RootId}; only the share to " +
                    "{SystemUserId} stands. Apply scripts/Deploy-ExternalRecordAccessPairedSystemUser.ps1.",
                    ExternalGrantLifecycle.EntityLogicalName, ExternalGrantLifecycle.PairedSystemUserAttribute, contactId,
                    rootType, rootId, systemUserId);
                return WorkforceAppAccess.NotGiven(WorkforcePairingColumnMissingReasonCode);
            default:
                logger.LogError(pairings.Fault,
                    "[CONTACT-SHARE] The pairing of contact {ContactId}'s grants on {RootType} {RootId} could not be read, so no " +
                    "workforce-app grant was written; only the share to {SystemUserId} stands.",
                    contactId, rootType, rootId, systemUserId);
                return WorkforceAppAccess.NotGiven(WorkforcePairingUnreadableReasonCode);
        }

        GrantCeiling ceiling;
        try
        {
            ceiling = await GrantExternalAccessEndpoint.ProbeGrantorCeilingAsync(callerAccessProbe, httpContext, rootType, rootId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[CONTACT-SHARE] The caller's own rights on {RootType} {RootId} could not be established, so no workforce-app " +
                "grant was written for contact {ContactId}; only the share to {SystemUserId} stands.",
                rootType, rootId, contactId, systemUserId);
            return WorkforceAppAccess.NotGiven(ExternalGrantLifecycle.CallerRightsUnreadableReasonCode);
        }

        GrantExternalAccessEndpoint.GrantUpsertOutcome outcome;
        try
        {
            outcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
                    new GrantAccessRequest(contactId, Guid.Empty, level, expiryDate, firmOrganizationId, rootType.ToString(), rootId),
                    rootType, rootId, ExternalGrantLifecycle.TodayUtc(_time), ceiling,
                    GrantExternalAccessEndpoint.ResolveCallerSystemUserId(httpContext),
                    dataverseClient, participations, accessibleRecords, _identities, _ownership, _ownerPrivileges, logger, ct,
                    reAddRestoresLapsed: true,
                    pairing: new GrantPairing(systemUserId, pairings.ByRow))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[CONTACT-SHARE] The workforce-app grant for contact {ContactId} on {RootType} {RootId} failed; only the share " +
                "to {SystemUserId} stands.", contactId, rootType, rootId, systemUserId);
            return WorkforceAppAccess.NotGiven(WorkforceGrantFailedReasonCode);
        }

        if (outcome.Refusal is { } refusal)
        {
            logger.LogInformation(
                "[CONTACT-SHARE] The workforce-app grant for contact {ContactId} on {RootType} {RootId} was refused ({ReasonCode}); " +
                "only the share to {SystemUserId} stands.", contactId, rootType, rootId, refusal.ReasonCode, systemUserId);
            return WorkforceAppAccess.NotGiven(refusal.ReasonCode ?? WorkforceGrantFailedReasonCode);
        }

        if (outcome.Warning is not null)
        {
            logger.LogWarning(
                "[CONTACT-SHARE] The workforce-app grant {AccessRecordId} for contact {ContactId} on {RootType} {RootId} confers " +
                "no access; only the share to {SystemUserId} stands.", outcome.AccessRecordId, contactId, rootType, rootId, systemUserId);
            return WorkforceAppAccess.NotGiven(WorkforceGrantFailedReasonCode);
        }

        logger.LogInformation(
            "[CONTACT-SHARE] Contact {ContactId} granted {Level} on {RootType} {RootId} until {Until} (row {AccessRecordId}), " +
            "paired with the share to {SystemUserId}.",
            contactId, outcome.GrantedLevel, rootType, rootId, outcome.GrantedExpiry, outcome.AccessRecordId, systemUserId);
        return new WorkforceAppAccess(true, null, outcome.GrantedExpiry, outcome.AccessRecordId);
    }

    /// <summary>
    /// The write-time No Access check /grant applies (<see cref="IAccessibleRecordSetService.CheckGranteeNoAccessAsync"/>,
    /// fail closed): <c>null</c> when allowed, else the 422 <c>grantee_denied</c> or the 503 <c>no_access_unverifiable</c>
    /// answer. Nothing is written either way.
    /// </summary>
    private static async Task<IResult?> NoAccessRefusalAsync(
        IAccessibleRecordSetService accessibleRecords, Guid contactId, Guid? firmOrganizationId,
        ExternalGrantRootType rootType, Guid rootId, HttpContext httpContext, ILogger logger, CancellationToken ct)
    {
        var organizations = firmOrganizationId is { } firm && firm != Guid.Empty ? new[] { firm } : Array.Empty<Guid>();
        NoAccessCheckAnswer answer;
        try
        {
            answer = await accessibleRecords.CheckGranteeNoAccessAsync(
                ExternalGrantRoot.LogicalNameFor(rootType), rootId, contactId, organizations, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[CONTACT-SHARE] DENY-LIST-UNREADABLE: the No Access check for {RootType} {RootId} threw; nothing was shared " +
                "(fail closed).", rootType, rootId);
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(GrantPolicyDecision.GranteeDenyListUnreadable, httpContext);
        }

        switch (answer)
        {
            case NoAccessCheckAnswer.Allowed:
                return null;
            case NoAccessCheckAnswer.Denied:
                logger.LogInformation(
                    "[CONTACT-SHARE] Contact {ContactId} is on the No Access list of {RootType} {RootId}; nothing was shared.",
                    contactId, rootType, rootId);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(GrantPolicyDecision.GranteeDenied, httpContext);
            default:
                logger.LogError(
                    "[CONTACT-SHARE] DENY-LIST-UNREADABLE: the No Access check for {RootType} {RootId} could not be completed " +
                    "({Answer}); nothing was shared (fail closed).", rootType, rootId, answer);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(GrantPolicyDecision.GranteeDenyListUnreadable, httpContext);
        }
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
/// <param name="Result">The answer to return as is: a refusal (nothing written), or the share core's related-records-pending
/// 500 (the record's share WAS written; its problem details name <c>sharedWithUser</c>). <c>null</c> for the grant path or a
/// confirmed share.</param>
/// <param name="Shared">The internal user the record was confirmed shared with; <c>null</c> otherwise.</param>
/// <param name="ShareResponse">The share core's confirmed answer, with <paramref name="Shared"/>.</param>
/// <param name="WorkforceApp">Task 193 G1: the paired workforce-app grant beside a confirmed share; <c>null</c> otherwise.</param>
internal sealed record ContactShareDecision(
    IResult? Result, SharedWithUser? Shared, ShareRecordWithUserResponse? ShareResponse, WorkforceAppAccess? WorkforceApp = null)
{
    /// <summary>The contact represents nobody: the caller's grant path goes on, unchanged.</summary>
    public static ContactShareDecision GrantPath { get; } = new(null, null, null);

    public bool IsGrantPath => Result is null && Shared is null;

    public static ContactShareDecision Answered(IResult result) => new(result, null, null);

    public static ContactShareDecision SharedWith(
        SharedWithUser user, ShareRecordWithUserResponse response, WorkforceAppAccess workforceApp) =>
        new(null, user, response, workforceApp);
}
