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
///
/// TODO (future tasks): Replace mock Dataverse fetch with real IDataverseService queries.
/// </remarks>
public class WorkspaceAiService
{
    private readonly ILogger<WorkspaceAiService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceAiService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public WorkspaceAiService(ILogger<WorkspaceAiService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an AI summary for the specified entity using the AI Playbook platform.
    /// </summary>
    /// <param name="request">Summary request containing entity type, entity ID, and optional context.</param>
    /// <param name="userId">Entra ID object ID of the authenticated user (for audit logging).</param>
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
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating AI summary. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}",
            userId,
            request.EntityType,
            request.EntityId);

        // Validate entity type — only known workspace entities are supported.
        var supportedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sprk_event",
            "sprk_matter",
            "sprk_project",
            "sprk_document"
        };

        if (!supportedEntityTypes.Contains(request.EntityType))
        {
            _logger.LogWarning(
                "Unsupported entity type requested. EntityType={EntityType}, UserId={UserId}",
                request.EntityType,
                userId);

            throw new InvalidOperationException(
                $"Entity type '{request.EntityType}' is not supported for AI summary. " +
                $"Supported types: {string.Join(", ", supportedEntityTypes)}.");
        }

        // --- Fetch entity from Dataverse ---
        // TODO: Replace with real IDataverseService query.
        // Query pattern (sprk_event example):
        //   GET /api/data/v9.2/sprk_events({entityId})?$select=sprk_name,sprk_description,
        //       sprk_todostatus,sprk_priority,sprk_effort,sprk_estimatedminutes
        // Return KeyNotFoundException if entity not found (HTTP 404 from Dataverse).
        _logger.LogDebug(
            "TODO: Fetching entity from Dataverse. EntityType={EntityType}, EntityId={EntityId}",
            request.EntityType,
            request.EntityId);

        var entityDescription = await FetchEntityDescriptionAsync(request, ct);

        // --- Invoke AI Playbook for analysis ---
        // TODO: Replace mock with IPlaybookOrchestrationService.ExecuteAsync() call.
        //
        // Pattern (follows AnalysisEndpoints.cs / AnalysisOrchestrationService):
        //   var playbookRequest = new PlaybookRunRequest
        //   {
        //       PlaybookId = <workspace-summary-playbook-id from config>,
        //       DocumentIds = [],
        //       UserContext = BuildContextPrompt(request, entityDescription)
        //   };
        //   await foreach (var evt in _orchestrationService.ExecuteAsync(playbookRequest, httpContext, ct))
        //   {
        //       // Accumulate NodeProgress content chunks
        //       // Extract suggested actions from NodeCompleted events
        //   }
        _logger.LogDebug(
            "TODO: Invoking AI Playbook for analysis. EntityType={EntityType}, EntityId={EntityId}",
            request.EntityType,
            request.EntityId);

        var result = await GenerateAnalysisAsync(request, entityDescription, ct);

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
    /// TODO: Replace mock with real Dataverse Web API call.
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
    /// Invokes the AI Playbook platform to generate analysis and suggested actions.
    /// </summary>
    /// <remarks>
    /// TODO: Replace mock with IPlaybookOrchestrationService.ExecuteAsync() streaming call.
    /// The real implementation should:
    /// 1. Look up the "Workspace AI Summary" playbook by name via IPlaybookService.GetByNameAsync
    /// 2. Build a context prompt from entityDescription + request.Context
    /// 3. Stream execution events via IPlaybookOrchestrationService.ExecuteAsync
    /// 4. Accumulate NodeProgress content into analysis text
    /// 5. Extract suggested actions from NodeCompleted output fields
    /// 6. Map confidence from PlaybookRunMetrics
    /// </remarks>
    private Task<AiSummaryResponse> GenerateAnalysisAsync(
        AiSummaryRequest request,
        string entityDescription,
        CancellationToken ct)
    {
        // Mock AI analysis — replace with real PlaybookOrchestrationService call.
        // The mock returns plausible analysis text and suggested actions for the entity type.

        var entityType = request.EntityType.ToLowerInvariant();

        var (analysis, suggestedActions, confidence) = entityType switch
        {
            "sprk_event" => (
                Analysis: "This event represents a high-priority deadline with significant time investment required. " +
                          "Based on the current workload and matter status, immediate attention is recommended. " +
                          "The estimated effort of 3 hours aligns with similar past events in this matter portfolio.",
                SuggestedActions: new[]
                {
                    "Schedule a 30-minute preparation block before the deadline",
                    "Review related documents and correspondence",
                    "Coordinate with team members on shared dependencies",
                    "Update matter timeline if deadline is adjusted"
                },
                Confidence: 0.82
            ),

            "sprk_matter" => (
                Analysis: "This matter is currently over 85% budget utilization with active overdue events. " +
                          "Risk level is elevated and proactive client communication is recommended. " +
                          "Consider reviewing billing arrangements and scope alignment with the client.",
                SuggestedActions: new[]
                {
                    "Schedule client status call to review progress",
                    "Review and resolve overdue events this week",
                    "Prepare budget status summary for partner review",
                    "Evaluate whether scope adjustments are needed"
                },
                Confidence: 0.88
            ),

            _ => (
                Analysis: $"AI analysis of {request.EntityType} entity. " +
                          "Contextual review indicates this item warrants your attention. " +
                          "Review the associated timeline and dependencies to determine next steps.",
                SuggestedActions: new[]
                {
                    "Review entity details and current status",
                    "Identify any blocked dependencies",
                    "Update status and communicate with stakeholders"
                },
                Confidence: 0.70
            )
        };

        var response = new AiSummaryResponse(
            Analysis: analysis,
            SuggestedActions: suggestedActions,
            Confidence: confidence,
            GeneratedAt: DateTimeOffset.UtcNow);

        return Task.FromResult(response);
    }
}
