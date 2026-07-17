using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Default <see cref="IDirectThreadAccessService"/> — see the interface for the mechanism + Component
/// Justification. Reads/writes <c>sprk_communicationthread</c> via the canonical
/// <see cref="IGenericEntityService"/> (SDK) and POA shares via <see cref="IDataverseAccessGrantService"/>
/// (Web API). Singleton-safe: both dependencies are stateless singletons.
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

    private readonly IGenericEntityService _entityService;
    private readonly IDataverseAccessGrantService _accessGrant;
    private readonly ILogger<DirectThreadAccessService> _logger;

    public DirectThreadAccessService(
        IGenericEntityService entityService,
        IDataverseAccessGrantService accessGrant,
        ILogger<DirectThreadAccessService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _accessGrant = accessGrant ?? throw new ArgumentNullException(nameof(accessGrant));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Establish the explicit two-party list: "Manage access" (POA) share to the other participant.
        await _accessGrant.GrantAccessAsync(ThreadEntitySet, threadId, otherSystemUserId, ReadAccessRights, ct);

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

        var participants = new List<Guid>();
        if (thread.GetAttributeValue<EntityReference>("ownerid") is { } owner
            && string.Equals(owner.LogicalName, SystemUserEntity, StringComparison.OrdinalIgnoreCase))
        {
            participants.Add(owner.Id);
        }

        IReadOnlyList<Guid> shared;
        try
        {
            shared = await _accessGrant.GetSharedSystemUserIdsAsync(ThreadEntity, threadId, ct);
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
    public async Task GrantMessageAccessAsync(Guid communicationId, Guid threadId, CancellationToken ct = default)
    {
        try
        {
            var participants = await GetParticipantSystemUserIdsAsync(threadId, ct);
            if (participants.Count == 0)
                return; // not a Direct thread (or no participants on record yet) — nothing to grant

            foreach (var participantId in participants)
            {
                try
                {
                    await _accessGrant.GrantAccessAsync(MessageEntitySet, communicationId, participantId, ReadAccessRights, ct);
                }
                catch (Exception ex)
                {
                    // Best-effort (NFR-02): the message already persisted; a missed grant degrades to
                    // "not yet visible to this participant", never fails the send/ingest.
                    _logger.LogWarning(
                        ex,
                        "Direct-thread message access grant failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}, Principal={Principal}",
                        communicationId, threadId, participantId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Direct-thread message access grant lookup failed (non-fatal) | CommunicationId={CommunicationId}, ThreadId={ThreadId}",
                communicationId, threadId);
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
            var shared = await _accessGrant.GetSharedSystemUserIdsAsync(ThreadEntity, candidate.Id, ct);
            if (shared.Contains(sharedCandidateId))
                return candidate.Id;
        }

        return null;
    }
}
