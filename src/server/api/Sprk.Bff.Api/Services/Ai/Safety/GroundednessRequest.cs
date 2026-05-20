namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Input to the Groundedness Detection check.
///
/// ADR-015: The text fields in this record are sent to the Azure AI Content Safety API.
/// They MUST NOT be logged — only counts and identifiers are permitted in Tier 1 app logs.
/// </summary>
/// <param name="LlmResponse">
/// The full text of the AI-generated answer to check. Required.
/// This is the complete response produced by the LLM after streaming completes.
/// </param>
/// <param name="SourceDocuments">
/// The document passages retrieved from the RAG knowledge index that were injected into
/// the LLM's context for this turn. Each entry is one passage (plain text, no metadata).
/// When this list is empty the API call is skipped — there are no sources to check against,
/// so the result is assumed grounded.
/// </param>
/// <param name="Query">
/// The user's original question for the turn. Used as the QnA "query" field in the API
/// request body. When null or empty the API is called with an empty query string (the API
/// accepts this; grounding quality degrades slightly but the call still succeeds).
/// </param>
public sealed record GroundednessRequest(
    string LlmResponse,
    IReadOnlyList<string> SourceDocuments,
    string? Query = null);
