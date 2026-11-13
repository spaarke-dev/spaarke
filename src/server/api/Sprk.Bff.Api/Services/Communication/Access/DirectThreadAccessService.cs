using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Default <see cref="IDirectThreadAccessService"/> — see the interface for the mechanism + Component
/// Justification. Reads/writes <c>sprk_communicationthread</c> via the canonical
/// <see cref="IGenericEntityService"/> (SDK) and POA shares via <see cref="IDataverseRecordShareService"/>
/// (Web API). Singleton-safe: all three dependencies are stateless singletons.
/// </summary>
public sealed class DirectThreadAccessService : IDirectThreadAccessService
{
    private const string ThreadEntity = "sprk_communicationthread";
    private const string ThreadEntitySet = "sprk_communicationthreads";
    private const string MessageEntitySet = "sprk_communications";
    private const string SystemUserEntity = "systemuser";

    // sprk_communicationthread.sprk_threadtype choice integers (task 004 schema; mirrors ThreadTopology).
    private const int ThreadTypeDirect = 100000001;
    private const int PrivacyStateOpen = 100000000;

    private const string ReadAccessRights = "ReadAccess";

    /// <summary>uac-r2 198 pass 2 item 7: the Read bit of a POA row's mask (as in GateDecisionRecordSharing).</summary>
    private const int ReadAccessMask = 1;

    /// <summary>
    /// uac-r2 198b: one page of the find-or-create candidate scan (the Direct threads one side of the pair owns, newest first).
    /// </summary>
    private const int CandidatePageSize = 50;

    /// <summary>
    /// uac-r2 198b: the bound of the find-or-create candidate scan, per direction — 10 pages of <see cref="CandidatePageSize"/>.
    /// Each candidate costs two reads (the thread row and its shares, fm's strict-pair reader), so the bound keeps a first
    /// message from a user with very many Direct threads to a bounded number of reads. The pair's thread is normally recent, and
    /// the scan is newest-first. Past the bound reuse is NOT decided: a new thread is created and a Warning says a duplicate is
    /// possible (NFR-03: a cap is visible).
    /// </summary>
    private const int MaxCandidateThreads = 500;

    private const string MessageEntity = "sprk_communication";

    private readonly IGenericEntityService _entityService;
    private readonly IDataverseRecordShareService _accessGrant;
    private readonly Lazy<IThreadMembershipDerivationService> _membershipDerivation;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DirectThreadAccessService> _logger;
    private readonly Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing _gateDecisionRecords;
    private readonly DirectMessageOwnerTeam _directMessageOwnerTeam;
    private readonly DataverseWebApiClient? _webApi;
    private readonly CoreAncestorResolver? _coreAncestors;
    private readonly IServiceScopeFactory? _scopes;

    /// <param name="membershipDerivation">
    /// Lazily-resolved (task 052 / FR-11): <see cref="IThreadMembershipDerivationService"/>'s default
    /// implementation depends on <c>IThreadExplicitParticipantReader</c>, whose Direct-topology
    /// implementation depends back on THIS service (<see cref="IDirectThreadAccessService"/>) — a genuine
    /// 3-node DI cycle (DirectThreadAccessService → IThreadMembershipDerivationService →
    /// IThreadExplicitParticipantReader → IDirectThreadAccessService) that the default container cannot
    /// construct eagerly. <see cref="Lazy{T}"/> defers resolution to first use inside
    /// <see cref="GrantMessageAccessAsync"/> (well after this singleton is already constructed and cached),
    /// which breaks the cycle without changing either dependency's shape.
    /// </param>
    /// <param name="configuration">The Secure Record business-unit and owner-team names (task 149 r1): a message owned by
    /// that team is a secure child, and its shares are <see cref="SecureChildShareSynchronizer"/>'s alone.</param>
    /// <param name="gateDecisionRecords">Ontology task 042b (D-169): an unfiled communication's gate Decision Records follow its shares.</param>
    /// <param name="directMessageOwnerTeam">fm task 054a: the Direct-message owner team lookup (uac-r2 task 198 Part B).</param>
    /// <param name="webApi">uac-r2 task 198 Part C: the user-flag read of the Restricted bar every Direct grant applies
    /// (<see cref="RestrictedFilingShareBar.EligibleForGrantAsync"/>). Optional so the test compositions keep compiling; the host
    /// registers it. Without it no participant of a Direct-team-owned message is granted here (fail closed; the share job
    /// grants them).</param>
    /// <param name="coreAncestors">uac-r2 task 198 Part C: the column probe of the bar's filing walk. Optional; without it a
    /// participant flagged external is not granted here.</param>
    /// <param name="scopes">uac-r2 task 197b: a scope per decision for THE external visibility predicate (the 191h seam,
    /// <c>AppOnlyExternalVisibility</c>, registered scoped; this service is a singleton). Optional so the test compositions keep
    /// compiling; the container always supplies it. Without it no participant flagged external is granted here (the share job
    /// grants them).</param>
    public DirectThreadAccessService(
        IGenericEntityService entityService,
        IDataverseRecordShareService accessGrant,
        Lazy<IThreadMembershipDerivationService> membershipDerivation,
        IConfiguration configuration,
        ILogger<DirectThreadAccessService> logger,
        Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing gateDecisionRecords,
        DirectMessageOwnerTeam directMessageOwnerTeam,
        DataverseWebApiClient? webApi = null,
        CoreAncestorResolver? coreAncestors = null,
        IServiceScopeFactory? scopes = null)
    {
        _webApi = webApi;
        _coreAncestors = coreAncestors;
        _scopes = scopes;
        _directMessageOwnerTeam = directMessageOwnerTeam ?? throw new ArgumentNullException(nameof(directMessageOwnerTeam));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _accessGrant = accessGrant ?? throw new ArgumentNullException(nameof(accessGrant));
        _membershipDerivation = membershipDerivation ?? throw new ArgumentNullException(nameof(membershipDerivation));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gateDecisionRecords = gateDecisionRecords ?? throw new ArgumentNullException(nameof(gateDecisionRecords));
    }

