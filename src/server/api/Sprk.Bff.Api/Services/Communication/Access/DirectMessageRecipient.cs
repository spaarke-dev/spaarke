using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// A Direct message's own two participants (task 054, owner 2026-11-09, option A): <c>sprk_sentby</c> and
/// <c>sprk_directrecipient</c>, stamped by the BFF in the write that files the message into a Direct thread — the thread
/// lookup and both participants land in ONE update, so a message never sits in a Direct thread without them when a
/// recipient is known. Every path that puts a message in a thread does so after the message exists (the thread is found or
/// created from the persisted row, or named by the caller and joined afterwards), so that write is the first one in which
/// the message is a Direct message.
/// </summary>
/// <remarks>
/// <para><b>Which messages.</b> A Message row (<c>sprk_communicationtype</c> = Message) that carries no typed regarding lookup
/// and joins a thread the join found Direct (<see cref="ThreadGrantAudience.DirectParticipants"/>: typed Direct, no typed
/// lookup). Every other row — an email, a record-filed message, the per-user master, a record thread — gets no column from
/// here.</para>
/// <para><b>The recipient</b> is the thread's other participant. The two-party list is read here (<see
/// cref="ReadParticipantsAsync"/>): the thread's user owner and the systemusers holding a share on the thread that grants
/// Read, empty unless the thread is typed Direct. The share read is the strict one
/// (<see cref="IDataverseRecordShareService.GetPrincipalAccessOrThrowAsync"/>): a read that fails stamps nothing, it is never
/// taken as "no shares". The list must be exactly two distinct, non-empty systemusers, one of them the sender, both enabled
/// and neither an application user; otherwise no recipient is stamped.</para>
/// <para><b>Why its own read.</b> The stamp decides a write from the shares, so it needs the strict read; the shared reader
/// (<see cref="IDirectThreadAccessService.GetParticipantSystemUserIdsAsync"/>) uses the soft one. This read is a deliberate
/// duplicate of that reader's rule and converges onto it once the shared reader filters by Read and reads strictly.</para>
/// <para><b>The thread's owner decides the pair.</b> Whoever owns the thread and whom it is shared to are what this reads;
/// a thread whose owner changes its shares before a message is filed yields the pair as it stands at the filing.</para>
/// <para><b>The sender</b> is the row's <c>sprk_sentby</c>, kept as it is when present. A row without one (an inbound chat
/// message) takes the systemuser its chat identity maps to through the existing identity map
/// (<c>systemuser.sprk_communicationuserid</c>, written by <c>AcsIdentityService</c>), and the sender is stamped with the
/// recipient. When the sender cannot be mapped, nothing is stamped and one Error is logged with
/// <see cref="RecipientUnmappedEventCode"/> and the message id.</para>
/// <para><b>Never fails the message.</b> Any fault stamps nothing and is logged; the thread lookup is still written.</para>
/// <para><b>Deploy order.</b> The schema script runs before a BFF built with this stamp deploys; when it has not, the thread
/// lookup is still written and one Error names the script (<see cref="WriteThreadFilingAsync"/>).</para>
/// <para>Written as the BFF application user (<see cref="IGenericEntityService"/>), never with an impersonated caller.</para>
/// </remarks>
internal static class DirectMessageRecipient
{
    /// <summary>The recipient lookup on <c>sprk_communication</c> (schema <c>sprk_DirectRecipient</c>, to systemuser).</summary>
    internal const string RecipientColumn = "sprk_directrecipient";

    /// <summary>The sender lookup on <c>sprk_communication</c> (to systemuser).</summary>
    internal const string SenderColumn = "sprk_sentby";

    /// <summary>The stable event code of the one Error logged when a Direct message's sender cannot be mapped to a systemuser.</summary>
    internal const string RecipientUnmappedEventCode = "direct_message.recipient_unmapped";

