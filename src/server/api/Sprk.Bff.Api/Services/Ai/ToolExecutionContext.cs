namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Execution context provided to tool handlers during analysis.
/// Contains all the runtime information needed for tool execution.
/// </summary>
/// <remarks>
/// The execution context is created by AnalysisOrchestrationService and passed
/// to each tool handler. It provides:
/// <list type="bullet">
/// <item>Document content (extracted text)</item>
/// <item>Analysis session state</item>
/// <item>Tenant isolation information</item>
/// <item>Service dependencies (logger, cache, etc.)</item>
/// </list>
/// </remarks>
public record ToolExecutionContext
{
    /// <summary>
    /// Unique identifier for this analysis session.
    /// Used for correlation in logs and caching.
    /// </summary>
    public required Guid AnalysisId { get; init; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// All operations must be scoped to this tenant.
    /// </summary>
    public required string TenantId { get; init; }

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
    /// User-provided context or instructions for this analysis.
    /// May include specific questions or focus areas.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Knowledge context from resolved scopes (RAG results, inline content).
    /// Pre-resolved by AnalysisOrchestrationService.
    /// </summary>
    public string? KnowledgeContext { get; init; }

    /// <summary>
    /// Maximum tokens to use for AI model calls.
    /// Handlers should respect this limit.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature setting for AI model calls (0.0 - 1.0).
    /// Lower values are more deterministic.
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// AI model deployment ID override for this execution.
    /// If null, uses the default model deployment.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Timestamp when this execution context was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
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
