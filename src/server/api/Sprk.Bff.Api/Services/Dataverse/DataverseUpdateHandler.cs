using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Handles Dataverse entity updates with optimistic concurrency and retry logic.
/// </summary>
public class DataverseUpdateHandler : IDataverseUpdateHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseUpdateHandler> _logger;

    public DataverseUpdateHandler(
        IDataverseService dataverseService,
        ILogger<DataverseUpdateHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct)
    {
        if (concurrencyMode == ConcurrencyMode.Optimistic)
        {
            await UpdateWithOptimisticConcurrencyAsync(
                entityLogicalName, recordId, fields, maxRetries, ct);
        }
        else
        {
            // Simple update - last write wins
            await _dataverseService.UpdateRecordFieldsAsync(
                entityLogicalName, recordId, fields, ct);

            _logger.LogInformation(
                "Updated {EntityType} {RecordId} with {FieldCount} fields (no concurrency control)",
                entityLogicalName, recordId, fields.Count);
        }
    }

    /// <summary>
    /// Update record with optimistic concurrency control.
    /// Reads current row version, includes it in update, retries on conflict.
    /// </summary>
    private async Task UpdateWithOptimisticConcurrencyAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        int maxRetries,
        CancellationToken ct)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;

                _logger.LogDebug(
                    "Optimistic concurrency update attempt {Attempt}/{MaxRetries} for {EntityType} {RecordId}",
                    attempt, maxRetries, entityLogicalName, recordId);

                // 1. Read current record to get row version
                var currentRecord = await _dataverseService.RetrieveAsync(
                    entityLogicalName, recordId, new[] { "versionnumber" }, ct);

                if (currentRecord == null)
                {
                    throw new InvalidOperationException(
                        $"Record {entityLogicalName} {recordId} not found for optimistic concurrency update");
                }

                var currentVersion = currentRecord.GetAttributeValue<long>("versionnumber");

                _logger.LogDebug(
                    "Current version for {EntityType} {RecordId} is {Version}",
                    entityLogicalName, recordId, currentVersion);

                // 2. Add row version to update request
                var fieldsWithVersion = new Dictionary<string, object?>(fields)
                {
                    ["versionnumber"] = currentVersion
                };

                // 3. Update with version check
                await _dataverseService.UpdateRecordFieldsAsync(
                    entityLogicalName, recordId, fieldsWithVersion, ct);

                _logger.LogInformation(
                    "Updated {EntityType} {RecordId} with optimistic concurrency (version {Version}, {FieldCount} fields)",
                    entityLogicalName, recordId, currentVersion, fields.Count);

                return; // Success - exit retry loop
            }
            catch (Exception ex) when (IsConcurrencyException(ex))
            {
                lastException = ex;

                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        ex,
                        "Optimistic concurrency failed after {MaxRetries} attempts for {EntityType} {RecordId}",
                        maxRetries, entityLogicalName, recordId);
                    throw new InvalidOperationException(
                        $"Optimistic concurrency update failed after {maxRetries} attempts for {entityLogicalName} {recordId}",
                        ex);
                }

                _logger.LogWarning(
                    "Concurrency conflict on {EntityType} {RecordId}, retrying (attempt {Attempt}/{MaxRetries})",
                    entityLogicalName, recordId, attempt, maxRetries);

                // Exponential backoff: 100ms, 200ms, 400ms, 800ms...
                var delayMs = (int)Math.Pow(2, attempt - 1) * 100;
                await Task.Delay(delayMs, ct);
            }
        }

        // Should not reach here due to throw inside loop, but handle defensively
        throw new InvalidOperationException(
            $"Optimistic concurrency update failed for {entityLogicalName} {recordId}",
            lastException);
    }

    /// <summary>
    /// Check if an exception is a Dataverse concurrency error.
    /// </summary>
    private static bool IsConcurrencyException(Exception ex)
    {
        // Dataverse concurrency error codes:
        // - 0x80060882: ConcurrencyVersionMismatch
        // - 0x80060883: ConcurrencyVersionNotProvided
        var message = ex.Message;

        return message.Contains("0x80060882", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("0x80060883", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ConcurrencyVersionMismatch", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ConcurrencyVersionNotProvided", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("OptimisticConcurrency", StringComparison.OrdinalIgnoreCase);
    }
}
