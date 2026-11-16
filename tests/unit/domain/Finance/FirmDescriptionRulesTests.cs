using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Finance;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060 design 3.1, uac-r2 C3/C4): <see cref="FirmDescriptionRules"/>, the one check of a firm description, and its
/// agreement with every path that uses it. The editor (RuleSetService), the writer preflight (<see cref="SignalWriter.PreflightRefusal"/>), the
/// mapper (<see cref="BillingFindingSignalMapper.FirmReasonOf"/>) and the replay all call <see cref="FirmDescriptionRules.Validate(string?)"/>;
/// the firm projection (<see cref="FirmRejectionFindings.ShownFirmReason"/>) shares its rune predicate. The bound counts runes, never UTF-16 units.
/// </summary>
public class FirmDescriptionRulesTests
{
    private const string RuleSetName = "Acme Outside Counsel Guidelines";
    private const string PolicyName = "Travel Expenses";

    private static string Runes(string rune, int count) => new StringBuilder().Insert(0, rune, count).ToString();

    // 25002500 The bound (200 runes) 25002500

    [Theory(DisplayName = "130e-1: 200 runes are accepted and 201 refused (too_long), counted in runes: ASCII, BMP and surrogate-pair emoji alike")]
    [InlineData("x")]
    [InlineData("\u00E9")]
    [InlineData("\u4E2D")]
    [InlineData("\U0001F600")]   // 2 UTF-16 units per rune: 200 runes are 400 units and still fit
    [InlineData("\U0001F44D")]
    public void TheBound_IsTwoHundredRunes(string rune)
    {
        var atBound = Runes(rune, FirmDescriptionRules.MaxRunes);
        var over = Runes(rune, FirmDescriptionRules.MaxRunes + 1);

        FirmDescriptionRules.Validate(atBound).Should().Be(new FirmDescriptionCheck(atBound, null));
        FirmDescriptionRules.Validate(over).RefusalCode.Should().Be(FirmDescriptionRules.TooLong).And.Be("firm_description_too_long");
        FirmDescriptionRules.Validate(over).Text.Should().BeNull("a refused text is never returned, so it cannot be copied");
    }

    [Fact(DisplayName = "130e-1: surrounding whitespace is trimmed before the count; a blank text is no description (not a refusal)")]
    public void Trim_AndBlank()
    {
        var atBound = Runes("\U0001F600", FirmDescriptionRules.MaxRunes);
        FirmDescriptionRules.Validate("   " + atBound + "  ").Should().Be(new FirmDescriptionCheck(atBound, null));

        foreach (var blank in new[] { null, "", "   " })
        {
            FirmDescriptionRules.Validate(blank).Should().Be(new FirmDescriptionCheck(null, null));
            FirmDescriptionRules.Validate(blank).IsValid.Should().BeTrue();
        }
    }

    // 25002500 Refused characters 25002500

    [Theory(DisplayName = "130e-1: braces and angle brackets are refused with their own codes")]
    [InlineData("{", FirmDescriptionRules.Braces)]
    [InlineData("}", FirmDescriptionRules.Braces)]
    [InlineData("<", FirmDescriptionRules.Markup)]
    [InlineData(">", FirmDescriptionRules.Markup)]
    public void BracesAndMarkup_AreRefused(string character, string code)
    {
        FirmDescriptionRules.Validate($"Rate {character} too high.").Should().Be(new FirmDescriptionCheck(null, code));
    }

