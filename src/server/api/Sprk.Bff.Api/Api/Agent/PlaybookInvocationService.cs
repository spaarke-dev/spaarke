using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Orchestrates playbook invocation from the M365 Copilot agent.
/// Coordinates the full flow: document search, context mapping, playbook selection,
/// execution, and Adaptive Card result formatting.
///
/// This service is a THIN ORCHESTRATOR — it delegates to existing BFF services:
///   - <see cref="ChatContextMappingService"/> for resolving available playbooks per document type
///   - <see cref="IPlaybookExecutionEngine"/> / <see cref="IPlaybookOrchestrationService"/> for execution
///   - <see cref="AdaptiveCardFormatterService"/> for Adaptive Card rendering
///   - <see cref="HandoffUrlBuilder"/> for deep-link generation
///
/// ADR-013: Extends BFF, does not create a separate service.
/// ADR-016: Respects rate limiting and timeouts for playbook execution.
/// ADR-010: Concrete type, registered as scoped — no unnecessary interface.
/// </summary>
public sealed class PlaybookInvocationService
{
    /// <summary>
    /// Maximum execution time for inline (synchronous) playbook execution.
    /// Playbooks estimated to exceed this threshold are executed asynchronously.
    /// </summary>
    private static readonly TimeSpan InlineExecutionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default number of nodes below which a playbook is considered "quick" and
    /// eligible for inline execution. Playbooks with more nodes are routed to async.
    /// </summary>
    private const int InlineNodeThreshold = 5;

    private readonly AdaptiveCardFormatterService _cardFormatter;
    private readonly HandoffUrlBuilder _handoffUrlBuilder;
    private readonly ILogger<PlaybookInvocationService> _logger;

    // TODO: Inject ChatContextMappingService when wiring is complete (MCI-022).
    // Used by SearchAndResolvePlaybooks to resolve available playbooks for a document type.
    // private readonly ChatContextMappingService _contextMappingService;

    // TODO: Inject IPlaybookOrchestrationService when wiring is complete (MCI-023).
    // Used by InvokePlaybook for executing playbooks (both inline and async).
    // private readonly IPlaybookOrchestrationService _playbookOrchestration;

    // TODO: Inject IPlaybookExecutionEngine when wiring is complete (MCI-023).
    // Used by DetermineExecutionStrategy to check playbook metadata (node count, estimated duration).
    // private readonly IPlaybookExecutionEngine _executionEngine;

    // TODO: Inject Azure AI Search client or existing search service for document search (MCI-024).
    // Used by SearchAndResolvePlaybooks to search documents by query text.
    // private readonly IAzureSearchService _searchService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybookInvocationService"/> class.
    /// ADR-010: Inject only concrete dependencies that are currently available.
    /// Additional services will be injected as their integration tasks are completed.
    /// </summary>
    public PlaybookInvocationService(
        AdaptiveCardFormatterService cardFormatter,
        HandoffUrlBuilder handoffUrlBuilder,
        ILogger<PlaybookInvocationService> logger)
    {
        _cardFormatter = cardFormatter;
        _handoffUrlBuilder = handoffUrlBuilder;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────
    // Search & Resolve
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches for documents matching the query and resolves available playbooks
    /// for each document's type using <see cref="ChatContextMappingService"/>.
    /// </summary>
    /// <param name="query">Natural-language search query from the Copilot user.</param>
    /// <param name="matterId">Optional matter scope to narrow the search.</param>
    /// <param name="tenantId">Tenant ID for cache-scoped operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A list of document search results, each paired with the playbooks available
    /// for that document's type. Returns an empty list when no documents match.
    /// </returns>
    public async Task<IReadOnlyList<DocumentWithPlaybooks>> SearchAndResolvePlaybooksAsync(
        string query,
        Guid? matterId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] SearchAndResolve: Query length={QueryLength}, MatterId={MatterId}",
            query.Length, matterId);

        // TODO: Call Azure AI Search to find documents matching the query.
        // The existing search service supports matter-scoped search and returns
        // document metadata including document type, name, page count, and matter context.
        //
        // var searchResults = await _searchService.SearchDocumentsAsync(query, matterId, cancellationToken);

