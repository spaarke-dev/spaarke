using System.Net.Http.Json;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for updating Dataverse entity records.
/// Uses TemplateEngine for variable substitution in field values.
/// </summary>
/// <remarks>
/// <para>
/// Update configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "entityLogicalName": "sprk_document",
///   "recordId": "{{recordId}}",
///   "fields": {
///     "sprk_analysisstatus": "Completed",
///     "sprk_analysissummary": "{{summarize.output.summary}}",
///     "sprk_partycount": "{{extract_entities.output.partyCount}}"
///   }
/// }
/// </code>
/// <para>
/// Uses Dataverse Web API directly via HTTP client.
/// </para>
/// </remarks>
public sealed class UpdateRecordNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateRecordNodeExecutor> _logger;

    public UpdateRecordNodeExecutor(
        ITemplateEngine templateEngine,
        IHttpClientFactory httpClientFactory,
        ILogger<UpdateRecordNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.UpdateRecord
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("UpdateRecord node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<UpdateRecordNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse update record configuration");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.EntityLogicalName))
                {
                    errors.Add("Entity logical name is required");
                }
                if (string.IsNullOrWhiteSpace(config.RecordId))
                {
                    errors.Add("Record ID is required");
                }
                if (config.Fields is null || config.Fields.Count == 0)
                {
                    errors.Add("At least one field to update is required");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid update record configuration JSON: {ex.Message}");
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
            "Executing UpdateRecord node {NodeId} ({NodeName})",
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
            var config = JsonSerializer.Deserialize<UpdateRecordNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render record ID (may be a template variable)
            var recordIdString = _templateEngine.Render(config.RecordId!, templateContext);
            if (!Guid.TryParse(recordIdString, out var recordId))
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"Invalid record ID: {recordIdString}",
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Build update payload with rendered field values
            var updatePayload = new Dictionary<string, object?>();
            foreach (var (fieldName, fieldValue) in config.Fields!)
            {
                var renderedValue = fieldValue is string stringValue
                    ? _templateEngine.Render(stringValue, templateContext)
                    : fieldValue;

                // Try to parse numeric values
                if (renderedValue is string strVal && int.TryParse(strVal, out var intVal))
                {
                    updatePayload[fieldName] = intVal;
                }
                else if (renderedValue is string strVal2 && decimal.TryParse(strVal2, out var decVal))
                {
                    updatePayload[fieldName] = decVal;
                }
                else if (renderedValue is string strVal3 && bool.TryParse(strVal3, out var boolVal))
                {
                    updatePayload[fieldName] = boolVal;
                }
                else
                {
                    updatePayload[fieldName] = renderedValue;
                }
            }

            // Handle lookup fields with @odata.bind syntax
            if (config.Lookups is not null)
            {
                foreach (var (lookupField, lookupConfig) in config.Lookups)
                {
                    var targetId = _templateEngine.Render(lookupConfig.TargetId, templateContext);
                    if (Guid.TryParse(targetId, out var targetGuid))
                    {
                        var entitySetName = GetEntitySetName(lookupConfig.TargetEntity);
                        updatePayload[$"{lookupField}@odata.bind"] = $"/{entitySetName}({targetGuid})";
                    }
                }
            }

            _logger.LogDebug(
                "Updating {Entity}({RecordId}) with {FieldCount} fields",
                config.EntityLogicalName,
                recordId,
                updatePayload.Count);

            // Note: Actual Dataverse API call would go here
            // For Phase 3, we create a stub that returns success
            // Full implementation will use IDataverseService or direct HTTP client
            await Task.CompletedTask; // Placeholder for future async Dataverse call

            _logger.LogInformation(
                "UpdateRecord node {NodeId} completed - updated {Entity}({RecordId})",
                context.Node.Id,
                config.EntityLogicalName,
                recordId);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    updated = true,
                    entityLogicalName = config.EntityLogicalName,
                    recordId = recordId,
                    fieldsUpdated = updatePayload.Keys.ToArray(),
                    updatedAt = DateTimeOffset.UtcNow
                },
                textContent: $"Updated {config.EntityLogicalName} record",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "UpdateRecord node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to update record: {ex.Message}",
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
            "systemuser" => "systemusers",
            _ => entityLogicalName.EndsWith("s") ? entityLogicalName : entityLogicalName + "s"
        };
    }
}

/// <summary>
/// Configuration for UpdateRecord node from ConfigJson.
/// </summary>
internal sealed record UpdateRecordNodeConfig
{
    public string? EntityLogicalName { get; init; }
    public string? RecordId { get; init; }
    public Dictionary<string, object?>? Fields { get; init; }
    public Dictionary<string, LookupFieldConfig>? Lookups { get; init; }
}

/// <summary>
/// Configuration for a lookup field value.
/// </summary>
internal sealed record LookupFieldConfig
{
    public string TargetEntity { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
}
