using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that enqueues RAG semantic indexing jobs for documents.
/// Submits a background RagIndexing job via Service Bus (fire-and-forget),
/// allowing the playbook to trigger semantic indexing as part of its flow.
/// </summary>
/// <remarks>
/// <para>
/// Configuration from node.ConfigJson:
/// </para>
/// <code>
/// {
///   "indexName": "knowledge",
///   "source": "document",
///   "parentEntity": {
///     "entityType": "matter",
///     "entityId": "{{document.parentEntityId}}",
///     "entityName": "{{document.parentEntityName}}"
///   },
///   "metadata": { "category": "legal" }
/// }
/// </code>
/// <para>
/// The node reads DriveId/ItemId from DocumentContext.Metadata (populated by
/// AppOnlyAnalysisService or AnalysisOrchestrationService) and submits an
/// existing RagIndexingJobPayload to the background job queue.
/// </para>
/// </remarks>
public sealed class DeliverToIndexNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly ILogger<DeliverToIndexNodeExecutor> _logger;

    public DeliverToIndexNodeExecutor(
        ITemplateEngine templateEngine,
        JobSubmissionService jobSubmissionService,
        ILogger<DeliverToIndexNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _jobSubmissionService = jobSubmissionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.DeliverToIndex
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config == null)
            return NodeValidationResult.Failure("DeliverToIndex node requires ConfigJson with at least 'indexName'.");

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.IndexName))
            errors.Add("'indexName' is required.");

        var source = string.IsNullOrWhiteSpace(config.Source) ? "document" : config.Source;

        if (source == "document" && context.Document == null)
            errors.Add("Document context is required when source is 'document'.");

        if (source == "content" && string.IsNullOrWhiteSpace(config.ContentVariable))
            errors.Add("'contentVariable' is required when source is 'content'.");

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing DeliverToIndex node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            var config = ParseConfig(context.Node.ConfigJson)!;

            // Build template context for variable resolution
            var templateContext = BuildTemplateContext(context);

            // Resolve template variables
            var indexName = ResolveTemplate(config.IndexName!, templateContext);

            // Extract DriveId/ItemId from DocumentContext.Metadata
            var driveId = context.Document?.Metadata?.TryGetValue("GraphDriveId", out var driveVal) == true
                ? driveVal?.ToString() : null;
            var itemId = context.Document?.Metadata?.TryGetValue("GraphItemId", out var itemVal) == true
                ? itemVal?.ToString() : null;

            if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
            {
                _logger.LogWarning(
                    "DeliverToIndex node {NodeId}: Missing GraphDriveId or GraphItemId in DocumentContext.Metadata",
                    context.Node.Id);

                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "Cannot enqueue RAG indexing: GraphDriveId and GraphItemId are required in DocumentContext.Metadata.",
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Build parent entity context if configured
            ParentEntityContext? parentEntity = null;
            if (config.ParentEntity is { } pe &&
                !string.IsNullOrWhiteSpace(pe.EntityType) &&
                !string.IsNullOrWhiteSpace(pe.EntityId))
            {
                parentEntity = new ParentEntityContext(
                    ResolveTemplate(pe.EntityType, templateContext),
                    ResolveTemplate(pe.EntityId, templateContext),
                    ResolveTemplate(pe.EntityName ?? "", templateContext));
            }

            // Resolve metadata templates
            Dictionary<string, string>? resolvedMetadata = null;
            if (config.Metadata is { Count: > 0 })
            {
                resolvedMetadata = new Dictionary<string, string>();
                foreach (var (key, value) in config.Metadata)
                {
                    resolvedMetadata[key] = ResolveTemplate(value, templateContext);
                }
            }

            var fileName = context.Document?.FileName ?? "unknown";

            // Build the RagIndexing job payload
            var payload = new RagIndexingJobPayload
            {
                TenantId = context.TenantId,
                DriveId = driveId,
                ItemId = itemId,
                FileName = fileName,
                DocumentId = context.Document?.DocumentId.ToString(),
                Metadata = resolvedMetadata,
                ParentEntity = parentEntity,
                Source = "PlaybookNode",
                EnqueuedAt = DateTimeOffset.UtcNow
            };

            // Build and submit the job contract
            var jobId = Guid.NewGuid();
            var job = new JobContract
            {
                JobId = jobId,
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = itemId,
                CorrelationId = context.RunId.ToString(),
                IdempotencyKey = $"rag-index-{driveId}-{itemId}",
                Attempt = 1,
                MaxAttempts = 3,
                Payload = JsonSerializer.SerializeToDocument(payload)
            };

            await _jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            _logger.LogInformation(
                "DeliverToIndex node {NodeId} enqueued RAG indexing job {JobId} for {FileName} (index: {IndexName})",
                context.Node.Id,
                jobId,
                fileName,
                indexName);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new { jobId, status = "queued", indexName, driveId, itemId },
                textContent: $"RAG indexing queued for {fileName} (index: {indexName})",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DeliverToIndex node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to enqueue RAG indexing job: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private string ResolveTemplate(string value, Dictionary<string, object?> templateContext)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("{{"))
            return value;

        return _templateEngine.Render(value, templateContext);
    }

    private static DeliverToIndexNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DeliverToIndexNodeConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds template context from previous node outputs, document, and run metadata.
    /// Reuses the same pattern as DeliverOutputNodeExecutor.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? TemplateEngine.ConvertJsonElement(output.StructuredData.Value)
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

        if (context.Document is not null)
        {
            templateContext["document"] = new
            {
                id = context.Document.DocumentId.ToString(),
                name = context.Document.Name,
                fileName = context.Document.FileName
            };
        }

        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId
        };

        return templateContext;
    }
}

/// <summary>
/// Configuration for DeliverToIndex node from ConfigJson.
/// </summary>
internal sealed record DeliverToIndexNodeConfig
{
    public string? IndexName { get; init; }
    public string? Source { get; init; }
    public string? ContentVariable { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public ParentEntityConfig? ParentEntity { get; init; }
}

/// <summary>
/// Parent entity configuration for entity-scoped indexing.
/// </summary>
internal sealed record ParentEntityConfig
{
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? EntityName { get; init; }
}