    [Theory(DisplayName = "130e-1: every Control, Format, LineSeparator and ParagraphSeparator rune is refused (invisible_character), inside the text or at its edge")]
    [InlineData("\u0000")]       // Control
    [InlineData("\u0007")]       // Control (bell)
    [InlineData("\t")]           // Control (tab)
    [InlineData("\n")]           // Control (line feed)
    [InlineData("\u0085")]       // Control (next line)
    [InlineData("\u007F")]       // Control (delete)
    [InlineData("\u061C")]       // Format (Arabic letter mark)
    [InlineData("\uFEFF")]       // Format (zero-width no-break space)
    [InlineData("\u200B")]       // Format (zero-width space)
    [InlineData("\u202E")]       // Format (right-to-left override)
    [InlineData("\u2066")]       // Format (left-to-right isolate)
    [InlineData("\u00AD")]       // Format (soft hyphen)
    [InlineData("\U000E0041")]   // Format, outside the BMP (tag letter A: a surrogate pair)
    [InlineData("\u2028")]       // LineSeparator
    [InlineData("\u2029")]       // ParagraphSeparator
    public void InvisibleRunes_AreRefused(string invisible)
    {
        FirmDescriptionRules.Validate($"Rate{invisible} too high.").RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter);
        FirmDescriptionRules.IsInvisibleControl(Rune.GetRuneAt(invisible, 0)).Should().BeTrue();
    }

    [Theory(DisplayName = "130e-1 must-not-fire: ordinary text is accepted as is: punctuation, accents, CJK, emoji, an emoji with a skin-tone modifier")]
    [InlineData("Your rate is above the agreed rate.")]
    [InlineData("It's 100% (half) rate - see 4.2; ok? \"yes\" & done!")]
    [InlineData("\u00C9t\u00E9 \u4E2D\u6587 \U0001F600 \U0001F44D\U0001F3FD")]
    public void OrdinaryText_IsAccepted(string text)
    {
        FirmDescriptionRules.Validate(text).Should().Be(new FirmDescriptionCheck(text, null));
    }

    // 25002500 C4: the version's own rule set and policy names 25002500

    [Theory(DisplayName = "130e-1 C4: a text containing the rule set's or the policy's own name, in any case, is refused (names_rule)")]
    [InlineData("Per the Acme Outside Counsel Guidelines, travel is half rate.")]
    [InlineData("per the ACME OUTSIDE COUNSEL GUIDELINES.")]
    [InlineData("See travel expenses.")]
    [InlineData("TRAVEL EXPENSES are capped.")]
    public void NamingTheRule_IsRefused(string text)
    {
        FirmDescriptionRules.Validate(text, RuleSetName, PolicyName).Should().Be(new FirmDescriptionCheck(null, FirmDescriptionRules.NamesTheRule));
        FirmDescriptionRules.Validate(text).IsValid.Should().BeTrue("the content-only check has no names to compare");
    }

    [Fact(DisplayName = "130e-1 C4 must-not-fire: a text naming neither, or blank or missing names, is accepted; a name is compared trimmed")]
    public void NotNamingTheRule_IsAccepted()
    {
        FirmDescriptionRules.Validate("Travel is billed at half rate.", RuleSetName, PolicyName).IsValid.Should().BeTrue();
        FirmDescriptionRules.Validate("Travel is billed at half rate.", null, "  ").IsValid.Should().BeTrue();
        FirmDescriptionRules.Validate("Billed under the expenses cap.", "  Expenses  ", null).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
    }

    // 25002500 One check, every path 25002500

    public static TheoryData<string> AgreementInputs()
    {
        var data = new TheoryData<string>
        {
            "Plain text.", "", "   ", Runes("\U0001F600", 200), Runes("\U0001F600", 201), Runes("x", 200), Runes("x", 201),
            "a{b", "a}b", "a<b", "a>b", "a\u0007b", "a\tb", "a\u061Cb", "a\uFEFFb", "a\U000E0041b", "a\u2028b", "a\u2029b", "a\u200Bb",
            "a\U0001F600b", "a\u00E9b",
        };
        return data;
    }

    [Theory(DisplayName = "130e-1 coordinator 1: the writer preflight and the mapper refuse exactly the inputs FirmDescriptionRules refuses (the same call, never a copy)")]
    [MemberData(nameof(AgreementInputs))]
    public void ThePreflightAndTheMapper_RefuseTheSameInputs(string text)
    {
        var rules = FirmDescriptionRules.Validate(text);
        var preflight = SignalWriter.PreflightRefusal(SignalWriterRequest() with { FirmReason = text });
        var mapper = BillingFindingSignalMapper.FirmReasonOf(Version("BILL-FD", RuleType.ExpenseRule) with { FirmDescription = text }, null);

        (preflight == SignalWriteRefusal.FirmReasonInvalid).Should().Be(!rules.IsValid, "the preflight refuses iff the rules refuse");
        if (!rules.IsValid)
        {
            preflight.Should().Be(SignalWriteRefusal.FirmReasonInvalid);
        }

        mapper.Should().Be(rules.Text, "the mapper copies exactly what the rules return (null when refused or blank)");
    }

    [Theory(DisplayName = "130e-1 coordinator 1: a rune the writer preflight refuses is exactly a rune the firm projection drops (one predicate)")]
    [InlineData("\u0000")]
    [InlineData("\u0007")]
    [InlineData("\t")]
    [InlineData("\u0085")]
    [InlineData("\u061C")]
    [InlineData("\uFEFF")]
    [InlineData("\u200B")]
    [InlineData("\u202E")]
    [InlineData("\U000E0041")]
    [InlineData("\U000E007F")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\U0001F600")]   // must-not-fire: kept by both
    [InlineData("\u00E9")]
    [InlineData("\u4E2D")]
    [InlineData("-")]
    public void APreflightRefusedRune_IsAProjectionDroppedRune(string rune)
    {
        var text = $"Rate{rune} high.";

        var refused = SignalWriter.PreflightRefusal(SignalWriterRequest() with { FirmReason = text }) == SignalWriteRefusal.FirmReasonInvalid;
        var dropped = FirmRejectionFindings.ShownFirmReason(text) != text;

        refused.Should().Be(dropped, "the writer refuses a rune iff the projection would drop it");
    }

    [Fact(DisplayName = "130e-1 C3: the mapper's drop logs a Warning with the stable code and the version id, never the text")]
    public void TheMappersDrop_LogsTheCodeOnly()
    {
        var logger = new CapturingLogger<FirmDescriptionRulesTests>();
        var rule = Version("BILL-FD", RuleType.ExpenseRule) with { FirmDescription = "SECRET {text}", PolicyName = PolicyName, RuleSetName = RuleSetName };

        BillingFindingSignalMapper.FirmReasonOf(rule, logger).Should().BeNull();

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(FirmDescriptionRules.DroppedLogCode).And.Contain(rule.PolicyVersionId.ToString())
            .And.Contain(FirmDescriptionRules.Braces).And.NotContain("SECRET");
    }

    private static SignalWriteRequest SignalWriterRequest() => new(
        PolicyId: Guid.Parse("130e0000-0000-4000-8000-0000000000a1"),
        PolicyCode: "BILL-FD",
        PolicyVersionId: Guid.Parse("130e0000-0000-4000-8000-0000000000a2"),
        SubjectEntityLogicalName: "sprk_invoice",
        SubjectId: Guid.Parse("130e0000-0000-4000-8000-0000000000a3"),
        SubjectDisplayName: "INV-1",
        ShortHeadline: "Expense not allowed",
        Lane: SignalWriter.LaneDecide,
        Severity: SignalWriter.SeverityWarning,
        MessageTemplate: "An expense is not allowed.",
        FactValues: new Dictionary<string, object?>());
}
