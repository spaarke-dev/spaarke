using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Generates AI summaries for Legal Operations Workspace feed items and to-do items.
/// Fetches the referenced entity from Dataverse and delegates to the AI Playbook
/// platform (via the <see cref="IWorkspacePrefillAi"/> public facade) for analysis.
/// </summary>
/// <remarks>
/// <para>
/// Follows refined ADR-013 (2026-05-20, task 046): AI playbook execution flows through
/// the <see cref="IWorkspacePrefillAi"/> public facade — no direct injection of
/// AI-internal orchestration or completion-client types into CRUD code.
/// Follows ADR-010: Concrete registration, no unnecessary interface seam.
/// </para>
/// <para>
/// The service maps an entity type and entity ID to an <see cref="AiSummaryResponse"/>
/// containing analysis text, suggested actions, and confidence score.
/// </para>
/// <para>
/// Supported entity types:
/// - <c>sprk_event</c>  — Updates Feed items and To-Do items
/// - <c>sprk_matter</c> — Matter-level context
/// - <c>sprk_project</c> — Project-level context
/// - <c>sprk_document</c> — Document analysis
/// </para>
/// <para>
/// <b>FR-02 stable-ID resolution</b> (chat-routing-redesign-r1 task 018, Wave 1-E
/// Pattern A migration): the prior hardcoded
/// <c>18cf3cc8-02ec-f011-8406-7c1e520aa4df</c> GUID constant (DEV "Document Profile"
/// playbook, <c>sprk_playbookcode=PB-002</c>) and the raw
/// <c>IConfiguration["Workspace:AiSummaryPlaybookId"]</c> indexer read have both been
/// removed. The playbook is now resolved at runtime by looking up
/// <see cref="WorkspaceOptions.AiSummaryPlaybookId"/> (typed-options per ADR-018)
/// through <see cref="IPlaybookLookupService.GetByIdAsync"/>, which queries the
/// <c>sprk_playbookid</c> alternate key on <c>sprk_analysisplaybook</c>
/// (Q&amp;A 2026-06-22 Q1) with 1-hour caching (ADR-014).
/// </para>
/// <para>
/// Empty / missing config is tolerated here (unlike the chat /summarize convergence
/// point in <c>SessionSummarizeOrchestrator</c>): if the lookup fails for any reason
/// the existing fallback-response path is taken so the workspace summary tile still
/// renders a placeholder rather than 500-ing. Per-environment configuration values
/// for <c>Workspace:AiSummaryPlaybookId</c> are populated at deploy time
/// (DEV: <c>18cf3cc8-02ec-f011-8406-7c1e520aa4df</c>).
/// </para>
/// <para>
/// TODO (future tasks): Replace mock Dataverse fetch with real IDataverseService queries.
/// </para>
/// </remarks>
public sealed class WorkspaceAiService
{
    private readonly IWorkspacePrefillAi? _prefillAi;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<WorkspaceAiService> _logger;
    private readonly IPlaybookLookupService _playbookLookup;
    private readonly IOptions<WorkspaceOptions> _workspaceOptions;

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
        IGenericEntityService genericEntityService,
        IDocumentDataverseService documentService,
        ILogger<WorkspaceAiService> logger,
        IPlaybookLookupService playbookLookup,
        IOptions<WorkspaceOptions> workspaceOptions,
        IWorkspacePrefillAi? prefillAi = null)
    {
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _playbookLookup = playbookLookup ?? throw new ArgumentNullException(nameof(playbookLookup));
        _workspaceOptions = workspaceOptions ?? throw new ArgumentNullException(nameof(workspaceOptions));
        _prefillAi = prefillAi; // Nullable: AI feature flags may be disabled. RequireAi() throws at use site.
    }

    /// <summary>
    /// Returns the AI facade or throws if AI features are disabled. Workspace AI summaries
    /// have no non-AI fallback path — when AI is disabled, the endpoint surface should treat
    /// the throw as the expected "feature disabled" signal.
    /// </summary>
    private IWorkspacePrefillAi RequireAi() =>
        _prefillAi ?? throw new InvalidOperationException(
            "Workspace AI summaries require AI features. Set 'Analysis:Enabled=true' AND 'DocumentIntelligence:Enabled=true' to enable.");

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
        // FR-02 stable-ID resolution (chat-routing-redesign-r1 task 018, Wave 1-E
        // Pattern A): resolve the AI summary playbook GUID at runtime via the
        // stable-ID alternate key (sprk_playbookid) per Q&A 2026-06-22 Q1,
        // replacing the prior hardcoded 18cf3cc8-02ec-f011-8406-7c1e520aa4df
        // DEV "Document Profile" GUID. The lookup service caches results for
        // 1 hour (ADR-014); per-environment values come from WorkspaceOptions.
        // Unlike the chat /summarize convergence point in SessionSummarizeOrchestrator
        // (FR-26), workspace AI summaries gracefully degrade to a template response
        // when config is missing or lookup fails — the tile must still render
        // rather than 500 (BuildFallbackResponse contract preserved).
        var configuredPlaybookId = _workspaceOptions.Value.AiSummaryPlaybookId;
        if (string.IsNullOrWhiteSpace(configuredPlaybookId))
        {
            _logger.LogWarning(
                "Workspace:AiSummaryPlaybookId is not configured. Falling back to template " +
                "response. EntityType={EntityType}, EntityId={EntityId}",
                request.EntityType, request.EntityId);
            return BuildFallbackResponse(request);
        }

        Guid playbookId;
        try
        {
            var playbook = await _playbookLookup
                .GetByIdAsync(configuredPlaybookId, ct)
                .ConfigureAwait(false);
            playbookId = playbook.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to resolve AI summary playbook via stable ID '{ConfiguredPlaybookId}'. " +
                "Falling back to template response. EntityType={EntityType}, EntityId={EntityId}",
                configuredPlaybookId, request.EntityType, request.EntityId);
            return BuildFallbackResponse(request);
        }

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
            await foreach (var evt in RequireAi().ExecutePlaybookAsync(playbookRequest, httpContext, timeoutCts.Token))
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
