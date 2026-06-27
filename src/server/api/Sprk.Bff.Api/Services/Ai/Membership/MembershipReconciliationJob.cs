// R3 Part 1 Phase 2 — Task 085 (2026-06-22)
// MembershipReconciliationJob — real-logic nightly reconciliation pass.
//
// Per spec FR-2P2.7 + Phase 2 owner decision (2026-06-20) + task 080
// inventory finding §3A: the 8 Q4 `sprk_assigned*` lookups on `sprk_matter`
// (plus `sprk_task` + `sprk_opportunity` — see inventory §3D / §3E) are
// NOT mutated by any BFF endpoint. They're exclusively maker-portal /
// Power Automate / plugin edits. Real-time event publishing (tasks 081-
// 083) therefore covers only a tiny subset of identity-Lookup mutations
// for the FR-2P2.5 entity set; THIS nightly recon job is the load-bearing
// path for keeping the junction table fresh against the source-of-truth
// Lookups on those entities.
//
// Algorithm (per task 085 POML + spec FR-2P2.7):
//
//   For each entity type in MembershipReconciliationOptions.EntityTypes:
//     1. DISCOVER its identity-Lookup fields via
//        IMembershipFieldDiscoveryService.DiscoverAsync(entityType) →
//        returns descriptors {Field, Role, IdentityType, TargetTable, …}.
//        Discovery is cached (60-min TTL) so the inner discovery cost is
//        amortized across recon runs (and across the live resolver +
//        admin endpoints in the same process).
//     2. SCAN parents — query active rows of `entityType` projecting only
//        the discovered Lookup attributes. Paginated via
//        IGenericEntityService.RetrieveMultipleAsync(QueryExpression) +
//        PagingInfo.PageNumber / PageSize / Cookie. Honors CancellationToken
//        between pages (NFR-07 30s drain ceiling).
//     3. For each (parent record, lookup field, populated identity GUID)
//        triple, synthesize a MembershipChangedEvent with mutationType=
//        Updated and dispatch to IMembershipJunctionUpdater.HandleAsync
//        (Scoped — resolved via IServiceScopeFactory.CreateScope() per
//        execution, matching the PlaybookSchedulerJob lifetime pattern).
//        Updated-mode is intentional: the handler's natural-key probe
//        (Retrieve → Update OR Create) self-heals both the "missing row"
//        and "stale role" cases idempotently per FR-2P2.4 — and the unit
//        test `HandleAsync_Updated_CreatesRowWhenMissing` already locks
//        this behavior.
//     4. SCAN orphans — query `sprk_userentityassociation` rows whose
//        {entityLogicalName, sourceField} match a discovered descriptor
//        BUT whose parent row's source Lookup is now null/changed. Build
//        the expected set from step 2; the difference is the orphan set.
//        For each orphan, synthesize a MembershipChangedEvent with
//        mutationType=Removed and dispatch.
//
// Idempotency (FR-2P2.4): re-running the recon for the same drift state
// is a no-op against the junction — the handler's RetrieveByAlternateKey
// + Update path overwrites the lastSyncedOn timestamp + role but does NOT
// create duplicate rows. Locked by MembershipJunctionUpdaterTests
// `HandleAsync_DuplicateDelivery_IsIdempotent`.
//
// Independence from Service Bus topic deploy (task 071): the recon job
// does NOT publish to the topic. It dispatches directly to the
// IMembershipJunctionUpdater handler (the same handler the topic
// subscription host uses, per task 084's IMembershipJunctionUpdater
// rationale — "Cross-cutting reuse: task 085's MembershipReconciliationJob
// will reuse the same handler by synthesizing MembershipChangedEvent
// payloads from source-of-truth lookup scans"). The recon job is
// therefore safe to ship + enable by default before 071's topic is
// operator-deployed. This is the explicit design intent of task 084's
// IMembershipJunctionUpdater contract.
//
// Lifetime pattern (mirrors PlaybookSchedulerJob — task 023):
//   - This class is registered as Singleton.
//   - Per-tick work resolves IMembershipJunctionUpdater +
//     IMembershipFieldDiscoveryService + IGenericEntityService from a
//     fresh scope created via IServiceScopeFactory.CreateScope(). The
//     handler is Scoped per MembershipModule (matching IDataverseService);
//     the discovery + entity services are Singleton but the scope is
//     still cheap to create and gives us correct disposal semantics for
//     any future Scoped collaborators we accumulate.
//
// Cancellation (NFR-07): the token is checked between pages, between
// entity types, and propagated to all async hops. Host shutdown drains
// within 30s.
//
// Result reporting (FR-2P2.7 + JobRunResult.ProcessedItems):
//   - ProcessedItems = total junction rows touched (added + removed +
//     verified).
//   - ResultJson = per-entity-type breakdown including: discoveredFields,
//     parentRowsScanned, verified, added, removed, errors. Mirrors
//     PlaybookSchedulerJob.SerializeChildren shape so admin UI can render
//     uniformly.
//   - Per-parent-row errors are logged + counted in ResultJson but do
//     NOT fail the whole run (per POML step 3 — "log + continue").
//   - Discovery failures for one entity type DO fail that entity's
//     contribution (recorded in ResultJson.errors) but the recon
//     continues to the next entity type.
//
// ADR-013 (placement under Services/Ai/Membership/); ADR-010 (Singleton +
// IServiceScopeFactory.CreateScope per execution); ADR-001 (pure in-process
// scheduling; no Azure Functions); bff-extensions.md §A pre-merge
// checklist applied in notes/bff-publish-size-task085.md.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.7,
//            FR-2.7, AC-2.3; design.md Part 1 Phase 2 § "Reconciliation
//            job"; projects/spaarke-platform-foundations-r3/notes/
//            event-source-inventory.md §3A / §3D / §3E (load-bearing-path
//            rationale).

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Real-logic nightly reconciliation job for the
/// <c>sprk_userentityassociation</c> junction table (FR-2P2.7). For each
/// configured entity type:
/// <list type="number">
///   <item>Discover identity-Lookup fields via
///     <see cref="IMembershipFieldDiscoveryService"/>.</item>
///   <item>Scan parent rows (paginated, cancellation-honoring) and
///     synthesize Updated events (idempotent upsert).</item>
///   <item>Scan junction rows for the same (entityLogicalName, sourceField)
///     keys and synthesize Removed events for any rows whose source
///     Lookup is now empty/changed.</item>
/// </list>
/// Reuses <see cref="IMembershipJunctionUpdater"/> (task 084) for the
/// write path — same idempotency contract, same handler, no duplicated
/// upsert logic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Independence from task 071's Service Bus topic deploy</b>: this job
/// dispatches directly to <see cref="IMembershipJunctionUpdater"/>; it
/// does NOT publish to the topic. Safe to enable by default before 071
/// is operator-deployed.
/// </para>
/// <para>
/// <b>Load-bearing path</b>: per the task 080 inventory, the 8 Q4
/// <c>sprk_assigned*</c> Lookups on <c>sprk_matter</c> + all
/// <c>sprk_task</c> + <c>sprk_opportunity</c> identity-Lookup mutations
/// flow through maker-portal / Power Automate / plugins — NOT through
/// BFF endpoints. This recon job is the freshness mechanism for those
/// fields (24-hour max staleness per spec).
/// </para>
/// </remarks>
public sealed class MembershipReconciliationJob : IScheduledJob
{
    /// <summary>
    /// Canonical <see cref="IScheduledJob.JobId"/> — matches the seeded
    /// <c>sprk_backgroundjob.sprk_jobid</c> row. Stable contract; do not
    /// rename without coordinating the seed in
    /// <c>SchedulingModule.SeedMembershipReconciliationJob</c>.
    /// </summary>
    public const string JobIdConstant = "membership-reconciliation";

