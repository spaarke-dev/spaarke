using System.Data;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using SearchIndexColumns = Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// Repair mode (field-mapping-server-write-path-r1 task 016, spec FR-14 / FR-25 / NFR-03): steps 3, 7, 8 and 10 of the
/// create pipeline applied to an EXISTING row, fill-only, against the row's CURRENT values.
/// </summary>
/// <remarks>
/// <para><b>One read, at most one write.</b> The row is read once, committed, with every column (its filing lookups decide
/// the mapping source and the owner, as the reparent's whole-row read does). The four steps add to ONE patch; an empty patch
/// writes nothing, so a second repair of the same row writes nothing (NFR-03). A non-empty patch is ONE update conditioned on
/// the version that was read (<see cref="Spaarke.Dataverse.IGenericEntityService.UpdateIfRowVersionMatchesAsync"/>): a row
/// another writer changed meanwhile is not written and not retried (the next pass reads it afresh), so a value that writer
/// set is never overwritten by a patch computed before it.</para>
/// <para><b>Fill-only.</b> A present value is never overwritten, even one that differs from what the mapping, the owner rule,
/// the business unit or the stamp would give (FR-02 "not supplied": scalars absent, null or blank; lookups unbound; booleans
/// and option sets absent). The owner is changed only on a systemuser-owned row (a team-owned row keeps its owner) and never
/// on a row its table's ownership exception keeps with its creator.</para>
/// <para><b>System principal (ADR-028, FR-07).</b> Every read and the write are app-only under the managed identity; there
/// is no caller, no WhoAmI, no impersonation and no caller check. Pre-write rules are not run: they are create rules, and
/// are not widened to updates (ebilling-r1, w2-coordination).</para>
/// <para><b>Fail closed (NFR-01, ADR-019).</b> An owner that cannot be resolved leaves <c>ownerid</c> unchanged and is
/// reported as a typed refusal; the other fields are still repaired. No exception text is returned: the detail is logged
/// with the row id.</para>
/// <para><b>ADR-010.</b> A method on the existing scoped service: no registration, no endpoint. The scheduled pass and the
/// admin trigger (task 052) call it.</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>
    /// The invoice family: ebilling-r1's <c>InvoiceFamilyCoreRecordGuard</c> owns every stored-row write of these — its
    /// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/> tables, all six in the registry since task 017, so
    /// none is repaired past the guard.
    /// </summary>
    private static readonly IReadOnlySet<string> InvoiceFamilyTables =
        new HashSet<string>(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Review F1-f / UAC-r2: tables Repair never touches. Repair's own set, NOT the registry's ownership exception: Create's
    /// step 7 reads <see cref="RecordTableRule.OwnershipException"/> and refuses an unfiled create with it, so a registry
    /// entry would change Create.
    /// </summary>
    private static readonly IReadOnlySet<string> AppendOnlyTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_decisionrecord" };

    /// <summary>Repair's warning for a mapping source it could not read — neutral: no caller is addressed (K4).</summary>
    internal const string RepairSourceUnreadableWarning =
        "The related record could not be read, so no fields were copied from it.";

    private const string OwnerColumn = "ownerid";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";
    private const string VersionNumberColumn = "versionnumber";
    private const string SearchIndexTable = "sprk_aisearchindex";

    /// <summary>
    /// Repairs <paramref name="table"/> <paramref name="id"/> as the system principal: mapping (step 3), owner (step 7),
    /// search-index default (step 8) and the I-1 stamp (step 10), fill-only, in ONE conditional update — or none when
    /// nothing is missing. Never throws for a Dataverse fault (cancellation aside): every outcome is in the report.
    /// </summary>
    /// <exception cref="ArgumentException">No table or no id.</exception>
    internal async Task<RecordRepairOutcome> RepairAsync(string table, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (id == Guid.Empty)
            throw new ArgumentException("A record id is required.", nameof(id));

        table = table.Trim().ToLowerInvariant();
        if (AppendOnlyTables.Contains(table))
        {
            // Review F1-f / UAC-r2: a Decision Record is append-only (D-144 as amended: a user-owned one stays its user's,
            // never its unit's team). Repair neither reads nor writes it: no owner, no mapping, no stamp, no search index.
            _logger.LogInformation("[RECORD-REPAIR] {Table} {Id}: the table is append-only; not repaired", table, id);
            return RecordRepairOutcome.Row(table, id, RecordRepairStatus.Skipped, RecordRepairReason.TableAppendOnly);
        }

        var rule = TableRules(table);
        if (rule is null)
        {
            _logger.LogInformation("[RECORD-REPAIR] {Table} {Id}: the table is not one this service creates; not repaired", table, id);
            return RecordRepairOutcome.Row(table, id, RecordRepairStatus.Skipped, RecordRepairReason.TableUnsupported);
        }

        Entity? row;
        try
        {
            row = await ReadRowForRepairAsync(table, id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-REPAIR] {Table} {Id} could not be read; nothing was repaired", table, id);
            return RecordRepairOutcome.Row(table, id, RecordRepairStatus.Failed, RecordRepairReason.ReadFailed);
        }

        if (row is null)
            return RecordRepairOutcome.Row(table, id, RecordRepairStatus.Skipped, RecordRepairReason.RowNotFound);

        var patch = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // #2247: the patch columns a mapping rule gave
        var report = new List<RecordRepairField>();
        var warnings = new List<string>();

        // Review F1-b: the owner (and what the row's filing resolves to) is decided BEFORE the mapping, which it may block.
        var ownerParents = new List<RecordOwnershipParent>();
        var owner = await RepairOwnerAsync(table, rule, row, patch, report, ownerParents, ct).ConfigureAwait(false);
        if (owner.MappingBlocked is { } blocked)
        {
            _logger.LogInformation("[RECORD-REPAIR] {Table} {Id}: nothing was mapped ({Reason})", table, id, blocked);
            report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, blocked));
        }
        else
        {
            await RepairMappingAsync(table, rule, row, ownerParents, patch, mapped, report, warnings, ct).ConfigureAwait(false);
        }

        await RepairSearchIndexAsync(table, rule, row, owner.BusinessUnit, patch, report, ct).ConfigureAwait(false);
        await RepairStampAsync(table, row, patch, report, ct).ConfigureAwait(false);

        RecordRepairOutcome Outcome(RecordRepairStatus status, string? reason) => new()
        {
            Table = table,
            Id = id,
            Status = status,
            ReasonCode = reason,
            Fields = reason is null ? report : NotWritten(report, reason),
            OwnerRefusal = owner.Refusal,
            Warnings = warnings,
        };

        if (patch.Count == 0)
            return Outcome(RecordRepairStatus.NothingToRepair, null);

        var version = row.RowVersion
            ?? (row.GetAttributeValue<long?>(VersionNumberColumn) is { } number ? number.ToString(CultureInfo.InvariantCulture) : null);
        if (string.IsNullOrWhiteSpace(version))
        {
            _logger.LogError("[RECORD-REPAIR] {Table} {Id} came back without its version, so it cannot be written conditionally; " +
                             "nothing was written", table, id);
            return Outcome(RecordRepairStatus.Failed, RecordRepairReason.VersionUnknown);
        }

        if (await InvoiceFamilyUpdateRefusalAsync(table, id, patch, ct).ConfigureAwait(false) is { } refusedBy)
            return Outcome(RecordRepairStatus.Skipped, refusedBy);

        // #2170: the write itself asks the app-only refusal (the per-member guard, EveryGenericUpdateWriter_AsksTheAppOnlyRefusal).
        // #2247: it asks over the MAPPED columns only (`mapped`: what a field-mapping rule, a configuration, gave), never over
        // the columns Repair decides itself: the owner (step 7, its ownership logic writes `ownerid`, which the refusal
        // refuses from every configuration), the search-index default (step 8) and the I-1 stamp (step 10). A rule that
        // targets the owner or the creator is never mapped: the per-rule ask (DocumentServerOwnedRefusal) skips it first, and
        // ProtectedFromMapping also holds the plain owner and creator-person names. So this only fires if the mapping ever
        // lets one through: nothing is written.
        if (InheritedAccessPermission.RefuseAppOnlyWrite(table, patch.Keys.Where(mapped.Contains)) is { } refusedWrite)
        {
            _logger.LogError("[RECORD-REPAIR] {Table} {Id}: the repair write names a column Spaarke sets ({Reason}); nothing " +
                             "was written", table, id, refusedWrite.Reason);
            return Outcome(RecordRepairStatus.Skipped,
                refusedWrite.RefusalCode ?? InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        }

        var ownerWritten = patch.ContainsKey(OwnerColumn);
        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(table, id, version, patch, ct).ConfigureAwait(false);
        }
        catch (DBConcurrencyException ex)
        {
            // Nothing was written: the row moved after it was read. Not retried (NFR-03) — the next pass reads it afresh.
            _logger.LogInformation(ex, "[RECORD-REPAIR] {Table} {Id} changed after it was read; nothing was written", table, id);
            return Outcome(RecordRepairStatus.RowChanged, RecordRepairReason.RowChanged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Ambiguous: it may have landed. Never retried blindly (NFR-03); an owner write is evicted for anyway.
            _logger.LogError(ex, "[RECORD-REPAIR] the repair write of {Table} {Id} failed ({Columns}); not retried", table, id,
                string.Join(", ", patch.Keys.Order(StringComparer.Ordinal)));
            if (ownerWritten)
                await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);
            return Outcome(RecordRepairStatus.Failed, RecordRepairReason.WriteFailed);
        }
        catch (OperationCanceledException)
        {
            if (ownerWritten)
                await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);
            throw;
        }

        if (ownerWritten)
            await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);

        _logger.LogInformation("[RECORD-REPAIR] {Table} {Id} repaired ({Columns})", table, id,
            string.Join(", ", patch.Keys.Order(StringComparer.Ordinal)));
        return Outcome(RecordRepairStatus.Repaired, null);
    }

    /// <summary>The row, read committed with every column (the reparent's whole-row read); <c>null</c> when it does not exist.</summary>
    private async Task<Entity?> ReadRowForRepairAsync(string table, Guid id, CancellationToken ct)
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = false,
        };
        query.Criteria.AddCondition($"{table}id", ConditionOperator.Equal, id);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    // ── Step 3: mapping ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 3 against the row's current values: the source inferred from the row's filing (FR-03: its pair, else its one
    /// filed-under parent; the record-type catalog read app-only), the profile, the source read app-only
    /// (<see cref="MappingSourceReader.System"/>, no probe), then <see cref="CreateTimeFieldMapping.ApplyFillOnly"/> — the
    /// create's own fill decision — over a copy of the row. A mapped value that would file the row under a record (a filing
    /// or ownership-parent lookup, or an I-1 stamp column) is not written: Repair never re-files a row.
    /// </summary>
    private async Task RepairMappingAsync(
        string table, RecordTableRule rule, Entity row, IReadOnlyList<RecordOwnershipParent> ownerParents,
        Dictionary<string, object> patch, HashSet<string> mappedColumns, List<RecordRepairField> report, List<string> warnings,
        CancellationToken ct)
    {
        if (!rule.MappingApplies)
        {
            report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingExcluded));
            return;
        }

        var mappedHere = new List<string>(); // the patch's mapping columns, removed again on a fault (the owner may be in it)
        try
        {
            // A stamped child's I-1 stamp columns are COPIES of its source's root, not filings: leaving them in would read the
            // row as filed under two records (its source and the root), which FR-03 cannot tell apart.
            var copies = StampCopyColumns(table, row);
            var resolution = await MappingSourceResolver.ResolveAsync(
                    table, null, null, row.Attributes.Where(a => a.Value is not null && !copies.Contains(a.Key)).ToArray(),
                    RecordTypeTableAppOnlyAsync, ct)
                .ConfigureAwait(false);
            if (resolution.Source is not { } source)
            {
                if (resolution.Origin == MappingSourceOrigin.Undetermined)
                {
                    report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingSourceUndetermined));
                }
                else
                {
                    // FR-03: filed under nothing, so no source — the silent no-op, counted by the caller from this code.
                    report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingSourceNone));
                }

                return;
            }

            // Review F1-e (i): the source must be a record the owner step resolved over. The pair can name ANY table and is
            // free text any writer sets; a source the owner was not decided over was never checked for secure data.
            if (!ownerParents.Any(p => p.RecordId == source.RecordId
                    && string.Equals(p.EntityLogicalName, source.EntityLogicalName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("[RECORD-REPAIR] {Table} {Id}: the mapping source {Source} {SourceId} is not what the owner was " +
                                   "decided over; nothing was mapped", table, row.Id, source.EntityLogicalName, source.RecordId);
                report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingSourceUndetermined));
                return;
            }

            FieldMappingProfileEntity? profile;
            try
            {
                profile = await _appOnly
                    .GetFieldMappingProfileWithRulesAsync(source.EntityLogicalName, table, activeRulesOnly: true, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RECORD-REPAIR] the field-mapping profile {Source} -> {Table} could not be read for {Id}; " +
                                       "nothing was mapped", source.EntityLogicalName, table, row.Id);
                warnings.Add(MappingConfigurationUnreadable);
                report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingUnavailable));
                return;
            }

            var rules = (profile?.Rules ?? []).Select(NormalizedRule).ToList();
            if (rules.Count == 0)
            {
                report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingNoRules));
                return;
            }

            // The source's own secure status (review F1-e (ii)) is read in the same one read as its mapped columns.
            var sourceColumns = SourceColumnsRepairMayCopy(source.EntityLogicalName, CreateTimeFieldMapping.CollectSourceColumns(rules))
                .Concat(SourceGateColumns(source.EntityLogicalName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var read = await MappingSourceReadProbe.ReadAsync(
                    MappingSourceReader.System, source, sourceColumns, _entities, _logger, ct)
                .ConfigureAwait(false);
            if (!read.Readable || read.Record is null)
            {
                // Fail closed: a source that cannot be read cannot be told not secure, so nothing is copied from it.
                // K4: the probe's warning addresses a caller ("not available to you"); Repair runs as the system principal.
                warnings.Add(RepairSourceUnreadableWarning);
                report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingSourceUnreadable));
                return;
            }

            if (!await SourceMayBeCopiedAsync(table, row, source, read.Record, ct).ConfigureAwait(false))
            {
                report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingSourceSecure));
                return;
            }

            var target = new Entity(table, row.Id);
            foreach (var (column, value) in row.Attributes)
                target[column] = value;

            // UAC-r2 condition (b) F1-1: on top of Create's protected set (unchanged), the columns Repair never maps on any table.
            var neverMapped = ColumnsRepairNeverMaps(table);
            var protectedColumns = new HashSet<string>(ProtectedFromMapping(table, rule, serverSetLookupColumns: null),
                StringComparer.OrdinalIgnoreCase);
            protectedColumns.UnionWith(neverMapped.Keys);

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CreateTimeFieldMapping.ApplyFillOnly(
                rules, target, read.Record, protectedColumns, warnings, _logger,
                moneyAttributes: null, mappedColumns: written);

            var columns = rules
                .Select(r => r.TargetField?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                if (DocumentServerOwnedRefusal(table, column) is { } serverOwned)
                {
                    // UAC-r2: a document's access record and its D7 links are Spaarke's. Fill-only does not make a write safe:
                    // filling an empty link changes who sees the document.
                    report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, column, serverOwned));
                    continue;
                }

                if (neverMapped.TryGetValue(column, out var neverReason))
                {
                    // F1-1: never filled, even where empty — the value would decide access or filing, not describe the row.
                    report.Add(HoldsValue(row, column)
                        ? RecordRepairField.Present(RecordRepairPart.Mapping, column)
                        : RecordRepairField.Skipped(RecordRepairPart.Mapping, column, neverReason));
                    continue;
                }

                if (written.Contains(column) && target.Attributes.TryGetValue(column, out var value) && value is not null)
                {
                    if (WouldFileTheRow(table, column, value))
                    {
                        report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, column, RecordRepairReason.MappedValueWouldRefile));
                        continue;
                    }

                    if (value is string text && string.IsNullOrWhiteSpace(text))
                    {
                        // Review F1-c (NFR-03): a blank mapped string is no value — written, it would read as unset again and be
                        // written on every pass.
                        report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, column, RecordRepairReason.MappingNoValue));
                        continue;
                    }

                    patch[column] = value;
                    mappedHere.Add(column);
                    report.Add(RecordRepairField.Repaired(RecordRepairPart.Mapping, column));
                }
                else if (HoldsValue(row, column))
                {
                    report.Add(RecordRepairField.Present(RecordRepairPart.Mapping, column));
                }
                else
                {
                    report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, column, RecordRepairReason.MappingNoValue));
                }
            }

            // #2247: what the write asks the app-only refusal over (a fault above maps nothing, so adds nothing).
            mappedColumns.UnionWith(mappedHere);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-REPAIR] mapping {Table} {Id} faulted; nothing was mapped", table, row.Id);
            foreach (var mapped in mappedHere)
                patch.Remove(mapped);
            report.RemoveAll(f => f.Part == RecordRepairPart.Mapping);
            warnings.Add(MappingFaulted);
            report.Add(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, RecordRepairReason.MappingUnavailable));
        }
    }

    /// <summary>
    /// Why <paramref name="column"/> of a <paramref name="table"/> row is never written by Repair's mapping, or <c>null</c>:
    /// the app-only refusal every generic writer asks (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>, asked per
    /// rule as the field-mapping push asks it) — a document's Access Permission and own-narrowing record, and its D7 links
    /// (#2170), and on every table the owner and the creator (#2247). Its code is the reason reported.
    /// </summary>
    private static string? DocumentServerOwnedRefusal(string table, string column) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [column]) is { } refused
            ? refused.RefusalCode ?? InheritedAccessPermission.AppOnlyDocumentWriteReasonCode
            : null;

    /// <summary>
    /// The mapped source columns Repair may copy (owner decision 2026-10-21, field security option 1). Today: all of them,
    /// unchanged.
    /// </summary>
    // TODO(task 033): drop every column of sourceTable whose IsSecured is true, read through task 033's app-only metadata
    // reader. Fail closed: when that metadata is unavailable (a fault, or IsSecured unknown) map nothing for the row, with
    // reason record_repair.mapping_source_metadata_unavailable. Task 052 stays blocked until this lands.
    private static IEnumerable<string> SourceColumnsRepairMayCopy(string sourceTable, IEnumerable<string> mappedSourceColumns)
    {
        _ = sourceTable;
        return mappedSourceColumns;
    }

    /// <summary>A secure root's security business unit (provisioning's; it decides where the record's access lives).</summary>
    private const string SecurityBusinessUnitColumn = "sprk_securitybu";

    /// <summary>A record's SharePoint Embedded container (provisioning's; it decides where its documents' access lives).</summary>
    private const string ContainerColumn = "sprk_containerid";

    /// <summary>
    /// UAC-r2 condition (b) F1-1: the target columns Repair's mapping never fills on <paramref name="table"/>, each with its
    /// reason — on EVERY table, not only where Create's protected set names them (Create's set is unchanged). The secure flag,
    /// the access record, the access inheritance, the security unit and the container decide access
    /// (<see cref="RecordRepairReason.MappedValueConfersAccess"/>); the regarding pair decides the filing
    /// (<see cref="RecordRepairReason.MappedValueWouldRefilePair"/>); the table's Assigned / access-conferring columns grant
    /// access (<see cref="RecordRepairReason.MappedValueAssignsAccess"/>), from BOTH the canonical registry
    /// (<see cref="MembershipOptionsDefaults.CanonicalAccessConferringRegistry"/>) and the bound one
    /// (<see cref="MembershipOptions.AccessConferringRoles"/>, which <c>AssignedAccessMaterializer.RegistryFor</c> reads), so
    /// neither can be widened past the other. A fault reading the bound registry propagates to the mapping step, which then
    /// maps nothing (fail closed).
    /// </summary>
    private IReadOnlyDictionary<string, string> ColumnsRepairNeverMaps(string table)
    {
        // A column → reason map of what is NOT written (never a payload): built from column lists.
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] confersAccess =
        [
            AccessFollowsParent.SecureFlagColumn, InheritedAccessPermission.Column, AccessInheritance.Column,
            SecurityBusinessUnitColumn, ContainerColumn,
        ];
        foreach (var column in confersAccess)
            columns.TryAdd(column, RecordRepairReason.MappedValueConfersAccess);
        foreach (var column in new[] { SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn })
            columns.TryAdd(column, RecordRepairReason.MappedValueWouldRefilePair);

        var conferring = new List<AccessConferringColumn>();
        if (MembershipOptionsDefaults.CanonicalAccessConferringRegistry.TryGetValue(table, out var canonical))
            conferring.AddRange(canonical);
        using (var scope = _scopes.CreateScope())
        {
            if (scope.ServiceProvider.GetService<IOptions<MembershipOptions>>()?.Value.AccessConferringRoles.Entities
                    .TryGetValue(table, out var bound) == true && bound is not null)
            {
                conferring.AddRange(bound);
            }
        }

        foreach (var field in conferring.Select(c => c?.Field?.Trim()).Where(f => !string.IsNullOrEmpty(f)))
            columns.TryAdd(field!, RecordRepairReason.MappedValueAssignsAccess);
        return columns;
    }

    /// <summary>The source columns Repair reads to tell whether it may copy from it (review F1-e (ii)).</summary>
    private static IEnumerable<string> SourceGateColumns(string sourceTable) =>
        SecureChildLineage.IsRoot(sourceTable) ? [OwnerColumn, AccessFollowsParent.SecureFlagColumn] : [OwnerColumn];

    /// <summary>
    /// Review F1-e (ii), belt and braces over the owner gate: whether the source's data may be copied into the row. Not when
    /// the source is a secure-flagged root whose flag is not an explicit <c>false</c> (empty reads as flagged), or is owned
    /// by the Secure Record owner team, unless the row is owned by that Secure team. Anything that cannot be told — an
    /// owner that did not come back, a fault deciding the team, or (review pass 3) a Secure Record owner team that is
    /// missing or ambiguous when a team owns the source — is secure (fail closed).
    /// </summary>
    private async Task<bool> SourceMayBeCopiedAsync(string table, Entity row, MappingSource source, Entity read, CancellationToken ct)
    {
        try
        {
            var flagged = SecureChildLineage.IsRoot(source.EntityLogicalName)
                && read.GetAttributeValue<bool?>(AccessFollowsParent.SecureFlagColumn) != false;
            var sourceOwner = read.GetAttributeValue<EntityReference>(OwnerColumn);
            if (sourceOwner is null || sourceOwner.Id == Guid.Empty)
            {
                _logger.LogWarning("[RECORD-REPAIR] {Table} {Id}: the owner of the mapping source {Source} {SourceId} did not come " +
                                   "back; nothing was mapped", table, row.Id, source.EntityLogicalName, source.RecordId);
                return false;
            }

            var rowOwner = row.GetAttributeValue<EntityReference>(OwnerColumn);
            var sourceTeamOwned = string.Equals(sourceOwner.LogicalName, "team", StringComparison.OrdinalIgnoreCase);
            var rowTeamOwned = rowOwner is { LogicalName: "team" } && rowOwner.Id != Guid.Empty;
            Guid? secureTeam = null;
            if (sourceTeamOwned || (flagged && rowTeamOwned))
            {
                // Review pass 3: a Secure Record owner team that is missing or ambiguous cannot tell a Secure-team owner
                // from any other, so the source's owner cannot be told: fail closed.
                var resolved = await _shares.SecureOwnerTeamAsync(ct).ConfigureAwait(false);
                if (resolved.TeamId is not { } known || known == Guid.Empty)
                {
                    _logger.LogWarning("[RECORD-REPAIR] {Table} {Id}: the Secure Record owner team is {State}, so whether the mapping " +
                                       "source {Source} {SourceId} is secure cannot be told; nothing was mapped", table, row.Id,
                        resolved.Refusal is null ? "missing" : "ambiguous", source.EntityLogicalName, source.RecordId);
                    return false;
                }

                secureTeam = known;
            }

            var sourceSecureTeam = sourceTeamOwned && sourceOwner.Id == secureTeam;
            if (!flagged && !sourceSecureTeam)
                return true;

            var isolated = rowTeamOwned
                && (sourceSecureTeam ? rowOwner!.Id == sourceOwner.Id : rowOwner!.Id == secureTeam);
            if (!isolated)
            {
                _logger.LogInformation("[RECORD-REPAIR] {Table} {Id}: the mapping source {Source} {SourceId} is secure and the row " +
                                       "is not isolated; nothing was mapped", table, row.Id, source.EntityLogicalName, source.RecordId);
            }

            return isolated;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-REPAIR] {Table} {Id}: whether the mapping source {Source} {SourceId} is secure could " +
                                   "not be told; nothing was mapped", table, row.Id, source.EntityLogicalName, source.RecordId);
            return false;
        }
    }

    /// <summary>The I-1 stamp columns of a row filed under an intermediate (its copies of that record's root); else none.</summary>
    private static IReadOnlySet<string> StampCopyColumns(string table, Entity row)
    {
        if (!CoreAncestorResolver.StampSourceColumns.ContainsKey(table)
            || CoreAncestorResolver.ClassifyStampSource(table, row, CoreAncestorResolver.PartyRegardingColumnNames(table)).Kind
               != StampSourceKind.Source)
        {
            return new HashSet<string>();
        }

        return CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether writing <paramref name="value"/> to <paramref name="column"/> would file the row under a record.</summary>
    private static bool WouldFileTheRow(string table, string column, object value) =>
        IsFilingLookup(table, column)
        || CoreAncestorResolver.CoreAncestorLookups.Any(l => string.Equals(l.LookupAttribute, column, StringComparison.OrdinalIgnoreCase))
        || (value is EntityReference reference
            && RecordOwnershipResolver.IsOwnershipParent(reference.LogicalName)
            && !RecordOwnershipResolver.IsCitation(table, column));

    /// <summary>FR-02 "supplied", for the report: a value that is not blank text and not an unbound lookup.</summary>
    private static bool HoldsValue(Entity row, string column) =>
        row.Attributes.TryGetValue(column, out var value) && value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            EntityReference reference => reference.Id != Guid.Empty,
            _ => true,
        };

    /// <summary>The table a <c>sprk_recordtype_ref</c> names, read app-only (the pair's type, FR-03).</summary>
    private async Task<string?> RecordTypeTableAppOnlyAsync(Guid typeId, CancellationToken ct)
    {
        var query = new QueryExpression(RecordTypeTable)
        {
            ColumnSet = new ColumnSet(OwnedChildWrite.RecordTypeLogicalNameColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(RecordTypeTable + "id", ConditionOperator.Equal, typeId);
        var type = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return type?.GetAttributeValue<string>(OwnedChildWrite.RecordTypeLogicalNameColumn);
    }

    // ── Step 8: search-index default ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 8 (FR-10) for an existing row: on a search-indexed table, each of the two columns the row does not hold is filled
    /// from its OWNING business unit (the repaired owner's when step 7 re-owned it), by the create's own reads. A unit that
    /// cannot be read fills nothing.
    /// </summary>
    private async Task RepairSearchIndexAsync(
        string table, RecordTableRule rule, Entity row, Guid? businessUnit, Dictionary<string, object> patch,
        List<RecordRepairField> report, CancellationToken ct)
    {
        if (!rule.SearchIndexed)
            return;

        var name = SearchIndexColumns.LegacyTextColumn;
        var lookup = SearchIndexColumns.LookupColumn;
        var holdsName = HoldsValue(row, name) || patch.ContainsKey(name);
        var holdsLookup = HoldsValue(row, lookup) || patch.ContainsKey(lookup);
        if (holdsName)
            report.Add(RecordRepairField.Present(RecordRepairPart.SearchIndex, name));
        if (holdsLookup)
            report.Add(RecordRepairField.Present(RecordRepairPart.SearchIndex, lookup));
        if (holdsName && holdsLookup)
            return;

        // UAC-r2 condition (b) F2-1: filled only once the owner is settled on a team — already team-owned, or re-owned in this
        // patch (step 7 writes only a team). A held column overrides SearchIndexNameResolver's own choice for good, so it is
        // never filled from the unit of an owner Repair did not settle (kept in another unit, refused, or unknown).
        var rowOwner = row.GetAttributeValue<EntityReference>(OwnerColumn);
        var ownerIsTeam = patch.ContainsKey(OwnerColumn)
            || (rowOwner is { } held && held.Id != Guid.Empty
                && string.Equals(held.LogicalName, "team", StringComparison.OrdinalIgnoreCase));
        if (!ownerIsTeam)
        {
            _logger.LogInformation("[RECORD-REPAIR] {Table} {Id}: the owner is not settled on a team; no search index filled",
                table, row.Id);
            if (!holdsName)
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, name, RecordRepairReason.SearchIndexOwnerUnsettled));
            if (!holdsLookup)
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, lookup, RecordRepairReason.SearchIndexOwnerUnsettled));
            return;
        }

        (string? Name, Guid? Index) defaults;
        try
        {
            if (businessUnit is not { } unit)
                throw new InvalidOperationException("the row's owning business unit is not known");
            defaults = await BusinessUnitSearchDefaultsAsync(unit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-REPAIR] the search-index defaults for {Table} {Id} could not be read; none filled", table, row.Id);
            if (!holdsName)
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, name, RecordRepairReason.SearchIndexUnavailable));
            if (!holdsLookup)
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, lookup, RecordRepairReason.SearchIndexUnavailable));
            return;
        }

        if (!holdsName)
        {
            if (defaults.Name is { } indexName)
            {
                patch[name] = indexName;
                report.Add(RecordRepairField.Repaired(RecordRepairPart.SearchIndex, name));
            }
            else
            {
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, name, RecordRepairReason.SearchIndexNoDefault));
            }
        }

        if (!holdsLookup)
        {
            if (defaults.Index is { } index)
            {
                patch[lookup] = new EntityReference(SearchIndexTable, index);
                report.Add(RecordRepairField.Repaired(RecordRepairPart.SearchIndex, lookup));
            }
            else
            {
                report.Add(RecordRepairField.Skipped(RecordRepairPart.SearchIndex, lookup, RecordRepairReason.SearchIndexNoDefault));
            }
        }
    }

    // ── Step 10: I-1 stamp ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 10 (I-1) for an existing row: the stamp columns it is MISSING, from its source's current root
    /// (<see cref="CoreAncestorRestamper.PlanMissingStampAsync"/>). A held stamp — stale or not — and the row's children are
    /// <see cref="CoreAncestorStampReconciliationJob"/>'s.
    /// </summary>
    private async Task RepairStampAsync(
        string table, Entity row, Dictionary<string, object> patch, List<RecordRepairField> report, CancellationToken ct)
    {
        if (!CoreAncestorResolver.StampSourceColumns.ContainsKey(table))
            return;

        Dictionary<string, object>? missing;
        try
        {
            missing = await _restamper.PlanMissingStampAsync(table, row, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-REPAIR] the core-ancestor stamp of {Table} {Id} could not be planned; none added", table, row.Id);
            report.Add(RecordRepairField.Skipped(RecordRepairPart.CoreAncestorStamp, null, RecordRepairReason.StampUnavailable));
            return;
        }

        if (missing is null)
        {
            report.Add(RecordRepairField.Present(RecordRepairPart.CoreAncestorStamp, null, RecordRepairReason.StampNothingMissing));
            return;
        }

        foreach (var (column, value) in missing.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            patch[column] = value;
            report.Add(RecordRepairField.Repaired(RecordRepairPart.CoreAncestorStamp, column));
        }
    }

    // ── The write ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ONE place ebilling-r1's invoice-family guard drops into (w2-coordination: tasks 016 and 040 call
    /// <c>InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync</c>, app-only, D-96, on stored-row writes). A reason code
    /// refuses the write (the row is skipped, nothing written); <c>null</c> lets it go ahead.
    /// </summary>
    /// <remarks>
    /// TODO(#1911): the guard is not on master yet (ebilling-r1 PR #1911 is a draft). Until it lands, a repair of a
    /// <c>sprk_invoice</c> or <c>sprk_billingevent</c> row is NOT written (fail closed): replace the refusal below with
    /// <c>await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(...)</c> over <paramref name="patch"/>, returning its
    /// refusal's reason code, or <c>null</c> when it admits the write. Pre-write rules are not widened to updates.
    /// </remarks>
    private Task<string?> InvoiceFamilyUpdateRefusalAsync(
        string table, Guid id, IReadOnlyDictionary<string, object> patch, CancellationToken ct)
    {
        _ = patch;
        _ = ct;
        if (!InvoiceFamilyTables.Contains(table))
            return Task.FromResult<string?>(null);

        _logger.LogWarning(
            "[RECORD-REPAIR] {Table} {Id} was not repaired: the invoice-family update guard (ebilling-r1 #1911) is not available " +
            "on this build, so a stored invoice-family row is not written", table, id);
        return Task.FromResult<string?>(RecordRepairReason.InvoiceFamilyGuardUnavailable);
    }

    /// <summary>
    /// An owner write stales the access caches (<see cref="IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync"/>:
    /// "every owner-changing writer MUST call it"), once per write ATTEMPT. Resolved from a scope, so the service's
    /// registration is unchanged; never throws — the TTLs are the backstop.
    /// </summary>
    private async Task EvictOwnerChangeAsync(string table, Guid id)
    {
        var set = (SecureChildLineage.Children.TryGetValue(table, out var lineage) ? lineage.EntitySet : null)
                  ?? RegardingNameFields.EntitySetName(table);
        if (set is null)
        {
            _logger.LogError("[RECORD-REPAIR] {Table} {Id} was re-owned, but its entity set is not known, so its cached access " +
                             "was not evicted (the TTL backstop applies)", table, id);
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            if (scope.ServiceProvider.GetService<IMembershipCacheInvalidator>() is not { } invalidator)
            {
                _logger.LogWarning("[RECORD-REPAIR] {Table} {Id} was re-owned, but no cache invalidator is registered in this host " +
                                   "(the TTL backstop applies)", table, id);
                return;
            }

            await invalidator.InvalidateRecordOwnerChangeAsync(table, set, id, $"record-repair:{table}:{id:D}", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-REPAIR] evicting the caches after re-owning {Table} {Id} failed; the TTLs are the backstop",
                table, id);
        }
    }

    /// <summary>The report of a patch that was not written: each repaired field becomes skipped, with the row's reason.</summary>
    private static IReadOnlyList<RecordRepairField> NotWritten(IReadOnlyList<RecordRepairField> report, string reason) =>
        report.Select(f => f.State == RecordRepairFieldState.Repaired
                ? f with { State = RecordRepairFieldState.Skipped, ReasonCode = reason }
                : f)
            .ToArray();
}

