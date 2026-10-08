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

    /// <summary>
    /// Task 158 r1c-v2 (round 39 item 2): the secure matter / project whose No Access list decided an answer of
    /// <see cref="SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>
    /// — <c>null</c> when the record's OWN list decided it (or nothing refused). For the message, which never names an
    /// entry or its reason.
    /// </summary>
    public string? ParentTable { get; init; }

    /// <summary>Task 158 r1c-v2: the id of <see cref="ParentTable"/>'s record (for the log).</summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Task 158 r1c-v2: <see cref="SecureShareWallOutcome.Unverifiable"/> because what the record is FILED UNDER could not
    /// be read (the record, its pair type, or a parent's flag) — so whose list applies is unknown; the message says "a secure
    /// record it is filed under", never "this record's list".
    /// </summary>
    public bool FilingUnreadable { get; init; }

    internal static SecureShareWallDecision NotSecure { get; } = new(SecureShareWallOutcome.NotSecure, Array.Empty<Guid>());

    internal static SecureShareWallDecision NotWalled { get; } = new(SecureShareWallOutcome.NotWalled, Array.Empty<Guid>());

    internal static SecureShareWallDecision Unreadable(string fault) =>
        new(SecureShareWallOutcome.Unverifiable, Array.Empty<Guid>(), fault);
}

/// <summary>
/// Task 158 r1c-v2 (round 39 item 2): how <see cref="SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>
/// asks about the record's OWN list. Every secure parent's list is asked as the secure record it is (its flag already read
/// by the walk).
/// </summary>
public enum SecureWallRecordScope
{
    /// <summary>A share on an existing record (<c>/share-user</c>): the record's own flag decides whether its list applies (Q4).</summary>
    AsFlagged,

    /// <summary>The record is being made secure (provisioning, its colleagues, a re-file under a secure record): its list applies whatever its flag reads.</summary>
    BeingSecured,

    /// <summary>
    /// The record does not exist yet (a create under a secure parent): its list is every entry naming an organization the
    /// create payload references — the overload taking the prospective organizations.
    /// </summary>
    Prospective,
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
/// (<see cref="AccessibleRecordSetService"/>) calls it whenever a candidate is SECURE or sits below a secure parent (GitHub
/// #1410, owner round 82) — adding the principal's derived contact, so the contact whose grants were composed is always among
/// the subjects — and removes every such candidate when it reports a fault; the enforcer (<see cref="NoAccessShareEnforcer"/>) asks the REVERSE question ("which systemusers does
/// contact C represent") over the same two links. On a non-secure record's OWN list the veto checks only the derived contact
/// and its organizations, because there the wall removes only that contact's grant contribution (owner N3) — a contact the
/// normalizer could not derive contributed no grant to remove. Task 149's <c>SecureChildShareSynchronizer</c> asks
/// <see cref="CheckAsync"/> about each of a child's secure ROOTS before any child grant or widening, and drops a user it
/// refuses (walled or unverifiable).</para>
/// </remarks>
public sealed class SecureShareNoAccessGuard
{
    private readonly ExternalParticipationService _participations;
    private readonly INoAccessListReader _noAccessList;
    private readonly IContactIdentityStore _identityStore;
    private readonly Spaarke.Dataverse.IGenericEntityService _dataverse;
    private readonly ILogger<SecureShareNoAccessGuard> _logger;

    /// <param name="dataverse">Task 158 r1c-v2 (round 39 item 2): the app-only reads of what a work assignment or project
    /// is filed under, for <see cref="CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>
    /// — through <see cref="Sprk.Bff.Api.Services.Access.SecureRootInheritance.ReadSecureParentsAsync"/>, the ONE parent
    /// walk (never a second copy). Registered unconditionally (GraphModule), so the guard gains no asymmetric dependency.</param>
    public SecureShareNoAccessGuard(
        ExternalParticipationService participations,
        INoAccessListReader noAccessList,
        IContactIdentityStore identityStore,
        Spaarke.Dataverse.IGenericEntityService dataverse,
        ILogger<SecureShareNoAccessGuard> logger)
    {
        _participations = participations;
        _noAccessList = noAccessList;
        _identityStore = identityStore;
        _dataverse = dataverse;
        _logger = logger;
    }

