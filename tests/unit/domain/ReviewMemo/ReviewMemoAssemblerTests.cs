using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.ReviewMemo;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for
/// <see cref="ReviewMemoAssembler"/> (ai-advanced-capabilities-agreements-r1 task 050, spec FR-13 /
/// Decision #4). Covers the ONE piece of real assembly logic — the before/after disposition rule —
/// across the task's named scenario: 1 accepted + 1 rejected + 1 untouched flagged section.
/// </summary>
public class ReviewMemoAssemblerTests
{
    private static ReviewMemoSectionInput MakeSection(
        string sectionRef,
        string quotedText,
        string? afterText,
        string assessment = "Materially narrower than the standard.",
        string standardRef = "B5 - Use & disclosure obligations",
        string flaggedClause = "The clause defines Confidential Information only as information marked in writing.",
        string? riskLevel = "High") =>
        new()
        {
            SectionRef = sectionRef,
            QuotedText = quotedText,
            AfterText = afterText,
            Assessment = assessment,
            StandardRef = standardRef,
            FlaggedClause = flaggedClause,
            RiskLevel = riskLevel,
        };

    [Fact]
    public void Assemble_AcceptedRejectedAndUntouchedSections_ProducesCorrectBeforeAfterPerSection()
    {
        // Arrange — the task's named scenario: 1 accepted + 1 rejected + 1 untouched.
        var accepted = MakeSection(
            "Section 4.2, para 2 (p. 3)",
            quotedText: "Confidential Information means information marked Confidential in writing.",
            afterText: "Confidential Information means any information disclosed, whether or not marked, including oral disclosures.");

        var rejected = MakeSection(
            "Section 6.1 (p. 4)",
            quotedText: "This Agreement shall be governed by the laws of Delaware.",
            afterText: null); // reviewer looked at a suggested edit here and discarded it

        var untouched = MakeSection(
            "Section 9.3 (p. 6)",
            quotedText: "Either party may terminate this Agreement upon 30 days written notice.",
            afterText: null); // reviewer never acted on this flagged section at all

        var request = new GenerateReviewMemoRequest
        {
            OverallRisk = "High",
            Sections = new[] { accepted, rejected, untouched },
        };

        // Act
        var memo = ReviewMemoAssembler.Assemble(request);

        // Assert — top-level header
        memo.SchemaVersion.Should().Be(ReviewMemoAssembler.SchemaVersion);
        memo.OverallRisk.Should().Be("High");
        memo.SectionCount.Should().Be(3);
        memo.Sections.Should().HaveCount(3);

        // Accepted: after differs from before — the accepted result is what's shown.
        var acceptedRow = memo.Sections[0];
        acceptedRow.Location.Should().Be("Section 4.2, para 2 (p. 3)");
        acceptedRow.Before.Should().Be(accepted.QuotedText);
        acceptedRow.After.Should().Be(accepted.AfterText);
        acceptedRow.After.Should().NotBe(acceptedRow.Before, "an accepted edit changed the final text");

        // Rejected: after equals before — the rejected outcome IS the original text standing
        // unchanged in the final document (Decision #4 — no separate status enum).
        var rejectedRow = memo.Sections[1];
        rejectedRow.Location.Should().Be("Section 6.1 (p. 4)");
        rejectedRow.Before.Should().Be(rejected.QuotedText);
        rejectedRow.After.Should().Be(rejected.QuotedText, "a rejected suggestion leaves the original text standing");

        // Untouched: after equals before — identical observable outcome to "rejected" by design
        // (Decision #4 does not distinguish rejected from untouched as a separate field).
        var untouchedRow = memo.Sections[2];
        untouchedRow.Location.Should().Be("Section 9.3 (p. 6)");
        untouchedRow.Before.Should().Be(untouched.QuotedText);
        untouchedRow.After.Should().Be(untouched.QuotedText);
    }

