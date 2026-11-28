using Sprk.Bff.Api.Services.Intake;

namespace Sprk.Bff.Api.Api.Intake;

/// <summary>
/// The claim route's record-level gate (ADR-008, spaarke-intake-pipeline-r1 task 017): <see cref="IntakeApprovalService.AuthorizeAsync"/>
/// for <see cref="IntakeApprovalAction.Claim"/>, as the caller, before the handler; a refusal never reaches the handler. Fails closed: a
/// missing or unparsable route id, or a missing service, is the uniform 404.
/// </summary>
public sealed class IntakeApprovalAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var approvals = http.RequestServices.GetService<IntakeApprovalService>();
        if (approvals is null
            || http.Request.RouteValues["requestId"]?.ToString() is not { } raw
            || !Guid.TryParse(raw, out var requestId)
            || requestId == Guid.Empty)
        {
            return IntakeApprovalEndpoints.ToProblem(new IntakeApprovalResult(IntakeApprovalOutcome.NotFound), http);
        }

        var authorized = await approvals.AuthorizeAsync(http, requestId, IntakeApprovalAction.Claim, http.RequestAborted).ConfigureAwait(false);
        return authorized.IsDone ? await next(context).ConfigureAwait(false) : IntakeApprovalEndpoints.ToProblem(authorized, http);
    }
}
