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
    /// <c>customMetrics/ontology.writer.skips</c> (task 042, K1): a write that was deliberately NOT made, by design (today only
    /// <c>gate_dr_skipped_no_unit_team</c>, which task 149 / D-144 put in place of task 042's <c>gate_dr_skipped_unfiled</c>, and task 153's
    /// <c>cleared_dr_skipped_*</c>), kept off
    /// <see cref="FailuresCounter"/> so an alert on <c>ontology.writer.failures</c> counts real
    /// failures only and stays unfiltered. Same <see cref="MeterName"/>.
    /// </summary>
    private static readonly Counter<long> SkipsCounter = Meter.CreateCounter<long>(
        name: "ontology.writer.skips",
        unit: "{skip}",
        description: "Count of writes skipped by design (not failures), by reason.");

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
    /// <c>customMetrics/billing.rule.invalid</c> (ebilling-r1 task 070 fix-round-1, K3): one billing policy version refused by
    /// <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.TryPrepareBillingRule"/>. A separate instrument so
    /// <c>ontology.policy.invalid</c> (and any Ontology dashboard or alert built on it) counts Ontology policies only; it rides
    /// the existing <see cref="MeterName"/> because that Meter is already registered with the exporter. Dimensioned by
    /// <c>reason</c> (<see cref="OntologyWriterFailureReason"/>, the same vocabulary).
    /// </summary>
    private static readonly Counter<long> BillingRuleInvalidCounter = Meter.CreateCounter<long>(
        name: "billing.rule.invalid",
        unit: "{policyversion}",
        description: "Count of billing sprk_policyversion rows refused by the fail-closed billing gate, by reason. " +
                     "Kept off ontology.policy.invalid so Ontology dashboards do not count billing rows.");

    /// <summary>
    /// <c>customMetrics/billing.rule.consequence_downgraded</c> (ebilling-r1 072 part W, the 073 review's K2): one stored billing
    /// version whose consequence its rule type may not carry, evaluated with Flag instead of being dropped, so the check survives.
    /// Beside <see cref="BillingRuleInvalidCounter"/> on the same Meter. Dimensioned by <c>rule_type</c> (the closed
    /// <see cref="Sprk.Bff.Api.Services.Signals.RuleType"/> name set).
    /// </summary>
    private static readonly Counter<long> BillingRuleConsequenceDowngradedCounter = Meter.CreateCounter<long>(
        name: "billing.rule.consequence_downgraded",
        unit: "{policyversion}",
        description: "Count of stored billing sprk_policyversion rows evaluated with Flag because their sprk_consequence is one " +
                     "their rule type may not carry (publish, accept and edit refuse such a row; one written around them still runs).");

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

    /// <summary>Records one write skipped BY DESIGN (task 042, K1): not a failure, never on <c>ontology.writer.failures</c>.
    /// <paramref name="reason"/> is one of the bounded constants of the writer that skipped.</summary>
    public static void RecordSkip(string reason) =>
        SkipsCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Records one <c>sprk_policyversion</c> validation refusal (task 022). <paramref name="reason"/> MUST be
    /// one of <see cref="OntologyWriterFailureReason"/>'s bounded-cardinality constants — never the rule body,
    /// the message template, or a validator error string.
    /// </summary>
    public static void RecordPolicyInvalid(string reason) =>
        PolicyInvalidCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records one billing policy version refused by the billing gate (ebilling-r1 task 070, K3).
    /// <paramref name="reason"/> MUST be one of <see cref="OntologyWriterFailureReason"/>'s bounded constants; never on
    /// <c>ontology.policy.invalid</c>.</summary>
    public static void RecordBillingRuleInvalid(string reason) =>
        BillingRuleInvalidCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records one stored billing version evaluated with Flag in place of a consequence its type may not carry (072 part W).
    /// <paramref name="ruleType"/> is a <see cref="Sprk.Bff.Api.Services.Signals.RuleType"/> name.</summary>
    public static void RecordBillingRuleConsequenceDowngraded(string ruleType) =>
        BillingRuleConsequenceDowngradedCounter.Add(1, new KeyValuePair<string, object?>("rule_type", ruleType));

    /// <summary>
    /// Records one refused decision plan (task 036). <paramref name="reason"/> MUST be one of
    /// <see cref="DecisionPlanRefusalReason"/>'s constants.
    /// </summary>
    public static void RecordDecisionPlanRefused(string reason) =>
        DecisionPlanRefusedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// <c>customMetrics/ontology.task.create_faults</c> (task 141 fix round 2, K1): a <c>TaskActionCore</c> create that
    /// returned <see cref="Guid.Empty"/> because of a fault that is NOT a refusal worth a Warning: Dataverse access denied
    /// or a programming fault (<see cref="TaskCreateFaultReason"/>). Same <see cref="MeterName"/>, already registered.
    /// </summary>
    private static readonly Counter<long> TaskCreateFaultsCounter = Meter.CreateCounter<long>(
        name: "ontology.task.create_faults",
        unit: "{fault}",
        description: "Count of task creates swallowed as Guid.Empty because Dataverse denied access or the call was malformed, by reason. " +
                     "The task core swallows these by contract; this makes them visible.");

    /// <summary>Records one swallowed task-create fault (task 141). <paramref name="reason"/> MUST be one of
    /// <see cref="TaskCreateFaultReason"/>'s constants.</summary>
    public static void RecordTaskCreateFault(string reason) =>
        TaskCreateFaultsCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records one rule description that could not be produced (task 026). <paramref name="reason"/> MUST be one of
    /// <see cref="RuleDescriptionRefusalReason"/>'s constants.</summary>
    public static void RecordRuleDescriptionRefused(string reason) =>
        RuleDescriptionRefusedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// <c>customMetrics/ontology.evaluator.runs</c> (task 031, owner directive 2026-10-04): one per attempt of the
    /// re-evaluating job, dimension <c>outcome</c> = <c>succeeded</c> | <c>failed</c>. A failed run is never recorded as a
    /// successful run with zero Signals.
    /// </summary>
    private static readonly Counter<long> EvaluatorRunsCounter = Meter.CreateCounter<long>(
        name: "ontology.evaluator.runs",
        unit: "{run}",
        description: "Re-evaluating job attempts by outcome (succeeded | failed).");

    /// <summary>
    /// <c>customMetrics/ontology.evaluator.signals</c> (task 031): what each run did, dimension <c>action</c> (see
    /// <see cref="EvaluatorSignalAction"/>): written, updated, closed, reraised, refused, reassigned.
    /// </summary>
    private static readonly Counter<long> EvaluatorSignalsCounter = Meter.CreateCounter<long>(
        name: "ontology.evaluator.signals",
        unit: "{signal}",
        description: "Signals the re-evaluating job wrote, updated, closed, re-raised, reassigned or was refused, by action.");

    private static long _lastEvaluatedUnixSeconds;

    /// <summary>
    /// <c>customMetrics/ontology.evaluator.last_evaluated</c> (task 031, D-61): the run-level last-evaluated time (Unix
    /// seconds) of the last COMPLETED run in this process. Staleness is told from this, never from a per-row stamp.
    /// </summary>
    private static readonly ObservableGauge<long> LastEvaluatedGauge = Meter.CreateObservableGauge(
        name: "ontology.evaluator.last_evaluated",
        observeValue: () => Interlocked.Read(ref _lastEvaluatedUnixSeconds),
        unit: "s",
        description: "Unix time of the re-evaluating job's last completed run in this instance (D-61: run-level, not per Signal).");

    /// <summary>Records one run of the re-evaluating job (task 031).</summary>
    public static void RecordEvaluatorRun(bool succeeded, DateTimeOffset evaluatedAt, IReadOnlyDictionary<string, long> signalsByAction)
    {
        ArgumentNullException.ThrowIfNull(signalsByAction);
        EvaluatorRunsCounter.Add(1, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
        foreach (var (action, count) in signalsByAction)
        {
            if (count > 0)
            {
                EvaluatorSignalsCounter.Add(count, new KeyValuePair<string, object?>("action", action));
            }
        }

        Interlocked.Exchange(ref _lastEvaluatedUnixSeconds, evaluatedAt.ToUnixTimeSeconds());
    }
}