/// <summary>What a repair did with the row (task 016).</summary>
internal enum RecordRepairStatus
{
    /// <summary>The fill-only patch was written in one update.</summary>
    Repaired,

    /// <summary>Nothing was missing: no write.</summary>
    NothingToRepair,

    /// <summary>The row was not repaired (unsupported table, no such row, the invoice-family guard): no write.</summary>
    Skipped,

    /// <summary>The row changed after it was read: nothing was written, not retried.</summary>
    RowChanged,

    /// <summary>The read or the write faulted (the write is not retried; see the logs, with the row id).</summary>
    Failed,
}

/// <summary>The pipeline step a reported field belongs to.</summary>
internal enum RecordRepairPart
{
    /// <summary>Step 3, field mapping.</summary>
    Mapping,

    /// <summary>Step 7, the owner.</summary>
    Owner,

    /// <summary>Step 8, the search-index default.</summary>
    SearchIndex,

    /// <summary>Step 10, the I-1 core-ancestor stamp.</summary>
    CoreAncestorStamp,
}

/// <summary>A reported field's outcome.</summary>
internal enum RecordRepairFieldState
{
    /// <summary>Filled by the repair's write.</summary>
    Repaired,

    /// <summary>The row holds a value: kept (fill-only).</summary>
    Present,

