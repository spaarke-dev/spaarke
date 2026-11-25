using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Signals;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The decision slice's one entry point for every host (spaarkeai-mcp-bff-api-r1 task 013, design §8 B-1, spec FR-10 as amended
/// 2026-11-07, option C): the REST routes (<see cref="SignalReadEndpoints"/>, <see cref="DecisionPlanEndpoints"/>) and the MCP tools
/// (tasks 022, 023) call these members and map the result through <see cref="DecisionResultMapper"/>. Self-authorising: every member
/// takes the caller from the request (the <see cref="HttpContext"/>, or its <see cref="ClaimsPrincipal"/> where that is enough) and
/// runs the existing decision services as that caller. It adds no rule.
/// </summary>
/// <remarks>
/// <para><b>No caller, no read.</b> A null <see cref="HttpContext"/> or a principal with no authenticated identity returns
/// <c>UserContextRequired</c> (<see cref="Infrastructure.Dataverse.DataverseUserClientErrorCodes.UserContextRequired"/>) before any
/// Dataverse call; nothing falls back to an app-only client. A REST request never reaches that answer: the routes'
/// <c>RequireAuthorization</c> answers 401 first. v1 limitation: the members run only inside an HTTP request.</para>
/// <para>Signatures carry no <c>HttpRequest</c>, <c>IResult</c> or MCP SDK type.</para>
/// </remarks>
public interface IDecisionCapability
{
    /// <summary>
    /// The caller's worklist, unfiltered: <see cref="SignalReadService.ReadAsync"/> as the caller. Category partitioning is the MCP
    /// list tool's (task 022), never here.
    /// </summary>
    /// <param name="user">The request's principal.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="set">The worklist set; <see cref="WorklistSet.Lanes"/> (the Decide and Do lanes) unless the REST route's
    /// <c>?set=</c> names another.</param>
    Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes);

    /// <summary>
    /// One page of the caller's Decide lane as slim rows (task 015, spec FR-05, FR-11): at most <see cref="DecideLanePaging.PageSize"/>
    /// <see cref="WorklistItemSummary"/> in exactly the order <see cref="SignalReadService"/> returns the lane for the Console, and the
    /// cursor for the next page (null on the last). Reads the lane as <see cref="ListDecideAsync"/> does, as the caller, every call; the
    /// cursor holds no server state, so an item resolved between pages is simply absent from the next one.
    /// </summary>
    /// <param name="user">The request's principal.</param>
    /// <param name="cursor">The previous page's <see cref="DecidePageResult.NextCursor"/>; null or empty for the first page. A cursor that
    /// does not decode is <see cref="DecidePageOutcome.CursorInvalid"/> (a stale one is not).</param>
    /// <param name="ct">Cancellation.</param>
    Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct);

    /// <summary>
    /// One Work Item's full row as the caller's <see cref="WorklistSet.Lanes"/> read has it (<c>spaarke_get_decision_plan</c>): the lanes
    /// response narrowed to <paramref name="signalId"/>, which holds that one row when the caller's lanes show the Signal and no row
    /// otherwise (hidden and unknown are the same answer). Failures are the lane read's outcomes. <see cref="DecisionCapability"/> reads
    /// only that Signal (<see cref="SignalReadService.ReadAsync"/> with <c>onlySignalId</c>, ontology task 222 B, NFR-01); this default,
    /// which test doubles inherit, reads the whole lane through <see cref="ListDecideAsync"/>, which holds the same row.
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: <see cref="ListDecideAsync"/>, the whole caller lane read.
    /// Extension: this narrows the same read, as the caller, through the read's own <c>onlySignalId</c> parameter; no rule, route or
    /// identity changes. Cost of doing nothing: opening one item in Copilot reads every Work Item the caller has (~2 s of serial
    /// Dataverse reads, dev App Insights 2026-11-25) against NFR-01's 3 s plan target.</remarks>
    /// <param name="user">The request's principal.</param>
    /// <param name="signalId">The Work Item to read.</param>
    /// <param name="ct">Cancellation.</param>
    Task<SignalReadResult> GetWorkItemAsync(ClaimsPrincipal? user, Guid signalId, CancellationToken ct) => ListDecideAsync(user, ct);

    /// <summary>
    /// A Signal's decision plan, resolved as the caller exactly as <c>GET /api/v1/signals/{signalId}/decision-plan</c> resolves it,
    /// and the plan-read stamp the commit route reads (<see cref="DecisionPlanReadStamp"/>), written only when the plan is returned.
    /// </summary>
    Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct);

    /// <summary>A Signal's dismissal count and suppression date, read as <c>GET /api/v1/signals/{signalId}/suppression</c> reads them.</summary>
    Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct);

    /// <summary>
    /// What opening one item needs (<c>spaarke_get_decision_plan</c>): <see cref="GetPlanAsync"/>'s answer, and when that is
    /// <see cref="DecisionPlanReadOutcome.Ok"/>, <see cref="GetSuppressionAsync"/>'s, each exactly as those members answer it, plan-read stamp
    /// included. Suppression is null when the plan is not Ok (nothing more is read). <see cref="DecisionCapability"/> asks the Signal access
    /// question once for both (NFR-01, 2026-11-25); this default, which test doubles inherit, calls the two members in turn.
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: <see cref="GetPlanAsync"/> and <see cref="GetSuppressionAsync"/>,
    /// which each run the same access check. Extension: this composes them and shares the one decision; their own bodies are unchanged, so the
    /// REST routes are untouched. Cost of doing nothing: every item opened in Copilot runs the access check twice, a second set of caller
    /// reads (~0.5 s) against NFR-01's 3 s plan target, which the plan tool misses today (p95 4.7 s).</remarks>
    async Task<(DecisionPlanReadResult Plan, SuppressionReadResult? Suppression)> GetPlanAndSuppressionAsync(
        HttpContext? http, Guid signalId, CancellationToken ct)
    {
        var plan = await GetPlanAsync(http, signalId, ct);
        return plan.Outcome == DecisionPlanReadOutcome.Ok ? (plan, await GetSuppressionAsync(http, signalId, ct)) : (plan, null);
    }

    /// <summary>
    /// Records one decision on a Signal (task 014): <see cref="DecisionCommitService.CommitAsync"/> as the caller, unchanged, with the
    /// request's <see cref="HttpContext"/>. Every commit invariant (order, ledger replay, the invoice-review refusal, the caller-rights
    /// check, locks) is that service's; this member adds only the no-caller guard, which answers
    /// <see cref="DecisionCapability.UserContextRequiredCommitResult"/> before the service is called.
    /// </summary>
    Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct);
}

