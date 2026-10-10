namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// Stable <see cref="EventId"/>s for Signal-writer Error logs (task 030 rework — owner directive 2026-10-04:
/// a refused write must not look like "no conditions found"). One <see cref="EventId"/> covers every refusal
/// path that throws; the owner-refusal SKIP (task 039) has its own Warning <see cref="WriteSkippedOwnerRefused"/>.
/// The log's structured <c>reason</c> property (one of
/// <see cref="Telemetry.OntologyWriterFailureReason"/>'s constants) distinguishes WHICH one — the same
/// vocabulary <see cref="Telemetry.OntologyWriterTelemetry.RecordFailure"/> uses for the matching metric
/// dimension, so a log query and a metric query always agree.
/// </summary>
public static class OntologyWriterEvents
{
    /// <summary>
    /// Logged at Error, exactly once per refusal, by <see cref="SignalWriter"/> or
    /// <see cref="OntologyWriterDataverseClient"/> — never swallowed; the exception is always rethrown after
    /// this is logged. Structured properties: <c>reason</c> (bounded-cardinality —
    /// <see cref="Telemetry.OntologyWriterFailureReason"/>) and <c>policyCode</c>. NEVER fact values or the
    /// rendered sentence (matter detail) — see each call site's own comment for why.
    /// </summary>
    public static readonly EventId WriteRefused = new(50300, nameof(WriteRefused));

    /// <summary>
    /// Logged at Error, exactly once per refused <c>sprk_policyversion</c>, by
    /// <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/> — task 022, owner directive 2026-10-03/04:
    /// admins may author policy rows directly in the Spaarke Platform app (bypassing the BFF), so the
    /// evaluator (task 031) re-validates every policy version's <c>sprk_rulebody</c> at evaluation time and
    /// must fail closed and loudly, never silently producing zero Signals. ONE EventId covers every
    /// policy-validation refusal; the structured <c>reason</c> property distinguishes WHICH. It is one of the
    /// SEVEN bounded policy-validation sub-reasons on <see cref="Telemetry.OntologyWriterFailureReason"/>:
    /// <c>rule_type_unsupported</c>, <c>rule_body_too_large</c>, <c>schema_invalid</c>, <c>compile_refused</c>,
    /// <c>template_token_outside_read_set</c>, <c>template_malformed</c> and <c>internal_error</c> (task 022
    /// rework round 1 review finding #7; <c>rule_body_too_large</c> added in round 2). Structured properties: <c>policyVersionId</c>, <c>policyCode</c> and
    /// <c>reason</c> ONLY — never the rule body, the message template, or the validator's field-level error
    /// text (all of which may echo admin-authored config content).
    /// </summary>
    public static readonly EventId PolicyVersionInvalid = new(50301, nameof(PolicyVersionInvalid));

    /// <summary>
    /// Logged at Warning, exactly once per refused decision plan, by <see cref="Actions.DecisionPlanService"/> (task 036):
    /// a plan that does not resolve against the closed action catalog is refused for that plan, never skipped silently.
    /// Structured properties: <c>policyVersionId</c> and <c>reason</c>
    /// (<see cref="Telemetry.DecisionPlanRefusalReason"/>) ONLY.
    /// </summary>
    public static readonly EventId DecisionPlanRefused = new(50302, nameof(DecisionPlanRefused));

    /// <summary>
    /// Logged at Warning, exactly once per rule description that could not be produced after the body validated, by
    /// <see cref="RuleBodyDescriber"/> (task 026). Structured property: <c>reason</c>
    /// (<see cref="Telemetry.RuleDescriptionRefusalReason"/>) ONLY; never the body, a name or the exception message.
    /// </summary>
    public static readonly EventId RuleDescriptionRefused = new(50303, nameof(RuleDescriptionRefused));

    /// <summary>
    /// Logged at Warning, exactly once per skipped write, by <see cref="SignalWriter"/> when uac-r2's ownership
    /// resolver REFUSES an owner (task 039, D-33 — for example a root flagged Secure but not isolated): nothing is
    /// written and nothing is thrown, the task 146 shape for a background writer. Metered as
    /// <see cref="Telemetry.OntologyWriterFailureReason.OwnerRefused"/>. Structured properties: <c>policyCode</c>,
    /// <c>subjectEntity</c>, <c>subjectId</c>, <c>matterId</c>, <c>reason</c> and <c>refusalCode</c> (one of
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal"/>'s codes) — never fact values or the sentence.
    /// </summary>
    public static readonly EventId WriteSkippedOwnerRefused = new(50304, nameof(WriteSkippedOwnerRefused));

    /// <summary>
    /// Logged at Warning, exactly once per skipped reconcile, by <see cref="SignalWriter"/> when an existing Signal's
    /// core record or no-core owner no longer matches the one derived from its subject now (task 037: the item was filed,
    /// un-filed, re-filed or reassigned since the Signal was written). Nothing is updated, not even
    /// <c>sprk_lastevaluated</c>; re-grouping and owner drift belong to task 031. Metered as
    /// <see cref="Telemetry.OntologyWriterFailureReason.CoreRecordChanged"/>. Structured properties: <c>policyCode</c>,
    /// <c>subjectEntity</c>, <c>subjectId</c>, <c>signalId</c>, <c>reason</c> ONLY.
    /// </summary>
    public static readonly EventId WriteSkippedCoreRecordChanged = new(50305, nameof(WriteSkippedCoreRecordChanged));
}
