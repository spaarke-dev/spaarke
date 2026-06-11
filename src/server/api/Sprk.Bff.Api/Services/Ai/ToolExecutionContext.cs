using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Execution context provided to tool handlers during playbook-node analysis.
/// Contains all the runtime information needed for playbook-driven tool execution.
/// </summary>
/// <remarks>
/// <para>
/// The execution context is created by <c>AnalysisOrchestrationService</c> and passed
/// to each tool handler. It provides:
/// </para>
/// <list type="bullet">
/// <item>Document content (extracted text)</item>
/// <item>Analysis session state</item>
/// <item>Tenant isolation information</item>
/// <item>Service dependencies (logger, cache, etc.)</item>
/// </list>
/// <para>
/// In R6 Pillar 2 (task D-A-09), this type was refactored to derive from
/// <see cref="ToolInvocationContextBase"/>. The shared fields (TenantId, MaxTokens,
/// Temperature, ModelDeploymentId, UserContext, CorrelationId, CreatedAt, and a
/// correlation id) now live on the base; playbook-specific fields (Document,
/// PreviousResults, ActionSystemPrompt, scope context, DownstreamNodes, etc.) stay here.
/// A new sibling type, <see cref="ChatInvocationContext"/>, derives from the same base for
/// chat-driven invocation (see task D-A-10 adapter). Existing handlers continue to receive
/// <c>ToolExecutionContext</c> with all previous fields accessible by their original names.
/// </para>
/// <para>
/// Per ADR-013 / FR-09 / NFR-08: this refactor is purely internal to <c>Services/Ai/</c>.
/// No PublicContracts boundary changes; the 11 production node executors and the existing
/// 4 handlers see identical semantics.
/// </para>
/// </remarks>
public record ToolExecutionContext : ToolInvocationContextBase
{
    /// <summary>
    /// Unique identifier for this analysis session.
    /// Used for correlation in logs and caching. Backed by <see cref="ToolInvocationContextBase.InvocationId"/>
    /// so existing handler code that reads <c>context.AnalysisId</c> continues to compile unchanged.
    /// </summary>
    /// <remarks>
    /// Marked <c>required</c>: every <c>ToolExecutionContext</c> caller MUST provide
    /// <c>AnalysisId = ...</c> at object-initializer time, matching the pre-R6 contract.
    /// The init accessor writes to the base <see cref="ToolInvocationContextBase.InvocationId"/>
    /// storage so the base's correlation invariant is satisfied without a duplicate property.
    /// </remarks>
    public required Guid AnalysisId
    {
        get => InvocationId;
        init => InvocationId = value;
    }

    /// <summary>
    /// The document being analyzed.
    /// </summary>
    public required DocumentContext Document { get; init; }

    /// <summary>
    /// Previously extracted results from other tools in this analysis session.
    /// Allows tools to build on each other's output.
    /// </summary>
    public IReadOnlyDictionary<string, ToolResult> PreviousResults { get; init; }
        = new Dictionary<string, ToolResult>();

    /// <summary>
    /// System prompt from the Action record (sprk_analysisaction.sprk_systemprompt).
    /// When set, tool handlers should use this as the primary AI instruction instead of
    /// their internal default prompt templates. This implements "Action = what to do,
    /// Tool = how to do it" separation (Option A).
    /// </summary>
    public string? ActionSystemPrompt { get; init; }

    /// <summary>
    /// Skill context assembled from resolved skill scopes (prompt fragments).
    /// Each skill contributes additional instructions or focus areas.
    /// </summary>
    public string? SkillContext { get; init; }

    /// <summary>
    /// Knowledge context from resolved scopes (RAG results, inline content).
    /// Pre-resolved by AnalysisOrchestrationService.
    /// </summary>
    public string? KnowledgeContext { get; init; }

    /// <summary>
    /// Downstream node info for <c>$choices</c> resolution in JPS prompts.
    /// Contains output variable names and configuration JSON from nodes that
    /// consume this AI node's output (e.g., UpdateRecord nodes with fieldMappings).
    /// Null when downstream node info is not available or not applicable.
    /// </summary>
    public IReadOnlyList<DownstreamNodeInfo>? DownstreamNodes { get; init; }

    /// <summary>
    /// Resolved knowledge references from JPS $ref entries. Populated by AiAnalysisNodeExecutor.
    /// </summary>
    public IReadOnlyList<ResolvedKnowledgeRef>? AdditionalKnowledge { get; init; }

    /// <summary>
    /// Resolved skill references from JPS $ref entries. Populated by AiAnalysisNodeExecutor.
    /// </summary>
    public IReadOnlyList<ResolvedSkillRef>? AdditionalSkills { get; init; }

    /// <summary>
    /// Template parameters extracted from node ConfigJson for Handlebars substitution.
    /// </summary>
    public Dictionary<string, object?>? TemplateParameters { get; init; }

    /// <summary>
    /// Pre-resolved <c>$choices</c> values from Dataverse lookup entities.
    /// Keyed by the full <c>$choices</c> reference string (e.g., <c>"lookup:sprk_mattertype_ref.sprk_name"</c>),
    /// values are arrays of valid option strings.
    /// Resolved by <see cref="LookupChoicesResolver"/> before tool execution.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? PreResolvedLookupChoices { get; init; }
}

/// <summary>
/// Document information and content for tool execution.
/// </summary>
/// <remarks>
/// Contains both metadata and extracted text content.
/// File binary content is not included - text extraction happens before tool execution.
/// </remarks>
public record DocumentContext
{
    /// <summary>
    /// Document entity ID in Dataverse.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Document name/title.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Original file name with extension.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// File MIME type (e.g., "application/pdf", "text/plain").
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Extracted text content from the document.
    /// This is the primary input for most tool handlers.
    /// </summary>
    public required string ExtractedText { get; init; }

    /// <summary>
    /// Number of characters in extracted text.
    /// Useful for token estimation and chunking decisions.
    /// </summary>
    public int TextLength => ExtractedText?.Length ?? 0;

    /// <summary>
    /// Estimated token count (rough: ~4 chars per token).
    /// More accurate estimation may be provided by tokenizer.
    /// </summary>
    public int EstimatedTokens => (int)Math.Ceiling(TextLength / 4.0);

    /// <summary>
    /// Document metadata from Dataverse (custom fields, categories, etc.).
    /// Key-value pairs for additional context.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();
}
