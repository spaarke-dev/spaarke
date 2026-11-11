using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// Ontology task 042b (owner decision D-169, #1876): who can read the gate Decision Record of an UNFILED communication. The record is
/// owned by the communication's own owning user (D-144 as amended); it is also shared, Read only, with the internal people the
/// communication is shared with, and it is re-owned to the resolver's team when the communication is filed.
/// </summary>
/// <remarks>
/// <para><b>One routine for both orders.</b> At write (<see cref="ShareAtWriteAsync"/>, called by <see cref="DecisionRecordWriter"/> after the
/// create) and on every new grant of the communication (<see cref="MirrorCommunicationSharesAsync"/>, called by
/// <c>DirectThreadAccessService</c>) the same steps run against CURRENT state: the communication is read (unfiled AND user-owned, else nothing is
/// shared; "filed" is the ownership resolver's own test, <see cref="RecordOwnershipContext.ParentsOf"/> over the whole row, so a communication
/// regarding only a contact, an account or an organization is unfiled), its POA shares are read (the strict read: a failed read is not "no shares"), the systemusers holding Read are kept, the externals are
/// dropped, and each user-owned gate record gets a Read share for each of them except its owner. So whichever of the record and the
/// communication share comes first, the record ends with the communication's sharees.</para>
/// <para><b>Internal only</b> (owner, 2026-10-18). A sharee is skipped when it is a team (no flag to read), when its <c>systemuser</c> row is
/// missing or cannot be read, when <c>sprk_isexternal</c> is <c>true</c>, or when its <c>domainname</c> marks a B2B guest
/// (<see cref="ContactBindingDecision.IsGuestDomainName"/>). A blank flag is internal (owner round 67: "sprk_isexternal means they're
/// external", the rule <c>ISystemUserIdentityResolver.IsExternalAsync</c> applies).</para>
/// <para><b>Never a team-owned record</b> (D-123). Only a record whose owner is a user is shared, and after each pass's grants the owner is read
/// again: when the record is now owned by a team (a filing re-owned it in between) or the owner cannot be read, every grant the pass made is
/// revoked. A communication owned by any team (the Secure Record team, or a category-routed team) is never mirrored.</para>
/// <para><b>Filing</b> (<see cref="ReownAfterFilingAsync"/>, called by <c>SecureChildReconciler.AfterRefileAsync</c>; and at write when the
/// communication was filed or team-owned between the writer's owner decision and its create). Each user-owned gate record of the
/// communication is asked of <see cref="IRecordOwnershipResolver"/> with the gate writer's own question (the communication first, as content
/// of it, plus the record's live core-ancestor stamps). An ordinary team answer is assigned through the ontology writer's identity
/// (<c>prvAssignsprk_DecisionRecord</c>, D-33c; it holds no Write on the table), the access caches are evicted, and the owner is read back;
/// ONLY a read-back naming that team revokes every share on the record. A Secure answer is <see cref="SecureChildReconciler"/>'s (nothing is
/// assigned or revoked here). Any other outcome keeps the user owner and the shares: the over-share stays bounded to the participants and the
/// record is never orphaned. A record already owned by the resolver's ORDINARY team that still carries user shares (a revoke that failed
/// earlier) has them revoked on the next filing; a Secure-team-owned record is never touched here (its shares are the Secure mirror's).</para>
/// <para><b>Best-effort, fail closed on visibility.</b> No public member throws. A failed read, grant, assign or revoke is logged at Warning
/// with ids only and counted on <c>ontology.writer.failures</c>; the record stays written and owner-visible, and nothing is shared beyond the
/// communication's sharees.</para>
/// <para><b>Direct 1:1 messages (owner ruling D-215, uac-r2 task 198 Part E; amends D-169 for Direct messages only).</b> A communication is
/// Direct-team-owned only when its owning team id IS the resolved Direct-message owner team's id: the ONE check,
/// <see cref="DirectMessageParticipantShares.IsDirectOwnerTeamAsync"/> (K3: ids compared only when resolved and non-empty). An unknown answer
/// changes nothing: no grant, no revoke, no re-own, logged. Without the lookup (a test composition) no Direct branch runs and D-169 applies as
/// before; the host always injects it. Every other team owner, and every user-owned communication, keeps D-169 exactly.</para>
/// <list type="bullet">
/// <item><b>The audience</b> (D-215 rule 2) of a gate record of a Direct message: its participant pair
/// (<see cref="DirectMessageParticipantShares.ParticipantsOf"/> over the communication row) that is ALSO a current Read sharee of the
/// communication (its POA shares, read strictly: a share-read fault writes nothing), kept to internal users (<see cref="InternalUsersAsync"/>).
/// An invalid pair grants and revokes nothing. So the record is never more visible than its message.</item>
/// <item><b>While unfiled</b> (D-215 rule 3): the record is owned by the Direct team. The share pass handles a Direct-team-owned communication
/// instead of skipping it as team-owned: a record the Direct team owns is granted the audience (and the owner read-back after the grants accepts
/// exactly that team); a record owned by a user, or by an ordinary team, is re-owned to the Direct team first (rule 5's sequence). At write a
/// gate record the writer made team-owned reaches this through <see cref="ShareTeamOwnedAtWriteAsync"/>; for any communication that is not
/// Direct-team-owned that call writes nothing.</item>
/// <item><b>Filing</b> (D-215 rule 4): a record owned by the Direct team is re-owned on filing as D-169 re-owns a user-owned one, but the
/// audience RECOMPUTED at filing time keeps its Read; every other share is revoked. The ONE Secure check
/// (<see cref="SecureChildShareSynchronizer.IsSecureOwnerTeamAsync"/>) runs on the answer first: a Secure target is the Secure mirror's (nothing
/// is assigned or kept here), an unknown changes nothing. A later filing's leftover revoke (<see cref="RevokeLeftoverSharesAsync"/>) spares the
/// recomputed audience too.</item>
/// <item><b>Unfile back to Direct</b> (D-215, agreed with ontology): when a re-file leaves the communication Direct-team-owned, each record is
/// (a) assigned to the Direct team through the ontology writer's identity, (b) the access caches are evicted once per attempt, (c) its owner is
/// read back, and (d) ONLY when the read-back names the Direct team the audience is recomputed and granted and every other share revoked. A
/// failed read-back keeps the owner and the shares (Warning); nothing re-runs it on a schedule: the communication's next re-file
/// (<see cref="ReownAfterFilingAsync"/>) or its next participant grant (<see cref="MirrorCommunicationSharesAsync"/>) retries it. A
/// Secure-team-owned record is left to the Secure reconciler.</item>
/// <item><b>Revoke side</b> (D-215 rule 5): none by design. No path removes an internal participant's Read on a Direct message (task 198), so
/// no revoke mirror is built; a path that adds one adds the mirror.</item>
/// </list>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="DecisionRecordWriter"/> is create-only and guarded
/// (<c>DecisionRecordContextImmutabilityTests</c>), <c>DirectThreadAccessService</c> grants communications (uac-r2), and
/// <c>SecureChildReconciler.ReownOrdinaryRowAsync</c> re-owns team-owned rows and refuses a user-owned one. Extension: the writer cannot
/// assign without breaking its create-only guard, and the other two are uac-r2's; one helper keeps the three call sites to one call each.
/// Cost of doing nothing: D-169 fails (the record stays visible to the communication's owner only) and a filed communication leaves its
/// participants' shares on a user-owned record. D-215: the Direct branches reuse the one Direct check, the one participant rule, the one Secure
/// check and this class's own grant, re-own and internal-user steps; the two optional constructor parameters are already-registered singletons
/// (no new registration). Without them a Direct message's gate records are readable by nobody but their owner (the Direct team has no members).</para>
/// </remarks>
public sealed class GateDecisionRecordSharing
{
    private const string Table = "sprk_decisionrecord";
    private const string EntitySet = "sprk_decisionrecords";
    private const string CommunicationTable = "sprk_communication";
    private const string SystemUserTable = "systemuser";
    private const string OwningUserColumn = "owninguser";
    private const string OwningTeamColumn = "owningteam";
    private const string OwnerColumn = "ownerid";
    private const string ConfirmedByColumn = "sprk_confirmedby";
    private const string ExternalFlagColumn = "sprk_isexternal";
    private const string DomainNameColumn = "domainname";
    private const string ReadAccessRights = "ReadAccess";