    /// <summary>Not filled, for <see cref="RecordRepairField.ReasonCode"/>.</summary>
    Skipped,

    /// <summary>The owner could not be decided: <see cref="RecordRepairOutcome.OwnerRefusal"/> is the typed refusal.</summary>
    Refused,
}

/// <summary>One field (or, with no <see cref="Column"/>, one whole step) of a repair report.</summary>
internal sealed record RecordRepairField(
    RecordRepairPart Part, string? Column, RecordRepairFieldState State, string? ReasonCode = null)
{
    internal static RecordRepairField Repaired(RecordRepairPart part, string column) =>
        new(part, column, RecordRepairFieldState.Repaired);

    internal static RecordRepairField Present(RecordRepairPart part, string? column, string? reason = null) =>
        new(part, column, RecordRepairFieldState.Present, reason);

    internal static RecordRepairField Skipped(RecordRepairPart part, string? column, string reason) =>
        new(part, column, RecordRepairFieldState.Skipped, reason);

    internal static RecordRepairField Refused(RecordRepairPart part, string column, string reason) =>
        new(part, column, RecordRepairFieldState.Refused, reason);
}

/// <summary>A repair's report (ADR-019: codes and caller-safe words only; the detail is in the logs, with the row id).</summary>
internal sealed record RecordRepairOutcome
{
    public required string Table { get; init; }

