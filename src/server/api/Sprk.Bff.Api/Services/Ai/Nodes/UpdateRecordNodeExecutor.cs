using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

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

            // Build update payload — two paths:
            //   1. New: typed fieldMappings with explicit coercion (choice→int, bool, etc.)
            //   2. Legacy: flat fields dict with heuristic int/decimal/bool parsing
            var updatePayload = new Dictionary<string, object?>();

            if (config.FieldMappings is { Length: > 0 })
            {
                // NEW PATH: typed field mappings with coercion.
                // Metadata-driven coercion (R5 task 030): resolve the target entity's column
                // metadata ONCE per run (not once per field) when at least one mapping declares
                // type:"string" — String-typed mappings targeting a Choice/Boolean/Number column
                // are coerced against the real column type instead of passed through verbatim.
                EntityMetadataDto? entityMetadata = null;
                if (config.FieldMappings.Any(m => m.Type == FieldMappingType.String))
                {
                    entityMetadata = await ResolveEntityMetadataAsync(
                        config.EntityLogicalName!, cancellationToken).ConfigureAwait(false);
                }

                foreach (var mapping in config.FieldMappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.Field)) continue;

                    var renderedValue = _templateEngine.Render(mapping.Value, templateContext);
                    var coercedValue = CoerceFieldValue(mapping, renderedValue, entityMetadata, _logger);
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
    /// <param name="mapping">The typed field mapping (declared type + optional Choice options).</param>
    /// <param name="renderedValue">The template-rendered string value.</param>
    /// <param name="entityMetadata">
    /// The target entity's cached column metadata (R5 task 030), or <c>null</c> if it was not
    /// resolved for this run (either no String-typed mapping required it, or resolution failed and
    /// was logged non-fatally). Only consulted by the <see cref="FieldMappingType.String"/> branch.
    /// </param>
    /// <param name="logger">Logger for coercion diagnostics.</param>
    /// <exception cref="FieldCoercionException">
    /// Thrown when a <c>type:"string"</c> mapping targets a Choice column and the rendered value
    /// does not match any option label or numeric option value (R5 task 030 / FR-C1 fail-loud rule).
    /// </exception>
    private static object? CoerceFieldValue(
        FieldMappingEntry mapping,
        string? renderedValue,
        EntityMetadataDto? entityMetadata,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(renderedValue))
            return null;

        switch (mapping.Type)
        {
            case FieldMappingType.String:
                return CoerceStringMapping(mapping.Field, renderedValue, entityMetadata, logger);

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

    /// <summary>
    /// Resolves the target entity's column metadata via the existing <see cref="MetadataService"/>
    /// (R5 task 030). <see cref="MetadataService"/> is registered Scoped (it depends on the Scoped
    /// <c>IDataverseService</c>), while this executor is registered Singleton — bridged via
    /// <see cref="IServiceScopeFactory"/> per the Singleton+Scoped pattern already used by
    /// <see cref="LookupUserMembershipNodeExecutor"/> and <see cref="AgentServiceNodeExecutor"/>.
    /// <see cref="MetadataService"/> caches the projected DTO in Redis for 6h (FR-BFF-03), so this
    /// call is a cache read in the common case, not a fresh Dataverse metadata round-trip.
    /// </summary>
    /// <remarks>
    /// Resolution failures (e.g., transient Dataverse/Redis errors) are logged and treated as
    /// non-fatal: the caller falls back to verbatim String pass-through for that run, preserving
    /// the executor's pre-existing behavior rather than blocking the whole record update. This is
    /// distinct from the FAIL LOUD rule for an unmatchable Choice label once metadata IS available.
    /// </remarks>
    private async Task<EntityMetadataDto?> ResolveEntityMetadataAsync(
        string entityLogicalName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var metadataService = scope.ServiceProvider.GetRequiredService<MetadataService>();
            return await metadataService.GetMetadataAsync(entityLogicalName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve column metadata for entity '{Entity}'; type:\"string\" mappings " +
                "will pass through verbatim for this run",
                entityLogicalName);
            return null;
        }
    }

    /// <summary>
    /// Metadata-driven coercion for a <c>type:"string"</c> mapping (R5 task 030 / FR-C1). If the
    /// target column's real Dataverse metadata type is Choice/Boolean/Number, coerces the rendered
    /// value accordingly instead of passing it through verbatim. Text/Memo columns (and any field
    /// not found in <paramref name="entityMetadata"/>, or when metadata resolution was unavailable)
    /// keep the existing verbatim pass-through behavior.
    /// </summary>
    /// <exception cref="FieldCoercionException">
    /// Thrown when the column is Choice and the rendered value cannot be matched to any option
    /// label or numeric option value — see <see cref="CoerceChoiceFromMetadata"/>.
    /// </exception>
    private static object CoerceStringMapping(
        string fieldName,
        string renderedValue,
        EntityMetadataDto? entityMetadata,
        ILogger logger)
    {
        var attribute = entityMetadata?.Attributes.FirstOrDefault(
            a => string.Equals(a.LogicalName, fieldName, StringComparison.OrdinalIgnoreCase));

        if (attribute is null)
        {
            // No metadata available for this run (resolution failed/skipped) or the field isn't
            // in the projected attribute list — preserve the original verbatim String behavior.
            return renderedValue;
        }

        var trimmed = renderedValue.Trim();

        switch (attribute.AttributeType)
        {
            case "Picklist":
            case "State":
            case "Status":
            case "MultiSelectPicklist":
                return CoerceChoiceFromMetadata(fieldName, trimmed, attribute.OptionSet, logger);

            case "Boolean":
                // Mirrors the FieldMappingType.Boolean branch above.
                return trimmed.ToLowerInvariant() switch
                {
                    "true" or "yes" or "1" or "on" => true,
                    "false" or "no" or "0" or "off" => false,
                    _ => bool.TryParse(trimmed, out var b) ? b : (object)renderedValue
                };

            case "Integer":
            case "BigInt":
            case "Decimal":
            case "Double":
            case "Money":
                // Mirrors the FieldMappingType.Number branch above: int first, then decimal.
                if (int.TryParse(trimmed, out var intVal)) return intVal;
                if (decimal.TryParse(trimmed, out var decVal)) return decVal;
                return renderedValue;

            default:
                // Text/Memo/etc. — verbatim pass-through (existing behavior preserved).
                return renderedValue;
        }
    }

    /// <summary>
    /// Resolves a rendered value against a Choice column's real option-set metadata. Mirrors the
    /// case-insensitive label lookup + numeric-value fallback used by the
    /// <see cref="FieldMappingType.Choice"/> branch of <see cref="CoerceFieldValue"/> (lines
    /// 488-514), but sources the valid options from Dataverse metadata instead of the mapping's
    /// own <c>options</c> map — and FAILS LOUD instead of passing the raw string through, because
    /// a metadata-confirmed Choice column will otherwise 500 on PATCH (R5 task 030 / FR-C1).
    /// </summary>
    /// <exception cref="FieldCoercionException">
    /// Thrown when the option set is empty, or the trimmed value matches neither an option label
    /// (case-insensitive) nor a valid numeric option value.
    /// </exception>
    private static object CoerceChoiceFromMetadata(
        string fieldName,
        string trimmedValue,
        OptionSetDto? optionSet,
        ILogger logger)
    {
        var options = optionSet?.Options ?? Array.Empty<OptionDto>();

        if (options.Count == 0)
        {
            logger.LogWarning(
                "Choice field '{Field}' has no option-set metadata; cannot coerce type:\"string\" value '{Value}'",
                fieldName, trimmedValue);
            throw new FieldCoercionException(
                $"Field '{fieldName}' is a Choice column with no option-set metadata available; " +
                $"cannot coerce value '{trimmedValue}'.");
        }

        // Case-insensitive label lookup — mirrors the FieldMappingType.Choice branch.
        foreach (var option in options)
        {
            if (string.Equals(option.Label, trimmedValue, StringComparison.OrdinalIgnoreCase))
                return option.Value;
        }

        // Fallback: AI may have returned the int option value directly (e.g. "100000002").
        if (int.TryParse(trimmedValue, out var intFallback) &&
            options.Any(o => o.Value == intFallback))
            return intFallback;

        var validLabels = string.Join(", ", options.Select(o => o.Label));

        logger.LogWarning(
            "Choice field '{Field}': value '{Value}' not found in metadata options [{Options}]",
            fieldName, trimmedValue, validLabels);

        // FAIL LOUD (R5 task 030 / FR-C1) — do NOT return the raw string; the caller (ExecuteAsync)
        // catches FieldCoercionException and returns a NODE_VALIDATION_FAILED NodeOutput.Error
        // BEFORE the Dataverse PATCH is issued, instead of letting Dataverse reject it with a 500.
        throw new FieldCoercionException(
            $"Field '{fieldName}': value '{trimmedValue}' is not a valid option. " +
            $"Valid options: {validLabels}.");
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

// ---------------------------------------------------------------------------
// Metadata-driven coercion failure (R5 task 030 / FR-C1)
// ---------------------------------------------------------------------------

/// <summary>
/// Thrown by <see cref="UpdateRecordNodeExecutor.CoerceFieldValue"/> (via
/// <c>CoerceStringMapping</c> / <c>CoerceChoiceFromMetadata</c>) when a <c>type:"string"</c>
/// mapping's rendered value cannot be resolved against the target column's real Choice metadata.
/// Caught by <see cref="UpdateRecordNodeExecutor.ExecuteAsync"/> and surfaced as a
/// <c>NODE_VALIDATION_FAILED</c> <see cref="NodeOutput"/> — the FAIL LOUD contract for an
/// unmatchable Choice label (never a silent pass-through, never a downstream Dataverse 500).
/// </summary>
internal sealed class FieldCoercionException : Exception
{
    public FieldCoercionException(string message) : base(message)
    {
    }
}
