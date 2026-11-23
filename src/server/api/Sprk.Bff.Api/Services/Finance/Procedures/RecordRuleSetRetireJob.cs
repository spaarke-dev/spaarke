using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 117 (procedure model 5.2 / 12, O-17, D-146 amended): the no-plugin cleanup after a core record's delete. A core-record
/// delete clears the record lookups of the record's record-scoped rule set and of its Procedure assignment rows (RemoveLink, task 080). This job
/// retires each such rule set (its record rules Off, then the set Retired) and deletes each assignment row left with no core record.
/// </summary>
/// <remarks>
/// <para><b>Each run, two parts</b>, each through its owner: (1) <see cref="RuleSetService.RetireUnlinkedRecordSetsAsync"/> — the one rule-set
/// lifecycle writer; (2) <see cref="ProcedureAssignmentService.DeleteOrphanAssignmentsAsync"/> — the assignment writer. Both select in id order,
/// <see cref="PageSizeKey"/> rows a page and at most <see cref="MaxPagesKey"/> pages, only rows last modified at least
/// <see cref="MinAgeMinutesKey"/> minutes ago (the age floor: a row whose lookup has not been written yet is never taken), and re-read and
/// re-check each row before writing it. Every run starts at the top: a row that was handled no longer matches, so the next run reaches the rest.</para>
/// <para><b>Report only by default.</b> The job writes only when <see cref="ApplyKey"/> is <c>true</c> (App Service setting
/// <c>RecordRuleSetRetireSweep__Apply</c>; absent or unparseable = report only), as <c>CommunicationThreadFilingSweepJob</c>. A report-only run
/// counts what it would do and writes nothing. Task 079 switches it on with the owner's OK.</para>
/// <para><b>The eBilling kill switch</b> (ADR-018: the same flag in endpoints and background work), read by the job itself first, as
/// <c>EligibleApproverRefreshJob</c> does: with <c>Ebilling:Enabled</c> false the run reads and writes nothing, whatever <see cref="ApplyKey"/>
/// says, and writes its heartbeat with <c>skippedReason=ebilling-disabled</c> and a successful result (the host alone sets <c>Skipped</c>).</para>
/// <para><b>Never</b>: a delete of anything but an orphan assignment row; an Assign, an owner or creator-person write, or a lookup write; a policy
/// version write; a set with a Procedure, a ClientDefault or Firm set, a set or row with any core lookup; any request against
/// <c>sprk_procedure</c>, <c>sprk_procedureversion</c>, <c>sprk_storedfile</c>, <c>sprk_ratecard</c>, <c>sprk_ratecardrate</c>, documents or
/// communications. Signals are not closed here: D-170's nightly sweep closes an Off policy's Signals.</para>
/// <para><b>Use.</b> Registered DISABLED (it never fires on its own); an admin runs it with
/// <c>POST /api/admin/jobs/finance-record-rule-set-retire/trigger</c> and reads the report, or enables the schedule
/// (<see cref="CronKey"/>, default <see cref="DefaultCronSchedule"/>).</para>
/// <para><b>ADR-036 A1.</b> Rule 1: the scheduler's lease. Rule 3: every step is keyed on observed state, so a re-run repeats nothing. Rule 4: a
/// page that cannot be read is a FAILED run, never "nothing to do"; a per-row fault is counted and the run continues; nothing escapes. Rule 5:
/// one heartbeat per run. Rule 6: <c>AddScheduledJob</c> in <c>AddFinanceModule</c>; no BackgroundService.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): BFF, in-process scheduler — BFF domain writers over BFF-owned tables, app-only, low volume. No
/// new package, store, host or endpoint. §11: <i>Existing</i> — <c>SignalReevaluationJob</c> closes Off policies' Signals but never finds an
/// unlinked set; <c>SecureChildReconciliationJob</c> re-owns secure children and writes no status; uac-r2's delete job writes no Retire.
/// <i>Extension</i> — the writes extend the two existing writers; only the trigger is new. <i>Cost of doing nothing</i> — a deleted record's
/// record rules stay enabled and its set Active for ever, and its orphan assignment rows stay.</para>
/// </remarks>
public sealed class RecordRuleSetRetireJob : IScheduledJob
{
    /// <summary>Stable job id: the admin trigger, status and history routes key off it.</summary>
    public const string JobIdConstant = "finance-record-rule-set-retire";

