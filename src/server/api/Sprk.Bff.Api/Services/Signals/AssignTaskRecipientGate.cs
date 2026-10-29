using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// Task 168 fix round (owner D-199 Q1/Q2): WHO may be sent the Assign Task email. One gate, asked by the in-request send AND by the
// system-mailbox job at send time, so the fallback can never reach anyone the gate refused.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GrantAccessNotifier.NotifyContactGrantAsync (master; the internal "who does this contact represent" rule over
//     IContactIdentityStore + AssignedAccessStore + the binder's oid decision, InternalShareEndpoints.ClassifyEligibility, the
//     SecureShareNoAccessGuard record-and-secure-parents check, and the external IsOnboardedWithEmail test);
//     IAccessibleRecordSetService.ComposeForCiamContactAsync (the portal's own grant term + vetoes for a contact);
//     CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync (Dataverse's answer for a named systemuser);
//     ExternalDataService.CheckLiveParentsAsync (fix round 2: the portal events list's per-row rule, tasks 039/187, for one event)
//     over CiamContactPrincipalStrategy.ComposeAsync (the principal the portal builds for the contact).
//   Extension: every one of those is called unchanged (IsOnboardedWithEmail only became internal; fix round 2 lifted the CIAM
//     principal composition out of CiamContactPrincipalStrategy.ResolveAsync into an internal static, same behaviour). The grant notifier cannot be
//     extended for this: it answers "who to tell about a grant on a root" and sends its own messages; this answers "may this one
//     assignee be emailed about a task under this record", which adds the task Read check and the external grant check.
//     uac-r2 task 193's ContactUserRepresentation (the one extracted contact -> user rule) is not on master yet (K-1): the
//     representation step below mirrors GrantAccessNotifier's lines and is to be replaced by it when 193 merges.
//   Cost of doing nothing: D-199's gate does not exist, so the email names a record to a person who cannot see it, and the
//     system-mailbox fallback would send to anyone the step bound.
// =====================================================================================================================

/// <summary>Who <see cref="AssignTaskRecipientGate"/> let through.</summary>
public enum AssignTaskRecipientKind
{
    /// <summary>Exactly one eligible internal user represents the contact, is not walled off the record and can read the task.</summary>
    Internal,

    /// <summary>An onboarded external contact (no internal user represents it) with an active grant carrying Read on the record.</summary>
    External,

    /// <summary>Nobody: no email, no bell (see <see cref="AssignTaskRefusal"/>).</summary>
    Refused,
}

/// <summary>Why the gate refused. Each is a commit warning the assigner sees (<see cref="AssignTaskRecipient.Warning"/>).</summary>
public enum AssignTaskRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>The assignee cannot see the record: inactive, walled, no Read on the task, no grant, not onboarded, or the caller.</summary>
    NoAccess,

    /// <summary>The contact represents more than one eligible user, so who it is cannot be told.</summary>
    SeveralUsers,

    /// <summary>A check could not be made (a read fault, an unreadable flag or wall, no answer from Dataverse): fail closed.</summary>
    Unverified,
}

/// <summary>The gate's verdict for one assignee.</summary>
/// <param name="Kind">Internal, External or Refused.</param>
/// <param name="Refusal">Why it was refused (<see cref="AssignTaskRefusal.None"/> otherwise).</param>
/// <param name="UserId">The internal user (Internal only): the bell's owner.</param>
public sealed record AssignTaskRecipient(AssignTaskRecipientKind Kind, AssignTaskRefusal Refusal, Guid? UserId)
{
    /// <summary>Commit warning: no email, the assignee cannot see the record.</summary>
    internal const string NoAccessWarning = "assignee_email_not_sent_no_access";

    /// <summary>Commit warning: no email, the assignee's contact is linked to more than one user.</summary>
    internal const string SeveralUsersWarning = "assignee_email_not_sent_several_users";

    /// <summary>Commit warning: no email, whether the assignee may see the record could not be checked.</summary>
    internal const string UnverifiedWarning = "assignee_email_not_sent_unverified";

    internal static AssignTaskRecipient ForInternal(Guid userId) => new(AssignTaskRecipientKind.Internal, AssignTaskRefusal.None, userId);

    internal static AssignTaskRecipient ForExternal() => new(AssignTaskRecipientKind.External, AssignTaskRefusal.None, null);

