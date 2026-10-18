using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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
/// shared), its POA shares are read (the strict read: a failed read is not "no shares"), the systemusers holding Read are kept, the externals are
/// dropped, and each user-owned gate record gets a Read share for each of them except its owner. So whichever of the record and the
/// communication share comes first, the record ends with the communication's sharees.</para>
/// <para><b>Internal only</b> (owner, 2026-10-18). A sharee is skipped when it is a team (no flag to read), when its <c>systemuser</c> row is
/// missing or cannot be read, when <c>sprk_isexternal</c> is <c>true</c>, or when its <c>domainname</c> marks a B2B guest
/// (<see cref="ContactBindingDecision.IsGuestDomainName"/>). A blank flag is internal (owner round 67: "sprk_isexternal means they're
/// external", the rule <c>ISystemUserIdentityResolver.IsExternalAsync</c> applies).</para>
/// <para><b>Never a team-owned record</b> (D-123). Only a record whose owner is a user is shared, and after each pass's grants the owner is read
/// again: when the record is now owned by a team (a filing re-owned it in between) or the owner cannot be read, every grant the pass made is
/// revoked. A communication owned by any team (the Secure Record team, or a category-routed team) is never mirrored.</para>
/// <para><b>Filing</b> (<see cref="ReownAfterFilingAsync"/>, called by <c>ReconciledEmailFiling</c>). Each user-owned gate record of the
/// communication is asked of <see cref="IRecordOwnershipResolver"/> with the gate writer's own question (the communication first, as content
/// of it, plus the record's live core-ancestor stamps). An ordinary team answer is assigned through the ontology writer's identity
/// (<c>prvAssignsprk_DecisionRecord</c>, D-33c; it holds no Write on the table), the access caches are evicted, and the owner is read back;
/// ONLY a read-back naming that team revokes every share on the record. A Secure answer is <see cref="SecureChildReconciler"/>'s (nothing is
/// assigned or revoked here). Any other outcome keeps the user owner and the shares: the over-share stays bounded to the participants and the
/// record is never orphaned.</para>
/// <para><b>Best-effort, fail closed on visibility.</b> No public member throws. A failed read, grant, assign or revoke is logged at Warning
/// with ids only and counted on <c>ontology.writer.failures</c>; the record stays written and owner-visible, and nothing is shared beyond the
/// communication's sharees.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="DecisionRecordWriter"/> is create-only and guarded
/// (<c>DecisionRecordContextImmutabilityTests</c>), <c>DirectThreadAccessService</c> grants communications (uac-r2), and
/// <c>SecureChildReconciler.ReownOrdinaryRowAsync</c> re-owns team-owned rows and refuses a user-owned one. Extension: the writer cannot
/// assign without breaking its create-only guard, and the other two are uac-r2's; one helper keeps the three call sites to one call each.
/// Cost of doing nothing: D-169 fails (the record stays visible to the communication's owner only) and a filed communication leaves its
/// participants' shares on a user-owned record.</para>
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
    private const string PairColumn = "sprk_regardingrecordid";
    private const string ReadAccessRights = "ReadAccess";

    /// <summary>Dataverse's <c>AccessRights.ReadAccess</c> bit in a share row's <c>accessrightsmask</c>.</summary>
    private const int ReadAccessMask = 1;

    /// <summary>Gate records per communication are at most two (authorize and deny, task 149); the cap is defensive.</summary>
    private const int MaxGateRecords = 10;

    /// <summary>The Decision Record's lookup to the communication it is filed under (the shared regarding builder's map, D-133).</summary>
    private static readonly string SubjectLookup = TodoRegardingBuilder.RegardingLookupsByHostTable[Table][CommunicationTable];

    /// <summary>Every column that says what a communication is filed under: the regarding family and the ADR-024 pair.</summary>
    private static readonly string[] CommunicationFilingColumns = RegardingFieldMap.AllRegardingFields.Append(PairColumn).ToArray();

    private readonly IGenericEntityService _entities;
    private readonly IDataverseRecordShareService _shares;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IMembershipCacheInvalidator _cacheInvalidator;
    private readonly ILogger<GateDecisionRecordSharing> _logger;

    /// <param name="entities">The shared sysadmin reader: the communication, its sharees' flags, the records and their owners (reads only).</param>
    /// <param name="shares">The BFF's one POA seam (the BFF app identity): every grant, read and revoke of a share.</param>
    /// <param name="writer">The ontology writer's connection: its identity (WhoAmI, so only records it created are touched) and the Assign.</param>
    /// <param name="ownership">uac-r2's owner resolver: the owner a filed communication's gate record takes.</param>
    /// <param name="cacheInvalidator">The owner-change eviction every owner-changing writer calls (task 132).</param>
    /// <param name="logger">Warnings (ids only) and decisions.</param>
    public GateDecisionRecordSharing(
        IGenericEntityService entities,
        IDataverseRecordShareService shares,
        OntologyWriterDataverseClient writer,
        IRecordOwnershipResolver ownership,
        IMembershipCacheInvalidator cacheInvalidator,
        ILogger<GateDecisionRecordSharing> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _cacheInvalidator = cacheInvalidator ?? throw new ArgumentNullException(nameof(cacheInvalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// After the gate record of an unfiled communication was created owned by <paramref name="ownerUserId"/>: shares it, Read only, with the
    /// communication's current internal sharees. Never throws; runs on <see cref="CancellationToken.None"/> (the record has landed).
    /// </summary>
    public async Task ShareAtWriteAsync(Guid recordId, Guid communicationId, Guid ownerUserId)
    {
        try
        {
            await ShareWithCommunicationShareesAsync(communicationId, [new GateRecord(recordId, ownerUserId, [])]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "at write", recordId, communicationId);
        }
    }

    /// <summary>
    /// After a principal was granted Read on <paramref name="communicationId"/>: brings its user-owned gate records in line with the
    /// communication's current internal sharees. One query when the communication has no such record. Never throws.
    /// </summary>
    public async Task MirrorCommunicationSharesAsync(Guid communicationId, CancellationToken ct)
    {
        try
        {
            var records = await FindUserOwnedGateRecordsAsync(communicationId, ct).ConfigureAwait(false);
            if (records.Count == 0)
                return;

            await ShareWithCommunicationShareesAsync(communicationId, records).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShareFailed(ex, "on a communication grant", recordId: null, communicationId);
        }
    }

    /// <summary>
    /// After an Office save filed <paramref name="communicationId"/>: re-owns each of its gate records still owned by a user to the resolver's
    /// ordinary team, reads the owner back, and only then revokes every share on the record. A Secure answer is left to
    /// <see cref="SecureChildReconciler"/>. Never throws; runs on <see cref="CancellationToken.None"/> (the filing has landed).
    /// </summary>
    public async Task ReownAfterFilingAsync(Guid communicationId)
    {
        IReadOnlyList<GateRecord> records;
        try
        {
            records = await FindUserOwnedGateRecordsAsync(communicationId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReownFailed(ex, "its gate Decision Records could not be read", recordId: null, communicationId);
            return;
        }

        foreach (var record in records)
        {
            try
            {
                await ReownAsync(communicationId, record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReownFailed(ex, "the re-own faulted", record.Id, communicationId);
            }
        }
    }

    // ── Share ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task ShareWithCommunicationShareesAsync(Guid communicationId, IReadOnlyList<GateRecord> records)
    {
        var ct = CancellationToken.None;
        var communication = await _entities
            .RetrieveAsync(CommunicationTable, communicationId, CommunicationFilingColumns.Append(OwnerColumn).Append(OwningTeamColumn).ToArray(), ct)
            .ConfigureAwait(false);
        if (communication is null)
        {
            ShareFailed(null, "the communication could not be read", recordId: null, communicationId);
            return;
        }

        if (IsFiled(communication))
        {
            _logger.LogInformation(
                "Communication {CommunicationId} is filed; its gate Decision Records take the team-owned path and get no participant share (D-169).",
                communicationId);
            return;
        }

        if (communication.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty
            || communication.GetAttributeValue<EntityReference>(OwnerColumn) is not { LogicalName: SystemUserTable })
        {
            _logger.LogInformation(
                "Communication {CommunicationId} is not owned by a user; its gate Decision Records get no participant share (D-169, D-123).",
                communicationId);
            return;
        }

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
            return;

        var internals = await InternalUsersAsync(communicationId, users).ConfigureAwait(false);
        foreach (var record in records)
        {
            await GrantAndConfirmAsync(communicationId, record, internals.Where(u => u != record.OwnerUserId).ToList()).ConfigureAwait(false);
        }
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
    /// in between) or cannot be read, every grant made here is revoked.
    /// </summary>
    private async Task GrantAndConfirmAsync(Guid communicationId, GateRecord record, IReadOnlyList<Guid> principals)
    {
        var granted = new List<Guid>();
        foreach (var principal in principals)
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

        var owner = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
        if (owner is { Kind: DataversePrincipalKind.SystemUser })
        {
            _logger.LogInformation(
                "Gate Decision Record {RecordId} shared Read with {Count} sharee(s) of communication {CommunicationId} (D-169).",
                record.Id, granted.Count, communicationId);
            return;
        }

        _logger.LogInformation(
            "Gate Decision Record {RecordId} is no longer owned by a user (or its owner cannot be read) after its participant grants; the {Count} " +
            "grant(s) are revoked (D-169, D-123).", record.Id, granted.Count);
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

    private async Task ReownAsync(Guid communicationId, GateRecord record)
    {
        var parents = new List<RecordOwnershipParent?> { new(CommunicationTable, communicationId) };
        parents.AddRange(record.Stamps);
        var answer = await _ownership
            .ResolveOwnerAsync(RecordOwnershipContext.ForParents(parents) with { KeepCreatorUnlessTargetIsTeamOwned = true }, CancellationToken.None)
            .ConfigureAwait(false);

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

        var owner = await ReadOwnerOrNullAsync(record.Id).ConfigureAwait(false);
        if (owner != DataversePrincipalRef.Team(team))
        {
            ReownFailed(assignFault, "the re-own did not read back; the user owner and the shares are kept", record.Id, communicationId);
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

        foreach (var share in shares)
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

        _logger.LogInformation(
            "Gate Decision Record {RecordId} of filed communication {CommunicationId} re-owned to team {TeamId}; {Count} share(s) revoked (D-169).",
            record.Id, communicationId, team, shares.Count);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The communication's gate records still owned by a user: filed under it, no confirmer (a human review always has one), created by the
    /// ontology writer (task 153 F1: a person's own row is never touched), with their live core-ancestor stamps. WhoAmI on the writer's
    /// connection is asked only when a candidate exists. A fault propagates.
    /// </summary>
    private async Task<IReadOnlyList<GateRecord>> FindUserOwnedGateRecordsAsync(Guid communicationId, CancellationToken ct)
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
        query.Criteria.AddCondition(OwningUserColumn, ConditionOperator.NotNull);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities;
        if (rows is null || rows.Count == 0)
            return [];

        var writerUserId = await _writer.GetWriterUserIdAsync(ct).ConfigureAwait(false);
        return rows
            .Where(r => r.Id != Guid.Empty
                        && r.GetAttributeValue<EntityReference>(DecisionRecordWriter.CreatedByColumn)?.Id == writerUserId
                        && r.GetAttributeValue<EntityReference>(OwningTeamColumn) is null)
            .Select(r => (Row: r, Owner: r.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id))
            .Where(r => r.Owner is { } owner && owner != Guid.Empty)
            .Select(r => new GateRecord(
                r.Row.Id,
                r.Owner!.Value,
                CoreAncestorResolver.CoreAncestorLookups
                    .Select(l => r.Row.GetAttributeValue<EntityReference>(l.LookupAttribute) is { Id: var id } && id != Guid.Empty
                        ? new RecordOwnershipParent(l.EntityType, id)
                        : null)
                    .Where(p => p is not null)
                    .ToList()))
            .ToList();
    }

    /// <summary>The record's owner now, or <c>null</c> when it cannot be read (the caller then fails closed).</summary>
    private async Task<DataversePrincipalRef?> ReadOwnerOrNullAsync(Guid recordId)
    {
        try
        {
            var row = await _entities
                .RetrieveAsync(Table, recordId, [OwningUserColumn, OwningTeamColumn], CancellationToken.None)
                .ConfigureAwait(false);
            if (row?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty)
                return DataversePrincipalRef.Team(team);
            if (row?.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } user && user != Guid.Empty)
                return DataversePrincipalRef.User(user);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gate Decision Record {RecordId}: its owner could not be read.", recordId);
            return null;
        }
    }

    private static bool IsFiled(Entity communication) =>
        CommunicationFilingColumns.Any(column => communication.Attributes.TryGetValue(column, out var value) && value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true,
        });

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

    /// <summary>One user-owned gate record: its owner and the core-ancestor stamps it carries now.</summary>
    private sealed record GateRecord(Guid Id, Guid OwnerUserId, IReadOnlyList<RecordOwnershipParent?> Stamps);
}
