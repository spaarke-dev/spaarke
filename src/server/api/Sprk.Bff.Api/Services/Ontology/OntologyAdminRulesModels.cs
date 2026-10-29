using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Ontology;

// Wire shapes of the Ontology admin Rules routes (ontology platform R1 task 100; spec FR-57; reconciliation M-3..M-5, H-7).
// Plain records, camelCase on the wire. Option-set values are sent as the labels the Console shows, never as raw numbers.

/// <summary>How a Rules service call ended. The route maps each to one HTTP answer (see <c>OntologyAdminRulesEndpoints</c>).</summary>
public enum OntologyAdminRulesOutcome
{
    Ok,

    /// <summary>No user context, or the on-behalf-of exchange was not configured or was refused: #1312's single 403 (D-29).</summary>
    CallerUnresolved,

    /// <summary>Dataverse refused the caller (401/403): they do not hold the privilege (for a write: the Ontology Administrator role).</summary>
    Forbidden,

    /// <summary>The rule does not exist, is not visible to the caller, or is not an Ontology rule (the uniform answer).</summary>
    NotFound,

    /// <summary>The request body is not acceptable (no reason, no value). Nothing was read or written.</summary>
    Invalid,

    /// <summary>The rule is in a state that refuses the change (inactive or Retired), or it changed since the caller read it. Nothing was written.</summary>
    Conflict,

    /// <summary>Dataverse could not be asked or answered with a fault. Never a shorter list.</summary>
    Unavailable,
}

/// <summary>A Rules service answer: the outcome, the value when <see cref="OntologyAdminRulesOutcome.Ok"/>, and a reason code and
/// text for every other outcome.</summary>
public sealed record OntologyAdminRulesResult<T>(OntologyAdminRulesOutcome Outcome, T? Value, string? ReasonCode, string? Detail)
{
    public static OntologyAdminRulesResult<T> Ok(T value) => new(OntologyAdminRulesOutcome.Ok, value, null, null);

    public static OntologyAdminRulesResult<T> Fail(OntologyAdminRulesOutcome outcome, string? reasonCode = null, string? detail = null) =>
        new(outcome, default, reasonCode, detail);
}

/// <summary>
/// Closes a retired policy's open Signals now (task 033's <see cref="SignalClosureService.ClosePolicyRetiredNowAsync"/>). A delegate
/// rather than the concrete service so the Retire ordering and the re-run rule can be proven without the Signal writer's
/// constructor (eight collaborators); registered in <c>SignalsModule</c> as that one method.
/// </summary>
public delegate Task<SignalClosureOutcome> PolicyRetiredClosure(Guid policyId, CancellationToken ct);

// ---- requests ---------------------------------------------------------------------------------------------------------------

/// <summary>PUT body: the value to set. Nullable so a body without it is refused rather than read as Off.</summary>
public sealed record RuleEnabledRequest(bool? Enabled);

/// <summary>POST body: why the rule is retired (required, at most <see cref="OntologyAdminRulesService.MaxReasonLength"/> characters).</summary>
public sealed record RuleRetireRequest(string? Reason);

// ---- list -------------------------------------------------------------------------------------------------------------------

/// <summary>A rule's action rate over the version in force (H-7): <c>acted / (acted + dismissed)</c>, 90 days; no rate below 5 judged.</summary>
public sealed record RuleActionRate(int Acted, int Dismissed, double? Rate, bool TooFewToJudge);

/// <summary>
/// The numbers #44 allows: open items, last fired, action rate. "Needs a look" is a rate below one half. The three are read
/// independently (<see cref="RuleMetricsAvailability"/>): a number that could not be read is null here and its flag is false, so one
/// failing read never blanks the other two. <see cref="LastFiredOn"/> is null both for "never" and for "could not be read" within the
/// window; the availability flag tells them apart.
/// </summary>
/// <param name="LastFiredOn">The newest Signal created for the rule in the last <see cref="OntologyAdminRulesService.LastFiredWindowDays"/> days.</param>
public sealed record RuleMetrics(int? OpenItems, DateTime? LastFiredOn, RuleActionRate? ActionRate, bool NeedsALook);

/// <summary>Which of the three Signal numbers were read. A false flag means that number is shown as "could not be read", not as zero or never.</summary>
public sealed record RuleMetricsAvailability(bool OpenItems, bool LastFired, bool ActionRate)
{
    public bool All => OpenItems && LastFired && ActionRate;
}

