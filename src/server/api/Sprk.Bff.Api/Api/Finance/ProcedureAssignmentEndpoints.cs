using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The assignment and override API of one core record (ebilling-r1 tasks 089b and 089c, EB-D-148; procedure model 5.1, 6.2, 6.3, 6.5 I-A1
/// and 15 answer 2), ADR-001 (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable <c>errorCode</c>). One
/// literal route chain per core-record type (<c>matters</c>, <c>projects</c>, <c>workassignments</c>): the table is fixed by the route, never
/// taken from input. The routes: assign a Procedure, exclude a rule, change a rule, preview a removal, remove an override.
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order, all as the caller and fail closed.</b></para>
/// <list type="number">
/// <item>Step 0 on the group: internal callers only. It answers in the first filter's shape, the uniform 404.</item>
/// <item>The record: Write and AppendTo on the core record (<c>ebilling.record.override</c>). An override changes how the record's invoices
/// are checked, and a new row points at the record. The denial is the uniform 404, so the status is not an existence oracle.</item>
/// <item>The subject and the role (403):
///   <list type="bullet">
///   <item>Assign: Read on the Procedure, and the billing-admin privilege (<see cref="BillingAdminAuthorization.Admin"/>, uac-r2 option (a),
///   2026-11-03).</item>
///   <item>Exclude rule: Read on the rule's policy, and the billing-admin privilege.</item>
///   <item>Change rule: Read on the rule's policy, and the privileges of what the change creates, composed from the existing checks with no
///   second marker: Create and Write on <c>sprk_ruleset</c> (<see cref="BillingAdminAuthorization.CreateRuleSet"/>), Create on
///   <c>sprk_policy</c> (<see cref="BillingAdminAuthorization.DraftRules"/>), and Create and Write on <c>sprk_policyversion</c>
///   (<see cref="BillingAdminAuthorization.EditRule"/>).</item>
///   <item>Removal preview: the billing-admin privilege. Remove: the billing-admin privilege and Write on <c>sprk_policy</c> (a changed
///   rule's removal switches the record rule Off).</item>
///   </list></item>
/// <item>The eBilling flag.</item>
/// <item>Removal and its preview only: Read on what the stored row overrides (<see cref="RequireReadOnTheOverridesSubject{TBuilder}"/>). The
/// request does not name it, so the filter reads the row and asks the caller's own Read on that Procedure or rule. It is never inferred from
/// the caller's rights on anything else (uac-r2 decision, 2026-11-03). Unreadable answers the 404 a missing row gets, not a 403.</item>
/// </list>
/// <para>The write then runs as the BFF application user: no role holds a privilege on the assignment table, and its core lookups are
/// field-secured.</para>
/// <para><b>An override only restricts.</b> It changes which rules check the record's invoices; it grants no one any access (no
/// access-conferring column is written: <c>BillingLookupsConferNoAccessTests</c>).</para>
/// <para><b>Refusal shapes.</b>
/// <list type="bullet">
/// <item>401: no caller, or an <c>oid</c> that is not a GUID (the service is never run with no requester).</item>
/// <item>404: step 0, or the uniform record not found.</item>
/// <item>403: <c>sdap.access.deny.*</c> (not for an unreadable override subject: that is the 404 below).</item>
/// <item>400: a missing body id.</item>
/// <item>503: <c>ebilling.disabled</c>.</item>
/// <item>From the service:
///   <list type="bullet">
///   <item>400 <c>assignment.invalid</c> with <c>errors</c>.</item>
///   <item>404 <c>assignment.procedure_not_found</c>, <c>assignment.rule_not_found</c> or <c>assignment.override_not_found</c>.</item>
///   <item>409 <c>assignment.duplicate</c>, <c>assignment.procedure_retired</c>, <c>assignment.system_rule</c>,
///   <c>assignment.rule_not_in_force</c>, <c>assignment.record_rule_conflict</c>, <c>assignment.concurrent_edit</c>, or the owner reason code.</item>
///   <item>500 <c>assignment.write_failed</c>.</item>
///   <item>503 <c>assignment.read_failed</c>: a Dataverse read before the write faulted, and nothing was written.</item>
///   </list></item>
/// </list></para>
/// </remarks>
public static class ProcedureAssignmentEndpoints
{
    internal const string Route = "/api/finance";

    internal const string PolicyEntitySet = "sprk_policies";

    private const string RecordRouteKey = "recordId";
    private const string OverrideRouteKey = "overrideId";

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
                    Created(http, oid => service.AssignProcedureAsync(MatterTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToMatter")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(MatterTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForMatter")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(MatterTable, recordId, body, oid, ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForMatter")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/matters/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(MatterTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(MatterTable)
            .WithName("PreviewOverrideRemovalForMatter")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/matters/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(MatterTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.MatterEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(RemoveTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(MatterTable)
            .WithName("RemoveOverrideForMatter")
            .WithSummary("Remove this override (a changed rule's removal closes its record rule)")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        // ── Project ──────────────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/projects/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.AssignProcedureAsync(ProjectTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToProject")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(ProjectTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForProject")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(ProjectTable, recordId, body, oid, ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForProject")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/projects/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(ProjectTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(ProjectTable)
            .WithName("PreviewOverrideRemovalForProject")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/projects/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(ProjectTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.ProjectEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(RemoveTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(ProjectTable)
            .WithName("RemoveOverrideForProject")
            .WithSummary("Remove this override (a changed rule's removal closes its record rule)")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        // ── Work assignment ──────────────────────────────────────────────────────────────────────────────────────
        group.MapPost("/workassignments/{recordId:guid}/procedure-assignments",
                (Guid recordId, ProcedureAssignmentRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.AssignProcedureAsync(WorkAssignmentTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToWorkAssignment")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(WorkAssignmentTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForWorkAssignment")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(WorkAssignmentTable, recordId, body, oid, ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForWorkAssignment")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/workassignments/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(WorkAssignmentTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .WithName("PreviewOverrideRemovalForWorkAssignment")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/workassignments/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(WorkAssignmentTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter("ebilling.record.override", FinanceAuthorizationFilter.WorkAssignmentEntitySet, RecordRouteKey, FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(RemoveTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .WithName("RemoveOverrideForWorkAssignment")
            .WithSummary("Remove this override (a changed rule's removal closes its record rule)")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

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

    /// <summary>
    /// Change rule: Read on the body's rule (its policy), then the privileges of what the change creates, composed from the existing
    /// declarations (Create and Write on rule set, Create on policy, Create and Write on policy version; the billing-admin marker, Write on
    /// rule set, is among them), plus Write on policy: the change switches its record rule on, as a removal switches it off. The filter
    /// cannot tell whether the record's set exists yet, so Create on rule set is always required.
    /// </summary>
    internal static FinanceAuthorizationTargets ChangeTargets(EndpointFilterInvocationContext context)
    {
        var body = context.Arguments.OfType<RuleChangeRequest>().FirstOrDefault();
        if (body?.PolicyId is not { } id || id == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Reject(Required("policyId"));
        }

        var privileges = BillingAdminAuthorization.CreateRuleSet().Checks
            .Concat(BillingAdminAuthorization.DraftRules().Checks)
            .Concat(BillingAdminAuthorization.EditRule().Checks)
            .Append(FinanceAuthorizationCheck.CallerPrivilege(BillingAdminAuthorization.WritePolicyPrivilege, PolicyEntitySet, "billing admin"))
            .Distinct();
        return FinanceAuthorizationTargets.Authorize(privileges.Prepend(ReadOn(PolicyEntitySet, id, "body.policyId")).ToArray());
    }

    /// <summary>Removal preview: the billing-admin privilege (the Read on the row's subject is the next filter).</summary>
    internal static FinanceAuthorizationTargets PreviewRemovalTargets(EndpointFilterInvocationContext context) => BillingAdminAuthorization.Admin();

    /// <summary>Remove: the billing-admin privilege and Write on <c>sprk_policy</c> (a changed rule's removal switches the record rule Off).</summary>
    internal static FinanceAuthorizationTargets RemoveTargets(EndpointFilterInvocationContext context) =>
        FinanceAuthorizationTargets.Authorize(BillingAdminAuthorization.Admin().Checks
            .Append(FinanceAuthorizationCheck.CallerPrivilege(BillingAdminAuthorization.WritePolicyPrivilege, PolicyEntitySet, "billing admin"))
            .ToArray());

    /// <summary>
    /// Task 089c (089b "089c must"): before a removal or its preview, the caller's own Read on what the stored override row overrides. That
    /// is the Procedure it assigns, pins or excludes, or the rule it excludes or changes. The request does not name it, so this filter reads
    /// the row (app-only, through <see cref="ProcedureAssignmentService.SubjectOfOverrideAsync"/>, for THIS route's record) and asks the
    /// right of Dataverse as the caller through <see cref="CallerReadCheck"/> (the same Record-path question the assign and exclude routes
    /// declare). Refused is the SAME 404 <c>assignment.override_not_found</c> a missing row gets (override ids are derived, so a different
    /// answer would show whether an override exists for a subject the caller cannot Read); a fault reading the row, or no answer, is 403
    /// <c>system_failure</c>. A row that is not this record's passes on, and the service answers its 404 (the record check has already run).
    /// Added after the route's two <c>AddFinanceAuthorizationFilter</c> declarations (step 0, the record, the billing-admin privileges) and
    /// after <c>RequireEbillingEnabled</c>, so a switched-off eBilling answers 503 before this filter reads Dataverse.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <see cref="FinanceAuthorizationFilter"/> evaluates a Record check, but its target declaration is
    /// synchronous and cannot read the stored row; <see cref="BillingAdminAuthorization.RequireReadOnTheRuleSetsProcedure{TBuilder}"/>
    /// reads a rule set's Procedure, not an override's subject. <i>Extension</i> — this filter is that one's shape (read the stored subject,
    /// then one <see cref="CallerReadCheck"/> question), over the override row. It asks the subject's own Read, never a right inferred
    /// from another record (uac-r2, 2026-11-03). <i>Cost of doing nothing</i> — a billing admin with Write on a record could remove an
    /// override of a Procedure or rule they cannot Read, and preview the open findings it would close.</remarks>
    internal static TBuilder RequireReadOnTheOverridesSubject<TBuilder>(this TBuilder builder, string coreTable)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices;
            var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
            if (!Guid.TryParse(http.Request.RouteValues[RecordRouteKey]?.ToString(), out var recordId)
                || !Guid.TryParse(http.Request.RouteValues[OverrideRouteKey]?.ToString(), out var overrideId))
            {
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            OverrideSubject? subject;
            try
            {
                subject = await services.GetRequiredService<ProcedureAssignmentService>()
                    .SubjectOfOverrideAsync(coreTable, recordId, overrideId, http.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Override {OverrideId}: its subject could not be read for the Read check; denying (fail closed)", overrideId);
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (subject is null)
            {
                return await next(context);
            }

            var caller = new InvoiceContextService.InvoiceCaller(
                CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
            var outcome = await CallerReadCheck.CheckAsync(
                services.GetRequiredService<AuthorizationService>(), caller, subject.EntitySet, "read", subject.Id, logger,
                $"Override {overrideId}: caller read on what it overrides", http.RequestAborted);
            if (outcome == CallerReadOutcome.Readable)
            {
                return await next(context);
            }

            // Override ids are derived (AssignmentIdFor), so a 403 here against a 404 for a missing row would tell a caller whether an override
            // exists for a subject they cannot Read. An unreadable subject therefore answers exactly what a missing row does (089c review K1).
            return outcome == CallerReadOutcome.Refused
                ? Map(ProcedureAssignmentService.OverrideNotFound<object>(), http)
                : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        });

    // ═══════════════════════════════════════════════════════════════════════════
    // Responses (ADR-019: ProblemDetails with a stable errorCode)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs a create as the caller. A caller whose <c>oid</c> is not a GUID is 401 (CallerResolution: "null means 401"): the service
    /// is never run with no requester, as the owner write stamps the creator person from it.
    /// </summary>
    private static async Task<IResult> Created<T>(HttpContext http, Func<Guid, Task<RuleSetOutcome<T>>> call, Func<T, Guid> idOf) where T : class
    {
        if (CallerOid(http) is not { } callerOid)
        {
            return NoRequester();
        }

        var outcome = await call(callerOid);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{http.Request.Path}/{idOf(outcome.Value!)}", outcome.Value)
            : Map(outcome, http);
    }

    /// <summary>Runs a removal or its preview, after the same 401 rule as <see cref="Created{T}"/>: no requester, no call.</summary>
    private static async Task<IResult> AsCaller<T>(HttpContext http, Func<Task<RuleSetOutcome<T>>> call) where T : class =>
        CallerOid(http) is null ? NoRequester() : Map(await call(), http);

    private static IResult NoRequester() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "User identity not found",
        type: "https://tools.ietf.org/html/rfc7235#section-3.1");

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
