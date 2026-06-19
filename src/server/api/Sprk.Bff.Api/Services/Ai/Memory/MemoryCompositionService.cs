using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Production implementation of <see cref="IMemoryCompositionService"/>.
///
/// Orchestrates the three R6 Pillar 7 primitives (compression, pinned-context,
/// selective recall) into a single tagged four-layer memory block under the
/// NFR-10 8K total budget. Pinned items NEVER drop; other layers drop in priority
/// order (retrieved-old → compressed-mid → recent-verbatim oldest-first).
/// </summary>
/// <remarks>
/// <para>
/// <b>R6 Pillar 7 role</b>: this service is the integration point consumed by task
/// 068 (SprkChatAgentFactory wiring). The composed memory block is rendered into
/// the system prompt as labelled sections (so the LLM can attribute provenance:
/// "recent", "earlier-summarised", "retrieved-relevant", "pinned-{pinType}").
/// </para>
/// <para>
/// <b>ADR-010</b>: registered as <c>AddScoped&lt;IMemoryCompositionService,
/// MemoryCompositionService&gt;()</c> inside the existing
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>. ZERO new
/// Program.cs lines. The interface seam is justified by ADR-010's "interface
/// required for genuine substitution" carve-out — task 068 will choose between a
/// real impl and a unit-test fake; the interface is the canonical substitution
/// point.
/// </para>
/// <para>
/// <b>ADR-013</b>: this service lives entirely inside <see cref="Memory"/>. It
/// depends only on AI-internal collaborators (<see cref="ISummarizationCompressionService"/>,
/// <see cref="IPinnedContextRepository"/>, <see cref="IPinnedContextRecallService"/>)
/// — no PublicContracts facade is needed per the refined 2026-05-20 ADR-013
/// boundary rule for AI-internal callers.
/// </para>
/// <para>
/// <b>ADR-014</b>: <c>tenantId</c> scopes every downstream call (partition key).
/// Cross-tenant reads are structurally impossible because the underlying Cosmos
/// queries are partition-scoped.
/// </para>
/// <para>
/// <b>ADR-015</b>: pin / message content is user-authored. This service does NOT
/// log content bodies — only deterministic identifiers (tenantId, userId, matterId,
/// counts, dropped-layer names) appear in telemetry.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: <see cref="MemoryCompositionOptions.TotalTokenBudget"/>
/// is the binding ceiling (default 8K). Layer drop priority documented on the
/// interface; the pinned tier is NEVER dropped — if pinned alone exceeds the
/// budget, the result returns ONLY the pinned tier (other layers dropped). The
/// chat prompt builder (task 068) owns any subsequent hard guard.
/// </para>
/// <para>
/// <b>Soft-failure behaviour</b>: returns <see cref="MemoryComposition.Empty"/>
/// (NOT a thrown exception) when the kill switch is off or all inputs are empty.
/// Per-primitive soft-failures (compression returns null, recall returns empty,
/// repository returns empty) degrade gracefully — the affected layer is omitted,
/// the other layers still compose. <see cref="OperationCanceledException"/> is
/// re-raised. This is the canonical P2 Quiet kill-switch posture for memory
/// infrastructure.
/// </para>
/// </remarks>
public sealed class MemoryCompositionService : IMemoryCompositionService
{
    /// <summary>Drop-priority tag for the retrieved-old layer (1st to drop).</summary>
    internal const string LayerRetrievedOld = "retrieved-old";

    /// <summary>Drop-priority tag for the compressed-mid layer (2nd to drop).</summary>
    internal const string LayerCompressedMid = "compressed-mid";

    /// <summary>Drop-priority tag for the recent-verbatim layer (3rd; oldest-first).</summary>
    internal const string LayerRecentVerbatim = "recent-verbatim";

    /// <summary>Layer tag for the pinned tier — NEVER dropped (FR-42 invariant).</summary>
    internal const string LayerPinned = "pinned";

    private readonly ISummarizationCompressionService _compression;
    private readonly IPinnedContextRepository _pinnedContextRepository;
    private readonly IPinnedContextRecallService _recall;
    private readonly MemoryCompositionOptions _options;
    private readonly ILogger<MemoryCompositionService> _logger;

