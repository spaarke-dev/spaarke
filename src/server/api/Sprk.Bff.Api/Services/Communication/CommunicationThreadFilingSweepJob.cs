using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// spaarkeai-word-add-in-r1 task 129 (backfill, and a standing safety net): makes EXISTING data satisfy what
/// <see cref="IThreadResolver.ReconcileThreadForFilingAsync"/> now guarantees for every NEW filing — a filed email's thread
/// is anchored to a record the email is filed to, so the email shows on that record's Messages panel — and gives the
/// threads that panel lists the owner S6 gives them, so a child-business-unit user can read them.
/// </summary>
/// <remarks>
/// <para><b>Component justification</b> (CLAUDE.md §11). <i>Existing</i> — <c>CoreAncestorStampReconciliationJob</c> and
/// <c>DocumentContainerMigrationJob</c> are the same sweep shape for other invariants. <i>Extension</i> — neither carries
/// the thread-filing invariant, and its rules stay in <see cref="ThreadResolver"/>: this job only SELECTS candidates and
/// calls the resolver. <i>Cost of doing nothing</i> — emails filed before the reconcile existed stay invisible on their
/// records, and child-business-unit users keep seeing empty Messages panels for threads owned by the BFF app user in the
/// root business unit (live, dev: 9 unanchored threads hold emails filed to records, and 27 of 35 anchored threads are
/// owned by the app user).</para>
///
/// <para><b>Each run, two parts</b> (each reads a bounded slice, in id order, after a per-instance cursor):</para>
/// <list type="number">
/// <item><b>Filing follow.</b> Active <c>sprk_communication</c> rows of type Email that are FILED (any typed regarding
/// lookup set) are read with their threads. A row whose thread is missing, or not anchored to a record the email
/// is filed to (<see cref="ThreadResolver.IsFiledTo"/>, the reconcile's own test) is a candidate; a row whose thread is
/// INACTIVE is not — a deactivated thread is a user's delete (the thread delete leaves its emails filed) and the backfill
/// must not undo it, so it is only counted (<c>skippedInactiveThread</c>); each candidate is handed to
/// <see cref="IThreadResolver.ReconcileThreadForFilingAsync(Guid, FilingReconcileHost, CancellationToken)"/>, which owns every rule (whole-thread move, secure guards,
/// default thread) and is a no-op when nothing is to be done — as a standalone reconcile (<see cref="FilingReconcileHost.Standalone"/>,
/// #2110): a threadless email that joins the thread its conversation key finds, anchored to a record it is not filed to, has
/// the step after a re-file run once for that join. A private thread is skipped (the reconcile leaves it).
/// Candidates are selected in the job, not by one query: the test compares columns of two tables (the email's lookups
/// against the thread's), which a filter cannot express, so the slice is the coarser "filed emails" and the thread rows are
/// read with it.</item>
/// <item><b>Anchored-thread owner.</b> Active, Open <c>sprk_communicationthread</c> rows with a typed regarding lookup are
/// handed to <see cref="IThreadResolver.ReconcileAnchoredThreadOwnerAsync"/>, which resolves the owner S6 gives (the same
/// step the reconcile uses), leaves a thread whose current owner or target is the Secure team (or cannot be told) or that
/// carries more than one typed regarding lookup (<c>skippedAmbiguousAnchor</c>), and otherwise assigns the owner — nothing
/// else on the thread changes. Each assigned thread is listed in the report with the owner it replaced
/// (<c>previousOwnerType</c> / <c>previousOwnerId</c>), so a wrong re-own can be reversed from the run history.</item>
/// </list>
///
/// <para><b>Dry run by default.</b> The job writes only when <see cref="ApplyKey"/> is <c>true</c> (an App Service setting,
/// <c>CommunicationThreadFilingSweep__Apply</c>; absent or unparseable = dry run). A dry run COUNTS and logs — per category:
/// <c>noThread</c>, <c>unanchored</c>, <c>anchoredElsewhere</c>, <c>skippedInactiveThread</c>, <c>ownerNotTeam</c>,
/// <c>skippedSecure</c>, <c>skippedAmbiguousAnchor</c> — and calls nothing that writes (the owner part asks the resolver with <c>dryRun</c>, which
/// computes the same answer and writes nothing). The summary is the run's <see cref="JobRunResult.ResultJson"/>, read at
/// <c>GET /api/admin/jobs/{jobId}/status</c> and <c>/history</c>. <c>skippedSecure</c> is the owner part's count (the
/// filing part's secure guards are inside the reconcile and are not visible to a dry run).</para>
///
/// <para><b>Use.</b> Registered DISABLED (it never fires on its own); the owner runs it on demand with
/// <c>POST /api/admin/jobs/communication-thread-filing-sweep/trigger</c> (SystemAdmin) and reads the report, then sets
/// <c>CommunicationThreadFilingSweep__Apply=true</c> and triggers again. <c>POST /api/admin/jobs/{jobId}/enable</c> turns on
/// the daily schedule as the standing safety net. No endpoint was added.</para>
///
/// <para><b>Batches.</b> A run examines at most <see cref="MaxEmailsPerRunKey"/> emails and
/// <see cref="MaxThreadsPerRunKey"/> threads (defaults <see cref="DefaultMaxEmailsPerRun"/> /
/// <see cref="DefaultMaxThreadsPerRun"/>); the next run continues after the last row (<c>startAfter</c> / <c>endAt</c>), and
/// a run that reaches the end reports <c>passComplete</c> and starts the next pass at the top. The cursor is per instance
/// (as <c>DocumentContainerMigrationJob</c>'s): a restart or another instance starts a pass over, which only repeats
/// no-ops.</para>
///
/// <para><b>ADR-036 A1.</b> Rule 1: the scheduler's lease — one run at a time. Rule 3: every step is keyed on observed
/// state (a reconciled email's thread is anchored to its record next time, an owned thread is already correct), so a re-run
/// completes and repeats nothing. Rule 4: an enumeration fault is recorded as a FAILED run (the slice was not examined),
/// never "nothing to do"; a per-row fault is counted and the run continues; a later tick revisits both, so the job does not
/// throw. Rule 5: one heartbeat per attempt. Rule 6: <c>AddScheduledJob</c> in <c>CommunicationModule</c>.</para>
///
/// <para><b>Known limits.</b> An email filed to a party (account / contact / organization) is reconciled like any other
/// filing — the reconcile anchors its thread to the party and keeps the thread's owner (a party has no team) — so a
/// candidate that stays <c>unresolved</c> is one the reconcile genuinely could not fix (a refused owner, a fault), and it is
/// found again next pass. The reconcile never throws, so an Apply run re-reads each candidate after it and re-applies <see cref="Classify"/>: <c>reconciled</c> counts
/// only emails that are no longer candidates, <c>unresolved</c> those that still are (the next pass finds them again), and
/// the status is <c>ok</c> only when nothing is unresolved or failed.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — BFF domain code
/// (<see cref="ThreadResolver"/>) over BFF-owned tables, BFF identity, low volume, operator triggered. No new package,
/// store, host or endpoint.</para>
/// </remarks>
public sealed class CommunicationThreadFilingSweepJob : IScheduledJob
{
    /// <summary>Stable job id — the admin trigger, status and history routes key off it.</summary>
    public const string JobIdConstant = "communication-thread-filing-sweep";

