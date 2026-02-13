using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates Dataverse updates based on playbook outputMapping configuration.
/// </summary>
/// <remarks>
/// This service reads the outputMapping section from a playbook, resolves variable
/// references (${context.invoiceId}), performs type conversions (Money, EntityReference),
/// and delegates actual Dataverse updates to IDataverseUpdateHandler.
/// </remarks>
public class OutputOrchestratorService : IOutputOrchestratorService
{
    private readonly IPlaybookService _playbookService;
    private readonly IDataverseUpdateHandler _dataverseUpdateHandler;
    private readonly ILogger<OutputOrchestratorService> _logger;

    public OutputOrchestratorService(
        IPlaybookService playbookService,
        IDataverseUpdateHandler dataverseUpdateHandler,
        ILogger<OutputOrchestratorService> logger)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _dataverseUpdateHandler = dataverseUpdateHandler ?? throw new ArgumentNullException(nameof(dataverseUpdateHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OutputMappingResult> ApplyOutputMappingAsync(
        Guid playbookId,
        PlaybookExecutionContext context,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Applying output mapping for playbook {PlaybookId}", playbookId);

            // 1. Load playbook
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, ct);
            if (playbook == null)
            {
                var error = $"Playbook {playbookId} not found";
                _logger.LogError(error);
                return OutputMappingResult.FailureResult(error);
            }

            // 2. Parse outputMapping from playbook configuration
            var outputMapping = ParseOutputMapping(playbook);
            if (outputMapping == null || outputMapping.Updates.Count == 0)
            {
                _logger.LogWarning("Playbook {PlaybookId} has no outputMapping defined, skipping updates", playbookId);
                return OutputMappingResult.SuccessResult(new List<EntityUpdateResult>());
            }

            _logger.LogInformation(
                "Playbook {PlaybookId} has {UpdateCount} entity updates in outputMapping",
                playbookId, outputMapping.Updates.Count);

            // 3. Apply each entity update
            var results = new List<EntityUpdateResult>();
            foreach (var update in outputMapping.Updates)
            {
                var result = await ApplyEntityUpdateAsync(update, context, ct);
                results.Add(result);
            }

            // 4. Check if all succeeded
            var allSucceeded = results.All(r => r.Success);
            if (!allSucceeded)
            {
                var failedCount = results.Count(r => !r.Success);
                var error = $"{failedCount} of {results.Count} updates failed";
                _logger.LogError("Output mapping partially failed: {Error}", error);
                return OutputMappingResult.FailureResult(error);
            }

            _logger.LogInformation(
                "Successfully applied output mapping for playbook {PlaybookId} - {UpdateCount} entities updated",
                playbookId, results.Count);

            return OutputMappingResult.SuccessResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply output mapping for playbook {PlaybookId}", playbookId);
            return OutputMappingResult.FailureResult(ex.Message);
        }
    }

