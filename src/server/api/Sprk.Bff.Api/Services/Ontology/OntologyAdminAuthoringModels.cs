namespace Sprk.Bff.Api.Services.Ontology;

// The request and response shapes of the Ontology admin authoring routes (ontology platform R1 task 101, D-22 slice b). Plain records;
// the behaviour is in OntologyAdminAuthoringService.

/// <summary>What one authoring call decided. The route maps each outcome to exactly one HTTP answer.</summary>
public enum OntologyAdminAuthoringOutcome
{
    /// <summary>200.</summary>
    Ok,

    /// <summary>201 (a new policy was saved).</summary>
    Created,

    /// <summary>400 with field-level errors; nothing was written.</summary>
    Invalid,

    /// <summary>The single 403 (D-29): the caller could not be resolved to a Dataverse user.</summary>
    CallerUnresolved,

    /// <summary>403: Dataverse refused the caller (no privilege for the read or write); nothing was written.</summary>
    Refused,

    /// <summary>403: the caller does not hold the authoring privilege the dry run requires (F1-1); nothing was evaluated.</summary>
    Forbidden,

    /// <summary>The uniform 404: the policy does not exist, the caller cannot read it, or it is not an Ontology policy.</summary>
    NotFound,

    /// <summary>409 with a reason code (no dry run on this body, the code is taken, the policy is retired or changed meanwhile).</summary>
    Conflict,

    /// <summary>503: a fault; nothing is claimed about the caller's rights.</summary>
    Unavailable,
}

/// <summary>One authoring call's outcome, its value on success, and its field errors or reason code otherwise.</summary>
/// <param name="PolicyId">On a failed save whose policy was created but whose current version could not be set (K6): that policy's
/// id, returned in the problem body so the client can recover by publishing a version of it.</param>
public sealed record OntologyAdminAuthoringResult<T>(
    OntologyAdminAuthoringOutcome Outcome,
    T? Value = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? ReasonCode = null,
    Guid? PolicyId = null)
    where T : class;

/// <summary>A rule version as the wizard authors it. Every member is validated server-side; nothing here is trusted.</summary>
/// <param name="RuleType">"Existence" or "Threshold" (or the option value). Switch is refused (D-16).</param>
/// <param name="RuleBody">The <c>sprk_rulebody</c> JSON, exactly as it will be stored.</param>
/// <param name="MessageTemplate">The <c>sprk_messagetemplate</c>; may name only the fields the body reads (§0.3).</param>
/// <param name="ShortHeadline">The <c>sprk_shortheadline</c> (required, at most 200 characters).</param>
/// <param name="ProposedAction">The <c>sprk_proposedaction</c> (required, at most 400 characters).</param>
/// <param name="DecisionPlan">The <c>sprk_decisionplan</c> JSON, resolved against task 036's closed action catalog for the lane.</param>
public sealed record OntologyRuleDraft(
    string? RuleType,
    string? RuleBody,
    string? MessageTemplate,
    string? ShortHeadline,
    string? ProposedAction,
    string? DecisionPlan);

/// <summary>A dry run (#39). <paramref name="PolicyId"/> names an existing Ontology policy (its lane and its open Signals are read as
/// the caller); without it, <paramref name="Lane"/> ("Decide" or "Do") is required and every row is new.</summary>
/// <param name="MatterId">Task 102b (D-165): the matter the rule is limited to, or null for all matters. Allowed only on a rule about
/// <c>sprk_matter</c>; the matter is read as the caller (absent and unreadable are the same refusal). The dry run evaluates within it.</param>
/// <param name="Tenant">Task 102b (D-165): never read. Present only so a request that carries a tenant is refused: the tenant scope stays
/// blank (every environment is single-tenant).</param>
/// <param name="AllMatters">Task 102b fix round 1 (F4): the explicit "all matters" intent. For a policy limited to a matter, a dry run
/// with neither <paramref name="MatterId"/> nor <c>AllMatters: true</c> is refused (<c>ontology.admin.scope_intent_required</c>), as is one
/// with both; for an unscoped policy or a new rule it may be omitted.</param>
public sealed record OntologyDryRunRequest(
    Guid? PolicyId,
    string? Lane,
    string? RuleType,
    string? RuleBody,
    string? MessageTemplate,
    Guid? MatterId = null,
    string? Tenant = null,
    bool? AllMatters = null);

/// <summary>A new rule (FR-10: it saves Off). <paramref name="Severity"/> and <paramref name="WorkType"/> are the option values of
/// <c>sprk_severity</c> / <c>sprk_worktype</c>; <paramref name="Priority"/> defaults to 500 and
/// <paramref name="CountsTowardSuppression"/> to true. <paramref name="MatterId"/> and <paramref name="Tenant"/> are the scope, as on
/// <see cref="OntologyDryRunRequest"/> (task 102b): the matter is written to <c>sprk_matter</c>; <c>sprk_tenant</c> is never written.</summary>
public sealed record OntologyNewPolicyRequest(
    string? PolicyCode,
    string? Name,
    string? ShortName,
    string? Lane,
    int? Severity,
    int? WorkType,
    int? Priority,
    bool? CountsTowardSuppression,
    OntologyRuleDraft? Draft,
    Guid? MatterId = null,
    string? Tenant = null);