    public required Guid Id { get; init; }

    public required RecordRepairStatus Status { get; init; }

    /// <summary>Why the row was skipped, changed or failed; <c>null</c> when it was repaired or had nothing to repair.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Every field each step considered, in step order.</summary>
    public IReadOnlyList<RecordRepairField> Fields { get; init; } = [];

    /// <summary>The typed refusal when the owner could not be decided (the owner is then unchanged).</summary>
    public RecordCreationRefusal? OwnerRefusal { get; init; }

    /// <summary>Field-mapping warnings, as the create reports them.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Whether the one update was written.</summary>
    public bool Wrote => Status == RecordRepairStatus.Repaired;

    internal static RecordRepairOutcome Row(string table, Guid id, RecordRepairStatus status, string reason) =>
        new() { Table = table, Id = id, Status = status, ReasonCode = reason };
}

/// <summary>The stable reason codes of a repair report.</summary>
internal static class RecordRepairReason
{
    internal const string TableUnsupported = "record_repair.table_unsupported";
    internal const string RowNotFound = "record_repair.row_not_found";
    internal const string ReadFailed = "record_repair.read_failed";
    internal const string VersionUnknown = "record_repair.version_unknown";
    internal const string RowChanged = "record_repair.row_changed";
    internal const string WriteFailed = "record_repair.write_failed";

