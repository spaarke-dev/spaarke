using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// <c>customMetrics/ontology.writer.failures</c> (task 030 rework — owner directive 2026-10-04: "the writer
/// fails closed by design; a broken credential or a refused write must not look like 'no conditions found'").
/// Dimensioned by <c>reason</c> (see <see cref="OntologyWriterFailureReason"/>) so a future alert rule (task
/// 035) can break down WHY the writer failed without a second metric.
/// </summary>
/// <remarks>
/// Follows the repo's EXISTING Meter-per-feature convention — <c>CacheMetrics</c>, <c>FinanceTelemetry</c>,
/// <c>CircuitBreakerRegistry</c> — a single static <see cref="Meter"/> owning this feature's instruments, no
/// new telemetry package. MUST be registered via <c>TelemetryModule.AddMeter(MeterName)</c> or the metric is
/// silently dropped from the App Insights export — the exact trap <c>TelemetryModule.cs</c>'s own comments
/// record for the Event Rules and Compose-save meters (both shipped once, unregistered, before being caught).
/// </remarks>
public static class OntologyWriterTelemetry
{
    /// <summary>Meter name registered in <c>TelemetryModule.AddMeter(...)</c>.</summary>
    public const string MeterName = "Sprk.Bff.Api.Ontology";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>(
        name: "ontology.writer.failures",
        unit: "{failure}",
        description: "Count of Signal-writer refusals/failures by reason. A fail-closed writer with zero " +
                     "observed failures is not evidence of health — it may mean the failures are silent.");

    /// <summary>
    /// <c>customMetrics/ontology.policy.invalid</c> (task 022, same <see cref="MeterName"/> as the writer's own
    /// failure counter — this is still the ontology domain, just the policy-validation side rather than the
    /// write side; CLAUDE.md §11 reuse-first: extending this class's existing Meter rather than standing up a
    /// second one). Dimensioned by <c>reason</c> (<see cref="OntologyWriterFailureReason"/>) for the same
    /// log/metric-vocabulary-agreement reason as <see cref="FailuresCounter"/>.
    /// </summary>
    private static readonly Counter<long> PolicyInvalidCounter = Meter.CreateCounter<long>(
        name: "ontology.policy.invalid",
        unit: "{policyversion}",
        description: "Count of sprk_policyversion rows whose sprk_rulebody/sprk_messagetemplate failed " +
                     "fail-closed validation at evaluation time, by reason. Admins can author policy rows " +
                     "directly in the Spaarke Platform app, bypassing BFF save-time validation, so this is the " +
                     "only guaranteed observation point for an invalid policy version.");

    /// <summary>
    /// <c>customMetrics/ontology.decisionplan.refused</c> (task 036): one decision plan refused at read time because it
    /// does not resolve against the closed action catalog. Same <see cref="MeterName"/> (CLAUDE.md section 11: extend,
    /// do not stand up a second Meter). Dimensioned by <c>reason</c> (<see cref="DecisionPlanRefusalReason"/>).
    /// </summary>
    private static readonly Counter<long> DecisionPlanRefusedCounter = Meter.CreateCounter<long>(
        name: "ontology.decisionplan.refused",
        unit: "{plan}",
        description: "Count of sprk_policyversion decision plans refused at read time (fail closed), by reason. " +
                     "A refused plan means the wizard cannot offer that Signal's actions.");

    private static readonly Counter<long> RuleDescriptionRefusedCounter = Meter.CreateCounter<long>(
        name: "ontology.ruledescription.refused",
        unit: "{description}",
        description: "Count of rule-body descriptions that could not be produced after the body validated, by reason " +
                     "(a reference-table read fault, or a lookup value with no name row). The decision plan is still served.");

