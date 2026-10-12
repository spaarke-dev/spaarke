using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// What suppression is for one grain's partition at one moment: the counted dismissals in the trailing window, every recorded
/// dismissal in it, and the date the partition's OWN counted dismissals justify (null below the threshold or once lapsed).
/// </summary>
/// <param name="DismissalsCounted">Dismissals that count toward the threshold (a reason that does not count, and a policy whose
/// <c>sprk_countstowardsuppression</c> is No, contribute none).</param>
/// <param name="DismissalsRecorded">Every human dismissal in the window, counted or not: recorded, queryable, clusterable.</param>
/// <param name="SuppressedUntil">The most recent counted dismissal + 30 days when the partition has at least the threshold and that
/// date is still in the future; otherwise null. A stored <c>sprk_suppresseduntil</c> is never reported for its own sake (#1661 F1):
/// a stamp the partition's own dismissals do not justify is withdrawn, not shown.</param>
/// <param name="PolicyCountsTowardSuppression"><c>sprk_policy.sprk_countstowardsuppression</c> (default Yes).</param>
public sealed record SuppressionStatus(
    int DismissalsCounted, int DismissalsRecorded, DateTime? SuppressedUntil, bool PolicyCountsTowardSuppression)
{
    public int Threshold => SignalSuppressionService.ThresholdDismissals;

    public int WindowDays => SignalSuppressionService.WindowDays;
}

/// <summary>
/// The suppression grain (FR-17 as amended; D-34, D-36, D-39): (policy, core-record type, core-record id) for EVERY core type,
/// read from the Signal's generic columns; a Signal with no core record is its own grain, (policy, item).
/// </summary>
public readonly record struct SuppressionGrain(Guid? CoreTypeRefId, Guid? CoreRecordId, string? SubjectEntity, Guid? SubjectId)
{
    public bool IsNoCore => CoreRecordId is null;

    public static SuppressionGrain ForCore(Guid? coreTypeRefId, Guid coreRecordId) => new(coreTypeRefId, coreRecordId, null, null);

    public static SuppressionGrain ForItem(string subjectEntity, Guid subjectId) => new(null, null, subjectEntity, subjectId);
}

