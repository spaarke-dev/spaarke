using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The ONE re-evaluating scheduled job (task 031; spec FR-11, FR-15, FR-16, FR-17a; design.md §8.3). Nightly, for every
/// enabled policy of either lane, it evaluates the current version (<see cref="SignalEvaluator"/>, side-effect free), then in
/// the SAME pass creates, re-raises (episodes, D-13/D-32), reconciles and closes Signals, writing only what changed (D-61).
/// </summary>
/// <remarks>
/// <para><b>Placement (ADR-052, ADR-036).</b> An <see cref="IScheduledJob"/> in the BFF on the shared Spaarke.Scheduling host:
/// the Redis lease gives one run per schedule across instances, the run history is in the admin jobs endpoint, and no timer
/// <c>BackgroundService</c> is added. Hundreds of rows a night is not a scale problem; fewer moving parts wins.</para>
/// <para><b>One component decides both directions.</b> A hit with no open Signal is raised; an open Signal whose subject is no
/// longer a hit is closed as <c>ConditionCleared</c> (FR-16); a Resolved Signal is never closed again. Task 153 (D-145b): a DO-lane
/// Signal closed that way (its subject no longer a hit, so not a re-file) then gets ONE Routine Decision Record naming, best effort, the
/// person who changed the subject (<see cref="DecisionRecordWriter.WriteConditionClearedAsync"/>), linked to the Signal; a Decide-lane
/// ConditionCleared, a re-file and every Superseded or PolicyRetired closure stay record-free. The
/// Superseded and PolicyRetired closures are task 033's (<see cref="SignalClosureService"/>, run from this job); the planner
/// re-raises their episodes.</para>
/// <para><b>Re-raise rule (D-13, D-32, FR-03, FR-17a).</b> See <see cref="SignalEpisodePlanner"/>.</para>
/// <para><b>No silent failure (owner 2026-10-04).</b> A heartbeat per attempt (<see cref="OntologyWriterEvents.EvaluatorHeartbeat"/>);
/// <c>ontology.evaluator.runs</c> by outcome plus per-action counts; any refusal, unreadable policy or unparseable key makes
/// the run <c>failed</c>, never a successful run with zero Signals; the last SUCCESSFUL run time is kept in the distributed
/// cache for <see cref="SignalEvaluatorHealthCheck"/> (a diagnostic check, not liveness).</para>
/// <para>§11: <i>Existing</i> — six <see cref="IScheduledJob"/>s, none evaluates policies; <see cref="SignalWriter"/> writes one
/// Signal and decides no episode or closure. <i>Extension</i> — the job reuses the ADR-036 seam, the validator gate, the writer
/// and uac-r2's resolver rather than adding a scheduler or an ownership path. <i>Cost of doing nothing</i> — no Signal is ever
/// raised, a cleared condition is never closed, and a resolved Signal can never return (FR-11, FR-15, D-13).</para>
/// </remarks>
public sealed class SignalReevaluationJob : IScheduledJob
{
    public const string JobIdConstant = "signal-reevaluation";

    /// <summary>02:30 UTC nightly (ADR-036 cron, UTC).</summary>
    internal const string DefaultCronSchedule = "30 2 * * *";

    /// <summary>The distributed-cache key holding the last successful run (ISO 8601 UTC), shared across instances.</summary>
    internal const string LastSuccessCacheKey = "sprk:ontology:evaluator:last-success";

    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SignalEvaluator _evaluator;
    private readonly SignalWriter _writer;
    private readonly SignalSuppressionService _suppression;
    private readonly SignalClosureService _closure;
    private readonly DecisionRecordWriter _records;
    private readonly IGenericEntityService _entities;
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalReevaluationJob> _logger;

    public SignalReevaluationJob(
        SignalEvaluator evaluator,
        SignalWriter writer,
        SignalSuppressionService suppression,
        SignalClosureService closure,
        DecisionRecordWriter records,
        IGenericEntityService entities,
        IDistributedCache cache,
        TimeProvider timeProvider,
        ILogger<SignalReevaluationJob> logger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _suppression = suppression ?? throw new ArgumentNullException(nameof(suppression));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Signal Re-evaluation";

    public string Description =>
        "Nightly re-evaluation of every enabled policy, both lanes (task 031): raises Signals whose predicate now holds " +
        "(episode-scoped keys, D-13), re-raises after resolution per the quiet-window and date-change rules, closes open " +
        "Signals whose predicate no longer holds as ConditionCleared, and writes only Signals whose result changed (D-61).";

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var counts = new RunCounts();

        IReadOnlyList<PolicyRow> policies;
        try
        {
            policies = await ReadEnabledPoliciesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            counts.PolicyFailures++;
            Finish(context, counts, started, success: false);
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Signal re-evaluation could not read the enabled policies; nothing was evaluated this attempt (attempt={Attempt}).",
                context.Attempt);
            // ADR-036 A1 rule 4: a retry this tick can still do the whole pass.
            throw new InvalidOperationException("Signal re-evaluation could not read the enabled policies.", ex);
        }

        foreach (var policy in policies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failuresBefore = counts.PolicyFailures + counts.InvalidPolicies;
            await EvaluatePolicyAsync(policy, counts, cancellationToken).ConfigureAwait(false);
            if (counts.PolicyFailures + counts.InvalidPolicies > failuresBefore)
            {
                counts.PoliciesFailedThisRun.Add(policy.Code);
            }
        }

        // Task 145 (uac-r2 review F1 on #1874): an in-force policy that stopped before its redaction (invalid rule, no current
        // version or lane, a version not in force, a partition skip, a fault, or a scoped policy the nightly pass does not
        // evaluate) still has its stored evidence checked.
        await SweepStoredEvidenceAsync(
            policies.Select(p => p.Code), "is in force but was not evaluated to the end this run", counts, cancellationToken).ConfigureAwait(false);

        await ClosePoliciesNotInForceAsync(counts, cancellationToken).ConfigureAwait(false);
        await RedactPoliciesNotInForceAsync(counts, cancellationToken).ConfigureAwait(false);

        var success = counts.IsSuccess;
        var duration = Finish(context, counts, started, success);
        if (success)
        {
            await RecordLastSuccessAsync(cancellationToken).ConfigureAwait(false);
        }