    /// <inheritdoc />
    public async Task<Guid> FindOrCreateDirectThreadAsync(
        Guid callerSystemUserId,
        Guid otherSystemUserId,
        CancellationToken ct = default)
    {
        // Ownership is not order-significant for REUSE: whichever participant started the thread first owns
        // it, so check both directions (owner=caller/shared=other, then owner=other/shared=caller).
        var existing = await FindExistingAsync(callerSystemUserId, otherSystemUserId, ct)
            ?? await FindExistingAsync(otherSystemUserId, callerSystemUserId, ct);

        if (existing is { } foundThreadId)
        {
            _logger.LogInformation(
                "Reused existing Direct thread {ThreadId} for participant pair ({A}, {B})",
                foundThreadId, callerSystemUserId, otherSystemUserId);
            return foundThreadId;
        }

        // CREATE — owned by the caller; NO ADR-024 regarding anchor (a Direct thread is person-to-person,
        // the sprk_threadtype discriminator alone distinguishes it — root §.6.5 / ADR-024 constraint).
        var thread = new Entity(ThreadEntity)
        {
            ["sprk_name"] = "Direct Conversation",
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
            ["ownerid"] = new EntityReference(SystemUserEntity, callerSystemUserId),
        };

        var threadId = await _entityService.CreateAsync(thread, ct);

        // Establish the explicit two-party list: "Manage access" (POA) share to the other participant. The thread was just
        // created owned by the CALLER with no regarding record, so it is never a secure child and the secure-child share
        // synchronizer (task 149), which touches only Secure-team-owned rows, never revokes this share.
        await _accessGrant.GrantAccessAsync(
            ThreadEntitySet, threadId, DataversePrincipalRef.User(otherSystemUserId), ReadAccessRights, ct);

        _logger.LogInformation(
            "Created Direct thread {ThreadId} owned by {Owner}, shared to {Other}",
            threadId, callerSystemUserId, otherSystemUserId);

        return threadId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetParticipantSystemUserIdsAsync(Guid threadId, CancellationToken ct = default)
    {
        var thread = await _entityService.RetrieveAsync(
            ThreadEntity, threadId, new[] { "sprk_threadtype", "ownerid" }, ct);

        if (thread is null || thread.Id == Guid.Empty)
            return Array.Empty<Guid>();

        var topology = thread.GetAttributeValue<OptionSetValue>("sprk_threadtype")?.Value;
        if (topology != ThreadTypeDirect)
            return Array.Empty<Guid>(); // topology gate — Open/record-anchored threads are unaffected

        // uac-r2 198 pass 2 item 7: a share-read fault THROWS here. This list is the ACS membership reconcile's desired set
        // (DirectThreadExplicitParticipantReader → ThreadMembershipDerivationService → MembershipReconciler), so "the owner
        // alone" on a fault would remove the other participant from the chat. The throw fails the reconcile before it removes anyone:
        // MembershipReconcileJob.IsRetryable retries a transient fault (an HTTP or network fault, a 429, 503, 504 or timeout, a cancelled
        // task, a Service Bus or ACS rate-limit fault) up to the job's max attempts, and dead-letters any other fault at once. Either
        // way no member is removed, and MembershipReconcileSweepService repairs the membership later.
        return await ReadOwnerAndSharesAsync(threadId, thread, shareFaultIsNoShares: false, ct);
    }

    /// <summary>
    /// A Direct thread's two-party list: its user owner ∪ the systemusers holding Read on it through a POA share. No
    /// topology check: the caller has decided the thread is Direct (the gate above, or the join for a grant, #1629).
    /// <paramref name="shareFaultIsNoShares"/>: true only for the grant, where a fault grants fewer (fail closed); false
    /// makes a fault throw.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ReadOwnerAndSharesAsync(
        Guid threadId, Entity thread, bool shareFaultIsNoShares, CancellationToken ct)
    {
        var participants = new List<Guid>();
        if (thread.GetAttributeValue<EntityReference>("ownerid") is { } owner
            && string.Equals(owner.LogicalName, SystemUserEntity, StringComparison.OrdinalIgnoreCase))
        {
            participants.Add(owner.Id);
        }

        IReadOnlyList<Guid> shared;
        try
        {
            shared = await GetSharedSystemUserIdsAsync(threadId, ct);
        }
        catch (Exception ex) when (shareFaultIsNoShares)
        {
            _logger.LogWarning(
                ex,
                "Failed to read Direct thread {ThreadId} POA shares (non-fatal; treating as no shares).",
                threadId);
            shared = Array.Empty<Guid>();
        }

        foreach (var id in shared)
        {
            if (!participants.Contains(id))
                participants.Add(id);
        }

        return participants;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Generalized (task 052 / FR-11): the audience decides the principal source, the grant call is the SAME one call in
    /// every branch (<see cref="IDataverseRecordShareService.GrantAccessAsync"/>). <b>Direct</b> → the owner ∪ POA two-party
    /// list (task 043). <b>Record</b> → the anchor record's grantees
    /// (<see cref="IThreadMembershipDerivationService.DeriveAnchorGranteeUserIdsAsync"/>; contacts skipped — R2 scope).
    /// <para><b>The audience is the join's, not a re-read (#1629 F-C).</b> A thread's <c>sprk_threadtype</c> and regarding
    /// lookups are columns its owner can rewrite. Re-reading them here let an owner flip a joined record thread to Direct
    /// (granting the message to themselves and their sharees) or swap its lookup to another record between the join and
    /// the grant. <paramref name="assignment"/> carries the topology and anchor the join matched (Tier 2: the message's own
    /// regarding) or read, so neither is read again.</para>
    /// <para><b>Never the message's owning team (#1629 F-A, F2).</b> A team-owned message is already readable by that
    /// team's members; expanding it (the business unit's default team owns ordinary records: 174 members for one dev
    /// matter) cost one synchronous share per member on the send/ingest path, every one redundant. The team skipped is the
    /// one the message read below names, not the anchor's owner: the message's owner is the default team of its primary
    /// ownership parent's business unit, which is not the anchor's owner for a party anchor, a non-default owner team, or
    /// a message whose primary parent is another record — and those teams' members are granted.</para>
    /// <para><b>Never on a secure message</b> (unified-access-control-r2 task 149 r1). A message filed under a secure
    /// project, matter or work assignment is owned by the Secure Record Owners team (task 146), and exactly the root's
    /// internal sharees may read it — <see cref="SecureChildShareSynchronizer"/> keeps its shares equal to the root's and
    /// revokes every other. The participant set here comes from the anchor record's membership lookups and explicit
    /// overlays, not from the root's shares, so granting it would show the message to people the secure record is not
    /// shared with until the next reconcile revoked them — a per-message over-share and a fight between two writers. Such a
    /// message is therefore never granted here; its sharees reach it through the synchronizer. When the message's owner
    /// cannot be determined, nothing is granted (fail closed, ADR-003; a missed grant is this method's documented
    /// best-effort degradation).</para>
    /// </remarks>
    public async Task GrantMessageAccessAsync(Guid communicationId, ThreadAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var threadId = assignment.ThreadId;
        try
        {
            if (assignment.Audience == ThreadGrantAudience.None)
                return;

            var (ordinary, messageOwningTeam) = await ReadMessageOwnershipAsync(communicationId, threadId, ct);
            if (!ordinary)
                return;

            // Task 198 Part B: a message owned by the Direct-message owner team (fm 054) is read by exactly its thread's
            // participants, through their own shares (the team has no members).
            // Fix round 1 R1: only a PROVEN Direct owner takes that branch. Any message whose owner is not proven Direct behaves
            // exactly as before task 198. When it cannot be told, the record-anchored grant below still runs (an unfiled message
            // has no anchor, so it grants nothing), and only the participant (Direct) grant is skipped: the two-minute share job
            // (SecureChildShareReconciliationJob) lists a Direct-team-owned message and grants its participants.
            if (messageOwningTeam is { } owningTeam)
            {
                switch (await IsDirectMessageOwnerTeamAsync(owningTeam, ct))
                {
                    case null when assignment.Audience == ThreadGrantAudience.DirectParticipants:
                        _logger.LogWarning(
                            "Message {CommunicationId} is team-owned and whether that team is the Direct-message owner team " +
                            "cannot be determined; no participant is granted now, and the share reconciliation retries it " +
                            "(ThreadId={ThreadId}).", communicationId, threadId);
                        return;
                    case true:
                        await GrantDirectTeamMessageAsync(communicationId, assignment, owningTeam, ct);
                        // Ontology D-215 (task 198 Part E): a Direct message's gate Decision Records follow its participants who read it now.
                        // No revoke path removes a participant's Read here, so no revoke mirror exists (D-215 rule 5).
                        await _gateDecisionRecords.MirrorCommunicationSharesAsync(communicationId, ct);
                        return;
                }
            }

            IReadOnlyList<Guid> principals = assignment.Audience switch
            {
                // uac-r2 197b: a participant flagged external is granted only what THE predicate allows (the NoRoot exemption only
                // for a member of a recognized unfiled Direct message's stamped pair, bound i').
                ThreadGrantAudience.DirectParticipants => await ClearDirectParticipantsAsync(
                    communicationId, threadId, await ReadDirectParticipantsForGrantAsync(threadId, ct), ct),
                ThreadGrantAudience.AnchorRecordMembers => await ClearAnchorGranteesAsync(
                    communicationId, threadId, await GetAnchorGranteesAsync(communicationId, assignment, messageOwningTeam, ct), ct),
                _ => Array.Empty<Guid>(),
            };

            if (principals.Count > 0)
            {
                await GrantReadAccessToPrincipalsAsync(communicationId, threadId, principals, ct);
                // Ontology 042b (D-169): a user-owned message's gate Decision Records follow its shares; a revoke path added here must mirror the revoke.
                if (messageOwningTeam is null)
                    await _gateDecisionRecords.MirrorCommunicationSharesAsync(communicationId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Message access grant lookup failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}",
                communicationId, threadId);
        }
    }

    /// <summary>
    /// THE one check that <paramref name="owningTeam"/> is the Direct-message owner team (fm 054a's lookup, the pattern of
    /// <see cref="ReadMessageOwnershipAsync"/>'s Secure check). <c>true</c>: it is; <c>false</c>: it is not; <c>null</c>: it
    /// cannot be told, and the caller grants nothing.
    /// <list type="bullet">
    /// <item>The team's own row proves it is not the Direct team (<see cref="DirectMessageOwnerTeam.CannotBeDirectOwnerTeamAsync"/>
    /// answers <c>true</c>): <c>false</c>. Its <c>false</c> is "not proven", never "safe": the full resolution decides.</item>
    /// <item>Resolved: <c>true</c> exactly when the resolved team is <paramref name="owningTeam"/>.</item>
    /// <item>Refused as <see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/>: <c>false</c>. No Direct team is configured,
    /// so fm's writer assigns no message to one.</item>
    /// <item>Any other refusal, or a fault (in the one-row check or in the resolution): <c>null</c> (unknown).</item>
    /// </list>
    /// Task 198 ruling Q5: only <see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/> settles "not Direct" (no Direct team can
    /// exist then, and fm's writer assigns none). Every other answer that is not a resolution is unknown, so every unknown reaches
    /// the caller's one fail-closed branch (nothing granted; the message is queued for re-grant, Part C). This is the ONE place
    /// the decision is made, so a change to the lookup's answer shape changes only this method.
    /// </summary>
    /// <remarks>Task 198 Part C: the decision itself lives in <see cref="DirectMessageParticipantShares.IsDirectOwnerTeamAsync"/>,
    /// shared with the re-grant, so the live grant and the re-grant can never decide differently (K3: ids compared only when
    /// resolved and non-empty).</remarks>
    private Task<bool?> IsDirectMessageOwnerTeamAsync(Guid owningTeam, CancellationToken ct) =>
        DirectMessageParticipantShares.IsDirectOwnerTeamAsync(_entityService, _directMessageOwnerTeam, owningTeam, _logger, ct);

    /// <summary>
    /// Task 198 Part B: the content rows of a Direct-team-owned message that its readers need, each as (table, the lookup to
    /// the message). They are granted to the same participants when the same Direct team owns them. Entity sets come from
    /// <see cref="SecureChildLineage.Children"/>. Gate Decision Records (<c>sprk_decisionrecord</c>) also follow the message to
    /// the Direct team, but their shares are <c>GateDecisionRecordSharing</c>'s (task 198 Part E, D-215: the mirror after this grant), never
    /// this list's.
    /// </summary>
    private static readonly (string Table, string MessageLookup)[] DirectContentRows =
    [
        ("sprk_communicationattachment", "sprk_communication"),
        ("sprk_communicationparticipant", "sprk_communication"),
        ("sprk_emailreviewlog", "sprk_communication"),
        ("sprk_document", "sprk_relatedcommunication"),
    ];

    /// <summary>Bounds each content-row read of one message (a defensive cap; a capped read is logged).</summary>
    private const int MaxDirectContentRowsPerTable = 250;

    /// <summary>
    /// A Direct-team-owned message: Read to exactly its participants — the message's BFF-stamped sender and recipient (fix
    /// round 1 R2; the sender included: the sender no longer owns the row) — on the message and on its content rows the same
    /// team owns. Never the anchor record's grantees, never the thread's owner or sharees as such, and never a team: a join
    /// that is not Direct grants nothing here.
    /// </summary>
    private async Task GrantDirectTeamMessageAsync(
        Guid communicationId, ThreadAssignment assignment, Guid directTeam, CancellationToken ct)
    {
        var threadId = assignment.ThreadId;
        if (assignment.Audience != ThreadGrantAudience.DirectParticipants)
        {
            _logger.LogWarning(
                "Message {CommunicationId} is owned by the Direct-message owner team but its join is {Audience}, not Direct; " +
                "no principal is granted (ThreadId={ThreadId}).", communicationId, assignment.Audience, threadId);
            return;
        }

        // Fix round 1 R2: the ONE participant rule the re-file step and the share job also apply — the message's BFF-stamped
        // sender and recipient (sprk_sentby, sprk_directrecipient), never the thread's owner or shares. An invalid pair grants
        // nobody and is settled (fm 054a logs a missing recipient at create); a read that faults grants nobody and the share job
        // retries the message.
        var derived = await DirectMessageParticipantShares.DeriveParticipantsAsync(_entityService, communicationId, _logger, ct);
        if (derived.NoPair is { } noPair)
        {
            _logger.LogInformation(
                "Message {CommunicationId} is owned by the Direct-message owner team, but {Reason}; no participant is granted " +
                "(ThreadId={ThreadId}).", communicationId, noPair, threadId);
            return;
        }

        if (derived.Participants is not { Count: > 0 } derivedParticipants)
        {
            _logger.LogWarning(
                "Message {CommunicationId}: its participants are unknown ({Reason}); none is granted now, and the share " +
                "reconciliation retries it (ThreadId={ThreadId}).", communicationId, derived.Unknown, threadId);
            return;
        }

        IReadOnlyList<Guid> participants = derivedParticipants.ToList();

        // Task 198 Part C (ruling 6): every Direct grant applies the Restricted bar — the message's own or inherited Access
        // Permission, through the one predicate. A participant it does not clear (barred, unread, or undecidable) is not granted
        // here; the share job retries the message. R2: a disabled participant is not granted either (settled, not retried).
        var bar = new RestrictedFilingShareBar(
            _entityService, _coreAncestors is { } resolver ? resolver.ProbeColumnsAsync : null, _accessGrant, _webApi, _logger);
        var eligibility = await bar.EligibilityForGrantAsync(communicationId, participants, excludeDisabled: true, ct);
        if (!eligibility.Complete)
        {
            _logger.LogWarning(
                "Message {CommunicationId}: {NotCleared} of its {Participants} participant(s) could not be cleared by the Restricted " +
                "bar; they are not granted now, and the share reconciliation retries it (ThreadId={ThreadId}).",
                communicationId, participants.Count - eligibility.Eligible.Count, participants.Count, threadId);
        }

        participants = eligibility.Eligible;
        if (participants.Count == 0)
            return;

        // Fix round 1 R5: the message may have been re-filed since its owner was read; a changed owner grants nothing here.
        if (!await DirectMessageParticipantShares.OwnerUnchangedAsync(_entityService, communicationId, directTeam, "grant", _logger, ct))
            return;

        var contentRows = await ReadDirectContentRowsAsync(communicationId, threadId, directTeam, ct);

        // uac-r2 task 197b: a participant flagged external is granted a row only when THE predicate allows it
        // (DirectMessageParticipantShares.DecideForDirectParticipantsAsync, one call over the message and its content rows). The
        // participant-scoped NoRoot exemption applies only when the message is positively recognized as an unfiled Direct message
        // (bound i': RecognizeUnfiledDirectMessageAsync) whose stamped pair is this audience (bound iii). This grant never revokes:
        // a flagged participant's share on a hidden row is removed by the share job's reconcile, which lists the message after its
        // owner write. Unflagged participants never ask the predicate.
        var flagged = participants.Where(eligibility.FlaggedExternal.Contains).ToHashSet();
        Func<string, Guid, IReadOnlyList<Guid>> granteesFor = (_, _) => participants;
        DirectMessageParticipantShares.DirectMessageRecognition? recognition = null;
        if (flagged.Count > 0)
        {
            recognition = await DirectMessageParticipantShares.RecognizeUnfiledDirectMessageAsync(_entityService, communicationId, _logger, ct);
            if (recognition is not null && !recognition.Pair.SetEquals(derivedParticipants))
                recognition = null; // re-stamped since the audience was read: no exemption on this pass
            var answers = await DecideForFlaggedParticipantsAsync(
                communicationId, contentRows.Append((Table: MessageEntity, Id: communicationId)).ToList(),
                directPairRecognized: recognition is not null, ct);
            granteesFor = (table, id) => participants
                .Where(u => !flagged.Contains(u)
                            || (answers.TryGetValue((table, id), out var answer) && answer == DirectMessageParticipantShares.FlaggedAnswer.Grant))
                .ToList();

            // 197b review F1: the decision above took reads of its own, so the owner and what the exemption rests on (still unfiled,
            // still on the same Direct thread and pair) are read again right before the message grant; a changed message grants
            // nothing here (the share job lists it and reconciles).
            if (!await StillTheSameDirectMessageAsync(communicationId, directTeam, recognition, "grant after the visibility decision", ct))
                return;
        }

        await GrantReadAccessToPrincipalsAsync(communicationId, threadId, granteesFor(MessageEntity, communicationId), ct);

        // 197b review pass 2 F1: with flagged participants in the audience, the content rows are granted only while the message is
        // still the Direct team's, and still the recognized Direct message (each content row is also confirmed by its own owner below).
        if (flagged.Count > 0 && !await StillTheSameDirectMessageAsync(communicationId, directTeam, recognition, "content grants", ct))
            return;

        await GrantDirectContentRowsAsync(communicationId, threadId, directTeam, contentRows, granteesFor, ct);
    }

    /// <summary>
    /// 197b review F1 (bound i'): the message is still owned by <paramref name="directTeam"/> and, when the NoRoot exemption was
    /// in play (<paramref name="recognition"/> not null), still the same unfiled Direct message on the same thread and pair.
    /// Either changed (or unreadable): <c>false</c>, logged, and the caller grants nothing more.
    /// </summary>
    private async Task<bool> StillTheSameDirectMessageAsync(
        Guid communicationId, Guid directTeam, DirectMessageParticipantShares.DirectMessageRecognition? recognition, string step,
        CancellationToken ct)
    {
        if (!await DirectMessageParticipantShares.OwnerUnchangedAsync(_entityService, communicationId, directTeam, step, _logger, ct))
            return false;
        return recognition is null || await StillRecognizedAsync(communicationId, recognition, step, ct);
    }

    /// <summary>
    /// 197b bound (i'): the message is re-read and is still the unfiled Direct message <paramref name="recognition"/> found (same
    /// thread, same pair). Otherwise (re-filed, moved, re-stamped, or unreadable) <c>false</c>, logged.
    /// </summary>
    private async Task<bool> StillRecognizedAsync(
        Guid communicationId, DirectMessageParticipantShares.DirectMessageRecognition recognition, string step, CancellationToken ct)
    {
        var now = await DirectMessageParticipantShares.RecognizeUnfiledDirectMessageAsync(_entityService, communicationId, _logger, ct);
        if (recognition.SameAs(now))
            return true;
        _logger.LogInformation(
            "Message {CommunicationId} is no longer the unfiled Direct message it was at the external visibility decision (before " +
            "{Step}); nothing more is granted here (ThreadId={ThreadId}).", communicationId, step, recognition.ThreadId);
        return false;
    }

    /// <summary>
    /// uac-r2 task 197b: THE predicate (the 191h seam, scoped, so resolved in a scope of its own: this service is a singleton) for
    /// the flagged participants of a Direct message. <paramref name="directPairRecognized"/>: the message was positively
    /// recognized as an unfiled Direct message (bound i', <see cref="DirectMessageParticipantShares.RecognizeUnfiledDirectMessageAsync"/>)
    /// and the answer is applied only to members of its stamped pair (the participant-scoped NoRoot exemption applies); otherwise
    /// NoRoot is not granted. No scope factory (a test composition): every row defers (no flagged participant is granted here).
    /// </summary>
    private async Task<IReadOnlyDictionary<(string Table, Guid Id), DirectMessageParticipantShares.FlaggedAnswer>> DecideForFlaggedParticipantsAsync(
        Guid communicationId, IReadOnlyCollection<(string Table, Guid Id)> rows, bool directPairRecognized, CancellationToken ct)
    {
        if (_scopes is null)
        {
            return await DirectMessageParticipantShares.DecideForDirectParticipantsAsync(
                null, communicationId, rows, directPairRecognized, _logger, ct);
        }

        await using var scope = _scopes.CreateAsyncScope();
        AppOnlyExternalVisibility? seam;
        try
        {
            seam = scope.ServiceProvider.GetService<AppOnlyExternalVisibility>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The seam could not be built: no flagged participant is granted here (every row defers), and the grant goes on.
            _logger.LogWarning(ex, "Message {CommunicationId}: the external visibility decision could not be created.", communicationId);
            seam = null;
        }

        return await DirectMessageParticipantShares.DecideForDirectParticipantsAsync(
            seam, communicationId, rows, directPairRecognized, _logger, ct);
    }

    /// <summary>
    /// uac-r2 task 197b: the Direct two-party list of a message the Direct team does NOT own (user-owned, or another ordinary team's:
    /// the join's Direct branch), cleared for users flagged external. A user the flag read returns unflagged is granted as before.
    /// A flagged user, or one whose flag cannot be read (not returned, a read fault, no reader; treated as flagged), is granted
    /// only when THE predicate allows it: Visible, or, for a member of the message's stamped pair on a message positively
    /// recognized as an unfiled Direct message (bound i', whoever owns it), exactly no root (the participant-scoped exemption).
    /// An unfiled message that is not recognized (an ordinary email, no valid pair), or a grantee outside the pair, gets nothing
    /// on no root. A Hidden message grants them nothing. When the exemption granted anyone, the message is re-recognized right
    /// before returning; if it changed (re-filed, moved, re-stamped) nobody is granted here. Never revokes (the 191c engine owns
    /// flagged users' shares on such rows, and consults the same exemption).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ClearDirectParticipantsAsync(
        Guid communicationId, Guid threadId, IReadOnlyList<Guid> participants, CancellationToken ct)
    {
        if (participants.Count == 0)
            return participants;

        Dictionary<Guid, Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.SystemUserRow> users = new();
        if (_webApi is not null)
        {
            // 197b review F2: no job re-grants a message the Direct team does not own, so a transient fault is retried once here.
            // A second fault fails closed (only a participant the predicate shows the message to is granted) and is logged as an
            // Error: an internal participant may then lack Read on this message until it is granted again.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    users = await RestrictedExternalShareRemover.ReadUsersAsync(_webApi, participants.Distinct().ToList(), ct);
                    break;
                }
                catch (Exception ex) when (attempt < 2 && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
                {
                    _logger.LogWarning(ex, "Message {CommunicationId}: its Direct participants' external flags could not be read; " +
                        "retrying once (ThreadId={ThreadId}).", communicationId, threadId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Message {CommunicationId}: its Direct participants' external flags could not be read twice; " +
                        "only a participant the external visibility predicate shows the message to is granted, and the others are NOT " +
                        "granted Read on it (ThreadId={ThreadId}).", communicationId, threadId);
                    break;
                }
            }
        }