/// <summary>What a <see cref="IDecisionCapability.ListDecidePageAsync"/> call decided.</summary>
public enum DecidePageOutcome
{
    /// <summary>The page, in <see cref="DecidePageResult.Items"/> (possibly empty).</summary>
    Ok,

    /// <summary>No request context or no authenticated caller: nothing was read.</summary>
    UserContextRequired,

    /// <summary>The cursor is not one this server wrote the shape of (malformed or tampered): nothing was read.</summary>
    CursorInvalid,

    /// <summary>The lane could not be read; <see cref="DecidePageResult.ReadFailure"/> says why (the worklist read's own answer).</summary>
    ReadFailed,
}

/// <summary>The Decide-lane page's answer. <see cref="DecisionResultMapper"/> maps a failure to an MCP tool error.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Items">The page when <see cref="Outcome"/> is <see cref="DecidePageOutcome.Ok"/>; empty otherwise.</param>
/// <param name="NextCursor">Opaque; null on the last page.</param>
/// <param name="ReadFailure">For <see cref="DecidePageOutcome.ReadFailed"/>, the worklist read's non-Ok result.</param>
public sealed record DecidePageResult(
    DecidePageOutcome Outcome,
    IReadOnlyList<WorklistItemSummary> Items,
    string? NextCursor = null,
    SignalReadResult? ReadFailure = null)
{
    public static readonly DecidePageResult UserContextRequired = new(DecidePageOutcome.UserContextRequired, []);

    public static readonly DecidePageResult CursorInvalid = new(DecidePageOutcome.CursorInvalid, []);

    private static readonly IReadOnlyDictionary<Guid, WorklistCoreSummary> NoCores = new Dictionary<Guid, WorklistCoreSummary>();

    /// <summary>The core records of the page's rows (015 review F4, FR-12), one entry per group with a row on this page, keyed by
    /// <see cref="WorklistItemSummary.CoreRecordId"/>; empty when the page is not <see cref="DecidePageOutcome.Ok"/>. A core record with
    /// no row on this page has no entry.</summary>
    public IReadOnlyDictionary<Guid, WorklistCoreSummary> Cores { get; init; } = NoCores;

    /// <summary>The whole lanes read the page was cut from (NFR-01, 2026-11-25): the same <see cref="WorklistResponse"/>
    /// <see cref="IDecisionCapability.ListDecideAsync"/> returns for <see cref="WorklistSet.Lanes"/>, so a caller that needs both the
    /// lane's counts and page 1 (<c>spaarke_show_decisions</c>) reads the lane once, not twice. Null when the page is not
    /// <see cref="DecidePageOutcome.Ok"/>.</summary>
    public WorklistResponse? Lane { get; init; }

    public static DecidePageResult Ok(
        IReadOnlyList<WorklistItemSummary> items,
        IReadOnlyDictionary<Guid, WorklistCoreSummary> cores,
        string? nextCursor,
        WorklistResponse? lane = null) =>
        new(DecidePageOutcome.Ok, items ?? throw new ArgumentNullException(nameof(items)), nextCursor)
        {
            Cores = cores ?? throw new ArgumentNullException(nameof(cores)),
            Lane = lane,
        };

    public static DecidePageResult ReadFailed(SignalReadResult failure) =>
        new(DecidePageOutcome.ReadFailed, [], ReadFailure: failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>What a <see cref="IDecisionCapability.GetPlanAsync"/> call decided.</summary>
public enum DecisionPlanReadOutcome
{
    /// <summary>The plan, in <see cref="DecisionPlanReadResult.Plan"/>.</summary>
    Ok,

    /// <summary>No request context or no authenticated caller: nothing was read.</summary>
    UserContextRequired,

    /// <summary>The Signal access check did not let the read go on; <see cref="DecisionPlanReadResult.Access"/> says why.</summary>
    NotAllowed,

    /// <summary>The plan does not resolve against the closed catalog; <see cref="DecisionPlanReadResult.RefusalReason"/> is the resolver's reason.</summary>
    Refused,

    /// <summary>A read the plan needs after the access check (the inquiry To Do check) failed with a fault.</summary>
    AccessUnavailable,
}

/// <summary>The decision-plan read's answer. <see cref="DecisionResultMapper"/> maps it to the REST answer or an MCP tool error.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Plan">The body when <see cref="Outcome"/> is <see cref="DecisionPlanReadOutcome.Ok"/>.</param>
/// <param name="Access">For <see cref="DecisionPlanReadOutcome.NotAllowed"/>, the access outcome. <see cref="SignalAccessOutcome.Allowed"/>
/// there means the decision lacked the lane or policy version the plan needs (the uniform 404).</param>
/// <param name="RefusalReason">For <see cref="DecisionPlanReadOutcome.Refused"/>, the resolver's reason.</param>
public sealed record DecisionPlanReadResult(
    DecisionPlanReadOutcome Outcome,
    DecisionPlanResponse? Plan = null,
    SignalAccessOutcome Access = SignalAccessOutcome.Allowed,
    string? RefusalReason = null)
{
    public static readonly DecisionPlanReadResult UserContextRequired = new(DecisionPlanReadOutcome.UserContextRequired);

    public static readonly DecisionPlanReadResult AccessUnavailable = new(DecisionPlanReadOutcome.AccessUnavailable);

    public static DecisionPlanReadResult Ok(DecisionPlanResponse plan) =>
        new(DecisionPlanReadOutcome.Ok, plan ?? throw new ArgumentNullException(nameof(plan)));

    public static DecisionPlanReadResult NotAllowed(SignalAccessOutcome access) => new(DecisionPlanReadOutcome.NotAllowed, Access: access);

    public static DecisionPlanReadResult Refused(string? reason) => new(DecisionPlanReadOutcome.Refused, RefusalReason: reason);
}

/// <summary>What a <see cref="IDecisionCapability.GetSuppressionAsync"/> call decided.</summary>
public enum SuppressionReadOutcome
{
    /// <summary>The count, in <see cref="SuppressionReadResult.Suppression"/>.</summary>
    Ok,

    /// <summary>No request context or no authenticated caller: nothing was read.</summary>
    UserContextRequired,

    /// <summary>The Signal access check did not let the read go on; <see cref="SuppressionReadResult.Access"/> says why.</summary>
    NotAllowed,

    /// <summary>The caller was allowed but the count read found nothing (the uniform 404).</summary>
    NotFound,

    /// <summary>The Signal's secure-family partition cannot be determined, so its count is never answered.</summary>
    PartitionUndetermined,

    /// <summary>An authorized caller's count could not be read.</summary>
    Unavailable,
}

/// <summary>The suppression read's answer. <see cref="DecisionResultMapper"/> maps it to the REST answer or an MCP tool error.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Suppression">The body when <see cref="Outcome"/> is <see cref="SuppressionReadOutcome.Ok"/>.</param>
/// <param name="Access">For <see cref="SuppressionReadOutcome.NotAllowed"/>, the access outcome.</param>
public sealed record SuppressionReadResult(
    SuppressionReadOutcome Outcome,
    SuppressionResponse? Suppression = null,
    SignalAccessOutcome Access = SignalAccessOutcome.Allowed)
{
    public static readonly SuppressionReadResult UserContextRequired = new(SuppressionReadOutcome.UserContextRequired);

    public static readonly SuppressionReadResult NotFound = new(SuppressionReadOutcome.NotFound);

    public static readonly SuppressionReadResult PartitionUndetermined = new(SuppressionReadOutcome.PartitionUndetermined);

    public static readonly SuppressionReadResult Unavailable = new(SuppressionReadOutcome.Unavailable);

    public static SuppressionReadResult Ok(SuppressionResponse suppression) =>
        new(SuppressionReadOutcome.Ok, suppression ?? throw new ArgumentNullException(nameof(suppression)));

    public static SuppressionReadResult NotAllowed(SignalAccessOutcome access) => new(SuppressionReadOutcome.NotAllowed, Access: access);
}
