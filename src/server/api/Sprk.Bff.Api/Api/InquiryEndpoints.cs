using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Api;

/// <summary>The body of <c>POST /api/v1/inquiries/{serviceRequestId}/disposition</c>.</summary>
/// <param name="ReplyCommunicationId">The inbound reply the person is answering from; it must be filed against the inquiry in the route.</param>
/// <param name="Disposition">The <c>sprk_servicerequest.sprk_disposition</c> option value: 100000000 Write-off, 100000001 Budget Revised, 100000002 Scope Approved, 100000003 No Action.</param>
public sealed record RecordDispositionRequest(Guid ReplyCommunicationId, int Disposition);

/// <summary>
/// Records how a budget Inquiry turned out (ontology task 071, spec FR-37, decision D-111). A HUMAN reads the reply and
/// chooses the disposition; nothing infers it (ADR-013). Recording closes the Inquiry and completes its "Record the outcome"
/// To Do.
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> <c>RequireAuthorization</c>, then a record-level filter: the caller needs Write on the
/// service request named in the route (no Read: the uniform 404, the same as an absent row). The write itself runs AS THE
/// CALLER (<c>IDataverseUserClient</c>), so Dataverse enforces Write on <c>sprk_servicerequest</c> a second time and there is
/// no application-identity write path.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>ChildRecordEndpoints</c> creates and re-files
/// child rows only and <c>EventEndpoints</c> completes events; nothing records an Inquiry's outcome, and the reply, the
/// service request, the To Do and the association status must change together. Extension: no existing route can take a
/// typed outcome with a precondition on the reply without becoming a generic update; the route carries no logic of its own
/// and calls <see cref="InquiryDispositionService"/>. Cost of doing nothing: success criterion 10 (the reply resolves the
/// Inquiry with a disposition) has no way to happen, since no AI sets it and a direct form edit would leave the To Do open
/// and the reply's association unconfirmed.</para>
/// <para><b>Placement (bff-extensions.md).</b> In the BFF: a request/response write under the caller's own identity, no
/// background work, no package, no AI capability.</para>
/// </remarks>
public static class InquiryEndpoints
{
    internal const string ServiceRequestEntitySet = "sprk_servicerequests";

    public static void MapInquiryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/inquiries")
            .WithTags("Inquiries")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapPost("/{serviceRequestId:guid}/disposition", RecordDispositionAsync)
            .AddRecordRouteAccessAuthorizationFilter("write", ServiceRequestEntitySet, "serviceRequestId")
            .WithName("RecordInquiryDisposition")
            .WithSummary("Record how a budget inquiry turned out")
            .WithDescription("The caller (who needs Write on the inquiry) names the inbound reply they are answering from and "
                + "one of the four dispositions. The reply must be Incoming and filed against this inquiry, and the inquiry must be "
                + "an open outbound one. The inquiry is closed with the disposition, its Record-the-outcome To Do is completed and "
                + "a Suggested association of the reply is confirmed. The first disposition wins.")
            .Produces<RecordDispositionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public sealed record RecordDispositionResponse(Guid ServiceRequestId, bool TodoCompleted);

    internal static async Task<IResult> RecordDispositionAsync(
        Guid serviceRequestId,
        [FromBody] RecordDispositionRequest body,
        [FromServices] InquiryDispositionService inquiries,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var result = await inquiries.RecordDispositionAsync(serviceRequestId, body.ReplyCommunicationId, body.Disposition, ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            InquiryResolutionOutcome.Resolved => Results.Ok(new RecordDispositionResponse(serviceRequestId, result.TodoCompleted)),
            InquiryResolutionOutcome.NotFound => ProblemDetailsHelper.UniformRecordNotFound(httpContext),
            InquiryResolutionOutcome.Invalid => Problem(StatusCodes.Status400BadRequest, "inquiry.invalid", result.Error),
            InquiryResolutionOutcome.Denied => Problem(StatusCodes.Status403Forbidden, "inquiry.denied", result.Error),
            InquiryResolutionOutcome.NotAReply => Problem(StatusCodes.Status409Conflict, "inquiry.not_a_reply", result.Error),
            InquiryResolutionOutcome.ReplyNotForInquiry => Problem(StatusCodes.Status409Conflict, "inquiry.reply_mismatch", result.Error),
            InquiryResolutionOutcome.NotAnInquiry => Problem(StatusCodes.Status409Conflict, "inquiry.not_an_inquiry", result.Error),
            InquiryResolutionOutcome.AlreadyResolved => Problem(StatusCodes.Status409Conflict, "inquiry.already_resolved", result.Error),
            _ => Problem(StatusCodes.Status502BadGateway, "inquiry.failed", result.Error),
        };
    }

    private static IResult Problem(int status, string code, string? detail) =>
        Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status400BadRequest => "Invalid request",
                StatusCodes.Status403Forbidden => "Not permitted",
                StatusCodes.Status409Conflict => "Disposition not recorded",
                _ => "Disposition not recorded",
            },
            detail: detail,
            extensions: new Dictionary<string, object?> { ["reasonCode"] = code });
}
