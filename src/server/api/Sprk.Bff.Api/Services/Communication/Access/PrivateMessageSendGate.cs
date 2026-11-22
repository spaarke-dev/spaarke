// =====================================================================================================================
// field-mapping-server-write-path-r1 task 056b (#2923): the send path of a private message in a record's context — a Message
// into the caller's own strict 1:1 Direct thread that also names ONE core record it is about (design:
// projects/field-mapping-server-write-path-r1/notes/task-056-private-message-design.md, Q2, Q3, Q4, 3.2, 3.6).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: CallerRecordAccessProbe (the caller's Read, asked through the equalised gate question; a named user's Read, asked
//     app-only), SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync (the No Access wall on the record and every secure
//     record it is filed under), ExternalParticipationService.GetEffectiveRootRecordFlagsAsync (Restricted through filing,
//     EffectiveRootFlags), InternalShareEndpoints.IsBarredOnRestricted (the one "external on Restricted" predicate),
//     DirectMessageRecipient.ReadStrictPairShapeAsync (the one Direct-pair shape rule). AssignTaskRecipientGate is the precedent
//     for asking those questions of a named recipient.
//   Extension: every one of those is called unchanged; this file only orders the questions (the sender's in full first) and maps
//     their answers onto one refusal per side. It is also the one writer of the reference columns (PrivateContextColumns), so a
//     guard can allow-list a single send-path file.
//   Cost of doing nothing: a Direct message cannot point at a record without becoming a record message (the typed regarding files
//     it), so "only the sender and the named recipient read it" and "shown in the record's conversation" cannot both hold.
// =====================================================================================================================

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// fm task 056b: the one gate a private message passes BEFORE anything is sent, and the one writer of its record reference.
/// </summary>
/// <remarks>
/// <para><b>What it checks, in order.</b> The request's shape (<see cref="RefuseInvalidRequest"/>, a 400); the target thread is the
/// caller's own strict 1:1 Direct thread and a reply stays in it (a 400; a fault reading it is a 503); then the SENDER's checks in
/// full, then the RECIPIENT's (the pair's other member), each the same three:</para>
/// <list type="number">
///   <item>Read on the record. The sender is asked as the caller through the equalised gate question
///   (<see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>), so an unknown or nonexistent id and a denied
///   one take the same answer and the same work (NFR-01). The recipient is asked app-only, naming the user
///   (<see cref="CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync"/>); no answer is a 503.</item>
///   <item>The No Access wall on the record and every secure record it is filed under
///   (<see cref="SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>,
///   asked as flagged: a non-secure record is not the wall's). Walled is an answer and refuses; Unverifiable is a 503.</item>
///   <item>A participant flagged external on a record that is Restricted through its filing
///   (<see cref="InternalShareEndpoints.IsBarredOnRestricted"/> over the effective flag). A flag that cannot be read is a 503.</item>
/// </list>
/// <para><b>Answers.</b> Every sender-side refusal is ONE 403 (<see cref="SenderAccessCode"/>, <see cref="SenderAccessDetail"/>),
/// and every recipient-side refusal ONE 403 (<see cref="RecipientAccessCode"/>, <see cref="RecipientAccessDetail"/>): the text never
/// says which check refused. A check that cannot be answered is a 503 (<see cref="CheckUnavailableCode"/>). A sender who is refused
/// learns nothing about the recipient: no recipient question is asked. Nothing is sent or written on any of them.</para>
/// <para><b>The reference columns.</b> Last, before the send, the columns are confirmed to exist on <c>sprk_communication</c>
/// (published metadata, read once per connection and then remembered; a missing column is never remembered). Missing is a 409
/// (<see cref="ColumnsMissingCode"/>) naming <see cref="SchemaScript"/>, so a private message is never sent without the reference
/// the record surface needs. On success the returned <see cref="PrivateMessageReference"/> sets exactly one reference column and
/// <c>sprk_isinternalonly = true</c> on the prepared row; nothing else on the row or the send changes, and the reference is never a
/// typed regarding lookup, so the message stays a Direct message (<see cref="DirectMessageRecipient.CarriesATypedRegarding"/>).</para>
/// <para><b>Seams.</b> The Dataverse-facing questions are <c>internal virtual</c> (the <see cref="Sprk.Bff.Api.Services.Signals.AssignTaskRecipientGate"/>
/// precedent), so a test runs the ordering and the mapping for real over scripted answers.</para>
/// </remarks>
internal class PrivateMessageSendGate
{
    /// <summary>403: a sender-side check refused (no Read, walled, external on Restricted, an unknown record).</summary>
    internal const string SenderAccessCode = "sdap.communication.private.sender_access";

