using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for updating Dataverse entity records.
/// Uses TemplateEngine for variable substitution in field values,
/// then delegates to IDataverseService for the actual PATCH call.
/// </summary>
/// <remarks>
/// <para>
/// Supports two configuration formats:
/// </para>
/// <para>
/// <b>New format (typed field mappings)</b> — each field specifies its Dataverse type
/// so AI string output is coerced to the correct value (e.g., "Complete" → 100000002
/// for Choice fields, "yes" → true for Boolean fields):
/// </para>
/// <code>
/// {
///   "entityLogicalName": "sprk_document",
///   "recordId": "{{document.id}}",
///   "fieldMappings": [
///     { "field": "sprk_filesummary", "type": "string", "value": "{{output_ai.text}}" },
///     { "field": "sprk_status", "type": "choice", "value": "{{output_ai.output.status}}",
///       "options": { "pending": 100000000, "complete": 100000002 } }
///   ]
/// }
/// </code>
/// <para>
/// <b>Legacy format</b> — flat field→value dictionary with heuristic type parsing:
/// </para>
/// <code>
/// {
///   "entityLogicalName": "sprk_document",
///   "recordId": "{{recordId}}",
///   "fields": { "sprk_analysisstatus": "Completed" }
/// }
/// </code>
/// <para>
/// Uses IFieldMappingDataverseService (Singleton) to PATCH records via the Dataverse Web API.
/// </para>
/// </remarks>
public sealed class UpdateRecordNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly ILogger<UpdateRecordNodeExecutor> _logger;

    public UpdateRecordNodeExecutor(
        ITemplateEngine templateEngine,
        IFieldMappingDataverseService fieldMappingService,
        ILogger<UpdateRecordNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _fieldMappingService = fieldMappingService;
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

        var config = ParseConfig(context.Node.ConfigJson);
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
            var hasFields = config.Fields is { Count: > 0 };
            var hasMappings = config.FieldMappings is { Length: > 0 };
            if (!hasFields && !hasMappings)
            {
                errors.Add("At least one field to update is required (use 'fields' or 'fieldMappings')");
            }
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

            // Parse configuration (handles both direct and nested configJson formats)
            var config = ParseConfig(context.Node.ConfigJson!)!;

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

            // Build update payload — two paths:
            //   1. New: typed fieldMappings with explicit coercion (choice→int, bool, etc.)
            //   2. Legacy: flat fields dict with heuristic int/decimal/bool parsing
            var updatePayload = new Dictionary<string, object?>();

            if (config.FieldMappings is { Length: > 0 })
            {
                // NEW PATH: typed field mappings with coercion
                foreach (var mapping in config.FieldMappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.Field)) continue;

                    var renderedValue = _templateEngine.Render(mapping.Value, templateContext);
                    var coercedValue = CoerceFieldValue(mapping, renderedValue, _logger);
                    updatePayload[mapping.Field] = coercedValue;
                }
            }
            else if (config.Fields is { Count: > 0 })
            {
                // LEGACY PATH: flat fields dict with heuristic type parsing
                foreach (var (fieldName, fieldValue) in config.Fields)
                {
                    var rawValue = ExtractStringValue(fieldValue);
                    var renderedValue = rawValue != null
                        ? _templateEngine.Render(rawValue, templateContext)
                        : null;
                    updatePayload[fieldName] = HeuristicParse(renderedValue);
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

            // PATCH the Dataverse record via IDataverseService
            await _fieldMappingService.UpdateRecordFieldsAsync(
                config.EntityLogicalName!,
                recordId,
                updatePayload,
                cancellationToken);

            _logger.LogInformation(
                "UpdateRecord node {NodeId} completed - updated {Entity}({RecordId}) with {FieldCount} fields",
                context.Node.Id,
                config.EntityLogicalName,
                recordId,
                updatePayload.Count);

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
    /// Parses update record configuration from ConfigJson.
    /// Handles two formats:
    ///   1. Direct: top-level entityLogicalName, recordId, fields (Code Page sync)
    ///   2. Nested: configJson property contains a JSON string with the config (PCF sync)
    /// </summary>
    private static UpdateRecordNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            // Try direct deserialization (Code Page buildConfigJson format)
            var config = JsonSerializer.Deserialize<UpdateRecordNodeConfig>(configJson, JsonOptions);
            if (!string.IsNullOrWhiteSpace(config?.EntityLogicalName))
                return config;

            // Fallback: check for nested configJson property (PCF stripKnownFields format)
            // The PCF sync stores the form's JSON string as a nested "configJson" property
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("configJson", out var nested) &&
                nested.ValueKind == JsonValueKind.String)
            {
                var nestedJson = nested.GetString();
                if (!string.IsNullOrWhiteSpace(nestedJson))
                {
                    return JsonSerializer.Deserialize<UpdateRecordNodeConfig>(nestedJson, JsonOptions);
                }
            }

            return config;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs and execution metadata.
    /// Includes document context ({{document.id}}, {{document.name}}) and run context
    /// ({{run.id}}, {{run.playbookId}}) for use in templates like recordId.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        // Add previous node outputs (e.g., {{analyze.text}}, {{analyze.output.summary}})
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? FlattenArrays(TemplateEngine.ConvertJsonElement(output.StructuredData.Value))
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

        // Add document context (e.g., {{document.id}}, {{document.name}}, {{document.fileName}})
        if (context.Document is not null)
        {
            templateContext["document"] = new
            {
                id = context.Document.DocumentId.ToString(),
                name = context.Document.Name,
                fileName = context.Document.FileName
            };
        }

        // Add run context (e.g., {{run.id}}, {{run.playbookId}}, {{run.tenantId}})
        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId
        };

        return templateContext;
    }

    /// <summary>
    /// Recursively converts List values to newline-joined strings so Handlebars can render
    /// them as scalar field values. AI often returns arrays (e.g., TL;DR bullet points)
    /// but Dataverse text fields expect strings.
    /// </summary>
    private static object? FlattenArrays(object? value)
    {
        if (value is List<object?> list)
            return string.Join("\n", list.Where(x => x != null).Select(x => $"- {x}"));
        if (value is Dictionary<string, object?> dict)
            return dict.ToDictionary(kv => kv.Key, kv => FlattenArrays(kv.Value), StringComparer.OrdinalIgnoreCase);
        return value;
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

    // ---------------------------------------------------------------------------
    // Typed field mapping coercion
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Coerces a rendered template string to the CLR type expected by the
    /// Dataverse OData Web API, based on the field mapping's declared type.
    /// </summary>
    private static object? CoerceFieldValue(
        FieldMappingEntry mapping,
        string? renderedValue,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(renderedValue))
            return null;

        switch (mapping.Type)
        {
            case FieldMappingType.String:
                return renderedValue;

            case FieldMappingType.Choice:
                if (mapping.Options is null || mapping.Options.Count == 0)
                {
                    logger.LogWarning(
                        "Choice field '{Field}' has no options map; falling back to int parse",
                        mapping.Field);
                    return int.TryParse(renderedValue.Trim(), out var raw) ? raw : (object)renderedValue;
                }

                // Case-insensitive label lookup
                var trimmed = renderedValue.Trim();
                foreach (var (label, optionValue) in mapping.Options)
                {
                    if (string.Equals(label, trimmed, StringComparison.OrdinalIgnoreCase))
                        return optionValue;
                }

                // Fallback: AI may have returned the int value directly (e.g. "100000002")
                if (int.TryParse(trimmed, out var intFallback) &&
                    mapping.Options.ContainsValue(intFallback))
                    return intFallback;

                logger.LogWarning(
                    "Choice field '{Field}': value '{Value}' not found in options [{Options}]",
                    mapping.Field, renderedValue,
                    string.Join(", ", mapping.Options.Keys));
                return renderedValue; // pass through; Dataverse will reject if invalid

            case FieldMappingType.Boolean:
                return renderedValue.Trim().ToLowerInvariant() switch
                {
                    "true" or "yes" or "1" or "on" => true,
                    "false" or "no" or "0" or "off" => false,
                    _ => bool.TryParse(renderedValue, out var b) ? b : (object)renderedValue
                };

            case FieldMappingType.Number:
                var numStr = renderedValue.Trim();
                if (int.TryParse(numStr, out var intVal)) return intVal;
                if (decimal.TryParse(numStr, out var decVal)) return decVal;
                return renderedValue;

            case FieldMappingType.Lookup:
                // Future: resolve by querying Dataverse for matching record.
                // For now, pass through (value should be a rendered GUID).
                return renderedValue;

            default:
                return renderedValue;
        }
    }

    // ---------------------------------------------------------------------------
    // Legacy field value helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Extracts a string from an object that may be a string, JsonElement, or null.
    /// Used by the legacy fields path.
    /// </summary>
    private static string? ExtractStringValue(object? fieldValue)
    {
        return fieldValue switch
        {
            string s => s,
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => je.GetRawText()
            },
            null => null,
            _ => fieldValue.ToString()
        };
    }

    /// <summary>
    /// Heuristic type parsing for the legacy fields path: tries int, then decimal, then bool.
    /// </summary>
    private static object? HeuristicParse(string? value)
    {
        if (value is null) return null;
        if (int.TryParse(value, out var i)) return i;
        if (decimal.TryParse(value, out var d)) return d;
        if (bool.TryParse(value, out var b)) return b;
        return value;
    }
}