    /// <summary>
    /// Dataverse logical name of the junction table — duplicates
    /// <see cref="MembershipJunctionUpdater.JunctionEntityLogicalName"/> to
    /// avoid forcing the orphan-scan code path through the handler's
    /// surface (we need to query the junction directly, not write to it,
    /// for orphan discovery).
    /// </summary>
    private const string JunctionEntityLogicalName = "sprk_userentityassociation";

    // Junction-row attribute logical names — mirrored from
    // MembershipJunctionUpdater so we can decode the orphan-scan
    // projection. Kept const here rather than adding to the handler's
    // public surface (the handler should not expose its column layout
    // beyond what HandleAsync needs).
    private const string JunctionAttrPersonId = "sprk_personid";
    private const string JunctionAttrPersonIdType = "sprk_personidtype";
    private const string JunctionAttrEntityLogicalName = "sprk_entitylogicalname";
    private const string JunctionAttrEntityRecordId = "sprk_entityrecordid";
    private const string JunctionAttrSourceField = "sprk_sourcefield";
    private const string JunctionAttrRole = "sprk_role";

    // Writer options for ResultJson — camelCase + omit-nulls to match the
    // PlaybookSchedulerJob convention (admin UI surface expects camelCase
    // field names; nulls dropped to keep the payload compact).
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<MembershipReconciliationOptions> _options;
    private readonly ILogger<MembershipReconciliationJob> _logger;