    private static readonly EventId RecipientUnmappedEvent = new(5401, RecipientUnmappedEventCode);

    /// <summary>Information: the Direct thread does not hold exactly two distinct participants, one of them the sender.</summary>
    internal const string NotTwoParticipantsEventCode = "direct_message.recipient_not_two_participants";

    /// <summary>Information: one of the two participants is disabled, missing, or an application user.</summary>
    internal const string ParticipantNotEligibleEventCode = "direct_message.recipient_participant_not_eligible";

    /// <summary>Warning: the message, the thread or the thread's shares could not be read.</summary>
    internal const string ParticipantsUnreadEventCode = "direct_message.recipient_participants_unread";

    /// <summary>
    /// Error: the filing write was refused because a stamp column does not exist on the table (the schema script has not run
    /// in this environment); the message was filed with the thread lookup alone.
    /// </summary>
    internal const string RecipientColumnMissingEventCode = "direct_message.recipient_column_missing";

    /// <summary>The script that adds the stamp's column, named by the <see cref="RecipientColumnMissingEventCode"/> Error.</summary>
    internal const string SchemaScriptCommand = "scripts/Set-DirectRecipientSchema.ps1 -Apply";

    private static readonly EventId NotTwoParticipantsEvent = new(5402, NotTwoParticipantsEventCode);
    private static readonly EventId ParticipantNotEligibleEvent = new(5403, ParticipantNotEligibleEventCode);
    private static readonly EventId ParticipantsUnreadEvent = new(5404, ParticipantsUnreadEventCode);
    private static readonly EventId RecipientColumnMissingEvent = new(5405, RecipientColumnMissingEventCode);

    private const string CommunicationTable = "sprk_communication";
    private const string CommunicationTypeColumn = "sprk_communicationtype";
    private const string ThreadTable = "sprk_communicationthread";
    private const string ThreadTypeColumn = "sprk_threadtype";
    private const string OwnerColumn = "ownerid";
    private const int ThreadTypeDirect = 100000001;
    private const string SystemUserTable = "systemuser";
    private const string IsDisabledColumn = "isdisabled";
    private const string ApplicationIdColumn = "applicationid";
    private const string ChatIdentityColumn = "sprk_communicationuserid";

    /// <summary>No column to write alongside the thread lookup.</summary>
    internal static readonly IReadOnlyDictionary<string, object> None = new Dictionary<string, object>();

    /// <summary>
    /// The rule on its own: the participant that is not <paramref name="sender"/>, when <paramref name="participants"/> is
    /// exactly two distinct, non-empty ids and one of them is the sender; otherwise <c>null</c>. Whether both are enabled is
    /// checked by the caller, and so is whether either is an application user.
    /// </summary>
    internal static Guid? OtherParticipant(Guid sender, IReadOnlyList<Guid>? participants)
    {
        if (sender == Guid.Empty || participants is not { Count: 2 })
            return null;

        var (first, second) = (participants[0], participants[1]);
        if (first == Guid.Empty || second == Guid.Empty || first == second)
            return null;

        return first == sender ? second : second == sender ? first : null;
    }

    /// <summary>
    /// <c>true</c> when <paramref name="row"/> is filed under a record: it carries a typed regarding lookup
    /// (<see cref="RegardingFieldMap.AllRegardingFields"/>) with a non-empty id. A Message row that does not is the only kind
    /// that can be a Direct message — the filing stamp's test below, and the outbound send's pair routing
    /// (<c>CommunicationService.IsOutboundDirectSend</c>, task 054a piece 2b) on the row it prepared.
    /// </summary>
    internal static bool CarriesATypedRegarding(Entity row) =>
        RegardingFieldMap.AllRegardingFields.Any(f => row.GetAttributeValue<EntityReference>(f) is { } er && er.Id != Guid.Empty);

