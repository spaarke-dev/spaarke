using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 (memory infrastructure) — sliding-window compression contract.
///
/// When the chat sliding-conversation window exceeds the system-prompt token budget
/// (NFR-10: 8K total), the caller replaces the OLDEST <em>M</em> turns with the single
/// system-role summary <see cref="ChatMessage"/> returned by <see cref="CompressAsync"/>.
/// This is the foundation primitive for task 067 (hierarchical memory composition).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary</b>: this service is internal to the chat memory pipeline. CRUD-side code
/// MUST NOT depend on it (ADR-013 §A.4 — no public-contract facade required because the
/// service is consumed only by AI-internal callers). It lives under
/// <see cref="Sprk.Bff.Api.Services.Ai.Memory"/> per the Memory tree convention.
/// </para>
/// <para>
/// <b>ADR-015 invariant</b>: the input messages contain raw user content; the OUTPUT
/// returned by this service is the LLM-generated summary ONLY. Implementations MUST NOT
/// persist or echo raw user text in the returned <see cref="ChatMessage"/>; the
/// <see cref="ChatMessage.Content"/> field carries the model's compressed summary
/// (typically "Summary of earlier conversation: ...") wrapped as a System-role message.
/// </para>
/// <para>
/// <b>Soft-failure contract</b>: the service is permitted to return <c>null</c> when
/// the underlying LLM is unavailable (circuit broken), the kill switch is off, or the
/// input is empty / too small to compress. The chat agent factory (task 068) is the
/// integration call site and short-circuits to the raw sliding window in those cases.
/// </para>
/// </remarks>
public interface ISummarizationCompressionService
{
    /// <summary>
    /// Compress the oldest <em>M</em> messages into a single System-role summary
    /// <see cref="ChatMessage"/> bounded by <paramref name="maxSummaryTokens"/>.
    /// </summary>
    /// <param name="oldestMessages">
    /// The ordered subsequence (oldest-first) of chat messages to be folded into a summary.
    /// The caller is responsible for choosing which messages to compress (typically the
    /// oldest N messages after the system prompt). Empty input returns <c>null</c>.
    /// </param>
    /// <param name="maxSummaryTokens">
    /// Hard ceiling on the LLM output, in tokens. Passed as <c>maxOutputTokens</c> to
    /// the underlying LLM call. The returned message is also defensively truncated at
    /// this budget. Must be in the range [128, 1024]; values outside the range are
    /// clamped before the LLM call.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A single System-role <see cref="ChatMessage"/> with the compressed summary, or
    /// <c>null</c> if compression was skipped (kill switch off, empty input, LLM
    /// unavailable, etc.). Callers MUST treat <c>null</c> as "no compression — use the
    /// raw sliding window instead".
    /// </returns>
    Task<ChatMessage?> CompressAsync(
        IReadOnlyList<ChatMessage> oldestMessages,
        int maxSummaryTokens,
        CancellationToken cancellationToken = default);
}
