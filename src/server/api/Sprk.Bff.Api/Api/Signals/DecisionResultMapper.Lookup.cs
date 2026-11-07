using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Signals;

/// <summary>
/// The lookup search's mapping (spaarkeai-mcp-bff-api-r1 task 016, design §8 B-3, spec FR-07), in the one mapper (B-2) so the REST route
/// and the MCP tool (task 023) answer one vocabulary. It adds no reason code: the access answers are the decision-plan route's
/// (<see cref="ToHttp(SignalAccessOutcome, HttpContext)"/>), a refused plan is that route's 422, a parameter the plan does not offer is
/// <see cref="DecisionCommitReasons.InvalidParameter"/> (<c>ontology.decision.invalid_parameter</c>, the commit route's code for a
/// parameter that is not valid), and a read that fails after the access check is <see cref="DecisionPlanEndpoints.AccessUnavailableReasonCode"/>
/// (503), the answer for a read a route needs after that check.
/// </summary>
public static partial class DecisionResultMapper
{
    private static readonly FixedProblem LookupInvalidParameterProblem = new(
        StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity",
        "This parameter cannot be searched for this item. Use a person or lookup parameter of the item's decision plan, and at most "
        + $"{DecisionLookupSearch.MaxQueryLength} characters of search text.",
        DecisionCommitReasons.InvalidParameter);

    /// <summary><c>GET /api/v1/signals/{signalId}/lookup</c>: the lookup search (task 016).</summary>
    public static IResult ToHttp(DecisionLookupResult result, HttpContext httpContext) =>
        result.Outcome switch
        {
            DecisionLookupOutcome.Ok when result.Response is { } response => Results.Ok(response),
            DecisionLookupOutcome.UserContextRequired => Problem(UserContextRequiredProblem, httpContext),
            DecisionLookupOutcome.NotAllowed => ToHttp(result.Access, httpContext),
            DecisionLookupOutcome.Refused => PlanRefused(result.RefusalReason, httpContext),
            DecisionLookupOutcome.InvalidParameter => Problem(LookupInvalidParameterProblem, httpContext),
            _ => AccessUnavailable(httpContext),
        };

    /// <summary>The lookup search for a tool: null when the candidates were returned, otherwise the REST route's code and status.</summary>
    public static DecisionToolError? ToToolError(DecisionLookupResult result) =>
        result.Outcome switch
        {
            DecisionLookupOutcome.Ok when result.Response is not null => null,
            DecisionLookupOutcome.UserContextRequired => ToToolError(UserContextRequiredProblem),
            DecisionLookupOutcome.NotAllowed => ToToolError(result.Access),
            DecisionLookupOutcome.Refused => ToToolError(PlanRefusedProblem),
            DecisionLookupOutcome.InvalidParameter => ToToolError(LookupInvalidParameterProblem),
            _ => ToToolError(AccessUnavailableProblem),
        };
}
