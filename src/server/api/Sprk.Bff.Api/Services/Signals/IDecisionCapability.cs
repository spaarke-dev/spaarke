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
    /// A Signal's decision plan, resolved as the caller exactly as <c>GET /api/v1/signals/{signalId}/decision-plan</c> resolves it,
    /// and the plan-read stamp the commit route reads (<see cref="DecisionPlanReadStamp"/>), written only when the plan is returned.
    /// </summary>
    Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct);

    /// <summary>A Signal's dismissal count and suppression date, read as <c>GET /api/v1/signals/{signalId}/suppression</c> reads them.</summary>
    Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct);
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