    public MembershipReconciliationJob(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<MembershipReconciliationOptions> options,
        ILogger<MembershipReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Membership Junction Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Nightly reconciliation of sprk_userentityassociation junction rows against " +
        "source-of-truth identity Lookups on configured entities (FR-2P2.7). " +
        "Reuses task 084's IMembershipJunctionUpdater handler. Q2 backstop for " +
        "maker-portal-only mutation paths (sprk_assigned* Lookups).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();
        var perEntityResults = new List<EntityReconciliationResult>();
        var totalProcessed = 0;

        try
        {
            _logger.LogInformation(
                "MembershipReconciliationJob tick started — correlationId={CorrelationId} runId={RunId}",
                context.CorrelationId, context.RunId);

            var opts = _options.CurrentValue;
            var entityTypes = (opts.EntityTypes ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (entityTypes.Count == 0)
            {
                _logger.LogWarning(
                    "MembershipReconciliationJob has no entity types configured (Membership:Reconciliation:EntityTypes is empty) — skipping run");
                sw.Stop();
                return new JobRunResult(
                    Success: true,
                    ErrorMessage: null,
                    ProcessedItems: 0,
                    Duration: sw.Elapsed,
                    ResultJson: SerializeResult(perEntityResults));
            }

            // Fresh scope per execution — mirrors PlaybookSchedulerJob pattern
            // (Singleton-with-Scoped-deps). All scope-bound services resolved
            // up-front; the recon loop reuses them across all entity types
            // and pages so we do NOT thrash scope construction.
            using var scope = _scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<IMembershipFieldDiscoveryService>();
            var junctionUpdater = scope.ServiceProvider.GetRequiredService<IMembershipJunctionUpdater>();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();

            foreach (var entityType in entityTypes)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "MembershipReconciliationJob cancellation observed between entity types — returning partial result (entitiesProcessed={Count})",
                        perEntityResults.Count);
                    break;
                }

                var entityResult = await ReconcileEntityTypeAsync(
                    entityType,
                    discovery,
                    entityService,
                    junctionUpdater,
                    opts,
                    context.CorrelationId,
                    cancellationToken).ConfigureAwait(false);

                perEntityResults.Add(entityResult);
                totalProcessed += entityResult.Verified + entityResult.Added + entityResult.Removed;
            }

            sw.Stop();

            _logger.LogInformation(
                "MembershipReconciliationJob tick completed — entitiesProcessed={Total} verified={Verified} added={Added} removed={Removed} errors={Errors} duration={DurationMs}ms correlationId={CorrelationId}",
                perEntityResults.Count,
                perEntityResults.Sum(r => r.Verified),
                perEntityResults.Sum(r => r.Added),
                perEntityResults.Sum(r => r.Removed),
                perEntityResults.Sum(r => r.Errors),
                (long)sw.Elapsed.TotalMilliseconds,
                context.CorrelationId);

