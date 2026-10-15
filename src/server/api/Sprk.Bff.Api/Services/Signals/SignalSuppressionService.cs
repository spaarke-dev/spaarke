using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
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
/// <param name="SuppressedUntil">The date the partition's own counted dismissals justify: the latest counted dismissal + 30 days that has
/// at least the threshold of counted dismissals within 30 days of each other ending at it, while that date is in the future; otherwise
/// null (#1661 round 2 F1: a suppression lasts its full 30 days, so this can be set while <paramref name="DismissalsCounted"/>, a trailing
/// count, has already fallen below the threshold). A stored <c>sprk_suppresseduntil</c> is never reported for its own sake (#1661 F1): a
/// stamp the partition's own dismissals do not justify is withdrawn, not shown.</param>
/// <param name="PolicyCountsTowardSuppression"><c>sprk_policy.sprk_countstowardsuppression</c> (default Yes).</param>
public sealed record SuppressionStatus(
    int DismissalsCounted, int DismissalsRecorded, DateTime? SuppressedUntil, bool PolicyCountsTowardSuppression)
{
    public int Threshold => SignalSuppressionService.ThresholdDismissals;

    public int WindowDays => SignalSuppressionService.WindowDays;
}

/// <summary>
/// The suppression grain (FR-17 as amended; D-34, D-36, D-39): (policy, core-record type, core-record id) for EVERY core type,
/// read from the Signal's generic columns; a Signal with no core record is its own grain, (policy, item). In the Do lane each grain is
/// further split by <see cref="SuppressionHolder"/> (D-145a), inside the service, so the grain a caller names is unchanged.
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

/// <summary>
/// Whose Do item a Signal is (owner D-145a, task 152; issue #1921): the Do-lane split of a grain, applied inside each secure-family
/// partition. A Do-lane Signal belongs to the person its subject is FOR, the contact in the subject table's "mine" person column
/// (<see cref="SignalReadService.DoLaneMineRules"/>, built on <see cref="OwnedChildWrite.ForPersonColumns"/>; D-129), read from the
/// subject NOW; a subject that names no individual (or whose table has no "mine" rule, or that cannot be found) is its own holder, the
/// per-item grain (D-39). Decide-lane Signals are not split (<see cref="Everyone"/>), so their grain is unchanged.
/// </summary>
/// <remarks>A dismissal counts toward a PERSON holder only when its Decision Record's decider (<c>sprk_assignedtointernal</c>, D-129) is
/// that person: the subject's person is read now, and a subject reassigned after a dismissal must not carry the old assignee's
/// dismissals to the new one (<see cref="SignalSuppressionService"/>, "Do lane").</remarks>
public readonly record struct SuppressionHolder
{
    private SuppressionHolder(Guid? personId, string? itemEntity, Guid? itemId)
    {
        PersonId = personId;
        ItemEntity = itemEntity;
        ItemId = itemId;
    }

    /// <summary>The contact the Do item is for; null for an item holder and for <see cref="Everyone"/>.</summary>
    public Guid? PersonId { get; }

    /// <summary>The subject table (lower case) of an item holder; null otherwise.</summary>
    public string? ItemEntity { get; }

    /// <summary>The subject id of an item holder; null otherwise.</summary>
    public Guid? ItemId { get; }

    /// <summary>Not split: every Decide-lane Signal.</summary>
    public static SuppressionHolder Everyone => default;

    /// <summary>A Do item for <paramref name="contactId"/>.</summary>
    public static SuppressionHolder Person(Guid contactId) => new(contactId, null, null);

    /// <summary>A Do item that names no individual: only its own dismissals count toward it (D-39).</summary>
    public static SuppressionHolder Item(string entity, Guid id) => new(null, entity.ToLowerInvariant(), id);

    public bool IsEveryone => PersonId is null && ItemEntity is null;

    public override string ToString() =>
        PersonId is { } person ? "person:" + person.ToString("D", CultureInfo.InvariantCulture)
        : ItemEntity is { } entity ? "item:" + entity + ":" + ItemId!.Value.ToString("D", CultureInfo.InvariantCulture)
        : "everyone";
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

    /// <summary>Warning: the read-time witness rule (task 038, D-123) could not place the families, or the caller's own read of the
    /// witness rows failed (task 151, D-142), so every detail on the worklist page is withheld (fail closed). Properties: Signal count
    /// and exception type, or the witness table and error code.</summary>
    public static readonly EventId WitnessReadFailed = new(50353, nameof(WitnessReadFailed));
}

/// <summary>What the nightly suppression pass hands the planner (#1661 round 2 F3).</summary>
/// <param name="Signals">The policy's Signals, placed in their partitions, with the stamps as corrected this pass.</param>
/// <param name="Failures">Failures to count against the run.</param>
/// <param name="PartitionsUnavailable">The partitions, the dismissal facts or a grain's count could not be read: nothing was written, and
/// the caller must plan NOTHING for the policy this pass (never a re-raise of every suppressed subject on one timeout).</param>
internal sealed record SuppressionReconciliation(IReadOnlyList<ExistingSignal> Signals, int Failures, bool PartitionsUnavailable)
{
    public void Deconstruct(out IReadOnlyList<ExistingSignal> signals, out int failures)
    {
        signals = Signals;
        failures = Failures;
    }
}

/// <summary>
/// The Signal a suppression count was asked for sits in the undetermined bucket (#1661 round 2 K5): which secure family it belongs to
/// cannot be known (for example a Secure-team-owned Signal with no secure root above it), so its count is never answered. Not a transient
/// fault: retrying does not change it until the record's placement is repaired.
/// </summary>
/// <remarks>§11: <i>Existing</i> — the route maps every other fault to one 503; <i>Extension</i> — a reason code on that 503 needs the
/// route to tell this case apart, which an exception type does without a second result channel; <i>Cost of doing nothing</i> — the user is
/// told to retry something a retry never fixes, and the log raises an Error for a data condition.</remarks>
public sealed class SuppressionPartitionUndeterminedException : InvalidOperationException
{
    public SuppressionPartitionUndeterminedException()
        : base("The Signal's secure-family partition cannot be determined, so its suppression count cannot be read.")
    {
    }
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
/// ordinary team still stop it (the resolver's own rule). A walk that is undetermined, or a Secure-team-owned Signal (for a hit not yet
/// written, a Secure-team-owned subject) with no root found, is <see cref="SuppressionPartition.Undetermined"/>; an ambiguous team or a
/// fault throws (fail closed). The same helper is used
/// by <see cref="ApplyAsync"/>, <see cref="ReconcileAsync"/>, the count read and the planner's hit partitions
/// (<see cref="PartitionsOfHitsAsync"/>).</para>
/// <para><b>The stamp, both ways.</b> At the threshold, <c>sprk_suppresseduntil</c> = the most recent counted dismissal + 30 days is
/// written on the partition's open Signals and on its dismissed Signals in the window (denormalized so the worklist query does not
/// count Decision Records per load; task 031's planner reads it for the re-raise rule, D-13). A stamp S is justified while at least
/// three counted dismissals within 30 days of each other end at S − 30 days (read over the last 60 days, #1661 round 2 F1), so it lasts
/// its full 30 days even after its oldest dismissal leaves the trailing count. A live stamp the partition's own counted dismissals do
/// not justify (a partition move, a stamp written before #1661, a policy switched to No) is lowered to the justified date or cleared, on
/// rows of any status (#1661 F1), and only while the row is as it was counted (round 2 K6). Only rows whose stamp would change are
/// written (D-61). This service does not implement the 14-day quiet window after a 1st or 2nd dismissal (FR-17a): that is task 031's
/// re-raise rule.</para>
/// <para><b>Do lane: per person (owner D-145a, task 152; issue #1921).</b> A dismissal of one person's Do item never suppresses another
/// person's Do item. Inside each partition, a Do-lane grain is split by <see cref="SuppressionHolder"/>: the person the subject is for
/// (the "mine" rule's person column), or the item itself when it names no individual (D-39). A holder is counted, stamped and muted from
/// its own rows only, and a person's count holds only the dismissals that person decided (the Decision Record's
/// <c>sprk_assignedtointernal</c>). Decide-lane grains are not split. No column is added: the holder is read from the subject, the
/// decider from the Decision Record.</para>
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
    /// lineage lookups, its owner as read (null for a hit: not written yet), and its subject. <paramref name="OwnerFromSubject"/> (a
    /// hit, #1661 round 2 N2): the Signal has no owner yet, so the SUBJECT's owner stands in for it in the "Secure-owned with no root
    /// found is undetermined" rule.</summary>
    private readonly record struct PartitionItem(Entity Signal, EntityReference? Owner, string? SubjectEntity, Guid SubjectId, bool OwnerFromSubject = false);

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
    /// when the core record is a matter (no owner yet: the writer's resolver decides it from the same truth, so the subject's owner stands
    /// in for it). Ontology task 144 (uac-r2 #1746 K7): a Signal now carries the core-ancestor stamp of its subject's family
    /// (<see cref="CoreAncestorResolver.StampSourceColumns"/>), and the core record is one of the records that stamp names (the writer
    /// takes it from the subject's stamps), so the probe carries the core's stamp column too: a project or work-assignment core is reached
    /// directly, as a matter core is through <c>sprk_matter</c>, and a hit on a secure one is placed in its secure partition, never
    /// Ordinary.</summary>
    private static PartitionItem ItemOf(string policyCode, SignalHit hit)
    {
        // partition probe, never created; id is the hit's identity (182 census: not a create site)
        var row = new Entity("sprk_signal", ProbeId(policyCode, hit));
        if (SignalWriter.RegardingLookupByEntity.TryGetValue(hit.SubjectEntity, out var column))
        {
            row[column] = new EntityReference(hit.SubjectEntity, hit.SubjectId);
        }

        if (hit.Core is { Entity: "sprk_matter" } core) row["sprk_matter"] = new EntityReference("sprk_matter", core.Id);
        if (hit.Core is { Entity: { } coreEntity, Id: var coreId } && coreId != Guid.Empty
            && CoreAncestorResolver.CoreAncestorLookups.FirstOrDefault(l => l.EntityType == coreEntity).LookupAttribute is { } stampColumn
            && !row.Contains(stampColumn))
        {
            row[stampColumn] = new EntityReference(coreEntity, coreId);
        }

        return new PartitionItem(row, null, hit.SubjectEntity, hit.SubjectId, OwnerFromSubject: true);
    }

