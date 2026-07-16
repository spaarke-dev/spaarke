using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Default-deny composition implementation of <see cref="ICommunicationAccessFilter"/> (FR-08 / NFR-06).
/// Reads only human-owned fields + existing security derivations — NO AI dependency exists on this type, so
/// "no AI at read time" (ADR-015) is a structural guarantee, not a runtime check. Every failure path denies.
/// </summary>
public sealed class CommunicationAccessFilter : ICommunicationAccessFilter
{
    // Message (sprk_communication) fields — as-built names (notes/messaging-schema-spec.md).
    private const string MessageIsPrivateField = "sprk_isprivate";
    private const string MessageIsInternalOnlyField = "sprk_isinternalonly";
    private const string MessagePrivilegeField = "sprk_privilegeclassification";
    private const string MessageCreatedOnField = "createdon";

    // Thread (sprk_communicationthread) fields — as-built names (task 004 schema).
    private const string ThreadEntity = "sprk_communicationthread";
    private const string ThreadPrivacyStateField = "sprk_privacystate";
    private const string ThreadPrivacyEffectiveFromField = "sprk_privacyeffectivefrom";
    private const string ThreadRegardingRecordIdField = "sprk_regardingrecordid";
    private const string ThreadRegardingRecordTypeField = "sprk_regardingrecordtype";

    private static readonly string[] ThreadColumns =
    {
        ThreadPrivacyStateField,
        ThreadPrivacyEffectiveFromField,
        ThreadRegardingRecordIdField,
        ThreadRegardingRecordTypeField,
    };

    private readonly IGenericEntityService _entityService;
    private readonly IMembershipResolverService _membership;
    private readonly IThreadPrivateGrantProvider _grantProvider;
    private readonly ILogger<CommunicationAccessFilter> _logger;

    public CommunicationAccessFilter(
        IGenericEntityService entityService,
        IMembershipResolverService membership,
        IThreadPrivateGrantProvider grantProvider,
        ILogger<CommunicationAccessFilter> logger)
    {
        _entityService = entityService;
        _membership = membership;
        _grantProvider = grantProvider;
        _logger = logger;
    }

    public async Task<ThreadAccessBasis> ResolveThreadAccessBasisAsync(
        CommunicationAccessContext caller,
        Guid threadId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        // ── Load the thread privacy snapshot + anchor. A load failure FAILS CLOSED (deny every message). ──
        Entity thread;
        try
        {
            thread = await _entityService.RetrieveAsync(ThreadEntity, threadId, ThreadColumns, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[COMM-ACCESS] Thread {ThreadId} could not be loaded — DENYING all messages (fail closed).", threadId);
            return ThreadAccessBasis.Denied(threadId);
        }

        if (thread is null)
        {
            _logger.LogWarning(
                "[COMM-ACCESS] Thread {ThreadId} not found — DENYING all messages (fail closed).", threadId);
            return ThreadAccessBasis.Denied(threadId);
        }

        var privacyState = ReadPrivacyState(thread);
        var effectiveFrom = ReadDateTimeOffset(thread, ThreadPrivacyEffectiveFromField);

        var hasOpenMembership = await ResolveOpenMembershipAsync(caller, thread, threadId, ct);
        var grants = await ResolveGrantsAsync(caller, threadId, ct);

        return new ThreadAccessBasis(
            threadId,
            ThreadLoaded: true,
            privacyState,
            effectiveFrom,
            hasOpenMembership,
            grants);
    }

    public CommunicationAccessDecision EvaluateMessage(
        CommunicationAccessContext caller,
        ThreadAccessBasis basis,
        Entity message)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(message);

        // Fail closed: an unresolved thread hides everything on it.
        if (!basis.ThreadLoaded)
            return CommunicationAccessDecision.Deny("thread-unresolved");

        var privilege = ReadPrivilege(message);

        // ── Dimension 1: internal-only (independent of privacy). Hidden from non-internal callers (D-05). ──
        var isInternalOnly = message.GetAttributeValue<bool>(MessageIsInternalOnlyField);
        if (isInternalOnly && !caller.IsInternalUser)
            return CommunicationAccessDecision.Deny("internal-only");

        // ── Dimension 2: privacy (membership ∪ grant), point-forward. ──
        var createdOn = ReadDateTimeOffset(message, MessageCreatedOnField);

        if (IsMessageEffectivelyPrivate(message, basis, createdOn))
        {
            // Private → requires an ACTIVE grant whose EffectiveFrom covers this message (point-forward).
            if (createdOn is null)
                return CommunicationAccessDecision.Deny("private-no-timestamp"); // fail closed — cannot verify point-forward

            var covered = basis.CallerGrants.Any(g => createdOn.Value >= g.EffectiveFrom);
            if (!covered)
                return CommunicationAccessDecision.Deny("private-no-grant");
        }
        else
        {
            // Open → requires ADR-034 open-thread membership on the anchor record.
            if (!basis.CallerHasOpenMembership)
                return CommunicationAccessDecision.Deny("open-not-member");
        }

        // ── Dimension 3: privilege — composed metadata only; it did NOT gate this read (ADR-015). ──
        return CommunicationAccessDecision.Allow(privilege);
    }

