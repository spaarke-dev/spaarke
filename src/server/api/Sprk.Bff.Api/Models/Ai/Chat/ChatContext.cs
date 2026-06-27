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
/// <param name="AdditionalDocumentIds">
/// Optional list of additional document IDs (max 5) that the user has pinned to the
/// conversation for cross-referencing, comparison, or comprehensive analysis.
/// Content from these documents is fetched via SpeFileStore (ADR-007) and included
/// in the AI agent's context alongside the primary document. The cap of 5 prevents
/// context window and token budget explosion.
/// </param>
public record ChatKnowledgeScope(
    IReadOnlyList<string> RagKnowledgeSourceIds,
    string? InlineContent,
    string? SkillInstructions,
    string? ActiveDocumentId,
    string? ParentEntityType = null,
    string? ParentEntityId = null,
    IReadOnlyList<string>? AdditionalDocumentIds = null,
    string? ScopeSearchGuidance = null)
{
    /// <summary>
    /// Maximum number of additional documents that can be pinned to a conversation.
    /// Prevents context window and token budget explosion.
    /// </summary>
    public const int MaxAdditionalDocuments = 5;

    /// <summary>
    /// Validates that <see cref="AdditionalDocumentIds"/> does not exceed <see cref="MaxAdditionalDocuments"/>.
    /// </summary>
    /// <returns>True when the additional document count is within the allowed limit.</returns>
    public bool ValidateAdditionalDocuments() =>
        AdditionalDocumentIds is null || AdditionalDocumentIds.Count <= MaxAdditionalDocuments;
}

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
/// <param name="UploadedFiles">
/// Optional manifest of files the end user uploaded into the current chat session
/// (via <see cref="Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints"/> — R5 task 032 / 033).
/// Surfaced verbatim from <see cref="ChatSession.UploadedFiles"/> by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.PlaybookChatContextProvider"/> so the agent
/// (and downstream tool-call reasoning in
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SprkChatAgentFactory"/>) can see which files
/// exist in the session and decide whether to invoke
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.Tools.InvokeSummarizePlaybookTool"/>.
/// <para>
/// Carries manifest metadata only (fileId + fileName + MIME + size + index doc IDs +
/// uploaded-at). Extracted text content is NEVER carried here — ADR-015 (no-leakage)
/// and R5 task 033 acceptance both bind the system prompt to manifest-only surfacing.
/// </para>
/// <para>
/// Default <c>null</c> for backward compatibility — pre-R5 sessions and call sites that
/// omit the parameter are semantically equivalent to "no files uploaded into this session".
/// Treat <c>null</c> and an empty list identically (both mean "no manifest").
/// </para>
/// </param>
public record ChatContext(
    string SystemPrompt,
    string? DocumentSummary,
    IReadOnlyDictionary<string, string>? AnalysisMetadata,
    Guid? PlaybookId,
    ChatKnowledgeScope? KnowledgeScope = null,
    IReadOnlyList<ChatSessionFile>? UploadedFiles = null);
