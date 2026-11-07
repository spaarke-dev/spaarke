using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Api.Signals;

/// <summary>
/// A decision-slice failure for a host that is not HTTP (the MCP tools, spaarkeai-mcp-bff-api-r1 B-2, spec FR-09). Task 020's
/// <c>Api/Mcp/McpToolResults</c> turns it into an <c>isError</c> tool result: a domain failure is never a transport error.
/// </summary>
/// <param name="ReasonCode">The <c>reasonCode</c> the REST route returns for the same result (one vocabulary), or an unknown code
/// passed through unchanged.</param>
/// <param name="Message">How to fix the call. It names the reason and the fix only: never record text, party names or matter names
/// (ADR-015), so it never repeats a result's <c>Detail</c>.</param>
/// <param name="Status">The HTTP status the REST route answers for the same result. Information for the host, never a transport status.</param>
public sealed record DecisionToolError(string ReasonCode, string Message, int Status);

/// <summary>
/// The ONE place the decision slice maps its results (spaarkeai-mcp-bff-api-r1 task 017, design §8 B-2, spec FR-09): to
/// ProblemDetails for the REST routes (<see cref="SignalReadEndpoints"/>, <see cref="DecisionPlanEndpoints"/>,
/// <see cref="DecisionCommitEndpoints"/>) or to a <see cref="DecisionToolError"/> for MCP, with one <c>reasonCode</c> vocabulary.
/// </summary>
/// <remarks>
/// <para><b>REST bytes are frozen.</b> Every <c>ToHttp</c> answer is the answer the endpoint statics gave before this class
/// existed: same status, title, detail, <c>type</c> and extension members in the same order. Task 011's golden baselines
/// (<c>tests/integration/contract/Api/Signals/Baselines/</c>) pin them; a change here that moves a byte is a REST contract change.</para>
/// <para><b>One vocabulary.</b> The reason codes are the existing constants (<see cref="DecisionCommitReasons"/>,
/// <see cref="DecisionActionReasons"/>, the route constants on <see cref="DecisionPlanEndpoints"/> and
/// <see cref="SignalReadEndpoints"/>, <see cref="ProblemDetailsHelper.RecordUnavailableReasonCode"/>); this class adds none. REST
/// and MCP take the status of a commit result from the same switch, so the two hosts cannot disagree.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> It replaces mapping statics
/// that lived in the three Signals endpoint classes; it is a static class with no DI registration, no package and no AI-internal
/// type. The MCP side needs no caller context (spec FR-10 as amended 2026-11-07, option C).</para>
/// </remarks>
public static class DecisionResultMapper
{
    /// <summary>A REST answer whose title, detail and status do not depend on the result (the plan, suppression and worklist routes).</summary>
    private sealed record FixedProblem(int Status, string Title, string Detail, string ReasonCode);

    private static readonly FixedProblem CallerUnresolvedProblem = new(
        StatusCodes.Status403Forbidden, "Forbidden", "The caller could not be resolved to a Dataverse user.",
        DecisionPlanEndpoints.CallerUnresolvedReasonCode);

    private static readonly FixedProblem AccessUnavailableProblem = new(
        StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "This item could not be checked right now. Try again.",
        DecisionPlanEndpoints.AccessUnavailableReasonCode);

    private static readonly FixedProblem WorklistUnavailableProblem = new(
        StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "The worklist could not be read. Try again.",
        SignalReadEndpoints.WorklistUnavailableReasonCode);

    private static readonly FixedProblem PlanRefusedProblem = new(
        StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity",
        "This Signal's decision plan does not resolve against the action catalog, so no actions can be offered.",
        DecisionPlanEndpoints.PlanRefusedReasonCode);

    private static readonly FixedProblem PartitionUndeterminedProblem = new(
        StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
        "The suppression count is not available for this Signal because the secure records it belongs to cannot be determined.",
        DecisionPlanEndpoints.PartitionUndeterminedReasonCode);

    private static readonly FixedProblem SuppressionUnavailableProblem = new(
        StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "The suppression count could not be read. Try again.",
        DecisionPlanEndpoints.SuppressionUnavailableReasonCode);