        // TODO: For each document result, resolve available playbooks via ChatContextMappingService.
        // The entity type for documents is typically "sprk_spefile" and the page type comes
        // from the document's classification (e.g., "contract", "invoice", "letter").
        //
        // foreach (var doc in searchResults)
        // {
        //     var mapping = await _contextMappingService.ResolveAsync(
        //         "sprk_spefile", doc.DocumentType, tenantId, cancellationToken);
        //     results.Add(new DocumentWithPlaybooks(doc, mapping.AvailablePlaybooks));
        // }

        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] SearchAndResolve complete: ResultCount=0 (pending service wiring)");

        // Return empty until search service injection is wired.
        return [];
    }

    // ────────────────────────────────────────────────────────────────
    // Playbook Execution
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes a playbook against a document. Determines whether to execute inline
    /// (synchronous, &lt;30s) or async (long-running with job tracking).
    /// </summary>
    /// <param name="playbookId">The playbook definition to execute.</param>
    /// <param name="documentId">The document to run the playbook against.</param>
    /// <param name="parameters">Optional key-value parameters for the playbook.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="PlaybookInvocationResult"/> containing either:
    /// - An Adaptive Card with inline results (for quick playbooks), or
    /// - A progress indicator card with job ID and deep-link (for async playbooks).
    /// </returns>
    public async Task<PlaybookInvocationResult> InvokePlaybookAsync(
        Guid playbookId,
        Guid documentId,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] InvokePlaybook: PlaybookId={PlaybookId}, DocumentId={DocumentId}, CorrelationId={CorrelationId}",
            playbookId, documentId, correlationId);

        try
        {
            var strategy = DetermineExecutionStrategy(playbookId);

            return strategy switch
            {
                ExecutionStrategy.Inline => await ExecuteInlineAsync(
                    playbookId, documentId, parameters, correlationId, cancellationToken),

                ExecutionStrategy.Async => await EnqueueAsyncExecutionAsync(
                    playbookId, documentId, parameters, correlationId, cancellationToken),

                _ => throw new InvalidOperationException($"Unknown execution strategy: {strategy}")
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "[PLAYBOOK-INVOCATION] Execution cancelled: PlaybookId={PlaybookId}, CorrelationId={CorrelationId}",
                playbookId, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PLAYBOOK-INVOCATION] Execution failed: PlaybookId={PlaybookId}, DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                playbookId, documentId, correlationId);

            var errorCard = _cardFormatter.FormatErrorCard(
                "The playbook could not be executed. Please try again or open in the Analysis Workspace.",
                correlationId,
                $"run_playbook:{playbookId}");

            return new PlaybookInvocationResult
            {
                Strategy = ExecutionStrategy.Inline,
                AdaptiveCardJson = errorCard,
                CorrelationId = correlationId
            };
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Execution Strategy
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a playbook should be executed inline (&lt;30s) or async (long-running).
    /// Decision is based on playbook metadata: node count, estimated duration, and document complexity.
    /// </summary>
    /// <param name="playbookId">The playbook to evaluate.</param>
    /// <returns>The recommended execution strategy.</returns>
    public ExecutionStrategy DetermineExecutionStrategy(Guid playbookId)
    {
        // TODO: Query playbook metadata from IPlaybookOrchestrationService or Dataverse to determine:
        //   1. Node count — playbooks with <= InlineNodeThreshold nodes run inline
        //   2. Estimated duration from historical run data (PlaybookRunMetrics.Duration)
        //   3. Whether the playbook uses expensive node types (e.g., document extraction, multi-model)
        //
        // var playbookMetadata = await _playbookOrchestration.GetPlaybookMetadataAsync(playbookId);
        // if (playbookMetadata.NodeCount <= InlineNodeThreshold
        //     && playbookMetadata.AverageDuration < InlineExecutionTimeout)
        // {
        //     return ExecutionStrategy.Inline;
        // }
        // return ExecutionStrategy.Async;

        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] DetermineStrategy: PlaybookId={PlaybookId}, Strategy=Async (default until metadata wiring)",
            playbookId);

        // Default to async until playbook metadata querying is wired.
        // This is the safe default — inline can miss the 30s Copilot response window.
        return ExecutionStrategy.Async;
    }

    // ────────────────────────────────────────────────────────────────
    // Result Formatting
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats a completed playbook run into an Adaptive Card for display in Copilot.
    /// Extracts risk findings, confidence scores, and summary data from the run detail
    /// and renders them using the risk-findings card template.
    /// </summary>
    /// <param name="runDetail">Completed playbook run with node-level outputs.</param>
    /// <returns>Adaptive Card JSON string.</returns>
    public string FormatPlaybookResult(PlaybookRunDetail runDetail)
    {
        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] FormatResult: RunId={RunId}, PlaybookId={PlaybookId}, Nodes={NodeCount}",
            runDetail.RunId, runDetail.PlaybookId, runDetail.NodeDetails.Length);

        // Extract risk findings from node outputs for the risk-findings card.
        var findings = ExtractRiskFindings(runDetail);

        var analysis = new RiskAnalysisCardItem
        {
            AnalysisId = runDetail.RunId.ToString(),
            DocumentId = runDetail.DocumentIds.FirstOrDefault().ToString(),
            PlaybookName = $"Playbook {runDetail.PlaybookId}", // TODO: Resolve playbook display name from catalog
            DocumentName = "Document", // TODO: Resolve document display name from SPE file metadata
            StandardClauseCount = runDetail.NodeDetails.Count(n => n.Success && n.Confidence >= 0.8),
            Confidence = DeriveOverallConfidence(runDetail)
        };

        return _cardFormatter.FormatRiskFindings(analysis, findings);
    }

    /// <summary>
    /// Builds a deep-link URL to the Analysis Workspace code page for a specific
    /// analysis run, document, and playbook combination.
    /// </summary>
    /// <param name="analysisId">The analysis/run ID.</param>
    /// <param name="documentId">The source document ID.</param>
    /// <param name="playbookId">The playbook that was executed.</param>
    /// <returns>Full Dataverse web resource URL for the Analysis Workspace.</returns>
    public string BuildAnalysisDeepLink(Guid analysisId, Guid documentId, Guid playbookId)
    {
        return _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
            analysisId: analysisId,
            sourceFileId: documentId,
            playbookId: playbookId);
    }

    // ────────────────────────────────────────────────────────────────
    // Private Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a playbook synchronously within the 30-second Copilot response window.
    /// Collects all streaming events, builds a result Adaptive Card, and returns it.
    /// </summary>
    private async Task<PlaybookInvocationResult> ExecuteInlineAsync(
        Guid playbookId,
        Guid documentId,
        IReadOnlyDictionary<string, string>? parameters,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] ExecuteInline: PlaybookId={PlaybookId}, DocumentId={DocumentId}",
            playbookId, documentId);

        // TODO: Execute playbook synchronously via IPlaybookOrchestrationService.ExecuteAsync().
        // Wrap with a CancellationTokenSource linked to InlineExecutionTimeout to enforce the 30s limit.
        //
        // using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // timeoutCts.CancelAfter(InlineExecutionTimeout);
        //
        // var request = new PlaybookRunRequest
        // {
        //     PlaybookId = playbookId,
        //     DocumentIds = [documentId],
        //     Parameters = parameters
        // };
        //
        // PlaybookRunDetail? runDetail = null;
        // Guid? runId = null;
        //
        // await foreach (var evt in _playbookOrchestration.ExecuteAsync(request, httpContext, timeoutCts.Token))
        // {
        //     if (evt.Type == PlaybookEventType.RunStarted) runId = evt.RunId;
        //     if (evt.Type == PlaybookEventType.RunCompleted && runId.HasValue)
        //     {
        //         runDetail = await _playbookOrchestration.GetRunDetailAsync(runId.Value, cancellationToken);
        //     }
        // }
        //
        // if (runDetail is not null)
        // {
        //     var resultCard = FormatPlaybookResult(runDetail);
        //     return new PlaybookInvocationResult
        //     {
        //         Strategy = ExecutionStrategy.Inline,
        //         AdaptiveCardJson = resultCard,
        //         CorrelationId = correlationId
        //     };
        // }

        // Fallback: return a placeholder card until orchestration wiring is complete.
        var deepLink = BuildAnalysisDeepLink(Guid.Empty, documentId, playbookId);
        var card = _cardFormatter.FormatHandoffCard(
            analysisType: "Playbook Analysis",
            analysisId: Guid.Empty.ToString(),
            sourceFileId: documentId.ToString(),
            playbookId: playbookId.ToString(),
            deepLinkUrl: deepLink);

        return new PlaybookInvocationResult
        {
            Strategy = ExecutionStrategy.Inline,
            AdaptiveCardJson = card,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Enqueues a playbook for asynchronous execution and returns a progress indicator card
    /// with a deep-link to the Analysis Workspace where the user can monitor progress.
    /// The job ID is returned for status polling via GET /api/agent/playbooks/status/{jobId}.
    /// </summary>
    private async Task<PlaybookInvocationResult> EnqueueAsyncExecutionAsync(
        Guid playbookId,
        Guid documentId,
        IReadOnlyDictionary<string, string>? parameters,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] EnqueueAsync: PlaybookId={PlaybookId}, DocumentId={DocumentId}",
            playbookId, documentId);

        // TODO: Enqueue playbook execution via IPlaybookOrchestrationService.
        // The BFF already supports background execution via BackgroundService (ADR-001).
        //
        // var request = new PlaybookRunRequest
        // {
        //     PlaybookId = playbookId,
        //     DocumentIds = [documentId],
        //     Parameters = parameters
        // };
        //
        // var jobId = await _playbookOrchestration.EnqueueAsync(request, cancellationToken);

        // Generate a placeholder job ID until orchestration is wired.
        var jobId = Guid.NewGuid();

        var deepLink = BuildAnalysisDeepLink(jobId, documentId, playbookId);

        // Build progress indicator card showing initial steps.
        var progressSteps = new List<ProgressStepCardItem>
        {
            new() { StatusIcon = "🔄", Order = 1, StepName = "Queued for execution" },
            new() { StatusIcon = "⏳", Order = 2, StepName = "Document extraction" },
            new() { StatusIcon = "⏳", Order = 3, StepName = "Analysis" },
            new() { StatusIcon = "⏳", Order = 4, StepName = "Results compilation" }
        };

        var progressCard = _cardFormatter.FormatProgressIndicator(
            documentName: $"Document {documentId}", // TODO: Resolve actual document name
            steps: progressSteps,
            analysisId: jobId.ToString(),
            documentId: documentId.ToString());

        _logger.LogInformation(
            "[PLAYBOOK-INVOCATION] EnqueueAsync complete: JobId={JobId}, PlaybookId={PlaybookId}",
            jobId, playbookId);

        return new PlaybookInvocationResult
        {
            Strategy = ExecutionStrategy.Async,
            JobId = jobId,
            AdaptiveCardJson = progressCard,
            DeepLinkUrl = deepLink,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Extracts risk findings from completed playbook node outputs.
    /// Scans node output previews for risk-flagged content.
    /// </summary>
    private static IReadOnlyList<RiskFlagCardItem> ExtractRiskFindings(PlaybookRunDetail runDetail)
    {
        var findings = new List<RiskFlagCardItem>();

        foreach (var node in runDetail.NodeDetails)
        {
            // Nodes with low confidence or failures indicate potential risk findings.
            if (node is { Success: true } && node.Confidence < 0.7)
            {
                findings.Add(new RiskFlagCardItem
                {
                    Description = node.OutputPreview ?? $"Risk flag in {node.NodeName}"
                });
            }

            if (node is { Success: false, ErrorMessage: not null })
            {
                findings.Add(new RiskFlagCardItem
                {
                    Description = $"Analysis incomplete: {node.NodeName} — {node.ErrorMessage}"
                });
            }
        }

        return findings;
    }

    /// <summary>
    /// Derives an overall confidence label from the playbook run's node-level confidence scores.
    /// </summary>
    private static string DeriveOverallConfidence(PlaybookRunDetail runDetail)
    {
        var confidences = runDetail.NodeDetails
            .Where(n => n.Success)
            .Select(n => n.Confidence)
            .ToList();

        if (confidences.Count == 0)
            return "N/A";

        var average = confidences.Average();
        return average switch
        {
            >= 0.9 => "High",
            >= 0.7 => "Medium",
            _ => "Low"
        };
    }
}

