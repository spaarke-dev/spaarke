using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Fan-out targeting for a Layer-C notification ping (spec FR-08 / NFR-07): given a persisted
/// <c>sprk_communication</c> row and its <c>sprk_communicationthread</c>, returns the deduplicated set of
/// Dataverse <c>systemuserid</c>s eligible to receive a signal about that message. The producer (task 024)
/// loops the returned ids into
/// <c>SignalRDeliveryService.PingUserAsync(outboxRowId, systemuserid, kind)</c> — the ping's identity
/// resolver handles <c>systemuserid → oid</c> internally, so this output is exactly what the producer needs.
///
/// <para>
/// <b>ZERO new access logic (root §10 / §11).</b> Every inclusion/exclusion decision is COMPOSED from an
/// EXISTING record-security primitive — this service invents no authorization model of its own:
/// </para>
/// <list type="number">
///   <item><b>Candidate set = the message's junction participants ONLY</b> — the message-grain
///     <c>sprk_communicationparticipant</c> index (ADR-048). The candidate set is NEVER widened to
///     "everyone with access to the record" / a security-role query / "recent participants"; the junction
///     already encodes exactly who was on the message.</item>
///   <item><b>Internal-only exclusion</b> — reuses <see cref="ICommunicationAccessFilter.EvaluateMessage"/>
///     (its fail-closed <c>IsInternalOnlyOrUnknown</c> rule): a candidate is eligible only if the filter says
///     the message is visible to a caller with that candidate's internal-user posture. An
///     <c>sprk_isinternalonly=true</c> message (or an UNREADABLE flag — fail closed) excludes every
///     non-internal candidate. The internal-only check is NOT re-implemented here.</item>
///   <item><b>Private-thread gate</b> — reuses <see cref="IThreadPrivateGrantProvider.GetActiveGrantsAsync"/>
///     (fail-closed). For a PRIVATE thread (or a thread whose privacy cannot be confirmed Open — fail closed),
///     a candidate is eligible only if the grant provider returns an active, point-forward grant for them.
///     The default <see cref="DenyAllThreadPrivateGrantProvider"/> grants nothing, so private-thread fan-out is
///     EMPTY until a real Dataverse-backed provider ships. This service does NOT bypass that default-deny.</item>
///   <item><b>Unresolved external → no recipient</b> — a junction row with <c>sprk_isresolved=false</c> (both
///     person lookups null) resolves to no <c>systemuserid</c>, so it contributes no recipient. A natural
///     exclusion, not a special case.</item>
/// </list>
///
/// <para>
/// <b>Output grain.</b> Only a junction row resolved to a <c>systemuser</c> (an internal user) yields a
/// pingable <c>systemuserid</c>. A row resolved to a <c>contact</c> (an external party) has no
/// <c>systemuserid</c>, so it never appears in the per-user fan-out — which is why external exclusion is
/// enforced BOTH by the internal-only filter (the load-bearing contract once contact-scoped push ships in
/// R2/R3) AND by the systemuserid projection (load-bearing today). Both point the same, fail-safe way.
/// </para>
///
/// <para>
/// <b>Junction read is app-only and is NOT the access gate.</b> This service runs on the producer's
/// server-side path with no acting user, so it reads the junction app-only (via the singleton
/// <see cref="IGenericEntityService"/>, mirroring <see cref="CommunicationParticipantIndexer"/>'s own
/// app-only writes) to enumerate the CANDIDATE set. The candidate set is intentionally "everyone on the
/// message"; the internal-only filter + private-thread grant gate above are what restrict it. The junction
/// read never widens access — it only enumerates candidates for the gates to narrow.
/// </para>
///
/// <para>
/// <b>Required projected fields (producer contract).</b> The caller MUST project onto the passed entities:
/// the message's <c>sprk_isinternalonly</c> (+ <c>createdon</c> when the thread may be private) and the
/// thread's <c>sprk_privacystate</c>. A missing <c>sprk_isinternalonly</c> is treated as internal-only
/// (fail closed); a missing/non-Open <c>sprk_privacystate</c> (or a null thread) is treated as PRIVATE
/// (fail closed) — so a forgotten projection under-fans (safe), never over-fans (leak).
/// </para>
/// </summary>
public sealed class CommunicationFanOutTargetingService
{
    private const string ParticipantEntity = "sprk_communicationparticipant";
    private const string ParticipantCommunicationField = "sprk_communication";
    private const string ParticipantSystemUserField = "sprk_systemuser";
    private const string ParticipantContactField = "sprk_contact";
    private const string ParticipantIsResolvedField = "sprk_isresolved";

