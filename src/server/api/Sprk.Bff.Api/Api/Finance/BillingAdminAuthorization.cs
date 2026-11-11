using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The "billing admin" check, declared once (ebilling-r1 task 076, D-76): who may review, edit, publish and share billing rule sets.
/// Task 075's drafting trigger and any later billing-rule admin route reuse it, so there is one definition of the role.
/// </summary>
/// <remarks>
/// <para><b>Mechanism (D-76).</b> A Dataverse TABLE privilege the CALLER holds, asked of Dataverse as the caller
/// (<see cref="FinanceAuthorizationCheck.CallerPrivilege"/>, fail closed) by the route's <c>AddFinanceAuthorizationFilter</c>. No
/// security-role name, no <c>SystemAdmin</c> app role.</para>
/// <para><b>The marker is Write on <c>sprk_ruleset</c>.</b> The policy tables are shared with the Ontology platform and its
/// Administrator role holds every privilege on them, so privileges on <c>sprk_policy</c> / <c>sprk_policyversion</c> alone would make
/// every Ontology Administrator a billing admin (over-granting). <c>sprk_ruleset</c> is billing's own table: the only solution role
/// with any privilege on it is "Spaarke Billing Administrator" (task 120; D-185: only billing roles read rule sets, so Core and Basic
/// User hold nothing on it; pinned by <c>RuleSetRolePrivilegeGuardTests</c>), so Create / Write on it names exactly the people given
/// billing rule administration, and every route requires it. A route that writes policy rows ALSO requires the privilege of the table
/// it writes, so Dataverse's own rights are never bypassed by the application-identity write that follows. Reads of the proposals
/// (clause text, rule bodies) are admin-only too, so they require the same Write marker, not Read.</para>
/// <para><b>Depth (D-108, owner 2026-10-17).</b> Write at ANY depth counts: there is no Org-depth requirement. The check asks whether the
/// caller holds the privilege, not at what depth. The "Spaarke Billing Administrator" role (task 120) holds the Create / Write markers
/// at Basic on purpose (uac-r2): the writes run app-only through <see cref="RuleSetService"/>, so a deeper native Write would only let
/// a billing admin edit rule sets and policies past its invariants. Read is Deep (owner EB-D-162, 2026-10-27; it was Global): the BFF reads these tables app-only, so no route depends on it.</para>
/// <para>Privilege names are the live ones (spaarkedev1 <c>privilege</c> table, 2026-10-17): <c>sprk_ruleset</c> is lower case;
/// <c>sprk_Policy</c> and <c>sprk_PolicyVersion</c> are not (as <c>OntologyAdminAuthoringService.AuthoringPrivilegeName</c>).</para>
/// </remarks>
public static class BillingAdminAuthorization
{
    public const string CreateRuleSetPrivilege = "prvCreatesprk_ruleset";
    public const string WriteRuleSetPrivilege = "prvWritesprk_ruleset";
    public const string CreatePolicyVersionPrivilege = "prvCreatesprk_PolicyVersion";
    public const string WritePolicyVersionPrivilege = "prvWritesprk_PolicyVersion";
    public const string WritePolicyPrivilege = "prvWritesprk_Policy";
    public const string CreatePolicyPrivilege = "prvCreatesprk_Policy";

    private const string Source = "billing admin";

    private static FinanceAuthorizationCheck Check(string privilege, string entitySet) =>
        FinanceAuthorizationCheck.CallerPrivilege(privilege, entitySet, Source);

    /// <summary>The billing-admin marker alone: reads of rule sets and proposals, assignment, visibility.</summary>
    public static FinanceAuthorizationTargets Admin() =>
        FinanceAuthorizationTargets.Authorize(Check(WriteRuleSetPrivilege, "sprk_rulesets"));

    /// <summary>Create a rule set (and its source document).</summary>
    public static FinanceAuthorizationTargets CreateRuleSet() =>
        FinanceAuthorizationTargets.Authorize(
            Check(CreateRuleSetPrivilege, "sprk_rulesets"),
            Check(WriteRuleSetPrivilege, "sprk_rulesets"));

    /// <summary>Edit a rule (in place, or as a new version).</summary>
    public static FinanceAuthorizationTargets EditRule() =>
        FinanceAuthorizationTargets.Authorize(
            Check(WriteRuleSetPrivilege, "sprk_rulesets"),
            Check(CreatePolicyVersionPrivilege, "sprk_policyversions"),
            Check(WritePolicyVersionPrivilege, "sprk_policyversions"));

    /// <summary>Accept or reject a rule.</summary>
    public static FinanceAuthorizationTargets DecideRule() =>
        FinanceAuthorizationTargets.Authorize(
            Check(WriteRuleSetPrivilege, "sprk_rulesets"),
            Check(WritePolicyVersionPrivilege, "sprk_policyversions"));

