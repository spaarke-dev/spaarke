using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates playbook execution supporting both Legacy and NodeBased modes.
/// </summary>
/// <remarks>
/// <para>
/// This service:
/// </para>
/// <list type="bullet">
/// <item>Detects playbook mode (Legacy vs NodeBased) based on node presence</item>
/// <item>For Legacy: Delegates to existing <see cref="IAnalysisOrchestrationService"/></item>
/// <item>For NodeBased: Uses <see cref="ExecutionGraph"/> and <see cref="INodeExecutorRegistry"/></item>
/// </list>
/// <para>
/// Phase 1 limitations:
/// </para>
/// <list type="bullet">
/// <item>Sequential node execution only (parallel execution is Phase 3)</item>
/// <item>In-memory run tracking (Dataverse persistence is Phase 2)</item>
/// <item>Single document support (multi-document is Phase 2)</item>
/// </list>
/// </remarks>
public class PlaybookOrchestrationService : IPlaybookOrchestrationService
{
    private readonly INodeService _nodeService;
    private readonly INodeExecutorRegistry _executorRegistry;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IAnalysisOrchestrationService _legacyOrchestrator;
    private readonly ILogger<PlaybookOrchestrationService> _logger;

    // In-memory run tracking (Phase 1) - replaced with Dataverse in Phase 2
    private readonly ConcurrentDictionary<Guid, PlaybookRunContext> _activeRuns = new();

    public PlaybookOrchestrationService(
        INodeService nodeService,
        INodeExecutorRegistry executorRegistry,
        IScopeResolverService scopeResolver,
        IAnalysisOrchestrationService legacyOrchestrator,
        ILogger<PlaybookOrchestrationService> logger)
    {
        _nodeService = nodeService;
        _executorRegistry = executorRegistry;
        _scopeResolver = scopeResolver;
        _legacyOrchestrator = legacyOrchestrator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PlaybookStreamEvent> ExecuteAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        _logger.LogInformation(
            "Starting playbook execution - RunId: {RunId}, PlaybookId: {PlaybookId}, Documents: {DocumentCount}",
            runId, request.PlaybookId, request.DocumentIds.Length);

        // Create run context
        var context = new PlaybookRunContext(
            runId,
            request.PlaybookId,
            request.DocumentIds,
            httpContext,
            request.UserContext,
            request.Parameters);

        _activeRuns[runId] = context;

        // Use a channel to handle events from try/catch blocks
        var channel = Channel.CreateUnbounded<PlaybookStreamEvent>();

        // Start execution in background
        var executionTask = ExecuteInternalAsync(request, context, channel.Writer, cancellationToken);

        // Read events from channel and yield them
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Ensure execution task completed
        await executionTask;

        // Schedule cleanup after 1 hour
        ScheduleCleanup(runId);
    }