        return new JobRunResult(
            Success: success,
            ErrorMessage: success ? null : counts.FailureSummary(),
            ProcessedItems: counts.Hits,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(counts.ToResult(context.Attempt), ResultJsonOptions));
    }

    /// <summary>
    /// Task 033 / task 101 contract: runs the SAME single-policy pass the nightly job runs (supersede the Signals citing an older
    /// version, plan, raise the new version's episodes), for one policy, now. Task 101's publish calls this after it has stamped
    /// the old version's <c>sprk_inforceto</c> and moved <c>sprk_currentversion</c>. A policy that is Off or Retired closes its
    /// Signals through <see cref="SignalClosureService.ClosePolicyRetiredNowAsync"/> instead and is not evaluated. The result
    /// reports success exactly as the nightly run would (any refusal or invalid policy is a failure); callers MUST check it.
    /// </summary>
    public async Task<PolicyReevaluationResult> ReevaluatePolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        if (policyId == Guid.Empty) throw new ArgumentException("policyId must not be Guid.Empty.", nameof(policyId));
        var row = await _entities.RetrieveAsync("sprk_policy", policyId, PolicyColumns, cancellationToken).ConfigureAwait(false);
        var enabled = row.GetAttributeValue<bool?>("sprk_enabled") ?? false;
        var active = row.GetAttributeValue<OptionSetValue>("statecode")?.Value is null or 0;
        if (!enabled || !active)
        {
            throw new InvalidOperationException(
                $"Policy {policyId} is Off or Retired; it is not evaluated. Close its Signals with SignalClosureService.ClosePolicyRetiredNowAsync.");
        }

        var counts = new RunCounts();
        await EvaluatePolicyAsync(ToPolicyRow(row), counts, cancellationToken).ConfigureAwait(false);
        return new PolicyReevaluationResult(counts.IsSuccess, counts.SupersededClosed, counts.Written + counts.Reraised, counts.FailureSummary());
    }

    private async Task EvaluatePolicyAsync(PolicyRow policy, RunCounts counts, CancellationToken ct, SubjectScope? scope = null)
    {
        if (policy.IsScoped)
        {
            // A tenant- or matter-scoped policy needs a scope the nightly pass does not have (defer-issues row, task 031).
            counts.ScopedNotEvaluated++;
            _logger.LogWarning(OntologyWriterEvents.EvaluatorPolicyFailed,
                "Policy {PolicyCode} is tenant- or matter-scoped; the nightly pass evaluates unscoped policies only, so it was " +
                "NOT evaluated.", policy.Code);
            return;
        }

        try
        {
            if (policy.CurrentVersionId is not { } versionId)
            {
                throw new InvalidOperationException($"Policy {policy.Code} has no current version (sprk_currentversion is empty).");
            }

            if (policy.Lane is not (SignalWriter.LaneDecide or SignalWriter.LaneDo))
            {
                // Cadence and the re-raise rule both derive from the lane; never guess one.
                throw new InvalidOperationException($"Policy {policy.Code} has no valid sprk_lane ({policy.Lane?.ToString(CultureInfo.InvariantCulture) ?? "empty"}).");
            }

            var lane = policy.Lane.Value;

            var version = await _entities.RetrieveAsync(
                "sprk_policyversion", versionId, new[] { "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_inforceto" }, ct)
                .ConfigureAwait(false);
            if (version.GetAttributeValue<DateTime?>("sprk_inforceto") is { } inForceTo && inForceTo <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                // No version is in force: the pointer is behind a publish, or the data is inconsistent. Superseding would reopen
                // the same version every night, so nothing is evaluated or closed for this policy and the run is failed (visible).
                counts.PolicyFailures++;
                _logger.LogError(OntologyWriterEvents.PolicyVersionNotInForce,
                    "Policy {PolicyCode}: its current version {VersionId} ended in force at {InForceTo:o}; nothing was evaluated, " +
                    "closed or raised for it. Fix sprk_currentversion or sprk_inforceto.", policy.Code, versionId, inForceTo);
                return;
            }

            var spec = new SignalEvaluationPolicy(
                policy.Id, policy.Code, lane, versionId,
                version.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(CultureInfo.InvariantCulture),
                version.GetAttributeValue<string>("sprk_rulebody"),
                version.GetAttributeValue<string>("sprk_messagetemplate"));

            if (scope is { } only && RuleSubjectEntity(spec.RuleBody) is { } ruleSubject
                && !string.Equals(ruleSubject, only.Entity, StringComparison.Ordinal))
            {
                // Task 032: an event trigger names ONE subject; a policy about another table cannot hold or clear for it.
                counts.NotApplicable++;
                return;
            }

            counts.PoliciesEvaluated++;
            if (lane == SignalWriter.LaneDo) counts.DoLanePolicies++;
            else counts.DecideLanePolicies++;

            var evaluation = await _evaluator.EvaluateAsync(spec, scope?.Id, ct).ConfigureAwait(false);
            if (!evaluation.PolicyValid)
            {
                // Logged and metered by the validator gate (50301). Nothing is closed on an unknown truth value.
                counts.InvalidPolicies++;
                return;
            }

            counts.Hits += evaluation.Hits.Count;
            // D-120 (task 025b): a subject the rule could not judge (an input absent) is neither a hit nor observed false.
            var unjudged = new HashSet<(string, Guid)>();
            foreach (var m in evaluation.MissingInputs ?? Array.Empty<SignalMissingInput>())
            {
                unjudged.Add((m.SubjectEntity, m.SubjectId));
            }

            counts.MissingInputs += unjudged.Count;
            var read = await ReadPolicySignalsAsync(policy.Code, counts, ct, scope).ConfigureAwait(false);
            // Task 033 (FR-16, D-13): Signals citing an older version close Superseded first, so the planner below re-raises
            // the subjects the current version still holds as new episodes in this same pass.
            var superseded = await _closure.SupersedeInPassAsync(policy.Code, versionId, read, ct).ConfigureAwait(false);
            counts.AddClosure(superseded.Outcome, retired: false);
            // Task 034 (FR-17) and #1661: place every stamped or countable Signal in its secure-family partition, stamp a partition
            // found at the threshold and withdraw a stamp its partition no longer justifies, so the planner below sees the result.
            var reconciled = await _suppression.ReconcileAsync(policy.Id, policy.Code, policy.CountsTowardSuppression, superseded.Existing, ct).ConfigureAwait(false);
            counts.PolicyFailures += reconciled.Failures;
            if (reconciled.PartitionsUnavailable)
            {
                // #1661 round 2 F3: without the partitions no stamp can be read as a mute or as no mute, for ANY partition. Nothing is
                // planned for the policy (no create, close, reconcile or reassign), so one timeout never re-raises every suppressed subject.
                SkipForPartitions(policy, counts, reconciled.Failures,
                    "its Signals could not be placed in their secure-family partitions or counted, so no stamp was written either");
                return;
            }

            var existing = reconciled.Signals;
            // #1661 R1 (D-123): a hit's new episode belongs to the partition the writer's truth gives it NOW, never its previous
            // episode's. Asked only for hits on a record that carries a live stamp (where the answer can change the outcome).
            var now = _timeProvider.GetUtcNow();
            var hitPartitions = await _suppression.PartitionsOfHitsAsync(
                policy.Code, SignalEpisodePlanner.HitsOnStampedGrains(evaluation.Hits, existing, now), ct).ConfigureAwait(false);
            if (hitPartitions is null)
            {
                // #1661 round 2 F2: a fault is never answered per hit (that would depend on which record carries a stamp): the whole
                // policy waits for the next pass.
                counts.PolicyFailures++;
                SkipForPartitions(policy, counts, 1,
                    "the secure-family partitions of its hits could not be read (the suppression pass before it completed and wrote only justified stamps)");
                return;
            }

            var plan = SignalEpisodePlanner.Plan(lane, evaluation.QuietWindowDays, now, evaluation.Hits, existing, hitPartitions,
                isThreshold: RuleBodySchemaValidator.TryParseRuleType(spec.RuleType, out var ruleType) && ruleType == RuleType.Threshold,
                unjudged: unjudged);
            if (superseded.NotClosed.Count > 0)
            {
                // A supersede close that did not land (changed since read / failed) is already counted; the planner must not
                // retry the same stale row in this pass. The next pass re-reads it.
                plan.Closes.RemoveAll(c => superseded.NotClosed.Contains(c.Id));
                plan.Reconciles.RemoveAll(r => superseded.NotClosed.Contains(r.Signal.Id));
                plan.Reassigns.RemoveAll(r => superseded.NotClosed.Contains(r.Signal.Id));
                plan.ClearedMarks.RemoveAll(m => superseded.NotClosed.Contains(m.Id));
            }

            counts.Add(plan);
            LogRefusedCores(policy, plan);
            if (plan.Anomalies > 0)
            {
                _logger.LogWarning(
                    "Policy {PolicyCode}: {Anomalies} Signal(s) were left untouched because their status is unreadable or an older " +
                    "episode is still open alongside a newer one.", policy.Code, plan.Anomalies);
            }

            if (plan.NoDatedEvidence > 0)
            {
                // One line per policy per pass, never per Signal and never retried within the pass (D-119).
                _logger.LogInformation(
                    "Policy {PolicyCode}: {Count} Acted Signal(s) still hold but their hit carries no dated evidence (or the " +
                    "resolution time is unknown), so they stay un-raised.", policy.Code, plan.NoDatedEvidence);
            }

            var closedNow = await ApplyAsync(policy, spec, plan, counts, ct).ConfigureAwait(false);
            // Last, so its conditional writes never invalidate the version this pass's own closes and reconciles were planned on.
            await RedactStoredEvidenceAsync(policy.Code, existing, counts, ct).ConfigureAwait(false);
            if (lane == SignalWriter.LaneDo)
            {
                // Task 153 (D-145b): after the redaction, whose conditional writes are planned on the versions read; each link below reads
                // its Signal again first.
                await RecordConditionClearedAsync(policy, evaluation.Hits, unjudged, existing, closedNow, now, counts, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.PolicyFailures++;
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Policy {PolicyCode} could not be re-evaluated this run ({ExceptionType}); its Signals were not changed.",
                policy.Code, ex.GetType().Name);
        }
    }

    /// <summary>Task 033 (FR-16, D-32): open Signals of a policy that is Off or Retired close PolicyRetired. A policy that is not
    /// in force is not evaluated, so this is the only step that touches its Signals.</summary>
    private async Task ClosePoliciesNotInForceAsync(RunCounts counts, CancellationToken ct)
    {
        try
        {
            counts.AddClosure(await _closure.ClosePoliciesNotInForceAsync(ct).ConfigureAwait(false), retired: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.PolicyFailures++;
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Policies that are Off or Retired could not be read ({ExceptionType}); their open Signals were not closed this run.",
                ex.GetType().Name);
        }
    }

    /// <summary>#1661 round 2 F2/F3: nothing is planned for the policy this pass (its Superseded closures, which run first, aside), and it
    /// is named in the run result with its failures, not only in the log.</summary>
    private void SkipForPartitions(PolicyRow policy, RunCounts counts, int failures, string reason)
    {
        counts.PartitionSkips.Add((policy.Id, policy.Code, failures));
        _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed,
            "Policy {PolicyCode} ({PolicyId}): {Reason}; nothing was raised, closed, reconciled or reassigned for it this run. " +
            "The next run retries.", policy.Code, policy.Id, reason);
    }

    private void LogRefusedCores(PolicyRow policy, EpisodePlan plan)
    {
        foreach (var hit in plan.RefusedHits)
        {
            // D-38: a refusal, never a skip. Same EventId and metric as every writer refusal, with the writer's escalation (its
            // message and inner fault) attached. Task 143 (review F1): a no-core item's OWNER refusal or unresolved owner is
            // named as such; its core record is not the problem.
            var reason = hit.CoreRefusalReason ?? OntologyWriterFailureReason.CoreRecordUnresolved;
            if (reason is OntologyWriterFailureReason.OwnerRefused or OntologyWriterFailureReason.NoCoreOwnerUnresolved)
            {
                _logger.LogError(OntologyWriterEvents.WriteRefused, hit.CoreRefusalCause,
                    "Signal writer refused for policy {PolicyCode} (reason={Reason}): {SubjectEntity} {SubjectId} has no core record " +
                    "and the owner of its Signal cannot be resolved (D-130, D-134). Nothing was written for it.",
                    policy.Code, reason, hit.SubjectEntity, hit.SubjectId);
            }
            else
            {
                _logger.LogError(OntologyWriterEvents.WriteRefused, hit.CoreRefusalCause,
                    "Signal writer refused for policy {PolicyCode} (reason={Reason}): the core record of {SubjectEntity} {SubjectId} " +
                    "cannot be derived. Nothing was written for it.", policy.Code, reason, hit.SubjectEntity, hit.SubjectId);
            }

            OntologyWriterTelemetry.RecordFailure(reason);
        }
    }

    /// <returns>Task 153: every Signal this pass closed as ConditionCleared, with the time it was closed.</returns>
    private async Task<IReadOnlyDictionary<Guid, DateTime>> ApplyAsync(
        PolicyRow policy, SignalEvaluationPolicy spec, EpisodePlan plan, RunCounts counts, CancellationToken ct)
    {
        // Task 145 (D-123, #1747 finding e): a Signal never carries the subject or sender of a witness in another (or an undetermined)
        // secure family. Every write below can create a row (a reconcile creates when its row is gone), so both take the filtered lines.
        var evidence = await _suppression.EvidenceWithinPartitionAsync(
            policy.Code, plan.Reconciles.Select(r => r.Hit).Concat(plan.Creates.Select(c => c.Hit)).ToList(), ct).ConfigureAwait(false);
        SignalHit Writable(SignalHit hit) =>
            evidence.TryGetValue((hit.SubjectEntity, hit.SubjectId), out var lines) ? hit with { Evidence = lines } : hit;

        // Ids of the Signals this pass actually closed. A re-file's new episode is created only when its predecessor closed.
        var closedIds = new HashSet<Guid>();
        var closedAt = new Dictionary<Guid, DateTime>();
        foreach (var signal in plan.Closes)
        {
            await GuardAsync(policy, "close", signal.SubjectId, counts, writerLogged: true, async () =>
            {
                var outcome = await _writer.CloseAsync(signal.Id, signal.RowVersion, SignalWriter.ResolutionConditionCleared, policy.Code, ct)
                    .ConfigureAwait(false);
                if (outcome == SignalCloseOutcome.Closed)
                {
                    counts.Closed++;
                    closedIds.Add(signal.Id);
                    closedAt[signal.Id] = _timeProvider.GetUtcNow().UtcDateTime;
                }
                else
                {
                    counts.ChangedSinceRead++;
                }
            }).ConfigureAwait(false);
        }

        foreach (var signal in plan.ClearedMarks)
        {
            await GuardAsync(policy, "cleared-marker", signal.SubjectId, counts, writerLogged: true, async () =>
            {
                if (await _writer.MarkClearedObservedAsync(signal.Id, signal.RowVersion, policy.Code, ct).ConfigureAwait(false)) counts.ClearedMarked++;
                else counts.ChangedSinceRead++;
            }).ConfigureAwait(false);
        }

        // Task 143 (review F2): a Signal whose re-own did not land this pass (refused, changed since read, gone, or failed: each
        // already counted once) is not also reconciled, which would only meet the old owner and report the same Signal again.
        var unsettledReowns = new HashSet<Guid>();
        foreach (var (signal, owner) in plan.Reassigns)
        {
            unsettledReowns.Add(signal.Id);
            await GuardAsync(policy, "reassign", signal.SubjectId, counts, writerLogged: true, async () =>
            {
                // Task 143: the re-own goes through uac-r2's ReownOrdinaryRowAsync (via the writer), which refuses a Secure-BU row
                // or team and a user-owned row; a refusal is already logged and metered by the writer and makes the run partial.
                switch (await _writer.ReassignNoCoreOwnerAsync(signal.Id, owner, policy.Code, ct).ConfigureAwait(false))
                {
                    case Sprk.Bff.Api.Services.Access.OrdinaryRowReownOutcome.Reowned:
                        counts.Reassigned++;
                        unsettledReowns.Remove(signal.Id);
                        break;
                    case Sprk.Bff.Api.Services.Access.OrdinaryRowReownOutcome.AlreadyOwned:
                        counts.Unchanged++;
                        unsettledReowns.Remove(signal.Id);
                        break;
                    case Sprk.Bff.Api.Services.Access.OrdinaryRowReownOutcome.NotFound
                        or Sprk.Bff.Api.Services.Access.OrdinaryRowReownOutcome.Conflict:
                        counts.ChangedSinceRead++;
                        break;
                    default:
                        counts.Refused++;
                        break;
                }
            }).ConfigureAwait(false);
        }

        foreach (var (signal, hit) in plan.Reconciles)
        {
            if (unsettledReowns.Contains(signal.Id))
            {
                _logger.LogInformation(
                    "Policy {PolicyCode}: Signal {SignalId} was not reconciled because its re-own did not land this pass; the next pass retries both.",
                    policy.Code, signal.Id);
                continue;
            }

            await GuardAsync(policy, "reconcile", hit.SubjectId, counts, writerLogged: false, async () =>
            {
                var result = await _writer.WriteAsync(BuildRequest(policy, spec, Writable(hit), signal.Episode), ct).ConfigureAwait(false);
                if (result.IsSkipped) counts.Refused++;
                else counts.Updated++;
            }).ConfigureAwait(false);
        }

        foreach (var create in plan.Creates)
        {
            if (create.ReplacesSignalId is { } replaced && !closedIds.Contains(replaced))
            {
                // A re-file's episode n+1 must not open beside a still-open n (two open Signals for one subject, the old one
                // visible to the old core's members). The close was refused (changed since read) or failed (already counted
                // and logged); the next pass re-reads the subject and retries.
                counts.CreatesDeferred++;
                _logger.LogWarning(OntologyWriterEvents.EvaluatorPolicyFailed,
                    "Policy {PolicyCode}: episode {Episode} for {SubjectEntity} {SubjectId} was NOT created because the Signal it " +
                    "replaces ({ReplacedSignalId}) was not closed; the next pass retries.",
                    policy.Code, create.Episode, create.Hit.SubjectEntity, create.Hit.SubjectId, replaced);
                continue;
            }

            await GuardAsync(policy, "create", create.Hit.SubjectId, counts, writerLogged: false, async () =>
            {
                var result = await _writer.WriteAsync(BuildRequest(policy, spec, Writable(create.Hit), create.Episode), ct).ConfigureAwait(false);
                if (result.IsSkipped) counts.Refused++;
                else if (!result.Created) counts.Updated++;
                else if (create.Episode > 1) counts.Reraised++;
                else counts.Written++;
            }).ConfigureAwait(false);
        }

        return closedAt;
    }

    /// <summary>Task 153: how long after its close a Do-lane ConditionCleared Signal whose record could not be written or linked is still
    /// retried by a later pass. Past it the closure stays unrecorded (it was logged and metered on every failed attempt).</summary>
    internal static readonly TimeSpan ConditionClearedRecordRetryWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Task 153 (D-145b, D-139): one Routine Decision Record per Do-lane Signal closed as ConditionCleared, written AFTER the close and never
    /// instead of it: a failure leaves the Signal closed, is logged and metered (<c>ontology.writer.failures</c>, by the writer) and fails the
    /// run, and a later pass retries through the record's idempotent key.
    /// </summary>
    /// <remarks>
    /// <para><b>Which closures.</b> (1) Every Signal this pass closed whose subject is not a hit: the condition cleared. A re-file's close (its
    /// subject still a hit, a new episode opens under the new core) is not a satisfied item and gets no record. (2) Retries: a Signal of this
    /// policy that is Resolved as ConditionCleared, links no record, was closed within <see cref="ConditionClearedRecordRetryWindow"/>, is
    /// the latest episode of its subject, and whose subject is neither a hit nor unjudged this pass. A Signal that already names a record is
    /// never touched (D-61: a re-run writes nothing).</para>
    /// <para><b>Per Signal.</b> The core record's table (once per type per pass), D-134's assignee for a no-core To Do, the writer's entry
    /// point (lookup, attribution read, filing, owner, create), then the link (<see cref="SignalWriter.LinkConditionClearedRecordAsync"/>).</para>
    /// </remarks>
    private async Task RecordConditionClearedAsync(
        PolicyRow policy, IReadOnlyList<SignalHit> hits, IReadOnlySet<(string, Guid)> unjudged, IReadOnlyList<ExistingSignal> existing,
        IReadOnlyDictionary<Guid, DateTime> closedNow, DateTimeOffset now, RunCounts counts, CancellationToken ct)
    {
        var hitSubjects = hits.Select(h => (h.SubjectEntity, h.SubjectId)).ToHashSet();
        var latestEpisode = existing
            .GroupBy(s => (s.SubjectEntity, s.SubjectId))
            .ToDictionary(g => g.Key, g => g.Max(s => s.Episode));
        var retryFrom = now.UtcDateTime - ConditionClearedRecordRetryWindow;

        var candidates = new List<(ExistingSignal Signal, DateTime ClosedOn)>();
        foreach (var signal in existing)
        {
            var subject = (signal.SubjectEntity, signal.SubjectId);
            if (hitSubjects.Contains(subject) || unjudged.Contains(subject))
            {
                continue; // still a hit (a re-file), or not judged: the condition is not known to have cleared
            }

            if (closedNow.TryGetValue(signal.Id, out var closedOn))
            {
                candidates.Add((signal, closedOn));
            }
            else if (signal.IsResolved
                     && signal.Resolution == SignalWriter.ResolutionConditionCleared
                     && signal.DecisionRecordId is null
                     && signal.ResolvedOn is { } resolvedOn
                     && DateTime.SpecifyKind(resolvedOn, DateTimeKind.Utc) >= retryFrom
                     && latestEpisode[subject] == signal.Episode)
            {
                candidates.Add((signal, DateTime.SpecifyKind(resolvedOn, DateTimeKind.Utc)));
            }
        }

        if (candidates.Count == 0) return;

        var coreTables = new Dictionary<Guid, string?>();
        int recorded = 0, failed = 0;
        foreach (var (signal, closedOn) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                DecisionRecordCore? core = null;
                if (signal.CoreTypeRefId is { } typeRef && signal.CoreRecordId is { } coreId)
                {
                    if (!coreTables.TryGetValue(typeRef, out var table))
                    {
                        var row = await _entities.RetrieveAsync("sprk_recordtype_ref", typeRef, new[] { "sprk_recordlogicalname" }, ct).ConfigureAwait(false);
                        table = row.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim().ToLowerInvariant();
                        coreTables[typeRef] = table;
                    }

                    if (table is null || !CoreAncestorResolver.CoreRecordEntities.Contains(table, StringComparer.Ordinal))
                    {
                        // D-34: the record copies its Signal's core; a core type that names no core table cannot be copied. Never written
                        // without it (that would file the closure as a no-core item's).
                        failed++;
                        _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed,
                            "Policy {PolicyCode}: the closure of Signal {SignalId} was not recorded (reason={Reason}): its core record type names no core table. The Signal stays closed.",
                            policy.Code, signal.Id, OntologyWriterFailureReason.CoreRecordTypeNotCataloged);
                        OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.CoreRecordTypeNotCataloged);
                        continue;
                    }

                    core = new DecisionRecordCore(table, coreId, typeRef);
                }

                var assignee = core is null && string.Equals(signal.SubjectEntity, PersonalToDoAssignee.Table, StringComparison.Ordinal)
                    ? await _writer.PersonalToDoAssigneeAsync(signal.SubjectId, ct).ConfigureAwait(false)
                    : null;
                var result = await _records.WriteConditionClearedAsync(new ConditionClearedRecordRequest(
                    signal.Id, signal.Episode, policy.Code, signal.PolicyVersionId ?? Guid.Empty,
                    new DecisionRecordRef(signal.SubjectEntity, signal.SubjectId), core, closedOn,
                    signal.LineageRow?.GetAttributeValue<DateTime?>("createdon"),
                    AssigneeSystemUserId: assignee,
                    PrivilegeFlagged: signal.LineageRow?.GetAttributeValue<bool?>("sprk_privilegeflagged") ?? false), ct).ConfigureAwait(false);
                if (!result.AlreadyRecorded) recorded++;

                switch (await _writer.LinkConditionClearedRecordAsync(signal.Id, result.RecordId, result.ConfirmedByUserId, policy.Code, ct).ConfigureAwait(false))
                {
                    case SignalRecordLinkOutcome.ChangedSinceRead:
                        counts.ChangedSinceRead++;
                        break;
                    case SignalRecordLinkOutcome.Linked:
                        counts.ClosureRecordsLinked++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The Signal stays closed. The writer (a DecisionRecordRefusedException) and SignalWriter (its escalation) have already
                // logged and metered their own failures; anything else is logged and metered here, once.
                failed++;
                if (ex is not DecisionRecordRefusedException && !SignalWriter.IsMeteredByWriter(ex))
                {
                    _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                        "Policy {PolicyCode}: the closure of Signal {SignalId} could not be recorded ({ExceptionType}); the Signal stays closed and the next pass retries.",
                        policy.Code, signal.Id, ex.GetType().Name);
                    OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.WriteFailed);
                }
            }
        }

        counts.ClosureRecorded += recorded;
        counts.ClosureRecordFailures += failed;
        _logger.LogInformation(
            "Policy {PolicyCode}: {Recorded} condition-cleared closure(s) recorded, {Failed} not recorded this pass (of {Candidates}).",
            policy.Code, recorded, failed, candidates.Count);
    }

    /// <summary>One write. A failure is counted as refused and logged at Error with the job's EventId; it is metered here only
    /// when neither the writer nor its client has already metered it.</summary>
    private async Task GuardAsync(
        PolicyRow policy, string action, Guid subjectId, RunCounts counts, bool writerLogged, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            counts.Refused++;
            if (SignalWriter.IsDeterministicPrivilegeRefusal(ex))
            {
                // K1 (task 143 follow-up): a privilege refusal (0x80040299 / 0x80040220) is deterministic: the same write is refused
                // the same way on a redelivery, so it is counted apart from the refusals a retry can change (SubjectEvaluationResult
                // does not retry on it). The writer already logged it at Error with the exception (and, for a create or re-own, the
                // owner team) and metered it as dataverse_access_denied, so this is a Warning without a second Error or a second metric.
                // A writer escalation that merely WRAPS a denial (a read-back or resolver fault) is not this case and stays retryable.
                // The nightly run still counts it Refused: the misconfigured team stays a failed heartbeat until its role is fixed.
                counts.AccessDenied++;
                _logger.LogWarning(OntologyWriterEvents.EvaluatorPolicyFailed,
                    "Signal {Action} refused for policy {PolicyCode}, subject {SubjectId} (reason={Reason}); nothing was changed for it. " +
                    "A retry cannot fix a missing privilege.",
                    action, policy.Code, subjectId, OntologyWriterFailureReason.DataverseAccessDenied);
                return;
            }

            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Signal {Action} failed for policy {PolicyCode}, subject {SubjectId} ({ExceptionType}); nothing was changed for it.",
                action, policy.Code, subjectId, ex.GetType().Name);
            if (!writerLogged && !SignalWriter.IsMeteredByWriter(ex))
            {
                OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.WriteFailed);
            }
        }
    }

    private static SignalWriteRequest BuildRequest(PolicyRow policy, SignalEvaluationPolicy spec, SignalHit hit, int episode) =>
        new(
            PolicyId: policy.Id,
            PolicyCode: policy.Code,
            PolicyVersionId: spec.PolicyVersionId,
            SubjectEntityLogicalName: hit.SubjectEntity,
            SubjectId: hit.SubjectId,
            SubjectDisplayName: hit.DisplayName,
            ShortHeadline: policy.Headline,
            Lane: spec.Lane,
            Severity: policy.Severity,
            MessageTemplate: spec.MessageTemplate ?? string.Empty,
            FactValues: hit.FactValues,
            EvidenceRefs: hit.Evidence)
        {
            Episode = episode,
            SubjectRecordNumber = hit.RecordNumber,
        };

    private TimeSpan Finish(JobRunContext context, RunCounts counts, long started, bool success)
    {
        var duration = _timeProvider.GetElapsedTime(started);
        var now = _timeProvider.GetUtcNow();
        OntologyWriterTelemetry.RecordEvaluatorRun(success, now, counts.ByAction());
        // THE HEARTBEAT (ADR-036 A1 rule 5) and the run-level last-evaluated (D-61): every attempt.
        _logger.Log(success ? LogLevel.Information : LogLevel.Warning, OntologyWriterEvents.EvaluatorHeartbeat,
            "[SIGNAL-EVALUATOR] heartbeat outcome={Outcome} lastEvaluated={LastEvaluated:o} policies={Policies} hits={Hits} " +
            "written={Written} reraised={Reraised} updated={Updated} closed={Closed} supersededClosed={SupersededClosed} policyRetiredClosed={PolicyRetiredClosed} reassigned={Reassigned} refused={Refused} " +
            "unchanged={Unchanged} suppressed={Suppressed} quietWindow={QuietWindow} dateUnchanged={DateUnchanged} awaitingEvidence={AwaitingEvidence} noDatedEvidence={NoDatedEvidence} clearedMarked={ClearedMarked} missingInputs={MissingInputs} " +
            "invalidPolicies={InvalidPolicies} policyFailures={PolicyFailures} attempt={Attempt} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            success ? "succeeded" : "failed", now, counts.PoliciesEvaluated, counts.Hits, counts.Written, counts.Reraised,
            counts.Updated, counts.Closed, counts.SupersededClosed, counts.RetiredClosed, counts.Reassigned, counts.Refused, counts.Unchanged, counts.Suppressed,
            counts.QuietWindow, counts.DateUnchanged, counts.AwaitingEvidence, counts.NoDatedEvidence, counts.ClearedMarked, counts.MissingInputs, counts.InvalidPolicies, counts.PolicyFailures, context.Attempt,
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);
        return duration;
    }

    private async Task RecordLastSuccessAsync(CancellationToken ct)
    {
        try
        {
            await _cache.SetStringAsync(LastSuccessCacheKey,
                _timeProvider.GetUtcNow().ToString("o", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The run itself succeeded; the health check will report it stale, which is the visible symptom.
            _logger.LogWarning(ex, "Signal re-evaluation succeeded but its last-success time could not be stored.");
        }
    }

    // ── Event triggers (task 032) ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The one subject an event trigger evaluates (task 032).</summary>
    internal readonly record struct SubjectScope(string Entity, Guid Id);

    /// <summary>
    /// Task 032 (D-12, D-48): the event-triggered pass. Evaluates every enabled <b>Decide-lane</b> policy about
    /// <paramref name="subjectEntity"/> for the ONE subject, through <see cref="EvaluatePolicyAsync"/> - the SAME evaluation,
    /// episode planner and writer path as the nightly pass (a second path is how the two come to disagree). Do-lane policies
    /// are not evaluated here (nightly only, cadence from <c>sprk_lane</c>). A tenant- or matter-scoped policy is skipped with the
    /// nightly Warning (F-62) and does not fail the trigger. A Signal already Resolved is never closed again (FR-16), so a
    /// revision recorded through a decision leaves its Acted Signal Acted.
    /// </summary>
    /// <exception cref="InvalidOperationException">The enabled policies could not be read; the caller must retry.</exception>
    internal async Task<SubjectEvaluationResult> EvaluateSubjectAsync(string subjectEntity, Guid subjectId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectEntity);
        if (subjectId == Guid.Empty) throw new ArgumentException("A subject id is required.", nameof(subjectId));

        var counts = new RunCounts();
        IReadOnlyList<PolicyRow> policies;
        try
        {
            policies = await ReadEnabledPoliciesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "A signal trigger could not read the enabled policies for {SubjectEntity} {SubjectId}.", subjectEntity, subjectId);
            throw new InvalidOperationException("Signal trigger could not read the enabled policies.", ex);
        }

        var scope = new SubjectScope(subjectEntity, subjectId);
        foreach (var policy in policies.Where(p => p.Lane == SignalWriter.LaneDecide))
        {
            ct.ThrowIfCancellationRequested();
            await EvaluatePolicyAsync(policy, counts, ct, scope).ConfigureAwait(false);
        }

        return new SubjectEvaluationResult(
            counts.PoliciesEvaluated, counts.Hits, counts.Written + counts.Reraised, counts.Updated, counts.Closed,
            counts.Refused, counts.PolicyFailures, counts.InvalidPolicies, counts.ScopedNotEvaluated, counts.AccessDenied);
    }

    /// <summary>The rule body's <c>subject</c> table, or null when the body cannot be read (the evaluator's gate then logs and
    /// meters it; this is only a cheap pre-filter, never the validation).</summary>
    private static string? RuleSubjectEntity(string? ruleBody)
    {
        try
        {
            using var doc = PredicateCompiler.ParseBounded(ruleBody);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("subject", out var subject)
                   && subject.ValueKind == JsonValueKind.String
                ? subject.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or PredicateCompilationException)
        {
            return null;
        }
    }

    // ── Reads ─────────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record PolicyRow(
        Guid Id, string Code, int? Lane, int? Severity, string Headline, Guid? CurrentVersionId, bool IsScoped, bool CountsTowardSuppression = true);

    /// <summary>Every enabled, active policy (the same filter as <see cref="PolicyScopeResolver"/>: <c>sprk_enabled</c> and
    /// <c>statecode</c> Active, so a disabled or Retired policy is not evaluated at all), with its lane, severity and headline.</summary>
    private async Task<IReadOnlyList<PolicyRow>> ReadEnabledPoliciesAsync(CancellationToken ct)
    {
        var query = new QueryExpression("sprk_policy")
        {
            ColumnSet = new ColumnSet("sprk_policycode", "sprk_lane", "sprk_severity", "sprk_shortname", "sprk_name",
                "sprk_currentversion", "sprk_tenant", "sprk_matter", "sprk_countstowardsuppression"),
        };
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.AddOrder("sprk_policycode", OrderType.Ascending);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        return rows.Entities.Select(ToPolicyRow).ToList();
    }

    private static readonly string[] PolicyColumns =
    {
        "sprk_policycode", "sprk_lane", "sprk_severity", "sprk_shortname", "sprk_name", "sprk_currentversion", "sprk_tenant",
        "sprk_matter", "sprk_enabled", "statecode", "sprk_countstowardsuppression",
    };

    private static PolicyRow ToPolicyRow(Entity row)
    {
        var code = row.GetAttributeValue<string>("sprk_policycode") ?? string.Empty;
        var headline = row.GetAttributeValue<string>("sprk_shortname") is { Length: > 0 } s ? s
            : row.GetAttributeValue<string>("sprk_name") is { Length: > 0 } n ? n : code;
        var version = row.GetAttributeValue<EntityReference>("sprk_currentversion");
        return new PolicyRow(
            row.Id,
            code,
            row.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value,
            row.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value,
            headline.Length > 200 ? headline[..200] : headline,
            version is { Id: var id } && id != Guid.Empty ? id : null,
            !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("sprk_tenant"))
                || row.GetAttributeValue<EntityReference>("sprk_matter") is { Id: var m } && m != Guid.Empty,
            row.GetAttributeValue<bool?>("sprk_countstowardsuppression") ?? true);
    }

    /// <summary>Every Signal of the policy, by the begins-with query D-13 names, keyed exactly by the parsed key. A key that
    /// does not parse is an Error and fails the run (its subject's episode cannot be known).</summary>
    /// <summary>
    /// Ontology task 145, review F1 (D-123; main session 2026-10-14, option A): every Signal of the policy read this pass, open AND
    /// resolved, loses the stored evidence detail that is no longer within its secure family
    /// (<see cref="SignalSuppressionService.StoredEvidenceToRedactAsync"/>), through <see cref="SignalWriter.RedactEvidenceAsync"/>,
    /// conditional on the version read. Monotonic (detail is removed, never added). A Signal changed since the read is counted
    /// <c>changedSinceRead</c> and retried next pass; when the families cannot be read nothing is written (a policy failure, retried
    /// next pass); a write failure is a refusal. Evidence is not a stamp, so task 144's report-only tables do not apply.
    /// </summary>
    private async Task RedactStoredEvidenceAsync(string policyCode, IReadOnlyList<ExistingSignal> signals, RunCounts counts, CancellationToken ct)
    {
        // Once per policy per run: the sweeps after the policy loop skip a policy already attempted here (whatever the outcome).
        counts.EvidenceRedactionAttempted.Add(policyCode);
        var redactions = await _suppression.StoredEvidenceToRedactAsync(policyCode, signals, ct).ConfigureAwait(false);
        if (redactions is null)
        {
            counts.PolicyFailures++;
            return;
        }

        foreach (var signal in signals)
        {
            if (!redactions.TryGetValue(signal.Id, out var redacted)) continue;
            if (string.IsNullOrWhiteSpace(signal.RowVersion))
            {
                counts.Refused++;
                _logger.LogWarning(
                    "Policy {PolicyCode}: Signal {SignalId} has stored evidence to clear but no version was read, so it cannot be written conditionally; the next pass retries.",
                    policyCode, signal.Id);
                continue;
            }

            try
            {
                if (await _writer.RedactEvidenceAsync(signal.Id, signal.RowVersion, redacted, policyCode, ct).ConfigureAwait(false)) counts.EvidenceRedacted++;
                else counts.ChangedSinceRead++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.Refused++; // logged and metered by the writer
            }
        }
    }

    /// <summary>
    /// Task 145 (review F1): a policy that is Off or Retired is never evaluated, so its Signals (closed PolicyRetired, or resolved
    /// before) would never pass through <see cref="RedactStoredEvidenceAsync"/> again. The nightly pass reads them, after the Off/Retired
    /// close, and redacts them by the same rule. A policy whose Signals cannot be read is a policy failure, retried next pass.
    /// </summary>
    private async Task RedactPoliciesNotInForceAsync(RunCounts counts, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, string> policies;
        try
        {
            policies = await _closure.PoliciesNotInForceAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.PolicyFailures++;
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Policies that are Off or Retired could not be read ({ExceptionType}); their Signals' stored evidence was not checked this run.",
                ex.GetType().Name);
            return;
        }

        await SweepStoredEvidenceAsync(policies.Values, "is not in force", counts, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 145: reads the Signals of each policy in <paramref name="policyCodes"/> that this run has not yet sent through
    /// <see cref="RedactStoredEvidenceAsync"/> (<see cref="RunCounts.EvidenceRedactionAttempted"/>), and redacts their stored
    /// evidence. Never twice per policy per run. A policy whose Signals cannot be read is a policy failure (logged, nothing
    /// written, the run fails visibly, the next pass retries) unless the policy already failed this run, which is not counted
    /// again; a family-read fault is handled by <see cref="RedactStoredEvidenceAsync"/> the same way. The sweep reads in
    /// <c>forRedactionSweep</c> mode (uac-r2 re-check F1): an unparseable dedupe key is skipped without being counted, logged or
    /// metered again, and the policy's parseable Signals are still redacted.
    /// </summary>
    private async Task SweepStoredEvidenceAsync(IEnumerable<string> policyCodes, string why, RunCounts counts, CancellationToken ct)
    {
        foreach (var code in policyCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (counts.EvidenceRedactionAttempted.Contains(code)) continue;
            try
            {
                var signals = await ReadPolicySignalsAsync(code, counts, ct, forRedactionSweep: true).ConfigureAwait(false);
                await RedactStoredEvidenceAsync(code, signals, counts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.EvidenceRedactionAttempted.Add(code);
                // One failure per policy per run: a policy that already failed the run is logged here but not counted twice.
                if (counts.PoliciesFailedThisRun.Add(code)) counts.PolicyFailures++;
                _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                    "Policy {PolicyCode} {Why}, and its Signals could not be read ({ExceptionType}); their stored evidence was not checked this run.",
                    code, why, ex.GetType().Name);
            }
        }
    }

    private async Task<IReadOnlyList<ExistingSignal>> ReadPolicySignalsAsync(
        string policyCode, RunCounts counts, CancellationToken ct, SubjectScope? scope = null, bool forRedactionSweep = false)
    {
        // A scoped (event-trigger) pass reads only the one subject's episodes: the planner closes every open Signal whose
        // subject is not a hit, so handing it the policy's other subjects would close them (task 032).
        var prefix = scope is { } only
            ? SignalWriter.DedupeKeySubjectPrefix(policyCode, only.Entity, only.Id.ToString("D", CultureInfo.InvariantCulture))
            : SignalWriter.DedupeKeyPolicyPrefix(policyCode);
        var query = new QueryExpression("sprk_signal")
        {
            // #1661: the lineage lookups too, so the suppression pass can place each Signal in its secure-family partition.
            ColumnSet = new ColumnSet(new[]
            {
                "sprk_dedupekey", "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon",
                "sprk_suppresseduntil", "sprk_duedate", "sprk_corerecordtype", "sprk_corerecordid", "ownerid", "versionnumber",
                "sprk_policyversion", "sprk_lastevaluated", "sprk_decisionrecord",
                // Task 153: when the Signal was raised (the attribution window) and its privilege flag (copied to its closure record).
                "createdon", "sprk_privilegeflagged",
                // Task 145 (review F1): the stored evidence, for the redaction pass (RedactStoredEvidenceAsync).
                SignalSuppressionService.EvidenceColumn,
            }.Concat(SignalSuppressionService.SignalLineageColumns).Distinct(StringComparer.Ordinal).ToArray()),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition("sprk_dedupekey", ConditionOperator.BeginsWith, prefix);
        query.AddOrder("sprk_signalid", OrderType.Ascending);

        var signals = new List<ExistingSignal>();
        var unparseable = 0;
        for (var page = 1; ; page++)
        {
            if (page > SignalEvaluator.MaxPages)
            {
                throw new InvalidOperationException($"Policy {policyCode} has more than {SignalEvaluator.MaxPages} pages of Signals.");
            }

            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                var key = row.GetAttributeValue<string>("sprk_dedupekey");
                if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue; // BeginsWith is a LIKE: '_' or '%' in a code can match another policy's keys
                }

                if (SignalWriter.TryParseDedupeKey(key, out var parsedCode, out _, out _, out _)
                    && !string.Equals(parsedCode, policyCode, StringComparison.Ordinal))
                {
                    continue; // another policy's key under the same prefix (a code containing '|', e.g. POL vs POL|ODD)
                }

                if (!SignalWriter.TryParseDedupeKey(key, out var code, out var subjectEntity, out var subjectId, out var episode))
                {
                    if (forRedactionSweep)
                    {
                        // Task 145 (uac-r2 re-check F1): counting, logging and metering an unparseable key (and failing the run)
                        // is the evaluation pass's job; the sweep only redacts, and a row with no readable subject cannot be placed.
                        continue;
                    }

                    counts.UnparseableKeys++;
                    unparseable++;
                    _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed,
                        "Signal {SignalId} under policy {PolicyCode} has a dedupe key that is not {{code}}|{{type}}|{{id}}|{{episode}} " +
                        "(reason={Reason}); its subject is not re-raised or closed this run.",
                        row.Id, policyCode, OntologyWriterFailureReason.DedupeKeyUnparseable);
                    OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.DedupeKeyUnparseable);
                    continue;
                }

                signals.Add(new ExistingSignal(
                    Id: row.Id,
                    RowVersion: row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null),
                    SubjectEntity: subjectEntity,
                    SubjectId: subjectId,
                    Episode: episode,
                    Status: row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value,
                    Resolution: row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
                    ResolvedOn: row.GetAttributeValue<DateTime?>("sprk_resolvedon"),
                    SuppressedUntil: row.GetAttributeValue<DateTime?>("sprk_suppresseduntil"),
                    DueDate: row.GetAttributeValue<DateTime?>("sprk_duedate")?.Date,
                    CoreTypeRefId: row.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id,
                    CoreRecordId: Guid.TryParse(row.GetAttributeValue<string>("sprk_corerecordid")?.Trim(), out var coreId) ? coreId : null,
                    Owner: row.GetAttributeValue<EntityReference>("ownerid"),
                    PolicyVersionId: row.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id,
                    LastEvaluated: row.GetAttributeValue<DateTime?>("sprk_lastevaluated"),
                    DecisionRecordId: row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id,
                    LineageRow: row));
            }

            if (!result.MoreRecords)
            {
                if (unparseable > 0)
                {
                    // A key whose subject and episode cannot be read could be re-raised as a duplicate episode 1, or never
                    // closed. Nothing is written for this policy until it is fixed.
                    throw new InvalidOperationException(
                        $"Policy {policyCode} has {unparseable} Signal(s) whose dedupe key is not episode-scoped; its Signals were not changed.");
                }

                return signals;
            }

            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    // ── Run counts ────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class RunCounts
    {
        public int PoliciesEvaluated, DecideLanePolicies, DoLanePolicies, InvalidPolicies, PolicyFailures, ScopedNotEvaluated;
        public int Hits, Written, Reraised, Updated, Closed, Reassigned, Refused, ChangedSinceRead;

        /// <summary>K1: the part of <see cref="Refused"/> that is a Dataverse privilege refusal (deterministic, so not retried by a trigger).</summary>
        public int AccessDenied;
        public int SupersededClosed, RetiredClosed;
        /// <summary>Task 145 (review F1): Signals whose stored evidence lost detail outside their secure family this pass.</summary>
        public int EvidenceRedacted;

        /// <summary>Task 153 (D-145b): condition-cleared Decision Records written this pass, Signals linked to one, and closures whose record
        /// could not be written or linked (each fails the run; the Signal stays closed and a later pass retries).</summary>
        public int ClosureRecorded, ClosureRecordsLinked, ClosureRecordFailures;

        /// <summary>Task 145 (uac-r2 F1 on #1874): the policies whose stored evidence this run already checked (or tried to), so the
        /// sweeps after the policy loop reach every other policy exactly once.</summary>
        public HashSet<string> EvidenceRedactionAttempted { get; } = new(StringComparer.Ordinal);

        /// <summary>Task 145 (uac-r2 re-check F1): the policies that already failed this run (a policy failure or an invalid rule),
        /// so a sweep's own read failure for one of them is logged but not counted a second time.</summary>
        public HashSet<string> PoliciesFailedThisRun { get; } = new(StringComparer.Ordinal);
        public int ClearedMarked, Unchanged, Suppressed, QuietWindow, DateUnchanged, AwaitingEvidence, NoDatedEvidence, UnparseableKeys, Anomalies, CreatesDeferred, NotApplicable, MissingInputs;

        /// <summary>#1661 round 2 F3: policies planned NOTHING this pass because their secure-family partitions could not be read (each is
        /// also a policy failure).</summary>
        public List<(Guid PolicyId, string PolicyCode, int Failures)> PartitionSkips { get; } = new();

        /// <summary>A scoped policy that is silently skipped, a Signal left untouched as an anomaly, or a policy skipped because its
        /// secure-family partitions are unknown (#1661, counted in <see cref="PolicyFailures"/>), is not a healthy run.</summary>
        public bool IsSuccess =>
            Refused == 0 && InvalidPolicies == 0 && PolicyFailures == 0 && UnparseableKeys == 0 && Anomalies == 0 && ScopedNotEvaluated == 0
            && PartitionSkips.Count == 0 && ClosureRecordFailures == 0;

        /// <summary>Task 033: a system closure by the closure service. A failed close is a refusal (the run fails); a Signal a
        /// person resolved after the read is the same benign skip as the job's own closes.</summary>
        public void AddClosure(SignalClosureOutcome outcome, bool retired)
        {
            Closed += outcome.Closed;
            if (retired) RetiredClosed += outcome.Closed; else SupersededClosed += outcome.Closed;
            ChangedSinceRead += outcome.ChangedSinceRead;
            Refused += outcome.Failed;
            AccessDenied += outcome.AccessDenied;
            PolicyFailures += outcome.PolicyFailures;
        }

        public void Add(EpisodePlan plan)
        {
            Unchanged += plan.Unchanged;
            Suppressed += plan.Suppressed;
            QuietWindow += plan.QuietWindow;
            DateUnchanged += plan.DateUnchanged;
            AwaitingEvidence += plan.AwaitingEvidence;
            NoDatedEvidence += plan.NoDatedEvidence;
            Refused += plan.RefusedHits.Count;
            Anomalies += plan.Anomalies;
        }

        public string FailureSummary() =>
            $"refused={Refused}, invalidPolicies={InvalidPolicies}, policyFailures={PolicyFailures}, unparseableKeys={UnparseableKeys}, " +
            $"anomalies={Anomalies}, scopedNotEvaluated={ScopedNotEvaluated}, partitionSkippedPolicies={PartitionSkips.Count}, " +
            $"closureRecordFailures={ClosureRecordFailures}";

        public IReadOnlyDictionary<string, long> ByAction() => new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [EvaluatorSignalAction.Written] = Written,
            [EvaluatorSignalAction.Updated] = Updated,
            [EvaluatorSignalAction.Closed] = Closed,
            [EvaluatorSignalAction.Reraised] = Reraised,
            [EvaluatorSignalAction.Refused] = Refused,
            [EvaluatorSignalAction.Reassigned] = Reassigned,
        };

        public object ToResult(int attempt) => new
        {
            outcome = IsSuccess ? "succeeded" : "failed",
            policiesEvaluated = PoliciesEvaluated,
            decideLanePolicies = DecideLanePolicies,
            doLanePolicies = DoLanePolicies,
            invalidPolicies = InvalidPolicies,
            policyFailures = PolicyFailures,
            scopedNotEvaluated = ScopedNotEvaluated,
            hits = Hits,
            written = Written,
            reraised = Reraised,
            updated = Updated,
            closed = Closed,
            supersededClosed = SupersededClosed,
            policyRetiredClosed = RetiredClosed,
            reassigned = Reassigned,
            refused = Refused,
            accessDenied = AccessDenied,
            changedSinceRead = ChangedSinceRead,
            unchanged = Unchanged,
            suppressed = Suppressed,
            quietWindow = QuietWindow,
            dateUnchanged = DateUnchanged,
            awaitingEvidence = AwaitingEvidence,
            noDatedEvidence = NoDatedEvidence,
            clearedMarked = ClearedMarked,
            evidenceRedacted = EvidenceRedacted,
            closureRecorded = ClosureRecorded,
            closureRecordsLinked = ClosureRecordsLinked,
            closureRecordFailures = ClosureRecordFailures,
            missingInputs = MissingInputs,
            unparseableKeys = UnparseableKeys,
            anomalies = Anomalies,
            createsDeferred = CreatesDeferred,
            partitionSkippedPolicies = PartitionSkips
                .Select(s => new { policyId = s.PolicyId, policyCode = s.PolicyCode, failures = s.Failures })
                .ToArray(),
            attempt,
        };
    }
}