    // ── REST ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary><c>GET /api/v1/signals/worklist</c>. The internal reason of an <see cref="SignalReadOutcome.Unavailable"/> result
    /// (for example <c>too_many_signals</c>) is never returned: every fault is the same 503.</summary>
    public static IResult ToHttp(SignalReadResult result, HttpContext httpContext) =>
        result.Outcome switch
        {
            SignalReadOutcome.Ok when result.Response is { } body => Results.Ok(body),
            SignalReadOutcome.CallerUnresolved => Problem(CallerUnresolvedProblem, httpContext),
            _ => Problem(WorklistUnavailableProblem, httpContext),
        };

    /// <summary>
    /// The plan and suppression routes' answer when <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> does not let the route go on:
    /// the single 403 for an unresolved caller, 503 for a fault, otherwise the uniform 404. <see cref="SignalAccessOutcome.Allowed"/>
    /// reaches here only when the decision lacks what the route needs (the plan route's lane and policy version), and is the uniform 404.
    /// </summary>
    public static IResult ToHttp(SignalAccessOutcome outcome, HttpContext httpContext) =>
        outcome switch
        {
            SignalAccessOutcome.CallerUnresolved => Problem(CallerUnresolvedProblem, httpContext),
            SignalAccessOutcome.Unavailable => Problem(AccessUnavailableProblem, httpContext),
            _ => ProblemDetailsHelper.UniformRecordNotFound(httpContext),
        };

    /// <summary>The uniform 404 (<see cref="ProblemDetailsHelper.UniformRecordNotFound"/>), for a read that found nothing after the
    /// caller was allowed.</summary>
    public static IResult RecordNotFound(HttpContext httpContext) => ProblemDetailsHelper.UniformRecordNotFound(httpContext);

    /// <summary>A read the route needs after the access check (the inquiry To Do check) failed with a fault: 503, never the uniform 404.</summary>
    public static IResult AccessUnavailable(HttpContext httpContext) => Problem(AccessUnavailableProblem, httpContext);

