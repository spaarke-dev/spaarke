namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Context object provided to SprkChatAgent containing system prompt, document summary,
/// analysis metadata, and the playbook ID that governs the agent's behaviour.
/// </summary>
/// <param name="SystemPrompt">
/// Full system prompt composed from the playbook's Action record (ACT-* in Dataverse).
/// Injected as the first message in every chat completion request.
/// </param>
/// <param name="DocumentSummary">
/// Short summary of the current document (extracted text preview or analysis summary).
/// Appended to the system prompt so the agent has immediate document context.
/// </param>
/// <param name="AnalysisMetadata">
/// Optional metadata from a prior analysis run (e.g. document type, key entities).
/// May be null when no prior analysis exists.
/// </param>
/// <param name="PlaybookId">
/// Dataverse ID of the playbook that produced this context.
/// Used by SprkChatAgentFactory for context-switching between documents without
/// creating a new session (constraint from spec: "support context switching").
/// </param>
public record ChatContext(
    string SystemPrompt,
    string? DocumentSummary,
    IReadOnlyDictionary<string, string>? AnalysisMetadata,
    Guid PlaybookId);