    /// <summary>ebilling-r1 #1911's guard is not on this build: an invoice-family row is not written (fail closed).</summary>
    internal const string InvoiceFamilyGuardUnavailable = "record_repair.invoice_family_guard_unavailable";

    internal const string MappingExcluded = "record_repair.mapping_excluded";

    /// <summary>FR-03: the row is filed under nothing, so no mapping source can be inferred (the silent no-op, counted).</summary>
    internal const string MappingSourceNone = "record_repair.mapping_source_none";

    internal const string MappingSourceUndetermined = "record_repair.mapping_source_undetermined";
    internal const string MappingUnavailable = "record_repair.mapping_unavailable";
    internal const string MappingNoRules = "record_repair.mapping_no_rules";
    internal const string MappingSourceUnreadable = "record_repair.mapping_source_unreadable";
    internal const string MappingNoValue = "record_repair.mapping_no_value";
    internal const string MappedValueWouldRefile = "record_repair.mapped_value_would_refile";

    /// <summary>UAC-r2 condition (b) F1-1: a column that decides access (the secure flag, the access record, the access
    /// inheritance, the security unit, the container) — never filled by Repair's mapping, on any table.</summary>
    internal const string MappedValueConfersAccess = "record_repair.mapped_value_confers_access";

    /// <summary>UAC-r2 condition (b) F1-1: an Assigned / access-conferring registry column (a value there is a grant) —
    /// never filled by Repair's mapping.</summary>
    internal const string MappedValueAssignsAccess = "record_repair.mapped_value_assigns_access";

