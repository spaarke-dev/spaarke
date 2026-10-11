using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
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
/// longer a hit is closed as <c>ConditionCleared</c> (no Decision Record, FR-16); a Resolved Signal is never closed again. The
/// Superseded and PolicyRetired closures are task 033's; this job re-raises their episodes.</para>
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
    private readonly IGenericEntityService _entities;
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalReevaluationJob> _logger;

    public SignalReevaluationJob(
        SignalEvaluator evaluator,
        SignalWriter writer,
        SignalSuppressionService suppression,
        IGenericEntityService entities,
        IDistributedCache cache,
        TimeProvider timeProvider,
        ILogger<SignalReevaluationJob> logger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _suppression = suppression ?? throw new ArgumentNullException(nameof(suppression));
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
            await EvaluatePolicyAsync(policy, counts, cancellationToken).ConfigureAwait(false);
        }

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

    private async Task EvaluatePolicyAsync(PolicyRow policy, RunCounts counts, CancellationToken ct)
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
                "sprk_policyversion", versionId, new[] { "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate" }, ct).ConfigureAwait(false);
            var spec = new SignalEvaluationPolicy(
                policy.Id, policy.Code, lane, versionId,
                version.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(CultureInfo.InvariantCulture),
                version.GetAttributeValue<string>("sprk_rulebody"),
                version.GetAttributeValue<string>("sprk_messagetemplate"));

            counts.PoliciesEvaluated++;
            if (lane == SignalWriter.LaneDo) counts.DoLanePolicies++;
            else counts.DecideLanePolicies++;

            var evaluation = await _evaluator.EvaluateAsync(spec, subjectId: null, ct).ConfigureAwait(false);
            if (!evaluation.PolicyValid)
            {
                // Logged and metered by the validator gate (50301). Nothing is closed on an unknown truth value.
                counts.InvalidPolicies++;
                return;
            }

            counts.Hits += evaluation.Hits.Count;
            var read = await ReadPolicySignalsAsync(policy.Code, counts, ct).ConfigureAwait(false);
            // Task 034 (FR-17): stamp a grain found at the dismissal threshold with no live stamp, so the planner below sees it.
            var (existing, suppressionFailures) = await _suppression.ReconcileAsync(policy.Id, policy.Code, policy.CountsTowardSuppression, read, ct).ConfigureAwait(false);
            counts.PolicyFailures += suppressionFailures;
            var plan = SignalEpisodePlanner.Plan(lane, evaluation.QuietWindowDays, _timeProvider.GetUtcNow(), evaluation.Hits, existing);
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

            await ApplyAsync(policy, spec, plan, counts, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.PolicyFailures++;
            _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                "Policy {PolicyCode} could not be re-evaluated this run ({ExceptionType}); its Signals were not changed.",
                policy.Code, ex.GetType().Name);
        }
    }

    private void LogRefusedCores(PolicyRow policy, EpisodePlan plan)
    {
        foreach (var hit in plan.RefusedHits)
        {
            // D-38: a refusal, never a skip. Same EventId and metric as every writer refusal.
            _logger.LogError(OntologyWriterEvents.WriteRefused,
                "Signal writer refused for policy {PolicyCode} (reason={Reason}): the core record of {SubjectEntity} {SubjectId} " +
                "cannot be derived. Nothing was written for it.", policy.Code, hit.CoreRefusalReason, hit.SubjectEntity, hit.SubjectId);
            OntologyWriterTelemetry.RecordFailure(hit.CoreRefusalReason ?? OntologyWriterFailureReason.CoreRecordUnresolved);
        }
    }

    private async Task ApplyAsync(PolicyRow policy, SignalEvaluationPolicy spec, EpisodePlan plan, RunCounts counts, CancellationToken ct)
    {
        // Ids of the Signals this pass actually closed. A re-file's new episode is created only when its predecessor closed.
        var closedIds = new HashSet<Guid>();
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
                }
                else
                {
                    counts.ChangedSinceRead++;
                }
            }).ConfigureAwait(false);
        }

        foreach (var (signal, owner) in plan.Reassigns)
        {
            await GuardAsync(policy, "reassign", signal.SubjectId, counts, writerLogged: true, async () =>
            {
                await _writer.ReassignNoCoreOwnerAsync(signal.Id, owner, policy.Code, ct).ConfigureAwait(false);
                counts.Reassigned++;
            }).ConfigureAwait(false);
        }

        foreach (var (signal, hit) in plan.Reconciles)
        {
            await GuardAsync(policy, "reconcile", hit.SubjectId, counts, writerLogged: false, async () =>
            {
                var result = await _writer.WriteAsync(BuildRequest(policy, spec, hit, signal.Episode), ct).ConfigureAwait(false);
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
                var result = await _writer.WriteAsync(BuildRequest(policy, spec, create.Hit, create.Episode), ct).ConfigureAwait(false);
                if (result.IsSkipped) counts.Refused++;
                else if (!result.Created) counts.Updated++;
                else if (create.Episode > 1) counts.Reraised++;
                else counts.Written++;
            }).ConfigureAwait(false);
        }
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
            "written={Written} reraised={Reraised} updated={Updated} closed={Closed} reassigned={Reassigned} refused={Refused} " +
            "unchanged={Unchanged} suppressed={Suppressed} quietWindow={QuietWindow} dateUnchanged={DateUnchanged} awaitingEvidence={AwaitingEvidence} noDatedEvidence={NoDatedEvidence} " +
            "invalidPolicies={InvalidPolicies} policyFailures={PolicyFailures} attempt={Attempt} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            success ? "succeeded" : "failed", now, counts.PoliciesEvaluated, counts.Hits, counts.Written, counts.Reraised,
            counts.Updated, counts.Closed, counts.Reassigned, counts.Refused, counts.Unchanged, counts.Suppressed,
            counts.QuietWindow, counts.DateUnchanged, counts.AwaitingEvidence, counts.NoDatedEvidence, counts.InvalidPolicies, counts.PolicyFailures, context.Attempt,
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

        return rows.Entities.Select(row =>
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
        }).ToList();
    }

    /// <summary>Every Signal of the policy, by the begins-with query D-13 names, keyed exactly by the parsed key. A key that
    /// does not parse is an Error and fails the run (its subject's episode cannot be known).</summary>
    private async Task<IReadOnlyList<ExistingSignal>> ReadPolicySignalsAsync(string policyCode, RunCounts counts, CancellationToken ct)
    {
        var prefix = SignalWriter.DedupeKeyPolicyPrefix(policyCode);
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet("sprk_dedupekey", "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon",
                "sprk_suppresseduntil", "sprk_duedate", "sprk_corerecordtype", "sprk_corerecordid", "ownerid", "versionnumber", "sprk_decisionrecord"),
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
                    DecisionRecordId: row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id));
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
        public int Unchanged, Suppressed, QuietWindow, DateUnchanged, AwaitingEvidence, NoDatedEvidence, UnparseableKeys, Anomalies, CreatesDeferred;

        /// <summary>A scoped policy that is silently skipped, or a Signal left untouched as an anomaly, is not a healthy run.</summary>
        public bool IsSuccess =>
            Refused == 0 && InvalidPolicies == 0 && PolicyFailures == 0 && UnparseableKeys == 0 && Anomalies == 0 && ScopedNotEvaluated == 0;

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
            $"anomalies={Anomalies}, scopedNotEvaluated={ScopedNotEvaluated}";

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
            reassigned = Reassigned,
            refused = Refused,
            changedSinceRead = ChangedSinceRead,
            unchanged = Unchanged,
            suppressed = Suppressed,
            quietWindow = QuietWindow,
            dateUnchanged = DateUnchanged,
            awaitingEvidence = AwaitingEvidence,
            noDatedEvidence = NoDatedEvidence,
            unparseableKeys = UnparseableKeys,
            anomalies = Anomalies,
            createsDeferred = CreatesDeferred,
            attempt,
        };
    }
}

