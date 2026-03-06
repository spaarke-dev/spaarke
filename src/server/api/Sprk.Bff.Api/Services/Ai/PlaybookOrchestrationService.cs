using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.Schemas;

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
/// Phase 3 parallel execution:
/// </para>
/// <list type="bullet">
/// <item>Nodes in same batch execute in parallel (independent nodes)</item>
/// <item>Throttled by maxParallelNodes to prevent overwhelming AI services</item>
/// <item>Rate limit handling with exponential backoff per ADR-016</item>
/// </list>
/// </remarks>
public class PlaybookOrchestrationService : IPlaybookOrchestrationService
{
    // Throttling configuration (per ADR-016)
    private const int DefaultMaxParallelNodes = 3;
    private const int MaxRateLimitRetries = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);

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

        // If the caller pre-loaded the document context, attach it so all nodes share it.
        if (request.Document != null)
        {
            context.Document = request.Document;
        }

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

    /// <inheritdoc />
    public async IAsyncEnumerable<PlaybookStreamEvent> ExecuteAppOnlyAsync(
        PlaybookRunRequest request,
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        _logger.LogInformation(
            "Starting app-only playbook execution - RunId: {RunId}, PlaybookId: {PlaybookId}, Documents: {DocumentCount}",
            runId, request.PlaybookId, request.DocumentIds.Length);

        // Create run context without HttpContext (app-only mode)
        var context = new PlaybookRunContext(
            runId,
            request.PlaybookId,
            request.DocumentIds,
            tenantId,
            cancellationToken,
            request.UserContext,
            request.Parameters);

        if (request.Document != null)
        {
            context.Document = request.Document;
        }

        _activeRuns[runId] = context;

        var channel = Channel.CreateUnbounded<PlaybookStreamEvent>();

        var executionTask = ExecuteAppOnlyInternalAsync(request, context, channel.Writer, cancellationToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        await executionTask;
        ScheduleCleanup(runId);
    }

    private async Task ExecuteAppOnlyInternalAsync(
        PlaybookRunRequest request,
        PlaybookRunContext context,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var nodes = await _nodeService.GetNodesAsync(request.PlaybookId, cancellationToken);

            if (nodes.Length == 0)
            {
                _logger.LogError(
                    "App-only execution requires node-based playbook. Playbook {PlaybookId} has no nodes.",
                    request.PlaybookId);

                context.MarkFailed("App-only execution requires a node-based playbook (no nodes found).");
                await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                    context.RunId, context.PlaybookId,
                    "App-only execution requires a node-based playbook. Configure nodes in the Playbook Builder."),
                    CancellationToken.None);
                return;
            }

            _logger.LogInformation(
                "App-only mode: Playbook {PlaybookId} has {NodeCount} nodes — using node-based execution",
                request.PlaybookId, nodes.Length);

            await ExecuteNodeBasedModeAsync(context, nodes, writer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            context.MarkCancelled();
            await writer.WriteAsync(PlaybookStreamEvent.RunCancelled(
                context.RunId, context.PlaybookId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App-only playbook execution failed - RunId: {RunId}", context.RunId);
            context.MarkFailed(ex.Message);
            await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                context.RunId, context.PlaybookId, ex.Message), CancellationToken.None);
        }
        finally
        {
            writer.Complete();
        }
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

            // Validate JPS prompt schemas on AI nodes
            await ValidatePromptSchemasAsync(nodes, graph, errors, warnings, cancellationToken);

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

    /// <inheritdoc />
    public Task<Models.Ai.PlaybookRunHistoryResponse> GetRunHistoryAsync(
        Guid playbookId,
        int page = 1,
        int pageSize = 20,
        string? stateFilter = null,
        CancellationToken cancellationToken = default)
    {
        // Filter runs for this playbook
        var runs = _activeRuns.Values
            .Where(r => r.PlaybookId == playbookId);

        // Apply state filter
        if (!string.IsNullOrWhiteSpace(stateFilter) &&
            Enum.TryParse<PlaybookRunState>(stateFilter, ignoreCase: true, out var state))
        {
            runs = runs.Where(r => r.State == state);
        }

        // Order by start time descending (most recent first)
        var orderedRuns = runs
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        var totalCount = orderedRuns.Count;

        // Apply pagination
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (Math.Max(1, page) - 1) * normalizedPageSize;

        var pageItems = orderedRuns
            .Skip(skip)
            .Take(normalizedPageSize)
            .Select(r => ConvertToRunSummary(r))
            .ToArray();

        var response = new Models.Ai.PlaybookRunHistoryResponse
        {
            Items = pageItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = normalizedPageSize
        };

        _logger.LogDebug(
            "Retrieved run history for playbook {PlaybookId}: {Count} of {Total} runs (page {Page})",
            playbookId, pageItems.Length, totalCount, page);

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<Models.Ai.PlaybookRunDetail?> GetRunDetailAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (!_activeRuns.TryGetValue(runId, out var context))
        {
            return Task.FromResult<Models.Ai.PlaybookRunDetail?>(null);
        }

        var summary = ConvertToRunSummary(context);
        var nodeDetails = context.NodeOutputs.Values
            .Select(output => new Models.Ai.NodeRunDetail
            {
                NodeId = output.NodeId,
                NodeName = output.OutputVariable, // Use output variable as proxy for name
                OutputVariable = output.OutputVariable,
                Success = output.Success,
                DurationMs = output.Metrics.DurationMs,
                TokensIn = output.Metrics.TokensIn,
                TokensOut = output.Metrics.TokensOut,
                Confidence = output.Confidence,
                ErrorMessage = output.ErrorMessage,
                OutputPreview = TruncateOutput(output.TextContent)
            })
            .ToArray();

        var detail = new Models.Ai.PlaybookRunDetail
        {
            RunId = summary.RunId,
            PlaybookId = summary.PlaybookId,
            State = summary.State,
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            DurationMs = summary.DurationMs,
            TotalNodes = summary.TotalNodes,
            CompletedNodes = summary.CompletedNodes,
            FailedNodes = summary.FailedNodes,
            SkippedNodes = summary.SkippedNodes,
            TotalTokensIn = summary.TotalTokensIn,
            TotalTokensOut = summary.TotalTokensOut,
            ErrorMessage = summary.ErrorMessage,
            DocumentIds = context.DocumentIds,
            UserContext = context.UserContext,
            NodeDetails = nodeDetails
        };

        _logger.LogDebug(
            "Retrieved run detail for {RunId}: {NodeCount} nodes",
            runId, nodeDetails.Length);

        return Task.FromResult<Models.Ai.PlaybookRunDetail?>(detail);
    }

    #region Private Methods - Run History Helpers

    private static Models.Ai.PlaybookRunSummary ConvertToRunSummary(PlaybookRunContext context)
    {
        var nodeCount = context.NodeOutputs.Count;
        var metrics = context.GetMetrics(nodeCount);

        return new Models.Ai.PlaybookRunSummary
        {
            RunId = context.RunId,
            PlaybookId = context.PlaybookId,
            State = context.State.ToString(),
            StartedAt = context.StartedAt,
            CompletedAt = context.CompletedAt,
            DurationMs = context.CompletedAt.HasValue
                ? (long)(context.CompletedAt.Value - context.StartedAt).TotalMilliseconds
                : null,
            TotalNodes = metrics.TotalNodes,
            CompletedNodes = metrics.CompletedNodes,
            FailedNodes = metrics.FailedNodes,
            SkippedNodes = metrics.SkippedNodes,
            TotalTokensIn = metrics.TotalTokensIn,
            TotalTokensOut = metrics.TotalTokensOut,
            ErrorMessage = context.ErrorMessage
        };
    }

    private static string? TruncateOutput(string? value, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }

    #endregion

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

        if (context.HttpContext is null)
        {
            context.MarkFailed("Legacy mode requires HttpContext — use node-based playbook for app-only execution");
            await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                context.RunId, context.PlaybookId,
                "Legacy mode requires HttpContext. Configure nodes in the Playbook Builder for app-only execution."),
                cancellationToken);
            return;
        }

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
    /// Phase 3: Executes nodes in parallel within batches, respecting dependencies.
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

        // Get execution batches for parallel processing
        var batches = graph.GetExecutionBatches();

        _logger.LogDebug(
            "Execution batches for playbook {PlaybookId}: {BatchCount} batches",
            context.PlaybookId, batches.Count);

        // Create semaphore for throttling parallel execution
        using var throttle = new SemaphoreSlim(DefaultMaxParallelNodes, DefaultMaxParallelNodes);

        // Execute batches sequentially (nodes within batch execute in parallel)
        var batchNumber = 0;
        foreach (var batch in batches)
        {
            batchNumber++;

            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Playbook execution cancelled before batch {BatchNumber}",
                    batchNumber);
                break;
            }

            _logger.LogDebug(
                "Executing batch {BatchNumber}/{TotalBatches} with {NodeCount} nodes: {Nodes}",
                batchNumber, batches.Count, batch.Count,
                string.Join(", ", batch.Select(n => n.Name)));

            // Execute all nodes in this batch in parallel (with throttling)
            var batchTasks = batch.Select(async node =>
            {
                // Acquire throttle slot
                await throttle.WaitAsync(context.CancellationToken);
                try
                {
                    context.CurrentNodeId = node.Id;
                    return await ExecuteNodeWithRetryAsync(context, node, graph, writer, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            });

            // Wait for all nodes in batch to complete
            var results = await Task.WhenAll(batchTasks);

            // Check for failures in this batch
            var failedNode = batch.Zip(results, (node, result) => new { node, result })
                .FirstOrDefault(x => !x.result.Success);

            if (failedNode != null)
            {
                // TODO (Phase 4): Check sprk_continueonerror field per node
                _logger.LogWarning(
                    "Node {NodeName} in batch {BatchNumber} failed - stopping playbook execution",
                    failedNode.node.Name, batchNumber);

                var failureDetail = $"Node '{failedNode.node.Name}' failed: {failedNode.result.ErrorMessage}";
                context.MarkFailed(failureDetail);

                await writer.WriteAsync(PlaybookStreamEvent.RunFailed(
                    context.RunId,
                    context.PlaybookId,
                    failureDetail,
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
    /// Executes a node with retry logic for rate limit (429) responses.
    /// Implements exponential backoff per ADR-016.
    /// </summary>
    private async Task<NodeOutput> ExecuteNodeWithRetryAsync(
        PlaybookRunContext context,
        PlaybookNodeDto node,
        ExecutionGraph graph,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;

        while (true)
        {
            try
            {
                return await ExecuteNodeAsync(context, node, graph, writer, cancellationToken);
            }
            catch (HttpRequestException ex) when (IsRateLimitError(ex) && retryCount < MaxRateLimitRetries)
            {
                retryCount++;
                var delay = CalculateBackoffDelay(retryCount, ex);

                _logger.LogWarning(
                    "Node {NodeName} hit rate limit (attempt {RetryCount}/{MaxRetries}), retrying after {Delay}ms",
                    node.Name, retryCount, MaxRateLimitRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Checks if an exception is a rate limit (429) error.
    /// </summary>
    private static bool IsRateLimitError(HttpRequestException ex)
    {
        return ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
    }

    /// <summary>
    /// Calculates exponential backoff delay with jitter.
    /// Respects Retry-After header if present in the exception.
    /// </summary>
    private static TimeSpan CalculateBackoffDelay(int retryCount, HttpRequestException ex)
    {
        // Check for Retry-After header in exception data
        if (ex.Data.Contains("Retry-After") && ex.Data["Retry-After"] is int retryAfterSeconds)
        {
            return TimeSpan.FromSeconds(retryAfterSeconds);
        }

        // Exponential backoff: 2^retryCount * baseDelay (2s, 4s, 8s)
        var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, retryCount) * BaseRetryDelay.TotalSeconds);

        // Add jitter (0-25% of delay) to prevent thundering herd
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)(exponentialDelay.TotalMilliseconds * 0.25)));

        return exponentialDelay + jitter;
    }

    /// <summary>
    /// Extracts the __actionType value from a node's ConfigJson.
    /// Returns null if ConfigJson is missing or doesn't contain the field.
    /// </summary>
    private static ActionType? ExtractActionTypeFromConfig(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("__actionType", out var actionTypeProp) &&
                actionTypeProp.TryGetInt32(out var actionTypeInt))
            {
                return (ActionType)actionTypeInt;
            }
        }
        catch
        {
            // Invalid JSON — fall through to null
        }

        return null;
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

            // Skip Start nodes — they are canvas anchors with no execution logic.
            // Detect via:
            //   1. Explicit __actionType == Start (33) in ConfigJson
            //   2. Control node with null/empty ConfigJson (auto-placed, no properties)
            //   3. Control node named "Start" with __actionType == Condition (30) — legacy
            //      canvas sync writes Condition instead of Start for the auto-placed node
            var configActionType = ExtractActionTypeFromConfig(node.ConfigJson);
            var isStartNode = configActionType == ActionType.Start
                || (node.NodeType == NodeType.Control && string.IsNullOrWhiteSpace(node.ConfigJson))
                || (node.NodeType == NodeType.Control && configActionType == ActionType.Condition
                    && string.Equals(node.Name, "Start", StringComparison.OrdinalIgnoreCase));
            if (isStartNode)
            {
                var skipOutput = NodeOutput.Ok(node.Id, node.OutputVariable, null, "Start node (passthrough)");
                runContext.StoreNodeOutput(skipOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeCompleted(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, skipOutput), cancellationToken);

                return skipOutput;
            }

            // Note: ConditionJson on nodes is for conditional execution guards (Phase 5).
            // The Condition ActionType is handled by ConditionNodeExecutor (Phase 4) which
            // returns ConditionResult with branch selection for orchestrator-level branching.

            // NodeType-driven scope resolution and action lookup.
            // AI nodes require an Action record and resolve all scopes (skills, knowledge, tools).
            // Structural nodes (Output, Control) need neither.
            AnalysisAction action;
            ActionType actionType;
            ResolvedScopes scopes;

            if (node.NodeType == NodeType.AIAnalysis)
            {
                // AI nodes: resolve scopes and load action from Dataverse
                scopes = await _scopeResolver.ResolveNodeScopesAsync(
                    node.Id, runContext.CancellationToken);

                if (node.ActionId == Guid.Empty)
                {
                    var errorMsg = $"AI node '{node.Name}' requires an Action but has no ActionId";
                    var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                    runContext.StoreNodeOutput(errorOutput);

                    await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                        runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                    return errorOutput;
                }

                var resolved = await _scopeResolver.GetActionAsync(
                    node.ActionId, runContext.CancellationToken);

                if (resolved == null)
                {
                    var errorMsg = $"Action {node.ActionId} not found for node '{node.Name}'";
                    var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                    runContext.StoreNodeOutput(errorOutput);

                    await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                        runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                    return errorOutput;
                }

                action = resolved;
                actionType = action.ActionType;
            }
            else
            {
                // Structural nodes (Output, Control, Workflow): no Action record, no scopes
                scopes = new ResolvedScopes([], [], []);

                // Read specific ActionType from ConfigJson (written during canvas sync)
                // Falls back to NodeType-based default if not present
                actionType = ExtractActionTypeFromConfig(node.ConfigJson) ?? node.NodeType switch
                {
                    NodeType.Output => ActionType.DeliverOutput,
                    NodeType.Control => ActionType.Condition,
                    NodeType.Workflow => ActionType.CreateTask,
                    _ => ActionType.DeliverOutput
                };
                action = new AnalysisAction
                {
                    Id = Guid.Empty,
                    Name = node.Name,
                    ActionType = actionType
                };

                _logger.LogDebug(
                    "Structural node '{NodeName}' (NodeType={NodeType}) — using ActionType {ActionType}, no scopes",
                    node.Name, node.NodeType, actionType);
            }

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

            // Collect downstream node info for $choices resolution in JPS prompts.
            // For AI analysis nodes, look at dependent nodes (nodes that consume this node's output)
            // and collect UpdateRecord-type nodes with their ConfigJson (which contains fieldMappings).
            var downstreamNodes = CollectDownstreamNodeInfo(node, graph);

            // Create node execution context with streaming callback for per-token SSE events
            var nodeContext = runContext.CreateNodeContext(node, action, scopes, actionType) with
            {
                DownstreamNodes = downstreamNodes,
                OnTokenReceived = async text =>
                {
                    await writer.WriteAsync(PlaybookStreamEvent.NodeProgress(
                        runContext.RunId, runContext.PlaybookId, node.Id, text), cancellationToken);
                }
            };

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

    /// <summary>
    /// Collects downstream node info for <c>$choices</c> resolution.
    /// Examines nodes that depend on the given node and returns info for
    /// UpdateRecord-type nodes that may contain fieldMappings with option sets.
    /// </summary>
    /// <param name="node">The current node being executed.</param>
    /// <param name="graph">The execution graph containing all nodes and edges.</param>
    /// <returns>
    /// List of downstream node info, or null if no relevant downstream nodes exist.
    /// </returns>
    private static IReadOnlyList<DownstreamNodeInfo>? CollectDownstreamNodeInfo(
        PlaybookNodeDto node,
        ExecutionGraph graph)
    {
        var dependentIds = graph.GetDependents(node.Id);
        if (dependentIds.Count == 0)
            return null;

        List<DownstreamNodeInfo>? result = null;

        foreach (var dependentId in dependentIds)
        {
            var dependentNode = graph.GetNode(dependentId);
            if (dependentNode is null)
                continue;

            // Only collect nodes that are likely UpdateRecord type.
            // Check ConfigJson for __actionType == UpdateRecord (22).
            var dependentActionType = ExtractActionTypeFromConfig(dependentNode.ConfigJson);
            if (dependentActionType != Nodes.ActionType.UpdateRecord)
                continue;

            // Only include if ConfigJson exists (it should contain fieldMappings)
            if (string.IsNullOrWhiteSpace(dependentNode.ConfigJson))
                continue;

            result ??= new List<DownstreamNodeInfo>();
            result.Add(new DownstreamNodeInfo(
                dependentNode.OutputVariable,
                dependentNode.ConfigJson));
        }

        return result;
    }

    #endregion

    #region Private Methods - JPS Validation

    /// <summary>
    /// Detects whether a system prompt is in JSON Prompt Schema (JPS) format.
    /// A prompt is JPS if it starts with '{' and contains "$schema".
    /// </summary>
    private static bool IsJpsPrompt(string? systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            return false;

        var trimmed = systemPrompt.TrimStart();
        return trimmed.StartsWith('{') && trimmed.Contains("\"$schema\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates JPS prompt schemas on AI analysis nodes before playbook execution.
    /// Checks schema parseability, required fields, structured output compatibility,
    /// and $choices downstream node resolution.
    /// </summary>
    /// <remarks>
    /// Per ADR-015: only logs field names and node IDs — never prompt content.
    /// Flat text prompts are skipped (no JPS validation applied).
    /// </remarks>
    private async Task ValidatePromptSchemasAsync(
        PlaybookNodeDto[] nodes,
        ExecutionGraph graph,
        List<string> errors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        // Only validate AI analysis nodes that have an action
        var aiNodes = nodes.Where(n => n.IsActive && n.NodeType == NodeType.AIAnalysis && n.ActionId != Guid.Empty).ToList();
        if (aiNodes.Count == 0)
            return;

        foreach (var node in aiNodes)
        {
            // Load the action to get the system prompt
            var action = await _scopeResolver.GetActionAsync(node.ActionId, cancellationToken);
            if (action == null)
            {
                // Already handled by existing validation — skip here
                continue;
            }

            // Skip flat text prompts — JPS validation only applies to JPS schemas
            if (!IsJpsPrompt(action.SystemPrompt))
                continue;

            _logger.LogDebug(
                "Validating JPS schema for node {NodeId} ({NodeName})",
                node.Id, node.Name);

            // (a) Schema parseable: verify it deserializes to a valid PromptSchema
            PromptSchema? schema;
            try
            {
                schema = JsonSerializer.Deserialize<PromptSchema>(action.SystemPrompt, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                });
            }
            catch (JsonException ex)
            {
                errors.Add($"Node '{node.Name}': JPS schema is not valid JSON — {ex.Message}");
                continue;
            }

            if (schema == null)
            {
                errors.Add($"Node '{node.Name}': JPS schema deserialized to null");
                continue;
            }

            // (b) Required fields: verify instruction.task is not null/empty
            if (string.IsNullOrWhiteSpace(schema.Instruction.Task))
            {
                errors.Add($"Node '{node.Name}': JPS schema is missing required 'instruction.task' field");
            }

            // (c) Structured output compatible: if structuredOutput=true, verify output.fields is not empty
            if (schema.Output is { StructuredOutput: true })
            {
                if (schema.Output.Fields == null || schema.Output.Fields.Count == 0)
                {
                    errors.Add($"Node '{node.Name}': JPS schema has structuredOutput=true but output.fields is empty — at least one output field is required for structured decoding");
                }
            }

            // (d) $choices resolvable: if any output field has $choices, verify the downstream node exists
            if (schema.Output?.Fields != null)
            {
                foreach (var field in schema.Output.Fields)
                {
                    if (string.IsNullOrEmpty(field.Choices))
                        continue;

                    // $choices format: "downstream:{outputVariable}.{fieldName}"
                    if (field.Choices.StartsWith("downstream:", StringComparison.OrdinalIgnoreCase))
                    {
                        var reference = field.Choices["downstream:".Length..];
                        var dotIndex = reference.IndexOf('.');
                        var targetOutputVariable = dotIndex > 0 ? reference[..dotIndex] : reference;

                        // Check if a downstream node with that output variable exists in the graph
                        var dependentIds = graph.GetDependents(node.Id);
                        var targetExists = false;

                        foreach (var depId in dependentIds)
                        {
                            var depNode = graph.GetNode(depId);
                            if (depNode != null &&
                                string.Equals(depNode.OutputVariable, targetOutputVariable, StringComparison.OrdinalIgnoreCase))
                            {
                                targetExists = true;
                                break;
                            }
                        }

                        if (!targetExists)
                        {
                            // Warning, not error — downstream node may not be directly dependent
                            // or may be resolved at runtime through indirect graph paths
                            warnings.Add(
                                $"Node '{node.Name}': output field '{field.Name}' has $choices reference " +
                                $"'downstream:{targetOutputVariable}' but no direct downstream node with " +
                                $"outputVariable '{targetOutputVariable}' was found in the graph");
                        }
                    }
                }
            }
        }
    }

    #endregion
}
