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
          ],
          "then": {}
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
          ],
          "then": {}
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
    public void Validate_WithEmptyAllArray_IsRefused()
    {
        const string body = """{ "type": "Existence", "subject": "sprk_matter", "all": [] }""";

        var result = _sut.Validate(RuleType.Existence, body);

        result.IsValid.Should().BeFalse();
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
}
