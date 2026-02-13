using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Generic tool handler for updating Dataverse entity records from playbooks.
/// Allows playbooks to update arbitrary entity fields without custom handlers.
/// </summary>
public class DataverseUpdateToolHandler : IAiToolHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseUpdateToolHandler> _logger;

    public const string ToolNameConst = "DataverseUpdate";
    public string ToolName => ToolNameConst;

    public DataverseUpdateToolHandler(
        IDataverseService dataverseService,
        ILogger<DataverseUpdateToolHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates a Dataverse entity record with specified fields.
    /// Expected parameters:
    /// - entity: string (logical entity name, e.g., "sprk_invoice")
    /// - recordId: Guid (ID of record to update)
    /// - fields: Dictionary&lt;string, object&gt; (field names and values to update)
    /// </summary>
    public async Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        try
        {
            var entityName = parameters.GetString("entity");
            var recordId = parameters.GetGuid("recordId");
            var fields = parameters.GetDictionary("fields");

            if (string.IsNullOrWhiteSpace(entityName))
            {
                return PlaybookToolResult.CreateError("Entity name is required");
            }

            if (recordId == Guid.Empty)
            {
                return PlaybookToolResult.CreateError("RecordId cannot be empty");
            }

            if (fields == null || fields.Count == 0)
            {
                return PlaybookToolResult.CreateError("Fields dictionary is required and must contain at least one field");
            }

            _logger.LogInformation(
                "Updating {EntityName} record {RecordId} with {FieldCount} field(s)",
                entityName, recordId, fields.Count);

            // Convert field values to proper Dataverse types
            var convertedFields = ConvertFieldValues(fields);
            var fieldsDict = new Dictionary<string, object?>(convertedFields);

            await _dataverseService.UpdateRecordFieldsAsync(entityName, recordId, fieldsDict, ct);

            _logger.LogInformation(
                "Successfully updated {EntityName} record {RecordId}",
                entityName, recordId);

            return PlaybookToolResult.CreateSuccess(new
            {
                Entity = entityName,
                RecordId = recordId,
                UpdatedFields = fields.Keys.ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Dataverse record");
            return PlaybookToolResult.CreateError($"Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts playbook field values to proper Dataverse types.
    /// Handles Money, EntityReference, and other Dataverse-specific types.
    /// </summary>
    private Dictionary<string, object?> ConvertFieldValues(Dictionary<string, object> fields)
    {
        var converted = new Dictionary<string, object?>();

        foreach (var (key, value) in fields)
        {
            if (value == null)
            {
                converted[key] = (object?)null;
                continue;
            }

            // Handle decimal/currency fields
            if (value is decimal decimalValue)
            {
                converted[key] = new Money { Value = decimalValue };
                continue;
            }

            // Handle int/double that should be Money
            if ((value is int || value is double || value is long) && IsMoneyField(key))
            {
                converted[key] = new Money { Value = Convert.ToDecimal(value) };
                continue;
            }

            // Handle lookup fields (EntityReference)
            if (value is Dictionary<string, object> dict &&
                dict.ContainsKey("logicalName") &&
                dict.ContainsKey("id"))
            {
                var logicalName = dict["logicalName"]?.ToString();
                var id = Guid.Parse(dict["id"]?.ToString() ?? string.Empty);
                converted[key] = new EntityReference(logicalName, id);
                continue;
            }

            // Pass through as-is for other types
            converted[key] = value;
        }

        return converted;
    }

    /// <summary>
    /// Heuristic to determine if a field should be treated as Money type.
    /// Checks for common currency field naming patterns.
    /// </summary>
    private static bool IsMoneyField(string fieldName)
    {
        var lowerName = fieldName.ToLowerInvariant();
        return lowerName.Contains("amount") ||
               lowerName.Contains("budget") ||
               lowerName.Contains("price") ||
               lowerName.Contains("cost") ||
               lowerName.Contains("total");
    }
}