    public async Task<CommunicationAccessResult> FilterThreadMessagesAsync(
        CommunicationAccessContext caller,
        Guid threadId,
        IReadOnlyCollection<Entity> messages,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var basis = await ResolveThreadAccessBasisAsync(caller, threadId, ct);

        var visible = new List<Entity>(messages.Count);
        var decisions = new List<(Entity, CommunicationAccessDecision)>(messages.Count);

        foreach (var message in messages)
        {
            var decision = EvaluateMessage(caller, basis, message);
            decisions.Add((message, decision));
            if (decision.IsVisible)
                visible.Add(message);
        }

        return new CommunicationAccessResult(visible, decisions);
    }

    /// <summary>
    /// Point-forward determination of whether a message is EFFECTIVELY private right now (D-04). Single-flip model
    /// (R1): the thread carries ONE <c>sprk_privacyeffectivefrom</c> — the moment of the last flip.
    /// <list type="bullet">
    ///   <item>Message-level <c>sprk_isprivate</c> always wins → private.</item>
    ///   <item>Thread Private + null effective-from → wholly private (fail closed).</item>
    ///   <item>Thread Private + effective-from T → messages authored <c>&gt;= T</c> are private (point-forward);
    ///         earlier messages keep their prior (Open) visibility.</item>
    ///   <item>Thread Open + null effective-from → born open → not private.</item>
    ///   <item>Thread Open + effective-from T → the thread was OPENED at T; messages authored <c>&lt; T</c> stay
    ///         private (prior private messages are not retroactively exposed); later messages are open.</item>
    /// </list>
    /// When the state/effective-from require a timestamp comparison but <c>createdon</c> is missing, returns
    /// <c>true</c> (private) — fail closed.
    /// </summary>
    private static bool IsMessageEffectivelyPrivate(Entity message, ThreadAccessBasis basis, DateTimeOffset? createdOn)
    {
        if (message.GetAttributeValue<bool>(MessageIsPrivateField))
            return true;

        switch (basis.PrivacyState)
        {
            case CommunicationPrivacyState.Open:
                if (basis.PrivacyEffectiveFrom is null)
                    return false; // never flipped → born open
                if (createdOn is null)
                    return true;  // fail closed — cannot place the message relative to the open-flip
                return createdOn.Value < basis.PrivacyEffectiveFrom.Value; // pre-open messages stay private

            case CommunicationPrivacyState.Private:
                if (basis.PrivacyEffectiveFrom is null)
                    return true;  // wholly private (fail closed)
                if (createdOn is null)
                    return true;  // fail closed
                return createdOn.Value >= basis.PrivacyEffectiveFrom.Value; // private from the flip forward

            default:
                return true; // unknown state → fail closed
        }
    }