    /// <summary>Daily at 03:30 UTC when an admin enables the job; registered disabled, so it never fires until then.</summary>
    internal const string DefaultCronSchedule = "30 3 * * *";

    /// <summary>The write switch (<c>CommunicationThreadFilingSweep__Apply</c>): <c>true</c> = write; absent or anything else = dry run.</summary>
    public const string ApplyKey = "CommunicationThreadFilingSweep:Apply";

    /// <summary>Emails examined per run (<c>CommunicationThreadFilingSweep__MaxEmailsPerRun</c>).</summary>
    public const string MaxEmailsPerRunKey = "CommunicationThreadFilingSweep:MaxEmailsPerRun";

    /// <summary>Threads examined per run (<c>CommunicationThreadFilingSweep__MaxThreadsPerRun</c>).</summary>
    public const string MaxThreadsPerRunKey = "CommunicationThreadFilingSweep:MaxThreadsPerRun";

    internal const int DefaultMaxEmailsPerRun = 500;
    internal const int DefaultMaxThreadsPerRun = 500;

    /// <summary>Rows per query. Settable only so a test can reach the paging.</summary>
    internal int ScanPageSize { get; init; } = 100;

    /// <summary>The report lists at most this many rows (the admin history surface stays small); all are counted.</summary>
    internal const int MaxListedPerRun = 200;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string LogPrefix = "[THREAD-FILING-SWEEP]";