/// <summary>What <see cref="SignalReevaluationJob.ReevaluatePolicyAsync"/> did for one policy (task 033 / 101 contract).</summary>
/// <param name="Success">False when anything was refused, the policy was invalid or unreadable, or no version is in force.
/// Callers MUST check it: the publish is not "done" while Signals are unreconciled.</param>
/// <param name="SupersededClosed">Open Signals of older versions closed Superseded.</param>
/// <param name="Raised">New Signals raised (first episodes and re-raised episodes, including those citing the new version).</param>
public sealed record PolicyReevaluationResult(bool Success, int SupersededClosed, int Raised, string FailureSummary);

/// <summary>One existing <c>sprk_signal</c> of a policy, as the planner needs it (task 031). <c>Partition</c> (#1661, D-123): the Signal's
/// secure-family partition, set by <see cref="SignalSuppressionService.ReconcileAsync"/> for every Signal of a record that carries a live
/// stamp (null: not placed, read as ordinary by the pure planner), so the planner reads a stamp only within the partition it was written
/// for. <c>LineageRow</c>: the row as read, with its lineage lookups (the partition's input).</summary>
internal sealed record ExistingSignal(
    Guid Id,
    string? RowVersion,
    string SubjectEntity,
    Guid SubjectId,
    int Episode,
    int? Status,
    int? Resolution,
    DateTime? ResolvedOn,
    DateTime? SuppressedUntil,
    DateTime? DueDate,
    Guid? CoreTypeRefId,
    Guid? CoreRecordId,
    EntityReference? Owner,
    Guid? PolicyVersionId = null,
    DateTime? LastEvaluated = null,
    Guid? DecisionRecordId = null,
    SuppressionPartition? Partition = null,
    Entity? LineageRow = null)
{
    /// <summary>D-120: how much later than <c>sprk_resolvedon</c> <c>sprk_lastevaluated</c> must be to read as "the job observed the
    /// condition cleared after the resolution" (a person's commit may stamp both within moments of each other).</summary>
    internal static readonly TimeSpan ClearedMarkerTolerance = TimeSpan.FromMinutes(1);

    /// <summary>D-120: a resolved Signal carries the cleared-observed marker when it was last evaluated after it was resolved.</summary>
    public bool ClearedObserved => ResolvedOn is { } resolved && LastEvaluated is { } evaluated && evaluated > resolved + ClearedMarkerTolerance;

    public bool IsOpen => Status is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged;

    public bool IsResolved => Status == SignalWriter.SignalStatusResolved;
}