    /// <summary>Daily at 03:45 UTC when an admin enables the job; registered disabled.</summary>
    internal const string DefaultCronSchedule = "45 3 * * *";

    /// <summary>The write switch (<c>RecordRuleSetRetireSweep__Apply</c>): <c>true</c> = write; absent or anything else = report only.</summary>
    public const string ApplyKey = "RecordRuleSetRetireSweep:Apply";

    /// <summary>The schedule (<c>RecordRuleSetRetireSweep__Cron</c>), read at registration; blank = <see cref="DefaultCronSchedule"/>.</summary>
    public const string CronKey = "RecordRuleSetRetireSweep:Cron";

    /// <summary>Rows per page (<c>RecordRuleSetRetireSweep__PageSize</c>), 1..<see cref="MaxPageSize"/>.</summary>
    public const string PageSizeKey = "RecordRuleSetRetireSweep:PageSize";

    /// <summary>Pages per part per run (<c>RecordRuleSetRetireSweep__MaxPages</c>).</summary>
    public const string MaxPagesKey = "RecordRuleSetRetireSweep:MaxPages";

    /// <summary>The age floor in minutes (<c>RecordRuleSetRetireSweep__MinAgeMinutes</c>): only rows last modified at least this long ago.</summary>
    public const string MinAgeMinutesKey = "RecordRuleSetRetireSweep:MinAgeMinutes";

    internal const int DefaultPageSize = 100;
    internal const int MaxPageSize = 5000;
    internal const int DefaultMaxPages = 10;
    internal const int DefaultMinAgeMinutes = 15;

    /// <summary>The heartbeat's skip reason when <c>Ebilling:Enabled</c> is false (as <c>EligibleApproverRefreshJob</c>).</summary>
    internal const string SkippedEbillingDisabled = "ebilling-disabled";

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    private const string LogPrefix = "[RECORD-RULESET-RETIRE]";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EbillingOptions> _ebilling;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecordRuleSetRetireJob> _logger;

    public RecordRuleSetRetireJob(
        IServiceScopeFactory scopeFactory,
        IOptions<EbillingOptions> ebilling,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<RecordRuleSetRetireJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Record Rule Set Retire Sweep";

    /// <inheritdoc />
    public string Description =>
        "After a core record's delete: retires each record-scoped billing rule set left with no record (its record rules Off, then the set "
        + "Retired) and deletes each Procedure assignment row left with no core record. Report only unless " + ApplyKey
        + " is true. Disabled until enabled; run on demand with POST /api/admin/jobs/" + JobIdConstant + "/trigger.";

    /// <summary>The cron the job is registered with: <see cref="CronKey"/>, or <see cref="DefaultCronSchedule"/> when blank.</summary>
    internal static string CronOf(IConfiguration configuration) =>
        configuration[CronKey] is { } cron && !string.IsNullOrWhiteSpace(cron) ? cron.Trim() : DefaultCronSchedule;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        // ADR-018: the kill switch first. Off = no read and no write, whatever Apply says.
        var skippedReason = _ebilling.Value.Enabled ? null : SkippedEbillingDisabled;
        var apply = skippedReason is null && bool.TryParse(_configuration[ApplyKey], out var a) && a;
        var pageSize = Math.Min(PositiveInt(_configuration[PageSizeKey], DefaultPageSize), MaxPageSize);
        var maxPages = PositiveInt(_configuration[MaxPagesKey], DefaultMaxPages);
        var minAgeMinutes = PositiveInt(_configuration[MinAgeMinutesKey], DefaultMinAgeMinutes);
        var modifiedBefore = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-minAgeMinutes);
        var problems = new List<string>();
        var status = StatusOk;
        RuleSetService.UnlinkedRecordSetRetireResult? sets = null;
        ProcedureAssignmentService.OrphanAssignmentDeleteResult? assignments = null;