/// <summary>One existing <c>sprk_signal</c> of a policy, as the planner needs it (task 031).</summary>
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
    Guid? DecisionRecordId = null)
{
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
}

/// <summary>
/// The pure episode rules (task 031): given one policy's hits and its existing Signals, decides every write. No I/O.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Grain (FR-17 as amended, D-34/D-36/D-39).</b> Suppression is read per (policy, core-record type, core-record id),
/// from the generic <c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c> pair, for EVERY core type alike; an item with no core
/// record is its own grain (policy, item). A Signal of the pair with <c>sprk_suppresseduntil</c> in the future suppresses
/// the pair: no first Signal and no re-raise.</item>
/// <item><b>No Signal yet</b>: episode 1, unless suppressed.</item>
/// <item><b>Latest Open or Acknowledged</b>: the same episode. Its core record re-derived and different (re-filed), it is
/// closed as ConditionCleared and episode n+1 opens under the new core (unless suppressed); a no-core Signal whose item has
/// a new owner is reassigned (D-35); a changed date is reconciled. Otherwise NOTHING is written (D-61).</item>
/// <item><b>Latest Resolved</b>: never closed again (FR-16). Re-raised as n+1 when not suppressed and: Superseded or
/// PolicyRetired (FR-16, D-32) always; Do lane only when the subject's date differs from the resolved Signal's
/// <c>sprk_duedate</c>; Decide lane unless it was Dismissed less than the quiet window ago (FR-17a). <b>D-119:</b> a Decide-lane
/// <b>Acted</b> Signal re-raises only when at least one witness line is dated after its <c>sprk_resolvedon</c>; a predicate that
/// merely still holds does not re-raise it, and a hit with no dated evidence stays un-raised.</item>
/// <item><b>Not a hit</b>: every open Signal is closed as ConditionCleared. A hit whose core record the writer refused is
/// still a hit: its Signal is neither written nor closed.</item>
/// </list>
/// </remarks>
internal static class SignalEpisodePlanner
{
    private readonly record struct Grain(Guid? CoreTypeRefId, Guid CoreId, string? SubjectEntity, Guid SubjectId);

    public static EpisodePlan Plan(
        int lane, int quietWindowDays, DateTimeOffset nowUtc, IReadOnlyList<SignalHit> hits, IReadOnlyList<ExistingSignal> existing)
    {
        var plan = new EpisodePlan();
        var now = nowUtc.UtcDateTime;
        var bySubject = existing
            .GroupBy(s => (s.SubjectEntity, s.SubjectId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Episode).ToList());
        var hitSubjects = new HashSet<(string, Guid)>();

        bool Suppressed(Grain grain) => existing.Any(s => GrainOf(s) == grain && s.SuppressedUntil is { } until && until > now);

        foreach (var hit in hits)
        {
            hitSubjects.Add((hit.SubjectEntity, hit.SubjectId));
            if (hit.Core is not { } core)
            {
                plan.RefusedHits.Add(hit);
                continue;
            }

            var grain = GrainOf(hit, core);
            if (!bySubject.TryGetValue((hit.SubjectEntity, hit.SubjectId), out var episodes))
            {
                if (Suppressed(grain)) plan.Suppressed++;
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
                    if (Suppressed(grain)) plan.Suppressed++;
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

            if (Suppressed(grain))
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
            if (signal.IsOpen && !hitSubjects.Contains((signal.SubjectEntity, signal.SubjectId)))
            {
                plan.Closes.Add(signal);
            }
        }

        return plan;
    }

    private enum EvidenceVerdict { Newer, Older, Undated }

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
