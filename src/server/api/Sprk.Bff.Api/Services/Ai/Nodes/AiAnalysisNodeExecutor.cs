using Microsoft.Extensions.DependencyInjection;
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
/// <para>
/// Registered as Singleton (required by NodeExecutorRegistry). Uses IServiceProvider
/// to resolve IToolHandlerRegistry per execution since the registry is Scoped.
/// </para>
/// </remarks>
public sealed class AiAnalysisNodeExecutor : INodeExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiAnalysisNodeExecutor> _logger;

    public AiAnalysisNodeExecutor(
        IServiceProvider serviceProvider,
        ILogger<AiAnalysisNodeExecutor> logger)
    {
        _serviceProvider = serviceProvider;
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
                // Handler must exist in registry (resolved per-call since IToolHandlerRegistry is Scoped)
                using var scope = _serviceProvider.CreateScope();
                var toolHandlerRegistry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();
                var handler = toolHandlerRegistry.GetHandler(context.Tool.HandlerClass);
                if (handler is null)
                {
                    var availableHandlers = toolHandlerRegistry.GetRegisteredHandlerIds();
                    errors.Add(
                        $"Tool handler '{context.Tool.HandlerClass}' is not registered. " +
                        $"Available handlers: [{string.Join(", ", availableHandlers)}]");
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

            // Resolve IToolHandlerRegistry from scope (Scoped service, executor is Singleton)
            using var scope = _serviceProvider.CreateScope();
            var toolHandlerRegistry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

            // Get the tool handler
            var tool = context.Tool!;
            var handler = toolHandlerRegistry.GetHandler(tool.HandlerClass!);
            if (handler is null)
            {
                var availableHandlers = toolHandlerRegistry.GetRegisteredHandlerIds();
                _logger.LogWarning(
                    "Tool handler '{HandlerClass}' not found for tool '{ToolName}'. " +
                    "Available handlers: [{AvailableHandlers}]",
                    tool.HandlerClass, tool.Name, string.Join(", ", availableHandlers));

                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"Tool handler '{tool.HandlerClass}' not found. Available handlers: [{string.Join(", ", availableHandlers)}]",
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

            // Execute the tool handler — streaming or blocking path
            _logger.LogDebug(
                "Calling tool handler {HandlerId} for node {NodeId}",
                handler.HandlerId,
                context.Node.Id);

            ToolResult toolResult;

            // Per-token streaming path: use StreamExecuteAsync when the handler supports it
            // and the caller provided a token callback (ADR-014: do not cache tokens).
            if (handler is IStreamingAnalysisToolHandler streamingHandler
                && context.OnTokenReceived != null)
            {
                _logger.LogDebug(
                    "Using streaming path for node {NodeId} with handler {HandlerId}",
                    context.Node.Id, handler.HandlerId);

                ToolResult? streamResult = null;

                await foreach (var evt in streamingHandler.StreamExecuteAsync(
                    toolContext, analysisTool, cancellationToken))
                {
                    if (evt is ToolStreamEvent.Token token)
                    {
                        // Forward immediately to SSE — no buffering per ADR-014
                        await context.OnTokenReceived(token.Text);
                    }
                    else if (evt is ToolStreamEvent.Completed completed)
                    {
                        streamResult = completed.Result;
                    }
                }

                toolResult = streamResult ?? ToolResult.Error(
                    handler.HandlerId,
                    analysisTool.Id,
                    analysisTool.Name,
                    "Streaming completed without a Completed event",
                    "STREAM_INCOMPLETE");
            }
            else
            {
                // Blocking path: handler does not support streaming or no callback provided
                toolResult = await handler.ExecuteAsync(
                    toolContext,
                    analysisTool,
                    cancellationToken);
            }

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
