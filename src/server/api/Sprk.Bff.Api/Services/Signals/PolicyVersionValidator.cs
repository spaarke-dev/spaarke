using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The outcome of validating one <c>sprk_policyversion</c>'s <c>sprk_rulebody</c> (+ <c>sprk_messagetemplate</c>)
/// via <see cref="PolicyVersionValidator.ValidateForSave"/>. Carries field-level error strings — never a bare
/// boolean — so a save-time caller can name the offending clause (task 022 step 3), plus a bounded-cardinality
/// <see cref="Reason"/> (task 022 rework, review finding #7) so a caller can meter/log WHY without parsing
/// free-text <see cref="Errors"/>.
/// </summary>
/// <param name="IsValid">True only if every check passed.</param>
/// <param name="Errors">Field-level error strings. Empty when <see cref="IsValid"/> is true.</param>
/// <param name="Reason">One of <see cref="OntologyWriterFailureReason"/>'s policy-validation sub-reasons
/// (<c>RuleTypeUnsupported</c> / <c>RuleBodyTooLarge</c> / <c>SchemaInvalid</c> / <c>CompileRefused</c> /
/// <c>TemplateTokenOutsideReadSet</c> / <c>TemplateMalformed</c> / <c>InternalError</c>). <c>null</c> when
/// <see cref="IsValid"/> is true.</param>
public sealed record PolicyVersionValidationResult(bool IsValid, IReadOnlyList<string> Errors, string? Reason)
{
    public static PolicyVersionValidationResult Success() => new(true, Array.Empty<string>(), null);

    public static PolicyVersionValidationResult Failure(string reason, IReadOnlyList<string> errors) =>
        new(false, errors, reason);

    public static PolicyVersionValidationResult Failure(string reason, string error) =>
        new(false, new[] { error }, reason);
}

/// <summary>
/// Everything <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/> needs from one
/// <c>sprk_policyversion</c> row — the four column values plus the two identifiers the no-silent-failure log
/// and metric are allowed to carry. The evaluator (task 031) builds one of these per row it reads; nothing
/// else about the row (e.g. <c>sprk_inforcefrom</c>, policy scope) belongs here — that is
/// <see cref="PolicyScopeResolver"/>'s job, not this validator's.
/// </summary>
/// <param name="Consequence">Billing only (ebilling-r1 task 073): the version's <c>sprk_consequence</c> option value, so the billing gate
/// (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>) can refuse a stored consequence its type does not allow. <c>null</c> =
/// not stated: nothing is checked (every Ontology caller, which has no consequence), and the submission decider treats a billing
/// version without one as a Flag. The Ontology gate ignores it.</param>
public sealed record PolicyVersionSnapshot(
    Guid PolicyVersionId, string PolicyCode, string? RuleType, string? RuleBody, string? MessageTemplate, int? Consequence = null);

/// <summary>
/// What <see cref="PolicyVersionValidator.ValidateForAuthoring"/> decided (task 102a): the validation result, plus the fields a
/// <c>sprk_messagetemplate</c> may name, which is the compiled predicate's <see cref="CompiledPredicate.TemplateEligibleFields"/> that are
/// on <see cref="PolicyVersionValidator.AuthorableColumns"/>. The list is filled whenever the body compiled, even when the template or the
/// allow-list refused it, so the editor can still offer the eligible fields. It is empty when the body did not compile.
/// </summary>
public sealed record PolicyVersionAuthoringResult(PolicyVersionValidationResult Validation, IReadOnlyList<string> TemplateEligibleFields);

