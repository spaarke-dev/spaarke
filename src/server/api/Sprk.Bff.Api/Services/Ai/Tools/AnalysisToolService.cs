using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Service for executing analysis tools.
/// Routes tool requests to appropriate IAnalysisToolHandler implementations.
/// Follows ADR-001 (BFF orchestration) and ADR-010 (DI minimalism).
/// </summary>
public interface IAnalysisToolService
{
    /// <summary>
    /// Execute a tool with the given context.
    /// </summary>
    /// <param name="tool">Tool definition from Dataverse.</param>
    /// <param name="documentContent">Extracted document text.</param>
    /// <param name="documentId">Document ID from SPE.</param>
    /// <param name="fileName">Optional filename for context.</param>
    /// <param name="mimeType">Optional MIME type.</param>
    /// <param name="analysisId">Optional analysis ID for tracking.</param>
    /// <param name="additionalContext">Optional context from parent analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool execution result.</returns>
    Task<AnalysisToolResult> ExecuteToolAsync(
        AnalysisTool tool,
        string documentContent,
        string documentId,
        string? fileName = null,
        string? mimeType = null,
        Guid? analysisId = null,
        string? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute multiple tools in sequence.
    /// </summary>
    /// <param name="tools">Tools to execute in order.</param>
    /// <param name="documentContent">Extracted document text.</param>
    /// <param name="documentId">Document ID from SPE.</param>
    /// <param name="fileName">Optional filename for context.</param>
    /// <param name="mimeType">Optional MIME type.</param>
    /// <param name="analysisId">Optional analysis ID for tracking.</param>
    /// <param name="additionalContext">Optional context from parent analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each tool execution.</returns>
    Task<IReadOnlyList<ToolExecutionSummary>> ExecuteToolsAsync(
        IEnumerable<AnalysisTool> tools,
        string documentContent,
        string documentId,
        string? fileName = null,
        string? mimeType = null,
        Guid? analysisId = null,
        string? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all registered tool handlers.
    /// </summary>
    IReadOnlyList<(ToolType Type, string DisplayName, string? HandlerClassName)> GetRegisteredHandlers();
}

/// <summary>
/// Summary of a single tool execution within a batch.
/// </summary>
public record ToolExecutionSummary
{
    public required Guid ToolId { get; init; }
    public required string ToolName { get; init; }
    public required ToolType ToolType { get; init; }
    public required AnalysisToolResult Result { get; init; }
}

/// <summary>
/// Implementation of IAnalysisToolService using registered IAnalysisToolHandler implementations.
/// </summary>
public class AnalysisToolService : IAnalysisToolService
{
    private readonly ILogger<AnalysisToolService> _logger;
    private readonly Dictionary<ToolType, IAnalysisToolHandler> _handlersByType;
    private readonly Dictionary<string, IAnalysisToolHandler> _handlersByClassName;
    private readonly List<IAnalysisToolHandler> _allHandlers;

    public AnalysisToolService(
        IEnumerable<IAnalysisToolHandler> handlers,
        ILogger<AnalysisToolService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _allHandlers = handlers?.ToList() ?? throw new ArgumentNullException(nameof(handlers));

        // Build lookup dictionaries for fast routing
        _handlersByType = new Dictionary<ToolType, IAnalysisToolHandler>();
        _handlersByClassName = new Dictionary<string, IAnalysisToolHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in _allHandlers)
        {
            // Register by ToolType (first handler wins for built-in types)
            if (handler.ToolType != ToolType.Custom && !_handlersByType.ContainsKey(handler.ToolType))
            {
                _handlersByType[handler.ToolType] = handler;
                _logger.LogDebug(
                    "Registered tool handler {DisplayName} for ToolType {ToolType}",
                    handler.DisplayName, handler.ToolType);
            }

            // Register by class name for Custom tools
            if (!string.IsNullOrEmpty(handler.HandlerClassName))
            {
                _handlersByClassName[handler.HandlerClassName] = handler;
                _logger.LogDebug(
                    "Registered custom tool handler {DisplayName} with class name {ClassName}",
                    handler.DisplayName, handler.HandlerClassName);
            }
        }

        _logger.LogInformation(
            "AnalysisToolService initialized with {HandlerCount} handlers ({BuiltInCount} built-in, {CustomCount} custom)",
            _allHandlers.Count,
            _handlersByType.Count,
            _handlersByClassName.Count);
    }

    public async Task<AnalysisToolResult> ExecuteToolAsync(
        AnalysisTool tool,
        string documentContent,
        string documentId,
        string? fileName = null,
        string? mimeType = null,
        Guid? analysisId = null,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrEmpty(documentContent);
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Executing tool {ToolName} (ID: {ToolId}, Type: {ToolType}) for document {DocumentId}",
            tool.Name, tool.Id, tool.Type, documentId);

        try
        {
            // Find the appropriate handler
            var handler = FindHandler(tool);
            if (handler == null)
            {
                var error = $"No handler found for tool type {tool.Type}" +
                    (tool.Type == ToolType.Custom ? $" with class '{tool.HandlerClass}'" : "");

                _logger.LogError(
                    "No handler found for tool {ToolName} (Type: {ToolType}, HandlerClass: {HandlerClass}). " +
                    "Available handlers: {AvailableHandlers}",
                    tool.Name, tool.Type, tool.HandlerClass,
                    string.Join(", ", _allHandlers.Select(h => $"{h.DisplayName}[{h.ToolType}]")));

                return AnalysisToolResult.Fail(error, durationMs: stopwatch.ElapsedMilliseconds);
            }

            // Validate configuration before execution
            var validation = handler.ValidateConfiguration(tool.Configuration);
            if (!validation.IsValid)
            {
                var error = $"Tool configuration invalid: {string.Join("; ", validation.Errors)}";
                _logger.LogWarning(
                    "Tool {ToolName} configuration validation failed: {Errors}",
                    tool.Name, string.Join("; ", validation.Errors));

                return AnalysisToolResult.Fail(error, durationMs: stopwatch.ElapsedMilliseconds);
            }

            // Parse configuration JSON
            Dictionary<string, object>? parsedConfig = null;
            if (!string.IsNullOrEmpty(tool.Configuration))
            {
                try
                {
                    parsedConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        tool.Configuration,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to parse tool configuration JSON for {ToolName}, proceeding without parsed config",
                        tool.Name);
                }
            }

            // Build context
            var context = new AnalysisToolContext
            {
                Tool = tool,
                DocumentContent = documentContent,
                DocumentId = documentId,
                FileName = fileName,
                MimeType = mimeType,
                AnalysisId = analysisId,
                AdditionalContext = additionalContext,
                ParsedConfiguration = parsedConfig
            };

            // Execute
            var result = await handler.ExecuteAsync(context, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Tool {ToolName} {Status} in {DurationMs}ms{TokenInfo}",
                tool.Name,
                result.Success ? "completed successfully" : "failed",
                stopwatch.ElapsedMilliseconds,
                result.Tokens != null ? $" (tokens: {result.Tokens.TotalTokens})" : "");

            // Ensure duration is set
            if (result.DurationMs == 0)
            {
                return result with { DurationMs = stopwatch.ElapsedMilliseconds };
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Tool {ToolName} execution cancelled after {DurationMs}ms",
                tool.Name, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Tool {ToolName} execution failed with exception after {DurationMs}ms: {Error}",
                tool.Name, stopwatch.ElapsedMilliseconds, ex.Message);

            return AnalysisToolResult.Fail(
                $"Tool execution failed: {ex.Message}",
                ex.ToString(),
                stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<ToolExecutionSummary>> ExecuteToolsAsync(
        IEnumerable<AnalysisTool> tools,
        string documentContent,
        string documentId,
        string? fileName = null,
        string? mimeType = null,
        Guid? analysisId = null,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var toolList = tools?.ToList() ?? throw new ArgumentNullException(nameof(tools));
        var results = new List<ToolExecutionSummary>(toolList.Count);

        _logger.LogInformation(
            "Executing {ToolCount} tools for document {DocumentId}",
            toolList.Count, documentId);

        foreach (var tool in toolList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteToolAsync(
                tool,
                documentContent,
                documentId,
                fileName,
                mimeType,
                analysisId,
                additionalContext,
                cancellationToken);

            results.Add(new ToolExecutionSummary
            {
                ToolId = tool.Id,
                ToolName = tool.Name,
                ToolType = tool.Type,
                Result = result
            });
        }

        var successCount = results.Count(r => r.Result.Success);
        _logger.LogInformation(
            "Completed {ToolCount} tools for document {DocumentId}: {SuccessCount} succeeded, {FailCount} failed",
            toolList.Count, documentId, successCount, toolList.Count - successCount);

        return results;
    }

    public IReadOnlyList<(ToolType Type, string DisplayName, string? HandlerClassName)> GetRegisteredHandlers()
    {
        return _allHandlers
            .Select(h => (h.ToolType, h.DisplayName, h.HandlerClassName))
            .ToList();
    }

    private IAnalysisToolHandler? FindHandler(AnalysisTool tool)
    {
        // For Custom tools, look up by handler class name
        if (tool.Type == ToolType.Custom)
        {
            if (string.IsNullOrEmpty(tool.HandlerClass))
            {
                _logger.LogWarning(
                    "Custom tool {ToolName} has no HandlerClass specified",
                    tool.Name);
                return null;
            }

            if (_handlersByClassName.TryGetValue(tool.HandlerClass, out var customHandler))
            {
                return customHandler;
            }

            _logger.LogWarning(
                "Custom tool {ToolName} handler class '{HandlerClass}' not found",
                tool.Name, tool.HandlerClass);
            return null;
        }

        // For built-in tool types, look up by ToolType
        if (_handlersByType.TryGetValue(tool.Type, out var handler))
        {
            return handler;
        }

        return null;
    }
}