    public MemoryCompositionService(
        ISummarizationCompressionService compression,
        IPinnedContextRepository pinnedContextRepository,
        IPinnedContextRecallService recall,
        IOptions<MemoryCompositionOptions> options,
        ILogger<MemoryCompositionService> logger)
    {
        ArgumentNullException.ThrowIfNull(compression);
        ArgumentNullException.ThrowIfNull(pinnedContextRepository);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _compression = compression;
        _pinnedContextRepository = pinnedContextRepository;
        _recall = recall;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MemoryComposition> ComposeAsync(
        MemoryCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        // 1. Kill switch (P2 Quiet — no exception; caller short-circuits to no memory).
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "MemoryCompositionService: kill switch off (MemoryComposition:Enabled=false); returning Empty");
            return MemoryComposition.Empty;
        }

        // 2. Clamp options to the hardened bounds. Defensive against operator misconfig
        // — composition is on the chat hot path and a misconfigured caller must not
        // surface as a 500 to the end user.
        var recentN = Math.Clamp(_options.RecentVerbatimTurns, 1, 50);
        var midEnd = Math.Clamp(_options.MidWindowEnd, recentN + 1, 200);
        var retrievedTopK = Math.Clamp(_options.RetrievedOldTopK, 1, 20);
        var totalBudget = Math.Clamp(_options.TotalTokenBudget, 1024, 32_000);
        var compressedMaxTokens = Math.Clamp(_options.CompressedMidMaxTokens, 128, 1024);

        var conversation = request.Conversation ?? Array.Empty<ChatMessage>();

        // 3. Recent verbatim — last N messages of the conversation.
        var recentVerbatim = SelectRecentVerbatim(conversation, recentN);

        // 4. Compressed mid — messages in window [length - midEnd, length - recentN),
        //    folded into a single LLM-generated summary via the compression primitive.
        ChatMessage? compressedMid = null;
        var midMessages = SelectMidWindow(conversation, recentN, midEnd);
        if (midMessages.Count > 0)
        {
            compressedMid = await _compression.CompressAsync(
                midMessages,
                compressedMaxTokens,
                cancellationToken);
        }

        // 5. Retrieved old — when the conversation has at least midEnd turns, surface
        //    the most-similar pinned items for the current user message. Skipped when
        //    matterId is null (recall requires non-empty matterId per its contract).
        IReadOnlyList<PinnedContextItem> retrievedOld = Array.Empty<PinnedContextItem>();
        if (conversation.Count >= midEnd
            && !string.IsNullOrWhiteSpace(request.MatterId)
            && !string.IsNullOrWhiteSpace(request.CurrentUserMessage))
        {
            try
            {
                retrievedOld = await _recall.RecallAsync(
                    request.TenantId,
                    request.MatterId!,
                    request.CurrentUserMessage,
                    retrievedTopK,
                    cancellationToken)
                    ?? Array.Empty<PinnedContextItem>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Soft-fail: recall is an enhancement; the other layers still compose.
                _logger.LogWarning(ex,
                    "MemoryCompositionService: selective recall failed; retrieved-old layer omitted (tenant={TenantId} matter={MatterId})",
                    request.TenantId, request.MatterId);
                retrievedOld = Array.Empty<PinnedContextItem>();
            }
        }

        // 6. Pinned — ALL pinned items for the (tenant, user, matter) tuple, grouped
        //    by pinType. ALWAYS included (FR-42). Two repository calls cover both
        //    scopings; results are deduplicated by id. Defensive against per-call
        //    failures: a primary-call failure does not nuke the whole layer.
        var pinned = await CollectAllPinsAsync(request, cancellationToken);

        // 7. Deduplicate retrieved-old against the pinned set — the recall service
        //    returns items already present in the pinned tier; eliding them from the
        //    retrieved tier keeps the prompt clean. The retrieved tier acts as
        //    "promote these to top-of-mind", not "add new items".
        if (retrievedOld.Count > 0 && pinned.Count > 0)
        {
            var pinnedIds = new HashSet<string>(
                pinned.SelectMany(kv => kv.Value).Select(p => p.Id),
                StringComparer.Ordinal);
            // Note: retrieved-old items ARE intentionally a subset of pinned (recall
            // ranks pinned items). We keep them in the retrieved tier as a
            // relevance-emphasised projection — task 068 renders them BEFORE the
            // unranked pinned block so the LLM sees similarity-promoted items first.
            // No dedup transformation needed; we simply log the overlap for telemetry.
            var overlap = retrievedOld.Count(p => pinnedIds.Contains(p.Id));
            if (overlap > 0)
            {
                _logger.LogDebug(
                    "MemoryCompositionService: retrieved-old overlaps pinned by {Overlap}/{Total} items (tenant={TenantId} matter={MatterId}); kept as relevance projection",
                    overlap, retrievedOld.Count, request.TenantId, request.MatterId);
            }
        }