/// <summary>
/// The single, reusable, fail-closed validation seam for a <c>sprk_policyversion</c>'s <c>sprk_rulebody</c> +
/// <c>sprk_messagetemplate</c> — built on top of <see cref="RuleBodySchemaValidator"/> (task 020, shape) and
/// <see cref="PredicateCompiler"/> (task 021, compiles AND defines the template-eligible field set), per the
/// owner decision recorded in task 022's POML (2026-10-03/04), reworked 2026-10-04 after an independent review
/// (findings #1-#10) and the coordinator's two settled design decisions (F5, F6 below).
/// </summary>
/// <remarks>
/// <para><b>Why this class exists — the owner decision.</b> A policy row (<c>sprk_policy</c> +
/// <c>sprk_policyversion</c>) is immutable after create and CAN be authored directly in the Spaarke Platform
/// app, bypassing the BFF entirely. Save-time validation alone is therefore bypassable, so the owner directed:
/// validate again at EVALUATION time, and fail closed.</para>
/// <para><b>F6 — the safe path is the ONLY path (coordinator decision, 2026-10-04).</b>
/// <see cref="TryPrepareForEvaluation"/> is the ONE method that returns a <see cref="CompiledPredicate"/> to a
/// caller: it validates, and on refusal logs + meters + returns <c>false</c> with <c>compiled</c> set to
/// <c>null</c> — there is no way to obtain a compiled predicate from this class without going through the
/// gate. The read-only half of the same check, with no logging/metering side effect, is
/// <see cref="ValidateForSave"/> (renamed from the task's first-pass "Validate" — review finding #6) for any
/// BFF write path that comes to exist for <c>sprk_policyversion</c> (none does today — verified by grep across
/// <c>Sprk.Bff.Api/Api/**</c> and <c>Sprk.Bff.Api/Services/**</c>, 2026-10-04).</para>
/// <para><b>Why <see cref="PredicateCompiler.Compile"/> is not non-public — and what enforces the single path
/// instead.</b> <see cref="PredicateCompiler"/>, this class and the future evaluator (task 031) all live in the
/// SAME <c>Sprk.Bff.Api</c> assembly, so <c>internal</c> would not stop the evaluator calling it; nesting the
/// compiler privately here would break its standalone tested contract (task 021's
/// <c>PredicateCompilerTests</c>). The single path is enforced by an architecture test instead,
/// <c>tests/Spaarke.ArchTests/PredicateCompilerCallerGuardTests.cs</c> (task 022 rework round 2 finding F11,
/// extended round 3 finding L1), which scans <c>Sprk.Bff.Api</c>'s IL. <b>Exactly what it enforces</b>: outside
/// this class and <see cref="PredicateCompiler"/>, no method may call, take a delegate to (<c>ldftn</c>), or
/// build an expression tree over (<c>ldtoken</c>) any <c>PredicateCompiler.Compile*</c> method; and outside
/// <see cref="PredicateCompiler"/> (and the record itself), no method may construct a
/// <see cref="CompiledPredicate"/> (<c>newobj</c>) or copy one with <c>with</c> (its compiler-generated
/// <c>&lt;Clone&gt;$</c>). <b>What it does NOT enforce</b>: reflection (<c>MethodInfo.Invoke</c>,
/// <c>Activator.CreateInstance</c>), <c>dynamic</c> dispatch, and code in other assemblies — those remain a
/// code-review concern.</para>
/// <para><b>The §0.3 template vocabulary</b> is <see cref="CompiledPredicate.TemplateEligibleFields"/>, defined
/// once, in the compiler (coordinator decisions F5, 2026-10-04, and F3, rework round 2): a subject <c>when</c>
/// field (any operator — the subject is one row), or an <c>exists</c>-clause field the clause PINS to exactly
/// one value (<c>eq</c>, or a one-element <c>in</c>); never a <c>notExists</c> field, never an ambiguous one.
/// <b>What task 031 must put in <c>SignalWriteRequest.FactValues</c></b> is documented on that parameter: the
/// subject's own value for a <c>when</c> token, and the clause's pinned literal for an <c>exists</c> token.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> <b>Existing</b> — <see cref="RuleBodySchemaValidator"/>
/// checks shape only (ADR-013: no logging, no Dataverse, no policy-version identity); <see cref="PredicateCompiler"/>
/// compiles a FetchXML query and defines the template-eligible field sets. Neither owns "is this whole
/// policy version usable, and if not, say so loudly with a stable identity attached" — that is a distinct
/// responsibility (orchestration + telemetry + logging) that would otherwise be hand-rolled independently by
/// a future save path and the task 031 evaluator, risking the exact EventId/reason-vocabulary drift CLAUDE.md's
/// telemetry conventions exist to prevent. <b>Extension</b> — no: adding logging/Dataverse-identity concerns to
/// either existing class would break its documented, already-tested, dependency-free scope. <b>Cost of doing
/// nothing</b> — the evaluator would either skip this check (an admin-authored invalid body silently never
/// produces a Signal, with NOTHING surfacing why) or reimplement it inline, duplicating this logic with no
/// shared reason vocabulary.</para>
/// <para><b>ADR-013 compliance.</b> No AI-internal types; no dependency outside this validator's three
/// collaborators (<see cref="RuleBodySchemaValidator"/>, <see cref="PredicateCompiler"/>, <see cref="ILogger{T}"/>).
/// <see cref="TryPrepareForEvaluation"/> never logs or meters the rule body, the message template, or this
/// validator's own error strings — only the policy version id, the policy code, and the bounded-cardinality
/// reason tag (owner's no-silent-failure directive, 2026-10-04; mirrors <see cref="SignalWriter"/>'s own
/// refusal-logging discipline).</para>
/// <para><b>Contract violations throw; authored content never does.</b> Exactly two caller bugs throw from
/// <see cref="TryPrepareForEvaluation"/>, both BEFORE any validation so they are never logged or metered as an
/// invalid policy: a null snapshot (<see cref="ArgumentNullException"/>) and a <c>subjectId</c> of
/// <see cref="Guid.Empty"/> (<see cref="ArgumentException"/>, task 022 rework round 2, finding F1 — previously it
/// reached the compiler, was refused as <c>compile_refused</c>, and logged EventId 50301 blaming a VALID policy
/// for the caller's bug). Everything about the policy version's own content is a <c>false</c> return.</para>
/// <para><b>"Never throws" for content is enforced at TWO layers (review finding #1).</b> <see cref="ValidateInternal"/>
/// wraps its own body in a catch-all that converts ANY exception — including ones not yet discovered, the way
/// duplicate-JSON-keys and <c>1e999999</c> were discovered by the reviewer's probe — into a bounded
/// <see cref="OntologyWriterFailureReason.InternalError"/> failure, logging/metering only the exception's
/// TYPE name, never its message (which can echo rule-body/template content). <see cref="TryPrepareForEvaluation"/>
/// wraps ITS OWN call into <see cref="ValidateInternal"/> in a second catch-all, so a bug inside the first
/// catch-all (or in this method's own tuple handling) still cannot escape as an unhandled exception from the
/// evaluator's one call-per-policy-version seam.</para>
/// </remarks>
public sealed class PolicyVersionValidator
{
    /// <summary>
    /// The closed set of <c>(entity, column)</c> pairs a rule authored through the BFF may name (task 102a, owner option 101(b)). The
    /// set covers every <c>when</c> field, the Threshold field, every <c>exists</c> and <c>notExists</c> filter field, and every join
    /// <c>path</c> (<see cref="CompiledPredicate.ReadColumns"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> The dry run evaluates a draft AS THE EVALUATOR, which reads these tables org-wide (#39). A clause on any column
    /// would make each dry run a one-bit-per-shown-row question about values the author may not be able to read, including a field-secured column.
    /// Only the shown rows are filtered by the caller. A closed list of plain, non-secured columns NARROWS that oracle to the listed
    /// columns; it does not close it.
    /// <c>SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema</c> checks each entry against live metadata
    /// (one run, one environment: field security enabled on a listed column later reopens the oracle for that column).</para>
    /// <para><b>Residual (accepted: the owner re-signed the ADR-028 tension D-151(c) at this scope, option A, D-161).</b> An <c>exists</c> or
    /// <c>notExists</c> filter on an allow-listed column of a joinable related table still lets a policy-version author learn that
    /// column's value on a related row they cannot read, one bit per readable subject row per dry run. Today those columns are <c>sprk_communication</c>:
    /// <c>sprk_triagecategory</c>, <c>sprk_reviewoutcome</c>, <c>sprk_receiveddate</c>, <c>statecode</c>;
    /// <c>sprk_budgetrevision</c>: <c>sprk_revisedon</c>, <c>statecode</c>; and, since task 063's join, <c>sprk_budget</c>:
    /// <c>statecode</c>, <c>sprk_budgetstartdate</c>, <c>sprk_budgetenddate</c>. The allow-list stays as it is (D-161); membership of
    /// the related-table filter columns remains the owner's call.</para>
    /// <para><b>Residual, derived Thresholds (accepted by the same re-signing, D-161).</b> A Threshold on <c>sprk_totalspendtodate</c>,
    /// <c>sprk_totalbudget</c> or <c>sprk_budgetutilizationpercent</c> records only the stored subject column in
    /// <see cref="CompiledPredicate.ReadColumns"/>, but the evaluator decides it from org-wide grouped sums
    /// (<c>FinanceBaseValues.BuildGroupedSpendFetchXml</c>) over <c>sprk_invoice.sprk_approvedtotal</c> and
    /// <c>sprk_invoice.sprk_totalamount</c> (the approved total, else the billed total: ebilling-r1 D-130),
    /// <c>sprk_invoice.statuscode</c> (ebilling-r1 D-41 / D-103, ontology D-166; counted = Approved, Sent to AP, Paid) and <c>sprk_budget.sprk_totalbudget</c>. Those base columns are not on this list and
    /// not covered by the live <c>IsSecured</c> test. Repeated dry runs with a moving threshold reveal the current sum, including
    /// invoices the author cannot read (the stored roll-up on the subject already shows a possibly stale copy).</para>
    /// <para><b>Seed (2026-10-16), and nothing else.</b>
    /// <list type="bullet">
    /// <item>The columns the six live Ontology policies' current versions read on spaarkedev1 (POL-MATTER-NOBUDGET added
    /// 2026-10-17).</item>
    /// <item>The columns the authoring tests read: <c>statecode</c> on every table in
    /// <see cref="PredicateCompiler.EvaluatorGlobalReadableEntities"/>, and <c>sprk_matter.sprk_matterid</c>.</item>
    /// <item>The compiler's typed comparables: <see cref="PredicateCompiler.DateOnlyColumns"/> and
    /// <see cref="PredicateCompiler.ThresholdColumns"/>.</item>
    /// <item>The <see cref="PredicateCompiler.VerifiedJoins"/> paths.</item>
    /// </list>
    /// Adding a column is a code change with review, and the live test must stay green.</para>
    /// <para><b>Where it is enforced.</b> Only by <see cref="ValidateForAuthoring"/>, which every authoring route (validate, dry run,
    /// save, publish) calls. <see cref="ValidateForSave"/> and <see cref="TryPrepareForEvaluation"/> do not check it. The evaluator
    /// shows its answers to no one, so a version written outside the BFF still evaluates as before.</para>
    /// </remarks>
    public static readonly FrozenSet<(string Entity, string Column)> AuthorableColumns =
        new (string Entity, string Column)[]
        {
            // The six live Ontology policies (POL-COMMIT-BUDGET v2, POL-TASK-OVERDUE, POL-TASK-DUESOON, POL-WA-RESPONSE, POL-TODO-OVERDUE,
            // and POL-MATTER-NOBUDGET, seeded Off by task 063 on 2026-10-17).
            ("sprk_communication", "sprk_triagecategory"),
            ("sprk_communication", "sprk_receiveddate"),
            ("sprk_communication", "sprk_reviewoutcome"),
            ("sprk_budgetrevision", "sprk_revisedon"),
            ("sprk_event", "sprk_eventtype_ref"),
            ("sprk_event", "statuscode"),
            ("sprk_todo", "statuscode"),
            ("sprk_workassignment", "statecode"),
            ("sprk_matter", "createdon"), // POL-MATTER-NOBUDGET (task 063), a subject 'when' column
            // The authoring tests (OntologyAdminAuthoringEndpointTests, OntologyAdminAuthoringLiveTests).
            ("sprk_matter", "statecode"),
            ("sprk_matter", "sprk_matterid"),
            ("sprk_communication", "statecode"),
            ("sprk_project", "statecode"),
            ("sprk_todo", "statecode"),
            ("sprk_event", "statecode"),
            ("sprk_invoice", "statecode"),
            ("sprk_budgetrevision", "statecode"),
            ("sprk_budget", "statecode"),
            ("sprk_memo", "statecode"),
        }
        .Concat(PredicateCompiler.DateOnlyColumns)
        .Concat(PredicateCompiler.ThresholdColumns)
        .Concat(PredicateCompiler.VerifiedJoins.Select(kv => (kv.Key.Related, kv.Value)))
        .ToFrozenSet();

