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
/// What suppression is for one grain at one moment: the counted dismissals in the trailing window, every recorded dismissal in it,
/// and the denormalized <c>sprk_suppresseduntil</c> that is still in the future (null when none).
/// </summary>
/// <param name="DismissalsCounted">Dismissals that count toward the threshold (a reason that does not count, and a policy whose
/// <c>sprk_countstowardsuppression</c> is No, contribute none).</param>
/// <param name="DismissalsRecorded">Every human dismissal in the window, counted or not: recorded, queryable, clusterable.</param>
/// <param name="SuppressedUntil">The latest <c>sprk_suppresseduntil</c> among the grain's Signals that is still in the future.</param>
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

/// <summary>Stable EventIds for suppression (the 5030x range is the Signal side's; 5031x the Decision Record's).</summary>
public static class SuppressionEvents
{
    /// <summary>Information: a grain reached the threshold and its Signals were stamped. Properties: policy code, counts, until.</summary>
    public static readonly EventId Stamped = new(50350, nameof(Stamped));

    /// <summary>Error: counting or stamping a grain failed; the next evaluation retries. Properties: policy code, exception type.</summary>
    public static readonly EventId Failed = new(50351, nameof(Failed));
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
/// <para><b>The secure partition (#1661, owner decision D-123).</b> A Signal owned by the Secure Record owner team (a secure subject
/// filed under a readable core record, task 039) can sit under a core record a non-member reads. Every grain is therefore split in
/// two, (policy, core record, isSecureOwned) and (policy, item, isSecureOwned): the dismissals of Secure-owned Signals count toward,
/// and stamp, only Secure-owned Signals of the same pairing; ordinary dismissals count toward and stamp only ordinary Signals. A
/// stamp, or a count, on a readable row therefore never reflects a hidden dismissal. "Secure-owned" is the Signal's <c>ownerid</c>
/// being the team uac-r2's <see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/> identifies (never an id of ours),
/// looked up only when a team-owned Signal is among the rows. The partition is applied in every place the grain is computed:
/// <see cref="ApplyAsync"/> (the commit route's follow-up), <see cref="ReconcileAsync"/> (nightly) and the count read.</para>
/// <para><b>The stamp.</b> At the threshold, <c>sprk_suppresseduntil</c> = the most recent counted dismissal + 30 days is written on
/// the grain's open Signals and on its dismissed Signals in the window (denormalized so the worklist query does not count Decision
/// Records per load; task 031's planner reads it for the re-raise rule, D-13). Only rows whose stamp would change are written, so
/// nothing is stamped nightly (D-61). The stamp lapses on its own: the dismissals that made it are then older than the window, so the
/// next dismissal counts as the first again. This service does not implement the 14-day quiet window after a 1st or 2nd dismissal
/// (FR-17a): that is task 031's re-raise rule.</para>
/// <para><b>Who calls it.</b> The nightly job (<see cref="ReconcileAsync"/>, a backstop that stamps a grain it finds at the threshold
/// with no live stamp) and the decision commit route (task 043) right after it closes the dismissed Signals
/// (<see cref="ApplyAsync"/>), so the effect is immediate rather than next night.</para>
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
    private readonly ILogger<SignalSuppressionService> _logger;

