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
///   <item>409 <c>assignment.duplicate</c>, <c>assignment.procedure_retired</c>, <c>assignment.procedure_not_billing</c> (EB-D-228),
///   <c>assignment.system_rule</c>,
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
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToMatter")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(MatterTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForMatter")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/matters/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(MatterTable, recordId, body, oid, TokenHelper.ExtractBearerTokenOrNull(http), ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForMatter")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/matters/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(MatterTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(MatterTable)
            .WithName("PreviewOverrideRemovalForMatter")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/matters/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(MatterTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.MatterEntitySet), FinanceDenial.UniformNotFound)
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
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToProject")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(ProjectTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForProject")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/projects/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(ProjectTable, recordId, body, oid, TokenHelper.ExtractBearerTokenOrNull(http), ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForProject")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/projects/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(ProjectTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(ProjectTable)
            .WithName("PreviewOverrideRemovalForProject")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/projects/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(ProjectTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.ProjectEntitySet), FinanceDenial.UniformNotFound)
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
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(AssignTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("AssignProcedureToWorkAssignment")
            .WithSummary("Assign, pin or exclude a Procedure for this record")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/exclude",
                (Guid recordId, RuleExcludeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ExcludeRuleAsync(WorkAssignmentTable, recordId, body, oid, ct), v => v.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ExcludeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ExcludeRuleForWorkAssignment")
            .WithSummary("Exclude one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<OverrideView>(StatusCodes.Status201Created);

        group.MapPost("/workassignments/{recordId:guid}/rule-overrides/change",
                (Guid recordId, RuleChangeRequest body, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    Created(http, oid => service.ChangeRuleAsync(WorkAssignmentTable, recordId, body, oid, TokenHelper.ExtractBearerTokenOrNull(http), ct), v => v.Override.Id))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ChangeTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .WithName("ChangeRuleForWorkAssignment")
            .WithSummary("Change one rule for this record (optionally for one firm's invoices)")
            .ProducesOverride<RuleChangedView>(StatusCodes.Status201Created);

        group.MapGet("/workassignments/{recordId:guid}/overrides/{overrideId:guid}/removal-preview",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.PreviewRemovalAsync(WorkAssignmentTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(PreviewRemovalTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .WithName("PreviewOverrideRemovalForWorkAssignment")
            .WithSummary("What removing this override would close (read-only)")
            .ProducesOverride<OverrideRemovalPreview>(StatusCodes.Status200OK);

        group.MapDelete("/workassignments/{recordId:guid}/overrides/{overrideId:guid}",
                (Guid recordId, Guid overrideId, ProcedureAssignmentService service, HttpContext http, CancellationToken ct) =>
                    AsCaller(http, () => service.RemoveAsync(WorkAssignmentTable, recordId, overrideId, TokenHelper.ExtractBearerTokenOrNull(http), ct)))
            .AddFinanceAuthorizationFilter(context => RecordTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet), FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(RemoveTargets, FinanceDenial.Forbidden)
            .RequireEbillingEnabled()
            .RequireReadOnTheOverridesSubject(WorkAssignmentTable)
            .WithName("RemoveOverrideForWorkAssignment")
            .WithSummary("Remove this override (a changed rule's removal closes its record rule)")
            .ProducesOverride<OverrideRemoved>(StatusCodes.Status200OK);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declarations. Each route: the record (its first filter, RecordTargets), then the subject and the role (*Targets). The
    // record and privilege parts are declared once below and shared with the write permissions the record read routes return (task 084g).
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The record check of every route here: Write and AppendTo on the route's core record (<c>ebilling.record.override</c>), as the caller.
    /// The route's first filter declares it through <see cref="RecordTargets"/>, and <see cref="WritePermissionChecks"/> starts with it.
    /// </summary>
    internal static FinanceAuthorizationCheck RecordWriteCheck(string coreEntitySet, Guid recordId) => new()
    {
        Path = FinanceCheckPath.Record,
        EntitySetName = coreEntitySet,
        RecordId = recordId,
        Operation = "ebilling.record.override",
        Source = "route." + RecordRouteKey,
    };

    /// <summary>
    /// The route's first filter: <see cref="RecordWriteCheck"/> on the route value <c>recordId</c>. An unreadable or empty id yields no check,
    /// which the filter denies (as <see cref="FinanceAuthorizationTargets.FromRoute"/>).
    /// </summary>
    internal static FinanceAuthorizationTargets RecordTargets(EndpointFilterInvocationContext context, string coreEntitySet) =>
        context.HttpContext.Request.RouteValues.TryGetValue(RecordRouteKey, out var raw) && Guid.TryParse(raw?.ToString(), out var id) && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(RecordWriteCheck(coreEntitySet, id))
            : FinanceAuthorizationTargets.Authorize();

    /// <summary>Assign and Exclude rule, beside the subject's Read: the billing-admin privilege (<see cref="BillingAdminAuthorization.Admin"/>, no second marker).</summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> AssignOrExcludePrivileges() => BillingAdminAuthorization.Admin().Checks;

    /// <summary>
    /// Change rule, beside the subject's Read: the privileges of what the change creates, composed from the existing declarations (Create and
    /// Write on rule set, Create on policy, Create and Write on policy version; the billing-admin marker, Write on rule set, is among them),
    /// plus Write on policy: the change switches its record rule on, as a removal switches it off. The filter cannot tell whether the
    /// record's set exists yet, so Create on rule set is always required.
    /// </summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> ChangePrivileges() =>
        BillingAdminAuthorization.CreateRuleSet().Checks
            .Concat(BillingAdminAuthorization.DraftRules().Checks)
            .Concat(BillingAdminAuthorization.EditRule().Checks)
            .Append(FinanceAuthorizationCheck.CallerPrivilege(BillingAdminAuthorization.WritePolicyPrivilege, PolicyEntitySet, "billing admin"))
            .Distinct()
            .ToArray();

    /// <summary>Removal preview: the billing-admin privilege (the Read on the row's subject is a later filter).</summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> PreviewRemovalPrivileges() => BillingAdminAuthorization.Admin().Checks;

    /// <summary>Remove: the billing-admin privilege and Write on <c>sprk_policy</c> (a changed rule's removal switches the record rule Off).</summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> RemovePrivileges() =>
        BillingAdminAuthorization.Admin().Checks
            .Append(FinanceAuthorizationCheck.CallerPrivilege(BillingAdminAuthorization.WritePolicyPrivilege, PolicyEntitySet, "billing admin"))
            .ToArray();

    /// <summary>The writes the record read routes report a permission for (task 084g, EB-D-221).</summary>
    internal enum WriteFlag
    {
        /// <summary><c>procedure-assignments</c> and <c>rule-overrides/exclude</c>.</summary>
        AssignOrExclude,

        /// <summary><c>DELETE overrides/{id}</c>, and its <c>removal-preview</c> (whose checks are a subset).</summary>
        Remove,

        /// <summary><c>rule-overrides/change</c>.</summary>
        Change,
    }

    /// <summary>
    /// Task 084g: the checks one permission flag evaluates, in the route's order: <see cref="RecordWriteCheck"/>, then the route's privileges.
    /// It is the write route's own declaration without the per-subject Read (the read already asks the caller's Read on every subject it
    /// exposes, EB-D-218). RecordWritePermissionsContractTests holds each route's declaration equal to its flag's plus that Read.
    /// </summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> WritePermissionChecks(WriteFlag flag, string coreEntitySet, Guid recordId)
    {
        var privileges = flag switch
        {
            WriteFlag.AssignOrExclude => AssignOrExcludePrivileges(),
            WriteFlag.Remove => RemovePrivileges(),
            WriteFlag.Change => ChangePrivileges(),
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
        };
        return privileges.Prepend(RecordWriteCheck(coreEntitySet, recordId)).ToArray();
    }

    /// <summary>
    /// Task 084g (EB-D-221): the caller's write permissions on one core record, for the record read routes. Each flag holds only when every
    /// check of <see cref="WritePermissionChecks"/> is answered and held, through the Finance filter's own evaluator for the caller
    /// (<see cref="FinanceAuthorizationFilter.EvaluateCheckForCallerAsync"/>: the caller's token, no app-only branch, no role lookup). Two
    /// rounds (084g fix round 1, reviews B4 and A4): first <see cref="RecordWriteCheck"/>, which every flag starts with, so a caller who may
    /// not write the record is asked nothing more (as the write route's first filter refuses before its privileges); then every other
    /// distinct check of the three flags at once, each asked once whatever number of flags use it. Each check has its own verdict, so a fault
    /// in one makes false only the flags that declare it (U-5). Fail closed: a check that is denied, faulted or not answered gives false, and
    /// any fault here gives every flag false, logged at Warning. Never throws except on a cancelled request. A hint for the client: no write
    /// route reads it; the routes still decide.
    /// </summary>
    internal static async Task<RecordWritePermissions> WritePermissionsForCallerAsync(
        HttpContext http, string coreEntitySet, Guid recordId, CancellationToken ct)
    {
        var logger = http.RequestServices.GetService<ILogger<FinanceAuthorizationFilter>>();
        var flags = new[] { WriteFlag.AssignOrExclude, WriteFlag.Remove, WriteFlag.Change };

        try
        {
            var record = RecordWriteCheck(coreEntitySet, recordId);
            if (await FinanceAuthorizationFilter.EvaluateCheckForCallerAsync(http, record, logger, ct).ConfigureAwait(false) != FinanceCheckVerdict.Allowed)
            {
                return RecordWritePermissions.None;
            }

            var rest = flags.SelectMany(f => WritePermissionChecks(f, coreEntitySet, recordId)).Where(c => c != record).Distinct().ToArray();
            // Known limit (uac-r2, 084g fix round 1): these checks run concurrently on ONE HttpContext and its request scope. That is safe only
            // while every scoped service this path reaches (IAccessDataSource and its cache, CallerRecordAccessProbe, the IAuthorizationRule
            // set) stays safe for concurrent use within a request, and while nothing on the path writes HttpContext.Items. A per-request cache
            // that writes, or an Items write, would make this a race: then ask the checks one at a time.
            var answers = await Task.WhenAll(rest.Select(c => FinanceAuthorizationFilter.EvaluateCheckForCallerAsync(http, c, logger, ct))).ConfigureAwait(false);
            var verdicts = rest.Zip(answers).ToDictionary(x => x.First, x => x.Second);
            verdicts[record] = FinanceCheckVerdict.Allowed;

            bool Holds(WriteFlag flag) => WritePermissionChecks(flag, coreEntitySet, recordId).All(c => verdicts[c] == FinanceCheckVerdict.Allowed);

            return new RecordWritePermissions(
                AssignOrExclude: Holds(WriteFlag.AssignOrExclude),
                Remove: Holds(WriteFlag.Remove),
                Change: Holds(WriteFlag.Change));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Record write permissions on a {CoreEntitySet} could not be computed; every flag is false", coreEntitySet);
            return RecordWritePermissions.None;
        }
    }

    private static FinanceAuthorizationCheck ReadOn(string entitySet, Guid id, string source) => new()
    {
        Path = FinanceCheckPath.Record,
        EntitySetName = entitySet,
        RecordId = id,
        Operation = "read",
        Source = source,
    };

    /// <summary>The subject's Read check, then <see cref="AssignOrExcludePrivileges"/>.</summary>
    private static FinanceAuthorizationTargets SubjectAndAdmin(FinanceAuthorizationCheck subject) =>
        FinanceAuthorizationTargets.Authorize(AssignOrExcludePrivileges().Prepend(subject).ToArray());

    // ── The per-write checks, declared once (task 084d-2a, uac-r2 C5). Each write route's target declaration and the dry run's
    //    (DryRunTargets) are built from these, so a dry run asks exactly what the matching write asks, never less.

    /// <summary>Assign: Read on the body's Procedure and the billing-admin privilege. No body or no id is a 400 before any Dataverse call.</summary>
    internal static FinanceAuthorizationTargets AssignChecks(ProcedureAssignmentRequest? body) =>
        body?.ProcedureId is { } id && id != Guid.Empty
            ? SubjectAndAdmin(ReadOn(BillingAdminAuthorization.ProcedureEntitySet, id, "body.procedureId"))
            : FinanceAuthorizationTargets.Reject(Required("procedureId"));

    /// <summary>Exclude rule: Read on the body's rule (its policy) and the billing-admin privilege.</summary>
    internal static FinanceAuthorizationTargets ExcludeChecks(RuleExcludeRequest? body) =>
        body?.PolicyId is { } id && id != Guid.Empty
            ? SubjectAndAdmin(ReadOn(PolicyEntitySet, id, "body.policyId"))
            : FinanceAuthorizationTargets.Reject(Required("policyId"));

    /// <summary>Change rule: Read on the body's rule (its policy), then <see cref="ChangePrivileges"/>.</summary>
    internal static FinanceAuthorizationTargets ChangeChecks(RuleChangeRequest? body) =>
        body?.PolicyId is { } id && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(ChangePrivileges().Prepend(ReadOn(PolicyEntitySet, id, "body.policyId")).ToArray())
            : FinanceAuthorizationTargets.Reject(Required("policyId"));

    /// <summary>Remove: <see cref="RemovePrivileges"/> (the Read on the row's subject is a later filter, <see cref="RequireReadOnTheOverridesSubject{TBuilder}"/>).</summary>
    internal static FinanceAuthorizationTargets RemoveChecks() => FinanceAuthorizationTargets.Authorize(RemovePrivileges().ToArray());

    /// <summary>Assign: <see cref="AssignChecks"/> on the bound body.</summary>
    internal static FinanceAuthorizationTargets AssignTargets(EndpointFilterInvocationContext context) =>
        AssignChecks(context.Arguments.OfType<ProcedureAssignmentRequest>().FirstOrDefault());

    /// <summary>Exclude rule: <see cref="ExcludeChecks"/> on the bound body.</summary>
    internal static FinanceAuthorizationTargets ExcludeTargets(EndpointFilterInvocationContext context) =>
        ExcludeChecks(context.Arguments.OfType<RuleExcludeRequest>().FirstOrDefault());

    /// <summary>Change rule: <see cref="ChangeChecks"/> on the bound body.</summary>
    internal static FinanceAuthorizationTargets ChangeTargets(EndpointFilterInvocationContext context) =>
        ChangeChecks(context.Arguments.OfType<RuleChangeRequest>().FirstOrDefault());

    /// <summary>Removal preview: <see cref="PreviewRemovalPrivileges"/> (the Read on the row's subject is the next filter).</summary>
    internal static FinanceAuthorizationTargets PreviewRemovalTargets(EndpointFilterInvocationContext context) =>
        FinanceAuthorizationTargets.Authorize(PreviewRemovalPrivileges().ToArray());

    /// <summary>Remove: <see cref="RemoveChecks"/>.</summary>
    internal static FinanceAuthorizationTargets RemoveTargets(EndpointFilterInvocationContext context) => RemoveChecks();

    // ── The dry run (task 084d-2a, design notes/084d-dry-run-design.md §3, uac-r2 C5) ─────────────────────────────────────

    /// <summary>
    /// The dry run's record filter (uniform 404): Read on the record (<c>finance.read</c>, as 084's list routes ask it) and then
    /// <see cref="RecordWriteCheck"/> (as every write route's first filter asks it). Both in ONE filter, so an unknown record, an unreadable
    /// one and one the caller may not write all get the same 404, before any 403. An unreadable or empty id yields no check, which the filter
    /// denies.
    /// </summary>
    internal static FinanceAuthorizationTargets DryRunRecordTargets(EndpointFilterInvocationContext context, string coreEntitySet) =>
        context.HttpContext.Request.RouteValues.TryGetValue(RecordRouteKey, out var raw) && Guid.TryParse(raw?.ToString(), out var id) && id != Guid.Empty
            ? FinanceAuthorizationTargets.Authorize(DryRunRecordChecks(coreEntitySet, id).ToArray())
            : FinanceAuthorizationTargets.Authorize();

    /// <summary>The dry run's record checks: <c>finance.read</c>, then <see cref="RecordWriteCheck"/>.</summary>
    internal static IReadOnlyList<FinanceAuthorizationCheck> DryRunRecordChecks(string coreEntitySet, Guid recordId) =>
    [
        new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record, EntitySetName = coreEntitySet, RecordId = recordId, Operation = "finance.read", Source = "route." + RecordRouteKey,
        },
        RecordWriteCheck(coreEntitySet, recordId),
    ];

    /// <summary>The dry run's subject and privilege filter (403): <see cref="DryRunChecks"/> on the bound body.</summary>
    internal static FinanceAuthorizationTargets DryRunTargets(EndpointFilterInvocationContext context) =>
        DryRunChecks(context.Arguments.OfType<ProcedureDryRunRequest>().FirstOrDefault());

    /// <summary>
    /// What a dry run asks beside the record: the union, in plan order, of the checks of every write it plans, from the same declarations the
    /// write routes use. Assign: <see cref="AssignChecks"/>; rule exclude: <see cref="ExcludeChecks"/>; rule change: <see cref="ChangeChecks"/>;
    /// a removal, or a create that replaces an override (<c>replacesOverrideId</c>): <see cref="RemoveChecks"/> as well. A missing kind or id is
    /// the same 400 as on the write. Stricter than the removal preview, which asks only the billing-admin privilege.
    /// </summary>
    internal static FinanceAuthorizationTargets DryRunChecks(ProcedureDryRunRequest? body)
    {
        var parts = new List<FinanceAuthorizationTargets>();
        switch (body?.Kind)
        {
            case ProcedureDryRunRequest.KindAssign:
                parts.Add(AssignChecks(body.Assign));
                break;
            case ProcedureDryRunRequest.KindExcludeRule:
                parts.Add(ExcludeChecks(body.ExcludeRule));
                break;
            case ProcedureDryRunRequest.KindChangeRule:
                parts.Add(ChangeChecks(body.ChangeRule));
                break;
            case ProcedureDryRunRequest.KindRemove:
                if (body.OverrideId is not { } removed || removed == Guid.Empty)
                {
                    return FinanceAuthorizationTargets.Reject(Required("overrideId"));
                }

                if (body.ReplacesOverrideId is not null)
                {
                    return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["replacesOverrideId"] = ["replacesOverrideId goes with a write that creates a row, not with a removal."],
                    }));
                }

                parts.Add(RemoveChecks());
                break;
            default:
                return FinanceAuthorizationTargets.Reject(Required("kind"));
        }

        if (body.ReplacesOverrideId is { } replaced)
        {
            if (replaced == Guid.Empty)
            {
                return FinanceAuthorizationTargets.Reject(Required("replacesOverrideId"));
            }

            parts.Add(RemoveChecks());
        }

        return parts.FirstOrDefault(p => p.Rejection is not null)
            ?? FinanceAuthorizationTargets.Authorize(parts.SelectMany(p => p.Checks).Distinct().ToArray());
    }

    /// <summary>
    /// The dry run's removal step: when the body removes an override (<c>kind</c> Remove with <c>overrideId</c>) or replaces one
    /// (<c>replacesOverrideId</c>), the caller's own Read on what that stored row overrides, through the same helper as the write routes'
    /// <see cref="RequireReadOnTheOverridesSubject{TBuilder}"/> (<see cref="RefuseUnlessCallerReadsOverridesSubjectAsync"/>): the same 404
    /// <c>assignment.override_not_found</c> and the same 403 <c>system_failure</c>. Added after <c>RequireEbillingEnabled</c>, as on the
    /// write routes. A body that removes nothing passes.
    /// </summary>
    internal static TBuilder RequireReadOnTheDryRunsOverridesSubject<TBuilder>(this TBuilder builder, string coreTable)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var body = context.Arguments.OfType<ProcedureDryRunRequest>().FirstOrDefault();
            var overrideId = body?.Kind == ProcedureDryRunRequest.KindRemove ? body.OverrideId : body?.ReplacesOverrideId;
            if (overrideId is not { } id)
            {
                return await next(context);
            }

            if (!Guid.TryParse(http.Request.RouteValues[RecordRouteKey]?.ToString(), out var recordId))
            {
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            return await RefuseUnlessCallerReadsOverridesSubjectAsync(http, coreTable, recordId, id) ?? await next(context);
        });

    /// <summary>
    /// Task 089c (089b "089c must"): before a removal or its preview, the caller's own Read on what the stored override row overrides. That
    /// is the Procedure it assigns, pins or excludes, or the rule it excludes or changes. The request does not name it, so this filter reads
    /// the row (app-only, through <see cref="ProcedureAssignmentService.SubjectOfOverrideAsync"/>, for THIS route's record) and asks the
    /// right of Dataverse as the caller through <see cref="CallerReadCheck"/> (the same Record-path question the assign and exclude routes
    /// declare). Refused is the SAME 404 <c>assignment.override_not_found</c> a missing row gets (override ids are derived, so a different
    /// answer would show whether an override exists for a subject the caller cannot Read); a fault reading the row, or no answer, is 403
    /// <c>system_failure</c>. No row, or a row that is not this record's, is answered by the filter with the same 404 and the request never reaches the service (the record check has already run).
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
            if (!Guid.TryParse(http.Request.RouteValues[RecordRouteKey]?.ToString(), out var recordId)
                || !Guid.TryParse(http.Request.RouteValues[OverrideRouteKey]?.ToString(), out var overrideId))
            {
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            return await RefuseUnlessCallerReadsOverridesSubjectAsync(http, coreTable, recordId, overrideId) ?? await next(context);
        });

    /// <summary>
    /// The body of <see cref="RequireReadOnTheOverridesSubject{TBuilder}"/>, shared with the dry run (task 084d-2a): reads the stored override
    /// row of <paramref name="recordId"/> app-only and asks the caller's own Read on what it overrides. Null when the caller may Read it (the
    /// request goes on). Otherwise the answer: the 404 <c>assignment.override_not_found</c> for no row, a row of another record, or an
    /// unreadable subject (one answer, so it is no existence oracle); the 403 <c>system_failure</c> for a fault or no answer.
    /// </summary>
    internal static async Task<IResult?> RefuseUnlessCallerReadsOverridesSubjectAsync(HttpContext http, string coreTable, Guid recordId, Guid overrideId)
    {
        var services = http.RequestServices;
        var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;

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
            // No row (or not this record's): the filter answers, so the service never reads the row a second time and acts on whatever is
            // there by then. Override ids are derived from the subject, so a row created later at this id has the subject that would have
            // been checked here (089c pass 2).
            return Map(ProcedureAssignmentService.OverrideNotFound<object>(), http);
        }

        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
        var outcome = await CallerReadCheck.CheckAsync(
            services.GetRequiredService<AuthorizationService>(), caller, subject.EntitySet, "read", subject.Id, logger,
            $"Override {overrideId}: caller read on what it overrides", http.RequestAborted);
        if (outcome == CallerReadOutcome.Readable)
        {
            return null;
        }

        // Override ids are derived (AssignmentIdFor), so a 403 here against a 404 for a missing row would tell a caller whether an override
        // exists for a subject they cannot Read. An unreadable subject therefore answers exactly what a missing row does (089c review K1).
        return outcome == CallerReadOutcome.Refused
            ? Map(ProcedureAssignmentService.OverrideNotFound<object>(), http)
            : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
    }

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

    internal static IResult NoRequester() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "User identity not found",
        type: "https://tools.ietf.org/html/rfc7235#section-3.1");

    /// <summary>
    /// <see cref="Map{T}"/> for callers in other files (the dry run's planning refusal is the write's own answer). Named apart because the
    /// route guard reads any <c>.Map(</c> member call as a route registration it cannot scan.
    /// </summary>
    internal static IResult RefusalOf<T>(RuleSetOutcome<T> outcome, HttpContext http) where T : class => Map(outcome, http);

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

    internal static Guid? CallerOid(HttpContext http) =>
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
