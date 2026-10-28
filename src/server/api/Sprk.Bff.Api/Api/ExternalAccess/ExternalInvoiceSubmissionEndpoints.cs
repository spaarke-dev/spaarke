// ebilling-r1 task 044 (2026-10-16) — FR-17 SPA channel, NFR-10 firm scope.
//
// POST /api/v1/external/{matters|projects|workassignments}/{id}/invoice-submissions  submit a PDF invoice (202)
// GET  /api/v1/external/invoice-submissions/{id}                                      one submission's status
// GET  /api/v1/external/invoice-submissions                                           the caller's firms' submissions (051 resume / flag probe)
//
// Mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so every route inherits the ExternalCollaboration
// policy and the group-level CallerPrincipalAuthorizationFilter. Each route then carries ExternalInvoiceSubmissionAuthorizationFilter
// (the core record and the firm, ADR-008) and, after it, RequireEbillingEnabled (ADR-018: the flag never replaces authorization).
// Workforce ceiling (uac-r2 task 186): each route declares its set with RequireWorkforceCeiling (WorkforceRoleCeilingGate). The group's
// ceiling filter runs BEFORE RequireEbillingEnabled, so with the flag off a workforce contact whose role lacks a privilege gets the
// ceiling's 403 instead of the flag's answer (known limit; the order is not changed).
// The write itself is InvoiceSubmissionService — the same operation the internal route calls (D-24).

