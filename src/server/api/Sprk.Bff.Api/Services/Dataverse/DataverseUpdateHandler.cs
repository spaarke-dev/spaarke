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
    private readonly Sprk.Bff.Api.Services.Access.SecureChildReconciler _children;

    /// <param name="children">#2091: the ONE step after a re-file (<see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync"/>).
    /// REQUIRED: without it a re-file into a Secure record would silently leave the row business-unit readable. Scoped, like
    /// this handler (ExternalAccessModule registers it unconditionally). It also lets a re-filed communication's thread follow
    /// the filing (spaarkeai-word-add-in-r1 task 129), which this handler used to ask the thread resolver for itself.</param>
    public DataverseUpdateHandler(
        IFieldMappingDataverseService fieldMappingService,
        IGenericEntityService genericEntityService,
        CoreAncestorRestamper restamper,
        IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        ILogger<DataverseUpdateHandler> logger,
        Sprk.Bff.Api.Services.Access.SecureChildReconciler children)
    {
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _fieldMappingService = fieldMappingService ?? throw new ArgumentNullException(nameof(fieldMappingService));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="RecordOwnerUnresolvedException">
    /// The update re-files a child record (an <see cref="Microsoft.Xrm.Sdk.EntityReference"/> value or an <c>@odata.bind</c>
    /// onto a parent) whose new owner cannot be resolved, or binds a filing lookup to something that names no row of its
    /// target. Nothing was written (task 146; #2091 round 3).
    /// </exception>
    public async Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct)
    {
        // unified-access-control-r2 task 189 fix round C (K5): this handler writes a playbook's output mapping app-only (the
        // output orchestrator), so it never writes a document's Access Permission or its own-narrowing record; only the
        // document rule's writers do. Refused before anything is read or written. Task 188 round 4e: nor its D7 link columns
        // (sprk_parentdocument, sprk_relatedcommunication), set, cleared or bound (InheritedAccessPermission.DocumentLinkColumns).
        // #2251: nor any row of a table whose rows are access (grants, membership, ethical walls; AccessTables), nor a secure
        // root's secure flag, access columns or container (SecureRootColumns). Asked before the secure-root filing gate.
        if (InheritedAccessPermission.RefuseAppOnlyWrite(entityLogicalName, fields.Keys) is { } documentRefusal)
        {
            throw new RecordOwnerUnresolvedException(entityLogicalName, documentRefusal);
        }

        // ebilling-r1 DEF-022 (#2056): an invoice or invoice line names exactly one core record, a line regards its invoice's,
        // and an invoice re-file keeps its lines (D-96). Decided on the stored lookups overlaid with this write, before it.
        if (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(entityLogicalName, recordId, fields, _genericEntityService, ct)
                .ConfigureAwait(false) is { RefusalCode: { } familyCode })
        {
            // Code only: the guard read as the app, so its reason can name records the caller has not read.
            throw InvoiceFamilyCoreRecordGuard.RefusalException(entityLogicalName, familyCode);
        }

        // Task 158 (owner round 6): a work assignment or project filed under a secure matter or project is secured. Whether
        // the record this write files it under is secure must be readable, or nothing is written (fail closed).
        if (await _rootFiling.CheckAsync(entityLogicalName, recordId, fields, ct).ConfigureAwait(false) is { } rootRefusal)
        {
            throw new RecordOwnerUnresolvedException(entityLogicalName, rootRefusal);
        }

        // Task 146: an EntityReference value or an @odata.bind onto a parent FILES a child table under a record — a reparent.
        // The child's owner is re-derived over every parent it will have (secure-if-any) BEFORE the write, and reassigned when
        // it moves. A root's own lookups never reassign it (provisioning owns a root's ownership). A filing lookup bound to
        // something that names no row of its target is refused here, before anything is read or written (#2091 round 3).
        //
        // A NULL value may CLEAR a lookup — moving the child OUT of a parent — so every null is passed as a candidate
        // clear (verifier item 8). This handler cannot tell a lookup from a text column; the resolver reads the row and
        // treats a null for a column that holds no parent as no parent change (the write then decides no owner).
        // #2110 (ontology K3): with the lineage lookups it sets that are no ownership parent (a message's thread), so the reparent
        // can tell whether it moved one (ParentsUnchanged); but for a message's thread (#2143 round 7c, a re-file on its own)
        // they never make a write a reparent on their own.
        var parentChanges = RecordOwnershipResolver.IsReparentableChild(entityLogicalName)
            ? RecordReparent.WithLineageLookupsIn(
                entityLogicalName, RecordReparent.ParentChangesWithClearsIn(entityLogicalName, fields), fields)
            : new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>();

        // #2091: a write that may re-file the row (the ONE predicate, RecordReparent.MayRefile) runs the ONE step after a
        // re-file (SecureChildReconciler.AfterRefileAsync), as every other re-file writer does. Whether the row was isolated
        // is read BEFORE the write (once more after a fault — a timeout included — then handed to the step as a fault): a move
        // OUT releases only from a row that was. Only the caller's own cancellation ends the write here.
        var refiledTable = entityLogicalName.Trim().ToLowerInvariant();
        var mayRefile = RecordReparent.MayRefile(refiledTable, parentChanges);
        var wasIsolated = mayRefile
            ? await Sprk.Bff.Api.Services.Access.RefileIsolationRead.BeforeWriteAsync(_children, refiledTable, recordId, ct)
                .ConfigureAwait(false)
            : null;

        // #2091 round 4: the reparent reads the row; a change that moves none of its parents (a rebind onto the parent it
        // holds) is written and is no re-file, so the step does not run.
        var refiled = false;
        RecordOwnerResolution? refiledBy = null;
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

            refiled = !reparent.ParentsUnchanged;
            refiledBy = reparent;
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

        // #2091, AFTER the re-stamp (the stamps on the rows filed under it are lookups the ownership rule reads): the re-filed
        // row's mirror and the pass over everything filed under it — and a communication's thread follows the filing (task
        // 129). Never throws; the write stands.
        if (refiled && wasIsolated is not null)
        {
            await _children.AfterRefileAsync(refiledTable, recordId, wasIsolated, refiledBy, CancellationToken.None).ConfigureAwait(false);
        }
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
            await PatchAsync(entityLogicalName, recordId, fields, ct);

            _logger.LogInformation(
                "Updated {EntityType} {RecordId} with {FieldCount} fields (no concurrency control)",
                entityLogicalName, recordId, fields.Count);
        }
    }

    /// <summary>
    /// The write. An invoice or line is written UPDATE-ONLY (If-Match: *): the guard at the top of <see cref="UpdateAsync"/>
    /// checked that the row exists, and a row deleted since must not be re-created by the upsert (ebilling-r1 #2081). It is
    /// refused like the guard, nothing created. This handler writes app-only, so there is no user whose access it hides.
    /// </summary>
    private async Task PatchAsync(string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, CancellationToken ct)
    {
        if (InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget(entityLogicalName))
        {
            if (await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                    _fieldMappingService, entityLogicalName, recordId, fields, impersonateSystemUserId: null, notFoundAsReadFailed: false, ct) is { } gone)
            {
                throw InvoiceFamilyCoreRecordGuard.RefusalException(entityLogicalName, gone);
            }

            return;
        }

        await _fieldMappingService.UpdateRecordFieldsAsync(entityLogicalName, recordId, fields, ct);
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
                await PatchAsync(entityLogicalName, recordId, fieldsWithVersion, ct);

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
