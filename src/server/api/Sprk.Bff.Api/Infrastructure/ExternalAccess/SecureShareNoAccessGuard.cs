namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// unified-access-control-r2 task 143 (GitHub #1066) — the No Access list for INTERNAL users on SECURE records
// (owner round 2 Q4; N1, N4 accepted as recommended). ONE write-time question — "is systemuser U walled off
// secure record R?" — asked by every BFF share write on a secure record before it writes anything.

/// <summary>What the write-time No Access check decided for one (systemuser, record) pair.</summary>
public enum SecureShareWallOutcome
{
    /// <summary>The record is not secure. Q4 scopes the internal wall to secure records: nothing applies.</summary>
    NotSecure,

    /// <summary>Secure, and no active entry covers this user on it.</summary>
    NotWalled,

    /// <summary>Secure, and at least one active entry covers this user on it. REFUSE the share.</summary>
    Walled,

    /// <summary>Something the decision needs could not be read. REFUSE the share (ADR-003 — never "not walled").</summary>
    Unverifiable,
}

/// <summary>The write-time No Access decision, with what a refusal needs to log (never to show).</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="EntryIds">The matching entries when <see cref="SecureShareWallOutcome.Walled"/>; for the LOG only —
/// a refusal message never names an entry or its reason.</param>
/// <param name="Fault">What could not be read, when <see cref="SecureShareWallOutcome.Unverifiable"/>.</param>
public sealed record SecureShareWallDecision(
    SecureShareWallOutcome Outcome,
    IReadOnlyList<Guid> EntryIds,
    string? Fault = null)
{
    /// <summary>Whether a share write must be refused: walled, or could not tell.</summary>
    public bool RefusesShare => Outcome is SecureShareWallOutcome.Walled or SecureShareWallOutcome.Unverifiable;

    internal static SecureShareWallDecision NotSecure { get; } = new(SecureShareWallOutcome.NotSecure, Array.Empty<Guid>());

    internal static SecureShareWallDecision NotWalled { get; } = new(SecureShareWallOutcome.NotWalled, Array.Empty<Guid>());

    internal static SecureShareWallDecision Unreadable(string fault) =>
        new(SecureShareWallOutcome.Unverifiable, Array.Empty<Guid>(), fault);
}

/// <summary>
/// A systemuser's No Access subjects (task 143): itself, the contacts that represent it, and those contacts'
/// organizations — or the reason they could not all be read.
/// </summary>
public sealed record SystemUserNoAccessSubjects(NoAccessSubjects Subjects, string? Fault)
{
    /// <summary>Every subject was read. When <c>false</c>, a write-time check refuses and the veto removes.</summary>
    public bool Readable => Fault is null;
}