    /// <summary>
    /// unified-access-control-r2 task 158 r1c-v2 — owner round 31 item 1 and main-session round 39 item 2: THE ONE entry
    /// point for a No Access decision that must honour BOTH a record's own list AND the list of every secure matter or
    /// project it is filed under. A work assignment or project filed under a secure record is secure itself (owner round 6),
    /// so a person walled off its secure parent is walled off it too: the person a record is secured for (its creator —
    /// round 31 item 1), a person given a DIRECT share on it (<c>/share-user</c>), and a colleague named when it is provisioned
    /// (<c>/provision-project</c>) — round 39 item 2. The parents are the record's CURRENT filing, read through
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureRootInheritance.ReadSecureParentsAsync"/> (the one parent walk). A table
    /// that files under nothing (a matter) has no parents: the answer is the record's own list alone.
    /// </summary>
    /// <remarks>Fails closed (ADR-003): a filing that cannot be read, or a parent whose flag cannot be read (or reads EMPTY —
    /// owner round 17 item 3), answers <see cref="SecureShareWallOutcome.Unverifiable"/>; a <see cref="SecureShareWallOutcome.Walled"/>
    /// answer on ANY list wins over an unverifiable one (it is final). <see cref="SecureShareWallDecision.ParentTable"/> names
    /// the parent whose list decided it.</remarks>
    public async Task<SecureShareWallDecision> CheckRecordAndSecureParentsAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, SecureWallRecordScope scope, CancellationToken ct)
    {
        // The walk never throws a read fault: an unreadable filing or parent comes back as SecureParentsAnswer.Unverifiable,
        // which refuses below.
        // Round 61 item 1: EVERY secure ancestor, not only the direct parent — the walk climbs level by level (bounded,
        // cycle-safe; a chain past the bound is unverifiable and refuses).
        var parents = await Sprk.Bff.Api.Services.Access.SecureRootInheritance
            .ReadSecureParentsAsync(_dataverse, _logger, entityLogicalName, recordId, ct,
                maxDepth: Sprk.Bff.Api.Services.Access.SecureRootInheritance.MaxFilingDepth)
            .ConfigureAwait(false);

        return await CheckOwnAndAncestorsAsync(entityLogicalName, recordId, systemUserId, scope, parents, ct, null)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The same decision over a filing the CALLER supplies — the filing as a write will leave it (a re-file's pre-check, a
    /// create's plan: both decided before anything is written). <paramref name="parents"/> comes from the one parent walk
    /// (<see cref="Sprk.Bff.Api.Services.Access.SecureRootInheritance"/>); this method asks every list it names.
    /// </summary>
    /// <param name="prospectiveOrganizations">With <see cref="SecureWallRecordScope.Prospective"/>: the organizations the create
    /// payload references (the record has no id yet; <paramref name="recordId"/> is the id it will be created with).</param>
    public async Task<SecureShareWallDecision> CheckRecordAndSecureParentsAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, SecureWallRecordScope scope,
        Sprk.Bff.Api.Services.Access.SecureParentsAnswer parents, CancellationToken ct,
        IReadOnlyCollection<Guid>? prospectiveOrganizations = null)
    {
        ArgumentNullException.ThrowIfNull(parents);

        // Round 61 item 1: the supplied filing names the DIRECT secure parents (as a write will leave it); every secure
        // record above each of them is asked as well, through the same bounded, cycle-safe climb.
        var ancestors = new List<Sprk.Bff.Api.Services.Access.SecureFilingParent>(parents.SecureParents);
        var unverifiable = parents.Unverifiable;
        foreach (var parent in parents.SecureParents)
        {
            var above = await Sprk.Bff.Api.Services.Access.SecureRootInheritance
                .ReadSecureParentsAsync(_dataverse, _logger, parent.Table, parent.Id, ct,
                    maxDepth: Sprk.Bff.Api.Services.Access.SecureRootInheritance.MaxFilingDepth)
                .ConfigureAwait(false);
            unverifiable ??= above.Unverifiable;
            foreach (var ancestor in above.SecureParents)
            {
                if (!ancestors.Any(a => string.Equals(a.Table, ancestor.Table, StringComparison.OrdinalIgnoreCase) && a.Id == ancestor.Id))
                    ancestors.Add(ancestor);
            }
        }

        return await CheckOwnAndAncestorsAsync(entityLogicalName, recordId, systemUserId, scope,
            new Sprk.Bff.Api.Services.Access.SecureParentsAnswer(ancestors, unverifiable), ct, prospectiveOrganizations)
            .ConfigureAwait(false);
    }

    /// <summary>The record's own list and the list of every secure record in <paramref name="parents"/> (already the full
    /// ancestry): a Walled answer anywhere wins, then an Unverifiable one.</summary>
    private async Task<SecureShareWallDecision> CheckOwnAndAncestorsAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, SecureWallRecordScope scope,
        Sprk.Bff.Api.Services.Access.SecureParentsAnswer parents, CancellationToken ct,
        IReadOnlyCollection<Guid>? prospectiveOrganizations)
    {
        var own = scope switch
        {
            SecureWallRecordScope.AsFlagged => await CheckAsync(entityLogicalName, recordId, systemUserId, ct).ConfigureAwait(false),
            SecureWallRecordScope.BeingSecured =>
                await CheckForSecuringAsync(entityLogicalName, recordId, systemUserId, ct).ConfigureAwait(false),
            SecureWallRecordScope.Prospective => await CheckProspectiveAsync(entityLogicalName, recordId,
                prospectiveOrganizations ?? throw new ArgumentNullException(nameof(prospectiveOrganizations)), systemUserId, ct)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };

        var decisions = new List<SecureShareWallDecision> { own };
        if (!parents.IsKnown)
            decisions.Add(SecureShareWallDecision.Unreadable(parents.Unverifiable!) with { FilingUnreadable = true });

        foreach (var parent in parents.SecureParents)
        {
            // The walk read the parent as secure; it is asked about as the secure record it is (never flag-dependent).
            var decision = await CheckForSecuringAsync(parent.Table, parent.Id, systemUserId, ct).ConfigureAwait(false);
            decisions.Add(decision with { ParentTable = parent.Table, ParentId = parent.Id });
        }

        return decisions.FirstOrDefault(d => d.Outcome == SecureShareWallOutcome.Walled)
               ?? decisions.FirstOrDefault(d => d.Outcome == SecureShareWallOutcome.Unverifiable)
               ?? own;
    }

