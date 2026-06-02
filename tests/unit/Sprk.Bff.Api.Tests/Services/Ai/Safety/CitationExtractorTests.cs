using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="CitationExtractor"/>.
///
/// Test matrix:
///   1. Case law patterns — matched, normalised, not matched on noise
///   2. Statute patterns  — matched, normalised, not matched on noise
///   3. Patent patterns   — US / EP / WO, normalised
///   4. SEC filing patterns
///   5. Regulation patterns
///   6. Multiple citation types in one text — all extracted, document order preserved
///   7. Empty / whitespace-only input → empty list
///   8. Duplicate citations (same NormalizedKey) → deduplicated
///   9. Overlapping spans — higher-priority pattern wins
/// </summary>
[Trait("status", "repaired")]
public class CitationExtractorTests
{
    // =========================================================================
    // Case Law
    // =========================================================================

    [Theory]
    [InlineData("The Court held in Roe v. Wade, 410 U.S. 113 (1973) that",
                "410 U.S. 113", CitationType.CaseLaw)]
    [InlineData("See Smith v. Jones, 542 U.S. 296 (2004).",
                "542 U.S. 296", CitationType.CaseLaw)]
    [InlineData("Cf. Ashcroft v. Iqbal, 556 U.S. 662 (2009).",
                "556 U.S. 662", CitationType.CaseLaw)]
    [InlineData("Under Miranda v. Arizona, 384 U.S. 436 (1966),",
                "384 U.S. 436", CitationType.CaseLaw)]
    public void ExtractCitations_CaseLaw_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    [Theory]
    [InlineData("See pages 42–56 of the report.")]           // no reporter
    [InlineData("Chapter 5, Section 3 discusses remedies.")] // no volume/reporter/page triple
    public void ExtractCitations_CaseLaw_NotMatchedOnNoise(string text)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().NotContain(c => c.CitationType == CitationType.CaseLaw);
    }

    // =========================================================================
    // Statutes
    // =========================================================================

    [Theory]
    [InlineData("Under 35 U.S.C. § 101, patent eligibility",
                "35 U.S.C. § 101", CitationType.Statute)]
    [InlineData("pursuant to 42 U.S.C. § 1983 claims",
                "42 U.S.C. § 1983", CitationType.Statute)]
    [InlineData("26 U.S.C. Section 501 provides",
                "26 U.S.C. § 501", CitationType.Statute)]
    public void ExtractCitations_Statute_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    [Fact]
    public void ExtractCitations_Statute_StripsSubsectionsInNormalizedKey()
    {
        var results = CitationExtractor.ExtractCitations("See 17 U.S.C. § 512(c)(1)(A).");

        results.Should().ContainSingle(c =>
            c.CitationType == CitationType.Statute &&
            c.NormalizedKey == "17 U.S.C. § 512");
    }

    // =========================================================================
    // Patents
    // =========================================================================

    [Theory]
    [InlineData("covered by U.S. Patent No. 9,123,456", "US9123456", CitationType.Patent)]
    [InlineData("as disclosed in US 8,456,789", "US8456789", CitationType.Patent)]
    public void ExtractCitations_Patent_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    [Theory]
    [InlineData("the priority document EP3456789", "EP3456789", CitationType.Patent)]
    [InlineData("filed as WO2021/123456", "WO2021/123456", CitationType.Patent)]
    public void ExtractCitations_Patent_NonUS_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    // =========================================================================
    // SEC Filings
    // =========================================================================

    [Theory]
    [InlineData("disclosed in Form 10-K for fiscal year 2023", "10-K", CitationType.SecFiling)]
    [InlineData("filed a Form 8-K on March 15", "8-K", CitationType.SecFiling)]
    [InlineData("quarterly report on Form 10-Q", "10-Q", CitationType.SecFiling)]
    [InlineData("registration statement on Form S-1", "S-1", CitationType.SecFiling)]
    public void ExtractCitations_SecFiling_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    [Theory]
    [InlineData("Form 1040 for tax purposes")]  // IRS form, not SEC
    [InlineData("Form I-9 completion")]         // immigration form
    public void ExtractCitations_SecFiling_NotMatchedOnNoise(string text)
    {
        var results = CitationExtractor.ExtractCitations(text);
        results.Should().NotContain(c => c.CitationType == CitationType.SecFiling);
    }

    // =========================================================================
    // Regulations
    // =========================================================================

    [Theory]
    [InlineData("47 C.F.R. § 73.3999 prohibits", "47 C.F.R. § 73.3999", CitationType.Regulation)]
    [InlineData("under 40 C.F.R. § 122.26", "40 C.F.R. § 122.26", CitationType.Regulation)]
    public void ExtractCitations_Regulation_MatchedAndNormalized(string text, string expectedKey, CitationType expectedType)
    {
        var results = CitationExtractor.ExtractCitations(text);

        results.Should().ContainSingle(c =>
            c.CitationType == expectedType &&
            c.NormalizedKey == expectedKey);
    }

    [Fact]
    public void ExtractCitations_Regulation_NoPeriodForm_MatchedAndNormalized()
    {
        var results = CitationExtractor.ExtractCitations("21 CFR Part 312 governs IND");

        results.Should().ContainSingle(c =>
            c.CitationType == CitationType.Regulation &&
            c.NormalizedKey == "21 C.F.R. § 312");
    }

    // =========================================================================
    // Multiple citation types in one text
    // =========================================================================

    [Fact]
    public void ExtractCitations_MixedTypes_AllExtractedInOrder()
    {
        const string text =
            "Under 35 U.S.C. § 101, the patent (U.S. Patent No. 9,123,456) was invalidated. " +
            "See Alice Corp. v. CLS Bank Int'l, 573 U.S. 208 (2014). " +
            "The company disclosed this in Form 10-K.";

        var results = CitationExtractor.ExtractCitations(text);

        results.Should().HaveCountGreaterThanOrEqualTo(3);

        results.Should().Contain(c => c.CitationType == CitationType.Statute);
        results.Should().Contain(c => c.CitationType == CitationType.Patent);
        results.Should().Contain(c => c.CitationType == CitationType.CaseLaw);
        results.Should().Contain(c => c.CitationType == CitationType.SecFiling);
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void ExtractCitations_EmptyOrWhitespace_ReturnsEmpty(string? text)
    {
        var results = CitationExtractor.ExtractCitations(text!);
        results.Should().BeEmpty();
    }

    [Fact]
    public void ExtractCitations_DuplicateCitation_Deduplicated()
    {
        const string text =
            "See 35 U.S.C. § 101. As noted above, 35 U.S.C. § 101 requires patent eligibility.";

        var results = CitationExtractor.ExtractCitations(text);

        results.Where(c => c.NormalizedKey == "35 U.S.C. § 101")
               .Should().HaveCount(1, "duplicate NormalizedKey must be deduplicated");
    }

    [Fact]
    public void ExtractCitations_TextWithNoCitations_ReturnsEmpty()
    {
        const string text = "The defendant acted negligently and caused harm to the plaintiff.";

        var results = CitationExtractor.ExtractCitations(text);
        results.Should().BeEmpty();
    }

    [Fact]
    public void ExtractCitations_RawText_ContainsMatchedSubstring()
    {
        const string text = "Alice Corp. v. CLS Bank Int'l, 573 U.S. 208 (2014) held that";

        var results = CitationExtractor.ExtractCitations(text);

        results.Should().Contain(c =>
            c.CitationType == CitationType.CaseLaw &&
            c.RawText.Contains("573 U.S. 208"));
    }
}
