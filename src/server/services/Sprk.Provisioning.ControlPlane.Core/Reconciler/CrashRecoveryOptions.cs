// -----------------------------------------------------------------------------
// CrashRecoveryOptions.cs
//
// L2 CONTROL-PLANE crash-recovery startup scan configuration (task 060, Wave C5).
//
// Bound from IConfiguration section "CrashRecovery" via Program.cs. Defaults
// chosen to match spec.md FR-23 + design.md §4.2 v3 (I6 crash recovery):
// "On startup, L2 scans Cosmos for status ∈ {Running, WaitingOnGate} runs
// older than 2× median-handler-duration."
//
// SCOPE:
//   Options only. The service itself (CrashRecoveryStartupService) uses these
//   to compute the orphan-age threshold as
//       threshold = MAX( 2× MedianHandlerDuration, FloorAge )
//   The FLOOR prevents zero-duration recovery loops in cold environments where
//   median telemetry is not yet available.
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only; NO BFF references; NO AI-internal
// injection. The crash-recovery service is orchestration infrastructure — the
// ADR-004 Path A exception at L2 scope applies (spec.md ADR Tensions row 1 /
// CLAUDE.md §6.5).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Bound options for <see cref="CrashRecoveryStartupService"/>. Section name =
/// <c>CrashRecovery</c>. Defaults align with spec.md FR-23 + design.md §4.2 v3
/// (I6 crash recovery — scan orphans older than 2× median-handler-duration).
/// </summary>
public sealed class CrashRecoveryOptions
{
    /// <summary>Configuration section name (bound via Program.cs).</summary>
    public const string SectionName = "CrashRecovery";

    /// <summary>
    /// Kill-switch for the startup scan. Defaults to <c>true</c> (scan runs on
    /// every L2 boot). Set <c>false</c> to register the hosted service
    /// (satisfying DI graph shape + integration-test composition) while
    /// suppressing the actual scan. Test-only escape hatch — production
    /// deployments leave this <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum orphan age required BEFORE a run is considered stranded and
    /// eligible for re-enqueue. Prevents a zero-duration recovery loop in cold
    /// environments where <see cref="MedianHandlerDuration"/> would otherwise
    /// compute a sub-second threshold. Default 5 minutes per the task 060 POML
    /// (FR-23 doesn't pin an absolute floor; 5 min is the conservative default
    /// aligned with the reconciler's 5s cadence — no run can be legitimately
    /// mid-handler AND appear untouched for 5 min unless the L2 instance
    /// crashed or was slot-swapped).
    /// </summary>
    public TimeSpan FloorAge { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Median handler duration used for the "2× median" clause in FR-23. In
    /// v3.2 there is no live telemetry feed into this option (handler duration
    /// is not yet surfaced from App Insights back into L2 configuration); the
    /// value is operator-configurable via <c>CrashRecovery:MedianHandlerDuration</c>
    /// and defaults to 2 minutes. The effective threshold is
    /// <c>MAX(2× MedianHandlerDuration, FloorAge)</c> — so the default pair
    /// (2 min × 2 = 4 min vs 5 min floor) resolves to the 5-minute floor,
    /// which the spec allows without additional tuning.
    /// </summary>
    public TimeSpan MedianHandlerDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Startup validation applied by Program.cs post-configure. Throws
    /// <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured L2 App Service fails fast at boot (NFR-05 parity).
    /// </summary>
    internal void Validate()
    {
        if (FloorAge < TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:FloorAge' must be >= 30 seconds (actual: {FloorAge}). " +
                "A sub-30s floor risks re-enqueueing runs that are still legitimately mid-handler; " +
                "the FR-23 crash-recovery scan is a safety net, not a heartbeat.");
        }

        if (MedianHandlerDuration < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:MedianHandlerDuration' must be >= 1 second (actual: {MedianHandlerDuration}). " +
                "A zero or negative median would collapse the 2× multiplier and defeat the FR-23 age filter.");
        }
    }
}
