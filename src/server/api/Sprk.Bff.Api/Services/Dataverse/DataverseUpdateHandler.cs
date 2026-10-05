using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Handles Dataverse entity updates with optimistic concurrency and retry logic.
/// </summary>
public class DataverseUpdateHandler : IDataverseUpdateHandler
{
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly IGenericEntityService _genericEntityService;
    private readonly CoreAncestorRestamper _restamper;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly Sprk.Bff.Api.Services.Access.SecureRootFilingGate _rootFiling;
    private readonly ILogger<DataverseUpdateHandler> _logger;

    public DataverseUpdateHandler(
        IFieldMappingDataverseService fieldMappingService,
        IGenericEntityService genericEntityService,
        CoreAncestorRestamper restamper,
        IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        ILogger<DataverseUpdateHandler> logger)
    {
        _fieldMappingService = fieldMappingService ?? throw new ArgumentNullException(nameof(fieldMappingService));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="RecordOwnerUnresolvedException">
    /// The update re-files a child record (an <see cref="Microsoft.Xrm.Sdk.EntityReference"/> value onto a parent)
    /// whose new owner cannot be resolved. Nothing was written (task 146).
    /// </exception>
    public async Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct)
    {
        // Task 158 (owner round 6): a work assignment or project filed under a secure matter or project is secured. Whether
        // the record this write files it under is secure must be readable, or nothing is written (fail closed).
        if (await _rootFiling.CheckAsync(entityLogicalName, recordId, fields, ct).ConfigureAwait(false) is { } rootRefusal)
        {
            throw new RecordOwnerUnresolvedException(entityLogicalName, rootRefusal);
        }

        // Task 146: an EntityReference value onto a parent FILES a child table under a record — a reparent. The child's
        // owner is re-derived over every parent it will have (secure-if-any) BEFORE the write, and reassigned when it
        // moves. A root's own lookups never reassign it (provisioning owns a root's ownership).
        //
        // A NULL value may CLEAR a lookup — moving the child OUT of a parent — so every null is passed as a candidate
        // clear (verifier item 8). This handler cannot tell a lookup from a text column; the resolver reads the row and
        // treats a null for a column that holds no parent as no parent change (the write then decides no owner).
        var parentChanges = RecordOwnershipResolver.IsReparentableChild(entityLogicalName)
            ? RecordReparent.ParentChangesWithClearsIn(fields)
            : new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>();
        if (parentChanges.Count == 0)
        {
            await WriteAsync(entityLogicalName, recordId, fields, concurrencyMode, maxRetries, ct);
        }
        else
        {
            var reparent = await _ownership.ReparentAsync(
                new RecordReparent
                {
                    EntityLogicalName = entityLogicalName,
                    RecordId = recordId,
                    ParentChanges = parentChanges,
                },
                token => WriteAsync(entityLogicalName, recordId, fields, concurrencyMode, maxRetries, token),
                ct);
            if (reparent.IsRefused)
            {
                throw new RecordOwnerUnresolvedException(entityLogicalName, reparent);
            }
        }

        // Task 156 (owner round 4 item 5, option b): this generic update can write what a to-do / event / communication /
        // analysis is filed under, or the matter / project of a record others are filed under — so the affected copies
        // are re-stamped in the same operation, after whichever path wrote it (the plain write, or the re-file once its
        // owner is settled; batch 4 integration). A write that cannot move a stamp reads nothing. Never thrown: a child
        // that fails is logged and the reconciliation job repairs it; this record's own update stands.
        await _restamper.AfterWriteAsync(entityLogicalName, recordId, fields.Keys, CancellationToken.None)
            .ConfigureAwait(false);
        // Task 158: a work assignment or project this write filed under a secure record is secured now (never thrown; an
        // incomplete securing is logged and the secure-root inheritance job completes it).
        await _rootFiling.SecureAfterWriteAsync(entityLogicalName, recordId, fields.Keys, traceId: null);
    }

    private async Task WriteAsync(
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
            await _fieldMappingService.UpdateRecordFieldsAsync(
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
                var currentRecord = await _genericEntityService.RetrieveAsync(
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
                await _fieldMappingService.UpdateRecordFieldsAsync(
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