    /// <summary>
    /// The columns the write that files <paramref name="communicationId"/> into the thread of <paramref name="assignment"/>
    /// carries beside the thread lookup: the recipient, and the sender when the row has none. Empty for anything that is not
    /// a Direct message, and whenever the rule does not hold. Never throws, except on cancellation.
    /// </summary>
    /// <param name="senderChatIdentity">The provider identity of an inbound message's sender (the ACS raw id); <c>null</c> on
    /// the send paths, whose row already carries <c>sprk_sentby</c>.</param>
    internal static async Task<IReadOnlyDictionary<string, object>> ColumnsForDirectFilingAsync(
        IGenericEntityService entities,
        IDataverseRecordShareService recordShares,
        Guid communicationId,
        ThreadAssignment assignment,
        string? senderChatIdentity,
        ILogger logger,
        CancellationToken ct)
    {
        if (assignment.Audience != ThreadGrantAudience.DirectParticipants)
            return None;

        try
        {
            var columns = new List<string> { CommunicationTypeColumn, SenderColumn };
            columns.AddRange(RegardingFieldMap.AllRegardingFields);
            var row = await entities.RetrieveAsync(CommunicationTable, communicationId, columns.ToArray(), ct);
            if (row is null
                || row.GetAttributeValue<OptionSetValue>(CommunicationTypeColumn)?.Value != (int)CommunicationType.Message
                || CarriesATypedRegarding(row))
            {
                return None; // not a Message, or filed under a record: not a Direct message
            }

            var stampedSender = row.GetAttributeValue<EntityReference>(SenderColumn)?.Id is { } s && s != Guid.Empty ? s : (Guid?)null;
            var sender = stampedSender ?? await MapChatIdentityAsync(entities, senderChatIdentity, ct);
            if (sender is not { } senderId)
            {
                logger.LogError(
                    RecipientUnmappedEvent,
                    "{EventCode}: the sender of Direct message {CommunicationId} (thread {ThreadId}) could not be mapped to a " +
                    "systemuser; no recipient is stamped",
                    RecipientUnmappedEventCode, communicationId, assignment.ThreadId);
                return None;
            }

            var result = new Dictionary<string, object>();
            if (stampedSender is null)
                result[SenderColumn] = new EntityReference(SystemUserTable, senderId);

            var participants = await ReadParticipantsAsync(entities, recordShares, assignment.ThreadId, ct);
            if (OtherParticipant(senderId, participants) is not { } recipient)
            {
                logger.LogInformation(
                    NotTwoParticipantsEvent,
                    "{EventCode}: Direct message {CommunicationId}: thread {ThreadId} does not hold exactly two distinct " +
                    "participants including the sender; no recipient is stamped",
                    NotTwoParticipantsEventCode, communicationId, assignment.ThreadId);
            }
            else if (!await BothEnabledPeopleAsync(entities, senderId, recipient, ct))
            {
                logger.LogInformation(
                    ParticipantNotEligibleEvent,
                    "{EventCode}: Direct message {CommunicationId}: a participant of thread {ThreadId} is disabled, missing or an " +
                    "application user; no recipient is stamped",
                    ParticipantNotEligibleEventCode, communicationId, assignment.ThreadId);
            }
            else
            {
                result[RecipientColumn] = new EntityReference(SystemUserTable, recipient);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ParticipantsUnreadEvent,
                ex,
                "{EventCode}: Direct message {CommunicationId}: its participants could not be read; no recipient is stamped " +
                "(thread {ThreadId})",
                ParticipantsUnreadEventCode, communicationId, assignment.ThreadId);
            return None;
        }
    }

