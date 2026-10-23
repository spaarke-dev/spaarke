using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The blocked-submission routes (ebilling-r1 task 105; FR-19, FR-37, D-24, D-39, D-73, D-76, D-101): read a blocked internal submission,
/// Override it (privilege "eBilling Override", a reason, one person), or Return it to the firm (no privilege). ADR-001 (Minimal API) and
/// ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Authorization, in two parts.</b> Each route carries its own <see cref="FinanceAuthorizationFilter"/> AS THE CALLER on the
/// submission named in the path (Read for the read, Read and Write for the two decisions, <c>ebilling.submission.decide</c>), then the
/// eBilling flag (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization. The override additionally needs the caller's
/// Dataverse table privilege <see cref="EbillingOverridePrivilege.PrivilegeName"/>, which the SERVICE asks on every call
/// (<see cref="SubmissionOverrideService.OverrideAsync"/>): a filter's denial is the generic
/// <c>sdap.access.deny.insufficient_privilege</c>, and the acceptance criterion is a stable <c>ebilling.override.not_permitted</c> that also
/// covers a privilege probe that faults. Return needs only the rights on the submission (D-73). Both decisions resolve the open Signals on
/// the invoice and its lines (D-122), closed by the app-identity writer, so when there are any the service also asks, as the caller, for
/// AppendTo on the invoice's core record and Write on each Signal's subject: 403 <c>ebilling.blocked.not_authorized</c> without them, nothing
/// written.</para>
/// <para><b>Inputs.</b> The submission id is the route; the only body field is <c>reason</c>. The decider is the authenticated caller, and the
/// invoice, the findings and the decision id are all derived server-side; none comes from the request.</para>
/// <para><b>Responses.</b> 200 with <see cref="BlockedDecisionResponse"/> (a repeat is 200 with <c>alreadyDecided</c> and writes nothing); refusals
/// are ProblemDetails with <c>errorCode</c> (<see cref="BlockedSubmissionErrors"/>); 401 unauthenticated; 503 <c>ebilling.disabled</c> when off.</para>
/// </remarks>
public static class SubmissionOverrideEndpoints
{
    internal const string Route = "/api/finance/submissions";

    public static IEndpointRouteBuilder MapSubmissionOverrideEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/submissions/{submissionId}/blocked: Read on the submission as the caller; an unreadable and an absent row are the same 404.
        group.MapGet("/{submissionId:guid}/blocked", GetBlockedAsync)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "submissionId", FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetBlockedSubmission")
            .WithSummary("Read a blocked internal submission: state, blocking findings and what this caller may do")
            .Produces<BlockedSubmissionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/submissions/{submissionId}/override: Read and Write on the submission, then the privilege inside the service.
        group.MapPost("/{submissionId:guid}/override", OverrideAsync)
            .AddFinanceAuthorizationFilter("ebilling.submission.decide", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "submissionId")
            .RequireEbillingEnabled()
            .WithName("OverrideBlockedSubmission")
            .WithSummary("Accept a blocked internal submission despite its blocking failure (eBilling Override privilege, reason required)")
            .Produces<BlockedDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/submissions/{submissionId}/return: Read and Write on the submission; no privilege (D-73).
        group.MapPost("/{submissionId:guid}/return", ReturnAsync)
            .AddFinanceAuthorizationFilter("ebilling.submission.decide", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "submissionId")
            .RequireEbillingEnabled()
            .WithName("ReturnBlockedSubmission")
            .WithSummary("Return a blocked internal submission to the firm (reason required; no privilege)")
            .Produces<BlockedDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetBlockedAsync(
        Guid submissionId, HttpContext http, SubmissionOverrideService service, CancellationToken ct)
    {
        try
        {
            var view = await service.GetAsync(submissionId, TokenHelper.ExtractBearerTokenOrNull(http), ct);
            return view is null ? FinanceAuthorizationFilter.UniformRecordNotFound(http) : Results.Ok(view);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }

    private static async Task<IResult> OverrideAsync(
        Guid submissionId, BlockedDecisionRequest? body, HttpContext http, SubmissionOverrideService service, CancellationToken ct) =>
        ToResult(await service.OverrideAsync(submissionId, body?.Reason, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    private static async Task<IResult> ReturnAsync(
        Guid submissionId, BlockedDecisionRequest? body, HttpContext http, SubmissionOverrideService service, CancellationToken ct) =>
        ToResult(await service.ReturnAsync(submissionId, body?.Reason, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    /// <summary>200 with the outcome (a repeat is 200 with <c>alreadyDecided</c>), or the refusal as ProblemDetails (ADR-019).</summary>
    internal static IResult ToResult(BlockedDecisionResult result, HttpContext http) => result.Kind switch
    {
        BlockedDecisionKind.Decided or BlockedDecisionKind.AlreadyDecided => Results.Ok(new BlockedDecisionResponse(
            result.SubmissionId, result.InvoiceId, result.Outcome!, result.RecordId, result.Kind == BlockedDecisionKind.AlreadyDecided)),
        _ => Refused(result, http),
    };

    private static IResult Refused(BlockedDecisionResult result, HttpContext http)
    {
        var extensions = new Dictionary<string, object?> { ["errorCode"] = result.ErrorCode, ["correlationId"] = http.TraceIdentifier };
        if (result.StatusCode == StatusCodes.Status403Forbidden)
        {
            // The same stable code under the key the other 403s of this API use, so a client matching on reasonCode finds it too.
            extensions["reasonCode"] = result.ErrorCode;
        }

        return Results.Problem(statusCode: result.StatusCode, detail: result.Detail, extensions: extensions);
    }
}

/// <summary>The 200 body of an override or a return. <see cref="AlreadyDecided"/> is true for a repeat, which wrote nothing.</summary>
/// <param name="SubmissionId">The submission.</param>
/// <param name="InvoiceId">The invoice the submission created (D-101), now released (override) or returned.</param>
/// <param name="Outcome"><c>Overridden</c> or <c>Returned</c>.</param>
/// <param name="RecordId">The Decision Record; null on a repeat that did not look it up.</param>
/// <param name="AlreadyDecided">True when the submission had already been decided this way.</param>
public sealed record BlockedDecisionResponse(Guid SubmissionId, Guid? InvoiceId, string Outcome, Guid? RecordId, bool AlreadyDecided);
