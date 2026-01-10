using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Shared state for a playbook execution run.
/// Passed between nodes and used for template variable substitution.
/// </summary>
/// <remarks>
/// <para>
/// PlaybookRunContext maintains:
/// </para>
/// <list type="bullet">
/// <item>Run metadata (IDs, timestamps)</item>
/// <item>Document context (extracted text, shared across nodes)</item>
/// <item>Node outputs (dictionary of outputVariable → NodeOutput)</item>
/// <item>User parameters (for template substitution)</item>
/// <item>Cancellation state</item>
/// </list>
/// <para>
/// Thread-safety: Uses ConcurrentDictionary for node outputs as nodes
/// may complete in parallel batches (Phase 3+).
/// </para>
/// </remarks>
public class PlaybookRunContext
{
    private readonly ConcurrentDictionary<string, NodeOutput> _nodeOutputs = new();
    private readonly CancellationTokenSource _cancellationSource;

    /// <summary>
    /// Creates a new run context.
    /// </summary>
    public PlaybookRunContext(
        Guid runId,
        Guid playbookId,
        Guid[] documentIds,
        HttpContext httpContext,
        string? userContext = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        RunId = runId;
        PlaybookId = playbookId;
        DocumentIds = documentIds;
        HttpContext = httpContext;
        UserContext = userContext;
        Parameters = parameters ?? new Dictionary<string, string>();
        TenantId = ExtractTenantId(httpContext);
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            httpContext.RequestAborted);
    }

    /// <summary>
    /// Unique identifier for this execution run.
    /// Used for correlation across all nodes and events.
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// The playbook being executed.
    /// </summary>
    public Guid PlaybookId { get; }

    /// <summary>
    /// Document IDs being analyzed.
    /// </summary>
    public Guid[] DocumentIds { get; }

    /// <summary>
    /// HTTP context for OBO authentication when accessing SPE.
    /// </summary>
    public HttpContext HttpContext { get; }

    /// <summary>
    /// User-provided context or instructions.
    /// </summary>
    public string? UserContext { get; }

    /// <summary>
    /// Parameters passed to the playbook for template substitution.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    /// When the run started.
    /// </summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the run completed (null if still running).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Current run state.
    /// </summary>
    public PlaybookRunState State { get; private set; } = PlaybookRunState.Pending;

    /// <summary>
    /// Error message if the run failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Currently executing node ID (for status reporting).
    /// </summary>
    public Guid? CurrentNodeId { get; set; }

    /// <summary>
    /// Extracted document context, populated after document processing.
    /// Shared across all nodes.
    /// </summary>
    public DocumentContext? Document { get; set; }

    /// <summary>
    /// Cancellation token for this run.
    /// </summary>
    public CancellationToken CancellationToken => _cancellationSource.Token;

    /// <summary>
    /// Gets a read-only view of all node outputs.
    /// </summary>
    public IReadOnlyDictionary<string, NodeOutput> NodeOutputs => _nodeOutputs;

    // Metrics tracking
    private int _completedNodes;
    private int _failedNodes;
    private int _skippedNodes;
    private int _totalTokensIn;
    private int _totalTokensOut;

    /// <summary>
    /// Marks the run as started.
    /// </summary>
    public void MarkRunning()
    {
        State = PlaybookRunState.Running;
    }

    /// <summary>
    /// Marks the run as completed.
    /// </summary>
    public void MarkCompleted()
    {
        State = PlaybookRunState.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the run as failed.
    /// </summary>
    public void MarkFailed(string error)
    {
        State = PlaybookRunState.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the run as cancelled.
    /// </summary>
    public void MarkCancelled()
    {
        State = PlaybookRunState.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Request cancellation of this run.
    /// </summary>
    public void Cancel()
    {
        _cancellationSource.Cancel();
        MarkCancelled();
    }

    /// <summary>
    /// Stores a node's output for downstream node access.
    /// </summary>
    /// <param name="output">The node output to store.</param>
    public void StoreNodeOutput(NodeOutput output)
    {
        _nodeOutputs[output.OutputVariable] = output;

        if (output.Success)
        {
            Interlocked.Increment(ref _completedNodes);
            if (output.Metrics.TokensIn.HasValue)
                Interlocked.Add(ref _totalTokensIn, output.Metrics.TokensIn.Value);
            if (output.Metrics.TokensOut.HasValue)
                Interlocked.Add(ref _totalTokensOut, output.Metrics.TokensOut.Value);
        }
        else
        {
            Interlocked.Increment(ref _failedNodes);
        }
    }

    /// <summary>
    /// Records that a node was skipped.
    /// </summary>
    public void RecordNodeSkipped()
    {
        Interlocked.Increment(ref _skippedNodes);
    }

    /// <summary>
    /// Gets a previous node's output by variable name.
    /// </summary>
    /// <param name="variableName">The output variable name.</param>
    /// <returns>The node output if found, otherwise null.</returns>
    public NodeOutput? GetOutput(string variableName)
    {
        return _nodeOutputs.TryGetValue(variableName, out var output) ? output : null;
    }

    /// <summary>
    /// Gets current run metrics.
    /// </summary>
    public PlaybookRunMetrics GetMetrics(int totalNodes)
    {
        return new PlaybookRunMetrics
        {
            TotalNodes = totalNodes,
            CompletedNodes = _completedNodes,
            FailedNodes = _failedNodes,
            SkippedNodes = _skippedNodes,
            TotalTokensIn = _totalTokensIn,
            TotalTokensOut = _totalTokensOut,
            Duration = (CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt
        };
    }

    /// <summary>
    /// Gets the current run status.
    /// </summary>
    public PlaybookRunStatus GetStatus(int totalNodes)
    {
        return new PlaybookRunStatus
        {
            RunId = RunId,
            PlaybookId = PlaybookId,
            State = State,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            CurrentNodeId = CurrentNodeId,
            ErrorMessage = ErrorMessage,
            Metrics = GetMetrics(totalNodes),
            Outputs = NodeOutputs
        };
    }

    /// <summary>
    /// Creates a NodeExecutionContext for a specific node.
    /// </summary>
    public NodeExecutionContext CreateNodeContext(
        PlaybookNodeDto node,
        AnalysisAction action,
        ResolvedScopes scopes,
        Nodes.ActionType actionType)
    {
        return new NodeExecutionContext
        {
            RunId = RunId,
            PlaybookId = PlaybookId,
            Node = node,
            Action = action,
            ActionType = actionType,
            Scopes = scopes,
            Document = Document,
            PreviousOutputs = NodeOutputs,
            TenantId = TenantId,
            UserContext = UserContext,
            ModelDeploymentId = node.ModelDeploymentId,
            CorrelationId = RunId.ToString()
        };
    }

    private static string ExtractTenantId(HttpContext httpContext)
    {
        // Extract tenant ID from claims or default
        var tenantClaim = httpContext.User.FindFirst("tid")
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid");
        return tenantClaim?.Value ?? "default";
    }
}

// Note: DocumentContext is defined in ToolExecutionContext.cs and reused here.