/// <param name="State">On, Off, Retired or Inactive. Retired wins over the On/Off column.</param>
/// <param name="Metrics">Always present; each number inside is null when it could not be read (the response's availability says which).</param>
public sealed record RuleSummary(
    Guid PolicyId,
    string Code,
    string Name,
    string Lane,
    string State,
    bool Enabled,
    int? VersionNumber,
    string? Headline,
    string Severity,
    string SubjectType,
    RuleMetrics Metrics);

/// <param name="MetricsAvailable">True only when all three numbers were read (<see cref="RuleMetricsAvailability.All"/>).</param>
/// <param name="LastFiredWindowDays">The window the last-fired number looks back over (its aggregate is bounded by Dataverse's 50,000-row limit).</param>
public sealed record RuleListResponse(
    IReadOnlyList<RuleSummary> Rules,
    bool MetricsAvailable,
    RuleMetricsAvailability MetricsAvailability,
    int ActionRateWindowDays,
    int LastFiredWindowDays,
    int MinimumJudged);

// ---- rule page --------------------------------------------------------------------------------------------------------------

public sealed record RuleClauseView(string ClauseId, string Kind, string Text, string Formal);

/// <param name="Refusal">Set, with no clauses, when the describer refused the body (it never guesses). Shown instead of the words.</param>
public sealed record RuleConditionView(string RuleType, IReadOnlyList<RuleClauseView> Clauses, string? Refusal);

public sealed record RuleWordingView(string? Headline, string? MessageTemplate, string? ProposedAction);

public sealed record RulePlanActionView(string Code, string Label, IReadOnlyList<string> EffectLines);

/// <param name="Refusal">Set, with empty lists, when the stored plan does not resolve against the closed catalog.</param>
public sealed record RulePlanView(
    IReadOnlyList<RulePlanActionView> Actions,
    IReadOnlyList<RulePlanActionView> NextSteps,
    IReadOnlyList<string> DismissalReasons,
    string? Refusal);

public sealed record RuleScopeView(string? Tenant, Guid? MatterId, string? MatterName, int? Priority);

public sealed record RuleNoiseView(bool CountsTowardSuppression, int SuppressAfterDismissals, int SuppressionWindowDays, int QuietWindowDays);

public sealed record RuleBehaviourView(string Lane, string Severity, string? WorkType, string SubjectType, string State, string? RetiredReason);

/// <summary>One immutable <c>sprk_policyversion</c> row. <see cref="InForce"/> marks the version the rule runs on.</summary>
public sealed record RuleVersionView(
    Guid VersionId,
    int Number,
    string? Headline,
    string? AuthoredBy,
    DateTime? CreatedOn,
    DateTime? InForceFrom,
    DateTime? InForceTo,
    bool InForce);

/// <summary>A change-history line (#43): built from the immutable version rows and the policy row's own modified-on and modified-by,
/// never from the audit log, which no Spaarke role can read.</summary>
public sealed record RuleHistoryEntry(DateTime? When, string? By, string Event);

public sealed record RuleDetailResponse(
    RuleSummary Summary,
    RuleConditionView Condition,
    RuleWordingView Wording,
    RulePlanView Plan,
    RuleScopeView Scope,
    RuleNoiseView Noise,
    RuleBehaviourView Behaviour,
    IReadOnlyList<RuleVersionView> Versions,
    IReadOnlyList<RuleHistoryEntry> History,
    bool MetricsAvailable,
    RuleMetricsAvailability MetricsAvailability,
    int ActionRateWindowDays,
    int LastFiredWindowDays,
    int MinimumJudged);

// ---- writes -----------------------------------------------------------------------------------------------------------------

public sealed record RuleEnabledResult(Guid PolicyId, bool Enabled);

/// <summary>What closing the retired rule's open Signals did (task 033). <see cref="Complete"/> false means some are still open: they
/// close at the next nightly pass. <see cref="Unavailable"/> means the closure could not run at all.</summary>
public sealed record RuleClosureView(int Closed, int ChangedSinceRead, int Failed, int PolicyFailures, bool Complete, bool Unavailable);

public sealed record RuleRetiredResult(Guid PolicyId, string State, string Reason, RuleClosureView Closure);
