using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The assignment and override API of one core record (ebilling-r1 task 089, EB-D-148; procedure model 6.2, 6.3, 6.5 I-A1 and 15 answer 2),
/// ADR-001 (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable <c>errorCode</c>). One literal route chain per
/// core-record type (<c>matters</c>, <c>projects</c>, <c>workassignments</c>): the table is fixed by the route, never taken from input.
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order, all as the caller and fail closed.</b> Step 0 on the group (internal callers only). Then the record:
/// Write and AppendTo on the core record (<c>ebilling.record.override</c>; an override changes how the record's invoices are checked and the
/// new rows point at it), answered as the uniform 404 so the status is not an existence oracle. Then the subject and the role (403): Read on the
/// Procedure (assign) or on the rule's policy (exclude, change), and the billing-admin marker (<see cref="BillingAdminAuthorization.WriteRuleSetPrivilege"/>);
/// a change also needs Create on the rule-set, policy and policy-version tables (it creates them as the application), a removal Write on
/// <c>sprk_policy</c> (it switches the record rule Off). For an existing override (preview, remove) the subject is read from the stored row and
/// the caller's Read on it is asked of Dataverse (403 <c>insufficient_rights</c> or <c>system_failure</c>). Then the eBilling flag. The writes
/// then run as the BFF application user (the Procedure tables carry no role privilege; the scope and core lookups are field-secured).</para>
/// <para><b>An override only restricts.</b> It changes which rules check the record's invoices; it grants no one any access (no access-conferring
/// column is written: <c>BillingLookupsConferNoAccessTests</c>).</para>
/// <para><b>Refusal shapes.</b> 401; 403 step 0 or <c>sdap.access.deny.*</c>; 404 the uniform record not found; 503 <c>ebilling.disabled</c>;
/// then, from the service: 400 <c>assignment.invalid</c> / <c>assignment.firm_mismatch</c> with <c>errors</c>; 404 <c>assignment.not_found</c> /
/// <c>assignment.procedure_not_found</c> / <c>assignment.rule_not_found</c>; 409 <c>assignment.duplicate</c>, <c>assignment.procedure_retired</c>,
/// <c>assignment.rule_not_in_force</c>, <c>assignment.system_rule</c>, <c>assignment.ruleset_unavailable</c>, <c>assignment.concurrent_edit</c>
/// or the owner reason code; 500 <c>assignment.write_failed</c>.</para>
/// </remarks>
public static class ProcedureAssignmentEndpoints
{
    internal const string Route = "/api/finance";

    /// <summary>The Record-path operation on the core record (Spaarke.Core <c>OperationAccessPolicy</c>: Write | AppendTo).</summary>
    internal const string RecordOperation = "ebilling.record.override";

    internal const string PolicyEntitySet = "sprk_policies";