    /// <summary>403: a recipient-side check refused (no Read, walled, external on Restricted).</summary>
    internal const string RecipientAccessCode = "sdap.communication.private.recipient_access";

    /// <summary>503: a check could not be answered.</summary>
    internal const string CheckUnavailableCode = "sdap.communication.private.check_unavailable";

    /// <summary>409: the reference columns do not exist on <c>sprk_communication</c> in this environment.</summary>
    internal const string ColumnsMissingCode = "sdap.communication.private.columns_missing";

    /// <summary>The sender-side refusal text (owner, 2026-11-22). Never says which check refused.</summary>
    internal const string SenderAccessDetail = "You do not have required record access";

    /// <summary>The recipient-side refusal text (owner, 2026-11-22). Never says which check refused.</summary>
    internal const string RecipientAccessDetail = "Recipient does not have required record access";

    /// <summary>The 503 text.</summary>
    internal const string CheckUnavailableDetail =
        "The message was not sent because record access could not be checked right now; try again in a moment.";

    /// <summary>The script that adds the reference columns, named by the 409 and its Error.</summary>
    internal const string SchemaScript = "scripts/Set-PrivateContextSchema.ps1";

    /// <summary>The 409 text.</summary>
    internal const string ColumnsMissingDetail =
        "The message was not sent: private messages are not set up in this environment (" + SchemaScript +
        " has not been run). Contact your administrator.";

    /// <summary>Information: a private send was refused by a participant check; the log carries the side and the cause.</summary>
    internal const string RefusedEventCode = "private_message.refused";

    /// <summary>Warning: a check of a private send could not be answered; nothing was sent.</summary>
    internal const string CheckUnavailableEventCode = "private_message.check_unavailable";

    /// <summary>Error: the reference columns do not exist on <c>sprk_communication</c>; nothing was sent.</summary>
    internal const string ColumnsMissingEventCode = "private_message.columns_missing";

    /// <summary>Information: both participants passed; the reference is set on the prepared row.</summary>
    internal const string AcceptedEventCode = "private_message.accepted";

    internal static readonly EventId RefusedEvent = new(5424, RefusedEventCode);
    internal static readonly EventId CheckUnavailableEvent = new(5425, CheckUnavailableEventCode);
    internal static readonly EventId ColumnsMissingEvent = new(5426, ColumnsMissingEventCode);
    internal static readonly EventId AcceptedEvent = new(5427, AcceptedEventCode);

    private const string CommunicationTable = "sprk_communication";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string InternalOnlyColumn = "sprk_isinternalonly";
    private const string SystemUserTable = "systemuser";
    private const string IsExternalColumn = "sprk_isexternal";

    /// <summary>
    /// Per entity service, the <c>sprk_communication</c> columns its metadata has shown to exist. Keyed by connection (one in
    /// production), so a missing column is asked again on the next question and a test's own connection never sees another's answer.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IGenericEntityService, System.Collections.Concurrent.ConcurrentDictionary<string, byte>>
        ColumnsConfirmedOn = new();

    private readonly CallerRecordAccessProbe _probe;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly ExternalParticipationService _participations;
    private readonly IGenericEntityService _entities;
    private readonly IDataverseRecordShareService _recordShares;
    private readonly ILogger<PrivateMessageSendGate> _logger;