    internal static AssignTaskRecipient Refused(AssignTaskRefusal why) => new(AssignTaskRecipientKind.Refused, why, null);

    /// <summary>The commit warning of a refusal; null when the gate let the assignee through.</summary>
    internal string? Warning => Refusal switch
    {
        AssignTaskRefusal.NoAccess => NoAccessWarning,
        AssignTaskRefusal.SeveralUsers => SeveralUsersWarning,
        AssignTaskRefusal.Unverified => UnverifiedWarning,
        _ => null,
    };
}

/// <summary>
/// D-199 Q1: the Assign Task email goes to the assignee only when they can see the record the task is filed under.
/// <list type="bullet">
/// <item><b>Internal</b> (the <see cref="GrantAccessNotifier"/> gate): exactly one eligible user represents the contact
/// (<see cref="AssignedLinkCandidate.Represents"/>, the oid only where the binder resolves it to this contact alone;
/// <see cref="InternalShareEndpoints.ClassifyEligibility"/>, with the record's Restricted state read for a user flagged external);
/// the No Access check on the record and its secure parents (<see cref="SecureWallRecordScope.AsFlagged"/>) is NotSecure or
/// NotWalled; and Dataverse answers Read for that user on the task.</item>
/// <item><b>External</b> (the <c>EmailExternalContactGrantAsync</c> gate): no internal user represents the contact; it is active,
/// onboarded with an email (<see cref="GrantAccessNotifier.IsOnboardedWithEmail"/>), not the assigner, the portal's own grant
/// composition (<see cref="IAccessibleRecordSetService.ComposeForCiamContactAsync"/>: grants, Secure suppression, No Access,
/// Restricted) gives it Read on the record, and the portal's events list would show it THIS task: the list's per-row rule
/// (<see cref="ExternalDataService.CheckLiveParentsAsync"/>, tasks 039/187) withholds a task also filed to a secure record the
/// contact cannot read.</item>
/// <item><b>Nobody else</b>, and nobody when a check cannot be made (ADR-003).</item>
/// </list>
/// Never throws (except the caller's cancellation): a fault is <see cref="AssignTaskRefusal.Unverified"/>. App-only reads; ids only in
/// the logs.
/// </summary>
/// <remarks>The Dataverse-facing steps (<see cref="WallAsync"/>, <see cref="TaskRightsAsync"/>, <see cref="RestrictedAsync"/>,
/// <see cref="ContactRightsAsync"/>, <see cref="PortalListsTaskAsync"/>) are virtual: the test seams, so the decision itself is
/// what the notifier tests run; each seam's own logic is tested directly over its real dependency.</remarks>
public class AssignTaskRecipientGate
{
    /// <summary>The task table's entity set, as <c>RetrievePrincipalAccess</c> names it.</summary>
    internal const string TaskEntitySet = "sprk_events";

    private readonly IContactIdentityStore _identities;
    private readonly AssignedAccessStore _links;
    private readonly ExternalParticipationService _participations;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly CallerRecordAccessProbe _probe;
    private readonly IAccessibleRecordSetService _accessibleSet;
    private readonly ExternalDataService _portalData;
    private readonly ILogger<AssignTaskRecipientGate> _logger;

    public AssignTaskRecipientGate(
        IContactIdentityStore identities,
        AssignedAccessStore links,
        ExternalParticipationService participations,
        SecureShareNoAccessGuard noAccessGuard,
        CallerRecordAccessProbe probe,
        IAccessibleRecordSetService accessibleSet,
        ExternalDataService portalData,
        ILogger<AssignTaskRecipientGate> logger)
    {
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _participations = participations;
        _noAccessGuard = noAccessGuard;
        _probe = probe;
        _accessibleSet = accessibleSet;
        _portalData = portalData;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// May <paramref name="contactId"/> be emailed about task <paramref name="taskId"/> filed under <paramref name="core"/>?
    /// <paramref name="assignerOid"/> is the assigner's Entra oid (an external contact signed in as the assigner is never emailed).
    /// </summary>
    public virtual async Task<AssignTaskRecipient> DecideAsync(
        Guid contactId, DecisionCoreRecord? core, Guid? taskId, Guid? assignerOid, CancellationToken ct)
    {
        try
        {
            return await DecideCoreAsync(contactId, core, taskId, assignerOid, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] whether contact {ContactId} may be emailed about task {TaskId} could not be checked ({ExceptionType}); " +
                "no email (fail closed).", contactId, taskId, ex.GetType().Name);
            return AssignTaskRecipient.Refused(AssignTaskRefusal.Unverified);
        }
    }

