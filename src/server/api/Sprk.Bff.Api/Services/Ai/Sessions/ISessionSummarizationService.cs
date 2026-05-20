namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Service that summarizes AI chat sessions when they grow beyond context window thresholds.
///
/// Summarization triggers when a session reaches <see cref="MessageThreshold"/> messages
/// OR <see cref="TokenThreshold"/> estimated tokens — whichever comes first (AIPU2-032).
///
/// The summary is written to Cosmos DB alongside the full verbatim message history
/// (messages are never deleted). After writing the summary, the in-memory session is
/// trimmed to the last 10 messages so subsequent LLM calls stay within context limits.
///
/// Model: GPT-4o (not GPT-4o-mini) — legal context requires the full model to preserve
/// exact legal qualifications such as "except in cases of gross negligence" (ADR-013).
///
/// Lifetime: Scoped — one instance per HTTP request.
/// </summary>
public interface ISessionSummarizationService
{
    /// <summary>
    /// Number of messages in the session that triggers summarization.
    /// Summarization fires when message count reaches or exceeds this threshold.
    /// </summary>
    const int MessageThreshold = 25;

    /// <summary>
    /// Estimated token count that triggers summarization.
    /// Tokens are approximated as (total character count of all message content) / 4.
    /// Summarization fires when the estimate reaches or exceeds this threshold.
    /// </summary>
    const int TokenThreshold = 8000;

    /// <summary>
    /// Returns true if the session has grown large enough to require summarization.
    ///
    /// Checks two independent thresholds (either is sufficient to trigger):
    ///   1. Message count >= <see cref="MessageThreshold"/> (25 messages)
    ///   2. Estimated token count >= <see cref="TokenThreshold"/> (8,000 tokens)
    ///      where estimated tokens = total content character count / 4.
    /// </summary>
    /// <param name="messages">All messages currently in the session.</param>
    /// <returns><c>true</c> if summarization should be triggered; <c>false</c> otherwise.</returns>
    bool ShouldSummarize(IReadOnlyList<SessionMessage> messages);

    /// <summary>
    /// Calls GPT-4o to produce a <see cref="SessionSummary"/> from the provided messages.
    ///
    /// The summary contains:
    ///   - <see cref="SessionSummary.NarrativeSummary"/> — free-text paragraph preserving
    ///     legal qualifications, document references, decisions, and entity context.
    ///   - <see cref="SessionSummary.KeyConclusions"/> — structured list of discrete legal
    ///     conclusions with topic, confidence, and optional source reference.
    ///
    /// Throws on model call failure — callers must handle exceptions and ensure
    /// that a failure here does not block the streaming response.
    /// </summary>
    /// <param name="messages">Messages to summarize. Typically the full session history.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="SessionSummary"/> record.</returns>
    Task<SessionSummary> SummarizeAsync(IReadOnlyList<SessionMessage> messages, CancellationToken ct = default);
}