    private readonly RuleBodySchemaValidator _schemaValidator;
    private readonly PredicateCompiler _compiler;
    private readonly ILogger<PolicyVersionValidator> _logger;

    public PolicyVersionValidator(
        RuleBodySchemaValidator schemaValidator,
        PredicateCompiler compiler,
        ILogger<PolicyVersionValidator> logger)
    {
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Pure, side-effect-free validation (renamed from the task's first-pass "Validate" — review finding #6):
    /// no logging, no metering, no compiled predicate returned. For any BFF write path that comes to exist for
    /// <c>sprk_policyversion</c> to call at save time and surface its own field-level error (ADR-002: one
    /// owner per invariant). Never throws for ANY input, however pathological (review finding #1).
    /// </summary>
    /// <param name="ruleTypeRaw">The policy version's <c>sprk_ruletype</c>, by name or numeric option-set value.</param>
    /// <param name="ruleBodyJson">The policy version's <c>sprk_rulebody</c>.</param>
    /// <param name="messageTemplate">The policy version's <c>sprk_messagetemplate</c>, or null/blank if none.</param>
    /// <param name="expectedDomain">Optional (ebilling-r1 task 070 fix-round-1, F4). When given, a <c>sprk_ruletype</c> that is a
    /// closed-set member of ANOTHER <see cref="RuleDomain"/> is refused with
    /// <see cref="OntologyWriterFailureReason.RuleTypeWrongDomain"/> before any body check: a save path for an Ontology policy
    /// passes <see cref="RuleDomain.Ontology"/>, a billing one <see cref="RuleDomain.Billing"/>, so a billing type cannot be saved
    /// onto an Ontology policy (or the reverse) and only be caught later by a reader's gate. An unknown type is still
    /// <c>rule_type_unsupported</c>. Omitted (<c>null</c>): behaviour is unchanged, any closed-set member is validated as its own domain.</param>
    /// <param name="billingConsequence">Billing only (ebilling-r1 task 073, FR-26, D-89): the version's <c>sprk_consequence</c> option
    /// value. When given for a billing type, a value that type may not carry (Reject on TimeIncrement, DailyHoursCap, NonBillableActivity,
    /// AiObservation or ApprovedTimekeeper; any value outside the three defined) is refused with
    /// <see cref="OntologyWriterFailureReason.ConsequenceNotAllowed"/>. <c>null</c>: the consequence is not checked here (the caller
    /// reports a missing one itself). Ignored for an Ontology type.</param>
    public PolicyVersionValidationResult ValidateForSave(
        string? ruleTypeRaw, string? ruleBodyJson, string? messageTemplate, RuleDomain? expectedDomain = null, int? billingConsequence = null)
    {
        var (result, _) = ValidateInternal(
            ruleTypeRaw, ruleBodyJson, messageTemplate, subjectId: null, forOntologyEvaluation: false, expectedDomain, authoring: false,
            billingConsequence);
        return result;
    }

    /// <summary>
    /// ebilling-r1 task 081 (procedure model 7.2, O-24): whether a policy version may carry <c>sprk_procedureversion</c>. Ontology-area
    /// Procedures are document-only in R1, so the link is refused unless the policy is Billing-domain (<paramref name="policyDomain"/> is
    /// its <c>sprk_domain</c> option value; null or empty is Ontology, as everywhere else). A version that names no Procedure Version
    /// (<paramref name="procedureVersionId"/> null) passes whatever the domain. Pure; never throws. Every BFF create of a policy version that
    /// sets the link calls it (the Procedure copy-forward and the re-keyed edit in <c>RuleSetService</c>).
    /// </summary>
    public static PolicyVersionValidationResult ValidateProcedureVersionLink(int? policyDomain, Guid? procedureVersionId)
    {
        if (procedureVersionId is null)
        {
            return PolicyVersionValidationResult.Success();
        }

        return policyDomain == RuleDomains.PolicyDomainBilling && procedureVersionId != Guid.Empty
            ? PolicyVersionValidationResult.Success()
            : PolicyVersionValidationResult.Failure(
                OntologyWriterFailureReason.ProcedureVersionWrongDomain,
                "sprk_procedureversion: only a Billing-domain policy version may belong to a Procedure Version (Ontology-area Procedures are document-only).");
    }

    /// <summary>
    /// <see cref="ValidateForSave"/> plus the authorable-column allow-list (task 102a, owner option 101(b)): a body naming a column
    /// outside <see cref="AuthorableColumns"/> is refused with <see cref="OntologyWriterFailureReason.ColumnNotAuthorable"/>. The check
    /// runs before the template check. This is the ONE validation every BFF authoring path uses (validate, dry run, save, publish), so
    /// they cannot disagree. It is pure: no logging, no metering, and it never throws.
    /// </summary>
    public PolicyVersionAuthoringResult ValidateForAuthoring(string? ruleTypeRaw, string? ruleBodyJson, string? messageTemplate)
    {
        // ebilling-r1 D-125: a system rule type is never authorable; refused before the domain check, with its own reason.
        if (RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out var systemType) && BillingRuleCatalogue.IsSystemType(systemType))
        {
            return new PolicyVersionAuthoringResult(
                PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported, BillingRuleCatalogue.SystemRuleRefusal),
                Array.Empty<string>());
        }

        // ebilling-r1 merge (070 F3): authoring is the Ontology admin's validation, so a billing rule type is refused here with
        // RuleTypeWrongDomain (FR-24) instead of passing as a billing body that the allow-list check never sees.
        var (result, compiled) = ValidateInternal(
            ruleTypeRaw, ruleBodyJson, messageTemplate, subjectId: null, forOntologyEvaluation: false, RuleDomain.Ontology, authoring: true);
        return new PolicyVersionAuthoringResult(result, compiled is null ? Array.Empty<string>() : AuthorableTemplateFields(compiled));
    }