/// <summary>The bounded <c>action</c> values of <c>ontology.evaluator.signals</c> (task 031).</summary>
public static class EvaluatorSignalAction
{
    /// <summary>A new Signal (episode 1) was created.</summary>
    public const string Written = "written";

    /// <summary>An open Signal's result changed (its date) and it was reconciled.</summary>
    public const string Updated = "updated";

    /// <summary>An open Signal whose predicate no longer holds was closed (ConditionCleared).</summary>
    public const string Closed = "closed";

    /// <summary>A new episode (n+1) was raised after the previous one was resolved.</summary>
    public const string Reraised = "reraised";

    /// <summary>A hit the writer or the ownership resolver refused; nothing was written for it.</summary>
    public const string Refused = "refused";

    /// <summary>A no-core Signal was reassigned to its item's current owner.</summary>
    public const string Reassigned = "reassigned";
}

/// <summary>The bounded <c>reason</c> values of <c>ontology.task.create_faults</c> (task 141 fix round 2).</summary>
public static class TaskCreateFaultReason
{
    /// <summary>Dataverse refused the BFF identity (403 / <c>0x80040220</c> / <c>0x80040299</c>).</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>An <see cref="ArgumentException"/>: the call itself was malformed.</summary>
    public const string ArgumentFault = "argument_fault";
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