// ────────────────────────────────────────────────────────────────
// Supporting Types
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Execution strategy for playbook invocation from the Copilot agent.
/// </summary>
public enum ExecutionStrategy
{
    /// <summary>
    /// Execute synchronously within the 30-second Copilot response window.
    /// Suitable for playbooks with few nodes and fast estimated execution.
    /// </summary>
    Inline,

    /// <summary>
    /// Execute asynchronously with job tracking.
    /// Returns a progress indicator card and deep-link to the Analysis Workspace.
    /// Status can be polled via GET /api/agent/playbooks/status/{jobId}.
    /// </summary>
    Async
}

/// <summary>
/// Result of a playbook invocation, containing either inline results or async job info.
/// </summary>
public sealed record PlaybookInvocationResult
{
    /// <summary>
    /// The execution strategy that was used.
    /// </summary>
    public required ExecutionStrategy Strategy { get; init; }

    /// <summary>
    /// Adaptive Card JSON for rendering in Copilot.
    /// For inline: contains the analysis results card.
    /// For async: contains the progress indicator card.
    /// </summary>
    public required string AdaptiveCardJson { get; init; }

    /// <summary>
    /// Unique correlation ID for tracing this invocation.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Job ID for async playbooks. Null for inline execution.
    /// Used for status polling via GET /api/agent/playbooks/status/{jobId}.
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>
    /// Deep-link URL to the Analysis Workspace. Null for inline execution.
    /// </summary>
    public string? DeepLinkUrl { get; init; }
}

/// <summary>
/// A document search result paired with its available playbooks, as resolved
/// by <see cref="ChatContextMappingService"/>.
/// </summary>
public sealed record DocumentWithPlaybooks
{
    /// <summary>Document ID.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Document display name.</summary>
    public required string DocumentName { get; init; }

    /// <summary>Document type (e.g., "Contract", "Invoice").</summary>
    public required string DocumentType { get; init; }

    /// <summary>Matter name the document belongs to.</summary>
    public required string MatterName { get; init; }

    /// <summary>Matter ID.</summary>
    public Guid? MatterId { get; init; }

    /// <summary>Number of pages in the document.</summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Playbooks available for this document's type, resolved via context mapping.
    /// </summary>
    public required IReadOnlyList<ChatPlaybookInfo> AvailablePlaybooks { get; init; }
}
