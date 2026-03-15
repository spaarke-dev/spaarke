using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
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
    private readonly IGenericEntityService _genericEntityService;
    private readonly IDocumentDataverseService _documentService;
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
        IGenericEntityService genericEntityService,
        IDocumentDataverseService documentService,
        ILogger<WorkspaceAiService> logger,
        IConfiguration configuration)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
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
    /// Retrieves entity by ID using IDataverseService.RetrieveAsync with explicit column selection
    /// (ADR-002 efficiency). Maps entity fields to a human-readable description for the AI Playbook.
    /// Throws KeyNotFoundException if entity not found.
    /// </remarks>
    private async Task<string> FetchEntityDescriptionAsync(AiSummaryRequest request, CancellationToken ct)
    {
        var entityType = request.EntityType.ToLowerInvariant();

        try
        {
            var description = entityType switch
            {
                "sprk_event" => await FetchEventDescriptionAsync(request.EntityId, ct),
                "sprk_matter" => await FetchMatterDescriptionAsync(request.EntityId, ct),
                "sprk_project" => await FetchProjectDescriptionAsync(request.EntityId, ct),
                "sprk_document" => await FetchDocumentDescriptionAsync(request.EntityId, ct),
                _ => $"Entity {request.EntityType} ID {request.EntityId}."
            };

            if (request.Context != null)
                description += $" Additional context: {request.Context}";

            return description;
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to fetch entity description from Dataverse. " +
                "EntityType={EntityType}, EntityId={EntityId}. Falling back to minimal description.",
                request.EntityType, request.EntityId);

            // Graceful fallback — return minimal description rather than failing the entire AI summary
            var fallback = $"Entity {request.EntityType} ID {request.EntityId}.";
            if (request.Context != null)
                fallback += $" Additional context: {request.Context}";
            return fallback;
        }
    }

    private async Task<string> FetchEventDescriptionAsync(Guid entityId, CancellationToken ct)
    {
        var entity = await _genericEntityService.RetrieveAsync(
            "sprk_event",
            entityId,
            new[] { "sprk_eventname", "sprk_description", "sprk_priority", "sprk_duedate", "statuscode" },
            ct);

        var name = entity.GetAttributeValue<string>("sprk_eventname") ?? "Unknown";
        var description = entity.GetAttributeValue<string>("sprk_description") ?? "";
        var priority = entity.GetAttributeValue<OptionSetValue>("sprk_priority")?.Value;
        var dueDate = entity.GetAttributeValue<DateTime?>("sprk_duedate");
        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;

        var priorityLabel = priority switch
        {
            0 => "Low",
            1 => "Normal",
            2 => "High",
            3 => "Urgent",
            _ => "Unknown"
        };
        var statusLabel = statusCode switch
        {
            1 => "Draft",
            2 => "Planned",
            3 => "Open",
            4 => "On Hold",
            5 => "Completed",
            6 => "Cancelled",
            7 => "Deleted",
            _ => "Unknown"
        };

        var parts = new List<string>
        {
            $"Event: {name}",
            $"Priority: {priorityLabel}",
            $"Status: {statusLabel}"
        };

        if (dueDate.HasValue)
            parts.Add($"Due: {dueDate.Value:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add($"Description: {description}");

        return string.Join(". ", parts) + ".";
    }

    private async Task<string> FetchMatterDescriptionAsync(Guid entityId, CancellationToken ct)
    {
        var entity = await _genericEntityService.RetrieveAsync(
            "sprk_matter",
            entityId,
            new[] { "sprk_name", "sprk_totalspend", "sprk_totalbudget", "sprk_utilizationpercent", "sprk_overdueeventcount", "sprk_status" },
            ct);

        var name = entity.GetAttributeValue<string>("sprk_name") ?? "Unknown";
        var totalSpend = entity.GetAttributeValue<Money>("sprk_totalspend")?.Value ?? 0m;
        var totalBudget = entity.GetAttributeValue<Money>("sprk_totalbudget")?.Value ?? 0m;
        var utilization = entity.GetAttributeValue<decimal?>("sprk_utilizationpercent") ?? 0m;
        var overdueCount = entity.GetAttributeValue<int?>("sprk_overdueeventcount") ?? 0;

        var parts = new List<string>
        {
            $"Matter: {name}",
            $"Budget utilization: {utilization:0.#}%",
            $"Total spend: {totalSpend:C0}",
            $"Total budget: {totalBudget:C0}"
        };

        if (overdueCount > 0)
            parts.Add($"Overdue events: {overdueCount}");

        return string.Join(". ", parts) + ".";
    }

    private async Task<string> FetchProjectDescriptionAsync(Guid entityId, CancellationToken ct)
    {
        var entity = await _genericEntityService.RetrieveAsync(
            "sprk_project",
            entityId,
            new[] { "sprk_name", "sprk_description", "statecode" },
            ct);

        var name = entity.GetAttributeValue<string>("sprk_name") ?? "Unknown";
        var description = entity.GetAttributeValue<string>("sprk_description") ?? "";
        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var stateLabel = stateCode == 0 ? "Active" : "Inactive";

        var parts = new List<string>
        {
            $"Project: {name}",
            $"Status: {stateLabel}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            parts.Add($"Description: {description}");

        return string.Join(". ", parts) + ".";
    }

    private async Task<string> FetchDocumentDescriptionAsync(Guid entityId, CancellationToken ct)
    {
        var doc = await _documentService.GetDocumentAsync(entityId.ToString(), ct);

        if (doc is null)
            throw new KeyNotFoundException($"Document {entityId} not found in Dataverse.");

        var parts = new List<string>
        {
            $"Document: {doc.Name ?? "Unknown"}"
        };

        if (!string.IsNullOrWhiteSpace(doc.MimeType))
            parts.Add($"Type: {doc.MimeType}");

        return string.Join(". ", parts) + ".";
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
