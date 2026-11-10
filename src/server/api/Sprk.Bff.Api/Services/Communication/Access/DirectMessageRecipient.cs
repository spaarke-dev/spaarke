using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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
/// <para><b>The recipient</b> is the thread's other participant, from the existing source of a Direct thread's two-party list
/// (<see cref="IDirectThreadAccessService.GetParticipantSystemUserIdsAsync"/>: the thread's user owner and the users it is
/// shared to, empty unless the thread is typed Direct). The list must be exactly two distinct, non-empty systemusers, one of
/// them the sender, and both enabled; otherwise no recipient is stamped.</para>
/// <para><b>The sender</b> is the row's <c>sprk_sentby</c>, kept as it is when present. A row without one (an inbound chat
/// message) takes the systemuser its chat identity maps to through the existing identity map
/// (<c>systemuser.sprk_communicationuserid</c>, written by <c>AcsIdentityService</c>), and the sender is stamped with the
/// recipient. When the sender cannot be mapped, nothing is stamped and one Error is logged with
/// <see cref="RecipientUnmappedEventCode"/> and the message id.</para>
/// <para><b>Never fails the message.</b> Any fault stamps nothing and is logged; the thread lookup is still written.</para>
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

    private const string CommunicationTable = "sprk_communication";
    private const string CommunicationTypeColumn = "sprk_communicationtype";
    private const string SystemUserTable = "systemuser";
    private const string IsDisabledColumn = "isdisabled";
    private const string ChatIdentityColumn = "sprk_communicationuserid";

    /// <summary>No column to write alongside the thread lookup.</summary>
    internal static readonly IReadOnlyDictionary<string, object> None = new Dictionary<string, object>();

    /// <summary>
    /// The rule on its own: the participant that is not <paramref name="sender"/>, when <paramref name="participants"/> is
    /// exactly two distinct, non-empty ids and one of them is the sender; otherwise <c>null</c>. Whether both are enabled is
    /// checked by the caller.
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
    /// The columns the write that files <paramref name="communicationId"/> into the thread of <paramref name="assignment"/>
    /// carries beside the thread lookup: the recipient, and the sender when the row has none. Empty for anything that is not
    /// a Direct message, and whenever the rule does not hold. Never throws, except on cancellation.
    /// </summary>
    /// <param name="senderChatIdentity">The provider identity of an inbound message's sender (the ACS raw id); <c>null</c> on
    /// the send paths, whose row already carries <c>sprk_sentby</c>.</param>
    internal static async Task<IReadOnlyDictionary<string, object>> ColumnsForDirectFilingAsync(
        IGenericEntityService entities,
        IDirectThreadAccessService directThreadAccess,
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
                || RegardingFieldMap.AllRegardingFields.Any(f => row.GetAttributeValue<EntityReference>(f) is { } er && er.Id != Guid.Empty))
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

            var participants = await directThreadAccess.GetParticipantSystemUserIdsAsync(assignment.ThreadId, ct);
            if (OtherParticipant(senderId, participants) is { } recipient
                && await BothEnabledAsync(entities, senderId, recipient, ct))
            {
                result[RecipientColumn] = new EntityReference(SystemUserTable, recipient);
            }
            else
            {
                logger.LogInformation(
                    "Direct message {CommunicationId}: thread {ThreadId} does not hold exactly two enabled participants including " +
                    "the sender; no recipient is stamped", communicationId, assignment.ThreadId);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Direct message {CommunicationId}: its participants could not be read; no recipient is stamped (thread {ThreadId})",
                communicationId, assignment.ThreadId);
            return None;
        }
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

    /// <summary><c>true</c> when both systemusers exist and read explicitly not disabled.</summary>
    private static async Task<bool> BothEnabledAsync(IGenericEntityService entities, Guid first, Guid second, CancellationToken ct)
    {
        var query = new QueryExpression(SystemUserTable) { ColumnSet = new ColumnSet(IsDisabledColumn), TopCount = 3, NoLock = true };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.In, first, second);
        var rows = (await entities.RetrieveMultipleAsync(query, ct))?.Entities;
        if (rows is null)
            return false;

        var enabled = rows
            .Where(r => r.GetAttributeValue<bool?>(IsDisabledColumn) == false)
            .Select(r => r.Id)
            .ToHashSet();
        return enabled.Contains(first) && enabled.Contains(second);
    }
}
