using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Unit tests for <see cref="RuleBodySchemaValidator"/> and the closed <see cref="RuleType"/> set
/// (spec FR-05, FR-08; design.md CM-7 / section 8.0.1(a); task 020 acceptance criteria).
/// </summary>
/// <remarks>
/// Pure domain/validation logic — no I/O, no Dataverse, no mocks. Per tests/CLAUDE.md this is maintain-class:
/// deleting it would let either (a) the project's one differentiated predicate become unsavable again
/// (criterion 1), or (b) the CM-3 cross-clause-variable violation design.md section 8.0.1(a) explicitly
/// rejects slip past schema validation into review-only enforcement (criterion 2) — the exact failure mode
/// the task's escalation trigger exists to prevent.
/// </remarks>
[Trait("status", "repaired")]
public class RuleBodySchemaValidatorTests
{
    private readonly RuleBodySchemaValidator _sut = new();

    // The adopted Existence body shape from design.md section 8.0.1(a) / mvp-technical-spec.md section 3.4a:
    // two independent ANDed clauses over a fixed window, no cross-clause binding.
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

    // The REJECTED proposal design.md section 8.0.1(a) names explicitly: clause 1 binds "commitment",
    // clause 2 dereferences "$commitment.sprk_receiveddate" -- not one Dataverse filter (breaks CM-3).
    private const string CrossClauseVariableBoundBody = """
        {
          "type": "Existence",
          "subject": "sprk_matter",
          "when": {},
          "all": [
            {
              "exists": "sprk_communication",
              "path": "sprk_regardingmatter",
              "filter": { "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d"] },
              "bind": "commitment"
            },
            {
              "notExists": "sprk_budgetrevision",
              "path": "sprk_matter",
              "filter": { "sprk_revisedon": { ">": "$commitment.sprk_receiveddate" } }
            }
          ]
        }
        """;

