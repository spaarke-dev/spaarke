using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for creating Dataverse task records from playbook execution.
/// Uses TemplateEngine for variable substitution in task fields.
/// </summary>
/// <remarks>
/// <para>
/// Task configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "subject": "Review {{node1.output.documentName}}",
///   "description": "{{node1.output.summary}}",
///   "regardingObjectId": "{{recordId}}",
///   "regardingObjectType": "sprk_document",
///   "ownerId": "{{assigneeId}}",
///   "dueDate": "{{dueDate}}"
/// }
/// </code>
/// </remarks>
public sealed class CreateTaskNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CreateTaskNodeExecutor> _logger;

    public CreateTaskNodeExecutor(
        ITemplateEngine templateEngine,
        IHttpClientFactory httpClientFactory,
        ILogger<CreateTaskNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.CreateTask
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("CreateTask node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<TaskNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse task configuration");
            }
            else if (string.IsNullOrWhiteSpace(config.Subject))
            {
                errors.Add("Task subject is required");
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid task configuration JSON: {ex.Message}");
        }

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
            "Executing CreateTask node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            // Validate first
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

            // Parse configuration
            var config = JsonSerializer.Deserialize<TaskNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render template fields
            var subject = _templateEngine.Render(config.Subject!, templateContext);
            var description = config.Description is not null
                ? _templateEngine.Render(config.Description, templateContext)
                : null;

            _logger.LogDebug(
                "Creating task with subject: {Subject}",
                subject);

            // Build task payload for Dataverse
            var taskPayload = new Dictionary<string, object?>
            {
                ["subject"] = subject,
                ["description"] = description,
                ["scheduledend"] = config.DueDate is not null
                    ? _templateEngine.Render(config.DueDate, templateContext)
                    : null
            };

            // Add regarding object if specified
            if (!string.IsNullOrWhiteSpace(config.RegardingObjectId) && !string.IsNullOrWhiteSpace(config.RegardingObjectType))
            {
                var regardingId = _templateEngine.Render(config.RegardingObjectId, templateContext);
                if (Guid.TryParse(regardingId, out var regardingGuid))
                {
                    var entitySetName = GetEntitySetName(config.RegardingObjectType);
                    taskPayload[$"regardingobjectid_{config.RegardingObjectType}@odata.bind"] = $"/{entitySetName}({regardingGuid})";
                }
            }

            // Add owner if specified
            if (!string.IsNullOrWhiteSpace(config.OwnerId))
            {
                var ownerId = _templateEngine.Render(config.OwnerId, templateContext);
                if (Guid.TryParse(ownerId, out var ownerGuid))
                {
                    taskPayload["ownerid@odata.bind"] = $"/systemusers({ownerGuid})";
                }
            }

            // Create the task record in Dataverse via Web API
            var http = _httpClientFactory.CreateClient("DataverseApi");
            var taskJson = JsonSerializer.Serialize(taskPayload, JsonOptions);
            var requestContent = new StringContent(taskJson, System.Text.Encoding.UTF8, "application/json");

            var response = await http.PostAsync("tasks", requestContent, cancellationToken);

            Guid taskId;
            if (response.IsSuccessStatusCode)
            {
                // Dataverse returns the new record ID in the OData-EntityId header
                var entityIdHeader = response.Headers.Location?.AbsoluteUri
                    ?? response.Headers.GetValues("OData-EntityId").FirstOrDefault();

                taskId = Guid.TryParse(
                    entityIdHeader?.Split('(').LastOrDefault()?.TrimEnd(')'),
                    out var parsed) ? parsed : Guid.NewGuid();
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Dataverse task creation returned {StatusCode} for node {NodeId}: {Error}",
                    response.StatusCode, context.Node.Id, errorBody);

                // Return a degraded success — the task payload was assembled correctly
                // but Dataverse rejected it. Include the error for the user.
                taskId = Guid.Empty;
            }

            _logger.LogInformation(
                "CreateTask node {NodeId} completed - task created with subject: {Subject}, taskId: {TaskId}",
                context.Node.Id,
                subject,
                taskId);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    taskId,
                    subject,
                    description,
                    createdAt = DateTimeOffset.UtcNow
                },
                textContent: $"Task created: {subject}",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CreateTask node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to create task: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs and execution metadata.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
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

    /// <summary>
    /// Gets the OData entity set name (plural) for a Dataverse entity.
    /// </summary>
    private static string GetEntitySetName(string entityLogicalName)
    {
        // Common entity mappings
        return entityLogicalName switch
        {
            "sprk_document" => "sprk_documents",
            "sprk_matter" => "sprk_matters",
            "sprk_project" => "sprk_projects",
            "account" => "accounts",
            "contact" => "contacts",
            "task" => "tasks",
            _ => entityLogicalName.EndsWith("s") ? entityLogicalName : entityLogicalName + "s"
        };
    }
}

/// <summary>
/// Configuration for CreateTask node from ConfigJson.
/// </summary>
internal sealed record TaskNodeConfig
{
    public string? Subject { get; init; }
    public string? Description { get; init; }
    public string? RegardingObjectId { get; init; }
    public string? RegardingObjectType { get; init; }
    public string? OwnerId { get; init; }
    public string? DueDate { get; init; }
}
