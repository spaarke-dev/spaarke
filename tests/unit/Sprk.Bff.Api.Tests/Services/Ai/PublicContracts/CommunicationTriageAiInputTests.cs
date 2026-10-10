using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// spaarke-ontology-platform-r1 D-117(b): the TRIAGE-EMAIL Action receives rung 5's taxonomy choice as
/// <c>classification.triageCategory</c> (its hint), and an input without it is byte-identical to the pre-D-117 input.
/// </summary>
public class CommunicationTriageAiInputTests
{
    private static CommunicationTriageRequest Request(string? triageCategory) => new()
    {
        Classification = new CommunicationClassificationResult
        {
            Category = "invoice",
            Urgency = null,
            CandidateRecordTypes = new[] { "sprk_invoice" },
            Rationale = null,
            TriageCategory = triageCategory,
        },
        Subject = "Subject",
        BodyText = "Body",
        TenantId = "t",
    };

    [Fact]
    public void BuildInput_WithTriageCategory_AddsItToClassification_KeepingEveryExistingField()
    {
        var withHint = CommunicationTriageAi.BuildInput(Request("Some taxonomy row"));
        var without = CommunicationTriageAi.BuildInput(Request(null));

        var classification = withHint.GetProperty("classification");
        classification.GetProperty("triageCategory").GetString().Should().Be("Some taxonomy row");
        classification.GetProperty("category").GetString().Should().Be("invoice");

        foreach (var field in without.GetProperty("classification").EnumerateObject())
        {
            classification.GetProperty(field.Name).GetRawText().Should().Be(field.Value.GetRawText(),
                $"'{field.Name}' is unchanged by the hint (including null values)");
        }
        withHint.GetProperty("message").GetRawText().Should().Be(without.GetProperty("message").GetRawText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void BuildInput_WithoutTriageCategory_IsThePreD117Shape(string? triageCategory)
    {
        var input = CommunicationTriageAi.BuildInput(Request(triageCategory));

        input.GetProperty("classification").TryGetProperty("triageCategory", out _).Should().BeFalse();
        input.GetRawText().Should().Be(
            """{"classification":{"candidateRecordTypes":["sprk_invoice"],"category":"invoice","urgency":null,"obligations":[],"suggestedActions":[],"privilegeFlagged":false,"rationale":null},"message":{"subject":"Subject","bodyText":"Body"}}""");
    }
}