    public PrivateMessageSendGate(
        CallerRecordAccessProbe probe,
        SecureShareNoAccessGuard noAccessGuard,
        ExternalParticipationService participations,
        IGenericEntityService entities,
        IDataverseRecordShareService recordShares,
        ILogger<PrivateMessageSendGate> logger)
    {
        _probe = probe;
        _noAccessGuard = noAccessGuard;
        _participations = participations;
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _recordShares = recordShares ?? throw new ArgumentNullException(nameof(recordShares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── The request's shape (synchronous; before any read) ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refuses, with a 400 in the validation-problem shape, a request carrying <see cref="SendCommunicationRequest.PrivateContext"/>
    /// that is not a private message: not a Message; an unknown table or an empty id; no <see cref="SendCommunicationRequest.ThreadId"/>;
    /// any To/Cc/Bcc entry; any association; a regarding inheritance. A request without a private context is not looked at.
    /// </summary>
    internal static void RefuseInvalidRequest(SendCommunicationRequest request, string correlationId)
    {
        if (request.PrivateContext is not { } context)
            return;

        if (request.CommunicationType != CommunicationType.Message)
            throw Invalid("A private message must be sent as a message (communicationType Message).", correlationId);

        if (string.IsNullOrWhiteSpace(context.EntityType)
            || !PrivateContextColumns.ColumnByTable.ContainsKey(context.EntityType.Trim())
            || context.Id == Guid.Empty)
        {
            throw Invalid(
                "privateContext must name a matter, project or work assignment (entityType sprk_matter, sprk_project or " +
                "sprk_workassignment) and a non-empty id.", correlationId);
        }

        if (request.ThreadId is not { } thread || thread == Guid.Empty)
            throw Invalid("A private message must be sent into your one-to-one direct conversation (threadId).", correlationId);

        if (request.To is { Length: > 0 } || request.Cc is { Length: > 0 } || request.Bcc is { Length: > 0 })
            throw Invalid("A private message goes to the other member of the conversation; to, cc and bcc must be empty.", correlationId);

        if (request.Associations is { Length: > 0 })
            throw Invalid("A private message cannot carry associations.", correlationId);

        if (request.InheritRegardingFromCommunicationId.HasValue)
            throw Invalid("A private message cannot inherit a regarding record.", correlationId);
    }

    // ── Before the send ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every check of a private send, before anything is sent: the thread, the reply, the sender's checks in full, the
    /// recipient's, then the reference columns. Returns what the row build applies. Call <see cref="RefuseInvalidRequest"/> first.
    /// </summary>
    /// <exception cref="SdapProblemException">400 (thread, reply), 403 (sender, recipient), 409 (columns), 503 (unanswered).</exception>
    internal async Task<PrivateMessageReference> CheckBeforeSendAsync(
        SendCommunicationRequest request, Guid senderSystemUserId, string? callerBearerToken, string correlationId, CancellationToken ct)
    {
        RefuseInvalidRequest(request, correlationId);
        var context = request.PrivateContext
            ?? throw new InvalidOperationException("CheckBeforeSendAsync is for a request carrying a private context.");
        var table = context.EntityType!.Trim().ToLowerInvariant();
        var recordId = context.Id;
        var threadId = request.ThreadId!.Value;

        var recipient = await RecipientOfPairThreadAsync(threadId, senderSystemUserId, correlationId, ct).ConfigureAwait(false);
        await RefuseReplyIntoAnotherThreadAsync(request, threadId, correlationId, ct).ConfigureAwait(false);

        var restricted = new RestrictedMemo(this, table, recordId);

        // The sender's checks run in full before any recipient question is asked (design Q2.3, "Order").
        await CheckParticipantAsync(Side.Sender, senderSystemUserId, callerBearerToken, table, recordId, restricted, correlationId, ct)
            .ConfigureAwait(false);
        await CheckParticipantAsync(Side.Recipient, recipient, null, table, recordId, restricted, correlationId, ct)
            .ConfigureAwait(false);

        await RefuseWhenColumnsMissingAsync(correlationId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            AcceptedEvent,
            "{EventCode}: a private message about {Table} {RecordId} into Direct thread {ThreadId} passed both participants' checks | " +
            "CorrelationId: {CorrelationId}",
            AcceptedEventCode, table, recordId, threadId, correlationId);

        return new PrivateMessageReference(table, recordId);
    }

    /// <summary>
    /// The other member of <paramref name="threadId"/> when it is a strict 1:1 Direct thread that is not the per-user master thread
    /// and the sender is one of its two members (the 1:1 rule of the Messages view); otherwise a 400. A fault reading it is a 503.
    /// </summary>
    private async Task<Guid> RecipientOfPairThreadAsync(Guid threadId, Guid sender, string correlationId, CancellationToken ct)
    {
        var shape = await DirectMessageRecipient.ReadStrictPairShapeAsync(_entities, _recordShares, threadId, ct).ConfigureAwait(false);
        if (shape.Reason == DirectPairShapeReason.ParticipantReadFault)
            throw Unavailable("thread_shape", shape.Fault, correlationId);

        if (shape.IsStrictPair && !shape.IsDefaultThread && shape.OwnerId is { } owner && shape.OtherId is { } other)
        {
            if (owner == sender)
                return other;
            if (other == sender)
                return owner;
        }

        throw Invalid("A private message must be sent into your one-to-one direct conversation (threadId).", correlationId);
    }

    /// <summary>
    /// A reply (<see cref="SendCommunicationRequest.InReplyToMessageId"/>, a message's <c>sprk_communication</c> id) must be to a
    /// message in the same thread; otherwise a 400, the same one for a parent that is in another thread and one that is not found.
    /// A fault reading the parent is a 503.
    /// </summary>
    private async Task RefuseReplyIntoAnotherThreadAsync(
        SendCommunicationRequest request, Guid threadId, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InReplyToMessageId))
            return;

        const string detail = "A private message can only reply to a message in the same conversation.";
        if (!Guid.TryParse(request.InReplyToMessageId.Trim(), out var parentId) || parentId == Guid.Empty)
            throw Invalid(detail, correlationId);

        Guid? parentThread;
        try
        {
            var query = new QueryExpression(CommunicationTable) { ColumnSet = new ColumnSet(ThreadLookup), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition("sprk_communicationid", ConditionOperator.Equal, parentId);
            var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities;
            parentThread = rows?.FirstOrDefault(r => r.Id == parentId)?.GetAttributeValue<EntityReference>(ThreadLookup)?.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw Unavailable("reply_parent", ex, correlationId);
        }

        if (parentThread != threadId)
            throw Invalid(detail, correlationId);
    }

    private enum Side
    {
        Sender,
        Recipient,
    }

    /// <summary>
    /// One participant's three checks, in order: Read, the No Access wall, external on Restricted. A refusal throws that side's one
    /// 403; a check that cannot be answered throws the 503.
    /// </summary>
    private async Task CheckParticipantAsync(
        Side side, Guid user, string? callerBearerToken, string table, Guid recordId, RestrictedMemo restricted,
        string correlationId, CancellationToken ct)
    {
        var entitySet = PrivateContextColumns.EntitySetByTable[table];

        // (1) Read on the record.
        if (side == Side.Sender)
        {
            // The equalised gate question: an unknown id and a denied one are the same None, after the same work (NFR-01).
            var rights = await AskAsync(() => SenderRightsAsync(callerBearerToken, entitySet, recordId, ct), ct).ConfigureAwait(false);
            // The probe answers None for a question it could not ask; only a throw, whatever the record, is unanswered.
            if (rights.Fault is not null)
                throw Unavailable("sender_rights", rights.Fault, correlationId);
            if (!rights.Value.HasFlag(AccessRights.Read))
                throw Refused(side, "no Read on the record", table, recordId, correlationId);
        }
        else
        {
            var rights = await AskAsync(() => RecipientRightsAsync(user, entitySet, recordId, ct), ct).ConfigureAwait(false);
            if (rights.Fault is not null || rights.Value is not { } answered)
                throw Unavailable("recipient_rights", rights.Fault, correlationId);
            if (!answered.HasFlag(AccessRights.Read))
                throw Refused(side, "no Read on the record", table, recordId, correlationId);
        }

        // (2) The No Access wall on the record and every secure record it is filed under.
        var wall = await AskAsync(() => WallAsync(table, recordId, user, ct), ct).ConfigureAwait(false);
        switch (wall.Fault is null ? wall.Value : SecureShareWallOutcome.Unverifiable)
        {
            case SecureShareWallOutcome.NotSecure:
            case SecureShareWallOutcome.NotWalled:
                break;
            case SecureShareWallOutcome.Walled:
                throw Refused(side, "on a No Access list of the record or a secure record it is filed under", table, recordId, correlationId);
            default:
                throw Unavailable(side == Side.Sender ? "sender_no_access" : "recipient_no_access", wall.Fault, correlationId);
        }

        // (3) Flagged external on a record that is Restricted through its filing. The flag is read only when it can matter.
        var isRestricted = await restricted.GetAsync(ct).ConfigureAwait(false);
        if (isRestricted == false)
            return;

        var external = await AskAsync(() => IsExternalAsync(user, ct), ct).ConfigureAwait(false);
        if (external.Fault is not null || external.Value is not { } isExternal)
            throw Unavailable(side == Side.Sender ? "sender_external_flag" : "recipient_external_flag", external.Fault, correlationId);
        if (!isExternal)
            return;
        if (isRestricted is null)
            throw Unavailable("restricted_flag", restricted.Fault, correlationId);
        if (InternalShareEndpoints.IsBarredOnRestricted(isExternal, isRestricted))
            throw Refused(side, "flagged external on a Restricted record", table, recordId, correlationId);
    }

    /// <summary>The reference columns exist on <c>sprk_communication</c>, else the 409; a metadata read that faults is the 503.</summary>
    private async Task RefuseWhenColumnsMissingAsync(string correlationId, CancellationToken ct)
    {
        var present = await AskAsync(() => ReferenceColumnsPresentAsync(ct), ct).ConfigureAwait(false);
        if (present.Fault is not null || present.Value is not { } known)
            throw Unavailable("reference_columns", present.Fault, correlationId);
        if (known)
            return;

        _logger.LogError(
            ColumnsMissingEvent,
            "{EventCode}: a private message was not sent because a reference column does not exist on sprk_communication in this " +
            "environment; run {SchemaScript} | CorrelationId: {CorrelationId}",
            ColumnsMissingEventCode, SchemaScript, correlationId);
        throw new SdapProblemException(
            code: ColumnsMissingCode,
            title: "Private messages are not set up",
            detail: ColumnsMissingDetail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: Extensions(ColumnsMissingCode, correlationId));
    }

    // ── The Dataverse-facing seams ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The caller's rights on the record, asked through the equalised gate question (an unknown id answers None).</summary>
    internal virtual Task<AccessRights> SenderRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct) =>
        _probe.GetCallerRightsForAuthorizationGateAsync(callerBearerToken, entitySet, recordId, ct);

    /// <summary>The named user's rights on the record, asked app-only; <c>null</c> when Dataverse gave no answer.</summary>
    internal virtual Task<AccessRights?> RecipientRightsAsync(Guid user, string entitySet, Guid recordId, CancellationToken ct) =>
        _probe.GetPrincipalRightsAppOnlyOrUnknownAsync(user, entitySet, recordId, ct);

    /// <summary>The No Access check on the record and its secure parents, as <c>/share-user</c> asks it.</summary>
    internal virtual async Task<SecureShareWallOutcome> WallAsync(string table, Guid recordId, Guid user, CancellationToken ct) =>
        (await _noAccessGuard.CheckRecordAndSecureParentsAsync(table, recordId, user, SecureWallRecordScope.AsFlagged, ct)
            .ConfigureAwait(false)).Outcome;

    /// <summary>Whether the record is Restricted, its own flag or one folded from what it is filed under; <c>null</c> when unread.</summary>
    internal virtual async Task<bool?> EffectiveRestrictedAsync(string table, Guid recordId, CancellationToken ct)
    {
        var flags = await _participations.GetEffectiveRootRecordFlagsAsync(table, new[] { recordId }, ct).ConfigureAwait(false);
        return flags.TryGetValue(recordId, out var f) && !f.IsUnreadable ? f.IsRestricted : null;
    }

    /// <summary>
    /// The user's stored <c>sprk_isexternal</c>, read live: only <c>true</c> is external, an unset value is not. A user with no row
    /// counts as external (fail closed, as the identity resolver counts an unresolved user).
    /// </summary>
    internal virtual async Task<bool?> IsExternalAsync(Guid user, CancellationToken ct)
    {
        var query = new QueryExpression(SystemUserTable) { ColumnSet = new ColumnSet(IsExternalColumn), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, user);
        var row = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities?.FirstOrDefault(r => r.Id == user);
        return row is null || row.GetAttributeValue<bool?>(IsExternalColumn) == true;
    }

    /// <summary>
    /// Whether every reference column exists on <c>sprk_communication</c> (published metadata). A yes is remembered for this
    /// connection; a no is asked again on the next send.
    /// </summary>
    internal virtual Task<bool?> ReferenceColumnsPresentAsync(CancellationToken ct) =>
        CommunicationColumnsPresentAsync(_entities, PrivateContextColumns.All, ct);

    /// <summary>
    /// Whether every one of <paramref name="columns"/> exists on <c>sprk_communication</c> (published metadata, read through
    /// <paramref name="entities"/>): <c>true</c>, <c>false</c>, or <c>null</c> when the metadata gave no answer. A yes is remembered
    /// per connection and column; a no is asked again next time, so a column added later is seen without a restart. A fault
    /// propagates; the caller decides what an unanswered question means. The one column-presence check of the private-message
    /// send and reads (fm task 056).
    /// </summary>
    internal static async Task<bool?> CommunicationColumnsPresentAsync(
        IGenericEntityService entities, IReadOnlyCollection<string> columns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(columns);

        if (ColumnsConfirmedOn.TryGetValue(entities, out var confirmed) && columns.All(confirmed.ContainsKey))
            return true;

        var metadata = await entities.GetColumnMetadataAsync(CommunicationTable, ct).ConfigureAwait(false);
        if (metadata is null)
            return null;

        if (!columns.All(metadata.ContainsKey))
            return false;

        var remembered = ColumnsConfirmedOn.GetValue(
            entities, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        foreach (var column in columns)
            remembered.TryAdd(column, 0);
        return true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One seam's answer, or the fault it threw. Only the caller's cancellation propagates.</summary>
    private static async Task<(T Value, Exception? Fault)> AskAsync<T>(Func<Task<T>> question, CancellationToken ct)
    {
        try
        {
            return (await question().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (default!, ex);
        }
    }

    /// <summary>The record's Restricted state, read at most once per send, and only when a check needs it.</summary>
    private sealed class RestrictedMemo(PrivateMessageSendGate gate, string table, Guid recordId)
    {
        private bool _read;
        private bool? _value;

        public Exception? Fault { get; private set; }

        public async Task<bool?> GetAsync(CancellationToken ct)
        {
            if (_read)
                return _value;

            var answer = await AskAsync(() => gate.EffectiveRestrictedAsync(table, recordId, ct), ct).ConfigureAwait(false);
            (_value, Fault, _read) = (answer.Fault is null ? answer.Value : null, answer.Fault, true);
            return _value;
        }
    }

    private SdapProblemException Refused(Side side, string cause, string table, Guid recordId, string correlationId)
    {
        _logger.LogInformation(
            RefusedEvent,
            "{EventCode}: a private message about {Table} {RecordId} was not sent: {Side} {Cause} | CorrelationId: {CorrelationId}",
            RefusedEventCode, table, recordId, side == Side.Sender ? "the sender is" : "the recipient is", cause, correlationId);

        var code = side == Side.Sender ? SenderAccessCode : RecipientAccessCode;
        return new SdapProblemException(
            code: code,
            title: "Private message not sent",
            detail: side == Side.Sender ? SenderAccessDetail : RecipientAccessDetail,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: Extensions(code, correlationId));
    }

    private SdapProblemException Unavailable(string check, Exception? fault, string correlationId)
    {
        _logger.LogWarning(
            CheckUnavailableEvent,
            fault,
            "{EventCode}: a private message was not sent because the {Check} check could not be answered | CorrelationId: {CorrelationId}",
            CheckUnavailableEventCode, check, correlationId);
        return new SdapProblemException(
            code: CheckUnavailableCode,
            title: "Private messages are temporarily unavailable",
            detail: CheckUnavailableDetail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: Extensions(CheckUnavailableCode, correlationId));
    }

    private static SdapProblemException Invalid(string detail, string correlationId) =>
        new(
            code: "VALIDATION_ERROR",
            title: "Validation Error",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });

    private static Dictionary<string, object> Extensions(string code, string correlationId) => new()
    {
        ["correlationId"] = correlationId,
        ["reasonCode"] = code,
    };

    /// <summary>Applies the reference to a prepared row: the one column for its table, and <c>sprk_isinternalonly = true</c>.</summary>
    internal static void Apply(PrivateMessageReference reference, Entity communication)
    {
        foreach (var column in PrivateContextColumns.All)
            communication.Attributes.Remove(column);

        communication[PrivateContextColumns.ColumnByTable[reference.Table]] = new EntityReference(reference.Table, reference.RecordId);
        communication[InternalOnlyColumn] = true;
    }
}

/// <summary>
/// fm task 056b: what a private send that passed <see cref="PrivateMessageSendGate.CheckBeforeSendAsync"/> writes on its row at
/// build (<see cref="ApplyTo"/>): the reference to <see cref="Table"/> <see cref="RecordId"/>.
/// </summary>
internal sealed record PrivateMessageReference(string Table, Guid RecordId)
{
    /// <summary>Sets the reference column and <c>sprk_isinternalonly = true</c> on <paramref name="communication"/>.</summary>
    internal void ApplyTo(Entity communication) => PrivateMessageSendGate.Apply(this, communication);
}
