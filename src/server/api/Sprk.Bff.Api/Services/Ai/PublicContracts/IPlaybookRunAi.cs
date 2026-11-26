using Sprk.Bff.Api.Api.Filters;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 030 (spec FR-05, FR-13, FR-14; owner decision D-030-run-model; ADR-052 amendment 2026-11-26).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: IPlaybookOrchestrationService (Services/Ai, which an MCP tool may not name: ADR-013 and the MCP ledger guard),
//     the REST run routes (PlaybookRunEndpoints, which run inside their own request and never detach), HandoffUrlBuilder
//     (Services/Ai/Handoff, likewise unreachable from Api/Mcp). No public contract starts or reads a playbook run.
//   Extension: no existing facade covers playbook runs; the REST routes stream inside the request, which a stateless MCP host cannot
//     keep open for a run of minutes. This contract is the one seam over the existing engine entry, task 007's authorization and
//     task 005's record, plus the run-link member the long-running tools share (task 020 verifier F2).
//   Cost of doing nothing: spaarke_run_playbook / spaarke_get_playbook_run cannot reach the engine at all (FR-05 fails), and
//     consoleLink has no builder (HOW-TO-ADD-AN-MCP-TOOL item 8).
// =====================================================================================================================

/// <summary>The bounds of a detached caller run (ADR-052 amendment 2026-11-26, condition 5).</summary>
public static class PlaybookRunLimits
{
    /// <summary>
    /// The longest a detached run may take: 50 minutes. The cap bounds the run; it does NOT keep the caller's token valid. The token the
    /// run carries may have only minutes left when the run starts. The run's delegated access rests on the OBO tokens obtained at the
    /// start (the pre-warm): MSAL's OBO cache, keyed by the caller's assertion, and the Graph Redis cache, whose TTL follows the Graph
    /// token's own expiry. A later exchange that misses those caches fails closed (<see cref="PlaybookRunReasonCodes.AuthExpired"/>),
    /// for example after <c>OrderedCredentialClientProvider</c> switches to another confidential client (its cache starts empty), or when
    /// a node reaches a Dataverse host other than the pre-warmed one (<c>EnvironmentUrl</c> differing from <c>ServiceUrl</c>).
    /// </summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromMinutes(50);

    /// <summary>
    /// The most detached runs one caller (by Entra object id) may have running at once in this process; a start beyond it is refused
    /// (<see cref="PlaybookRunReasonCodes.RunLimit"/>). It bounds what one user, or one stolen token, can keep running for up to
    /// <see cref="MaxRunDuration"/>. Per instance: the run table is this process's, so the bound is per BFF instance, not per tenant.
    /// </summary>
    public const int MaxConcurrentRunsPerCaller = 3;

    /// <summary>How long the start waits for the engine to accept the run (its first event names the run id) before giving up.</summary>
    public static readonly TimeSpan AcceptBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long past <see cref="MaxRunDuration"/> a durable record may still say Running before it is read as interrupted (a run lost to
    /// a restart or a scale-in never writes its final state).
    /// </summary>
    public static readonly TimeSpan InterruptedMargin = TimeSpan.FromMinutes(10);
}

/// <summary>The stable reason codes a detached caller run reports (ADR-019: codes, never exception text).</summary>
public static class PlaybookRunReasonCodes
{
    /// <summary>The caller's delegated access could not be obtained at the start. Nothing ran.</summary>
    public const string AuthUnavailable = "playbook.run-auth-unavailable";

    /// <summary>The run's delegated access failed mid-run (an expired assertion, a consent or Conditional Access challenge).</summary>
    public const string AuthExpired = "playbook.run-auth-expired";

    /// <summary>The run reached <see cref="PlaybookRunLimits.MaxRunDuration"/>.</summary>
    public const string TimeLimit = "playbook.run-time-limit";

    /// <summary>The app shut down while the run was in flight.</summary>
    public const string HostStopping = "playbook.run-host-stopping";

    /// <summary>The engine did not accept the run within <see cref="PlaybookRunLimits.AcceptBudget"/>; it was stopped.</summary>
    public const string NotAccepted = "playbook.run-not-accepted";

    /// <summary>Playbook runs are not available on this host.</summary>
    public const string Unavailable = "playbook.run-unavailable";

    /// <summary>The caller already has <see cref="PlaybookRunLimits.MaxConcurrentRunsPerCaller"/> runs running. Nothing ran.</summary>
    public const string RunLimit = "playbook.run-limit";

    /// <summary>The run failed (the engine's own terminal code, <see cref="PlaybookStreamEvent.RunFailedErrorCode"/>).</summary>
    public const string Failed = PlaybookStreamEvent.RunFailedErrorCode;

    /// <summary>A durable record still Running long after the cap: the run was lost to a restart or a scale-in.</summary>
    public const string Interrupted = "playbook.run-interrupted";
}

/// <summary>Where a playbook run is.</summary>
public enum PlaybookRunPhase
{
    /// <summary>Accepted and not finished (a durable record still Pending or Running reads as this).</summary>
    Running,

    /// <summary>Finished with a result.</summary>
    Completed,

    /// <summary>Finished without a result.</summary>
    Failed,

