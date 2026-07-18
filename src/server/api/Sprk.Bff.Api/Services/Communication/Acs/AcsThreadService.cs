using Azure;
using Azure.Communication;
using Azure.Communication.Chat;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// ACS trusted-service implementation of the thread + membership plane (FR-15 / design §8.4, §8.7). A thin
/// adapter over the ACS Chat SDK (<see cref="ChatClient"/> / <see cref="ChatThreadClient"/>) behind the
/// Spaarke-owned <see cref="IAcsThreadService"/> seam — every <c>Azure.Communication.*</c> type stays inside
/// this boundary (ADR-045). The injected <see cref="ChatClient"/> is built from a chat token rooted in the
/// DI-injected central <c>TokenCredential</c> (ADR-028 / NFR-05 — see
/// <see cref="AcsServiceChatCredential"/>); this class never touches a credential directly.
/// </summary>
public sealed class AcsThreadService : IAcsThreadService
{
    /// <summary>
    /// Max participants per underlying ACS <c>AddParticipants</c> call. Each batch is one participant-mutation
    /// against the ACS budget (10 mutations/10s + 30/min per thread); chunking a large add into batches of
    /// this size gives the reconcile job (task 041) a natural throttle boundary.
    /// </summary>
    internal const int MaxParticipantsPerBatch = 10;

    /// <summary>ACS <c>ThreadCreationDateRetentionPolicy</c> accepts 30–90 days (design §8.7 floor is 30).</summary>
    internal const int MinRetentionDays = 30;
    internal const int MaxRetentionDays = 90;

    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(MinRetentionDays);

    // Lazy so CONSTRUCTING this service never builds the ACS ChatClient (ADR-032 boot-safety): the client
    // factory throws when Communication:Acs:Endpoint is unconfigured, and this service is reachable from the
    // startup hosted-service graph. Deferring the build to first use lets a BFF with no ACS config boot; only
    // an actual ACS thread operation fails.
    private readonly Lazy<ChatClient> _chatClient;
    private readonly ILogger<AcsThreadService> _logger;

