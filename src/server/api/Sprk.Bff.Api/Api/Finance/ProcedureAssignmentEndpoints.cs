using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The assignment and override API of one core record (ebilling-r1 task 089b, EB-D-148; procedure model 5.1, 6.2, 6.3 and 6.5 I-A1), ADR-001
/// (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable <c>errorCode</c>). One literal route chain per
/// core-record type (<c>matters</c>, <c>projects</c>, <c>workassignments</c>): the table is fixed by the route, never taken from input. Task
/// 089c adds change rule and remove with preview here.
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order, all as the caller and fail closed.</b> Step 0 on the group (internal callers only; it answers in the
/// first filter's shape, the uniform 404). Then the record: Write and AppendTo on the core record (<c>ebilling.record.override</c>: an override
/// changes how the record's invoices are checked, and the new row points at it), answered as the uniform 404 so the status is not an
/// existence oracle. Then the subject and the role (403): Read on the Procedure (assign) or on the rule's policy (exclude rule), and the
/// billing-admin privilege (<see cref="BillingAdminAuthorization.Admin"/>, uac-r2 option (a), 2026-11-03). Then the eBilling flag. The write
/// then runs as the BFF application user (no role holds a privilege on the assignment table; its core lookups are field-secured).</para>
/// <para><b>An override only restricts.</b> It changes which rules check the record's invoices; it grants no one any access (no
/// access-conferring column is written: <c>BillingLookupsConferNoAccessTests</c>).</para>
/// <para><b>Refusal shapes.</b> 401; 404 step 0 or the uniform record not found; 403 <c>sdap.access.deny.*</c>; 400 a missing body id; 503
/// <c>ebilling.disabled</c>; then, from the service: 400 <c>assignment.invalid</c> with <c>errors</c>; 404
/// <c>assignment.procedure_not_found</c> / <c>assignment.rule_not_found</c>; 409 <c>assignment.duplicate</c>,
/// <c>assignment.procedure_retired</c>, <c>assignment.system_rule</c>, <c>assignment.concurrent_edit</c> or the owner reason code; 500
/// <c>assignment.write_failed</c>.</para>
/// </remarks>
public static class ProcedureAssignmentEndpoints
{
    internal const string Route = "/api/finance";

    internal const string PolicyEntitySet = "sprk_policies";

    private const string RecordRouteKey = "recordId";

    // The core tables, one per literal route chain (the table is fixed by the route, never taken from input).
    internal const string MatterTable = "sprk_matter";
    internal const string ProjectTable = "sprk_project";
    internal const string WorkAssignmentTable = "sprk_workassignment";

    public static IEndpointRouteBuilder MapProcedureAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // ── Matter ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/matters/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.AssignProcedureAsync(MatterTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToMatter")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(MatterTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForMatter")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride(StatusCodes.Status201Created);

        // ── Project ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/projects/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.AssignProcedureAsync(ProjectTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToProject")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(ProjectTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForProject")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride(StatusCodes.Status201Created);

        // ── Work assignment ──────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/workassignments/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.AssignProcedureAsync(WorkAssignmentTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToWorkAssignment")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(WorkAssignmentTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForWorkAssignment")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride(StatusCodes.Status201Created);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declarations (the subject and the role; the record is the route's first filter)
    // ═══════════════════════════════════════════════════════════════════════════

    private static FinanceAuthorizationCheck ReadOn(string entitySet, Guid id, string source) => new()
    {
        Path = FinanceCheckPath.Record,
        EntitySetName = entitySet,
        RecordId = id,
        Operation = "read",
        Source = source,
    };

    /// <summary>The subject's Read check, then the billing-admin privilege composed from <see cref="BillingAdminAuthorization.Admin"/> (no second marker).</summary>
    private static FinanceAuthorizationTargets SubjectAndAdmin(FinanceAuthorizationCheck subject) =>
        FinanceAuthorizationTargets.Authorize(BillingAdminAuthorization.Admin().Checks.Prepend(subject).ToArray());

    /// <summary>Assign: Read on the body's Procedure and the billing-admin privilege. No body or no id is a 400 before any Dataverse call.</summary>
    internal static FinanceAuthorizationTargets AssignTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<ProcedureAssignmentRequest>().FirstOrDefault();
        return body?.ProcedureId is { } id && id != Guid.Empty
            ? SubjectAndAdmin(ReadOn(BillingAdminAuthorization.ProcedureEntitySet, id, "body.procedureId"))
            : FinanceAuthorizationTargets.Reject(Required("procedureId"));
    }

    /// <summary>Exclude rule: Read on the body's rule (its policy) and the billing-admin privilege.</summary>
    internal static FinanceAuthorizationTargets ExcludeTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<RuleExcludeRequest>().FirstOrDefault();
        return body?.PolicyId is { } id && id != Guid.Empty
            ? SubjectAndAdmin(ReadOn(PolicyEntitySet, id, "body.policyId"))
            : FinanceAuthorizationTargets.Reject(Required("policyId"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Responses (ADR-019: ProblemDetails with a stable errorCode)
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> Created(Task<RuleSetOutcome<OverrideView>> pending, HttpContext http)
    {
        var outcome = await pending;
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{http.Request.Path}/{outcome.Value!.Id}", outcome.Value)
            : Map(outcome, http);
    }

    internal static IResult Map<T>(RuleSetOutcome<T> outcome, HttpContext http) where T : class =>
        outcome.Kind switch
        {
            RuleSetOutcomeKind.Done => Results.Ok(outcome.Value),
            RuleSetOutcomeKind.Invalid => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Override request is not valid",
                detail: $"{outcome.Errors.Count} problem(s) found; nothing was written.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = outcome.ErrorCode,
                    ["errors"] = outcome.Errors
                        .Select(e => (object)new { policyId = e.PolicyId, rule = e.RuleName, field = e.Field, message = e.Message, reason = e.Reason })
                        .ToArray(),
                }),
            RuleSetOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "override", http.TraceIdentifier),
            _ => Results.Problem(
                statusCode: outcome.StatusCode, detail: outcome.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = outcome.ErrorCode }),
        };

    private static IResult Required(string field) =>
        ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]> { [field] = [$"{field} is required."] });

    private static Guid? CallerOid(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;

    private static RouteHandlerBuilder ProducesOverride(this RouteHandlerBuilder builder, int status) =>
        builder.Produces<OverrideView>(status)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