    /// <summary>
    /// Is <paramref name="systemUserId"/> walled off <paramref name="recordId"/>? Asked BEFORE any share write.
    /// </summary>
    /// <param name="entityLogicalName">The record's LOGICAL name (<c>sprk_project</c> / <c>sprk_matter</c> /
    /// <c>sprk_workassignment</c>).</param>
    public Task<SecureShareWallDecision> CheckAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, CancellationToken ct)
        => CheckCoreAsync(entityLogicalName, recordId, systemUserId, WallScope.AsFlagged, null, ct);

    /// <summary>
    /// unified-access-control-r2 task 158 r1 (owner round 31 item 1): is <paramref name="systemUserId"/> walled off
    /// <paramref name="recordId"/> AS A SECURE RECORD — asked by a provisioning that is making the record secure (or by a
    /// writer filing it under a secure record), BEFORE its first write. The record's own flag is NOT read: on the inherited
    /// path the record is still unflagged when the creator's share is decided, and a check that answered
    /// <see cref="SecureShareWallOutcome.NotSecure"/> there would let a walled creator be shared on a record that becomes
    /// secure a moment later. Everything else — the subjects, the record's referenced organizations, the deny list, every
    /// fail-closed rule — is <see cref="CheckAsync"/>'s.
    /// </summary>
    public Task<SecureShareWallDecision> CheckForSecuringAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, CancellationToken ct)
        => CheckCoreAsync(entityLogicalName, recordId, systemUserId, WallScope.BeingSecured, null, ct);

    /// <summary>
    /// unified-access-control-r2 task 158 r1 (owner round 31 items 1 + 2): is <paramref name="systemUserId"/> walled off a
    /// secure record that does NOT EXIST YET — a work assignment or project about to be created under a secure parent. Its
    /// No Access list is every active entry naming one of the organizations the create payload references
    /// (<paramref name="referencedOrganizationIds"/>, the org-typed lookups <see cref="ExternalParticipationService"/>
    /// registers for the table); no entry can name a row that has no id yet. Asked before the create, so a walled creator
    /// is refused with nothing written.
    /// </summary>
    /// <param name="prospectiveRecordId">The id the row will be created with (logs and the deny-list candidate).</param>
    public Task<SecureShareWallDecision> CheckProspectiveAsync(
        string entityLogicalName, Guid prospectiveRecordId, IReadOnlyCollection<Guid> referencedOrganizationIds,
        Guid systemUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(referencedOrganizationIds);
        return CheckCoreAsync(entityLogicalName, prospectiveRecordId, systemUserId, WallScope.BeingSecured,
            referencedOrganizationIds.Where(id => id != Guid.Empty).Distinct().ToArray(), ct);
    }

    /// <summary>Whether the record's own flag decides the scope (a share) or the record is being secured (task 158 r1).</summary>
    private enum WallScope
    {
        /// <summary><see cref="CheckAsync"/>: the record's flag decides — not secure = nothing applies (Q4).</summary>
        AsFlagged,

        /// <summary>The record is being made secure: the wall applies whatever the flag reads now.</summary>
        BeingSecured,
    }

    private async Task<SecureShareWallDecision> CheckCoreAsync(
        string entityLogicalName, Guid recordId, Guid systemUserId, WallScope scope,
        IReadOnlyCollection<Guid>? knownOrganizationIds, CancellationToken ct)
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
            if (scope == WallScope.AsFlagged)
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
            }

            var subjects = await ResolveSubjectsAsync(systemUserId, ct).ConfigureAwait(false);
            if (!subjects.Readable)
            {
                return Refuse(subjects.Fault!, entityLogicalName, recordId, systemUserId);
            }

            IReadOnlyCollection<Guid> organizations;
            if (knownOrganizationIds is not null)
            {
                organizations = knownOrganizationIds;
            }
            else
            {
                var referenced = await _participations
                    .GetReferencedOrganizationIdsAsync(entityLogicalName, new[] { recordId }, ct).ConfigureAwait(false);
                if (referenced.TryGetValue(recordId, out var refs) && refs.Unreadable)
                {
                    return Refuse("referenced-organizations", entityLogicalName, recordId, systemUserId);
                }

                organizations = referenced.TryGetValue(recordId, out var resolved) ? resolved.OrganizationIds : Array.Empty<Guid>();
            }

            var candidate = new NoAccessCandidateRecord(entityLogicalName, recordId, organizations);

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