    private const string ThreadPrivacyStateField = "sprk_privacystate";
    private const string MessageCreatedOnField = "createdon";

    private readonly IGenericEntityService _entityService;
    private readonly ICommunicationAccessFilter _accessFilter;
    private readonly IThreadPrivateGrantProvider _grantProvider;
    private readonly ILogger<CommunicationFanOutTargetingService> _logger;

    public CommunicationFanOutTargetingService(
        IGenericEntityService entityService,
        ICommunicationAccessFilter accessFilter,
        IThreadPrivateGrantProvider grantProvider,
        ILogger<CommunicationFanOutTargetingService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _grantProvider = grantProvider ?? throw new ArgumentNullException(nameof(grantProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Computes the deduplicated set of <c>systemuserid</c>s eligible to receive a Layer-C ping about
    /// <paramref name="message"/> in <paramref name="thread"/>, by composing the existing access primitives
    /// (see the type remarks). Fail-closed throughout: when a security-relevant flag cannot be read, the
    /// candidate is EXCLUDED.
    /// </summary>
    /// <param name="message">
    /// The persisted <c>sprk_communication</c> row. Carries at least <see cref="Entity.Id"/> and
    /// <c>sprk_isinternalonly</c> (and <c>createdon</c> when the thread may be private).
    /// </param>
    /// <param name="thread">
    /// The message's <c>sprk_communicationthread</c> (carrying <see cref="Entity.Id"/> +
    /// <c>sprk_privacystate</c>). A null thread — or one whose privacy is not confirmed Open — is treated as
    /// PRIVATE (fail closed) and, under the default deny-all grant provider, yields an EMPTY recipient set.
    /// </param>
    public async Task<IReadOnlyCollection<Guid>> GetEligibleRecipientsAsync(
        Entity message,
        Entity? thread,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Id == Guid.Empty)
        {
            _logger.LogWarning("[FANOUT] Message has no id — no recipients (fail closed).");
            return Array.Empty<Guid>();
        }

        // Privacy posture — fail closed: only an EXPLICIT Open privacy state on a present thread avoids the
        // private-thread grant gate. A null thread, a missing/unreadable sprk_privacystate, or an explicit
        // Private state all route through the grant gate (mirrors CommunicationAccessFilter's fail-closed
        // IsInternalOnlyOrUnknown discipline — never default-allow on missing data).
        var threadIsPrivateOrUnknown = !IsExplicitlyOpen(thread);
        var threadId = thread?.Id ?? Guid.Empty;
        var messageCreatedOn = ReadCreatedOn(message);

        // 1) Candidate set — the message's junction participants ONLY (ADR-048). App-only read; NOT the gate.
        var participants = await LoadParticipantsAsync(message.Id, ct);

        var recipients = new HashSet<Guid>();
        foreach (var participant in participants)
        {
            // 2) Identity from the junction (ADR-048 rule 2 / ADR-034 path C): EXACTLY ONE of systemuser XOR
            //    contact for a resolved person, or neither for an unresolved external address.
            var isResolved = participant.GetAttributeValue<bool>(ParticipantIsResolvedField);
            var systemUserRef = participant.GetAttributeValue<EntityReference>(ParticipantSystemUserField);
            var contactRef = participant.GetAttributeValue<EntityReference>(ParticipantContactField);

            // Rule 4 — unresolved external (sprk_isresolved=false; both person lookups null) resolves to no
            // deliverable systemuserid, so it contributes no recipient (natural exclusion).
            if (!isResolved || (systemUserRef is null && contactRef is null))
                continue;

            // Candidate access context, built from the junction identity type. A systemuser participant is an
            // INTERNAL user (R1 model — see CommunicationAccessContext); a contact participant is EXTERNAL.
            // EvaluateMessage reads only the message flags + IsInternalUser, so a contact's Guid.Empty
            // systemuserid is never dereferenced.
            var isInternal = systemUserRef is not null;
            var callerContext = new CommunicationAccessContext(
                CallerSystemUserId: systemUserRef?.Id ?? Guid.Empty,
                IsInternalUser: isInternal,
                CallerContactId: contactRef?.Id);

            // Rule 2 — internal-only exclusion. REUSE the existing filter (fail-closed on an unreadable
            // sprk_isinternalonly via IsInternalOnlyOrUnknown). A candidate that could not READ the message is
            // never PINGED about it.
            if (!_accessFilter.EvaluateMessage(callerContext, message).IsVisible)
                continue;

            // Rule 3 — private-thread gate. REUSE the fail-closed grant provider. For a private (or
            // privacy-unknown) thread, a candidate is eligible only with an active, point-forward grant; the
            // DenyAllThreadPrivateGrantProvider default returns none, so private-thread fan-out is empty until a
            // real provider ships. Do NOT bypass default-deny to "make it work".
            if (threadIsPrivateOrUnknown &&
                !await HasActiveGrantAsync(threadId, callerContext, messageCreatedOn, ct))
            {
                continue;
            }

            // Output grain — only a systemuser-typed candidate yields a pingable systemuserid (per-user
            // fan-out targets systemuserids; a contact is external and has none). A candidate that passed the
            // gates above but is a contact simply contributes no recipient.
            if (systemUserRef is not null && systemUserRef.Id != Guid.Empty)
                recipients.Add(systemUserRef.Id);
        }

        _logger.LogDebug(
            "[FANOUT] message={MessageId} thread={ThreadId} privateOrUnknown={Private} candidates={Candidates} recipients={Recipients}",
            message.Id, threadId, threadIsPrivateOrUnknown, participants.Count, recipients.Count);

        return recipients;
    }

    /// <summary>
    /// Loads the message's participant junction rows (candidate set). App-only read via the canonical
    /// <see cref="IGenericEntityService"/>, filtered by the message lookup — mirrors
    /// <see cref="CommunicationParticipantIndexer"/>'s own junction queries. This is the queryable
    /// "who was on this message" index, not the access gate.
    /// </summary>
    private async Task<IReadOnlyList<Entity>> LoadParticipantsAsync(Guid messageId, CancellationToken ct)
    {
        var query = new QueryExpression(ParticipantEntity)
        {
            ColumnSet = new ColumnSet(
                ParticipantSystemUserField, ParticipantContactField, ParticipantIsResolvedField),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(ParticipantCommunicationField, ConditionOperator.Equal, messageId),
                },
            },
        };

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.ToList();
    }