/// <summary>A new version of an existing Ontology policy, published. <paramref name="MatterId"/> is the policy's scope from this version
/// on, as on <see cref="OntologyDryRunRequest"/> (task 102b); a change is written with the pointer move, and the policy's open Signals
/// outside the new scope close Superseded in the same publish. <paramref name="Tenant"/> is refused when set. <paramref name="AllMatters"/>
/// (fix round 1, F4): clearing a stored matter scope takes <c>AllMatters: true</c>; a publish of a scoped policy with neither it nor a
/// matter is refused (<c>ontology.admin.scope_intent_required</c>) and never clears the scope, and one with both is refused the same way.
/// An unscoped policy published with neither stays unscoped.</summary>
public sealed record OntologyPublishRequest(OntologyRuleDraft? Draft, Guid? MatterId = null, string? Tenant = null, bool? AllMatters = null);

/// <summary>A compile-only check of a draft (task 102a). With <paramref name="PolicyId"/> it is checked as a new version of that Ontology
/// policy would be on publish (its lane, read as the caller). Without one, <paramref name="Lane"/> is required and it is checked as a new
/// rule would be on save. <paramref name="MatterId"/> and <paramref name="Tenant"/> are checked as the dry run and publish check them (task
/// 102b); <paramref name="AllMatters"/> (fix round 2, K2) is the explicit "all matters" intent, with the same meaning as on
/// <see cref="OntologyDryRunRequest"/>: for an existing policy limited to a matter, neither a matter nor <c>AllMatters: true</c>, or both,
/// is refused with the same code and field as on a dry run or publish. A new rule has no stored scope, so there it may be omitted.</summary>
public sealed record OntologyValidateRequest(
    Guid? PolicyId, string? Lane, OntologyRuleDraft? Draft, Guid? MatterId = null, string? Tenant = null, bool? AllMatters = null);

/// <summary>The answer of the validate route. Nothing was written or evaluated.</summary>
/// <param name="Valid">True when save or publish would accept the draft, apart from the dry-run proof and the policy's own fields.</param>
/// <param name="Errors">The field errors save and publish would return, one per message, ordered by field.</param>
/// <param name="ReasonCode">A stable code for a refusal that has one (<c>ontology.admin.column_not_authorable</c>,
/// <c>ontology.admin.duplicate_action</c>, <c>ontology.admin.scope_subject_mismatch</c>); otherwise null.</param>
/// <param name="TemplateEligibleFields">The fields the message template may name: the compiled predicate's TemplateEligibleFields that
/// are on the authorable-column allow-list (section 0.3). Empty when the body does not compile.</param>
public sealed record OntologyRuleValidation(
    bool Valid,
    IReadOnlyList<OntologyValidationError> Errors,
    string? ReasonCode,
    IReadOnlyList<string> TemplateEligibleFields);

/// <summary>One field error.</summary>
public sealed record OntologyValidationError(string Field, string Message);

/// <summary>The authorable condition catalog (#46), every entry read from the compiler's own lists. <paramref name="Lanes"/> (task 102a)
/// holds, per lane, what the wizard's Actions and Basics steps offer, read from the server's closed catalogs.</summary>
public sealed record OntologyAuthoringCatalog(
    IReadOnlyList<string> RuleTypes,
    IReadOnlyList<OntologyCatalogSubject> Subjects,
    OntologyCatalogOperators Operators,
    OntologyCatalogRelativeDates RelativeDates,
    OntologyCatalogLimits Limits,
    OntologyCatalogTriage Triage,
    IReadOnlyList<OntologyCatalogLane> Lanes);

/// <summary>A table a rule may be about, with the fields and joins the compiler verifies for it. <paramref name="AuthorableFields"/>
/// (task 102a) are the columns of this table a rule may name (<c>PolicyVersionValidator.AuthorableColumns</c>).</summary>
public sealed record OntologyCatalogSubject(
    string Entity,
    IReadOnlyList<string> DateOnlyFields,
    IReadOnlyList<OntologyCatalogThresholdField> ThresholdFields,
    IReadOnlyList<OntologyCatalogJoin> Joins,
    IReadOnlyList<string> AuthorableFields);

/// <summary>A column a Threshold rule may compare, with the range Dataverse holds for it.</summary>
public sealed record OntologyCatalogThresholdField(string Field, decimal Min, decimal Max);

/// <summary>A clause table reachable from the subject through its one verified lookup. <paramref name="AuthorableFields"/> (task 102a)
/// are the columns of the clause table a filter may name (the join path itself is not a filter field).</summary>
public sealed record OntologyCatalogJoin(string Related, string Path, IReadOnlyList<string> DateOnlyFields, IReadOnlyList<string> AuthorableFields);

