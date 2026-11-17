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

    // -- The bound (200 runes) --

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

    // -- Refused characters --

    [Theory(DisplayName = "130e-1 F1(b) must-fire: an unpaired UTF-16 surrogate is refused (invisible_character), wherever it sits")]
    // The code units are passed as numbers: test-case data travels as UTF-8, which turns a lone surrogate into U+FFFD before the test sees it.
    [InlineData("Rate ", 0xD83D, 0, " too high.")]   // a lone high surrogate
    [InlineData("Rate ", 0xDE00, 0, " too high.")]   // a lone low surrogate
    [InlineData("Rate too high.", 0xD83D, 0, "")]    // a high surrogate at the end
    [InlineData("", 0xDE00, 0xD83D, "")]             // low before high: two unpaired halves
    public void AnUnpairedSurrogate_IsRefused(string before, int unit, int second, string after)
    {
        var text = before + (char)unit + (second == 0 ? string.Empty : ((char)second).ToString()) + after;
        text.Should().Contain(((char)unit).ToString(), "the setup: the lone code unit reached the test");

        FirmDescriptionRules.Validate(text).RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter);
    }

    [Fact(DisplayName = "130e-1 F1(b) must-not-fire: a properly paired surrogate (an emoji) is accepted")]
    public void APairedSurrogate_IsAccepted()
    {
        FirmDescriptionRules.Validate("Rate \uD83D\uDE00 too high.").IsValid.Should().BeTrue();
    }

    [Theory(DisplayName = "130e-1 F4: with a rule type, a Duplicate takes no description (firm_description_duplicate); any other type is the plain check")]
    [InlineData("Duplicate", FirmDescriptionRules.OnDuplicate)]
    [InlineData("100000011", FirmDescriptionRules.OnDuplicate)]   // the option value, as the loader and the replay read sprk_ruletype
    [InlineData("100000004", null)]                               // ApprovedRate
    [InlineData("ApprovedRate", null)]
    [InlineData(null, null)]
    public void WithARuleType_ADuplicateTakesNone(string? ruleType, string? expected)
    {
        var check = FirmDescriptionRules.Validate("Travel is billed at half rate.", RuleSetName, PolicyName, ruleType);

        check.RefusalCode.Should().Be(expected);
        FirmDescriptionRules.Validate("   ", RuleSetName, PolicyName, "Duplicate").Should().Be(new FirmDescriptionCheck(null, null), "a blank text is no description on any type");
    }

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

    // -- C4: the version's own rule set and policy names --

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

    // Fix round 2 (F3): the separators are passed as numbers so the source shows which character each row tests. -1 is a doubled space.
    [Theory(DisplayName = "130e-1 fix round 2 F3 (C4): a name spelled with another white space between its words is still refused (names_rule)")]
    [InlineData(0x00A0)] // no-break space
    [InlineData(0x2000)] // en quad
    [InlineData(0x2002)] // en space
    [InlineData(0x2005)] // four-per-em space
    [InlineData(0x2009)] // thin space
    [InlineData(0x200A)] // hair space
    [InlineData(0x202F)] // narrow no-break space
    [InlineData(0x205F)] // medium mathematical space
    [InlineData(0x3000)] // ideographic space
    [InlineData(-1)]     // two plain spaces
    public void ANameWithOtherWhiteSpace_IsRefused(int separator)
    {
        var gap = separator == -1 ? "  " : ((char)separator).ToString();
        var setText = "Per the " + RuleSetName.Replace(" ", gap) + ", travel is half rate.";
        var policyText = "See " + PolicyName.Replace(" ", gap) + ".";

        FirmDescriptionRules.Validate(setText, RuleSetName, PolicyName).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
        FirmDescriptionRules.Validate(policyText, RuleSetName, PolicyName).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
        FirmDescriptionRules.Validate(setText).IsValid.Should().BeTrue("the setup: the separator itself is an allowed character");
    }

    [Theory(DisplayName = "130e-1 fix round 2 F3 (C4): a name in fullwidth letters (alone or with an ideographic space), or a stored name with odd spacing, is refused")]
    [InlineData(false)]
    [InlineData(true)]
    public void AFullwidthName_IsRefused(bool ideographicSpace)
    {
        var name = Fullwidth(PolicyName);
        if (ideographicSpace)
        {
            name = name.Replace(" ", ((char)0x3000).ToString());
        }

        FirmDescriptionRules.Validate("See " + name + ".", RuleSetName, PolicyName).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
        FirmDescriptionRules.Validate("See travel expenses.", RuleSetName, "Travel" + (char)0x00A0 + " Expenses ").RefusalCode
            .Should().Be(FirmDescriptionRules.NamesTheRule, "the stored name is normalised the same way");
    }

    [Theory(DisplayName = "130e-1 fix round 2 F3 must-not-fire: normalising does not join words a name does not have")]
    [InlineData("Travel-Expenses are capped.")]
    [InlineData("TravelExpenses are capped.")]
    [InlineData("Acme Outside Counsel guidance applies.")]
    public void NormalisingCreatesNoName(string text)
    {
        FirmDescriptionRules.Validate(text, RuleSetName, PolicyName).IsValid.Should().BeTrue();
    }

    // 130e-2 fix round 1 (F1): the Turkish dotless i (U+0131) and dotted I (U+0130) are not case pairs of i and I under OrdinalIgnoreCase, so a
    // name like "YILLIK SINIR" was not found in "y\u0131ll\u0131k s\u0131n\u0131r". Both sides now read them as "i". The characters are written as escapes.
    [Theory(DisplayName = "130e-2 fix round 1 F1 (C4): a Turkish dotless or dotted form of the name, or a plain text against a Turkish name, is refused (names_rule)")]
    [InlineData("Billed under the y\u0131ll\u0131k s\u0131n\u0131r.", "YILLIK SINIR")]                // dotless text, plain upper name
    [InlineData("Billed under the YILLIK SINIR.", "Y\u0131ll\u0131k S\u0131n\u0131r")]                 // plain text, dotless name
    [InlineData("Billed under the Y\u0130LL\u0130K S\u0130N\u0130R.", "yillik sinir")]                // dotted capitals text, plain lower name
    [InlineData("Billed under the yillik sinir.", "Y\u0130LL\u0130K S\u0130N\u0130R")]                // plain text, dotted capitals name
    [InlineData("Billed under the y\u0131ll\u0131k s\u0131n\u0131r.", "Y\u0130LL\u0130K S\u0130N\u0130R")] // dotless text, dotted name
    [InlineData("Billed under the Y\u0130LL\u0130K S\u0130N\u0130R.", "y\u0131ll\u0131k s\u0131n\u0131r")] // dotted text, dotless name
    public void ATurkishCaseFormOfTheName_IsRefused(string text, string name)
    {
        FirmDescriptionRules.Validate(text, name, null).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule, "the name is in the set slot");
        FirmDescriptionRules.Validate(text, null, name).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule, "and in the rule slot");
        FirmDescriptionRules.Validate(text).IsValid.Should().BeTrue("the setup: the text alone is an allowed text");
    }

    [Theory(DisplayName = "130e-2 fix round 1 F1 must-not-fire: unrelated text holding a dotless or dotted i is accepted")]
    [InlineData("Ak\u0131ll\u0131 hizmet bedeli uygulanm\u0131\u015Ft\u0131r.")]
    [InlineData("\u0130stanbul office, hourly rates apply.")]
    [InlineData("Billed under the y\u0131ll\u0131k tarife.")] // shares one word with the name only
    public void UnrelatedTurkishText_IsAccepted(string text)
    {
        FirmDescriptionRules.Validate(text, "YILLIK SINIR", PolicyName).IsValid.Should().BeTrue();
        FirmDescriptionRules.Validate(text, RuleSetName, "Y\u0131ll\u0131k S\u0131n\u0131r").IsValid.Should().BeTrue();
    }

    // -- 130e-2 fix round 2 (F1): Default_Ignorable_Code_Point. The code points are numbers so the source shows which character each row tests. --

    [Theory(DisplayName = "130e-2 fix round 2 F1: a default-ignorable rune that is not category Format (combining grapheme joiner, variation selectors, Hangul fillers ...) is refused (invisible_character)")]
    [InlineData(0x034F)]  // combining grapheme joiner (Mn)
    [InlineData(0x180B)]  // Mongolian free variation selector one (Mn)
    [InlineData(0x180D)]  // Mongolian free variation selector three (Mn)
    [InlineData(0xFE00)]  // variation selector 1 (Mn)
    [InlineData(0xFE0F)]  // variation selector 16 (Mn)
    [InlineData(0xE0100)] // variation selector 17 (Mn)
    [InlineData(0xE01EF)] // variation selector 256 (Mn)
    [InlineData(0x115F)]  // Hangul choseong filler (Lo)
    [InlineData(0x3164)]  // Hangul filler (Lo)
    [InlineData(0xFFA0)]  // halfwidth Hangul filler (Lo)
    [InlineData(0x17B4)]  // Khmer vowel inherent AQ (Mn)
    [InlineData(0x1BCA0)] // shorthand format letter overlap (Cf)
    [InlineData(0x1D173)] // musical symbol begin beam (Cf)
    [InlineData(0x2065)]  // unassigned, inside U+2060 to U+206F
    [InlineData(0xE0FFF)] // last of U+E0000 to U+E0FFF
    public void ADefaultIgnorableRune_IsRefused(int codePoint)
    {
        var text = $"Rate{char.ConvertFromUtf32(codePoint)} too high.";

        FirmDescriptionRules.IsDefaultIgnorable(new Rune(codePoint)).Should().BeTrue();
        FirmDescriptionRules.Validate(text).RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter).And.Be("firm_description_invisible_character");
        FirmDescriptionRules.Validate(text).Text.Should().BeNull();
    }

    [Theory(DisplayName = "130e-2 fix round 2 F1 (C4): an invisible mark inside a name no longer gets it past the check (refused as invisible_character, with or without the names)")]
    [InlineData(0x034F)]
    [InlineData(0xFE0F)]
    [InlineData(0xE0100)]
    [InlineData(0x180B)]
    public void AnInvisibleMarkInsideAName_IsRefused(int codePoint)
    {
        var mark = char.ConvertFromUtf32(codePoint);
        var text = "Billed under the YIL" + mark + "LIK SINIR.";

        FirmDescriptionRules.Validate(text, "YILLIK SINIR", PolicyName).RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter);
        FirmDescriptionRules.Validate(text, RuleSetName, "YILLIK SINIR").RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter);
        FirmDescriptionRules.Validate(text).RefusalCode.Should().Be(FirmDescriptionRules.InvisibleCharacter, "the check does not need the names");
    }

    [Theory(DisplayName = "130e-2 fix round 2 F1 must-not-fire: text with accents (precomposed or decomposed), neighbours of the ranges and other combining marks are accepted")]
    [InlineData("caf\u00E9 hours")]        // precomposed e acute
    [InlineData("cafe\u0301 hours")]       // decomposed: e + combining acute (U+0301 is Mn, not default-ignorable)
    [InlineData("na\u00EFve \u00C5ngstr\u00F6m")]
    [InlineData("Registered \u00AE rates")] // U+00AE, next to U+00AD
    [InlineData("Digit \u1810 and \u180A")] // Mongolian digit zero and the nirugu, either side of U+180B to U+180F
    [InlineData("Mark \u034E \u0350")]      // either side of U+034F
    [InlineData("Presentation \uFE10 \uFE20")] // after U+FE00 to U+FE0F
    [InlineData("Superscript \u2070 \u2071")]  // after U+2060 to U+206F
    [InlineData("Emoji \U0001F600 with tone \U0001F44D\U0001F3FD")]
    public void OrdinaryAndNeighbouringText_IsAccepted(string text)
    {
        FirmDescriptionRules.Validate(text).Should().Be(new FirmDescriptionCheck(text, null));
    }

    [Fact(DisplayName = "130e-2 fix round 2 F1: a decomposed accent is composed by NFKC before the name comparison, so a decomposed name is still refused (names_rule)")]
    public void ADecomposedNameIsStillCaught()
    {
        FirmDescriptionRules.Validate("Per the cafe\u0301 rule.", "CAF\u00C9", null).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
        FirmDescriptionRules.Validate("Per the caf\u00E9 rule.", "CAFE\u0301", null).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
    }

    [Fact(DisplayName = "130e-2 fix round 2 F1: the range table is Unicode 16.0's Default_Ignorable_Code_Point, exactly (the client holds the same table; a source-scan test keeps them equal)")]
    public void TheDefaultIgnorableTable_IsUnicode16()
    {
        FirmDescriptionRules.DefaultIgnorableRanges.Should().Equal(
            (0x00AD, 0x00AD), (0x034F, 0x034F), (0x061C, 0x061C), (0x115F, 0x1160), (0x17B4, 0x17B5), (0x180B, 0x180F), (0x200B, 0x200F),
            (0x202A, 0x202E), (0x2060, 0x206F), (0x3164, 0x3164), (0xFE00, 0xFE0F), (0xFEFF, 0xFEFF), (0xFFA0, 0xFFA0), (0xFFF0, 0xFFF8),
            (0x1BCA0, 0x1BCA3), (0x1D173, 0x1D17A), (0xE0000, 0xE0FFF));

        foreach (var (first, last) in FirmDescriptionRules.DefaultIgnorableRanges)
        {
            FirmDescriptionRules.IsDefaultIgnorable(new Rune(first)).Should().BeTrue($"U+{first:X4} starts a range");
            FirmDescriptionRules.IsDefaultIgnorable(new Rune(last)).Should().BeTrue($"U+{last:X4} ends a range");
            FirmDescriptionRules.IsDefaultIgnorable(new Rune(first - 1)).Should().BeFalse($"U+{first - 1:X4} is just before it");
            FirmDescriptionRules.IsDefaultIgnorable(new Rune(last + 1)).Should().BeFalse($"U+{last + 1:X4} is just after it");
        }
    }

    [Fact(DisplayName = "130e-2 fix round 2 F1: the read side keeps its own rule (IsInvisibleControl is unchanged): a variation selector is not Control or Format")]
    public void TheReadSidePredicate_IsUnchanged()
    {
        FirmDescriptionRules.IsInvisibleControl(new Rune(0xFE0F)).Should().BeFalse();
        FirmDescriptionRules.IsInvisibleControl(new Rune(0x034F)).Should().BeFalse();
        FirmDescriptionRules.IsInvisibleControl(new Rune(0x200B)).Should().BeTrue();
    }

    /// <summary>Printable ASCII mapped to its fullwidth form (U+FF01 to U+FF5E); the space is kept.</summary>
    private static string Fullwidth(string ascii) =>
        string.Concat(ascii.Select(c => c is >= '!' and <= '~' ? (char)(c + 0xFEE0) : c));

    [Fact(DisplayName = "130e-1 C4 must-not-fire: a text naming neither, or blank or missing names, is accepted; a name is compared trimmed")]
    public void NotNamingTheRule_IsAccepted()
    {
        FirmDescriptionRules.Validate("Travel is billed at half rate.", RuleSetName, PolicyName).IsValid.Should().BeTrue();
        FirmDescriptionRules.Validate("Travel is billed at half rate.", null, "  ").IsValid.Should().BeTrue();
        FirmDescriptionRules.Validate("Billed under the expenses cap.", "  Expenses  ", null).RefusalCode.Should().Be(FirmDescriptionRules.NamesTheRule);
    }

    // -- One check, every path --

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