    private async Task<AssignTaskRecipient> DecideCoreAsync(
        Guid contactId, DecisionCoreRecord? core, Guid? taskId, Guid? assignerOid, CancellationToken ct)
    {
        // ── Who the contact is (status-first: a read that failed is "could not tell", never "represents nobody") ──────────
        ContactBindingRow? row = null;
        Guid? boundOid = null;
        var contact = await _identities.GetContactAsync(contactId, ct).ConfigureAwait(false);
        switch (contact.Status)
        {
            case LookupStatus.Read:
                // An inactive (or missing) contact is nobody's (ADR-003), and a reassigned-away or retired assignee is not emailed.
                if (contact.Rows.FirstOrDefault(r => r.ContactId == contactId) is not { IsActive: true } read)
                    return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "the contact is not active");
                row = read;
                boundOid = Guid.TryParse(read.RawOid?.Trim(), out var oid) && oid != Guid.Empty ? oid : null;
                break;

            case LookupStatus.ColumnMissing:
                // No binding column here, so no oid and no onboarding; the contact must still be active.
                switch (await _participations.ReadContactStateAsync(contactId, ct).ConfigureAwait(false))
                {
                    case ContactRecordState.Active:
                        break;
                    case ContactRecordState.Inactive:
                        return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "the contact is not active");
                    default:
                        return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the contact's state could not be read");
                }

                break;

