using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Keeps a child record's core-ancestor stamp equal to the root of the record it is filed under (unified-access-control-r2
/// task 156, owner decision round 4 item 5 — option b: "re-stamp the children whenever the intermediate record is
/// re-filed").
/// </summary>
/// <remarks>
/// <para><b>Why the stamp goes stale.</b> A to-do filed under a communication carries a COPY of that communication's
/// project / matter / work assignment in its own <c>sprk_regarding{core}</c> columns (<see cref="CoreAncestorResolver"/>,
/// FR-26). The copy is what makes access inheritance and the storage resolver one hop. Until this task nothing refreshed
/// it: re-file the communication from ordinary matter A to SECURE matter B and the to-do still reads "matter A" — an
/// access over-grant (task 051 §1) and, for a resolver that trusted it, the #1038 leak (task 155 escalation trigger 2).</para>
///
/// <para><b>Three entry points, one rule.</b></para>
/// <list type="bullet">
/// <item><see cref="AfterWriteAsync"/> — the cascade, called by every BFF path that writes a record's root or what it is
/// filed under, in the same operation, AFTER that write succeeded. A child that fails is REPORTED and the caller's own
/// write stands: the reconciliation job repairs the child on its next run.</item>
/// <item><see cref="RestampChildAsync"/> — one child, repaired from its source's CURRENT root (the
/// <c>CoreAncestorRestamp</c> job the storage resolver enqueues on a stale refusal, and the reconciliation job).</item>
/// <item><see cref="RestampChildrenOfAsync"/> — every child stamped from one intermediate.</item>
/// </list>
/// <para>All three classify a row with <see cref="CoreAncestorResolver.ClassifyStampSource"/>, derive the source's root
/// with <see cref="CoreAncestorResolver.ResolveStampsAsync"/> and change ONLY the stamp columns of the root types that
/// source can carry (<see cref="CoreAncestorResolver.CarriableRootTypes"/>). A root column the row's pair names (the
/// user's direct choice — the Office carrier to-do) is never written, and neither is the owner (tasks 146-148).</para>
///
/// <para><b>Transitive.</b> A child that is itself an intermediate (an event under a communication, with to-dos under the
/// event) cascades in turn, depth-first, bounded by <see cref="MaxCascadeDepth"/>. It recurses only below a child whose
/// copy it just CHANGED, so a cycle (an event filed under an event filed under the first) converges: the second visit finds
/// the copy already equal. Past a bound the report says TRUNCATED rather than pretending to be complete.</para>
///
/// <para><b>Placement (CLAUDE.md §10 / bff-extensions.md).</b> In the BFF, beside the stamp's owner
/// (<see cref="CoreAncestorResolver"/>, ADR-002 WP-1: one server owner per invariant — derivation and refresh are the
/// same invariant). It adds no package, endpoint or interface (ADR-010: a concrete singleton over the already-registered
/// <see cref="IGenericEntityService"/>), and no plugin (ADR-002 / D-1). It writes APP-ONLY: the stamp is a server-owned
/// invariant, like <see cref="CoreAncestorResolver"/>'s own reads.</para>
/// </remarks>
public sealed class CoreAncestorRestamper
{
    /// <summary>How many levels of children a cascade follows (an event under a communication is level 1).</summary>
    internal const int MaxCascadeDepth = 4;

    /// <summary>Rows per page when the children of one intermediate are listed.</summary>
    internal const int ChildPageSize = 500;

    /// <summary>
    /// The most children of ONE intermediate in ONE child table a cascade re-stamps in a request. Live spaarkedev1
    /// (read-only, 2026-10-02) has at most TWO stamped children under any one intermediate, so this is far above any
    /// real shape (escalation trigger 2 did not fire). Past it the report is TRUNCATED and the reconciliation job
    /// finishes the rest; the storage resolver refuses a stale child meanwhile (never misfiles it).
    /// </summary>
    internal const int MaxChildrenPerSource = 5000;

    internal const string LogPrefix = "[CORE-ANCESTOR-RESTAMP]";

    /// <summary>
    /// The page size used when listing one intermediate's children (<see cref="ChildPageSize"/>). Settable only so a test
    /// can reach the <see cref="ChildrenPerSourceBound"/> truncation without thousands of rows.
    /// </summary>
    internal int ChildPageSizeInUse { get; init; } = ChildPageSize;