/// <summary>
/// One lane's authoring lists (task 102a, owner option A), each read from its server source:
/// <list type="bullet">
/// <item>actions: the codes <c>DecisionActionCatalog</c> allows in this lane's plan, with their mutual excludes;</item>
/// <item>next steps: the codes a plan's <c>nextSteps</c> may name;</item>
/// <item>dismissal reasons: the lane's closed dismissal-reason list;</item>
/// <item>severities and work types: the <c>sprk_policy</c> option labels the Rules page uses.</item>
/// </list>
/// The server has no lane mapping for severities or work types, so both lanes carry the full list.
/// </summary>
public sealed record OntologyCatalogLane(
    string Lane,
    IReadOnlyList<OntologyCatalogAction> Actions,
    IReadOnlyList<OntologyCatalogAction> NextSteps,
    IReadOnlyList<OntologyCatalogChoice> DismissalReasons,
    IReadOnlyList<OntologyCatalogOption> Severities,
    IReadOnlyList<OntologyCatalogOption> WorkTypes);

/// <summary>One catalog action: its stable code, its label, and the codes it excludes (mutual).</summary>
public sealed record OntologyCatalogAction(string Code, string Label, IReadOnlyList<string> Excludes);

/// <summary>A choice whose stable value is a code (a dismissal reason).</summary>
public sealed record OntologyCatalogChoice(string Value, string Label);

/// <summary>A choice whose value is a Dataverse option value (severity, work type).</summary>
public sealed record OntologyCatalogOption(int Value, string Label);

/// <summary>The operators, by where they may appear.</summary>
public sealed record OntologyCatalogOperators(
    IReadOnlyList<string> Comparison,
    IReadOnlyList<string> Threshold,
    IReadOnlyList<string> RangeLower,
    IReadOnlyList<string> RangeUpper,
    IReadOnlyList<string> NullTests,
    int MaxInListLength);

/// <summary>The relative-date grammar: the forms and the largest day count.</summary>
public sealed record OntologyCatalogRelativeDates(IReadOnlyList<string> Forms, int MaxDays);

/// <summary>The compiler's limits.</summary>
public sealed record OntologyCatalogLimits(
    int MaxClauses, int MaxRuleBodyLength, int DefaultQuietWindowDays, int MaxQuietWindowDays);

/// <summary>The triage categories (by name) a communication clause may name, read as the caller.</summary>
public sealed record OntologyCatalogTriage(string Entity, string Field, IReadOnlyList<OntologyCatalogCategory> Categories);

/// <summary>One <c>sprk_triagecategory</c> row.</summary>
public sealed record OntologyCatalogCategory(Guid Id, string Name, bool Enabled);

/// <summary>The dry run's answer. Every row and every count is over rows the CALLER can read; nothing counts a row they cannot.</summary>
/// <param name="Rows">Per subject the draft holds for: <c>open</c> (an open Work Item exists), <c>suppressed</c> (muted, or in the
/// quiet window after a dismissal) or <c>new</c>.</param>
/// <param name="WouldSupersede">The caller-readable open Signals of the policy that publishing closes as Superseded.</param>
/// <param name="WouldReraise">Of those, the ones whose subject the draft still holds for: re-raised as a new episode.</param>
/// <param name="NotPlaceable">Subjects the draft holds for whose core record the writer cannot derive: never raised (D-38).</param>
/// <param name="PolicyEnabled">Whether the policy is On; publishing re-evaluates only an enabled policy (null for a new rule).</param>
/// <param name="Recorded">Whether the dry run was recorded for this caller and body; publish and save require it.</param>
/// <param name="StoredMatterId">Task 102b fix round 1 (F4): for an existing policy, the matter it is limited to NOW (as stored), or null
/// for all matters (and for a new rule). The wizard compares it with the scope it publishes to say when the publish changes it.</param>
/// <param name="StoredMatterName">The stored matter's name, when the caller can read it; otherwise null (the id is still returned).</param>
public sealed record OntologyDryRunResult(
    IReadOnlyList<OntologyDryRunRow> Rows,
    int Open,
    int Suppressed,
    int New,
    int WouldSupersede,
    int WouldReraise,
    int NotPlaceable,
    bool? PolicyEnabled,
    DateTimeOffset WindowAnchorUtc,
    bool Recorded,
    Guid? StoredMatterId = null,
    string? StoredMatterName = null);

/// <summary>One subject of the dry run. Only identity and display: never facts or witness evidence.</summary>
public sealed record OntologyDryRunRow(
    string SubjectEntity, Guid SubjectId, string? Name, string? Number, string? CoreEntity, Guid? CoreId, string Status);

/// <summary>A saved new policy (Off) and its first version.</summary>
public sealed record OntologyPolicySaved(Guid PolicyId, Guid PolicyVersionId, int VersionNumber, bool Enabled);

/// <summary>A published version. <paramref name="Reevaluation"/> is <c>completed</c>, <c>failed</c> (the nightly run retries) or
/// <c>notEvaluated</c> (the policy is Off).</summary>
public sealed record OntologyVersionPublished(
    Guid PolicyId,
    Guid PolicyVersionId,
    int VersionNumber,
    Guid? PreviousVersionId,
    string Reevaluation,
    int SupersededClosed,
    int Raised);
