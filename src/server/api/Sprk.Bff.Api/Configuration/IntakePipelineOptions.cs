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

    /// <summary>The engine job's plumbing (task 014): its request queue, the staging container and the hard timeout.</summary>
    public IntakeEngineOptions Engine { get; set; } = new();

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

/// <summary>
/// How the BFF reaches the intake engine job (spaarke-intake-pipeline-r1 task 014, FR-01, FR-07, NFR-03, NFR-05), bound from
/// <c>IntakePipeline:Engine</c>. The completion queue is <c>ServiceBus:IntakeEngineCompletionQueueName</c>: the engine sends there
/// and the BFF consumes it, so it is a Service Bus setting, not an intake one.
/// </summary>
/// <remarks>
/// <para><b>Not configured is a state, not a DI failure.</b> An empty <see cref="StagingBlobEndpoint"/> means the stamp has no
/// engine (<c>deployIntakeEngine=false</c>): a dispatch holds the run with an admin-attention code and sends nothing.</para>
/// <para>§11: <i>Existing</i> - <c>FinanceFileStoreOptions</c> (a blob endpoint for one store) and <c>ServiceBusOptions</c> (queue
/// names). <i>Extension</i> - neither names the engine's staging container, its request queue or its timeout, and they belong with
/// the intake kill switches. <i>Cost of doing nothing</i> - the dispatcher would hard-code names that differ per stamp.</para>
/// </remarks>
public sealed class IntakeEngineOptions
{
    /// <summary>The queue the engine job consumes requests from (task 002). App setting <c>IntakePipeline__Engine__RequestQueueName</c>.</summary>
    public string RequestQueueName { get; set; } = "intake-engine-requests";

    /// <summary>
    /// The stamp's blob endpoint holding the staging container, e.g. <c>https://{account}.blob.core.windows.net</c>: a bare https
    /// endpoint, never a connection string, key or SAS (managed identity only, ADR-028). Empty: no engine on this stamp.
    /// </summary>
    public string StagingBlobEndpoint { get; set; } = string.Empty;

    /// <summary>The private staging container (task 002; a 7-day lifecycle rule deletes its blobs).</summary>
    public string StagingContainer { get; set; } = "intake-staging";

    /// <summary>
    /// The hard timeout in minutes: how long a dispatched step may wait for its completion before the run is failed ("engine
    /// timeout", NFR-05). Never below <see cref="MinimumTimeout"/> (the engine job's own retry window plus a minute: a shorter
    /// timeout fails runs the job is still retrying). Not positive: <see cref="DefaultTimeoutMinutes"/>.
    /// </summary>
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;

    /// <summary>
    /// 30: a normal pass takes about two minutes; this leaves room for one engine-job timeout (10 minutes), the first back-off
    /// (1 minute) and a second full run.
    /// </summary>
    public const int DefaultTimeoutMinutes = 30;

    /// <summary>The floor of <see cref="TimeoutMinutes"/>: <c>JobContract.RetryWindow</c> (the engine job's 1+2+4+8 minute back-off) plus one minute.</summary>
    public static TimeSpan MinimumTimeout => Sprk.Bff.Api.Services.Jobs.JobContract.RetryWindow + TimeSpan.FromMinutes(1);

    /// <summary>The timeout in force: configured, bounded below by <see cref="MinimumTimeout"/>.</summary>
    public TimeSpan EffectiveTimeout()
    {
        var configured = TimeSpan.FromMinutes(TimeoutMinutes > 0 ? TimeoutMinutes : DefaultTimeoutMinutes);
        return configured < MinimumTimeout ? MinimumTimeout : configured;
    }

    /// <summary>True when a staging endpoint is set (an engine exists on this stamp).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(StagingBlobEndpoint);
}