    public AcsThreadService(Lazy<ChatClient> chatClient, ILogger<AcsThreadService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AcsThreadCreateResult> CreateThreadAsync(
        string topic,
        IEnumerable<string> initialParticipantCommunicationUserIds,
        TimeSpan? retention = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Thread topic is required.", nameof(topic));

        var retentionDays = ClampRetention(retention ?? DefaultRetention);

        // 30-day auto-delete retention set AT CREATE TIME (FR-15) so ACS never becomes a shadow record
        // store — Dataverse sprk_communication is the record (design §8.7).
        var options = new CreateChatThreadOptions(topic)
        {
            RetentionPolicy = new ThreadCreationDateRetentionPolicy(deleteThreadAfterDays: retentionDays),
        };
        foreach (var id in DistinctValidMris(initialParticipantCommunicationUserIds))
        {
            options.Participants.Add(new ChatParticipant(new CommunicationUserIdentifier(id)));
        }

        try
        {
            Response<CreateChatThreadResult> response = await _chatClient.Value.CreateChatThreadAsync(options, ct);
            var threadId = response.Value.ChatThread.Id;

            _logger.LogInformation(
                "Created ACS chat thread {ChatThreadId} with {RetentionDays}-day retention and {Count} initial participant(s).",
                threadId, retentionDays, options.Participants.Count);

            return new AcsThreadCreateResult { ChatThreadId = threadId, RetentionDays = retentionDays };
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw RateLimit("create chat thread", ex);
        }
    }

    /// <inheritdoc />
    public async Task<AcsMembershipChange> AddParticipantsAsync(
        string chatThreadId,
        IEnumerable<string> communicationUserIds,
        CancellationToken ct = default)
    {
        var threadClient = GetThreadClient(chatThreadId);
        var ids = DistinctValidMris(communicationUserIds).ToList();
        if (ids.Count == 0)
        {
            return new AcsMembershipChange
            {
                ChatThreadId = chatThreadId,
                Requested = 0,
                Applied = 0,
                NoOped = 0,
                BatchCount = 0,
            };
        }

        var noOped = 0;
        var batchCount = 0;

        // Chunk into per-batch mutations so task 041 can throttle at the batch boundary (10/10s + 30/min
        // per thread). Membership add is idempotent — ACS reports an already-present participant as a
        // non-fatal InvalidParticipant, which we count as a no-op rather than an error.
        foreach (var chunk in ids.Chunk(MaxParticipantsPerBatch))
        {
            batchCount++;
            var participants = chunk
                .Select(id => new ChatParticipant(new CommunicationUserIdentifier(id)))
                .ToList();

            try
            {
                Response<AddChatParticipantsResult> response = await threadClient.AddParticipantsAsync(participants, ct);
                var invalid = response.Value.InvalidParticipants;
                if (invalid is { Count: > 0 })
                {
                    noOped += invalid.Count;
                    _logger.LogWarning(
                        "ACS AddParticipants on thread {ChatThreadId} reported {InvalidCount} non-fatal invalid participant(s) [{Codes}]; treated as no-ops (reconciled by the next sweep, task 041).",
                        chatThreadId, invalid.Count, string.Join(",", invalid.Select(e => e.Code)));
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 429)
            {
                throw RateLimit("add participants", ex);
            }
        }

        return new AcsMembershipChange
        {
            ChatThreadId = chatThreadId,
            Requested = ids.Count,
            Applied = ids.Count - noOped,
            NoOped = noOped,
            BatchCount = batchCount,
        };
    }

    /// <inheritdoc />
    public async Task<AcsMembershipChange> RemoveParticipantAsync(
        string chatThreadId,
        string communicationUserId,
        CancellationToken ct = default)
    {
        var threadClient = GetThreadClient(chatThreadId);
        var mri = RequireValidMri(communicationUserId);

        try
        {
            await threadClient.RemoveParticipantAsync(new CommunicationUserIdentifier(mri), ct);
            return new AcsMembershipChange
            {
                ChatThreadId = chatThreadId,
                Requested = 1,
                Applied = 1,
                NoOped = 0,
                BatchCount = 1,
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw RateLimit("remove participant", ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Idempotent: the participant is already absent → safe no-op.
            _logger.LogDebug(
                "ACS RemoveParticipant on thread {ChatThreadId}: participant already absent; no-op.",
                chatThreadId);
            return new AcsMembershipChange
            {
                ChatThreadId = chatThreadId,
                Requested = 1,
                Applied = 0,
                NoOped = 1,
                BatchCount = 1,
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListParticipantsAsync(
        string chatThreadId,
        CancellationToken ct = default)
    {
        var threadClient = GetThreadClient(chatThreadId);
        var mris = new List<string>();

        try
        {
            // GetParticipantsAsync pages internally; enumerate the whole thread membership. Only
            // CommunicationUserIdentifier participants carry an ACS communicationUserId MRI — that is the only
            // identity kind the reconcile projects (bots/phone/Teams identities are out of scope for R1).
            await foreach (var participant in threadClient.GetParticipantsAsync(cancellationToken: ct).ConfigureAwait(false))
            {
                if (participant.User is CommunicationUserIdentifier user && !string.IsNullOrWhiteSpace(user.Id))
                    mris.Add(user.Id);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw RateLimit("list participants", ex);
        }

        return mris.Distinct(StringComparer.Ordinal).ToList();
    }

    private ChatThreadClient GetThreadClient(string chatThreadId)
    {
        if (string.IsNullOrWhiteSpace(chatThreadId))
            throw new ArgumentException("chatThreadId is required.", nameof(chatThreadId));
        return _chatClient.Value.GetChatThreadClient(chatThreadId);
    }

    private static int ClampRetention(TimeSpan retention)
    {
        var days = (int)Math.Round(retention.TotalDays, MidpointRounding.AwayFromZero);
        if (days < MinRetentionDays) return MinRetentionDays;
        if (days > MaxRetentionDays) return MaxRetentionDays;
        return days;
    }

    private static IEnumerable<string> DistinctValidMris(IEnumerable<string>? ids)
        => (ids ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(RequireValidMri)
            .Distinct(StringComparer.Ordinal);

    private static string RequireValidMri(string communicationUserId)
    {
        if (string.IsNullOrWhiteSpace(communicationUserId))
            throw new ArgumentException("communicationUserId is required.", nameof(communicationUserId));

        // ACS user MRIs are prefixed "8:" (e.g. 8:acs:{resource}_{guid}). A raw Entra id / GUID / UPN is
        // NOT a valid ACS identity and MUST be resolved via IAcsIdentityService.EnsureIdentityAsync first
        // (never pass a raw Entra id to ACS — task 011 constraint).
        if (!communicationUserId.StartsWith("8:", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{communicationUserId}' is not an ACS communicationUserId (MRI). Resolve the participant " +
                "via IAcsIdentityService.EnsureIdentityAsync before calling ACS thread ops.",
                nameof(communicationUserId));
        }

        return communicationUserId;
    }

    private static AcsRateLimitException RateLimit(string operation, RequestFailedException ex)
    {
        TimeSpan? retryAfter = null;
        if (ex.GetRawResponse() is { } raw
            && raw.Headers.TryGetValue("Retry-After", out var value)
            && int.TryParse(value, out var seconds))
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
        }

        return new AcsRateLimitException(
            $"ACS throttled the '{operation}' call (429). Retry after back-off within the " +
            "10-mutations/10s + 30/min per-thread budget.",
            retryAfter, ex);
    }
}
