using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a: the ONE step that runs after a message joins a thread, on every join path
/// (the send with no thread id, the respond and routed sends, and the inbound capture). It grants the message to the thread's
/// audience, then, for a Direct message filed under no record, assigns the message and its content rows to the configured Direct
/// message owner team (<see cref="DirectMessageOwnerTeam"/>). It is also the one pre-send check of that team for a 1:1 message
/// (<see cref="CheckBeforeSendAsync"/>).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> (1) The grant (<see cref="IDirectThreadAccessService.GrantMessageAccessAsync"/>) while the message is still
/// owned by its creator, so its participants read it whatever happens next. (2) Only a <see cref="ThreadGrantAudience.DirectParticipants"/>
/// join goes on. (3) The lookup: <see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/> stops here with nothing written and nothing
/// refused. (4) The message, read whole: it must be a Message, owned by a user, filed under nothing (no ownership parent and no typed
/// regarding lookup), filed into the joined thread, and carry exactly two distinct non-empty systemusers in <c>sprk_sentby</c> and
/// <c>sprk_directrecipient</c> (<see cref="DirectMessageParticipantShares.ParticipantsOf"/>); otherwise it keeps its owner. (5) A lookup
/// that is not resolved (any other refusal, including <see cref="DirectMessageOwnerTeamRefusal.PartiallyConfigured"/>, or a fault)
/// keeps the owner, with an Error. There is no default-team fallback, and the message is never dropped. (6) The owner write, conditional
/// on the version read in (4), evicted and read back. (7) Each content row (<see cref="RestrictedFilingShareBar.ContentRows"/>) that
/// had the message's previous owner follows it, in its own conditional write; a row that is refused or faults stays where it is.
/// (8) The grant again: the message is now Direct-team owned, so the grant takes its participant branch (uac-r2 task 198) and its
/// gate Decision Records follow the message to the Direct team as <see cref="Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing"/>
/// defines it (D-215). No owner is ever rolled back.</para>
/// <para><b>Owner writes.</b> Every owner write of this class is <see cref="WriteDirectTeamOwnerAsync"/>: an owner-only update with
/// a typed team reference, conditional on the row version. The architecture test <c>DirectMessageOwnerStepGuardTests</c> holds it
/// as the only member that may assign a communication to the Direct team.</para>
/// </remarks>
public sealed class DirectMessageOwnerStep
{
    /// <summary>Error: the message is eligible for the Direct team but the lookup did not resolve; it keeps its owner.</summary>
    internal const string OwnerNotSetEventCode = "direct_message.owner_team_not_set";

    /// <summary>Error: the message's owner write was refused, faulted or did not read back; it keeps its owner.</summary>
    internal const string MessageNotMovedEventCode = "direct_message.owner_write_not_confirmed";

    /// <summary>Error: a content row of a message moved to the Direct team did not move with it.</summary>
    internal const string ContentRowNotMovedEventCode = "direct_message.content_row_not_moved";

    /// <summary>Warning: a 1:1 message send was refused before anything was sent.</summary>
    internal const string SendRefusedEventCode = "direct_message.send_refused";

    internal static readonly EventId OwnerNotSetEvent = new(5414, OwnerNotSetEventCode);
    internal static readonly EventId MessageNotMovedEvent = new(5415, MessageNotMovedEventCode);
    internal static readonly EventId ContentRowNotMovedEvent = new(5416, ContentRowNotMovedEventCode);
    internal static readonly EventId SendRefusedEvent = new(5417, SendRefusedEventCode);

    private const string CommunicationTable = "sprk_communication";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string TypeColumn = "sprk_communicationtype";
    private const string OwnerColumn = "ownerid";
    private const string OwningTeamColumn = "owningteam";
    private const string VersionColumn = "versionnumber";
    private const string EvictionReason = "direct-message-owner-step";

    private readonly IDirectThreadAccessService _access;
    private readonly IGenericEntityService _dataverse;
    private readonly DirectMessageOwnerTeam _lookup;
    private readonly IMembershipCacheInvalidator? _cacheInvalidator;
    private readonly ILogger<DirectMessageOwnerStep> _logger;

