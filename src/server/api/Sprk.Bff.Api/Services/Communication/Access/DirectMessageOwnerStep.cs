using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using RecordOwnerResolution = Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution;
using RecordReparent = Sprk.Bff.Api.Services.Dataverse.RecordReparent;
using UnfiledOwnership = Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a: the ONE step that runs after a message joins a thread, on every join path
/// (the send with no thread id, the respond and routed sends, and the inbound capture). It grants the message to the thread's
/// audience, then, for a Direct message filed under no record, assigns the message and its content rows to the configured Direct
/// message owner team (<see cref="DirectMessageOwnerTeam"/>). It is also the one pre-send check of that team for a 1:1 message
/// (<see cref="CheckBeforeSendAsync"/>).
/// </summary>
/// <remarks>
/// <para><b>When.</b> Step 1 runs at the join (<see cref="GrantAtJoinAsync"/>). Steps 2 to 8 (<see cref="OwnDirectAfterContentAsync"/>)
/// run once the path has written the message's content rows (the send and the inbound capture: after the participant index and
/// the enrichment), so those rows move with the message and the second grant covers them. A content row created later is owned
/// by the record ownership resolver's Direct rule, never by a business unit's default team.</para>
/// <para><b>Order.</b> (1) The grant (<see cref="IDirectThreadAccessService.GrantMessageAccessAsync"/>) while the message is still
/// owned by its creator, so its participants read it whatever happens next. (2) Only a <see cref="ThreadGrantAudience.DirectParticipants"/>
/// join goes on. (3) The lookup: <see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/> stops here with nothing written and nothing
/// refused. (4) The message, read whole: it must be a Message, owned by a user, filed under nothing (no ownership parent and no typed
/// regarding lookup), filed into the joined thread, and carry exactly two distinct non-empty systemusers in <c>sprk_sentby</c> and
/// <c>sprk_directrecipient</c> (<see cref="DirectMessageParticipantShares.ParticipantsOf"/>); otherwise it keeps its owner. Its thread,
/// read by type and default flag, must be typed Direct and not the per-user master thread (<see cref="WhyThreadIsNotDirect"/>, task 198's
/// narrowing); a thread read fault keeps the owner, with an Error. (5) A lookup
/// that is not resolved (any other refusal, including <see cref="DirectMessageOwnerTeamRefusal.PartiallyConfigured"/>, or a fault)
/// keeps the owner, with an Error. There is no default-team fallback, and the message is never dropped. (6) The owner write, conditional
/// on the version read in (4), evicted and read back. (7) Each content row (<see cref="RestrictedFilingShareBar.ContentRows"/>) that
/// had the message's previous owner and is not itself filed under a record follows it, in its own conditional write; a row that is
/// filed under a record, refused or faults stays where it is.
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

    /// <summary>Warning: piece 3b, an unfile of a Direct message was refused before anything was written, because the Direct
    /// message owner team could not be confirmed.</summary>
    internal const string UnfileRefusedEventCode = "direct_message.unfile_refused";

    internal static readonly EventId OwnerNotSetEvent = new(5414, OwnerNotSetEventCode);
    internal static readonly EventId MessageNotMovedEvent = new(5415, MessageNotMovedEventCode);
    internal static readonly EventId ContentRowNotMovedEvent = new(5416, ContentRowNotMovedEventCode);
    internal static readonly EventId SendRefusedEvent = new(5417, SendRefusedEventCode);
    internal static readonly EventId UnfileRefusedEvent = new(5419, UnfileRefusedEventCode);

    private const string CommunicationTable = "sprk_communication";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string TypeColumn = "sprk_communicationtype";
    private const string OwnerColumn = "ownerid";
    private const string OwningTeamColumn = "owningteam";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";
    private const string VersionColumn = "versionnumber";
    private const string EvictionReason = "direct-message-owner-step";

    private readonly IDirectThreadAccessService _access;
    private readonly IGenericEntityService _dataverse;
    private readonly DirectMessageOwnerTeam _lookup;
    private readonly IMembershipCacheInvalidator? _cacheInvalidator;

    /// <summary>The strict share read the 1:1 thread shape is decided from (<see cref="IsOneToOneThreadAsync"/>); without it a reply's
    /// shape cannot be read.</summary>
    private readonly IDataverseRecordShareService? _recordShares;
    private readonly ILogger<DirectMessageOwnerStep> _logger;

    /// <summary>Names the Secure Record business unit (<c>SecureRecord:BusinessUnitName</c>) for the check an unfile makes on the owner
    /// it reads back (<see cref="WhyOwnerLeftSecureSinceDecidedAsync"/>). Without it that check cannot be made, so such an unfile is refused.</summary>
    private readonly IConfiguration? _configuration;

    public DirectMessageOwnerStep(
        IDirectThreadAccessService access,
        IGenericEntityService dataverse,
        DirectMessageOwnerTeam lookup,
        ILogger<DirectMessageOwnerStep> logger,
        IMembershipCacheInvalidator? cacheInvalidator = null,
        IDataverseRecordShareService? recordShares = null,
        IConfiguration? configuration = null)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheInvalidator = cacheInvalidator;
        _recordShares = recordShares;
        _configuration = configuration;
    }

    /// <summary>
    /// Grants <paramref name="communicationId"/> to the audience its join fixed, then (Direct joins only) assigns it and its content
    /// rows to the Direct message owner team: <see cref="GrantAtJoinAsync"/> then <see cref="OwnDirectAfterContentAsync"/>, for a
    /// join with no content rows still to write. Never throws for a Dataverse fault; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    public async Task GrantThenOwnDirectAsync(Guid communicationId, ThreadAssignment assignment, CancellationToken ct)
    {
        if (await GrantAtJoinAsync(communicationId, assignment, ct).ConfigureAwait(false) is { } join)
            await OwnDirectAfterContentAsync(join, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Step 1 of the class remarks, at the join: grants <paramref name="communicationId"/> to the audience its join fixed, while it is
    /// still owned by its creator. Returns the join a <see cref="ThreadGrantAudience.DirectParticipants"/> join hands to
    /// <see cref="OwnDirectAfterContentAsync"/> once the path has written the message's content rows; <c>null</c> for any other join.
    /// Never throws for a Dataverse fault; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    public async Task<DirectOwnerJoin?> GrantAtJoinAsync(Guid communicationId, ThreadAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        // The grant is best-effort and logs its own outcome; it may have been skipped or failed, so this says only that it ran.
        await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
        _logger.LogDebug(
            "Message {CommunicationId}: grant attempted for its thread's {Audience} audience (ThreadId={ThreadId}).",
            communicationId, assignment.Audience, assignment.ThreadId);

        return assignment.Audience == ThreadGrantAudience.DirectParticipants ? new DirectOwnerJoin(communicationId, assignment) : null;
    }

    /// <summary>
    /// Steps 2 to 8 of the class remarks, the one entry to the owner write: a join whose grant ran (<see cref="GrantAtJoinAsync"/>
    /// is the only maker of <paramref name="join"/>). A path calls it after it has written the message's content rows (the
    /// participant index, the review logs), so they move with the message and the second grant covers them. Never throws for a
    /// Dataverse fault; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    public async Task OwnDirectAfterContentAsync(DirectOwnerJoin join, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(join);
        var (communicationId, assignment) = (join.CommunicationId, join.Assignment);

        try
        {
            if (await ConfirmDirectMoveAsync(communicationId, assignment.ThreadId, unfile: false, ct).ConfigureAwait(false) is { } move
                && await WriteDirectMoveAsync(move, leavesSecureOwner: false, ct).ConfigureAwait(false))
                await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(OwnerNotSetEvent, ex,
                "Message {CommunicationId} keeps its owner: the Direct owner step faulted (ThreadId={ThreadId}).",
                communicationId, assignment.ThreadId);
        }
    }

    // ── piece 3b: the unfile direction ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Piece 3b, the ONE choice every re-file writer makes for what an unfile does (<see cref="RecordReparent.WhenUnfiled"/>):
    /// a <c>sprk_communication</c> re-filed for a person takes <see cref="UnfiledOwnership.UnfiledCommunication"/>: an eligible
    /// Direct message returns to the Direct message owner team (decided by <see cref="DecideUnfileAsync"/> before the write,
    /// moved by <see cref="AfterReparentAsync"/> after it), and any other communication is Unchanged pending #2722. Without the
    /// step to move it, <see cref="UnfiledOwnership.KeepCreator"/>. A write that acts for nobody (app-only) and every other
    /// table keep <see cref="UnfiledOwnership.ActingUserTeam"/>, so their refusal when no person is named stays as it is.
    /// </summary>
    /// <param name="table">The re-filed row's table.</param>
    /// <param name="actsForAPerson">The writer names the person it acts for.</param>
    /// <param name="step">The step that moves the message after the write; <c>null</c> where none is composed.</param>
    public static UnfiledOwnership UnfiledOwnershipFor(string table, bool actsForAPerson, DirectMessageOwnerStep? step)
    {
        if (!string.Equals(table?.Trim(), CommunicationTable, StringComparison.OrdinalIgnoreCase) || !actsForAPerson)
            return UnfiledOwnership.ActingUserTeam;
        return step is null ? UnfiledOwnership.KeepCreator : UnfiledOwnership.UnfiledCommunication;
    }

    /// <summary>
    /// Piece 3b, after a written re-file: when the reparent answered that the unfiled message returns to the Direct team
    /// (<see cref="RecordOwnerResolution.DirectUnfile"/>), confirms it first, on the row read back after the write (the same
    /// eligibility rule as before the write, <see cref="WhyNotAnEligibleUnfile"/>; its thread; the lookup), and only then grants it
    /// to its two participants, assigns it to the Direct team through the one owner write, moves its content rows (unless it was
    /// owned by the Secure team: the secure-child reconcile moves those and takes their mirror off), and grants it again. A message
    /// that does not pass the checks is neither granted nor moved. Any other answer does nothing. Never throws for a Dataverse
    /// fault: the unfile has landed, so a message that cannot be moved keeps its owner with an Error, and
    /// <c>Migrate-DirectMessagesToOwnerTeam.ps1</c> is the recovery. A writer calls it before the step after the re-file
    /// (<c>SecureChildReconciler.AfterRefileAsync</c>), so that step sees the message's new owner.
    /// </summary>
    public async Task AfterReparentAsync(Guid communicationId, RecordOwnerResolution? reparent, CancellationToken ct)
    {
        if (reparent?.DirectUnfile is not { } due)
            return;

        try
        {
            if (await ConfirmDirectMoveAsync(communicationId, due.ThreadId, unfile: true, ct, due.LeavesSecureOwner).ConfigureAwait(false) is not { } move)
                return;

            var assignment = ThreadAssignment.Direct(due.ThreadId);
            await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
            if (await WriteDirectMoveAsync(move, due.LeavesSecureOwner, ct).ConfigureAwait(false))
                await _access.GrantMessageAccessAsync(communicationId, assignment, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(MessageNotMovedEvent, ex,
                "Message {CommunicationId} was unfiled and keeps its owner: its return to the Direct team faulted (ThreadId={ThreadId}).",
                communicationId, due.ThreadId);
        }
    }

    /// <summary>
    /// Piece 3b: the row a re-file leaves, judged BEFORE it is written: <paramref name="current"/> (as read before the change) with
    /// every column the change clears removed, every lookup it sets applied (<paramref name="parentChanges"/>), every typed regarding
    /// lookup it sets onto a table that is no ownership parent applied (<paramref name="regardingLinksSet"/>), every other column
    /// the rule reads that it writes applied (<paramref name="ruleColumnsWritten"/>: the type and the stamped pair; a <c>null</c>
    /// removes the column), and its thread lookup naming <paramref name="threadIdAfter"/>. <see cref="DecideUnfileAsync"/> asks
    /// <see cref="WhyNotAnEligibleUnfile"/> of it, the rule <see cref="AfterReparentAsync"/> asks of the row read back after the
    /// write.
    /// </summary>
    internal static Entity RowAfterChange(
        Entity current,
        IReadOnlyDictionary<string, EntityReference?> parentChanges,
        IReadOnlyDictionary<string, EntityReference> regardingLinksSet,
        Guid? threadIdAfter,
        IReadOnlyDictionary<string, object?>? ruleColumnsWritten = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(parentChanges);
        ArgumentNullException.ThrowIfNull(regardingLinksSet);

        var after = new Entity(current.LogicalName, current.Id);
        foreach (var attribute in current.Attributes)
            after[attribute.Key] = attribute.Value;

        foreach (var (key, value) in parentChanges)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = SecureRootInheritance.NormalizeColumn(key);
            if (value is null || value.Id == Guid.Empty)
                after.Attributes.Remove(column);
            else
                after[column] = value;
        }

        foreach (var (key, link) in regardingLinksSet)
        {
            if (!string.IsNullOrWhiteSpace(key) && link is { } set && set.Id != Guid.Empty)
                after[SecureRootInheritance.NormalizeColumn(key)] = set;
        }

        foreach (var (key, value) in ruleColumnsWritten ?? new Dictionary<string, object?>())
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = SecureRootInheritance.NormalizeColumn(key);
            if (value is null)
                after.Attributes.Remove(column);
            else
                after[column] = value;
        }

        if (threadIdAfter is { } thread && thread != Guid.Empty)
            after[ThreadLookup] = new EntityReference(DirectMessageParticipantShares.ThreadTable, thread);
        else
            after.Attributes.Remove(ThreadLookup);

        return after;
    }

    /// <summary>
    /// Piece 3b, the ONE eligibility rule of an unfile, asked of the row as the write leaves it: before the write of the row
    /// <see cref="RowAfterChange"/> builds (<see cref="DecideUnfileAsync"/>), after it of the row read back
    /// (<see cref="AfterReparentAsync"/>). Eligible: a Message, linked to no record (no ownership parent and no typed regarding
    /// lookup), filed into <paramref name="threadId"/>, carrying a valid stamped pair. Why <paramref name="rowAfter"/> is not, or
    /// <c>null</c> when it is. Reads nothing.
    /// </summary>
    internal static string? WhyNotAnEligibleUnfile(Entity rowAfter, Guid? threadId)
    {
        ArgumentNullException.ThrowIfNull(rowAfter);
        if (WhyNotAMessage(rowAfter) is { } notAMessage)
            return notAMessage;
        if (WhyFiledUnderARecord(rowAfter) is { } filed)
            return filed;
        if (threadId is not { } thread || thread == Guid.Empty
            || rowAfter.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } filedThread || filedThread != thread)
            return "it is not filed into the thread it was unfiled in";
        return WhyNoDirectPair(rowAfter);
    }

    /// <summary>
    /// Piece 3b, BEFORE an unfile is written: what the unfile does with the owner of <paramref name="rowAfter"/> (the row as the
    /// change leaves it, <see cref="RowAfterChange"/>) in <paramref name="threadId"/>. The record ownership resolver asks it for
    /// <see cref="UnfiledOwnership.UnfiledCommunication"/>. The checks that read nothing come first, so a communication that is not
    /// an eligible Direct message costs no read.
    /// <list type="bullet">
    /// <item>Not eligible (<see cref="WhyNotAnEligibleUnfile"/>: not a Message, still linked to a record, not in the thread, no
    /// valid stamped pair): <see cref="DirectUnfileVerdict.KeepOwner"/>, nothing read.</item>
    /// <item>Neither Direct setting configured: <see cref="DirectUnfileVerdict.KeepOwner"/>, from configuration alone.</item>
    /// <item>A thread not typed Direct, or the per-user master thread: <see cref="DirectUnfileVerdict.KeepOwner"/>.</item>
    /// <item>Eligible, and the lookup resolves: <see cref="DirectUnfileVerdict.ToDirectTeam"/>.</item>
    /// <item>Eligible, and the lookup refuses (<see cref="DirectMessageOwnerTeamRefusal.PartiallyConfigured"/> included):
    /// <see cref="DirectUnfileVerdict.Refused"/>. A fault reading the lookup or the thread: <see cref="DirectUnfileVerdict.Unavailable"/>.
    /// Both refuse the unfile before anything is written.</item>
    /// </list>
    /// Never throws for a fault; cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    internal static async Task<DirectUnfileDecision> DecideUnfileAsync(
        IGenericEntityService dataverse, DirectMessageOwnerTeam? lookup, Entity rowAfter, Guid? threadId, ILogger logger,
        CancellationToken ct)
    {
        if (lookup is null)
            return DirectUnfileDecision.KeepOwner;

        var message = rowAfter;
        if (WhyNotAnEligibleUnfile(message, threadId) is { } reason)
        {
            logger.LogDebug("Unfiling message {CommunicationId} keeps its owner: {Reason}.", message.Id, reason);
            return DirectUnfileDecision.KeepOwner;
        }

        var thread = threadId!.Value;
        DirectMessageOwnerTeamResolution? resolution = null;
        Exception? fault;
        try
        {
            resolution = await lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
            fault = resolution.Fault;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            fault = ex;
        }

        if (fault is null && resolution!.IsNotConfigured)
            return DirectUnfileDecision.KeepOwner;

        Entity? threadRow;
        try
        {
            threadRow = await ReadThreadAsync(dataverse, thread, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(UnfileRefusedEvent, ex,
                "Unfiling message {CommunicationId} was refused: its thread {ThreadId} could not be read. Nothing was written.",
                message.Id, thread);
            return DirectUnfileDecision.Unavailable(ex);
        }

        if (WhyThreadIsNotDirect(threadRow) is { } threadReason)
        {
            logger.LogDebug("Unfiling message {CommunicationId} keeps its owner: {Reason}.", message.Id, threadReason);
            return DirectUnfileDecision.KeepOwner;
        }

        if (fault is not null)
        {
            logger.LogWarning(UnfileRefusedEvent, fault,
                "Unfiling Direct message {CommunicationId} was refused: the Direct message owner team could not be read. Nothing was " +
                "written.", message.Id);
            return DirectUnfileDecision.Unavailable(fault);
        }

        if (!resolution!.IsResolved || resolution.TeamId is not { } teamId || teamId == Guid.Empty)
        {
            logger.LogWarning(UnfileRefusedEvent,
                "Unfiling Direct message {CommunicationId} was refused: the Direct message owner team is not usable ({Refusal}, missing " +
                "setting {MissingSetting}). Nothing was written.", message.Id, resolution.Refusal, resolution.MissingSetting);
            return DirectUnfileDecision.Refused(resolution.Refusal ?? DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        }

        return message.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id == teamId
            ? DirectUnfileDecision.KeepOwner
            : DirectUnfileDecision.ToDirectTeam;
    }

    /// <summary>
    /// The pre-send check of a message the caller has found 1:1 from its addressees (<see cref="CheckBeforeSendAsync(Guid?, CancellationToken)"/>
    /// with no thread).
    /// </summary>
    public Task<DirectOwnerSendCheck> CheckBeforeSendAsync(CancellationToken ct) => CheckBeforeSendAsync(threadId: null, ct);

    /// <summary>
    /// The pre-send check of a Message send filed under no record. <see cref="DirectOwnerSendCheck.Proceed"/> when the Direct team
    /// resolves or no Direct setting is configured (nothing else is read); otherwise the send is refused only when it is 1:1:
    /// <see cref="DirectOwnerSendCheck.Refused"/> for a configuration problem, <see cref="DirectOwnerSendCheck.Unavailable"/> when the
    /// lookup could not read Dataverse. Never throws for a fault.
    /// </summary>
    /// <param name="threadId">A send into an existing thread (a reply): the thread's shape decides whether it is 1:1, never its
    /// addressees. It is 1:1 exactly when the thread is a strict Direct pair (<see cref="DirectMessageRecipient.ReadStrictPairShapeAsync"/>,
    /// typed Direct) that is not the per-user master thread (<see cref="WhyThreadIsNotDirect"/>). A shape that cannot be read is
    /// <see cref="DirectOwnerSendCheck.Unavailable"/>. <c>null</c>: the caller found the send 1:1 from its addressees.</param>
    public async Task<DirectOwnerSendCheck> CheckBeforeSendAsync(Guid? threadId, CancellationToken ct)
    {
        DirectMessageOwnerTeamResolution? resolution = null;
        Exception? fault;
        try
        {
            resolution = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
            fault = resolution.Fault;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            fault = ex;
        }

        if (fault is null && resolution is not null && (resolution.IsResolved || resolution.IsNotConfigured))
            return DirectOwnerSendCheck.Proceed;

        if (threadId is { } thread)
        {
            var (oneToOne, shapeFault) = await IsOneToOneThreadAsync(thread, ct).ConfigureAwait(false);
            if (oneToOne == false)
            {
                _logger.LogDebug("A send into thread {ThreadId} is not 1:1, so the Direct message owner team does not gate it.", thread);
                return DirectOwnerSendCheck.Proceed;
            }

            if (oneToOne is null)
            {
                _logger.LogWarning(SendRefusedEvent, shapeFault,
                    "A send into thread {ThreadId} was refused: whether it is a 1:1 thread could not be read.", thread);
                return DirectOwnerSendCheck.Unavailable;
            }
        }

        if (fault is not null)
        {
            _logger.LogWarning(SendRefusedEvent, fault,
                "A 1:1 message send was refused: the Direct message owner team could not be read ({Refusal}).", resolution?.Refusal);
            return DirectOwnerSendCheck.Unavailable;
        }

        _logger.LogWarning(SendRefusedEvent,
            "A 1:1 message send was refused: the Direct message owner team is not usable ({Refusal}, missing setting {MissingSetting}).",
            resolution!.Refusal, resolution.MissingSetting);
        return DirectOwnerSendCheck.Refused(resolution.Refusal ?? DirectMessageOwnerTeamRefusal.PartiallyConfigured);
    }

    /// <summary>
    /// Whether <paramref name="threadId"/> is a 1:1 thread: a strict Direct pair that is not the per-user master thread.
    /// <c>null</c> (with the fault, when there is one) when that cannot be read.
    /// </summary>
    private async Task<(bool? OneToOne, Exception? Fault)> IsOneToOneThreadAsync(Guid threadId, CancellationToken ct)
    {
        if (_recordShares is null)
            return (null, null);

        var shape = await DirectMessageRecipient.ReadStrictPairShapeAsync(_dataverse, _recordShares, threadId, ct).ConfigureAwait(false);
        if (shape.Reason == DirectPairShapeReason.ParticipantReadFault)
            return (null, shape.Fault);
        return (shape.IsStrictPair && !shape.IsDefaultThread, null);
    }

    /// <summary>
    /// The thread narrowing task 198 applies (<see cref="DirectMessageParticipantShares"/>): the thread must be typed Direct and must
    /// not be the per-user master thread. Why <paramref name="thread"/> fails it, or <c>null</c> when it passes.
    /// </summary>
    internal static string? WhyThreadIsNotDirect(Entity? thread)
    {
        if (thread is null || thread.Id == Guid.Empty)
            return "its thread was not found";
        if (thread.GetAttributeValue<OptionSetValue>(DirectMessageParticipantShares.ThreadTypeColumn)?.Value
            != DirectMessageParticipantShares.ThreadTypeDirect)
            return "its thread is not a Direct thread";
        if (thread.GetAttributeValue<bool?>(DirectMessageParticipantShares.DefaultThreadColumn) == true)
            return "its thread is the per-user master thread";
        return null;
    }

    /// <summary>A message confirmed for the owner write (<see cref="ConfirmDirectMoveAsync"/>): the version it was read at, its
    /// owner then, and the resolved Direct team.</summary>
    private sealed record DirectMove(Guid CommunicationId, long Version, EntityReference PreviousOwner, Guid TeamId);

    /// <summary>
    /// Steps 3 to 5 of the class remarks, writing and granting nothing: the lookup, the message read whole and judged, its thread,
    /// the resolved team. The move to make, or <c>null</c> when the message keeps its owner.
    /// </summary>
    /// <param name="unfile">Piece 3b: the message was just unfiled (<see cref="AfterReparentAsync"/>), so it is owned by the team
    /// of the record it left rather than by a user, and it is judged by <see cref="WhyNotAnEligibleUnfile"/>. <c>false</c> at a
    /// join.</param>
    /// <param name="leavesSecureOwner">Piece 3b, with <paramref name="unfile"/>: the answer before the write said the message was NOT
    /// owned in the Secure Record business unit (<see cref="DirectUnfileDue.LeavesSecureOwner"/> <c>false</c>), so its content rows
    /// move here. The owner read back must still be outside that unit; if it is in it, or cannot be told, nothing is granted or moved
    /// (<see cref="WhyOwnerLeftSecureSinceDecidedAsync"/>). Ignored at a join.</param>
    private async Task<DirectMove?> ConfirmDirectMoveAsync(
        Guid communicationId, Guid threadId, bool unfile, CancellationToken ct, bool leavesSecureOwner = false)
    {
        var resolution = await _lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
        if (resolution.IsNotConfigured && resolution.Fault is null)
        {
            _logger.LogDebug("Message {CommunicationId} keeps its owner: no Direct message owner team is configured.", communicationId);
            return null;
        }

        var message = await ReadMessageAsync(communicationId, ct).ConfigureAwait(false);
        if (message is null || message.GetAttributeValue<long?>(VersionColumn) is not { } version)
        {
            _logger.LogError(MessageNotMovedEvent,
                "Message {CommunicationId} keeps its owner: it could not be read before its Direct owner write.", communicationId);
            return null;
        }

        if (WhyNotDirect(message, threadId, unfile) is { } reason)
        {
            _logger.LogInformation("Message {CommunicationId} keeps its owner: {Reason}.", communicationId, reason);
            return null;
        }

        // Task 198's thread narrowing, applied before the owner moves: the join's audience alone does not decide it.
        Entity? thread;
        try
        {
            thread = await ReadThreadAsync(_dataverse, threadId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(MessageNotMovedEvent, ex,
                "Message {CommunicationId} keeps its owner: its thread {ThreadId} could not be read before its Direct owner write.",
                communicationId, threadId);
            return null;
        }

        if (WhyThreadIsNotDirect(thread) is { } threadReason)
        {
            _logger.LogInformation("Message {CommunicationId} keeps its owner: {Reason}.", communicationId, threadReason);
            return null;
        }

        if (!resolution.IsResolved || resolution.TeamId is not { } teamId || teamId == Guid.Empty)
        {
            _logger.LogError(OwnerNotSetEvent, resolution.Fault,
                "Message {CommunicationId} keeps its owner: the Direct message owner team did not resolve ({Refusal}, missing setting " +
                "{MissingSetting}).", communicationId, resolution.Refusal, resolution.MissingSetting);
            return null;
        }

        if (message.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id == teamId)
        {
            _logger.LogInformation("Message {CommunicationId} is already owned by the Direct message owner team.", communicationId);
            return null;
        }

        // Piece 3b: the content rows of a message decided as NOT Secure-owned move with it here; one that is Secure-owned now (or whose
        // owner cannot be told) is the secure-child reconcile's, which was not told, so it is not moved at all. The unfile stands;
        // Migrate-DirectMessagesToOwnerTeam.ps1 (054c) is the recovery.
        if (unfile && !leavesSecureOwner
            && await WhyOwnerLeftSecureSinceDecidedAsync(message, ct).ConfigureAwait(false) is { } changed)
        {
            _logger.LogError(MessageNotMovedEvent,
                "Message {CommunicationId} was unfiled and keeps its owner: the owner changed after the unfile was decided ({Reason}). " +
                "Nothing was granted or moved.", communicationId, changed);
            return null;
        }

        return new DirectMove(communicationId, version, message.GetAttributeValue<EntityReference>(OwnerColumn)!, teamId);
    }

    /// <summary>
    /// Piece 3b: why the owner read back after an unfile is in the Secure Record business unit, or cannot be told to be outside it,
    /// although the answer before the write said the message was not Secure-owned; <c>null</c> when it is outside it. The same
    /// reading as that answer: an owning unit that did not come back counts as Secure, and an environment with no Secure Record
    /// business unit has none for a message to be in. A unit that cannot be decided (two carry its name, or no configuration was
    /// given) is a reason too. A fault reading the unit propagates to <see cref="AfterReparentAsync"/>, which logs it.
    /// </summary>
    private async Task<string?> WhyOwnerLeftSecureSinceDecidedAsync(Entity message, CancellationToken ct)
    {
        if (_configuration is null)
            return "the Secure Record business unit cannot be read";

        var secureBu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
        if (secureBu.Ambiguous)
            return "the Secure Record business unit cannot be decided";

        var owningUnit = message.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;
        return secureBu.Id is { } secureId && (owningUnit is null || owningUnit == Guid.Empty || owningUnit == secureId)
            ? "it is owned in the Secure Record business unit, or its owning unit is not known"
            : null;
    }

    /// <summary>
    /// Steps 6 and 7 of the class remarks, for a move <see cref="ConfirmDirectMoveAsync"/> confirmed: the owner write, then the
    /// content rows. <c>true</c> when the message now reads back as owned by the Direct team.
    /// </summary>
    /// <param name="leavesSecureOwner">Piece 3b: the message was unfiled from the Secure team; its content rows are left to the
    /// secure-child reconcile. <c>false</c> at a join.</param>
    private async Task<bool> WriteDirectMoveAsync(DirectMove move, bool leavesSecureOwner, CancellationToken ct)
    {
        var (communicationId, version, previousOwner, teamId) = (move.CommunicationId, move.Version, move.PreviousOwner, move.TeamId);
        if (await WriteDirectTeamOwnerAsync(CommunicationTable, communicationId, version, teamId, ct).ConfigureAwait(false) is { } fault)
        {
            _logger.LogError(MessageNotMovedEvent,
                "Message {CommunicationId} keeps its owner: its Direct owner write was not confirmed ({Fault}).", communicationId, fault);
            return false;
        }

        // Piece 3b: a message unfiled from a Secure record leaves its content rows to the secure-child reconcile, which moves them
        // out of the Secure team by the record ownership resolver's Direct rule and takes their mirrored shares off with them.
        if (leavesSecureOwner)
        {
            _logger.LogInformation(
                "Message {CommunicationId} returned to the Direct team from the Secure team; its content rows follow it through the " +
                "secure-child reconcile.", communicationId);
            return true;
        }

        await MoveContentRowsAsync(communicationId, previousOwner, teamId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Why the message is not a Direct message this step may assign, or <c>null</c> when it is.</summary>
    /// <param name="unfile">Piece 3b: the message was just unfiled, so its owner is the team of the record it left and any owner
    /// read back is accepted; it is judged by <see cref="WhyNotAnEligibleUnfile"/>, the rule asked before the write. At a join
    /// (<c>false</c>) it must be owned by a user.</param>
    private static string? WhyNotDirect(Entity message, Guid threadId, bool unfile)
    {
        if (message.GetAttributeValue<EntityReference>(OwnerColumn) is not { } owner || owner.Id == Guid.Empty)
            return WhyNotAMessage(message) ?? "its owner could not be read";
        if (unfile)
            return WhyNotAnEligibleUnfile(message, threadId);

        if (WhyNotAMessage(message) is { } notAMessage)
            return notAMessage;
        if (!string.Equals(owner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
            || message.GetAttributeValue<EntityReference>(OwningTeamColumn) is { Id: var team } && team != Guid.Empty)
            return "it is not owned by a user";
        if (WhyFiledUnderARecord(message) is { } filed)
            return filed;
        if (message.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } filedThread || filedThread != threadId)
            return "it is not filed into the joined thread";
        return WhyNoDirectPair(message);
    }

    /// <summary>Why <paramref name="message"/> is filed under a record (an ownership parent or a typed regarding lookup), or
    /// <c>null</c> when it is linked to none (the join and the unfile).</summary>
    private static string? WhyFiledUnderARecord(Entity message) =>
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ParentsOf(message).Count > 0
        || DirectMessageRecipient.CarriesATypedRegarding(message)
            ? "it is filed under a record"
            : null;

    /// <summary>Why <paramref name="message"/> is not a Message row, or <c>null</c> when it is (the join and the unfile).</summary>
    internal static string? WhyNotAMessage(Entity message) =>
        message.GetAttributeValue<OptionSetValue>(TypeColumn)?.Value != (int)CommunicationType.Message ? "it is not a Message" : null;

    /// <summary>
    /// Why <paramref name="message"/> carries no valid stamped pair (<see cref="DirectMessageParticipantShares.ParticipantsOf"/>:
    /// exactly two distinct non-empty systemusers in <c>sprk_sentby</c> and <c>sprk_directrecipient</c>), or <c>null</c> when it
    /// does (the join and the unfile).
    /// </summary>
    internal static string? WhyNoDirectPair(Entity message) =>
        DirectMessageParticipantShares.ParticipantsOf(message) is { Participants: null } derivation
            ? $"it names no Direct pair ({derivation.NoPair ?? derivation.Unknown})"
            : null;

    /// <summary>The thread's type and default flag, the two columns task 198 narrows on. A fault propagates to the caller.</summary>
    private static async Task<Entity?> ReadThreadAsync(IGenericEntityService dataverse, Guid threadId, CancellationToken ct)
    {
        var query = new QueryExpression(DirectMessageParticipantShares.ThreadTable)
        {
            ColumnSet = new ColumnSet(DirectMessageParticipantShares.ThreadTypeColumn, DirectMessageParticipantShares.DefaultThreadColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(DirectMessageParticipantShares.ThreadTable + "id", ConditionOperator.Equal, threadId);
        return (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
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
                    ColumnSet = new ColumnSet(new[] { OwnerColumn, VersionColumn }.Concat(RecordLookupsOf(table)).ToArray()),
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

                // A row filed under a record is that record's, not the message's: it keeps its owner.
                if (IsFiledUnderARecord(row))
                {
                    _logger.LogInformation(
                        "Message {CommunicationId} moved to the Direct team; its {Table} row {RowId} is filed under a record and keeps its owner.",
                        communicationId, table, row.Id);
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
    /// The tables a content row may also link to and still be the message's own content: communications, documents and file
    /// versions are content, not a record the row is filed under.
    /// </summary>
    private static readonly HashSet<string> ContentTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_communication", "sprk_document", "sprk_fileversion",
    };

    /// <summary>
    /// The lookups of a content table that can file its row under a record: its lineage lookups
    /// (<see cref="SecureChildLineage.Children"/>), and for a document every record link (<see cref="DocumentLinkFields.All"/>, which
    /// also names the service request the lineage leaves out), each to an ownership-parent table that is not content.
    /// </summary>
    private static IEnumerable<string> RecordLookupsOf(string table) =>
        SecureChildLineage.Children[table].Lookups
            .Select(l => (Column: l.Key, Target: l.Value))
            .Concat(string.Equals(table, "sprk_document", StringComparison.OrdinalIgnoreCase)
                ? DocumentLinkFields.All.Select(f => (Column: f.LogicalName, Target: f.TargetEntityLogicalName))
                : [])
            .Where(l => !ContentTables.Contains(l.Target)
                        && Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(l.Target))
            .Select(l => l.Column)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>true</c> when the content row names a record it is filed under (any ownership parent that is not content).</summary>
    private static bool IsFiledUnderARecord(Entity row) =>
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ParentsOf(row).Any(p => !ContentTables.Contains(p.EntityLogicalName));

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

/// <summary>
/// A Direct join whose grant ran (<see cref="DirectMessageOwnerStep.GrantAtJoinAsync"/>), waiting for its owner part
/// (<see cref="DirectMessageOwnerStep.OwnDirectAfterContentAsync"/>). Only the step makes one, so the owner part always follows a grant.
/// </summary>
public sealed class DirectOwnerJoin
{
    internal DirectOwnerJoin(Guid communicationId, ThreadAssignment assignment)
    {
        CommunicationId = communicationId;
        Assignment = assignment;
    }

    /// <summary>The message that joined.</summary>
    public Guid CommunicationId { get; }

    /// <summary>The join's assignment (its thread and its Direct audience).</summary>
    public ThreadAssignment Assignment { get; }
}

/// <summary>Piece 3b: what an unfile does with a communication's owner (<see cref="DirectMessageOwnerStep.DecideUnfileAsync"/>).</summary>
public enum DirectUnfileVerdict
{
    /// <summary>It keeps its owner: not an eligible Direct message, or the Direct owner rule is off. Unchanged pending #2722.</summary>
    KeepOwner,

    /// <summary>An eligible Direct message, and the Direct team resolved: it returns to the Direct team after the write.</summary>
    ToDirectTeam,

    /// <summary>An eligible Direct message, and the Direct team is configured in a way that cannot be used: the unfile is refused.</summary>
    Refused,

    /// <summary>An eligible Direct message, and the lookup or its thread could not be read: the unfile is refused, try again.</summary>
    Unavailable,
}

/// <summary>Piece 3b: <see cref="DirectMessageOwnerStep.DecideUnfileAsync"/>'s answer.</summary>
/// <param name="Verdict">What the unfile does.</param>
/// <param name="Refusal">With <see cref="DirectUnfileVerdict.Refused"/>, the lookup's stable refusal code.</param>
/// <param name="Fault">With <see cref="DirectUnfileVerdict.Unavailable"/>, the fault.</param>
public sealed record DirectUnfileDecision(DirectUnfileVerdict Verdict, string? Refusal = null, Exception? Fault = null)
{
    internal static readonly DirectUnfileDecision KeepOwner = new(DirectUnfileVerdict.KeepOwner);
    internal static readonly DirectUnfileDecision ToDirectTeam = new(DirectUnfileVerdict.ToDirectTeam);

    internal static DirectUnfileDecision Refused(string refusal) => new(DirectUnfileVerdict.Refused, refusal);

    internal static DirectUnfileDecision Unavailable(Exception fault) => new(DirectUnfileVerdict.Unavailable, Fault: fault);
}

/// <summary>
/// Piece 3b: carried on a written unfile's answer (<see cref="RecordOwnerResolution.DirectUnfile"/>): the message returns to the
/// Direct message owner team (<see cref="DirectMessageOwnerStep.AfterReparentAsync"/>).
/// </summary>
/// <param name="ThreadId">The Direct thread the message sits in after the unfile; its participants are granted.</param>
/// <param name="LeavesSecureOwner">The message was owned in the Secure Record business unit before the unfile: its content rows are
/// moved by the secure-child reconcile, not by the step.</param>
public sealed record DirectUnfileDue(Guid ThreadId, bool LeavesSecureOwner);

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