    private async Task ExecuteInternalAsync(
        PlaybookRunRequest request,
        PlaybookRunContext context,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            // Load nodes to detect mode
            var nodes = await _nodeService.GetNodesAsync(request.PlaybookId, cancellationToken);

            if (nodes.Length == 0)
            {
                // Legacy mode - delegate to existing orchestrator
                _logger.LogInformation(
                    "Playbook {PlaybookId} has no nodes - using Legacy mode",
                    request.PlaybookId);

                await ExecuteLegacyModeAsync(request, context, writer, cancellationToken);
            }
            else
            {
                // NodeBased mode - use new orchestration
                _logger.LogInformation(
                    "Playbook {PlaybookId} has {NodeCount} nodes - using NodeBased mode",
                    request.PlaybookId, nodes.Length);

                await ExecuteNodeBasedModeAsync(context, nodes, writer, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            context.MarkCancelled();
            await writer.WriteAsync(PlaybookStreamEvent.RunCancelled(
                context.RunId, context.PlaybookId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playbook execution failed - RunId: {RunId}", context.RunId);
            context.MarkFailed(ex.Message);
            await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                context.RunId, context.PlaybookId, ex.Message), CancellationToken.None);
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookValidationResult> ValidateAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            var nodes = await _nodeService.GetNodesAsync(playbookId, cancellationToken);

            if (nodes.Length == 0)
            {
                // Legacy mode - no graph validation needed
                return PlaybookValidationResult.Success(
                    ["Playbook uses Legacy mode (no nodes defined)"]);
            }

            // Build execution graph to validate
            var graph = new ExecutionGraph(nodes);

            // Check for cycles (throws if cycle detected)
            if (!graph.IsValid())
            {
                errors.Add("Playbook contains circular dependencies");
                return PlaybookValidationResult.Failure(errors.ToArray());
            }

            // Validate each node
            foreach (var node in nodes)
            {
                // Check action exists
                var action = await _scopeResolver.GetActionAsync(node.ActionId, cancellationToken);
                if (action == null)
                {
                    errors.Add($"Node '{node.Name}' references non-existent action");
                    continue;
                }

                // Check dependencies are valid
                foreach (var depId in node.DependsOn)
                {
                    if (!nodes.Any(n => n.Id == depId))
                    {
                        errors.Add($"Node '{node.Name}' depends on non-existent node {depId}");
                    }
                }

                // Check output variable is unique
                var duplicateOutputs = nodes
                    .Where(n => n.Id != node.Id && n.OutputVariable == node.OutputVariable)
                    .ToList();
                if (duplicateOutputs.Count > 0)
                {
                    errors.Add($"Output variable '{node.OutputVariable}' is used by multiple nodes");
                }
            }

            // Warn about disabled nodes
            var disabledNodes = nodes.Where(n => !n.IsActive).ToList();
            if (disabledNodes.Count > 0)
            {
                warnings.Add($"{disabledNodes.Count} node(s) are disabled and will be skipped");
            }

            if (errors.Count > 0)
            {
                return PlaybookValidationResult.Failure(errors.ToArray());
            }

            return PlaybookValidationResult.Success(warnings.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating playbook {PlaybookId}", playbookId);
            return PlaybookValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<PlaybookRunStatus?> GetRunStatusAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (_activeRuns.TryGetValue(runId, out var context))
        {
            // Approximate node count from stored outputs
            var nodeCount = context.NodeOutputs.Count;
            return Task.FromResult<PlaybookRunStatus?>(context.GetStatus(nodeCount));
        }

        return Task.FromResult<PlaybookRunStatus?>(null);
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (_activeRuns.TryGetValue(runId, out var context))
        {
            if (context.State == PlaybookRunState.Running)
            {
                context.Cancel();
                _logger.LogInformation("Cancelled playbook run {RunId}", runId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    #region Private Methods - Cleanup

    private void ScheduleCleanup(Guid runId)
    {
        // Remove from cache after 1 hour
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromHours(1));
            _activeRuns.TryRemove(runId, out _);
        });
    }

    #endregion

    #region Private Methods - Legacy Mode

    /// <summary>
    /// Executes playbook in Legacy mode by delegating to existing orchestrator.
    /// Wraps AnalysisStreamChunk events as PlaybookStreamEvent.
    /// </summary>
    private async Task ExecuteLegacyModeAsync(
        PlaybookRunRequest request,
        PlaybookRunContext context,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // Write start event
        await writer.WriteAsync(PlaybookStreamEvent.RunStarted(
            context.RunId, context.PlaybookId, 1), cancellationToken);

        // Create legacy request
        var legacyRequest = new PlaybookExecuteRequest
        {
            PlaybookId = request.PlaybookId,
            DocumentIds = request.DocumentIds,
            AdditionalContext = request.UserContext
        };

        var tokenUsage = new TokenUsage(0, 0);

        await foreach (var chunk in _legacyOrchestrator.ExecutePlaybookAsync(
            legacyRequest, context.HttpContext, cancellationToken))
        {
            // Convert AnalysisStreamChunk to PlaybookStreamEvent
            switch (chunk.Type)
            {
                case "metadata":
                    // Already sent RunStarted
                    break;

                case "chunk":
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        await writer.WriteAsync(PlaybookStreamEvent.NodeProgress(
                            context.RunId, context.PlaybookId, Guid.Empty, chunk.Content), cancellationToken);
                    }
                    break;

                case "done":
                    if (chunk.TokenUsage != null)
                    {
                        tokenUsage = chunk.TokenUsage;
                    }
                    break;

                case "error":
                    context.MarkFailed(chunk.Error ?? "Unknown error");
                    await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                        context.RunId, context.PlaybookId, chunk.Error ?? "Unknown error"), cancellationToken);
                    return;
            }
        }

        // Write completion
        context.MarkCompleted();
        var metrics = new PlaybookRunMetrics
        {
            TotalNodes = 1,
            CompletedNodes = 1,
            TotalTokensIn = tokenUsage.Input,
            TotalTokensOut = tokenUsage.Output,
            Duration = DateTimeOffset.UtcNow - startedAt
        };

        await writer.WriteAsync(PlaybookStreamEvent.RunCompleted(
            context.RunId, context.PlaybookId, metrics), cancellationToken);
    }

    #endregion

    #region Private Methods - NodeBased Mode

    /// <summary>
    /// Executes playbook in NodeBased mode using ExecutionGraph.
    /// </summary>
    private async Task ExecuteNodeBasedModeAsync(
        PlaybookRunContext context,
        PlaybookNodeDto[] nodes,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        context.MarkRunning();

        // Build execution graph
        var graph = new ExecutionGraph(nodes);
        var totalNodes = graph.NodeCount;

        _logger.LogDebug(
            "Built execution graph for playbook {PlaybookId}: {NodeCount} active nodes",
            context.PlaybookId, totalNodes);

        // Write start event
        await writer.WriteAsync(PlaybookStreamEvent.RunStarted(
            context.RunId, context.PlaybookId, totalNodes), cancellationToken);

        // Get topological order for sequential execution (Phase 1)
        // Phase 3 will use GetExecutionBatches() for parallel execution
        var executionOrder = graph.GetTopologicalOrder();

        _logger.LogDebug(
            "Execution order for playbook {PlaybookId}: {Order}",
            context.PlaybookId,
            string.Join(" → ", executionOrder.Select(n => n.Name)));

        // Execute nodes in order
        foreach (var node in executionOrder)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Playbook execution cancelled at node {NodeName}",
                    node.Name);
                break;
            }

