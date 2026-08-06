using FluentAssertions;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pure domain-logic tests (ADR-038 unit) for the Job B propose shaping helpers (task 030, FR-09/NFR-06):
/// <see cref="EmailUpdateFieldCoercion"/> (field-type coercion — the drop-if-uncoercible gate) and
/// <see cref="CitationVerifier"/> (verify-cited-text — the drop-if-unlocatable gate). These two gates are
/// the load-bearing trust properties of the propose path; they are exercised here in isolation (no mocks,
/// no I/O) so a regression in either is caught at the branch level.
/// </summary>
public class EmailProposalShapingTests
{
    // ── EmailUpdateFieldTypes.FromOptionSet — the as-built sprk_fieldtype integers ──

    [Theory]
    [InlineData(100000000, "Text")]
    [InlineData(100000001, "Lookup")]
    [InlineData(100000002, "OptionSet")]
    [InlineData(100000003, "Number")]
    [InlineData(100000004, "DateTime")]
    [InlineData(100000005, "Boolean")]
    [InlineData(100000006, "Memo")]
    [InlineData(100000007, "Currency")]
    public void FromOptionSet_ForAsBuiltInteger_ReturnsCanonicalLabel(int value, string expected) =>
        EmailUpdateFieldTypes.FromOptionSet(value).Should().Be(expected);

    [Fact]
    public void FromOptionSet_ForUnknownInteger_ReturnsNull_SoTheRowIsSkippedNeverGuessed() =>
        EmailUpdateFieldTypes.FromOptionSet(999999).Should().BeNull();

    // ── Coercion: Number / Currency ──

    [Theory]
    [InlineData("Number", "1234.50", "1234.50")]
    [InlineData("Number", "$1,234.50", "1234.50")]
    [InlineData("Currency", "€ 2 000", "2000")]
    public void TryCoerce_NumericTypes_StripsSymbolsAndNormalizes(string fieldType, string raw, string expected)
    {
        EmailUpdateFieldCoercion.TryCoerce(fieldType, raw, out var coerced).Should().BeTrue();
        coerced.Should().Be(expected);
    }

    [Fact]
    public void TryCoerce_NumberWithNonNumericValue_ReturnsFalse_SoTheProposalIsDropped()
    {
        EmailUpdateFieldCoercion.TryCoerce("Number", "sometime next week", out var coerced).Should().BeFalse();
        coerced.Should().BeNull();
    }

    // ── Coercion: Boolean ──

    [Theory]
    [InlineData("true", "true")]
    [InlineData("Yes", "true")]
    [InlineData("1", "true")]
    [InlineData("false", "false")]
    [InlineData("No", "false")]
    [InlineData("0", "false")]
    public void TryCoerce_Boolean_NormalizesToTrueFalse(string raw, string expected)
    {
        EmailUpdateFieldCoercion.TryCoerce("Boolean", raw, out var coerced).Should().BeTrue();
        coerced.Should().Be(expected);
    }

    [Fact]
    public void TryCoerce_BooleanWithAmbiguousValue_ReturnsFalse()
    {
        EmailUpdateFieldCoercion.TryCoerce("Boolean", "maybe", out _).Should().BeFalse();
    }

    // ── Coercion: DateTime ──

    [Theory]
    [InlineData("August 15, 2026", "2026-08-15")]
    [InlineData("2026-08-15", "2026-08-15")]
    [InlineData("8/15/2026", "2026-08-15")]
    public void TryCoerce_DateOnly_NormalizesToIsoDate(string raw, string expected)
    {
        EmailUpdateFieldCoercion.TryCoerce("DateTime", raw, out var coerced).Should().BeTrue();
        coerced.Should().Be(expected);
    }

    [Fact]
    public void TryCoerce_DateTimeWithNonDateValue_ReturnsFalse_SoTheProposalIsDropped()
    {
        EmailUpdateFieldCoercion.TryCoerce("DateTime", "whenever the court decides", out _).Should().BeFalse();
    }

    // ── Coercion: Text / Memo / OptionSet / Lookup preserve the (trimmed) label ──

    [Theory]
    [InlineData("Text", "  Closed  ", "Closed")]
    [InlineData("Memo", "A longer note.", "A longer note.")]
    [InlineData("OptionSet", "High", "High")]
    [InlineData("Lookup", "Acme Corp", "Acme Corp")]
    public void TryCoerce_LabelTypes_PreserveTheTrimmedLabelFor031ToResolve(string fieldType, string raw, string expected)
    {
        EmailUpdateFieldCoercion.TryCoerce(fieldType, raw, out var coerced).Should().BeTrue();
        coerced.Should().Be(expected);
    }

    [Fact]
    public void TryCoerce_BlankValue_ReturnsFalse()
    {
        EmailUpdateFieldCoercion.TryCoerce("Text", "   ", out _).Should().BeFalse();
    }

    [Fact]
    public void TryCoerce_UnknownFieldType_ReturnsFalse_NeverStoresAnIllTypedProposal()
    {
        EmailUpdateFieldCoercion.TryCoerce("Geography", "somewhere", out _).Should().BeFalse();
    }

    // ── Verify-cited-text (NFR-06) ──

    [Fact]
    public void IsCitedTextPresent_WhenQuoteIsAVerbatimSpan_ReturnsTrue()
    {
        var source = CitationVerifier.BuildSourceText(
            subject: "Closing moved",
            bodyText: "Counsel, the closing has been moved to August 15, 2026. Update your calendars.",
            attachmentText: null);

        CitationVerifier.IsCitedTextPresent(source, "the closing has been moved to August 15, 2026")
            .Should().BeTrue();
    }

    [Fact]
    public void IsCitedTextPresent_ToleratesWhitespaceAndCaseDifferences()
    {
        var source = CitationVerifier.BuildSourceText(null, "The  closing\nhas been moved.", null);

        CitationVerifier.IsCitedTextPresent(source, "the closing has been moved")
            .Should().BeTrue("whitespace-collapse + case-fold normalization must not defeat a genuine verbatim quote");
    }

    [Fact]
    public void IsCitedTextPresent_WhenQuoteIsNotInTheSource_ReturnsFalse()
    {
        var source = CitationVerifier.BuildSourceText(null, "Please advise on strategy.", null);

        CitationVerifier.IsCitedTextPresent(source, "the closing has been moved to August 15, 2026")
            .Should().BeFalse("a fabricated citation must not verify — the proposal is dropped");
    }

    [Fact]
    public void IsCitedTextPresent_CanVerifyAgainstAttachmentText()
    {
        var source = CitationVerifier.BuildSourceText(
            subject: "Office Action",
            bodyText: "See attached.",
            attachmentText: "The response deadline is set for September 30, 2026.");

        CitationVerifier.IsCitedTextPresent(source, "response deadline is set for September 30, 2026")
            .Should().BeTrue("attachment text is part of the citable source (NFR-06 attachment-grounded extraction)");
    }

    [Fact]
    public void IsCitedTextPresent_WithEmptyQuoteOrSource_ReturnsFalse()
    {
        CitationVerifier.IsCitedTextPresent("some source", "   ").Should().BeFalse();
        CitationVerifier.IsCitedTextPresent(null, "some quote").Should().BeFalse();
    }
}