/// <summary>A Signal to create: episode 1 for a first detection, n+1 for a re-raise.</summary>
/// <param name="ReplacesSignalId">The open Signal this episode replaces (a re-file to another core record): the create runs only
/// when that Signal's close returned <see cref="SignalCloseOutcome.Closed"/>.</param>
internal sealed record PlannedCreate(SignalHit Hit, int Episode, Guid? ReplacesSignalId = null);

/// <summary>What one policy's pass will write, and what it counted without writing (task 031).</summary>
internal sealed class EpisodePlan
{
    public List<PlannedCreate> Creates { get; } = new();
    public List<(ExistingSignal Signal, SignalHit Hit)> Reconciles { get; } = new();
    public List<(ExistingSignal Signal, EntityReference Owner)> Reassigns { get; } = new();
    public List<ExistingSignal> Closes { get; } = new();
    public List<SignalHit> RefusedHits { get; } = new();
    public int Unchanged { get; set; }
    public int Suppressed { get; set; }
    public int QuietWindow { get; set; }
    public int DateUnchanged { get; set; }

    /// <summary>D-119: Decide-lane Acted Signals whose predicate still holds but whose evidence is all older than the resolution.</summary>
    public int AwaitingEvidence { get; set; }

    /// <summary>D-119: Decide-lane Acted Signals held un-raised because the hit carries no dated evidence (or the resolution time is unknown).</summary>
    public int NoDatedEvidence { get; set; }
    public int Anomalies { get; set; }

