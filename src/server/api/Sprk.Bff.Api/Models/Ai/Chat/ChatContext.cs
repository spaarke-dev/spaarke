namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Knowledge scope resolved from the playbook's N:N relationships.
/// Carries RAG source IDs, inline content, skill instructions, and active document ID
/// through the agent creation pipeline to tool classes.
/// </summary>
/// <param name="RagKnowledgeSourceIds">
/// IDs of knowledge sources with <see cref="Sprk.Bff.Api.Services.Ai.KnowledgeType.RagIndex"/> type.
/// Used by search tools to scope queries to the playbook's knowledge base via OData filter.
/// </param>
/// <param name="InlineContent">
/// Concatenated content from knowledge sources with <see cref="Sprk.Bff.Api.Services.Ai.KnowledgeType.Inline"/> type.
/// Injected directly into the system prompt under "## Reference Materials".
/// </param>
/// <param name="SkillInstructions">
/// Concatenated PromptFragment values from the playbook's Skills.
/// Injected into the system prompt under "## Specialized Instructions".
/// </param>
/// <param name="ActiveDocumentId">
/// The document ID for the current chat session's active document.
/// </param>
public record ChatKnowledgeScope(
    IReadOnlyList<string> RagKnowledgeSourceIds,
    string? InlineContent,
    string? SkillInstructions,
    string? ActiveDocumentId,
    string? ParentEntityType = null,
    string? ParentEntityId = null);

/// <summary>
/// Context object provided to SprkChatAgent containing system prompt, document summary,
/// analysis metadata, the playbook ID, and knowledge scope.
/// </summary>
/// <param name="SystemPrompt">
/// Full system prompt composed from the playbook's Action record (ACT-* in Dataverse),
/// enriched with inline knowledge and skill instructions from the playbook's scopes.
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
/// <param name="KnowledgeScope">
/// Knowledge scope resolved from the playbook's skills and knowledge source relationships.
/// Null when the playbook has no knowledge or skill scopes configured.
/// </param>
public record ChatContext(
    string SystemPrompt,
    string? DocumentSummary,
    IReadOnlyDictionary<string, string>? AnalysisMetadata,
    Guid PlaybookId,
    ChatKnowledgeScope? KnowledgeScope = null);