    /// <summary>
    /// The ONE write that files <paramref name="communicationId"/> into <paramref name="threadId"/>: the thread lookup plus
    /// <paramref name="alongside"/> (<see cref="ColumnsForDirectFilingAsync"/>), in one update. Both writers use it — the
    /// resolver's join and create, and the respond-into-thread path.
    /// </summary>
    /// <remarks>
    /// The schema script (<see cref="SchemaScriptCommand"/>) runs before a BFF that stamps these columns deploys. When it has
    /// not, the combined write is refused because a stamp column does not exist on the table; the write is then sent once
    /// more with the thread lookup alone, and one Error (<see cref="RecipientColumnMissingEventCode"/>) names the script.
    /// Only that refusal retries — matched by <see cref="CommunicationService.IsMissingColumnFault"/> against a stamp column
    /// this write carried (the SDK's typed <c>0x80041103</c> fault, or the "no such attribute / property" text the SDK and the
    /// Web API answer with) — and only when the write carried stamp columns. Any other fault, and any fault of a write without
    /// them, propagates unchanged to the caller's best-effort catch. A fault of the retry propagates the same way.
    /// </remarks>
    internal static async Task WriteThreadFilingAsync(
        IGenericEntityService entities,
        ILogger logger,
        Guid communicationId,
        Guid threadId,
        IReadOnlyDictionary<string, object> alongside,
        CancellationToken ct)
    {
        var fields = ThreadResolver.ThreadFilingFields(threadId, alongside);
        try
        {
            await entities.UpdateAsync(CommunicationTable, communicationId, fields, ct);
        }
        catch (Exception ex) when (AStampColumnIsMissing(ex, fields))
        {
            await entities.UpdateAsync(CommunicationTable, communicationId, ThreadResolver.ThreadFilingFields(threadId, None), ct);
            logger.LogError(
                RecipientColumnMissingEvent,
                ex,
                "{EventCode}: Direct message {CommunicationId} was filed into thread {ThreadId} without its participants because " +
                "a participant column does not exist on sprk_communication in this environment; run {SchemaScript}",
                RecipientColumnMissingEventCode, communicationId, threadId, SchemaScriptCommand);
        }
    }

    /// <summary>
    /// <c>true</c> when <paramref name="ex"/> is Dataverse refusing the write because one of the stamp columns in
    /// <paramref name="fields"/> (every column but the thread lookup) does not exist on the table.
    /// </summary>
    private static bool AStampColumnIsMissing(Exception ex, IReadOnlyDictionary<string, object> fields) =>
        fields.Keys.Any(column =>
            !string.Equals(column, ThreadResolver.ThreadLookupOnCommunication, StringComparison.OrdinalIgnoreCase)
            && CommunicationService.IsMissingColumnFault(ex, column));

