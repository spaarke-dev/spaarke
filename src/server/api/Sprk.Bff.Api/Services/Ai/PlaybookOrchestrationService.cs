using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;
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
    private readonly ITemplateEngine _templateEngine;
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
        ITemplateEngine templateEngine,
        ILogger<PlaybookOrchestrationService> logger,
        Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter? contextEventEmitter = null)
    {
        _nodeService = nodeService;
        _executorRegistry = executorRegistry;
        _scopeResolver = scopeResolver;
        _legacyOrchestrator = legacyOrchestrator;
        _insightsRouter = insightsRouter ?? throw new ArgumentNullException(nameof(insightsRouter));
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
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
    /// <remarks>
    /// R7 task 025 (2026-06-28, FR-08): PRESERVED — sole remaining caller is
    /// <see cref="CollectDownstreamNodeInfo"/> for <c>$choices</c> option-set
    /// resolution on downstream UpdateRecord nodes. This is NOT structural
    /// dispatch fallback (which was deleted) — it's payload introspection
    /// for cross-node option-set hydration. Spec FR-08 scope is the dispatch
    /// ladder; this helper survives that scope per task 024 caller audit.
    /// </remarks>
    private static ExecutorType? ExtractActionTypeFromConfig(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("__actionType", out var actionTypeProp) &&
                actionTypeProp.TryGetInt32(out var actionTypeInt))
            {
                return (ExecutorType)actionTypeInt;
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

            // R7 Wave 2 task 024 (FR-07) — SINGLE-HOP DISPATCH.
            // Dispatch reads `node.SprkExecutortype` (sprk_executortype Choice column) DIRECTLY.
            // The legacy 3-layer chain (`node.actionid` → `Action.actiontypeid` → `lookup_row.executoractiontype`)
            // and structural fallback ladder have been removed. Per FR-19, all
            // production nodes are backfilled (Wave 5 task 054); a null sprk_executortype
            // indicates an unmigrated row and MUST throw rather than silently fall back —
            // silent fallback would defeat the refactor.
            //
            // Action FK is still required for prompt-driven executors (AiAnalysis, AiCompletion,
            // AiEmbedding) via per-executor `Validate()`; it carries the SystemPrompt + OutputSchema.
            // The orchestrator resolves it as payload, not as dispatch source.
            //
            // R7 task 025 (FR-08, 2026-06-28): structural fallback ladder helpers
            // (IsDeployedStartNode / IsDeployedLoadKnowledgeNode / IsDeployedReturnResponseNode)
            // DELETED. ExtractActionTypeFromConfig PRESERVED — sole remaining caller is
            // CollectDownstreamNodeInfo for $choices option-set hydration (payload introspection,
            // not dispatch).
            //
            // R7 task 026 (FR-09, 2026-06-28): Action.ExecutorType override branch
            // ("Action ActionType is canonical regardless of NodeType" — Insights Engine
            // r2 Wave B legacy) was inline-deleted by task 024 when ExecuteNodeAsync moved
            // to single-hop dispatch. No standalone code block remains to delete; closure
            // confirmed.

            ExecutorType actionType;
            if (node.SprkExecutortype.HasValue)
            {
                actionType = node.SprkExecutortype.Value;
            }
            else
            {
                var errorMsg = $"Node '{node.Name}' (id={node.Id}) has null sprk_executortype — backfill required per FR-19. Single-hop dispatch (FR-07) does not fall back to Action lookup.";
                var errorOutput = NodeOutput.Error(node.Id, node.OutputVariable, errorMsg, NodeErrorCodes.InvalidConfiguration);
                runContext.StoreNodeOutput(errorOutput);

                await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                    runContext.RunId, runContext.PlaybookId, node.Id, node.Name, errorMsg), cancellationToken);

                EmitNodeCompleted("failed"); // R6 Pillar 6c (FR-37 / task 063)

                return errorOutput;
            }

            // Resolve scopes (skills, knowledge, tools) for AI nodes only.
            // Structural nodes (Control / Output / Workflow) have no resolved scopes.
            // Use NodeType as the coarse category indicator until Wave 8 collapses it into ExecutorType.
            ResolvedScopes scopes = node.NodeType == NodeType.AIAnalysis
                ? await _scopeResolver.ResolveNodeScopesAsync(node.Id, runContext.CancellationToken)
                : new ResolvedScopes([], [], []);

            // Resolve Action payload (SystemPrompt + OutputSchema) when Action FK is set.
            // Prompt-driven executors validate the Action presence in their own Validate().
            AnalysisAction action;
            if (node.ActionId != Guid.Empty)
            {
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
            }
            else
            {
                // Structural nodes (Start, Condition, ReturnResponse, DeliverOutput, etc.) without
                // an Action FK get a synthetic AnalysisAction shell so the existing NodeExecutionContext
                // contract (Action non-null) is preserved. Per-executor Validate() enforces FR-13
                // invariants (e.g., StartNodeExecutor.Validate does NOT require an Action; AiCompletionNodeExecutor.Validate
                // DOES require one). The ExecutorType field carries the dispatch type for diagnostic display only —
                // dispatch itself comes from `node.SprkExecutortype` above.
                action = new AnalysisAction
                {
                    Id = Guid.Empty,
                    Name = node.Name,
                    ExecutorType = actionType
                };

                _logger.LogDebug(
                    "Structural node '{NodeName}' (NodeType={NodeType}, ExecutorType={ExecutorType}) — no Action FK, no scopes",
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

            // R7 Wave 11 task 114: fan-out iteration detection BEFORE Layer 1 template
            // resolution. If the node's RAW configJson declares iteration.iterateOver +
            // iteration.itemAlias, run the executor N times (one per element of iterateOver)
            // with a per-iteration overlay context. Outputs collected into an ordered
            // array; aggregated into a single composite NodeOutput. Per ADR-037 +
            // SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md. Sequential v1 (parallelism deferred).
            // Detection-on-RAW-configJson is intentional: the iteration metadata is itself
            // templated (`iterateOver: "{{channelRegistry.channels}}"`), so we must read it
            // before Layer 1 rewrites it into a resolved string.
            if (TryExtractIterationConfig(node.ConfigJson, out var iterateOverExpr, out var itemAlias))
            {
                var compositeOutput = await ExecuteFanOutIterationAsync(
                    runContext, node, action, scopes, actionType, executor, downstreamNodes,
                    iterateOverExpr!, itemAlias!, writer, cancellationToken).ConfigureAwait(false);

                runContext.StoreNodeOutput(compositeOutput);

                if (compositeOutput.Success)
                {
                    _logger.LogInformation(
                        "Fan-out node {NodeName} completed: {Iterations} iterations, total duration: {Duration}ms",
                        node.Name, compositeOutput.ToolResults.Count, compositeOutput.Metrics.DurationMs);
                    await writer.WriteAsync(PlaybookStreamEvent.NodeCompleted(
                        runContext.RunId, runContext.PlaybookId, node.Id, node.Name, compositeOutput), cancellationToken);
                }
                else
                {
                    await writer.WriteAsync(PlaybookStreamEvent.NodeFailed(
                        runContext.RunId, runContext.PlaybookId, node.Id, node.Name,
                        compositeOutput.ErrorMessage ?? "Iteration failed"), cancellationToken);
                }

                EmitNodeCompleted(compositeOutput.Success ? "completed" : "failed");
                await ScanForUnrenderedTemplatesAsync(runContext, node, compositeOutput, writer, cancellationToken).ConfigureAwait(false);
                return compositeOutput;
            }

            var substitutedNode = ApplyConfigJsonTemplates(node, runContext);

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

                // FR-53 / chat-routing-redesign-r1 task 114a — per-section SSE streaming.
                // When the completed node is a DeliverComposite node, re-iterate the composed
                // section list and emit `section_started` → `section_data` → `section_completed`
                // for each section, keyed by section name (NOT schema position). The emission
                // is APPENDED to (not REPLACING) the standard `NodeCompleted` event — so all
                // existing consumers see unchanged behavior.
                //
                // Backward-compat invariant: emits ONLY for actionType == DeliverComposite.
                // Existing NodeType.Output → DeliverOutput path emits ZERO section events; its
                // `FieldDelta` stream (via PlaybookExecutionEngine.ExecuteChatSummarizeAsync)
                // continues UNCHANGED until migrated by FR-58 (task 118R).
                if (actionType == ExecutorType.DeliverComposite)
                {
                    await EmitDeliverCompositeSectionEventsAsync(
                        runContext, node, output, writer, cancellationToken).ConfigureAwait(false);
                }
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
            if (dependentActionType != Nodes.ExecutorType.UpdateRecord)
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
    /// <summary>
    /// Emits per-section SSE stream events for a completed
    /// <see cref="NodeType.DeliverComposite"/> node (FR-53 /
    /// chat-routing-redesign-r1 task 114a). For each
    /// <see cref="CompositeSectionResult"/> in the composite payload (in completion
    /// order), emits three events: <c>section_started</c> → <c>section_data</c> →
    /// <c>section_completed</c>, all keyed by the section's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Approach A (orchestrator-emits)</b>: keeps the executor
    /// (<see cref="DeliverCompositeNodeExecutor"/>) pure (returns structured data) and
    /// localizes streaming concern to the orchestrator (ADR-013 separation of concerns).
    /// </para>
    /// <para>
    /// <b>Phase A emission</b>: composite sections today are non-streaming (the upstream
    /// executor produces consolidated content). One emission per section per lifecycle
    /// stage — total of 3*N events for an N-section composite payload. A future
    /// incremental-streaming phase (when individual composite-feeding nodes start
    /// emitting partial outputs) emits multiple <c>section_data</c> events per section,
    /// all sharing the same <c>SectionName</c>.
    /// </para>
    /// <para>
    /// <b>Empty-sections safety</b>: when the composite produces zero sections (per
    /// FR-52 "a partial composite is still a valid composite"), no <c>section_*</c>
    /// events are emitted — the <c>NodeCompleted</c> event already in the stream is
    /// sufficient. The frontend handles a composite payload with zero sections.
    /// </para>
    /// <para>
    /// <b>ADR-015 tier-1 telemetry</b>: logs <c>(sectionCount, sectionNames=[...],
    /// totalLatencyMs)</c> — section names are deterministic configuration identifiers
    /// (safe). Section <i>content</i> is NEVER logged — it flows on the SSE wire which
    /// is the canonical record.
    /// </para>
    /// <para>
    /// <b>Malformed-payload safety</b>: if <see cref="NodeOutput.StructuredData"/>
    /// cannot be deserialized to a <see cref="CompositeOutputPayload"/>, logs a warning
    /// and returns (no events emitted). Emission is best-effort; downstream consumers
    /// rely on the <c>NodeCompleted</c> event for canonical success signaling.
    /// </para>
    /// </remarks>
    private async Task EmitDeliverCompositeSectionEventsAsync(
        PlaybookRunContext runContext,
        PlaybookNodeDto node,
        NodeOutput output,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        // Deserialize the composite payload from NodeOutput.StructuredData.
        // The executor (DeliverCompositeNodeExecutor) places a CompositeOutputPayload here.
        CompositeOutputPayload? payload;
        try
        {
            payload = output.GetData<CompositeOutputPayload>();
        }
        catch (JsonException ex)
        {
            // Defensive: if a future change produces malformed StructuredData, surface a
            // warning + skip emission rather than aborting the run. The NodeCompleted
            // event is already on the stream — consumers can still proceed.
            _logger.LogWarning(ex,
                "DeliverComposite node {NodeId} ({NodeName}): failed to deserialize CompositeOutputPayload " +
                "from NodeOutput.StructuredData — skipping per-section SSE emission. RunId={RunId}",
                node.Id, node.Name, runContext.RunId);
            return;
        }

        if (payload is null || payload.Sections is null || payload.Sections.Count == 0)
        {
            // Empty composite per FR-52 is valid — no section events to emit. NodeCompleted
            // already signals overall success.
            _logger.LogDebug(
                "DeliverComposite node {NodeId} ({NodeName}): composite payload has zero sections — " +
                "no per-section SSE events to emit. RunId={RunId}",
                node.Id, node.Name, runContext.RunId);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var totalSections = payload.Sections.Count;
        var sectionNames = new List<string>(totalSections);

        for (var i = 0; i < totalSections; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var section = payload.Sections[i];
            sectionNames.Add(section.SectionName);

            // section_started — announce the section's start with position metadata.
            var startedData = new SectionStartedSseEventData(
                SectionName: section.SectionName,
                DisplayLabel: section.DisplayLabel,
                SectionIndex: i,
                TotalSections: totalSections);

            await writer.WriteAsync(PlaybookStreamEvent.SectionStarted(
                runContext.RunId, runContext.PlaybookId, node.Id, node.Name, startedData),
                cancellationToken).ConfigureAwait(false);

            // section_data — emit one consolidated content event for Phase A. Future
            // incremental phases emit multiple deltas per section, all sharing the same
            // SectionName.
            var dataData = new SectionDataSseEventData(
                SectionName: section.SectionName,
                TextDelta: section.TextContent,
                StructuredData: section.StructuredData);

            await writer.WriteAsync(PlaybookStreamEvent.SectionData(
                runContext.RunId, runContext.PlaybookId, node.Id, node.Name, dataData),
                cancellationToken).ConfigureAwait(false);

            // section_completed — finalize with idempotent re-emission of the section's
            // final state so frontends that drop the intermediate section_data event still
            // render correctly.
            var completedData = new SectionCompletedSseEventData(
                SectionName: section.SectionName,
                FinalText: section.TextContent,
                FinalStructuredData: section.StructuredData,
                SourceNodeId: section.SourceNodeId == Guid.Empty ? null : section.SourceNodeId);

            await writer.WriteAsync(PlaybookStreamEvent.SectionCompleted(
                runContext.RunId, runContext.PlaybookId, node.Id, node.Name, completedData),
                cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();

        // ADR-015 tier-1 telemetry: section names are deterministic configuration identifiers
        // (safe to log); section content is NOT duplicated into logs (SSE wire is canonical).
        _logger.LogInformation(
            "DeliverComposite node {NodeId} ({NodeName}) per-section SSE emission completed: " +
            "sectionCount={SectionCount}, sectionNames=[{SectionNames}], totalLatencyMs={LatencyMs}, " +
            "RunId={RunId}",
            node.Id, node.Name, totalSections, string.Join(",", sectionNames),
            stopwatch.ElapsedMilliseconds, runContext.RunId);
    }

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
    /// R7 Wave 11 task 114: detects fan-out iteration metadata in a node's RAW configJson
    /// (BEFORE Layer 1 template resolution rewrites it). Returns true + outputs the
    /// iterateOver template expression + itemAlias when the node has
    /// <c>iteration.iterateOver</c> + <c>iteration.itemAlias</c> declared.
    /// </summary>
    /// <remarks>
    /// Detection-on-RAW is intentional: the iteration metadata is itself templated
    /// (<c>iterateOver: "{{channelRegistry.channels}}"</c>), so we must read it BEFORE
    /// Layer 1 rewrites it into a resolved string. Defensive: missing iteration block,
    /// missing keys, or malformed JSON → returns false (single-call path).
    /// </remarks>
    private static bool TryExtractIterationConfig(
        string? configJson,
        out string? iterateOverExpr,
        out string? itemAlias)
    {
        iterateOverExpr = null;
        itemAlias = null;

        if (string.IsNullOrWhiteSpace(configJson) || !configJson.Contains("iteration", StringComparison.Ordinal))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("iteration", out var iteration)
                || iteration.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!iteration.TryGetProperty("iterateOver", out var iterOver) || iterOver.ValueKind != JsonValueKind.String)
                return false;

            if (!iteration.TryGetProperty("itemAlias", out var alias) || alias.ValueKind != JsonValueKind.String)
                return false;

            iterateOverExpr = iterOver.GetString();
            itemAlias = alias.GetString();
            return !string.IsNullOrWhiteSpace(iterateOverExpr) && !string.IsNullOrWhiteSpace(itemAlias);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// R7 Wave 11 task 114 (ADR-037 fan-out semantic): runs the node's executor N times,
    /// one per element of the resolved iterateOver collection. Per iteration the configJson
    /// is rendered against a context overlay where <paramref name="itemAlias"/> binds the
    /// current iteration's item. Per-iteration NodeOutputs are aggregated into a single
    /// composite NodeOutput whose StructuredData is a JsonElement-array — downstream nodes
    /// reference the aggregate via <c>{{node.OutputVariable}}</c> as if it were a single
    /// node's output containing an array. Sequential execution v1 (parallelism deferred).
    /// </summary>
    private async Task<NodeOutput> ExecuteFanOutIterationAsync(
        PlaybookRunContext runContext,
        PlaybookNodeDto node,
        AnalysisAction action,
        ResolvedScopes scopes,
        ExecutorType actionType,
        INodeExecutor executor,
        IReadOnlyList<DownstreamNodeInfo>? downstreamNodes,
        string iterateOverExpr,
        string itemAlias,
        ChannelWriter<PlaybookStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // 1. Resolve the iterateOver expression. Wrap in {{ }} if missing, then auto-wrap
        //    with the json helper (Option D pattern) so the rendered output is JSON-parseable
        //    array text even when iterateOver references a bare variable like
        //    `{{channelRegistry.channels}}` (which would otherwise stringify as Dictionary garbage).
        var wrappedExpr = iterateOverExpr.Contains("{{", StringComparison.Ordinal)
            ? iterateOverExpr
            : "{{" + iterateOverExpr + "}}";
        if (IsPureTemplate(wrappedExpr))
        {
            wrappedExpr = AutoWrapWithJsonHelper(wrappedExpr);
        }
        var baseContext = PlaybookTemplateContextBuilder.Build(runContext);
        string resolvedIterateOver;
        try
        {
            resolvedIterateOver = _templateEngine.Render(wrappedExpr, baseContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Fan-out node {NodeName}: failed to render iterateOver expression '{Expr}'; treating as empty iteration",
                node.Name, iterateOverExpr);
            return NodeOutput.Ok(
                node.Id,
                node.OutputVariable,
                data: Array.Empty<object>(),
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        // 2. Parse the rendered iterateOver as JSON to recover the array.
        var iterationItems = TryParseIterationItems(resolvedIterateOver, node.Name);

        // 3. Strip the iteration block from configJson so the executor doesn't see it.
        var configWithoutIteration = StripIterationBlock(node.ConfigJson);

        // 4. Per-iteration execution.
        var perIterationOutputs = new List<JsonElement?>();
        var failedIterations = new List<string>();
        var iterIndex = 0;
        foreach (var item in iterationItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Build overlay context: base + { [itemAlias]: currentItem }
            var overlay = new Dictionary<string, object?>(baseContext, StringComparer.Ordinal)
            {
                [itemAlias] = item
            };

            string renderedConfigJson;
            try
            {
                renderedConfigJson = _templateEngine.Render(configWithoutIteration ?? string.Empty, overlay);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Fan-out node {NodeName}: failed to render configJson for iteration {Index}; skipping",
                    node.Name, iterIndex);
                failedIterations.Add($"iteration[{iterIndex}]: render failed");
                iterIndex++;
                continue;
            }

            var iterationNode = node with { ConfigJson = renderedConfigJson };
            var iterationNodeContext = runContext.CreateNodeContext(iterationNode, action, scopes, actionType) with
            {
                DownstreamNodes = downstreamNodes
            };

            var validation = executor.Validate(iterationNodeContext);
            if (!validation.IsValid)
            {
                failedIterations.Add($"iteration[{iterIndex}]: validation failed: {string.Join("; ", validation.Errors)}");
                iterIndex++;
                continue;
            }

            NodeOutput iterationOutput;
            try
            {
                iterationOutput = await executor.ExecuteAsync(iterationNodeContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Fan-out node {NodeName} iteration {Index} threw; recording failure",
                    node.Name, iterIndex);
                failedIterations.Add($"iteration[{iterIndex}]: {ex.GetType().Name}: {ex.Message}");
                iterIndex++;
                continue;
            }

            perIterationOutputs.Add(iterationOutput.StructuredData);
            if (!iterationOutput.Success)
            {
                failedIterations.Add($"iteration[{iterIndex}]: {iterationOutput.ErrorMessage}");
            }
            iterIndex++;
        }

        // 5. Aggregate per-iteration outputs into a single composite NodeOutput.
        var arrayData = perIterationOutputs
            .Select(je => je is JsonElement v && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined
                ? TemplateEngine.ConvertJsonElement(v)
                : null)
            .ToArray();

        var allSucceeded = failedIterations.Count == 0;
        var metrics = NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow);

        if (allSucceeded)
        {
            return NodeOutput.Ok(
                nodeId: node.Id,
                outputVariable: node.OutputVariable,
                data: arrayData,
                metrics: metrics,
                toolResults: PadToolResults(perIterationOutputs.Count));
        }
        else
        {
            var errMsg = $"Fan-out completed with {failedIterations.Count}/{iterIndex} failed iteration(s): " +
                         string.Join(" | ", failedIterations.Take(3));
            return NodeOutput.Error(node.Id, node.OutputVariable, errMsg, NodeErrorCodes.InternalError, metrics);
        }
    }

    /// <summary>
    /// Parses the rendered iterateOver value into an enumeration of items.
    /// Accepts JSON-array text (from <c>{{json X}}</c> or raw collection rendering).
    /// Defensive: malformed / non-array / null → empty enumeration.
    /// </summary>
    private IEnumerable<object?> TryParseIterationItems(string? renderedValue, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(renderedValue))
            return Array.Empty<object?>();

        try
        {
            using var doc = JsonDocument.Parse(renderedValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(e => TemplateEngine.ConvertJsonElement(e))
                    .ToList();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex,
                "Fan-out node {NodeName}: iterateOver rendered to non-JSON string; treating as empty iteration",
                nodeName);
        }
        return Array.Empty<object?>();
    }

    /// <summary>
    /// Returns a copy of the configJson with the <c>iteration</c> top-level property removed,
    /// so executors don't receive iteration metadata in their configJson.
    /// </summary>
    private static string? StripIterationBlock(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return configJson;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return configJson;

            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "iteration", StringComparison.Ordinal))
                        continue;
                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return configJson; // defensive: malformed → pass through unchanged
        }
    }

    /// <summary>
    /// Placeholder ToolResults list sized to per-iteration count (used to signal iteration
    /// count to log lines that read ToolResults.Count without semantic meaning).
    /// </summary>
    private static IReadOnlyList<ToolResult> PadToolResults(int count)
        => count <= 0 ? Array.Empty<ToolResult>() : new ToolResult[count];

    /// <summary>
    /// Resolve Handlebars-style placeholders (<c>{{X}}</c>, <c>{{X.Y.Z}}</c>, custom helpers)
    /// in the node's ConfigJson against the merged playbook run context (Parameters +
    /// NodeOutputs + run metadata). Returns a NEW <see cref="PlaybookNodeDto"/> record with the
    /// rendered ConfigJson; never mutates the input. Missing references render as empty string
    /// (graceful per Handlebars configuration).
    /// </summary>
    /// <remarks>
    /// <para>
    /// R7 Wave 11 (FR-15 / task 111 / Option B, 2026-06-29): Layer 1 of the two-layer architecture.
    /// Replaces the prior literal <c>Replace</c> loop with <see cref="ITemplateEngine.Render"/>
    /// against a merged context built by <see cref="PlaybookTemplateContextBuilder"/>. Every
    /// executor's configJson now benefits uniformly — no per-executor template-resolution code.
    /// </para>
    /// <para>
    /// Pre-Wave-11 behavior preserved: <c>{{paramName}}</c> against Parameters still substitutes
    /// (Handlebars handles flat <c>{{key}}</c> natively against the merged context dict).
    /// </para>
    /// <para>
    /// Per Insights Engine r2 Wave B (2026-06-02): the original centralized substitution prevented
    /// playbook nodes like <c>resolveLiveFacts</c> from receiving literal
    /// <c>"matter:{{matterId}}"</c> and failing at <c>LiveFactResolver.ParseMatterSubject</c>.
    /// Option B (R7 task 111) extends that fix to support <c>{{nodeName.field.subfield}}</c>
    /// references against prior node outputs, enabling the DAILY-BRIEFING-NARRATE playbook's
    /// rich template expressions to resolve end-to-end. Bug being closed: <c>/narrate</c> HTTP 200
    /// with empty content (LLM nodes saw literal <c>{{json start}}</c> as their input).
    /// </para>
    /// </remarks>
    private PlaybookNodeDto ApplyConfigJsonTemplates(
        PlaybookNodeDto node,
        PlaybookRunContext runContext)
    {
        if (string.IsNullOrEmpty(node.ConfigJson) || !node.ConfigJson.Contains("{{", StringComparison.Ordinal))
            return node;

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        // R7 Wave 11 Option D (operator-approved 2026-06-29): JSON-aware substitution.
        // The configJson is parsed as a JSON tree. For each STRING value:
        //   - Pure-template value (entire string is one `{{...}}` expression):
        //     render via Handlebars, then try parse the rendered result as JSON. If the
        //     rendered output is a valid JSON value (array/object/number/bool/null),
        //     replace this property's value with the native JSON shape instead of a
        //     string. This eliminates the "string-encoded array" problem at quoted
        //     template positions like `"allowList": "{{distinct (concat ...)}}"`.
        //   - Mixed value (template + literal text):
        //     render normally; the result stays a string (existing behavior).
        // Net effect: the engine produces VALID configJson of the correct shape, every
        // time, with no executor-side workarounds. Future narrative consumers benefit.
        // See docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md §3 (Layer 1).
        string rendered;
        try
        {
            rendered = RenderConfigJsonStructurally(node.ConfigJson, context);
        }
        catch (JsonException ex)
        {
            // configJson is not valid JSON — fall back to flat string substitution
            // (preserves the previous behavior for non-JSON configJson values).
            _logger.LogWarning(ex,
                "ApplyConfigJsonTemplates: configJson is not valid JSON for node {NodeId}; falling back to flat string substitution",
                node.Id);
            rendered = _templateEngine.Render(node.ConfigJson, context);
        }

        return node with { ConfigJson = rendered };
    }

    /// <summary>
    /// R7 Wave 11 Option D (operator-approved 2026-06-29): JSON-aware template substitution.
    /// Walks the parsed configJson tree depth-first; for each string value that is a
    /// "pure template" (entire string is a single <c>{{...}}</c> expression), renders the
    /// template via the engine and parses the result as JSON if possible — so an
    /// IEnumerable helper result becomes a native JSON array, an object helper result
    /// becomes a native JSON object, etc. Mixed values (text-with-template) render as
    /// strings per the existing behavior.
    /// </summary>
    private string RenderConfigJsonStructurally(string configJson, Dictionary<string, object?> context)
    {
        using var sourceDoc = JsonDocument.Parse(configJson);
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteJsonElementWithTemplateExpansion(writer, sourceDoc.RootElement, context);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Recursive depth-first walker. For Object/Array nodes, descends into children.
    /// For String nodes, expands templates per the pure-vs-mixed rule described above.
    /// For other scalar nodes (Number/Bool/Null), writes verbatim.
    /// </summary>
    private void WriteJsonElementWithTemplateExpansion(
        Utf8JsonWriter writer,
        JsonElement element,
        Dictionary<string, object?> context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteJsonElementWithTemplateExpansion(writer, prop.Value, context);
                }
                writer.WriteEndObject();
                return;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonElementWithTemplateExpansion(writer, item, context);
                }
                writer.WriteEndArray();
                return;

            case JsonValueKind.String:
                var raw = element.GetString() ?? string.Empty;
                if (!raw.Contains("{{", StringComparison.Ordinal))
                {
                    writer.WriteStringValue(raw);
                    return;
                }

                // R7 W12 2026-07-02: nested-JSON string preservation. When a string value
                // contains embedded JSON (starts with `{` or `[`) AND has template markers,
                // the target executor's ParseConfig re-parses the string as JSON and does
                // its own per-value template render — with the correct executor-local
                // context shape (e.g. UpdateRecordNodeExecutor wraps outputs in `.output/.text/.success`).
                // Rendering this string at Layer 1 with Handlebars corrupts the enclosed JSON
                // whenever a substituted value contains characters that need JSON-escaping
                // (a `"`, backslash, or unescaped newline). Skip Layer 1 rendering here — the
                // executor handles it. Top-level configJson templates (pure-template pattern
                // and non-JSON mixed strings) are unaffected and continue to render as before.
                var trimmedRaw = raw.AsSpan().TrimStart();
                if (trimmedRaw.Length > 0 && (trimmedRaw[0] == '{' || trimmedRaw[0] == '['))
                {
                    writer.WriteStringValue(raw);
                    return;
                }

                if (IsPureTemplate(raw))
                {
                    // R7 Wave 11 Option D auto-wrap: source authors write natural Handlebars
                    // (`{{tldrResult}}`, `{{start.channels}}`, `{{distinct (concat …)}}`).
                    // The engine wraps with the `json` helper so the rendered output is
                    // ALWAYS JSON-parseable text — scalars become JSON scalars, objects
                    // become JSON objects, arrays become JSON arrays. No source-author
                    // burden to remember when to use {{json X}} vs {{X}}.
                    //
                    // After wrap+render+parse, the property value is the native JSON shape
                    // (object/array/number/bool/null/string), not Dictionary.ToString() garbage.
                    var wrappedTemplate = AutoWrapWithJsonHelper(raw);
                    var renderedJson = _templateEngine.Render(wrappedTemplate, context);
                    if (TryParseAsJson(renderedJson, out var parsedElement))
                    {
                        WriteJsonElementVerbatim(writer, parsedElement!.Value);
                        return;
                    }
                    // Should be rare: the json helper failed to produce parseable JSON.
                    // Fall through to render-as-string for graceful degradation.
                }

                var renderedString = _templateEngine.Render(raw, context);
                writer.WriteStringValue(renderedString);
                return;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                return;

            default:
                // Number / True / False — write verbatim.
                element.WriteTo(writer);
                return;
        }
    }

    /// <summary>
    /// R7 Wave 11 Option D auto-wrap: takes a pure-template string (e.g. <c>"{{tldrResult}}"</c>
    /// or <c>"{{distinct (concat ...)}}"</c>) and wraps it with the <c>{{json …}}</c> helper
    /// so the rendered output is always JSON-parseable text — regardless of whether the
    /// inner expression returns a scalar, object, array, or helper-composed result.
    /// </summary>
    /// <remarks>
    /// Detection: if the inner expression already uses the json helper (e.g.
    /// <c>"{{json start}}"</c>), returns the original unchanged. Otherwise wraps the inner
    /// expression: <c>"{{json &lt;inner&gt;}}"</c>. The json helper accepts any value
    /// (variable, helper-call, scalar) and produces JSON-encoded text.
    /// </remarks>
    private static string AutoWrapWithJsonHelper(string pureTemplate)
    {
        var trimmed = pureTemplate.Trim();
        // Strip the outer {{ and }}; inner is what's between them.
        var inner = trimmed.Substring(2, trimmed.Length - 4).Trim();
        if (inner.StartsWith("json ", StringComparison.Ordinal) || inner.StartsWith("json(", StringComparison.Ordinal))
        {
            return pureTemplate; // already wrapped
        }
        // If inner contains whitespace at top level (helper call like `distinct X` or
        // `join '\n' X Y`), wrap with parens to make it a subexpression — `{{json (inner)}}`
        // — so the inner helper's return value becomes the first arg to json.
        // For single-token variable references like `tldrResult` or `start.channels`, no
        // parens needed — Handlebars treats `{{json varName}}` as json(varName).
        var needsParens = ContainsTopLevelWhitespace(inner);
        return needsParens
            ? "{{json (" + inner + ")}}"
            : "{{json " + inner + "}}";
    }

    /// <summary>
    /// Returns true if <paramref name="expr"/> contains whitespace at the top level
    /// (not inside nested parens or quotes). Used to distinguish helper-call expressions
    /// (e.g. <c>distinct X</c>) from variable references (e.g. <c>start.channels</c>).
    /// </summary>
    private static bool ContainsTopLevelWhitespace(string expr)
    {
        var parenDepth = 0;
        var inSingle = false;
        var inDouble = false;
        foreach (var c in expr)
        {
            if (inSingle) { if (c == '\'') inSingle = false; continue; }
            if (inDouble) { if (c == '"') inDouble = false; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '"') { inDouble = true; continue; }
            if (c == '(') { parenDepth++; continue; }
            if (c == ')') { parenDepth--; continue; }
            if (parenDepth == 0 && char.IsWhiteSpace(c)) return true;
        }
        return false;
    }

    /// <summary>
    /// Tests whether a string value is a "pure template" — the entire value (after
    /// trimming whitespace) is a single Handlebars expression. Detection rule:
    /// trimmed string starts with <c>{{</c> and ends with <c>}}</c>, AND there is
    /// exactly ONE outermost <c>{{…}}</c> span (no literal text interleaved).
    /// </summary>
    /// <remarks>
    /// We approximate by counting brace pairs: trimmed `{{X}}` is pure;
    /// `{{X}}{{Y}}` is mixed (two top-level templates) — render as string.
    /// `prefix{{X}}` is mixed (literal prefix) — render as string.
    /// </remarks>
    private static bool IsPureTemplate(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{{", StringComparison.Ordinal)) return false;
        if (!trimmed.EndsWith("}}", StringComparison.Ordinal)) return false;
        // Walk to find the matching outermost `}}` for the leading `{{`. If it's at the
        // END of the trimmed string, this is a pure template; otherwise mixed.
        var depth = 0;
        var i = 0;
        while (i < trimmed.Length - 1)
        {
            if (trimmed[i] == '{' && trimmed[i + 1] == '{')
            {
                depth++;
                i += 2;
                continue;
            }
            if (trimmed[i] == '}' && trimmed[i + 1] == '}')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i == trimmed.Length;
                }
                continue;
            }
            i++;
        }
        return false;
    }

    /// <summary>
    /// Attempts to parse <paramref name="value"/> as a JSON document. Returns true and
    /// sets <paramref name="parsed"/> to the (cloned, detached) RootElement when the value
    /// is a valid JSON value (array, object, number, bool, null). Bare strings without
    /// surrounding quotes are NOT considered JSON here — they fall through to the
    /// string-render path.
    /// </summary>
    private static bool TryParseAsJson(string value, out JsonElement? parsed)
    {
        parsed = null;
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        // Cheap pre-check: only object/array/number/bool/null shapes — bare strings
        // (no quotes) should remain as strings (caller writes them as JSON string values).
        var first = trimmed[0];
        if (first != '{' && first != '[' && first != '-'
            && !(first >= '0' && first <= '9')
            && !trimmed.StartsWith("true", StringComparison.Ordinal)
            && !trimmed.StartsWith("false", StringComparison.Ordinal)
            && !trimmed.StartsWith("null", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            parsed = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a JsonElement verbatim to the writer (handles all ValueKinds).
    /// </summary>
    private static void WriteJsonElementVerbatim(Utf8JsonWriter writer, JsonElement element)
    {
        element.WriteTo(writer);
    }

    #endregion
}
