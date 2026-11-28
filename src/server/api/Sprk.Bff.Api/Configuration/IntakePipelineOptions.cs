namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Kill switches of the intake pipeline (spaarke-intake-pipeline-r1 NFR-08, ADR-032), bound from the
/// <c>IntakePipeline</c> section and read through <c>IOptionsMonitor</c> on every use, so an App Service setting change
/// takes effect without a redeploy (<c>IntakePipeline__Harnesses__nda=false</c>).
/// </summary>
/// <remarks>
/// <para><b>Absent means enabled.</b> A door or harness is off only when its entry says <c>false</c>; the switches exist
/// to stop one thing quickly, not to opt each new harness in.</para>
/// <para><b>Harness off</b>: <c>IntakeRunJobHandler</c> holds the run with admin-attention text before it calls the
/// harness (never Failed, never Completed); re-enabling and re-driving resumes it from its stored state.
/// <b>Door off</b>: enforced by each door (Front Door 021, Console 022, email 040, Copilot 044) with
/// <see cref="IsDoorEnabled"/> before it creates a request; the orchestrator does not re-check it, because a run that was
/// already accepted finishes.</para>
/// <para>§11: <i>Existing</i> - <c>EbillingOptions.Enabled</c> is one flag for one pipeline; the per-door and per-harness
/// split is the NFR-08 requirement. <i>Extension</i> - no existing options class covers intake. <i>Cost of doing
/// nothing</i> - a misbehaving harness or door could only be stopped by a redeploy (NFR-08 fails).</para>
/// </remarks>
public sealed class IntakePipelineOptions
{
    public const string SectionName = "IntakePipeline";

    /// <summary>
    /// Per-door switches, keyed by the door name (<c>FrontDoor</c>, <c>Console</c>, <c>Email</c>, <c>Copilot</c>),
    /// matched case-insensitively. Absent → enabled.
    /// </summary>
    public Dictionary<string, bool> Doors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-harness switches, keyed by the harness key (<c>nda</c>), matched case-insensitively. Absent → enabled.</summary>
    public Dictionary<string, bool> Harnesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How often, in seconds, a delivery refreshes its run claim while it waits on a long step (the AI call, a harness step,
    /// the persist, the door return), so a redelivery of the same message takes the run over only after the holder fell silent
    /// (task 013 review F1). Bounded by <c>IntakeRunStore.ClaimHeartbeat</c> to 5 s through a third of the same-job takeover
    /// age (60 s, so at most 20 s): a refresh can miss twice before a takeover is allowed. Absent or not positive: 20.
    /// </summary>
    public int ClaimHeartbeatSeconds { get; set; } = 20;

    /// <summary>True unless <see cref="Doors"/> sets <paramref name="door"/> to false.</summary>
    public bool IsDoorEnabled(string door) => IsEnabled(Doors, door);

    /// <summary>True unless <see cref="Harnesses"/> sets <paramref name="harnessKey"/> to false.</summary>
    public bool IsHarnessEnabled(string harnessKey) => IsEnabled(Harnesses, harnessKey);

    // Configuration binding can replace the dictionaries with ordinal (case-sensitive) ones, so match by hand.
    private static bool IsEnabled(Dictionary<string, bool> switches, string key)
    {
        foreach (var (name, enabled) in switches)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return enabled;
            }
        }

        return true;
    }
}
