using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Pure domain tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for the two changes the 2026-09-07
/// client wiring required of <see cref="ComposeSummaryPageGenerator"/>.
///
/// <para>The Summary Page had been server-complete but unreachable since nda-r1 task 041: no HTTP body
/// property, no client sender. Wiring it exposed that its input type still modelled the PRE-FR-05-split
/// finding shape, so the digest would have rendered the wrong half of every current finding — the same
/// defect §GAPS-5 Phase 1 fixed in the review-summary panel, about to reappear in a second place.</para>
/// </summary>
public class ComposeSummaryPageGeneratorVintageTests
{
    private const string FlaggedClause = "The agreement permits assignment without the counterparty consenting.";
    private const string Assessment = "This deviates from the firm standard, which requires prior written consent.";

    /// <summary>The exact blob both client projections compose for a post-split payload: the discrete
    /// fields joined by a blank line, with NO "Grounded fact —"/"Judgment —" markers.</summary>
    private const string ComposedExplanation = FlaggedClause + "\n\n" + Assessment;

    private static string OverviewLine(NdaReviewSummaryPageInput input)
    {
        var blocks = ComposeSummaryPageGenerator.Build(input);
        // The bullet lines are the paragraphs whose first run is the literal "• ".
        var bullet = blocks.Single(b => b.Runs.Count > 0 && b.Runs[0].Text == "• ");
        return string.Concat(bullet.Runs.Select(r => r.Text));
    }

    private static NdaReviewSummaryPageInput One(NdaReviewFlaggedSectionInput section) =>
        new("High", new[] { section });

    /// <summary>
    /// THE REGRESSION GUARD. The overview line is a ONE-LINE digest truncated at 220 chars. On a
    /// post-split payload `explanation` is the composed blob, which STARTS with the grounded fact — so
    /// rendering it put the fact in the digest and pushed the judgment past the truncation. The judgment
    /// is what belongs in a one-line summary.
    /// </summary>
    [Fact]
    public void OverviewLine_PrefersTheDiscreteAssessment_NotTheComposedExplanationBlob()
    {
        var line = OverviewLine(One(new NdaReviewFlaggedSectionInput(
            SectionRef: "4.2",
            QuotedText: "Either party may assign…",
            RiskLevel: "High",
            Explanation: ComposedExplanation,
            StandardRef: "B5",
            FlaggedClause: FlaggedClause,
            Assessment: Assessment)));

        line.Should().Contain(Assessment, "the judgment is the digest");
        line.Should().NotContain(FlaggedClause, "the grounded fact belongs on the comment thread, not the one-line overview");
    }

    /// <summary>A legacy (pre-split) payload carries no discrete fields and must still render its "why".</summary>
    [Fact]
    public void OverviewLine_FallsBackToExplanation_ForALegacyPayload()
    {
        var line = OverviewLine(One(new NdaReviewFlaggedSectionInput(
            SectionRef: "4.2",
            QuotedText: "Either party may assign…",
            RiskLevel: "High",
            Explanation: "Grounded fact — X. Judgment — Y.",
            StandardRef: "B5")));

        line.Should().Contain("Grounded fact — X. Judgment — Y.");
    }

    /// <summary>
    /// Every field is optional now (the same D3 relaxation the Review Summary took). Absent parts are
    /// OMITTED rather than em-dashed: this is running prose, where "(Standard: —)" is noise — unlike
    /// <c>ReviewMemoAssembler</c>, which fills a table CELL where a blank reads as a rendering bug.
    /// A missing locator still needs a word, or the line starts mid-sentence.
    /// </summary>
    [Fact]
    public void OverviewLine_OmitsAbsentParts_ButAlwaysNamesRiskAndLocator()
    {
        var line = OverviewLine(One(new NdaReviewFlaggedSectionInput(QuotedText: "A clause.")));

        line.Should().Contain("[Unrated]");
        line.Should().Contain("Unreferenced");
        line.Should().NotContain("Standard:", "an absent standard clause is self-evident in prose");
        line.Should().NotContain("—", "with no 'why' there is nothing for the dash to introduce");
    }

    [Fact]
    public void OverviewLine_EmitsTheStandardClause_WhenPresent()
    {
        var line = OverviewLine(One(new NdaReviewFlaggedSectionInput(
            SectionRef: "4.2", RiskLevel: "High", Assessment: "Deviates.", StandardRef: "B5 — Assignment")));

        line.Should().Contain("(Standard: B5 — Assignment)");
    }

    /// <summary>
    /// The deliberate asymmetry with the revision report, pinned so it is not "fixed" into symmetry:
    /// an empty findings list still produces a page, because a clean NDA is itself a finding. Returning
    /// nothing would turn "we reviewed it and it is clean" into "we did not review it".
    /// </summary>
    [Fact]
    public void ACleanReview_StillProducesAPage_UnlikeTheRevisionReport()
    {
        var blocks = ComposeSummaryPageGenerator.Build(
            new NdaReviewSummaryPageInput("Low", Array.Empty<NdaReviewFlaggedSectionInput>()));

        blocks.Should().NotBeEmpty();
        string.Concat(blocks.SelectMany(b => b.Runs).Select(r => r.Text))
            .Should().Contain("No material deviations");
    }
}
