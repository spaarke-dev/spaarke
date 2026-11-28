using Sprk.Bff.Api.Services.Intake;

namespace Sprk.Bff.Api.Api.Intake;

/// <summary>The 200 body of <c>POST /api/v1/intake/requests/{requestId}/claim</c>.</summary>
/// <param name="RequestId">The claimed request.</param>
/// <param name="Claimed">Always true: the caller is now the request's claimer (it may already have been).</param>
public sealed record IntakeClaimResponse(Guid RequestId, bool Claimed);

/// <summary>
/// The intake approval's claim route (spaarke-intake-pipeline-r1 task 017, FR-11). A member of the request's owning team takes the
/// request for themselves: the claimer column is set to the caller's OWN linked contact (resolved on the server, never from the
/// request). Approve and reject are NOT here: they are the Decide-lane plan actions <c>approve-intake</c> / <c>reject-intake</c>,
/// committed through the decision commit route (one writer; the model-driven form's entry point calls that same route).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> <c>RequireAuthorization</c> (no token: 401), then <see cref="IntakeApprovalAuthorizationFilter"/>:
/// <see cref="IntakeApprovalService.AuthorizeAsync"/> as the caller, in the agreed order. A failed record check (no Read or no
/// Write, a fault, an unknown id, a row that is not an intake request) is ONE 404 whose body differs only by the correlation id;
/// not in the owning team, no single linked contact: 403. The handler then runs the whole chain again inside
/// <see cref="IntakeApprovalService.ClaimAsync"/> (so a change between the filter and the write still refuses), checks the run's
/// harness switch (off: 503, nothing written) and the state, and writes the claim conditionally on the row version (a race: one 409).</para>
/// <para><b>ProblemDetails (ADR-019).</b> <c>reasonCode</c> is one of INTAKE_RUN_NOT_FOUND, INTAKE_FORBIDDEN, INTAKE_RUN_WRONG_STATE,
/// INTAKE_ALREADY_CLAIMED, INTAKE_DISABLED; <c>correlationId</c> is the request's trace id. Details are fixed strings.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the decision commit route records decisions but has no
/// claim (a claim is not a decision and writes no Decision Record); the Assigned-To write path (ADR-034 A4) grants contacts access,
/// which a service request must never do (uac-r2 2026-11-28). Extension: the route is a thin adapter over
/// <see cref="IntakeApprovalService"/>, which the executors share. Cost of doing nothing: no one can take an approval, so
/// approve/reject (claimer only) can never be committed and FR-11 never completes.</para>
/// <para><b>Placement (bff-extensions.md).</b> In the BFF: a request/response write, no background work, no package, no AI.</para>
/// </remarks>
public static class IntakeApprovalEndpoints
{
    public const string NotFoundCode = "INTAKE_RUN_NOT_FOUND";
    public const string ForbiddenCode = "INTAKE_FORBIDDEN";
    public const string WrongStateCode = "INTAKE_RUN_WRONG_STATE";
    public const string AlreadyClaimedCode = "INTAKE_ALREADY_CLAIMED";
    public const string DisabledCode = "INTAKE_DISABLED";
    public const string UnavailableCode = "INTAKE_UNAVAILABLE";

    /// <summary>The fixed 404 detail: the same for every record-check failure.</summary>
    internal const string NotFoundDetail = "The requested record was not found.";

    public static void MapIntakeApprovalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/intake/requests")
            .WithTags("Intake")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapPost("/{requestId:guid}/claim", ClaimAsync)
            .AddEndpointFilter<IntakeApprovalAuthorizationFilter>()
            .WithName("ClaimIntakeRequest")
            .WithSummary("Claim an intake request waiting for approval")
            .WithDescription("A member of the request's owning team, with Read and Write on it, sets themselves (their own linked "
                + "contact) as the request's claimer. Only the claimer may then approve or reject it through the decision commit route. "
                + "404 for any failed record check; 403 for a non-member or a caller with no single linked contact; 409 when the request "
                + "is not waiting for approval or someone else claimed it; 503 when the run's harness is switched off.")
            .Produces<IntakeClaimResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> ClaimAsync(Guid requestId, IntakeApprovalService approvals, HttpContext httpContext, CancellationToken ct)
    {
        var result = await approvals.ClaimAsync(httpContext, requestId, ct).ConfigureAwait(false);
        return result.IsDone ? Results.Ok(new IntakeClaimResponse(requestId, Claimed: true)) : ToProblem(result, httpContext);
    }

    /// <summary>The ProblemDetails of a refusal. The 404 never carries the service's detail, so its body is the same for every cause.</summary>
    internal static IResult ToProblem(IntakeApprovalResult result, HttpContext httpContext) => result.Outcome switch
    {
        IntakeApprovalOutcome.NotFound => Problem(httpContext, StatusCodes.Status404NotFound, "Not Found", NotFoundCode, NotFoundDetail),
        IntakeApprovalOutcome.Forbidden => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", ForbiddenCode, "You cannot claim or decide this request."),
        IntakeApprovalOutcome.Disabled => Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Service Unavailable", DisabledCode, "Approval of this request is switched off right now. Try again later."),
        IntakeApprovalOutcome.AlreadyClaimed => Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", AlreadyClaimedCode, "Someone else has claimed this request."),
        IntakeApprovalOutcome.WrongState => Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", WrongStateCode, "The request is not waiting for approval."),
        _ => Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Service Unavailable", UnavailableCode, "The request could not be claimed right now. Try again."),
    };

    private static IResult Problem(HttpContext httpContext, int status, string title, string code, string detail) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = code,
                ["correlationId"] = httpContext.TraceIdentifier,
            });
}