using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.SubmitInvoiceToMatter)
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the bearer token + the caller's record rights, not a form token
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForMatter")
            .WithSubmitContract("matter");

        externalGroup.MapPost("/projects/{id:guid}/invoice-submissions", SubmitAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.Submit, InvoiceCoreRecordKind.Project)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.SubmitInvoiceToProject)
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForProject")
            .WithSubmitContract("project");

        externalGroup.MapPost("/workassignments/{id:guid}/invoice-submissions", SubmitAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.Submit, InvoiceCoreRecordKind.WorkAssignment)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.SubmitInvoiceToWorkAssignment)
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .RequireRateLimiting("dataverse-query")
            .WithName("SubmitExternalInvoiceForWorkAssignment")
            .WithSubmitContract("work assignment");

        externalGroup.MapGet("/invoice-submissions/{id:guid}", GetAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.ReadOne)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceSubmissions)
            .RequireEbillingEnabled()
            .WithName("GetExternalInvoiceSubmission")
            .WithSummary("Read one of the caller's firm's invoice submissions")
            .Produces<InvoiceSubmissionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        externalGroup.MapGet("/invoice-submissions", ListAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.List)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ReadInvoiceSubmissions)
            .RequireEbillingEnabled()
            .WithName("ListExternalInvoiceSubmissions")
            .WithSummary("List the caller's firms' invoice submissions on records the caller can read, newest first")
            .Produces<IReadOnlyList<InvoiceSubmissionView>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // D-106 (task 051): the picker's link read. The guid constraint on {id} keeps "firm-records" from matching the read-one route.
        externalGroup.MapGet("/invoice-submissions/firm-records", FirmRecordsAsync)
            .AddExternalInvoiceSubmissionAuthorizationFilter(ExternalInvoiceSubmissionAccess.FirmRecords)
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.ListInvoiceFirmRecords)
            .RequireEbillingEnabled()
            .RequireRateLimiting("dataverse-query")
            .WithName("ListExternalInvoiceFirmRecords")
            .WithSummary("Which of the caller's readable matters, projects and work assignments are linked to one of the caller's firms")
            .Produces<ExternalInvoiceFirmRecords>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static ExternalInvoiceSubmissionScope? Scope(HttpContext http) =>
        http.Items[ExternalInvoiceSubmissionScope.HttpContextItemsKey] as ExternalInvoiceSubmissionScope;

    private static IResult NoScope() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "Your access could not be determined.");

    private static async Task<IResult> SubmitAsync(HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        // The filter decided the record and the firm; a handler reached without that decision refuses (fail closed).
        if (Scope(http) is not { FirmId: { } firmId, CoreRecord: { } core } scope)
        {
            return NoScope();
        }

        var form = await InvoiceSubmissionHttp.ReadSubmissionFormAsync(http.Request, service, StatusUrl, ct);
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

        return InvoiceSubmissionHttp.ToResult(outcome, StatusUrl, http.TraceIdentifier);
    }

    private static IResult GetAsync(HttpContext http) =>
        Scope(http) is { Submission: { } view } ? Results.Ok(ExternalView(view)) : NoScope();

    /// <summary>
    /// ebilling-r1 task 098 review round 4b: the view an external caller is answered with. The submission's document link is read
    /// to decide the submission and is never returned to an external caller (no external client reads it).
    /// </summary>
    internal static InvoiceSubmissionView ExternalView(InvoiceSubmissionView view) => view with { DocumentId = null };

    /// <summary>The most record ids returned per kind (matters, projects, work assignments): the picker's own page is 200 rows per source.</summary>
    internal const int MaxFirmRecordsPerKind = 200;

    /// <summary>
    /// The most of the caller's readable records of one kind whose links are read (<c>GetReferencedOrganizationIdsAsync</c> reads 50 records per
    /// Dataverse request, so this bounds a call to 10 requests per kind however large the caller's access set is).
    /// </summary>
    internal const int MaxFirmRecordCandidatesPerKind = 500;

    /// <summary>
    /// The picker's link read (D-106): which of the caller's OWN readable matters, projects and work assignments the chosen firm is linked to.
    /// <para>
    /// <b>Scope.</b> The candidates are the caller's composed, Read-gated record sets on <see cref="CallerPrincipal"/> (the access model's
    /// post-veto answer, the same sets every other external read route scopes by), narrowed to the records the caller could submit on (Read and
    /// Create, the submit filter's own test), so a record the caller has no grant on is never read and never named, even when the firm is linked to
    /// it: the list is the INTERSECTION of "the caller can submit on it" and "the firm is one of its assigned law firms", never "every record of
    /// the firm". The firm itself was checked by the route filter against the caller's own ACTIVE memberships, and a
    /// systemuser was refused there, before anything is read.
    /// </para>
    /// <para>
    /// <b>Link.</b> The same definition the submit gate applies (<see cref="InvoiceSubmissionService.LinkOf"/>), batched. A record whose links
    /// could not be read is left out and <c>Partial</c> says so (fail closed).
    /// </para>
    /// <para>
    /// <b>Bounds.</b> At most <see cref="MaxFirmRecordCandidatesPerKind"/> readable records per kind are examined (in id order, so the answer is
    /// stable) and at most <see cref="MaxFirmRecordsPerKind"/> ids per kind are returned; either cut sets <c>Truncated</c>, so the list is never
    /// presented as complete when it is not.
    /// </para>
    /// </summary>
    private static async Task<IResult> FirmRecordsAsync(
        HttpContext http, ExternalParticipationService participations, CancellationToken ct)
    {
        if (Scope(http) is not { FirmId: { } firmId } scope)
        {
            return NoScope();
        }

        var principal = scope.Principal;
        var partial = false;
        var truncated = false;

        async Task<IReadOnlyList<Guid>> LinkedAsync(InvoiceCoreRecordKind kind, IEnumerable<Guid> readable)
        {
            // Only records the caller could submit on (Read + Create, the submit filter's own test): a Read-only record would be offered and then refused.
            var rootType = new InvoiceCoreRecord(kind, Guid.Empty).GrantRootType;
            var candidates = readable
                .Where(id => id != Guid.Empty && principal.RightsOn(rootType, id).HasFlag(ExternalInvoiceSubmissionAuthorizationFilter.SubmitRights))
                .Distinct().Order().ToList();
            if (candidates.Count > MaxFirmRecordCandidatesPerKind)
            {
                truncated = true;
                candidates = candidates.GetRange(0, MaxFirmRecordCandidatesPerKind);
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            IReadOnlyDictionary<Guid, ReferencedOrganizations> references;
            try
            {
                references = await participations.GetReferencedOrganizationIdsAsync(new InvoiceCoreRecord(kind, Guid.Empty).EntityLogicalName, candidates, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                partial = true; // none of this kind's links could be read: none is offered
                return Array.Empty<Guid>();
            }

            var linked = new List<Guid>();
            foreach (var id in candidates)
            {
                switch (InvoiceSubmissionService.LinkOf(references is not null && references.TryGetValue(id, out var record) ? record : null, firmId))
                {
                    case InvoiceFirmLink.Linked when linked.Count < MaxFirmRecordsPerKind:
                        linked.Add(id);
                        break;
                    case InvoiceFirmLink.Linked:
                        truncated = true;
                        break;
                    case InvoiceFirmLink.Unreadable:
                        partial = true;
                        break;
                }
            }

            return linked;
        }

        var matters = await LinkedAsync(InvoiceCoreRecordKind.Matter, principal.AccessibleMatterIds);
        var projects = await LinkedAsync(InvoiceCoreRecordKind.Project, principal.GetAccessibleProjectIds());
        var workAssignments = await LinkedAsync(InvoiceCoreRecordKind.WorkAssignment, principal.AccessibleWorkAssignmentIds);
        return Results.Ok(new ExternalInvoiceFirmRecords(firmId, matters, projects, workAssignments, partial, truncated));
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, InvoiceSubmissionService service,
        [Microsoft.AspNetCore.Mvc.FromServices] ExternalInvoiceSubmissionHiding? hideSubmissions, ILogger<Program> logger,
        CancellationToken ct)
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
            var visible = await VisibleSubmissionsAsync(principal, views, hideSubmissions, logger, ct);
            return Results.Ok(visible.Select(ExternalView).ToList());
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }

    /// <summary>
    /// ebilling-r1 task 098 (model 9.1, (a')), review round 4b: THE decision of BOTH external submission reads (this list and the
    /// single read, <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/>): the submissions of <paramref name="views"/> the
    /// caller may see, in order, decided in ONE call of the caller-aware seam (<see cref="ExternalInvoiceSubmissionHiding"/>:
    /// with an invoice, the invoice predicate; without one, the submission's own source). A withheld submission is omitted, never
    /// marked. No seam withholds every asked submission (fail closed; logged at Error, a registration defect). A failed read
    /// THROWS <see cref="InvoiceSubmissionService.ReadUnavailable"/> (EB-D-169: the routes answer a retryable 503, never 200
    /// <c>[]</c> and never the not-found 404); it never shows a row.
    /// </summary>
    internal static async Task<IReadOnlyList<InvoiceSubmissionView>> VisibleSubmissionsAsync(
        CallerPrincipal principal, IReadOnlyList<InvoiceSubmissionView> views, ExternalInvoiceSubmissionHiding? hideSubmissions,
        ILogger logger, CancellationToken ct)
    {
        if (views.Count == 0)
            return views;

        if (hideSubmissions is null)
        {
            logger.LogError(
                "[EBILLING-SUBMIT] The submission decision (ExternalInvoiceSubmissionHiding) is not registered: {Count} submission(s) "
                + "withheld (fail closed).", views.Count);
            return Array.Empty<InvoiceSubmissionView>();
        }

        var decision = await hideSubmissions(principal, views, ct).ConfigureAwait(false);
        if (decision.Faulted)
        {
            // EB-D-169: the decision could not be read. A retryable 503 for the whole page; no row is shown.
            throw InvoiceSubmissionService.ReadUnavailable();
        }

        var kept = views.Where(v => decision.IsVisible(v.SubmissionId)).ToList();
        if (kept.Count < views.Count)
        {
            logger.LogInformation(
                "[EBILLING-SUBMIT] {Withheld} of {Count} submission(s) withheld: decided by their invoice or their source ({Reasons}).",
                views.Count - kept.Count, views.Count, decision.HiddenSummary());
        }

        return kept;
    }
}

/// <summary>
/// The body of <c>GET /api/v1/external/invoice-submissions/firm-records</c> (ebilling-r1 task 051, D-106): the ids, among the caller's own readable
/// records, that the named firm is linked to. <paramref name="Partial"/> is true when some record's links could not be read (those are left out).
/// <paramref name="Truncated"/> is true when the list was cut at its bound (too many readable records to examine, or more linked records than are
/// returned), so a linked record may be missing: a client must say so and never present the list as complete.
/// </summary>
public sealed record ExternalInvoiceFirmRecords(
    Guid FirmId, IReadOnlyList<Guid> Matters, IReadOnlyList<Guid> Projects, IReadOnlyList<Guid> WorkAssignments, bool Partial, bool Truncated);