    /// <summary>D-120: Acted-resolved Threshold Signals whose subject is NOT a hit and that carry no cleared-observed marker yet;
    /// the pass stamps each once.</summary>
    public List<ExistingSignal> ClearedMarks { get; } = new();
}

/// <summary>
/// The pure episode rules (task 031): given one policy's hits and its existing Signals, decides every write. No I/O.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Grain (FR-17 as amended, D-34/D-36/D-39).</b> Suppression is read per (policy, core-record type, core-record id),
/// from the generic <c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c> pair, for EVERY core type alike; an item with no core
/// record is its own grain (policy, item). A Signal of the pair with <c>sprk_suppresseduntil</c> in the future suppresses
/// the pair: no first Signal and no re-raise. <b>#1661 (D-123, audit F6):</b> only within its secure-family partition: a stamp
/// mutes a hit only when the hit's NEW episode is in the stamped row's partition, decided from the current truth
/// (<see cref="SignalSuppressionService.PartitionsOfHitsAsync"/>), never from a previous episode. A hit whose partition is undetermined
/// is live: the undetermined bucket never mutes and is never muted, so its outcome does not depend on another family's stamp (round 2 F2).
/// When the partitions cannot be read at all the job plans nothing for the policy.</item>
/// <item><b>No Signal yet</b>: episode 1, unless suppressed.</item>
/// <item><b>Latest Open or Acknowledged</b>: the same episode. Its core record re-derived and different (re-filed), it is
/// closed as ConditionCleared and episode n+1 opens under the new core (unless suppressed); a no-core Signal whose item has
/// a new owner is reassigned (D-35); a changed date is reconciled. Otherwise NOTHING is written (D-61).</item>
/// <item><b>Latest Resolved</b>: never closed again (FR-16). Re-raised as n+1 when not suppressed and: Superseded or
/// PolicyRetired (FR-16, D-32) always; Do lane only when the subject's date differs from the resolved Signal's
/// <c>sprk_duedate</c>; Decide lane unless it was Dismissed less than the quiet window ago (FR-17a). <b>D-119:</b> a Decide-lane
/// <b>Acted</b> Signal re-raises only when at least one witness line is dated after its <c>sprk_resolvedon</c>; a predicate that
/// merely still holds does not re-raise it, and a hit with no dated evidence stays un-raised.</item>
/// <item><b>Unjudged (D-120, task 025b)</b>: a subject the rule could not judge for want of an input is neither a hit nor
/// observed false: its open Signal is NOT closed and its Acted Signal is NOT stamped cleared.</item>
/// <item><b>Not a hit</b>: every other open Signal is closed as ConditionCleared. A hit whose core record the writer refused is
/// still a hit: its Signal is neither written nor closed.</item>
/// </list>
/// </remarks>
internal static class SignalEpisodePlanner
{
    private readonly record struct Grain(Guid? CoreTypeRefId, Guid CoreId, string? SubjectEntity, Guid SubjectId);

