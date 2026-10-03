using System.Text.Json;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 143 (GitHub #1066) — the No Access safety net: every 5 minutes, every ACTIVE
/// <c>sprk_noaccessentry</c> is enforced on the secure records it covers (owner Q4; round 3 R3/R4: "minutes, never
/// hourly"; the save-time call and the "Update Access" command are the fast paths).
/// </summary>
/// <remarks>
/// <para><b>What it catches that nothing else does.</b> An out-of-band share — the model-driven app's own Share
/// dialog, which every Collaborate/Full holder can use since task 139 — made AFTER an entry exists; a record that
/// became secure after the entry; a task-141 link that appeared after it; an entry authored where no save-time call
/// ran (an import, the Web API, a form without the script). Each run recomputes from current data through the ONE
/// enforcer (<see cref="NoAccessShareEnforcer"/>), so this job adds no rule of its own.</para>
///
/// <para><b>Posture</b> (owner R4 as answered in round 3: enabled, writes on, a safety net at 5 minutes or less).
/// Registered ENABLED and it REMOVES shares — unlike <see cref="ExternalAccessReconciliationJob"/>'s report-only
/// default (D-2 part 3), because the owner explicitly required the wall and set its exposure window. It never grants.
/// Every removal is bounded by the enforcer's own rules: secure records only, never a team or role share, never
/// unless the entry's author holds Write on the record (N5), never the last person who can see a secure record (S5).</para>
///
/// <para><b>Cache key.</b> The job has no caller, so a walled user's impersonated root-set cache is cleared under the
/// DEPLOYMENT's tenant (<see cref="ImpersonatedRootSetSource.DeploymentCacheTenant"/>) — the namespace that user's
/// own reads write — never <c>CacheTenantFor(null)</c> = "anonymous", which no read uses. Unconfigured, nothing is
/// cleared and a stale set lapses within <see cref="ImpersonatedRootSetSource.CacheTtl"/>; the run says so.</para>
///
/// <para><b>The ExternalAccessReconciliationJob A1 rules, applied.</b> A bounded scan (<see cref="MaxEntriesPerRun"/>,
/// one more reports TRUNCATED rather than reconciling a silent prefix); a failed scan or any entry the enforcer could
/// not complete records <c>Success=false</c> — never "nothing to reconcile"; a heartbeat line every attempt; and no
/// throw from <see cref="ExecuteAsync"/> (ADR-036 A1 rule 4: idempotent by construction — a removed share is not
/// there to remove next time — so the next tick picks up whatever this one could not). No chunk claims: every write
/// is a revoke decided from a strict read and confirmed by a read-back, which a second instance repeats as a no-op.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): the BFF's in-process <c>Spaarke.Scheduling</c> host, registered
/// with <c>AddScheduledJob</c> in <c>ExternalAccessModule</c> beside the identity-link job — BFF domain code (the
/// enforcer, the share seam), BFF-owned tables, BFF identity, low volume (B2/B3). Deliberately not an R4 inside
/// <see cref="ExternalAccessReconciliationJob"/>: that job's one reason to change is the lifecycle of grant and
/// membership rows, and it ships disabled.</para>
/// </remarks>
public sealed class NoAccessShareReconciliationJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "no-access-share-reconciliation";

    /// <summary>Every 5 minutes (owner round 3 R3/R4).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>The active entries one run enforces. One more reports TRUNCATED.</summary>
    internal const int MaxEntriesPerRun = 500;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    /// <summary>How many entry ids with problems the result lists (the logs carry every one).</summary>
    internal const int MaxSampledEntries = 50;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NoAccessShareReconciliationJob> _logger;

    public NoAccessShareReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<NoAccessShareReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "No Access Share Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Every 5 minutes, removes the direct shares an active No Access entry walls off on secure records — including " +
        "shares made outside the product after the entry — and reports access a wall cannot remove per user. Never " +
        "grants; never removes the last person who can see a secure record.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var status = StatusOk;
        var problems = new List<string>();
        var entries = 0;
        var removed = 0;
        var notEnforceable = 0;
        var notEnforced = 0;
        var incompleteEntries = new List<Guid>();
        var truncated = false;

        var tenant = ImpersonatedRootSetSource.DeploymentCacheTenant(_configuration);
        var cacheTenants = tenant is null ? Array.Empty<string>() : new[] { tenant };
        if (tenant is null)
        {
            problems.Add("AzureAd:TenantId is not configured, so walled users' cached root sets were not cleared; " +
                         $"they lapse within {ImpersonatedRootSetSource.CacheTtl.TotalMinutes:0} minutes.");
            _logger.LogWarning(
                "[NO-ACCESS-RECON] No deployment tenant configured (AzureAd:TenantId / TENANT_ID): removals will not " +
                "clear the walled users' cached root sets (never the 'anonymous' key).");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<NoAccessEnforcementStore>();
            var enforcer = scope.ServiceProvider.GetRequiredService<NoAccessShareEnforcer>();

            var (ids, scanTruncated) = await store.ReadActiveEntryIdsAsync(MaxEntriesPerRun, cancellationToken)
                .ConfigureAwait(false);
            truncated = scanTruncated;
            if (scanTruncated)
            {
                problems.Add($"TRUNCATED: more than {MaxEntriesPerRun} active entries; later entries were not enforced this run.");
            }

            foreach (var entryId in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report = await enforcer.EnforceEntryAsync(entryId, cacheTenants, cancellationToken).ConfigureAwait(false);
                entries++;
                removed += report.Removed.Count;
                notEnforceable += report.NotEnforceable.Count;
                notEnforced += report.NotEnforced.Count;
                truncated |= report.Truncated;

                // Inactive (deactivated between the scan and the read) and malformed entries wall nothing: that is
                // their answer, not a failure. Anything else short of complete is a failure.
                var terminalButFine = report.Outcome is NoAccessEnforcementOutcome.Inactive
                    or NoAccessEnforcementOutcome.Malformed or NoAccessEnforcementOutcome.NotFound;
                if (!report.Complete && !terminalButFine)
                {
                    incompleteEntries.Add(entryId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every entry was enforced.");
        }
        catch (Exception ex)
        {
            // The scan itself failed: recorded FAILED, never "nothing to reconcile" (ADR-003, inverted for a writer).
            status = StatusError;
            problems.Add($"The active No Access entries could not be read: {ex.Message}");
            _logger.LogError(ex,
                "[NO-ACCESS-RECON] Run failed — the entries could not be scanned; nothing was reconciled. " +
                "attempt={Attempt} correlationId={CorrelationId}", context.Attempt, context.CorrelationId);
        }

        if (incompleteEntries.Count > 0)
        {
            status = StatusError;
            problems.Add($"{incompleteEntries.Count} entr(y/ies) could not be fully enforced; see the per-entry errors.");
        }

        if (status == StatusOk && (truncated || problems.Count > 0))
        {
            status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT (ADR-036 A1 rule 5): one line on every attempt, whatever happened.
        _logger.LogInformation(
            "[NO-ACCESS-RECON] heartbeat status={Status} entries={Entries} removed={Removed} notEnforceable={NotEnforceable} " +
            "notEnforced={NotEnforced} incomplete={Incomplete} truncated={Truncated} cacheTenantConfigured={CacheTenant} " +
            "durationMs={DurationMs} attempt={Attempt} correlationId={CorrelationId}",
            status, entries, removed, notEnforceable, notEnforced, incompleteEntries.Count, truncated, tenant is not null,
            (long)duration.TotalMilliseconds, context.Attempt, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: removed,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    entries,
                    removed,
                    notEnforceable,
                    notEnforced,
                    truncated,
                    cacheTenantConfigured = tenant is not null,
                    incompleteEntries = incompleteEntries.Take(MaxSampledEntries).ToArray(),
                    incompleteTotal = incompleteEntries.Count,
                    attempt = context.Attempt,
                },
                ResultJsonOptions));
    }
}
