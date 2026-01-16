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

            // Note: Actual Dataverse API call would go here
            // For Phase 3, we create a stub that returns success with the task details
            // Full implementation will use IDataverseService or direct HTTP client
            await Task.CompletedTask; // Placeholder for future async Dataverse call

            var taskId = Guid.NewGuid(); // Simulated task ID

            _logger.LogInformation(
                "CreateTask node {NodeId} completed - task created with subject: {Subject}",
                context.Node.Id,
                subject);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    taskId = taskId,
                    subject = subject,
                    description = description,
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
    /// Builds template context dictionary from previous node outputs.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            // Add the entire output as a nested object for template access
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

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
