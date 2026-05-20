namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Input to the Prompt Shields scan.
///
/// ADR-015: The text fields in this record are sent to the Azure AI Content Safety API.
/// They MUST NOT be logged — only counts and identifiers are permitted in Tier 1 app logs.
/// </summary>
/// <param name="UserMessage">
/// The user's chat turn text. Required. Scanned for direct jailbreak attempts
/// (userPromptAttack class in the Prompt Shields API response).
/// </param>
/// <param name="Documents">
/// Passages retrieved from the RAG knowledge index to be injected into the LLM context.
/// Each entry is one document chunk (plain text, no metadata). Optional — pass an empty list
/// or null when no retrieval has occurred. Scanned for indirect injection attacks
/// (documentAttack class in the Prompt Shields API response).
/// </param>
public sealed record PromptShieldRequest(
    string UserMessage,
    IReadOnlyList<string>? Documents = null);