    /// <summary>UAC-r2 condition (b) F1-1: the ADR-024 regarding pair — filling it would change what the row is filed under
    /// (its stamp source), so Repair's mapping never fills it.</summary>
    internal const string MappedValueWouldRefilePair = "record_repair.mapped_value_would_refile_pair";

    /// <summary>UAC-r2 condition (b) F2-1: the owner is not settled on a team, so the search index is not filled from the
    /// row's current unit (the column would override the resolver's own choice for good).</summary>
    internal const string SearchIndexOwnerUnsettled = "record_repair.search_index_owner_unsettled";

    internal const string OwnerUnknown = "record_repair.owner_unknown";
    internal const string OwnerTeamOwned = "record_repair.owner_team_owned";
    internal const string OwnerKeptByException = "record_repair.owner_kept_by_exception";

    /// <summary>Review F1-a: a secure-flagged root (flag not an explicit false) is provisioned by task 144, never re-owned here.</summary>
    internal const string OwnerRootSecure = "record_repair.owner_root_secure";

    /// <summary>Review F1-f / UAC-r2: a Decision Record is append-only; Repair neither reads nor writes it.</summary>
    internal const string TableAppendOnly = "record_repair.table_append_only";

    /// <summary>Review F1-b: the row's filing resolves to the Secure team, which does not own the row yet — nothing is mapped.</summary>
    internal const string MappingSourceSecure = "record_repair.mapping_source_secure";

    /// <summary>Review F1-b: the owner of what the row is filed under could not be decided — nothing is mapped (fail closed).</summary>
    internal const string MappingFilingUndecided = "record_repair.mapping_filing_undecided";

    /// <summary>Task 016 escalation trigger 2: the owner would move the row to another business unit (fail closed).</summary>
    internal const string OwnerMovesBusinessUnit = "record_repair.owner_moves_business_unit";

    internal const string SearchIndexUnavailable = "record_repair.search_index_unavailable";
    internal const string SearchIndexNoDefault = "record_repair.search_index_no_default";

    internal const string StampUnavailable = "record_repair.stamp_unavailable";
    internal const string StampNothingMissing = "record_repair.stamp_nothing_missing";
}
