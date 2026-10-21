using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="BillingRuleDraftTranslator"/> (ebilling-r1 task 075, FR-27, D-27): the closed list the model is offered, the fixed wording of each
/// type, how a proposal becomes a flat rule body, and the splitting of a long document.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the wording of every type must pass the REAL billing validation (a template naming a field the type's handler does
/// not supply would make every drafted rule of that type unsaveable), the closed list must stay the catalogue, and a model cannot widen a body.
/// </remarks>
public sealed class BillingRuleDraftTranslatorTests
{
    private static readonly PolicyVersionValidator Validator = CreateValidator();

    private static PolicyVersionValidator CreateValidator()
    {
        var schema = new RuleBodySchemaValidator();
        return new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
    }

    /// <summary>One valid body per catalogue type.</summary>
    public static TheoryData<string, string> ValidBodies => new()
    {
        { "ApprovedTimekeeper", """{"type":"ApprovedTimekeeper"}""" },
        { "ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":5}""" },
        { "TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""" },
        { "DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":10}""" },
        { "NonBillableActivity", """{"type":"NonBillableActivity","codes":["L190","L210"]}""" },
        { "ExpenseRule", """{"type":"ExpenseRule","nonReimbursableCodes":["E101"],"maxAmountPerItem":500}""" },
        { "InvoiceAge", """{"type":"InvoiceAge","maxAgeDays":90}""" },
        { "CoreRecordWindow", """{"type":"CoreRecordWindow","graceDays":7}""" },
        { "Duplicate", """{"type":"Duplicate","matchFields":["firmInvoiceNumber","lineDate"]}""" },
        { "RequiredData", """{"type":"RequiredData","fields":["taskCode","narrative"]}""" },
        { "AiObservation", """{"type":"AiObservation","observation":"blockBilling","minConfidence":0.8}""" },
    };

    // ── the closed list ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RuleTypeOptions_AreTheCatalogueTypes_InOrder_EachWithADescriptionAndAParameterSchema()
    {
        var options = BillingRuleDraftTranslator.RuleTypeOptions();

        options.Select(o => o.RuleType).Should().Equal(BillingRuleCatalogue.Types.Select(t => t.ToString()));
        options.Should().HaveCount(13);
        foreach (var option in options)
        {
            option.Description.Should().NotBeNullOrWhiteSpace(option.RuleType);
            JsonNode.Parse(option.ParameterSchemaJson).Should().BeOfType<JsonObject>(option.RuleType);
        }
    }

    [Fact]
    public void RuleTypeOptions_EachDescriptionNamesExactlyTheConsequencesTheCatalogueAllows_F1b()
    {
        // 073 review F1-b: the drafting prompt reads the allowed consequences from the description, so it never suggests one the
        // catalogue refuses (Reject on TimeIncrement, say).
        foreach (var option in BillingRuleDraftTranslator.RuleTypeOptions())
        {
            var type = Enum.Parse<RuleType>(option.RuleType);
            var allowed = string.Join(", ", BillingRuleCatalogue.AllowedConsequences(type).OrderBy(c => (int)c));
            option.Description.Should().Contain($"Allowed consequences: {allowed}.", option.RuleType);
        }

        BillingRuleDraftTranslator.RuleTypeOptions().Single(o => o.RuleType == "TimeIncrement").Description.Should().NotContain("Reject");
        BillingRuleDraftTranslator.RuleTypeOptions().Single(o => o.RuleType == "Duplicate").Description.Should().Contain("firmInvoiceNumber");
    }

    [Theory]
    [MemberData(nameof(ValidBodies))]
    public void ParameterSchema_LeavesOutTheTypeMemberTheApplicationAdds_AndTheProse(string type, string _)
    {
        var schema = JsonNode.Parse(BillingRuleDraftTranslator.ParameterSchemaJson(Enum.Parse<RuleType>(type)))!.AsObject();

        schema.ContainsKey("$schema").Should().BeFalse();
        schema.ContainsKey("$id").Should().BeFalse();
        schema.ContainsKey("description").Should().BeFalse("the long author prose is not the model's business");
        schema["properties"]!.AsObject().ContainsKey("type").Should().BeFalse();
        (schema["required"]?.AsArray().Select(r => r!.GetValue<string>()) ?? Enumerable.Empty<string>()).Should().NotContain("type");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse("the body schema stays closed");
    }

    [Fact]
    public void ParameterSchema_KeepsWhatTheParametersNeed_TheCodePatternAndTheAtLeastOneRule()
    {
        var expense = JsonNode.Parse(BillingRuleDraftTranslator.ParameterSchemaJson(RuleType.ExpenseRule))!.AsObject();

        expense.ContainsKey("$defs").Should().BeTrue("the code member refers to it");
        expense.ContainsKey("anyOf").Should().BeTrue("at least one of the two parameters");
        JsonNode.Parse(BillingRuleDraftTranslator.ParameterSchemaJson(RuleType.ApprovedTimekeeper))!["properties"]!.AsObject()
            .Should().BeEmpty("ApprovedTimekeeper has no parameters");
    }

    // ── the fixed wording ───────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ValidBodies))]
    public void Wording_OfEveryType_PassesBillingValidateForSave_WithAValidBody(string type, string body)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(type));

        headline.Should().NotBeNullOrWhiteSpace().And.Subject.Length.Should().BeLessThanOrEqualTo(200);
        template.Length.Should().BeLessThanOrEqualTo(2000);
        var result = Validator.ValidateForSave(type, body, template, RuleDomain.Billing);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(ValidBodies))]
    public void Wording_OfEveryType_NamesOnlyTheTokensThatTypesHandlerSupplies(string type, string _)
    {
        var (_, template) = BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(type));
        var allowed = BillingRuleCatalogue.TemplateFactFields(Enum.Parse<RuleType>(type));

        var tokens = System.Text.RegularExpressions.Regex.Matches(template, @"\{\{([^}]+)\}\}").Select(m => m.Groups[1].Value);

        tokens.Should().NotBeEmpty();
        tokens.Should().OnlyContain(t => allowed.Contains(t), $"{type}'s template may name only {string.Join(", ", allowed)}");
    }

    // ── proposal to body ────────────────────────────────────────────────────────────────────────

    private static BillingRuleProposal Proposal(string type, string parameters, string quote = "The clause.", string consequence = "Flag", string? location = "Section 1") =>
        new() { RuleType = type, ParametersJson = parameters, Consequence = consequence, ClauseQuote = quote, ClauseLocation = location };

    private const string AllNull = """
        {"tolerancePercent":null,"incrementHours":null,"maxHoursPerDay":null,"codes":null,"nonReimbursableCodes":null,"maxAmountPerItem":null,
         "maxAgeDays":null,"graceDays":null,"matchFields":null,"fields":null,"observation":null,"minConfidence":null}
        """;

    private static string With(string member, string value) =>
        JsonNode.Parse(AllNull)!.AsObject() is { } o ? SetMember(o, member, value) : throw new InvalidOperationException();

    private static string SetMember(JsonObject o, string member, string value)
    {
        o[member] = JsonNode.Parse(value);
        return o.ToJsonString();
    }

    [Fact]
    public void Translate_RemovesTheNullMembers_AndAddsTheType_SoTheBodyIsTheFlatRuleBody()
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(new[]
        {
            Proposal("TimeIncrement", With("incrementHours", "0.1"), "Tenth hours.", "Reject", "Section 4.2"),
        });

        refused.Should().BeEmpty();
        var rule = rules.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be("TimeIncrement");
        rule.RuleBody.Should().Be("""{"type":"TimeIncrement","incrementHours":0.1}""");
        rule.Consequence.Should().Be("Reject");
        rule.ClauseQuote.Should().Be("Tenth hours.");
        rule.ClauseLocation.Should().Be("Section 4.2");
        (rule.ShortHeadline, rule.MessageTemplate).Should().Be(BillingRuleDraftTranslator.Wording(RuleType.TimeIncrement));
    }

    // ── the clause quote must be in the document ────────────────────────────────────────────────

    private const string Document = "4.2 Time is billed in\r\n  tenth-hour increments.\n\n5.1 \u201CNo timekeeper\u201D may bill over 12 hours \u2013 per day.";

    [Theory]
    [InlineData("4.2 Time is billed in tenth-hour increments.")] // line break and indent collapsed
    [InlineData("4.2 TIME IS BILLED IN TENTH-HOUR INCREMENTS.")] // case
    [InlineData("5.1 \"No timekeeper\" may bill over 12 hours - per day.")] // typographic quotes and dash
    [InlineData("tenth-hour")] // a part of a sentence
    public void Translate_AQuoteThatIsInTheDocument_IsKept_IgnoringWhiteSpaceCaseAndTypographicCharacters(string quote)
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(
            new[] { Proposal("TimeIncrement", With("incrementHours", "0.1"), quote) }, Document);

        refused.Should().BeEmpty();
        rules.Should().ContainSingle().Which.ClauseQuote.Should().Be(quote, "the stored quote is the model's, unchanged");
    }

    [Theory]
    [InlineData("4.2 Time is billed in quarter-hour increments.")] // reworded
    [InlineData("A clause that is not in the document at all.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Translate_AQuoteThatIsNotInTheDocument_IsRefusedWithItsClause_NotStored(string quote)
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(
            new[] { Proposal("TimeIncrement", With("incrementHours", "0.1"), quote, location: "Section 4.2") }, Document);

        rules.Should().BeEmpty();
        var listed = refused.Should().ContainSingle().Subject;
        listed.Text.Should().Be(quote);
        listed.Location.Should().Be("Section 4.2");
        listed.Reason.Should().Contain("not found in the document");
    }

    [Fact]
    public void Translate_WithoutTheDocumentText_DoesNotCheckTheQuote()
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(new[] { Proposal("TimeIncrement", With("incrementHours", "0.1"), "Anything.") });

        refused.Should().BeEmpty();
        rules.Should().ContainSingle();
    }

    [Fact]
    public void Translate_ATypeWithNoParameters_IsJustTheTypeMember()
    {
        var (rules, _) = BillingRuleDraftTranslator.Translate(new[] { Proposal("ApprovedTimekeeper", AllNull) });

        rules.Should().ContainSingle().Which.RuleBody.Should().Be("""{"type":"ApprovedTimekeeper"}""");
    }

    [Theory]
    [InlineData("Existence")]
    [InlineData("Threshold")]
    [InlineData("timeincrement")]
    [InlineData("TIMEINCREMENT")]
    [InlineData("100000005")]
    [InlineData("TimeIncrement,Flag")]
    [InlineData(" TimeIncrement")]
    [InlineData("")]
    public void Translate_ATypeThatIsNotAnExactCatalogueName_IsRefusedWithItsClause_NotMapped(string type)
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(new[] { Proposal(type, With("incrementHours", "0.1"), "Clause text.", location: "S9") });

        rules.Should().BeEmpty();
        var entry = refused.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Clause text.");
        entry.Location.Should().Be("S9");
        entry.Reason.Should().Contain("not in the offered list");
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("42")]
    public void Translate_ParametersThatAreNotAnObject_AreRefused(string parameters)
    {
        var (rules, refused) = BillingRuleDraftTranslator.Translate(new[] { Proposal("TimeIncrement", parameters) });

        rules.Should().BeEmpty();
        refused.Should().ContainSingle().Which.Reason.Should().Contain("parameters");
    }

    [Fact]
    public void Translate_AModelCannotSetTheTypeMember_ItIsTheApplicationsAlone()
    {
        var parameters = With("incrementHours", "0.1").Replace("{", "{\"type\":\"Existence\",");

        var (rules, _) = BillingRuleDraftTranslator.Translate(new[] { Proposal("TimeIncrement", parameters) });

        JsonNode.Parse(rules.Single().RuleBody)!["type"]!.GetValue<string>().Should().Be("TimeIncrement");
    }

    [Fact]
    public void Translate_TheSameRuleTwice_IsKeptOnce_AndOrderIsTheModelsOrder()
    {
        var (rules, _) = BillingRuleDraftTranslator.Translate(new[]
        {
            Proposal("DailyHoursCap", With("maxHoursPerDay", "10"), "Cap."),
            Proposal("TimeIncrement", With("incrementHours", "0.1"), "Inc."),
            Proposal("DailyHoursCap", With("maxHoursPerDay", "10"), "Cap."),
            Proposal("DailyHoursCap", With("maxHoursPerDay", "10"), "Another clause with the same cap."),
        });

        rules.Select(r => r.ClauseQuote).Should().Equal("Cap.", "Inc.", "Another clause with the same cap.");
    }

    [Fact]
    public void Translate_AParameterThatDoesNotBelongToTheType_IsKept_SoTheBodySchemaRefusesIt()
    {
        // The Action's parameters object holds every member; a model that fills one the type lacks must not be silently cleaned up.
        var parameters = JsonNode.Parse(With("incrementHours", "0.1"))!.AsObject();
        parameters["maxHoursPerDay"] = 8;

        var (rules, _) = BillingRuleDraftTranslator.Translate(new[] { Proposal("TimeIncrement", parameters.ToJsonString()) });

        var rule = rules.Should().ContainSingle().Subject;
        var result = Validator.ValidateForSave(rule.RuleType, rule.RuleBody, rule.MessageTemplate, RuleDomain.Billing);
        result.IsValid.Should().BeFalse("additionalProperties is false on every billing body schema");
    }

    [Theory]
    [MemberData(nameof(ValidBodies))]
    public void Translate_AFullyPopulatedProposal_OfEveryType_ProducesABodyThatPassesValidateForSave(string type, string body)
    {
        var parameters = JsonNode.Parse(AllNull)!.AsObject();
        foreach (var member in JsonNode.Parse(body)!.AsObject().Where(m => m.Key != "type"))
        {
            parameters[member.Key] = member.Value!.DeepClone();
        }

        var (rules, refused) = BillingRuleDraftTranslator.Translate(new[] { Proposal(type, parameters.ToJsonString()) });

        refused.Should().BeEmpty();
        var rule = rules.Should().ContainSingle().Subject;
        JsonNode.DeepEquals(JsonNode.Parse(rule.RuleBody), JsonNode.Parse(body)).Should().BeTrue();
        var result = Validator.ValidateForSave(rule.RuleType, rule.RuleBody, rule.MessageTemplate, RuleDomain.Billing);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    // ── long documents ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Split_AShortText_IsOnePiece_AndAllOfItIsRead()
    {
        var pieces = BillingRuleDraftTranslator.Split("one\n\ntwo", 1000, 8, out var unreadFrom);

        pieces.Should().Equal("one\n\ntwo");
        unreadFrom.Should().Be(-1);
    }

    [Fact]
    public void Split_CutsAtAParagraphBreak_AndLosesNothing()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 20).Select(i => new string((char)('a' + i), 100)));

        var pieces = BillingRuleDraftTranslator.Split(text, 450, 100, out var unreadFrom);

        unreadFrom.Should().Be(-1);
        string.Concat(pieces).Should().Be(text, "the pieces tile the text exactly");
        pieces.Should().OnlyContain(p => p.Length <= 450);
        pieces.Take(pieces.Count - 1).Should().OnlyContain(p => p.EndsWith("\n\n", StringComparison.Ordinal), "each cut is at a paragraph break");
    }

    [Fact]
    public void Split_WithoutParagraphBreaks_CutsAtALineBreak_ThenAtTheLimit()
    {
        var lines = string.Join("\n", Enumerable.Range(0, 30).Select(i => new string('x', 49)));
        var solid = new string('y', 1000);

        var byLine = BillingRuleDraftTranslator.Split(lines, 500, 100, out _);
        var bySize = BillingRuleDraftTranslator.Split(solid, 300, 100, out _);

        string.Concat(byLine).Should().Be(lines);
        byLine.Should().OnlyContain(p => p.Length <= 500);
        byLine.Take(byLine.Count - 1).Should().OnlyContain(p => p.EndsWith('\n'));
        bySize.Select(p => p.Length).Should().Equal(300, 300, 300, 100);
    }

    [Fact]
    public void Split_MoreThanTheMaximumPieces_ReportsWhereTheUnreadTextStarts()
    {
        var text = new string('z', 1000);

        var pieces = BillingRuleDraftTranslator.Split(text, 100, 3, out var unreadFrom);

        pieces.Should().HaveCount(3);
        unreadFrom.Should().Be(300, "the text after the pieces is reported, never silently dropped");
    }

    [Fact]
    public void Split_Limits_AreWhatTheHandlerDocuments()
    {
        BillingRuleDraftTranslator.ChunkChars.Should().Be(24_000);
        BillingRuleDraftTranslator.MaxChunks.Should().Be(8);
    }

    [Fact]
    public void BuildBody_ForAnyTypeButTheFirst_NeverEmitsANullMember()
    {
        var body = BillingRuleDraftTranslator.BuildBody(RuleType.ExpenseRule, With("maxAmountPerItem", "250"));

        JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("type", "maxAmountPerItem");
    }
}
