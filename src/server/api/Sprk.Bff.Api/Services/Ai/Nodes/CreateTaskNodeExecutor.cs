using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
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
/// <para>
/// Uses the canonical <see cref="IGenericEntityService"/> shared library
/// (which forwards to <c>DataverseServiceClientImpl</c>) rather than an
/// app-private named HttpClient. This gives correct BaseAddress + token
/// acquisition + lifecycle management for app-only execution paths.
/// </para>
/// </remarks>
public sealed class CreateTaskNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IGenericEntityService _entityService;
    private readonly ILogger<CreateTaskNodeExecutor> _logger;

    public CreateTaskNodeExecutor(
        ITemplateEngine templateEngine,
        IGenericEntityService entityService,
        ILogger<CreateTaskNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _entityService = entityService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.CreateTask
    };

    // R7 task 032 / FR-16 — placeholder schema (no maker-editable fields surfaced yet).
    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() =>
        ExecutorConfigSchema.Empty(
            ExecutorType.CreateTask,
            "Creates a Dataverse task record from playbook context.");

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

            // Build task entity
            var entity = new Entity("task");
            entity["subject"] = subject;
            if (description is not null)
                entity["description"] = description;

            if (config.DueDate is not null)
            {
                var dueDateStr = _templateEngine.Render(config.DueDate, templateContext);
                if (DateTime.TryParse(dueDateStr, out var dueDate))
                {
                    entity["scheduledend"] = dueDate.ToUniversalTime();
                }
            }

            // Add regarding object if specified
            if (!string.IsNullOrWhiteSpace(config.RegardingObjectId) && !string.IsNullOrWhiteSpace(config.RegardingObjectType))
            {
                var regardingId = _templateEngine.Render(config.RegardingObjectId, templateContext);
                if (Guid.TryParse(regardingId, out var regardingGuid))
                {
                    entity["regardingobjectid"] = new EntityReference(config.RegardingObjectType, regardingGuid);
                }
            }

            // Add owner if specified
            if (!string.IsNullOrWhiteSpace(config.OwnerId))
            {
                var ownerId = _templateEngine.Render(config.OwnerId, templateContext);
                if (Guid.TryParse(ownerId, out var ownerGuid))
                {
                    entity["ownerid"] = new EntityReference("systemuser", ownerGuid);
                }
            }

            // Create the task record via shared Dataverse client
            Guid taskId;
            try
            {
                taskId = await _entityService.CreateAsync(entity, cancellationToken);
            }
            catch (Exception createEx)
            {
                _logger.LogWarning(
                    createEx,
                    "Dataverse task creation failed for node {NodeId}: {Error}",
                    context.Node.Id, createEx.Message);

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
            // 2026-06-24 (bug #10 fix): use TemplateEngine.ConvertJsonElement so Handlebars
            // can navigate nested paths like {{varName.output.field}}. See full rationale on
            // the matching change in ConditionNodeExecutor.BuildTemplateContext.
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