    /// <summary>
    /// A Direct thread's two-party list as the stamp decides from it: the thread's user owner and every systemuser holding a
    /// share on the thread that grants Read (<see cref="RecordShareLevels.CanRead"/>; a zero-mask row is inherited access, not
    /// a share). Empty when the thread is missing or not typed Direct. A team owner or a team share is never a participant.
    /// The share read is strict: a failed read throws, so the caller stamps nothing.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> ReadParticipantsAsync(
        IGenericEntityService entities, IDataverseRecordShareService recordShares, Guid threadId, CancellationToken ct)
    {
        var thread = await entities.RetrieveAsync(ThreadTable, threadId, new[] { ThreadTypeColumn, OwnerColumn }, ct);
        if (thread is null || thread.Id == Guid.Empty
            || thread.GetAttributeValue<OptionSetValue>(ThreadTypeColumn)?.Value != ThreadTypeDirect)
        {
            return Array.Empty<Guid>();
        }

        var participants = new List<Guid>();
        if (thread.GetAttributeValue<EntityReference>(OwnerColumn) is { } owner
            && string.Equals(owner.LogicalName, SystemUserTable, StringComparison.OrdinalIgnoreCase))
        {
            participants.Add(owner.Id);
        }

        var shares = await recordShares.GetPrincipalAccessOrThrowAsync(ThreadTable, threadId, ct);
        foreach (var share in shares)
        {
            if (share.Principal.Kind == DataversePrincipalKind.SystemUser
                && RecordShareLevels.CanRead(share.AccessRightsMask)
                && !participants.Contains(share.Principal.Id))
            {
                participants.Add(share.Principal.Id);
            }
        }

        return participants;
    }

    /// <summary>
    /// The one systemuser whose chat identity (<c>sprk_communicationuserid</c>) is <paramref name="chatIdentity"/>; <c>null</c>
    /// for a blank identity, none, or more than one.
    /// </summary>
    private static async Task<Guid?> MapChatIdentityAsync(IGenericEntityService entities, string? chatIdentity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chatIdentity))
            return null;

        var query = new QueryExpression(SystemUserTable) { ColumnSet = new ColumnSet(false), TopCount = 2, NoLock = true };
        query.Criteria.AddCondition(ChatIdentityColumn, ConditionOperator.Equal, chatIdentity.Trim());
        var rows = (await entities.RetrieveMultipleAsync(query, ct))?.Entities;
        return rows is { Count: 1 } && rows[0].Id != Guid.Empty ? rows[0].Id : null;
    }

    /// <summary>
    /// <c>true</c> when both systemusers exist, read explicitly not disabled, and are people: an application user
    /// (<c>applicationid</c> set) is never a Direct message's participant.
    /// </summary>
    private static async Task<bool> BothEnabledPeopleAsync(IGenericEntityService entities, Guid first, Guid second, CancellationToken ct)
    {
        var eligibility = await ReadEligibilityAsync(entities, new[] { first, second }, ct);
        return eligibility[first] == ParticipantEligibility.Eligible && eligibility[second] == ParticipantEligibility.Eligible;
    }

    /// <summary>
    /// Whether each of <paramref name="systemUserIds"/> can be a Direct message's participant, in ONE read of
    /// <c>systemuser</c>: <see cref="ParticipantEligibility.Eligible"/> only for a row that reads explicitly not disabled and
    /// carries no <c>applicationid</c>. The rule <see cref="BothEnabledPeopleAsync"/> applies to the pair, and the outbound
    /// pair routing (task 054a piece 2b) to the recipient it mapped. A fault of the read propagates.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, ParticipantEligibility>> ReadEligibilityAsync(
        IGenericEntityService entities, IReadOnlyList<Guid> systemUserIds, CancellationToken ct)
    {
        var query = new QueryExpression(SystemUserTable)
        {
            ColumnSet = new ColumnSet(IsDisabledColumn, ApplicationIdColumn),
            TopCount = systemUserIds.Count + 1,
            NoLock = true,
        };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.In, systemUserIds.Cast<object>().ToArray());
        var rows = (await entities.RetrieveMultipleAsync(query, ct))?.Entities;

        var result = new Dictionary<Guid, ParticipantEligibility>();
        foreach (var id in systemUserIds)
        {
            var row = rows?.FirstOrDefault(r => r.Id == id);
            result[id] = row is null
                ? ParticipantEligibility.Missing
                : row.GetAttributeValue<Guid?>(ApplicationIdColumn) is { } app && app != Guid.Empty
                    ? ParticipantEligibility.ApplicationUser
                    : row.GetAttributeValue<bool?>(IsDisabledColumn) == false
                        ? ParticipantEligibility.Eligible
                        : ParticipantEligibility.Disabled;
        }

        return result;
    }
}

/// <summary>What one <c>systemuser</c> read says about a Direct message's participant (<see cref="DirectMessageRecipient.ReadEligibilityAsync"/>).</summary>
internal enum ParticipantEligibility
{
    /// <summary>Read explicitly not disabled, and no <c>applicationid</c>: a person who can be a participant.</summary>
    Eligible,

    /// <summary>No row for the id.</summary>
    Missing,

    /// <summary><c>isdisabled</c> is true, or not read as false.</summary>
    Disabled,

    /// <summary>The row carries an <c>applicationid</c>: an application user, never a participant.</summary>
    ApplicationUser,
}