    /// <summary>The compiled predicate's template-eligible fields whose every read is on <see cref="AuthorableColumns"/>, in ordinal
    /// order. Fails closed: with no recorded reads (<see cref="CompiledPredicate.ReadColumns"/> null) no field is eligible.</summary>
    internal static IReadOnlyList<string> AuthorableTemplateFields(CompiledPredicate compiled)
    {
        if (compiled.ReadColumns is not { } reads)
        {
            return Array.Empty<string>();
        }

        return compiled.TemplateEligibleFields
            .Where(field => reads.Where(r => r.Column == field).All(AuthorableColumns.Contains))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The allow-list check of a compiled body: null when every read is on <see cref="AuthorableColumns"/>, otherwise the
    /// <see cref="OntologyWriterFailureReason.ColumnNotAuthorable"/> refusal. Fails closed: a predicate whose reads were not recorded
    /// (<see cref="CompiledPredicate.ReadColumns"/> null) cannot be shown to be on the list, so it is refused.</summary>
    internal static PolicyVersionValidationResult? CheckAuthorableColumns(CompiledPredicate compiled)
    {
        if (compiled.ReadColumns is not { } reads)
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.ColumnNotAuthorable,
                "The columns this rule reads were not recorded, so it cannot be checked against the authorable columns; it is refused.");
        }