/// <summary>
/// The ONE write-time No Access check for internal users on secure records (task 143 · owner Q4).
/// </summary>
/// <remarks>
/// <para><b>Who is walled</b> (owner N1 and N4, accepted as recommended). A systemuser U is walled off a secure record R
/// when an ACTIVE <c>sprk_noaccessentry</c> names, as its subject, (i) U itself (<c>sprk_subjectsystemuser</c>), (ii) a
/// contact that represents U, or (iii) an organization such a contact is an ACTIVE member of (the <c>statecode</c>-only
/// wall set, owner D-2 / D-10) — and, as its object, R itself or an organization R references in ANY org-typed lookup
/// (B-10 over-match). "A contact that represents U" is the task-141 link (<c>systemuser.sprk_primarycontact</c>) AND
/// every contact bound to U's Entra oid (<c>contact.sprk_externalobjectid</c>) — more than one is possible only for an
/// ambiguous binding, and over-matching is the safe direction for a wall.</para>
///
/// <para><b>Secure only</b> (Q4). On a non-secure record internal access comes from role depth in the business unit,
/// which no share refusal can remove, and the owner's rule is scoped to secure records; this check answers
/// <see cref="SecureShareWallOutcome.NotSecure"/> there and the share proceeds exactly as before.</para>
///
/// <para><b>Fails closed</b> (ADR-003 / WP-6). An unreadable flag set, a record whose flags did not come back, an
/// unreadable link or binding, an unreadable organization membership, an unreadable record-side organization set, a
/// fail-closed deny-list read, or any throw — each answers <see cref="SecureShareWallOutcome.Unverifiable"/>, and every
/// caller refuses the share. A fault is never "not walled".</para>
///
/// <para><b>Reuse, not a second rule</b> (CLAUDE.md §11). The matching is the one <see cref="INoAccessListReader"/>; the
/// flag and organization reads are <see cref="ExternalParticipationService"/>'s; the link is
/// <see cref="IContactIdentityStore"/>'s status-bearing read (not <c>IIdentityNormalizationService</c>, whose "never an
/// exception" contract turns a faulted link read into "no contact" — a fail-OPEN for a wall).</para>
///
/// <para><b>Who else asks, and how</b> (task 143 r1). "Which contacts represent systemuser U" has ONE answer —
/// the static <c>ResolveSubjectsAsync</c>:
/// the <c>sprk_primarycontact</c> link plus every contact bound to U's oid, each read status-first, and those contacts'
/// wall organizations. This guard calls it; the systemuser-plane read-time veto
/// (<see cref="AccessibleRecordSetService"/>) calls it for every SECURE candidate — adding the principal's derived contact,
/// so the contact whose grants were composed is always among the subjects — and removes every secure candidate when it
/// reports a fault; the enforcer (<see cref="NoAccessShareEnforcer"/>) asks the REVERSE question ("which systemusers does
/// contact C represent") over the same two links. On a non-secure record the veto checks only the derived contact and its
/// organizations, because there the wall removes only that contact's grant contribution (owner N3) — a contact the
/// normalizer could not derive contributed no grant to remove. Task 149's <c>SecureChildShareSynchronizer</c> asks
/// <see cref="CheckAsync"/> about each of a child's secure ROOTS before any child grant or widening, and drops a user it
/// refuses (walled or unverifiable).</para>
/// </remarks>
public sealed class SecureShareNoAccessGuard
{
    private readonly ExternalParticipationService _participations;
    private readonly INoAccessListReader _noAccessList;
    private readonly IContactIdentityStore _identityStore;
    private readonly ILogger<SecureShareNoAccessGuard> _logger;

    public SecureShareNoAccessGuard(
        ExternalParticipationService participations,
        INoAccessListReader noAccessList,
        IContactIdentityStore identityStore,
        ILogger<SecureShareNoAccessGuard> logger)
    {
        _participations = participations;
        _noAccessList = noAccessList;
        _identityStore = identityStore;
        _logger = logger;
    }

