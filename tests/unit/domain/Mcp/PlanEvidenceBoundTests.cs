using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Unit.Domain.Mcp;

/// <summary>
/// The plan tool's bound on the returned row (<see cref="PlanAndLookupToolSupport.BoundRow"/>, task 025 review, FR-05/FR-11 &lt; 64 KiB):
/// the edge inputs the integration size tests do not reach. Pure function over a record, no host.
/// </summary>
public class PlanEvidenceBoundTests
{
    private static WorklistItemRow Row(string? evidenceRefs, string title = "Title") => new(
        SignalId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Lane: "Decide",
        Status: "Active",
        Title: title,
        SubjectName: "Subject",
        RuleShortName: "Rule",
        WorkType: "chaseReply",
        PastDueWording: "late",
        RaisedOn: DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DueDate: null,
        EvidenceRefs: evidenceRefs,
        PolicyVersionId: null);

    private static int EncodedBytes(string text) => JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

    [Theory]
    [InlineData(null, 0)]
    [InlineData("[]", 0)]
    [InlineData("[{\"kind\":\"email\",\"id\":\"a\"}]", 1)]
    public void BoundRow_WithinEveryBound_ReturnsTheStoredEvidenceUnchanged(string? evidence, int total)
    {
        var bounded = PlanAndLookupToolSupport.BoundRow(Row(evidence));

        bounded.Row!.EvidenceRefs.Should().Be(evidence);
        bounded.EvidenceTotal.Should().Be(total);
        bounded.EvidenceTruncated.Should().BeFalse();
    }

    [Fact]
    public void BoundRow_EvidenceWithHeavyWhitespace_IsReserializedWithinTheBudget()
    {
        // Each line passes the per-line bounds, but the stored string's padding alone exceeds the evidence budget.
        var padding = new string(' ', 400);
        var evidence = "[" + string.Join(",", Enumerable.Range(0, 60).Select(i => $"{padding}{{\"id\":\"{i}\"}}")) + "]";
        EncodedBytes(evidence).Should().BeGreaterThan(PlanAndLookupToolSupport.EvidenceBudgetBytes, "the seed must exceed the budget");

        var bounded = PlanAndLookupToolSupport.BoundRow(Row(evidence));

        EncodedBytes(bounded.Row!.EvidenceRefs!).Should().BeLessThanOrEqualTo(PlanAndLookupToolSupport.EvidenceBudgetBytes);
        bounded.EvidenceTotal.Should().Be(60);
        bounded.EvidenceTruncated.Should().BeFalse("every line was kept; only the whitespace went");
    }

    [Fact]
    public void BoundRow_NestedArrayLines_AreDroppedAndTheStoredStringIsNotReturned()
    {
        var line = "[" + string.Join(",", Enumerable.Repeat("\"" + new string('y', 150) + "\"", 10)) + ",1]";
        var nested = "[" + string.Join(",", Enumerable.Repeat(line, 100)) + "]";
        EncodedBytes(nested).Should().BeGreaterThan(PlanAndLookupToolSupport.EvidenceBudgetBytes, "the seed must exceed the budget");

        var bounded = PlanAndLookupToolSupport.BoundRow(Row(nested));

        bounded.Row!.EvidenceRefs.Should().NotBe(nested);
        EncodedBytes(bounded.Row.EvidenceRefs!).Should().BeLessThanOrEqualTo(PlanAndLookupToolSupport.EvidenceBudgetBytes);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"id\":\"a\"}")]
    [InlineData("\"\\uD800\"")]
    public void BoundRow_NonArrayEvidenceThatFits_IsKeptWithoutThrowing(string evidence)
    {
        var bounded = PlanAndLookupToolSupport.BoundRow(Row(evidence));

        bounded.Row!.EvidenceRefs.Should().Be(evidence);
        bounded.EvidenceTotal.Should().Be(0);
        bounded.EvidenceTruncated.Should().BeFalse();
    }

    [Fact]
    public void BoundRow_NonArrayEvidenceOverTheBudget_IsDroppedAndFlagged()
    {
        var evidence = "x" + new string('y', PlanAndLookupToolSupport.EvidenceBudgetBytes + 10);

        var bounded = PlanAndLookupToolSupport.BoundRow(Row(evidence));

        bounded.Row!.EvidenceRefs.Should().BeNull();
        bounded.EvidenceTruncated.Should().BeTrue();
    }

    [Fact]
    public void BoundRow_NonLatinTitleOfOrdinaryLength_IsNotClipped()
    {
        // 100 Greek letters: 600 encoded bytes under the default encoder.
        var title = string.Concat(Enumerable.Repeat("λ", 100));

        var bounded = PlanAndLookupToolSupport.BoundRow(Row(null, title));

        bounded.Row!.Title.Should().Be(title);
    }
}