    /// <summary>The same code appears twice in one list, or in both <c>actions</c> and <c>nextSteps</c>.</summary>
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

    /// <summary>Task 143 (D-130): a Signal with a core record does not read back with the ordinary team the ownership resolver
    /// chose as its <c>ownerid</c> — after the create, or on a re-evaluation (reconcile) read (its core record moved business
    /// unit, or the row was written before task 143). (Was <c>owning_business_unit_mismatch</c> while the writer set the business
    /// unit directly.)</summary>
    public const string OwnerMismatch = "owner_mismatch";

    /// <summary>The Signal was created, but reading its owner back to verify it (or, on reconcile, re-reading the existing row)
    /// itself failed (R1, second independent review) — distinct from a mismatch, which means the read SUCCEEDED and disagreed.
    /// (Was <c>owning_business_unit_read_back_failed</c> before task 143.)</summary>
    public const string OwnerReadBackFailed = "owner_read_back_failed";

    /// <summary>The message template could not be rendered strictly from the detection-time fact snapshot — a
    /// referenced token is absent, null, or a leftover placeholder survived rendering (§0.3; R2, second
    /// independent review). This is a refused write under the owner's no-silent-failure rule, logged and
    /// metered WITHOUT the rendered content (R2 removed the rendered string from the exception message).</summary>
    public const string SentenceTemplateInvalid = "sentence_template_invalid";

    /// <summary>Task 039 (D-33), task 143 (D-130): uac-r2's <c>RecordOwnershipResolver</c> refused an owner for the Signal, or
    /// named no team (for example a root flagged Secure but not isolated, the Secure Record Owners team unresolved, or a parent
    /// with no business-unit default team). The write is SKIPPED — nothing is written — and logged at Warning on
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

    /// <summary>Task 141 (review F1): the subject lookup and ADR-024 pair could not be built, for example because the subject
    /// table's <c>sprk_recordtype_ref</c> read faulted. A fault is not "no catalog row", so the Signal is not written untyped.</summary>
    public const string SubjectPairUnresolved = "subject_pair_unresolved";

    /// <summary>Task 037 (D-35), task 143 (D-130, D-134): a subject with no core record, whose owner cannot be resolved — the
    /// ownership resolver faulted, or a personal To Do's Assigned To could not be read while its own answer is ordinary. The write
    /// is refused (fail closed) rather than owned by the writer or by a fallback.</summary>
    public const string NoCoreOwnerUnresolved = "no_core_owner_unresolved";