        var unflagged = participants.Where(id => users.TryGetValue(id, out var user) && user.IsExternal != true).ToList();
        var others = participants.Where(id => !unflagged.Contains(id)).ToList();
        if (others.Count == 0)
            return participants;

        // Bound (i'): the exemption is asked only for the flagged members of a recognized unfiled Direct message's stamped pair
        // (bound iii); everyone else flagged is asked without it.
        var recognition = await DirectMessageParticipantShares.RecognizeUnfiledDirectMessageAsync(_entityService, communicationId, _logger, ct);
        List<Guid> inPair = recognition is null ? [] : others.Where(recognition.Pair.Contains).ToList();
        var outsidePair = others.Where(id => !inPair.Contains(id)).ToList();

        var granted = new List<Guid>();
        if (inPair.Count > 0 && await MessageAllowedAsync(directPairRecognized: true))
            granted.AddRange(inPair);
        var exemptionGranted = granted.Count > 0;
        if (outsidePair.Count > 0 && await MessageAllowedAsync(directPairRecognized: false))
            granted.AddRange(outsidePair);

        _logger.LogInformation("Message {CommunicationId}: {Granted} of {Count} Direct participant(s) flagged external (or unreadable) " +
            "are granted ({InPair} of them in its recognized Direct pair) (ThreadId={ThreadId}).",
            communicationId, granted.Count, others.Count, inPair.Count, threadId);