    public SignalSuppressionService(
        IGenericEntityService entities,
        OntologyWriterDataverseClient writer,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<SignalSuppressionService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── The secure partition (#1661, owner decision D-123) ──────────────────────────────────────────────────────

    /// <summary>The <c>logical name</c> of an owner that is a team: only a team can be the Secure Record owner team.</summary>
    private const string TeamLogicalName = "team";

    private static bool IsTeamOwned(EntityReference? owner) => owner is { LogicalName: TeamLogicalName };

    /// <summary>True when <paramref name="owner"/> is the Secure Record owner team (and that team exists: <paramref name="secureTeamId"/>).</summary>
    private static bool IsSecureOwned(EntityReference? owner, Guid? secureTeamId) =>
        secureTeamId is { } team && team != Guid.Empty && IsTeamOwned(owner) && owner!.Id == team;

    /// <summary>
    /// The Secure Record owner team's id, identified by uac-r2's one rule (<see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>:
    /// the configured business unit and its NAMED, non-default Owner team, TOP 2 each); null when this environment has no such team,
    /// so no Signal can be secure-owned. An ambiguous answer throws: a count that cannot be partitioned is never a guess (fail closed).
    /// Not cached, as uac-r2's callers do not cache it; callers ask once per operation and only when a team-owned Signal is in hand.
    /// </summary>
    private async Task<Guid?> ResolveSecureTeamAsync(CancellationToken ct)
    {
        var (teamId, refusal) = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_entities, _configuration, ct).ConfigureAwait(false);
        return refusal is null
            ? teamId
            : throw new InvalidOperationException($"The Secure Record owner team cannot be identified, so suppression cannot be partitioned ({refusal}).");
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
    /// Dataverse client. A fault (including a caller read that is not a success) throws: it is never an empty or a partial count. Null when the Signal does not carry what the grain
    /// needs (no policy, or an unparseable key on a no-core Signal): the route answers the uniform 404, never a guess.
    /// </summary>
    public async Task<SuppressionStatus?> GetStatusForSignalAsync(Guid signalId, IDataverseUserClient caller, CancellationToken ct)
    {
        var signal = await _entities.RetrieveAsync("sprk_signal", signalId,
            ["sprk_policy", "sprk_corerecordtype", "sprk_corerecordid", "sprk_dedupekey", "ownerid"], ct).ConfigureAwait(false);

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
        // are read app-only for those rows only. And it is the anchor Signal's PARTITION's (#1661): a Secure-team-owned Signal is
        // counted only with other Secure-team-owned Signals of the pairing, an ordinary one only with ordinary ones, so the answer
        // never carries a number or a stamp from across the secure wall.
        var anchorOwner = signal.GetAttributeValue<EntityReference>("ownerid");
        var secureOwned = IsTeamOwned(anchorOwner) && IsSecureOwned(anchorOwner, await ResolveSecureTeamAsync(ct).ConfigureAwait(false));
        var visible = await ReadVisibleSignalIdsAsync(caller, policyId, code, grain, _timeProvider.GetUtcNow().UtcDateTime.AddDays(-WindowDays - 1), ct).ConfigureAwait(false);
        return (await AssessAsync(policyId, code, grain, policyCounts: null, visible, secureOwned, ct).ConfigureAwait(false)).Single().Assessment.Status;
    }

    /// <summary>
    /// Counts the pairing and, at the threshold, stamps <c>sprk_suppresseduntil</c>. Idempotent: a second call writes nothing.
    /// The pairing is counted in two partitions that never see each other (#1661, D-123): the Secure-team-owned Signals and the rest.
    /// With <paramref name="secureOwned"/> null (what a caller that has just closed Signals of both kinds wants) both are applied; the
    /// result is the ordinary partition's, the only one a non-member may be told about. Pass true or false to apply one partition and
    /// get its status.
    /// </summary>
    public async Task<SuppressionStatus> ApplyAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, CancellationToken ct, bool? secureOwned = null)
    {
        var assessments = await AssessAsync(policyId, policyCode, grain, policyCounts: null, visibleSignalIds: null, secureOwned, ct).ConfigureAwait(false);
        foreach (var (_, assessment) in assessments) await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);

        var reported = assessments.First(a => a.SecureOwned == (secureOwned ?? false)).Assessment;
        return reported.Status with { SuppressedUntil = reported.StampUntil ?? reported.Status.SuppressedUntil };
    }

