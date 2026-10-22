using Sprk.Bff.Api.Api.Filters;

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
/// every Ontology Administrator a billing admin (over-granting). <c>sprk_ruleset</c> is billing's own table: no solution role carries
/// anything beyond Read on it (Core and Basic User: Read, Deep), so Create / Write on it names exactly the people given billing rule
/// administration, and every route requires it. A route that writes policy rows ALSO requires the privilege of the table it writes,
/// so Dataverse's own rights are never bypassed by the application-identity write that follows. Reads of the proposals (clause text,
/// rule bodies) are admin-only too, so they require the same Write marker, not Read (which every Core User holds).</para>
/// <para><b>Depth (D-108, owner 2026-10-17).</b> Write at ANY depth counts: there is no Org-depth requirement. The check asks whether the
/// caller holds the privilege, not at what depth. The role the owner grants carries Org depth in practice, so a Basic-depth Write would
/// still pass this check; that is accepted.</para>
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

    private static FinanceAuthorizationTargets OnProcedure(EndpointFilterInvocationContext context, params FinanceAuthorizationCheck[] extra)
    {
        var record = FinanceAuthorizationTargets.FromRoute(context, ProcedureRouteKey, ProcedureEntitySet, "write");
        return record.Checks.Count == 0
            ? record // no id, no check: the filter denies
            : FinanceAuthorizationTargets.Authorize(record.Checks.Append(Check(WriteRuleSetPrivilege, "sprk_rulesets")).Concat(extra).ToArray());
    }
}
