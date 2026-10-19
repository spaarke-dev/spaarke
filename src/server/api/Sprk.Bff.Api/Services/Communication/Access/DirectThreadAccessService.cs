using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Membership;

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

    /// <summary>
    /// Bounds the find-or-create candidate scan (threads owned by one side of the pair) — a defensive cap,
    /// not an expected R1 ceiling (a user with more than this many Direct threads is not expected in R1).
    /// </summary>
    private const int MaxCandidateThreads = 50;

    private const string MessageEntity = "sprk_communication";

    private readonly IGenericEntityService _entityService;
    private readonly IDataverseRecordShareService _accessGrant;
    private readonly Lazy<IThreadMembershipDerivationService> _membershipDerivation;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DirectThreadAccessService> _logger;
    private readonly Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing _gateDecisionRecords;

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
    public DirectThreadAccessService(
        IGenericEntityService entityService,
        IDataverseRecordShareService accessGrant,
        Lazy<IThreadMembershipDerivationService> membershipDerivation,
        IConfiguration configuration,
        ILogger<DirectThreadAccessService> logger,
        Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing gateDecisionRecords)
    {
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

        return await ReadOwnerAndSharesAsync(threadId, thread, ct);
    }

    /// <summary>
    /// A Direct thread's two-party list: its user owner ∪ the systemusers it is POA-shared to. No topology check: the
    /// caller has decided the thread is Direct (the gate above, or the join for a grant, #1629).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ReadOwnerAndSharesAsync(Guid threadId, Entity thread, CancellationToken ct)
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
        catch (Exception ex)
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

            IReadOnlyList<Guid> principals = assignment.Audience switch
            {
                ThreadGrantAudience.DirectParticipants => await ReadDirectParticipantsForGrantAsync(threadId, ct),
                ThreadGrantAudience.AnchorRecordMembers => await GetAnchorGranteesAsync(communicationId, assignment, messageOwningTeam, ct),
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

    /// <summary>The Direct two-party list of a thread the join found Direct (#1629: its type is not re-read).</summary>
    private async Task<IReadOnlyList<Guid>> ReadDirectParticipantsForGrantAsync(Guid threadId, CancellationToken ct)
    {
        var thread = await _entityService.RetrieveAsync(ThreadEntity, threadId, new[] { "ownerid" }, ct);
        if (thread is null || thread.Id == Guid.Empty)
            return Array.Empty<Guid>();
        return await ReadOwnerAndSharesAsync(threadId, thread, ct);
    }

    /// <summary>
    /// <c>Ordinary</c> is <c>true</c> when the message is NOT a secure child — owned by a user, or by a team that is not
    /// the Secure Record Owners team — so its participants may be granted Read; <c>false</c> for a Secure-team-owned
    /// message, and whenever that cannot be told (the message is not found, or the Secure Record team is ambiguous).
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

        if (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(_entityService, _configuration, owningTeam, ct))
            return (true, owningTeam);

        var secure = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_entityService, _configuration, ct);
        if (secure.Refusal is { } refusal)
        {
            _logger.LogWarning(
                "Message {CommunicationId} is team-owned and the Secure Record team cannot be determined ({Refusal}); no " +
                "participant is granted (ThreadId={ThreadId}).", communicationId, refusal, threadId);
            return (false, null);
        }

        if (secure.TeamId == owningTeam)
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

    // ── find-or-create helper ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks for a Direct thread owned by <paramref name="ownerCandidateId"/> that is shared to
    /// <paramref name="sharedCandidateId"/>. Bounded scan (<see cref="MaxCandidateThreads"/>) over the
    /// owner's Direct threads — a per-share POA read is unavoidable (POA has no "shared to X AND owned by
    /// Y" composite filter), but R1's per-user Direct-thread count is expected to be small.
    /// </summary>
    private async Task<Guid?> FindExistingAsync(Guid ownerCandidateId, Guid sharedCandidateId, CancellationToken ct)
    {
        var query = new QueryExpression(ThreadEntity)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = MaxCandidateThreads,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("ownerid", ConditionOperator.Equal, ownerCandidateId),
                    new ConditionExpression("sprk_threadtype", ConditionOperator.Equal, ThreadTypeDirect),
                },
            },
        };

        var candidates = await _entityService.RetrieveMultipleAsync(query, ct);
        foreach (var candidate in candidates.Entities)
        {
            var shared = await GetSharedSystemUserIdsAsync(candidate.Id, ct);
            if (shared.Contains(sharedCandidateId))
                return candidate.Id;
        }

        return null;
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
    /// </remarks>
    private async Task<IReadOnlyList<Guid>> GetSharedSystemUserIdsAsync(Guid threadId, CancellationToken ct)
    {
        var shares = await _accessGrant.GetPrincipalAccessAsync(ThreadEntity, threadId, ct);

        return shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser)
            .Select(s => s.Principal.Id)
            .Distinct()
            .ToList();
    }
}
