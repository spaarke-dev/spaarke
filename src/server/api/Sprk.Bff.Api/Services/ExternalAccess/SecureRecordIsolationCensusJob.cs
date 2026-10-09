using System.Text.Json;
using Spaarke.Contracts.Provisioning;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 144 (C10 part 1, #967; owner decision F2 = a) — a READ-ONLY scheduled census of the
/// Secure Record business unit that logs a CRITICAL finding for every way a secure record has become readable.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Provisioning checks the named owner team is memberless and the Secure Record business
/// unit user-free (<see cref="SecureRecordOwnerTeam"/>) — but only when something is provisioned. An administrator can
/// Change-BU a user into the Secure Record BU, add a member to the owner team, or widen a role's depth at any moment,
/// and no BFF code can block that (no plugins, ADR-002 / owner D-1). Each exposes every EXISTING secure record. Before
/// this job the only detector was the manual NFR-05 run; this narrows the window to one schedule interval.</para>
///
/// <para><b>What it checks</b> — exactly the NFR-05 standing assertion, through the SAME evaluator and census
/// composition the manual gate uses (<see cref="SecureBuRoleDepthAssertion"/>,
/// <see cref="SecureBuRoleDepthCensusBuilder"/>), read through <see cref="SecureRecordIsolationCensus"/> — the one census
/// the provisioning acceptance route also runs (task 260): role-depth reach into the secure BU (clause 1); the named owner team
/// resolves and has no members of any kind (2); the owner role is held by that team alone (3); the BU holds no
/// systemusers of any kind (4); the owner role holds Read at User depth on every table of the codified set,
/// <c>config/secure-record-owner-role.json</c> (5, task 145). Every exposure finding is logged at
/// <see cref="LogLevel.Critical"/>, one line each, naming the principal; a clause-5 gap is logged at
/// <see cref="LogLevel.Error"/>, naming the table — it makes secure-child writes fail closed, it exposes nothing.</para>
///
/// <para><b>What it never does.</b> It writes nothing to Dataverse, moves nobody, strips no role (relocating users is
/// an owner decision) and touches no CI. The run result carries the findings for <c>/api/admin/jobs/{id}/status</c>.</para>
///
/// <para><b>ADR-036 A1.</b> Rule 3 (idempotency) has no unit of work to claim — it has no side effect. Rule 4 (retry):
/// a census READ failure throws after the heartbeat, because a retry this tick can still produce the answer; findings do
/// not throw (a retry would find them again) — they return <c>Success = false</c> with the findings in
/// <c>ErrorMessage</c>. Rule 5: one heartbeat per attempt, including a clean one. Rule 6: registered through
/// <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF, on the in-process <c>Spaarke.Scheduling</c> host — the
/// "BFF, schedule" placement owner decision F2 names. Low volume (nine paged reads per run), BFF identity, BFF domain
/// code (the same evaluator provisioning's invariants come from), and the one-dispatch-per-schedule need is met by the
/// scheduler's lease. No new package, scheduler, store or interface.</para>
/// </remarks>
public sealed class SecureRecordIsolationCensusJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "secure-record-isolation-census";

    /// <summary>Every 15 minutes. Short, because each run bounds how long a Change-BU into the secure BU goes unseen.</summary>
    internal const string DefaultCronSchedule = "*/15 * * * *";

    // Paging (PageSize / MaxPages), the census read and the status mapping live in SecureRecordIsolationCensus (task
    // 260): the provisioning acceptance route runs the same census synchronously.
    internal const string StatusIsolated = KeylessProofContract.SecureRecordIsolationCensus.Isolated;
    internal const string StatusError = KeylessProofContract.SecureRecordIsolationCensus.Error;

    // The Dataverse seam is resolved per run from a scope, as the sibling jobs do (GrantExpiryReminderJob,
    // ExternalAccessReconciliationJob): constructing the job — which the scheduler's registry does at startup — then
    // needs nothing beyond the framework, and the job does not pin a Dataverse client for the host's lifetime.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecureRecordIsolationCensusJob> _logger;

    public SecureRecordIsolationCensusJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<SecureRecordIsolationCensusJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Secure Record Isolation Census";

    /// <inheritdoc />
    public string Description =>
        "Read-only census of the Secure Record business unit (spec NFR-05): no role reaches it by depth, it holds no " +
        "users, its named owner team resolves and has no members, only that team holds the owner role, and the role " +
        "covers every table in config/secure-record-owner-role.json. Logs a CRITICAL line per exposure and an ERROR " +
        "line per coverage gap. Writes nothing.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        SecureBuAssertionOutcome? outcome = null;
        Exception? readFailure = null;
        var status = StatusError;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            outcome = await SecureRecordIsolationCensus.EvaluateAsync(dataverse, _configuration, cancellationToken)
                .ConfigureAwait(false);
            status = SecureRecordIsolationCensus.StatusOf(outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            readFailure = ex;
            _logger.LogError(ex,
                "[SECURE-CENSUS] The census could not be read, so isolation of secure records is UNKNOWN this run — " +
                "not reported as clean. attempt={Attempt} correlationId={CorrelationId}",
                context.Attempt, context.CorrelationId);
        }

        if (outcome is not null)
        {
            foreach (var finding in outcome.Findings)
            {
                if (finding.Verdict == SecureBuVerdict.SecureBusinessUnitNotFound)
                {
                    // Inert, not an exposure: there is no secure BU to protect. Loud, but not CRITICAL.
                    _logger.LogWarning("[SECURE-CENSUS] {Verdict}: {Message} correlationId={CorrelationId}",
                        finding.Verdict, finding.Message, context.CorrelationId);
                    continue;
                }

                if (finding.Verdict == SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege)
                {
                    // Task 145: a coverage gap makes secure-child writes FAIL CLOSED — an availability defect, not a
                    // disclosure. Error, not CRITICAL, so the exposure lines stay the ones that page someone.
                    _logger.LogError("[SECURE-CENSUS] {Verdict}: {Message} correlationId={CorrelationId}",
                        finding.Verdict, finding.Message, context.CorrelationId);
                    continue;
                }

                _logger.LogCritical(
                    "[SECURE-CENSUS] {Verdict}: {Message} correlationId={CorrelationId}",
                    finding.Verdict, finding.Message, context.CorrelationId);
            }
        }

        var findingCount = outcome?.Findings.Count ?? 0;
        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT (ADR-036 A1 rule 5). Every attempt, including a clean one — "isolated" and "the job died" must
        // not look alike.
        _logger.Log(
            status == StatusIsolated ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CENSUS] heartbeat status={Status} verdict={Verdict} findings={Findings} attempt={Attempt} " +
            "durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            status, outcome?.Verdict.ToString() ?? "unknown", findingCount, context.Attempt,
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        if (readFailure is not null)
        {
            // ADR-036 A1 rule 4: a retry this tick can still produce the answer, so fail the attempt.
            throw new InvalidOperationException(
                "The Secure Record isolation census could not be read; isolation is unknown this run.", readFailure);
        }

        return new JobRunResult(
            Success: status == StatusIsolated,
            ErrorMessage: status == StatusIsolated ? null : outcome!.Message,
            ProcessedItems: findingCount,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                status,
                verdict = outcome!.Verdict.ToString(),
                findings = outcome.Findings.Select(f => new { verdict = f.Verdict.ToString(), message = f.Message }),
                attempt = context.Attempt
            }));
    }
}
