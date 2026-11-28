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

    /// <summary>
    /// The most documents one Copilot run takes (<c>spaarke_run_playbook</c>'s cap, constraint 3), and the most of a run's documents the
    /// results read queries and lists (task 037 review F-D: a REST run has no cap, and an uncapped list would break the result's 64 KiB
    /// bound and the OData <c>$filter</c> URL length).
    /// </summary>
    public const int MaxDocuments = 20;
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

    /// <summary>
    /// Task 037a (D-037-engine): the analysis that holds the run's results could not be created for the caller on the run's first document;
    /// <see cref="PlaybookRunStartResult.Analysis"/> carries the create's outcome, status and <c>reasonCode</c>. Nothing ran.
    /// </summary>
    AnalysisRefused,
}

/// <summary>The answer to a start.</summary>
/// <param name="Outcome">How the start ended.</param>
/// <param name="Authorization">Task 007's decision (null when the start never reached it).</param>
/// <param name="Run">The run, when <see cref="PlaybookRunStartOutcome.Started"/>.</param>
/// <param name="Analysis">
/// Task 037a: the analysis created for the run's results (when <see cref="PlaybookRunStartOutcome.Started"/>), or the refused create (when
/// <see cref="PlaybookRunStartOutcome.AnalysisRefused"/>); <c>null</c> when the start never reached the create.
/// </param>
public sealed record PlaybookRunStartResult(
    PlaybookRunStartOutcome Outcome, PlaybookRunAuthorization? Authorization, PlaybookRunView? Run, AnalysisStartResult? Analysis = null);

/// <summary>Whether a run's results can be shown (task 037b; 037-decisions §5).</summary>
public enum PlaybookRunResultsState
{
    /// <summary>The run is still running: no content is read or returned yet.</summary>
    Pending,

    /// <summary>The run has finished and its analysis's working document holds text: <see cref="PlaybookRunResults.WorkingDocument"/>.</summary>
    Available,

    /// <summary>
    /// The run has finished and there is nothing to show: it has no linked analysis (a run started before task 037a, or a run started
    /// outside the Copilot start), or its analysis's working document is empty (a playbook that wrote to a record and not to the analysis,
    /// or a run that failed before its output was saved).
    /// </summary>
    None,

    /// <summary>
    /// Task 037 review F-A: the run COMPLETED, but its analysis has no working document and was never finalized: the save of its output
    /// failed (for example a Dataverse write that was throttled or refused). The output is lost; the caller can open the analysis or run
    /// the playbook again.
    /// </summary>
    Unsaved,
}

/// <summary>One of a run's source documents the caller can read (its own row read as the caller), with its "open in Spaarke" link.</summary>
/// <param name="DocumentId">The <c>sprk_document</c> id.</param>
/// <param name="Name">The document's name as the caller reads it (<c>sprk_documentname</c>); empty when it has none.</param>
/// <param name="ConsoleLink">Where the caller may open the document in the Spaarke Console (<see cref="IPlaybookRunAi.RunConsoleLink"/>).</param>
public sealed record PlaybookRunSourceDocument(Guid DocumentId, string Name, string? ConsoleLink);