// ---------------------------------------------------------------------------
// Configuration records
// ---------------------------------------------------------------------------

/// <summary>
/// Configuration for UpdateRecord node from ConfigJson.
/// Supports two field formats: legacy <see cref="Fields"/> dict and
/// new typed <see cref="FieldMappings"/> array.
/// </summary>
internal sealed record UpdateRecordNodeConfig
{
    public string? EntityLogicalName { get; init; }
    public string? RecordId { get; init; }

    /// <summary>Legacy flat field→value dictionary (backward compat).</summary>
    public Dictionary<string, object?>? Fields { get; init; }

    /// <summary>Typed field mappings with coercion metadata (preferred).</summary>
    public FieldMappingEntry[]? FieldMappings { get; init; }

    /// <summary>Lookup field configurations with @odata.bind resolution.</summary>
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

// ---------------------------------------------------------------------------
// Typed field mapping types
// ---------------------------------------------------------------------------

/// <summary>
/// Discriminator for field value coercion in UpdateRecord nodes.
/// Determines how AI string output is converted to a Dataverse-compatible value.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum FieldMappingType
{
    /// <summary>Pass-through string value. No coercion.</summary>
    String,
    /// <summary>Map label → int via options dictionary. Case-insensitive.</summary>
    Choice,
    /// <summary>Parse truthy/falsy strings to bool.</summary>
    Boolean,
    /// <summary>Parse to int or decimal.</summary>
    Number,
    /// <summary>Future: resolve lookup by targetEntity + matchField.</summary>
    Lookup
}

/// <summary>
/// A single typed field mapping with optional coercion metadata.
/// Used by the UpdateRecord node to convert AI string output to Dataverse-compatible values.
/// </summary>
internal sealed record FieldMappingEntry
{
    /// <summary>Dataverse field logical name (e.g. "sprk_filesummarystatus").</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>Field type discriminator for coercion.</summary>
    public FieldMappingType Type { get; init; } = FieldMappingType.String;

    /// <summary>Template value (Handlebars). Rendered against context before coercion.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// For Choice fields: case-insensitive label → Dataverse option value mapping.
    /// E.g. { "pending": 100000000, "complete": 100000002 }.
    /// </summary>
    public Dictionary<string, int>? Options { get; init; }
}
