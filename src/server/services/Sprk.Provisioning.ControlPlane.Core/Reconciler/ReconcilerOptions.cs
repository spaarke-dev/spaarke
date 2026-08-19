// -----------------------------------------------------------------------------
// ReconcilerOptions.cs
//
// L2 CONTROL-PLANE state-reconciler configuration (task 058, Wave C5).
//
// Bound from IConfiguration section "Reconciler" via
// <see cref="ReconcilerModule.AddReconcilerModule"/>. Defaults chosen to match
// design.md §4.2 v3.2 handler-execution model ("polls Cosmos every 5 seconds").
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only; NO BFF references; NO AI-internal
// injection. The reconciler is orchestration infrastructure — the ADR-004
// Path A exception applies here (spec.md ADR Tensions row 1 / CLAUDE.md §6.5).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Bound options for <see cref="StateReconcilerService"/>. Section name =
/// <c>Reconciler</c>. Defaults align with design.md §4.2 v3.2 (5-second poll).
/// </summary>
public sealed class ReconcilerOptions
{
    /// <summary>Configuration section name (bound via <see cref="ReconcilerModule.AddReconcilerModule"/>).</summary>
    public const string SectionName = "Reconciler";

    /// <summary>
    /// Interval between reconciler ticks. Default 5 seconds per design.md
    /// §4.2 v3.2 ("polls Cosmos every 5 seconds"). Enforced &gt;= 1 second at
    /// startup so a misconfiguration cannot melt Cosmos with sub-second polling.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Kill-switch for the reconciler ExecuteAsync loop. Defaults to <c>true</c>
    /// (reconciler runs). Set <c>false</c> to register the hosted service
    /// (satisfying DI graph shape + integration-test composition) while
    /// suppressing the actual polling loop. Test-only escape hatch —
    /// production deployments leave this <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Startup validation applied by <see cref="ReconcilerModule"/>. Throws
    /// <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured L2 App Service fails fast at boot (NFR-05 parity).
    /// </summary>
    internal void Validate()
    {
        if (PollInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:PollInterval' must be >= 1 second (actual: {PollInterval}). " +
                "Sub-second polling would overwhelm Cosmos RU budget for orchestration reads.");
        }
    }
}