    private const string EmailTable = "sprk_communication";
    private const string EmailPk = "sprk_communicationid";
    private const string ThreadTable = "sprk_communicationthread";
    private const string ThreadPk = "sprk_communicationthreadid";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string TypeField = "sprk_communicationtype";
    private const string PrivacyField = "sprk_privacystate";
    private const string StateField = "statecode";
    private const string OwnerField = "ownerid";
    private const string OwningTeamField = "owningteam";
    private const int StateActive = 0;
    private const int PrivacyOpen = 100000000;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommunicationThreadFilingSweepJob> _logger;

    /// <summary>The last email of the previous run of this pass; null = the next run starts the pass.</summary>
    private Guid? _emailCursor;

    /// <summary>The last thread of the previous run of this pass; null = the next run starts the pass.</summary>
    private Guid? _threadCursor;

    public CommunicationThreadFilingSweepJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<CommunicationThreadFilingSweepJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Communication Thread Filing Sweep";

    /// <inheritdoc />
    public string Description =>
        "Makes existing filed emails show on their records: finds filed emails whose thread is missing or not "
        + "anchored to a record the email is filed to (leaving deactivated threads alone) and asks the thread resolver to reconcile each, and gives threads "
        + "anchored to a record the owner S6 gives them (the record's team), leaving secure threads alone. Dry run unless "
        + ApplyKey + " is true. Disabled until enabled; run on demand with POST /api/admin/jobs/" + JobIdConstant
        + "/trigger.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var apply = bool.TryParse(_configuration[ApplyKey], out var a) && a;
        var maxEmails = PositiveInt(_configuration[MaxEmailsPerRunKey], DefaultMaxEmailsPerRun);
        var maxThreads = PositiveInt(_configuration[MaxThreadsPerRunKey], DefaultMaxThreadsPerRun);
        var filing = new FilingReport();
        var owners = new OwnerReport();
        var problems = new List<string>();
        var status = StatusOk;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var resolver = scope.ServiceProvider.GetRequiredService<IThreadResolver>();