    public DirectMessageOwnerStep(
        IDirectThreadAccessService access,
        IGenericEntityService dataverse,
        DirectMessageOwnerTeam lookup,
        ILogger<DirectMessageOwnerStep> logger,
        IMembershipCacheInvalidator? cacheInvalidator = null)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheInvalidator = cacheInvalidator;
    }

    /// <summary>
    /// Grants <paramref name="communicationId"/> to the audience its join fixed, then (Direct joins only) assigns it and its content
    /// rows to the Direct message owner team. Never throws for a Dataverse fault; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    public async Task GrantThenOwnDirectAsync(Guid communicationId, ThreadAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Message {CommunicationId} granted to its thread's {Audience} audience (ThreadId={ThreadId}).",
            communicationId, assignment.Audience, assignment.ThreadId);

        if (assignment.Audience != ThreadGrantAudience.DirectParticipants)
            return;

        try
        {
            if (await OwnDirectAsync(communicationId, assignment.ThreadId, ct).ConfigureAwait(false))
                await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(OwnerNotSetEvent, ex,
                "Message {CommunicationId} keeps its owner: the Direct owner step faulted (ThreadId={ThreadId}).",
                communicationId, assignment.ThreadId);
        }
    }

    /// <summary>
    /// The pre-send check of a 1:1 message: <see cref="DirectOwnerSendCheck.Proceed"/> when the Direct team resolves or no Direct
    /// setting is configured; <see cref="DirectOwnerSendCheck.Refused"/> for any other refusal (a configuration problem);
    /// <see cref="DirectOwnerSendCheck.Unavailable"/> when the lookup could not read Dataverse. Never throws for a fault.
    /// </summary>
    public async Task<DirectOwnerSendCheck> CheckBeforeSendAsync(CancellationToken ct)
    {
        DirectMessageOwnerTeamResolution resolution;
        try
        {
            resolution = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(SendRefusedEvent, ex, "A 1:1 message send was refused: the Direct message owner team could not be read.");
            return DirectOwnerSendCheck.Unavailable;
        }

        if (resolution.Fault is not null)
        {
            _logger.LogWarning(SendRefusedEvent, resolution.Fault,
                "A 1:1 message send was refused: the Direct message owner team could not be read ({Refusal}).", resolution.Refusal);
            return DirectOwnerSendCheck.Unavailable;
        }

        if (resolution.IsResolved || resolution.IsNotConfigured)
            return DirectOwnerSendCheck.Proceed;

        _logger.LogWarning(SendRefusedEvent,
            "A 1:1 message send was refused: the Direct message owner team is not usable ({Refusal}, missing setting {MissingSetting}).",
            resolution.Refusal, resolution.MissingSetting);
        return DirectOwnerSendCheck.Refused(resolution.Refusal ?? DirectMessageOwnerTeamRefusal.PartiallyConfigured);
    }

    /// <summary>
    /// Steps 3 to 7 of the class remarks. <c>true</c> when the message now reads back as owned by the Direct team.
    /// </summary>
    private async Task<bool> OwnDirectAsync(Guid communicationId, Guid threadId, CancellationToken ct)
    {
        var resolution = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
        if (resolution.IsNotConfigured && resolution.Fault is null)
        {
            _logger.LogDebug("Message {CommunicationId} keeps its owner: no Direct message owner team is configured.", communicationId);
            return false;
        }

        var message = await ReadMessageAsync(communicationId, ct).ConfigureAwait(false);
        if (message is null || message.GetAttributeValue<long?>(VersionColumn) is not { } version)
        {
            _logger.LogError(MessageNotMovedEvent,
                "Message {CommunicationId} keeps its owner: it could not be read before its Direct owner write.", communicationId);
            return false;
        }

        if (WhyNotDirect(message, threadId) is { } reason)
        {
            _logger.LogInformation("Message {CommunicationId} keeps its owner: {Reason}.", communicationId, reason);
            return false;
        }

        if (!resolution.IsResolved || resolution.TeamId is not { } teamId || teamId == Guid.Empty)
        {
            _logger.LogError(OwnerNotSetEvent, resolution.Fault,
                "Message {CommunicationId} keeps its owner: the Direct message owner team did not resolve ({Refusal}, missing setting " +
                "{MissingSetting}).", communicationId, resolution.Refusal, resolution.MissingSetting);
            return false;
        }

        var previousOwner = message.GetAttributeValue<EntityReference>(OwnerColumn)!;
        if (await WriteDirectTeamOwnerAsync(CommunicationTable, communicationId, version, teamId, ct).ConfigureAwait(false) is { } fault)
        {
            _logger.LogError(MessageNotMovedEvent,
                "Message {CommunicationId} keeps its owner: its Direct owner write was not confirmed ({Fault}).", communicationId, fault);
            return false;
        }

        await MoveContentRowsAsync(communicationId, previousOwner, teamId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Why the message is not a Direct message this step may assign, or <c>null</c> when it is.</summary>
    private static string? WhyNotDirect(Entity message, Guid threadId)
    {
        if (message.GetAttributeValue<OptionSetValue>(TypeColumn)?.Value != (int)CommunicationType.Message)
            return "it is not a Message";
        if (message.GetAttributeValue<EntityReference>(OwnerColumn) is not { } owner
            || !string.Equals(owner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase) || owner.Id == Guid.Empty
            || message.GetAttributeValue<EntityReference>(OwningTeamColumn) is { Id: var team } && team != Guid.Empty)
            return "it is not owned by a user";
        if (Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ParentsOf(message).Count > 0
            || DirectMessageRecipient.CarriesATypedRegarding(message))
            return "it is filed under a record";
        if (message.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } filedThread || filedThread != threadId)
            return "it is not filed into the joined thread";
        return DirectMessageParticipantShares.ParticipantsOf(message) is { Participants: null } derivation
            ? $"it names no Direct pair ({derivation.NoPair ?? derivation.Unknown})"
            : null;
    }

    private async Task<Entity?> ReadMessageAsync(Guid communicationId, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(CommunicationTable) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Criteria.AddCondition(CommunicationTable + "id", ConditionOperator.Equal, communicationId);
            return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Message {CommunicationId} could not be read for its Direct owner step.", communicationId);
            return null;
        }
    }

    /// <summary>Step 7: each content row that had the message's previous owner follows it, each in its own write.</summary>
    private async Task MoveContentRowsAsync(Guid communicationId, EntityReference previousOwner, Guid teamId, CancellationToken ct)
    {
        foreach (var (table, lookup) in RestrictedFilingShareBar.ContentRows)
        {
            IReadOnlyList<Entity> rows;
            try
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = new ColumnSet(OwnerColumn, VersionColumn),
                    TopCount = RestrictedFilingShareBar.MaxRowsPerTable,
                };
                query.Criteria.AddCondition(lookup, ConditionOperator.Equal, communicationId);
                rows = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.ToList() ?? [];
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ContentRowNotMovedEvent, ex,
                    "Message {CommunicationId} moved to the Direct team, but its {Table} rows could not be read; they stay where they are.",
                    communicationId, table);
                continue;
            }

            if (rows.Count >= RestrictedFilingShareBar.MaxRowsPerTable)
            {
                _logger.LogError(ContentRowNotMovedEvent,
                    "Message {CommunicationId} moved to the Direct team; its {Table} read reached its cap of {Cap}, so rows beyond it stay " +
                    "where they are.", communicationId, table, RestrictedFilingShareBar.MaxRowsPerTable);
            }

            foreach (var row in rows)
            {
                if (row.Id == Guid.Empty
                    || row.GetAttributeValue<EntityReference>(OwnerColumn) is not { } owner
                    || owner.Id != previousOwner.Id
                    || !string.Equals(owner.LogicalName, previousOwner.LogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fault = row.GetAttributeValue<long?>(VersionColumn) is { } version
                    ? await WriteDirectTeamOwnerAsync(table, row.Id, version, teamId, ct).ConfigureAwait(false)
                    : "its version could not be read";
                if (fault is not null)
                {
                    _logger.LogError(ContentRowNotMovedEvent,
                        "Message {CommunicationId} moved to the Direct team, but its {Table} row {RowId} stays where it is ({Fault}).",
                        communicationId, table, row.Id, fault);
                }
            }
        }
    }

    /// <summary>
    /// THE owner write of this class: the row to the Direct team, owner only, conditional on <paramref name="version"/>, then the
    /// owner-change eviction (once per attempt that may have landed) and a read-back. <c>null</c> when the row reads back as owned
    /// by the team; otherwise what went wrong. Never throws for a Dataverse fault.
    /// </summary>
    private async Task<string?> WriteDirectTeamOwnerAsync(string table, Guid id, long version, Guid teamId, CancellationToken ct)
    {
        var owner = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", teamId) };
        string? writeFault = null;
        try
        {
            await _dataverse.UpdateIfRowVersionMatchesAsync(
                table, id, version.ToString(System.Globalization.CultureInfo.InvariantCulture), owner, ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            return "the row changed after it was read; nothing was written";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            writeFault = ex.Message;
        }

        await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);
        return await ReadBackAsync(table, id, teamId, writeFault, ct).ConfigureAwait(false);
    }

    private async Task EvictOwnerChangeAsync(string table, Guid id)
    {
        if (_cacheInvalidator is null || !SecureChildLineage.Children.TryGetValue(table, out var lineage))
            return;
        try
        {
            await _cacheInvalidator.InvalidateRecordOwnerChangeAsync(table, lineage.EntitySet, id, EvictionReason, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evicting the caches after re-owning {Table} {Id} failed; the TTLs are the backstop.", table, id);
        }
    }

    private async Task<string?> ReadBackAsync(string table, Guid id, Guid teamId, string? writeFault, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(OwningTeamColumn), TopCount = 1 };
            query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
            var after = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
            if (after?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } owningTeam && owningTeam == teamId)
                return null;
            return writeFault is null ? "the write was accepted but did not read back" : $"Dataverse refused the write ({writeFault})";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return writeFault is null ? $"the read-back failed ({ex.Message})" : $"Dataverse refused the write ({writeFault})";
        }
    }
}

/// <summary>The pre-send check of a 1:1 message (<see cref="DirectMessageOwnerStep.CheckBeforeSendAsync"/>).</summary>
public sealed record DirectOwnerSendCheck(bool CanSend, bool IsTransient, string? RefusalCode)
{
    /// <summary>Send: the Direct team resolved, or no Direct setting is configured.</summary>
    public static readonly DirectOwnerSendCheck Proceed = new(true, false, null);

    /// <summary>Do not send, try again: the lookup could not read Dataverse.</summary>
    public static readonly DirectOwnerSendCheck Unavailable = new(false, true, DirectOwnerSendUnavailableCode);

    /// <summary>The stable code of <see cref="Unavailable"/>.</summary>
    public const string DirectOwnerSendUnavailableCode = "sdap.communication.direct_owner_team.unavailable";

    /// <summary>Do not send: the Direct message owner team is configured in a way that cannot be used.</summary>
    public static DirectOwnerSendCheck Refused(string refusalCode) => new(false, false, refusalCode);
}