        var unlisted = reads
            .Where(c => !AuthorableColumns.Contains(c))
            .Select(c => $"{c.Entity}.{c.Column}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return unlisted.Length == 0
            ? null
            : PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.ColumnNotAuthorable,
                $"The rule names column(s) a rule may not use: [{string.Join(", ", unlisted)}]. A rule may name only the " +
                "authorable columns the catalog offers; adding one is a reviewed code change.");
    }

    /// <summary>
    /// The fail-closed evaluation-time gate (task 022 owner decision) and, per the coordinator's F6 decision,
    /// the ONLY place the evaluator (task 031) may obtain a <see cref="CompiledPredicate"/>. On refusal: logs
    /// <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> (carrying ONLY <paramref name="pv"/>'s
    /// <see cref="PolicyVersionSnapshot.PolicyVersionId"/> + <see cref="PolicyVersionSnapshot.PolicyCode"/> +
    /// the bounded reason — never rule body, template, or error text), records
    /// <see cref="OntologyWriterTelemetry.RecordPolicyInvalid"/>, sets <paramref name="compiled"/> to
    /// <c>null</c>, and returns <c>false</c> — never throws for anything about the policy version's content, so
    /// the caller's own loop over many policy versions is never aborted by one bad one (review finding #1,
    /// two-layer catch — see class remarks).
    /// </summary>
    /// <param name="pv">The policy version's identity + the four column values this check needs.</param>
    /// <param name="subjectId">Optional: narrow the compiled predicate to one subject record (event-triggered
    /// runs). <c>null</c> = all subjects; <see cref="Guid.Empty"/> is a caller bug and throws.</param>
    /// <param name="compiled">The compiled predicate on success; <c>null</c> on refusal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pv"/> is null (a caller bug).</exception>
    /// <exception cref="ArgumentException"><paramref name="subjectId"/> is <see cref="Guid.Empty"/> (a caller bug,
    /// finding F1) — thrown before any validation, so nothing is logged or metered against the policy.</exception>
    public bool TryPrepareForEvaluation(
        PolicyVersionSnapshot pv, Guid? subjectId, [NotNullWhen(true)] out CompiledPredicate? compiled)
    {
        ArgumentNullException.ThrowIfNull(pv);
        if (subjectId == Guid.Empty)
        {
            // Finding F1: a contract violation by the CALLER must be loud, and must never be counted as an
            // invalid POLICY -- so it is checked here, outside the validation (and its catch-all) entirely.
            throw new ArgumentException(
                "subjectId must not be Guid.Empty; pass null to evaluate every subject.", nameof(subjectId));
        }

        PolicyVersionValidationResult result;
        CompiledPredicate? candidate;
        try
        {
            (result, candidate) = ValidateInternal(
                pv.RuleType, pv.RuleBody, pv.MessageTemplate, subjectId, forOntologyEvaluation: true, expectedDomain: null, authoring: false);
        }
        catch (Exception)
        {
            // Second layer (review finding #1c): ValidateInternal already catches everything IT knows how to
            // name a reason for -- this exists only so a future bug inside that catch-all, or in this method's
            // own tuple deconstruction, still cannot escape as an unhandled exception from the evaluator's
            // one-call-per-policy-version seam.
            result = PolicyVersionValidationResult.Failure(
                OntologyWriterFailureReason.InternalError,
                "Unexpected failure preparing this policy version for evaluation.");
            candidate = null;
        }

        return Conclude(pv, result, candidate, out compiled);
    }

    /// <summary>
    /// The tail of <see cref="TryPrepareForEvaluation"/>, split out so the "validation passed but nothing was compiled" branch
    /// can be driven directly by a test (ebilling-r1 task 070 fix-round-1, K1). Success needs BOTH a valid result and a
    /// compiled predicate. A valid result with no predicate breaks <see cref="ValidateInternal"/>'s contract (Success() is paired
    /// with a predicate); it is refused like any other invalid policy, logged and metered as
    /// <see cref="OntologyWriterFailureReason.InternalError"/>, never returned as <c>false</c> in silence.
    /// </summary>
    internal bool Conclude(
        PolicyVersionSnapshot pv, PolicyVersionValidationResult result, CompiledPredicate? candidate,
        [NotNullWhen(true)] out CompiledPredicate? compiled)
    {
        if (result.IsValid)
        {
            if (candidate is not null)
            {
                compiled = candidate;
                return true;
            }

            result = PolicyVersionValidationResult.Failure(
                OntologyWriterFailureReason.InternalError,
                "Validation passed but produced no compiled predicate.");
        }

        _logger.LogError(
            OntologyWriterEvents.PolicyVersionInvalid,
            "Policy version {PolicyVersionId} ({PolicyCode}) failed rule-body validation at evaluation time; " +
            "producing no Signal for it and continuing with the other policies (reason={Reason}).",
            pv.PolicyVersionId, pv.PolicyCode, result.Reason ?? OntologyWriterFailureReason.InternalError);
        OntologyWriterTelemetry.RecordPolicyInvalid(result.Reason ?? OntologyWriterFailureReason.InternalError);

        compiled = null;
        return false;
    }

    /// <summary>
    /// The fail-closed billing gate (ebilling-r1 task 070, spec FR-24): the ONLY way to obtain a
    /// <see cref="BillingRuleDefinition"/>. Mirrors <see cref="TryPrepareForEvaluation"/>: it runs the same save-time checks
    /// as <see cref="ValidateForSave"/> for a billing type (length cap, the type's schema, the template against the type's
    /// fact-field set) and on refusal logs <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> (id, code and bounded
    /// reason only; the message names it a billing policy version), meters <see cref="OntologyWriterTelemetry.RecordBillingRuleInvalid"/>
    /// (<c>billing.rule.invalid</c>, NOT <c>ontology.policy.invalid</c>), sets <paramref name="definition"/> to
    /// null and returns false. An Ontology type, an unknown, blank or combined type, an invalid body and an oversized body
    /// are all refused. Task 073 (FR-26, I-5, D-89): a row whose <see cref="PolicyVersionSnapshot.Consequence"/> its type may not
    /// carry is refused too, with <see cref="OntologyWriterFailureReason.ConsequenceNotAllowed"/>, because a row authored directly in
    /// Dataverse never passed the publish check. Never throws for the row's content.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pv"/> is null (a caller bug).</exception>
    public bool TryPrepareBillingRule(PolicyVersionSnapshot pv, [NotNullWhen(true)] out BillingRuleDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(pv);

        PolicyVersionValidationResult result;
        definition = null;
        try
        {
            if (!RuleBodySchemaValidator.TryParseRuleType(pv.RuleType, out var ruleType))
            {
                result = UnknownRuleType(pv.RuleType);
            }
            else if (!RuleDomains.IsBilling(ruleType))
            {
                result = PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                    $"sprk_ruletype '{ruleType}' is not a billing rule type; the billing gate evaluates only the billing " +
                    "catalogue (FR-24).");
            }
            else
            {
                result = ValidateBilling(ruleType, pv.RuleBody, pv.MessageTemplate, pv.Consequence, atSave: false);
                if (result.IsValid)
                {
                    definition = BillingRuleDefinition.FromValidatedBody(pv.PolicyVersionId, pv.PolicyCode, ruleType, pv.RuleBody!);
                }
            }
        }
        catch (Exception ex)
        {
            // Same rule as TryPrepareForEvaluation: nothing about the row's content escapes as an exception. Type name only.
            result = PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.InternalError,
                $"Unexpected {ex.GetType().Name} preparing this billing rule.");
            definition = null;
        }

        if (result.IsValid && definition is not null)
        {
            return true;
        }

        var reason = result.Reason ?? OntologyWriterFailureReason.InternalError;
        _logger.LogError(
            OntologyWriterEvents.PolicyVersionInvalid,
            "Billing policy version {PolicyVersionId} ({PolicyCode}) failed validation at the billing gate; it is not " +
            "evaluated (reason={Reason}).",
            pv.PolicyVersionId, pv.PolicyCode, reason);
        // K3 (fix-round-1): its own instrument, so ontology.policy.invalid counts Ontology policies only.
        OntologyWriterTelemetry.RecordBillingRuleInvalid(reason);

        definition = null;
        return false;
    }

    private static PolicyVersionValidationResult UnknownRuleType(string? ruleTypeRaw) =>
        PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
            $"Unknown sprk_ruletype '{ruleTypeRaw}'. The closed set is {RuleBodySchemaValidator.ClosedSetDescription} " +
            "(design.md CM-7; ebilling-r1 FR-24); adding a type is a code change with review, not a data value.");

    /// <summary>
    /// Save-time checks for a billing type (ebilling-r1 task 070): the length cap before any parse, the type's schema, then
    /// the template. A billing body is never compiled: <see cref="PredicateCompiler"/> refuses every billing type. A template
    /// token must be in <see cref="BillingRuleCatalogue.TemplateFactFields"/> for the type, the static set of values the
    /// type's handler (task 071) supplies for a finding.
    /// </summary>
    /// <remarks><paramref name="atSave"/>: true on the save path (<see cref="ValidateForSave"/>), false at the billing gate. Only the save
    /// path refuses a Reject on a Duplicate rule that compares lines only (073 review, owner question 3); see the check below.</remarks>
    private PolicyVersionValidationResult ValidateBilling(
        RuleType ruleType, string? ruleBodyJson, string? messageTemplate, int? consequence, bool atSave)
    {
        // D-125: a system type is never authored, saved, or evaluated from a body (the billing gate refuses it too: 049 creates its
        // findings directly, never through a BillingRuleDefinition).
        if (BillingRuleCatalogue.IsSystemType(ruleType))
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported, BillingRuleCatalogue.SystemRuleRefusal);
        }

        if (ruleBodyJson is not null && ruleBodyJson.Length > RuleBodySchemaValidator.MaxRuleBodyLength)
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleBodyTooLarge,
                RuleBodySchemaValidator.RuleBodyTooLongMessage(ruleBodyJson.Length));
        }

        var schemaResult = _schemaValidator.Validate(ruleType, ruleBodyJson);
        if (!schemaResult.IsValid)
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.SchemaInvalid, schemaResult.Errors);
        }

        // Task 115 (procedure model 6.3, v5.1 row 12): a schema-valid AddedFee form whose facts are not extracted yet is refused at save
        // AND at the billing gate, so it is never published and never evaluated as "holds". Only the cap form passes.
        if (BillingRuleCatalogue.UnsupportedFormRefusal(ruleType, ruleBodyJson) is { } formRefusal)
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleFormNotSupported, formRefusal);
        }

        if (!string.IsNullOrWhiteSpace(messageTemplate))
        {
            if (SignalWriter.HasMalformedPlaceholder(messageTemplate))
            {
                return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateMalformed,
                    "sprk_messagetemplate contains a malformed {{...}} placeholder (unbalanced braces, an empty token, or a " +
                    "character outside [A-Za-z0-9_.] in the field name). Curly braces are reserved for {{field}} placeholders.");
            }

            var allowed = BillingRuleCatalogue.TemplateFactFields(ruleType);
            var refused = SignalWriter.ExtractTemplateTokens(messageTemplate).Where(t => !allowed.Contains(t)).ToArray();
            if (refused.Length > 0)
            {
                return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateTokenOutsideReadSet,
                    $"sprk_messagetemplate references field(s) a {ruleType} rule does not read: " +
                    $"[{string.Join(", ", refused)}]. Allowed: [{string.Join(", ", allowed.Order(StringComparer.Ordinal))}].");
            }
        }

        // Task 073 (FR-26, I-5, D-89): the consequence belongs to the type. Not stated (null) = not checked here.
        if (consequence is { } stated && !BillingRuleCatalogue.IsConsequenceAllowed(ruleType, stated))
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.ConsequenceNotAllowed,
                BillingRuleCatalogue.ConsequenceRefusal(ruleType, stated));
        }

        // 073 review, owner question 3: a Duplicate rule whose matchFields lack firmInvoiceNumber compares lines only, and a line-level
        // Duplicate never rejects (design.md 6.1, "Reject (invoice), Flag (line)"), so a Reject on it would be silently held. Refused at
        // save, with the same stable reason. Save only: the billing gate leaves such a stored row to evaluate, and the rejection rule
        // holds its line-level findings (duplicate_at_line_level) as Flags, which is what the rule can do.
        if (atSave && ruleType == RuleType.Duplicate && consequence == (int)Finance.BillingConsequence.Reject
            && !DuplicateComparesInvoiceNumber(ruleBodyJson))
        {
            return PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.ConsequenceNotAllowed,
                "A Duplicate rule whose matchFields do not include firmInvoiceNumber compares lines only, and a duplicate line is " +
                "flagged, never rejected (design.md 6.1). Add firmInvoiceNumber to matchFields to reject a duplicate invoice, or use Flag.");
        }

        return PolicyVersionValidationResult.Success();
    }

    /// <summary>True when a schema-valid Duplicate body lists <c>firmInvoiceNumber</c> in <c>matchFields</c> (an invoice-level comparison).</summary>
    private static bool DuplicateComparesInvoiceNumber(string? ruleBodyJson)
    {
        if (string.IsNullOrWhiteSpace(ruleBodyJson))
        {
            return false;
        }

        using var document = System.Text.Json.JsonDocument.Parse(ruleBodyJson);
        return document.RootElement.TryGetProperty("matchFields", out var fields)
               && fields.ValueKind == System.Text.Json.JsonValueKind.Array
               && fields.EnumerateArray().Any(f => f.ValueKind == System.Text.Json.JsonValueKind.String && f.GetString() == "firmInvoiceNumber");
    }

    /// <summary>
    /// The shared validation body for both <see cref="ValidateForSave"/> and
    /// <see cref="TryPrepareForEvaluation"/>: rule type, then the length cap (before any parse — finding F2),
    /// then schema shape (evaluated ONCE — finding F2), then full compilation without re-evaluating the schema,
    /// then the malformed-placeholder check, then the template-eligibility check. Returns the compiled predicate
    /// alongside a success result so <see cref="TryPrepareForEvaluation"/> never has to compile twice.
    /// </summary>
    /// <remarks>Task 102a: with <paramref name="authoring"/>, the allow-list check runs between compilation and the template check. Once
    /// the body has compiled, a refusal still returns the compiled predicate, so <see cref="ValidateForAuthoring"/> can report the
    /// eligible fields. <see cref="TryPrepareForEvaluation"/> discards it on any refusal.</remarks>
    private (PolicyVersionValidationResult Result, CompiledPredicate? Compiled) ValidateInternal(
        string? ruleTypeRaw, string? ruleBodyJson, string? messageTemplate, Guid? subjectId, bool forOntologyEvaluation,
        RuleDomain? expectedDomain, bool authoring, int? billingConsequence = null)
    {
        try
        {
            // Rule-type recognition is decided ONCE, HERE, and categorized as RuleTypeUnsupported --
            // deliberately NOT by delegating to RuleBodySchemaValidator.ValidateRaw, which folds "unknown
            // ruletype" and "known-type body fails its schema" into the SAME RuleBodyValidationResult.Failure.
            // That conflation was a real bug surfaced by the reviewer's reason-tag probe: every unknown/blank
            // ruletype was being metered as schema_invalid instead of rule_type_unsupported.
            if (!RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out var ruleType))
            {
                return (UnknownRuleType(ruleTypeRaw), null);
            }

            if (expectedDomain is { } expected && Enum.IsDefined(ruleType) && RuleDomains.DomainOf(ruleType) != expected)
            {
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeWrongDomain,
                    $"sprk_ruletype '{ruleType}' is a {RuleDomains.DomainOf(ruleType)} rule type; this policy is a {expected} " +
                    "policy and takes only its own domain's rule types (FR-24)."), null);
            }

            if (!RuleDomains.IsOntology(ruleType))
            {
                // ebilling-r1 task 070 (FR-24 MUST): the Ontology evaluator never compiles, evaluates or treats as true a type
                // of another domain -- refused FIRST, before any length, schema or compile step. Save-time validation of a
                // billing type runs the billing checks instead (no compile).
                if (forOntologyEvaluation || !RuleDomains.IsBilling(ruleType))
                {
                    return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                        $"sprk_ruletype '{ruleType}' is a {RuleDomainName(ruleType)} rule type; the Ontology evaluator does " +
                        "not evaluate it (FR-24: a consumer that cannot evaluate a type refuses it)."), null);
                }

                return (ValidateBilling(ruleType, ruleBodyJson, messageTemplate, billingConsequence, atSave: true), null);
            }

            if (ruleType == RuleType.Switch)
            {
                // Switch is a closed-set member that D-16 defers: no schema, no compiler. Fail closed rather than
                // call RuleBodySchemaValidator.Validate and let its NotSupportedException (by design, for this
                // known-but-unimplemented case) propagate. Existence and Threshold (task 025) are the shipped types.
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                    $"sprk_ruletype '{ruleType}' is not supported: Switch is deferred (D-16), so its sprk_rulebody " +
                    "cannot be validated. The supported types are Existence and Threshold."), null);
            }

            // Finding F2: the length cap BEFORE any parse or schema evaluation, so an oversized row never holds
            // the schema validator's process-wide lock, and is reported as what it is (rule_body_too_large),
            // not as whatever the parser would say about its content.
            if (ruleBodyJson is not null && ruleBodyJson.Length > RuleBodySchemaValidator.MaxRuleBodyLength)
            {
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleBodyTooLarge,
                    RuleBodySchemaValidator.RuleBodyTooLongMessage(ruleBodyJson.Length)), null);
            }

            // ruleType is Existence or Threshold here, so RuleBodySchemaValidator.Validate never throws NotSupportedException
            // for it -- the try/catch is defensive only, in case that contract ever changes.
            RuleBodyValidationResult schemaResult;
            try
            {
                schemaResult = _schemaValidator.Validate(ruleType, ruleBodyJson);
            }
            catch (NotSupportedException ex)
            {
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                    $"sprk_ruletype '{ruleType}' could not be validated: {ex.Message}"), null);
            }

            if (!schemaResult.IsValid)
            {
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.SchemaInvalid, schemaResult.Errors), null);
            }

            CompiledPredicate compiled;
            try
            {
                // Finding F2: the schema was evaluated just above -- do not evaluate it a second time inside
                // the compiler. This is the ONLY production caller of a PredicateCompiler.Compile* method
                // (PredicateCompilerCallerGuardTests).
                compiled = _compiler.CompileSchemaValidated(ruleBodyJson!, subjectId, ruleType);
            }
            catch (PredicateCompilationException ex)
            {
                // The compiler enforces strictly MORE than the schema (verified joins, global-readable
                // entities, no cross-clause '$' references, Dataverse link-entity/body-length/in-list limits)
                // -- a body that is schema-valid but not compilable is still an invalid policy version.
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.CompileRefused, ex.Message), null);
            }

            // Task 102a (owner option 101(b)): only allow-listed columns, wherever the body names them.
            if (authoring && CheckAuthorableColumns(compiled) is { } notAuthorable)
            {
                return (notAuthorable, compiled);
            }

            if (!string.IsNullOrWhiteSpace(messageTemplate))
            {
                if (SignalWriter.HasMalformedPlaceholder(messageTemplate))
                {
                    return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateMalformed,
                        "sprk_messagetemplate contains a malformed {{...}} placeholder (unbalanced braces, an " +
                        "empty token, or a character outside [A-Za-z0-9_.] in the field name) that would " +
                        "otherwise render as literal text. Curly braces are reserved for {{field}} " +
                        "placeholders: a single literal brace in prose (e.g. \"(see {policy})\") or a full-width " +
                        "brace is refused too -- write the prose without braces."), compiled);
                }

                var tokens = SignalWriter.ExtractTemplateTokens(messageTemplate);
                var refused = tokens.Where(t => !compiled.TemplateEligibleFields.Contains(t)).ToArray();
                if (refused.Length > 0)
                {
                    var ambiguous = refused.Where(compiled.AmbiguousTemplateFields.Contains).ToArray();
                    var unpinned = refused.Where(compiled.UnpinnedTemplateFields.Contains).ToArray();
                    var unknown = refused.Except(ambiguous).Except(unpinned).ToArray();
                    var detail = new List<string>();
                    if (unknown.Length > 0)
                    {
                        detail.Add("not read by the subject's own 'when' filter or any 'exists' clause (a " +
                                   $"'notExists' clause's fields can never be read for a firing subject): [{string.Join(", ", unknown)}]");
                    }

                    if (unpinned.Length > 0)
                    {
                        detail.Add("read by an 'exists' clause that does not pin it to exactly one value (a " +
                                   "range, '<>' or multi-value list can match several rows with different " +
                                   "values, so the value is not defined; pin it with a single value, or remove " +
                                   "it from the template), or required to be empty by a 'when' null test (an " +
                                   $"empty field has no value to state): [{string.Join(", ", unpinned)}]");
                    }

                    if (ambiguous.Length > 0)
                    {
                        detail.Add("ambiguous -- read on more than one entity or row, or pinned to different " +
                                   $"values by different clauses, so the value is not well defined: [{string.Join(", ", ambiguous)}]");
                    }

                    return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateTokenOutsideReadSet,
                        "sprk_messagetemplate references {{...}} field(s) " + string.Join("; ", detail) +
                        ". Section 0.3 is binding: the sentence may assert only what the predicate actually " +
                        "read for the firing subject."), compiled);
                }
            }

            return (PolicyVersionValidationResult.Success(), compiled);
        }
        catch (Exception ex)
        {
            // Final catch-all (review finding #1): the libraries this validator calls into are not fully
            // audited for every exception they can throw on syntactically-valid-but-pathological input --
            // duplicate JSON keys and 1e999999 both escaped here before the task 022 rework fixed
            // RuleBodySchemaValidator itself; this exists for whatever is discovered NEXT. Bounded message:
            // the exception's TYPE name only -- never ex.Message, which can echo admin-authored rule-body or
            // template content the owner's no-silent-failure directive forbids logging/metering.
            return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.InternalError,
                $"Unexpected {ex.GetType().Name} during rule-body validation."), null);
        }
    }

    private static string RuleDomainName(RuleType ruleType) =>
        Enum.IsDefined(ruleType) ? RuleDomains.DomainOf(ruleType).ToString() : "unknown";
}