    /// <param name="hitPartitions">#1661 R1: each hit's partition from the truth (<see cref="SignalSuppressionService.PartitionsOfHitsAsync"/>);
    /// a hit that is absent or undetermined is live (round 2 F2). Null only for callers with no partition truth at all (the pure rule
    /// tests): every hit and every unplaced Signal is then ordinary.</param>
    public static EpisodePlan Plan(
        int lane, int quietWindowDays, DateTimeOffset nowUtc, IReadOnlyList<SignalHit> hits, IReadOnlyList<ExistingSignal> existing,
        IReadOnlyDictionary<(string SubjectEntity, Guid SubjectId), SuppressionPartition>? hitPartitions = null,
        bool isThreshold = false, IReadOnlySet<(string, Guid)>? unjudged = null)
    {
        var plan = new EpisodePlan();
        var now = nowUtc.UtcDateTime;
        var bySubject = existing
            .GroupBy(s => (s.SubjectEntity, s.SubjectId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Episode).ToList());
        var hitSubjects = new HashSet<(string, Guid)>();

        // #1661 (D-123, audit F6): a stamp mutes only its own secure-family partition, and a hit's partition is the one its NEW episode
        // will have: the writer decides the new Signal's owner from the current truth, so the previous episode's partition is not the
        // hit's (a subject that left a secure family must not be muted by that family's stamp, nor the reverse). The truth comes in
        // hitPartitions. Round 2 F2: an undetermined (or unplaced) hit is live, exactly as it would be with no stamp on the record: the
        // undetermined bucket never mutes and is never muted, so whether it is raised says nothing about any family's dismissals.
        bool Suppressed(SignalHit hit, Grain grain)
        {
            var stamped = StampedPartitions(existing, grain, now);
            if (stamped.Count == 0) return false;
            var partition = hitPartitions is null
                ? SuppressionPartition.Ordinary
                : hitPartitions.GetValueOrDefault((hit.SubjectEntity, hit.SubjectId), SuppressionPartition.Undetermined);
            return stamped.Contains(partition); // StampedPartitions never holds the undetermined bucket
        }

        foreach (var hit in hits)
        {
            hitSubjects.Add((hit.SubjectEntity, hit.SubjectId));
            if (hit.Core is not { } core)
            {
                plan.RefusedHits.Add(hit);
                continue;
            }

            var grain = GrainOf(hit, core);
            var suppressed = Suppressed(hit, grain);
            if (!bySubject.TryGetValue((hit.SubjectEntity, hit.SubjectId), out var episodes))
            {
                if (suppressed) plan.Suppressed++;
                else plan.Creates.Add(new PlannedCreate(hit, 1));
                continue;
            }

            var latest = episodes[0];
            plan.Anomalies += episodes.Skip(1).Count(s => s.IsOpen);

            if (latest.IsOpen)
            {
                if (CoreChanged(latest, core))
                {
                    plan.Closes.Add(latest);
                    if (suppressed) plan.Suppressed++;
                    else plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1, latest.Id));
                    continue;
                }

                var changed = false;
                if (core.NoCoreOwner is { } owner
                    && (latest.Owner is null || latest.Owner.Id != owner.Id
                        || !string.Equals(latest.Owner.LogicalName, owner.LogicalName, StringComparison.Ordinal)))
                {
                    plan.Reassigns.Add((latest, owner));
                    changed = true;
                }

                if (latest.DueDate?.Date != hit.DueDate?.Date)
                {
                    plan.Reconciles.Add((latest, hit));
                    changed = true;
                }

                if (!changed) plan.Unchanged++;
                continue;
            }