    /// <summary>
    /// Is <paramref name="systemUserId"/> walled off <paramref name="recordId"/>? Asked BEFORE any share write.
    /// </summary>
    /// <param name="entityLogicalName">The record's LOGICAL name (<c>sprk_project</c> / <c>sprk_matter</c> /
    /// <c>sprk_workassignment</c>).</param>
    public async Task<SecureShareWallDecision> CheckAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || recordId == Guid.Empty || systemUserId == Guid.Empty)
        {
            _logger.LogError(
                "[NO-ACCESS-GUARD] Called without a record or user ({Entity} {RecordId}, user {SystemUserId}); refusing.",
                entityLogicalName, recordId, systemUserId);
            return SecureShareWallDecision.Unreadable("request");
        }

        if (!ExternalParticipationService.IsFlagBearingRootType(entityLogicalName))
        {
            // Not a secure ROOT type: no direct POA share on it is a secure-record share. A child of a secure record
            // (C10 part 2, tasks 146/149) is asked about through its ROOT: SecureChildShareSynchronizer calls this check
            // for each of the child's secure roots before any child grant or widening.
            return SecureShareWallDecision.NotSecure;
        }

        try
        {
            var flags = await _participations
                .GetRootRecordFlagsAsync(entityLogicalName, new[] { recordId }, ct).ConfigureAwait(false);

            // Absent = unreadable at write time (task 138's rule): the read path's "no veto" reading of an absent key
            // does not hold when the question is whether to WRITE access.
            if (!flags.TryGetValue(recordId, out var f) || f.IsUnreadable)
            {
                return Refuse("flags", entityLogicalName, recordId, systemUserId);
            }

            if (!f.IsSecure)
            {
                return SecureShareWallDecision.NotSecure;
            }

            var subjects = await ResolveSubjectsAsync(systemUserId, ct).ConfigureAwait(false);
            if (!subjects.Readable)
            {
                return Refuse(subjects.Fault!, entityLogicalName, recordId, systemUserId);
            }

            var referenced = await _participations
                .GetReferencedOrganizationIdsAsync(entityLogicalName, new[] { recordId }, ct).ConfigureAwait(false);
            if (referenced.TryGetValue(recordId, out var refs) && refs.Unreadable)
            {
                return Refuse("referenced-organizations", entityLogicalName, recordId, systemUserId);
            }

            var candidate = new NoAccessCandidateRecord(
                entityLogicalName, recordId,
                referenced.TryGetValue(recordId, out var resolved) ? resolved.OrganizationIds : Array.Empty<Guid>());

            var result = await _noAccessList
                .GetDeniedRecordsAsync(subjects.Subjects, new[] { candidate }, ct).ConfigureAwait(false);

            if (result is null || result.FailedClosed)
            {
                return Refuse("deny-list", entityLogicalName, recordId, systemUserId);
            }

            if (!result.DeniedRecordIds.Contains(recordId))
            {
                return SecureShareWallDecision.NotWalled;
            }

            var entryIds = result.DenyingEntryIds.TryGetValue(recordId, out var ids) ? ids : Array.Empty<Guid>();
            _logger.LogWarning(
                "[NO-ACCESS-GUARD] {SystemUserId} is WALLED off secure {Entity} {RecordId} by entr(y/ies) {EntryIds}.",
                systemUserId, entityLogicalName, recordId, string.Join(",", entryIds));
            return new SecureShareWallDecision(SecureShareWallOutcome.Walled, entryIds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[NO-ACCESS-GUARD] The No Access check threw for {SystemUserId} on {Entity} {RecordId}; refusing (fail closed).",
                systemUserId, entityLogicalName, recordId);
            return SecureShareWallDecision.Unreadable("exception");
        }
    }

    /// <summary>
    /// A systemuser's three subject kinds, each read status-first (task 143). The same answer the read-time veto uses on
    /// secure records, so who is "the same person" has one answer.
    /// </summary>
    public Task<SystemUserNoAccessSubjects> ResolveSubjectsAsync(Guid systemUserId, CancellationToken ct)
        => ResolveSubjectsAsync(_identityStore, _participations, systemUserId, null, null, ct);

    /// <summary>
    /// The ONE answer to "what are systemuser U's No Access subjects" (task 143; shared with the read-time veto in task 143
    /// r1): U itself, the contacts that represent U — the task-141 link and every contact bound to U's oid, each read
    /// status-first — and those contacts' wall organizations. Never throws a read FAULT into "no contact": a link, binding or
    /// membership read that fails is reported in <see cref="SystemUserNoAccessSubjects.Fault"/>, and every caller then
    /// refuses (write time) or removes (read time).
    /// </summary>
    /// <param name="identityStore">The status-bearing identity reads.</param>
    /// <param name="participations">The membership read.</param>
    /// <param name="systemUserId">The user.</param>
    /// <param name="alsoContactId">A contact the caller already knows represents the user (the read-time veto's derived
    /// contact). Added to the subjects whatever the link reads say, so the subject set never lacks it.</param>
    /// <param name="alsoContactMemberships">That contact's memberships, already read by the caller; reused instead of read
    /// twice. Ignored without <paramref name="alsoContactId"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task<SystemUserNoAccessSubjects> ResolveSubjectsAsync(
        IContactIdentityStore identityStore,
        ExternalParticipationService participations,
        Guid systemUserId,
        Guid? alsoContactId,
        ActiveOrgMemberships? alsoContactMemberships,
        CancellationToken ct)
    {
        var contacts = new HashSet<Guid>();
        if (alsoContactId is { } known && known != Guid.Empty)
        {
            contacts.Add(known);
        }

        var user = await identityStore.GetSystemUserAsync(systemUserId, ct).ConfigureAwait(false);
        if (user.Status == LookupStatus.ColumnMissing)
        {
            // The link columns (task 141) are not provisioned here, so no contact CAN be linked to this user: only the
            // systemuser subject (and a contact the caller already knows) applies. A schema fact, not a fault.
            return await WithOrganizationsAsync(participations, systemUserId, contacts, alsoContactId, alsoContactMemberships, ct)
                .ConfigureAwait(false);
        }

        if (user.Status != LookupStatus.Read)
        {
            return new SystemUserNoAccessSubjects(Only(systemUserId), "link");
        }

        if (user.Row?.PrimaryContactId is { } linked && linked != Guid.Empty)
        {
            contacts.Add(linked);
        }

        if (user.Row?.Oid is { } oid && oid != Guid.Empty)
        {
            var bound = await identityStore.FindContactsByOidAsync(oid, ct).ConfigureAwait(false);
            switch (bound.Status)
            {
                case LookupStatus.Read:
                    foreach (var row in bound.Rows)
                    {
                        contacts.Add(row.ContactId);
                    }

                    break;

                case LookupStatus.ColumnMissing:
                    // The binding column does not exist here, so no contact CAN be bound to the oid: a schema fact,
                    // not a fault (refusing every secure share in such an environment would be an outage, not a wall).
                    break;

                default:
                    return new SystemUserNoAccessSubjects(Only(systemUserId), "binding");
            }
        }

        return await WithOrganizationsAsync(participations, systemUserId, contacts, alsoContactId, alsoContactMemberships, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The subjects with every contact's wall organizations; an unreadable membership is a fault.</summary>
    private static async Task<SystemUserNoAccessSubjects> WithOrganizationsAsync(
        ExternalParticipationService participations,
        Guid systemUserId,
        HashSet<Guid> contacts,
        Guid? knownContactId,
        ActiveOrgMemberships? knownMemberships,
        CancellationToken ct)
    {
        var organizations = new HashSet<Guid>();
        foreach (var contactId in contacts)
        {
            var memberships = knownMemberships is { } already && knownContactId == contactId
                ? already
                : await participations.ReadOrganizationMembershipsAsync(contactId, ct).ConfigureAwait(false);
            if (memberships.Unreadable)
            {
                return new SystemUserNoAccessSubjects(Only(systemUserId), "organization-membership");
            }

            organizations.UnionWith(memberships.WallSubjectOrganizationIds);
        }

        return new SystemUserNoAccessSubjects(
            new NoAccessSubjects(contacts.ToList(), organizations.ToList(), systemUserId), Fault: null);
    }

    private static NoAccessSubjects Only(Guid systemUserId) =>
        new(Array.Empty<Guid>(), Array.Empty<Guid>(), systemUserId);

    private SecureShareWallDecision Refuse(string fault, string entity, Guid recordId, Guid systemUserId)
    {
        _logger.LogError(
            "[NO-ACCESS-GUARD] Could not read the {Fault} needed to decide whether {SystemUserId} is walled off {Entity} " +
            "{RecordId}; refusing the share (fail closed).", fault, systemUserId, entity, recordId);
        return SecureShareWallDecision.Unreadable(fault);
    }
}