    /// <summary>
    /// Records one Signal-writer refusal/failure. <paramref name="reason"/> MUST be one of
    /// <see cref="OntologyWriterFailureReason"/>'s bounded-cardinality constants — never a raw exception
    /// message, a policy code, or any fact/sentence content.
    /// </summary>
    public static void RecordFailure(string reason) =>
        FailuresCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Records one <c>sprk_policyversion</c> validation refusal (task 022). <paramref name="reason"/> MUST be
    /// one of <see cref="OntologyWriterFailureReason"/>'s bounded-cardinality constants — never the rule body,
    /// the message template, or a validator error string.
    /// </summary>
    public static void RecordPolicyInvalid(string reason) =>
        PolicyInvalidCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Records one refused decision plan (task 036). <paramref name="reason"/> MUST be one of
    /// <see cref="DecisionPlanRefusalReason"/>'s constants.
    /// </summary>
    public static void RecordDecisionPlanRefused(string reason) =>
        DecisionPlanRefusedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records one rule description that could not be produced (task 026). <paramref name="reason"/> MUST be one of
    /// <see cref="RuleDescriptionRefusalReason"/>'s constants.</summary>
    public static void RecordRuleDescriptionRefused(string reason) =>
        RuleDescriptionRefusedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));
}

/// <summary>Bounded-cardinality reasons a rule description could not be produced (task 026).</summary>
public static class RuleDescriptionRefusalReason
{
    /// <summary>Reading the reference table that names a lookup value failed (Dataverse fault).</summary>
    public const string LookupReadFailed = "lookup_read_failed";

    /// <summary>A lookup value has no name row, so it cannot be shown as a name.</summary>
    public const string LookupUnresolved = "lookup_unresolved";
}

/// <summary>Bounded-cardinality reasons a decision plan is refused (task 036). Shared by the metric and the log.</summary>
public static class DecisionPlanRefusalReason
{
    /// <summary>The policy version has no decision plan, or an empty one.</summary>
    public const string PlanMissing = "plan_missing";

    /// <summary>The plan is not the expected JSON shape.</summary>
    public const string PlanMalformed = "plan_malformed";

    /// <summary>A code in the plan is not in the closed action catalog.</summary>
    public const string UnknownAction = "unknown_action";

    /// <summary>A code is in the catalog but may not appear in this position (an action that is not a plan action for
    /// the Signal's lane, or a Next step that is not a Next-step creator).</summary>
    public const string ActionNotAllowed = "action_not_allowed";

    /// <summary>The same code appears twice in one list.</summary>
    public const string DuplicateAction = "duplicate_action";
}

/// <summary>
/// Bounded-cardinality reason tags shared by <see cref="OntologyWriterTelemetry.RecordFailure"/>'s metric
/// dimension and the matching Error log's structured <c>reason</c> property (task 030 rework), so a log query
/// and a metric query always agree on vocabulary. Adding a new writer failure mode means adding a constant
/// here, not inventing an ad hoc string at the call site.
/// </summary>
public static class OntologyWriterFailureReason
{
    /// <summary>The writer's dedicated managed-identity credential is unset, or the dedicated
    /// <see cref="Sprk.Bff.Api.Services.Signals.OntologyWriterDataverseClient"/> connection could not be
    /// established.</summary>
    public const string CredentialUnresolvable = "credential_unresolvable";

    /// <summary>The pinned credential resolved, but acquiring a token from it failed (e.g. a transient AAD/MI
    /// token-endpoint error).</summary>
    public const string TokenAcquisitionFailed = "token_acquisition_failed";

    /// <summary>The credential resolved and a token was acquired, but the <see cref="Microsoft.PowerPlatform.Dataverse.Client.ServiceClient"/>
    /// connect handshake itself failed (network, org lookup, etc.) — distinct from
    /// <see cref="TokenAcquisitionFailed"/> (R7, second independent review).</summary>
    public const string ConnectFailed = "connect_failed";

    /// <summary>Dataverse refused the writer's own operation (403 / <c>0x80040220</c> / <c>0x80040299</c> —
    /// a privilege the writer's role union does not grant).</summary>
    public const string DataverseAccessDenied = "dataverse_access_denied";

