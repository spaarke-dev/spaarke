using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for AI analysis actions that delegate to existing tool handlers.
/// Bridges the node-based orchestration system to the IAnalysisToolHandler pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This executor reuses the existing tool handler infrastructure per ADR-013
/// "reuse existing tool handler infrastructure". It:
/// </para>
/// <list type="bullet">
/// <item>Converts NodeExecutionContext to ToolExecutionContext</item>
/// <item>Looks up the appropriate handler from IToolHandlerRegistry</item>
/// <item>Executes the handler and converts ToolResult to NodeOutput</item>
/// <item>Tracks metrics (tokens, duration) from the tool execution</item>
/// </list>
/// </remarks>
public sealed class AiAnalysisNodeExecutor : INodeExecutor
{
    private readonly IToolHandlerRegistry _toolHandlerRegistry;
    private readonly ILogger<AiAnalysisNodeExecutor> _logger;

    public AiAnalysisNodeExecutor(
        IToolHandlerRegistry toolHandlerRegistry,
        ILogger<AiAnalysisNodeExecutor> logger)
    {
        _toolHandlerRegistry = toolHandlerRegistry;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.AiAnalysis
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        // AI analysis requires a tool
        if (context.Tool is null)
        {
            errors.Add("AI analysis node requires a tool to be configured");
        }
        else
        {
            // Tool must have a handler class
            if (string.IsNullOrWhiteSpace(context.Tool.HandlerClass))
            {
                errors.Add($"Tool '{context.Tool.Name}' does not have a handler class configured");
            }
            else
            {
                // Handler must exist in registry
                var handler = _toolHandlerRegistry.GetHandler(context.Tool.HandlerClass);
                if (handler is null)
                {
                    errors.Add($"Tool handler '{context.Tool.HandlerClass}' is not registered");
                }
            }
        }

        // Document context required for analysis
        if (context.Document is null)
        {
            errors.Add("AI analysis node requires document context");
        }
        else if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
        {
            errors.Add("Document has no extracted text for analysis");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing AI analysis node {NodeId} ({NodeName}) with tool {ToolName}",
            context.Node.Id,
            context.Node.Name,
            context.Tool?.Name ?? "none");

        try
        {
            // Validate first
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Get the tool handler
            var tool = context.Tool!;
            var handler = _toolHandlerRegistry.GetHandler(tool.HandlerClass!);
            if (handler is null)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"Tool handler '{tool.HandlerClass}' not found",
                    NodeErrorCodes.ToolHandlerNotFound,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Convert to tool execution context
            var toolContext = CreateToolExecutionContext(context);

            // Convert AnalysisTool to the handler's expected format
            var analysisTool = tool;

            // Validate with handler
            var toolValidation = handler.Validate(toolContext, analysisTool);
            if (!toolValidation.IsValid)
            {
                _logger.LogWarning(
                    "Tool handler validation failed for node {NodeId}: {Errors}",
                    context.Node.Id,
                    string.Join(", ", toolValidation.Errors));

                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", toolValidation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Execute the tool handler
            _logger.LogDebug(
                "Calling tool handler {HandlerId} for node {NodeId}",
                handler.HandlerId,
                context.Node.Id);

            var toolResult = await handler.ExecuteAsync(
                toolContext,
                analysisTool,
                cancellationToken);

            // Convert tool result to node output
            var nodeOutput = ConvertToNodeOutput(context, toolResult, startedAt);

            _logger.LogDebug(
                "AI analysis node {NodeId} completed successfully. " +
                "Tokens: {TokensIn}/{TokensOut}, Duration: {Duration}ms",
                context.Node.Id,
                nodeOutput.Metrics.TokensIn,
                nodeOutput.Metrics.TokensOut,
                nodeOutput.Metrics.DurationMs);

            return nodeOutput;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AI analysis node {NodeId} was cancelled",
                context.Node.Id);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI analysis node {NodeId} failed with error: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Internal error: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Creates a ToolExecutionContext from the NodeExecutionContext.
    /// </summary>
    private ToolExecutionContext CreateToolExecutionContext(NodeExecutionContext context)
    {
        // Build previous results dictionary from node outputs
        var previousResults = new Dictionary<string, ToolResult>();
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            // If the previous node had tool results, use the first one
            var firstToolResult = output.ToolResults.FirstOrDefault();
            if (firstToolResult is not null)
            {
                previousResults[varName] = firstToolResult;
            }
        }

        // Build knowledge context from resolved scopes
        var knowledgeContext = BuildKnowledgeContext(context.Scopes);

        return new ToolExecutionContext
        {
            AnalysisId = context.RunId,
            TenantId = context.TenantId,
            Document = context.Document!,
            PreviousResults = previousResults,
            UserContext = context.UserContext,
            KnowledgeContext = knowledgeContext,
            MaxTokens = context.MaxTokens,
            Temperature = context.Temperature,
            ModelDeploymentId = context.ModelDeploymentId ?? context.Node.ModelDeploymentId,
            CorrelationId = context.CorrelationId,
            CreatedAt = context.CreatedAt
        };
    }

    /// <summary>
    /// Builds knowledge context string from resolved scopes.
    /// </summary>
    private static string? BuildKnowledgeContext(ResolvedScopes scopes)
    {
        if (scopes.Knowledge.Length == 0)
            return null;

        var contextParts = new List<string>();

        foreach (var knowledge in scopes.Knowledge)
        {
            if (knowledge.Type == KnowledgeType.Inline && !string.IsNullOrWhiteSpace(knowledge.Content))
            {
                contextParts.Add($"[{knowledge.Name}]\n{knowledge.Content}");
            }
            // TODO: Document and RagIndex types will be resolved in PlaybookOrchestrationService
            // and pre-populated into knowledge.Content before reaching the executor
        }

        return contextParts.Count > 0
            ? string.Join("\n\n", contextParts)
            : null;
    }

    /// <summary>
    /// Converts a ToolResult to NodeOutput.
    /// </summary>
    private static NodeOutput ConvertToNodeOutput(
        NodeExecutionContext context,
        ToolResult toolResult,
        DateTimeOffset startedAt)
    {
        if (!toolResult.Success)
        {
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                toolResult.ErrorMessage ?? "Tool execution failed",
                toolResult.ErrorCode ?? NodeErrorCodes.InternalError,
                NodeExecutionMetrics.FromToolMetadata(toolResult.Execution));
        }

        return new NodeOutput
        {
            NodeId = context.Node.Id,
            OutputVariable = context.Node.OutputVariable,
            Success = true,
            TextContent = toolResult.Summary,
            StructuredData = toolResult.Data,
            Confidence = toolResult.Confidence,
            Metrics = NodeExecutionMetrics.FromToolMetadata(toolResult.Execution),
            ToolResults = new[] { toolResult },
            Warnings = toolResult.Warnings
        };
    }
}
