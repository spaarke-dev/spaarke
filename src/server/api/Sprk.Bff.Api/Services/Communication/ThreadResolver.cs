using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Direction-symmetric, channel-agnostic implementation of <see cref="IThreadResolver"/> (FR-06). Dispatches
/// to a per-channel <see cref="IThreadKeyStrategy"/> (keyed by <see cref="CommunicationType"/>, mirroring
/// <see cref="Channels.CommunicationChannelDispatcher"/>) to JOIN an existing <c>sprk_communicationthread</c>
/// or CREATE a new one, then stamps the <c>sprk_communicationthread</c> lookup on the message. Wholly
/// best-effort / non-fatal (NFR-02): any failure is logged and swallowed — the caller's send/capture path
/// never fails because of thread resolution.
/// </summary>
public sealed class ThreadResolver : IThreadResolver
{
    // sprk_communicationthread choice integers (task 004 schema spec).
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;
    private const int PrivacyStateOpen = 100000000;

    private readonly IGenericEntityService _entityService;
    private readonly IReadOnlyDictionary<CommunicationType, IThreadKeyStrategy> _strategies;
    private readonly ILogger<ThreadResolver> _logger;

    public ThreadResolver(
        IEnumerable<IThreadKeyStrategy> strategies,
        IGenericEntityService entityService,
        ILogger<ThreadResolver> logger)
    {
        _entityService = entityService;
        // Fail fast on a duplicate channel registration (mirrors CommunicationChannelDispatcher's guard).
        _strategies = strategies.ToDictionary(s => s.SupportedType);
        _logger = logger;
    }

    public async Task<Guid?> ResolveAndAssignThreadAsync(ThreadResolutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (!_strategies.TryGetValue(request.ChannelType, out var strategy))
            {
                _logger.LogDebug(
                    "No thread-key strategy for channel {ChannelType}; skipping thread resolution | CommunicationId: {CommunicationId}",
                    request.ChannelType, request.CommunicationId);
                return null;
            }

            var resolution = await strategy.ResolveAsync(request, ct);

            // JOIN — an existing thread was found; assign it and we're done.
            if (resolution.ExistingThreadId is { } existing)
            {
                await AssignThreadAsync(request.CommunicationId, existing, ct);
                _logger.LogInformation(
                    "Joined existing thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                    existing, request.CommunicationId, request.ChannelType);
                return existing;
            }

            // SKIP — no groupable key (e.g. a chat message with no ACS thread id). Do not create an orphan.
            if (!resolution.CreateWhenAbsent)
            {
                _logger.LogDebug(
                    "No groupable thread key | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                    request.CommunicationId, request.ChannelType);
                return null;
            }

            // CREATE — a thin thread anchored to the message's resolved regarding (ADR-024 reuse).
            var threadId = await CreateThreadAsync(request, ct);
            await strategy.OnThreadCreatedAsync(threadId, request, ct);
            await AssignThreadAsync(request.CommunicationId, threadId, ct);
            _logger.LogInformation(
                "Created thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                threadId, request.CommunicationId, request.ChannelType);
            return threadId;
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — never fail the send or inbound-capture path.
            _logger.LogWarning(
                ex,
                "Thread resolution failed (non-fatal) | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                request.CommunicationId, request.ChannelType);
            return null;
        }
    }

    private async Task<Guid> CreateThreadAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        var anchor = await ReadRegardingAnchorAsync(request.CommunicationId, ct);

        var thread = new DataverseEntity("sprk_communicationthread")
        {
            ["sprk_name"] = TruncateTo(BuildTopic(request, anchor), 200),
            // Record-Anchored when the message resolved a regarding record; else a Direct 1:1 conversation.
            ["sprk_threadtype"] = new OptionSetValue(anchor is not null ? ThreadTypeRecordAnchored : ThreadTypeDirect),
            // Threads start Open; the point-forward privacy flip (Private) is task 042, not this seam.
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
        };

        // Anchor = REUSE the ADR-024 regarding family (NOT a second mechanism / NOT new sprk_anchor* fields).
        if (anchor is not null)
        {
            thread["sprk_regardingrecordid"] = TruncateTo(anchor.RecordId, 100);
            thread["sprk_regardingrecordtype"] = TruncateTo(anchor.RecordType, 100);
            if (!string.IsNullOrWhiteSpace(anchor.RecordName))
                thread["sprk_regardingrecordname"] = TruncateTo(anchor.RecordName, 400);
            if (!string.IsNullOrWhiteSpace(anchor.RecordUrl))
                thread["sprk_regardingrecordurl"] = TruncateTo(anchor.RecordUrl, 400);
        }

        return await _entityService.CreateAsync(thread, ct);
    }

    /// <summary>
    /// Reads the message's resolved regarding and derives the thread anchor. Prefers the denormalized
    /// polymorphic pointer (<c>sprk_regardingrecordid/type/name/url</c>); falls back to the first typed
    /// <c>sprk_regarding*</c> lookup in ADR-024 priority order (<see cref="RegardingFieldMap"/>). Returns
    /// null when the message has no regarding (e.g. an unassociated chat message → a Direct thread).
    /// </summary>
    private async Task<RegardingAnchor?> ReadRegardingAnchorAsync(Guid communicationId, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardingrecordname", "sprk_regardingrecordurl",
        };
        columns.AddRange(RegardingFieldMap.AllRegardingFields);

        var comm = await _entityService.RetrieveAsync("sprk_communication", communicationId, columns.ToArray(), ct);

        var recordType = comm.GetAttributeValue<string>("sprk_regardingrecordtype");
        var recordId = comm.GetAttributeValue<string>("sprk_regardingrecordid");
        if (!string.IsNullOrWhiteSpace(recordType) && !string.IsNullOrWhiteSpace(recordId))
        {
            return new RegardingAnchor(
                recordId,
                recordType,
                comm.GetAttributeValue<string>("sprk_regardingrecordname"),
                comm.GetAttributeValue<string>("sprk_regardingrecordurl"));
        }

        // Fall back to the first typed regarding lookup present (ADR-024 priority order).
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (comm.GetAttributeValue<EntityReference>(field) is { } er)
            {
                return new RegardingAnchor(
                    er.Id.ToString(),
                    string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName,
                    er.Name,
                    RecordUrl: null);
            }
        }

        return null;
    }

    private Task AssignThreadAsync(Guid communicationId, Guid threadId, CancellationToken ct) =>
        _entityService.UpdateAsync(
            "sprk_communication",
            communicationId,
            new Dictionary<string, object>
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            },
            ct);

    private static string BuildTopic(ThreadResolutionRequest request, RegardingAnchor? anchor)
    {
        if (!string.IsNullOrWhiteSpace(request.Message.Subject))
            return request.Message.Subject!;
        if (!string.IsNullOrWhiteSpace(anchor?.RecordName))
            return anchor!.RecordName!;
        return request.ChannelType == CommunicationType.Message ? "Conversation" : "(No Subject)";
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record RegardingAnchor(string RecordId, string RecordType, string? RecordName, string? RecordUrl);
}