            default:
                return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the contact's binding could not be read");
        }

        // ── Which internal users it represents (GrantAccessNotifier's rule; ContactUserRepresentation after uac-r2 193) ─────
        var candidates = await _links.ReadLinkCandidatesAsync(contactId, boundOid, ct).ConfigureAwait(false)
                         ?? Array.Empty<AssignedLinkCandidate>();
        var oidResolvesHere = false;
        if (boundOid is { } bound && candidates.Any(c => c.PrimaryContactId is null && c.Oid == bound))
        {
            var byOid = await _identities.FindContactsByOidAsync(bound, ct).ConfigureAwait(false);
            if (byOid.Status != LookupStatus.Read)
                return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the contacts bound to its oid could not be read");

            var decision = ContactBindingDecision.DecideBoundContact(byOid);
            oidResolvesHere = decision is { Action: BindingAction.ResolveByOid } && decision.ContactId == contactId;
        }

        var represented = candidates
            .Where(u => u.SystemUserId != Guid.Empty && u.Represents(contactId, oidResolvesHere ? boundOid : null))
            .DistinctBy(u => u.SystemUserId)
            .ToList();

        if (represented.Count == 0)
        {
            // External only when NO internal user can be said to represent it (not by link, not by its oid even unconfirmed).
            if (row is not null && !candidates.Any(u => u.SystemUserId != Guid.Empty && u.Represents(contactId, boundOid)))
                return await DecideExternalAsync(row, core, taskId, assignerOid, ct).ConfigureAwait(false);

            return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "no internal user is confirmed for the contact");
        }

        return await DecideInternalAsync(contactId, represented, core, taskId, ct).ConfigureAwait(false);
    }

    private async Task<AssignTaskRecipient> DecideInternalAsync(
        Guid contactId, IReadOnlyList<AssignedLinkCandidate> represented, DecisionCoreRecord? core, Guid? taskId, CancellationToken ct)
    {
        // Enabled interactive people first (the Restricted bar is asked below, once, for the one user it can apply to).
        var people = represented
            .Where(u => InternalShareEndpoints.ClassifyEligibility(u.IsDisabled, u.AccessMode, u.ApplicationId, u.IsExternal, rootIsRestricted: false)
                        == InternalShareEndpoints.ShareEligibility.Eligible)
            .ToList();
        if (people.Count == 0)
            return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "no enabled internal person represents the contact");
        if (people.Count > 1)
            return Refuse(AssignTaskRefusal.SeveralUsers, contactId, taskId, "the contact represents several users");

        var user = people[0];

        // A user flagged external is barred from a Restricted record (owner round 67): its Restricted state is read for them only.
        if (user.IsExternal == true)
        {
            bool? restricted = core is null ? null : await RestrictedAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
            if (InternalShareEndpoints.ClassifyEligibility(user.IsDisabled, user.AccessMode, user.ApplicationId, user.IsExternal, restricted)
                != InternalShareEndpoints.ShareEligibility.Eligible)
            {
                return restricted is null
                    ? Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the record's Restricted state could not be read for a user flagged external")
                    : Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "the user is flagged external and the record is Restricted");
            }
        }

        // The No Access wall on the record and every secure record it is filed under.
        if (core is not null)
        {
            SecureShareWallOutcome wall;
            try
            {
                wall = await WallAsync(core.Entity, core.Id, user.SystemUserId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                    "[ASSIGN-TASK-NOTIFY] the No Access check for user {UserId} on {Entity} {RecordId} threw ({ExceptionType}).",
                    user.SystemUserId, core.Entity, core.Id, ex.GetType().Name);
                wall = SecureShareWallOutcome.Unverifiable;
            }

            switch (wall)
            {
                case SecureShareWallOutcome.NotSecure:
                case SecureShareWallOutcome.NotWalled:
                    break;
                case SecureShareWallOutcome.Walled:
                    return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "the user is on the record's No Access list");
                default:
                    return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the No Access check could not be made");
            }
        }

        // Dataverse's own answer: the user can read the task (a secure matter's non-member cannot).
        if (taskId is not { } task || task == Guid.Empty)
            return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "the task id is unknown, so its Read cannot be checked");

        var rights = await TaskRightsAsync(user.SystemUserId, task, ct).ConfigureAwait(false);
        if (rights is not { } answered)
            return Refuse(AssignTaskRefusal.Unverified, contactId, taskId, "Dataverse gave no answer for the user's Read on the task");
        if (!answered.HasFlag(AccessRights.Read))
            return Refuse(AssignTaskRefusal.NoAccess, contactId, taskId, "the user cannot read the task");

        return AssignTaskRecipient.ForInternal(user.SystemUserId);
    }

    private async Task<AssignTaskRecipient> DecideExternalAsync(
        ContactBindingRow contact, DecisionCoreRecord? core, Guid? taskId, Guid? assignerOid, CancellationToken ct)
    {
        if (!GrantAccessNotifier.IsOnboardedWithEmail(contact))
            return Refuse(AssignTaskRefusal.NoAccess, contact.ContactId, taskId, "the external contact is not onboarded (or has no email)");

        // Never the assigner (a contact whose sign-in is the caller's own).
        if (contact.Binding.Oid is { } bound && assignerOid is { } caller && caller == bound)
            return Refuse(AssignTaskRefusal.NoAccess, contact.ContactId, taskId, "the external contact is the assigner");

        var grantTable = core?.Entity switch
        {
            AccessibleRecordSetService.MatterEntity => AccessibleRecordSetService.MatterEntity,
            AccessibleRecordSetService.ProjectEntity => AccessibleRecordSetService.ProjectEntity,
            AccessibleRecordSetService.WorkAssignmentEntity => AccessibleRecordSetService.WorkAssignmentEntity,
            _ => null,
        };
        if (grantTable is null || core is null)
            return Refuse(AssignTaskRefusal.NoAccess, contact.ContactId, taskId, "the task is not filed under a record an external grant can name");

        var rights = await ContactRightsAsync(contact.ContactId, grantTable, core.Id, ct).ConfigureAwait(false);
        if (rights is not { } answered)
            return Refuse(AssignTaskRefusal.Unverified, contact.ContactId, taskId, "the contact's grants could not be fully read");
        if (!answered.HasFlag(AccessRights.Read))
            return Refuse(AssignTaskRefusal.NoAccess, contact.ContactId, taskId, "the external contact has no active grant with Read on the record");

        // The portal's events list decides each task row on its own (tasks 039/187): a task ALSO filed to a secure record the
        // contact cannot read is withheld there, so it is not emailed with a link to a page that hides it.
        if (taskId is not { } task || task == Guid.Empty)
            return Refuse(AssignTaskRefusal.Unverified, contact.ContactId, taskId, "the task id is unknown, so the portal's rule for it cannot be asked");

        switch (await PortalListsTaskAsync(contact, task, ct).ConfigureAwait(false))
        {
            case true:
                return AssignTaskRecipient.ForExternal();
            case false:
                return Refuse(AssignTaskRefusal.NoAccess, contact.ContactId, taskId,
                    "the portal's events list withholds the task (it is also filed to a secure record the contact cannot read)");
            default:
                return Refuse(AssignTaskRefusal.Unverified, contact.ContactId, taskId, "the portal's rule for the task could not be decided");
        }
    }

    private AssignTaskRecipient Refuse(AssignTaskRefusal why, Guid contactId, Guid? taskId, string reason)
    {
        if (why == AssignTaskRefusal.Unverified)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}, task {TaskId}: no email, {Reason} (fail closed).", contactId, taskId, reason);
        }
        else
        {
            _logger.LogInformation(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] contact {ContactId}, task {TaskId}: no email, {Reason}.", contactId, taskId, reason);
        }

        return AssignTaskRecipient.Refused(why);
    }

    // ── The Dataverse-facing seams ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The No Access check on the record and its secure parents, as <c>/share-user</c> and the grant notifier ask it.</summary>
    internal virtual async Task<SecureShareWallOutcome> WallAsync(string entity, Guid recordId, Guid userId, CancellationToken ct) =>
        (await _noAccessGuard.CheckRecordAndSecureParentsAsync(entity, recordId, userId, SecureWallRecordScope.AsFlagged, ct)
            .ConfigureAwait(false)).Outcome;

    /// <summary>Dataverse's rights for <paramref name="userId"/> on the task, asked app-only naming the user; null when unanswered.</summary>
    internal virtual Task<AccessRights?> TaskRightsAsync(Guid userId, Guid taskId, CancellationToken ct) =>
        _probe.GetPrincipalRightsAppOnlyOrUnknownAsync(userId, TaskEntitySet, taskId, ct);

    /// <summary>Whether the record is Restricted, itself or through a parent (the effective flags <c>/share-user</c> reads); null
    /// when it could not be read.</summary>
    internal virtual async Task<bool?> RestrictedAsync(string entity, Guid recordId, CancellationToken ct)
    {
        try
        {
            var flags = await _participations.GetEffectiveRootRecordFlagsAsync(entity, new[] { recordId }, ct).ConfigureAwait(false);
            if (flags.Count == 0)
                return false; // a table with no flag columns: no veto applies (a static fact, not a failed read)
            return flags.TryGetValue(recordId, out var f) && !f.IsUnreadable ? f.IsRestricted : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.EmailSkipped,
                "[ASSIGN-TASK-NOTIFY] the access settings of {Entity} {RecordId} could not be read ({ExceptionType}).",
                entity, recordId, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The contact's rights on the record from the portal's own composition (the CIAM principal's grant term and vetoes);
    /// null when the composition is known to be incomplete and the record is not in it.</summary>
    internal virtual async Task<AccessRights?> ContactRightsAsync(Guid contactId, string grantTable, Guid recordId, CancellationToken ct)
    {
        var set = await _accessibleSet.ComposeForCiamContactAsync(contactId, grantTable, ct).ConfigureAwait(false);
        var rights = set.RightsFor(recordId);
        return !rights.HasFlag(AccessRights.Read) && set.Capped ? null : rights;
    }

    /// <summary>
    /// Would the portal's events list show this contact the task? The list's own per-row rule for one event
    /// (<see cref="ExternalDataService.CheckLiveParentsAsync"/>: the task's three <c>sprk_regarding*</c> roots and the live roots
    /// of its other parents, the secure flags of those the contact cannot read, <see cref="ExternalMultiRootRule.ChildRowReadable"/>)
    /// on the principal the portal composes for the contact (<see cref="CiamContactPrincipalStrategy.ComposeAsync"/>). True: listed;
    /// false: withheld; null: a read the rule needed failed, or it was withheld while a composition was capped (the contact may
    /// hold the root the principal lacks).
    /// </summary>
    internal virtual async Task<bool?> PortalListsTaskAsync(ContactBindingRow contact, Guid taskId, CancellationToken ct)
    {
        var composed = await CiamContactPrincipalStrategy
            .ComposeAsync(_accessibleSet, contact.ContactId, contact.Email, contact.Binding.Oid?.ToString("D"), ct)
            .ConfigureAwait(false);
        var decision = await _portalData
            .CheckLiveParentsAsync(ExternalDataService.DeletableKind.Event, taskId, composed.Principal, ct)
            .ConfigureAwait(false);
        return decision switch
        {
            ExternalDataService.LiveParentDecision.Readable => true,
            ExternalDataService.LiveParentDecision.Withheld when !composed.Capped => false,
            _ => null,
        };
    }
}
