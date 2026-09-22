using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// Owner decision D-1 option B (2026-09-19), ISS-026 / #1006 write half and ISS-020 / #999 part 4 —
/// ONE scheduled pass that makes a row's OWN <c>statecode</c> / <c>sprk_expiresdate</c> the truth about
/// whether it confers access, so nothing has to re-derive that from a parent or a date on every read.
/// </summary>
/// <remarks>
/// <para><b>The one root defect, three symptoms.</b> (1) An external grant created outside the BFF — a form,
/// the Web API, a flow, an import — carries no <c>sprk_expiresdate</c>, so it was unbounded AND invisible to
/// <see cref="GrantExpiryReminderJob"/>, whose window is today..+30. (2) NO query on the organization access
/// path consults <c>sprk_organization.statecode</c>, so deactivating a firm revokes nothing — its grant rows
/// and membership rows stay active and every member keeps inherited access, with no error and no surface that
/// shows it. (3) Nothing in the repo writes <c>sprk_contactorganization</c> at all (those rows are
/// maker-authored), so a membership ended by date keeps conferring indefinitely.</para>
///
/// <para><b>Three rules, one responsibility</b> (CLAUDE.md §11.5 — this is ONE reason to change, not three):
/// <list type="number">
///   <item><b>R1</b> — an ACTIVE <c>sprk_externalrecordaccess</c> with no <c>sprk_expiresdate</c> is stamped
///     with <see cref="ExternalGrantLifecycle.DefaultExpiry"/>, the same value <c>/grant</c> writes. There is
///     exactly ONE definition of the 90-day default and this job does not add a second.</item>
///   <item><b>R2</b> — an ACTIVE grant whose <c>sprk_organization</c> points at an INACTIVE
///     <c>sprk_organization</c> is deactivated.</item>
///   <item><b>R3</b> — an ACTIVE <c>sprk_contactorganization</c> whose <c>sprk_enddate</c> has PASSED is
///     deactivated. Access holds THROUGH the end date, matching the read path's expiry boundary.</item>
/// </list>
/// R2 wins over R1 on the same row: stamping an expiry onto a row this run is about to deactivate is wasted
/// work, and would put two updates for one id into one transaction.</para>
///
/// <para><b>Coherent with task 107, which shipped first.</b> Task 107 (D-1 option A) inverted the READ default:
/// a null <c>sprk_expiresdate</c> now confers NOTHING. So R1 is no longer a security fix — the exposure is
/// already closed at read time — it is a DATA REPAIR that turns a row conferring nothing into a row conferring
/// access for 90 more days. That makes R1 the one rule here that GRANTS rather than removes, which is why it is
/// gated by the same owner switch as R2 and R3 rather than treated as harmless.</para>
///
/// <para><b>Ships disabled, and writes nothing until an owner says so</b> (owner decision D-2 part 3). TWO
/// independent switches, both of which must be thrown: the registration is
/// <c>AddScheduledJob&lt;&gt;(cron, enabled: false)</c> so the scheduler never ticks it, and writes are gated
/// on <see cref="WritesEnabledConfigKey"/>, which DEFAULTS TO REPORT-ONLY. The flag is named positively on
/// purpose: an absent, empty or unparseable value is <c>false</c>, so every way of getting the configuration
/// wrong lands on "write nothing". A before-state line is logged for EVERY row the run would change, in both
/// modes, before anything is written — so the report a manual admin trigger produces is the same evidence the
/// write pass would act on.</para>
///
/// <para><b>Fail direction — deliberately inverted relative to the read path</b> (ADR-003). For a READER,
/// an empty result on a fault is the ISS-019 hazard: it reads as "no access" or "no rows" and is acted on. For
/// this WRITER the same empty result is fail-SAFE — it writes nothing. So the requirement here is not that a
/// failure still produces writes; it is that the RUN IS RECORDED FAILED and never reported as "nothing to
/// reconcile". A scan that throws yields <c>Success=false</c>, an error status in the heartbeat and
/// <c>scanFailed=true</c> for that rule — distinguishable in the logs from a clean run with zero drift.</para>
///
/// <para><b>Retry</b> (ADR-036 A1 rule 4): this job does NOT throw from <see cref="ExecuteAsync"/>. The rule is
/// "throw only when a retry THIS tick could complete work the tick would otherwise lose", and nothing here is
/// lost: reconciliation is idempotent by construction, so a rule that failed today is picked up whole by
/// tomorrow's tick — which is also why ADR-036 A1.1's lease-outage case needs no catch-up mechanism. Against
/// that, the retry policy would re-run an ACCESS-REMOVAL write path three times in quick succession for no
/// gain. The failure is reported loudly instead (<c>Success=false</c> + <c>ErrorMessage</c> + an Error log +
/// the heartbeat), which is what ADR-036's "MUST NOT swallow exceptions" requires.</para>
///
/// <para><b>At most once per chunk</b> (ADR-036 A1 rule 3): "already applied?" → take an atomic claim → check
/// again under the claim → write the chunk → write the completion marker → release.
/// <see cref="IIdempotencyService"/>'s claim is check-then-set and fails open (#984); the second check closes
/// the window between the first check and the claim, and the scheduler's lease (task 103) means no second run
/// is normally in flight at all. Nothing is written for the lease or the slot guard here — that is the HOST's
/// job (ADR-036 A1 rules 1, 2 and 7).</para>
///
/// <para><b>Real paging, transactional chunks.</b> Each scan pages with a paging cookie to
/// <see cref="MaxPages"/> pages of <see cref="PageSize"/>; past that the rule is reported TRUNCATED rather
/// than silently reconciling a prefix. Writes go through
/// <see cref="IGenericEntityService.BulkUpdateAsync"/> in chunks of <see cref="ChunkSize"/>, which is ONE
/// <c>ExecuteTransactionRequest</c> — all-or-nothing (task 096), so a chunk that faults leaves every row in it
/// unchanged and later chunks are still applied. Every field this job writes SETS a value; none clears one,
/// which is what keeps it inside <c>BulkUpdateAsync</c>'s documented limitation.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF, on the in-process <c>Spaarke.Scheduling</c>
/// host, registered with <c>AddScheduledJob</c> in <c>ExternalAccessModule</c> beside
/// <see cref="GrantExpiryReminderJob"/>. BFF domain code over BFF-owned tables, BFF identity, BFF release
/// cadence, one scan a day (ADR-052 B2/B3); the one Functions-leaning signal — one dispatch per schedule — is
/// already met in place by the host's lease. Moving it would cost a deployable per stamp and the extraction of
/// <see cref="ExternalGrantLifecycle"/>, for two queries a day. No new scheduler, store, client or package.</para>
/// </remarks>
public sealed class ExternalAccessReconciliationJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and the admin endpoints key off it.</summary>
    public const string JobIdConstant = "external-access-reconciliation";

    /// <summary>
    /// Daily at 05:00 UTC — an hour before <see cref="GrantExpiryReminderJob"/>, so a row R1 stamps today is
    /// visible to the reminder sweep on the same morning rather than the next one.
    /// </summary>
    internal const string DefaultCronSchedule = "0 5 * * *";

    /// <summary>
    /// The owner switch that turns report-only into writes. Named positively so that absent, empty or
    /// unparseable configuration all mean <c>false</c> — every way of getting it wrong writes nothing.
    /// </summary>
    internal const string WritesEnabledConfigKey = "ExternalAccess:Reconciliation:WritesEnabled";

    /// <summary>Rows per page of each scan.</summary>
    internal const int PageSize = 5000;

    /// <summary>The paging ceiling per scan. Past it the rule reports truncated instead of a silent prefix.</summary>
    internal const int MaxPages = 20;

    /// <summary>
    /// Rows per <see cref="IGenericEntityService.BulkUpdateAsync"/> transaction. Well under the documented
    /// 1,000 ceiling: a smaller chunk means a fault rolls back fewer rows, and every chunk is an independent
    /// all-or-nothing unit with its own claim.
    /// </summary>
    internal const int ChunkSize = 100;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string ModeReportOnly = "report-only";
    internal const string ModeWrite = "write";

    /// <summary>R1 — stamp the FR-33 default expiry onto an active, undated grant.</summary>
    internal const string RuleStampDefaultExpiry = "R1-stamp-default-expiry";

    /// <summary>R2 — deactivate an active grant whose organization is inactive.</summary>
    internal const string RuleDeactivateOrphanedOrgGrant = "R2-deactivate-grant-of-inactive-organization";

    /// <summary>R3 — deactivate an active membership whose end date has passed.</summary>
    internal const string RuleDeactivateEndedMembership = "R3-deactivate-ended-membership";

    internal const string JunctionEntityLogicalName = "sprk_contactorganization";
    internal const string OrganizationEntityLogicalName = "sprk_organization";

    /// <summary>
    /// The grant row's own organization lookup, as an SDK ATTRIBUTE name. Distinct from the Web API
    /// projection <c>_sprk_organization_value</c> that <see cref="ExternalGrantKey.ToActiveRowsFilter"/>
    /// filters on, and from <c>ExternalParticipationService</c>'s per-ROOT law-firm lookups
    /// (<c>sprk_assignedlawfirm1/2</c>) — three different things that happen to concern organizations.
    /// Same local-constant convention as <see cref="GrantExpiryReminderJob"/>'s FetchXML names.
    /// </summary>
    internal const string OrganizationLookupAttribute = "sprk_organization";

    private const string OrganizationAlias = "og";

    // Dataverse Inactive on BOTH tables, live-verified 2026-09-21 (MCP describe): statecode Inactive(1) and
    // statuscode Inactive(2). Writing statecode alone leaves an inconsistent status reason, which is why the
    // deactivation payload always carries both (the shape ProjectClosureEndpoint writes).
    private const int StateCodeInactive = 1;
    private const int StatusCodeInactive = 2;

    // Outlives a retried tick and the host's 2 h MaxRunDuration, so a chunk applied by one attempt is still
    // marked for the next; short enough that it never blocks a genuinely later reconciliation run.
    private static readonly TimeSpan AppliedMarkerLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChunkClaimDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many affected row ids each rule lists in <see cref="JobRunResult.ResultJson"/>. The COMPLETE list is
    /// in the per-row before-state log lines — <c>ResultJson</c> is an admin-surface payload and is required to
    /// stay small, so it carries a bounded sample plus the true total.
    /// </summary>
    internal const int MaxSampledIdsPerRule = 200;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalAccessReconciliationJob> _logger;

    public ExternalAccessReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<ExternalAccessReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "External Access State Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Makes an external-access row's own state the truth: stamps the default expiry on an undated grant, " +
        "deactivates a grant whose organization is inactive, and deactivates a membership whose end date has " +
        "passed. Ships disabled and in report-only mode — writes require an explicit owner switch.";

    /// <summary>
    /// Whether this run may write. Report-only is the default and the fail-safe: an absent, empty or
    /// unparseable value all resolve to <c>false</c>.
    /// </summary>
    internal bool WritesEnabled =>
        bool.TryParse(_configuration[WritesEnabledConfigKey], out var enabled) && enabled;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var today = ExternalGrantLifecycle.TodayUtc(_timeProvider);
        var writesEnabled = WritesEnabled;
        var mode = writesEnabled ? ModeWrite : ModeReportOnly;

        var grantStamp = new RuleCounts(RuleStampDefaultExpiry, ExternalGrantLifecycle.EntityLogicalName);
        var grantDeactivate = new RuleCounts(RuleDeactivateOrphanedOrgGrant, ExternalGrantLifecycle.EntityLogicalName);
        var membershipDeactivate = new RuleCounts(RuleDeactivateEndedMembership, JunctionEntityLogicalName);
        var rules = new[] { grantStamp, grantDeactivate, membershipDeactivate };

        var status = StatusOk;
        var problems = new List<string>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

            // ── Grants: ONE scan serves R1 and R2 ──────────────────────────────────────────────────────
            // Two rules over the same table read once. That is not only cheaper — it is what makes R2's
            // precedence over R1 STRUCTURAL: a row is classified once, so it cannot end up in both rules'
            // change sets and put two updates for one id into one transaction.
            await ReconcileAsync(
                entityService, idempotency, context, mode,
                page => BuildGrantScanFetchXml(page.Page, page.Cookie),
                row => PlanGrantChange(row, today),
                new[] { grantStamp, grantDeactivate },
                writesEnabled, cancellationToken).ConfigureAwait(false);

            // ── Memberships: R3 ────────────────────────────────────────────────────────────────────────
            await ReconcileAsync(
                entityService, idempotency, context, mode,
                page => BuildMembershipScanFetchXml(today, page.Page, page.Cookie),
                row => PlanMembershipChange(row, today),
                new[] { membershipDeactivate },
                writesEnabled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every rule was reconciled; no further rows were written.");
        }