    /// <summary>Dataverse's <c>AccessRights.ReadAccess</c> bit in a share row's <c>accessrightsmask</c>.</summary>
    private const int ReadAccessMask = 1;

    /// <summary>Gate records per communication are at most two (authorize and deny, task 149); the cap is defensive.</summary>
    private const int MaxGateRecords = 10;

    /// <summary>The Decision Record's lookup to the communication it is filed under (the shared regarding builder's map, D-133).</summary>
    private static readonly string SubjectLookup = TodoRegardingBuilder.RegardingLookupsByHostTable[Table][CommunicationTable];

    private readonly IGenericEntityService _entities;
    private readonly IDataverseRecordShareService _shares;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IMembershipCacheInvalidator _cacheInvalidator;
    private readonly ILogger<GateDecisionRecordSharing> _logger;
    private readonly DirectMessageOwnerTeam? _directTeam;
    private readonly IConfiguration? _configuration;

    /// <param name="entities">The shared sysadmin reader: the communication, its sharees' flags, the records and their owners (reads only).</param>
    /// <param name="shares">The BFF's one POA seam (the BFF app identity): every grant, read and revoke of a share.</param>
    /// <param name="writer">The ontology writer's connection: its identity (WhoAmI, so only records it created are touched) and the Assign.</param>
    /// <param name="ownership">uac-r2's owner resolver: the owner a filed communication's gate record takes.</param>
    /// <param name="cacheInvalidator">The owner-change eviction every owner-changing writer calls (task 132).</param>
    /// <param name="logger">Warnings (ids only) and decisions.</param>
    /// <param name="directMessageOwnerTeam">D-215: fm 054a's Direct-message owner team lookup (the one Direct check). Optional so the test
    /// compositions keep compiling; the host injects it. Without it no Direct branch runs (D-169 as before).</param>
    /// <param name="configuration">D-215: the Secure Record settings (the one Secure check on a Direct record's filing). Optional; without it, or
    /// without the lookup, no Direct branch runs.</param>
    public GateDecisionRecordSharing(
        IGenericEntityService entities,
        IDataverseRecordShareService shares,
        OntologyWriterDataverseClient writer,
        IRecordOwnershipResolver ownership,
        IMembershipCacheInvalidator cacheInvalidator,
        ILogger<GateDecisionRecordSharing> logger,
        DirectMessageOwnerTeam? directMessageOwnerTeam = null,
        IConfiguration? configuration = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _cacheInvalidator = cacheInvalidator ?? throw new ArgumentNullException(nameof(cacheInvalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _directTeam = directMessageOwnerTeam;
        _configuration = configuration;
    }

    /// <summary>
    /// After the gate record of an unfiled communication was created owned by <paramref name="ownerUserId"/>: shares it, Read only, with the
    /// communication's current internal sharees. When the communication now reads as FILED (a filing landed between the writer's owner decision
    /// and its create, and that filing's <c>AfterRefileAsync</c> found no record yet) or the communication became team-owned (for example by a thread join, which stamps
    /// no parent lookup), the record is re-owned as a filing would. Never throws; runs on <see cref="CancellationToken.None"/> (the record has landed).
    /// </summary>
    public async Task ShareAtWriteAsync(Guid recordId, Guid communicationId, Guid ownerUserId)
    {
        bool filedOrTeamOwned;
        try
        {
            filedOrTeamOwned = await ShareWithCommunicationShareesAsync(communicationId, [new GateRecord(recordId, ownerUserId, null, [])]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "at write", recordId, communicationId);
            return;
        }

        if (!filedOrTeamOwned)
            return;

        try
        {
            var records = await FindGateRecordsAsync(communicationId, CancellationToken.None).ConfigureAwait(false);
            foreach (var record in records.Where(r => r.Id == recordId && r.OwnerUserId is not null))
                await ReownAsync(communicationId, record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReownFailed(ex, "at write, the record of a communication filed before its create could not be re-owned", recordId, communicationId);
        }
    }

    /// <summary>
    /// D-215 rule 3, at write: after the gate record of a communication was created owned by the team <paramref name="ownerTeamId"/> (not the
    /// Secure team). Only when the communication is unfiled and Direct-team-owned (the one Direct check), and the record's live owner (read by
    /// id) is still <paramref name="ownerTeamId"/>, is anything written: the record is
    /// brought to the Direct team and its audience (<see cref="ShareDirectAsync"/>). For every other communication nothing is written (D-169: a
    /// team-owned record is never shared). Never throws; runs on <see cref="CancellationToken.None"/> (the record has landed).
    /// </summary>
    public async Task ShareTeamOwnedAtWriteAsync(Guid recordId, Guid communicationId, Guid ownerTeamId)
    {
        if (!DirectBranchAvailable)
            return;

        try
        {
            var communication = await ReadCommunicationAsync(communicationId).ConfigureAwait(false);
            if (communication is null)
            {
                // 198 pass 2 N4: not a silent return (as the user-owned path, ShareWithCommunicationShareesAsync).
                ShareFailed(null, "the communication could not be read", recordId, communicationId);
                return;
            }

            if (IsFiled(communication, communicationId))
            {
                // 198 pass 2 N1 (mirrors ShareAtWriteAsync for D-169): a filing landed between the writer's owner decision and the create,
                // and that filing's gate step found no record yet. A record the Direct team owns is re-owned as that filing would have.
                await ReownFiledAtWriteAsync(recordId, communicationId, communication).ConfigureAwait(false);
                return;
            }

            if (communication.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is not { } team || team == Guid.Empty)
                return;

            var direct = await IsDirectTeamAsync(team).ConfigureAwait(false);
            if (direct is null)
            {
                ShareFailed(null, "whether the communication's owner team is the Direct-message owner team cannot be told; nothing is granted, " +
                    "revoked or re-owned (D-215)", recordId, communicationId);
                return;
            }

            if (direct != true)
                return;

            // 198 fix round (ontology): the record's LIVE owner, read by id, before any grant or assign. A Secure mirror may have re-owned
            // it since the create; the read-back in GrantAndConfirmAsync stays as the backstop, but nothing is granted to a record not
            // still owned by the team the writer created it with. An owner that cannot be read is never assumed: nothing is granted.
            var (owner, ownerReadFault) = await ReadOwnerOrNullAsync(recordId).ConfigureAwait(false);
            if (owner is null)
            {
                ShareFailed(ownerReadFault, "the record's owner could not be read before the participant grants; nothing is granted, revoked " +
                    "or re-owned (D-215)", recordId, communicationId);
                return;
            }

            if (owner != DataversePrincipalRef.Team(ownerTeamId))
            {
                _logger.LogInformation(
                    "Gate Decision Record {RecordId} of Direct message {CommunicationId} is no longer owned by the team it was created with; " +
                    "nothing is granted, revoked or re-owned at write, and its current owner's path (the Secure mirror) keeps it (D-215).",
                    recordId, communicationId);
                return;
            }

            await ShareDirectAsync(communicationId, communication, team, [new GateRecord(recordId, null, ownerTeamId, [])]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "at write, the record of a Direct message could not be shared (D-215)", recordId, communicationId);
        }
    }

    /// <summary>
    /// 198 pass 2 N1: at write, the record of a communication that is already FILED. Only a record the Direct-message owner team owns (the
    /// one Direct check, by id) is re-owned, as the filing's own step would have (<see cref="ReownAsync"/> with <c>keepAudience</c>, D-215
    /// rule 4). Any other owner writes nothing; an unknown answer writes nothing and is logged. A fault propagates to the caller's catch.
    /// </summary>
    private async Task ReownFiledAtWriteAsync(Guid recordId, Guid communicationId, Entity communication)
    {
        var record = (await FindGateRecordsAsync(communicationId, CancellationToken.None).ConfigureAwait(false))
            .FirstOrDefault(r => r.Id == recordId);
        if (record?.OwnerTeamId is not { } recordTeam)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of filed communication {CommunicationId} is not team-owned at write; nothing is written here (D-215).",
                recordId, communicationId);
            return;
        }

        var direct = await IsDirectTeamAsync(recordTeam).ConfigureAwait(false);
        if (direct is null)
        {
            ReownFailed(null, "at write, the communication is filed and whether the record's owner team is the Direct-message owner team " +
                "cannot be told; nothing is re-owned (D-215)", recordId, communicationId);
            return;
        }

        if (direct == false)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of filed communication {CommunicationId} is owned by another team; nothing is written at write (D-169).",
                recordId, communicationId);
            return;
        }

