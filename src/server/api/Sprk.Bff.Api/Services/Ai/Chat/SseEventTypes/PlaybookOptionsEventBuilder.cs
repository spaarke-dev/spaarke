using System.Diagnostics;
using Microsoft.Extensions.Logging;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// Composes the <see cref="PlaybookOptionsSseEventData"/> payload for the
/// <c>playbook_options</c> SSE event (chat-routing-redesign-r1 task 117a / FR-49).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pipeline role</b>: this builder is the projection layer that converts the upstream
/// chat-routing-redesign-r1 selection contracts into the locked SSE wire shape consumed by
/// the chat frontend (task 117b). It owns the orchestration of:
/// <list type="bullet">
///   <item><description><see cref="IPlaybookCandidateSelector.Select"/> (task 113R) — aggregates per-file Phase B results into a top-N selection with a rerank-recommended signal.</description></item>
///   <item><description><see cref="IIntentRerankerService.RerankAsync"/> (task 111R) — invoked ONLY when the selector recommends rerank; produces the final top-3 with controlled-vocabulary reason tags.</description></item>
///   <item><description>Projection to the locked five-field <see cref="PlaybookOptionCandidate"/> shape with empty-string fallback for fields the upstream contracts do not surface (today: <c>PlaybookCode</c>).</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>FR-48 invariant</b>: this builder NEVER calls <c>PlaybookExecutionEngine</c> or any
/// execution surface. It returns the SSE payload only; the user clicks to invoke.
/// </para>
///
/// <para>
/// <b>FR-51 invariant</b>: the resulting event always has
/// <see cref="PlaybookOptionsSseEventData.LibraryModalCta"/> set to <c>true</c>,
/// regardless of candidate count.
/// </para>
///
/// <para>
/// <b>ADR-013 boundary</b>: internal AI orchestration in <c>Services/Ai/Chat/</c>. Not in
/// the <c>Services/Ai/PublicContracts/</c> facade. CRUD code MUST NOT inject this directly.
/// </para>
///
/// <para>
/// <b>ADR-015 tier-1 telemetry contract (binding)</b>: log lines emit ONLY:
/// candidate counts, rerank flags + controlled-vocabulary reason tags, attachment counts,
/// and wall-clock latency. They MUST NOT emit:
/// <list type="bullet">
///   <item><description>The user message (verbatim or paraphrased)</description></item>
///   <item><description>Attachment filenames, MIME types, or text content</description></item>
///   <item><description>Per-candidate confidence scores beyond top-1 (the count is the signal)</description></item>
///   <item><description>Free-form LLM rerank reasons (only the controlled-vocabulary <c>RerankReason</c> tag)</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Wiring</b>: this service is intentionally NOT wired into
/// <c>ChatEndpoints</c> in task 117a — defining the contract + projection is sufficient
/// for the BE side. A future orchestrator task (or 117b's BE companion) chooses the
/// emission point in the chat streaming flow and wires this through
/// <see cref="ChatSseEventFactory.CreatePlaybookOptionsEvent"/>.
/// </para>
/// </remarks>
public sealed class PlaybookOptionsEventBuilder
{
    private readonly IPlaybookCandidateSelector _selector;
    private readonly IIntentRerankerService _reranker;
    private readonly ILogger<PlaybookOptionsEventBuilder> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="PlaybookOptionsEventBuilder"/>.
    /// </summary>
    /// <param name="selector">Top-N candidate selector (task 113R).</param>
    /// <param name="reranker">Hybrid intent reranker (task 111R).</param>
    /// <param name="logger">Logger.</param>
    public PlaybookOptionsEventBuilder(
        IPlaybookCandidateSelector selector,
        IIntentRerankerService reranker,
        ILogger<PlaybookOptionsEventBuilder> logger)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _reranker = reranker ?? throw new ArgumentNullException(nameof(reranker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the <see cref="PlaybookOptionsSseEventData"/> payload for the locked
    /// <c>playbook_options</c> SSE wire shape (FR-49).
    /// </summary>
    /// <param name="phaseBResults">
    /// Per-file vector match results from <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/>
    /// (task 112). May be empty (yields a no-match event with the library CTA still on).
    /// </param>
    /// <param name="userMessage">
    /// Verbatim user message — passed through to the reranker per ADR-015 (the user's own
    /// text is permitted on the LLM input boundary). This builder NEVER logs it.
    /// </param>
    /// <param name="rerankerAttachmentMetadata">
    /// Per-attachment metadata for the reranker LLM call. Filename + content-type + integer
    /// text length only (ADR-015 tier-1 input contract). May be empty when the user sent no
    /// attachments. The shape mirrors <see cref="AttachmentMetadata"/>.
    /// </param>
    /// <param name="sessionAttachmentIds">
    /// Deterministic session attachment IDs that will be surfaced verbatim in the SSE
    /// payload's <c>sessionAttachmentIds</c> field. ADR-015 tier-1 safe — opaque IDs only.
    /// </param>
    /// <param name="cancellationToken">Cancellation token, propagated to both selector and reranker.</param>
    /// <returns>The locked-shape SSE payload, ready to wrap via <see cref="ChatSseEventFactory.CreatePlaybookOptionsEvent"/>.</returns>
    public async Task<PlaybookOptionsSseEventData> BuildAsync(
        IReadOnlyList<PhaseBPerFileResult> phaseBResults,
        string userMessage,
        IReadOnlyList<AttachmentMetadata> rerankerAttachmentMetadata,
        IReadOnlyList<string> sessionAttachmentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phaseBResults);
        ArgumentNullException.ThrowIfNull(userMessage);
        ArgumentNullException.ThrowIfNull(rerankerAttachmentMetadata);
        ArgumentNullException.ThrowIfNull(sessionAttachmentIds);

        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();

        // 1. Selector (task 113R) — pure in-memory aggregation.
        var selection = _selector.Select(phaseBResults, cancellationToken);

        // 2. Conditional rerank (task 111R) — only when selector flags ambiguity.
        IReadOnlyList<PlaybookOptionCandidate> finalCandidates;
        bool rerankInvoked;
        string? rerankReason;

        if (selection.RerankRecommended && selection.TopCandidates.Count > 0)
        {
            var rerankInput = new IntentRerankerInput
            {
                UserMessage = userMessage,
                AttachmentMetadata = rerankerAttachmentMetadata,
                Top5Candidates = selection.TopCandidates,
            };

            var rerankResult = await _reranker.RerankAsync(rerankInput, cancellationToken).ConfigureAwait(false);

            rerankInvoked = true;
            rerankReason = rerankResult.Reason;

            // Graceful-degrade: if the reranker returned zero candidates (shouldn't happen
            // for non-empty input, but the contract permits it), fall back to the selector's
            // top candidates so the user still sees options.
            if (rerankResult.Top3.Count > 0)
            {
                finalCandidates = rerankResult.Top3
                    .Select(r => ProjectFromRanked(r, selection.Reason))
                    .ToList();
            }
            else
            {
                finalCandidates = selection.TopCandidates
                    .Select(c => ProjectFromSelector(c, selection.Reason))
                    .ToList();
            }
        }
        else
        {
            rerankInvoked = false;
            rerankReason = null;
            finalCandidates = selection.TopCandidates
                .Select(c => ProjectFromSelector(c, selection.Reason))
                .ToList();
        }

        stopwatch.Stop();

        // ADR-015 tier-1 telemetry: counts + tags + latency ONLY.
        // No user message text, no attachment names/types, no per-candidate confidences
        // beyond top-1 signal counted via candidateCount.
        _logger.LogInformation(
            "playbook_options event built: candidateCount={CandidateCount} rerankInvoked={RerankInvoked} rerankReason={RerankReason} selectorReason={SelectorReason} sessionAttachmentCount={SessionAttachmentCount} latencyMs={LatencyMs}",
            finalCandidates.Count,
            rerankInvoked,
            rerankReason ?? "n/a",
            selection.Reason,
            sessionAttachmentIds.Count,
            stopwatch.ElapsedMilliseconds);

        return new PlaybookOptionsSseEventData(
            Candidates: finalCandidates,
            LibraryModalCta: true, // FR-51 invariant — always on.
            SessionAttachmentIds: sessionAttachmentIds,
            RerankInvoked: rerankInvoked,
            RerankReason: rerankReason);
    }

    /// <summary>
    /// Projects an upstream <see cref="PlaybookCandidate"/> (from the selector) to the
    /// locked five-field <see cref="PlaybookOptionCandidate"/> shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PlaybookCode</c> is emitted as empty string when the upstream contract does not
    /// surface it (current state — the selector + reranker work off <c>PlaybookCandidate</c>
    /// which carries id + name + confidence + contributing-file-count but not code). A
    /// future orchestrator task may enrich via <c>IPlaybookLookupService</c> before emit.
    /// </para>
    /// <para>
    /// The <c>Reason</c> on the projected candidate uses the selector's high-level reason
    /// tag (controlled vocabulary per FR-49 / ADR-015). For the rerank path see
    /// <see cref="ProjectFromRanked"/>.
    /// </para>
    /// </remarks>
    private static PlaybookOptionCandidate ProjectFromSelector(
        PlaybookCandidate candidate,
        string selectorReason)
        => new(
            PlaybookId: candidate.PlaybookId,
            PlaybookCode: string.Empty,
            DisplayName: candidate.PlaybookName,
            Confidence: candidate.Confidence,
            Reason: selectorReason);

    /// <summary>
    /// Projects a <see cref="RankedPlaybookCandidate"/> (from the reranker) to the locked
    /// five-field <see cref="PlaybookOptionCandidate"/> shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reranker's per-candidate <c>RerankReason</c> is FREE-FORM LLM TEXT and MUST NOT
    /// be emitted in the ADR-015 tier-1 SSE payload. Instead, this projection uses the
    /// upstream selector's controlled-vocabulary <paramref name="selectorReason"/> tag.
    /// The overall rerank outcome tag (<see cref="IntentRerankerResult.Reason"/>) is
    /// surfaced separately via <see cref="PlaybookOptionsSseEventData.RerankReason"/>,
    /// also controlled-vocabulary.
    /// </para>
    /// </remarks>
    private static PlaybookOptionCandidate ProjectFromRanked(
        RankedPlaybookCandidate ranked,
        string selectorReason)
        => new(
            PlaybookId: ranked.Candidate.PlaybookId,
            PlaybookCode: string.Empty,
            DisplayName: ranked.Candidate.PlaybookName,
            Confidence: ranked.Candidate.Confidence,
            Reason: selectorReason);
}
