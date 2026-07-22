using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
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
/// <para>
/// <b>Metadata-driven coercion (defect-hardening, R5 task 030)</b> — a <c>type:"string"</c> mapping
/// whose TARGET column is actually Choice/Boolean/Number is coerced against the column's real
/// Dataverse metadata (resolved via <see cref="Dataverse.MetadataService"/>, which caches the
/// projected entity metadata for 6h in Redis — see <see cref="Dataverse.MetadataService"/> remarks).
/// This closes the gap where an AI-authored fieldMapping declares <c>type:"string"</c> but the
/// rendered value is a Choice label; previously this fell into the verbatim String branch and
/// Dataverse rejected the PATCH with a 500. An unmatchable Choice label now fails loud with a
/// descriptive <see cref="FieldCoercionException"/> instead of a silent pass-through.
/// </para>
/// </remarks>
public sealed class UpdateRecordNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITemplateEngine _templateEngine;
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UpdateRecordNodeExecutor> _logger;

    public UpdateRecordNodeExecutor(
        ITemplateEngine templateEngine,
        IFieldMappingDataverseService fieldMappingService,
        IServiceScopeFactory scopeFactory,
        ILogger<UpdateRecordNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _fieldMappingService = fieldMappingService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.UpdateRecord
    };

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from UpdateRecordNodeConfig: entityLogicalName (required), recordId (required,
    // template-rendered), fieldMappings (typed) OR fields (legacy flat), lookups.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.UpdateRecord),
        ExecutorTypeValue: (int)ExecutorType.UpdateRecord,
        Description: "Updates a Dataverse entity record via PATCH. At least one of fieldMappings (typed, preferred) or fields (legacy flat) MUST be set. Supports {{var}} substitution.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "entityLogicalName",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Dataverse entity logical name (e.g., 'sprk_document', 'sprk_matter'). Required.",
                Default: null),
            new(
                Name: "recordId",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Record GUID to update. Required. Supports {{var}} substitution (e.g., '{{document.id}}', '{{recordId}}').",
                Default: null),
            new(
                Name: "fieldMappings",
                Type: SchemaFieldType.Array,
                Required: false,
                Description: "Typed field mappings (preferred). Array of { field, type (string|choice|boolean|number|lookup), value (template), options? (label→int for choice) }. AI string output is coerced to the declared Dataverse type.",
                Default: null),
            new(
                Name: "fields",
                Type: SchemaFieldType.Object,
                Required: false,
                Description: "Legacy flat field→value dictionary (backward compat). Values support {{var}} substitution and are coerced via heuristic int/decimal/bool parse. Prefer fieldMappings for new playbooks.",
                Default: null),
            new(
                Name: "lookups",
                Type: SchemaFieldType.Object,
                Required: false,
                Description: "Optional lookup-field map: { fieldName: { targetEntity, targetId } }. Resolved to OData @odata.bind syntax. targetId supports {{var}} substitution.",
                Default: null)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

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

            // Render field values against the template context (session-specific), then
            // delegate coercion + metadata resolution + PATCH to the session-agnostic
            // UpdateRecordActionCore (task 031). Two config paths — typed fieldMappings
            // (preferred) OR legacy flat fields; mappings win when both are present.
            List<RenderedFieldMapping>? renderedMappings = null;
            if (config.FieldMappings is { Length: > 0 })
            {
                renderedMappings = new List<RenderedFieldMapping>(config.FieldMappings.Length);
                foreach (var mapping in config.FieldMappings)
                {
                    var renderedValue = _templateEngine.Render(mapping.Value, templateContext);
                    renderedMappings.Add(new RenderedFieldMapping(
                        mapping.Field, mapping.Type, renderedValue, mapping.Options));
                }
            }

            Dictionary<string, string?>? renderedLegacyFields = null;
            if ((renderedMappings is null || renderedMappings.Count == 0) && config.Fields is { Count: > 0 })
            {
                renderedLegacyFields = new Dictionary<string, string?>();
                foreach (var (fieldName, fieldValue) in config.Fields)
                {
                    var rawValue = ExtractStringValue(fieldValue);
                    var renderedValue = rawValue != null
                        ? _templateEngine.Render(rawValue, templateContext)
                        : null;
                    renderedLegacyFields[fieldName] = renderedValue;
                }
            }

            List<RenderedLookup>? renderedLookups = null;
            if (config.Lookups is not null)
            {
                renderedLookups = new List<RenderedLookup>(config.Lookups.Count);
                foreach (var (lookupField, lookupConfig) in config.Lookups)
                {
                    var targetId = _templateEngine.Render(lookupConfig.TargetId, templateContext);
                    renderedLookups.Add(new RenderedLookup(lookupField, lookupConfig.TargetEntity, targetId));
                }
            }

            var core = new UpdateRecordActionCore(_fieldMappingService, _scopeFactory, _logger);
            var fieldsUpdated = await core.UpdateAsync(
                new UpdateRecordActionInput(
                    config.EntityLogicalName!,
                    recordId,
                    renderedMappings,
                    renderedLegacyFields,
                    renderedLookups),
                cancellationToken);

            _logger.LogInformation(
                "UpdateRecord node {NodeId} completed - updated {Entity}({RecordId}) with {FieldCount} fields",
                context.Node.Id,
                config.EntityLogicalName,
                recordId,
                fieldsUpdated.Count);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    updated = true,
                    entityLogicalName = config.EntityLogicalName,
                    recordId = recordId,
                    fieldsUpdated = fieldsUpdated.ToArray(),
                    updatedAt = DateTimeOffset.UtcNow
                },
                textContent: $"Updated {config.EntityLogicalName} record",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (FieldCoercionException ex)
        {
            // Fail-loud path for an unmatchable Choice label (or missing option-set metadata) —
            // R5 task 030 / FR-C1. This is a validation-shaped failure caught BEFORE the Dataverse
            // PATCH is issued, so it never surfaces as the downstream Dataverse 500 the verbatim
            // pass-through used to produce.
            _logger.LogWarning(
                "UpdateRecord node {NodeId} field coercion failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                ex.Message,
                NodeErrorCodes.ValidationFailed,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
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
