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
    private readonly Sprk.Bff.Api.Services.Access.IDataverseRecordShareService? _shares;
    private readonly Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? _readAsPerson;

    /// <param name="children">#2091: the ONE step after a re-file (<see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync"/>).
    /// REQUIRED: without it a re-file into a Secure record would silently leave the row business-unit readable. Scoped, like
    /// this handler (ExternalAccessModule registers it unconditionally). It also lets a re-filed communication's thread follow
    /// the filing (spaarkeai-word-add-in-r1 task 129), which this handler used to ask the thread resolver for itself.</param>
    /// <param name="shares">#2257: the record-access seam a write made AS a person asks for that person's rights when it moves a
    /// row out of a secure root (<see cref="Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForImpersonatedUser"/>), as the
    /// Update Record core does. Optional: without it that question faults, and such a move is refused (fail closed).</param>
    /// <param name="readAsPerson">#2257 fix round 1 (K3): the existing impersonated OData read (<c>MSCRMCallerID</c>; registered
    /// unconditionally by CommunicationModule), through which an Optimistic update made AS a person reads the row's
    /// <c>versionnumber</c> AS that person, so a row they cannot read looks the same as a missing one. Optional: without it
    /// such an update writes nothing (fail closed); it never reads app-only for a person.</param>
    public DataverseUpdateHandler(
        IFieldMappingDataverseService fieldMappingService,
        IGenericEntityService genericEntityService,
        CoreAncestorRestamper restamper,
        IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        ILogger<DataverseUpdateHandler> logger,
        Sprk.Bff.Api.Services.Access.SecureChildReconciler children,
        Sprk.Bff.Api.Services.Access.IDataverseRecordShareService? shares = null,
        Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? readAsPerson = null)
    {
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _shares = shares;
        _readAsPerson = readAsPerson;
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
        CancellationToken ct,
        Guid? impersonateSystemUserId = null)
    {
        // unified-access-control-r2 task 189 fix round C (K5): this handler writes a playbook's output mapping (the output
        // orchestrator; app-only for a System run, as the runner for a person's run since #2257), so it never writes a document's Access Permission or its own-narrowing record; only the
        // document rule's writers do. Refused before anything is read or written. Task 188 round 4e: nor its D7 link columns
        // (sprk_parentdocument, sprk_relatedcommunication), set, cleared or bound (InheritedAccessPermission.DocumentLinkColumns).
        // #2251: nor any row of a table whose rows are access (grants, membership, ethical walls; AccessTables), nor a secure
        // root's secure flag, access columns or container (SecureRootColumns). Asked before the secure-root filing gate.
        // #2257 fix round 1 (main-session decision): asked whatever identity the write runs as. A person's run now writes AS
        // that person, but the mapping is the playbook's configuration (its author, who may not be the runner), never theirs.
        // Master merge after #2253: so it is asked as WriteIdentity.AppOnly (every set, AccessColumns included) even when the
        // write impersonates the runner; WriteIdentity.Caller is the field-mapping push's only.
        if (InheritedAccessPermission.RefuseAppOnlyWrite(
                entityLogicalName, fields.Keys, InheritedAccessPermission.WriteIdentity.AppOnly) is { } documentRefusal)
        {
            throw new RecordOwnerUnresolvedException(entityLogicalName, documentRefusal);
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
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller = null;
        if (parentChanges.Count == 0)
        {
            await WriteAsync(entityLogicalName, recordId, fields, concurrencyMode, maxRetries, ct, impersonateSystemUserId);
        }
        else
        {
            var reparent = await _ownership.ReparentAsync(
                new RecordReparent
                {
                    EntityLogicalName = entityLogicalName,
                    RecordId = recordId,
                    ParentChanges = parentChanges,
                    // #2257: as the Update Record core — a move out of a secure root (F3) is asked of the person this write
                    // runs AS; an app-only write passes no caller and is refused such a move.
                    CallerSystemUserId = impersonateSystemUserId,
                    SecureExitCaller = secureExitCaller = impersonateSystemUserId is { } person
                        ? Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForImpersonatedUser(person, _shares)
                        : null,
                },
                token => WriteAsync(entityLogicalName, recordId, fields, concurrencyMode, maxRetries, token, impersonateSystemUserId),
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
            await _children.AfterRefileAsync(
                refiledTable, recordId, wasIsolated, Sprk.Bff.Api.Services.Access.RefileThreadStep.Reconcile,
                secureExitCaller, refiledBy, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct,
        Guid? impersonateSystemUserId)
    {
        if (concurrencyMode == ConcurrencyMode.Optimistic)
        {
            await UpdateWithOptimisticConcurrencyAsync(
                entityLogicalName, recordId, fields, maxRetries, ct, impersonateSystemUserId);
        }
        else
        {
            // Simple update - last write wins
            await _fieldMappingService.UpdateRecordFieldsAsync(
                entityLogicalName, recordId, fields, ct, impersonateSystemUserId);

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
        CancellationToken ct,
        Guid? impersonateSystemUserId)
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

                // 1. Read current record to get row version, AS the person a person's write runs as (#2257 fix round 1, K3)
                var readVersion = impersonateSystemUserId is { } person
                    ? await ReadVersionAsPersonAsync(entityLogicalName, recordId, person, ct)
                    : (await _genericEntityService.RetrieveAsync(
                        entityLogicalName, recordId, new[] { "versionnumber" }, ct))?.GetAttributeValue<long>("versionnumber");

                if (readVersion is not { } currentVersion)
                {
                    throw new InvalidOperationException(
                        $"Record {entityLogicalName} {recordId} not found for optimistic concurrency update");
                }

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
                    entityLogicalName, recordId, fieldsWithVersion, ct, impersonateSystemUserId);

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
    /// #2257 fix round 1 (K3): the row's <c>versionnumber</c>, read AS <paramref name="person"/> through the same impersonation
    /// (<c>MSCRMCallerID</c>) the write uses, or <see langword="null"/> when no row comes back. A row the person cannot read
    /// comes back as none, so it is reported exactly as a missing row: the update is no existence oracle. Never read app-only
    /// for a person: without the impersonated read nothing is written.
    /// </summary>
    private async Task<long?> ReadVersionAsPersonAsync(string entityLogicalName, Guid recordId, Guid person, CancellationToken ct)
    {
        if (_readAsPerson is null)
        {
            throw new InvalidOperationException(
                $"The row version of {entityLogicalName} {recordId} cannot be read as the run's user here, so nothing was written");
        }

        // The name is pasted into OData below: a bare logical name only (fail closed).
        var table = entityLogicalName.Trim().ToLowerInvariant();
        if (table.Length is 0 or > 128 || !table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException($"'{entityLogicalName}' is not a table name, so nothing was written");
        }

        // The table's entity set and primary key (table metadata, which every user reads), then the row, both AS the person.
        var definitions = await _readAsPerson.QueryAsync(
            "EntityDefinitions", $"$select=EntitySetName,PrimaryIdAttribute&$filter=LogicalName eq '{table}'", person, ct);
        var definition = definitions.FirstOrDefault();
        if (definition is null
            || !definition.TryGetValue("EntitySetName", out var set) || set.ValueKind != System.Text.Json.JsonValueKind.String
            || !definition.TryGetValue("PrimaryIdAttribute", out var key) || key.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new InvalidOperationException($"Table {entityLogicalName} has no entity set or primary key, so nothing was written");
        }

        var rows = await _readAsPerson.QueryAsync(
            set.GetString()!, $"$select=versionnumber&$filter={key.GetString()} eq {recordId:D}", person, ct);
        var row = rows.FirstOrDefault();
        if (row is null || !row.TryGetValue("versionnumber", out var version))
        {
            return null;
        }

        return version.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when version.TryGetInt64(out var number) => number,
            System.Text.Json.JsonValueKind.String when long.TryParse(version.GetString(), out var text) => text,
            _ => throw new InvalidOperationException(
                $"The row version of {entityLogicalName} {recordId} is unreadable, so nothing was written"),
        };
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