    /// <summary>
    /// The per-intermediate, per-table bound (<see cref="MaxChildrenPerSource"/>): past it the report is TRUNCATED, never a
    /// silent prefix (task 156 escalation trigger 2's mitigation). Settable only for the same test.
    /// </summary>
    internal int ChildrenPerSourceBound { get; init; } = MaxChildrenPerSource;

    private readonly IGenericEntityService _entityService;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly ILogger<CoreAncestorRestamper> _logger;

    public CoreAncestorRestamper(
        IGenericEntityService entityService,
        CoreAncestorResolver coreAncestors,
        ILogger<CoreAncestorRestamper> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The cascade a BFF write path calls AFTER it wrote <paramref name="writtenColumns"/> on a record: re-stamp the record
    /// itself when the write changed what it is filed under, and re-stamp its children when the write changed its root.
    /// Does nothing (and reads nothing) when the write touched neither.
    /// </summary>
    /// <remarks>
    /// Never throws for a child that fails — the caller's own write stands, the failure is in the report and the log, and
    /// the reconciliation job repairs it. Only a CALLER cancellation propagates.
    /// </remarks>
    /// <param name="writtenColumns">
    /// The keys the caller wrote, in any of the shapes the BFF writes use: a logical name (<c>sprk_matter</c>), a Web API
    /// bind (<c>sprk_Matter@odata.bind</c>) or a lookup value (<c>_sprk_matter_value</c>).
    /// </param>
    public async Task<RestampReport> AfterWriteAsync(
        string entityLogicalName,
        Guid recordId,
        IEnumerable<string> writtenColumns,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writtenColumns);

        var report = new RestampReport();
        if (string.IsNullOrWhiteSpace(entityLogicalName) || recordId == Guid.Empty)
        {
            return report;
        }

        var entity = entityLogicalName.Trim().ToLowerInvariant();
        var (touchesOwnSource, touchesRoot) = Touches(entity, NormalizeColumns(writtenColumns));

        if (touchesOwnSource)
        {
            report.Merge(await RestampChildAsync(entity, recordId, ct).ConfigureAwait(false));
        }
        else if (touchesRoot && CoreAncestorResolver.IsStampedChildEntity(entity))
        {
            // The write set this record's OWN root columns, and the record is one that can carry a COPY (an event, a
            // communication, an analysis). When it is filed under another record, those columns are a copy of THAT
            // record's root, not the writer's to set (a field-mapping push or a generic update writing
            // sprk_regardingmatter on an event filed under a communication): re-derive the record from its source FIRST,
            // so the cascade below carries the source's root to the children — never the hand-written value, which the
            // reconciliation job would only revert (and cascade again) a cycle later. A record whose root columns are its
            // own (a direct link, or filed under nothing) is skipped here and cascades its written value below; an
            // orphan-shaped pair is not cleared on this path (the write may be the user's direct choice after a clear).
            report.Merge(await RestampChildAsync(entity, recordId, ct, clearOrphans: false).ConfigureAwait(false));
        }

        var selfFailed = report.Failures.Any(f => f.Entity == entity && f.Id == recordId);
        if (touchesRoot && !report.ChangedRecords.Contains((entity, recordId)) && !selfFailed)
        {
            // The record's own stamp did not change (or it carries none), but its root columns were written directly —
            // its children's copies may now be stale. (When the record's own re-derivation FAILED, its root is not
            // known to be its own, so nothing is cascaded from it: the failure is reported and the job repairs both.)
            report.Merge(await RestampChildrenOfAsync(entity, recordId, ct).ConfigureAwait(false));
        }

        if (touchesOwnSource || touchesRoot)
        {
            Log(report, $"after a write to {entity} {recordId}");
        }

        return report;
    }

