using System.Text.Json;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Generates AI summaries for Legal Operations Workspace feed items and to-do items.
/// Fetches the referenced entity from Dataverse and delegates to the AI Playbook
/// platform (IPlaybookOrchestrationService) for analysis.
/// </summary>
/// <remarks>
/// Follows ADR-013: Uses PlaybookService (AI Playbook platform) — NOT direct OpenAI calls.
/// Follows ADR-010: Concrete registration, no unnecessary interface seam.
///
/// The service maps an entity type and entity ID to an <see cref="AiSummaryResponse"/>
/// containing analysis text, suggested actions, and confidence score.
///
/// Supported entity types:
/// - <c>sprk_event</c>  — Updates Feed items and To-Do items
/// - <c>sprk_matter</c> — Matter-level context
/// - <c>sprk_project</c> — Project-level context
/// - <c>sprk_document</c> — Document analysis
///
/// TODO (future tasks): Replace mock Dataverse fetch with real IDataverseService queries.
/// </remarks>
public class WorkspaceAiService
{
    private readonly IPlaybookOrchestrationService _playbookService;
    private readonly ILogger<WorkspaceAiService> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Default playbook ID for workspace AI summaries. Override via Workspace:AiSummaryPlaybookId.
    /// </summary>
    private static readonly Guid DefaultAiSummaryPlaybookId =
        Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");

