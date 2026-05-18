using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Summary of a chat session produced by <see cref="ISessionSummarizationService"/> (AIPU2-032).
///
/// Written alongside the full verbatim message history in Cosmos DB — it is an additional
/// field on <see cref="StoredSession"/>, never a replacement for the messages.
///
/// The summary is used post-generation to reconstruct a compact in-memory context:
/// the session is trimmed to the summary + last 10 messages so subsequent LLM calls
/// stay within the context window budget.
///
/// Model: GPT-4o (not GPT-4o-mini) — legal context quality requires the full model
/// to preserve exact qualifications (ADR-013, AIPU2-032 acceptance criterion).
/// </summary>
/// <param name="NarrativeSummary">
/// Free-text paragraph summarising the conversation. Preserves key legal qualifications,
/// document references, decisions made, and entity context (matter name, parties, dates).
/// </param>
/// <param name="KeyConclusions">
/// Structured list of discrete legal conclusions extracted from the conversation.
/// Each item captures a topic, conclusion text, confidence, and optional source reference.
/// </param>
/// <param name="OriginalMessageCount">
/// Number of messages in the session at the time summarization was triggered.
/// Used for audit and to verify that no messages were lost.
/// </param>
/// <param name="SummarizedAt">UTC timestamp when summarization completed.</param>
/// <param name="ModelUsed">
/// Azure OpenAI deployment name used for summarization. Always "gpt-4o" (AIPU2-032 criterion).
/// </param>
public record SessionSummary(
    [property: JsonPropertyName("narrativeSummary")] string NarrativeSummary,
    [property: JsonPropertyName("keyConclusions")] List<KeyConclusion> KeyConclusions,
    [property: JsonPropertyName("originalMessageCount")] int OriginalMessageCount,
    [property: JsonPropertyName("summarizedAt")] DateTimeOffset SummarizedAt,
    [property: JsonPropertyName("modelUsed")] string ModelUsed);