/// <summary>
/// The secure-family partition of a grain (#1661, owner decision D-123; uac-r2 secure-schema audit F6): <see cref="Ordinary"/>,
/// one partition per SET of secure roots a Signal sits under (two families under one readable core record never share a count or a
/// stamp), or <see cref="Undetermined"/> when the set cannot be known. The undetermined bucket is fail closed: it is never stamped,
/// its stored stamps are withdrawn, and it never mutes or is muted by another partition.
/// </summary>
public readonly record struct SuppressionPartition
{
    private const string UndeterminedKey = "?";

    private SuppressionPartition(string? key) => Key = key;

    /// <summary>Null for the ordinary partition, "?" for the undetermined bucket, else the sorted "table:id" roots joined by ",".</summary>
    public string? Key { get; }

    public static SuppressionPartition Ordinary => default;

    public static SuppressionPartition Undetermined => new(UndeterminedKey);

    /// <summary>The partition of a Signal under exactly <paramref name="roots"/> (none: <see cref="Ordinary"/>).</summary>
    public static SuppressionPartition Secure(IEnumerable<(string Table, Guid Id)> roots)
    {
        var key = string.Join(",", roots
            .Select(r => r.Table.ToLowerInvariant() + ":" + r.Id.ToString("D", CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
        return key.Length == 0 ? Ordinary : new SuppressionPartition(key);
    }

    public bool IsOrdinary => Key is null;

    public bool IsUndetermined => Key == UndeterminedKey;

    public override string ToString() => Key switch { null => "ordinary", UndeterminedKey => "undetermined", var k => "secure[" + k + "]" };
}

/// <summary>Stable EventIds for suppression (the 5030x range is the Signal side's; 5031x the Decision Record's).</summary>
public static class SuppressionEvents
{
    /// <summary>Information: a grain reached the threshold and its Signals were stamped. Properties: policy code, counts, until.</summary>
    public static readonly EventId Stamped = new(50350, nameof(Stamped));

    /// <summary>Error: counting or stamping a grain failed; the next evaluation retries. Properties: policy code, exception type.</summary>
    public static readonly EventId Failed = new(50351, nameof(Failed));

    /// <summary>Information: stamps a partition's own dismissals no longer justify were lowered or cleared (#1661 F1).</summary>
    public static readonly EventId Withdrawn = new(50352, nameof(Withdrawn));
}

/// <summary>
/// Three counted dismissals of one policy on one core record suppress that pairing for 30 days (task 034; spec FR-17, success
/// criterion 4; D-11 as amended by D-34/D-36/D-39). The ONE counter: the nightly job (task 031) reads the stamp it writes and the
/// decision wizard / admin read the same number through <c>GET /api/v1/signals/{signalId}/suppression</c>.
/// </summary>
/// <remarks>
/// <para><b>What counts.</b> A Signal closed <c>Dismissed</c> (a human resolution: an auto-closure is <c>ConditionCleared</c>,
/// <c>Superseded</c> or <c>PolicyRetired</c> and never counts), whose Decision Record is of class Dismissal when it has one, decided
/// in the trailing <see cref="WindowDays"/> days. One review that dismisses several Signals writes ONE Decision Record and is one
/// dismissal: the count is of distinct Decision Records. The reason code (the part of <c>sprk_reason</c> before " - ") is looked up
/// in task 036's catalog: a code whose <c>CountsTowardSuppression</c> is false (Wrong matter - misresolved) is recorded and
/// counted in <see cref="SuppressionStatus.DismissalsRecorded"/> but never in <see cref="SuppressionStatus.DismissalsCounted"/>.
/// A code the catalog does not know counts (the switch is "declared non-counting", never "undeclared"). A policy whose
/// <c>sprk_countstowardsuppression</c> is No counts nothing.</para>
/// <para><b>The secure-family partition (#1661, owner decision D-123; uac-r2 audit F6).</b> A secure subject can be filed under a
/// core record a non-member reads, and two secure families can share one core record. Every grain is therefore split by
/// <see cref="SuppressionPartition"/>: the Signals under no secure root, and one partition per SET of secure roots. A partition's
/// dismissals count toward, and stamp, only that partition's Signals, so a stamp or a count on a row never reflects a dismissal its
/// readers cannot see. The truth is the union uac-r2 asked for (F2): a Signal is secure when its owner is the Secure Record owner team
/// (<see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>) OR uac-r2's own upward walk
/// (<see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>) puts it, or its subject, under a secure root. The walk starts from
/// the Signal row AND from its subject row (uac-r2's answer on the walk stop: the walk ignores the owners of the rows it is given, so a
/// subject still owned by an ordinary team while it waits to be re-owned is placed under its root); intermediate rows owned by an
/// ordinary team still stop it (the resolver's own rule). A walk that is undetermined, or a Secure-team-owned Signal with no root
/// found, is <see cref="SuppressionPartition.Undetermined"/>; an ambiguous team or a fault throws (fail closed). The same helper is used
/// by <see cref="ApplyAsync"/>, <see cref="ReconcileAsync"/>, the count read and the planner's hit partitions
/// (<see cref="PartitionsOfHitsAsync"/>).</para>
/// <para><b>The stamp, both ways.</b> At the threshold, <c>sprk_suppresseduntil</c> = the most recent counted dismissal + 30 days is
/// written on the partition's open Signals and on its dismissed Signals in the window (denormalized so the worklist query does not
/// count Decision Records per load; task 031's planner reads it for the re-raise rule, D-13). A live stamp the partition's own counted
/// dismissals do not justify (a partition move, a stamp written before #1661, a policy switched to No) is lowered to the justified
/// date or cleared, on rows of any status (#1661 F1). Only rows whose stamp would change are written (D-61). The stamp lapses on its
/// own: the dismissals that made it are then older than the window. This service does not implement the 14-day quiet window after a
/// 1st or 2nd dismissal (FR-17a): that is task 031's re-raise rule.</para>
/// <para><b>Who calls it.</b> The nightly job (<see cref="ReconcileAsync"/>) and the decision commit route (task 043) right after it
/// closes the dismissed Signals (<see cref="ApplyAsync"/>), so the effect is immediate rather than next night.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="SignalEpisodePlanner"/> already READS the stamp;
/// <see cref="PolicyActionRateService"/> counts acted vs dismissed per policy for a different question (rate, not a per-record
/// threshold). Extension: neither can host the per-record count and the stamp write; this one service owns both and the read route.
/// Cost of doing nothing: nothing ever sets <c>sprk_suppresseduntil</c>, so the fatigue case criterion 4 exists for is never
/// suppressed and the wizard cannot show the effect (reconciliation Z-7).</para>
/// </remarks>
public sealed class SignalSuppressionService
{
    /// <summary>Counted dismissals that suppress a grain (D-11).</summary>
    public const int ThresholdDismissals = 3;

    /// <summary>Both the counting window and the length of the suppression (D-11).</summary>
    public const int WindowDays = 30;

    private const string CountsReasonFailure = "suppression_failed";
    private const int InChunk = 200;

    private static readonly FrozenSet<string> NonCountingReasonCodes =
        DecisionActionCatalog.DismissalReasons(DecisionLane.Decide)
            .Concat(DecisionActionCatalog.DismissalReasons(DecisionLane.Do))
            .Where(r => !r.CountsTowardSuppression)
            .Select(r => r.Code)
            .ToHashSet(StringComparer.Ordinal)
            .ToFrozenSet(StringComparer.Ordinal);

    private readonly IGenericEntityService _entities;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SignalSuppressionService> _logger;

    public SignalSuppressionService(
        IGenericEntityService entities,
        OntologyWriterDataverseClient writer,
        TimeProvider timeProvider,
        IConfiguration configuration,
        IServiceScopeFactory scopes,
        ILogger<SignalSuppressionService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── The secure-family partition (#1661, owner decision D-123; audit F6) ──────────────────────────────────────

    /// <summary>The <c>logical name</c> of an owner that is a team: only a team can be the Secure Record owner team.</summary>
    private const string TeamLogicalName = "team";

    /// <summary>uac-r2's lineage of <c>sprk_signal</c> (the matter, the Decision Record and the subject's regarding lookups).</summary>
    private static readonly SecureChildLineage.Table SignalLineage = SecureChildLineage.Children["sprk_signal"];

    /// <summary>The <c>sprk_signal</c> lookup columns the walk follows: every read that partitions a Signal selects them.</summary>
    internal static IReadOnlyCollection<string> SignalLineageColumns => SignalLineage.Lookups.Keys.ToArray();

    /// <summary>True when <paramref name="owner"/> is the Secure Record owner team (and that team exists: <paramref name="secureTeamId"/>).</summary>
    private static bool IsSecureOwned(EntityReference? owner, Guid? secureTeamId) =>
        secureTeamId is { } team && team != Guid.Empty && owner is { LogicalName: TeamLogicalName } && owner.Id == team;

    /// <summary>
    /// The Secure Record owner team's id, identified by uac-r2's one rule (<see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>:
    /// the configured business unit and its NAMED, non-default Owner team, TOP 2 each); null when this environment has no such team,
    /// so no Signal can be secure. An ambiguous answer throws: a count that cannot be partitioned is never a guess (fail closed).
    /// </summary>
    private async Task<Guid?> ResolveSecureTeamAsync(CancellationToken ct)
    {
        var (teamId, refusal) = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_entities, _configuration, ct).ConfigureAwait(false);
        return refusal is null
            ? teamId
            : throw new InvalidOperationException($"The Secure Record owner team cannot be identified, so suppression cannot be partitioned ({refusal}).");
    }

    /// <summary>One Signal to place in a partition: the Signal row as read (or as the writer would write it, for a hit) with its
    /// lineage lookups, its owner as read (null for a hit: not written yet), and its subject.</summary>
    private readonly record struct PartitionItem(Entity Signal, EntityReference? Owner, string? SubjectEntity, Guid SubjectId);

    private static PartitionItem ItemOf(Entity signal) =>
        SignalWriter.TryParseDedupeKey(signal.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subjectId, out _)
            ? new PartitionItem(signal, signal.GetAttributeValue<EntityReference>("ownerid"), entity, subjectId)
            : new PartitionItem(signal, signal.GetAttributeValue<EntityReference>("ownerid"), null, Guid.Empty);

    private static PartitionItem ItemOf(ExistingSignal signal)
    {
        var row = new Entity("sprk_signal", signal.Id);
        if (signal.LineageRow is { } read)
        {
            foreach (var column in SignalLineage.Lookups.Keys)
            {
                if (read.GetAttributeValue<EntityReference>(column) is { } parent) row[column] = parent;
            }
        }

        return new PartitionItem(row, signal.Owner, signal.SubjectEntity, signal.SubjectId);
    }

    /// <summary>A hit's Signal row as <see cref="SignalWriter"/> would write it: the subject's regarding lookup, and <c>sprk_matter</c>
    /// when the core record is a matter (no owner yet: the writer's resolver decides it from the same truth).</summary>
    private static PartitionItem ItemOf(SignalHit hit)
    {
        var row = new Entity("sprk_signal", Guid.NewGuid());
        if (SignalWriter.RegardingLookupByEntity.TryGetValue(hit.SubjectEntity, out var column))
        {
            row[column] = new EntityReference(hit.SubjectEntity, hit.SubjectId);
        }

        if (hit.Core is { Entity: "sprk_matter" } core) row["sprk_matter"] = new EntityReference("sprk_matter", core.Id);
        return new PartitionItem(row, null, hit.SubjectEntity, hit.SubjectId);
    }

    /// <summary>
    /// THE partition truth (#1661 R3/R4), one helper for the count, the stamps and the planner. Per item: the secure roots above the
    /// Signal row and its subject row (uac-r2's <see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>, never re-implemented);
    /// undetermined, or a Secure-team-owned Signal with no root found: <see cref="SuppressionPartition.Undetermined"/>. No Secure team
    /// in this environment: every item is ordinary. An ambiguous team, a walk that does not complete or a fault throws; with
    /// <paramref name="tolerateItemFaults"/> a fault on ONE item's own walk leaves that item null instead (the caller skips it).
    /// </summary>
    private async Task<SuppressionPartition?[]> PartitionsAsync(IReadOnlyList<PartitionItem> items, bool tolerateItemFaults, CancellationToken ct)
    {
        var result = new SuppressionPartition?[items.Count];
        if (items.Count == 0) return result;

        var secureTeamId = await ResolveSecureTeamAsync(ct).ConfigureAwait(false);
        if (secureTeamId is null)
        {
            Array.Fill(result, SuppressionPartition.Ordinary);
            return result;
        }

        var subjects = await ReadSubjectRowsAsync(items, ct).ConfigureAwait(false);
        var lineages = items.Select(item =>
                (IReadOnlyList<Entity>)(item.SubjectEntity is { } entity && subjects.TryGetValue((entity.ToLowerInvariant(), item.SubjectId), out var subject)
                    ? [item.Signal, subject]
                    : [item.Signal]))
            .ToList();
        bool SecureOwned(int i) => IsSecureOwned(items[i].Owner, secureTeamId);

        using var scope = _scopes.CreateScope();
        var walker = scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>();

        // One walk over every row first: most pairings have no secure root anywhere, and then no Signal needs a walk of its own.
        var everything = RequireAnswer(await walker.SecureRootsAboveAsync(lineages.SelectMany(l => l).ToList(), ct).ConfigureAwait(false));
        if (everything.Roots.Count == 0 && everything.Undetermined.Count == 0)
        {
            for (var i = 0; i < items.Count; i++) result[i] = SecureOwned(i) ? SuppressionPartition.Undetermined : SuppressionPartition.Ordinary;
            return result;
        }

        // Rows with the same lookups sit under the same roots (the walk ignores the owners of the rows it is given).
        var byLineage = new Dictionary<string, SuppressionPartition?>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var key = LineageKey(lineages[i]);
            if (!byLineage.TryGetValue(key, out var found))
            {
                try
                {
                    var above = RequireAnswer(await walker.SecureRootsAboveAsync(lineages[i], ct).ConfigureAwait(false));
                    found = above.Undetermined.Count > 0 ? SuppressionPartition.Undetermined : SuppressionPartition.Secure(above.Roots);
                }
                catch (Exception ex) when (tolerateItemFaults && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
                {
                    _logger.LogWarning(SuppressionEvents.Failed, ex,
                        "Suppression: the secure roots above one Signal could not be read ({ExceptionType}); it is skipped this pass.", ex.GetType().Name);
                    found = null;
                }

                byLineage[key] = found;
            }

            result[i] = found is { IsOrdinary: true } && SecureOwned(i) ? SuppressionPartition.Undetermined : found;
        }

        return result;
    }

    private static SecureRootsAbove RequireAnswer(SecureRootsAbove above) =>
        above.Status == SecureChildShareSyncStatus.Completed
            ? above
            : throw new InvalidOperationException($"The secure roots above the Signals could not be determined ({above.Status}: {above.Detail}).");

    private static string LineageKey(IReadOnlyList<Entity> rows) =>
        string.Join("|", rows.Select(r => r.LogicalName + ":" + string.Join(",",
            SecureChildLineage.Children[r.LogicalName].Lookups.Keys.Order(StringComparer.Ordinal)
                .Select(k => r.GetAttributeValue<EntityReference>(k)?.Id.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty))));

    /// <summary>The subjects that are lineage children (a To Do, a communication, a document, an event, an invoice), read with their
    /// lineage lookups and owner columns, so the walk can start from them. A root-table subject is reached through the Signal's own
    /// regarding lookup. A subject that no longer exists is absent (the Signal's walk then reports its missing parent). A fault throws.</summary>
    private async Task<Dictionary<(string Entity, Guid Id), Entity>> ReadSubjectRowsAsync(IReadOnlyList<PartitionItem> items, CancellationToken ct)
    {
        var found = new Dictionary<(string, Guid), Entity>();
        var wanted = items
            .Where(i => i.SubjectEntity is { } e && i.SubjectId != Guid.Empty && SecureChildLineage.IsChild(e)
                && !string.Equals(e, SignalLineage.LogicalName, StringComparison.OrdinalIgnoreCase))
            .Select(i => (Entity: i.SubjectEntity!.ToLowerInvariant(), i.SubjectId))
            .Distinct()
            .GroupBy(s => s.Entity, StringComparer.Ordinal);
        foreach (var group in wanted)
        {
            var table = SecureChildLineage.Children[group.Key];
            foreach (var chunk in group.Select(s => s.SubjectId).Chunk(InChunk))
            {
                var query = new QueryExpression(table.LogicalName)
                {
                    ColumnSet = new ColumnSet(table.Lookups.Keys.Append("owningteam").Append("owninguser").ToArray()),
                    NoLock = true,
                };
                query.Criteria.AddCondition(table.IdColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
                var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                foreach (var row in rows.Entities) found[(table.LogicalName, row.Id)] = row;
            }
        }

        return found;
    }

    /// <summary>
    /// #1661 R1: the partition each hit's NEW episode will belong to, from the same truth as the rows (the hit's Signal as the writer
    /// would write it, plus its subject). Only resolved hits are returned: a hit whose answer is undetermined, whose walk faults, or
    /// any hit when the Secure team is ambiguous or the subjects cannot be read, is ABSENT, and the planner then skips it this pass
    /// and counts it (never a create or a mute on a guess). Logs once per pass when anything is left out.
    /// </summary>
    internal async Task<IReadOnlyDictionary<(string SubjectEntity, Guid SubjectId), SuppressionPartition>> PartitionsOfHitsAsync(
        string policyCode, IReadOnlyList<SignalHit> hits, CancellationToken ct)
    {
        var resolved = new Dictionary<(string, Guid), SuppressionPartition>();
        if (hits.Count == 0) return resolved;

        SuppressionPartition?[] partitions;
        try
        {
            partitions = await PartitionsAsync(hits.Select(ItemOf).ToList(), tolerateItemFaults: true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(policyCode, ex);
            return resolved;
        }

        for (var i = 0; i < hits.Count; i++)
        {
            if (partitions[i] is { IsUndetermined: false } partition) resolved[(hits[i].SubjectEntity, hits[i].SubjectId)] = partition;
        }

        if (resolved.Count < hits.Count)
        {
            _logger.LogWarning(SuppressionEvents.Failed,
                "Suppression: policy {PolicyCode}: {Count} hit(s) on a suppressed record could not be placed in a secure-family partition; " +
                "they are neither raised nor muted this pass, and the next evaluation retries.", policyCode, hits.Count - resolved.Count);
        }

        return resolved;
    }

    /// <summary>True when <paramref name="reason"/> ("code - detail") names a catalog code that never counts toward suppression.</summary>
    public static bool IsNonCountingReason(string? reason) => NonCountingReasonCodes.Contains(ReasonCode(reason));

    /// <summary>The code of a stored "code - detail" reason (the whole text when there is no separator).</summary>
    internal static string ReasonCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return string.Empty;
        var cut = reason.IndexOf(" - ", StringComparison.Ordinal);
        return (cut >= 0 ? reason[..cut] : reason).Trim();
    }

    /// <summary>
    /// The read the wizard's dismissal step and the admin screen use, keyed by a Signal the caller is ALREADY authorized for
    /// (the route calls <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> first); <paramref name="caller"/> is that same caller's
    /// Dataverse client. A fault (including a caller read that is not a success, an ambiguous Secure team, or an anchor whose partition
    /// is undetermined) throws: it is never an empty or a partial count. Null when the Signal does not carry what the grain needs (no
    /// policy, or an unparseable key on a no-core Signal): the route answers the uniform 404, never a guess.
    /// </summary>
    public async Task<SuppressionStatus?> GetStatusForSignalAsync(Guid signalId, IDataverseUserClient caller, CancellationToken ct)
    {
        var signal = await _entities.RetrieveAsync("sprk_signal", signalId,
            ["sprk_policy", "sprk_corerecordtype", "sprk_corerecordid", "sprk_dedupekey", "ownerid", .. SignalLineageColumns], ct).ConfigureAwait(false);

        if (signal.GetAttributeValue<EntityReference>("sprk_policy") is not { Id: var policyId } || policyId == Guid.Empty)
        {
            return null;
        }

        var coreIdText = signal.GetAttributeValue<string>("sprk_corerecordid")?.Trim();
        SuppressionGrain grain;
        if (!string.IsNullOrEmpty(coreIdText))
        {
            if (!Guid.TryParse(coreIdText, out var coreId) || coreId == Guid.Empty) return null;
            grain = SuppressionGrain.ForCore(signal.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id, coreId);
        }
        else if (SignalWriter.TryParseDedupeKey(signal.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subjectId, out _))
        {
            grain = SuppressionGrain.ForItem(entity, subjectId);
        }
        else
        {
            return null;
        }

        var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_policycode"], ct).ConfigureAwait(false);
        if (policy.GetAttributeValue<string>("sprk_policycode") is not { Length: > 0 } code)
        {
            return null; // a no-core grain is keyed by the policy code: without it the count would be silently zero
        }

        // The count is the CALLER's: the grain's Signals are trimmed to those the caller's own read returns, then Decision-Record facts
        // are read app-only for those rows only. And it is the anchor Signal's PARTITION's (#1661): the anchor is placed by the same truth
        // as every row, and only rows of that partition are counted, so the answer never carries a number or a date from across the wall.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var visible = await ReadVisibleSignalIdsAsync(caller, policyId, code, grain, now.AddDays(-WindowDays - 1), now, ct).ConfigureAwait(false);
        var assessed = await AssessAsync(policyId, code, grain, policyCounts: null, visible, known: null, anchor: signal, ct).ConfigureAwait(false);
        if (assessed.Anchor is not { IsUndetermined: false } anchor)
        {
            throw new InvalidOperationException("The Signal's secure-family partition cannot be determined, so its suppression count cannot be read.");
        }

        return assessed.Partitions.FirstOrDefault(p => p.Partition == anchor).Assessment?.Status
            ?? new SuppressionStatus(0, 0, null, assessed.PolicyCounts);
    }

    /// <summary>
    /// Counts the pairing and brings its stamps into line, in every partition (#1661, D-123): at the threshold the partition's open and
    /// in-window dismissed Signals are stamped, and any live stamp the partition's own dismissals do not justify is lowered or cleared
    /// (F1, on rows of any status). Idempotent: a second call writes nothing. Returns the status of <paramref name="report"/> (default the
    /// ordinary partition: the only one a non-member may be told about; an empty status when it has no Signal).
    /// </summary>
    public async Task<SuppressionStatus> ApplyAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, CancellationToken ct, SuppressionPartition? report = null)
    {
        var assessed = await AssessAsync(policyId, policyCode, grain, policyCounts: null, visibleSignalIds: null, known: null, anchor: null, ct).ConfigureAwait(false);
        foreach (var (_, assessment) in assessed.Partitions) await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);

        var wanted = report ?? SuppressionPartition.Ordinary;
        return assessed.Partitions.FirstOrDefault(p => p.Partition == wanted).Assessment?.Status
            ?? new SuppressionStatus(0, 0, null, assessed.PolicyCounts);
    }

    /// <summary>
    /// The nightly pass (task 031 calls it once per policy with the Signals it already read and the policy's
    /// <c>sprk_countstowardsuppression</c>). First every Signal of a grain that has a live stamp, or enough dismissals in total for some
    /// partition to reach the threshold, is placed in its partition (<see cref="ExistingSignal.Partition"/>, which the planner reads).
    /// Then, in memory, a (grain, partition) is worked when it is at the threshold with a stamp missing or older than its latest live
    /// one, or when it carries a live stamp its own dismissals do not justify (fewer than the threshold, a stamp later than its latest
    /// dismissal + 30 days, a policy that does not count, or the undetermined bucket). Such a grain is counted exactly and its stamps are
    /// raised, lowered or cleared; only rows that change are written (D-61). Returns the Signals with the corrected stamps, so the planner
    /// that runs next sees them even if a write failed, and the number of failures (the run is then not successful, and the next run
    /// retries). Fail closed: when the partitions cannot be computed, every live stamp is withheld from the planner in memory (K1;
    /// nothing is written); when a grain cannot be counted, the live stamps of its worked partitions are withheld the same way.
    /// </summary>
    internal async Task<(IReadOnlyList<ExistingSignal> Signals, int Failures)> ReconcileAsync(
        Guid policyId, string policyCode, bool policyCountsTowardSuppression, IReadOnlyList<ExistingSignal> existing, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-WindowDays);

        bool Live(ExistingSignal s) => s.SuppressedUntil is { } u && u > now;
        bool InWindowDismissal(ExistingSignal s) =>
            s.Resolution == SignalWriter.ResolutionDismissed && s.ResolvedOn is { } on && on >= windowStart.AddDays(-1);
        static int Dismissals(IEnumerable<ExistingSignal> rows, Func<ExistingSignal, bool> inWindow) =>
            rows.Where(inWindow).Select(s => s.DecisionRecordId ?? s.Id).Distinct().Count();

        // Partitioning only splits a count, so a grain below the threshold in total, with no live stamp, needs nothing at all.
        var grains = existing.GroupBy(GrainKeyOf)
            .Where(g => g.Any(Live) || (policyCountsTowardSuppression && Dismissals(g, InWindowDismissal) >= ThresholdDismissals))
            .ToList();
        if (grains.Count == 0) return (existing, 0);

        var rows = grains.SelectMany(g => g).ToList();
        SuppressionPartition?[] partitions;
        try
        {
            partitions = await PartitionsAsync(rows.Select(ItemOf).ToList(), tolerateItemFaults: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // K1: a pairing that cannot be partitioned is never counted or muted whole. Nothing is written; the next run retries.
            LogFailure(policyCode, ex);
            return (existing.Select(s => Live(s) ? s with { SuppressedUntil = null } : s).ToList(), 1);
        }

        var partitionOf = new Dictionary<Guid, SuppressionPartition>();
        for (var i = 0; i < rows.Count; i++) partitionOf[rows[i].Id] = partitions[i]!.Value;
        existing = existing.Select(s => partitionOf.TryGetValue(s.Id, out var p) ? s with { Partition = p } : s).ToList();

        bool NeedsWork(IReadOnlyList<ExistingSignal> g, SuppressionPartition partition)
        {
            var live = g.Where(Live).ToList();
            if (partition.IsUndetermined || !policyCountsTowardSuppression) return live.Count > 0; // nothing may stand: withdraw it

            var dismissed = g.Where(InWindowDismissal).ToList();
            var count = Dismissals(g, InWindowDismissal);
            if (live.Count > 0)
            {
                // F1: a live stamp the partition's own dismissals do not justify (a move, a pre-#1661 stamp, a reason that stopped counting).
                if (count < ThresholdDismissals) return true;
                var justified = dismissed.Max(s => s.ResolvedOn!.Value).AddDays(WindowDays);
                if (live.Any(s => s.SuppressedUntil > justified)) return true;
            }

            if (count < ThresholdDismissals) return false;
            if (live.Count == 0) return true;
            var latest = live.Max(s => s.SuppressedUntil!.Value);
            var dismissedIds = dismissed.Select(s => s.Id).ToHashSet();
            return g.Any(s => (s.IsOpen || dismissedIds.Contains(s.Id)) && (s.SuppressedUntil is null || s.SuppressedUntil < latest));
        }

        var candidates = existing
            .Where(s => s.Partition is not null)
            .GroupBy(s => (Grain: GrainKeyOf(s), Partition: s.Partition!.Value))
            .Where(g => NeedsWork(g.ToList(), g.Key.Partition))
            .GroupBy(g => g.Key.Grain)
            .ToList();
        if (candidates.Count == 0) return (existing, 0);

        var changed = new Dictionary<Guid, DateTime?>();
        var failures = 0;
        foreach (var grainGroup in candidates)
        {
            var (type, core, entity, subject) = grainGroup.Key;
            var grain = new SuppressionGrain(type, core, entity, subject);
            var counted = false;
            try
            {
                var assessed = await AssessAsync(policyId, policyCode, grain, policyCountsTowardSuppression, visibleSignalIds: null, partitionOf, anchor: null, ct)
                    .ConfigureAwait(false);
                counted = true;
                foreach (var (_, assessment) in assessed.Partitions)
                {
                    // In memory first: the planner must see the corrected stamps this pass even when a write below fails.
                    foreach (var (id, to) in assessment.Changes) changed[id] = to;
                }

                foreach (var (_, assessment) in assessed.Partitions) await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                LogFailure(policyCode, ex);
                if (!counted)
                {
                    foreach (var row in grainGroup.SelectMany(g => g).Where(Live)) changed[row.Id] = null;
                }
            }
        }

        return changed.Count == 0
            ? (existing, failures)
            : (existing.Select(s => changed.TryGetValue(s.Id, out var to) ? s with { SuppressedUntil = to } : s).ToList(), failures);
    }

    private static (Guid?, Guid?, string?, Guid?) GrainKeyOf(ExistingSignal s) =>
        s.CoreRecordId is { } core ? (s.CoreTypeRefId, core, null, null) : (null, null, s.SubjectEntity, s.SubjectId);

    // ── Counting ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="StampUntil">The date the partition's own counted dismissals justify (null below the threshold, once lapsed, or in the
    /// undetermined bucket).</param>
    /// <param name="Targets">Rows to RAISE to <paramref name="StampUntil"/>: open, or dismissed in the window, with no or an older stamp.</param>
    /// <param name="Lowers">Rows of any status whose live stamp is later than <paramref name="StampUntil"/> (or live with none justified): lowered
    /// to it, or cleared (#1661 F1).</param>
    private sealed record Assessment(
        SuppressionStatus Status, DateTime? StampUntil, IReadOnlyList<(Guid Id, DateTime? Current)> Targets, IReadOnlyList<Guid> Lowers)
    {
        public IEnumerable<(Guid Id, DateTime? To)> Changes =>
            (StampUntil is { } until ? Targets.Select(t => (t.Id, (DateTime?)until)) : [])
                .Concat(Lowers.Select(id => (id, StampUntil)));
    }

    private sealed record GrainAssessment(
        IReadOnlyList<(SuppressionPartition Partition, Assessment Assessment)> Partitions, SuppressionPartition? Anchor, bool PolicyCounts);

    private sealed record DismissedSignal(Guid SignalId, DateTime ResolvedOn, Guid? DecisionRecordId);

    /// <summary>
    /// Counts one pairing in every partition it has (#1661, D-123): the grain's Signals are read once, each is placed in its partition
    /// (<paramref name="known"/> placements are reused; the rest, and <paramref name="anchor"/>, are placed by <see cref="PartitionsAsync"/>),
    /// and each partition is counted from its own rows only. The ordinary partition comes first.
    /// </summary>
    private async Task<GrainAssessment> AssessAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, bool? policyCounts, IReadOnlySet<Guid>? visibleSignalIds,
        IReadOnlyDictionary<Guid, SuppressionPartition>? known, Entity? anchor, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-WindowDays);

        if (policyCounts is null)
        {
            var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_countstowardsuppression"], ct).ConfigureAwait(false);
            policyCounts = policy.GetAttributeValue<bool?>("sprk_countstowardsuppression") ?? true;
        }

        var all = await ReadGrainSignalsAsync(policyId, policyCode, grain, windowStart.AddDays(-1), now, ct).ConfigureAwait(false);
        if (visibleSignalIds is not null) all = all.Where(s => visibleSignalIds.Contains(s.Id)).ToList();

        var toPlace = all.Where(s => known is null || !known.ContainsKey(s.Id)).ToList();
        var items = toPlace.Select(ItemOf).ToList();
        if (anchor is not null) items.Add(ItemOf(anchor));
        var placed = await PartitionsAsync(items, tolerateItemFaults: false, ct).ConfigureAwait(false);

        var partitionOf = new Dictionary<Guid, SuppressionPartition>();
        for (var i = 0; i < toPlace.Count; i++) partitionOf[toPlace[i].Id] = placed[i]!.Value;
        SuppressionPartition PartitionOf(Entity s) => known is not null && known.TryGetValue(s.Id, out var p) ? p : partitionOf[s.Id];

        var results = new List<(SuppressionPartition, Assessment)>();
        foreach (var group in all.GroupBy(PartitionOf).OrderBy(g => g.Key.IsOrdinary ? 0 : 1).ThenBy(g => g.Key.Key, StringComparer.Ordinal))
        {
            var stampable = policyCounts.Value && !group.Key.IsUndetermined;
            results.Add((group.Key, await AssessPartitionAsync(group.ToList(), policyCounts.Value, stampable, now, windowStart, ct).ConfigureAwait(false)));
        }

        return new GrainAssessment(results, anchor is null ? null : placed[^1], policyCounts.Value);
    }

    private async Task<Assessment> AssessPartitionAsync(
        IReadOnlyList<Entity> signals, bool policyCounts, bool stampable, DateTime now, DateTime windowStart, CancellationToken ct)
    {
        var dismissed = signals
            .Where(s => s.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value == SignalWriter.ResolutionDismissed
                && s.GetAttributeValue<DateTime?>("sprk_resolvedon") is not null)
            .Select(s => new DismissedSignal(
                s.Id,
                DateTime.SpecifyKind(s.GetAttributeValue<DateTime>("sprk_resolvedon"), DateTimeKind.Utc),
                s.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id))
            .ToList();

        var records = await ReadDecisionRecordsAsync(dismissed.Where(d => d.DecisionRecordId is not null).Select(d => d.DecisionRecordId!.Value).Distinct().ToList(), ct)
            .ConfigureAwait(false);

        // One dismissal per Decision Record (a review may resolve several Signals); a Signal with no record is its own dismissal.
        var events = new Dictionary<Guid, (DateTime On, bool Counts)>();
        foreach (var d in dismissed)
        {
            Guid key;
            DateTime on;
            bool counts = true;
            if (d.DecisionRecordId is { } recordId && records.TryGetValue(recordId, out var record))
            {
                if (record.Class != (int)RecordedDecisionClass.Dismissal) continue; // an action was taken: not a dismissal
                key = recordId;
                on = record.DecidedOn ?? d.ResolvedOn;
                counts = !IsNonCountingReason(record.Reason);
            }
            else
            {
                key = d.DecisionRecordId ?? d.SignalId;
                on = d.ResolvedOn;
            }

            if (on < windowStart) continue;
            events[key] = (on, counts);
        }

        var counted = policyCounts ? events.Values.Where(e => e.Counts).ToList() : new List<(DateTime On, bool Counts)>();

        // The partition's OWN justified date: the only date it may carry or report (#1661 F1).
        DateTime? justified = null;
        if (stampable && counted.Count >= ThresholdDismissals && counted.Max(e => e.On).AddDays(WindowDays) is var until && until > now)
        {
            justified = until;
        }

        var targets = new List<(Guid, DateTime?)>();
        var lowers = new List<Guid>();
        var inWindow = dismissed.Where(d => d.ResolvedOn >= windowStart.AddDays(-1)).Select(d => d.SignalId).ToHashSet();
        foreach (var s in signals)
        {
            var current = s.GetAttributeValue<DateTime?>("sprk_suppresseduntil") is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : (DateTime?)null;
            if (current is { } live && live > now && (justified is null || live > justified))
            {
                lowers.Add(s.Id);
                continue;
            }

            if (justified is not { } raiseTo) continue;
            var status = s.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value;
            var open = status is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged;
            if (!open && !inWindow.Contains(s.Id)) continue;
            if (current is { } c2 && c2 >= raiseTo) continue;
            targets.Add((s.Id, current));
        }

        return new Assessment(new SuppressionStatus(counted.Count, events.Count, justified, policyCounts), justified, targets, lowers);
    }

    private async Task StampAsync(string policyCode, Assessment assessment, CancellationToken ct)
    {
        if (assessment.Lowers.Count > 0)
        {
            foreach (var id in assessment.Lowers)
            {
                await _writer.UpdateAsync("sprk_signal", id, new Dictionary<string, object> { ["sprk_suppresseduntil"] = assessment.StampUntil! }, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(SuppressionEvents.Withdrawn,
                "Suppression: policy {PolicyCode}: {Signals} Signal(s) carried a stamp their partition's {Counted} counted dismissal(s) do not justify; " +
                "lowered to {Until:o} (empty: cleared).", policyCode, assessment.Lowers.Count, assessment.Status.DismissalsCounted, assessment.StampUntil);
        }

        if (assessment.StampUntil is not { } until || assessment.Targets.Count == 0) return;

        foreach (var (id, _) in assessment.Targets)
        {
            await _writer.UpdateAsync("sprk_signal", id, new Dictionary<string, object> { ["sprk_suppresseduntil"] = until }, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(SuppressionEvents.Stamped,
            "Suppression: policy {PolicyCode} reached {Counted} counted dismissal(s) on one record; {Signals} Signal(s) suppressed until {Until:o}.",
            policyCode, assessment.Status.DismissalsCounted, assessment.Targets.Count, until);
    }

    private void LogFailure(string policyCode, Exception ex)
    {
        _logger.LogError(SuppressionEvents.Failed, ex,
            "Suppression could not be counted or stamped for policy {PolicyCode} ({ExceptionType}); the next evaluation retries.",
            policyCode, ex.GetType().Name);
        OntologyWriterTelemetry.RecordFailure(CountsReasonFailure);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ids of the grain's Signals the CALLER can read: the same filter (grain; open, dismissed in the window, or carrying a live
    /// stamp) as the app-only read, asked through the caller's own Dataverse client, which trims the result to rows that caller may see. A
    /// read that is not a success, or a result as large as the page cap, throws: a partial answer would be a wrong count.</summary>
    private static async Task<IReadOnlySet<Guid>> ReadVisibleSignalIdsAsync(
        IDataverseUserClient caller, Guid policyId, string policyCode, SuppressionGrain grain, DateTime resolvedSince, DateTime now, CancellationToken ct)
    {
        const int cap = 4999;
        static string Quote(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        static string Utc(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var filter = $"_sprk_policy_value eq {policyId:D}";
        if (grain.CoreRecordId is { } coreId)
        {
            filter += $" and sprk_corerecordid eq {Quote(coreId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant())}";
            if (grain.CoreTypeRefId is { } typeRef) filter += $" and _sprk_corerecordtype_value eq {typeRef:D}";
        }
        else
        {
            var prefix = SignalWriter.DedupeKeySubjectPrefix(policyCode, grain.SubjectEntity!, grain.SubjectId!.Value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant());
            filter += $" and sprk_corerecordid eq null and startswith(sprk_dedupekey,{Quote(prefix)})";
        }

        // The same status/window clause as the app-only read: without it the page cap would count every Signal of the policy on the record, of any age.
        filter += $" and (sprk_signalstatus eq {SignalWriter.SignalStatusOpen} or sprk_signalstatus eq {SignalWriter.SignalStatusAcknowledged}"
            + $" or (sprk_resolutiontype eq {SignalWriter.ResolutionDismissed} and sprk_resolvedon ge {Utc(resolvedSince)})"
            + $" or sprk_suppresseduntil gt {Utc(now)})";

        var response = await caller.GetAsync(
            $"sprk_signals?$select=sprk_signalid&$top={cap}&$filter={Uri.EscapeDataString(filter)}", ct).ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is not { ValueKind: System.Text.Json.JsonValueKind.Object } body
            || !body.TryGetProperty("value", out var rows) || rows.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The caller's read of the Signals on this record failed ({response.ErrorCode ?? "no body"}).");
        }

        var ids = new HashSet<Guid>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("sprk_signalid", out var id) && Guid.TryParse(id.GetString(), out var guid)) ids.Add(guid);
        }

        if (rows.GetArrayLength() >= cap)
        {
            throw new InvalidOperationException("More Signals than one caller-read page on one record; the count cannot be trimmed exactly.");
        }

        return ids;
    }

    /// <summary>The grain's Signals of one policy: by the generic core pair, or by the subject's dedupe-key prefix. Open ones, dismissed ones
    /// in the window, and any row still carrying a live stamp (so a stamp the partition no longer justifies can be withdrawn, #1661 F1),
    /// with their lineage lookups, owner and dedupe key (the partition).</summary>
    private async Task<IReadOnlyList<Entity>> ReadGrainSignalsAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, DateTime resolvedSince, DateTime now, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet(new[] { "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_suppresseduntil", "sprk_decisionrecord", "ownerid", "sprk_dedupekey" }
                .Concat(SignalLineageColumns).Distinct(StringComparer.Ordinal).ToArray()),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
        if (grain.CoreRecordId is { } coreId)
        {
            query.Criteria.AddCondition("sprk_corerecordid", ConditionOperator.Equal, coreId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant());
            if (grain.CoreTypeRefId is { } typeRef) query.Criteria.AddCondition("sprk_corerecordtype", ConditionOperator.Equal, typeRef);
        }
        else
        {
            // A To Do later filed under a core record has core-bearing episodes under the same key prefix: those belong to the core grain.
            query.Criteria.AddCondition("sprk_corerecordid", ConditionOperator.Null);
            query.Criteria.AddCondition("sprk_dedupekey", ConditionOperator.BeginsWith,
                SignalWriter.DedupeKeySubjectPrefix(policyCode, grain.SubjectEntity!, grain.SubjectId!.Value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant()));
        }

        // Open ones (to hide), the dismissed ones in the window (to count) and the stamped ones (to keep honest): nothing else matters.
        var statusFilter = query.Criteria.AddFilter(LogicalOperator.Or);
        statusFilter.AddCondition("sprk_signalstatus", ConditionOperator.In, SignalWriter.SignalStatusOpen, SignalWriter.SignalStatusAcknowledged);
        var dismissedFilter = statusFilter.AddFilter(LogicalOperator.And);
        dismissedFilter.AddCondition("sprk_resolutiontype", ConditionOperator.Equal, SignalWriter.ResolutionDismissed);
        dismissedFilter.AddCondition("sprk_resolvedon", ConditionOperator.GreaterEqual, resolvedSince);
        statusFilter.AddCondition("sprk_suppresseduntil", ConditionOperator.GreaterThan, now);

        var rows = new List<Entity>();
        for (var page = 1; ; page++)
        {
            if (page > SignalEvaluator.MaxPages)
            {
                throw new InvalidOperationException($"Policy {policyCode} has more than {SignalEvaluator.MaxPages} pages of Signals on one record.");
            }

            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords) return rows;
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    private sealed record DecisionRecordFacts(int? Class, string? Reason, DateTime? DecidedOn);

    private async Task<IReadOnlyDictionary<Guid, DecisionRecordFacts>> ReadDecisionRecordsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = new Dictionary<Guid, DecisionRecordFacts>();
        foreach (var chunk in ids.Chunk(InChunk))
        {
            var query = new QueryExpression("sprk_decisionrecord")
            {
                ColumnSet = new ColumnSet("sprk_recordclass", "sprk_reason", "sprk_decidedon"),
            };
            query.Criteria.AddCondition("sprk_decisionrecordid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                found[row.Id] = new DecisionRecordFacts(
                    row.GetAttributeValue<OptionSetValue>("sprk_recordclass")?.Value,
                    row.GetAttributeValue<string>("sprk_reason"),
                    row.GetAttributeValue<DateTime?>("sprk_decidedon") is { } on ? DateTime.SpecifyKind(on, DateTimeKind.Utc) : null);
            }
        }

        return found;
    }
}
