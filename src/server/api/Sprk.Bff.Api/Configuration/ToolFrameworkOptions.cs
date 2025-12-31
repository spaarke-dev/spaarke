namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for the AI Tool Framework.
/// Controls tool handler registration and behavior.
/// </summary>
public class ToolFrameworkOptions
{
    public const string SectionName = "ToolFramework";

    /// <summary>
    /// Master switch to enable/disable the tool framework.
    /// When false, no tool handlers are registered.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// List of handler IDs to disable.
    /// Handlers in this list are registered but not returned from GetHandler.
    /// Useful for temporarily disabling specific tools without code changes.
    /// Example: ["ClauseAnalyzerHandler", "CustomToolHandler"]
    /// </summary>
    public string[] DisabledHandlers { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Default timeout for tool execution in seconds.
    /// Individual tools may override this.
    /// Default: 60 seconds
    /// </summary>
    public int DefaultExecutionTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of tools that can be executed in parallel.
    /// Default: 3
    /// </summary>
    public int MaxParallelToolExecutions { get; set; } = 3;

    /// <summary>
    /// Enable detailed execution logging for debugging.
    /// When true, logs tool inputs/outputs at Debug level.
    /// Should be false in production.
    /// Default: false
    /// </summary>
    public bool VerboseLogging { get; set; }
}