    /// <summary>Task 037 (D-35), task 143: a no-core Signal does not read back with the resolver's team as its <c>ownerid</c>, or
    /// the job's re-own of it to that team was refused (a row or team in the Secure Record business unit, or a user-owned row).</summary>
    public const string NoCoreOwnerMismatch = "no_core_owner_mismatch";

    /// <summary>Task 031: a system closure (ConditionCleared) of a Signal failed in Dataverse.</summary>
    public const string SignalCloseFailed = "signal_close_failed";

    /// <summary>Ontology task 145 (D-123, review F1): clearing stored evidence detail that is no longer within the Signal's secure
    /// family failed in Dataverse.</summary>
    public const string SignalEvidenceRedactFailed = "signal_evidence_redact_failed";

    /// <summary>Task 031 (D-35), task 143: re-owning a no-core Signal to its item's current owner team failed (uac-r2's
    /// <c>ReownOrdinaryRowAsync</c> answered Failed, or faulted).</summary>
    public const string SignalReassignFailed = "signal_reassign_failed";

    /// <summary>Ontology task 153 (D-145b): linking a Do-lane Signal closed as ConditionCleared to its Routine Decision Record
    /// (<c>sprk_decisionrecord</c>, <c>sprk_resolvedby</c>) failed in Dataverse. The record exists; the next pass finds it by its key and links it.</summary>
    public const string SignalRecordLinkFailed = "signal_record_link_failed";

    /// <summary>Ontology task 153 (adversarial pass 1 F1/F2): the <c>ontology.writer.skips</c> tags of a Do-lane condition-cleared closure
    /// whose Decision Record can never be written or linked. Each is permanent, so it is a counted skip, never a failure, and it is not
    /// retried: the subject no longer exists; it cannot be filed (its lineage conflicts); it is not a table a record can be filed under; its
    /// core record type names no core table; or the Signal names a Decision Record other than the one it named when it was closed.</summary>
    public const string ClosureRecordSkippedSubjectMissing = "cleared_dr_skipped_subject_missing";

    /// <inheritdoc cref="ClosureRecordSkippedSubjectMissing"/>
    public const string ClosureRecordSkippedSubjectUnfileable = "cleared_dr_skipped_subject_unfileable";

    /// <inheritdoc cref="ClosureRecordSkippedSubjectMissing"/>
    public const string ClosureRecordSkippedSubjectUnsupported = "cleared_dr_skipped_subject_unsupported";

    /// <inheritdoc cref="ClosureRecordSkippedSubjectMissing"/>
    public const string ClosureRecordSkippedCoreTypeNotCataloged = "cleared_dr_skipped_core_type_not_cataloged";

    /// <inheritdoc cref="ClosureRecordSkippedSubjectMissing"/>
    public const string ClosureRecordSkippedForeignLink = "cleared_dr_skipped_foreign_link";

    /// <summary>Ontology task 153 (uac-r2 re-check K2): a closed Signal links a Decision Record the retry does not recognise as a D-128 link
    /// it may replace (not found among the writer's own records, created after the close, or with a code the catalog does not know), so
    /// its closure's record is not retried. A counted skip, logged at Warning, never silent.</summary>
    public const string ClosureRecordSkippedUnrecognisedPriorLink = "cleared_dr_skipped_unrecognised_prior_link";

    /// <summary>Ontology task 153 (adversarial pass 2 K1): a Do-lane condition-cleared closure's Decision Record was written, but before the
    /// job could link it a person's decision re-closed the Signal (with its own record). The human decision wins and the closure's record is
    /// left unlinked; a counted skip, logged at Warning, never retried.</summary>
    public const string ClosureRecordSkippedSignalReclosed = "cleared_dr_skipped_signal_reclosed";