    /// <summary>Fixed namespace of <see cref="ProbeId"/> (random v4 Guid, generated once; never change it: ids derive from it).</summary>
    private static readonly Guid ProbeNamespace = new("5c1f6a52-3b0e-4f0b-9d2a-7e4a1c8b6d30");

    /// <summary>
    /// The probe row's id: a name-based (RFC 4122 version 5, SHA-1) Guid over <see cref="ProbeNamespace"/> and the hit's dedupe key
    /// (<see cref="SignalWriter.BuildDedupeKey"/>, episode 0: a probe belongs to no episode, only to the policy and subject). The walk de-duplicates
    /// rows by (table, id) (<see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>), so every hit needs an id of its own,
    /// and the same hit gets the same id on every call.
    /// </summary>
    internal static Guid ProbeId(string policyCode, SignalHit hit)
    {
        var name = SignalWriter.BuildDedupeKey(
            policyCode, hit.SubjectEntity, hit.SubjectId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant(), 0);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        ProbeNamespace.TryWriteBytes(input, bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);
        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var id = hash.AsSpan(0, 16).ToArray();
        id[6] = (byte)((id[6] & 0x0F) | 0x50); // version 5
        id[8] = (byte)((id[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(id, bigEndian: true);
    }

    /// <summary>
    /// THE partition truth (#1661 R3/R4), one helper for the count, the stamps and the planner. Per item: the secure roots above the
    /// Signal row and its subject row (uac-r2's <see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>, never re-implemented);
    /// undetermined, or a Secure-team-owned Signal with no root found (for a hit, a Secure-team-owned SUBJECT, round 2 N2):
    /// <see cref="SuppressionPartition.Undetermined"/>. No Secure team in this environment: every item is ordinary. An ambiguous team, a
    /// walk that does not complete or a fault throws: the caller never places part of a set (round 2 F2/F3). Every distinct lineage is
    /// walked in ONE run of uac-r2's walker (<see cref="SecureChildShareSynchronizer.SecureRootsAboveEachAsync"/>), so each parent row
    /// is read once per call whatever the number of items (task 038). <paramref name="knownSecureTeam"/>: the Secure team the caller
    /// already identified in this request (by <see cref="ResolveSecureTeamAsync"/>), so it is not identified again.
    /// <paramref name="readAsSubjects"/> (uac-r2 K1 on ontology #1891; the read-time rule only): rows the caller read with
    /// <see cref="ReadSubjectRowsAsync"/>. When given, those rows and the subjects read here are handed to the walker, which then
    /// does not read them again; null (every other caller) hands it nothing.
    /// </summary>
    private async Task<SuppressionPartition[]> PartitionsAsync(
        IReadOnlyList<PartitionItem> items, CancellationToken ct, Guid? knownSecureTeam = null, IReadOnlyCollection<Entity>? readAsSubjects = null)
    {
        var result = new SuppressionPartition[items.Count];
        if (items.Count == 0) return result;

        var secureTeamId = knownSecureTeam ?? await ResolveSecureTeamAsync(ct).ConfigureAwait(false);
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
        bool SecureOwned(int i) => IsSecureOwned(items[i].Owner, secureTeamId)
            || (items[i].OwnerFromSubject && items[i].SubjectEntity is { } entity
                && subjects.TryGetValue((entity.ToLowerInvariant(), items[i].SubjectId), out var subject)
                && IsSecureOwned(subject.GetAttributeValue<EntityReference>("owningteam"), secureTeamId));

        using var scope = _scopes.CreateScope();
        var walker = scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>();

        // Rows with the same lookups sit under the same roots (the walk ignores the owners of the rows it is given), so each distinct
        // lineage is walked once, and all of them share ONE run (one read cache).
        var keys = lineages.Select(LineageKey).ToList();
        var distinct = keys.Select((key, i) => (key, i)).DistinctBy(k => k.key, StringComparer.Ordinal).ToList();

        // uac-r2 K1 on ontology #1891: the rows already read with the walk's columns (SubjectColumns), so it does not read them again.
        var readRows = readAsSubjects?.Concat(subjects.Values)
            .Select(row => (row, SubjectColumns(SecureChildLineage.Children[row.LogicalName])))
            .ToList();
        var answers = await walker.SecureRootsAboveEachAsync(
            distinct.Select(k => (IReadOnlyCollection<Entity>)lineages[k.i]).ToList(), secureTeamId.Value, ct, readRows).ConfigureAwait(false);
        var byLineage = new Dictionary<string, SuppressionPartition>(StringComparer.Ordinal);
        for (var d = 0; d < distinct.Count; d++)
        {
            var above = RequireAnswer(answers[d]);
            byLineage[distinct[d].key] = above.Undetermined.Count > 0 ? SuppressionPartition.Undetermined : SuppressionPartition.Secure(above.Roots);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var found = byLineage[keys[i]];
            result[i] = found.IsOrdinary && SecureOwned(i) ? SuppressionPartition.Undetermined : found;
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
                    ColumnSet = SubjectColumns(table),
                    NoLock = true,
                };
                query.Criteria.AddCondition(table.IdColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
                var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                foreach (var row in rows.Entities) found[(table.LogicalName, row.Id)] = row;
            }
        }

        return found;
    }

    /// <summary>What <see cref="ReadSubjectRowsAsync"/> reads of a subject row: its lineage lookups and its owner (what the walk reads).</summary>
    private static ColumnSet SubjectColumns(SecureChildLineage.Table table) =>
        new(table.Lookups.Keys.Append("owningteam").Append("owninguser").ToArray());

    /// <summary>
    /// #1661 R1: the partition each hit's NEW episode will belong to, from the same truth as the rows (the hit's Signal as the writer
    /// would write it, plus its subject). Every hit is returned, an undetermined one as <see cref="SuppressionPartition.Undetermined"/>:
    /// the undetermined bucket never mutes and is never muted, so the planner raises it and the writer's resolver decides its owner
    /// (round 2 F2: its outcome never depends on whether another family's stamp is on the record). Null when the partitions cannot be
    /// computed at all (an ambiguous Secure team, a walk that does not complete, a fault): the caller then plans NOTHING for the policy
    /// this pass (round 2 F2/F3), so the outcome is the same for every hit whatever its record carries.
    /// </summary>
    internal async Task<IReadOnlyDictionary<(string SubjectEntity, Guid SubjectId), SuppressionPartition>?> PartitionsOfHitsAsync(
        string policyCode, IReadOnlyList<SignalHit> hits, CancellationToken ct)
    {
        var placed = new Dictionary<(string, Guid), SuppressionPartition>();
        if (hits.Count == 0) return placed;

        SuppressionPartition[] partitions;
        try
        {
            partitions = await PartitionsAsync(hits.Select(h => ItemOf(policyCode, h)).ToList(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(policyCode, ex);
            return null;
        }

        for (var i = 0; i < hits.Count; i++) placed[(hits[i].SubjectEntity, hits[i].SubjectId)] = partitions[i];
        return placed;
    }

    /// <summary>
    /// D-145a (task 152): whose Do item each hit's new episode is (<see cref="SuppressionHolder"/>), read from its subject now, so a stamp
    /// mutes a hit only within the same person's (or the same item's) Do items. Asked only for Do-lane hits on a grain that carries a live
    /// stamp. Keyed like <see cref="PartitionsOfHitsAsync"/>. Null on a fault: the caller then plans nothing for the policy this pass,
    /// exactly as for a partition fault (a hit is never answered per hit on a guess).
    /// </summary>
    internal async Task<IReadOnlyDictionary<(string SubjectEntity, Guid SubjectId), SuppressionHolder>?> HoldersOfHitsAsync(
        string policyCode, IReadOnlyList<SignalHit> hits, CancellationToken ct)
    {
        var placed = new Dictionary<(string, Guid), SuppressionHolder>();
        if (hits.Count == 0) return placed;

        IReadOnlyDictionary<(string, Guid), SuppressionHolder> holders;
        try
        {
            holders = await HoldersOfSubjectsAsync(hits.Select(h => (h.SubjectEntity, h.SubjectId)), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(policyCode, ex);
            return null;
        }

        foreach (var hit in hits) placed[(hit.SubjectEntity, hit.SubjectId)] = HolderOf(holders, hit.SubjectEntity, hit.SubjectId);
        return placed;
    }

    /// <summary>
    /// D-145a: each Do-lane subject's holder, from the "mine" rule's person column of its table (<see cref="SignalReadService.DoLaneMineRules"/>,
    /// never a second map), read app-only in batches. A subject whose table has no "mine" rule, whose row is absent, or whose person
    /// column is empty is its own item holder. A fault throws (fail closed: the caller writes nothing).
    /// </summary>
    private async Task<IReadOnlyDictionary<(string, Guid), SuppressionHolder>> HoldersOfSubjectsAsync(
        IEnumerable<(string Entity, Guid Id)> subjects, CancellationToken ct)
    {
        var found = new Dictionary<(string, Guid), SuppressionHolder>();
        var wanted = subjects
            .Where(s => s.Id != Guid.Empty && SignalReadService.DoLaneMineRules.ContainsKey(s.Entity))
            .Select(s => (Entity: s.Entity.ToLowerInvariant(), s.Id))
            .Distinct()
            .GroupBy(s => s.Entity, StringComparer.Ordinal);
        foreach (var group in wanted)
        {
            var rule = SignalReadService.DoLaneMineRules[group.Key];
            foreach (var chunk in group.Select(s => s.Id).Chunk(InChunk))
            {
                // Every subject asked is answered (the item itself unless its row names a person), so a later count does not ask again.
                foreach (var id in chunk) found[(rule.Table, id)] = SuppressionHolder.Item(rule.Table, id);
                var query = new QueryExpression(rule.Table) { ColumnSet = new ColumnSet(rule.PersonColumn), NoLock = true };
                query.Criteria.AddCondition(rule.Table + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());
                var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                foreach (var row in rows.Entities)
                {
                    if (row.GetAttributeValue<EntityReference>(rule.PersonColumn) is { Id: var person } && person != Guid.Empty)
                    {
                        found[(rule.Table, row.Id)] = SuppressionHolder.Person(person);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The holder of one subject from <see cref="HoldersOfSubjectsAsync"/>'s answer: its person, else the item itself.</summary>
    private static SuppressionHolder HolderOf(IReadOnlyDictionary<(string, Guid), SuppressionHolder> holders, string entity, Guid id) =>
        holders.TryGetValue((entity.ToLowerInvariant(), id), out var holder) ? holder : SuppressionHolder.Item(entity, id);

    /// <summary>The holder of a Signal row read by <see cref="ReadGrainSignalsAsync"/> (its subject from the dedupe key); a row whose key
    /// cannot be read is its own holder, so it never pools with another.</summary>
    private static SuppressionHolder HolderOf(IReadOnlyDictionary<(string, Guid), SuppressionHolder> holders, Entity signal) =>
        SignalWriter.TryParseDedupeKey(signal.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subjectId, out _)
            ? HolderOf(holders, entity, subjectId)
            : SuppressionHolder.Item(signal.LogicalName, signal.Id);

    private static (string Entity, Guid Id)? SubjectOf(Entity signal) =>
        SignalWriter.TryParseDedupeKey(signal.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subjectId, out _)
            ? (entity, subjectId)
            : null;

    /// <summary>
    /// Ontology task 142 (child-record contract C9, acceptance 7; uac-r2 #1665 N1 / #1778): the secure family of each of
    /// <paramref name="signalIds"/>, in order, from THE partition truth (<see cref="PartitionsAsync"/>: the secure roots above the Signal
    /// row and its subject, uac-r2's own walk). The decision commit route refuses a Decision Record whose Signals are not all in one
    /// family. Each Signal is read app-only with its lineage lookups, owner and dedupe key (the subject). Never a partial answer: a read
    /// fault, a missing Signal, an ambiguous Secure team or a walk that does not complete throws, and the caller refuses (fail closed);
    /// an undetermined Signal is <see cref="SuppressionPartition.Undetermined"/>.
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: <see cref="PartitionsAsync"/> is the one partition truth;
    /// this only reads the rows it is given. Extension: the existing entry points take hits or a grain, not a set of Signal ids.
    /// Cost of doing nothing: one Decision Record could resolve Signals of two secure families, filing one family's decision where
    /// the other's members read it.</remarks>
    internal async Task<IReadOnlyList<SuppressionPartition>> PartitionsOfSignalsAsync(IReadOnlyList<Guid> signalIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signalIds);
        var items = new List<PartitionItem>(signalIds.Count);
        foreach (var id in signalIds)
        {
            var signal = await _entities.RetrieveAsync("sprk_signal", id, ["sprk_dedupekey", "ownerid", .. SignalLineageColumns], ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Signal {id} could not be read, so its secure family is unknown.");
            items.Add(ItemOf(signal));
        }

        return await PartitionsAsync(items, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ontology task 145 (D-123; uac-r2 #1747 finding e): each hit's witness lines as its Signal may carry them. A witness line's
    /// detail (<see cref="SignalEvidenceRef.Text"/>, the communication's subject, and <see cref="SignalEvidenceRef.Source"/>, its sender)
    /// is kept only when the witness row's secure family is KNOWN and EQUAL to the family of the Signal the hit will write; otherwise
    /// both are cleared and the rest of the line (kind, ref, as-of, clause) stays, so the planner's dated-evidence rule (D-119) and the
    /// line itself are unchanged. Both families come from THE partition truth (<see cref="PartitionsAsync"/>: uac-r2's walk above the
    /// hit's Signal as the writer would write it plus its subject, and above the witness row itself). Undetermined counts as secure: an
    /// undetermined family on either side, a witness row that cannot be read, a ref that names no lineage table, or a fault clears the
    /// detail (fail closed; a fault clears it on every line of the call, logged once at Warning). Keyed like
    /// <see cref="PartitionsOfHitsAsync"/>: by the hit's (subject table, subject id).
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: <see cref="PartitionsAsync"/> (the one partition truth) and
    /// <see cref="ReadSubjectRowsAsync"/> (a lineage row read by table and id); this only applies them to the witness rows. Extension: the
    /// existing entry points place Signals and hits, never the related rows a hit's evidence quotes. Cost of doing nothing: a matter
    /// Signal owned by the matter's business-unit team carries the subject and sender of a communication that is secure through another
    /// filing, so a non-member of that family reads them (D-123).</remarks>
    internal async Task<IReadOnlyDictionary<(string SubjectEntity, Guid SubjectId), IReadOnlyList<SignalEvidenceRef>>> EvidenceWithinPartitionAsync(
        string policyCode, IReadOnlyList<SignalHit> hits, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hits);
        var result = new Dictionary<(string, Guid), IReadOnlyList<SignalEvidenceRef>>();
        foreach (var hit in hits) result[(hit.SubjectEntity, hit.SubjectId)] = hit.Evidence;

        var detailed = hits.Where(h => h.Evidence.Any(HasDetail)).ToList();
        if (detailed.Count == 0) return result;

        try
        {
            // No Secure Record owner team in this environment: every family is ordinary (PartitionsAsync's own rule), nothing to read.
            if (await ResolveSecureTeamAsync(ct).ConfigureAwait(false) is null) return result;

            var hitPartitions = await PartitionsAsync(detailed.Select(h => ItemOf(policyCode, h)).ToList(), ct).ConfigureAwait(false);

            var familyOfWitness = await WitnessFamiliesAsync(
                detailed.SelectMany(h => h.Evidence).Where(HasDetail).Select(l => l.Ref), ct).ConfigureAwait(false);

            var cleared = 0;
            for (var i = 0; i < detailed.Count; i++)
            {
                var signalFamily = hitPartitions[i];
                result[(detailed[i].SubjectEntity, detailed[i].SubjectId)] = detailed[i].Evidence.Select(line =>
                {
                    if (!HasDetail(line) || KeepsDetail(signalFamily, line.Ref, familyOfWitness)) return line;
                    cleared++;
                    return line with { Text = null, Source = null };
                }).ToList();
            }

            if (cleared > 0)
            {
                _logger.LogInformation(
                    "Policy {PolicyCode}: the detail of {Count} witness line(s) was left off their Signals because the witness is in another (or an undetermined) secure family (D-123).",
                    policyCode, cleared);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(SuppressionEvents.Failed, ex,
                "Policy {PolicyCode}: the secure families of the Signals' witnesses could not be read ({ExceptionType}); every witness line is written without its detail (D-123, fail closed).",
                policyCode, ex.GetType().Name);
            foreach (var hit in detailed)
            {
                result[(hit.SubjectEntity, hit.SubjectId)] = hit.Evidence.Select(l => HasDetail(l) ? l with { Text = null, Source = null } : l).ToList();
            }
        }

        return result;

        static bool HasDetail(SignalEvidenceRef line) => line.Text is not null || line.Source is not null;
    }

    /// <summary>The stored evidence column (<see cref="SignalWriter"/> writes it on create; <see cref="StoredEvidenceToRedactAsync"/> reads it).</summary>
    internal const string EvidenceColumn = "sprk_evidencerefs";

    /// <summary>
    /// Ontology task 145, review F1 (D-123; main session 2026-10-14, option A): the STORED evidence of existing Signals, open and
    /// resolved. A Signal's evidence is written once, at create (<see cref="EvidenceWithinPartitionAsync"/>), so a witness re-filed into
    /// a secure family afterwards (or a Signal whose own family moved) would keep detail the Signal's readers may no longer see. Each
    /// Signal is placed by <see cref="PartitionsAsync"/> from its row as read, and every stored line that still carries a <c>text</c> or
    /// <c>source</c> keeps them only by the same rule as a create (<see cref="KeepsDetail"/>); otherwise both properties are removed and
    /// everything else on the line (kind, ref, tier, confidence, asOf, clause, and any property this code does not know) stays as stored.
    /// Returns the redacted <c>sprk_evidencerefs</c> JSON of each Signal that lost detail; a Signal that keeps everything is absent.
    /// <b>Monotonic</b>: detail is only ever removed, never added or restored. <b>A fault</b> (the families could not be read) returns
    /// null, logged at Warning: the caller writes nothing this pass, so a fault neither strips detail that may be within its family nor
    /// widens anything (the next pass retries). Stored evidence that is not a JSON array is left as it is.
    /// </summary>
    internal async Task<IReadOnlyDictionary<Guid, string>?> StoredEvidenceToRedactAsync(
        string policyCode, IReadOnlyList<ExistingSignal> signals, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var result = new Dictionary<Guid, string>();
        var stored = new List<(ExistingSignal Signal, JsonArray Lines)>();
        foreach (var signal in signals)
        {
            if (StoredLines(signal.LineageRow?.GetAttributeValue<string>(EvidenceColumn)) is { } lines
                && lines.OfType<JsonObject>().Any(HasStoredDetail))
            {
                stored.Add((signal, lines));
            }
        }

        if (stored.Count == 0) return result;

        try
        {
            // No Secure Record owner team in this environment: every family is ordinary (PartitionsAsync's own rule), nothing to clear.
            if (await ResolveSecureTeamAsync(ct).ConfigureAwait(false) is null) return result;

            var signalFamilies = await PartitionsAsync(stored.Select(s => ItemOf(s.Signal)).ToList(), ct).ConfigureAwait(false);
            var familyOfWitness = await WitnessFamiliesAsync(
                stored.SelectMany(s => s.Lines).OfType<JsonObject>().Where(HasStoredDetail).Select(RefOf), ct).ConfigureAwait(false);

            var cleared = 0;
            for (var i = 0; i < stored.Count; i++)
            {
                var changed = false;
                foreach (var line in stored[i].Lines.OfType<JsonObject>().Where(HasStoredDetail).ToList())
                {
                    if (KeepsDetail(signalFamilies[i], RefOf(line), familyOfWitness)) continue;
                    line.Remove("text");
                    line.Remove("source");
                    changed = true;
                    cleared++;
                }

                if (changed) result[stored[i].Signal.Id] = stored[i].Lines.ToJsonString();
            }

            if (cleared > 0)
            {
                _logger.LogInformation(
                    "Policy {PolicyCode}: the detail of {Count} stored witness line(s) on {Signals} Signal(s) is no longer within the Signal's secure family and is cleared (D-123).",
                    policyCode, cleared, result.Count);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(SuppressionEvents.Failed, ex,
                "Policy {PolicyCode}: the secure families of the stored evidence could not be read ({ExceptionType}); no stored evidence is changed this pass (the next pass retries).",
                policyCode, ex.GetType().Name);
            return null;
        }

        static JsonArray? StoredLines(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonNode.Parse(json) as JsonArray;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        static bool HasStoredDetail(JsonObject line) => line["text"] is not null || line["source"] is not null;

        static string? RefOf(JsonObject line) => line["ref"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>The secure family of each witness the <paramref name="references"/> name (<see cref="WitnessOf"/>), from its row as read and
    /// THE partition truth. A witness that names no lineage table, or whose row cannot be read, is absent (its detail is never kept).</summary>
    private async Task<Dictionary<(string, Guid), SuppressionPartition>> WitnessFamiliesAsync(IEnumerable<string?> references, CancellationToken ct)
    {
        var familyOfWitness = new Dictionary<(string, Guid), SuppressionPartition>();
        var (readable, items) = await WitnessItemsAsync(references, ct).ConfigureAwait(false);
        if (readable.Count == 0) return familyOfWitness;

        var witnessPartitions = await PartitionsAsync(items, ct).ConfigureAwait(false);
        for (var i = 0; i < readable.Count; i++) familyOfWitness[readable[i]] = witnessPartitions[i];
        return familyOfWitness;
    }

    /// <summary>The witnesses the <paramref name="references"/> name (<see cref="WitnessOf"/>) whose rows can be read, and each one's
    /// partition item (its row as read, with its lineage lookups, and its owner): what <see cref="PartitionsAsync"/> places. Shared by
    /// <see cref="WitnessFamiliesAsync"/> and the read-time rule (<see cref="WitnessDetailShownOnReadAsync"/>, task 038), so a witness is
    /// placed one way everywhere: from its roots, never from its owner alone.</summary>
    private async Task<(List<(string Table, Guid Id)> Readable, List<PartitionItem> Items)> WitnessItemsAsync(
        IEnumerable<string?> references, CancellationToken ct)
    {
        var witnesses = references.Select(WitnessOf).OfType<(string Table, Guid Id)>().Distinct().ToList();
        if (witnesses.Count == 0) return ([], []);

        // ReadSubjectRowsAsync reads only the subject pair; the item's row is an in-memory reference to the witness, never written.
        var rows = await ReadSubjectRowsAsync(
            witnesses.Select(w => new PartitionItem(new Entity(w.Table, w.Id), null, w.Table, w.Id)).ToList(), ct).ConfigureAwait(false);
        var readable = witnesses.Where(rows.ContainsKey).ToList();
        return (readable, readable.Select(w => new PartitionItem(rows[w], rows[w].GetAttributeValue<EntityReference>("owningteam"), null, Guid.Empty)).ToList());
    }

    /// <summary>D-123: a witness line keeps its detail only when the Signal's family and the witness's are both KNOWN and EQUAL
    /// (Undetermined counts as secure and equals nothing; a witness with no known family keeps nothing).</summary>
    private static bool KeepsDetail(
        SuppressionPartition signalFamily, string? reference, IReadOnlyDictionary<(string, Guid), SuppressionPartition> familyOfWitness) =>
        !signalFamily.IsUndetermined
        && WitnessOf(reference) is { } witness
        && familyOfWitness.TryGetValue(witness, out var witnessFamily)
        && !witnessFamily.IsUndetermined
        && witnessFamily == signalFamily;

    /// <summary>The (table, id) a witness line's <see cref="SignalEvidenceRef.Ref"/> (<c>{table}:{id}</c>, as the evaluator writes it)
    /// names, when the table is a lineage child the walk can start from; otherwise null (its detail is then never kept). The table is
    /// lower-cased. The Signal read route's caller read (task 151, D-142) keys its answer by this same pair.</summary>
    internal static (string Table, Guid Id)? WitnessOf(string? reference)
    {
        var separator = reference?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (separator <= 0) return null;
        var table = reference![..separator].ToLowerInvariant();
        return SecureChildLineage.IsChild(table) && !string.Equals(table, SignalLineage.LogicalName, StringComparison.Ordinal)
            && Guid.TryParse(reference[(separator + 1)..], out var id) && id != Guid.Empty
                ? (table, id)
                : null;
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
    /// <para><b>Do lane (D-145a, task 152; D-141).</b> The count is the anchor's HOLDER's (<see cref="SuppressionHolder"/>) within its
    /// partition: a Do item for a person reports only that person's own counted dismissals, and only to that person
    /// (<paramref name="callerContactId"/>, the caller's linked contact the "mine" read resolved); any other caller, including one the
    /// "mine" rule admits as the item's owner, creator or assigner, is told nothing and that its dismissal counts toward nothing (an empty
    /// status whose <see cref="SuppressionStatus.PolicyCountsTowardSuppression"/> is false). So is the caller of a Do Signal whose policy
    /// is no longer in the Do lane. An item with no individual reports its own per-item count.</para>
    /// </summary>
    public async Task<SuppressionStatus?> GetStatusForSignalAsync(
        Guid signalId, IDataverseUserClient caller, CancellationToken ct, Guid? callerContactId = null)
    {
        var signal = await _entities.RetrieveAsync("sprk_signal", signalId,
            ["sprk_policy", "sprk_corerecordtype", "sprk_corerecordid", "sprk_dedupekey", "ownerid", "sprk_lane", .. SignalLineageColumns], ct).ConfigureAwait(false);

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

        var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_policycode", "sprk_lane"], ct).ConfigureAwait(false);
        if (policy.GetAttributeValue<string>("sprk_policycode") is not { Length: > 0 } code)
        {
            return null; // a no-core grain is keyed by the policy code: without it the count would be silently zero
        }

        // Task 152 review K3: the count is split by the POLICY's lane, the route's "mine" gate by the SIGNAL's. A Do Signal of a policy
        // since switched to another lane would be answered from the unsplit (policy, core) count, colleagues' dismissals included: it is
        // answered as a dismissal that counts toward nothing instead.
        if (signal.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value == SignalWriter.LaneDo
            && policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value != SignalWriter.LaneDo)
        {
            return CountsTowardNothing;
        }

        // The count is the CALLER's: the grain's Signals are trimmed to those the caller's own read returns, then Decision-Record facts
        // are read app-only for those rows only. And it is the anchor Signal's PARTITION's (#1661): the anchor is placed by the same truth
        // as every row, and only rows of that partition are counted, so the answer never carries a number or a date from across the wall.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var visible = await ReadVisibleSignalIdsAsync(caller, policyId, code, grain, ReadSince(now), now, ct).ConfigureAwait(false);
        var assessed = await AssessAsync(policyId, code, grain, policyCounts: null, visible, known: null, anchor: signal, ct).ConfigureAwait(false);
        if (assessed.Anchor is not { IsUndetermined: false } anchor)
        {
            throw new SuppressionPartitionUndeterminedException();
        }

        if (assessed.AnchorHolder.PersonId is { } person && person != callerContactId)
        {
            // D-145a / D-141: another person's Do-lane count is never reported, and this caller's dismissal of it counts toward no one
            // (it is not theirs to count, review F1), so the wizard says it does not count rather than "1 of 3".
            return CountsTowardNothing;
        }

        return assessed.Partitions.FirstOrDefault(p => p.Partition == anchor && p.Holder == assessed.AnchorHolder).Assessment?.Status
            ?? new SuppressionStatus(0, 0, null, assessed.PolicyCounts);
    }

    /// <summary>The count for a caller whose dismissal of this Signal counts toward no one (task 152): nothing counted, nothing reported, and
    /// <see cref="SuppressionStatus.PolicyCountsTowardSuppression"/> false so the wizard does not promise a count.</summary>
    private static SuppressionStatus CountsTowardNothing => new(0, 0, null, false);

    /// <summary>
    /// Counts the pairing and brings its stamps into line, in every partition (#1661, D-123): at the threshold the partition's open and
    /// in-window dismissed Signals are stamped, and any live stamp the partition's own dismissals do not justify is lowered or cleared
    /// (F1, on rows of any status). Idempotent: a second call writes nothing. Returns the status of <paramref name="report"/> (default the
    /// ordinary partition: the only one a non-member may be told about; an empty status when it has no Signal). In the Do lane every
    /// holder of every partition is brought into line (D-145a), and the status reported is the first holder's of that partition (the
    /// decision route ignores it; the count route is <see cref="GetStatusForSignalAsync"/>).
    /// </summary>
    public async Task<SuppressionStatus> ApplyAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, CancellationToken ct, SuppressionPartition? report = null)
    {
        var assessed = await AssessAsync(policyId, policyCode, grain, policyCounts: null, visibleSignalIds: null, known: null, anchor: null, ct).ConfigureAwait(false);
        foreach (var (_, _, assessment) in assessed.Partitions) await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);

        var wanted = report ?? SuppressionPartition.Ordinary;
        return assessed.Partitions.FirstOrDefault(p => p.Partition == wanted).Assessment?.Status
            ?? new SuppressionStatus(0, 0, null, assessed.PolicyCounts);
    }

    /// <summary>
    /// The nightly pass (task 031 calls it once per policy with the Signals it already read and the policy's
    /// <c>sprk_countstowardsuppression</c>). First every Signal of a grain that has a live stamp, or enough dismissals in the read window
    /// for some partition to reach the threshold, is placed in its partition (<see cref="ExistingSignal.Partition"/>, which the planner
    /// reads), and the Decision-Record facts of their dismissals are read in one batch. Then, in memory and with the SAME rule as the exact
    /// count (<see cref="Assess"/>, round 2 N1/F4), a (grain, partition) is worked only when that rule would change one of its stamps.
    /// Every worked grain is then counted exactly from a fresh read BEFORE anything is written; its stamps are raised, lowered or cleared
    /// and only rows that change are written (D-61). Returns the Signals with the corrected stamps, so the planner that runs next sees them
    /// even if a write failed, and the number of failures (the run is then not successful, and the next run retries).
    /// </summary>
    /// <remarks>Fail closed, the same way for every partition (#1661 round 2 F3, K1): when the partitions, the Decision-Record facts or
    /// ANY worked grain's count cannot be read, <see cref="SuppressionReconciliation.PartitionsUnavailable"/> is set, NOTHING is written
    /// (no stamp is raised, lowered or cleared), and the caller must plan nothing for the policy this pass. Withholding the stamps instead
    /// would let the planner re-raise every suppressed subject of the policy on one timeout.
    /// <para><b>Do lane (D-145a, task 152).</b> <paramref name="doLane"/> (the policy's <c>sprk_lane</c>; the job passes it) splits every
    /// partition by <see cref="SuppressionHolder"/> (<see cref="ExistingSignal.Holder"/>, which the planner reads). A stamp written before
    /// this split, on (policy, core record) for every Do item alike, is therefore RE-DERIVED on the first pass: it stays on the rows of a
    /// holder whose own dismissals justify it, and is lowered or withdrawn on every other holder's rows (only rows that change are written,
    /// D-61). A holder read fault is a partition fault (fail closed, nothing written).</para></remarks>
    internal async Task<SuppressionReconciliation> ReconcileAsync(
        Guid policyId, string policyCode, bool policyCountsTowardSuppression, IReadOnlyList<ExistingSignal> existing, CancellationToken ct,
        bool doLane = false)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var readSince = ReadSince(now);
        var trailingSince = now.AddDays(-WindowDays - 1);

        bool Live(ExistingSignal s) => s.SuppressedUntil is { } u && u > now;
        static bool DismissedSince(ExistingSignal s, DateTime since) =>
            s.Resolution == SignalWriter.ResolutionDismissed && s.ResolvedOn is { } on && on >= since;

        // Partitioning only splits a count, so a grain that cannot reach the threshold in total (three distinct dismissals in the read
        // window, the latest in the trailing window), with no live stamp, needs nothing at all.
        var grains = existing.GroupBy(GrainKeyOf)
            .Where(g => g.Any(Live)
                || (policyCountsTowardSuppression && g.Any(s => DismissedSince(s, trailingSince))
                    && g.Where(s => DismissedSince(s, readSince)).Select(s => s.DecisionRecordId ?? s.Id).Distinct().Count() >= ThresholdDismissals))
            .ToList();
        if (grains.Count == 0) return new SuppressionReconciliation(existing, 0, PartitionsUnavailable: false);

        var rows = grains.SelectMany(g => g).ToList();
        var partitionOf = new Dictionary<Guid, SuppressionPartition>();
        IReadOnlyDictionary<(string, Guid), SuppressionHolder>? holders = null;
        IReadOnlyDictionary<Guid, DecisionRecordFacts> records;
        try
        {
            var partitions = await PartitionsAsync(rows.Select(ItemOf).ToList(), ct).ConfigureAwait(false);
            for (var i = 0; i < rows.Count; i++) partitionOf[rows[i].Id] = partitions[i];
            if (doLane) holders = await HoldersOfSubjectsAsync(rows.Select(s => (s.SubjectEntity, s.SubjectId)), ct).ConfigureAwait(false);

            // The facts the exact rule needs (class, reason, decided-on), for the dismissals of the partitions that may carry a stamp.
            records = policyCountsTowardSuppression
                ? await ReadDecisionRecordsAsync(rows
                    .Where(s => !partitionOf[s.Id].IsUndetermined && DismissedSince(s, readSince) && s.DecisionRecordId is not null)
                    .Select(s => s.DecisionRecordId!.Value).Distinct().ToList(), ct).ConfigureAwait(false)
                : new Dictionary<Guid, DecisionRecordFacts>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(policyCode, ex);
            return new SuppressionReconciliation(existing, 1, PartitionsUnavailable: true);
        }

        existing = existing.Select(s => partitionOf.TryGetValue(s.Id, out var p)
            ? s with { Partition = p, Holder = holders is null ? SuppressionHolder.Everyone : HolderOf(holders, s.SubjectEntity, s.SubjectId) }
            : s).ToList();

        var candidates = existing
            .Where(s => s.Partition is not null)
            .GroupBy(s => (Grain: GrainKeyOf(s), Partition: s.Partition!.Value, Holder: s.Holder ?? SuppressionHolder.Everyone))
            .Where(g => Assess(g.Select(RowOf).ToList(), records, policyCountsTowardSuppression,
                stampable: policyCountsTowardSuppression && !g.Key.Partition.IsUndetermined, now, g.Key.Holder).Changes.Any())
            .Select(g => g.Key.Grain)
            .Distinct()
            .ToList();
        if (candidates.Count == 0) return new SuppressionReconciliation(existing, 0, PartitionsUnavailable: false);

        // Every worked grain is counted before anything is written: a count that fails leaves the policy as it was, whole.
        var assessed = new List<GrainAssessment>();
        foreach (var (type, core, entity, subject) in candidates)
        {
            try
            {
                assessed.Add(await AssessAsync(policyId, policyCode, new SuppressionGrain(type, core, entity, subject), policyCountsTowardSuppression,
                    visibleSignalIds: null, partitionOf, anchor: null, ct, doLane, holders).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LogFailure(policyCode, ex);
                return new SuppressionReconciliation(existing, 1, PartitionsUnavailable: true);
            }
        }

        // In memory first: the planner must see the corrected stamps this pass even when a write below fails.
        var changed = new Dictionary<Guid, DateTime?>();
        foreach (var (_, _, assessment) in assessed.SelectMany(a => a.Partitions))
        {
            foreach (var (id, to) in assessment.Changes) changed[id] = to;
        }

        var failures = 0;
        foreach (var grainAssessment in assessed)
        {
            try
            {
                foreach (var (_, _, assessment) in grainAssessment.Partitions) await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                LogFailure(policyCode, ex);
            }
        }

        return new SuppressionReconciliation(
            changed.Count == 0 ? existing : existing.Select(s => changed.TryGetValue(s.Id, out var to) ? s with { SuppressedUntil = to } : s).ToList(),
            failures,
            PartitionsUnavailable: false);
    }

    /// <summary>The earliest <c>sprk_resolvedon</c> a dismissal can have and still justify a live stamp (#1661 round 2 F1): a stamp lives
    /// 30 days after its latest dismissal, which is 30 days after the earliest of its three, plus a day's slack for the Decision Record's
    /// <c>sprk_decidedon</c> following the Signal's close.</summary>
    private static DateTime ReadSince(DateTime now) => now.AddDays(-2 * WindowDays - 1);

    private static (Guid?, Guid?, string?, Guid?) GrainKeyOf(ExistingSignal s) =>
        s.CoreRecordId is { } core ? (s.CoreTypeRefId, core, null, null) : (null, null, s.SubjectEntity, s.SubjectId);

    // ── Counting ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="StampUntil">The date the partition's own counted dismissals justify (null below the threshold, once lapsed, or in the
    /// undetermined bucket).</param>
    /// <param name="Targets">Rows to RAISE to <paramref name="StampUntil"/>: open, or dismissed in the window, with no or an older stamp.</param>
    /// <param name="Lowers">Rows of any status whose live stamp is later than <paramref name="StampUntil"/> (or live with none justified): lowered
    /// to it, or cleared (#1661 F1), each with the row version it was read at (round 2 K6).</param>
    private sealed record Assessment(
        SuppressionStatus Status, DateTime? StampUntil, IReadOnlyList<(Guid Id, DateTime? Current)> Targets, IReadOnlyList<(Guid Id, string? RowVersion)> Lowers)
    {
        public IEnumerable<(Guid Id, DateTime? To)> Changes =>
            (StampUntil is { } until ? Targets.Select(t => (t.Id, (DateTime?)until)) : [])
                .Concat(Lowers.Select(l => (l.Id, StampUntil)));
    }

    /// <param name="Partitions">One assessment per (partition, holder): a Decide-lane grain has one holder per partition
    /// (<see cref="SuppressionHolder.Everyone"/>); a Do-lane grain one per person or item (D-145a).</param>
    /// <param name="AnchorHolder">The anchor Signal's holder (<see cref="SuppressionHolder.Everyone"/> without an anchor or in the Decide lane).</param>
    private sealed record GrainAssessment(
        IReadOnlyList<(SuppressionPartition Partition, SuppressionHolder Holder, Assessment Assessment)> Partitions, SuppressionPartition? Anchor,
        bool PolicyCounts, SuppressionHolder AnchorHolder = default);

    /// <summary>One Signal as the counting rule needs it, whether read by the grain query or handed over by the nightly job: the SAME rule
    /// (<see cref="Assess"/>) decides both whether a grain needs work and what is written (#1661 round 2 N1/F4).</summary>
    private readonly record struct StampRow(
        Guid Id, bool IsOpen, int? Resolution, DateTime? ResolvedOn, DateTime? SuppressedUntil, Guid? DecisionRecordId, string? RowVersion);

    private static StampRow RowOf(ExistingSignal s) =>
        new(s.Id, s.IsOpen, s.Resolution, Utc(s.ResolvedOn), Utc(s.SuppressedUntil), s.DecisionRecordId, s.RowVersion);

    private static StampRow RowOf(Entity s) => new(
        s.Id,
        s.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged,
        s.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
        Utc(s.GetAttributeValue<DateTime?>("sprk_resolvedon")),
        Utc(s.GetAttributeValue<DateTime?>("sprk_suppresseduntil")),
        s.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id,
        s.RowVersion ?? (s.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null));

    private static DateTime? Utc(DateTime? value) => value is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

    /// <summary>
    /// Counts one pairing in every partition it has (#1661, D-123): the grain's Signals are read once, each is placed in its partition
    /// (<paramref name="known"/> placements are reused; the rest, and <paramref name="anchor"/>, are placed by <see cref="PartitionsAsync"/>),
    /// and each partition is counted from its own rows only. The ordinary partition comes first. In the Do lane (D-145a) each partition is
    /// further split by <see cref="SuppressionHolder"/> (<paramref name="knownHolders"/> are reused; the rest are read), and each holder is
    /// counted from its own rows only. <paramref name="doLane"/> null (the commit route and the count read): the policy's <c>sprk_lane</c>
    /// decides, read with its counting switch.
    /// </summary>
    private async Task<GrainAssessment> AssessAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, bool? policyCounts, IReadOnlySet<Guid>? visibleSignalIds,
        IReadOnlyDictionary<Guid, SuppressionPartition>? known, Entity? anchor, CancellationToken ct, bool? doLane = null,
        IReadOnlyDictionary<(string, Guid), SuppressionHolder>? knownHolders = null)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (policyCounts is null || doLane is null)
        {
            var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_countstowardsuppression", "sprk_lane"], ct).ConfigureAwait(false);
            policyCounts ??= policy.GetAttributeValue<bool?>("sprk_countstowardsuppression") ?? true;
            doLane ??= policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value == SignalWriter.LaneDo;
        }

        var all = await ReadGrainSignalsAsync(policyId, policyCode, grain, ReadSince(now), now, ct).ConfigureAwait(false);
        if (visibleSignalIds is not null) all = all.Where(s => visibleSignalIds.Contains(s.Id)).ToList();

        var toPlace = all.Where(s => known is null || !known.ContainsKey(s.Id)).ToList();
        var items = toPlace.Select(ItemOf).ToList();
        if (anchor is not null) items.Add(ItemOf(anchor));
        var placed = await PartitionsAsync(items, ct).ConfigureAwait(false);

        var partitionOf = new Dictionary<Guid, SuppressionPartition>();
        for (var i = 0; i < toPlace.Count; i++) partitionOf[toPlace[i].Id] = placed[i];
        SuppressionPartition PartitionOf(Entity s) => known is not null && known.TryGetValue(s.Id, out var p) ? p : partitionOf[s.Id];

        // D-145a: whose Do item each row (and the anchor) is; Decide-lane rows are not split.
        IReadOnlyDictionary<(string, Guid), SuppressionHolder> holders = new Dictionary<(string, Guid), SuppressionHolder>();
        if (doLane.Value)
        {
            var subjects = all.Append(anchor).OfType<Entity>().Select(SubjectOf).OfType<(string Entity, Guid Id)>()
                .Where(s => knownHolders is null || !knownHolders.ContainsKey((s.Entity.ToLowerInvariant(), s.Id)))
                .ToList();
            var read = subjects.Count == 0 ? holders : await HoldersOfSubjectsAsync(subjects, ct).ConfigureAwait(false);
            holders = knownHolders is null ? read
                : read.Count == 0 ? knownHolders
                : knownHolders.Concat(read).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        }

        SuppressionHolder HolderOfRow(Entity s) => doLane.Value ? HolderOf(holders, s) : SuppressionHolder.Everyone;

        var rows = all.Select(RowOf).ToList();
        var records = await ReadDecisionRecordsAsync(rows
            .Where(r => r.Resolution == SignalWriter.ResolutionDismissed && r.DecisionRecordId is not null)
            .Select(r => r.DecisionRecordId!.Value).Distinct().ToList(), ct).ConfigureAwait(false);

        var results = new List<(SuppressionPartition, SuppressionHolder, Assessment)>();
        foreach (var group in all.GroupBy(s => (Partition: PartitionOf(s), Holder: HolderOfRow(s)))
                     .OrderBy(g => g.Key.Partition.IsOrdinary ? 0 : 1).ThenBy(g => g.Key.Partition.Key, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Holder.ToString(), StringComparer.Ordinal))
        {
            var stampable = policyCounts.Value && !group.Key.Partition.IsUndetermined;
            results.Add((group.Key.Partition, group.Key.Holder, Assess(group.Select(RowOf).ToList(), records, policyCounts.Value, stampable, now, group.Key.Holder)));
        }

        return new GrainAssessment(results, anchor is null ? null : placed[^1], policyCounts.Value,
            anchor is null ? SuppressionHolder.Everyone : HolderOfRow(anchor));
    }

    /// <summary>
    /// THE counting rule for one partition, pure (#1661 round 2): both the nightly "does this grain need work" and the exact count use it,
    /// with the same date source on both sides (a dismissal's date is its Decision Record's <c>sprk_decidedon</c>, else the Signal's
    /// <c>sprk_resolvedon</c>), so a stamp it wrote is the stamp it justifies (F4: no nightly re-read of every stamped grain).
    /// <list type="bullet">
    /// <item>One dismissal per Decision Record of class Dismissal (a review may resolve several Signals); a Signal with no record is its
    /// own. A non-counting reason, or a policy that does not count, contributes to the recorded number only.</item>
    /// <item>The status reports the counted and recorded dismissals of the trailing <see cref="WindowDays"/> days.</item>
    /// <item>The justified date (<see cref="JustifiedUntil"/>) is the latest S such that at least three counted dismissals within
    /// <see cref="WindowDays"/> days of each other end at S − 30 days, while S is in the future (F1): a stamp lives its full 30 days
    /// although its oldest dismissal ages out of the trailing count first (spec FR-17).</item>
    /// <item>A live stamp later than the justified date (or live with none justified) is lowered or cleared; open rows, and rows
    /// dismissed in the trailing window, with no or an earlier stamp are raised to it. A stamp at or below it is never lowered.</item>
    /// <item>D-145a: for a PERSON <paramref name="holder"/>, a dismissal is that person's (counted and recorded) only when its Decision
    /// Record is of class Dismissal and its decider (<c>sprk_assignedtointernal</c>) is that person. The subject's person is read now, so a
    /// dismissal of an item that was reassigned since, one made by the item's owner or creator rather than its person, or one with no
    /// decider on record, counts toward no one: never toward another person's Do items.</item>
    /// </list>
    /// </summary>
    private static Assessment Assess(
        IReadOnlyList<StampRow> rows, IReadOnlyDictionary<Guid, DecisionRecordFacts> records, bool policyCounts, bool stampable, DateTime now,
        SuppressionHolder holder = default)
    {
        var windowStart = now.AddDays(-WindowDays);
        var readSince = ReadSince(now);
        var dismissed = rows
            .Where(r => r.Resolution == SignalWriter.ResolutionDismissed && r.ResolvedOn is { } on && on >= readSince)
            .ToList();

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
                if (holder.PersonId is { } person && record.DeciderContactId != person) continue; // D-145a: not this person's dismissal
                key = recordId;
                on = record.DecidedOn ?? d.ResolvedOn!.Value;
                counts = !IsNonCountingReason(record.Reason);
            }
            else
            {
                if (holder.PersonId is not null) continue; // D-145a: no record names who dismissed it, so it is no one's
                key = d.DecisionRecordId ?? d.Id;
                on = d.ResolvedOn!.Value;
            }

            events[key] = (on, counts);
        }

        var counted = policyCounts ? events.Values.Where(e => e.Counts).Select(e => e.On).ToList() : new List<DateTime>();

        // The partition's OWN justified date: the only date it may carry or report (#1661 F1).
        var justified = stampable ? JustifiedUntil(counted, now) : null;

        var targets = new List<(Guid, DateTime?)>();
        var lowers = new List<(Guid, string?)>();
        var trailing = dismissed.Where(d => d.ResolvedOn >= windowStart.AddDays(-1)).Select(d => d.Id).ToHashSet();
        foreach (var s in rows)
        {
            if (s.SuppressedUntil is { } live && live > now && (justified is null || live > justified))
            {
                lowers.Add((s.Id, s.RowVersion));
                continue;
            }

            if (justified is not { } raiseTo) continue;
            if (!s.IsOpen && !trailing.Contains(s.Id)) continue;
            if (s.SuppressedUntil is { } current && current >= raiseTo) continue;
            targets.Add((s.Id, s.SuppressedUntil));
        }

        var status = new SuppressionStatus(
            counted.Count(on => on >= windowStart), events.Values.Count(e => e.On >= windowStart), justified, policyCounts);
        return new Assessment(status, justified, targets, lowers);
    }

    /// <summary>The latest date the <paramref name="counted"/> dismissal dates justify (#1661 round 2 F1): S = d + 30 days for the latest
    /// counted dismissal d that has at least <see cref="ThresholdDismissals"/> − 1 others no more than <see cref="WindowDays"/> days before
    /// it, while S is after <paramref name="now"/>; null otherwise.</summary>
    internal static DateTime? JustifiedUntil(IReadOnlyCollection<DateTime> counted, DateTime now)
    {
        var dates = counted.Order().ToList();
        for (var i = dates.Count - 1; i >= ThresholdDismissals - 1; i--)
        {
            if (dates[i] - dates[i - (ThresholdDismissals - 1)] > TimeSpan.FromDays(WindowDays)) continue;
            var until = dates[i].AddDays(WindowDays);
            return until > now ? until : null; // every earlier one ends earlier still
        }

        return null;
    }

    private async Task StampAsync(string policyCode, Assessment assessment, CancellationToken ct)
    {
        if (assessment.Lowers.Count > 0)
        {
            var lowered = 0;
            foreach (var (id, rowVersion) in assessment.Lowers)
            {
                var fields = new Dictionary<string, object> { ["sprk_suppresseduntil"] = assessment.StampUntil! };
                if (rowVersion is null)
                {
                    // Every row Dataverse returns carries its version; a row read without one is lowered unconditionally.
                    await _writer.UpdateAsync("sprk_signal", id, fields, ct).ConfigureAwait(false);
                    lowered++;
                    continue;
                }

                try
                {
                    // Round 2 K6: only while the row is as it was counted. A row re-stamped since (the decision route's immediate apply)
                    // is not lowered from a stale count; the next pass counts it again.
                    await _writer.UpdateIfRowVersionMatchesAsync("sprk_signal", id, rowVersion, fields, ct).ConfigureAwait(false);
                    lowered++;
                }
                catch (Exception ex) when (SignalWriter.IsConcurrencyVersionMismatch(ex))
                {
                    _logger.LogWarning(SuppressionEvents.Withdrawn,
                        "Suppression: policy {PolicyCode}: Signal {SignalId} changed since it was counted; its stamp was NOT lowered. The next pass counts it again.",
                        policyCode, id);
                }
            }

            _logger.LogInformation(SuppressionEvents.Withdrawn,
                "Suppression: policy {PolicyCode}: {Signals} Signal(s) carried a stamp their partition's {Counted} counted dismissal(s) do not justify; " +
                "lowered to {Until:o} (empty: cleared).", policyCode, lowered, assessment.Status.DismissalsCounted, assessment.StampUntil);
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
    /// since <paramref name="resolvedSince"/> (<see cref="ReadSince"/>: every dismissal that can still justify a live stamp, #1661 round 2
    /// F1), and any row still carrying a live stamp (so a stamp the partition no longer justifies can be withdrawn, #1661 F1), with their
    /// lineage lookups, owner and dedupe key (the partition) and row version (a lower is conditional on it, round 2 K6).</summary>
    private async Task<IReadOnlyList<Entity>> ReadGrainSignalsAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, DateTime resolvedSince, DateTime now, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet(new[] { "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_suppresseduntil", "sprk_decisionrecord", "ownerid", "sprk_dedupekey", "versionnumber" }
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

    /// <param name="DeciderContactId">The decider (<c>sprk_assignedtointernal</c>, D-129), which a Do-lane person holder counts by (D-145a).</param>
    private sealed record DecisionRecordFacts(int? Class, string? Reason, DateTime? DecidedOn, Guid? DeciderContactId = null);

    private async Task<IReadOnlyDictionary<Guid, DecisionRecordFacts>> ReadDecisionRecordsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = new Dictionary<Guid, DecisionRecordFacts>();
        foreach (var chunk in ids.Chunk(InChunk))
        {
            var query = new QueryExpression("sprk_decisionrecord")
            {
                ColumnSet = new ColumnSet("sprk_recordclass", "sprk_reason", "sprk_decidedon", "sprk_assignedtointernal"),
            };
            query.Criteria.AddCondition("sprk_decisionrecordid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                found[row.Id] = new DecisionRecordFacts(
                    row.GetAttributeValue<OptionSetValue>("sprk_recordclass")?.Value,
                    row.GetAttributeValue<string>("sprk_reason"),
                    row.GetAttributeValue<DateTime?>("sprk_decidedon") is { } on ? DateTime.SpecifyKind(on, DateTimeKind.Utc) : null,
                    row.GetAttributeValue<EntityReference>("sprk_assignedtointernal")?.Id);
            }
        }

        return found;
    }

    // ── Ontology task 038 (task 145 review F1, option B; D-123; uac-r2 #1747 finding e): the witness rule at READ time ──────────

    /// <summary>
    /// Which of each Signal's witness references may still show their detail (a communication's subject and sender) to the readers
    /// of that Signal, judged NOW: a reference keeps its detail only when its witness row's secure family is KNOWN and EQUAL to the
    /// Signal's own family (D-123; the same rule as the evaluator's write-time check, task 145). Evidence is written once, so a
    /// witness re-filed under a secure family afterwards would otherwise show detail its Signal's readers may no longer see until the
    /// job's next pass. Both families come from THE partition truth (<see cref="PartitionsAsync"/>, read from the ROOTS, never from
    /// an owner alone): the Signals are read app-only with their lineage lookups and owner, the witness rows with their lookups and
    /// owner, and every lineage is placed in ONE call (the Secure team identified once, one walker run, so the reads are bounded by
    /// the distinct rows and parents). A reference that names no lineage child, a witness or Signal row that cannot be read, or an
    /// undetermined family on either side keeps nothing. No Secure Record owner team in this environment: every family is ordinary
    /// and every reference the caller reads keeps its detail. A fault (an ambiguous team, a walk that does not complete, a read
    /// failure) returns null, logged at Warning (<see cref="SuppressionEvents.WitnessReadFailed"/>): the caller then withholds every
    /// line's detail.
    /// <para><b>Both rules must keep (task 151, D-142).</b> A reference keeps its detail only when the caller's own read admitted its
    /// witness row as well: <paramref name="readableByCaller"/> holds the witnesses (<see cref="WitnessOf"/>) the caller's batched
    /// read returned. A reference whose witness is not in it keeps nothing, and is never placed (no app-only read of a row the
    /// caller cannot read). The secure-family rule above is unchanged and applies to the rest.</para>
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: <see cref="PartitionsAsync"/> (the one partition truth) and
    /// <see cref="ReadSubjectRowsAsync"/>; <see cref="EvidenceWithinPartitionAsync"/> / <see cref="StoredEvidenceToRedactAsync"/> (task
    /// 145) apply the same rule to hits and stored rows. Extension: this applies that rule (<see cref="WitnessItemsAsync"/>,
    /// <see cref="KeepsDetail"/>, <see cref="WitnessOf"/>) to the rows a caller's reads admitted, with the Signals and the witnesses
    /// placed in one call (one rule, one helper; DI-87, #1890). Cost of doing nothing: until the job's next pass, a matter's readers
    /// see the subject and sender of a communication that has since been filed under another secure family (D-123).</remarks>
    internal async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>?> WitnessDetailShownOnReadAsync(
        IReadOnlyList<(Guid SignalId, IReadOnlyList<string> References)> signals,
        IReadOnlySet<(string Table, Guid Id)> readableByCaller,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(readableByCaller);
        var shown = new Dictionary<Guid, IReadOnlySet<string>>();

        // D-142: only a reference whose witness the caller's own read returned is judged at all; every other keeps nothing.
        var wanted = signals.GroupBy(s => s.SignalId)
            .Select(g => (SignalId: g.Key, References: g.SelectMany(s => s.References)
                .Where(r => WitnessOf(r) is { } witness && readableByCaller.Contains(witness))
                .Distinct(StringComparer.Ordinal).ToList()))
            .Where(w => w.References.Count > 0)
            .ToList();
        if (wanted.Count == 0) return shown;

        try
        {
            // The Secure team, identified ONCE for the request. None in this environment: every family is ordinary.
            if (await ResolveSecureTeamAsync(ct).ConfigureAwait(false) is not { } secureTeam)
            {
                foreach (var (id, references) in wanted) shown[id] = references.ToHashSet(StringComparer.Ordinal);
                return shown;
            }

            // The Signals as stored, app-only, with what the walk needs. The caller's own reads have already admitted these rows; this
            // read only places them and returns nothing of them.
            var rows = new Dictionary<Guid, Entity>();
            foreach (var chunk in wanted.Select(w => w.SignalId).Chunk(InChunk))
            {
                var query = new QueryExpression("sprk_signal")
                {
                    ColumnSet = new ColumnSet(new[] { "sprk_dedupekey", "ownerid" }.Concat(SignalLineageColumns).Distinct(StringComparer.Ordinal).ToArray()),
                    NoLock = true,
                };
                query.Criteria.AddCondition("sprk_signalid", ConditionOperator.In, chunk.Cast<object>().ToArray());
                foreach (var row in (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities) rows[row.Id] = row;
            }

            var placed = wanted.Where(w => rows.ContainsKey(w.SignalId)).ToList();

            // The witness rows (with their lineage lookups and owner), as task 145 places them; a witness that cannot be read keeps nothing.
            var (readable, witnessItems) = await WitnessItemsAsync(placed.SelectMany(w => w.References), ct).ConfigureAwait(false);

            // ONE placement for the Signals and the witnesses together. A witness's family comes from its roots: a row already filed
            // under secure X but not yet re-owned by the Secure team (the re-own window) is in X's family, never ordinary.
            // uac-r2 K1 on ontology #1891: the witness rows and the subjects are handed to the walker as read, not read again.
            var families = await PartitionsAsync(
                placed.Select(w => ItemOf(rows[w.SignalId])).Concat(witnessItems).ToList(), ct, secureTeam,
                readAsSubjects: witnessItems.Select(i => i.Signal).ToList()).ConfigureAwait(false);
            var familyOfWitness = new Dictionary<(string, Guid), SuppressionPartition>();
            for (var i = 0; i < readable.Count; i++) familyOfWitness[readable[i]] = families[placed.Count + i];

            for (var i = 0; i < placed.Count; i++)
            {
                shown[placed[i].SignalId] = placed[i].References
                    .Where(r => KeepsDetail(families[i], r, familyOfWitness))
                    .ToHashSet(StringComparer.Ordinal);
            }

            // A Signal the app-only read did not return is absent: it keeps nothing.
            return shown;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(SuppressionEvents.WitnessReadFailed,
                "Signal read: the secure families of {Count} Signal(s)' witnesses could not be read ({ExceptionType}); every witness line is shown without its detail (D-123, fail closed).",
                wanted.Count, ex.GetType().Name);
            return null;
        }
    }
}