    /// <summary>
    /// The nightly backstop (task 031 calls it once per policy with the Signals it already read and the policy's
    /// <c>sprk_countstowardsuppression</c>, read once with the policies). A policy that does not count is skipped outright. Otherwise a
    /// grain is counted exactly and stamped when it has at least the threshold of DISTINCT dismissals (one per Decision Record) in the
    /// window AND either no live stamp, or an open / in-window dismissed Signal whose stamp is missing or older than the grain's latest
    /// live one (a partly written stamp is repaired). Only rows that change are written (D-61). Returns the Signals with the stamps
    /// applied so the planner that runs next sees them even if a write failed, and the number of grains that failed (the run is then not
    /// successful, and the next run retries).
    /// </summary>
    internal async Task<(IReadOnlyList<ExistingSignal> Signals, int Failures)> ReconcileAsync(
        Guid policyId, string policyCode, bool policyCountsTowardSuppression, IReadOnlyList<ExistingSignal> existing, CancellationToken ct)
    {
        // #1661 (D-123): mark the Secure-owned Signals first, whatever the policy does (the planner reads the mark too, so a stamp on a
        // Secure-owned row never mutes a readable subject). The Secure team is looked up only when a team-owned Signal is among the rows.
        // A lookup that cannot be answered fails the pass for this policy (counted, retried tomorrow) and withholds the stamps of
        // team-owned rows from the planner in memory: a pairing that cannot be partitioned is never counted or muted whole.
        try
        {
            existing = await MarkSecureOwnedAsync(existing, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(policyCode, ex);
            return (existing.Select(s => IsTeamOwned(s.Owner) && s.SuppressedUntil is not null ? s with { SuppressedUntil = null } : s).ToList(), 1);
        }

        if (!policyCountsTowardSuppression) return (existing, 0);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-WindowDays);

        // The grain plus its secure partition: Secure-team-owned Signals are a grain of their own, apart from the ordinary ones of the pairing.
        static (Guid?, Guid?, string?, Guid?, bool) Key(ExistingSignal s) =>
            s.CoreRecordId is { } core
                ? (s.CoreTypeRefId, core, null, null, s.SecureOwned)
                : (null, null, s.SubjectEntity, s.SubjectId, s.SecureOwned);

        bool NeedsWork(IGrouping<(Guid?, Guid?, string?, Guid?, bool), ExistingSignal> g)
        {
            var dismissed = g.Where(s => s.Resolution == SignalWriter.ResolutionDismissed && s.ResolvedOn is { } on && on >= windowStart.AddDays(-1)).ToList();
            if (dismissed.Select(s => s.DecisionRecordId ?? s.Id).Distinct().Count() < ThresholdDismissals) return false;

            DateTime? live = null;
            foreach (var s in g)
            {
                if (s.SuppressedUntil is { } u && u > now && (live is null || u > live)) live = u;
            }

            if (live is null) return true;
            var dismissedIds = dismissed.Select(s => s.Id).ToHashSet();
            return g.Any(s => (s.IsOpen || dismissedIds.Contains(s.Id)) && (s.SuppressedUntil is null || s.SuppressedUntil < live));
        }

        var candidates = existing.GroupBy(Key).Where(NeedsWork).ToList();
        if (candidates.Count == 0) return (existing, 0);

        var stamped = new Dictionary<Guid, DateTime>();
        var failures = 0;
        foreach (var group in candidates)
        {
            var (type, core, entity, subject, secureOwned) = group.Key;
            var grain = new SuppressionGrain(type, core, entity, subject);
            try
            {
                var assessment = (await AssessAsync(policyId, policyCode, grain, policyCountsTowardSuppression, visibleSignalIds: null, secureOwned, ct)
                    .ConfigureAwait(false)).Single().Assessment;
                if (assessment.StampUntil is { } until)
                {
                    // In memory first: the planner must see the suppression this pass even when the write below fails.
                    var targetIds = assessment.StampTargets;
                    foreach (var signal in group.Where(s => targetIds.Contains(s.Id))) stamped[signal.Id] = until;
                    await StampAsync(policyCode, assessment, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                LogFailure(policyCode, ex);
            }
        }

        return stamped.Count == 0
            ? (existing, failures)
            : (existing.Select(s => stamped.TryGetValue(s.Id, out var until) ? s with { SuppressedUntil = until } : s).ToList(), failures);
    }

    /// <summary>The rows with <see cref="ExistingSignal.SecureOwned"/> set from their owner. Looks the Secure team up only when a team owns
    /// a row; the same list back when none does, or when this environment has no Secure Record owner team. An unanswerable lookup throws.</summary>
    private async Task<IReadOnlyList<ExistingSignal>> MarkSecureOwnedAsync(IReadOnlyList<ExistingSignal> existing, CancellationToken ct)
    {
        if (!existing.Any(s => IsTeamOwned(s.Owner))) return existing;
        var secureTeamId = await ResolveSecureTeamAsync(ct).ConfigureAwait(false);
        return secureTeamId is null
            ? existing
            : existing.Select(s => IsSecureOwned(s.Owner, secureTeamId) ? s with { SecureOwned = true } : s).ToList();
    }

    // ── Counting ────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Assessment(
        SuppressionStatus Status, DateTime? StampUntil, IReadOnlyList<(Guid Id, DateTime? Current)> Targets)
    {
        public HashSet<Guid> StampTargets => Targets.Select(t => t.Id).ToHashSet();
    }

    private sealed record DismissedSignal(Guid SignalId, DateTime ResolvedOn, Guid? DecisionRecordId);

    /// <summary>
    /// Counts one pairing, once per requested SECURE PARTITION (#1661, D-123): the grain's Signals are read once and split by whether
    /// their owner is the Secure Record owner team, and each partition is counted, stamped and (for the route) reported from its own
    /// rows only. <paramref name="secureOwned"/> null returns both partitions (ordinary first); true or false returns that one. The
    /// Secure team is looked up only when a team-owned Signal is among the rows.
    /// </summary>
    private async Task<IReadOnlyList<(bool SecureOwned, Assessment Assessment)>> AssessAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, bool? policyCounts, IReadOnlySet<Guid>? visibleSignalIds, bool? secureOwned, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-WindowDays);

        if (policyCounts is null)
        {
            var policy = await _entities.RetrieveAsync("sprk_policy", policyId, ["sprk_countstowardsuppression"], ct).ConfigureAwait(false);
            policyCounts = policy.GetAttributeValue<bool?>("sprk_countstowardsuppression") ?? true;
        }