    private async Task<bool> ResolveOpenMembershipAsync(
        CommunicationAccessContext caller, Entity thread, Guid threadId, CancellationToken ct)
    {
        var anchorType = thread.GetAttributeValue<string>(ThreadRegardingRecordTypeField);
        var anchorIdRaw = thread.GetAttributeValue<string>(ThreadRegardingRecordIdField);

        // Anchorless (Direct 1:1) thread → no open membership; visible only to grant-holders. Fail closed.
        if (string.IsNullOrWhiteSpace(anchorType) || string.IsNullOrWhiteSpace(anchorIdRaw))
            return false;

        if (!Guid.TryParse(anchorIdRaw, out var anchorId))
        {
            _logger.LogWarning(
                "[COMM-ACCESS] Thread {ThreadId} has an unparseable anchor id '{AnchorId}' — treating as no open membership.",
                threadId, anchorIdRaw);
            return false;
        }

        // Guard: an unresolved caller cannot be a member.
        if (caller.CallerSystemUserId == Guid.Empty)
            return false;

        try
        {
            var membership = await _membership.ResolveAsync(caller.CallerSystemUserId, anchorType, options: null, ct);
            return membership.Ids.Contains(anchorId);
        }
        catch (Exception ex)
        {
            // NFR-06: a membership-resolution failure must NOT open access.
            _logger.LogWarning(ex,
                "[COMM-ACCESS] Membership resolution failed for caller {Caller} on {AnchorType} {AnchorId} — DENYING open access (fail closed).",
                caller.CallerSystemUserId, anchorType, anchorId);
            return false;
        }
    }

    private async Task<IReadOnlyList<ThreadPrivateGrant>> ResolveGrantsAsync(
        CommunicationAccessContext caller, Guid threadId, CancellationToken ct)
    {
        try
        {
            return await _grantProvider.GetActiveGrantsAsync(threadId, caller, ct)
                   ?? Array.Empty<ThreadPrivateGrant>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[COMM-ACCESS] Grant resolution failed for caller {Caller} on thread {ThreadId} — DENYING private access (fail closed).",
                caller.CallerSystemUserId, threadId);
            return Array.Empty<ThreadPrivateGrant>();
        }
    }

    private static CommunicationPrivacyState ReadPrivacyState(Entity thread)
    {
        var osv = thread.GetAttributeValue<OptionSetValue>(ThreadPrivacyStateField);
        if (osv is null)
            return CommunicationPrivacyState.Private; // absent → fail closed
        return Enum.IsDefined(typeof(CommunicationPrivacyState), osv.Value)
            ? (CommunicationPrivacyState)osv.Value
            : CommunicationPrivacyState.Private; // unknown integer → fail closed
    }

    private static CommunicationPrivilegeClassification ReadPrivilege(Entity message)
    {
        var osv = message.GetAttributeValue<OptionSetValue>(MessagePrivilegeField);
        if (osv is null)
            return CommunicationPrivilegeClassification.None;
        return Enum.IsDefined(typeof(CommunicationPrivilegeClassification), osv.Value)
            ? (CommunicationPrivilegeClassification)osv.Value
            : CommunicationPrivilegeClassification.None;
    }

    /// <summary>
    /// Reads a Dataverse date attribute (SDK surfaces these as <see cref="DateTime"/> in UTC) as a
    /// <see cref="DateTimeOffset"/>. Returns <c>null</c> when absent.
    /// </summary>
    private static DateTimeOffset? ReadDateTimeOffset(Entity entity, string field)
    {
        var value = entity.GetAttributeValue<DateTime?>(field);
        if (value is null)
            return null;
        var utc = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
        return new DateTimeOffset(utc);
    }
}