    private const string RecordRouteKey = "recordId";
    private const string AssignmentRouteKey = "assignmentId";
    private const string Source = "record override";

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
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(MatterTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForMatter")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ChangeRuleAsync(MatterTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForMatter")
            .WithSummary("Change one rule for this record: an Exclude row and a record rule in the record's own rule set, in one transaction")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapGet("/matters/{recordId:guid}/overrides/{assignmentId:guid}/removal-preview",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.PreviewRemoveAsync(MatterTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(MatterTable)
            .RequireEbillingEnabled()
            .WithName("PreviewOverrideRemovalForMatter")
            .WithSummary("The removal confirmation: the warning and the caller-scoped count of affected open invoices (writes nothing)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/matters/{recordId:guid}/overrides/{assignmentId:guid}",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.RemoveAsync(MatterTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => RemoveTargets(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(MatterTable)
            .RequireEbillingEnabled()
            .WithName("RemoveOverrideForMatter")
            .WithSummary("Remove an override; a changed rule is switched Off and its open findings close PolicyRetired")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        // ── Project ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/projects/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.AssignProcedureAsync(ProjectTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToProject")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(ProjectTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForProject")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ChangeRuleAsync(ProjectTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForProject")
            .WithSummary("Change one rule for this record: an Exclude row and a record rule in the record's own rule set, in one transaction")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapGet("/projects/{recordId:guid}/overrides/{assignmentId:guid}/removal-preview",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.PreviewRemoveAsync(ProjectTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(ProjectTable)
            .RequireEbillingEnabled()
            .WithName("PreviewOverrideRemovalForProject")
            .WithSummary("The removal confirmation: the warning and the caller-scoped count of affected open invoices (writes nothing)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/projects/{recordId:guid}/overrides/{assignmentId:guid}",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.RemoveAsync(ProjectTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => RemoveTargets(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(ProjectTable)
            .RequireEbillingEnabled()
            .WithName("RemoveOverrideForProject")
            .WithSummary("Remove an override; a changed rule is switched Off and its open findings close PolicyRetired")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        // ── WorkAssignment ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/workassignments/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.AssignProcedureAsync(WorkAssignmentTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToWorkAssignment")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ExcludeRuleAsync(WorkAssignmentTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForWorkAssignment")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(service.ChangeRuleAsync(WorkAssignmentTable, recordId, body, CallerOid(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForWorkAssignment")
            .WithSummary("Change one rule for this record: an Exclude row and a record rule in the record's own rule set, in one transaction")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapGet("/workassignments/{recordId:guid}/overrides/{assignmentId:guid}/removal-preview",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.PreviewRemoveAsync(WorkAssignmentTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.Admin(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .RequireEbillingEnabled()
            .WithName("PreviewOverrideRemovalForWorkAssignment")
            .WithSummary("The removal confirmation: the warning and the caller-scoped count of affected open invoices (writes nothing)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/workassignments/{recordId:guid}/overrides/{assignmentId:guid}",
                async (Guid recordId, Guid assignmentId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Map(await service.RemoveAsync(WorkAssignmentTable, recordId, assignmentId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(_ => RemoveTargets(), FinanceDenial.Forbidden)
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .RequireEbillingEnabled()
            .WithName("RemoveOverrideForWorkAssignment")
            .WithSummary("Remove an override; a changed rule is switched Off and its open findings close PolicyRetired")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declarations (the subject and the role; the record is the route's first filter)
    // ═══════════════════════════════════════════════════════════════════════════

    private static FinanceAuthorizationCheck Privilege(string privilege, string entitySet) =>
        FinanceAuthorizationCheck.CallerPrivilege(privilege, entitySet, Source);

    private static FinanceAuthorizationCheck Marker() => Privilege(BillingAdminAuthorization.WriteRuleSetPrivilege, "sprk_rulesets");

    private static FinanceAuthorizationCheck ReadOn(string entitySet, Guid id, string source) => new()
    {
        Path = FinanceCheckPath.Record,
        EntitySetName = entitySet,
        RecordId = id,
        Operation = "read",
        Source = source,
    };

    /// <summary>Assign: Read on the body's Procedure and the billing-admin marker. No body or no id is a 400 before any Dataverse call.</summary>
    internal static FinanceAuthorizationTargets AssignTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<ProcedureAssignmentRequest>().FirstOrDefault();
        return body?.ProcedureId is { } id && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(ReadOn(BillingAdminAuthorization.ProcedureEntitySet, id, "body.procedureId"), Marker())
            : FinanceAuthorizationTargets.Reject(Required("procedureId"));
    }

    /// <summary>Exclude: Read on the body's rule (its policy) and the billing-admin marker.</summary>
    internal static FinanceAuthorizationTargets ExcludeTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<RuleExcludeRequest>().FirstOrDefault();
        return body?.PolicyId is { } id && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(ReadOn(PolicyEntitySet, id, "body.policyId"), Marker())
            : FinanceAuthorizationTargets.Reject(Required("policyId"));
    }

    /// <summary>Change: as <see cref="ExcludeTargets"/>, plus Create on the rule-set, policy and policy-version tables (created as the application).</summary>
    internal static FinanceAuthorizationTargets ChangeTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<RuleChangeRequest>().FirstOrDefault();
        return body?.PolicyId is { } id && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(
                ReadOn(PolicyEntitySet, id, "body.policyId"),
                Marker(),
                Privilege(BillingAdminAuthorization.CreateRuleSetPrivilege, "sprk_rulesets"),
                Privilege(BillingAdminAuthorization.CreatePolicyPrivilege, PolicyEntitySet),
                Privilege(BillingAdminAuthorization.CreatePolicyVersionPrivilege, "sprk_policyversions"))
            : FinanceAuthorizationTargets.Reject(Required("policyId"));
    }

    /// <summary>Remove: the billing-admin marker and Write on <c>sprk_policy</c> (a changed rule's record rule is switched Off).</summary>
    internal static FinanceAuthorizationTargets RemoveTargets() =>
        FinanceAuthorizationTargets.Authorize(Marker(), Privilege(BillingAdminAuthorization.WritePolicyPrivilege, PolicyEntitySet));

    /// <summary>
    /// For an existing override: the caller's own Read on what it overrides (its Procedure, or the rule it excludes), read server-side from the
    /// stored row (<see cref="ProcedureAssignmentService.TargetOfAsync"/>) and asked of Dataverse as the caller. Runs after the record and role
    /// filters. A row that does not exist on this record passes to the service's 404. Fails closed: a fault reading the row is 403
    /// <c>system_failure</c>.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <see cref="BillingAdminAuthorization.RequireReadOnTheRuleSetsProcedure"/> does this for a rule set's
    /// Procedure. <i>Extension</i> — the same shape and the same <see cref="CallerReadCheck"/>; its target lookup is the rule set's, so it cannot be
    /// reused as is. <i>Cost of doing nothing</i> — a caller with rights on the record but no Read on a Procedure or rule could see and remove
    /// an override of it.</remarks>
    internal static TBuilder RequireReadOnTheOverridesSubject<TBuilder>(this TBuilder builder, string table) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(ReadOnTheOverridesSubject(table));

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> ReadOnTheOverridesSubject(string table) =>
        async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices;
            var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
            if (!TryRouteGuid(http, RecordRouteKey, out var recordId) || !TryRouteGuid(http, AssignmentRouteKey, out var assignmentId))
            {
                return await next(context); // the route constraints guarantee guids; the service answers anything else
            }

            OverrideTarget? target;
            try
            {
                target = await services.GetRequiredService<ProcedureAssignmentService>().TargetOfAsync(table, recordId, assignmentId, http.RequestAborted);
            }
            catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
            {
                logger.LogError(ex, "Override {AssignmentId}: what it overrides could not be read for the caller's Read check; denying (fail closed)", assignmentId);
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (target is null)
            {
                return await next(context);
            }

            var caller = new InvoiceContextService.InvoiceCaller(
                CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
            var outcome = await CallerReadCheck.CheckAsync(
                services.GetRequiredService<AuthorizationService>(), caller, target.EntitySet, "read", target.Id, logger,
                $"Override {assignmentId}: caller read on what it overrides", http.RequestAborted);
            if (outcome == CallerReadOutcome.Readable)
            {
                return await next(context);
            }

            return outcome == CallerReadOutcome.Refused
                ? ProblemDetailsHelper.Forbidden(
                    FinanceAuthorizationFilter.InsufficientRightsReasonCode,
                    detail: target.EntitySet == PolicyEntitySet ? "You need Read on the rule this override changes." : "You need Read on this override's Procedure.",
                    traceId: http.TraceIdentifier)
                : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        };

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

    private static bool TryRouteGuid(HttpContext http, string key, out Guid id)
    {
        id = Guid.Empty;
        return http.Request.RouteValues.TryGetValue(key, out var raw) && Guid.TryParse(raw?.ToString(), out id) && id != Guid.Empty;
    }

    private static Guid? CallerOid(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;

    private static RouteHandlerBuilder ProducesOverride<T>(this RouteHandlerBuilder builder, int status) =>
        builder.Produces<T>(status)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
