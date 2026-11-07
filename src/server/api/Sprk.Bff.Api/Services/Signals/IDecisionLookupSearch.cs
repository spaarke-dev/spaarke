using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Signals;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The decision wizard's search for a Person or Lookup parameter, answered on the server for every workforce host
/// (spaarkeai-mcp-bff-api-r1 task 016, design §8 B-3, spec FR-07): the REST route
/// <c>GET /api/v1/signals/{signalId}/lookup</c> (<see cref="DecisionPlanEndpoints"/>) and the MCP tool
/// <c>spaarke_search_decision_lookup</c> (task 023) call it and map the result through <see cref="DecisionResultMapper"/>.
/// </summary>
/// <remarks>
/// <para><b>The plan drives it.</b> The caller names only the Signal, a parameter code and the text typed. The table and the scope
/// come from the Signal's decision plan: a Person parameter searches active contacts, <c>sprk_budget</c> searches the active budgets
/// of the Signal's matter only (D-18), <c>sprk_organization</c> searches active organisations. Every read runs as the caller, so a row
/// the caller cannot read never appears. At most <see cref="DecisionLookupSearch.MaxResults"/> candidates per call.</para>
/// <para><b>No caller, no read.</b> A null <see cref="HttpContext"/> or a principal with no authenticated identity returns
/// <see cref="DecisionLookupOutcome.UserContextRequired"/> before any Dataverse call, exactly as <see cref="IDecisionCapability"/>
/// does; nothing falls back to an app-only client.</para>
/// </remarks>
public interface IDecisionLookupSearch
{
    /// <summary>The candidates for <paramref name="parameterCode"/> of <paramref name="signalId"/>'s plan that match <paramref name="query"/>.</summary>
    /// <param name="http">The request; its caller is the identity every read runs as.</param>
    /// <param name="signalId">The Signal whose plan names the parameter.</param>
    /// <param name="parameterCode">A Person or Lookup parameter code of an action or Next step the plan offers.</param>
    /// <param name="query">The text typed; never part of a filter unescaped.</param>
    /// <param name="ct">Cancellation.</param>
    Task<DecisionLookupResult> SearchAsync(HttpContext? http, Guid signalId, string? parameterCode, string? query, CancellationToken ct);
}

/// <summary>What a <see cref="IDecisionLookupSearch.SearchAsync"/> call decided.</summary>
public enum DecisionLookupOutcome
{
    /// <summary>The candidates, in <see cref="DecisionLookupResult.Response"/> (possibly none).</summary>
    Ok,

    /// <summary>No request context or no authenticated caller: nothing was read.</summary>
    UserContextRequired,

    /// <summary>The Signal access check did not let the search go on; <see cref="DecisionLookupResult.Access"/> says why. The same
    /// answer the decision-plan route gives for the same Signal.</summary>
    NotAllowed,

    /// <summary>The plan does not resolve against the closed catalog; <see cref="DecisionLookupResult.RefusalReason"/> is the resolver's reason.</summary>
    Refused,

    /// <summary>The parameter code is not a Person or Lookup parameter the plan offers, or the query is too long: nothing was searched.</summary>
    InvalidParameter,

    /// <summary>The plan or the search could not be read (a fault): no candidates, never a partial or unscoped list.</summary>
    Unavailable,
}

/// <summary>One record the caller may pick.</summary>
/// <param name="Id">The record id, the value the commit route receives.</param>
/// <param name="Name">The record's display name.</param>
/// <param name="Email">A contact's <c>emailaddress1</c>; null for any other table or a contact without one.</param>
public sealed record DecisionLookupCandidate(Guid Id, string Name, string? Email = null);

/// <summary>The search's answer.</summary>
/// <param name="ParameterCode">The parameter searched.</param>
/// <param name="Entity">The table searched (<c>contact</c>, <c>sprk_budget</c>, <c>sprk_organization</c>), from the plan.</param>
/// <param name="Candidates">At most <see cref="DecisionLookupSearch.MaxResults"/>, by name.</param>
public sealed record DecisionLookupResponse(string ParameterCode, string Entity, IReadOnlyList<DecisionLookupCandidate> Candidates);

/// <summary>The lookup search's answer. <see cref="DecisionResultMapper"/> maps it to the REST answer or an MCP tool error.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Response">The body when <see cref="Outcome"/> is <see cref="DecisionLookupOutcome.Ok"/>.</param>
/// <param name="Access">For <see cref="DecisionLookupOutcome.NotAllowed"/>, the access outcome (as <see cref="DecisionPlanReadResult.Access"/>).</param>
/// <param name="RefusalReason">For <see cref="DecisionLookupOutcome.Refused"/>, the resolver's reason.</param>
public sealed record DecisionLookupResult(
    DecisionLookupOutcome Outcome,
    DecisionLookupResponse? Response = null,
    SignalAccessOutcome Access = SignalAccessOutcome.Allowed,
    string? RefusalReason = null)
{
    public static readonly DecisionLookupResult UserContextRequired = new(DecisionLookupOutcome.UserContextRequired);

    public static readonly DecisionLookupResult InvalidParameter = new(DecisionLookupOutcome.InvalidParameter);

    public static readonly DecisionLookupResult Unavailable = new(DecisionLookupOutcome.Unavailable);

    public static DecisionLookupResult Ok(DecisionLookupResponse response) =>
        new(DecisionLookupOutcome.Ok, response ?? throw new ArgumentNullException(nameof(response)));

    public static DecisionLookupResult NotAllowed(SignalAccessOutcome access) => new(DecisionLookupOutcome.NotAllowed, Access: access);

    public static DecisionLookupResult Refused(string? reason) => new(DecisionLookupOutcome.Refused, RefusalReason: reason);
}