// A scheduled job's last line of defence: report the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex,
                "[EXT-ACCESS-RECON] Run failed outside any single rule — reconciliation did not complete. " +
                "attempt={Attempt} correlationId={CorrelationId}",
                context.Attempt, context.CorrelationId);
        }

        foreach (var rule in rules)
        {
            // ADR-003, inverted for a writer: a scan that FAILED must never be reported as "nothing to
            // reconcile". An empty change set from a fault writes nothing (fail-safe), so what has to be
            // true is that the RUN IS RECORDED FAILED.
            if (rule.ScanFailed)
            {
                status = StatusError;
                problems.Add($"{rule.Rule}: the scan failed, so its rows were not reconciled ({rule.ScanError}).");
            }

            if (rule.Truncated)
            {
                problems.Add(
                    $"{rule.Rule}: stopped after {MaxPages} pages of {PageSize} rows; later rows were not reconciled.");
            }

            if (rule.Failed > 0)
            {
                problems.Add($"{rule.Rule}: {rule.Failed} row(s) were not written; see the per-chunk warnings.");
            }

            if (rule.ClaimHeld > 0)
            {
                problems.Add($"{rule.Rule}: {rule.ClaimHeld} row(s) were skipped because a chunk claim was still held.");
            }
        }

        if (status == StatusOk && problems.Count > 0)
        {
            status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var changed = rules.Sum(r => r.Changed);

        LogHeartbeat(status, mode, today, context, grantStamp, grantDeactivate, membershipDeactivate, rules, duration);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: changed,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    mode,
                    writesEnabled,
                    today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    attempt = context.Attempt,
                    rules = rules.Select(r => r.ToReport()).ToArray(),
                },
                ResultJsonOptions));
        // Skipped is deliberately never set: the HOST owns it (ADR-036 A1 rule 1).
    }

    /// <summary>
    /// THE HEARTBEAT (ADR-036 A1 rule 5). One structured line on every attempt, whatever happened — including
    /// an attempt that found nothing to reconcile. "Nothing to do" and "the job died" must not look alike, and
    /// a run whose counts an operator can read is the only visibility this job has while it ships disabled.
    /// </summary>
    private void LogHeartbeat(
        string status,
        string mode,
        DateOnly today,
        JobRunContext context,
        RuleCounts grantStamp,
        RuleCounts grantDeactivate,
        RuleCounts membershipDeactivate,
        IReadOnlyList<RuleCounts> rules,
        TimeSpan duration)
    {
        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "[EXT-ACCESS-RECON] heartbeat status={Status} mode={Mode} today={Today} attempt={Attempt} " +
            "r1Scanned={R1Scanned} r1Planned={R1Planned} r1Changed={R1Changed} r1Failed={R1Failed} r1Truncated={R1Truncated} r1ScanFailed={R1ScanFailed} " +
            "r2Scanned={R2Scanned} r2Planned={R2Planned} r2Changed={R2Changed} r2Failed={R2Failed} r2Truncated={R2Truncated} r2ScanFailed={R2ScanFailed} " +
            "r3Scanned={R3Scanned} r3Planned={R3Planned} r3Changed={R3Changed} r3Failed={R3Failed} r3Truncated={R3Truncated} r3ScanFailed={R3ScanFailed} " +
            "claimHeld={ClaimHeld} alreadyApplied={AlreadyApplied} markFailed={MarkFailed} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            status, mode, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), context.Attempt,
            grantStamp.Scanned, grantStamp.Planned, grantStamp.Changed, grantStamp.Failed, grantStamp.Truncated, grantStamp.ScanFailed,
            grantDeactivate.Scanned, grantDeactivate.Planned, grantDeactivate.Changed, grantDeactivate.Failed, grantDeactivate.Truncated, grantDeactivate.ScanFailed,
            membershipDeactivate.Scanned, membershipDeactivate.Planned, membershipDeactivate.Changed, membershipDeactivate.Failed, membershipDeactivate.Truncated, membershipDeactivate.ScanFailed,
            rules.Sum(r => r.ClaimHeld), rules.Sum(r => r.AlreadyApplied), rules.Sum(r => r.MarkFailed),
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);
    }

    /// <summary>
    /// The shape every rule shares: page the scan, classify each row, log a before-state for every planned
    /// change, then — only when writes are enabled — apply the changes in transactional chunks.
    /// </summary>
    private async Task ReconcileAsync(
        IGenericEntityService entityService,
        IIdempotencyService idempotency,
        JobRunContext context,
        string mode,
        Func<PageCursor, string> fetchXml,
        Func<Entity, PlannedChange?> plan,
        IReadOnlyList<RuleCounts> rules,
        bool writesEnabled,
        CancellationToken ct)
    {
        var planned = new Dictionary<string, List<PlannedChange>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            planned[rule.Rule] = new List<PlannedChange>();
        }

        string? cookie = null;
        var truncated = true;

        for (var page = 1; page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            EntityCollection result;
            try
            {
                result = await entityService
                    .RetrieveMultipleAsync(new FetchExpression(fetchXml(new PageCursor(page, cookie))), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            // One rule's scan failing is recorded on that rule; the others still run.
            catch (Exception ex)
            {
                // The fail direction that matters here (ADR-003, inverted for a writer): this writes NOTHING,
                // and the rule is marked ScanFailed so the RUN is recorded failed rather than "nothing to do".
                foreach (var rule in rules)
                {
                    rule.ScanFailed = true;
                    rule.ScanError = ex.Message;
                }

                _logger.LogError(ex,
                    "[EXT-ACCESS-RECON] Scan failed on page {Page} for rule(s) {Rules}; nothing was written and the run is recorded FAILED. correlationId={CorrelationId}",
                    page, string.Join(",", rules.Select(r => r.Rule)), context.CorrelationId);
                return;
            }

            foreach (var row in result.Entities)
            {
                foreach (var rule in rules)
                {
                    rule.Scanned++;
                }

                var change = plan(row);
                if (change is null)
                {
                    continue;
                }

                planned[change.Rule].Add(change);
            }

            if (!result.MoreRecords)
            {
                truncated = false;
                break;
            }

            cookie = result.PagingCookie;
        }

        // The before-state, recorded for EVERY row this run would change, in BOTH modes and BEFORE anything
        // is written (owner decision D-2 part 3). This is the COMPLETE list; ResultJson carries a bounded
        // sample of the same ids.
        //
        // Emitted here — after the scan has completed — rather than per row as the pages arrive. A scan that
        // failed on page 3 returns above without reaching this point, so the report never contains "this row
        // would change" lines for a run that then reconciled nothing. The claim an operator reads and the
        // rows the write pass would act on are the same set.
        foreach (var rule in rules)
        {
            rule.Planned = planned[rule.Rule].Count;
            rule.Truncated = truncated;
            rule.SampleIds = planned[rule.Rule].Take(MaxSampledIdsPerRule).Select(c => c.RowId).ToArray();

            foreach (var change in planned[rule.Rule])
            {
                _logger.LogInformation(
                    "[EXT-ACCESS-RECON] before-state mode={Mode} rule={Rule} entity={Entity} rowId={RowId} before=[{Before}] after=[{After}] correlationId={CorrelationId}",
                    mode, change.Rule, change.EntityLogicalName, change.RowId, change.BeforeState, change.AfterState,
                    context.CorrelationId);
            }

            if (truncated)
            {
                _logger.LogError(
                    "[EXT-ACCESS-RECON] {Rule} still had rows after {MaxPages} pages of {PageSize} — the run reconciled a PREFIX, not the table. correlationId={CorrelationId}",
                    rule.Rule, MaxPages, PageSize, context.CorrelationId);
            }
        }

        if (!writesEnabled)
        {
            // Report-only: the counts and the before-state lines above are the whole output. Nothing is written.
            return;
        }

        foreach (var rule in rules)
        {
            await ApplyAsync(entityService, idempotency, rule, planned[rule.Rule], context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies one rule's planned changes in transactional chunks, each under an atomic claim with a
    /// completion marker (ADR-036 A1 rule 3). A chunk whose transaction faults leaves every row in it
    /// unchanged, is counted, and does not stop later chunks.
    /// </summary>
    private async Task ApplyAsync(
        IGenericEntityService entityService,
        IIdempotencyService idempotency,
        RuleCounts rule,
        List<PlannedChange> changes,
        JobRunContext context,
        CancellationToken ct)
    {
        foreach (var chunk in changes.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();

            var key = ClaimKey(rule.Rule, chunk.Select(c => c.RowId));

            if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
            {
                rule.AlreadyApplied += chunk.Length;
                continue;
            }

            if (!await idempotency.TryAcquireProcessingLockAsync(key, ChunkClaimDuration, ct).ConfigureAwait(false))
            {
                // Under the host's lease no other run should be applying this chunk, so a held claim with no
                // marker is a leftover from a release that failed. It expires on its own and the next run
                // re-plans the same rows — reconciliation is idempotent by construction.
                rule.ClaimHeld += chunk.Length;
                _logger.LogWarning(
                    "[EXT-ACCESS-RECON] {Rule}: a chunk of {Count} row(s) was not applied — its claim is still held. correlationId={CorrelationId}",
                    rule.Rule, chunk.Length, context.CorrelationId);
                continue;
            }

            try
            {
                // Check again UNDER the claim: the claim itself is check-then-set and fails open (#984).
                if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
                {
                    rule.AlreadyApplied += chunk.Length;
                    continue;
                }

                try
                {
                    await entityService.BulkUpdateAsync(
                        rule.EntityLogicalName,
                        chunk.Select(c => (c.RowId, c.Fields)).ToList(),
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
// One faulted chunk must not stop the rest; it rolled back whole (task 096).
                catch (Exception ex)
                {
                    rule.Failed += chunk.Length;
                    _logger.LogWarning(ex,
                        "[EXT-ACCESS-RECON] {Rule}: a chunk of {Count} row(s) failed and rolled back whole — every row in it is unchanged; later chunks still apply. correlationId={CorrelationId}",
                        rule.Rule, chunk.Length, context.CorrelationId);
                    continue;
                }

                rule.Changed += chunk.Length;

                // The rows are written: record it even if the host is stopping. IIdempotencyService swallows
                // its own write failures, so read the marker back — one that did not stick means a later run
                // may re-apply the chunk (harmless, because every rule is idempotent, but it is counted).
                bool marked;
                try
                {
                    await idempotency.MarkEventAsProcessedAsync(key, AppliedMarkerLifetime, CancellationToken.None).ConfigureAwait(false);
                    marked = await idempotency.IsEventProcessedAsync(key, CancellationToken.None).ConfigureAwait(false);
                }
// The marker is advisory; failing to write it never fails the applied chunk.
                catch (Exception ex)
                {
                    marked = false;
                    _logger.LogDebug(ex, "[EXT-ACCESS-RECON] Marker write for {Key} threw", key);
                }

                if (!marked)
                {
                    rule.MarkFailed += chunk.Length;
                }
            }
            finally
            {
                try
                {
                    await idempotency.ReleaseProcessingLockAsync(key, CancellationToken.None).ConfigureAwait(false);
                }
// A claim that cannot be released expires on its own.
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EXT-ACCESS-RECON] Could not release the claim on {Key}; it expires on its own", key);
                }
            }
        }
    }

    /// <summary>
    /// Classifies one grant row: R2 (its organization is inactive) beats R1 (it carries no expiry), and a row
    /// matching neither yields <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Re-decided IN CODE rather than trusted from the scan filter. The filter is a bound on how much comes
    /// back; the rule is what is applied. Keeping the two separate is what makes an over-broad filter a cost
    /// rather than an access change.
    /// </remarks>
    internal static PlannedChange? PlanGrantChange(Entity row, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Id == Guid.Empty || !IsActive(StateCodeOf(row)))
        {
            // A row with no usable id cannot be addressed by an update, and an already-inactive row is
            // already what the rules want it to be.
            return null;
        }

        // R2 FIRST. A row that is about to be deactivated must not also be stamped with an expiry: the write
        // would be wasted, and two updates for one id in one transaction is a shape worth never producing.
        var organization = row.GetAttributeValue<EntityReference>(OrganizationLookupAttribute);
        if (organization is { } org && org.Id != Guid.Empty)
        {
            var organizationState = Aliased(row, OrganizationAlias, "statecode");
            if (organizationState is OptionSetValue { Value: StateCodeInactive })
            {
                return new PlannedChange(
                    RuleDeactivateOrphanedOrgGrant,
                    ExternalGrantLifecycle.EntityLogicalName,
                    row.Id,
                    BeforeState: $"statecode={Describe(StateCodeOf(row))} organizationId={org.Id} organizationStatecode=1",
                    AfterState: $"statecode={StateCodeInactive} statuscode={StatusCodeInactive}",
                    Fields: DeactivationFields());
            }
        }

        // R1. The ONE definition of the 90-day default (ExternalGrantLifecycle.DefaultExpiry) — the same value
        // /grant writes, never a re-derived 90.
        if (row.GetAttributeValue<DateTime?>(ExpiresDateAttribute) is null)
        {
            var expiry = ExternalGrantLifecycle.DefaultExpiry(today);
            return new PlannedChange(
                RuleStampDefaultExpiry,
                ExternalGrantLifecycle.EntityLogicalName,
                row.Id,
                BeforeState: $"statecode={Describe(StateCodeOf(row))} expiresDate=(null)",
                AfterState: $"expiresDate={expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                Fields: new Dictionary<string, object>
                {
                    [ExpiresDateAttribute] = ExternalGrantLifecycle.ToSdkDateOnly(expiry),
                });
        }

        return null;
    }

    /// <summary>
    /// Classifies one membership row: deactivate when its end date has PASSED. Access holds THROUGH the end
    /// date, so today's date is not yet ended — the same boundary the read path's expiry predicate uses.
    /// </summary>
    internal static PlannedChange? PlanMembershipChange(Entity row, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Id == Guid.Empty || !IsActive(StateCodeOf(row)))
        {
            return null;
        }

        if (row.GetAttributeValue<DateTime?>(EndDateAttribute) is not { } endValue)
        {
            // A membership with no end date is open-ended, not ended. R3 says nothing about it.
            return null;
        }

        var endDate = DateOnly.FromDateTime(endValue);
        if (endDate >= today)
        {
            return null;
        }

        return new PlannedChange(
            RuleDeactivateEndedMembership,
            JunctionEntityLogicalName,
            row.Id,
            BeforeState: $"statecode={Describe(StateCodeOf(row))} endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            AfterState: $"statecode={StateCodeInactive} statuscode={StatusCodeInactive}",
            Fields: DeactivationFields());
    }

    /// <summary>
    /// The deactivation payload: <c>statecode</c> AND <c>statuscode</c>, always together — statecode alone
    /// leaves an inconsistent status reason.
    /// </summary>
    /// <remarks>
    /// <see cref="OptionSetValue"/>, not <see cref="int"/>. This is the SDK path:
    /// <c>BulkUpdateAsync</c> assigns each value straight onto an <see cref="Entity"/>, so a bare int is not
    /// the right type for a state/status attribute. (The bare ints in <c>ProjectClosureEndpoint</c> are the
    /// Web API path, where the payload is JSON.)
    /// </remarks>
    internal static Dictionary<string, object> DeactivationFields() => new()
    {
        ["statecode"] = new OptionSetValue(StateCodeInactive),
        ["statuscode"] = new OptionSetValue(StatusCodeInactive),
    };

    /// <summary>
    /// The grant scan: ACTIVE rows that could match R1 or R2, with the organization's own state joined in.
    /// </summary>
    /// <remarks>
    /// <para><b>Null statecode is ACTIVE</b> (<see cref="ExternalGrantRow.IsActive"/>: <c>StateCode is null
    /// or 0</c>). A bare <c>statecode eq 0</c> would silently EXCLUDE such a row from every rule, so the
    /// condition is a disjunction with an explicit null test.</para>
    /// <para><b>The second disjunction is a provable superset, not a guess.</b> R1 needs a row with no
    /// <c>sprk_expiresdate</c>; R2 needs a row with an <c>sprk_organization</c>. A row with a date AND no
    /// organization can match neither, so excluding it cannot change any outcome — and it is the common case,
    /// so the scan stays proportional to the drift rather than to the table. The rule itself is still decided
    /// in code (<see cref="PlanGrantChange"/>).</para>
    /// <para>The join is OUTER: a grant whose organization lookup is empty, or points at a row the join cannot
    /// resolve, must still come back — an inner join would drop exactly the contact-keyed grants R1 exists
    /// for. Ordered by the row id so paging is stable.</para>
    /// </remarks>
    internal static string BuildGrantScanFetchXml(int page, string? pagingCookie)
    {
        var entity = new XElement("entity", new XAttribute("name", ExternalGrantLifecycle.EntityLogicalName),
            Attributes("sprk_externalrecordaccessid", ExpiresDateAttribute, OrganizationLookupAttribute, "statecode"),
            new XElement("order", new XAttribute("attribute", "sprk_externalrecordaccessid")),
            new XElement("filter", new XAttribute("type", "and"),
                ActiveOrNullStateFilter(),
                new XElement("filter", new XAttribute("type", "or"),
                    Condition(ExpiresDateAttribute, "null"),
                    Condition(OrganizationLookupAttribute, "not-null"))),
            new XElement("link-entity",
                new XAttribute("name", OrganizationEntityLogicalName),
                new XAttribute("from", "sprk_organizationid"),
                new XAttribute("to", OrganizationLookupAttribute),
                new XAttribute("link-type", "outer"),
                new XAttribute("alias", OrganizationAlias),
                Attributes("statecode")));

        return Fetch(page, pagingCookie, entity);
    }

    /// <summary>
    /// The membership scan: ACTIVE <c>sprk_contactorganization</c> rows whose end date is BEFORE today.
    /// </summary>
    /// <remarks>
    /// <c>sprk_enddate</c> is <b>Date Only</b> in live metadata (re-verified 2026-09-21 by Dataverse MCP
    /// <c>describe</c>, which is why this task's "is it date-only?" escalation trigger did not fire), so it is
    /// compared as a bare <c>yyyy-MM-dd</c> with a strict <c>lt</c> — access holds THROUGH the end date. A row
    /// with a null end date does not match a comparison and is therefore not selected, which is the same
    /// answer <see cref="PlanMembershipChange"/> reaches independently.
    /// </remarks>
    internal static string BuildMembershipScanFetchXml(DateOnly today, int page, string? pagingCookie)
    {
        var entity = new XElement("entity", new XAttribute("name", JunctionEntityLogicalName),
            Attributes("sprk_contactorganizationid", EndDateAttribute, "statecode"),
            new XElement("order", new XAttribute("attribute", "sprk_contactorganizationid")),
            new XElement("filter", new XAttribute("type", "and"),
                ActiveOrNullStateFilter(),
                Condition(EndDateAttribute, "lt", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

        return Fetch(page, pagingCookie, entity);
    }

    /// <summary>The claim key for one chunk: its rule plus a content hash of the row ids it will write.</summary>
    /// <remarks>
    /// Keyed by CONTENT, not by date or page number. A retried attempt re-scans and re-chunks; any chunk whose
    /// rows were already written no longer matches the scan, and any chunk that is re-formed identically finds
    /// its own marker. Ids are sorted and formatted invariantly so the key cannot change with enumeration
    /// order or the server's culture.
    /// </remarks>
    internal static string ClaimKey(string rule, IEnumerable<Guid> rowIds)
    {
        var joined = string.Join(',', rowIds.OrderBy(id => id).Select(id => id.ToString("N", CultureInfo.InvariantCulture)));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        return $"external-access-reconciliation:{rule}:{digest}";
    }

    internal const string ExpiresDateAttribute = "sprk_expiresdate";
    internal const string EndDateAttribute = "sprk_enddate";

    private static string Fetch(int page, string? pagingCookie, XElement entity)
    {
        var fetch = new XElement("fetch",
            new XAttribute("version", "1.0"),
            new XAttribute("mapping", "logical"),
            new XAttribute("no-lock", "true"),
            new XAttribute("count", PageSize),
            new XAttribute("page", page));

        if (pagingCookie is not null)
        {
            fetch.Add(new XAttribute("paging-cookie", pagingCookie));
        }

        fetch.Add(entity);
        return fetch.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Active means <c>statecode = 0</c> OR <c>statecode</c> is null — never <c>eq 0</c> alone.</summary>
    private static XElement ActiveOrNullStateFilter()
        => new("filter", new XAttribute("type", "or"),
            Condition("statecode", "eq", "0"),
            Condition("statecode", "null"));

    private static IEnumerable<XElement> Attributes(params string[] names)
        => names.Select(name => new XElement("attribute", new XAttribute("name", name)));

    private static XElement Condition(string attribute, string op)
        => new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op));

    private static XElement Condition(string attribute, string op, string value)
        => new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op), new XAttribute("value", value));

    private static int? StateCodeOf(Entity row)
        => row.GetAttributeValue<OptionSetValue>("statecode")?.Value;

    /// <summary>A NULL statecode counts as ACTIVE — <see cref="ExternalGrantRow.IsActive"/>'s semantics.</summary>
    private static bool IsActive(int? stateCode) => stateCode is null or 0;

    private static string Describe(int? stateCode)
        => stateCode?.ToString(CultureInfo.InvariantCulture) ?? "(null)";

    private static object? Aliased(Entity row, string alias, string attribute)
        => row.Attributes.TryGetValue($"{alias}.{attribute}", out var value)
            ? value is AliasedValue aliased ? aliased.Value : value
            : null;

    /// <summary>One page request of a scan.</summary>
    internal readonly record struct PageCursor(int Page, string? Cookie);

    /// <summary>
    /// One row this run would change, with the before-state recorded alongside the write.
    /// </summary>
    internal sealed record PlannedChange(
        string Rule,
        string EntityLogicalName,
        Guid RowId,
        string BeforeState,
        string AfterState,
        Dictionary<string, object> Fields);

    /// <summary>Per-rule outcome, reported in the heartbeat and in <see cref="JobRunResult.ResultJson"/>.</summary>
    internal sealed class RuleCounts(string rule, string entityLogicalName)
    {
        public string Rule { get; } = rule;
        public string EntityLogicalName { get; } = entityLogicalName;

        public int Scanned;
        public int Planned;
        public int Changed;
        public int Failed;
        public int ClaimHeld;
        public int AlreadyApplied;
        public int MarkFailed;
        public bool Truncated;
        public bool ScanFailed;
        public string? ScanError;
        public IReadOnlyList<Guid> SampleIds = Array.Empty<Guid>();

        public object ToReport() => new
        {
            rule = Rule,
            entity = EntityLogicalName,
            Scanned,
            Planned,
            Changed,
            Failed,
            ClaimHeld,
            AlreadyApplied,
            MarkFailed,
            Truncated,
            ScanFailed,
            ScanError,
            sampleIds = SampleIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
            sampleTruncated = Planned > SampleIds.Count,
        };
    }
}