            await SweepFilingAsync(entityService, resolver, apply, maxEmails, filing, problems, context, cancellationToken)
                .ConfigureAwait(false);
            await SweepOwnersAsync(entityService, resolver, apply, maxThreads, owners, problems, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before the run finished.");
        }
        // A scheduled job's last line of defence: record the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "{Prefix} Run failed. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
        }

        if (status == StatusOk)
        {
            // ADR-003 for a writer: a slice that could not be read is a FAILED run, never "nothing to do".
            if (filing.EnumerationFailed || owners.EnumerationFailed)
                status = StatusError;
            else if (filing.Failed > 0 || filing.Unresolved > 0 || owners.Failed > 0)
                status = StatusPartial; // unresolved: the reconcile returned but the email is still a candidate (it never throws)
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var mode = apply ? "apply" : "dry-run";
        var level = status != StatusOk ? LogLevel.Warning
            : !apply && (filing.Candidates > 0 || owners.Assigned > 0) ? LogLevel.Warning
            : LogLevel.Information;

        // THE HEARTBEAT (ADR-036 A1 rule 5): one line per attempt, with nothing to do as well.
        _logger.Log(
            level,
            "{Prefix} heartbeat status={Status} mode={Mode} emailsExamined={EmailsExamined} noThread={NoThread} "
            + "unanchored={Unanchored} anchoredElsewhere={AnchoredElsewhere} skippedInactiveThread={SkippedInactiveThread} "
            + "privateThread={PrivateThread} threadUnreadable={ThreadUnreadable} candidates={Candidates} reconciled={Reconciled} "
            + "unresolved={Unresolved} filingFailed={FilingFailed} emailPassComplete={EmailPassComplete} "
            + "threadsExamined={ThreadsExamined} "
            + "ownerNotTeam={OwnerNotTeam} ownerAssigned={OwnerAssigned} ownerAlreadyCorrect={OwnerAlreadyCorrect} "
            + "skippedSecure={SkippedSecure} ownerNotApplicable={OwnerNotApplicable} ownerRefused={OwnerRefused} "
            + "skippedAmbiguousAnchor={SkippedAmbiguousAnchor} "
            + "ownerFailed={OwnerFailed} threadPassComplete={ThreadPassComplete} attempt={Attempt} durationMs={DurationMs} "
            + "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, mode, filing.Examined, filing.NoThread, filing.Unanchored, filing.AnchoredElsewhere,
            filing.InactiveThread, filing.PrivateThread, filing.ThreadUnreadable, filing.Candidates, filing.Reconciled,
            filing.Unresolved, filing.Failed, filing.PassComplete, owners.Examined, owners.OwnerNotTeam, owners.Assigned,
            owners.AlreadyCorrect, owners.SkippedSecure, owners.NotApplicable, owners.Refused, owners.SkippedAmbiguousAnchor,
            owners.Failed, owners.PassComplete, context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId,
            context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: filing.Examined + owners.Examined,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    mode,
                    apply,
                    attempt = context.Attempt,
                    filing = filing.ToJson(),
                    owner = owners.ToJson(apply),
                },
                ResultJsonOptions));
    }

    // ── Part 1: filing follow ────────────────────────────────────────────────────────────────────────────────────

    private async Task SweepFilingAsync(
        IGenericEntityService entityService, IThreadResolver resolver, bool apply, int maxEmails,
        FilingReport report, List<string> problems, JobRunContext context, CancellationToken ct)
    {
        report.StartAfter = _emailCursor;
        var last = _emailCursor;
        var passComplete = false;

        while (report.Examined < maxEmails)
        {
            ct.ThrowIfCancellationRequested();

            var take = Math.Min(ScanPageSize, maxEmails - report.Examined);
            IReadOnlyList<Entity> emails;
            IReadOnlyDictionary<Guid, Entity> threads;
            try
            {
                emails = (await entityService.RetrieveMultipleAsync(BuildEmailQuery(last, take), ct).ConfigureAwait(false)).Entities;
                threads = await ReadThreadsAsync(entityService, emails, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                report.EnumerationFailed = true;
                problems.Add($"The filed emails could not be read, so none after {last?.ToString("D") ?? "the start"} were examined ({ex.Message}).");
                _logger.LogError(ex, "{Prefix} The filed emails could not be read; the run is FAILED. correlationId={CorrelationId}",
                    LogPrefix, context.CorrelationId);
                break;
            }

            foreach (var email in emails)
            {
                ct.ThrowIfCancellationRequested();
                last = email.Id;
                report.Examined++;

                var verdict = Classify(email, threads);
                report.Count(verdict);
                if (!IsCandidate(verdict))
                    continue;

                report.Candidates++;
                var threadId = email.GetAttributeValue<EntityReference>(ThreadLookup)?.Id;
                _logger.LogInformation(
                    "{Prefix} candidate mode={Mode} communicationId={CommunicationId} category={Category} threadId={ThreadId} "
                    + "correlationId={CorrelationId}",
                    LogPrefix, apply ? "apply" : "dry-run", email.Id, verdict, threadId, context.CorrelationId);
                if (report.Sample.Count < MaxListedPerRun)
                    report.Sample.Add(new { communicationId = email.Id, category = verdict.ToString(), threadId });

                if (!apply)
                    continue;

                try
                {
                    // #2110: on its own, not inside the step after a re-file — a join it makes under a record the email is
                    // not filed to runs that step for the join (FilingReconcileHost.Standalone).
                    await resolver.ReconcileThreadForFilingAsync(email.Id, FilingReconcileHost.Standalone, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // The reconcile does not throw (NFR-02); this is the run's own guard, so one row never stops the run.
                    report.Failed++;
                    _logger.LogWarning(ex, "{Prefix} Reconcile of {CommunicationId} failed; the run continues. correlationId={CorrelationId}",
                        LogPrefix, email.Id, context.CorrelationId);
                    continue;
                }

                // The reconcile never throws and logs its own faults, so "it returned" says nothing about the outcome. Re-read
                // the email and its thread and apply the same test: only an email that no longer needs the reconcile counts as
                // reconciled; one that still does is reported as unresolved (and makes the run partial).
                if (await IsResolvedAsync(entityService, email.Id, context, ct).ConfigureAwait(false))
                {
                    report.Reconciled++;
                }
                else
                {
                    report.Unresolved++;
                    _logger.LogWarning(
                        "{Prefix} Reconcile of {CommunicationId} left it unresolved: it is still a candidate. correlationId={CorrelationId}",
                        LogPrefix, email.Id, context.CorrelationId);
                }
            }

            if (emails.Count < take)
            {
                passComplete = true;
                break;
            }
        }

        report.EndAt = last;
        report.PassComplete = passComplete;
        _emailCursor = passComplete ? null : last;
    }

    /// <summary>
    /// After an Apply reconcile: <c>true</c> when the email, read afresh, is no longer a candidate (its thread is now
    /// anchored to a record it is filed to), or it is no longer in scope (deactivated, or unfiled meanwhile). <c>false</c> when
    /// it is still a candidate, or when that could not be read — an outcome that cannot be verified is not reported as done.
    /// </summary>
    private async Task<bool> IsResolvedAsync(
        IGenericEntityService entityService, Guid emailId, JobRunContext context, CancellationToken ct)
    {
        try
        {
            var columns = new List<string> { ThreadLookup, StateField };
            columns.AddRange(RegardingFieldMap.AllRegardingFields);
            var email = await entityService.RetrieveAsync(EmailTable, emailId, columns.ToArray(), ct).ConfigureAwait(false);
            if (email is null)
                return false;

            var filed = RegardingFieldMap.AllRegardingFields.Any(
                f => email.GetAttributeValue<EntityReference>(f) is { } er && er.Id != Guid.Empty);
            if (!filed || (email.GetAttributeValue<OptionSetValue>(StateField)?.Value ?? StateActive) != StateActive)
                return true; // out of the sweep's scope now (unfiled or deactivated since): nothing left to reconcile

            var threads = await ReadThreadsAsync(entityService, [email], ct).ConfigureAwait(false);
            return !IsCandidate(Classify(email, threads));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} The result of reconciling {CommunicationId} could not be verified. correlationId={CorrelationId}",
                LogPrefix, emailId, context.CorrelationId);
            return false;
        }
    }

    /// <summary>The next <paramref name="take"/> active, filed emails after <paramref name="after"/>, in id order.</summary>
    internal static QueryExpression BuildEmailQuery(Guid? after, int take)
    {
        var columns = new List<string> { ThreadLookup };
        columns.AddRange(RegardingFieldMap.AllRegardingFields);

        var query = new QueryExpression(EmailTable)
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            TopCount = take,
        };
        query.Criteria.AddCondition(StateField, ConditionOperator.Equal, StateActive);
        query.Criteria.AddCondition(TypeField, ConditionOperator.Equal, (int)CommunicationType.Email);
        if (after is { } cursor)
            query.Criteria.AddCondition(EmailPk, ConditionOperator.GreaterThan, cursor);

        // FILED: any typed regarding lookup is set.
        var filed = query.Criteria.AddFilter(LogicalOperator.Or);
        foreach (var field in RegardingFieldMap.AllRegardingFields)
            filed.AddCondition(field, ConditionOperator.NotNull);

        query.AddOrder(EmailPk, OrderType.Ascending);
        return query;
    }

    /// <summary>The threads of <paramref name="emails"/> (any state — an inactive one is a verdict, not an absence), by id.</summary>
    private static async Task<IReadOnlyDictionary<Guid, Entity>> ReadThreadsAsync(
        IGenericEntityService entityService, IReadOnlyList<Entity> emails, CancellationToken ct)
    {
        var ids = emails
            .Select(e => e.GetAttributeValue<EntityReference>(ThreadLookup)?.Id)
            .Where(id => id is { } v && v != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Entity>();

        var columns = new List<string>(ThreadResolver.ThreadRegardingColumns) { PrivacyField, StateField };
        var query = new QueryExpression(ThreadTable)
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            TopCount = ids.Count,
        };
        query.Criteria.AddCondition(ThreadPk, ConditionOperator.In, ids.Cast<object>().ToArray());

        var result = await entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Where(t => t.Id != Guid.Empty).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>What a filed email's thread is, against the reconcile's rule 1 and its skips.</summary>
    internal enum FilingVerdict
    {
        /// <summary>Active, Open and anchored to a record the email is filed to — on that record's panel already.</summary>
        Ok,

        /// <summary>The email has no thread.</summary>
        NoThread,

        /// <summary>The email names a thread that could not be read; the reconcile would skip it too.</summary>
        ThreadUnreadable,

        /// <summary>The thread is not Open: the reconcile leaves a private thread's isolation alone.</summary>
        PrivateThread,

        /// <summary>The thread is deactivated — a user's delete (their emails stay filed): counted, never a candidate.</summary>
        InactiveThread,

        /// <summary>The thread is anchored to no record.</summary>
        Unanchored,

        /// <summary>The thread is anchored to a record the email is not filed to.</summary>
        AnchoredElsewhere,
    }

    /// <summary>
    /// Classifies one filed email against its thread with the reconcile's own tests (<see cref="ThreadResolver.ThreadAnchorOf"/>,
    /// <see cref="ThreadResolver.IsFiledTo"/>): a superset of what the reconcile acts on, never a different rule.
    /// </summary>
    internal static FilingVerdict Classify(Entity email, IReadOnlyDictionary<Guid, Entity> threads)
    {
        if (email.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } threadId || threadId == Guid.Empty)
            return FilingVerdict.NoThread;

        if (!threads.TryGetValue(threadId, out var thread))
            return FilingVerdict.ThreadUnreadable;

        if (thread.GetAttributeValue<OptionSetValue>(PrivacyField)?.Value != PrivacyOpen)
            return FilingVerdict.PrivateThread;

        if ((thread.GetAttributeValue<OptionSetValue>(StateField)?.Value ?? StateActive) != StateActive)
            return FilingVerdict.InactiveThread;

        var anchor = ThreadResolver.ThreadAnchorOf(thread);
        if (!anchor.HasAnchor)
            return FilingVerdict.Unanchored;

        return ThreadResolver.IsFiledTo(email, anchor) ? FilingVerdict.Ok : FilingVerdict.AnchoredElsewhere;
    }

    /// <remarks>
    /// <see cref="FilingVerdict.InactiveThread"/> is counted (<c>skippedInactiveThread</c>) but is NOT a candidate: the user
    /// thread delete (<c>DeactivateThreadAsync</c>) only deactivates the thread — its emails stay active and filed — so an
    /// inactive thread is a user's delete, and the backfill must not undo it by moving those emails into a live thread.
    /// (A thread the reconcile itself retires when it empties holds no email, so it never reaches this test.)
    /// </remarks>
    private static bool IsCandidate(FilingVerdict verdict) =>
        verdict is FilingVerdict.NoThread or FilingVerdict.Unanchored or FilingVerdict.AnchoredElsewhere;

    // ── Part 2: anchored-thread owner ────────────────────────────────────────────────────────────────────────────

    private async Task SweepOwnersAsync(
        IGenericEntityService entityService, IThreadResolver resolver, bool apply, int maxThreads,
        OwnerReport report, List<string> problems, CancellationToken ct)
    {
        report.StartAfter = _threadCursor;
        var last = _threadCursor;
        var passComplete = false;

        while (report.Examined < maxThreads)
        {
            ct.ThrowIfCancellationRequested();

            var take = Math.Min(ScanPageSize, maxThreads - report.Examined);
            IReadOnlyList<Entity> threads;
            try
            {
                threads = (await entityService.RetrieveMultipleAsync(BuildAnchoredThreadQuery(last, take), ct).ConfigureAwait(false)).Entities;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                report.EnumerationFailed = true;
                problems.Add($"The anchored threads could not be read, so none after {last?.ToString("D") ?? "the start"} were examined ({ex.Message}).");
                _logger.LogError(ex, "{Prefix} The anchored threads could not be read; the run is FAILED.", LogPrefix);
                break;
            }

            foreach (var thread in threads)
            {
                ct.ThrowIfCancellationRequested();
                last = thread.Id;
                report.Examined++;
                if (!IsTeamOwned(thread))
                    report.OwnerNotTeam++;

                // Dry run asks the same question and writes nothing; the answer is the same either way.
                ThreadOwnerReconcileResult result;
                try
                {
                    result = await resolver.ReconcileAnchoredThreadOwnerAsync(thread.Id, dryRun: !apply, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // The resolver does not throw (NFR-02); this is the run's own guard, so one row never stops the run.
                    result = ThreadOwnerReconcileOutcome.Failed;
                    _logger.LogWarning(ex, "{Prefix} Owner reconcile of thread {ThreadId} failed; the run continues.", LogPrefix, thread.Id);
                }

                report.Count(result.Outcome);
                if (result.Outcome == ThreadOwnerReconcileOutcome.Assigned && report.Sample.Count < MaxListedPerRun)
                {
                    // The previous owner is the before-state: the write replaces it, so a wrong re-own is reversed from here.
                    report.Sample.Add(new
                    {
                        threadId = thread.Id,
                        ownerIsTeam = IsTeamOwned(thread),
                        previousOwnerType = result.PreviousOwnerType,
                        previousOwnerId = result.PreviousOwnerId,
                        newOwnerTeamId = result.NewOwnerTeamId,
                    });
                }
            }

            if (threads.Count < take)
            {
                passComplete = true;
                break;
            }
        }

        report.EndAt = last;
        report.PassComplete = passComplete;
        _threadCursor = passComplete ? null : last;
    }

    /// <summary>The next <paramref name="take"/> active, Open threads anchored by a typed regarding lookup after <paramref name="after"/>.</summary>
    internal static QueryExpression BuildAnchoredThreadQuery(Guid? after, int take)
    {
        var columns = new List<string>(ThreadResolver.ThreadRegardingColumns) { OwnerField, OwningTeamField };
        var query = new QueryExpression(ThreadTable)
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            TopCount = take,
        };
        query.Criteria.AddCondition(StateField, ConditionOperator.Equal, StateActive);
        query.Criteria.AddCondition(PrivacyField, ConditionOperator.Equal, PrivacyOpen);
        if (after is { } cursor)
            query.Criteria.AddCondition(ThreadPk, ConditionOperator.GreaterThan, cursor);

        var anchored = query.Criteria.AddFilter(LogicalOperator.Or);
        foreach (var field in ThreadResolver.ThreadRegardingColumns)
            anchored.AddCondition(field, ConditionOperator.NotNull);

        query.AddOrder(ThreadPk, OrderType.Ascending);
        return query;
    }

    private static bool IsTeamOwned(Entity thread) =>
        thread.GetAttributeValue<EntityReference>(OwningTeamField) is not null
        || string.Equals(thread.GetAttributeValue<EntityReference>(OwnerField)?.LogicalName, "team", StringComparison.OrdinalIgnoreCase);

    private static int PositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;

    // ── Run reports ──────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class FilingReport
    {
        public Guid? StartAfter { get; set; }
        public Guid? EndAt { get; set; }
        public bool PassComplete { get; set; }
        public bool EnumerationFailed { get; set; }
        public int Examined { get; set; }
        public int Ok { get; private set; }
        public int NoThread { get; private set; }
        public int ThreadUnreadable { get; private set; }
        public int PrivateThread { get; private set; }
        public int InactiveThread { get; private set; }
        public int Unanchored { get; private set; }
        public int AnchoredElsewhere { get; private set; }
        public int Candidates { get; set; }
        public int Reconciled { get; set; }
        public int Unresolved { get; set; }
        public int Failed { get; set; }
        public List<object> Sample { get; } = [];

        public void Count(FilingVerdict verdict)
        {
            switch (verdict)
            {
                case FilingVerdict.Ok: Ok++; break;
                case FilingVerdict.NoThread: NoThread++; break;
                case FilingVerdict.ThreadUnreadable: ThreadUnreadable++; break;
                case FilingVerdict.PrivateThread: PrivateThread++; break;
                case FilingVerdict.InactiveThread: InactiveThread++; break;
                case FilingVerdict.Unanchored: Unanchored++; break;
                case FilingVerdict.AnchoredElsewhere: AnchoredElsewhere++; break;
            }
        }

        public object ToJson() => new
        {
            startAfter = StartAfter,
            endAt = EndAt,
            passComplete = PassComplete,
            examined = Examined,
            ok = Ok,
            noThread = NoThread,
            unanchored = Unanchored,
            anchoredElsewhere = AnchoredElsewhere,
            skippedInactiveThread = InactiveThread,
            privateThread = PrivateThread,
            threadUnreadable = ThreadUnreadable,
            candidates = Candidates,
            reconciled = Reconciled,
            unresolved = Unresolved,
            failed = Failed,
            enumerationFailed = EnumerationFailed,
            sampleTotal = Candidates,
            sampleListed = Sample.Count,
            sample = Sample,
        };
    }

    private sealed class OwnerReport
    {
        public Guid? StartAfter { get; set; }
        public Guid? EndAt { get; set; }
        public bool PassComplete { get; set; }
        public bool EnumerationFailed { get; set; }
        public int Examined { get; set; }
        public int OwnerNotTeam { get; set; }
        public int Assigned { get; private set; }
        public int AlreadyCorrect { get; private set; }
        public int SkippedSecure { get; private set; }
        public int NotApplicable { get; private set; }
        public int Refused { get; private set; }
        public int SkippedAmbiguousAnchor { get; private set; }
        public int Failed { get; private set; }
        public List<object> Sample { get; } = [];

        public void Count(ThreadOwnerReconcileOutcome outcome)
        {
            switch (outcome)
            {
                case ThreadOwnerReconcileOutcome.Assigned: Assigned++; break;
                case ThreadOwnerReconcileOutcome.AlreadyCorrect: AlreadyCorrect++; break;
                case ThreadOwnerReconcileOutcome.SkippedSecure: SkippedSecure++; break;
                case ThreadOwnerReconcileOutcome.SkippedAmbiguousAnchor: SkippedAmbiguousAnchor++; break;
                case ThreadOwnerReconcileOutcome.NotApplicable: NotApplicable++; break;
                case ThreadOwnerReconcileOutcome.Refused: Refused++; break;
                default: Failed++; break;
            }
        }

        public object ToJson(bool apply) => new
        {
            startAfter = StartAfter,
            endAt = EndAt,
            passComplete = PassComplete,
            examined = Examined,
            ownerNotTeam = OwnerNotTeam,
            assigned = apply ? Assigned : 0,
            wouldAssign = apply ? 0 : Assigned,
            alreadyCorrect = AlreadyCorrect,
            skippedSecure = SkippedSecure,
            notApplicable = NotApplicable,
            refused = Refused,
            skippedAmbiguousAnchor = SkippedAmbiguousAnchor,
            failed = Failed,
            enumerationFailed = EnumerationFailed,
            sampleListed = Sample.Count,
            sample = Sample,
        };
    }
}
