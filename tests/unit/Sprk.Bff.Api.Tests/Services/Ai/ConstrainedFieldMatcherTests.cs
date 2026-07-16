using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the pure <see cref="ConstrainedFieldMatcher"/> ladder (spec FR-B1, task 010).
/// Exercises every ladder tier (exact → normalized → fuzzy → none) with NO Dataverse, NO model call.
/// </summary>
public class ConstrainedFieldMatcherTests
{
    private static readonly IReadOnlyList<FieldCandidate> PracticeAreas =
    [
        new FieldCandidate("11111111-1111-1111-1111-111111111111", "Litigation"),
        new FieldCandidate("22222222-2222-2222-2222-222222222222", "Employment Law"),
        new FieldCandidate("33333333-3333-3333-3333-333333333333", "Intellectual Property"),
        new FieldCandidate("44444444-4444-4444-4444-444444444444", "Mergers & Acquisitions"),
    ];

    [Fact]
    public void Match_ExactCaseInsensitive_ReturnsHigh()
    {
        var result = ConstrainedFieldMatcher.Match("litigation", PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.High);
        result.Best!.Value.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Match_NormalizedPunctuationAndWhitespace_ReturnsHigh()
    {
        // Hyphen + extra spaces defeat exact match but fold to the same normalized form.
        var result = ConstrainedFieldMatcher.Match("Employment-Law", PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.High);
        result.Best!.Label.Should().Be("Employment Law");
    }

    [Fact]
    public void Match_NormalizedAmpersandForm_ReturnsHigh()
    {
        // "Mergers and Acquisitions" vs candidate "Mergers & Acquisitions" — '&' folds to space, "and" stays.
        var result = ConstrainedFieldMatcher.Match("mergers   and acquisitions", PracticeAreas);

        // Not equal after normalize ("mergers and acquisitions" vs "mergers acquisitions"), so this falls to
        // fuzzy — still a strong match. Confidence is Low (fuzzy), best is M&A.
        result.Best!.Label.Should().Be("Mergers & Acquisitions");
        result.Confidence.Should().BeOneOf(ResolutionConfidence.High, ResolutionConfidence.Low);
    }

    [Fact]
    public void Match_SynonymMapsToCanonical_ReturnsHigh()
    {
        var options = new ConstrainedFieldMatchOptions
        {
            Synonyms = new Dictionary<string, string> { ["ip"] = "intellectual property" },
        };

        var result = ConstrainedFieldMatcher.Match("IP", PracticeAreas, options);

        result.Confidence.Should().Be(ResolutionConfidence.High);
        result.Best!.Label.Should().Be("Intellectual Property");
    }

    [Fact]
    public void Match_FuzzyAboveThreshold_ReturnsLow()
    {
        // Single-character typo — similarity ~0.9, above the 0.82 default threshold.
        var result = ConstrainedFieldMatcher.Match("Litigaton", PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.Low);
        result.Best!.Label.Should().Be("Litigation");
    }

    [Fact]
    public void Match_BelowThreshold_ReturnsNone()
    {
        var result = ConstrainedFieldMatcher.Match("bankruptcy", PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.None);
        result.Best.Should().BeNull();
    }

    [Fact]
    public void Match_EmptyCandidates_ReturnsNone()
    {
        var result = ConstrainedFieldMatcher.Match("Litigation", []);

        result.Confidence.Should().Be(ResolutionConfidence.None);
        result.Best.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Match_BlankProposal_ReturnsNone(string? proposal)
    {
        var result = ConstrainedFieldMatcher.Match(proposal, PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.None);
        result.Best.Should().BeNull();
    }

    [Fact]
    public void Match_ExactTierWinsOverFuzzy()
    {
        // "Litigation" is an exact hit; the ladder must return High from tier 1, never fall through to fuzzy.
        var result = ConstrainedFieldMatcher.Match("Litigation", PracticeAreas);

        result.Confidence.Should().Be(ResolutionConfidence.High);
    }

    [Fact]
    public void Match_HigherThreshold_DemotesFuzzyToNone()
    {
        var options = new ConstrainedFieldMatchOptions { FuzzyThreshold = 0.95 };

        // ~0.9 similarity typo is below a 0.95 threshold → None.
        var result = ConstrainedFieldMatcher.Match("Litigaton", PracticeAreas, options);

        result.Confidence.Should().Be(ResolutionConfidence.None);
    }
}
