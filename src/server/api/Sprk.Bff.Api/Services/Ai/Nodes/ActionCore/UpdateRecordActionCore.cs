using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

// ---------------------------------------------------------------------------
// Session-agnostic Layer-A action seam — UpdateRecord (task 031 / FR-07).
//
// Extracted VERBATIM from UpdateRecordNodeExecutor's private coercion + PATCH
// internals so the SAME logic is reachable WITHOUT a NodeExecutionContext (no
// playbook run, no chat session). Two callers share this core:
//   (a) UpdateRecordNodeExecutor — renders ConfigJson templates against its
//       NodeExecutionContext, THEN hands already-rendered values here.
//   (b) IActionSeam/ActionSeam (Services/Ai/PublicContracts) — supplies typed
//       values directly (no templates).
// The coercion behavior (typed Choice/Boolean/Number, legacy HeuristicParse,
// metadata-driven fail-loud FieldCoercionException) is byte-for-byte identical
// to the pre-031 executor internals — proven by task 030's characterization
// tests passing unmodified.
// ---------------------------------------------------------------------------

/// <summary>
/// A single field mapping whose template value has ALREADY been rendered by the caller.
/// Mirrors <see cref="FieldMappingEntry"/> minus the raw template — the core deals in
/// resolved values, not templates (the session-agnostic boundary).
/// </summary>
internal sealed record RenderedFieldMapping(
    string Field,
    FieldMappingType Type,
    string? RenderedValue,
    IReadOnlyDictionary<string, int>? Options);

/// <summary>A lookup field whose target id has already been rendered by the caller.</summary>
internal sealed record RenderedLookup(
    string Field,
    string TargetEntity,
    string RenderedTargetId);

/// <summary>Session-agnostic input for a record update — all values pre-rendered/typed.</summary>
internal sealed record UpdateRecordActionInput(
    string EntityLogicalName,
    Guid RecordId,
    IReadOnlyList<RenderedFieldMapping>? FieldMappings,
    IReadOnlyDictionary<string, string?>? LegacyFields,
    IReadOnlyList<RenderedLookup>? Lookups);

/// <summary>
/// Session-agnostic core that coerces already-rendered field values to their Dataverse
/// CLR types and PATCHes the record. Constructed inline by the executor (from its own
/// injected fields) and by <c>ActionSeam</c> (from its own injected fields) — never
/// injected into the executor (its constructor is frozen per task 031).
/// </summary>
internal sealed class UpdateRecordActionCore
{
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    public UpdateRecordActionCore(
        IFieldMappingDataverseService fieldMappingService,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        _fieldMappingService = fieldMappingService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Coerces the input's field values and PATCHes the record. Returns the exact set of
    /// payload keys written (used by callers for their result's <c>fieldsUpdated</c>).
    /// </summary>
    /// <exception cref="FieldCoercionException">
    /// Thrown (fail-loud, FR-C1) when a <c>type:"string"</c> mapping targets a
    /// metadata-confirmed Choice column with an unmatchable value. Callers catch this and
    /// surface a validation-shaped failure BEFORE the PATCH is issued.
    /// </exception>
    public async Task<IReadOnlyList<string>> UpdateAsync(
        UpdateRecordActionInput input,
        CancellationToken cancellationToken)
    {
        var updatePayload = new Dictionary<string, object?>();

        if (input.FieldMappings is { Count: > 0 })
        {
            // Metadata-driven coercion (R5 task 030): resolve the target entity's column
            // metadata ONCE per run when at least one mapping declares type:"string" —
            // String-typed mappings targeting a Choice/Boolean/Number column are coerced
            // against the real column type instead of passed through verbatim.
            EntityMetadataDto? entityMetadata = null;
            if (input.FieldMappings.Any(m => m.Type == FieldMappingType.String))
            {
                entityMetadata = await ResolveEntityMetadataAsync(
                    input.EntityLogicalName, cancellationToken).ConfigureAwait(false);
            }

            foreach (var mapping in input.FieldMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Field)) continue;

                var coercedValue = CoerceFieldValue(mapping, entityMetadata, _logger);
                updatePayload[mapping.Field] = coercedValue;
            }
        }
        else if (input.LegacyFields is { Count: > 0 })
        {
            // LEGACY PATH: flat fields dict with heuristic type parsing
            foreach (var (fieldName, renderedValue) in input.LegacyFields)
            {
                updatePayload[fieldName] = HeuristicParse(renderedValue);
            }
        }

        // Handle lookup fields with @odata.bind syntax
        if (input.Lookups is { Count: > 0 })
        {
            foreach (var lookup in input.Lookups)
            {
                if (Guid.TryParse(lookup.RenderedTargetId, out var targetGuid))
                {
                    var entitySetName = GetEntitySetName(lookup.TargetEntity);
                    updatePayload[$"{lookup.Field}@odata.bind"] = $"/{entitySetName}({targetGuid})";
                }
            }
        }

        await _fieldMappingService.UpdateRecordFieldsAsync(
            input.EntityLogicalName,
            input.RecordId,
            updatePayload,
            cancellationToken);

        return updatePayload.Keys.ToArray();
    }

    // ---------------------------------------------------------------------------
    // Typed field mapping coercion (moved verbatim from UpdateRecordNodeExecutor)
    // ---------------------------------------------------------------------------

    private static object? CoerceFieldValue(
        RenderedFieldMapping mapping,
        EntityMetadataDto? entityMetadata,
        ILogger logger)
    {
        var renderedValue = mapping.RenderedValue;
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
                    mapping.Options.Values.Contains(intFallback))
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
                if (int.TryParse(trimmed, out var intVal)) return intVal;
                if (decimal.TryParse(trimmed, out var decVal)) return decVal;
                return renderedValue;

            default:
                // Text/Memo/etc. — verbatim pass-through (existing behavior preserved).
                return renderedValue;
        }
    }

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

        // FAIL LOUD (R5 task 030 / FR-C1) — do NOT return the raw string.
        throw new FieldCoercionException(
            $"Field '{fieldName}': value '{trimmedValue}' is not a valid option. " +
            $"Valid options: {validLabels}.");
    }

    private static object? HeuristicParse(string? value)
    {
        if (value is null) return null;
        if (int.TryParse(value, out var i)) return i;
        if (decimal.TryParse(value, out var d)) return d;
        if (bool.TryParse(value, out var b)) return b;
        return value;
    }

    private static string GetEntitySetName(string entityLogicalName)
    {
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
/// Thrown when a <c>type:"string"</c> mapping's rendered value cannot be resolved against the
/// target column's real Choice metadata. Caught by both <see cref="UpdateRecordNodeExecutor"/>
/// and <c>ActionSeam</c> and surfaced as a <c>NODE_VALIDATION_FAILED</c> / validation-shaped
/// failure — the FAIL LOUD contract (never a silent pass-through, never a downstream Dataverse 500).
/// Moved here from <see cref="UpdateRecordNodeExecutor"/> in task 031 so both callers share it.
/// </summary>
internal sealed class FieldCoercionException : Exception
{
    public FieldCoercionException(string message) : base(message)
    {
    }
}
