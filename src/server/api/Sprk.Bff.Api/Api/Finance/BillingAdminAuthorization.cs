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
}
