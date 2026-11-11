// ebilling-r1 task 130b (2026-11-08) — FR-38 firm-visible outcome, NFR-10 / D-70 firm scope.
//
// GET /api/v1/external/ebilling/invoices/{invoiceId}/outcome         the firm-visible outcome of one invoice
// GET /api/v1/external/ebilling/submissions/{submissionId}/outcome   the firm-visible outcome of one submission
//
// Mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so every route inherits the ExternalCollaboration policy and the
// group-level CallerPrincipalAuthorizationFilter. Each route then carries FirmOutcomeAuthorizationFilter (the firm, the core record and the (a')
// visibility decision, ADR-008: the projection has no caller check) and, after it, RequireEbillingEnabled (ADR-018: the flag never replaces
// authorization). The handler runs only behind the filter and projects the id the filter decided.
// Workforce ceiling (uac-r2 task 186, run first by the group's filter, independent of the id): each route declares Read on every table it returns
// (WorkforceRoleCeilingGate.ReadInvoiceOutcome / ReadSubmissionOutcome: the invoice, line, Decision Record and Signal tables); a workforce
// SYSTEMUSER is refused by the route's filter whatever its role. Rule citations are not a table the route returns as such: a citation is shown
// only as the policy's firm flag publishes it (FirmFindingsProjection).

using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The external firm outcome routes. See the file header.</summary>
public static class FirmOutcomeEndpoints
{
    /// <summary>Registers the routes on the shared external collaboration group (its policy and principal filter are inherited).</summary>
    public static RouteGroupBuilder MapFirmOutcomeEndpoints(this RouteGroupBuilder externalGroup)
    {
        // Two literal chains (not a loop): the RouteAuthorizationGuard scanner reads each route's path and attachments from its own chain.
        externalGroup.MapGet("/ebilling/invoices/{invoiceId:guid}/outcome", GetInvoiceOutcomeAsync)
            .AddFirmOutcomeAuthorizationFilter(FirmOutcomeSubject.Invoice)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceOutcome)
            .RequireEbillingEnabled()
            .WithName("GetExternalInvoiceOutcome")
            .WithSummary("The firm-visible outcome of one of the caller's firm's invoices")
            .Produces<FirmOutcome>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        externalGroup.MapGet("/ebilling/submissions/{submissionId:guid}/outcome", GetSubmissionOutcomeAsync)
            .AddFirmOutcomeAuthorizationFilter(FirmOutcomeSubject.Submission)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadSubmissionOutcome)
            .RequireEbillingEnabled()
            .WithName("GetExternalSubmissionOutcome")
            .WithSummary("The firm-visible outcome of one of the caller's firm's invoice submissions")
            .Produces<FirmOutcome>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static FirmOutcomeScope? Scope(HttpContext http) =>
        http.Items[FirmOutcomeScope.HttpContextItemsKey] as FirmOutcomeScope;

    private static async Task<IResult> GetInvoiceOutcomeAsync(HttpContext http, FirmFindingsProjection projection, CancellationToken ct)
    {
        // The filter decided the invoice; a handler reached without that decision refuses (fail closed).
        if (Scope(http) is not { Subject: FirmOutcomeSubject.Invoice, SubjectId: var invoiceId })
        {
            return FirmOutcomeErrors.Forbidden();
        }

        return ToResult(http, await projection.GetForInvoiceAsync(invoiceId, ct));
    }

    private static async Task<IResult> GetSubmissionOutcomeAsync(HttpContext http, FirmFindingsProjection projection, CancellationToken ct)
    {
        if (Scope(http) is not { Subject: FirmOutcomeSubject.Submission, SubjectId: var submissionId, InvoiceDecided: var invoiceDecided })
        {
            return FirmOutcomeErrors.Forbidden();
        }

        // Fix round 2 (F1): whether the filter decided the submission's invoice. When it did not (the invoice did not exist yet), the projection
        // reads nothing of that invoice: an in-progress submission is status only, and an accepted one is a 503 so the retry is decided afresh.
        return ToResult(http, await projection.GetForSubmissionAsync(submissionId, invoiceDecided, ct));
    }

    /// <summary>
    /// A projection that read nothing (the id vanished since the filter decided it) is the same 403 as any other refusal. One that did not
    /// complete because a read FAULTED, or because the filter did not decide the invoice it would show (<see cref="FirmOutcome.Degraded"/>, or
    /// <see cref="FirmOutcomeKind.Unavailable"/>), is a retryable 503, never a 200 with an empty list (130a). A closed state at the time of the
    /// request (<see cref="FirmOutcomeKind.Closed"/>: a failed submission, a cancelled or superseded invoice, an unknown status) is a 200 with the
    /// status only. Anything else is the outcome. Not cacheable: it names what a reviewer decided.
    /// </summary>
    private static IResult ToResult(HttpContext http, FirmOutcome? outcome)
    {
        if (outcome is null)
        {
            return FirmOutcomeErrors.Forbidden();
        }

        if (outcome.Degraded || outcome.Kind == FirmOutcomeKind.Unavailable)
        {
            return FirmOutcomeErrors.Unavailable();
        }

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(outcome);
    }
}

/// <summary>The stable codes and the two bodies of the firm outcome routes (ADR-019). A refusal says nothing about WHY.</summary>
internal static class FirmOutcomeErrors
{
    /// <summary>403: not this caller's to see (any reason, including that it does not exist).</summary>
    public const string ForbiddenCode = "ebilling.firm-outcome.forbidden";

    /// <summary>503: the outcome or the check could not be completed right now.</summary>
    public const string UnavailableCode = "ebilling.firm-outcome.unavailable";

    public static IResult Forbidden() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "You cannot view this outcome.",
        extensions: new Dictionary<string, object?> { ["errorCode"] = ForbiddenCode });

    public static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
        detail: "The outcome could not be read right now. Try again shortly.",
        extensions: new Dictionary<string, object?> { ["errorCode"] = UnavailableCode });
}