    private static readonly HashSet<string> SupportedEntityTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "sprk_event",
            "sprk_matter",
            "sprk_project",
            "sprk_document"
        };

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceAiService"/>.
    /// </summary>
    public WorkspaceAiService(
        IPlaybookOrchestrationService playbookService,
        ILogger<WorkspaceAiService> logger,
        IConfiguration configuration)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Generates an AI summary for the specified entity using the AI Playbook platform.
    /// </summary>
    /// <param name="request">Summary request containing entity type, entity ID, and optional context.</param>
    /// <param name="userId">Entra ID object ID of the authenticated user (for audit logging).</param>
    /// <param name="httpContext">HTTP context for OBO authentication in playbook execution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>AI summary response with analysis text and suggested actions.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the referenced entity is not found in Dataverse.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the entity type is not supported or the AI Playbook call fails.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the AI Playbook analysis exceeds the configured timeout.
    /// </exception>
    public async Task<AiSummaryResponse> GenerateAiSummaryAsync(
        AiSummaryRequest request,
        string userId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating AI summary. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}",
            userId,
            request.EntityType,
            request.EntityId);

        if (!SupportedEntityTypes.Contains(request.EntityType))
        {
            _logger.LogWarning(
                "Unsupported entity type requested. EntityType={EntityType}, UserId={UserId}",
                request.EntityType,
                userId);

            throw new InvalidOperationException(
                $"Entity type '{request.EntityType}' is not supported for AI summary. " +
                $"Supported types: {string.Join(", ", SupportedEntityTypes)}.");
        }

        // --- Fetch entity from Dataverse ---
        // TODO: Replace with real IDataverseService query.
        var entityDescription = await FetchEntityDescriptionAsync(request, ct);

        // --- Invoke AI Playbook for analysis ---
        var result = await ExecutePlaybookAnalysisAsync(request, entityDescription, httpContext, ct);

        _logger.LogInformation(
            "AI summary generated. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
            "Confidence={Confidence:F2}, SuggestedActions={ActionCount}",
            userId,
            request.EntityType,
            request.EntityId,
            result.Confidence,
            result.SuggestedActions.Length);

        return result;
    }

    /// <summary>
    /// Fetches a human-readable description of the entity from Dataverse.
    /// </summary>
    /// <remarks>
    /// TODO: Replace mock with real Dataverse Web API call via IDataverseService.
    /// Query pattern (sprk_event example):
    ///   GET /api/data/v9.2/sprk_events({entityId})?$select=sprk_name,sprk_description,
    ///       sprk_todostatus,sprk_priority,sprk_effort,sprk_estimatedminutes
    /// Return KeyNotFoundException if entity not found (HTTP 404 from Dataverse).
    /// </remarks>
    private Task<string> FetchEntityDescriptionAsync(AiSummaryRequest request, CancellationToken ct)
    {
        // Mock: Return a representative description based on entity type.
        // Real implementation: query Dataverse entity by request.EntityId and map fields.
        var entityType = request.EntityType.ToLowerInvariant();

        var description = entityType switch
        {
            "sprk_event" =>
                $"Event ID {request.EntityId}: Deadline review scheduled. " +
                "Priority: High. Effort: 3 hours. Status: In Progress. " +
                (request.Context != null ? $"Additional context: {request.Context}" : string.Empty),

            "sprk_matter" =>
                $"Matter ID {request.EntityId}: Active matter with 2 overdue events. " +
                "Budget utilization: 87%. Requires immediate attention. " +
                (request.Context != null ? $"Additional context: {request.Context}" : string.Empty),

            "sprk_project" =>
                $"Project ID {request.EntityId}: Active project. " +
                (request.Context != null ? $"Additional context: {request.Context}" : string.Empty),

            "sprk_document" =>
                $"Document ID {request.EntityId}: Document requiring review. " +
                (request.Context != null ? $"Additional context: {request.Context}" : string.Empty),

            _ => $"Entity {request.EntityType} ID {request.EntityId}."
        };

        return Task.FromResult(description);
    }

    /// <summary>
    /// Executes the AI Playbook to generate analysis and suggested actions.
    /// Consumes the playbook stream and extracts the final analysis from NodeCompleted events.
    /// Falls back to a template response if the playbook fails or times out.
    /// </summary>
    private async Task<AiSummaryResponse> ExecutePlaybookAnalysisAsync(
        AiSummaryRequest request,
        string entityDescription,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var playbookIdStr = _configuration["Workspace:AiSummaryPlaybookId"];
        var playbookId = !string.IsNullOrEmpty(playbookIdStr) && Guid.TryParse(playbookIdStr, out var parsed)
            ? parsed
            : DefaultAiSummaryPlaybookId;

        var playbookRequest = new PlaybookRunRequest
        {
            PlaybookId = playbookId,
            DocumentIds = [],
            UserContext = entityDescription,
            Parameters = new Dictionary<string, string>
            {
                ["entity_type"] = request.EntityType,
                ["entity_id"] = request.EntityId.ToString(),
                ["analysis_mode"] = "workspace_summary"
            }
        };

        string? analysisText = null;
        string[]? suggestedActions = null;
        double confidence = 0;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));

        try
        {
            await foreach (var evt in _playbookService.ExecuteAsync(playbookRequest, httpContext, timeoutCts.Token))
            {
                if (evt.Type == PlaybookEventType.NodeCompleted && evt.NodeOutput != null)
                {
                    // Extract analysis from the last completed node's output
                    if (evt.NodeOutput.StructuredData.HasValue)
                    {
                        try
                        {
                            var data = evt.NodeOutput.StructuredData.Value;
                            analysisText = data.TryGetProperty("analysis", out var analysisProp)
                                ? analysisProp.GetString()
                                : evt.NodeOutput.TextContent;

                            if (data.TryGetProperty("suggestedActions", out var actionsProp) &&
                                actionsProp.ValueKind == JsonValueKind.Array)
                            {
                                suggestedActions = actionsProp.EnumerateArray()
                                    .Select(a => a.GetString() ?? string.Empty)
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToArray();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to parse structured data from playbook node output. " +
                                "Falling back to text content. NodeId={NodeId}",
                                evt.NodeId);
                            analysisText = evt.NodeOutput.TextContent;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(evt.NodeOutput.TextContent))
                    {
                        analysisText = evt.NodeOutput.TextContent;
                    }

                    confidence = evt.NodeOutput.Confidence ?? confidence;
                }

                if (evt.Type == PlaybookEventType.RunFailed)
                {
                    _logger.LogWarning(
                        "AI summary playbook failed. EntityType={EntityType}, EntityId={EntityId}, Error={Error}",
                        request.EntityType, request.EntityId, evt.Error);

                    return BuildFallbackResponse(request);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "AI summary playbook timed out (45s). EntityType={EntityType}, EntityId={EntityId}",
                request.EntityType, request.EntityId);

            return BuildFallbackResponse(request);
        }

        if (string.IsNullOrWhiteSpace(analysisText))
        {
            return BuildFallbackResponse(request);
        }

        return new AiSummaryResponse(
            Analysis: analysisText,
            SuggestedActions: suggestedActions ?? ["Review entity details and current status"],
            Confidence: confidence,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Builds a fallback response when the playbook is unavailable or fails.
    /// </summary>
    private static AiSummaryResponse BuildFallbackResponse(AiSummaryRequest request)
    {
        var entityType = request.EntityType.ToLowerInvariant();

        var (analysis, actions, conf) = entityType switch
        {
            "sprk_event" => (
                "This event represents a high-priority deadline with significant time investment required. " +
                "Based on the current workload and matter status, immediate attention is recommended.",
                new[]
                {
                    "Schedule a preparation block before the deadline",
                    "Review related documents and correspondence",
                    "Coordinate with team members on shared dependencies",
                    "Update matter timeline if deadline is adjusted"
                },
                0.50
            ),

            "sprk_matter" => (
                "This matter requires attention based on current status indicators. " +
                "Consider reviewing billing arrangements and scope alignment with the client.",
                new[]
                {
                    "Schedule client status call to review progress",
                    "Review and resolve overdue events",
                    "Prepare budget status summary for partner review",
                    "Evaluate whether scope adjustments are needed"
                },
                0.50
            ),

            _ => (
                $"AI analysis of {request.EntityType} entity. " +
                "Review the associated timeline and dependencies to determine next steps.",
                new[]
                {
                    "Review entity details and current status",
                    "Identify any blocked dependencies",
                    "Update status and communicate with stakeholders"
                },
                0.40
            )
        };

        return new AiSummaryResponse(
            Analysis: analysis,
            SuggestedActions: actions,
            Confidence: conf,
            GeneratedAt: DateTimeOffset.UtcNow);
    }
}
