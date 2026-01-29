namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for analysis tool handlers in the extensible AI tool framework.
/// Implementations process specific analysis tasks (entity extraction, clause analysis, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Tool handlers are loaded dynamically based on the HandlerClass property
/// in the AnalysisTool Dataverse entity. Each handler must:
/// </para>
/// <list type="bullet">
/// <item>Provide metadata describing its capabilities</item>
/// <item>Validate execution context and parameters</item>
/// <item>Execute asynchronously with cancellation support</item>
/// <item>Return standardized ToolResult</item>
/// </list>
/// <para>
/// See ADR-013 for AI architecture patterns.
/// </para>
/// </remarks>
public interface IAnalysisToolHandler
{
    /// <summary>
    /// Gets the unique identifier for this tool handler type.
    /// This should match the value stored in AnalysisTool.HandlerClass.
    /// </summary>
    /// <example>
    /// "EntityExtractorHandler", "ClauseAnalyzerHandler", "CustomToolHandler"
    /// </example>
    string HandlerId { get; }

    /// <summary>
    /// Gets the metadata describing this tool's capabilities.
    /// Used by the tool registry for discovery and documentation.
    /// </summary>
    ToolHandlerMetadata Metadata { get; }

    /// <summary>
    /// Gets the tool types this handler can process.
    /// Most handlers support a single type; custom handlers may support multiple.
    /// </summary>
    IReadOnlyList<ToolType> SupportedToolTypes { get; }

    /// <summary>
    /// Validates that the execution context and tool configuration are valid.
    /// Called before ExecuteAsync to fail fast on invalid inputs.
    /// </summary>
    /// <param name="context">The execution context containing document and analysis state.</param>
    /// <param name="tool">The tool configuration from Dataverse.</param>
    /// <returns>Validation result with success/failure and any error messages.</returns>
    ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool);

    /// <summary>
    /// Executes the tool analysis asynchronously.
    /// </summary>
    /// <param name="context">The execution context containing document text, analysis state, etc.</param>
    /// <param name="tool">The tool configuration from Dataverse including custom parameters.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Tool execution result containing extracted data, confidence, and any errors.</returns>
    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken);
}

/// <summary>
/// Metadata describing a tool handler's capabilities.
/// Used for tool registry, documentation, and UI display.
/// </summary>
/// <param name="Name">Human-readable name of the tool handler.</param>
/// <param name="Description">Description of what this tool handler does.</param>
/// <param name="Version">Handler version for compatibility tracking.</param>
/// <param name="SupportedInputTypes">Content types this handler can process (e.g., "text/plain", "application/pdf").</param>
/// <param name="Parameters">Parameter definitions for configuration.</param>
/// <param name="ConfigurationSchema">JSON Schema (Draft 07) for configuration validation. Null if no schema defined.</param>
public record ToolHandlerMetadata(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> SupportedInputTypes,
    IReadOnlyList<ToolParameterDefinition> Parameters,
    object? ConfigurationSchema = null);

/// <summary>
/// Defines a configuration parameter for a tool handler.
/// </summary>
/// <param name="Name">Parameter name (JSON property name in Configuration).</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Type">Parameter type (string, int, bool, array).</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="DefaultValue">Default value if not specified.</param>
public record ToolParameterDefinition(
    string Name,
    string Description,
    ToolParameterType Type,
    bool Required,
    object? DefaultValue = null);

/// <summary>
/// Parameter types for tool configuration.
/// </summary>
public enum ToolParameterType
{
    /// <summary>String value.</summary>
    String = 0,

    /// <summary>Integer value.</summary>
    Integer = 1,

    /// <summary>Boolean value.</summary>
    Boolean = 2,

    /// <summary>Decimal/float value.</summary>
    Decimal = 3,

    /// <summary>Array of values.</summary>
    Array = 4,

    /// <summary>JSON object.</summary>
    Object = 5
}

/// <summary>
/// Result of tool validation before execution.
/// </summary>
/// <param name="IsValid">Whether the context and tool configuration are valid.</param>
/// <param name="Errors">List of validation error messages if invalid.</param>
public record ToolValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ToolValidationResult Success() => new(true, Array.Empty<string>());

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    public static ToolValidationResult Failure(params string[] errors) => new(false, errors);

    /// <summary>
    /// Creates a failed validation result from a list of errors.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    public static ToolValidationResult Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}