    /// <summary>Task 031 (D-13): a <c>sprk_signal.sprk_dedupekey</c> under a policy is not
    /// <c>{policycode}|{type}|{id}|{episode}</c>, so its episode cannot be read and the subject is not re-raised.</summary>
    public const string DedupeKeyUnparseable = "dedupe_key_unparseable";

    /// <summary>Task 031: a write raised by the re-evaluating job failed with an exception the writer did not classify
    /// (for example a Dataverse timeout). Logged by the job and counted here once.</summary>
    public const string WriteFailed = "write_failed";

    // ── sprk_policyversion validation sub-reasons (task 022 rework, review finding #7) ─────────────────────
    // Replaces the single bucket "policy_version_rule_body_invalid" with bounded, mutually-exclusive
    // sub-reasons so an alert/dashboard can distinguish "nobody has authored a Threshold policy's schema yet"
    // from "an admin typo'd a field name in the message template" without parsing free-text error strings.

    /// <summary>The policy version's <c>sprk_ruletype</c> is unrecognized, OR is a closed-set member with no
    /// authored schema yet (<see cref="Sprk.Bff.Api.Services.Signals.RuleType.Threshold"/> /
    /// <see cref="Sprk.Bff.Api.Services.Signals.RuleType.Switch"/>, task 020 scope).</summary>
    public const string RuleTypeUnsupported = "rule_type_unsupported";

    /// <summary>ebilling-r1 task 070 fix-round-1: a save-time caller named an expected rule domain
    /// (<see cref="Sprk.Bff.Api.Services.Signals.RuleDomain"/>) and the version's <c>sprk_ruletype</c> is a closed-set member of
    /// ANOTHER domain (an Ontology policy carrying a billing type, or the reverse). Distinct from
    /// <see cref="RuleTypeUnsupported"/>, which means the type is unknown or the consumer cannot evaluate it at all.</summary>
    public const string RuleTypeWrongDomain = "rule_type_wrong_domain";

    /// <summary>ebilling-r1 task 081 (procedure model 7.2, O-24): a policy version names a Procedure Version
    /// (<c>sprk_policyversion.sprk_procedureversion</c>) but its policy is not Billing-domain. Ontology-area Procedures are document-only
    /// in R1, so no Ontology (or domain-less) policy version is ever keyed to a Procedure Version. Returned by
    /// <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.ValidateProcedureVersionLink"/>.</summary>
    public const string ProcedureVersionWrongDomain = "procedure_version_wrong_domain";

    /// <summary>ebilling-r1 task 073 (FR-26, I-5, D-89): a billing version's <c>sprk_consequence</c> is one its rule type may not carry
    /// (Reject on a type that is not reject-eligible or holds only, or a value outside the defined three). Returned by
    /// <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.ValidateForSave"/> when it is given the consequence (the
    /// publish path), and by <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.TryPrepareBillingRule"/> for a stored row
    /// (logged and metered on <c>billing.rule.invalid</c>).</summary>
    public const string ConsequenceNotAllowed = "consequence_not_allowed";

    /// <summary>ebilling-r1 task 115: a billing body is schema-valid but states a form its type cannot evaluate yet, because the facts it
    /// needs are not extracted: an AddedFee allowance of <c>notAllowed</c> (no per-line fee kind) or <c>afterDays</c> (no per-line fee
    /// kind and no due date). Returned at save and by the billing gate (<see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.TryPrepareBillingRule"/>,
    /// logged and metered on <c>billing.rule.invalid</c>), so such a rule is never stored as publishable nor evaluated as "holds".</summary>
    public const string RuleFormNotSupported = "rule_form_not_supported";

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

    /// <summary>Task 102a (owner option 101(b)): the body compiled, but it names a column outside
    /// <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.AuthorableColumns"/>. Returned only by
    /// <see cref="Sprk.Bff.Api.Services.Signals.PolicyVersionValidator.ValidateForAuthoring"/> (the BFF authoring routes); never
    /// logged or metered by the evaluator.</summary>
    public const string ColumnNotAuthorable = "column_not_authorable";

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