            return new JobRunResult(
                Success: true,
                ErrorMessage: null,
                ProcessedItems: totalProcessed,
                Duration: sw.Elapsed,
                ResultJson: SerializeResult(perEntityResults));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "MembershipReconciliationJob cancelled before completion — partial result (entitiesProcessed={Count}) correlationId={CorrelationId}",
                perEntityResults.Count, context.CorrelationId);
            return new JobRunResult(
                Success: false,
                ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                ProcessedItems: totalProcessed,
                Duration: sw.Elapsed,
                ResultJson: SerializeResult(perEntityResults));
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "MembershipReconciliationJob tick threw unexpectedly — correlationId={CorrelationId}",
                context.CorrelationId);
            return new JobRunResult(
                Success: false,
                ErrorMessage: ex.Message,
                ProcessedItems: totalProcessed,
                Duration: sw.Elapsed,
                ResultJson: SerializeResult(perEntityResults));
        }
    }

    /// <summary>
    /// Reconciles one entity type: discovers fields, scans parents +
    /// dispatches Updated events for every populated lookup, scans
    /// junction for orphans + dispatches Removed events. Per-row errors
    /// are caught + counted; one bad parent record does NOT fail the
    /// whole entity-type pass.
    /// </summary>
    internal async Task<EntityReconciliationResult> ReconcileEntityTypeAsync(
        string entityType,
        IMembershipFieldDiscoveryService discovery,
        IGenericEntityService entityService,
        IMembershipJunctionUpdater junctionUpdater,
        MembershipReconciliationOptions opts,
        string correlationId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        IReadOnlyList<MembershipDescriptor> descriptors;
        try
        {
            var discoveryResult = await discovery.DiscoverAsync(entityType, ct).ConfigureAwait(false);
            descriptors = discoveryResult.DiscoveredFields;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Entity-not-found or metadata fetch failure — record + continue.
            // (Operators may misconfigure EntityTypes to include a non-existent
            // entity; that's their lookout but should not break the whole job.)
            _logger.LogWarning(
                ex,
                "MembershipReconciliationJob discovery failed for entity={EntityType} — skipping this entity (correlationId={CorrelationId})",
                entityType, correlationId);
            sw.Stop();
            return new EntityReconciliationResult(
                EntityType: entityType,
                DiscoveredFields: 0,
                ParentRowsScanned: 0,
                Verified: 0,
                Added: 0,
                Removed: 0,
                Errors: 1,
                DurationMs: (long)sw.Elapsed.TotalMilliseconds,
                ErrorMessage: $"Discovery failed: {ex.Message}");
        }

        if (descriptors.Count == 0)
        {
            _logger.LogInformation(
                "MembershipReconciliationJob entity={EntityType} has no discovered identity-Lookup fields — nothing to reconcile (correlationId={CorrelationId})",
                entityType, correlationId);
            sw.Stop();
            return new EntityReconciliationResult(
                EntityType: entityType,
                DiscoveredFields: 0,
                ParentRowsScanned: 0,
                Verified: 0,
                Added: 0,
                Removed: 0,
                Errors: 0,
                DurationMs: (long)sw.Elapsed.TotalMilliseconds,
                ErrorMessage: null);
        }

        var fieldToDescriptor = descriptors.ToDictionary(
            d => d.Field,
            StringComparer.OrdinalIgnoreCase);

        // ── Step A: Scan parents → synthesize Updated events ─────────────
        var (parentRowsScanned, addedOrUpdated, parentErrors, expectedKeys) =
            await ScanParentsAndDispatchAsync(
                entityType,
                descriptors,
                entityService,
                junctionUpdater,
                opts,
                correlationId,
                ct).ConfigureAwait(false);

        // ── Step B: Scan junction → synthesize Removed events for orphans ─
        var (removedCount, orphanErrors) = await ScanOrphansAndDispatchAsync(
            entityType,
            fieldToDescriptor,
            entityService,
            junctionUpdater,
            opts,
            expectedKeys,
            correlationId,
            ct).ConfigureAwait(false);

        sw.Stop();

        var totalErrors = parentErrors + orphanErrors;

        _logger.LogInformation(
            "MembershipReconciliationJob entity={EntityType} done — fields={Fields} parents={Parents} verified={Verified} removed={Removed} errors={Errors} durationMs={DurationMs} correlationId={CorrelationId}",
            entityType, descriptors.Count, parentRowsScanned, addedOrUpdated, removedCount, totalErrors,
            (long)sw.Elapsed.TotalMilliseconds, correlationId);

        return new EntityReconciliationResult(
            EntityType: entityType,
            DiscoveredFields: descriptors.Count,
            ParentRowsScanned: parentRowsScanned,
            // We don't separately track "verified-still-correct" vs "newly-added"
            // because the junction handler is idempotent — an Updated mutationType
            // on an existing row is identical to a verification (overwrites
            // lastSyncedOn + role) and on a missing row creates it. From the
            // recon job's perspective, both count under `Verified` (which is the
            // operationally-useful "junction row touched" total). The separate
            // Removed count comes from the orphan scan.
            Verified: addedOrUpdated,
            Added: 0,
            Removed: removedCount,
            Errors: totalErrors,
            DurationMs: (long)sw.Elapsed.TotalMilliseconds,
            ErrorMessage: null);
    }

    /// <summary>
    /// Scan source-of-truth parent rows, paginated. For each (parent,
    /// descriptor, non-empty identity GUID) triple, synthesize an Updated
    /// event and dispatch. Returns (rowsScanned, eventsDispatched,
    /// errorCount, expectedJunctionKeys).
    /// </summary>
    private async Task<(int rowsScanned, int dispatched, int errors, HashSet<JunctionKey> expectedKeys)>
        ScanParentsAndDispatchAsync(
            string entityType,
            IReadOnlyList<MembershipDescriptor> descriptors,
            IGenericEntityService entityService,
            IMembershipJunctionUpdater junctionUpdater,
            MembershipReconciliationOptions opts,
            string correlationId,
            CancellationToken ct)
    {
        var rowsScanned = 0;
        var dispatched = 0;
        var errors = 0;
        var expectedKeys = new HashSet<JunctionKey>();

        // Columns we want from each parent: the row id (always projected by
        // QueryExpression) + each discovered lookup field. The id column is
        // implicitly returned via Entity.Id; we add the lookup columns.
        var columns = new ColumnSet(descriptors.Select(d => d.Field).ToArray());

        var query = new QueryExpression(entityType)
        {
            ColumnSet = columns,
            // Filter: at least one of the discovered lookups must be populated.
            // Without this we'd scan every row regardless of whether it has any
            // membership data at all — the recon is meaningless for rows with
            // ALL lookups null. The filter uses NotNull conditions OR'd together.
            Criteria = BuildAtLeastOneNotNullFilter(descriptors),
            PageInfo = new PagingInfo
            {
                PageNumber = 1,
                Count = Math.Max(1, opts.FetchPageSize),
                PagingCookie = null,
                ReturnTotalRecordCount = false,
            },
        };

        while (!ct.IsCancellationRequested)
        {
            EntityCollection page;
            try
            {
                page = await entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "MembershipReconciliationJob parent scan threw for entity={EntityType} pageNumber={PageNumber} — aborting parent scan for this entity (correlationId={CorrelationId})",
                    entityType, query.PageInfo.PageNumber, correlationId);
                errors++;
                break;
            }

            if (page is null || page.Entities is null || page.Entities.Count == 0)
            {
                break;
            }

            foreach (var parent in page.Entities)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                rowsScanned++;

                foreach (var descriptor in descriptors)
                {
                    var (personId, personIdType) = ReadLookupAsIdentity(parent, descriptor);
                    if (personId is null || personIdType is null)
                    {
                        continue;
                    }

                    var key = new JunctionKey(
                        PersonId: personId.Value,
                        PersonIdType: personIdType.Value,
                        EntityLogicalName: entityType,
                        EntityRecordId: parent.Id,
                        SourceField: descriptor.Field);

                    expectedKeys.Add(key);

                    var evt = BuildEvent(
                        key: key,
                        role: descriptor.Role,
                        mutationType: MembershipMutationType.Updated,
                        correlationId: correlationId);

                    try
                    {
                        await junctionUpdater.HandleAsync(evt, ct).ConfigureAwait(false);
                        dispatched++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogWarning(
                            ex,
                            "MembershipReconciliationJob handler failed for entity={EntityType} parent={ParentId} field={Field} — logging + continuing (correlationId={CorrelationId})",
                            entityType, parent.Id, descriptor.Field, correlationId);
                    }
                }
            }

            // Paging — if the SDK returned MoreRecords=true, advance.
            if (!page.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        return (rowsScanned, dispatched, errors, expectedKeys);
    }

    /// <summary>
    /// Scan junction rows whose (entityLogicalName, sourceField) matches a
    /// discovered descriptor. For each row NOT present in
    /// <paramref name="expectedKeys"/> (i.e., parent's source Lookup is now
    /// null/changed), synthesize a Removed event + dispatch. Returns
    /// (removedCount, errorCount).
    /// </summary>
    private async Task<(int removed, int errors)> ScanOrphansAndDispatchAsync(
        string entityType,
        IReadOnlyDictionary<string, MembershipDescriptor> fieldToDescriptor,
        IGenericEntityService entityService,
        IMembershipJunctionUpdater junctionUpdater,
        MembershipReconciliationOptions opts,
        HashSet<JunctionKey> expectedKeys,
        string correlationId,
        CancellationToken ct)
    {
        var removed = 0;
        var errors = 0;

        // Project just enough to reconstruct the natural key + decide whether
        // it's expected. role is included so the synthesized Removed event
        // carries the same role the original Added event would have used.
        var orphanQuery = new QueryExpression(JunctionEntityLogicalName)
        {
            ColumnSet = new ColumnSet(
                JunctionAttrPersonId,
                JunctionAttrPersonIdType,
                JunctionAttrEntityLogicalName,
                JunctionAttrEntityRecordId,
                JunctionAttrSourceField,
                JunctionAttrRole),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression(JunctionAttrEntityLogicalName, ConditionOperator.Equal, entityType),
                    // sourceField IN (discovered field names) — only rows whose
                    // sourceField is currently a discovered descriptor (i.e., a
                    // membership-tracked Lookup). Skips rows for fields no longer
                    // configured to avoid spurious deletes during a config
                    // shrink (operator's responsibility to do an explicit
                    // cleanup if they want stale sourceField rows removed).
                    new ConditionExpression(
                        JunctionAttrSourceField,
                        ConditionOperator.In,
                        fieldToDescriptor.Keys.Cast<object>().ToArray()),
                },
            },
            PageInfo = new PagingInfo
            {
                PageNumber = 1,
                Count = Math.Max(1, opts.OrphanFetchPageSize),
                PagingCookie = null,
                ReturnTotalRecordCount = false,
            },
        };

        while (!ct.IsCancellationRequested)
        {
            EntityCollection page;
            try
            {
                page = await entityService.RetrieveMultipleAsync(orphanQuery, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "MembershipReconciliationJob orphan scan threw for entity={EntityType} pageNumber={PageNumber} — aborting orphan scan for this entity (correlationId={CorrelationId})",
                    entityType, orphanQuery.PageInfo.PageNumber, correlationId);
                errors++;
                break;
            }

            if (page is null || page.Entities is null || page.Entities.Count == 0)
            {
                break;
            }

            foreach (var row in page.Entities)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var (key, role) = TryBuildKeyFromJunctionRow(row);
                if (key is null)
                {
                    // Malformed row — skip + log; do NOT count as error
                    // (operator visibility through a single warning is enough).
                    _logger.LogDebug(
                        "MembershipReconciliationJob junction row {RowId} for entity={EntityType} has unparseable natural-key columns — skipping (correlationId={CorrelationId})",
                        row.Id, entityType, correlationId);
                    continue;
                }

                if (expectedKeys.Contains(key.Value))
                {
                    // Row is still expected — parent's source Lookup still points
                    // at this person. No action; the Updated event dispatched in
                    // Step A already refreshed lastSyncedOn.
                    continue;
                }

                // Orphan — parent's source Lookup is null or no longer points
                // at this person. Synthesize Removed + dispatch.
                var evt = BuildEvent(
                    key: key.Value,
                    role: role ?? string.Empty,
                    mutationType: MembershipMutationType.Removed,
                    correlationId: correlationId);

                try
                {
                    await junctionUpdater.HandleAsync(evt, ct).ConfigureAwait(false);
                    removed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(
                        ex,
                        "MembershipReconciliationJob orphan handler failed for entity={EntityType} junctionRow={RowId} — logging + continuing (correlationId={CorrelationId})",
                        entityType, row.Id, correlationId);
                }
            }

            if (!page.MoreRecords)
            {
                break;
            }

            orphanQuery.PageInfo.PageNumber++;
            orphanQuery.PageInfo.PagingCookie = page.PagingCookie;
        }

        return (removed, errors);
    }

    /// <summary>
    /// Reads a lookup attribute from a parent entity and resolves it to
    /// (PersonId, PersonIdentityType). Returns (null, null) when the
    /// lookup is not populated OR the target entity is not a recognized
    /// identity type (the descriptor's IdentityType string maps to the
    /// closed <see cref="PersonIdentityType"/> enum via
    /// <see cref="TryParseIdentityType"/>).
    /// </summary>
    internal static (Guid? personId, PersonIdentityType? personIdType)
        ReadLookupAsIdentity(Entity parent, MembershipDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!parent.Contains(descriptor.Field))
        {
            return (null, null);
        }

        var raw = parent[descriptor.Field];
        if (raw is null)
        {
            return (null, null);
        }

        Guid? id = raw switch
        {
            EntityReference er => er.Id == Guid.Empty ? null : er.Id,
            Guid g => g == Guid.Empty ? null : g,
            _ => null,
        };
        if (id is null)
        {
            return (null, null);
        }

        if (!TryParseIdentityType(descriptor.IdentityType, out var identityType))
        {
            return (null, null);
        }

        return (id, identityType);
    }

    /// <summary>
    /// Map the open-string identity-type label (per
    /// <see cref="MembershipDescriptor.IdentityType"/>) to the closed
    /// <see cref="PersonIdentityType"/> enum. Returns false for unknown
    /// labels (e.g., <c>"BusinessUnit"</c> — derived in the resolver
    /// pipeline, not a real lookup target per Q4).
    /// </summary>
    internal static bool TryParseIdentityType(string label, out PersonIdentityType type)
    {
        switch (label?.Trim().ToLowerInvariant())
        {
            case "user":
            case "systemuser":
                type = PersonIdentityType.User;
                return true;
            case "contact":
                type = PersonIdentityType.Contact;
                return true;
            case "team":
                type = PersonIdentityType.Team;
                return true;
            case "organization":
                type = PersonIdentityType.Organization;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>
    /// Decode a junction row into its natural-key + role. Returns (null, null)
    /// when any required attribute is missing/malformed.
    /// </summary>
    internal static (JunctionKey? key, string? role) TryBuildKeyFromJunctionRow(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.Contains(JunctionAttrPersonId) || row[JunctionAttrPersonId] is not string personIdString
            || !Guid.TryParse(personIdString, out var personId))
        {
            return (null, null);
        }

        if (!row.Contains(JunctionAttrPersonIdType) || row[JunctionAttrPersonIdType] is not OptionSetValue personIdTypeOsv
            || !Enum.IsDefined(typeof(PersonIdentityType), personIdTypeOsv.Value))
        {
            return (null, null);
        }
        var personIdType = (PersonIdentityType)personIdTypeOsv.Value;

        if (!row.Contains(JunctionAttrEntityLogicalName) || row[JunctionAttrEntityLogicalName] is not string entityLogicalName
            || string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return (null, null);
        }

        if (!row.Contains(JunctionAttrEntityRecordId) || row[JunctionAttrEntityRecordId] is not string entityRecordIdString
            || !Guid.TryParse(entityRecordIdString, out var entityRecordId))
        {
            return (null, null);
        }

        if (!row.Contains(JunctionAttrSourceField) || row[JunctionAttrSourceField] is not string sourceField
            || string.IsNullOrWhiteSpace(sourceField))
        {
            return (null, null);
        }

        var role = row.Contains(JunctionAttrRole) ? row[JunctionAttrRole] as string : null;

        return (new JunctionKey(personId, personIdType, entityLogicalName, entityRecordId, sourceField), role);
    }

    /// <summary>
    /// Build a top-level OR filter that returns rows where AT LEAST ONE
    /// discovered lookup is non-null. Without this filter every parent row
    /// is fetched regardless of whether it has any membership data.
    /// </summary>
    internal static FilterExpression BuildAtLeastOneNotNullFilter(IReadOnlyList<MembershipDescriptor> descriptors)
    {
        var filter = new FilterExpression(LogicalOperator.Or);
        foreach (var d in descriptors)
        {
            filter.Conditions.Add(new ConditionExpression(d.Field, ConditionOperator.NotNull));
        }
        return filter;
    }

    private static MembershipChangedEvent BuildEvent(
        JunctionKey key,
        string role,
        MembershipMutationType mutationType,
        string correlationId)
    {
        return new MembershipChangedEvent
        {
            PersonId = key.PersonId,
            PersonIdType = key.PersonIdType,
            EntityLogicalName = key.EntityLogicalName,
            EntityRecordId = key.EntityRecordId,
            SourceField = key.SourceField,
            Role = role,
            MutationType = mutationType,
            CorrelationId = correlationId,
            OccurredOnUtc = DateTime.UtcNow,
        };
    }

    private static string SerializeResult(IReadOnlyList<EntityReconciliationResult> results)
    {
        return JsonSerializer.Serialize(new ResultPayload(results), JsonWriteOptions);
    }

    /// <summary>
    /// Composite natural key for a junction row. Matches the
    /// <c>sprk_uea_natural_key</c> 5-tuple used by
    /// <see cref="IMembershipJunctionUpdater"/>.
    /// </summary>
    internal readonly record struct JunctionKey(
        Guid PersonId,
        PersonIdentityType PersonIdType,
        string EntityLogicalName,
        Guid EntityRecordId,
        string SourceField);

    /// <summary>
    /// Per-entity recon outcome — serialized into
    /// <see cref="JobRunResult.ResultJson"/>.
    /// </summary>
    /// <param name="EntityType">Entity logical name reconciled.</param>
    /// <param name="DiscoveredFields">Count of identity-Lookup fields returned by discovery.</param>
    /// <param name="ParentRowsScanned">Parent rows fetched from Dataverse for this entity.</param>
    /// <param name="Verified">Updated events dispatched (idempotent upsert — verifies-or-creates).</param>
    /// <param name="Added">Reserved for future separation of "newly-added" from "verified"; always 0 today.</param>
    /// <param name="Removed">Removed events dispatched for orphaned junction rows.</param>
    /// <param name="Errors">Per-row exceptions during dispatch (logged + counted but not fatal).</param>
    /// <param name="DurationMs">Wall-clock ms spent on this entity's recon pass.</param>
    /// <param name="ErrorMessage">Discovery-failure message when the whole entity-type pass was aborted; null otherwise.</param>
    public sealed record EntityReconciliationResult(
        string EntityType,
        int DiscoveredFields,
        int ParentRowsScanned,
        int Verified,
        int Added,
        int Removed,
        int Errors,
        long DurationMs,
        string? ErrorMessage);

    /// <summary>Wrapper object for the JSON ResultJson payload.</summary>
    private sealed record ResultPayload(IReadOnlyList<EntityReconciliationResult> Entities);
}