    /// <summary>The Signal's plan does not resolve against the closed catalog (422); <paramref name="refusalReason"/> is the resolver's reason.</summary>
    public static IResult PlanRefused(string? refusalReason, HttpContext httpContext) =>
        Results.Problem(
            title: PlanRefusedProblem.Title,
            detail: PlanRefusedProblem.Detail,
            statusCode: PlanRefusedProblem.Status,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = PlanRefusedProblem.ReasonCode,
                ["reason"] = refusalReason,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    /// <summary>The Signal's secure-family partition cannot be determined, so its suppression count is never answered (503).</summary>
    public static IResult SuppressionPartitionUndetermined(HttpContext httpContext) => Problem(PartitionUndeterminedProblem, httpContext);

    /// <summary>An authorized caller's suppression count could not be read (503).</summary>
    public static IResult SuppressionUnavailable(HttpContext httpContext) => Problem(SuppressionUnavailableProblem, httpContext);

    /// <summary><c>POST /api/v1/signals/{signalId}/decision</c>. A <see cref="DecisionCommitStatus.Failed"/> result lists the writes that
    /// landed (D-17).</summary>
    public static IResult ToHttp(DecisionCommitResult result, HttpContext httpContext)
    {
        switch (result.Status)
        {
            case DecisionCommitStatus.Recorded:
                return Results.Ok(result);
            case DecisionCommitStatus.NotFound:
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
            case DecisionCommitStatus.CallerUnresolved:
                return CommitProblem(CallerUnresolvedProblem.Status, CallerUnresolvedProblem.Title, CallerUnresolvedProblem.Detail,
                    CallerUnresolvedProblem.ReasonCode, httpContext, result: null);
        }

        var status = CommitStatusCode(result.Status);
        var title = status switch
        {
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            _ => "The decision was not recorded",
        };
        return CommitProblem(status, title, result.Detail ?? title, result.ReasonCode, httpContext, result);
    }

    // ── MCP ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The worklist read for a tool: null when the read succeeded, otherwise the error the REST route's answer carries.</summary>
    public static DecisionToolError? ToToolError(SignalReadResult result) =>
        result.Outcome switch
        {
            SignalReadOutcome.Ok when result.Response is not null => null,
            SignalReadOutcome.CallerUnresolved => ToToolError(CallerUnresolvedProblem),
            _ => ToToolError(WorklistUnavailableProblem),
        };

    /// <summary>The access check for a tool, called when the route cannot go on (see <see cref="ToHttp(SignalAccessOutcome, HttpContext)"/>).</summary>
    public static DecisionToolError ToToolError(SignalAccessOutcome outcome) =>
        outcome switch
        {
            SignalAccessOutcome.CallerUnresolved => ToToolError(CallerUnresolvedProblem),
            SignalAccessOutcome.Unavailable => ToToolError(AccessUnavailableProblem),
            _ => ToToolError(ProblemDetailsHelper.RecordUnavailableReasonCode, StatusCodes.Status404NotFound),
        };

    /// <summary>A commit result for a tool: null when the decision was recorded, otherwise the REST route's reason code and status.</summary>
    public static DecisionToolError? ToToolError(DecisionCommitResult result) =>
        result.Status switch
        {
            DecisionCommitStatus.Recorded => null,
            DecisionCommitStatus.NotFound => ToToolError(ProblemDetailsHelper.RecordUnavailableReasonCode, StatusCodes.Status404NotFound),
            DecisionCommitStatus.CallerUnresolved => ToToolError(CallerUnresolvedProblem),
            _ => ToToolError(
                string.IsNullOrWhiteSpace(result.ReasonCode) ? FallbackReasonCode(result.Status) : result.ReasonCode,
                CommitStatusCode(result.Status)),
        };

    /// <summary>
    /// Any reason code with the status the REST route answers it with. A code outside the vocabulary is passed through unchanged with a
    /// general message for its status class; it never throws for an unknown code.
    /// </summary>
    public static DecisionToolError ToToolError(string reasonCode, int status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        var message = FixMessages.TryGetValue(reasonCode, out var known)
            ? known
            : status >= StatusCodes.Status500InternalServerError
                ? UnknownServerFailureMessage
                : UnknownRefusalMessage;
        return new DecisionToolError(reasonCode, message, status);
    }

    // ── Shared ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ONE status switch of a commit result that is not Recorded, NotFound or CallerUnresolved; REST and MCP both use it.</summary>
    private static int CommitStatusCode(DecisionCommitStatus status) =>
        status switch
        {
            DecisionCommitStatus.Forbidden => StatusCodes.Status403Forbidden,
            DecisionCommitStatus.Conflict or DecisionCommitStatus.InProgress => StatusCodes.Status409Conflict,
            DecisionCommitStatus.Refused => StatusCodes.Status422UnprocessableEntity,
            DecisionCommitStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

    /// <summary>
    /// A tool error needs a code. Every failure <see cref="DecisionCommitService"/> builds today carries one, so this is a guard for a
    /// future result without one (null or blank), so that a domain failure never becomes an exception: the code the service itself uses for that status (<c>step_failed</c> for a failed step,
    /// <c>idempotency_unavailable</c> for Unavailable, <c>review_in_progress</c> for InProgress, <c>not_authorized</c> for Forbidden),
    /// <c>signal_not_open</c> for Conflict and <c>review_invalid</c> for any other refusal. REST is unaffected (it keeps a null reasonCode).
    /// </summary>
    private static string FallbackReasonCode(DecisionCommitStatus status) =>
        status switch
        {
            DecisionCommitStatus.Unavailable => DecisionCommitReasons.IdempotencyUnavailable,
            DecisionCommitStatus.InProgress => DecisionCommitReasons.ReviewInProgress,
            DecisionCommitStatus.Forbidden => DecisionCommitReasons.NotAuthorized,
            DecisionCommitStatus.Conflict => DecisionCommitReasons.SignalNotOpen,
            DecisionCommitStatus.Refused => DecisionCommitReasons.ReviewInvalid,
            _ => DecisionCommitReasons.StepFailed,
        };

    private static DecisionToolError ToToolError(FixedProblem problem) => ToToolError(problem.ReasonCode, problem.Status);

    private static IResult Problem(FixedProblem problem, HttpContext httpContext) =>
        Results.Problem(
            title: problem.Title,
            detail: problem.Detail,
            statusCode: problem.Status,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = problem.ReasonCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    private static IResult CommitProblem(int status, string title, string detail, string? reasonCode, HttpContext httpContext, DecisionCommitResult? result)
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

    private const string UnknownRefusalMessage =
        "This decision call was refused. Read the item's decision plan again and correct the call, or open the item in Spaarke.";

    private const string UnknownServerFailureMessage =
        "This decision call could not be completed right now. Check the item in Spaarke before trying again.";

    /// <summary>
    /// How to fix the call, per reason code. Fixed text only (ADR-015): no record text, party names or matter names. Every code a
    /// decision route can return has an entry (DecisionResultMapperTests checks the constants classes against this table).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FixMessages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Access, worklist, plan and suppression routes.
        [DecisionPlanEndpoints.CallerUnresolvedReasonCode] =
            "Your Spaarke user could not be identified. Sign in again with your Spaarke work account, then retry.",
        [ProblemDetailsHelper.RecordUnavailableReasonCode] =
            "This item was not found or is not available to you. Refresh the decision list and choose an item from it.",
        [DecisionPlanEndpoints.AccessUnavailableReasonCode] =
            "This item could not be checked right now; nothing was done. Try again shortly.",
        [SignalReadEndpoints.WorklistUnavailableReasonCode] =
            "The decision list could not be read right now. Try again shortly.",
        [DecisionPlanEndpoints.PlanRefusedReasonCode] =
            "No actions can be offered for this item here. Open the item in Spaarke to decide it.",
        [DecisionPlanEndpoints.SuppressionUnavailableReasonCode] =
            "The dismissal count could not be read right now. Try again shortly.",
        [DecisionPlanEndpoints.PartitionUndeterminedReasonCode] =
            "The dismissal count is not available for this item, and retrying will not change that. Decide it without the count, or open it in Spaarke.",

        // The decision commit route (DecisionCommitReasons).
        [DecisionCommitReasons.ReviewInvalid] =
            "The review is not valid. Give it a review id, and stay within the limits on Also resolve items and Next steps.",
        [DecisionCommitReasons.NotConfirmed] =
            "The decision was not confirmed. Have the user confirm it, then send it again with confirmed set to true.",
        [DecisionCommitReasons.ActionsDoNotMatchPlan] =
            "The actions do not match the item's current decision plan. Read the plan again and send every offered action exactly once, taken or skipped.",
        [DecisionCommitReasons.UnresolvedAction] =
            "An action or Next step code is not one this decision can use. Use only the codes the item's decision plan offers.",
        [DecisionCommitReasons.GateUndeclared] =
            "An action cannot be recorded from here. Skip it, or open the item in Spaarke to decide it.",
        [DecisionCommitReasons.InvalidParameter] =
            "A value for a taken action or Next step is missing or not valid. Check the values the decision plan lists for it, then send again.",
        [DecisionCommitReasons.ActionsExclude] =
            "Two taken actions exclude each other. Take only one of them.",
        [DecisionCommitReasons.DismissalReasonRequired] =
            "Nothing is taken, so the decision is a dismissal and needs a reason. Choose one of the plan's dismissal reasons.",
        [DecisionCommitReasons.DismissalReasonUnknown] =
            "The dismissal reason is not one this item's plan offers. Choose one of the plan's dismissal reasons.",
        [DecisionCommitReasons.DismissalDetailRequired] =
            "This dismissal reason needs a short explanation. Add the detail, then send again.",
        [DecisionCommitReasons.DismissalWithActions] =
            "A dismissal cannot be sent with taken actions or Next steps. Send either a dismissal or the actions.",
        [DecisionCommitReasons.AlsoResolveNotAllowed] =
            "An item in Also resolve cannot be decided together with this one. Remove it from Also resolve and decide it separately.",
        [DecisionCommitReasons.SecureFamilyUnavailable] =
            "Whether these items can be decided together could not be checked right now; nothing was done. Try again shortly.",
        [DecisionCommitReasons.SignalNotOpen] =
            "This item is no longer open, so nothing was done. Refresh the decision list.",
        [DecisionCommitReasons.SignalUnreadable] =
            "An item in this decision could not be read right now; nothing was done. Try again shortly.",
        [DecisionCommitReasons.CoreUnsupported] =
            "This item's matter or project could not be used for a decision; nothing was done. Try again shortly, or open the item in Spaarke.",
        [DecisionCommitReasons.ItemOwnerUnsupported] =
            "This item's owner could not be used for a decision; nothing was done. Try again shortly, or open the item in Spaarke.",
        [DecisionCommitReasons.RecordRefused] =
            "The decision could not be recorded; nothing was done. Try again shortly, or open the item in Spaarke.",
        [DecisionCommitReasons.StepFailed] =
            "A step of the decision did not complete and no decision was recorded. Check the item in Spaarke before trying again.",
        [DecisionCommitReasons.RecordWriteFailed] =
            "The decision could not be recorded. Check the item in Spaarke before trying again, and do not send any message again.",
        [DecisionCommitReasons.ReviewInProgress] =
            "This decision is already being recorded. Wait a moment, then read the item again before sending anything.",
        [DecisionCommitReasons.IdempotencyUnavailable] =
            "The decision could not be recorded right now; nothing was done. Try again shortly with the same review id.",
        [DecisionCommitReasons.ReviewChanged] =
            "This review id was already used for a different decision. Send a new decision with a new review id.",
        [DecisionCommitReasons.NotResumable] =
            "This failed decision cannot be resumed. Check the item in Spaarke to see what was done.",
        [DecisionCommitReasons.NotAuthorized] =
            "You do not have permission to make this decision. Ask someone with access to decide it.",
        [DecisionCommitReasons.CoreChanged] =
            "The item's matter or project changed during the review; nothing was done. Read the decision plan again and send a new decision.",
        [DecisionCommitReasons.InquiryDispositionRequired] =
            "This To Do is completed by recording the inquiry's outcome, not by Mark complete. Skip Mark complete and record the outcome in Spaarke.",
        [DecisionCommitReasons.SignalSubjectMissing] =
            "This item's subject no longer exists, so it cannot be decided. Refresh the decision list.",
        [DecisionCommitReasons.DecidedInInvoiceReview] =
            "Invoice and invoice-line items are decided in Invoice Review only; nothing was done. Open the invoice in Invoice Review to decide it.",

        // A taken action's own refusal or failure (DecisionActionReasons), returned by the commit route unchanged.
        [DecisionActionReasons.InvalidParameter] =
            "A value for this action is not valid. Check the action's values against the decision plan, then send again.",
        [DecisionActionReasons.SubjectUnsupported] =
            "This action cannot be taken on this item. Skip it or choose another action.",
        [DecisionActionReasons.NotAuthorized] =
            "You do not have permission to take this action. Skip it, or ask someone with access to take it.",
        [DecisionActionReasons.NotFound] =
            "A record this action needs was not found or is not available to you. Choose another value.",
        [DecisionActionReasons.InvalidState] =
            "A record this action changes is not in a state that allows it. Read the item again and choose another action.",
        [DecisionActionReasons.Conflict] =
            "A record this action changes was changed by someone else. Read the item again, then try again.",
        [DecisionActionReasons.WriteFailed] =
            "This action's write failed and no decision was recorded. Check the item in Spaarke before trying again.",
        [DecisionActionReasons.RecipientUnresolved] =
            "A message recipient could not be resolved. Choose a recipient who has an email address.",
        [DecisionActionReasons.BudgetNotOnMatter] =
            "The budget is not on this item's matter. Choose a budget from the item's matter.",
        [DecisionActionReasons.CallerUnresolved] =
            "Your Spaarke user could not be identified for this action. Sign in again with your Spaarke work account, then retry.",
        [DecisionActionReasons.OwnerUnresolved] =
            "The person chosen for this action could not be resolved. Choose another person.",
        [DecisionActionReasons.BudgetAmountNotWritten] =
            "The budget revision was written but its amount was not. Check the budget in Spaarke before trying again.",
    };
}
