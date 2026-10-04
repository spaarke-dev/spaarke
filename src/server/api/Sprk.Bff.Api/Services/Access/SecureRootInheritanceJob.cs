using System.Text.Json;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 158 (owner round 6; R3/R4: "the background job is only a safety net, running at ≤5 min")
/// — finds every work assignment and project FILED UNDER a secure matter or project and makes it secure through
/// <see cref="SecureRootInheritance"/> (provisioning's own steps, for its creator), and gives every secure one its secure
/// parents' sharees.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The BFF's create and re-file paths secure a filed record inline, and provisioning secures
/// the records filed under the record it secures. A record filed OUTSIDE the BFF — a wizard's <c>Xrm.WebApi</c> create, a
/// form edit, an import, a flow — or one whose inline securing failed, is reached only here. It reads the DATA, not the
/// path that wrote it, so no writer can hide a filed record from it (POML escalation trigger: does not fire). It is also
/// where a parent's later share change (a model-driven-app Share, a <c>/share-user</c>) reaches its filed records.</para>
/// <para><b>Writes ON, enabled</b> (owner R3/R4: minutes, never hourly; round 6: such a record IS secure). Every write is
/// provisioning's own (the record's creator shared first, read back, compensated) or the synchronizer's add-only mirror —
/// nothing here takes a record out of isolation (round 6 item 4: never auto-unsecure).</para>
/// <para><b>ADR-036 A1.</b> Rule 3: each record's step is idempotent and read back (an isolated record is only given a
/// missing sharee), so no claim marker. Rule 4: a run that could not LIST the secure parents or the filed records throws —
/// a failed scan is a failed run, nothing decided; a run in which some record could not be secured returns
/// <c>Success = false</c> and the next run revisits it. Rule 5: one heartbeat per attempt. Rule 6: registered through
/// <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>. Rule 7: no dependency on the scheduler's store.</para>
/// <para><b>Bounded.</b> At most <see cref="MaxProvisioningsPerRun"/> records are PROVISIONED per run (each creates an SPE
/// container); the rest stay candidates and are reached by the next run, oldest-listed first. Isolated records only have
/// their sharees checked, which every run does for all of them.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — BFF identity, the same
/// inheritance the endpoints call (L1 and L4 run ONE invariant owner), low volume (the filed records of an environment's
/// secure records). No package, store, column or interface.</para>
/// </remarks>
public sealed class SecureRootInheritanceJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "secure-root-inheritance";

    /// <summary>Every 5 minutes (owner R3/R4: the safety net runs at ≤ 5 minutes).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>Provisionings per run (each creates a container); the rest wait for the next run.</summary>
    internal const int MaxProvisioningsPerRun = 25;

    /// <summary>Records listed in <c>ResultJson</c> (the per-record log lines are complete).</summary>
    internal const int MaxSampledRecords = 200;

    private const int PageSize = 5000;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecureRootInheritanceJob> _logger;

    public SecureRootInheritanceJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SecureRootInheritanceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Secure Root Inheritance";

    /// <inheritdoc />
    public string Description =>
        "Makes every work assignment and project filed under a secure matter or project secure itself (its own owner team, " +
        "container and creator share), and gives each its secure parents' sharees (task 158, owner round 6).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();

        using var scope = _scopeFactory.CreateScope();
        var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();

        // A scan that cannot complete decides nothing: ADR-036 A1 rule 4 — it throws, and the run is a failed run.
        var parents = await ListSecureParentsAsync(dataverse, cancellationToken).ConfigureAwait(false);
        var filed = parents.Count == 0
            ? Array.Empty<FiledRootRef>()
            : await inheritance.ListFiledRootsAsync(parents, cancellationToken).ConfigureAwait(false);

        var counts = new Dictionary<SecureRootInheritOutcome, int>();
        var incomplete = new List<string>();
        var sampled = new List<object>();
        var provisionings = 0;
        var deferred = 0;
        var sharesWritten = 0;
        var traceId = $"job:{context.RunId}";

        // Not-yet-secure records first (they need provisioning), then the secure ones (their sharees).
        foreach (var record in filed.OrderBy(r => r.FlaggedSecure).ThenBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id))
        {
            if (!record.FlaggedSecure && provisionings >= MaxProvisioningsPerRun)
            {
                deferred++;
                continue;
            }

            var result = await inheritance.SecureIfFiledUnderSecureAsync(record.Table, record.Id, traceId, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome is SecureRootInheritOutcome.Secured or SecureRootInheritOutcome.Refused or SecureRootInheritOutcome.Failed)
                provisionings++;

            counts[result.Outcome] = counts.TryGetValue(result.Outcome, out var n) ? n + 1 : 1;
            if (result.Shares is { } shares)
                sharesWritten += shares.SharesGranted + shares.SharesChanged;

            _logger.LogInformation(
                "[SECURE-INHERIT] run={RunId} {Table} {RecordId}: {Outcome} {Code} {Detail}",
                context.RunId, record.Table, record.Id, result.WireOutcome, result.ReasonCode, result.Detail);

            if (!result.IsComplete)
                incomplete.Add($"{record.Table}:{record.Id:D}: {result.WireOutcome} ({result.ReasonCode})");

            if (result.Outcome != SecureRootInheritOutcome.NotFiledUnderSecure && sampled.Count < MaxSampledRecords)
            {
                sampled.Add(new
                {
                    table = record.Table,
                    id = record.Id,
                    outcome = result.WireOutcome,
                    reasonCode = result.IsComplete ? null : result.ReasonCode,
                    sharesWritten = result.Shares is { } s ? s.SharesGranted + s.SharesChanged : 0,
                });
            }
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var success = incomplete.Count == 0;
        int Count(SecureRootInheritOutcome outcome) => counts.TryGetValue(outcome, out var n) ? n : 0;

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            success ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-INHERIT] heartbeat secureParents={Parents} filed={Filed} secured={Secured} alreadySecure={Already} " +
            "unverifiable={Unverifiable} refused={Refused} failed={Failed} deferred={Deferred} sharesWritten={Shares} " +
            "attempt={Attempt} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            parents.Count, filed.Count, Count(SecureRootInheritOutcome.Secured), Count(SecureRootInheritOutcome.AlreadySecure),
            Count(SecureRootInheritOutcome.Unverifiable), Count(SecureRootInheritOutcome.Refused),
            Count(SecureRootInheritOutcome.Failed), deferred, sharesWritten, context.Attempt, (long)duration.TotalMilliseconds,
            context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: success,
            ErrorMessage: success
                ? null
                : $"{incomplete.Count} filed record(s) not secure yet (or not given their parents' sharees); the next run " +
                  "revisits them: " + string.Join("; ", incomplete.Take(20)),
            ProcessedItems: filed.Count - deferred,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                secureParents = parents.Count,
                filed = filed.Count,
                secured = Count(SecureRootInheritOutcome.Secured),
                alreadySecure = Count(SecureRootInheritOutcome.AlreadySecure),
                notFiledUnderSecure = Count(SecureRootInheritOutcome.NotFiledUnderSecure),
                unverifiable = Count(SecureRootInheritOutcome.Unverifiable),
                refused = Count(SecureRootInheritOutcome.Refused),
                failed = Count(SecureRootInheritOutcome.Failed),
                deferred,
                sharesWritten,
                incomplete = incomplete.Take(50).ToArray(),
                records = sampled,
                attempt = context.Attempt,
            }, ResultJsonOptions));
    }

    /// <summary>
    /// Every matter and project flagged <c>sprk_issecure = true</c>, paged; past the page ceiling it throws rather than
    /// decide on part of them.
    /// </summary>
    private static async Task<List<(string Table, Guid Id)>> ListSecureParentsAsync(
        IGenericEntityService dataverse, CancellationToken ct)
    {
        var parents = new List<(string, Guid)>();
        foreach (var table in new[] { SecureRootInheritance.Matter, SecureRootInheritance.Project })
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(table + "id"),
                NoLock = true,
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
            };
            query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Equal, true);

            for (var page = 1; ; page++)
            {
                if (page > MaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had secure records to list after {MaxPages} pages; nothing is decided on part of them.");

                var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                parents.AddRange(result.Entities.Select(e => (table, e.Id)));
                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        return parents.Distinct().ToList();
    }
}