        var all = await ReadGrainSignalsAsync(policyId, policyCode, grain, windowStart.AddDays(-1), ct).ConfigureAwait(false);
        if (visibleSignalIds is not null) all = all.Where(s => visibleSignalIds.Contains(s.Id)).ToList();

        var secureTeamId = all.Any(s => IsTeamOwned(s.GetAttributeValue<EntityReference>("ownerid")))
            ? await ResolveSecureTeamAsync(ct).ConfigureAwait(false)
            : null;

        var results = new List<(bool, Assessment)>();
        foreach (var partition in secureOwned is { } only ? [only] : new[] { false, true })
        {
            var rows = all.Where(s => IsSecureOwned(s.GetAttributeValue<EntityReference>("ownerid"), secureTeamId) == partition).ToList();
            results.Add((partition, await AssessPartitionAsync(rows, policyCounts.Value, now, windowStart, ct).ConfigureAwait(false)));
        }

        return results;
    }

    private async Task<Assessment> AssessPartitionAsync(
        IReadOnlyList<Entity> signals, bool policyCounts, DateTime now, DateTime windowStart, CancellationToken ct)
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
        DateTime? liveStamp = null;
        foreach (var signal in signals)
        {
            if (signal.GetAttributeValue<DateTime?>("sprk_suppresseduntil") is { } stored)
            {
                var stamp = DateTime.SpecifyKind(stored, DateTimeKind.Utc);
                if (stamp > now && (liveStamp is null || stamp > liveStamp)) liveStamp = stamp;
            }
        }

        DateTime? stampUntil = null;
        var targets = new List<(Guid, DateTime?)>();
        if (counted.Count >= ThresholdDismissals)
        {
            var newUntil = counted.Max(e => e.On).AddDays(WindowDays);
            if (newUntil > now)
            {
                stampUntil = newUntil;
                var inWindow = dismissed.Where(d => d.ResolvedOn >= windowStart.AddDays(-1)).Select(d => d.SignalId).ToHashSet();
                foreach (var s in signals)
                {
                    var status = s.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value;
                    var open = status is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged;
                    if (!open && !inWindow.Contains(s.Id)) continue;
                    var current = s.GetAttributeValue<DateTime?>("sprk_suppresseduntil");
                    if (current is { } c && DateTime.SpecifyKind(c, DateTimeKind.Utc) >= newUntil) continue;
                    targets.Add((s.Id, current));
                }
            }
        }

        var until = stampUntil is { } fresh && (liveStamp is null || fresh > liveStamp) ? fresh : liveStamp;
        return new Assessment(new SuppressionStatus(counted.Count, events.Count, until, policyCounts), stampUntil, targets);
    }

    private async Task StampAsync(string policyCode, Assessment assessment, CancellationToken ct)
    {
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

    /// <summary>The ids of the grain's Signals the CALLER can read: the same filter (grain, open or dismissed in the window) as the app-only read, asked through the caller's own
    /// Dataverse client, which trims the result to rows that caller may see. A read that is not a success, or a result as large as the page
    /// cap, throws: a partial answer would be a wrong count.</summary>
    private static async Task<IReadOnlySet<Guid>> ReadVisibleSignalIdsAsync(
        IDataverseUserClient caller, Guid policyId, string policyCode, SuppressionGrain grain, DateTime resolvedSince, CancellationToken ct)
    {
        const int cap = 4999;
        static string Quote(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
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
            + $" or (sprk_resolutiontype eq {SignalWriter.ResolutionDismissed} and sprk_resolvedon ge {resolvedSince.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}))";

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

    /// <summary>The grain's Signals (any status) of one policy: by the generic core pair, or by the subject's dedupe-key prefix.</summary>
    private async Task<IReadOnlyList<Entity>> ReadGrainSignalsAsync(
        Guid policyId, string policyCode, SuppressionGrain grain, DateTime resolvedSince, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet("sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_suppresseduntil", "sprk_decisionrecord", "ownerid"),
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

        // Open ones (to hide) and the dismissed ones in the window (to count): everything else of the grain is irrelevant.
        var statusFilter = query.Criteria.AddFilter(LogicalOperator.Or);
        statusFilter.AddCondition("sprk_signalstatus", ConditionOperator.In, SignalWriter.SignalStatusOpen, SignalWriter.SignalStatusAcknowledged);
        var dismissedFilter = statusFilter.AddFilter(LogicalOperator.And);
        dismissedFilter.AddCondition("sprk_resolutiontype", ConditionOperator.Equal, SignalWriter.ResolutionDismissed);
        dismissedFilter.AddCondition("sprk_resolvedon", ConditionOperator.GreaterEqual, resolvedSince);

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
