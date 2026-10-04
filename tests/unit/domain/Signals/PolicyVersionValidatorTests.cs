using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // reuse CapturingLogger<T>/LogEntry (internal, same assembly)
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Unit tests for <see cref="PolicyVersionValidator"/> (task 022; spec FR-08; owner decision 2026-10-03/04 —
/// validate at EVALUATION time too, fail closed, because an admin can author a policy row directly in the
/// Spaarke Platform app, bypassing the BFF). Rewritten 2026-10-04 after an independent review returned FAIL
/// (findings #1-#10) and the coordinator settled two design questions (F5, F6) — see
/// <c>notes/022-progress.md</c> and this task's POML <c>&lt;rework&gt;</c> element for the fixed/rejected
/// disposition of every finding.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure logic over <see cref="RuleBodySchemaValidator"/>
/// and <see cref="PredicateCompiler"/> (both already maintain-class), plus the observability contract on
/// <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/> — a silent failure here means an admin-authored
/// invalid policy version evaluates as though it does not exist, with NOTHING surfacing that fact (the exact
/// failure mode the owner's no-silent-failure directive targets).
/// </remarks>
[Trait("status", "new")]
public class PolicyVersionValidatorTests
{
    private static readonly Guid PolicyVersionId = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
    private const string PolicyCode = "POL-COMMIT-BUDGET";

    // The live Path B body (notes/004-seed-policy-rows.md). Positive fields (subject "when" + "exists" clause):
    // sprk_triagecategory, sprk_receiveddate, sprk_reviewoutcome. Negative (notExists-only) field: sprk_revisedon.
    private const string ValidExistenceBody = """
        {
          "type": "Existence",
          "subject": "sprk_matter",
          "when": {},
          "all": [
            {
              "exists": "sprk_communication",
              "path": "sprk_regardingmatter",
              "filter": {
                "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d", "8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],
                "sprk_receiveddate": { ">=": "now-30d" },
                "sprk_reviewoutcome": { "<>": 100000003 }
              }
            },
            {
              "notExists": "sprk_budgetrevision",
              "path": "sprk_matter",
              "filter": { "sprk_revisedon": { ">=": "now-30d" } }
            }
          ]
        }
        """;

    private static PolicyVersionValidator Sut(ILogger<PolicyVersionValidator>? logger = null) =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            logger ?? new CapturingLogger<PolicyVersionValidator>());

    private static PolicyVersionSnapshot Snapshot(string? ruleType, string? body, string? template = null) =>
        new(PolicyVersionId, PolicyCode, ruleType, body, template);

    // =====================================================================================
    // ValidateForSave() -- pure, save-time shape (acceptance criteria 1-4; review finding #6 rename)
    // =====================================================================================

    [Fact]
    public void ValidateForSave_ValidExistenceBody_NoTemplate_ReturnsSuccess()
    {
        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, messageTemplate: null);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void ValidateForSave_ValidExistenceBody_TemplateReferencingOnlyPositiveFields_ReturnsSuccess()
    {
        const string template = "A communication was received {{sprk_receiveddate}}.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateForSave_MalformedJson_IsRefusedWithFieldLevelError_ReasonSchemaInvalid()
    {
        var result = Sut().ValidateForSave("Existence", "{ not valid json", messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().ContainSingle(e => e.Contains("JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateForSave_ValidJsonFailingExistenceSchema_IsRefusedNamingTheOffendingClause_ReasonSchemaInvalid()
    {
        // Cross-clause variable binding -- design.md section 8.0.1(a)'s own rejected example (schema-level,
        // additionalProperties:false on the clause definition rejects the unexpected "bind" property).
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_triagecategory": ["x"] }, "bind": "commitment" },
                { "notExists": "sprk_budgetrevision", "path": "sprk_matter", "filter": { "sprk_revisedon": { ">": "$commitment.sprk_receiveddate" } } }
              ]
            }
            """;

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().OnlyContain(e => e.Contains(':', StringComparison.Ordinal), "each field-level error names the offending JSON Pointer location, not a bare boolean");
    }

    [Fact]
    public void ValidateForSave_BodyShapeDoesNotMatchDeclaredRuleType_IsRefused_ReasonSchemaInvalid()
    {
        // sprk_ruletype column says Existence, but the body's own "type" discriminator says Threshold --
        // the schema's "type": {"const": "Existence"} refuses this at the schema level.
        const string body = """
            {
              "type": "Threshold",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "a": "b" } }
              ]
            }
            """;

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    [Fact]
    public void ValidateForSave_SchemaValidButCompilerRefuses_IsStillInvalid_ReasonCompileRefused()
    {
        // Schema-valid (passes RuleBodySchemaValidator) but names an unverified join -- PredicateCompiler
        // enforces strictly MORE than the schema (verified joins, task 021). A policy version this broken
        // would never produce a usable predicate, so it must be refused here too, not only at evaluation time.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingbudget", "filter": { "a": "b" } }
              ]
            }
            """;

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.CompileRefused);
        result.Errors.Should().ContainSingle(e => e.Contains("verified", StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================================
    // Task 022 rework, review finding #1: pathological-but-syntactically-valid bodies must never throw,
    // end-to-end through the validator (RuleBodySchemaValidatorTests covers the same inputs in isolation).
    // =====================================================================================

    [Fact]
    public void ValidateForSave_DuplicateTopLevelKey_IsRefused_NeverThrows_ReasonSchemaInvalid()
    {
        const string body = """{"type":"Existence","subject":"sprk_matter","subject":"sprk_communication","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}]}""";

        var act = () => Sut().ValidateForSave("Existence", body, messageTemplate: null);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    [Fact]
    public void ValidateForSave_ExtremeNumericLiteral_IsRefused_NeverThrows_ReasonSchemaInvalid()
    {
        const string body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_amount":1e999999}}]}""";

        var act = () => Sut().ValidateForSave("Existence", body, messageTemplate: null);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateForSave_NullOrWhitespaceBody_IsRefused_NeverThrows(string? body)
    {
        var act = () => Sut().ValidateForSave("Existence", body, messageTemplate: null);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    // =====================================================================================
    // F5 (coordinator decision): the section 0.3 template vocabulary is POSITIVE fields only -- subject "when"
    // + "exists" clauses. NEVER a "notExists" clause (fabricated-value risk). Ambiguous fields refused too.
    // =====================================================================================

    [Fact]
    public void ValidateForSave_TemplateReferencingNotExistsOnlyField_IsRefused_ReasonTemplateTokenOutsideReadSet()
    {
        // sprk_revisedon is read ONLY inside the notExists clause -- a firing subject has no such row, so this
        // value can never be read for it. (Previously accepted before the F5 design decision; must now refuse.)
        const string template = "No revision since {{sprk_revisedon}}.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("sprk_revisedon", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSave_TemplateReferencingUnknownField_IsRefused_ReasonTemplateTokenOutsideReadSet()
    {
        const string template = "Flagged by {{sprk_nonexistentfield}}.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("sprk_nonexistentfield", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSave_TemplateReferencingAmbiguousPositiveField_IsRefused_ReasonTemplateTokenOutsideReadSet()
    {
        // "sprk_name" is read by BOTH the subject's own "when" filter (entity sprk_matter) AND an "exists"
        // clause's filter (entity sprk_communication) -- two distinct positive entities, so the value is
        // not well defined.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "when": { "sprk_name": "whatever" },
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_name": "x" } }
              ]
            }
            """;
        const string template = "Named {{sprk_name}}.";

        var result = Sut().ValidateForSave("Existence", body, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
            && e.Contains("sprk_name", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSave_BlankMessageTemplate_SkipsTheCheck_ReturnsSuccess()
    {
        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, messageTemplate: "   ");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateForSave_MessageTemplateWithZeroPlaceholders_ReturnsSuccess()
    {
        // mvp-technical-spec.md's own corrected message (notes/004-seed-policy-rows.md): a static sentence
        // with zero field placeholders holds vacuously against any read set.
        const string template = "A commitment with financial consequence was raised in the last 30 days and " +
                                 "the budget has not been revised in that period.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeTrue();
    }

    // =====================================================================================
    // Review finding #3: malformed {{...}} placeholders must be refused, not render literally.
    // =====================================================================================

    [Theory]
    [InlineData("Flagged by {{sprk-x}}.")]           // disallowed character (hyphen)
    [InlineData("Flagged by {{a b}}.")]              // embedded space
    [InlineData("Flagged by {{}}.")]                 // empty token
    [InlineData("Flagged by {{x")]                   // unclosed
    [InlineData("Flagged by {{{sprk_receiveddate}}}")] // extra brace around an otherwise-valid token
    public void ValidateForSave_MalformedPlaceholder_IsRefused_ReasonTemplateMalformed(string template)
    {
        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateMalformed);
    }

    // Review finding #11: uppercase and dotted token names are WELL-FORMED (matched by the token regex) but
    // fail the positive-read-set check (Ordinal, case-sensitive, flat/un-dotted field names) -- a DIFFERENT
    // reason than "malformed".
    [Fact]
    public void ValidateForSave_UppercaseTokenName_IsRefused_ReasonOutsideReadSet_NotMalformed()
    {
        const string template = "Received {{SPRK_RECEIVEDDATE}}.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
    }

    [Fact]
    public void ValidateForSave_DottedTokenName_IsRefused_ReasonOutsideReadSet_NotMalformed()
    {
        const string template = "Received {{sprk_communication.sprk_receiveddate}}.";

        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
    }

    // =====================================================================================
    // Closed rule-type set + review findings #7/#8/#11: exact-name/numeric parsing, bounded reasons.
    // =====================================================================================

    [Theory]
    [InlineData("Threshold")]
    [InlineData("Switch")]
    [InlineData("100000000")] // Threshold's numeric option-set value
    public void ValidateForSave_RuleTypeWithNoAuthoredSchemaYet_IsRefused_ReasonRuleTypeUnsupported_NeverThrows(string ruleTypeRaw)
    {
        var act = () => Sut().ValidateForSave(ruleTypeRaw, "{}", messageTemplate: null);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("NotARealRuleType")]
    [InlineData("Threshold,Switch")] // review finding #8: comma-combining must not parse as a valid type
    public void ValidateForSave_UnknownOrCombinedRuleType_IsRefused_ReasonRuleTypeUnsupported(string? ruleTypeRaw)
    {
        var result = Sut().ValidateForSave(ruleTypeRaw, ValidExistenceBody, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
    }

    [Fact]
    public void ValidateForSave_NumericExistenceRuleType_ReturnsSuccess()
    {
        // 100000002 is Existence's Dataverse option-set numeric value.
        var result = Sut().ValidateForSave("100000002", ValidExistenceBody, messageTemplate: null);

        result.IsValid.Should().BeTrue();
    }

    // =====================================================================================
    // TryPrepareForEvaluation() -- the ONLY path to a CompiledPredicate (F6); the fail-closed evaluation-time
    // gate (task 022 owner decision; task 031's seam).
    // =====================================================================================

    [Fact]
    public void TryPrepareForEvaluation_ValidBody_ReturnsTrue_OutputsCompiledPredicate_LogsNothing()
    {
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        var ok = sut.TryPrepareForEvaluation(Snapshot("Existence", ValidExistenceBody), subjectId: null, out var compiled);

        ok.Should().BeTrue();
        compiled.Should().NotBeNull();
        compiled!.SubjectEntity.Should().Be("sprk_matter");
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TryPrepareForEvaluation_InvalidBody_ReturnsFalse_OutputsNull_NeverThrows()
    {
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        var act = () => sut.TryPrepareForEvaluation(Snapshot("Existence", "{ not valid json"), subjectId: null, out var compiled);

        var ok = act.Should().NotThrow("an invalid policy version must not abort evaluation of every OTHER policy too").Subject;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryPrepareForEvaluation_InvalidBody_CompiledOutParamIsNull()
    {
        var sut = Sut();

        sut.TryPrepareForEvaluation(Snapshot("Existence", "{ not valid json"), subjectId: null, out var compiled);

        compiled.Should().BeNull();
    }

    // Review finding #2: the original no-leak assertion checked "not valid json" against the real text
    // "not valid JSON" -- a tautology that would have passed even with a real leak, since the substring it
    // checked for never appeared either way. Fixed: assert the EXACT structured field set and the EXACT
    // rendered message, so any additional interpolated content (a future accidental leak) breaks this test.
    [Fact]
    public void TryPrepareForEvaluation_InvalidBody_LogsExactlyTheAllowedStructuredFields_NoBodyOrTemplateLeak()
    {
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        sut.TryPrepareForEvaluation(Snapshot("Existence", "{ not valid json"), subjectId: null, out _);

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        errorEntry.Fields.Keys.Should().BeEquivalentTo(new[] { "PolicyVersionId", "PolicyCode", "Reason", "{OriginalFormat}" });

        var expectedMessage =
            $"Policy version {PolicyVersionId} ({PolicyCode}) failed rule-body validation at evaluation time; " +
            $"producing no Signal for it and continuing with the other policies (reason={OntologyWriterFailureReason.SchemaInvalid}).";
        errorEntry.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public void TryPrepareForEvaluation_TemplateFailure_AlsoLogsAndMeters()
    {
        // Review finding #11: the TEMPLATE-failure path's log, not only the schema-failure path's.
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        sut.TryPrepareForEvaluation(
            Snapshot("Existence", ValidExistenceBody, "Flagged by {{sprk-x}}."), subjectId: null, out var compiled);

        compiled.Should().BeNull();
        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.TemplateMalformed);
    }

    [Theory]
    [InlineData("Threshold", null, null, OntologyWriterFailureReason.RuleTypeUnsupported)]
    [InlineData("Existence", "{ not valid json", null, OntologyWriterFailureReason.SchemaInvalid)]
    public void TryPrepareForEvaluation_InvalidBody_RecordsExactReasonTagOnTheMetric(
        string ruleType, string? body, string? template, string expectedReason)
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;

        Sut().TryPrepareForEvaluation(Snapshot(ruleType, body, template), subjectId: null, out _);

        reasons().Should().Equal(expectedReason);
    }

    [Fact]
    public void TryPrepareForEvaluation_SchemaInvalidBody_RecordsCompileRefusedReasonTag()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;

        const string body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingbudget","filter":{"a":"b"}}]}""";
        Sut().TryPrepareForEvaluation(Snapshot("Existence", body), subjectId: null, out _);

        reasons().Should().Equal(OntologyWriterFailureReason.CompileRefused);
    }

    [Fact]
    public void TryPrepareForEvaluation_AmbiguousTemplateField_RecordsTemplateTokenOutsideReadSetReasonTag()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;

        Sut().TryPrepareForEvaluation(
            Snapshot("Existence", ValidExistenceBody, "No revision since {{sprk_revisedon}}."), subjectId: null, out _);

        reasons().Should().Equal(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
    }

    [Fact]
    public void TryPrepareForEvaluation_ThresholdRuleType_ReturnsFalse_NeverThrows()
    {
        // Defense in depth: a Threshold policy version (no schema authored yet, task 020 scope) must not
        // crash the evaluator -- it is simply an invalid policy version like any other.
        var sut = Sut();

        var act = () => sut.TryPrepareForEvaluation(Snapshot("Threshold", "{}"), subjectId: null, out var compiled);

        var ok = act.Should().NotThrow().Subject;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryPrepareForEvaluation_NullSnapshot_Throws()
    {
        // The ONE place a contract violation (a null snapshot, a programming error, not an authored-content
        // problem) still throws -- ArgumentNullException is the standard .NET contract for this, not a
        // policy-validation refusal.
        var sut = Sut();

        var act = () => sut.TryPrepareForEvaluation(null!, subjectId: null, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    // =====================================================================================
    // Metric scaffolding — same AsyncLocal-scoped MeterListener pattern as SignalWriterTests
    // (OntologyWriterTelemetry's Meter/Counter are process-global statics). Captures the REASON tag value,
    // not only the count, closing review finding #7's "assert the metric's reason tag in tests".
    // =====================================================================================

    private static readonly AsyncLocal<Guid> PolicyInvalidMetricScope = new();

    private static (MeterListener Listener, Func<IReadOnlyList<string>> Reasons) ListenPolicyInvalidReasonsScoped(Guid scope)
    {
        var reasons = new List<string>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.policy.invalid")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (PolicyInvalidMetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string reason)
                {
                    lock (reasons)
                    {
                        reasons.Add(reason);
                    }
                }
            }
        });
        listener.Start();
        return (listener, () =>
        {
            lock (reasons)
            {
                return reasons.ToArray();
            }
        });
    }
}
