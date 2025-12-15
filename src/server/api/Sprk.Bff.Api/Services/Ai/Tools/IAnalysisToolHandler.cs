// ToolType and AnalysisTool are defined in IScopeResolverService.cs
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Interface for analysis tool handlers.
/// Each tool (EntityExtractor, ClauseAnalyzer, DocumentClassifier) implements this
/// to process specific analysis operations.
///
/// Tools are discovered by ToolType enum and executed by the AnalysisToolService.
/// Follows ADR-001 (BFF orchestration) and ADR-010 (DI minimalism).
/// </summary>
public interface IAnalysisToolHandler
{
    /// <summary>
    /// The tool type this handler processes.
    /// Maps to <see cref="ToolType"/> enum from IScopeResolverService.
    /// </summary>
    ToolType ToolType { get; }

    /// <summary>
    /// Handler class name for Custom tools. Used for routing when ToolType is Custom.
    /// Should match the fully-qualified class name stored in sprk_analysistool.HandlerClass.
    /// </summary>
    string? HandlerClassName { get; }

    /// <summary>
    /// Display name for logging and UI.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Execute the tool with the given context.
    /// Implementations should be idempotent for safe retry.
    /// </summary>
    /// <param name="context">Execution context with document content and configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool execution result.</returns>
    Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate tool configuration before execution.
    /// </summary>
    /// <param name="configuration">JSON configuration string from sprk_analysistool.</param>
    /// <returns>Validation result with any errors.</returns>
    ToolValidationResult ValidateConfiguration(string? configuration);
}

/// <summary>
/// Context for tool execution containing all required inputs.
/// </summary>
public record AnalysisToolContext
{
    /// <summary>
    /// The tool definition from Dataverse.
    /// </summary>
    public required AnalysisTool Tool { get; init; }

    /// <summary>
    /// Document content to process (extracted text).
    /// </summary>
    public required string DocumentContent { get; init; }

    /// <summary>
    /// Original document ID from SPE.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Document filename for context.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Document MIME type.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Analysis ID this tool execution is part of.
    /// </summary>
    public Guid? AnalysisId { get; init; }

    /// <summary>
    /// Additional context from parent analysis (e.g., action prompt, skills).
    /// </summary>
    public string? AdditionalContext { get; init; }

    /// <summary>
    /// Parsed configuration from Tool.Configuration JSON.
    /// </summary>
    public Dictionary<string, object>? ParsedConfiguration { get; init; }
}

/// <summary>
/// Result of tool execution.
/// </summary>
public record AnalysisToolResult
{
    /// <summary>
    /// Whether execution succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Tool output data (JSON-serializable).
    /// Structure depends on tool type.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Human-readable summary for UI display.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Markdown-formatted output for inclusion in analysis document.
    /// </summary>
    public string? MarkdownOutput { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Exception details for logging (not exposed to UI).
    /// </summary>
    public string? ExceptionDetails { get; init; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Token usage if AI model was called.
    /// </summary>
    public TokenUsage? Tokens { get; init; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static AnalysisToolResult Ok(object? data, string? summary = null, string? markdown = null, long durationMs = 0, TokenUsage? tokens = null)
        => new()
        {
            Success = true,
            Data = data,
            Summary = summary,
            MarkdownOutput = markdown,
            DurationMs = durationMs,
            Tokens = tokens
        };

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static AnalysisToolResult Fail(string errorMessage, string? exceptionDetails = null, long durationMs = 0)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            ExceptionDetails = exceptionDetails,
            DurationMs = durationMs
        };
}

/// <summary>
/// Token usage tracking for AI tool calls.
/// </summary>
public record TokenUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// Configuration validation result.
/// </summary>
public record ToolValidationResult
{
    /// <summary>
    /// Whether configuration is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Validation errors if invalid.
    /// </summary>
    public string[] Errors { get; init; } = [];

    /// <summary>
    /// Create a valid result.
    /// </summary>
    public static ToolValidationResult Valid() => new() { IsValid = true };

    /// <summary>
    /// Create an invalid result with errors.
    /// </summary>
    public static ToolValidationResult Invalid(params string[] errors)
        => new() { IsValid = false, Errors = errors };
}
