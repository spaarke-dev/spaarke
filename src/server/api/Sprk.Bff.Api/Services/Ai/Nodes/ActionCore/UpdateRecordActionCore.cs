using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Services.RecordCreation;

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
/// <remarks>
/// <para><paramref name="ImpersonateSystemUserId"/> (task 031): OPTIONAL Dataverse <c>systemuserid</c> to run the PATCH
/// AS (MSCRMCallerID impersonation). Null = app-only. <see cref="Guid.Empty"/> is refused with an
/// <see cref="ArgumentException"/> before the PATCH is sent (task 104, fail closed). Two callers supply it: the Job B
/// apply seam (<c>ActionSeam</c>, the confirming user) and, since #2257 (owner round 123, "run as the user"), the Update
/// Record node on a run a person started (the run's user). A system run leaves it null.</para>
/// <para><paramref name="PayloadSource"/> (#2257 fix round 1): who built the payload. The default,
/// <see cref="UpdateRecordPayloadSource.Configuration"/>, is fail closed: the payload is asked
/// <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> whatever identity the write runs as. Only
/// <see cref="UpdateRecordPayloadSource.ConfirmedProposal"/> with an impersonated user skips it.</para>
/// </remarks>
internal sealed record UpdateRecordActionInput(
    string EntityLogicalName,
    Guid RecordId,
    IReadOnlyList<RenderedFieldMapping>? FieldMappings,
    IReadOnlyDictionary<string, string?>? LegacyFields,
    IReadOnlyList<RenderedLookup>? Lookups,
    Guid? ImpersonateSystemUserId = null,
    UpdateRecordPayloadSource PayloadSource = UpdateRecordPayloadSource.Configuration);

/// <summary>
/// Who built an <see cref="UpdateRecordActionInput"/>'s payload, which decides whether it is asked the deny-lists of
/// <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> (#2257 fix round 1, main-session decision).
/// </summary>
internal enum UpdateRecordPayloadSource
{
    /// <summary>
    /// A configuration (a playbook author, who may not be the person running it) or a model built the payload: it is asked
    /// the refusal whatever identity the write runs as, impersonated or app-only. The Update Record node's payload. The
    /// default, so a new caller is fail closed.
    /// </summary>
    Configuration = 0,