    /// <summary>The grouping matter's subject type has no verified matter-derivation path
    /// (<see cref="Sprk.Bff.Api.Services.Signals.SignalWriter.VerifiedSubjects"/>).</summary>
    public const string MatterDerivationUnverified = "matter_derivation_unverified";

    /// <summary>The subject's own matter-lookup field (e.g. <c>sprk_communication.sprk_regardingmatter</c>)
    /// is empty.</summary>
    public const string MatterLookupEmpty = "matter_lookup_empty";

    /// <summary>The grouping matter has no <c>owningbusinessunit</c>.</summary>
    public const string OwningBusinessUnitNotFound = "owning_business_unit_not_found";

    /// <summary>After create, the row's <c>owningbusinessunit</c> did not match the intended business unit —
    /// most likely <c>EnableOwnershipAcrossBusinessUnits</c> is disabled in this environment, OR (R1, second
    /// independent review) the row was found to have drifted on a RECONCILE (re-evaluation) read, not only on
    /// the post-create read-back.</summary>
    public const string OwningBusinessUnitMismatch = "owning_business_unit_mismatch";

    /// <summary>The Signal was created, but reading its <c>owningbusinessunit</c> back to verify it (or, on
    /// reconcile, re-reading it) itself failed (R1, second independent review) — distinct from
    /// <see cref="OwningBusinessUnitMismatch"/>, which means the read SUCCEEDED and disagreed.</summary>
    public const string OwningBusinessUnitReadBackFailed = "owning_business_unit_read_back_failed";

    /// <summary>The message template could not be rendered strictly from the detection-time fact snapshot — a
    /// referenced token is absent, null, or a leftover placeholder survived rendering (§0.3; R2, second
    /// independent review). This is a refused write under the owner's no-silent-failure rule, logged and
    /// metered WITHOUT the rendered content (R2 removed the rendered string from the exception message).</summary>
    public const string SentenceTemplateInvalid = "sentence_template_invalid";

    /// <summary>Task 039 (D-33): uac-r2's <c>RecordOwnershipResolver</c> refused an owner for the Signal (for
    /// example a root flagged Secure but not isolated, or the Secure Record Owners team unresolved). The write is
    /// SKIPPED — nothing is written — and logged at Warning on
    /// <see cref="Sprk.Bff.Api.Services.Signals.OntologyWriterEvents.WriteSkippedOwnerRefused"/>.</summary>
    public const string OwnerRefused = "owner_refused";

    /// <summary>Task 039 (D-33): a Signal the resolver gave to the Secure Record Owners team does not read back
    /// with that team as its <c>owningteam</c> — after the create, or on a re-evaluation (reconcile) read.</summary>
    public const string SecureOwnerMismatch = "secure_owner_mismatch";

    /// <summary>Task 037 (D-34): <c>CoreAncestorResolver</c> could not derive the subject's core record (a read or
    /// metadata fault, or a subject naming two different records of one core type). The write is refused.</summary>
    public const string CoreRecordUnresolved = "core_record_unresolved";

    /// <summary>Task 037: on a re-evaluation the existing Signal's core record (or, for a no-core Signal, its owner) differs
    /// from the one derived from the subject now. The reconcile is skipped, loudly (Warning
    /// <see cref="Sprk.Bff.Api.Services.Signals.OntologyWriterEvents.WriteSkippedCoreRecordChanged"/> + this metric);
    /// re-grouping is task 031's.</summary>
    public const string CoreRecordChanged = "core_record_changed";

    /// <summary>Task 037 (D-37): the subject names more than one core record and neither its direct filed-under record
    /// nor matter-over-project decides between them. The write is refused.</summary>
    public const string CoreRecordAmbiguous = "core_record_ambiguous";

    /// <summary>Task 037 (D-36): the core record's table has no active <c>sprk_recordtype_ref</c> catalog row, so the
    /// Signal's <c>sprk_corerecordtype</c> cannot be written.</summary>
    public const string CoreRecordTypeNotCataloged = "core_record_type_not_cataloged";

