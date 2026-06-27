using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Hybrid LLM intent reranker (chat-routing-redesign-r1 FR-46, task 111R).
/// Consumes a top-5 candidate list (typically the
/// <see cref="PlaybookCandidateSelection.TopCandidates"/> output of task
/// 113R when <see cref="PlaybookCandidateSelection.RerankRecommended"/> is
/// <c>true</c>) plus the original user message and attachment metadata,
/// invokes Azure OpenAI gpt-4o-mini with structured output (JSON schema)
/// to select the best 3, and returns the reranked subset.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary (ADR-013)</b>: this is an internal AI orchestration service
/// scoped to <c>Services/Ai/Chat/</c>. It is NOT part of the
/// <c>Services/Ai/PublicContracts/</c> facade and must not be consumed
/// directly by CRUD code.
/// </para>
/// <para>
/// <b>ADR-015 tier-1 data governance</b>: the LLM input is metadata-only —
/// the user message verbatim (the user's own text), attachment metadata
/// (filename, contentType, textLength only), and candidate metadata
/// (PlaybookId, PlaybookName, Confidence). File text content, file binary
/// data, and embedding vectors NEVER cross this boundary. Logs emit only
/// counts + latency; user message text and LLM response content are
/// NEVER logged.
/// </para>
/// <para>
/// <b>FR-46 latency budget</b>: hard timeout per
/// <see cref="Configuration.IntentRerankerOptions.TimeoutMs"/> (default
/// 800 ms). On timeout the service falls back to top-3-by-confidence with
/// <c>RerankInvoked=true</c> and a "timeout-graceful-degrade" reason — it
/// never throws to the caller.
/// </para>
/// <para>
/// <b>FR-48 invariant</b>: the reranker NEVER auto-executes a playbook —
/// it returns the reranked candidate list only. The downstream
/// <c>playbook_options</c> SSE event (task 117a) is the only path to user
/// confirmation; the user clicks to invoke. The return shape carries no
/// "auto-execute" flag.
/// </para>
/// <para>
/// <b>Decision boundary (when to invoke)</b>: the reranker itself does NOT
/// decide when to fire. The CALLER (an orchestrator wired in a future
/// task) inspects the upstream
/// <see cref="PlaybookCandidateSelection.RerankRecommended"/> flag and
/// calls this service only when ambiguity is present. Calling the service
/// when no ambiguity exists is wasteful but not incorrect.
/// </para>
/// </remarks>
public interface IIntentRerankerService
{
    /// <summary>
    /// Reranks the supplied top-5 candidate list down to a top-3 using
    /// gpt-4o-mini structured output. Metadata-only LLM input per
    /// ADR-015 tier-1.
    /// </summary>
    /// <param name="input">
    /// Reranker input — user message, attachment metadata, and the top-5
    /// candidates to rerank.
    /// </param>
    /// <param name="cancellationToken">Cancellation token. The service composes a linked
    /// internal timeout per <see cref="Configuration.IntentRerankerOptions.TimeoutMs"/>.</param>
    /// <returns>
    /// A <see cref="IntentRerankerResult"/> carrying the reranked top-3 with
    /// reason text, the always-true <see cref="IntentRerankerResult.RerankInvoked"/>
    /// flag, and a short telemetry-friendly
    /// <see cref="IntentRerankerResult.Reason"/> tag.
    /// </returns>
    Task<IntentRerankerResult> RerankAsync(
        IntentRerankerInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input to <see cref="IIntentRerankerService.RerankAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-015 contract</b>: only the fields on this record are sent to the
/// LLM. <c>UserMessage</c> is the verbatim user input (allowed per ADR-015
/// because it IS the user's own message). Attachment fields are limited
/// to filename, contentType, and the integer textLength — no file body,
/// no binary data. Candidate fields are the public metadata already
/// surfaced upstream — no embeddings, no per-file vectors.
/// </para>
/// </remarks>
public sealed record IntentRerankerInput
{
    /// <summary>Verbatim user message (ADR-015: user's own text is permitted).</summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Per-attachment metadata for the current turn. May be empty when the
    /// user sent no attachments. Filename, contentType, and textLength only —
    /// NO body or binary.
    /// </summary>
    public required IReadOnlyList<AttachmentMetadata> AttachmentMetadata { get; init; }

    /// <summary>
    /// Top-5 candidate list from 113R's
    /// <see cref="IPlaybookCandidateSelector.Select"/> (or any upstream
    /// vector-match step). Reusing the existing
    /// <see cref="PlaybookCandidate"/> record keeps the cross-stage contract
    /// stable and avoids drift between the selector and the reranker.
    /// </summary>
    public required IReadOnlyList<PlaybookCandidate> Top5Candidates { get; init; }
}

/// <summary>
/// Per-attachment metadata sent to the rerank LLM. ADR-015 tier-1: filename,
/// contentType, and integer textLength only. File body and binary content
/// MUST NOT cross this boundary.
/// </summary>
public sealed record AttachmentMetadata
{
    /// <summary>Original filename (e.g., <c>"contract.pdf"</c>).</summary>
    public required string Filename { get; init; }

    /// <summary>MIME content-type (e.g., <c>"application/pdf"</c>).</summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Length in characters of the extracted text. Permitted because it is a
    /// scalar metric and carries no content. <c>0</c> for files where text
    /// extraction failed or is not applicable.
    /// </summary>
    public required int TextLength { get; init; }
}

/// <summary>
/// Result of <see cref="IIntentRerankerService.RerankAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>FR-48 invariant</b>: this record intentionally carries no
/// <c>AutoExecute</c> flag. The shape itself enforces the no-auto-execute
/// rule. The reranked candidates flow to the SSE <c>playbook_options</c>
/// event (task 117a) and the user clicks to invoke.
/// </para>
/// </remarks>
/// <param name="Top3">
/// Reranked top-3 candidates with per-candidate LLM reason text. Ordered
/// in the order the LLM returned them. May contain fewer than 3 entries
/// when the LLM returns a shorter list, when the input top-5 itself
/// contained fewer than 3, or when the input was empty.
/// </param>
/// <param name="RerankInvoked">
/// Always <c>true</c> when this service is reached — the service is the
/// rerank step itself, so a returned result means the rerank pathway was
/// exercised. Distinguishing graceful-degrade from happy-path is the role
/// of <paramref name="Reason"/>, not this flag.
/// </param>
/// <param name="Reason">
/// Telemetry tag explaining the outcome — one of:
/// <list type="bullet">
///   <item><description><c>"llm-rerank-from-5"</c> — happy path: LLM returned a valid top-3.</description></item>
///   <item><description><c>"llm-rerank-truncated"</c> — LLM returned more than 3 entries; truncated to top-3.</description></item>
///   <item><description><c>"llm-rerank-partial"</c> — LLM returned fewer than 3 entries; result reflects what the LLM returned.</description></item>
///   <item><description><c>"timeout-graceful-degrade"</c> — LLM exceeded the FR-46 budget; fell back to top-3-by-confidence from the input.</description></item>
///   <item><description><c>"llm-error-graceful-degrade"</c> — LLM threw a non-cancellation error; fell back to top-3-by-confidence from the input.</description></item>
///   <item><description><c>"parse-error-graceful-degrade"</c> — LLM response failed JSON parse / schema; fell back to top-3-by-confidence from the input.</description></item>
///   <item><description><c>"no-input-candidates"</c> — input top-5 was empty; returned empty top-3 with no LLM call.</description></item>
/// </list>
/// </param>
/// <param name="LatencyMs">Wall-clock latency of the rerank call. Useful telemetry signal for FR-46 budget verification.</param>
public sealed record IntentRerankerResult(
    IReadOnlyList<RankedPlaybookCandidate> Top3,
    bool RerankInvoked,
    string Reason,
    TimeSpan LatencyMs);

/// <summary>
/// A reranked candidate carrying the underlying
/// <see cref="PlaybookCandidate"/> plus the per-candidate
/// <see cref="RerankReason"/> text returned by the LLM. Wrapping
/// (rather than mutating) keeps the upstream
/// <see cref="PlaybookCandidate"/> shape stable across the project's
/// shared contract surface.
/// </summary>
/// <param name="Candidate">The underlying candidate from the input top-5.</param>
/// <param name="RerankReason">
/// Short LLM-generated reason ("why this candidate"). Truncated to a
/// reasonable length by the service. May be empty on graceful-degrade
/// paths where no LLM reason is available.
/// </param>
public sealed record RankedPlaybookCandidate(
    PlaybookCandidate Candidate,
    string RerankReason);
