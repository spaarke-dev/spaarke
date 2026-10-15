using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Signals;

/// <summary>
/// <c>POST /api/v1/signals/{signalId}/decision</c> (task 043; spec FR-18, FR-22, FR-51, NFR-10; D-17): records ONE decision on a
/// Signal. The whole review is validated first; then the taken actions' writes, the Next steps and the messages run in order; then
/// the ONE Decision Record is written and the resolved Signals closed. A failure before the record writes no record, closes no
/// Signal and lists the writes that landed. A repeated review id returns the first outcome and writes nothing.
/// </summary>
/// <remarks>
/// <para><b>Authorization (NFR-10, D-29).</b> <c>RequireAuthorization</c> on the group. The record-level checks run in
/// <see cref="DecisionCommitService"/>: every Signal in the review through <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> as
/// the caller (unreadable: the uniform 404; unresolved caller: the single 403), then every action's preflight as the caller (a
/// missing right: 403 naming the action, before any write).</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> The BFF is the single
/// writer of Signals and Decision Records (FR-24; the writer identity is server-only), and the executors it sequences are BFF
/// services; a client cannot hold the record-last order. No AI-internal type is used (the gate tier comes from the PublicContracts
/// pure function, D-52); the route and its services are registered unconditionally (SignalsModule), so nothing is asymmetric.</para>
/// </remarks>
public static class DecisionCommitEndpoints
{
    public static void MapDecisionCommitEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/signals")
            .WithTags("Signals")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapPost("/{signalId:guid}/decision", CommitDecisionAsync)
            .WithName("CommitSignalDecision")
            .WithSummary("Record one decision on a Signal: actions, Next steps, messages, then the one Decision Record")
            .WithDescription("200 with the record id, the steps and the Signals closed (a repeated review id returns its first "
                + "outcome). 403 when the caller is unresolved or lacks a right an action needs; 404 when a Signal or its core "
                + "record is not readable; 409 when a Signal is no longer open or the review is in progress; 422 when the review "
                + "is invalid; 500 when a step failed (no record; the writes that landed are listed); 503 when the route cannot "
                + "check the review id.")
            .Accepts<DecisionReview>("application/json")
            .Produces<DecisionCommitResult>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> CommitDecisionAsync(
        Guid signalId,
        DecisionReview review,
        HttpContext httpContext,
        DecisionCommitService commits,
        CancellationToken ct)
    {
        var result = await commits.CommitAsync(signalId, review, httpContext, ct);
        return ToHttp(result, httpContext);
    }

    internal static IResult ToHttp(DecisionCommitResult result, HttpContext httpContext)
    {
        switch (result.Status)
        {
            case DecisionCommitStatus.Recorded:
                return Results.Ok(result);
            case DecisionCommitStatus.NotFound:
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
            case DecisionCommitStatus.CallerUnresolved:
                return Problem(StatusCodes.Status403Forbidden, "Forbidden", "The caller could not be resolved to a Dataverse user.",
                    DecisionPlanEndpoints.CallerUnresolvedReasonCode, httpContext, result: null);
        }

        var status = result.Status switch
        {
            DecisionCommitStatus.Forbidden => StatusCodes.Status403Forbidden,
            DecisionCommitStatus.Conflict or DecisionCommitStatus.InProgress => StatusCodes.Status409Conflict,
            DecisionCommitStatus.Refused => StatusCodes.Status422UnprocessableEntity,
            DecisionCommitStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
        var title = status switch
        {
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            _ => "The decision was not recorded",
        };
        return Problem(status, title, result.Detail ?? title, result.ReasonCode, httpContext, result);
    }

    private static IResult Problem(int status, string title, string detail, string? reasonCode, HttpContext httpContext, DecisionCommitResult? result)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = reasonCode,
            ["correlationId"] = httpContext.TraceIdentifier,
        };
        if (result is not null)
        {
            extensions["reviewId"] = result.ReviewId;
            extensions["actionCode"] = result.ActionCode;
            if (result.Status == DecisionCommitStatus.Failed)
            {
                // D-17: the user sees which writes landed.
                extensions["failedStep"] = result.FailedStep;
                extensions["landed"] = result.Landed;
                extensions["possiblyLanded"] = result.PossiblyLanded;
                extensions["steps"] = result.Steps;
                extensions["recordMayExist"] = result.RecordMayExist;
                extensions["sentButNotRecorded"] = result.SentButNotRecorded;
                extensions["resumable"] = result.Resumable;
                extensions["replayed"] = result.Replayed;
            }
        }

        return Results.Problem(title: title, detail: detail, statusCode: status, extensions: extensions);
    }
}