    /// <summary>Task 037 (D-35): a subject with no core record is owned by the subject's owner, and that owner is neither a
    /// system user nor a team (or is empty). The write is refused rather than owned by the writer.</summary>
    public const string NoCoreOwnerUnresolved = "no_core_owner_unresolved";

    /// <summary>Task 037 (D-35): a no-core Signal does not read back with the subject's owner as its <c>ownerid</c>.</summary>
    public const string NoCoreOwnerMismatch = "no_core_owner_mismatch";

    // ── sprk_policyversion validation sub-reasons (task 022 rework, review finding #7) ─────────────────────
    // Replaces the single bucket "policy_version_rule_body_invalid" with bounded, mutually-exclusive
    // sub-reasons so an alert/dashboard can distinguish "nobody has authored a Threshold policy's schema yet"
    // from "an admin typo'd a field name in the message template" without parsing free-text error strings.

    /// <summary>The policy version's <c>sprk_ruletype</c> is unrecognized, OR is a closed-set member with no
    /// authored schema yet (<see cref="Sprk.Bff.Api.Services.Signals.RuleType.Threshold"/> /
    /// <see cref="Sprk.Bff.Api.Services.Signals.RuleType.Switch"/>, task 020 scope).</summary>
    public const string RuleTypeUnsupported = "rule_type_unsupported";

    /// <summary>The <c>sprk_rulebody</c> exceeds
    /// <see cref="Sprk.Bff.Api.Services.Signals.RuleBodySchemaValidator.MaxRuleBodyLength"/> characters. Refused
    /// by length alone, BEFORE any parse or schema evaluation (task 022 rework round 2, finding F2), so it is its
    /// own reason rather than being reported as whatever the parser would have said about the content.</summary>
    public const string RuleBodyTooLarge = "rule_body_too_large";

    /// <summary>The <c>sprk_rulebody</c> failed <see cref="Sprk.Bff.Api.Services.Signals.RuleBodySchemaValidator"/>
    /// — malformed JSON (incl. duplicate keys, an out-of-range number), a forbidden body-embedded
    /// <c>messageTemplate</c>, or a JSON Schema violation.</summary>
    public const string SchemaInvalid = "schema_invalid";

    /// <summary>The <c>sprk_rulebody</c> passed schema validation but
    /// <see cref="Sprk.Bff.Api.Services.Signals.PredicateCompiler"/> refused it — an unverified join, a
    /// non-global-readable entity, a cross-clause <c>$</c>-reference, or a bounded-refusal limit (clause count,
    /// <c>in</c>-list length).</summary>
    public const string CompileRefused = "compile_refused";

    /// <summary>The <c>sprk_messagetemplate</c> references a <c>{{field}}</c> token outside
    /// <see cref="Sprk.Bff.Api.Services.Signals.CompiledPredicate.TemplateEligibleFields"/> — absent entirely,
    /// present only on a <c>notExists</c> clause (whose value can never be read for a firing subject), read by an
    /// <c>exists</c> clause that does not pin it to exactly one value, or ambiguous.</summary>
    public const string TemplateTokenOutsideReadSet = "template_token_outside_read_set";

    /// <summary>The <c>sprk_messagetemplate</c> contains a malformed <c>{{...}}</c> placeholder attempt
    /// (unbalanced braces, an empty token, a disallowed character, a lone literal brace in prose, or a full-width
    /// brace) that would otherwise render as literal text — see
    /// <see cref="Sprk.Bff.Api.Services.Signals.SignalWriter.HasMalformedPlaceholder"/>.</summary>
    public const string TemplateMalformed = "template_malformed";

    /// <summary>An exception not covered by any of the above escaped the validator's own checks (task 022
    /// review finding #1 — e.g. a library throwing on pathological-but-syntactically-valid input not yet
    /// discovered). The validator still fails closed rather than propagate it.</summary>
    public const string InternalError = "internal_error";
}
