using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // reuse CapturingLogger<T>/LogEntry (internal, same assembly)
using Sprk.Bff.Api.Tests.Services.Signals; // RuleBodySchemaValidatorTests.PathologicalBodies (shared theory rows)
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

    // The live Path B body (notes/004-seed-policy-rows.md). Its exists clause reads sprk_triagecategory (a
    // TWO-value 'in'), sprk_receiveddate (a range) and sprk_reviewoutcome ('<>') -- none pins its field to one
    // value, so (finding F3) NONE is template-eligible. Negative (notExists-only) field: sprk_revisedon.
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

    // A body whose template-eligible fields are known exactly: sprk_mattertype (subject 'when', range-filtered --
    // still eligible, the subject is one row), sprk_triagecategory (exists, pinned by a ONE-element 'in'),
    // sprk_direction (exists, pinned by a bare scalar), sprk_channel (exists, pinned by {"=": v}). Not eligible:
    // sprk_receiveddate (exists, range). Not positive at all: sprk_revisedon (notExists). Column names are
    // illustrative: the validator checks the body's SHAPE and read set, never Dataverse column existence, so
    // nothing here pins a schema fact (ADR-038 / spec NFR-05(i) does not apply).
    private const string PinnedExistenceBody = """
        {
          "type": "Existence",
          "subject": "sprk_matter",
          "when": { "sprk_mattertype": { ">=": 100000001 } },
          "all": [
            {
              "exists": "sprk_communication",
              "path": "sprk_regardingmatter",
              "filter": {
                "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d"],
                "sprk_direction": 100000000,
                "sprk_channel": { "=": 100000002 },
                "sprk_receiveddate": { ">=": "now-30d" }
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

    [Fact]
    public void ValidateForSave_TemplateReferencingOnlyEligibleFields_ReturnsSuccess()
    {
        const string template =
            "Matter type {{sprk_mattertype}}: a {{sprk_triagecategory}} communication ({{sprk_direction}}, " +
            "{{sprk_channel}}) was received and the budget has not been revised.";

        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, template);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    // =====================================================================================
    // Rework round 2, finding F3 (coordinator decision): an exists-clause field is template-eligible ONLY if the
    // clause pins it to exactly one value; a subject 'when' field is eligible whatever its operator.
    // =====================================================================================

    [Fact]
    public void ValidateForSave_TemplateReferencingRangeFilteredExistsField_IsRefused_AsNotPinned()
    {
        // sprk_receiveddate >= now-30d can match many communications with many dates -- "the" date is undefined.
        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, "Received {{sprk_receiveddate}}.");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("does not pin it to exactly one value", StringComparison.Ordinal)
                                                  && e.Contains("sprk_receiveddate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sprk_triagecategory")] // 'in' with two values (live Path B body)
    [InlineData("sprk_reviewoutcome")]  // '<>' (live Path B body)
    public void ValidateForSave_TemplateReferencingMultiValueOrNotEqualExistsField_IsRefused(string field)
    {
        var result = Sut().ValidateForSave("Existence", ValidExistenceBody, "Saw {{" + field + "}}.");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("does not pin it to exactly one value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sprk_triagecategory")] // one-element 'in'
    [InlineData("sprk_direction")]      // bare scalar (eq)
    [InlineData("sprk_channel")]        // {"=": v}
    public void ValidateForSave_TemplateReferencingEqPinnedExistsField_IsAccepted(string field)
    {
        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, "Saw {{" + field + "}}.");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForSave_TemplateReferencingWhenFieldWithRangeFilter_IsAccepted()
    {
        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, "Matter type {{sprk_mattertype}}.");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void ValidateForSave_TemplateReferencingFieldPinnedToDifferentValuesByTwoClauses_IsRefused_AsAmbiguous()
    {
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": 100000000 } },
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_direction": 100000001 } }
              ]
            }
            """;

        var result = Sut().ValidateForSave("Existence", body, "Direction {{sprk_direction}}.");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        result.Errors.Should().ContainSingle(e => e.Contains("ambiguous", StringComparison.Ordinal)
                                                  && e.Contains("sprk_direction", StringComparison.Ordinal));
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
        // Finding F6: assert the REAL JSON Pointer locations, not merely that each error contains a ':' --
        // clause 0 is refused for its "bind" property, clause 1 for its "$"-prefixed value.
        result.Errors.Should().Contain(e => e.StartsWith("/all/0", StringComparison.Ordinal),
            "the 'bind' property makes clause 0 the offending clause");
        result.Errors.Should().Contain(e => e.StartsWith("/all/1/filter/sprk_revisedon", StringComparison.Ordinal),
            "the '$commitment...' dereference makes clause 1's filter value the offending location");
    }

    // =====================================================================================
    // Rework round 2, finding F2: the length cap runs BEFORE any parse or schema evaluation.
    // =====================================================================================

    [Fact]
    public void ValidateForSave_OversizedBodyThatIsAlsoInvalidJson_IsRefusedForSize_NotParsed()
    {
        // Invalid JSON AND over the cap: if anything parsed it first, the reason would be schema_invalid with a
        // "not valid JSON" error. Getting the size reason proves the cap ran before any parse/schema evaluation.
        var body = "{ not valid json " + new string('x', RuleBodySchemaValidator.MaxRuleBodyLength);

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleBodyTooLarge);
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(RuleBodySchemaValidator.RuleBodyTooLongMessage(body.Length));
    }

    [Fact]
    public void ValidateForSave_BodyExactlyAtTheCap_IsNotRefusedForSize()
    {
        // Boundary: the cap is "longer than", not "at least". Padding with whitespace keeps the body valid.
        var body = ValidExistenceBody.PadRight(RuleBodySchemaValidator.MaxRuleBodyLength);

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    // =====================================================================================
    // Rework round 2, finding F7: 'when' values obey the same per-value rules as clause filter values.
    // =====================================================================================

    [Theory]
    [InlineData("""{"sprk_amount":1e999999}""")]
    [InlineData("""{"sprk_name":"$commitment.x"}""")]
    [InlineData("""{"sprk_name":{"between":[1,2]}}""")]
    public void ValidateForSave_WhenValueBreakingTheFilterValueGrammar_IsRefused_ReasonSchemaInvalid(string when)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","when":""" + when +
                   ""","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":1}}]}""";

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    // =====================================================================================
    // Rework round 2, finding F8: a message template inside the rule body is refused (single source:
    // sprk_messagetemplate). It was accepted unchecked before -- e.g. one naming a field the body never reads.
    // =====================================================================================

    [Theory]
    [InlineData("""{"messageTemplate":"Unreconciled against its budget {{sprk_budgetamount}}"}""", "/then/messageTemplate")]
    [InlineData("""{"MessageTemplate":"x"}""", "/then/MessageTemplate")]
    [InlineData("""{"evidenceRefs":[{"messageTemplate":"x"}]}""", "/then/evidenceRefs/0/messageTemplate")]
    public void ValidateForSave_MessageTemplateInsideRuleBody_IsRefused_NamingItsLocation(string then, string pointer)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":1}}],"then":""" +
                   then + "}";

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().ContainSingle(e => e.StartsWith(pointer + ":", StringComparison.Ordinal)
                                                  && e.Contains("sprk_messagetemplate", StringComparison.Ordinal));
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

    // Rows: duplicate key, 1e999999, and (round 3, finding M1) unpaired UTF-16 surrogate escapes in property
    // names and in a value. Before M1 the property-name rows were contained only by the catch-all and came back
    // as internal_error; they must be schema_invalid.
    [Theory]
    [MemberData(nameof(RuleBodySchemaValidatorTests.PathologicalBodies), MemberType = typeof(RuleBodySchemaValidatorTests))]
    public void ValidateForSave_PathologicalBody_IsRefused_NeverThrows_ReasonSchemaInvalid(string body)
    {
        var act = () => Sut().ValidateForSave("Existence", body, messageTemplate: null);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    [Fact]
    public void TryPrepareForEvaluation_UnpairedSurrogateInPropertyName_RecordsSchemaInvalid_NotInternalError()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        const string body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_\ud800":1}}]}""";

        Sut().TryPrepareForEvaluation(Snapshot("Existence", body), subjectId: null, out _);

        reasons().Should().Equal(OntologyWriterFailureReason.SchemaInvalid);
    }

    // =====================================================================================
    // Round 3, finding L2: 'then' is reserved -- refused end-to-end as schema_invalid.
    // =====================================================================================

    [Fact]
    public void ValidateForSave_ThenProperty_IsRefusedAsReserved_ReasonSchemaInvalid()
    {
        const string body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":1}}],"then":{"signalType":100000003}}""";

        var result = Sut().ValidateForSave("Existence", body, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("/then: 'then' is reserved");
    }

    // =====================================================================================
    // Round 3, findings L3 + L5: pins compare by VALUE; two clauses pinning the SAME value stay eligible.
    // =====================================================================================

    [Theory]
    [InlineData("100000000", "100000000")] // identical literal
    [InlineData("1", "1.0")]               // L3: same decimal value, different JSON spelling
    [InlineData("100", "1e2")]
    public void ValidateForSave_TemplateReferencingFieldPinnedToTheSameValueByTwoClauses_IsAccepted(string first, string second)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","all":[""" +
                   """{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":""" + first + "}}," +
                   """{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":{"=":""" + second + "}}}]}";

        var result = Sut().ValidateForSave("Existence", body, "Direction {{sprk_direction}}.");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
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
    [InlineData("Flagged (see {policy}).")]           // F12: a single literal brace in prose -- braces are reserved
    [InlineData("Flagged by \uFF5B\uFF5Bsprk_direction\uFF5D\uFF5D.")] // F12: full-width braces
    [InlineData("Flagged by {{sprk_direction}\uFF5D.")] // F12: mixed ASCII/full-width close
    public void ValidateForSave_MalformedPlaceholder_IsRefused_ReasonTemplateMalformed(string template)
    {
        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, template);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.TemplateMalformed);
    }

    [Fact]
    public void ValidateForSave_MalformedPlaceholderRefusal_StatesTheReservedBraceAuthoringRule()
    {
        // F12: the rule is documented where the author meets it -- in the refusal itself.
        var result = Sut().ValidateForSave("Existence", PinnedExistenceBody, "Flagged (see {policy}).");

        result.Errors.Should().ContainSingle(e => e.Contains("reserved for {{field}} placeholders", StringComparison.Ordinal)
                                                  && e.Contains("(see {policy})", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(" +100000002 ")] // F13: whitespace + sign were accepted by the default int.TryParse
    [InlineData("+100000002")]
    [InlineData("100000002 ")]
    public void ValidateForSave_NumericRuleTypeWithSignOrWhitespace_IsRefused_ReasonRuleTypeUnsupported(string ruleTypeRaw)
    {
        var result = Sut().ValidateForSave(ruleTypeRaw, ValidExistenceBody, messageTemplate: null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
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
    // Round 3, finding M2: the body AND the template are distinctive and non-null, so a leak of either into the
    // log would change the rendered message or add a field -- the exact-message + exact-keys asserts then fail.
    // (Round 2 used a null template, so a template leak could not have been detected.) One row per refusal
    // stage: a body refusal (schema_invalid) and a TEMPLATE refusal (template_malformed /
    // template_token_outside_read_set), where the template is the very thing being refused.
    [Theory]
    [InlineData("{ SECRET-BODY not valid json", "SECRET-TEMPLATE {{sprk_direction}}", OntologyWriterFailureReason.SchemaInvalid)]
    [InlineData(null, "SECRET-TEMPLATE {{sprk-x}}", OntologyWriterFailureReason.TemplateMalformed)]
    [InlineData(null, "SECRET-TEMPLATE {{sprk_receiveddate}}", OntologyWriterFailureReason.TemplateTokenOutsideReadSet)]
    public void TryPrepareForEvaluation_Refusal_LogsExactlyTheAllowedStructuredFields_NoBodyOrTemplateLeak(
        string? body, string template, string expectedReason)
    {
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        sut.TryPrepareForEvaluation(Snapshot("Existence", body ?? PinnedExistenceBody, template), subjectId: null, out _);

        var errorEntry = logger.Entries.Should().ContainSingle().Subject;
        errorEntry.Level.Should().Be(LogLevel.Error);
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        errorEntry.Fields.Keys.Should().BeEquivalentTo(new[] { "PolicyVersionId", "PolicyCode", "Reason", "{OriginalFormat}" });
        errorEntry.Field("Reason").Should().Be(expectedReason);

        var expectedMessage =
            $"Policy version {PolicyVersionId} ({PolicyCode}) failed rule-body validation at evaluation time; " +
            $"producing no Signal for it and continuing with the other policies (reason={expectedReason}).";
        errorEntry.Message.Should().Be(expectedMessage);
        errorEntry.Message.Should().NotContain("SECRET");
    }

    [Fact]
    public void TryPrepareForEvaluation_TemplateFailure_AlsoLogsAndMeters()
    {
        // Review finding #11: the TEMPLATE-failure path's log, not only the schema-failure path's. Round 3,
        // finding M3: and its METRIC reason, not only the log.
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        sut.TryPrepareForEvaluation(
            Snapshot("Existence", ValidExistenceBody, "Flagged by {{sprk-x}}."), subjectId: null, out var compiled);

        compiled.Should().BeNull();
        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.TemplateMalformed);
        reasons().Should().Equal(OntologyWriterFailureReason.TemplateMalformed);
    }

    // Round 3, finding M3: the internal_error path's metric + log. After M1 no known authored input reaches it,
    // so it is forced through a seam that already exists -- PredicateCompiler's injected TimeProvider -- throwing
    // an exception type nothing in the validator names. No production seam was added for this.
    [Fact]
    public void TryPrepareForEvaluation_UnexpectedExceptionInsideValidation_RecordsInternalError_LogsOnlyTheAllowedFields()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = new PolicyVersionValidator(
            new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new ThrowingTimeProvider()),
            logger);

        var act = () => sut.TryPrepareForEvaluation(Snapshot("Existence", ValidExistenceBody), subjectId: null, out _);

        act.Should().NotThrow().Subject.Should().BeFalse();
        reasons().Should().Equal(OntologyWriterFailureReason.InternalError);
        var errorEntry = logger.Entries.Should().ContainSingle().Subject;
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.InternalError);
        errorEntry.Fields.Keys.Should().BeEquivalentTo(new[] { "PolicyVersionId", "PolicyCode", "Reason", "{OriginalFormat}" });
        errorEntry.Message.Should().NotContain(ThrowingTimeProvider.Marker, "the exception's own message is never logged");
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public const string Marker = "SECRET-EXCEPTION-TEXT";

        public override DateTimeOffset GetUtcNow() => throw new TimeProviderFailureException(Marker);
    }

    private sealed class TimeProviderFailureException(string message) : Exception(message);

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
    public void TryPrepareForEvaluation_OversizedBody_RecordsRuleBodyTooLargeReasonTag()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        var body = new string(' ', RuleBodySchemaValidator.MaxRuleBodyLength + 1);

        Sut().TryPrepareForEvaluation(Snapshot("Existence", body), subjectId: null, out _);

        reasons().Should().Equal(OntologyWriterFailureReason.RuleBodyTooLarge);
    }

    // Finding F1: Guid.Empty is a CALLER bug. It must throw, and must never be logged or metered as an invalid
    // POLICY -- before the fix it reached the compiler, came back as compile_refused, and logged EventId 50301
    // against a valid policy version.
    [Fact]
    public void TryPrepareForEvaluation_EmptySubjectId_ThrowsArgumentException_AndLogsAndMetersNothing()
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        var act = () => sut.TryPrepareForEvaluation(Snapshot("Existence", ValidExistenceBody), Guid.Empty, out _);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("subjectId");
        logger.Entries.Should().BeEmpty("a caller bug is not an invalid policy version");
        reasons().Should().BeEmpty("ontology.policy.invalid counts invalid POLICIES, not caller bugs");
    }

    [Fact]
    public void TryPrepareForEvaluation_UnverifiedJoin_RecordsCompileRefusedReasonTag()
    {
        // Schema-VALID body whose clause names an unverified join (sprk_regardingbudget) -- refused by the compiler.
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;

        const string body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingbudget","filter":{"a":"b"}}]}""";
        Sut().TryPrepareForEvaluation(Snapshot("Existence", body), subjectId: null, out _);

        reasons().Should().Equal(OntologyWriterFailureReason.CompileRefused);
    }

    [Fact]
    public void TryPrepareForEvaluation_NotExistsOnlyTemplateField_RecordsTemplateTokenOutsideReadSetReasonTag()
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

    // =====================================================================================
    // Task 024 (D-16): the Do-lane grammar passes the save gate and the evaluation gate; the refusals it did not
    // widen are still refused at BOTH.
    // =====================================================================================

    private const string OverdueEventBody = """
        {"type":"Existence","subject":"sprk_event",
         "when":{"statuscode":659490001,"sprk_duedate":{"<":"now-5d"}},"all":[]}
        """;

    [Fact]
    public void ValidateForSave_SubjectOnlyEventBody_ReturnsSuccess_AndEvaluationGetsOneFetchWithNoLinkEntity()
    {
        var sut = Sut();

        sut.ValidateForSave("Existence", OverdueEventBody, "Task overdue since {{sprk_duedate}}.").IsValid.Should().BeTrue();

        sut.TryPrepareForEvaluation(Snapshot("Existence", OverdueEventBody), subjectId: null, out var compiled).Should().BeTrue();
        compiled!.FetchXml.Should().NotContain("link-entity");
        compiled.QuietWindowDays.Should().Be(PredicateCompiler.DefaultQuietWindowDays);
        compiled.DateOnlyWhenFields.Should().Equal("sprk_duedate");
    }

    [Theory]
    // a clause whose join is not in VerifiedJoins (D-16 adds none)
    [InlineData("""{"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[{"exists":"sprk_communication","path":"sprk_regardingevent","filter":{"sprk_direction":1}}]}""")]
    // a notExists over a table the writer reads below org-wide depth (task 030 finding (b))
    [InlineData("""{"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[{"notExists":"sprk_document","path":"sprk_regardingevent","filter":{"statecode":0}}]}""")]
    public void DoLaneSubject_UnverifiedJoinOrNotExistsOverBasicDepthTable_IsRefusedAtSaveAndAtEvaluation(string body)
    {
        var scope = Guid.NewGuid();
        PolicyInvalidMetricScope.Value = scope;
        var (listener, reasons) = ListenPolicyInvalidReasonsScoped(scope);
        using var listenerScope = listener;
        var logger = new CapturingLogger<PolicyVersionValidator>();
        var sut = Sut(logger);

        sut.ValidateForSave("Existence", body, null).Reason.Should().Be(OntologyWriterFailureReason.CompileRefused);

        sut.TryPrepareForEvaluation(Snapshot("Existence", body), subjectId: null, out var compiled).Should().BeFalse();
        compiled.Should().BeNull();
        reasons().Should().Equal(OntologyWriterFailureReason.CompileRefused);
        logger.Entries.Should().ContainSingle(e => e.EventId.Id == OntologyWriterEvents.PolicyVersionInvalid.Id);
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