    /// <summary>
    /// Apply a single entity update configuration.
    /// </summary>
    private async Task<EntityUpdateResult> ApplyEntityUpdateAsync(
        EntityUpdateConfig update,
        PlaybookExecutionContext context,
        CancellationToken ct)
    {
        try
        {
            // 1. Resolve recordId from variable reference
            var recordIdStr = ResolveVariable(update.RecordIdSource, context);
            if (!Guid.TryParse(recordIdStr, out var recordId) || recordId == Guid.Empty)
            {
                var error = $"Invalid or empty recordId resolved from '{update.RecordIdSource}': {recordIdStr}";
                _logger.LogError(error);
                return new EntityUpdateResult
                {
                    EntityType = update.EntityType,
                    Success = false,
                    ErrorMessage = error
                };
            }

            _logger.LogDebug(
                "Resolved recordId {RecordId} for entity {EntityType} from '{RecordIdSource}'",
                recordId, update.EntityType, update.RecordIdSource);

            // 2. Build field dictionary with resolved values
            var fields = new Dictionary<string, object?>();
            foreach (var fieldMapping in update.Fields)
            {
                var value = ResolveFieldValue(fieldMapping.Value, context);
                fields[fieldMapping.Key] = value;

                _logger.LogDebug(
                    "Mapped field {FieldName} = {Value} (type: {Type})",
                    fieldMapping.Key,
                    value?.ToString() ?? "null",
                    value?.GetType().Name ?? "null");
            }

            // 3. Delegate to DataverseUpdateHandler
            await _dataverseUpdateHandler.UpdateAsync(
                update.EntityType,
                recordId,
                fields,
                update.ConcurrencyMode ?? ConcurrencyMode.None,
                update.MaxRetries ?? 3,
                ct);

            _logger.LogInformation(
                "Updated {EntityType} {RecordId} with {FieldCount} fields",
                update.EntityType, recordId, fields.Count);

            return new EntityUpdateResult
            {
                EntityType = update.EntityType,
                RecordId = recordId,
                Success = true,
                FieldsUpdated = fields.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update {EntityType}: {Error}", update.EntityType, ex.Message);
            return new EntityUpdateResult
            {
                EntityType = update.EntityType,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Resolve a variable reference to its value.
    /// </summary>
    /// <param name="expression">Expression to resolve (e.g., "${context.invoiceId}" or "100000001")</param>
    /// <param name="context">Execution context containing variables</param>
    /// <returns>Resolved value as string, or the expression itself if not a variable reference</returns>
    private string? ResolveVariable(string expression, PlaybookExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        // Check if it's a variable reference (${variableName})
        if (expression.StartsWith("${") && expression.EndsWith("}"))
        {
            var varName = expression[2..^1]; // Remove ${ and }
            var value = context.GetVariable<object>(varName);
            return value?.ToString();
        }

        // Not a variable reference - return as-is (constant value)
        return expression;
    }

    /// <summary>
    /// Resolve a field value specification to an actual value.
    /// Handles simple values (strings, numbers) and complex types (Money, EntityReference).
    /// </summary>
    private object? ResolveFieldValue(object valueSpec, PlaybookExecutionContext context)
    {
        // Handle null
        if (valueSpec == null)
        {
            return null;
        }

        // Handle simple string value (variable reference or constant)
        if (valueSpec is string strValue)
        {
            return ResolveVariable(strValue, context);
        }

        // Handle numeric constant
        if (valueSpec is int or long or decimal or double or float)
        {
            return valueSpec;
        }

        // Handle boolean constant
        if (valueSpec is bool)
        {
            return valueSpec;
        }

        // Handle complex type specification (Money, EntityReference, etc.)
        if (valueSpec is JsonElement jsonElement)
        {
            return ResolveComplexValue(jsonElement, context);
        }

        // Unknown type - log warning and return as-is
        _logger.LogWarning("Unknown value type: {Type}, returning as-is", valueSpec.GetType().Name);
        return valueSpec;
    }

    /// <summary>
    /// Resolve a complex value specification (Money, EntityReference, DateTime).
    /// </summary>
    private object? ResolveComplexValue(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        // Check if it has a "type" property indicating a complex type
        if (jsonElement.TryGetProperty("type", out var typeProperty))
        {
            var type = typeProperty.GetString();
            return type?.ToLowerInvariant() switch
            {
                "money" => ResolveMoney(jsonElement, context),
                "entityreference" => ResolveEntityReference(jsonElement, context),
                "datetime" => ResolveDateTime(jsonElement, context),
                _ => jsonElement.GetRawText()
            };
        }

        // No type property - return as raw JSON string
        return jsonElement.GetRawText();
    }

    /// <summary>
    /// Resolve a Money type specification.
    /// Format: { "type": "Money", "value": "${extraction.totalAmount}", "currency": "USD" }
    /// </summary>
    private Money ResolveMoney(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var valueStr = jsonElement.GetProperty("value").GetString() ?? "0";
        var resolvedValue = ResolveVariable(valueStr, context);

        if (!decimal.TryParse(resolvedValue, out var amount))
        {
            _logger.LogWarning("Invalid Money value '{Value}', defaulting to 0", resolvedValue);
            amount = 0;
        }

        return new Money(amount);
    }

    /// <summary>
    /// Resolve an EntityReference type specification.
    /// Format: { "type": "EntityReference", "entityType": "sprk_matter", "id": "${context.matterId}" }
    /// </summary>
    private EntityReference ResolveEntityReference(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var entityType = jsonElement.GetProperty("entityType").GetString() ?? "";
        var idStr = jsonElement.GetProperty("id").GetString() ?? "";
        var resolvedId = ResolveVariable(idStr, context);

        if (!Guid.TryParse(resolvedId, out var id) || id == Guid.Empty)
        {
            _logger.LogWarning("Invalid EntityReference ID '{Id}', using Empty GUID", resolvedId);
            id = Guid.Empty;
        }

        return new EntityReference(entityType, id);
    }

    /// <summary>
    /// Resolve a DateTime type specification.
    /// Format: { "type": "DateTime", "value": "${extraction.invoiceDate}" }
    /// </summary>
    private DateTime ResolveDateTime(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var valueStr = jsonElement.GetProperty("value").GetString() ?? "";
        var resolvedValue = ResolveVariable(valueStr, context);

        if (DateTime.TryParse(resolvedValue, out var dateTime))
        {
            return dateTime;
        }

        _logger.LogWarning("Invalid DateTime value '{Value}', using current UTC time", resolvedValue);
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Parse outputMapping from playbook response.
    /// </summary>
    /// <remarks>
    /// For MVP, we expect outputMapping to be stored as a JSON string in a dedicated field.
    /// Future: Could be part of playbook.ConfigJson or a separate sprk_outputmapping entity.
    /// </remarks>
    private OutputMappingConfig? ParseOutputMapping(PlaybookResponse playbook)
    {
        // TODO: Determine where outputMapping is stored in playbook entity
        // Options:
        // 1. Dedicated field: playbook.OutputMappingJson
        // 2. Part of ConfigJson: playbook.ConfigJson.outputMapping
        // 3. Separate entity: sprk_playbook_outputmapping relationship

        // For MVP stub, return null (will be implemented once schema is finalized)
        // In production, parse from JSON and deserialize to OutputMappingConfig

        _logger.LogWarning(
            "ParseOutputMapping not implemented - outputMapping storage location TBD. Playbook: {PlaybookId}",
            playbook.Id);

        return null;
    }
}

/// <summary>
/// Configuration model for outputMapping section of playbook.
/// </summary>
public record OutputMappingConfig
{
    /// <summary>
    /// List of entity updates to apply.
    /// </summary>
    public List<EntityUpdateConfig> Updates { get; init; } = new();
}

/// <summary>
/// Configuration for updating a single entity.
/// </summary>
public record EntityUpdateConfig
{
    /// <summary>
    /// Entity logical name (e.g., "sprk_invoice", "sprk_matter").
    /// </summary>
    public string EntityType { get; init; } = null!;

    /// <summary>
    /// Variable reference for record ID (e.g., "${context.invoiceId}").
    /// </summary>
    public string RecordIdSource { get; init; } = null!;

    /// <summary>
    /// Field mappings: field name → value specification.
    /// Value can be a string (variable reference or constant), number, or complex object.
    /// </summary>
    public Dictionary<string, object> Fields { get; init; } = new();

    /// <summary>
    /// Concurrency mode for this update (None or Optimistic).
    /// </summary>
    public ConcurrencyMode? ConcurrencyMode { get; init; }

    /// <summary>
    /// Whether to retry on concurrency conflicts.
    /// </summary>
    public bool RetryOnConflict { get; init; }

    /// <summary>
    /// Maximum number of retries for optimistic concurrency conflicts.
    /// </summary>
    public int? MaxRetries { get; init; }
}

/// <summary>
/// Concurrency mode for Dataverse updates.
/// </summary>
public enum ConcurrencyMode
{
    /// <summary>
    /// No concurrency checking. Last write wins.
    /// </summary>
    None,

    /// <summary>
    /// Optimistic concurrency using row version. Fails if record changed since read.
    /// </summary>
    Optimistic
}