            context.CurrentNodeId = node.Id;

            // Execute single node
            var result = await ExecuteNodeAsync(context, node, graph, writer, cancellationToken);

            // Stop if node failed and playbook doesn't continue on error
            if (!result.Success)
            {
                // Phase 1: Always stop on failure
                // Phase 2: Check sprk_continueonerror field
                _logger.LogWarning(
                    "Node {NodeName} failed - stopping playbook execution",
                    node.Name);

                context.MarkFailed($"Node '{node.Name}' failed: {result.ErrorMessage}");

                await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                    context.RunId,
                    context.PlaybookId,
                    $"Node '{node.Name}' failed",
                    context.GetMetrics(totalNodes)), cancellationToken);

                return;
            }
        }

        // Check final state
        if (context.CancellationToken.IsCancellationRequested)
        {
            context.MarkCancelled();
            await writer.WriteAsync(PlaybookStreamEvent.RunCancelled(
                context.RunId, context.PlaybookId), cancellationToken);
        }
        else
        {
            context.MarkCompleted();
            await writer.WriteAsync(PlaybookStreamEvent.RunCompleted(
                context.RunId,
                context.PlaybookId,
                context.GetMetrics(totalNodes)), cancellationToken);
        }
    }

    /// <summary>
    /// Executes a single node and writes events to the channel.
    /// Returns the node output (success or failure).
    /// </summary>
    private async Task<NodeOutput> ExecuteNodeAsync(
        PlaybookRunContext runContext,
        PlaybookNodeDto node,
        ExecutionGraph graph,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing node {NodeId}: {NodeName}", node.Id, node.Name);

        // Write start event
        await writer.WriteAsync(PlaybookStreamEvent.NodeStarted(
            runContext.RunId, runContext.PlaybookId, node.Id, node.Name), cancellationToken);

        try
        {
            // Check if dependencies failed
            foreach (var depId in node.DependsOn)
            {
                var depNode = graph.GetNode(depId);
                if (depNode != null)
                {
                    var depOutput = runContext.GetOutput(depNode.OutputVariable);
                    if (depOutput != null && !depOutput.Success)
                    {
                        var skipReason = $"Dependency '{depNode.Name}' failed";
                        runContext.RecordNodeSkipped();

                        _logger.LogDebug(
                            "Skipping node {NodeName}: {Reason}",
                            node.Name, skipReason);

                        await writer.WriteAsync(PlaybookStreamEvent.NodeSkipped(
                            runContext.RunId, runContext.PlaybookId, node.Id, node.Name, skipReason), cancellationToken);

                        // Return a skip output (treated as success for flow control)
                        return NodeOutput.Ok(node.Id, node.OutputVariable, null, skipReason);
                    }
                }
            }

            // TODO: Evaluate condition if present (Phase 3+)

            // Resolve scopes for this node
            var scopes = await _scopeResolver.ResolveNodeScopesAsync(
                node.Id, runContext.CancellationToken);

            // Get action definition
            var action = await _scopeResolver.GetActionAsync(
                node.ActionId, runContext.CancellationToken);

            if (action == null)
            {
                var errorMsg = $"Action {node.ActionId} not found for node '{node.Name}'";
                var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                runContext.StoreNodeOutput(errorOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                return errorOutput;
            }

            // Determine action type (Phase 1: All nodes are AiAnalysis)
            // Phase 2: ActionType field will be on the action entity
            var actionType = ActionType.AiAnalysis;

            // Get executor for action type
            var executor = _executorRegistry.GetExecutor(actionType);
            if (executor == null)
            {
                var errorMsg = $"No executor registered for action type '{actionType}'";
                var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InternalError);
                runContext.StoreNodeOutput(errorOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                return errorOutput;
            }

            // Create node execution context
            var nodeContext = runContext.CreateNodeContext(node, action, scopes, actionType);

            // Validate before execution
            var validation = executor.Validate(nodeContext);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors);
                var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, $"Validation failed: {errors}", NodeErrorCodes.ValidationFailed);
                runContext.StoreNodeOutput(errorOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, $"Validation failed: {errors}"), cancellationToken);

                return errorOutput;
            }

            // Execute the node
            _logger.LogDebug("Calling executor for node {NodeName}", node.Name);
            var output = await executor.ExecuteAsync(nodeContext, runContext.CancellationToken);

            // Store output for downstream nodes
            runContext.StoreNodeOutput(output);

            // Write completion event
            if (output.Success)
            {
                _logger.LogInformation(
                    "Node {NodeName} completed successfully - Duration: {Duration}ms, Tokens: {TokensIn}/{TokensOut}",
                    node.Name,
                    output.Metrics.DurationMs,
                    output.Metrics.TokensIn,
                    output.Metrics.TokensOut);

                await writer.WriteAsync(PlaybookStreamEvent.NodeCompleted(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, output), cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Node {NodeName} failed: {Error}",
                    node.Name, output.ErrorMessage);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name,
                    output.ErrorMessage ?? "Unknown error"), cancellationToken);
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Node {NodeName} was cancelled", node.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node {NodeName} threw exception", node.Name);

            // Create error output to store
            var errorOutput = NodeOutput.Error(
                node.Id,
                node.OutputVariable,
                ex.Message,
                NodeErrorCodes.InternalError);

            runContext.StoreNodeOutput(errorOutput);

            await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                runContext.RunId, runContext.PlaybookId, node.Id, node.Name, ex.Message), cancellationToken);

            return errorOutput;
        }
    }

    #endregion
}
