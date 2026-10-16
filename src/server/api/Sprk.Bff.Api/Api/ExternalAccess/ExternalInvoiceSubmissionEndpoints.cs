// ebilling-r1 task 044 (2026-10-16) — FR-17 SPA channel, NFR-10 firm scope.
//
// POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions  submit a PDF invoice (202)
// GET  /api/v1/external/invoice-submissions/{id}                                      one submission's status
// GET  /api/v1/external/invoice-submissions                                           the caller's firms' submissions (051 resume / flag probe)
//
// Mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so every route inherits the ExternalCollaboration
// policy and the group-level CallerPrincipalAuthorizationFilter. Each route then carries ExternalInvoiceSubmissionAuthorizationFilter
// (the core record and the firm, ADR-008) and, after it, RequireEbillingEnabled (ADR-018: the flag never replaces authorization).
// The write itself is InvoiceSubmissionService — the same operation the internal route calls (D-24).

using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The external (SPA) invoice submission routes. See the file header.</summary>
public static class ExternalInvoiceSubmissionEndpoints
{
    /// <summary>The status route of a submission on the external surface.</summary>
    internal static string StatusUrl(Guid submissionId) => $"/api/v1/external/invoice-submissions/{submissionId}";

    /// <summary>Registers the routes on the shared external collaboration group (its policy and principal filter are inherited).</summary>
    public static RouteGroupBuilder MapExternalInvoiceSubmissionEndpoints(this RouteGroupBuilder externalGroup)
    {
        // Three literal chains (not a loop): the RouteAuthorizationGuard scanner reads each route's path and attachments from its own chain.
        externalGroup.MapPost("/matters/{id:guid}/invoice-submissions", SubmitAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.Submit, InvoiceCoreRecordKind.Matter)
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the bearer token + the caller's record rights, not a form token
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionEndpoints.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForMatter")
            .WithSubmitContract("matter");

        externalGroup.MapPost("/projects/{id:guid}/invoice-submissions", SubmitAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.Submit, InvoiceCoreRecordKind.Project)
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionEndpoints.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForProject")
            .WithSubmitContract("project");

        externalGroup.MapPost("/workassignments/{id:guid}/invoice-submissions", SubmitAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.Submit, InvoiceCoreRecordKind.WorkAssignment)
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionEndpoints.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForWorkAssignment")
            .WithSubmitContract("work assignment");

        externalGroup.MapGet("/invoice-submissions/{id:guid}", GetAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.ReadOne)
            .RequireEbillingEnabled()
            .WithName("GetExternalInvoiceSubmission")
            .WithSummary("Read one of the caller's firm's invoice submissions")
            .Produces<InvoiceSubmissionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        externalGroup.MapGet("/invoice-submissions", ListAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.List)
            .RequireEbillingEnabled()
            .WithName("ListExternalInvoiceSubmissions")
            .WithSummary("List the caller's firms' invoice submissions on records the caller can read, newest first")
            .Produces<IReadOnlyList<InvoiceSubmissionView>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static ExternalInvoiceSubmissionScope? Scope(HttpContext http) =>
        http.Items[ExternalInvoiceSubmissionScope.HttpContextItemsKey] as ExternalInvoiceSubmissionScope;

    private static IResult NoScope() =>
        InvoiceSubmissionEndpoints.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "Your access could not be determined.");

    private static async Task<IResult> SubmitAsync(HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        // The filter decided the record and the firm; a handler reached without that decision refuses (fail closed).
        if (Scope(http) is not { FirmId: { } firmId, CoreRecord: { } core } scope)
        {
            return NoScope();
        }

        var form = await InvoiceSubmissionEndpoints.ReadSubmissionFormAsync(http.Request, service, ct);
        if (form.Refusal is { } refusal)
        {
            return refusal;
        }

        var outcome = await service.SubmitAsync(new InvoiceSubmissionRequest
        {
            FirmId = firmId,
            CoreRecord = core,
            Channel = InvoiceSubmissionChannel.Spa,
            FirmInvoiceNumber = form.FirmInvoiceNumber,
            PoNumber = form.PoNumber,
            File = form.File,
            ExternalCaller = scope.Principal,
            CorrelationId = http.TraceIdentifier,
        }, ct);

        return InvoiceSubmissionEndpoints.ToResult(outcome, StatusUrl, http.TraceIdentifier);
    }

    private static IResult GetAsync(HttpContext http) =>
        Scope(http) is { Submission: { } view } ? Results.Ok(view) : NoScope();

    private static async Task<IResult> ListAsync(HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        if (Scope(http) is not { } scope)
        {
            return NoScope();
        }

        var principal = scope.Principal;
        try
        {
            var views = await service.ListAsync(
                scope.FirmIds,
                principal.AccessibleMatterIds,
                principal.GetAccessibleProjectIds().ToList(),
                principal.AccessibleWorkAssignmentIds,
                ct);
            return Results.Ok(views);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionEndpoints.ReadFault(ex);
        }
    }
}
