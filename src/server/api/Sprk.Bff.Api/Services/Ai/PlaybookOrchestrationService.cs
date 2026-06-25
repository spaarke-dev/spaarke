using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
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
    private readonly IInsightsActionRouter _insightsRouter;
    private readonly ILogger<PlaybookOrchestrationService> _logger;

    /// <summary>
    /// R6 Pillar 6c (FR-37 / task 063) — optional context.* event emitter for
    /// <c>context.playbook_node_executing</c> / <c>context.playbook_node_completed</c>
    /// emission at the orchestration WRAPPER level (NFR-08 BINDING: NOT inside any of the
    /// 11 production node executors). Optional so existing test fixtures construct cleanly.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter? _contextEventEmitter;

    // In-memory run tracking (Phase 1) - replaced with Dataverse in Phase 2
    private readonly ConcurrentDictionary<Guid, PlaybookRunContext> _activeRuns = new();

    public PlaybookOrchestrationService(
        INodeService nodeService,
        INodeExecutorRegistry executorRegistry,
        IScopeResolverService scopeResolver,
        IAnalysisOrchestrationService legacyOrchestrator,
        IInsightsActionRouter insightsRouter,
        ILogger<PlaybookOrchestrationService> logger,
        Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter? contextEventEmitter = null)
    {
        _nodeService = nodeService;
        _executorRegistry = executorRegistry;
        _scopeResolver = scopeResolver;
        _legacyOrchestrator = legacyOrchestrator;
        _insightsRouter = insightsRouter ?? throw new ArgumentNullException(nameof(insightsRouter));
        _logger = logger;
        _contextEventEmitter = contextEventEmitter;
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

        // Extract userId from parameters (set by PlaybookSchedulerJob when fanning out per-user).
        // Without this, QueryDataverseNodeExecutor.ResolveUserId() returns null and the
        // FetchXml 'eq-userid' substitution is skipped — Dataverse then evaluates as the
        // BFF service principal (MI), returning 0 records. R3's PlaybookSchedulerJob passes
        // userId via Parameters["userId"] but did not wire it into context.UserId here.
        Guid? userId = null;
        if (request.Parameters is not null
            && request.Parameters.TryGetValue("userId", out var userIdStr)
            && Guid.TryParse(userIdStr, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        // Create run context without HttpContext (app-only mode)
        var context = new PlaybookRunContext(
            runId,
            request.PlaybookId,
            request.DocumentIds,
            tenantId,
            cancellationToken,
            request.UserContext,
            request.Parameters)
        {
            UserId = userId
        };

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
                // TRACKED: GitHub #233 - Check sprk_continueonerror field per node
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
    /// Wave C1 task 020 — Gap #2 helper per design-a5 §7.2. Extracts the <c>selectedBranch</c>
    /// from a branching gate's <see cref="NodeOutput"/> structured data. Returns the downstream
    /// node name on the selected branch, or null when the upstream is not a branching gate
    /// (no <c>selectedBranch</c> property in its structured data).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Branching gates today:
    /// <see cref="Sprk.Bff.Api.Services.Ai.Nodes.EvidenceSufficiencyResult.SelectedBranch"/> and
    /// <see cref="Sprk.Bff.Api.Services.Ai.Nodes.ConditionResult.SelectedBranch"/>. Both expose
    /// the property under the JSON name <c>"selectedBranch"</c> via default record serialization.
    /// </para>
    /// <para>
    /// Reads from <see cref="NodeOutput.StructuredData"/> via case-insensitive property lookup
    /// (the JSON serializer uses PascalCase or camelCase depending on options; we accept both).
    /// </para>
    /// </remarks>
    private static string? TryExtractSelectedBranch(NodeOutput depOutput)
    {
        if (depOutput.StructuredData is null) return null;
        var data = depOutput.StructuredData.Value;
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        // Try common casings: "selectedBranch" (camelCase JSON output) and "SelectedBranch" (record name).
        if (data.TryGetProperty("selectedBranch", out var camelCase) &&
            camelCase.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return camelCase.GetString();
        }
        if (data.TryGetProperty("SelectedBranch", out var pascalCase) &&
            pascalCase.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return pascalCase.GetString();
        }

        return null;
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

        // R6 Pillar 6c (FR-37 / task 063) — context.playbook_node_executing emission.
        // ADR-015 audit: playbookId / nodeId are deterministic GUIDs; nodeType is enum-like.
        // NFR-08 BINDING: this emission is AT THE WRAPPER LEVEL — the 11 production node
        // executors are NOT touched. Per-node wrapper timer started here for the matching
        // completed event below.
        var nodeStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _contextEventEmitter?.PlaybookNodeExecuting(
            playbookId: runContext.PlaybookId,
            nodeId: node.Id,
            nodeType: node.NodeType.ToString(),
            sessionId: null,
            tenantId: null);

        // R6 Pillar 6c — local helper for context.playbook_node_completed emission.
        // Called below at each return site (the inner try-catch has multiple return paths;
        // a finally block on a freshly-opened try would force restructuring the entire
        // existing try/catch — calling this helper inline keeps the change surgical).
        // ADR-015 audit: decision is enum-like; durationMs is numeric; no payload.
        void EmitNodeCompleted(string decision)
        {
            nodeStopwatch.Stop();
            _contextEventEmitter?.PlaybookNodeCompleted(
                playbookId: runContext.PlaybookId,
                nodeId: node.Id,
                decision: decision,
                durationMs: nodeStopwatch.ElapsedMilliseconds,
                sessionId: null,
                tenantId: null);
        }

        try
        {
            // Wave C1 task 020 — Gap #2 patch: branch-aware dependency resolution per design-a5 §7.2.
            // When an upstream EvidenceSufficiencyNode (or ConditionNode) emits a selectedBranch,
            // downstream nodes NOT on the selected branch are skipped — even if all their upstream
            // dependencies succeeded. Without this patch, a "gated-off" node (e.g., layer2Extract
            // in universal-ingest@v1 when checkLayer2Gate=insufficient) would execute because
            // the existing AND-only dependency check treats EvidenceSufficiencyNode's "success-with-
            // insufficient-verdict" as a successful upstream.
            //
            // Semantics: an upstream's selectedBranch is the NAME of the downstream node on the
            // selected branch (see EvidenceSufficiencyNode line 158-160:
            //   selectedBranch = sufficient ? config.SufficientBranch : config.InsufficientBranch
            //   where SufficientBranch / InsufficientBranch hold downstream node names).
            // We skip the current node when ALL of:
            //   1) The upstream returned a selectedBranch (i.e., upstream is a branching gate);
            //   2) The current node's name != selectedBranch.
            // If a node has MULTIPLE upstreams and at least ONE branching gate selected it, run it
            // (handles emitObservations which has groundingVerify [sufficient path] + checkLayer2Gate
            // [insufficient path] as upstreams). Implementation: track whether at least one branching
            // upstream selected this node; only skip if every branching upstream rejected it.
            //
            // Backward compatibility: nodes whose upstreams have no selectedBranch (most existing
            // playbooks) behave unchanged — branchingDepCount stays 0 and the skip branch is bypassed.
            int branchingDepCount = 0;
            int branchingDepsSelectingThisNode = 0;
            foreach (var depId in node.DependsOn)
            {
                var depNode = graph.GetNode(depId);
                if (depNode == null) continue;

                var depOutput = runContext.GetOutput(depNode.OutputVariable);
                if (depOutput == null || !depOutput.Success) continue;  // existing failure path handles below

                var selectedBranch = TryExtractSelectedBranch(depOutput);
                if (selectedBranch is null) continue;

                branchingDepCount++;
                if (string.Equals(selectedBranch, node.Name, StringComparison.OrdinalIgnoreCase))
                    branchingDepsSelectingThisNode++;
            }

            if (branchingDepCount > 0 && branchingDepsSelectingThisNode == 0)
            {
                var skipReason = $"Branch not selected: {branchingDepCount} upstream branching gate(s) routed to a different branch";
                runContext.RecordNodeSkipped();

                _logger.LogDebug(
                    "Skipping node {NodeName}: {Reason}",
                    node.Name, skipReason);

                await writer.WriteAsync(PlaybookStreamEvent.NodeSkipped(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, skipReason), cancellationToken);

                EmitNodeCompleted("skipped"); // R6 Pillar 6c (FR-37 / task 063)

                // Return a skip output (treated as success for flow control — matches existing
                // dependency-failure-skip semantics; downstream nodes see this as "Ok(null)").
                return NodeOutput.Ok(node.Id, node.OutputVariable, null, skipReason);
            }

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

                        EmitNodeCompleted("skipped"); // R6 Pillar 6c (FR-37 / task 063)

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

                EmitNodeCompleted("skipped"); // R6 Pillar 6c (FR-37 / task 063) — Start anchor

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

            // Per Insights Engine r2 Wave B (2026-06-02): when sprk_actionid is set,
            // the action's ActionType is the canonical dispatch source REGARDLESS of
            // nodeType. Insights nodes like checkSufficiency (Control), groundCitations
            // (Control), ReturnInsightArtifactNode (Output), and declineInsufficient
            // (Output) all need their specific executor (EvidenceSufficiency, GroundingVerify,
            // ReturnInsightArtifact, DeclineToFind) — not the nodeType-based default
            // (Condition / DeliverOutput) that the original logic fell back to.
            //
            // The legacy structural-node path is preserved for backward compat: when no
            // action FK is set, fall back to ConfigJson __actionType (canvas-Designer
            // convention) or nodeType-based default.
            if (node.ActionId != Guid.Empty)
            {
                // Resolve scopes only for AI nodes (Control/Output/Workflow have none)
                scopes = node.NodeType == NodeType.AIAnalysis
                    ? await _scopeResolver.ResolveNodeScopesAsync(node.Id, runContext.CancellationToken)
                    : new ResolvedScopes([], [], []);

                var resolved = await _scopeResolver.GetActionAsync(
                    node.ActionId, runContext.CancellationToken);

                if (resolved == null)
                {
                    var errorMsg = $"Action {node.ActionId} not found for node '{node.Name}'";
                    var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                    runContext.StoreNodeOutput(errorOutput);

                    await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                        runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                    EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063)

                    return errorOutput;
                }

                action = resolved;
                actionType = action.ActionType;
            }
            else if (node.NodeType == NodeType.AIAnalysis)
            {
                var errorMsg = $"AI node '{node.Name}' requires an Action but has no ActionId";
                var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                runContext.StoreNodeOutput(errorOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063)

                return errorOutput;
            }
            else
            {
                // Structural nodes (Output, Control, Workflow) WITHOUT an action FK:
                // legacy path — uses ConfigJson __actionType or nodeType-based default.
                scopes = new ResolvedScopes([], [], []);

                actionType = ExtractActionTypeFromConfig(node.ConfigJson) ?? node.NodeType switch
                {
                    NodeType.Output => ActionType.DeliverOutput,
                    NodeType.Control => ActionType.Condition,
                    NodeType.Workflow => ActionType.CreateTask,
                    // FR-52 / Phase 5R Wave 5-C task 114R: composite delivery node maps to a
                    // SEPARATE ActionType so the legacy Output → DeliverOutput dispatch is
                    // UNCHANGED (backward-compat invariant). The DeliverCompositeNodeExecutor
                    // is the only executor for DeliverComposite.
                    NodeType.DeliverComposite => ActionType.DeliverComposite,
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

                EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063)

                return errorOutput;
            }

            // Collect downstream node info for $choices resolution in JPS prompts.
            // For AI analysis nodes, look at dependent nodes (nodes that consume this node's output)
            // and collect UpdateRecord-type nodes with their ConfigJson (which contains fieldMappings).
            var downstreamNodes = CollectDownstreamNodeInfo(node, graph);

            // Insights Engine r2 Wave D4 (task 033) — per-(area, type) routing for universal-ingest@v1.
            // Layer 1 (layer1Classify): if parameters.practiceAreaHint is set AND a per-area action row
            // INS-L1C-<AREA>@v1 exists, swap the resolved action for the per-area variant. Fall back to
            // the generic action row otherwise — preserves the "every matter classifies" invariant per
            // design-a3 §2.5.
            //
            // Layer 2 (layer2Extract): consult the sprk_practicearea_documenttype matrix for
            // (practiceAreaHint, layer1.documentTypeCode). Outcomes:
            //   - per-pair action: swap action, run executor against per-pair prompt
            //   - NULL sprk_layer2actioncode (gate-fail by design, e.g., CTRNS × NDA): skip the node,
            //     emit Layer-1-only Observation (handled by setting branchSkipped flag below)
            //   - no matrix row (unmapped pair): fall back to generic INS-L2X@v1
            //   - missing inputs: PassThrough (no change)
            //
            // Routing decisions are cached in-process (15-min sliding TTL) per InsightsActionRouter.
            // The routing happens BEFORE template substitution so the substituted prompt + parameters
            // reach the executor as a single coherent payload.
            (action, var insightsL2GateFail) = await ApplyInsightsRoutingAsync(
                node, action, runContext, cancellationToken).ConfigureAwait(false);

            if (insightsL2GateFail)
            {
                // Structured Layer 2 gate-fail per design-a3 §2.5 step 4 (CTRNS × NDA pattern).
                // Surface as a successful skip so emitObservations downstream still runs with
                // Layer-1-only output. Mirrors the dependency-failure-skip semantics already
                // used by the branch-aware skip path.
                var skipReason = "Insights Layer 2 routing: matrix row carries NULL sprk_layer2actioncode (intentional per-pair gate-fail)";
                _logger.LogInformation(
                    "Insights Layer 2 gate-fail for node '{NodeName}' (runId={RunId}, playbookId={PlaybookId}) — {SkipReason}. Universal-ingest will emit Layer-1-only Observation downstream.",
                    node.Name, runContext.RunId, runContext.PlaybookId, skipReason);

                var skipOutput = NodeOutput.Ok(node.Id, node.OutputVariable, null, skipReason);
                runContext.RecordNodeSkipped();
                runContext.StoreNodeOutput(skipOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeSkipped(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, skipReason), cancellationToken);

                EmitNodeCompleted("skipped"); // R6 Pillar 6c (FR-37 / task 063) — Insights L2 gate-fail

                return skipOutput;
            }

            // Apply {{paramName}} template substitution to ConfigJson before the executor
            // runs. Variables come from runContext.Parameters (populated by the caller +
            // enriched by InsightsOrchestrator with matterId/projectId/invoiceId derived from
            // Subject). Per Insights Engine r2 Wave B (2026-06-02 smoke trace): without this
            // substitution, playbook nodes like resolveLiveFacts received the literal
            // "matter:{{matterId}}" and failed at LiveFactResolver. Centralizing the fix here
            // applies it to every node executor uniformly (LiveFact / IndexRetrieve /
            // ReturnInsightArtifact all use {{matterId}} in synthesis playbooks).
            var substitutedNode = ApplyConfigJsonTemplates(node, runContext.Parameters);

            // Create node execution context with streaming callback for per-token SSE events
            var nodeContext = runContext.CreateNodeContext(substitutedNode, action, scopes, actionType) with
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

                EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063)

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

            // R3 FR-3H1.4 / AC-H1.2 — scan NodeOutput for literal `{{` substrings
            // (unrendered Handlebars templates). Non-fatal: logs warning + emits a
            // PlaybookStreamEvent of type UnrenderedTemplateDetected so the UI / SSE
            // consumers surface the leak. Output already stored + emitted above; this
            // is observation only and does NOT mutate or block the stream.
            await ScanForUnrenderedTemplatesAsync(runContext, node, output, writer, cancellationToken)
                .ConfigureAwait(false);

            // R6 Pillar 6c (FR-37 / task 063) — wrapper-level completion emission.
            // NFR-08 BINDING: this is at the WRAPPER, not inside the executor.
            EmitNodeCompleted(output.Success ? "success" : "failed");

            return output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Node {NodeName} was cancelled", node.Name);
            EmitNodeCompleted("cancelled"); // R6 Pillar 6c (FR-37 / task 063)
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

            EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063) — exception path

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

    /// <summary>
    /// R3 FR-3H1.4 / AC-H1.2 — scans a completed node's <see cref="NodeOutput"/>
    /// for literal <c>{{</c> substrings, indicating a Handlebars template that
    /// leaked into the output unrendered. When detected, logs a structured warning
    /// AND emits a <see cref="PlaybookEventType.UnrenderedTemplateDetected"/> stream
    /// event with sample text + correlation IDs. Non-fatal — does NOT throw;
    /// downstream nodes still see the output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scanned string fields (in order):
    /// <see cref="NodeOutput.TextContent"/>, <see cref="NodeOutput.ErrorMessage"/>,
    /// every entry in <see cref="NodeOutput.Warnings"/>, and the serialized
    /// <see cref="NodeOutput.StructuredData"/>.
    /// </para>
    /// <para>
    /// One warning + one stream event per node — the warning identifies every
    /// field that contained <c>{{</c>; the stream event sample is taken from the
    /// first such field, capped at 200 chars. Sample text is logged so operators
    /// can identify which template token leaked.
    /// </para>
    /// <para>
    /// CorrelationId: <see cref="PlaybookRunContext.RunId"/> per NFR-08 (matches
    /// the run-scoped trace identifier surfaced on every PlaybookStreamEvent).
    /// HttpContext.TraceIdentifier is also logged when present.
    /// </para>
    /// </remarks>
    private async Task ScanForUnrenderedTemplatesAsync(
        PlaybookRunContext runContext,
        PlaybookNodeDto node,
        NodeOutput output,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        // Lazy: build a list of (fieldName, value) pairs we will scan.
        // Keep allocation minimal — most outputs have only TextContent populated.
        List<(string Field, string Value)>? offenders = null;

        void Check(string fieldName, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (value.IndexOf("{{", StringComparison.Ordinal) < 0)
                return;
            offenders ??= new List<(string, string)>(capacity: 2);
            offenders.Add((fieldName, value));
        }

        Check(nameof(NodeOutput.TextContent), output.TextContent);
        Check(nameof(NodeOutput.ErrorMessage), output.ErrorMessage);

        // Warnings list — index in the message so operators can locate the entry.
        if (output.Warnings.Count > 0)
        {
            for (var i = 0; i < output.Warnings.Count; i++)
            {
                Check($"{nameof(NodeOutput.Warnings)}[{i}]", output.Warnings[i]);
            }
        }

        // StructuredData — serialize once to scan; cheaper than walking JSON tokens
        // for the common path (no leaks) since the call is bounded by NodeOutput size
        // which is already capped by upstream contracts (CHAT-ATTACHMENT-POLICY etc.).
        if (output.StructuredData is JsonElement structured && structured.ValueKind != JsonValueKind.Undefined && structured.ValueKind != JsonValueKind.Null)
        {
            // GetRawText avoids re-serializing; quotes around JSON strings won't
            // produce false-positive "{{" because the literal is a 2-char sequence
            // (JSON cannot embed it as escape — `{` is not escapable inside strings).
            string raw;
            try
            {
                raw = structured.GetRawText();
            }
            catch (InvalidOperationException)
            {
                // Disposed JsonDocument or otherwise unreadable — skip silently.
                raw = string.Empty;
            }
            Check(nameof(NodeOutput.StructuredData), raw);
        }

        if (offenders is null)
            return;

        // Build sample from first offender, capped at 200 chars per task spec.
        var firstField = offenders[0].Field;
        var firstValue = offenders[0].Value;
        var sample = firstValue.Length <= 200 ? firstValue : firstValue[..200];
        var fieldList = string.Join(", ", offenders.Select(o => o.Field));

        var httpTraceId = runContext.HttpContext?.TraceIdentifier;

        _logger.LogWarning(
            "Unrendered template detected in node {NodeName} (NodeId={NodeId}, RunId={RunId}, CorrelationId={CorrelationId}, HttpTraceId={HttpTraceId}). " +
            "Field(s) containing '{{{{': {Fields}. Sample (first 200 chars of {SampleField}): {TemplateSample}",
            node.Name,
            node.Id,
            runContext.RunId,
            runContext.RunId,
            httpTraceId,
            fieldList,
            firstField,
            sample);

        await writer.WriteAsync(
            PlaybookStreamEvent.UnrenderedTemplateDetected(
                runContext.RunId,
                runContext.PlaybookId,
                node.Id,
                node.Name,
                sample),
            cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Universal-ingest@v1 Layer 1 node name (per <c>universal-ingest.playbook.json</c>).
    /// Detection point for per-area Layer 1 routing.
    /// </summary>
    private const string UniversalIngestLayer1NodeName = "layer1Classify";

    /// <summary>
    /// Universal-ingest@v1 Layer 2 node name (per <c>universal-ingest.playbook.json</c>).
    /// Detection point for per-(area, type) Layer 2 routing.
    /// </summary>
    private const string UniversalIngestLayer2NodeName = "layer2Extract";

    /// <summary>
    /// Universal-ingest@v1 Layer 1 output variable name (consumed by Layer 2 routing
    /// to read the <c>document_type_code</c> classification result).
    /// </summary>
    private const string UniversalIngestLayer1OutputVariable = "layer1";

    /// <summary>
    /// Apply Insights Engine r2 Wave D4 (task 033) per-(area, type) routing to the
    /// universal-ingest@v1 playbook's Layer 1 and Layer 2 nodes. Returns the (possibly
    /// swapped) action plus a flag indicating whether the node should be skipped
    /// with a structured Layer 2 gate-fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Routing is identified by <see cref="PlaybookNodeDto.Name"/> matching the canonical
    /// universal-ingest node names. Other playbooks (including future Insights variants
    /// that don't follow this naming) flow through unchanged — routing is OPT-IN by
    /// node name, never silently applied.
    /// </para>
    /// <para>
    /// <b>Layer 1 routing</b>: reads <c>parameters.practiceAreaHint</c>, asks the router
    /// for the per-area action; on miss, returns the orchestrator's default action.
    /// </para>
    /// <para>
    /// <b>Layer 2 routing</b>: reads <c>parameters.practiceAreaHint</c> + extracts the
    /// <c>document_type_code</c> from the upstream <c>layer1</c> node output, asks the
    /// router for the matrix lookup. Returns one of:
    /// <list type="bullet">
    ///   <item>PassThrough — inputs missing, action unchanged</item>
    ///   <item>UsePerPairAction — swap action to per-pair variant</item>
    ///   <item>GateFailNullActionCode — gate-fail flag set; caller emits Skip output</item>
    ///   <item>FallbackToGeneric — unmapped pair, action unchanged</item>
    /// </list>
    /// </para>
    /// </remarks>
    private async Task<(AnalysisAction Action, bool GateFailLayer2)> ApplyInsightsRoutingAsync(
        PlaybookNodeDto node,
        AnalysisAction action,
        PlaybookRunContext runContext,
        CancellationToken cancellationToken)
    {
        // Identify the universal-ingest L1/L2 nodes by name. Other playbooks (and
        // synthesis playbooks like predict-matter-cost@v1) pass through unchanged.
        if (string.IsNullOrEmpty(node.Name))
        {
            return (action, false);
        }

        var practiceAreaHint = TryGetParameter(runContext.Parameters, "practiceAreaHint");

        if (string.Equals(node.Name, UniversalIngestLayer1NodeName, StringComparison.Ordinal))
        {
            var routed = await _insightsRouter.ResolveLayer1ActionAsync(
                practiceAreaHint, action, cancellationToken).ConfigureAwait(false);
            return (routed, false);
        }

        if (string.Equals(node.Name, UniversalIngestLayer2NodeName, StringComparison.Ordinal))
        {
            // Read the Layer 1 output to extract the document_type_code. When unavailable
            // (sanitize short-circuited OR a prior node failed before Layer 1 ran), fall
            // through to the default action — the orchestrator's branch-aware skip handles
            // the rest.
            var documentTypeHint = TryGetParameter(runContext.Parameters, "documentTypeHint")
                ?? ExtractDocumentTypeFromLayer1Output(runContext);

            var routing = await _insightsRouter.ResolveLayer2ActionAsync(
                practiceAreaHint, documentTypeHint, action, cancellationToken).ConfigureAwait(false);

            return routing.Decision switch
            {
                InsightsLayer2RoutingDecision.GateFailNullActionCode => (action, true),
                InsightsLayer2RoutingDecision.UsePerPairAction => (routing.Action, false),
                _ => (action, false)
            };
        }

        return (action, false);
    }

    /// <summary>
    /// Safe lookup of a string parameter from the run context's parameter dictionary.
    /// Returns null when missing or empty; never throws.
    /// </summary>
    private static string? TryGetParameter(IReadOnlyDictionary<string, string>? parameters, string key)
    {
        if (parameters is null) return null;
        return parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>
    /// Extract <c>document_type_code</c> from the upstream <c>layer1</c> node's structured
    /// output. The Layer 1 prompt emits a JSON object shape
    /// <c>{ document_type_code, signals { ... }, confidence, rationale }</c> per
    /// design-a3 §5.1. Returns null when the layer1 output is absent or malformed —
    /// caller falls back to the default action.
    /// </summary>
    private string? ExtractDocumentTypeFromLayer1Output(PlaybookRunContext runContext)
    {
        var layer1Output = runContext.GetOutput(UniversalIngestLayer1OutputVariable);
        if (layer1Output is null || !layer1Output.Success || layer1Output.StructuredData is null)
        {
            return null;
        }

        try
        {
            var root = layer1Output.StructuredData.Value;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Try canonical key first (design-a3 §5.1), then a couple of common
            // case variants in case prompt authors emit camelCase.
            if (TryReadStringProperty(root, "document_type_code", out var v1)) return v1;
            if (TryReadStringProperty(root, "documentTypeCode", out var v2)) return v2;
            if (TryReadStringProperty(root, "classification", out var v3)) return v3;

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "PlaybookOrchestrationService.ExtractDocumentTypeFromLayer1Output: malformed layer1 output; falling back to default Layer 2 action.");
            return null;
        }
    }

    private static bool TryReadStringProperty(JsonElement root, string propertyName, out string? value)
    {
        if (root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Substitute <c>{{paramName}}</c> placeholders in the node's ConfigJson with values
    /// from the run parameters. Returns a NEW PlaybookNodeDto record with the substituted
    /// ConfigJson; never mutates the input. Idempotent — placeholders without matching
    /// parameters are left intact (the executor surfaces the original string for clear
    /// authoring feedback).
    /// </summary>
    /// <remarks>
    /// Per Insights Engine r2 Wave B (2026-06-02): adds centralized template substitution
    /// previously missing from the orchestration pipeline. Without it, playbook nodes like
    /// <c>resolveLiveFacts</c> received literal <c>"matter:{{matterId}}"</c> from ConfigJson
    /// and failed at <c>LiveFactResolver.ParseMatterSubject</c> with
    /// <c>LiveFactNotSupportedException</c>, returning a scaffold "no-artifact-produced"
    /// decline to the caller. Centralized fix applies uniformly to all executor types.
    /// </remarks>
    private static PlaybookNodeDto ApplyConfigJsonTemplates(
        PlaybookNodeDto node,
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return node;
        if (string.IsNullOrEmpty(node.ConfigJson) || !node.ConfigJson.Contains("{{", StringComparison.Ordinal))
            return node;

        var rendered = node.ConfigJson;
        foreach (var kvp in parameters)
        {
            if (string.IsNullOrEmpty(kvp.Key)) continue;
            var placeholder = "{{" + kvp.Key + "}}";
            if (rendered.Contains(placeholder, StringComparison.Ordinal))
            {
                rendered = rendered.Replace(placeholder, kvp.Value ?? string.Empty, StringComparison.Ordinal);
            }
        }

        return node with { ConfigJson = rendered };
    }

    #endregion
}