        // 8. Token accounting. Estimate per layer; sum.
        var recentTokens = EstimateTokens(recentVerbatim);
        var compressedTokens = compressedMid is null ? 0 : EstimateTokens(compressedMid.Content);
        var retrievedTokens = EstimateTokens(retrievedOld);
        var pinnedTokens = EstimatePinnedTokens(pinned);
        var aggregate = recentTokens + compressedTokens + retrievedTokens + pinnedTokens;

        // 9. Budget enforcement — drop priority: retrieved-old → compressed-mid →
        //    recent-verbatim oldest-first. Pinned NEVER drops.
        var dropped = new List<string>();

        if (aggregate > totalBudget && retrievedOld.Count > 0)
        {
            dropped.Add(LayerRetrievedOld);
            aggregate -= retrievedTokens;
            retrievedOld = Array.Empty<PinnedContextItem>();
            retrievedTokens = 0;
        }

        if (aggregate > totalBudget && compressedMid is not null)
        {
            dropped.Add(LayerCompressedMid);
            aggregate -= compressedTokens;
            compressedMid = null;
            compressedTokens = 0;
        }

        if (aggregate > totalBudget && recentVerbatim.Count > 0)
        {
            // Drop oldest-first from the recent-verbatim list. We mutate a working list.
            var working = new List<ChatMessage>(recentVerbatim);
            var droppedAny = false;
            while (working.Count > 0 && aggregate > totalBudget)
            {
                var oldest = working[0];
                working.RemoveAt(0);
                var t = EstimateTokens(oldest.Content);
                aggregate -= t;
                recentTokens -= t;
                droppedAny = true;
            }
            recentVerbatim = working;
            if (droppedAny)
            {
                dropped.Add(LayerRecentVerbatim);
            }
        }

        // 10. FR-42 invariant: pinned is preserved even if it alone exceeds the budget.
        //     We do NOT drop pinned items here. If aggregate is STILL over budget after
        //     dropping every droppable layer, we log a warning and let the chat prompt
        //     builder (task 068) handle the final hard guard. Returning pinned-only is
        //     the canonical degraded shape.
        if (aggregate > totalBudget)
        {
            _logger.LogWarning(
                "MemoryCompositionService: pinned tier alone exceeds total budget {Budget} ({Pinned} tokens, tenant={TenantId} matter={MatterId}); FR-42 preserved — pinned NOT dropped; caller must enforce final hard guard",
                totalBudget, pinnedTokens, request.TenantId, request.MatterId);
        }

        if (dropped.Count > 0)
        {
            _logger.LogDebug(
                "MemoryCompositionService: dropped {Count} layer(s) {Layers} under budget {Budget} (tenant={TenantId} matter={MatterId})",
                dropped.Count, string.Join(",", dropped), totalBudget, request.TenantId, request.MatterId);
        }