        // Bound (i') re-check: the exemption rests on the message being this unfiled Direct message; re-filed in between, nobody
        // is granted here (its new filing's own grant and reconcile decide).
        if (exemptionGranted && !await StillRecognizedAsync(communicationId, recognition!, "the participant grant", ct))
            return [];

        return participants.Where(id => unflagged.Contains(id) || granted.Contains(id)).ToList();

        async Task<bool> MessageAllowedAsync(bool directPairRecognized)
        {
            var answers = await DecideForFlaggedParticipantsAsync(
                communicationId, [(MessageEntity, communicationId)], directPairRecognized, ct);
            return answers.TryGetValue((MessageEntity, communicationId), out var answer)
                && answer == DirectMessageParticipantShares.FlaggedAnswer.Grant;
        }
    }

    /// <summary>
    /// The <see cref="DirectContentRows"/> rows of the message that <paramref name="directTeam"/> owns. Best-effort per table: a
    /// read that faults is logged and that table's rows are not granted here (the share job retries them).
    /// </summary>
    private async Task<IReadOnlyList<(string Table, Guid Id)>> ReadDirectContentRowsAsync(
        Guid communicationId, Guid threadId, Guid directTeam, CancellationToken ct)
    {
        var found = new List<(string Table, Guid Id)>();
        foreach (var (table, messageLookup) in DirectContentRows)
        {
            List<Guid> rowIds;
            try
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount = MaxDirectContentRowsPerTable,
                    NoLock = true,
                };
                query.Criteria.AddCondition(messageLookup, ConditionOperator.Equal, communicationId);
                query.Criteria.AddCondition("owningteam", ConditionOperator.Equal, directTeam);
                rowIds = (await _entityService.RetrieveMultipleAsync(query, ct))?.Entities
                    .Select(e => e.Id).Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Direct message content-row read failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}, Table={Table}",
                    communicationId, threadId, table);
                continue;
            }

            if (rowIds.Count >= MaxDirectContentRowsPerTable)
            {
                _logger.LogWarning(
                    "Direct message content-row read reached its cap of {Cap}; rows beyond it are not granted here " +
                    "| CommunicationId={CommunicationId}, ThreadId={ThreadId}, Table={Table}",
                    MaxDirectContentRowsPerTable, communicationId, threadId, table);
            }

            found.AddRange(rowIds.Select(id => (table, id)));
        }

        return found;
    }

    /// <summary>
    /// Read on each of <paramref name="rows"/> (the message's Direct-team-owned content rows) to the participants
    /// <paramref name="granteesFor"/> names for that row. Best-effort per row and principal, as the message grant is.
    /// </summary>
    private async Task GrantDirectContentRowsAsync(
        Guid communicationId, Guid threadId, Guid directTeam, IReadOnlyList<(string Table, Guid Id)> rows,
        Func<string, Guid, IReadOnlyList<Guid>> granteesFor, CancellationToken ct)
    {
        foreach (var (table, rowId) in rows)
        {
            var participants = granteesFor(table, rowId);
            if (participants.Count == 0)
                continue;

            // 198 pass 2 F1: the row's shares first (198 fix round K2: only a participant holding NO share on it is granted,
            // so a mismatch below never revokes a share this call did not make), then grant-then-confirm by the row's owner id.
            // A read fault, a grant fault, or an owner that is no longer exactly the Direct team grants nothing that stays
            // here; the share job (SecureChildShareReconciliationJob's Direct pass) retries the message.
            IReadOnlyList<DataversePrincipalAccess> existingShares;
            try
            {
                existingShares = await _accessGrant.GetPrincipalAccessOrThrowAsync(table, rowId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Direct message content-row shares could not be read; not granted here, the share reconciliation retries it " +
                    "| CommunicationId={CommunicationId}, ThreadId={ThreadId}, Table={Table}, RowId={RowId}",
                    communicationId, threadId, table, rowId);
                continue;
            }

            var (_, complete) = await DirectMessageParticipantShares.GrantContentRowAndConfirmAsync(
                _entityService, _accessGrant, table, rowId, directTeam, participants, existingShares, communicationId, _logger, ct);
            if (!complete)
            {
                _logger.LogWarning(
                    "Direct message content row not settled (a grant failed, or its owner is no longer the Direct team); the share " +
                    "reconciliation retries it | CommunicationId={CommunicationId}, ThreadId={ThreadId}, Table={Table}, RowId={RowId}",
                    communicationId, threadId, table, rowId);
            }
        }
    }

    /// <summary>The Direct two-party list of a thread the join found Direct (#1629: its type is not re-read).</summary>
    private async Task<IReadOnlyList<Guid>> ReadDirectParticipantsForGrantAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _entityService.RetrieveAsync(ThreadEntity, threadId, new[] { "ownerid" }, ct);
        if (thread is null || thread.Id == Guid.Empty)
            return Array.Empty<Guid>();
        return await ReadOwnerAndSharesAsync(threadId, thread, shareFaultIsNoShares: true, ct); // a fault grants fewer
    }

    /// <summary>
    /// <c>Ordinary</c> is <c>true</c> when the message is NOT a secure child — owned by a user, or by a team that is not
    /// the Secure Record Owners team — so its participants may be granted Read; <c>false</c> for a Secure-team-owned
    /// message, and whenever that cannot be told (the message is not found, the Secure Record team is ambiguous, or the
    /// configured Secure Record settings name a unit or team Dataverse does not find —
    /// <see cref="SecureChildShareSynchronizer.IsSecureOwnerTeamAsync"/>).
    /// <c>OwningTeam</c> is the team that owns an ordinary team-owned message (<c>null</c> when user-owned): the one team
    /// the anchor grant does not expand (#1629 F2). A Dataverse fault propagates to the caller's catch, which grants nothing.
    /// </summary>
    private async Task<(bool Ordinary, Guid? OwningTeam)> ReadMessageOwnershipAsync(Guid communicationId, Guid threadId, CancellationToken ct)
    {
        var query = new QueryExpression(MessageEntity) { ColumnSet = new ColumnSet("owningteam"), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition("sprk_communicationid", ConditionOperator.Equal, communicationId);
        var message = (await _entityService.RetrieveMultipleAsync(query, ct))?.Entities.FirstOrDefault();
        if (message is null)
        {
            _logger.LogWarning(
                "Message {CommunicationId} could not be found, so whether it is a secure record's message is unknown; no " +
                "participant is granted (ThreadId={ThreadId}).", communicationId, threadId);
            return (false, null);
        }

        if (message.GetAttributeValue<EntityReference>("owningteam")?.Id is not { } owningTeam || owningTeam == Guid.Empty)
            return (true, null); // user-owned: never a secure child (the synchronizer touches only the Secure team's rows)

        var (secure, undetermined) = await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(_entityService, _configuration, owningTeam, ct);
        if (secure is null)
        {
            // Task 198 Part C: a Direct-team-owned message is retried by the share job (SecureChildShareReconciliationJob),
            // which re-asks this same check; any other team's message is not granted here.
            _logger.LogWarning(
                "Message {CommunicationId} is team-owned and the Secure Record team cannot be determined ({Refusal}); no " +
                "participant is granted now (ThreadId={ThreadId}).", communicationId, undetermined, threadId);
            return (false, null);
        }

        if (secure == true)
        {
            _logger.LogInformation(
                "Message {CommunicationId} belongs to a secure record; its readers are the record's sharees (secure-child " +
                "share synchronizer), so no thread participant is granted here (ThreadId={ThreadId}).",
                communicationId, threadId);
            return (false, null);
        }

        return (true, owningTeam);
    }

    /// <summary>
    /// Grants Read access on <paramref name="communicationId"/> to each principal — the ONE grant call
    /// shared by both the Direct two-party branch and the Open derived-set branch. Best-effort per
    /// principal (NFR-02): one failed grant never blocks the rest.
    /// </summary>
    private async Task GrantReadAccessToPrincipalsAsync(
        Guid communicationId, Guid threadId, IReadOnlyList<Guid> principalIds, CancellationToken ct)
    {
        foreach (var principalId in principalIds)
        {
            try
            {
                await _accessGrant.GrantAccessAsync(
                    MessageEntitySet, communicationId, DataversePrincipalRef.User(principalId), ReadAccessRights, ct);
            }
            catch (Exception ex)
            {
                // Best-effort (NFR-02): the message already persisted; a missed grant degrades to
                // "not yet visible to this participant", never fails the send/ingest.
                _logger.LogWarning(
                    ex,
                    "Message access grant failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}, Principal={Principal}",
                    communicationId, threadId, principalId);
            }
        }
    }

    /// <summary>
    /// Record branch: the anchor record's grantees (<see cref="IThreadMembershipDerivationService.DeriveAnchorGranteeUserIdsAsync"/>
    /// — the anchor the join fixed; never the MESSAGE's owning team). De-duplicated. A derivation failure is swallowed
    /// (best-effort, NFR-02) and returns empty, which the caller treats as a no-op.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> GetAnchorGranteesAsync(
        Guid communicationId, ThreadAssignment assignment, Guid? messageOwningTeam, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignment.AnchorEntityType) || assignment.AnchorRecordId is not { } anchorId || anchorId == Guid.Empty)
            return Array.Empty<Guid>();

        try
        {
            return await _membershipDerivation.Value.DeriveAnchorGranteeUserIdsAsync(
                assignment.AnchorEntityType, anchorId, messageOwningTeam, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Record-thread grantee derivation failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}",
                communicationId, assignment.ThreadId);
            return Array.Empty<Guid>();
        }
    }

    /// <summary>
    /// uac-r2 task 198 fix round 1 (R3, finding 4; coordinator ruling 2): the record-anchored grant applies the Restricted bar
    /// AT GRANT TIME. A message created under a record that is already Restricted is stamped Restricted at create, and nothing
    /// re-files it or changes its value, so a share granted here would otherwise stay. Owner rounds 67/76: Restricted wins over
    /// AC6 for users flagged external.
    /// <list type="bullet">
    /// <item>The message's own value and the value it inherits through its filing are NOT Restricted: every grantee, exactly as
    /// before (no user is read).</item>
    /// <item>Restricted, or that cannot be decided: the grantees are cleared through
    /// <see cref="RestrictedFilingShareBar.EligibleForGrantAsync"/> — the ONE predicate
    /// (<c>InternalShareEndpoints.IsBarredOnRestricted</c>), so a user flagged external is not granted, and an internal or
    /// blank-flag user is. Fail closed as that method is: a user the flag read does not return, or a read that faults, is not
    /// granted (logged).</item>
    /// </list>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ClearAnchorGranteesAsync(
        Guid communicationId, Guid threadId, IReadOnlyList<Guid> grantees, CancellationToken ct)
    {
        if (grantees.Count == 0)
            return grantees;

        var bar = new RestrictedFilingShareBar(
            _entityService, _coreAncestors is { } resolver ? resolver.ProbeColumnsAsync : null, _accessGrant, _webApi, _logger);
        if (await bar.RestrictedForGrantAsync(communicationId, ct) == false)
            return grantees;

        var (eligible, complete) = await bar.EligibleForGrantAsync(communicationId, grantees, ct);
        if (eligible.Count < grantees.Count || !complete)
        {
            _logger.LogWarning(
                "Message {CommunicationId} is Restricted (or that cannot be decided): {NotGranted} of its {Grantees} anchor-record " +
                "grantee(s) are not granted (flagged external, or not cleared; complete {Complete}) (ThreadId={ThreadId}).",
                communicationId, grantees.Count - eligible.Count, grantees.Count, complete, threadId);
        }

        return eligible;
    }

    // ── find-or-create helper ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks for the strict 1:1 Direct thread of the pair among the Direct threads <paramref name="ownerCandidateId"/> owns.
    /// <para><b>The match is fm 054a's one strict-pair rule</b> (uac-r2 198b;
    /// <see cref="DirectMessageRecipient.ReadStrictPairShapeAsync"/>): a candidate matches only when it is a strict pair whose
    /// {owner, other} is exactly {<paramref name="ownerCandidateId"/>, <paramref name="sharedCandidateId"/>}. A thread that
    /// merely includes the other user (a third principal, a team share, a typed regarding, privacy not confirmed Open) is never
    /// reused as the 1:1 thread: a message sent there would reach more than the two. A candidate that is not a strict pair is
    /// logged (Information) and skipped; one whose read faults is logged (Warning) and skipped, never a match.</para>
    /// <para><b>The scan</b>: the owner's Direct threads newest first (<c>createdon</c> descending, the id as tie-break so the
    /// paging cookie is stable), paged by <see cref="PagingInfo"/> (never a skip), to <see cref="MaxCandidateThreads"/>. The
    /// share seam has no "records shared to principal X" read (every <see cref="IDataverseRecordShareService"/> read is per
    /// record), so each candidate is read on its own. When the bound is hit with no match, reuse is not decided and a Warning
    /// says so: the caller then creates a thread, and a duplicate is possible.</para>
    /// </summary>
    private async Task<Guid?> FindExistingAsync(Guid ownerCandidateId, Guid sharedCandidateId, CancellationToken ct)
    {
        var query = new QueryExpression(ThreadEntity)
        {
            ColumnSet = new ColumnSet(false),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("ownerid", ConditionOperator.Equal, ownerCandidateId),
                    new ConditionExpression("sprk_threadtype", ConditionOperator.Equal, ThreadTypeDirect),
                },
            },
        };
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("sprk_communicationthreadid", OrderType.Descending);

        string? cookie = null;
        for (var page = 1; ; page++)
        {
            query.PageInfo = new PagingInfo { Count = CandidatePageSize, PageNumber = page, PagingCookie = cookie };
            var candidates = await _entityService.RetrieveMultipleAsync(query, ct);
            foreach (var candidate in candidates.Entities)
            {
                if (await IsThePairsStrictThreadAsync(candidate.Id, ownerCandidateId, sharedCandidateId, ct))
                    return candidate.Id;
            }

            if (!candidates.MoreRecords)
                return null;

            if (page * CandidatePageSize >= MaxCandidateThreads)
            {
                _logger.LogWarning(
                    "Direct thread lookup for owner {Owner} and participant {Other} reached its bound of {Bound} candidate threads " +
                    "with no strict 1:1 match; reuse was not decided for the threads past it, so a new thread is created and a " +
                    "duplicate Direct thread is possible.",
                    ownerCandidateId, sharedCandidateId, MaxCandidateThreads);
                return null;
            }

            cookie = candidates.PagingCookie;
        }
    }

    /// <summary>
    /// <c>true</c> when <paramref name="threadId"/> is a strict 1:1 Direct thread (fm's
    /// <see cref="DirectMessageRecipient.ReadStrictPairShapeAsync"/>) of exactly the pair {<paramref name="first"/>,
    /// <paramref name="second"/>}. A thread that is not a strict pair is logged and skipped; a read fault is logged as a
    /// Warning and skipped. A strict pair of other people is skipped silently (an ordinary other conversation).
    /// </summary>
    private async Task<bool> IsThePairsStrictThreadAsync(Guid threadId, Guid first, Guid second, CancellationToken ct)
    {
        var shape = await DirectMessageRecipient.ReadStrictPairShapeAsync(_entityService, _accessGrant, threadId, ct);
        if (shape.IsStrictPair)
        {
            return (shape.OwnerId == first && shape.OtherId == second)
                || (shape.OwnerId == second && shape.OtherId == first);
        }

        if (shape.Reason == DirectPairShapeReason.ParticipantReadFault)
        {
            _logger.LogWarning(
                shape.Fault,
                "Direct thread {ThreadId} could not be read for the pair lookup ({Reason}); the candidate is skipped, never reused.",
                threadId, shape.Reason);
        }
        else
        {
            // Debug, not Information (fm review): the lookup runs on every routed send, so the same old non-strict threads would
            // log on every message. A read fault stays a Warning.
            _logger.LogDebug(
                "Direct thread {ThreadId} is not a strict 1:1 thread ({Reason}); it is not reused for the pair lookup.",
                threadId, shape.Reason);
        }

        return false;
    }

    /// <summary>
    /// The <c>systemuser</c> principals holding a POA share on a thread.
    /// </summary>
    /// <remarks>
    /// Task 060: the seam's read is principal-KIND-typed (it must be — teams now share through the same
    /// client), so the Direct-thread caller projects it back to the systemuser ids this service reasons
    /// about. That filter is what the pre-060 <c>GetSharedSystemUserIdsAsync</c> primitive merely
    /// ASSUMED, so this tightens the no-leak property rather than relaxing it: a team share on a thread
    /// can no longer be mistaken for a participating user.
    /// <para>uac-r2 198 pass 2 item 7: only rows holding the Read bit count (a zero-mask row is inherited access, not a
    /// share of the thread), and the read is strict: a fault throws, never an empty list.</para>
    /// </remarks>
    private async Task<IReadOnlyList<Guid>> GetSharedSystemUserIdsAsync(Guid threadId, CancellationToken ct)
    {
        var shares = await _accessGrant.GetPrincipalAccessOrThrowAsync(ThreadEntity, threadId, ct);

        return shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser
                        && (s.AccessRightsMask & ReadAccessMask) == ReadAccessMask)
            .Select(s => s.Principal.Id)
            .Distinct()
            .ToList();
    }
}
