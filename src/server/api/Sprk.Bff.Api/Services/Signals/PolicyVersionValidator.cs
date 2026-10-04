using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
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
/// (<c>RuleTypeUnsupported</c> / <c>SchemaInvalid</c> / <c>CompileRefused</c> /
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
public sealed record PolicyVersionSnapshot(
    Guid PolicyVersionId, string PolicyCode, string? RuleType, string? RuleBody, string? MessageTemplate);

/// <summary>
/// The single, reusable, fail-closed validation seam for a <c>sprk_policyversion</c>'s <c>sprk_rulebody</c> +
/// <c>sprk_messagetemplate</c> — built on top of <see cref="RuleBodySchemaValidator"/> (task 020, shape) and
/// <see cref="PredicateCompiler"/> (task 021, compiles AND exposes the predicate's positive read set), per the
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
/// <para><b>Could <see cref="PredicateCompiler.Compile"/> itself be made non-public to force this?</b> No,
/// without a contortion that costs more than it buys (F6's own escape hatch — "otherwise say why not"):
/// <see cref="PredicateCompiler"/>, this class, and the future evaluator (task 031) all live in the SAME
/// <c>Sprk.Bff.Api</c> assembly, so marking <c>Compile</c> <c>internal</c> would not stop the evaluator from
/// calling it directly — <c>internal</c> only blocks a DIFFERENT assembly, which was never the actual risk.
/// Achieving true single-path enforcement at the language level would mean nesting <see cref="PredicateCompiler"/>
/// as a private class inside this one, which would break its own standalone, already-tested public contract
/// (<c>PredicateCompilerTests.cs</c>, task 021's maintain-class tests) and its independent DI registration for
/// no real gain. The coordinator's own mitigation — a POML constraint on tasks 031/032 requiring this gate,
/// enforced at code-review time — is the right-sized control for what is fundamentally an intra-assembly
/// discipline question, not a trust-boundary one.</para>
/// <para><b>F5 — the §0.3 template vocabulary is POSITIVE fields only (coordinator decision, 2026-10-04).</b>
/// A <c>sprk_messagetemplate</c> token may reference ONLY a field read by the subject's own <c>when</c> filter
/// or by an <c>exists</c> clause — never a <c>notExists</c> clause, because a firing subject has, by
/// definition, no matching <c>notExists</c> row, so that clause's filter VALUES cannot be read for it (only
/// the fact of absence is known; literal template text can still describe that). A field name appearing on
/// MORE than one distinct positive entity is also refused — the value would be ambiguous. See
/// <see cref="CompiledPredicate.PositiveReadFields"/> / <see cref="CompiledPredicate.AmbiguousPositiveFields"/>
/// for where this is computed. <b>What task 031 must put in <c>SignalWriteRequest.FactValues</c></b>: exactly
/// the values read for the FIRING SUBJECT from its positive clauses (the subject's own field values for a
/// <c>when</c>-filtered field; the matched related row's field values for an <c>exists</c>-clause field) —
/// never a value sourced from a <c>notExists</c> clause, and never a value for an ambiguous field name without
/// first disambiguating by entity.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> <b>Existing</b> — <see cref="RuleBodySchemaValidator"/>
/// checks shape only (ADR-013: no logging, no Dataverse, no policy-version identity); <see cref="PredicateCompiler"/>
/// compiles a FetchXML query and exposes the positive/ambiguous read-field sets. Neither owns "is this whole
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
/// <para><b>"Never throws" is enforced at TWO layers (review finding #1).</b> <see cref="ValidateInternal"/>
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
    public PolicyVersionValidationResult ValidateForSave(string? ruleTypeRaw, string? ruleBodyJson, string? messageTemplate)
    {
        var (result, _) = ValidateInternal(ruleTypeRaw, ruleBodyJson, messageTemplate, subjectId: null);
        return result;
    }

    /// <summary>
    /// The fail-closed evaluation-time gate (task 022 owner decision) and, per the coordinator's F6 decision,
    /// the ONLY place the evaluator (task 031) may obtain a <see cref="CompiledPredicate"/>. On refusal: logs
    /// <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> (carrying ONLY <paramref name="pv"/>'s
    /// <see cref="PolicyVersionSnapshot.PolicyVersionId"/> + <see cref="PolicyVersionSnapshot.PolicyCode"/> +
    /// the bounded reason — never rule body, template, or error text), records
    /// <see cref="OntologyWriterTelemetry.RecordPolicyInvalid"/>, sets <paramref name="compiled"/> to
    /// <c>null</c>, and returns <c>false</c> — never throws, so the caller's own loop over many policy
    /// versions is never aborted by one bad one (review finding #1, two-layer catch — see class remarks).
    /// </summary>
    /// <param name="pv">The policy version's identity + the four column values this check needs.</param>
    /// <param name="subjectId">Optional: narrow the compiled predicate to one subject record (event-triggered
    /// runs) — passed straight through to <see cref="PredicateCompiler.Compile"/>.</param>
    /// <param name="compiled">The compiled predicate on success; <c>null</c> on refusal.</param>
    public bool TryPrepareForEvaluation(
        PolicyVersionSnapshot pv, Guid? subjectId, [NotNullWhen(true)] out CompiledPredicate? compiled)
    {
        ArgumentNullException.ThrowIfNull(pv);

        PolicyVersionValidationResult result;
        try
        {
            (result, compiled) = ValidateInternal(pv.RuleType, pv.RuleBody, pv.MessageTemplate, subjectId);
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
            compiled = null;
        }

        if (result.IsValid)
        {
            // ValidateInternal's contract pairs Success() with a non-null compiled predicate (and only with
            // one) -- phrased as "return compiled is not null" rather than a bare "return true" so the
            // compiler's own nullable-flow analysis can verify the [NotNullWhen(true)] contract on `compiled`,
            // not just this method's own narrative.
            return compiled is not null;
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
    /// The shared validation body for both <see cref="ValidateForSave"/> and
    /// <see cref="TryPrepareForEvaluation"/>: schema shape, then (for <see cref="RuleType.Existence"/>) full
    /// compilation, then the malformed-placeholder check, then the positive-read-set / ambiguity check.
    /// Returns the compiled predicate alongside a success result so <see cref="TryPrepareForEvaluation"/>
    /// never has to compile twice.
    /// </summary>
    private (PolicyVersionValidationResult Result, CompiledPredicate? Compiled) ValidateInternal(
        string? ruleTypeRaw, string? ruleBodyJson, string? messageTemplate, Guid? subjectId)
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
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                    $"Unknown sprk_ruletype '{ruleTypeRaw}'. The closed set is Threshold, Switch, Existence " +
                    "(design.md CM-7); adding a type is a code change with review, not a data value."), null);
            }

            if (ruleType != RuleType.Existence)
            {
                // Threshold/Switch: closed-set members but no schema authored yet (task 020 scope). Fail
                // closed rather than call RuleBodySchemaValidator.Validate and let its NotSupportedException
                // (by design, for this known-but-unimplemented case) propagate.
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.RuleTypeUnsupported,
                    $"sprk_ruletype '{ruleType}' has no schema authored yet, so its sprk_rulebody cannot be " +
                    "validated (task 020 scope: Existence only)."), null);
            }

            // ruleType is Existence here, so RuleBodySchemaValidator.Validate never throws NotSupportedException
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
                compiled = _compiler.Compile(ruleBodyJson!, subjectId);
            }
            catch (PredicateCompilationException ex)
            {
                // The compiler enforces strictly MORE than the schema (verified joins, global-readable
                // entities, no cross-clause '$' references, Dataverse link-entity/body-length/in-list limits)
                // -- a body that is schema-valid but not compilable is still an invalid policy version.
                return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.CompileRefused, ex.Message), null);
            }

            if (!string.IsNullOrWhiteSpace(messageTemplate))
            {
                if (SignalWriter.HasMalformedPlaceholder(messageTemplate))
                {
                    return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateMalformed,
                        "sprk_messagetemplate contains a malformed {{...}} placeholder (unbalanced braces, an " +
                        "empty token, or a character outside [A-Za-z0-9_.] in the field name) that would " +
                        "otherwise render as literal text."), null);
                }

                var tokens = SignalWriter.ExtractTemplateTokens(messageTemplate);
                var outsideReadSet = tokens.Where(t => !compiled.PositiveReadFields.Contains(t)).ToArray();
                if (outsideReadSet.Length > 0)
                {
                    var ambiguous = outsideReadSet.Where(t => compiled.AmbiguousPositiveFields.Contains(t)).ToArray();
                    var unknown = outsideReadSet.Except(ambiguous).ToArray();
                    var detail = new List<string>();
                    if (unknown.Length > 0)
                    {
                        detail.Add("not read by the subject's own 'when' filter or any 'exists' clause (a " +
                                   $"'notExists' clause's fields can never be read for a firing subject): [{string.Join(", ", unknown)}]");
                    }

                    if (ambiguous.Length > 0)
                    {
                        detail.Add("ambiguous -- read by more than one distinct positive entity, so the value " +
                                   $"is not well defined: [{string.Join(", ", ambiguous)}]");
                    }

                    return (PolicyVersionValidationResult.Failure(OntologyWriterFailureReason.TemplateTokenOutsideReadSet,
                        "sprk_messagetemplate references {{...}} field(s) " + string.Join("; ", detail) +
                        ". Section 0.3 is binding: the sentence may assert only what the predicate actually " +
                        "read for the firing subject, from a positive ('when' / 'exists') clause."), null);
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
}