    /// <summary>Stopped before it finished (the app shut down, or the run was cancelled).</summary>
    Cancelled,
}

/// <summary>
/// A playbook run as its starter may see it: state and counts only, never node output or document content (ADR-015).
/// </summary>
/// <param name="RunId">The run id the engine assigned.</param>
/// <param name="PlaybookId">The JPS playbook that ran.</param>
/// <param name="Phase">Where the run is.</param>
/// <param name="Progress">Percent of nodes finished, 0 to 100, when known.</param>
/// <param name="CompletedNodes">Nodes that finished with an output.</param>
/// <param name="FailedNodes">Nodes that failed.</param>
/// <param name="SkippedNodes">Nodes that were skipped.</param>
/// <param name="ReasonCode">Why a run that did not complete ended, when known (a stable code, never exception text).</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="CompletedAt">When it finished.</param>
/// <param name="FromDurableRecord">True when this was read from the durable run record, not from this process's memory.</param>
public sealed record PlaybookRunView(
    Guid RunId,
    Guid PlaybookId,
    PlaybookRunPhase Phase,
    int? Progress,
    int CompletedNodes,
    int FailedNodes,
    int SkippedNodes,
    string? ReasonCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool FromDurableRecord);

/// <summary>How a start ended.</summary>
public enum PlaybookRunStartOutcome
{
    /// <summary>The engine accepted the run; <see cref="PlaybookRunStartResult.Run"/> says where it is.</summary>
    Started,

    /// <summary>Task 007's run authorization refused; <see cref="PlaybookRunStartResult.Authorization"/> carries the REST reasonCode. Nothing ran.</summary>
    Refused,

    /// <summary>The caller's delegated Graph or Dataverse access could not be obtained while the request was authenticated. Nothing ran.</summary>
    AuthUnavailable,

    /// <summary>The engine did not accept the run within <see cref="PlaybookRunLimits.AcceptBudget"/>; it was cancelled.</summary>
    NotAccepted,

    /// <summary>Playbook runs are not available on this host (the AI features are off, or the run ended before the engine accepted it).</summary>
    Unavailable,

    /// <summary>The caller already has <see cref="PlaybookRunLimits.MaxConcurrentRunsPerCaller"/> runs running in this process. Nothing ran.</summary>
    RunLimit,
}

/// <summary>The answer to a start.</summary>
/// <param name="Outcome">How the start ended.</param>
/// <param name="Authorization">Task 007's decision (null when the start never reached it).</param>
/// <param name="Run">The run, when <see cref="PlaybookRunStartOutcome.Started"/>.</param>
public sealed record PlaybookRunStartResult(PlaybookRunStartOutcome Outcome, PlaybookRunAuthorization? Authorization, PlaybookRunView? Run);

/// <summary>
/// The one seam through which a host other than the REST run routes starts and reads a JPS playbook run (spaarkeai-mcp-bff-api-r2
/// task 030): today the MCP tools <c>spaarke_run_playbook</c> and <c>spaarke_get_playbook_run</c>.
/// </summary>
/// <remarks>
/// <para><b>Start.</b> Authorizes with task 007's <see cref="PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync"/> inside the caller's
/// request, then runs the playbook through the engine's own person-run entry as a detached caller run (ADR-052 amendment 2026-11-26): the
/// run continues after the request has answered, acting as the caller, capped at <see cref="PlaybookRunLimits.MaxRunDuration"/>. It returns
/// once the engine has accepted the run, never waiting for it to finish.</para>
/// <para><b>Read.</b> A run this process is running, else the engine's own status read (in memory, then task 005's durable record), for
/// the caller who started it only. Another caller's run and an unknown id both read as <c>null</c>.</para>
/// </remarks>
public interface IPlaybookRunAi
{
    /// <summary>Authorizes and starts a run for the caller of <paramref name="http"/> (see the interface remarks).</summary>
    /// <param name="http">The caller's own request context (its principal and bearer token are what the run acts with).</param>
    /// <param name="playbookId">The JPS playbook.</param>
    /// <param name="documentIds">The documents; this exact instance is authorized and handed to the run.</param>
    /// <param name="parameters">The run parameters; this exact instance is authorized and handed to the run.</param>
    /// <param name="userContext">
    /// What the caller asked the run to focus on, already trimmed and capped by the caller of this method (task 038); <c>null</c> when
    /// there is none. Handed to the engine as <c>PlaybookRunRequest.UserContext</c>, which reaches the AI prompt as a fenced "User focus"
    /// block (<c>PromptContextHelper.BuildUserFocusBlock</c>, #3268): it may narrow emphasis, but cannot change the action's
    /// instructions, the output format or the grounding rules. It is user query text: it travels in the detached run's request
    /// object in memory only, and this contract never logs or stores it (ADR-015, ADR-052).
    /// </param>
    /// <param name="cancellationToken">The caller's token (the start, not the run).</param>
    Task<PlaybookRunStartResult> StartAsCallerAsync(
        HttpContext http,
        Guid playbookId,
        Guid[] documentIds,
        IReadOnlyDictionary<string, string>? parameters,
        string? userContext,
        CancellationToken cancellationToken);

    /// <summary>The caller's run <paramref name="runId"/>, or <c>null</c> when it is unknown or another caller's.</summary>
    Task<PlaybookRunView?> ReadAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// The run-link member (task 020 verifier F2): where the caller may open the run's subject in the Spaarke Console, built by
    /// <c>HandoffUrlBuilder</c>; <c>null</c> when this host builds no links. An "open in Spaarke" link, not a hand-off.
    /// </summary>
    string? RunConsoleLink(HttpContext http, Guid? documentId);
}