        await ReownAsync(communicationId, record, keepAudience: true, communication).ConfigureAwait(false);
    }

    /// <summary>
    /// After a principal was granted Read on <paramref name="communicationId"/>: brings its user-owned gate records in line with the
    /// communication's current internal sharees, and (D-215) the gate records of a Direct-team-owned communication in line with its audience.
    /// One query when the communication has no gate record. Never throws.
    /// </summary>
    public async Task MirrorCommunicationSharesAsync(Guid communicationId, CancellationToken ct)
    {
        try
        {
            // D-215: every gate record is passed; the user-owned branch takes only the user-owned ones (D-169, unchanged), the Direct branch
            // takes the team-owned ones too.
            var records = (await FindGateRecordsAsync(communicationId, ct).ConfigureAwait(false))
                .Where(r => r.OwnerUserId is not null || DirectBranchAvailable)
                .ToList();
            if (records.Count == 0)
                return;

            await ShareWithCommunicationShareesAsync(communicationId, records).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller's own cancellation is not a failure: nothing more is shared, and the next grant brings the records in line.
            _logger.LogInformation("Communication {CommunicationId}: the gate Decision Record mirror was cancelled by its caller (D-169).", communicationId);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "on a communication grant", recordId: null, communicationId);
        }
    }

    /// <summary>
    /// After any re-file of <paramref name="communicationId"/> (<see cref="SecureChildReconciler.AfterRefileAsync"/>, the one step every re-file
    /// writer runs): re-owns each of its gate records still owned by a user to the resolver's ordinary team, reads the owner back, and only then
    /// revokes every share on the record. A Secure answer is left to <see cref="SecureChildReconciler"/>; an unfiled communication (Unchanged)
    /// keeps its records as they are. A record already owned by the resolver's ordinary team that still carries user shares (a revoke that
    /// failed before) has them revoked. Idempotent: a team-owned record with no user share costs one share read and changes nothing. Never
    /// throws; runs on <see cref="CancellationToken.None"/> (the filing has landed).
    /// </summary>
    public async Task ReownAfterFilingAsync(Guid communicationId)
    {
        IReadOnlyList<GateRecord> records;
        try
        {
            records = await FindGateRecordsAsync(communicationId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReownFailed(ex, "its gate Decision Records could not be read", recordId: null, communicationId);
            return;
        }

        // D-215: the communication as the re-file left it, once, only when it has a gate record. A communication that is not found is not
        // Direct-team-owned (and names no pair); one that cannot be read, or whose owner team cannot be identified, changes nothing.
        Entity? communication = null;
        Guid? directTeam = null;
        if (records.Count > 0 && DirectBranchAvailable)
        {
            try
            {
                communication = await ReadCommunicationAsync(communicationId).ConfigureAwait(false);
                if (communication is not null && !IsFiled(communication, communicationId)
                    && communication.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty)
                {
                    var direct = await IsDirectTeamAsync(team).ConfigureAwait(false);
                    if (direct is null)
                    {
                        ReownFailed(null, "whether the communication's owner team is the Direct-message owner team cannot be told; nothing is " +
                            "re-owned, granted or revoked (D-215)", recordId: null, communicationId);
                        return;
                    }

                    directTeam = direct == true ? team : null;
                }
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "the communication could not be read; nothing is re-owned, granted or revoked (D-215)", recordId: null, communicationId);
                return;
            }
        }

        foreach (var record in records)
        {
            try
            {
                if (directTeam is { } direct)
                {
                    // D-215: the re-file left the message Direct-team-owned (an unfile back to Direct): its records follow it.
                    await ReownToDirectAsync(communicationId, communication!, record, direct).ConfigureAwait(false);
                }
                else if (record.OwnerUserId is not null)
                {
                    await ReownAsync(communicationId, record).ConfigureAwait(false);
                }
                else if (DirectBranchAvailable)
                {
                    // D-215 rule 4: a record the Direct team owns is a Direct message's, filed now: re-owned as a user-owned one is, keeping
                    // the recomputed audience's Read. Any other team-owned record: the leftover revoke, which spares that audience.
                    var directRecord = await IsDirectTeamAsync(record.OwnerTeamId!.Value).ConfigureAwait(false);
                    if (directRecord is null)
                    {
                        ReownFailed(null, "whether the record's owner team is the Direct-message owner team cannot be told; it is left as it is " +
                            "(D-215)", record.Id, communicationId);
                    }
                    else if (directRecord == true)
                    {
                        await ReownAsync(communicationId, record, keepAudience: true, communication).ConfigureAwait(false);
                    }
                    else
                    {
                        await RevokeLeftoverSharesAsync(communicationId, record, communication).ConfigureAwait(false);
                    }
                }
                else
                {
                    await RevokeLeftoverSharesAsync(communicationId, record, communication: null).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "the re-own faulted", record.Id, communicationId);
            }
        }
    }

    // ── Share ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <returns><c>true</c> when nothing was shared because the communication is FILED or team-owned (a thread join owns it to a team without stamping
    /// its parent lookups); the caller at write re-owns the record.</returns>
    private async Task<bool> ShareWithCommunicationShareesAsync(Guid communicationId, IReadOnlyList<GateRecord> records)
    {
        var ct = CancellationToken.None;

        var communication = await ReadCommunicationAsync(communicationId).ConfigureAwait(false);
        if (communication is null)
        {
            ShareFailed(null, "the communication could not be read", recordId: null, communicationId);
            return false;
        }

        if (IsFiled(communication, communicationId))
        {
            _logger.LogInformation(
                "Communication {CommunicationId} is filed under an ownership parent; no participant share is added to its gate Decision Records " +
                "(the filing re-owns them, D-169).", communicationId);
            return true;
        }

        // D-215 rule 3, BEFORE the team-owned skip: a communication the Direct-message owner team owns (the one Direct check) is handled, not
        // skipped. An unknown answer changes nothing (no grant, no revoke, and no re-own at write). Any other team falls to the skip below.
        if (DirectBranchAvailable
            && communication.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } owningTeam && owningTeam != Guid.Empty)
        {
            var direct = await IsDirectTeamAsync(owningTeam).ConfigureAwait(false);
            if (direct is null)
            {
                ShareFailed(null, "whether the communication's owner team is the Direct-message owner team cannot be told; nothing is granted, " +
                    "revoked or re-owned (D-215)", recordId: null, communicationId);
                return false;
            }

            if (direct == true)
            {
                await ShareDirectAsync(communicationId, communication, owningTeam, records).ConfigureAwait(false);
                return false;
            }
        }

        if (communication.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty)
        {
            // A thread join stamps only the thread and a team owner, never the communication's own parent lookups, so IsFiled is false here.
            _logger.LogInformation(
                "Communication {CommunicationId} is team-owned; no participant share is added (D-169); at write the record is re-owned " +
                "through the ownership resolver.", communicationId);
            return true;
        }

        if (communication.GetAttributeValue<EntityReference>(OwnerColumn) is not { LogicalName: SystemUserTable })
        {
            _logger.LogInformation(
                "Communication {CommunicationId} is not owned by a user; its gate Decision Records get no participant share (D-169, D-123).",
                communicationId);
            return false;
        }

        // D-169: only user-owned records are shared (the mirror passes every record so the Direct branch above sees the team-owned ones).
        records = records.Where(r => r.OwnerUserId is not null).ToList();
        if (records.Count == 0)
            return false;

        var sharees = (await _shares.GetPrincipalAccessOrThrowAsync(CommunicationTable, communicationId, ct).ConfigureAwait(false))
            .Where(s => (s.AccessRightsMask & ReadAccessMask) == ReadAccessMask)
            .Select(s => s.Principal)
            .Distinct()
            .ToList();
        var users = sharees.Where(p => p.Kind == DataversePrincipalKind.SystemUser).Select(p => p.Id).ToList();
        if (sharees.Count > users.Count)
        {
            _logger.LogInformation(
                "Communication {CommunicationId}: {Count} non-user sharee(s) skipped for its gate Decision Records (internal only, D-169).",
                communicationId, sharees.Count - users.Count);
        }

        if (users.Count == 0)
            return false;

        var internals = await InternalUsersAsync(communicationId, users).ConfigureAwait(false);
        foreach (var record in records)
        {
            await GrantAndConfirmAsync(communicationId, record, internals.Where(u => u != record.OwnerUserId).ToList()).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>The internal users among <paramref name="userIds"/>, from one read of their flags (a fault propagates: nothing is shared).</summary>
    private async Task<IReadOnlyList<Guid>> InternalUsersAsync(Guid communicationId, IReadOnlyList<Guid> userIds)
    {
        var query = new QueryExpression(SystemUserTable)
        {
            ColumnSet = new ColumnSet(ExternalFlagColumn, DomainNameColumn),
            NoLock = true,
        };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.In, userIds.Cast<object>().ToArray());
        var rows = (await _entities.RetrieveMultipleAsync(query, CancellationToken.None).ConfigureAwait(false))?.Entities
            ?? throw new InvalidOperationException("The sharees' external flags could not be read.");

        var internals = new List<Guid>();
        foreach (var userId in userIds)
        {
            var row = rows.FirstOrDefault(r => r.Id == userId);
            if (row is null)
            {
                ShareFailed(null, "a sharee's external flag could not be read; skipped", recordId: null, communicationId);
                continue;
            }

            if (row.GetAttributeValue<bool?>(ExternalFlagColumn) == true
                || ContactBindingDecision.IsGuestDomainName(row.GetAttributeValue<string>(DomainNameColumn)))
            {
                _logger.LogInformation(
                    "Communication {CommunicationId}: sharee {UserId} is external; its gate Decision Records are not shared with them (D-169).",
                    communicationId, userId);
                continue;
            }

            internals.Add(userId);
        }

        return internals;
    }

    /// <summary>
    /// Grants Read on one record to each principal, then reads the record's owner again: when it is no longer a user (a filing re-owned it
    /// in between) or cannot be read, every grant made here is revoked. D-215 rule 3: <paramref name="acceptedTeam"/> (the Direct-message
    /// owner team, for a Direct message's record) is the one team owner also accepted; any other team owner still revokes the pass.
    /// </summary>
    private async Task GrantAndConfirmAsync(Guid communicationId, GateRecord record, IReadOnlyList<Guid> principals, Guid? acceptedTeam = null)
    {
        // 198 pass 2 F3: the record's current shares first (as ReownToDirectAsync reads them). Only the missing are granted, so a rejected
        // read-back revokes only what THIS call granted, never a share held before it (one a filing kept, D-215 rule 4). A read fault grants
        // nothing; the next grant or re-file retries.
        HashSet<Guid> holding;
        try
        {
            holding = (await _shares.GetPrincipalAccessOrThrowAsync(Table, record.Id, CancellationToken.None).ConfigureAwait(false))
                .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && (s.AccessRightsMask & ReadAccessMask) == ReadAccessMask)
                .Select(s => s.Principal.Id)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "the record's shares could not be read before the participant grants; nothing is granted", record.Id, communicationId);
            return;
        }

        var granted = new List<Guid>();
        foreach (var principal in principals.Where(p => !holding.Contains(p)))
        {
            try
            {
                await _shares.GrantAccessAsync(EntitySet, record.Id, DataversePrincipalRef.User(principal), ReadAccessRights, CancellationToken.None)
                    .ConfigureAwait(false);
                granted.Add(principal);
            }
            catch (Exception ex)
            {
                ShareFailed(ex, "a Read grant failed", record.Id, communicationId);
            }
        }

        if (granted.Count == 0)
            return;

        var (owner, ownerReadFault) = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
        if (owner is { Kind: DataversePrincipalKind.SystemUser }
            || (acceptedTeam is { } accepted && accepted != Guid.Empty && owner == DataversePrincipalRef.Team(accepted)))
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} shared Read with {Count} sharee(s) of communication {CommunicationId} (D-169).",
                record.Id, granted.Count, communicationId);
            return;
        }

        if (owner is null)
            ShareFailed(ownerReadFault, "the owner could not be read after the participant grants; they are revoked", record.Id, communicationId);
        else
            _logger.LogInformation(
                "Gate Decision Record {RecordId} is no longer owned by a user after its participant grants; the {Count} grant(s) are revoked " +
                "(D-169, D-123).", record.Id, granted.Count);
        foreach (var principal in granted)
        {
            try
            {
                await _shares.RevokeAccessAsync(EntitySet, record.Id, DataversePrincipalRef.User(principal), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ShareFailed(ex, "a participant grant on a record no longer user-owned could not be revoked", record.Id, communicationId);
            }
        }
    }

    // ── Filing ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The gate writer's own question: the communication first, as content of it, plus the record's live core-ancestor stamps.</summary>
    private Task<RecordOwnerResolution> AskOwnerAsync(Guid communicationId, GateRecord record)
    {
        var parents = new List<RecordOwnershipParent?> { new(CommunicationTable, communicationId) };
        parents.AddRange(record.Stamps);
        return _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(parents) with { KeepCreatorUnlessTargetIsTeamOwned = true }, CancellationToken.None);
    }

    /// <param name="communicationId">The filed communication.</param>
    /// <param name="record">A user-owned record (D-169), or (D-215 rule 4) one the Direct-message owner team owns.</param>
    /// <param name="keepAudience">D-215 rule 4: the record is a Direct message's. A Secure answer, by the resolver or by the one Secure check,
    /// is the Secure mirror's; otherwise the audience recomputed after the read-back keeps its Read and every other share is revoked.</param>
    /// <param name="communication">The communication row (the pair) for <paramref name="keepAudience"/>; <c>null</c> keeps no one.</param>
    private async Task ReownAsync(Guid communicationId, GateRecord record, bool keepAudience = false, Entity? communication = null)
    {
        var answer = await AskOwnerAsync(communicationId, record).ConfigureAwait(false);

        if (answer.IsSecureOwner)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of communication {CommunicationId} belongs to a secure record; SecureChildReconciler re-owns it " +
                "and its shares (D-169).", record.Id, communicationId);
            return;
        }

        if (answer.Outcome == RecordOwnerOutcome.Unchanged)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId}: communication {CommunicationId} still reads as unfiled; the record keeps its owner and shares (D-169).",
                record.Id, communicationId);
            return;
        }

        if (!answer.IsOwned)
        {
            ReownFailed(null, $"the owner was refused ({answer.RefusalCode}); the user owner and the shares are kept", record.Id, communicationId);
            return;
        }

        var team = answer.OwningTeamId!.Value;
        if (keepAudience)
        {
            // D-215 rule 4: before a participant Read is kept, the ONE Secure check on the target. Secure: the Secure mirror decides (nothing is
            // assigned, kept or revoked here, as for the resolver's own Secure answer). Unknown: nothing changes; the next filing retries.
            var (secure, undetermined) = await SecureOfAsync(team).ConfigureAwait(false);
            if (secure == true)
            {
                _logger.LogInformation(
                    "Gate Decision Record {RecordId} of Direct message {CommunicationId} is filed under a secure record; the Secure mirror decides " +
                    "its owner and shares (D-215).", record.Id, communicationId);
                return;
            }

            if (secure is null)
            {
                ReownFailed(null, $"whether the filing target is secure cannot be told ({undetermined}); nothing is re-owned or revoked (D-215)",
                    record.Id, communicationId);
                return;
            }
        }

        Exception? assignFault = null;
        try
        {
            await _writer.AssignAsync(Table, record.Id, new EntityReference("team", team), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            assignFault = ex;
        }

        // Once per write attempt, whatever the read-back says: a write that reports failure can have committed (task 132).
        await _cacheInvalidator
            .InvalidateRecordOwnerChangeAsync(Table, EntitySet, record.Id, $"gate-dr-reown:{record.Id:D}", CancellationToken.None)
            .ConfigureAwait(false);

        var (owner, ownerReadFault) = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
        if (owner != DataversePrincipalRef.Team(team))
        {
            // One warning carries every fault: the Assign's, the owner read's, or both.
            var fault = assignFault is not null && ownerReadFault is not null
                ? new AggregateException(assignFault, ownerReadFault)
                : assignFault ?? ownerReadFault;
            ReownFailed(fault, "the re-own did not read back; the user owner and the shares are kept", record.Id, communicationId);
            return;
        }

        // Only now, with the new owner confirmed: every share on the record goes (no role shares a Decision Record, so nothing else is lost).
        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await _shares.GetPrincipalAccessOrThrowAsync(Table, record.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReownFailed(ex, "re-owned, but its shares could not be read to revoke", record.Id, communicationId);
            return;
        }

        // D-215 rule 4: the audience recomputed now (never a stale copy) keeps its Read. A fault writes nothing more: the shares stay, bounded to
        // what the record already carried, and the next filing's leftover revoke recomputes them.
        IReadOnlySet<Guid> kept = new HashSet<Guid>();
        if (keepAudience && communication is not null)
        {
            try
            {
                kept = ((await DirectAudienceAsync(communicationId, communication).ConfigureAwait(false)).Users ?? []).ToHashSet();
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "re-owned, but the participants could not be recomputed; no share is revoked now (D-215)", record.Id, communicationId);
                return;
            }
        }

        var toRevoke = shares
            .Where(s => !(s.Principal.Kind == DataversePrincipalKind.SystemUser && kept.Contains(s.Principal.Id)))
            .ToList();
        foreach (var share in toRevoke)
        {
            try
            {
                await _shares.RevokeAccessAsync(EntitySet, record.Id, share.Principal, DataversePrincipalRef.Team(team), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "re-owned, but a share could not be revoked", record.Id, communicationId);
            }
        }

        if (keepAudience)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of filed Direct message {CommunicationId} re-owned to team {TeamId}; {Count} share(s) revoked, " +
                "{Kept} participant Read share(s) kept (D-215).", record.Id, communicationId, team, toRevoke.Count, shares.Count - toRevoke.Count);
            return;
        }

        _logger.LogInformation(
            "Gate Decision Record {RecordId} of filed communication {CommunicationId} re-owned to team {TeamId}; {Count} share(s) revoked (D-169).",
            record.Id, communicationId, team, shares.Count);
    }

    /// <summary>
    /// A record already TEAM-owned that may still carry participant shares (a revoke failed after an earlier re-own): when it holds a user share
    /// and the resolver's answer is that same ORDINARY team, every user share is revoked. A Secure answer, a refusal or another team leaves the
    /// record as it is (a Secure-team-owned record's shares are the Secure mirror's). D-215 rule 4: the audience recomputed over
    /// <paramref name="communication"/> (a Direct message's participants who read it now) is not leftover and keeps its Read; with no row, or
    /// a pair that is not valid (every message that is not a Direct one), nobody is spared, as before. A fault in that recompute revokes nothing.
    /// </summary>
    private async Task RevokeLeftoverSharesAsync(Guid communicationId, GateRecord record, Entity? communication)
    {
        var userShares = (await _shares.GetPrincipalAccessOrThrowAsync(Table, record.Id, CancellationToken.None).ConfigureAwait(false))
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser)
            .ToList();
        if (userShares.Count == 0)
            return;

        var answer = await AskOwnerAsync(communicationId, record).ConfigureAwait(false);
        if (answer.IsSecureOwner || !answer.IsOwned || answer.OwningTeamId != record.OwnerTeamId)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of communication {CommunicationId} is team-owned with {Count} user share(s), and its owner is not the " +
                "resolver's ordinary team; its shares are left as they are (D-169, D-123).", record.Id, communicationId, userShares.Count);
            return;
        }

        if (communication is not null)
        {
            IReadOnlyList<Guid> kept;
            try
            {
                kept = (await DirectAudienceAsync(communicationId, communication).ConfigureAwait(false)).Users ?? [];
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "the participants could not be recomputed; no leftover share is revoked now (D-215)", record.Id, communicationId);
                return;
            }

            userShares = userShares.Where(s => !kept.Contains(s.Principal.Id)).ToList();
            if (userShares.Count == 0)
                return;
        }

        var team = record.OwnerTeamId!.Value;
        var revoked = 0;
        foreach (var share in userShares)
        {
            try
            {
                await _shares.RevokeAccessAsync(EntitySet, record.Id, share.Principal, DataversePrincipalRef.Team(team), CancellationToken.None)
                    .ConfigureAwait(false);
                revoked++;
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "a leftover participant share on the team-owned record could not be revoked", record.Id, communicationId);
            }
        }

        _logger.LogInformation(
            "Gate Decision Record {RecordId} of filed communication {CommunicationId}: {Count} of {Leftover} leftover participant share(s) revoked " +
            "(D-169).", record.Id, communicationId, revoked, userShares.Count);
    }

    // ── Direct 1:1 messages (D-215) ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The Direct branches run only with the lookup and the Secure settings (the host injects both).</summary>
    private bool DirectBranchAvailable => _directTeam is not null && _configuration is not null;

    /// <summary>THE one Direct check (<see cref="DirectMessageParticipantShares.IsDirectOwnerTeamAsync"/>; K3): <c>true</c> it is,
    /// <c>false</c> it is not, <c>null</c> it cannot be told.</summary>
    private Task<bool?> IsDirectTeamAsync(Guid team) =>
        DirectMessageParticipantShares.IsDirectOwnerTeamAsync(_entities, _directTeam!, team, _logger, CancellationToken.None);

    /// <summary>THE one Secure check (<see cref="SecureChildShareSynchronizer.IsSecureOwnerTeamAsync"/>); a fault is "cannot be told".</summary>
    private async Task<(bool? Secure, string? Undetermined)> SecureOfAsync(Guid team)
    {
        try
        {
            return await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(_entities, _configuration!, team, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Whether team {TeamId} is the Secure Record team could not be read (D-215).", team);
            return (null, "the Secure Record team could not be read");
        }
    }

    /// <summary>
    /// D-215 rule 2, the audience of a Direct message's gate records: its participant pair (<see cref="DirectMessageParticipantShares.ParticipantsOf"/>
    /// over <paramref name="communication"/>) that holds Read on the communication NOW (its POA shares, read strictly), kept to internal users
    /// (<see cref="InternalUsersAsync"/>). <c>Users</c> is <c>null</c> when the pair is not valid (<c>NoPair</c> says why): nothing is granted or
    /// revoked for it. A fault in either read propagates: the caller writes nothing more.
    /// </summary>
    private async Task<(IReadOnlyList<Guid>? Users, string? NoPair)> DirectAudienceAsync(Guid communicationId, Entity communication)
    {
        var pair = DirectMessageParticipantShares.ParticipantsOf(communication);
        if (pair.Participants is not { } participants)
            return (null, pair.NoPair ?? pair.Unknown ?? "no valid participant pair");

        var readers = (await _shares.GetPrincipalAccessOrThrowAsync(CommunicationTable, communicationId, CancellationToken.None).ConfigureAwait(false))
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && (s.AccessRightsMask & ReadAccessMask) == ReadAccessMask)
            .Select(s => s.Principal.Id)
            .ToHashSet();
        var current = participants.Where(readers.Contains).OrderBy(id => id).ToList();
        if (current.Count == 0)
            return ([], null);

        return (await InternalUsersAsync(communicationId, current).ConfigureAwait(false), null);
    }

    /// <summary>
    /// D-215 rule 3: the gate records of an unfiled, Direct-team-owned communication. A record the Direct team owns is granted the audience
    /// (the owner read-back after the grants accepts exactly that team). A record owned by a user, or by an ordinary team, is re-owned to the
    /// Direct team first (<see cref="ReownToDirectAsync"/>).
    /// </summary>
    private async Task ShareDirectAsync(Guid communicationId, Entity communication, Guid directTeam, IReadOnlyList<GateRecord> records)
    {
        (IReadOnlyList<Guid>? Users, string? NoPair)? audience = null;
        foreach (var record in records)
        {
            if (record.OwnerTeamId != directTeam)
            {
                await ReownToDirectAsync(communicationId, communication, record, directTeam).ConfigureAwait(false);
                continue;
            }

            audience ??= await DirectAudienceAsync(communicationId, communication).ConfigureAwait(false);
            if (audience.Value.Users is not { } users)
            {
                _logger.LogInformation(
                    "Gate Decision Record {RecordId} of Direct message {CommunicationId}: {Reason}; nothing is granted or revoked (D-215).",
                    record.Id, communicationId, audience.Value.NoPair);
                continue;
            }

            await GrantAndConfirmAsync(communicationId, record, users, acceptedTeam: directTeam).ConfigureAwait(false);
            await RevokeOutsideAudienceAsync(communicationId, record, users, directTeam).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 198 pass 2 N3: on a record the Direct team owns, with a valid audience, every user share outside that audience (a revoke that failed
    /// earlier) is revoked, the same revoke <see cref="ReownToDirectAsync"/> sends. Only after the owner is read back as exactly the Direct
    /// team (a record a filing or the Secure reconciler re-owned meanwhile is theirs). A share read or owner read that faults revokes nothing.
    /// <paramref name="everyPrincipal"/> (<see cref="ReownToDirectAsync"/>, F-198-1): a share held by a team, not only a user's, is leftover too.
    /// </summary>
    private async Task RevokeOutsideAudienceAsync(
        Guid communicationId, GateRecord record, IReadOnlyList<Guid> audience, Guid directTeam, bool everyPrincipal = false)
    {
        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await _shares.GetPrincipalAccessOrThrowAsync(Table, record.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "its shares could not be read; no leftover share is revoked (D-215)", record.Id, communicationId);
            return;
        }

        var keep = audience.ToHashSet();
        var leftovers = shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser ? !keep.Contains(s.Principal.Id) : everyPrincipal)
            .Select(s => s.Principal)
            .Distinct()
            .ToList();
        if (leftovers.Count == 0)
            return;

        var (owner, ownerReadFault) = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
        if (owner != DataversePrincipalRef.Team(directTeam))
        {
            if (owner is null)
                ShareFailed(ownerReadFault, "the owner could not be read; no leftover share is revoked (D-215)", record.Id, communicationId);
            return;
        }

        foreach (var principal in leftovers)
        {
            try
            {
                await _shares.RevokeAccessAsync(EntitySet, record.Id, principal, DataversePrincipalRef.Team(directTeam), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "owned by the Direct-message owner team, but a share that is not a participant's could not be revoked (D-215)",
                    record.Id, communicationId);
            }
        }

        _logger.LogInformation(
            "Gate Decision Record {RecordId} of Direct message {CommunicationId}: {Count} leftover non-participant share(s) revoked (D-215).",
            record.Id, communicationId, leftovers.Count);
    }

    /// <summary>
    /// D-215 rules 3 and 5: a gate record of a Direct-team-owned communication follows it to the Direct team, in this order: (a) assigned to
    /// <paramref name="directTeam"/> through the ontology writer's identity, (b) the access caches evicted once per attempt, (c) the owner read
    /// back, and (d) ONLY when the read-back names the Direct team, the audience recomputed, granted, and every other share revoked, each write
    /// re-checking that the owner is still the Direct team (F-198-1: a grant it rejects is undone, a revoke it rejects is not sent). A failed
    /// read-back keeps the owner and the shares (Warning); the communication's next re-file or participant grant retries it. A record the Direct
    /// team already owns skips (a) to (c). A record a team owns that the ONE Secure check names Secure is the Secure reconciler's (nothing here);
    /// an unknown answer changes nothing. An invalid pair, or a fault recomputing the audience, grants and revokes nothing.
    /// </summary>
    private async Task ReownToDirectAsync(Guid communicationId, Entity communication, GateRecord record, Guid directTeam)
    {
        if (record.OwnerTeamId is { } currentTeam && currentTeam != directTeam)
        {
            var (secure, undetermined) = await SecureOfAsync(currentTeam).ConfigureAwait(false);
            if (secure == true)
            {
                _logger.LogInformation(
                    "Gate Decision Record {RecordId} of Direct message {CommunicationId} is Secure-team-owned; the Secure reconciler moves it (D-215).",
                    record.Id, communicationId);
                return;
            }

            if (secure is null)
            {
                ReownFailed(null, $"whether its owner team is the Secure team cannot be told ({undetermined}); it is left as it is (D-215)",
                    record.Id, communicationId);
                return;
            }
        }

        if (record.OwnerTeamId != directTeam)
        {
            Exception? assignFault = null;
            try
            {
                await _writer.AssignAsync(Table, record.Id, new EntityReference("team", directTeam), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                assignFault = ex;
            }

            // Once per attempt, whatever the read-back says: a write that reports failure can have committed (task 132).
            await _cacheInvalidator
                .InvalidateRecordOwnerChangeAsync(Table, EntitySet, record.Id, $"gate-dr-direct:{record.Id:D}", CancellationToken.None)
                .ConfigureAwait(false);

            var (owner, ownerReadFault) = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
            if (owner != DataversePrincipalRef.Team(directTeam))
            {
                var fault = assignFault is not null && ownerReadFault is not null
                    ? new AggregateException(assignFault, ownerReadFault)
                    : assignFault ?? ownerReadFault;
                ReownFailed(fault, "the re-own to the Direct-message owner team did not read back; the owner and the shares are kept, and the " +
                    "next re-file or participant grant retries it (D-215)", record.Id, communicationId);
                return;
            }
        }

        (IReadOnlyList<Guid>? Users, string? NoPair) audience;
        try
        {
            audience = await DirectAudienceAsync(communicationId, communication).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "owned by the Direct-message owner team, but its participants could not be read; nothing is granted or " +
                "revoked (D-215)", record.Id, communicationId);
            return;
        }

        if (audience.Users is not { } users)
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} of Direct message {CommunicationId} is owned by the Direct-message owner team, but {Reason}; " +
                "nothing is granted or revoked (D-215).", record.Id, communicationId, audience.NoPair);
            return;
        }

        // F-198-1: both writes re-read the owner after this pass's reads. A re-file that moved the record meanwhile (the Secure reconciler, or
        // a filing) undoes this pass's grants and leaves every other share to the new owner's path: the same two helpers ShareDirectAsync uses.
        await GrantAndConfirmAsync(communicationId, record, users, acceptedTeam: directTeam).ConfigureAwait(false);
        await RevokeOutsideAudienceAsync(communicationId, record, users, directTeam, everyPrincipal: true).ConfigureAwait(false);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The communication's whole row, as the ownership resolver reads a content row's parent (ReadOwnershipParentsOfAsync): what it is filed
    /// under is every ownership-parent lookup it carries, whichever column holds it; it also carries the owner and (D-215) the participant pair.
    /// <c>null</c> when it is not found; a fault propagates.
    /// </summary>
    private async Task<Entity?> ReadCommunicationAsync(Guid communicationId)
    {
        var query = new QueryExpression(CommunicationTable) { ColumnSet = new ColumnSet(true), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition(CommunicationTable + "id", ConditionOperator.Equal, communicationId);
        return (await _entities.RetrieveMultipleAsync(query, CancellationToken.None).ConfigureAwait(false))?.Entities.FirstOrDefault();
    }

    /// <summary>
    /// The communication's gate records: filed under it, no confirmer (a human review always has one), created by the ontology writer (task 153
    /// F1: a person's own row is never touched), owned by a user or a team, with their live core-ancestor stamps. WhoAmI on the writer's
    /// connection is asked only when a candidate exists. A fault propagates.
    /// </summary>
    private async Task<IReadOnlyList<GateRecord>> FindGateRecordsAsync(Guid communicationId, CancellationToken ct)
    {
        var query = new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(new[] { OwningUserColumn, OwningTeamColumn, DecisionRecordWriter.CreatedByColumn }
                .Concat(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute)).ToArray()),
            TopCount = MaxGateRecords,
            NoLock = true,
        };
        query.Criteria.AddCondition(SubjectLookup, ConditionOperator.Equal, communicationId);
        query.Criteria.AddCondition(ConfirmedByColumn, ConditionOperator.Null);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities;
        if (rows is null || rows.Count == 0)
            return [];

        var writerUserId = await _writer.GetWriterUserIdAsync(ct).ConfigureAwait(false);
        return rows
            .Where(r => r.Id != Guid.Empty
                        && r.GetAttributeValue<EntityReference>(DecisionRecordWriter.CreatedByColumn)?.Id == writerUserId)
            .Select(r => (
                Row: r,
                Team: r.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } t && t != Guid.Empty ? t : (Guid?)null,
                User: r.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } u && u != Guid.Empty ? u : (Guid?)null))
            .Where(r => r.Team is not null || r.User is not null)
            .Select(r => new GateRecord(
                r.Row.Id,
                r.Team is null ? r.User : null,
                r.Team,
                CoreAncestorResolver.CoreAncestorLookups
                    .Select(l => r.Row.GetAttributeValue<EntityReference>(l.LookupAttribute) is { Id: var id } && id != Guid.Empty
                        ? new RecordOwnershipParent(l.EntityType, id)
                        : null)
                    .Where(p => p is not null)
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// The record's owner now, or <c>null</c> when it cannot be read (the caller then fails closed). The read's fault is returned, not logged:
    /// the caller's one failure warning carries it (042b round 4).
    /// </summary>
    private async Task<(DataversePrincipalRef? Owner, Exception? Fault)> ReadOwnerOrNullAsync(Guid recordId)
    {
        try
        {
            var row = await _entities
                .RetrieveAsync(Table, recordId, [OwningUserColumn, OwningTeamColumn], CancellationToken.None)
                .ConfigureAwait(false);
            if (row?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty)
                return (DataversePrincipalRef.Team(team), null);
            if (row?.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } user && user != Guid.Empty)
                return (DataversePrincipalRef.User(user), null);
            return (null, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>
    /// Filed exactly as the ownership resolver decides it for a content row's parent (<c>RecordOwnershipResolver</c>, the
    /// <c>KeepCreatorUnlessTargetIsTeamOwned</c> branch): at least one ownership-parent lookup on the row other than the row itself. A
    /// communication regarding only a contact, an account or an organization is unfiled.
    /// </summary>
    private static bool IsFiled(Entity communication, Guid communicationId) =>
        RecordOwnershipContext.ParentsOf(communication.Attributes, CommunicationTable) // #2084: keyed by the row's table (a communication cites nothing)
            .Any(parent => parent != new RecordOwnershipParent(CommunicationTable, communicationId));

    // ── Failures: Warning, ids only, counted ───────────────────────────────────────────────────────────────────

    private void ShareFailed(Exception? ex, string what, Guid? recordId, Guid communicationId)
    {
        _logger.LogWarning(DecisionRecordEvents.ParticipantShareFailed, ex,
            "Gate Decision Record participant share (D-169): {What} (record {RecordId}, communication {CommunicationId}); nothing is shared " +
            "beyond the communication's sharees and the record stays owner-visible.", what, recordId, communicationId);
        OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.ParticipantShareFailed);
    }

    private void ReownFailed(Exception? ex, string what, Guid? recordId, Guid communicationId)
    {
        _logger.LogWarning(DecisionRecordEvents.FilingReownFailed, ex,
            "Gate Decision Record re-own after filing (D-169): {What} (record {RecordId}, communication {CommunicationId}); the filing stands.",
            what, recordId, communicationId);
        OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.FilingReownFailed);
    }

    /// <summary>One gate record: its owning user OR its owning team, and the core-ancestor stamps it carries now.</summary>
    private sealed record GateRecord(Guid Id, Guid? OwnerUserId, Guid? OwnerTeamId, IReadOnlyList<RecordOwnershipParent?> Stamps);
}