    /// <summary>
    /// Start the rule drafting job (task 075): the job creates policies and versions (as the application) and writes the rule set's
    /// not-converted list, so the caller must hold Create on both policy tables and Write on the rule set marker.
    /// </summary>
    public static FinanceAuthorizationTargets DraftRules() =>
        FinanceAuthorizationTargets.Authorize(
            Check(WriteRuleSetPrivilege, "sprk_rulesets"),
            Check(CreatePolicyPrivilege, "sprk_policies"),
            Check(CreatePolicyVersionPrivilege, "sprk_policyversions"));

    /// <summary>Publish a rule set: puts versions in force.</summary>
    public static FinanceAuthorizationTargets Publish() =>
        FinanceAuthorizationTargets.Authorize(
            Check(WriteRuleSetPrivilege, "sprk_rulesets"),
            Check(WritePolicyVersionPrivilege, "sprk_policyversions"),
            Check(WritePolicyPrivilege, "sprk_policies"));

    // ── Procedures (ebilling-r1 task 081, procedure model 3.1 I-P5 and 5.1) ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create on <c>sprk_procedure</c>, as the live privilege table names it (lower case, spaarkedev1 2026-10-22; pinned by
    /// <c>ProcedureEndpointsTests</c>). No solution role carries it until uac-r2 task 191 (Procedures are internal-only), so today only a
    /// System Administrator passes.
    /// </summary>
    public const string CreateProcedurePrivilege = "prvCreatesprk_procedure";

    /// <summary><c>sprk_procedure</c>'s entity set, the Record-path target of every Procedure route.</summary>
    public const string ProcedureEntitySet = "sprk_procedures";

    private const string ProcedureRouteKey = "procedureId";

    /// <summary>
    /// Create a Procedure: Create on <c>sprk_procedure</c>, and the billing-admin marker plus Create on <c>sprk_ruleset</c>, because the same
    /// transaction creates the Procedure's rule set as the application.
    /// </summary>
    public static FinanceAuthorizationTargets CreateProcedure() =>
        FinanceAuthorizationTargets.Authorize(
            Check(CreateProcedurePrivilege, ProcedureEntitySet),
            Check(CreateRuleSetPrivilege, "sprk_rulesets"),
            Check(WriteRuleSetPrivilege, "sprk_rulesets"));

    /// <summary>Read a Procedure and its versions: the caller's own Read on the Procedure (Record path, as the caller).</summary>
    public static FinanceAuthorizationTargets ReadProcedure(EndpointFilterInvocationContext context) =>
        FinanceAuthorizationTargets.FromRoute(context, ProcedureRouteKey, ProcedureEntitySet, "read");

    /// <summary>
    /// Change a Procedure, a version's dates, retire a version, or preview the Procedure's retire: the caller's own Write on the Procedure AND
    /// the billing-admin marker (I-P5). An unreadable route id yields no check, which the filter denies.
    /// </summary>
    public static FinanceAuthorizationTargets WriteProcedure(EndpointFilterInvocationContext context) => OnProcedure(context);

    /// <summary>Create a version: as <see cref="WriteProcedure"/>, plus Create on <c>sprk_policyversion</c> (the copy-forward creates them).</summary>
    public static FinanceAuthorizationTargets CreateProcedureVersion(EndpointFilterInvocationContext context) =>
        OnProcedure(context, Check(CreatePolicyVersionPrivilege, "sprk_policyversions"));

    /// <summary>Publish a version: as <see cref="WriteProcedure"/>, plus Write on both policy tables (it puts policy versions in force).</summary>
    public static FinanceAuthorizationTargets PublishProcedureVersion(EndpointFilterInvocationContext context) =>
        OnProcedure(context, Check(WritePolicyVersionPrivilege, "sprk_policyversions"), Check(WritePolicyPrivilege, "sprk_policies"));

    /// <summary>Retire the Procedure: as <see cref="WriteProcedure"/>, plus Write on <c>sprk_policy</c> (it switches every policy Off).</summary>
    public static FinanceAuthorizationTargets RetireProcedure(EndpointFilterInvocationContext context) =>
        OnProcedure(context, Check(WritePolicyPrivilege, "sprk_policies"));