    [Fact]
    public void Assemble_EachSection_CarriesWhyAndGoldenRefFromDiscreteFields()
    {
        // Arrange
        var section = MakeSection(
            "Section 3.1",
            quotedText: "Original clause text.",
            afterText: "Revised clause text.",
            assessment: "This deviates materially from the standard.",
            standardRef: "B2 - Term & termination",
            flaggedClause: "The clause states X plainly.",
            riskLevel: "Medium");

        var request = new GenerateReviewMemoRequest
        {
            OverallRisk = "Medium",
            Sections = new[] { section },
        };

        // Act
        var memo = ReviewMemoAssembler.Assemble(request);

        // Assert — why = assessment; golden-ref = {flaggedClause, standardRef} (task prompt: "golden-ref(standardRef + flaggedClause)").
        var row = memo.Sections.Single();
        row.Why.Should().Be("This deviates materially from the standard.");
        row.StandardRef.Should().Be("B2 - Term & termination");
        row.FlaggedClause.Should().Be("The clause states X plainly.");
        row.RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void Assemble_AfterTextEmptyString_TreatedSameAsNull_AfterDefaultsToBefore()
    {
        // Arrange — an empty-string AfterText (vs. null) must not be treated as "accepted with empty text".
        var section = MakeSection("Section 1.1", quotedText: "Original text.", afterText: string.Empty);
        var request = new GenerateReviewMemoRequest { OverallRisk = "Low", Sections = new[] { section } };

        // Act
        var memo = ReviewMemoAssembler.Assemble(request);

        // Assert
        memo.Sections.Single().After.Should().Be("Original text.");
    }

    [Fact]
    public void AssembledMemo_SerializesToJson_WithNoSessionOrLedgerBackReferences()
    {
        // ADR-015 self-containment regression guard: the memo is the ONLY review artifact that
        // survives DELETE /sessions (the Cosmos ledger is erased on delete) — its JSON body must
        // never carry a sessionId/ledgerRef/bindingId that would be dangling after that deletion.
        var section = MakeSection("Section 2.2", quotedText: "Text.", afterText: "Revised text.");
        var request = new GenerateReviewMemoRequest { OverallRisk = "Low", Sections = new[] { section } };

        var memo = ReviewMemoAssembler.Assemble(request);
        var json = JsonSerializer.Serialize(memo);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("sessionId", out _).Should().BeFalse();
        root.TryGetProperty("ledgerRef", out _).Should().BeFalse();
        root.TryGetProperty("bindingId", out _).Should().BeFalse();

        // And the fields that DO travel are exactly the closed FR-13 set (+ header) — parses back
        // to the same shape a later reader (e.g. task 051) would deserialize.
        var roundTripped = JsonSerializer.Deserialize<ReviewMemoDocument>(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Sections.Should().ContainSingle();
        roundTripped.Sections[0].Location.Should().Be("Section 2.2");
    }

    [Fact]
    public void Assemble_MultipleSections_PreservesInputOrder()
    {
        var s1 = MakeSection("Section 1", "Before 1", "After 1");
        var s2 = MakeSection("Section 2", "Before 2", null);
        var s3 = MakeSection("Section 3", "Before 3", "After 3");

        var request = new GenerateReviewMemoRequest { OverallRisk = "Medium", Sections = new[] { s1, s2, s3 } };

        var memo = ReviewMemoAssembler.Assemble(request);

        memo.Sections.Select(s => s.Location).Should().ContainInOrder("Section 1", "Section 2", "Section 3");
    }

    // -----------------------------------------------------------------------
    // R8 §GAPS-5 decision D3 (2026-09-07) — partial grounding is RENDERED, never dropped.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The model does not always ground every finding, which is why the CLIENT's finding type has
    /// always had these four optional. The server contract declared them <c>required</c>, so a
    /// partially-grounded finding could not be sent at all. D3 relaxed them and renders an em dash:
    /// silently excluding a flagged clause from a review summary is a worse failure than showing it
    /// with one cell unfilled.
    /// </summary>
    [Fact]
    public void Assemble_SectionMissingEveryOptionalGrounding_RendersPlaceholdersAndKeepsTheSection()
    {
        var request = new GenerateReviewMemoRequest
        {
            OverallRisk = "Medium",
            Sections = new[]
            {
                new ReviewMemoSectionInput { QuotedText = "A clause with no other grounding." },
            },
        };

        var memo = ReviewMemoAssembler.Assemble(request);

        memo.Sections.Should().ContainSingle("a partially-grounded finding is never dropped");
        var section = memo.Sections[0];
        section.Before.Should().Be("A clause with no other grounding.");
        section.After.Should().Be(section.Before, "no afterText ⇒ the original text stands (decision D2)");
        section.Location.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
        section.Why.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
        section.FlaggedClause.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
        section.StandardRef.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
    }

    /// <summary>An EMPTY cell reads as a rendering bug; the placeholder must be a visible mark.</summary>
    [Fact]
    public void Assemble_WhitespaceOnlyGrounding_IsTreatedAsAbsentNotAsContent()
    {
        var request = new GenerateReviewMemoRequest
        {
            OverallRisk = "Low",
            Sections = new[]
            {
                new ReviewMemoSectionInput { QuotedText = "Clause.", SectionRef = "   ", StandardRef = "" },
            },
        };

        var memo = ReviewMemoAssembler.Assemble(request);

        memo.Sections[0].Location.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
        memo.Sections[0].StandardRef.Should().Be(ReviewMemoAssembler.MissingValuePlaceholder);
        ReviewMemoAssembler.MissingValuePlaceholder.Should().NotBeNullOrWhiteSpace(
            "an empty cell reads as a rendering bug — the absence must be visibly marked");
    }

    /// <summary>Present values are never touched by the placeholder logic.</summary>
    [Fact]
    public void Assemble_FullyGroundedSection_IsUnaffectedByThePlaceholderRule()
    {
        var request = new GenerateReviewMemoRequest
        {
            OverallRisk = "High",
            Sections = new[]
            {
                new ReviewMemoSectionInput
                {
                    QuotedText = "Assignment without consent.",
                    SectionRef = "4.2",
                    Assessment = "Deviates from the firm standard.",
                    StandardRef = "B5",
                    FlaggedClause = "The clause permits assignment.",
                    RiskLevel = "High",
                },
            },
        };

        var memo = ReviewMemoAssembler.Assemble(request);

        var section = memo.Sections[0];
        section.Location.Should().Be("4.2");
        section.Why.Should().Be("Deviates from the firm standard.");
        section.StandardRef.Should().Be("B5");
        section.FlaggedClause.Should().Be("The clause permits assignment.");
    }
}
