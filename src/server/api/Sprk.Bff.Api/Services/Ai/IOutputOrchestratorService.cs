using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates Dataverse updates based on playbook outputMapping configuration.
/// Reads outputMapping from playbook, resolves variable references, and applies updates.
/// </summary>
/// <remarks>
/// This service enables business analysts to configure field mappings via Playbook Builder
/// without code deployment. It processes the outputMapping section of playbooks and
/// delegates actual updates to IDataverseUpdateHandler.
/// </remarks>
public interface IOutputOrchestratorService
{
    /// <summary>
    /// Apply outputMapping from playbook to Dataverse entities.
    /// </summary>
    /// <param name="playbookId">Playbook containing outputMapping configuration</param>
    /// <param name="context">Execution context with tool output variables</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result containing success/failure and updated record IDs</returns>
    Task<OutputMappingResult> ApplyOutputMappingAsync(
        Guid playbookId,
        PlaybookExecutionContext context,
        CancellationToken ct);
}

/// <summary>
/// Execution context for playbook runs. Stores variables from job payload and tool outputs.
/// </summary>
/// <remarks>
/// Variables use dot notation for namespacing:
/// - context.* = Job payload variables (invoiceId, documentId, matterId)
/// - extraction.* = Output from InvoiceExtractionToolHandler (TL-009)
/// - calculation.* = Output from FinancialCalculationToolHandler (TL-011)
/// </remarks>
public class PlaybookExecutionContext
{
    /// <summary>
    /// Variable storage. Keys use dot notation (e.g., "context.invoiceId", "extraction.aiSummary").
    /// </summary>
    public Dictionary<string, object?> Variables { get; init; } = new();

    /// <summary>
    /// Get variable value with type conversion.
    /// </summary>
    /// <typeparam name="T">Target type for conversion</typeparam>
    /// <param name="key">Variable key in dot notation</param>
    /// <returns>Converted value or default(T) if not found</returns>
    public T? GetVariable<T>(string key)
    {
        if (Variables.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }

            try
            {
                return (T?)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    /// <summary>
    /// Set variable value.
    /// </summary>
    /// <param name="key">Variable key in dot notation</param>
    /// <param name="value">Value to store</param>
    public void SetVariable(string key, object? value)
    {
        Variables[key] = value;
    }

    /// <summary>
    /// Set variables from a tool result object.
    /// Flattens properties into dot notation (e.g., extraction.aiSummary).
    /// </summary>
    /// <param name="prefix">Variable prefix (e.g., "extraction")</param>
    /// <param name="resultObject">Result object from tool handler</param>
    public void SetVariablesFromObject(string prefix, object resultObject)
    {
        if (resultObject == null)
        {
            return;
        }

        // Use JSON serialization to flatten object properties
        var json = JsonSerializer.Serialize(resultObject);
        using var doc = JsonDocument.Parse(json);

        FlattenJsonElement(prefix, doc.RootElement);
    }

    private void FlattenJsonElement(string prefix, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = $"{prefix}.{property.Name}";
                    FlattenJsonElement(key, property.Value);
                }
                break;

            case JsonValueKind.Array:
                // Store array as-is
                Variables[prefix] = element;
                break;

            case JsonValueKind.String:
                Variables[prefix] = element.GetString();
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intValue))
                {
                    Variables[prefix] = intValue;
                }
                else if (element.TryGetDecimal(out var decimalValue))
                {
                    Variables[prefix] = decimalValue;
                }
                else
                {
                    Variables[prefix] = element.GetDouble();
                }
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                Variables[prefix] = element.GetBoolean();
                break;

            case JsonValueKind.Null:
                Variables[prefix] = null;
                break;
        }
    }
}

/// <summary>
/// Result from applying output mappings.
/// </summary>
public record OutputMappingResult
{
    /// <summary>
    /// Whether all updates succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Individual entity update results.
    /// </summary>
    public List<EntityUpdateResult> Updates { get; init; } = new();

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Create a success result with update details.
    /// </summary>
    public static OutputMappingResult SuccessResult(List<EntityUpdateResult> updates) =>
        new() { Success = true, Updates = updates };

    /// <summary>
    /// Create a failure result with error message.
    /// </summary>
    public static OutputMappingResult FailureResult(string error) =>
        new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Result from updating a single entity.
/// </summary>
public record EntityUpdateResult
{
    /// <summary>
    /// Entity logical name (e.g., "sprk_invoice").
    /// </summary>
    public string EntityType { get; init; } = null!;

    /// <summary>
    /// Record ID that was updated.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Whether the update succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of fields updated.
    /// </summary>
    public int FieldsUpdated { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