        if (skippedReason is null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ruleSets = scope.ServiceProvider.GetRequiredService<RuleSetService>();
                var assignmentService = scope.ServiceProvider.GetRequiredService<ProcedureAssignmentService>();

                sets = await ruleSets.RetireUnlinkedRecordSetsAsync(apply, modifiedBefore, pageSize, maxPages, cancellationToken)
                    .ConfigureAwait(false);
                assignments = await assignmentService.DeleteOrphanAssignmentsAsync(apply, modifiedBefore, pageSize, maxPages, cancellationToken)
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
                // ADR-003 for a writer: a page that could not be read is a FAILED run, never "nothing to do".
                if (sets!.ReadFailed || assignments!.ReadFailed)
                {
                    status = StatusError;
                    problems.Add("A page of the selection could not be read; the rows after it were not examined.");
                }
                else if (sets.Faulted > 0 || assignments.Faulted > 0)
                {
                    status = StatusPartial;
                }
            }
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var mode = apply ? "apply" : "report-only";
        var level = status != StatusOk ? LogLevel.Warning
            : !apply && ((sets?.Selected ?? 0) > 0 || (assignments?.Selected ?? 0) > 0) ? LogLevel.Warning
            : LogLevel.Information;

        // THE HEARTBEAT (ADR-036 A1 rule 5): one line per run, with nothing to do (or the kill switch off) as well.
        _logger.Log(
            level,
            "{Prefix} heartbeat status={Status} skippedReason={SkippedReason} mode={Mode} apply={Apply} setsSelected={SetsSelected} "
            + "setsRetired={SetsRetired} setsSkipped={SetsSkipped} setsFaulted={SetsFaulted} policiesSwitchedOff={PoliciesSwitchedOff} "
            + "setsMoreRemain={SetsMoreRemain} assignmentsSelected={AssignmentsSelected} assignmentsDeleted={AssignmentsDeleted} "
            + "assignmentsSkipped={AssignmentsSkipped} assignmentsFaulted={AssignmentsFaulted} assignmentsMoreRemain={AssignmentsMoreRemain} "
            + "modifiedBefore={ModifiedBefore:o} attempt={Attempt} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, skippedReason ?? "no", mode, apply, sets?.Selected ?? 0, sets?.Retired ?? 0, sets?.Skipped ?? 0,
            sets?.Faulted ?? 0, sets?.PoliciesSwitchedOff ?? 0, sets?.MoreRemain ?? false, assignments?.Selected ?? 0, assignments?.Deleted ?? 0,
            assignments?.Skipped ?? 0, assignments?.Faulted ?? 0, assignments?.MoreRemain ?? false, modifiedBefore, context.Attempt,
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: (sets?.Selected ?? 0) + (assignments?.Selected ?? 0),
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    skippedReason,
                    mode,
                    apply,
                    attempt = context.Attempt,
                    modifiedBefore,
                    ruleSets = sets is null ? null : new
                    {
                        selected = sets.Selected,
                        retired = sets.Retired,
                        skipped = sets.Skipped,
                        faulted = sets.Faulted,
                        policiesSwitchedOff = sets.PoliciesSwitchedOff,
                        readFailed = sets.ReadFailed,
                        moreRemain = sets.MoreRemain,
                    },
                    assignments = assignments is null ? null : new
                    {
                        selected = assignments.Selected,
                        deleted = assignments.Deleted,
                        skipped = assignments.Skipped,
                        faulted = assignments.Faulted,
                        readFailed = assignments.ReadFailed,
                        moreRemain = assignments.MoreRemain,
                    },
                },
                ResultJsonOptions));
    }

    private static int PositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
