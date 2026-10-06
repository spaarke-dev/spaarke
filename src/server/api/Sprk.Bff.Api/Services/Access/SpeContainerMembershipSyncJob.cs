using System.Text.Json;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 171 (owner rounds 69 + 70) — every 5 minutes, keeps SharePoint Embedded container ROLES
/// in line with Dataverse through <see cref="SpeContainerMembershipSync"/>: standing writers on business-unit containers
/// (internal users of the unit) and removal of just-in-time Office-edit grants on secure containers once their holder has
/// lost Write.
/// </summary>
/// <remarks>
/// <para><b>Posture.</b> Registered ENABLED with writes: round 70 makes the standing sync THE mechanism (it replaces
/// hand-adding users and the self-registration Step 8), and round 69 requires the JIT grant to go when Write goes. Every
/// write is bounded by the marked-grant rule: only roles this code created are ever removed; owner and hand-granted roles
/// are never touched. Disable without a redeploy through <c>POST /api/admin/jobs/spe-container-membership-sync/disable</c>.</para>
/// <para><b>ADR-036 A1.</b> One dispatch per tick (the host's lease). Idempotent by construction (A1-3): a grant for a user
/// who already holds a role answers 409 and records nothing; a removal deletes only the permission its marker names.
/// Retry (A1-4): <see cref="ExecuteAsync"/> THROWS only when neither pass could start (the business-unit list and the
/// securable catalog both unreadable) — no progress was possible; otherwise per-container failures are counted, the run
/// reports <c>Success=false</c>, and the next tick revisits them. Heartbeat (A1-5): one structured line per attempt.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): the BFF's in-process <c>Spaarke.Scheduling</c> host, beside the other
/// access reconciliation jobs — BFF domain code (the marked-grant primitives, the Dataverse reads), the BFF identity, low
/// volume (B2/B3). Not an extension of an existing job: every existing reconciliation job keeps Dataverse SHARES; this
/// one's single reason to change is SPE container roles.</para>
/// </remarks>
public sealed class SpeContainerMembershipSyncJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "spe-container-membership-sync";

    /// <summary>Every 5 minutes — the project's access-reconciliation cadence (owner round 3 R3/R4: minutes, never hourly).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SpeContainerMembershipSyncJob> _logger;

    public SpeContainerMembershipSyncJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SpeContainerMembershipSyncJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "SPE Container Membership Sync";

    /// <inheritdoc />
    public string Description =>
        "Every 5 minutes, keeps internal users as standing writers on their business unit's SharePoint Embedded container, " +
        "and removes just-in-time Office-edit grants on secure containers once the holder no longer has Write. Only roles " +
        "this job (or the Office edit-open) created are ever removed.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();

        using var scope = _scopeFactory.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<SpeContainerMembershipSync>();

        SpeContainerMembershipSync.StandingResult? standing = null;
        SpeContainerMembershipSync.JitResult? jit = null;
        Exception? standingFault = null, jitFault = null;

        try
        {
            standing = await sync.SyncStandingWritersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            standingFault = ex;
            _logger.LogError(ex, "[SPE-MEMBERSHIP-SYNC] The standing-writer pass could not start.");
        }

        try
        {
            jit = await sync.RemoveRevokedJitGrantsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            jitFault = ex;
            _logger.LogError(ex, "[SPE-MEMBERSHIP-SYNC] The just-in-time removal pass could not start.");
        }

        var duration = _timeProvider.GetElapsedTime(started);
        _logger.LogInformation(
            "[SPE-MEMBERSHIP-SYNC] heartbeat attempt={Attempt} standing={{containers={Containers}, granted={Granted}, alreadyHeld={AlreadyHeld}, "
            + "removed={StandingRemoved}, unknown={StandingUnknown}, failed={StandingFailed}, capped={Capped}}} jit={{secureContainers={SecureContainers}, "
            + "grants={JitSeen}, removed={JitRemoved}, kept={JitKept}, unknown={JitUnknown}, failed={JitFailed}, truncated={Truncated}}} durationMs={DurationMs}",
            context.Attempt,
            standing?.Containers, standing?.Granted, standing?.AlreadyHeld, standing?.Removed, standing?.Unknown, standing?.Failed, standing?.Capped,
            jit?.SecureContainers, jit?.GrantsSeen, jit?.Removed, jit?.Kept, jit?.Unknown, jit?.Failed, jit?.Truncated,
            (long)duration.TotalMilliseconds);

        if (standing is null && jit is null)
        {
            // Neither pass could read its starting set: nothing was done, so a retry may complete it (A1-4).
            throw new InvalidOperationException(
                "SPE container membership sync could not start either pass.",
                new AggregateException(new[] { standingFault, jitFault }.OfType<Exception>()));
        }

        var success = standingFault is null && jitFault is null
                      && (standing?.Failed ?? 0) == 0 && (jit?.Failed ?? 0) == 0
                      && standing?.Capped != true && jit?.Truncated != true;
        var resultJson = JsonSerializer.Serialize(new
        {
            standing,
            standingFault = standingFault?.GetType().Name,
            jit,
            jitFault = jitFault?.GetType().Name,
        }, ResultJsonOptions);

        return new JobRunResult(
            Success: success,
            ErrorMessage: success ? null : "Some containers could not be synced, or a pass was capped/truncated — see ResultJson; the next run retries.",
            ProcessedItems: (standing?.Granted ?? 0) + (standing?.Removed ?? 0) + (jit?.Removed ?? 0),
            Duration: duration,
            ResultJson: resultJson);
    }
}