            if (!latest.IsResolved)
            {
                plan.Anomalies++;
                continue;
            }

            if (suppressed)
            {
                plan.Suppressed++;
            }
            else if (latest.Resolution is SignalWriter.ResolutionSuperseded or SignalWriter.ResolutionPolicyRetired)
            {
                plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1));
            }
            else if (CoreChanged(latest, core) && !InDecideQuietWindow(lane, latest, quietWindowDays, now))
            {
                // The latest episode belongs to a different core record: a re-file whose old episode was closed here (so a
                // failed n+1 create left the subject with no open Signal), or a Signal resolved by the old core's members.
                // The new core's members have never seen it, so the Do-lane date rule does not apply (D-13, D-34).
                plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1));
            }
            else if (lane == SignalWriter.LaneDo)
            {
                if (latest.DueDate?.Date != hit.DueDate?.Date) plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1));
                else plan.DateUnchanged++;
            }
            else if (InDecideQuietWindow(lane, latest, quietWindowDays, now))
            {
                plan.QuietWindow++;
            }
            else if (lane == SignalWriter.LaneDecide && latest.Resolution == SignalWriter.ResolutionActed)
            {
                // D-119: an Acted decision is not undone by the predicate still holding. Only evidence newer than it re-raises.
                if (isThreshold)
                {
                    // D-120: a Threshold hit has no dated witness. It re-raises after a RE-CROSSING: the job observed the
                    // condition false after the resolution (the marker), and it holds again now. Never cleared: silent.
                    if (latest.ClearedObserved) plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1));
                    else if (latest.ResolvedOn is null) plan.NoDatedEvidence++;
                    else plan.AwaitingEvidence++;
                    continue;
                }

                switch (EvidenceAfter(hit, latest.ResolvedOn))
                {
                    case EvidenceVerdict.Newer: plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1)); break;
                    case EvidenceVerdict.Older: plan.AwaitingEvidence++; break;
                    default: plan.NoDatedEvidence++; break;
                }
            }
            else
            {
                plan.Creates.Add(new PlannedCreate(hit, latest.Episode + 1));
            }
        }

        foreach (var signal in existing)
        {
            if (signal.IsOpen && !hitSubjects.Contains((signal.SubjectEntity, signal.SubjectId))
                && unjudged?.Contains((signal.SubjectEntity, signal.SubjectId)) != true)
            {
                plan.Closes.Add(signal);
            }
        }

        if (isThreshold && lane == SignalWriter.LaneDecide)
        {
            // D-120: the latest episode of a subject that is not a hit, resolved Acted, is stamped ONCE (D-61: never an unchanged row
            // nightly). A subject whose latest episode is open or otherwise resolved is not this case.
            foreach (var (subject, episodes) in bySubject)
            {
                var latest = episodes[0];
                if (!hitSubjects.Contains(subject) && unjudged?.Contains(subject) != true && latest.IsResolved && latest.Resolution == SignalWriter.ResolutionActed
                    && !latest.ClearedObserved)
                {
                    // Stamp only when the stamp WILL register as a marker (later than resolvedon + tolerance): an unknown resolution
                    // time can never be compared (counted with the other "no dated evidence" cases), and a pass within the tolerance
                    // would write a stamp that is not a marker and be stamped again next pass (D-61).
                    if (latest.ResolvedOn is not { } resolvedOn) plan.NoDatedEvidence++;
                    else if (now > resolvedOn + ExistingSignal.ClearedMarkerTolerance) plan.ClearedMarks.Add(latest);
                }
            }
        }

        return plan;
    }

    private enum EvidenceVerdict { Newer, Older, Undated }

    /// <summary>The partitions that carry a live stamp on <paramref name="grain"/> (an unplaced Signal reads as ordinary; the
    /// undetermined bucket never mutes).</summary>
    private static HashSet<SuppressionPartition> StampedPartitions(IReadOnlyList<ExistingSignal> existing, Grain grain, DateTime now) =>
        existing
            .Where(s => GrainOf(s) == grain && s.SuppressedUntil is { } until && until > now)
            .Select(s => s.Partition ?? SuppressionPartition.Ordinary)
            .Where(p => !p.IsUndetermined)
            .ToHashSet();

    /// <summary>#1661 R1: the hits whose partition can change the outcome, those on a record (or item) that carries a live stamp. Only
    /// these are placed (<see cref="SignalSuppressionService.PartitionsOfHitsAsync"/>); every other hit is not suppressed whatever its
    /// partition.</summary>
    public static IReadOnlyList<SignalHit> HitsOnStampedGrains(IReadOnlyList<SignalHit> hits, IReadOnlyList<ExistingSignal> existing, DateTimeOffset nowUtc)
    {
        var now = nowUtc.UtcDateTime;
        return hits.Where(h => h.Core is { } core && StampedPartitions(existing, GrainOf(h, core), now).Count > 0).ToList();
    }

    /// <summary>D-119 (generic, no policy knowledge): the hit's witness lines (the rows the Existence clauses matched, each carrying
    /// its clause date as <c>AsOf</c>) against the resolution time. <see cref="EvidenceVerdict.Newer"/> when at least one is dated
    /// strictly after it; <see cref="EvidenceVerdict.Undated"/> when the resolution time is unknown or no witness has a readable
    /// date (the Signal then stays un-raised, never re-raised on a guess). Context lines (no clause) are not evidence of the test.</summary>
    private static EvidenceVerdict EvidenceAfter(SignalHit hit, DateTime? resolvedOn)
    {
        if (resolvedOn is not { } resolved) return EvidenceVerdict.Undated;
        var dated = false;
        foreach (var line in hit.Evidence)
        {
            if (line.Clause is null
                || !DateTimeOffset.TryParse(line.AsOf, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var asOf))
            {
                continue;
            }

            dated = true;
            if (asOf.UtcDateTime > DateTime.SpecifyKind(resolved, DateTimeKind.Utc)) return EvidenceVerdict.Newer;
        }

        return dated ? EvidenceVerdict.Older : EvidenceVerdict.Undated;
    }

    private static bool InDecideQuietWindow(int lane, ExistingSignal latest, int quietWindowDays, DateTime now) =>
        lane == SignalWriter.LaneDecide
        && latest.Resolution == SignalWriter.ResolutionDismissed
        && (latest.ResolvedOn is not { } resolvedOn || resolvedOn.AddDays(quietWindowDays) > now);

    private static Grain GrainOf(SignalHit hit, SignalCoreRecord core) =>
        core.IsNoCore ? new Grain(null, Guid.Empty, hit.SubjectEntity, hit.SubjectId) : new Grain(core.TypeRefId, core.Id, null, Guid.Empty);

    private static Grain GrainOf(ExistingSignal signal) =>
        signal.CoreRecordId is { } coreId
            ? new Grain(signal.CoreTypeRefId, coreId, null, Guid.Empty)
            : new Grain(null, Guid.Empty, signal.SubjectEntity, signal.SubjectId);

    private static bool CoreChanged(ExistingSignal latest, SignalCoreRecord core) =>
        core.IsNoCore
            ? latest.CoreRecordId is not null
            : latest.CoreRecordId != core.Id || (latest.CoreTypeRefId is { } typeRef && typeRef != core.TypeRefId);
}

/// <summary>What one event-triggered pass did (task 032). <see cref="PolicyFailures"/> (a read or write that threw) and
/// <see cref="Refused"/> (a write the writer refused) are the outcomes a retry can change; the rest were already logged by the
/// code that found them. <see cref="AccessDenied"/> is the part of <see cref="Refused"/> that is a Dataverse privilege refusal
/// (0x80040299 / 0x80040220): deterministic, so a redelivery would be refused the same way and does not count toward a retry.</summary>
internal sealed record SubjectEvaluationResult(
    int PoliciesEvaluated, int Hits, int Created, int Updated, int Closed, int Refused, int PolicyFailures, int InvalidPolicies,
    int ScopedNotEvaluated, int AccessDenied = 0)
{
    public bool NeedsRetry => PolicyFailures > 0 || Refused > AccessDenied;
}