        return new MemoryComposition(
            RecentVerbatim: recentVerbatim,
            CompressedMid: compressedMid,
            RetrievedOld: retrievedOld,
            Pinned: pinned,
            EstimatedTokenCount: aggregate,
            DroppedLayers: dropped);
    }

    // =========================================================================
    // Internal helpers (internal for unit test access)
    // =========================================================================

    /// <summary>
    /// Slice the last <paramref name="n"/> messages from the conversation. Returns an
    /// empty list when the conversation is empty.
    /// </summary>
    internal static IReadOnlyList<ChatMessage> SelectRecentVerbatim(
        IReadOnlyList<ChatMessage> conversation,
        int n)
    {
        if (conversation.Count == 0 || n <= 0)
        {
            return Array.Empty<ChatMessage>();
        }

        if (conversation.Count <= n)
        {
            return conversation;
        }

        var start = conversation.Count - n;
        var slice = new List<ChatMessage>(n);
        for (var i = start; i < conversation.Count; i++)
        {
            slice.Add(conversation[i]);
        }
        return slice;
    }

    /// <summary>
    /// Slice the mid-distance window <c>[length - midEnd, length - recentN)</c> from
    /// the conversation. Returns an empty list when the conversation is shorter than
    /// the recent-verbatim cut-off (no mid window exists).
    /// </summary>
    internal static IReadOnlyList<ChatMessage> SelectMidWindow(
        IReadOnlyList<ChatMessage> conversation,
        int recentN,
        int midEnd)
    {
        if (conversation.Count <= recentN)
        {
            return Array.Empty<ChatMessage>();
        }

        var endExclusive = conversation.Count - recentN;
        var startInclusive = Math.Max(0, conversation.Count - midEnd);
        var count = endExclusive - startInclusive;
        if (count <= 0)
        {
            return Array.Empty<ChatMessage>();
        }

        var slice = new List<ChatMessage>(count);
        for (var i = startInclusive; i < endExclusive; i++)
        {
            slice.Add(conversation[i]);
        }
        return slice;
    }

    /// <summary>
    /// Aggregate all pins for the (tenant, user) + (tenant, matter) scopes, group
    /// by <see cref="PinType"/>, and deduplicate by id. Soft-fails per scope: a
    /// repository failure on one scope does not nuke the other.
    /// </summary>
    internal async Task<IReadOnlyDictionary<PinType, IReadOnlyList<PinnedContextItem>>> CollectAllPinsAsync(
        MemoryCompositionRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PinnedContextItem> userPins = Array.Empty<PinnedContextItem>();
        IReadOnlyList<PinnedContextItem> matterPins = Array.Empty<PinnedContextItem>();

        try
        {
            userPins = await _pinnedContextRepository.GetByUserAsync(
                request.TenantId, request.UserId, cancellationToken)
                ?? Array.Empty<PinnedContextItem>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MemoryCompositionService: GetByUserAsync failed; user-scope pinned tier empty (tenant={TenantId} user={UserId})",
                request.TenantId, request.UserId);
        }

        if (!string.IsNullOrWhiteSpace(request.MatterId))
        {
            try
            {
                matterPins = await _pinnedContextRepository.GetByMatterAsync(
                    request.TenantId, request.MatterId!, cancellationToken)
                    ?? Array.Empty<PinnedContextItem>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MemoryCompositionService: GetByMatterAsync failed; matter-scope pinned tier empty (tenant={TenantId} matter={MatterId})",
                    request.TenantId, request.MatterId);
            }
        }

        if (userPins.Count == 0 && matterPins.Count == 0)
        {
            return new Dictionary<PinType, IReadOnlyList<PinnedContextItem>>();
        }

        // Deduplicate by id; group by pinType in stable enum order.
        var byId = new Dictionary<string, PinnedContextItem>(StringComparer.Ordinal);
        foreach (var p in userPins)
        {
            if (p is null || string.IsNullOrEmpty(p.Id)) continue;
            byId[p.Id] = p;
        }
        foreach (var p in matterPins)
        {
            if (p is null || string.IsNullOrEmpty(p.Id)) continue;
            byId[p.Id] = p;
        }

        var grouped = byId.Values
            .GroupBy(p => p.PinType)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PinnedContextItem>)g.ToList());

        return grouped;
    }

    /// <summary>
    /// Estimate the token count of a list of chat messages by summing per-message
    /// content lengths and dividing by <see cref="MemoryCompositionOptions.CharsPerToken"/>.
    /// </summary>
    internal int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return 0;
        var total = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            total += EstimateTokens(messages[i].Content);
        }
        return total;
    }

    /// <summary>
    /// Estimate the token count of a list of pinned items by summing title + content
    /// lengths and dividing by <see cref="MemoryCompositionOptions.CharsPerToken"/>.
    /// </summary>
    internal int EstimateTokens(IReadOnlyList<PinnedContextItem> pins)
    {
        if (pins.Count == 0) return 0;
        var total = 0;
        for (var i = 0; i < pins.Count; i++)
        {
            var p = pins[i];
            total += EstimateTokens(p.Title) + EstimateTokens(p.Content);
        }
        return total;
    }

    /// <summary>
    /// Estimate the aggregate token count of the grouped pinned tier.
    /// </summary>
    internal int EstimatePinnedTokens(IReadOnlyDictionary<PinType, IReadOnlyList<PinnedContextItem>> pinned)
    {
        if (pinned.Count == 0) return 0;
        var total = 0;
        foreach (var kv in pinned)
        {
            total += EstimateTokens(kv.Value);
        }
        return total;
    }

    /// <summary>
    /// Conservative token estimate (charsPerToken default 4.0, matches GPT-4o
    /// English-prose tokenisation). Used uniformly across all layers so the
    /// budget arithmetic stays consistent with the compression service's output.
    /// </summary>
    internal int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / _options.CharsPerToken);
    }
}
