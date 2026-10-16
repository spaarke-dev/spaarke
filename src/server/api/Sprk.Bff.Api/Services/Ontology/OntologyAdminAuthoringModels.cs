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

    /// <summary>The uniform 404: the policy does not exist, the caller cannot read it, or it is not an Ontology policy.</summary>
    NotFound,

    /// <summary>409 with a reason code (no dry run on this body, the code is taken, the policy is retired or changed meanwhile).</summary>
    Conflict,

    /// <summary>503: a fault; nothing is claimed about the caller's rights.</summary>
    Unavailable,
}

/// <summary>One authoring call's outcome, its value on success, and its field errors or reason code otherwise.</summary>
public sealed record OntologyAdminAuthoringResult<T>(
    OntologyAdminAuthoringOutcome Outcome,
    T? Value = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? ReasonCode = null)
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
public sealed record OntologyDryRunRequest(
    Guid? PolicyId,
    string? Lane,
    string? RuleType,
    string? RuleBody,
    string? MessageTemplate);

/// <summary>A new rule (FR-10: it saves Off). <paramref name="Severity"/> and <paramref name="WorkType"/> are the option values of
/// <c>sprk_severity</c> / <c>sprk_worktype</c>; <paramref name="Priority"/> defaults to 500 and
/// <paramref name="CountsTowardSuppression"/> to true.</summary>
public sealed record OntologyNewPolicyRequest(
    string? PolicyCode,
    string? Name,
    string? ShortName,
    string? Lane,
    int? Severity,
    int? WorkType,
    int? Priority,
    bool? CountsTowardSuppression,
    OntologyRuleDraft? Draft);

/// <summary>A new version of an existing Ontology policy, published.</summary>
public sealed record OntologyPublishRequest(OntologyRuleDraft? Draft);

/// <summary>The authorable condition catalog (#46), every entry read from the compiler's own lists.</summary>
public sealed record OntologyAuthoringCatalog(
    IReadOnlyList<string> RuleTypes,
    IReadOnlyList<OntologyCatalogSubject> Subjects,
    OntologyCatalogOperators Operators,
    OntologyCatalogRelativeDates RelativeDates,
    OntologyCatalogLimits Limits,
    OntologyCatalogTriage Triage);

/// <summary>A table a rule may be about, with the fields and joins the compiler verifies for it.</summary>
public sealed record OntologyCatalogSubject(
    string Entity,
    IReadOnlyList<string> DateOnlyFields,
    IReadOnlyList<OntologyCatalogThresholdField> ThresholdFields,
    IReadOnlyList<OntologyCatalogJoin> Joins);

/// <summary>A column a Threshold rule may compare, with the range Dataverse holds for it.</summary>
public sealed record OntologyCatalogThresholdField(string Field, decimal Min, decimal Max);

/// <summary>A clause table reachable from the subject through its one verified lookup.</summary>
public sealed record OntologyCatalogJoin(string Related, string Path, IReadOnlyList<string> DateOnlyFields);

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
    bool Recorded);

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
