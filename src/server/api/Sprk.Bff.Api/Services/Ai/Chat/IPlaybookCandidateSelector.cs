using Sprk.Bff.Api.Models.Ai;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// File-aware playbook-candidate selector (chat-routing-redesign-r1 FR-47 + FR-48).
/// Consumes the per-file top-K output of
/// <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/> (task 112) and
/// produces the final top-N candidate list surfaced to the user via the
/// <c>playbook_options</c> SSE event (task 117a).
/// </summary>
/// <remarks>
/// <para>
/// <b>FR-48 invariant — NEVER auto-execute</b>: this selector ALWAYS returns
/// a candidate list. There is no path through <see cref="Select"/> that
/// dispatches a playbook directly — even on a high-confidence single match.
/// The downstream <c>playbook_options</c> SSE event surfaces candidates to
/// the user; the user clicks to invoke. The return shape intentionally does
/// not carry an "auto-execute" flag.
/// </para>
/// <para>
/// <b>Boundary (ADR-013)</b>: this is an internal AI orchestration service
/// scoped to <c>Services/Ai/Chat/</c>. It is NOT part of the
/// <c>Services/Ai/PublicContracts/</c> facade and must not be consumed
/// directly by CRUD code.
/// </para>
/// <para>
/// <b>Rerank integration point (task 111R)</b>: when the per-file vector
/// match returns an ambiguous result (top-1 and top-2 within
/// <see cref="Configuration.PlaybookSelectorOptions.ConfidenceDeltaMargin"/>,
/// or top-1 below
/// <see cref="Configuration.PlaybookSelectorOptions.ConfidenceThreshold"/>),
/// the selector sets
/// <see cref="PlaybookCandidateSelection.RerankRecommended"/> to <c>true</c>.
/// The CALLER (a higher-level orchestrator wired in task 111R or later) is
/// responsible for the "if recommended, invoke <c>IIntentRerankerService</c>"
/// flow. The selector itself does not call any LLM.
/// </para>
/// </remarks>
public interface IPlaybookCandidateSelector
{
    /// <summary>
    /// Selects the final top-N candidate playbooks from the per-file vector
    /// match results (task 112 output). Pure in-memory aggregation +
    /// thresholding — no I/O, no LLM calls.
    /// </summary>
    /// <param name="perFileResults">
    /// Per-file top-K results from
    /// <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/>. May be
    /// empty (returns <see cref="PlaybookCandidateSelection.NoMatch"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PlaybookCandidateSelection"/> carrying:
    /// (a) the top-N candidates above
    /// <see cref="Configuration.PlaybookSelectorOptions.SecondaryThreshold"/>,
    /// (b) the <see cref="PlaybookCandidateSelection.RerankRecommended"/>
    /// flag, and (c) a <see cref="PlaybookCandidateSelection.Reason"/> tag
    /// for telemetry and tests. <b>Never</b> carries an auto-execute flag.
    /// </returns>
    PlaybookCandidateSelection Select(
        IReadOnlyList<PhaseBPerFileResult> perFileResults,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Top-N candidate selection result (chat-routing-redesign-r1 FR-47).
/// </summary>
/// <remarks>
/// <para>
/// <b>FR-48 invariant</b>: this record intentionally carries no
/// <c>AutoExecute</c> flag. Even when <see cref="Reason"/> is
/// <c>"high-confidence-single"</c>, the caller MUST surface the candidates
/// to the user — the selector enforces the no-auto-execute rule by the
/// shape of the return type.
/// </para>
/// </remarks>
/// <param name="TopCandidates">
/// Top-N candidates (ordered by confidence descending) above
/// <see cref="Configuration.PlaybookSelectorOptions.SecondaryThreshold"/>.
/// Truncated to
/// <see cref="Configuration.PlaybookSelectorOptions.MaxCandidates"/>.
/// May be empty when no per-file result rises above the secondary threshold.
/// </param>
/// <param name="RerankRecommended">
/// <c>true</c> when the selection is ambiguous (top-1 and top-2 within
/// <see cref="Configuration.PlaybookSelectorOptions.ConfidenceDeltaMargin"/>,
/// or top-1 below
/// <see cref="Configuration.PlaybookSelectorOptions.ConfidenceThreshold"/>).
/// The caller (orchestrator wired in task 111R) should route through the
/// LLM reranker before surfacing candidates. <c>false</c> for the
/// high-confidence-single path and for the no-match path.
/// </param>
/// <param name="Reason">
/// Telemetry tag explaining the selection outcome — one of:
/// <list type="bullet">
///   <item><description><c>"no-match"</c> — input empty or all candidates below <c>SecondaryThreshold</c>.</description></item>
///   <item><description><c>"high-confidence-single"</c> — top-1 ≥ <c>ConfidenceThreshold</c> AND top-2 below by more than <c>ConfidenceDeltaMargin</c>.</description></item>
///   <item><description><c>"ambiguous-top-2-within-margin"</c> — top-1 ≥ <c>ConfidenceThreshold</c> but top-2 within margin; rerank recommended.</description></item>
///   <item><description><c>"ambiguous-below-threshold"</c> — top-1 below <c>ConfidenceThreshold</c>; rerank recommended.</description></item>
/// </list>
/// </param>
public sealed record PlaybookCandidateSelection(
    IReadOnlyList<PlaybookCandidate> TopCandidates,
    bool RerankRecommended,
    string Reason)
{
    /// <summary>
    /// Canonical no-match result. Empty candidate list, no rerank
    /// recommendation, <c>Reason = "no-match"</c>.
    /// </summary>
    public static PlaybookCandidateSelection NoMatch { get; } = new(
        TopCandidates: Array.Empty<PlaybookCandidate>(),
        RerankRecommended: false,
        Reason: "no-match");
}

/// <summary>
/// Single candidate playbook surfaced to the user (chat-routing-redesign-r1
/// FR-47). Aggregated from one or more per-file
/// <see cref="PlaybookSearchResult"/> hits.
/// </summary>
/// <param name="PlaybookId">Opaque immutable Dataverse PK (sprk_aiplaybook GUID — string form for round-trip with vector index).</param>
/// <param name="PlaybookName">Display name (Dataverse <c>sprk_name</c>).</param>
/// <param name="Confidence">
/// Aggregated similarity score (max across files when the same playbook
/// matches multiple attachments). In the unit interval <c>[0, 1]</c>.
/// </param>
/// <param name="ContributingFileCount">
/// Number of per-file results that contributed this candidate. Useful
/// telemetry signal — higher count → broader cross-file agreement.
/// </param>
public sealed record PlaybookCandidate(
    string PlaybookId,
    string PlaybookName,
    double Confidence,
    int ContributingFileCount);