    [Fact]
    public void Validate_WithValidExistenceBody_ReturnsSuccess()
    {
        var result = _sut.Validate(RuleType.Existence, ValidExistenceBody);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithValidExistenceBody_BodyRoundTripsUnchanged()
    {
        // Validation is a read-only check -- it must not mutate or require reformatting the stored body.
        var before = JsonNode.Parse(ValidExistenceBody);

        var result = _sut.Validate(RuleType.Existence, ValidExistenceBody);

        var after = JsonNode.Parse(ValidExistenceBody);
        result.IsValid.Should().BeTrue();
        JsonNode.DeepEquals(before, after).Should().BeTrue();
    }

    [Fact]
    public void Validate_WithCrossClauseVariableBinding_IsRefusedWithFieldLevelError()
    {
        var result = _sut.Validate(RuleType.Existence, CrossClauseVariableBoundBody);

        result.IsValid.Should().BeFalse();
        // Field-level, not a bare "invalid": each error string is prefixed with the JSON Pointer location
        // of the offending node (the schema additionalProperties:false on the clause definition rejects
        // the unexpected "bind" property), never just a boolean.
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().OnlyContain(e => e.Contains(':', StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithDereferenceSyntaxButNoBindProperty_IsRefusedByValuePatternAlone()
    {
        // Second, independent defense: even without the "bind" declaration, a filter value starting with
        // '$' is rejected by the scalar pattern -- the dereference itself is refused, not merely its
        // declaration. Proves the refusal is not solely an additionalProperties side effect.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "sprk_triagecategory": ["x"] } },
                { "notExists": "sprk_budgetrevision", "path": "sprk_matter", "filter": { "sprk_revisedon": { ">": "$commitment.sprk_receiveddate" } } }
              ]
            }
            """;

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Transition")]   // explicitly deferred per design.md CM-7
    [InlineData("Trend")]        // explicitly deferred per design.md CM-8 (needs baselines/history)
    [InlineData("Deviation")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateRaw_WithUnknownRuleType_IsRefused(string? unknownRuleType)
    {
        var result = _sut.ValidateRaw(unknownRuleType, ValidExistenceBody);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public void ValidateRaw_WithKnownExistenceRuleTypeByName_ValidatesTheBody()
    {
        var result = _sut.ValidateRaw("Existence", ValidExistenceBody);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateRaw_WithKnownExistenceRuleTypeByDataverseOptionValue_ValidatesTheBody()
    {
        // 100000002 is the Dataverse option-set numeric value for Existence (DESCRIBE TABLE sprk_policyversion).
        var result = _sut.ValidateRaw("100000002", ValidExistenceBody);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBlankBody_IsRefused()
    {
        var result = _sut.Validate(RuleType.Existence, "   ");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithMalformedJson_IsRefused()
    {
        var result = _sut.Validate(RuleType.Existence, "{ not valid json");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithClauseHavingBothExistsAndNotExists_IsRefused()
    {
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "notExists": "sprk_budgetrevision", "path": "sprk_matter", "filter": { "a": "b" } }
              ]
            }
            """;

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithClauseHavingNeitherExistsNorNotExists_IsRefused()
    {
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "path": "sprk_matter", "filter": { "a": "b" } }
              ]
            }
            """;

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithWrongTypeDiscriminator_IsRefused()
    {
        const string body = """
            {
              "type": "Threshold",
              "subject": "sprk_matter",
              "all": [
                { "exists": "sprk_communication", "path": "sprk_regardingmatter", "filter": { "a": "b" } }
              ]
            }
            """;

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyAllWithoutWhen_IsRefused()
    {
        // Task 024: an empty 'all' is legal for a subject-only body, but only with a non-empty 'when'; without one
        // the body would match every row of the subject.
        const string body = """{ "type": "Existence", "subject": "sprk_matter", "all": [] }""";

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("when", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleType_ClosedSet_ContainsExactlyThresholdSwitchExistence()
    {
        var members = Enum.GetValues<RuleType>();

        members.Should().BeEquivalentTo(new[] { RuleType.Threshold, RuleType.Switch, RuleType.Existence });
    }

    [Fact]
    public void RuleType_Values_MatchDataverseOptionSetNumericValues()
    {
        // Pinned against DESCRIBE TABLE sprk_policyversion (2026-10-03): Threshold (100000000),
        // Switch (100000001), Existence (100000002). A mismatch here means this enum and the real
        // Dataverse choice column have drifted -- exactly the ADR-038 NFR-05(i) class of bug this
        // project's own forensics called out (a shape-only test pinning a non-existent column).
        ((int)RuleType.Threshold).Should().Be(100000000);
        ((int)RuleType.Switch).Should().Be(100000001);
        ((int)RuleType.Existence).Should().Be(100000002);
    }

    [Theory]
    [InlineData(RuleType.Threshold)]
    [InlineData(RuleType.Switch)]
    public void Validate_WithRuleTypeHavingNoAuthoredSchemaYet_ThrowsNotSupported(RuleType ruleType)
    {
        // Threshold/Switch are closed-set members but this task's scope is Existence only (spec FR-05);
        // no sprk_policyversion row of either type exists yet. The validator must fail loudly, not
        // silently accept an unvalidated body under a known-but-unimplemented type.
        var act = () => _sut.Validate(ruleType, "{}");

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(RuleType.Threshold, null)]
    [InlineData(RuleType.Switch, "{ not valid json")]
    public void Validate_WithRuleTypeHavingNoAuthoredSchemaYet_ThrowsWhateverTheBody(RuleType ruleType, string? body)
    {
        // Task 022 rework round 2, finding F9: the documented throwing contract is about the TYPE, so it must
        // not depend on the body -- previously a blank or malformed body returned a Failure for these types
        // while "{}" threw, which made the doc's "never throws for ANY input" claim false in both directions.
        var act = () => _sut.Validate(ruleType, body);

        act.Should().Throw<NotSupportedException>();
    }

    // =====================================================================================
    // Task 022 rework round 2, finding F2: the length cap runs BEFORE parse and schema evaluation (the schema
    // evaluation holds a process-wide lock; a 0.9 MB body was measured holding it ~3.7 s).
    // =====================================================================================

    [Fact]
    public void Validate_WithOversizedBodyThatIsAlsoInvalidJson_ReturnsTheSizeError_NotTheJsonError()
    {
        var body = "{ not valid json " + new string('x', RuleBodySchemaValidator.MaxRuleBodyLength);

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Equal(RuleBodySchemaValidator.RuleBodyTooLongMessage(body.Length));
    }

    // =====================================================================================
    // Task 022 rework round 2, finding F7: 'when' shares the clause filters' per-value grammar.
    // =====================================================================================

    [Theory]
    [InlineData("""{"sprk_amount":1e999999}""")]
    [InlineData("""{"sprk_name":"$commitment.x"}""")]
    [InlineData("""{"sprk_name":{"between":[1,2]}}""")]
    [InlineData("""{"sprk_name":{">=":1,"<=":2}}""")]
    public void Validate_WithWhenValueBreakingTheFilterValueGrammar_IsRefused(string when)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","when":""" + when +
                   ""","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}]}""";

        var act = () => _sut.Validate(RuleType.Existence, body);

        act.Should().NotThrow().Subject.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithEmptyWhen_StillMeansAllSubjects()
    {
        // 'when' uses filterConditions WITHOUT the clause filter's minProperties:1 -- {} stays legal.
        _sut.Validate(RuleType.Existence, ValidExistenceBody).IsValid.Should().BeTrue();
        ValidExistenceBody.Should().Contain("\"when\": {}");
    }

    // =====================================================================================
    // Task 022 rework round 2, finding F8: a message template is never accepted inside the rule body.
    // =====================================================================================

    [Theory]
    [InlineData("""{"messageTemplate":"Unreconciled against its budget {{sprk_budgetamount}}"}""", "/then/messageTemplate")]
    [InlineData("""{"MESSAGETEMPLATE":"x"}""", "/then/MESSAGETEMPLATE")]
    [InlineData("""{"proposedAction":{"messageTemplate":"x"}}""", "/then/proposedAction/messageTemplate")]
    public void Validate_WithMessageTemplateInsideBody_IsRefused_NamingItsLocation(string then, string pointer)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}],"then":""" +
                   then + "}";

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith(pointer + ":");
    }

    // =====================================================================================
    // Task 022 rework round 2, finding F13: numeric rule types parse as digits only.
    // =====================================================================================

    [Theory]
    [InlineData(" +100000002 ")]
    [InlineData("+100000002")]
    [InlineData(" 100000002")]
    [InlineData("100000002\n")]
    [InlineData("100,000,002")]
    public void TryParseRuleType_WithSignWhitespaceOrSeparators_IsRefused(string ruleTypeRaw)
    {
        // The plain-digits positive case is ValidateRaw_WithKnownExistenceRuleTypeByDataverseOptionValue_*.
        RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out _).Should().BeFalse();
    }

    // =====================================================================================
    // Task 022 rework (review finding #1): pathological-but-syntactically-valid JSON must be REFUSED, never
    // thrown. Both cases previously escaped as unhandled ArgumentException/FormatException.
    // =====================================================================================

    /// <summary>Pathological-but-parseable bodies (round 1 finding #1; round 3 finding M1). Shared with
    /// <c>PolicyVersionValidatorTests</c>, which asserts the same rows end-to-end as <c>schema_invalid</c>.</summary>
    public static TheoryData<string> PathologicalBodies => new()
    {
        // Two top-level "subject" keys: JsonNode construction used to throw ArgumentException.
        """{"type":"Existence","subject":"sprk_matter","subject":"sprk_communication","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}]}""",
        // 1e999999: the schema library's GetDecimal used to throw FormatException.
        """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_amount":1e999999}}]}""",
        // M1: an unpaired high-surrogate escape in a FILTER PROPERTY NAME threw InvalidOperationException.
        """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_\ud800":1}}]}""",
        // M1: an unpaired low-surrogate escape in a property name nested under a top-level object.
        """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}],"then":{"x\udc00":1}}""",
        // M1 companion: the same escape in a VALUE (already refused before round 3; pinned so it stays so).
        """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_name":"a\ud800"}}]}""",
    };

    [Theory]
    [MemberData(nameof(PathologicalBodies))]
    public void Validate_WithPathologicalBody_IsRefused_NeverThrows(string body)
    {
        var act = () => _sut.Validate(RuleType.Existence, body);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"sprk_\ud800":1}""")]
    [InlineData("""{"x":{"y\udc00":1}}""")]
    public void Validate_WithUnpairedSurrogateInPropertyName_IsRefusedAsInvalidJson(string filter)
    {
        // Round 3, finding M1: reported as what it is (not valid JSON), not as an internal error.
        var body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":""" +
                   filter + "}]}";

        var result = _sut.Validate(RuleType.Existence, body);

        result.Errors.Should().ContainSingle().Which.Should().StartWith("sprk_rulebody is not valid JSON");
    }

    // =====================================================================================
    // Round 3, finding L2 (coordinator decision): 'then' is reserved and refused outright.
    // =====================================================================================

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"signalType":100000003,"severity":100000001}""")]
    [InlineData("""{"msgTemplate":"Unreconciled {{sprk_budgetamount}}"}""")] // a messageTemplate lookalike
    public void Validate_WithThenProperty_IsRefusedAsReserved(string then)
    {
        var body = """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"a":"b"}}],"then":""" +
                   then + "}";

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("/then: 'then' is reserved");
    }

    [Fact]
    public void Validate_WithDuplicateKeyInsideNestedFilter_IsRefused_NeverThrows()
    {
        // The duplicate is INSIDE a clause's "filter" object, two nesting levels deep -- proves
        // AllowDuplicateProperties=false applies recursively, not only at the document root.
        const string body = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                {
                  "exists": "sprk_communication",
                  "path": "sprk_regardingmatter",
                  "filter": { "sprk_triagecategory": "a", "sprk_triagecategory": "b" }
                }
              ]
            }
            """;

        var act = () => _sut.Validate(RuleType.Existence, body);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
    }

    // =====================================================================================
    // Task 022 rework (review finding #8): rule-type name parsing must be EXACT, never a comma-combined set.
    // =====================================================================================

    [Theory]
    [InlineData("Threshold,Switch")]
    [InlineData("Threshold, Switch")]
    [InlineData("Existence,Threshold")]
    public void TryParseRuleType_WithCommaCombinedNames_IsRefused(string ruleTypeRaw)
    {
        // Enum.TryParse treats a comma-separated name list as a bitwise-OR combination even though RuleType
        // carries no [Flags] attribute. TryParseRuleType must not inherit that leniency.
        var parsed = RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out _);

        parsed.Should().BeFalse();
    }

    // =====================================================================================
    // Task 022 rework: discovered WHILE fixing review finding #1, not one of the reviewer's listed items --
    // Json.Schema.Net 7.3.4's JsonSchema.Evaluate is not thread-safe for concurrent calls against the SAME
    // JsonSchema instance (confirmed via a throwaway 2000/4000-iteration Parallel.For repro against the
    // package directly: ~40% false IsValid=true on an actually-invalid body, and a separate run threw an
    // unhandled IndexOutOfRangeException from inside the library). ExistenceSchema is a process-wide static
    // singleton and RuleBodySchemaValidator is an AddSingleton, so concurrent calls are a real production
    // shape, not a test-only artifact. A false IsValid=true is a FAIL-OPEN against the owner's fail-closed
    // mandate -- this permanent regression test pins the fix (a lock around Evaluate in Validate()).
    // =====================================================================================

    [Fact]
    public void Validate_CalledConcurrently_NeverReturnsTrueForAnActuallyInvalidBody()
    {
        const string invalidBody = """
            {
              "type": "Existence",
              "subject": "sprk_matter",
              "all": [
                { "path": "sprk_matter", "filter": { "a": "b" } }
              ]
            }
            """;

        var falsePositives = 0;
        Parallel.For(0, 500, _ =>
        {
            var result = new RuleBodySchemaValidator().Validate(RuleType.Existence, invalidBody);
            if (result.IsValid)
            {
                Interlocked.Increment(ref falsePositives);
            }
        });

        falsePositives.Should().Be(0);
    }

    [Fact]
    public void Validate_CalledConcurrentlyWithMixOfValidAndInvalid_NeverThrows_NeverMisclassifies()
    {
        var mismatches = 0;
        Parallel.For(0, 1000, i =>
        {
            var body = i % 2 == 0 ? ValidExistenceBody : """
                {"type":"Existence","subject":"sprk_matter","all":[{"path":"sprk_matter","filter":{"a":"b"}}]}
                """;
            var result = new RuleBodySchemaValidator().Validate(RuleType.Existence, body);
            var expectedValid = i % 2 == 0;
            if (result.IsValid != expectedValid)
            {
                Interlocked.Increment(ref mismatches);
            }
        });

        mismatches.Should().Be(0);
    }
}