    /// <summary>
    /// The person the write impersonates confirmed this exact payload (the Job B apply through <c>ActionSeam</c>): their own
    /// edit, decided by their Dataverse privileges and field security. Without an impersonated user it is still refused.
    /// </summary>
    ConfirmedProposal = 1,
}

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

        // Handle lookup fields with @odata.bind syntax. The target's entity set is read from metadata (#2091 round 4: a
        // guessed plural is wrong for sprk_analysis → sprk_analysises and every -y table), once per target table per run.
        if (input.Lookups is { Count: > 0 } lookups && lookups.Any(l => Guid.TryParse(l.RenderedTargetId, out _)))
        {
            using var metadataScope = _scopeFactory.CreateScope();
            var entities = metadataScope.ServiceProvider.GetService<IGenericEntityService>()
                ?? throw new InvalidOperationException(
                    "No IGenericEntityService is registered, so a lookup's entity set cannot be read; the record was not written.");
            var entitySets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lookup in lookups)
            {
                if (Guid.TryParse(lookup.RenderedTargetId, out var targetGuid))
                {
                    if (!entitySets.TryGetValue(lookup.TargetEntity, out var entitySetName))
                    {
                        entitySetName = await entities.GetEntitySetNameAsync(lookup.TargetEntity, cancellationToken).ConfigureAwait(false);
                        entitySets[lookup.TargetEntity] = entitySetName;
                    }

                    updatePayload[$"{lookup.Field}@odata.bind"] = $"/{entitySetName}({targetGuid})";
                }
            }
        }

        // Task 039 (FR-38): on a search-indexed table both index columns are the SERVER's (step 8 of the create writes them from
        // the owning business unit). A mapping, default or confirmed proposal that names either has it dropped — never refused,
        // and not written — so the row's index stays as it is (and fieldsUpdated does not list it).
        // Round 2 (S5): nothing left to write means no write is asked of Dataverse (not even an empty one) and none of the reads
        // or after-write steps run; a payload that was empty before the drop is not affected (DropSearchIndexFromUpdate).
        if (RecordTableRules.DropSearchIndexFromUpdate(
                input.EntityLogicalName, input.RecordId, updatePayload,
                input.PayloadSource == UpdateRecordPayloadSource.ConfirmedProposal
                    ? RecordTableRules.SearchIndexOriginCaller
                    : RecordTableRules.SearchIndexOriginMapping,
                _logger).Emptied)
        {
            return [];
        }

        // unified-access-control-r2 task 189 fix round C (K5): a payload that is not a confirmed proposal written AS its
        // confirming user (see #2257 below: the Update Record node's, whoever runs the playbook, and the action seam's without
        // an impersonated user) never writes a document's Access Permission or its own-narrowing record; only the document
        // rule's writers do. Refused before anything is written. Task 188 round 4e: nor its D7 link columns
        // (sprk_parentdocument, sprk_relatedcommunication), set, cleared or bound (InheritedAccessPermission.DocumentLinkColumns).
        // #2251: nor any row of a table whose rows are access (grants, membership, ethical walls; AccessTables), nor a secure
        // root's secure flag, access columns or container (SecureRootColumns); #2253: nor a non-root table's access columns
        // (AccessColumns), nor a table name that is not a bare identifier.
        // #2257 fix round 1 (main-session decision): a payload built by configuration or a model — the Update Record node's,
        // whoever runs the playbook — is asked the refusal even when the write impersonates the person who started the run:
        // the author of the payload may not be that person. Only a confirmed proposal (ActionSeam's Job B apply, the
        // confirming user's own edit) written AS that user skips it, and runs under that user's own Dataverse privileges,
        // which decide it. Fix round 2 (K4): Guid.Empty is no person.
        // Master merge after #2253: the refusal is asked as WriteIdentity.AppOnly (every set) even when the run writes AS a
        // person. WriteIdentity.Caller (the pre-#2251 sets only) is the field-mapping push's, which #2253 leaves to its
        // caller's own privileges; a playbook payload is built by configuration or a model, not by the runner, so the
        // runner's own rights are not the only lock on it (PlaybookRunIdentityTests).
        if ((input.PayloadSource != UpdateRecordPayloadSource.ConfirmedProposal
                || input.ImpersonateSystemUserId is not { } confirmingUser || confirmingUser == Guid.Empty)
            && InheritedAccessPermission.RefuseAppOnlyWrite(
                input.EntityLogicalName, updatePayload.Keys, InheritedAccessPermission.WriteIdentity.AppOnly) is { } documentRefusal)
        {
            throw new RecordOwnerUnresolvedException(input.EntityLogicalName, documentRefusal);
        }

        async Task Patch(CancellationToken ct)
        {
            // ebilling-r1 #2081: an invoice or line is written UPDATE-ONLY (If-Match: *, impersonation kept). The guard below
            // checked that the row exists; a row deleted since must not be re-created by the upsert. Refused like the guard.
            if (InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget(input.EntityLogicalName))
            {
                if (await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                        _fieldMappingService, input.EntityLogicalName, input.RecordId, updatePayload,
                        input.ImpersonateSystemUserId, notFoundAsReadFailed: input.ImpersonateSystemUserId is not null, ct)
                        .ConfigureAwait(false) is { } gone)
                {
                    throw InvoiceFamilyCoreRecordGuard.RefusalException(input.EntityLogicalName, gone);
                }

                return;
            }

            await _fieldMappingService.UpdateRecordFieldsAsync(
                input.EntityLogicalName,
                input.RecordId,
                updatePayload,
                ct,
                input.ImpersonateSystemUserId).ConfigureAwait(false);
        }

        // ebilling-r1 DEF-022 (#2056): an invoice or invoice line names exactly one core record, a line regards its invoice's,
        // and an invoice re-file keeps its lines (D-96) — decided BEFORE the PATCH on the stored lookups (read as the app)
        // overlaid with this payload. A refusal writes nothing and throws the guard's stable code (as an owner refusal does).
        if (InvoiceFamilyCoreRecordGuard.GuardedLookups(input.EntityLogicalName) is not null)
        {
            using var familyScope = _scopeFactory.CreateScope();
            if (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(
                    input.EntityLogicalName, input.RecordId, updatePayload,
                    familyScope.ServiceProvider.GetService<IGenericEntityService>(), cancellationToken,
                    writeIsImpersonated: input.ImpersonateSystemUserId is not null).ConfigureAwait(false)
                is { RefusalCode: { } familyCode })
            {
                // Code only: the guard read as the app, so its reason can name records the caller has not read.
                throw InvoiceFamilyCoreRecordGuard.RefusalException(input.EntityLogicalName, familyCode);
            }
        }

        // Task 146: a lookup that FILES a child table under a record (a document onto a matter, an event onto a
        // secure project) is a reparent — the child's owner is re-derived over every parent it will have
        // (secure-if-any) BEFORE the PATCH, and reassigned when it moves. A refusal writes nothing and throws
        // RecordOwnerUnresolvedException (ActionSeam returns it as a typed failure; the node executor fails the node).
        // The resolver is a singleton; it is resolved from the scope factory because this core's constructor is
        // frozen (task 031).
        // Task 158 (owner round 6): a work assignment or project filed under a secure matter or project is secured. Whether
        // the record this update files it under is secure must be readable, or nothing is written — the same refusal as an
        // unresolved owner (ActionSeam returns it as a typed failure; the node executor fails the node).
        if (Sprk.Bff.Api.Services.Access.SecureRootInheritance.Inherits(input.EntityLogicalName))
        {
            using var gateScope = _scopeFactory.CreateScope();
            var gate = gateScope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.SecureRootFilingGate>();
            var refusal = gate is null
                ? (Sprk.Bff.Api.Services.Access.SecureRootInheritance.FilingColumnsOf(input.EntityLogicalName.Trim().ToLowerInvariant())
                        .Overlaps(updatePayload.Keys.Select(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn))
                    ? RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                        "whether the record it would be filed under is secure cannot be checked here, so it was not written")
                    : null)
                : await gate.CheckAsync(input.EntityLogicalName, input.RecordId, updatePayload, cancellationToken).ConfigureAwait(false);
            if (refusal is not null)
                throw new RecordOwnerUnresolvedException(input.EntityLogicalName, refusal);
        }

        // A filing lookup bound to something that names no row of its target is refused here, before anything is read or
        // written (#2091 round 3; RecordOwnerUnresolvedException, like an unresolved owner).
        var parentChanges = ParentChangesOf(input, updatePayload);

        // #2091: a write that may re-file the row (the ONE predicate, RecordReparent.MayRefile) runs the ONE step after a
        // re-file (SecureChildReconciler.AfterRefileAsync), as every other re-file writer does. Whether the row was isolated
        // is read BEFORE the write (once more after a fault — a timeout included — then handed to the step as a fault): a move
        // OUT releases only from a row that was. Only the caller's own cancellation ends the write here. The reconciler is
        // scoped and this core's constructor is frozen (task 031), so it comes from a scope.
        var refiledTable = input.EntityLogicalName.Trim().ToLowerInvariant();
        var mayRefile = RecordReparent.MayRefile(refiledTable, parentChanges);
        using var refileScope = mayRefile ? _scopeFactory.CreateScope() : null;
        var children = refileScope?.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.SecureChildReconciler>();
        if (mayRefile && children is null)
        {
            // Every host registers it (ExternalAccessModule); a composition without it leaves a re-filed row's sharing as it
            // was, so say so loudly rather than skip it silently.
            _logger.LogError(
                "UpdateRecord re-filed {EntityType} {RecordId} but no SecureChildReconciler is registered: its sharing and the rows "
                + "filed under it were NOT reconciled after the move (#2091)",
                refiledTable, input.RecordId);
        }

        var wasIsolated = children is not null
            ? await Sprk.Bff.Api.Services.Access.RefileIsolationRead.BeforeWriteAsync(children, refiledTable, input.RecordId, cancellationToken)
                .ConfigureAwait(false)
            : null;

        // #2091 round 4: the reparent reads the row; a change that moves none of its parents (a rebind onto the parent it
        // holds) is written and is no re-file, so the step does not run.
        var refiled = false;
        RecordOwnerResolution? refiledBy = null;
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller = null;
        if (parentChanges.Count == 0)
        {
            await Patch(cancellationToken);
        }
        else
        {
            using var scope = _scopeFactory.CreateScope();
            var ownership = scope.ServiceProvider.GetRequiredService<IRecordOwnershipResolver>();
            var reparent = await ownership.ReparentAsync(
                new RecordReparent
                {
                    EntityLogicalName = input.EntityLogicalName,
                    RecordId = input.RecordId,
                    ParentChanges = parentChanges,
                    CallerSystemUserId = input.ImpersonateSystemUserId,
                    // Task 146 c1-r1, owner round 13 item 8: a move out of a secure root (F3) is asked of the user this
                    // update IMPERSONATES — the person it acts for — with RetrievePrincipalAccess asked as that user. An
                    // update that impersonates nobody (a system run's playbook node) passes no caller and is refused such
                    // a move.
                    SecureExitCaller = secureExitCaller = input.ImpersonateSystemUserId is { } person
                        ? Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForImpersonatedUser(
                            person, scope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>())
                        : null,
                },
                Patch,
                cancellationToken).ConfigureAwait(false);
            if (reparent.IsRefused)
            {
                throw new RecordOwnerUnresolvedException(input.EntityLogicalName, reparent);
            }

            refiled = !reparent.ParentsUnchanged;
            refiledBy = reparent;
        }

        // After whichever path wrote the record (the plain PATCH or the re-file): task 156's re-stamp, then task 142's
        // Assigned-To materializer (batch 4 integration — the write, then the restamp, then the materializer).
        await RestampAfterWriteAsync(input.EntityLogicalName, input.RecordId, updatePayload.Keys).ConfigureAwait(false);
        await SecureFiledRootAfterWriteAsync(input.EntityLogicalName, input.RecordId, updatePayload.Keys).ConfigureAwait(false);

        // #2091, AFTER the re-stamp (the stamps on the rows filed under it are lookups the ownership rule reads): the re-filed
        // row's mirror and the pass over everything filed under it — and a communication's thread follows the filing. Never
        // throws; the write stands.
        if (children is not null && wasIsolated is not null)
        {
            if (refiled)
            {
                // #2143 round 7 (F1): with the person the re-file's F3 check acted for, so the thread that follows moves on it.
                // #2159 slice 2: with the reparent's answer (its F3 permission), so a re-filed document's file release is recorded.
                await children.AfterRefileAsync(
                    refiledTable, input.RecordId, wasIsolated, Sprk.Bff.Api.Services.Access.RefileThreadStep.Reconcile,
                    secureExitCaller, refiledBy, CancellationToken.None).ConfigureAwait(false);
            }
        }
        // Spaarkeai-word-add-in-r1 task 129: a re-file that could not run the step above (a host without the reconciler, logged
        // above) still lets a re-filed communication's thread follow the filing — once. Never throws.
        else if (mayRefile && refiled)
        {
            await ReconcileThreadAfterRefileAsync(input.EntityLogicalName, input.RecordId, secureExitCaller).ConfigureAwait(false);
        }

        // Task 142 (L1, owner Q5 + A4): a PATCH that wrote a root's "Assigned *" column (as a value or an @odata.bind)
        // materializes the Assigned-To access now. After the PATCH committed; never throws and never fails this update.
        await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
            _scopeFactory, input.EntityLogicalName, input.RecordId, updatePayload.Keys, grantorOid: null, _logger,
            cancellationToken).ConfigureAwait(false);

        return updatePayload.Keys.ToArray();
    }

    /// <summary>
    /// Task 129: after a written re-file of a <c>sprk_communication</c>, its thread follows the filing
    /// (<see cref="Sprk.Bff.Api.Services.Communication.IThreadResolver.ReconcileThreadForFilingAsync"/>, best-effort, never
    /// throws). No other table; a host without the resolver skips it. Runs to completion once the record is written. It runs
    /// outside the after-re-file step, so it reconciles as <c>Standalone</c>: a join it makes under another record runs that
    /// step itself (#2110 re-check K2).
    /// </summary>
    private async Task ReconcileThreadAfterRefileAsync(
        string entityLogicalName, Guid recordId, Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller)
    {
        if (!string.Equals(entityLogicalName?.Trim(), "sprk_communication", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Communication.IThreadResolver>() is { } threads)
            await threads.ReconcileThreadForFilingAsync(
                recordId, Sprk.Bff.Api.Services.Communication.FilingReconcileHost.Standalone, secureExitCaller,
                beforeMessageGrant: null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 156 (owner round 4 item 5, option b): an UpdateRecord node can write ANY column — including what a to-do /
    /// event / communication / analysis is filed under, or the matter / project of a record others are filed under. Such a
    /// write re-stamps the affected copies in the same operation (<see cref="CoreAncestorRestamper"/>). A write that cannot
    /// move a stamp resolves nothing. Never thrown: a child that fails is logged and the reconciliation job repairs it;
    /// this record's own update stands. Runs to completion once the record is written (no caller token).
    /// </summary>
    private async Task RestampAfterWriteAsync(string entityLogicalName, Guid recordId, IEnumerable<string> writtenKeys)
    {
        if (!CoreAncestorRestamper.WriteCanMoveAStamp(entityLogicalName, writtenKeys))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CoreAncestorRestamper>()
            .AfterWriteAsync(entityLogicalName, recordId, writtenKeys, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The parent lookups this update writes, as <c>column → parent</c>, when the target is a child table whose
    /// owner follows its parents (<see cref="RecordOwnershipResolver.IsReparentableChild"/>). The column is the
    /// lookup's attribute name, lower-cased (the navigation-property spelling the bind uses differs only in case).
    /// </summary>
    /// <remarks>
    /// The node's own <see cref="UpdateRecordActionInput.Lookups"/> are read as typed references. Everything else the PATCH
    /// writes — field mappings and legacy fields, which can carry a <c>"sprk_RegardingMatter@odata.bind": "/sprk_matters(id)"</c>
    /// or a clear — goes through the ONE payload reading, <see cref="RecordReparent.ParentChangesWithClearsIn{TValue}"/>
    /// (#2091 round 3): a bound filing lookup is a parent change, a malformed or mismatched bind refuses the write, and every
    /// <c>null</c> is a candidate CLEAR (task 146 verifier item 8) that the resolver ignores for a column holding no parent.
    /// </remarks>
    private static IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> ParentChangesOf(
        UpdateRecordActionInput input, IReadOnlyDictionary<string, object?> payload)
    {
        var changes = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        if (!RecordOwnershipResolver.IsReparentableChild(input.EntityLogicalName))
            return changes;

        var lookupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookup in input.Lookups ?? [])
        {
            if (!Guid.TryParse(lookup.RenderedTargetId, out var targetId))
                continue;

            lookupKeys.Add($"{lookup.Field}@odata.bind");
            if (RecordOwnershipResolver.IsOwnershipParent(lookup.TargetEntity) && targetId != Guid.Empty)
                changes[lookup.Field.ToLowerInvariant()] = new Microsoft.Xrm.Sdk.EntityReference(lookup.TargetEntity, targetId);
        }

        foreach (var (column, parent) in RecordReparent.ParentChangesWithClearsIn(
                     input.EntityLogicalName, payload.Where(p => !lookupKeys.Contains(p.Key))))
        {
            changes.TryAdd(column, parent);
        }

        // #2110 (ontology K3): with the lineage lookups the update sets that are no ownership parent (a message's thread) — the
        // typed lookups are binds in the payload by now — so the reparent can tell whether it moved one (ParentsUnchanged).
        return RecordReparent.WithLineageLookupsIn(input.EntityLogicalName, changes, payload);
    }

    /// <summary>
    /// Task 158 (owner round 6): a work assignment or project this update filed under a secure matter or project is secured
    /// now, through provisioning's own steps (<see cref="Sprk.Bff.Api.Services.Access.SecureRootFilingGate"/>). Never thrown:
    /// an incomplete securing is logged and the secure-root inheritance job completes it. Costs nothing for any other table.
    /// </summary>
    private async Task SecureFiledRootAfterWriteAsync(string entityLogicalName, Guid recordId, IEnumerable<string> writtenKeys)
    {
        if (!Sprk.Bff.Api.Services.Access.SecureRootInheritance.Inherits(entityLogicalName))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var gate = scope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.SecureRootFilingGate>();
        if (gate is not null)
        {
            await gate.SecureAfterWriteAsync(entityLogicalName, recordId, writtenKeys, traceId: null).ConfigureAwait(false);
        }
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
