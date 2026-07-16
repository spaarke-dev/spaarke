namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Options for the membership-reconcile periodic sweep (messaging-communication-app-r1 task 041 / FR-07).
/// Bound from configuration section <c>Communication:MembershipReconcile</c>.
/// </summary>
public sealed class MembershipReconcileOptions
{
    public const string SectionName = "Communication:MembershipReconcile";

    /// <summary>
    /// Operational kill-switch for the periodic sweep hosted service. Default <c>false</c> — the sweep is a
    /// backstop; event-driven reconcile (task 042/043 triggers) is the primary path and works without the
    /// sweep. Flip to <c>true</c> (no redeploy — bound via IOptions) to enable the eventual-consistency safety
    /// net. Enqueueing (event-driven) is always available regardless of this flag.
    /// </summary>
    public bool SweepEnabled { get; set; }

    /// <summary>How often the sweep runs. Default 15 minutes (design §8.4 eventual-consistency window).</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Initial delay before the first sweep after startup (lets the app warm up). Default 2 minutes.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Max messaging threads enqueued per sweep pass (bounds a single pass). Default 500.</summary>
    public int MaxThreadsPerSweep { get; set; } = 500;
}