    /// <summary>
    /// True only when the thread's <c>sprk_privacystate</c> is EXPLICITLY Open. A null thread, a missing/
    /// unreadable state, or an explicit Private state all return false → the private-thread grant gate applies
    /// (fail closed).
    /// </summary>
    private static bool IsExplicitlyOpen(Entity? thread)
    {
        var state = thread?.GetAttributeValue<OptionSetValue>(ThreadPrivacyStateField);
        return state is not null && state.Value == (int)ThreadPrivacyState.Open;
    }

    /// <summary>
    /// Fail-closed active-grant check for a candidate on a private thread — composes
    /// <see cref="IThreadPrivateGrantProvider.GetActiveGrantsAsync"/> (which itself returns EMPTY on any error).
    /// A grant is honored only for THIS candidate (<see cref="ThreadPrivateGrant.PrincipalId"/> match) and only
    /// point-forward (<c>message.createdon &gt;= grant.EffectiveFrom</c>, per D-04). An unreadable
    /// <c>createdon</c> cannot satisfy the point-forward boundary, so it excludes (fail closed). Under the
    /// default deny-all provider this always returns false.
    /// </summary>
    private async Task<bool> HasActiveGrantAsync(
        Guid threadId,
        CommunicationAccessContext caller,
        DateTimeOffset? messageCreatedOn,
        CancellationToken ct)
    {
        if (threadId == Guid.Empty || caller.CallerSystemUserId == Guid.Empty)
            return false; // no thread to grant against, or a non-systemuser candidate → cannot hold a grant.

        var grants = await _grantProvider.GetActiveGrantsAsync(threadId, caller, ct);
        if (grants.Count == 0)
            return false;

        return messageCreatedOn is { } createdOn
            && grants.Any(g => g.PrincipalId == caller.CallerSystemUserId && createdOn >= g.EffectiveFrom);
    }

    private static DateTimeOffset? ReadCreatedOn(Entity message)
    {
        var value = message.GetAttributeValue<DateTime?>(MessageCreatedOnField);
        return value is { } dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : null;
    }
}