    /// <summary>
    /// uac-r2 review item D (2026-10-22, I-P5): on a rule set that belongs to a Procedure (<c>sprk_ruleset.sprk_procedure</c> set), a rule
    /// edit or decision also needs the caller's own Write on THAT Procedure: the same Record-path "write" check on <c>sprk_procedures</c> the
    /// Procedure routes declare (<see cref="WriteProcedure"/>), asked of Dataverse as the caller; otherwise 403 (<c>insufficient_rights</c> when Dataverse
    /// answered that the right is not held, <c>system_failure</c> when it gave no answer).
    /// A legacy set (no Procedure) or an unknown set is unchanged (the route's own declaration, then the service's 404). Added AFTER the
    /// route's <c>AddFinanceAuthorizationFilter</c> (step 0 and the billing-admin privileges run first). Fails closed: a fault reading the
    /// rule set is a 403 <c>sdap.access.error.system_failure</c>.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <see cref="FinanceAuthorizationFilter"/> evaluates a Record check; its target declaration is
    /// synchronous and cannot read the rule set. <i>Extension</i> — this filter reads the set's Procedure, then asks the same rights question
    /// through <see cref="CallerReadCheck"/> (the finance read APIs' caller-rights check, which keeps "denied" apart from "no answer"). <i>Cost of doing nothing</i> — a billing
    /// admin without Write on a Procedure could still change and decide its rules through the rule set routes.</remarks>
    public static TBuilder RequireWriteOnTheRuleSetsProcedure<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(OnTheRuleSetsProcedure("write"));

    /// <summary>
    /// uac-r2 re-check round 2 (2026-10-22, I-P5): the sibling of <see cref="RequireWriteOnTheRuleSetsProcedure"/> for the READ of a rule set.
    /// On a set that belongs to a Procedure, reading its rules also needs the caller's own Read on THAT Procedure (the Record-path "read" check
    /// on <c>sprk_procedures</c> the Procedure read route declares); otherwise 403. A legacy set or an unknown set is unchanged. Same
    /// resolution of the Procedure (server-side, from the stored set), same ordering (step 0, billing admin, then the Procedure), same fail closed.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — the Write filter above. <i>Extension</i> — one shared body, parameterised by the operation. <i>Cost of
    /// doing nothing</i> — a billing admin without Read on a Procedure could read its rules (clause text, rule bodies) through the rule set read.</remarks>
    public static TBuilder RequireReadOnTheRuleSetsProcedure<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(OnTheRuleSetsProcedure("read"));

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> OnTheRuleSetsProcedure(string operation) =>
        async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices;
            if (!http.Request.RouteValues.TryGetValue(RuleSetRouteKey, out var raw) || !Guid.TryParse(raw?.ToString(), out var ruleSetId))
            {
                return await next(context); // the route constraint guarantees a guid; the service answers anything else
            }

            Guid? procedureId;
            try
            {
                procedureId = await services.GetRequiredService<RuleSetService>().ProcedureOfRuleSetAsync(ruleSetId, http.RequestAborted);
            }
            catch (Exception ex)
            {
                services.GetService<ILogger<FinanceAuthorizationFilter>>()?.LogError(ex,
                    "Rule set {RuleSetId}: its Procedure could not be read for the Procedure {Operation} check; denying (fail closed)", ruleSetId, operation);
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (procedureId is not { } id || id == Guid.Empty)
            {
                return await next(context);
            }

            // The caller's own rights on the Procedure, asked of Dataverse as the caller (the Procedure routes' check). Denied only when Dataverse
            // answered and the right is not held; no answer (a fault, a throttle, no identity) is system_failure, never insufficient_rights.
            var caller = new InvoiceContextService.InvoiceCaller(
                CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
            var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
            var outcome = await CallerReadCheck.CheckAsync(
                services.GetRequiredService<AuthorizationService>(), caller, ProcedureEntitySet, operation, id, logger,
                $"Rule set {ruleSetId}: caller {operation} on its Procedure", http.RequestAborted);
            if (outcome == CallerReadOutcome.Readable)
            {
                return await next(context);
            }

            // uac-r2 re-check round 3 (F1): the detail names the right, so the user sees what is missing; a no-answer carries the generic detail.
            return outcome == CallerReadOutcome.Refused
                ? ProblemDetailsHelper.Forbidden(
                    FinanceAuthorizationFilter.InsufficientRightsReasonCode,
                    detail: $"You need {(operation == "write" ? "Write" : "Read")} on this rule set's Procedure.",
                    traceId: http.TraceIdentifier)
                : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        };

    private const string RuleSetRouteKey = "ruleSetId";

    private static FinanceAuthorizationTargets OnProcedure(EndpointFilterInvocationContext context, params FinanceAuthorizationCheck[] extra)
    {
        var record = FinanceAuthorizationTargets.FromRoute(context, ProcedureRouteKey, ProcedureEntitySet, "write");
        return record.Checks.Count == 0
            ? record // no id, no check: the filter denies
            : FinanceAuthorizationTargets.Authorize(record.Checks.Append(Check(WriteRuleSetPrivilege, "sprk_rulesets")).Concat(extra).ToArray());
    }
}
