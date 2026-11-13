using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The two read models of one core record (ebilling-r1 task 084 Part A; procedure model 6.6, EB-D-218): the Procedures it is subject to and
/// the rules in force on a date. ADR-001 (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable
/// <c>errorCode</c>). One literal route chain per core-record type (<c>matters</c>, <c>projects</c>, <c>workassignments</c>): the table is
/// fixed by the route, never taken from input. Reads only; nothing is written.
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order, all as the caller and fail closed.</b> (1) Step 0 on the group: internal callers only, the uniform 404.
/// (2) Read on the core record (<c>finance.read</c>, <see cref="FinanceDenial.UniformNotFound"/>): unknown and unreadable answer the same
/// 404, byte for byte. (3) The eBilling flag. (4) In the service: ONE batched check that the caller can Read EVERY row the answer exposes
/// (EB-D-218); otherwise 503, never a partial list.</para>
/// <para><b>Refusal shapes.</b> 404: step 0, or the uniform record not found. 400 <c>procedures.invalid_query</c>: a <c>firmId</c> or <c>date</c> that does not parse (after
/// the record is known readable). 503 <c>ebilling.disabled</c>. 503 <c>procedures.unavailable</c>: the rows could not be read or resolved,
/// or the caller cannot Read a row the answer would expose, or the rights could not be established.</para>
/// <para><b>Query.</b> <c>firmId</c> (default: the record's billing firm, else none) and <c>date</c> (<c>yyyy-MM-dd</c>, default today UTC).</para>
/// <para><b>Write permissions (task 084g, EB-D-221).</b> A 200 also carries <c>permissions</c> {assignOrExclude, remove, change}: the caller's
/// rights for each write of <see cref="ProcedureAssignmentEndpoints"/> on this record, from those routes' own record and privilege
/// declarations (<see cref="ProcedureAssignmentEndpoints.WritePermissionsForCallerAsync"/>), asked after the list is complete. A hint for the
/// client only; a fault there makes a flag false and never changes the list or its status. The flags are computed per request for this
/// caller; the record-rights answer comes from the per-caller 60-second cache the write route also uses, and the privilege questions are
/// uncached.</para>
/// <para><b>Dry run (task 084d-2a, uac-r2 C5).</b> <c>POST .../procedures/dry-run?firmId=&amp;date=</c> with a
/// <see cref="ProcedureDryRunRequest"/>: both lists now, and "if saved now" with one proposed override, nothing written. Filters, in the write
/// routes' order: step 0; ONE record filter, uniform 404, asking <c>finance.read</c> and the write routes' record check
/// (<see cref="ProcedureAssignmentEndpoints.DryRunRecordTargets"/>); then (403) the planned writes' own subject and privilege checks, from the
/// declarations the write routes use (<see cref="ProcedureAssignmentEndpoints.DryRunTargets"/>); the kill switch; for a removal or a replaced
/// override, the caller's Read on the stored row's subject (the write routes' helper). Then 401 for a non-GUID caller, the query, the planning
/// (the write's own refusals) and ONE batched Read check over both sides (503 <c>procedures.unavailable</c>). Both checks are stricter than
/// the removal preview's: the dry run also needs <c>finance.read</c>, and a removal needs the remove route's privileges.</para>
/// <para><b>Assignable Procedures (task 084d-5a, EB-D-227..230).</b> <c>GET .../procedures/assignable?firmId=&amp;date=&amp;q=</c>: the active
/// Billing Procedures the record could be assigned (Follow latest), each as it would show if assigned now, for the lists' firm; at most
/// <see cref="RecordProcedureReadService.AssignableBound"/>, else <c>procedures</c> is null and <c>tooMany</c> is set. Nothing is written.
/// Filters: step 0; the dry run's record filter (<see cref="ProcedureAssignmentEndpoints.DryRunRecordTargets"/>, uniform 404); the assign
/// write's privileges (<see cref="ProcedureAssignmentEndpoints.AssignableTargets"/>, 403); the kill switch. Then 401 for a non-GUID caller,
/// the query (400 <c>procedures.invalid_query</c>, <c>q</c> included) and ONE batched Read check (503 <c>procedures.unavailable</c>).</para>
/// <para><b>Known limit.</b> These routes are internal-only (step 0). Opening them to external callers needs the external-visibility
/// predicate first (task 191).</para>
/// </remarks>
public static class RecordProcedureEndpoints
{
    private const string Route = "/api/finance";
    private const string RecordRouteKey = "recordId";