    /// <summary>
    /// Whether a write of <paramref name="writtenColumns"/> to <paramref name="entityLogicalName"/> can move a stamp — it
    /// changes what the record is filed under (a stamped child's source or pair) or its root (an intermediate with
    /// children). A caller that constructs its dependencies inline asks this first, so a write that cannot move a stamp
    /// costs nothing at all.
    /// </summary>
    public static bool WriteCanMoveAStamp(string entityLogicalName, IEnumerable<string> writtenColumns)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || writtenColumns is null)
        {
            return false;
        }

        var (ownSource, root) = Touches(entityLogicalName.Trim().ToLowerInvariant(), NormalizeColumns(writtenColumns));
        return ownSource || root;
    }

    private static (bool OwnSource, bool Root) Touches(string entity, IReadOnlySet<string> written)
    {
        var ownSource = CoreAncestorResolver.StampSourceColumns.TryGetValue(entity, out var sources)
            && (sources.Any(s => written.Contains(s.Column))
                || written.Contains(CoreAncestorResolver.RegardingRecordIdColumn)
                || written.Contains(CoreAncestorResolver.RegardingRecordTypeColumn));

        var root = CoreAncestorResolver.IntermediateRootColumns.TryGetValue(entity, out var roots)
            && roots.Any(r => written.Contains(r.Column))
            && ChildColumnsOf(entity).Count > 0;

        return (ownSource, root);
    }

    /// <summary>Re-stamp every child whose stamp is copied from <paramref name="intermediate"/> <paramref name="intermediateId"/>.</summary>
    public async Task<RestampReport> RestampChildrenOfAsync(
        string intermediate, Guid intermediateId, CancellationToken ct = default)
    {
        var report = new RestampReport();
        await CascadeAsync(intermediate, intermediateId, depth: 0, report, ct)
            .ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Repair ONE child from its source's CURRENT root, and cascade to its own children when its stamp changed.
    /// </summary>
    /// <remarks>
    /// <para>A child whose source column was CLEARED (task 051 F-051-6: a native form clear of
    /// <c>sprk_regardingcommunication</c>) keeps its old copy and its pair still names the old intermediate. When the
    /// pair names an intermediate this table can be filed under and the row no longer carries it — by its TYPE, or, when
    /// the pair has no type (live: <c>sprk_recordtype_ref</c> has no row for a communication or an agreement, so the
    /// client writes the pair id alone), by finding the pair's id among those intermediates
    /// (<see cref="FindClearedSourceAsync"/>) — each stamp column that still EQUALS that record's current root is an
    /// orphaned copy and is CLEARED — fail closed: the record stops inheriting the old root's access. A stamp that does
    /// NOT match it may be a direct choice made after the clear, so it is left alone and reported.</para>
    /// <para>A missing child is not an error (it was deleted); an unreadable one is a failure in the report.</para>
    /// </remarks>
    /// <param name="clearOrphans">
    /// <see langword="false"/> on the after-write path for a record whose own root columns were just written: a row filed
    /// under nothing is then taken at its word (the write may be the user's direct choice), and only the job clears orphans.
    /// </param>
    public async Task<RestampReport> RestampChildAsync(
        string childEntity, Guid childId, CancellationToken ct = default, bool clearOrphans = true)
    {
        var report = new RestampReport();
        var entity = (childEntity ?? string.Empty).Trim().ToLowerInvariant();

        if (!CoreAncestorResolver.StampSourceColumns.TryGetValue(entity, out var sources) || childId == Guid.Empty)
        {
            return report;
        }

        IReadOnlySet<string> host;
        try
        {
            host = await _coreAncestors.ProbeColumnsAsync(entity, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            report.Fail(entity, childId, $"column metadata unavailable: {ex.Message}");
            return report;
        }

        Entity row;
        try
        {
            row = await _entityService
                .RetrieveAsync(entity, childId, ChildColumns(entity, host, withPairType: true), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(ex))
        {
            report.Skipped++;
            return report;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            report.Fail(entity, childId, $"could not be read: {ex.Message}");
            return report;
        }

        report.Examined++;

        var decision = CoreAncestorResolver.ClassifyStampSource(
            entity, row, CoreAncestorResolver.PartyRegardingColumnNames(entity));

        Dictionary<string, object>? fields;
        switch (decision.Kind)
        {
            case StampSourceKind.Source:
                var root = await _coreAncestors
                    .ResolveStampsAsync(decision.Source!.Intermediate, decision.Source.Id, ct)
                    .ConfigureAwait(false);
                if (!root.Succeeded)
                {
                    report.Fail(entity, childId,
                        $"the root of its {decision.Source.Intermediate} could not be derived: {root.Error}");
                    return report;
                }

                fields = PlanStamp(
                    row, CoreAncestorResolver.CarriableRootTypes(decision.Source.Intermediate), root.Stamps, host);
                break;

            case StampSourceKind.NotFiledUnderAnIntermediate when clearOrphans:
                fields = await PlanOrphanClearAsync(row, entity, host, report, ct).ConfigureAwait(false);
                break;

            default:
                // A direct link (the user chose the root), an ambiguous or an inconsistent row — or, on the after-write
                // path (clearOrphans false), a row filed under nothing: nothing here is a copy this component may
                // overwrite. The storage resolver refuses the ambiguous / inconsistent shapes.
                report.Skipped++;
                return report;
        }

        if (fields is null)
        {
            return report;
        }

        if (!await TryWriteAsync(entity, childId, fields, report, ct).ConfigureAwait(false))
        {
            return report;
        }

        if (CoreAncestorResolver.IntermediateRootColumns.ContainsKey(entity) && ChildColumnsOf(entity).Count > 0)
        {
            await CascadeAsync(entity, childId, depth: 1, report, ct).ConfigureAwait(false);
        }

        return report;
    }

    private async Task CascadeAsync(
        string intermediate,
        Guid intermediateId,
        int depth,
        RestampReport report,
        CancellationToken ct)
    {
        var childColumns = ChildColumnsOf(intermediate);
        if (childColumns.Count == 0)
        {
            return;
        }

        if (depth >= MaxCascadeDepth)
        {
            report.Truncated = true;
            _logger.LogWarning(
                "{Prefix} Cascade stopped at depth {Depth} below {Entity} {Id}; the reconciliation job finishes it.",
                LogPrefix, depth, intermediate, intermediateId);
            return;
        }

        var root = await _coreAncestors.ResolveStampsAsync(intermediate, intermediateId, ct).ConfigureAwait(false);
        if (!root.Succeeded)
        {
            report.Fail(intermediate, intermediateId, $"its root could not be derived: {root.Error}");
            return;
        }

        var carriable = CoreAncestorResolver.CarriableRootTypes(intermediate);

        foreach (var (childTable, column) in childColumns)
        {
            IReadOnlySet<string> host;
            try
            {
                host = await _coreAncestors.ProbeColumnsAsync(childTable, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                report.Fail(childTable, Guid.Empty, $"column metadata unavailable: {ex.Message}");
                continue;
            }

            if (!host.Contains(column))
            {
                continue; // this org's table has no such column, so nothing can be filed under the intermediate by it
            }

            var parties = CoreAncestorResolver.PartyRegardingColumnNames(childTable);
            var query = new QueryExpression(childTable)
            {
                ColumnSet = new ColumnSet(ChildColumns(childTable, host, withPairType: false)),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression(column, ConditionOperator.Equal, intermediateId) },
                },
                Orders = { new OrderExpression(childTable + "id", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = ChildPageSizeInUse, PageNumber = 1 },
            };

            var listed = 0;
            while (true)
            {
                EntityCollection page;
                try
                {
                    page = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!IsCallerCancellation(ex, ct))
                {
                    report.Fail(childTable, Guid.Empty,
                        $"the {childTable} rows filed under {intermediate} {intermediateId} could not be listed: {ex.Message}");
                    break;
                }

                foreach (var child in page.Entities)
                {
                    listed++;
                    report.Examined++;

                    var decision = CoreAncestorResolver.ClassifyStampSource(childTable, child, parties);
                    if (decision.Kind != StampSourceKind.Source
                        || decision.Source!.Id != intermediateId
                        || !string.Equals(decision.Source.Column, column, StringComparison.OrdinalIgnoreCase))
                    {
                        // Its copy comes from somewhere else (this intermediate is a carrier), the user chose its root
                        // directly, or the row disagrees with itself — not this cascade's to change.
                        report.Skipped++;
                        continue;
                    }

                    var fields = PlanStamp(child, carriable, root.Stamps, host);
                    if (fields is null)
                    {
                        continue; // already equal to the root
                    }

                    if (await TryWriteAsync(childTable, child.Id, fields, report, ct).ConfigureAwait(false)
                        && ChildColumnsOf(childTable).Count > 0)
                    {
                        // Transitive: this child is itself an intermediate, and its copy just changed.
                        await CascadeAsync(childTable, child.Id, depth + 1, report, ct).ConfigureAwait(false);
                    }
                }

                if (!page.MoreRecords)
                {
                    break;
                }

                if (listed >= ChildrenPerSourceBound)
                {
                    report.Truncated = true;
                    _logger.LogWarning(
                        "{Prefix} {Entity} {Id} has more than {Max} {Child} rows filed under it; the rest are left to the "
                        + "reconciliation job (the storage resolver refuses a stale copy meanwhile).",
                        LogPrefix, intermediate, intermediateId, ChildrenPerSourceBound, childTable);
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }

    /// <summary>
    /// The stamp columns to write so that <paramref name="row"/>'s copy equals <paramref name="sourceRoot"/> — for the root
    /// types the source can carry, on columns the table has — or <see langword="null"/> when it already does. A value is
    /// SET to the source's root, or CLEARED (<see cref="DBNull.Value"/>, the <see cref="IGenericEntityService.UpdateAsync"/>
    /// clear sentinel) when the source no longer names a root of that type.
    /// </summary>
    internal static Dictionary<string, object>? PlanStamp(
        Entity row,
        IReadOnlySet<string> carriableRootTypes,
        IReadOnlyList<CoreAncestorStamp> sourceRoot,
        IReadOnlySet<string> hostColumns)
    {
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootType in carriableRootTypes)
        {
            var column = CoreAncestorResolver.StampColumnFor(rootType);
            if (!hostColumns.Contains(column))
            {
                continue;
            }

            var desired = sourceRoot.FirstOrDefault(s =>
                string.Equals(s.EntityType, rootType, StringComparison.OrdinalIgnoreCase))?.RecordId;
            var current = row.GetAttributeValue<EntityReference>(column)?.Id;
            if (current == Guid.Empty)
            {
                current = null;
            }

            if (desired == current)
            {
                continue;
            }

            fields[column] = desired is { } id ? new EntityReference(rootType, id) : DBNull.Value;
        }

        return fields.Count > 0 ? fields : null;
    }

    /// <summary>
    /// F-051-6: the row is filed under no intermediate, but its pair names one — the typed source column was cleared and
    /// the copy left behind. Clear each copy that still equals that intermediate's current root.
    /// </summary>
    private async Task<Dictionary<string, object>?> PlanOrphanClearAsync(
        Entity row, string entity, IReadOnlySet<string> host, RestampReport report, CancellationToken ct)
    {
        var cleared = await FindClearedSourceAsync(entity, row, recordTypes: null, ct).ConfigureAwait(false);
        switch (cleared.Outcome)
        {
            case ClearedSourceOutcome.Found:
                break;

            case ClearedSourceOutcome.Gone:
                // The record the pair names no longer exists, so the copy cannot be shown to be its copy: left alone
                // (interpretation xvi) — and counted, never a failure that repeats on every run.
                report.Skipped++;
                _logger.LogWarning(
                    "{Prefix} {Entity} {Id}: its pair names {PairId}, which no intermediate holds any more; its stamp is left "
                    + "alone (it cannot be shown to be a copy).", LogPrefix, entity, row.Id, cleared.PairId);
                return null;

            case ClearedSourceOutcome.Unreadable:
                report.Fail(entity, row.Id, cleared.Error ?? "the record its pair names could not be read");
                return null;

            default:
                return null;
        }

        var root = await _coreAncestors.ResolveStampsAsync(cleared.Intermediate!, cleared.PairId, ct).ConfigureAwait(false);
        if (!root.Succeeded)
        {
            // Unreadable: the copy cannot be shown to be its copy, so it is left alone — and reported.
            report.Fail(entity, row.Id, $"its cleared {cleared.Intermediate}'s root could not be derived: {root.Error}");
            return null;
        }

        return PlanOrphanClear(row, root.Stamps, host, cleared.PairId);
    }

    /// <summary>
    /// For a row filed under NO intermediate (no typed source column set): which intermediate its pair still names — the
    /// record whose source column was cleared (task 051 F-051-6) — or why there is none. The ONE lookup the restamper
    /// and the reconciliation job share.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>No pair, a pair that is not a GUID, or a pair equal to a root or party lookup the row carries (a direct link,
    /// or a person) → <see cref="ClearedSourceOutcome.NotAnOrphan"/>.</item>
    /// <item>A TYPED pair → its <c>sprk_recordtype_ref</c> names the entity; only an intermediate THIS table is filed
    /// under by a typed column counts (anything else is <see cref="ClearedSourceOutcome.NotAnOrphan"/>).</item>
    /// <item>An UNTYPED pair — the live shape for a communication or an agreement, which have no
    /// <c>sprk_recordtype_ref</c> row (spaarkedev1, read-only, 2026-10-02: 14 rows, neither among them), so the client
    /// regarding writer sets the id alone — is looked for in each intermediate this table is filed under. Exactly one hit
    /// is the cleared record; none is <see cref="ClearedSourceOutcome.Gone"/>; two is
    /// <see cref="ClearedSourceOutcome.Unreadable"/> (never a guess).</item>
    /// <item>The named record missing → <see cref="ClearedSourceOutcome.Gone"/>; unreadable →
    /// <see cref="ClearedSourceOutcome.Unreadable"/>.</item>
    /// </list>
    /// A row that carries no pair at all (TaskActionCore's events and the live event <c>edfef460</c>) leaves no trace of
    /// what was cleared and cannot be found here — see the task 156 note.
    /// </remarks>
    /// <param name="recordTypes">The <c>sprk_recordtype_ref</c> rows (id → logical name) when the caller already holds
    /// them (the job); <see langword="null"/> reads the one row a typed pair names.</param>
    internal async Task<ClearedSource> FindClearedSourceAsync(
        string childEntity,
        Entity row,
        IReadOnlyDictionary<Guid, string>? recordTypes,
        CancellationToken ct)
    {
        if (!CoreAncestorResolver.StampSourceColumns.TryGetValue(childEntity, out var sources))
        {
            return ClearedSource.NotAnOrphan;
        }

        var raw = row.GetAttributeValue<string>(CoreAncestorResolver.RegardingRecordIdColumn);
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw.Trim(), out var pairId) || pairId == Guid.Empty)
        {
            return ClearedSource.NotAnOrphan;
        }

        // The pair names something the row still carries: its direct root, or a person / organization.
        var carried = CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute)
            .Concat(CoreAncestorResolver.PartyRegardingColumnNames(childEntity))
            .Any(c => row.GetAttributeValue<EntityReference>(c)?.Id == pairId);
        if (carried)
        {
            return ClearedSource.NotAnOrphan;
        }

        var intermediates = sources.Select(s => s.Intermediate).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (row.GetAttributeValue<EntityReference>(CoreAncestorResolver.RegardingRecordTypeColumn) is { } typeRef
            && typeRef.Id != Guid.Empty)
        {
            string? pairEntity;
            if (recordTypes is not null)
            {
                pairEntity = recordTypes.TryGetValue(typeRef.Id, out var known) ? known : null;
            }
            else
            {
                try
                {
                    var typeRow = await _entityService
                        .RetrieveAsync("sprk_recordtype_ref", typeRef.Id, ["sprk_recordlogicalname"], ct)
                        .ConfigureAwait(false);
                    pairEntity = typeRow?.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim().ToLowerInvariant();
                }
                catch (Exception ex) when (!IsCallerCancellation(ex, ct))
                {
                    return ClearedSource.Unreadable(pairId, $"its regarding type could not be read: {ex.Message}");
                }
            }

            // Only an intermediate THIS table can be filed under by a typed column counts: that column is now empty.
            if (pairEntity is null || !intermediates.Contains(pairEntity, StringComparer.OrdinalIgnoreCase))
            {
                return ClearedSource.NotAnOrphan;
            }

            intermediates = [pairEntity];
        }

        var hits = new List<string>(1);
        foreach (var intermediate in intermediates)
        {
            try
            {
                // Existence only (no columns): the root is derived afterwards by the one derivation.
                await _entityService.RetrieveAsync(intermediate, pairId, [], ct).ConfigureAwait(false);
                hits.Add(intermediate);
            }
            catch (Exception ex) when (Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(ex))
            {
                // Not this table.
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                return ClearedSource.Unreadable(pairId, $"the {intermediate} its pair may name could not be read: {ex.Message}");
            }
        }

        return hits.Count switch
        {
            0 => ClearedSource.Gone(pairId),
            1 => ClearedSource.Found(hits[0], pairId),
            _ => ClearedSource.Unreadable(pairId,
                $"its pair id names a record in more than one table ({string.Join(", ", hits)}), so which one was cleared is not known"),
        };
    }

    /// <summary>
    /// The F-051-6 plan (the restamper and the reconciliation job both use it): CLEAR each stamp column that still EQUALS
    /// the cleared intermediate's current root — an orphaned copy. A stamp that differs is not shown to be its copy, so it
    /// is left alone. Returns <see langword="null"/> when there is nothing to clear.
    /// </summary>
    internal static Dictionary<string, object>? PlanOrphanClear(
        Entity row, IReadOnlyList<CoreAncestorStamp> clearedIntermediateRoot, IReadOnlySet<string> hostColumns, Guid pairId)
    {
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var stamp in clearedIntermediateRoot)
        {
            if (!hostColumns.Contains(stamp.LookupAttribute)
                || row.GetAttributeValue<EntityReference>(stamp.LookupAttribute)?.Id != stamp.RecordId
                || stamp.RecordId == pairId)
            {
                continue;
            }

            fields[stamp.LookupAttribute] = DBNull.Value;
        }

        return fields.Count > 0 ? fields : null;
    }

    private async Task<bool> TryWriteAsync(
        string entity, Guid id, Dictionary<string, object> fields, RestampReport report, CancellationToken ct)
    {
        try
        {
            await _entityService.UpdateAsync(entity, id, fields, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            // Reported, never thrown: the caller's own write stands and the reconciliation job repairs this child.
            report.Fail(entity, id, $"could not be re-stamped: {ex.Message}");
            return false;
        }

        report.Changed++;
        report.ChangedRecords.Add((entity, id));
        _logger.LogInformation(
            "{Prefix} Re-stamped {Entity} {Id}: {Columns}.",
            LogPrefix, entity, id, string.Join(", ", fields.Select(f =>
                $"{f.Key}={(f.Value is EntityReference r ? r.Id.ToString() : "(cleared)")}")));
        return true;
    }

    /// <summary>The columns a child row must carry to be classified and planned: sources, stamps, pair, parties.</summary>
    internal static string[] ChildColumns(string childTable, IReadOnlySet<string> host, bool withPairType)
    {
        var columns = new List<string>();
        columns.AddRange(CoreAncestorResolver.StampSourceColumns[childTable].Select(s => s.Column));
        columns.AddRange(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute));
        columns.Add(CoreAncestorResolver.RegardingRecordIdColumn);
        if (withPairType)
        {
            columns.Add(CoreAncestorResolver.RegardingRecordTypeColumn);
        }

        columns.AddRange(CoreAncestorResolver.PartyRegardingColumnNames(childTable));
        return columns.Where(host.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Every (child table, column) by which a record can be filed under <paramref name="intermediate"/>.</summary>
    internal static IReadOnlyList<(string ChildTable, string Column)> ChildColumnsOf(string intermediate) =>
        CoreAncestorResolver.StampSourceColumns
            .SelectMany(t => t.Value
                .Where(s => string.Equals(s.Intermediate, intermediate, StringComparison.OrdinalIgnoreCase))
                .Select(s => (t.Key, s.Column)))
            .ToArray();

    /// <summary>
    /// The <c>sprk_document</c> columns an <see cref="UpdateDocumentRequest"/> writes that can change the document's root
    /// (the request model's association lookups — <c>DataverseServiceClientImpl.UpdateDocumentAsync</c> maps them). The
    /// request cannot clear a lookup (a null property is skipped), so only a set value is a write.
    /// </summary>
    public static IReadOnlyList<string> DocumentColumnsWritten(UpdateDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var columns = new List<string>(3);
        if (request.MatterLookup.HasValue) columns.Add("sprk_matter");
        if (request.ProjectLookup.HasValue) columns.Add("sprk_project");
        if (request.WorkAssignmentLookup.HasValue) columns.Add("sprk_workassignment");
        return columns;
    }

    /// <summary>
    /// The logical column names behind a write's keys: <c>sprk_Matter@odata.bind</c> → <c>sprk_matter</c>,
    /// <c>_sprk_matter_value</c> → <c>sprk_matter</c>, any case → lower.
    /// </summary>
    internal static IReadOnlySet<string> NormalizeColumns(IEnumerable<string> keys)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var name = key.Trim();
            var at = name.IndexOf('@');
            if (at >= 0)
            {
                name = name[..at];
            }

            if (name.StartsWith('_') && name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
            {
                name = name[1..^"_value".Length];
            }

            set.Add(name.ToLowerInvariant());
        }

        return set;
    }

    private void Log(RestampReport report, string context)
    {
        if (report.Failures.Count > 0 || report.Truncated)
        {
            _logger.LogWarning(
                "{Prefix} Cascade INCOMPLETE {Context}: examined={Examined} changed={Changed} skipped={Skipped} "
                + "failed={Failed} truncated={Truncated} [{Failures}]. The record's own write stands; the reconciliation "
                + "job repairs the rest, and the storage resolver refuses a stale copy until it does.",
                LogPrefix, context, report.Examined, report.Changed, report.Skipped, report.Failures.Count,
                report.Truncated, string.Join("; ", report.Failures.Select(f => $"{f.Entity} {f.Id}: {f.Reason}")));
        }
        else if (report.Changed > 0)
        {
            _logger.LogInformation(
                "{Prefix} Cascade {Context}: examined={Examined} changed={Changed} skipped={Skipped}.",
                LogPrefix, context, report.Examined, report.Changed, report.Skipped);
        }
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;
}

/// <summary>What one re-stamp operation did. A failure or truncation is never reported as "complete".</summary>
public sealed class RestampReport
{
    /// <summary>Child rows looked at.</summary>
    public int Examined { get; internal set; }

    /// <summary>Child rows whose stamp was written.</summary>
    public int Changed { get; internal set; }

    /// <summary>Rows not re-stamped because their stamp is not a copy this operation owns (or the row is gone).</summary>
    public int Skipped { get; internal set; }

    /// <summary>True when a bound stopped the cascade before every child was seen.</summary>
    public bool Truncated { get; internal set; }

    /// <summary>Rows (or scans, with an empty id) that failed — each one left for the reconciliation job.</summary>
    public List<RestampFailure> Failures { get; } = [];

    /// <summary>Every (entity, id) whose stamp was written.</summary>
    public HashSet<(string Entity, Guid Id)> ChangedRecords { get; } = [];

    /// <summary>True when every child was seen and every write succeeded.</summary>
    public bool Complete => Failures.Count == 0 && !Truncated;

    internal void Fail(string entity, Guid id, string reason) => Failures.Add(new RestampFailure(entity, id, reason));

    internal void Merge(RestampReport other)
    {
        Examined += other.Examined;
        Changed += other.Changed;
        Skipped += other.Skipped;
        Truncated |= other.Truncated;
        Failures.AddRange(other.Failures);
        ChangedRecords.UnionWith(other.ChangedRecords);
    }
}

/// <summary>One child (or one scan, when <see cref="Id"/> is empty) a re-stamp could not complete.</summary>
public sealed record RestampFailure(string Entity, Guid Id, string Reason);

/// <summary>What <see cref="CoreAncestorRestamper.FindClearedSourceAsync"/> found for a row filed under nothing.</summary>
internal enum ClearedSourceOutcome
{
    /// <summary>The row's pair names no intermediate it could have been filed under: nothing was cleared.</summary>
    NotAnOrphan,

    /// <summary>The pair names <see cref="ClearedSource.Intermediate"/>, which exists: its typed column was cleared.</summary>
    Found,

    /// <summary>The pair names a record no intermediate table holds any more (deleted): nothing to compare the copy with.</summary>
    Gone,

    /// <summary>Whether (or which) record the pair names could not be read: not guessed.</summary>
    Unreadable,
}

/// <summary>The outcome of <see cref="CoreAncestorRestamper.FindClearedSourceAsync"/>.</summary>
internal sealed record ClearedSource(ClearedSourceOutcome Outcome, string? Intermediate, Guid PairId, string? Error)
{
    public static readonly ClearedSource NotAnOrphan = new(ClearedSourceOutcome.NotAnOrphan, null, Guid.Empty, null);

    public static ClearedSource Found(string intermediate, Guid pairId) => new(ClearedSourceOutcome.Found, intermediate, pairId, null);

    public static ClearedSource Gone(Guid pairId) => new(ClearedSourceOutcome.Gone, null, pairId, null);

    public static ClearedSource Unreadable(Guid pairId, string error) => new(ClearedSourceOutcome.Unreadable, null, pairId, error);
}
