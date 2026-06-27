using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// In-memory top-N selector (chat-routing-redesign-r1 FR-47 + FR-48).
/// Aggregates per-file vector match output from
/// <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/> (task 112) by
/// playbook ID, applies the FR-47 confidence-threshold + delta-margin
/// decision tree, and returns the candidates for downstream
/// <c>playbook_options</c> SSE rendering (task 117a).
/// </summary>
/// <remarks>
/// <para>
/// <b>FR-48 invariant</b>: the implementation NEVER calls
/// <c>PlaybookExecutionEngine</c> or any execution surface. It returns
/// candidates only; the user confirms before invoke.
/// </para>
/// <para>
/// <b>Aggregation strategy</b>: when the same playbook appears in multiple
/// per-file results, the candidate's confidence is the MAX across files
/// (the file with the best match wins). This is the most permissive
/// aggregation — it ensures a strong single-file signal is not diluted by
/// weaker hits on sibling files. Cross-file agreement is captured
/// separately via <see cref="PlaybookCandidate.ContributingFileCount"/>.
/// </para>
/// <para>
/// <b>Telemetry (ADR-015 tier 1)</b>: log line includes candidate counts,
/// top-1 confidence, contributing-file totals, and the
/// <see cref="PlaybookCandidateSelection.Reason"/> tag. Playbook names ARE
/// logged (these are admin-facing strings, not user content). User message
/// text, file content, and the underlying per-file similarity vectors are
/// NEVER logged through this surface.
/// </para>
/// </remarks>
public sealed class PlaybookCandidateSelector : IPlaybookCandidateSelector
{
    private readonly IOptions<PlaybookSelectorOptions> _options;
    private readonly ILogger<PlaybookCandidateSelector> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="PlaybookCandidateSelector"/>.
    /// </summary>
    /// <param name="options">Typed FR-47 thresholds (ADR-018).</param>
    /// <param name="logger">Logger instance.</param>
    public PlaybookCandidateSelector(
        IOptions<PlaybookSelectorOptions> options,
        ILogger<PlaybookCandidateSelector> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public PlaybookCandidateSelection Select(
        IReadOnlyList<PhaseBPerFileResult> perFileResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(perFileResults);
        cancellationToken.ThrowIfCancellationRequested();

        var opts = _options.Value;

        // === Step 1: empty input → no-match ===
        if (perFileResults.Count == 0)
        {
            _logger.LogInformation(
                "PlaybookCandidateSelector: no per-file results — returning NoMatch");
            return PlaybookCandidateSelection.NoMatch;
        }

        // === Step 2: aggregate per-file results by playbook ID ===
        // Map: playbookId → (maxScore, name, contributingFileCount).
        var aggregated = new Dictionary<string, AggregatedCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileResult in perFileResults)
        {
            if (fileResult.Candidates is null || fileResult.Candidates.Count == 0)
            {
                continue;
            }

            foreach (var hit in fileResult.Candidates)
            {
                if (string.IsNullOrWhiteSpace(hit.PlaybookId))
                {
                    continue;
                }

                if (aggregated.TryGetValue(hit.PlaybookId, out var existing))
                {
                    // MAX confidence across files; increment contributing file count.
                    var maxScore = Math.Max(existing.MaxScore, hit.Score);
                    aggregated[hit.PlaybookId] = existing with
                    {
                        MaxScore = maxScore,
                        ContributingFileCount = existing.ContributingFileCount + 1,
                    };
                }
                else
                {
                    aggregated[hit.PlaybookId] = new AggregatedCandidate(
                        PlaybookId: hit.PlaybookId,
                        PlaybookName: hit.PlaybookName,
                        MaxScore: hit.Score,
                        ContributingFileCount: 1);
                }
            }
        }

        // === Step 3: order by confidence DESC, prune below SecondaryThreshold ===
        var ordered = aggregated.Values
            .OrderByDescending(c => c.MaxScore)
            .ToList();

        if (ordered.Count == 0)
        {
            _logger.LogInformation(
                "PlaybookCandidateSelector: aggregation produced 0 candidates — returning NoMatch");
            return PlaybookCandidateSelection.NoMatch;
        }

        var aboveSecondary = ordered
            .Where(c => c.MaxScore >= opts.SecondaryThreshold)
            .ToList();

        if (aboveSecondary.Count == 0)
        {
            _logger.LogInformation(
                "PlaybookCandidateSelector: 0 candidates above SecondaryThreshold={SecondaryThreshold:F3} " +
                "(top-1={TopScore:F3}) — returning NoMatch",
                opts.SecondaryThreshold, ordered[0].MaxScore);
            return PlaybookCandidateSelection.NoMatch;
        }

        // === Step 4: FR-47 decision tree ===
        var top1Score = aboveSecondary[0].MaxScore;
        var top2Score = aboveSecondary.Count > 1 ? aboveSecondary[1].MaxScore : (double?)null;

        bool rerankRecommended;
        string reason;

        if (top1Score >= opts.ConfidenceThreshold)
        {
            // High-confidence single requires the gap to top-2 to exceed the
            // margin. A close runner-up means the model is genuinely uncertain
            // between two plausible playbooks — surface both AND recommend a
            // rerank so the LLM can break the tie.
            var gap = top2Score is { } t2 ? top1Score - t2 : double.PositiveInfinity;
            if (gap > opts.ConfidenceDeltaMargin)
            {
                rerankRecommended = false;
                reason = "high-confidence-single";
            }
            else
            {
                rerankRecommended = true;
                reason = "ambiguous-top-2-within-margin";
            }
        }
        else
        {
            // Top-1 below confidence threshold → rerank recommended regardless
            // of gap. (FR-48 still applies: surface candidates to user; rerank
            // is the caller's decision.)
            rerankRecommended = true;
            reason = "ambiguous-below-threshold";
        }

        // === Step 5: cap at MaxCandidates ===
        var capped = aboveSecondary
            .Take(opts.MaxCandidates)
            .Select(a => new PlaybookCandidate(
                PlaybookId: a.PlaybookId,
                PlaybookName: a.PlaybookName,
                Confidence: a.MaxScore,
                ContributingFileCount: a.ContributingFileCount))
            .ToList();

        // === Step 6: telemetry (tier-1 ADR-015) ===
        _logger.LogInformation(
            "PlaybookCandidateSelector: returned {CandidateCount} candidate(s) (top-1={TopScore:F3}, " +
            "rerankRecommended={RerankRecommended}, reason={Reason}, filesContributing={FileCount})",
            capped.Count,
            top1Score,
            rerankRecommended,
            reason,
            perFileResults.Count);

        return new PlaybookCandidateSelection(
            TopCandidates: capped,
            RerankRecommended: rerankRecommended,
            Reason: reason);
    }

    /// <summary>
    /// Internal aggregation row — represents one playbook's combined view
    /// across all contributing files.
    /// </summary>
    private sealed record AggregatedCandidate(
        string PlaybookId,
        string PlaybookName,
        double MaxScore,
        int ContributingFileCount);
}