/// <summary>
/// A finished (or still running) run's results as its starter may read them (task 037b). Every value that is not an identifier was read
/// AS THE CALLER (<c>IDataverseUserClient</c>, OBO): the analysis's working document, the playbook's name and the documents' names.
/// </summary>
/// <param name="Run">The run, as <see cref="IPlaybookRunAi.ReadAsCallerAsync"/> answers it.</param>
/// <param name="State">Whether there is anything to show.</param>
/// <param name="AnalysisId">The analysis the run saved its results to (the run record's regarding), when it has one.</param>
/// <param name="PlaybookName">The JPS playbook's name, when the caller can read it.</param>
/// <param name="WorkingDocument">
/// The analysis's <c>sprk_workingdocument</c>, unbounded and unformatted: only when <see cref="State"/> is
/// <see cref="PlaybookRunResultsState.Available"/>. The host bounds it (NFR-02) and never logs it (ADR-015).
/// </param>
/// <param name="Documents">The run's source documents the caller can read, in the run's order (the analysis's own document first).</param>
/// <param name="ConsoleLink">
/// Where the caller opens the results in Spaarke: the analysis in the Console (<c>HandoffUrlBuilder.BuildAnalysisWorkspaceUrl</c>) when the
/// run has one, else <see cref="IPlaybookRunAi.RunConsoleLink"/> for its first readable document; <c>null</c> when this host builds no links.
/// </param>
/// <param name="MoreDocuments">
/// Task 037 review F-D: how many of the run's documents were neither read nor listed, past the first
/// <see cref="PlaybookRunLimits.MaxDocuments"/> (a REST run has no cap); 0 otherwise.
/// </param>
public sealed record PlaybookRunResults(
    PlaybookRunView Run,
    PlaybookRunResultsState State,
    Guid? AnalysisId,
    string? PlaybookName,
    string? WorkingDocument,
    IReadOnlyList<PlaybookRunSourceDocument> Documents,
    string? ConsoleLink,
    int MoreDocuments = 0);

/// <summary>
/// The one seam through which a host other than the REST run routes starts and reads a JPS playbook run (spaarkeai-mcp-bff-api-r2
/// task 030): today the MCP tools <c>spaarke_run_playbook</c> and <c>spaarke_get_playbook_run</c>.
/// </summary>
/// <remarks>
/// <para><b>Start.</b> Authorizes with task 007's <see cref="PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync"/> inside the caller's
/// request, then runs the playbook through the engine's own person-run entry as a detached caller run (ADR-052 amendment 2026-11-26): the
/// run continues after the request has answered, acting as the caller, capped at <see cref="PlaybookRunLimits.MaxRunDuration"/>. It returns
/// once the engine has accepted the run, never waiting for it to finish. Task 037a (D-037-engine): after the authorization and the pre-warm,
/// and before the run starts, it creates the caller's analysis on the run's first document (<see cref="IAnalysisStartAi"/>, the route's
/// create); the run saves its output there and its record is regarding it. A refused create starts nothing
/// (<see cref="PlaybookRunStartOutcome.AnalysisRefused"/>). A start that creates the analysis but does not end
/// <see cref="PlaybookRunStartOutcome.Started"/> deletes that analysis as the caller, in the same request (D-037-cleanup, task 037d).</para>
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
    /// block (<c>PromptContextHelper.BuildUserFocusBlock</c>, #3268): it may narrow emphasis, and the model is instructed not to change the action's
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
    /// Task 037b (D-037-results): the results of the caller's run <paramref name="runId"/>, read as the caller; <c>null</c> (the one uniform
    /// not-found) when the run is unknown or another caller's (<see cref="ReadAsCallerAsync"/>), when the run's analysis fails the
    /// analysis-read rule (its document or other anchor is unreadable, or the external narrowing hides it), when the caller's own read of
    /// the analysis row is refused, or, for a run with no analysis, when the caller cannot read the run's first document.
    /// </summary>
    /// <remarks>
    /// The analysis id comes only from the run record's regarding columns (<c>sprk_regardingentitytype = sprk_analysis</c>), which the
    /// server wrote at the Copilot start (task 037a); no caller argument names it. A fault that is not a refusal throws: the host answers
    /// "could not read", never the not-found.
    /// </remarks>
    Task<PlaybookRunResults?> ReadResultsAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// The run-link member (task 020 verifier F2): where the caller may open the run's subject in the Spaarke Console, built by
    /// <c>HandoffUrlBuilder</c>; <c>null</c> when this host builds no links. An "open in Spaarke" link, not a hand-off.
    /// </summary>
    string? RunConsoleLink(HttpContext http, Guid? documentId);
}