    /// <summary>The 400 <c>errorCode</c> of a <c>firmId</c> or <c>date</c> that does not parse.</summary>
    internal const string InvalidQueryErrorCode = "procedures.invalid_query";

    public static IEndpointRouteBuilder MapRecordProcedureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // ── Matter ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapGet("/matters/{recordId:guid}/procedures",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Procedures(http, service, ProcedureAssignmentEndpoints.MatterTable, FinanceAuthorizationFilter.MatterEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetMatterProcedures")
            .WithSummary("The Procedures this matter is subject to, for a firm and a date");

        group.MapGet("/matters/{recordId:guid}/rules-in-force",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Rules(http, service, ProcedureAssignmentEndpoints.MatterTable, FinanceAuthorizationFilter.MatterEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetMatterRulesInForce")
            .WithSummary("The rules in force on this matter, for a firm and a date");

        group.MapPost("/matters/{recordId:guid}/procedures/dry-run",
                (Guid recordId, ProcedureDryRunRequest body, string? firmId, string? date, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    DryRun(http, service, planner, ProcedureAssignmentEndpoints.MatterTable, recordId, body, firmId, date, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.DryRunTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheDryRunsOverridesSubject(ProcedureAssignmentEndpoints.MatterTable)
            .WithName("DryRunMatterProcedureOverride")
            .WithSummary("The Procedures and rules on this matter now, and if one proposed override were saved now (nothing is written)");

        group.MapGet("/matters/{recordId:guid}/procedures/assignable",
                (Guid recordId, string? firmId, string? date, string? q, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    Assignable(http, service, planner, ProcedureAssignmentEndpoints.MatterTable, recordId, firmId, date, q, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.AssignableTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("GetMatterAssignableProcedures")
            .WithSummary("The Procedures this matter could be assigned, each as it would show if assigned now (nothing is written)");
        // ── Project ─────────────────────────────────────────────────────────────────────────────────────────────
        group.MapGet("/projects/{recordId:guid}/procedures",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Procedures(http, service, ProcedureAssignmentEndpoints.ProjectTable, FinanceAuthorizationFilter.ProjectEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetProjectProcedures")
            .WithSummary("The Procedures this project is subject to, for a firm and a date");

        group.MapGet("/projects/{recordId:guid}/rules-in-force",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Rules(http, service, ProcedureAssignmentEndpoints.ProjectTable, FinanceAuthorizationFilter.ProjectEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetProjectRulesInForce")
            .WithSummary("The rules in force on this project, for a firm and a date");

        group.MapPost("/projects/{recordId:guid}/procedures/dry-run",
                (Guid recordId, ProcedureDryRunRequest body, string? firmId, string? date, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    DryRun(http, service, planner, ProcedureAssignmentEndpoints.ProjectTable, recordId, body, firmId, date, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.DryRunTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheDryRunsOverridesSubject(ProcedureAssignmentEndpoints.ProjectTable)
            .WithName("DryRunProjectProcedureOverride")
            .WithSummary("The Procedures and rules on this project now, and if one proposed override were saved now (nothing is written)");

        group.MapGet("/projects/{recordId:guid}/procedures/assignable",
                (Guid recordId, string? firmId, string? date, string? q, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    Assignable(http, service, planner, ProcedureAssignmentEndpoints.ProjectTable, recordId, firmId, date, q, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.AssignableTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("GetProjectAssignableProcedures")
            .WithSummary("The Procedures this project could be assigned, each as it would show if assigned now (nothing is written)");
        // ── Work assignment ─────────────────────────────────────────────────────────────────────────────────────
        group.MapGet("/workassignments/{recordId:guid}/procedures",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Procedures(http, service, ProcedureAssignmentEndpoints.WorkAssignmentTable, FinanceAuthorizationFilter.WorkAssignmentEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetWorkAssignmentProcedures")
            .WithSummary("The Procedures this work assignment is subject to, for a firm and a date");

        group.MapGet("/workassignments/{recordId:guid}/rules-in-force",
                (Guid recordId, string? firmId, string? date, RecordProcedureReadService service, HttpContext http, CancellationToken ct) =>
                    Rules(http, service, ProcedureAssignmentEndpoints.WorkAssignmentTable, FinanceAuthorizationFilter.WorkAssignmentEntitySet, recordId, firmId, date, ct))
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetWorkAssignmentRulesInForce")
            .WithSummary("The rules in force on this work assignment, for a firm and a date");

        group.MapPost("/workassignments/{recordId:guid}/procedures/dry-run",
                (Guid recordId, ProcedureDryRunRequest body, string? firmId, string? date, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    DryRun(http, service, planner, ProcedureAssignmentEndpoints.WorkAssignmentTable, recordId, body, firmId, date, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.DryRunTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheDryRunsOverridesSubject(ProcedureAssignmentEndpoints.WorkAssignmentTable)
            .WithName("DryRunWorkAssignmentProcedureOverride")
            .WithSummary("The Procedures and rules on this work assignment now, and if one proposed override were saved now (nothing is written)");

        group.MapGet("/workassignments/{recordId:guid}/procedures/assignable",
                (Guid recordId, string? firmId, string? date, string? q, [FromServices] RecordProcedureReadService service,
                    [FromServices] ProcedureAssignmentService planner, HttpContext http, CancellationToken ct) =>
                    Assignable(http, service, planner, ProcedureAssignmentEndpoints.WorkAssignmentTable, recordId, firmId, date, q, ct))
            .AddFinanceAuthorizationFilter(context => ProcedureAssignmentEndpoints.DryRunRecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ProcedureAssignmentEndpoints.AssignableTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("GetWorkAssignmentAssignableProcedures")
            .WithSummary("The Procedures this work assignment could be assigned, each as it would show if assigned now (nothing is written)");
        return app;
    }

    private static async Task<IResult> Procedures(
        HttpContext http, RecordProcedureReadService service, string table, string entitySet, Guid recordId, string? firmId, string? date,
        CancellationToken ct)
    {
        if (!TryParseQuery(http, firmId, date, out var firm, out var day, out var invalid))
        {
            return invalid!;
        }

        var result = await service.ListProceduresAsync(table, recordId, firm, day, TokenHelper.ExtractBearerTokenOrNull(http), ct).ConfigureAwait(false);
        if (result is { Status: RecordProcedureReadStatus.Done, Value: { } view })
        {
            result = result with { Value = view with { Permissions = await ProcedureAssignmentEndpoints.WritePermissionsForCallerAsync(http, entitySet, recordId, ct).ConfigureAwait(false) } };
        }

        return Answer(http, result);
    }

    private static async Task<IResult> Rules(
        HttpContext http, RecordProcedureReadService service, string table, string entitySet, Guid recordId, string? firmId, string? date,
        CancellationToken ct)
    {
        if (!TryParseQuery(http, firmId, date, out var firm, out var day, out var invalid))
        {
            return invalid!;
        }

        var result = await service.ListRulesInForceAsync(table, recordId, firm, day, TokenHelper.ExtractBearerTokenOrNull(http), ct).ConfigureAwait(false);
        if (result is { Status: RecordProcedureReadStatus.Done, Value: { } view })
        {
            result = result with { Value = view with { Permissions = await ProcedureAssignmentEndpoints.WritePermissionsForCallerAsync(http, entitySet, recordId, ct).ConfigureAwait(false) } };
        }

        return Answer(http, result);
    }

    /// <summary>
    /// Task 084d-2a: the dry run, after the route's filters (step 0; the record's Read and Write+AppendTo, uniform 404; the planned writes'
    /// subject and privilege checks, 403; the kill switch; for a removal or a replacement, the caller's Read on the stored row's subject). Then
    /// 401 for a caller whose <c>oid</c> is not a GUID (as the write routes), the query, and the service: a planning refusal is the write's own
    /// answer (<see cref="ProcedureAssignmentEndpoints.Map{T}"/>); a refused or faulted row is 503 <c>procedures.unavailable</c>.
    /// </summary>
    private static async Task<IResult> DryRun(
        HttpContext http, RecordProcedureReadService service, ProcedureAssignmentService planner, string table, Guid recordId,
        ProcedureDryRunRequest body, string? firmId, string? date, CancellationToken ct)
    {
        if (ProcedureAssignmentEndpoints.CallerOid(http) is null)
        {
            return ProcedureAssignmentEndpoints.NoRequester();
        }

        if (!TryParseQuery(http, firmId, date, out var firm, out var day, out var invalid))
        {
            return invalid!;
        }

        var result = await service.DryRunAsync(planner, table, recordId, body, firm, day, TokenHelper.ExtractBearerTokenOrNull(http), ct).ConfigureAwait(false);
        return result.Status == RecordProcedureReadStatus.PlanRefused
            ? ProcedureAssignmentEndpoints.RefusalOf(result.Refusal!, http)
            : Answer(http, new RecordProcedureReadResult<ProcedureDryRunView>(result.Status, result.Value));
    }

    /// <summary>
    /// Task 084d-5a: the assignable list, after the route's filters (step 0; the record's Read and Write+AppendTo, uniform 404; the assign
    /// write's privileges, 403; the kill switch). Then 401 for a caller whose <c>oid</c> is not a GUID, the query (400
    /// <c>procedures.invalid_query</c>, the only bad-input answer) and the service (503 <c>procedures.unavailable</c> on a refused or faulted row).
    /// </summary>
    private static async Task<IResult> Assignable(
        HttpContext http, RecordProcedureReadService service, ProcedureAssignmentService planner, string table, Guid recordId, string? firmId,
        string? date, string? q, CancellationToken ct)
    {
        if (ProcedureAssignmentEndpoints.CallerOid(http) is null)
        {
            return ProcedureAssignmentEndpoints.NoRequester();
        }

        if (!TryParseQuery(http, firmId, date, out var firm, out var day, out var invalid))
        {
            return invalid!;
        }

        if (!TryParseNameSearch(http, q, out var nameContains, out invalid))
        {
            return invalid!;
        }

        return Answer(http, await service.ListAssignableAsync(
            planner, table, recordId, firm, day, nameContains, TokenHelper.ExtractBearerTokenOrNull(http), ct).ConfigureAwait(false));
    }

    /// <summary>
    /// uac-r2 note 4: <c>q</c>, when present, must hold 1 to <see cref="ProcedureAssignmentService.MaxNameSearchChars"/> characters after
    /// trimming and no control character; otherwise 400. Absent, there is no name filter. The LIKE wildcards are escaped where the query is
    /// built (<see cref="Infrastructure.Dataverse.RecordContainerResolver.EscapeForLike"/>), so any other text is a literal.
    /// </summary>
    private static bool TryParseNameSearch(HttpContext http, string? q, out string? nameContains, out IResult? invalid)
    {
        nameContains = null;
        invalid = null;
        if (!http.Request.Query.ContainsKey("q"))
        {
            return true;
        }

        var raw = q ?? string.Empty;
        var trimmed = raw.Trim();
        if (raw.Any(char.IsControl) || trimmed.Length == 0 || trimmed.Length > ProcedureAssignmentService.MaxNameSearchChars)
        {
            invalid = InvalidQuery(http, $"q must be 1 to {ProcedureAssignmentService.MaxNameSearchChars} characters, with no control characters.");
            return false;
        }

        nameContains = trimmed;
        return true;
    }

    private static IResult Answer<T>(HttpContext http, RecordProcedureReadResult<T> result) where T : class => result.Status switch
    {
        RecordProcedureReadStatus.Done => Results.Ok(result.Value),
        RecordProcedureReadStatus.RecordNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(http),
        _ => Results.Problem(
            title: "Procedures are unavailable",
            detail: "The Procedures and rules for this record could not be read right now. No list is shown.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = RecordProcedureReadService.UnavailableErrorCode,
                ["correlationId"] = http.TraceIdentifier,
            }),
    };

    /// <summary>The 400 for a query value that does not parse: a stable <c>errorCode</c> (ADR-019), as the sibling Finance routes answer.</summary>
    private static IResult InvalidQuery(HttpContext http, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = InvalidQueryErrorCode,
                ["correlationId"] = http.TraceIdentifier,
            });

    private static bool TryParseQuery(HttpContext http, string? firmId, string? date, out Guid? firm, out DateOnly? day, out IResult? invalid)
    {
        firm = null;
        day = null;
        invalid = null;
        if (!string.IsNullOrWhiteSpace(firmId))
        {
            if (!Guid.TryParse(firmId, out var parsedFirm) || parsedFirm == Guid.Empty)
            {
                invalid = InvalidQuery(http, "firmId must be a GUID.");
                return false;
            }

            firm = parsedFirm;
        }

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDay))
            {
                invalid = InvalidQuery(http, "date must be a calendar date, yyyy-MM-dd.");
                return false;
            }

            day = parsedDay;
        }

        return true;
    }
}
