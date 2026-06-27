using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Routing;

/// <summary>
/// ADR-032 P3 Fail-fast Null-Object verification for
/// <see cref="NullInsightsIntentClassifier"/> (Wave E2 task 041 / FR-05).
/// </summary>
/// <remarks>
/// Mirrors the <c>NullRagService</c> + <c>NullBriefingAi</c> test pattern. The Null-Object
/// MUST throw <see cref="FeatureDisabledException"/> with a stable ErrorCode so endpoint
/// catch sites can convert to 503 ProblemDetails via
/// <see cref="FeatureDisabledResults.AsFeatureDisabled503"/>. A P2 quiet no-op (returning
/// a default routing decision) would mis-attribute every query to the RAG path under
/// disabled state — forbidden for query services per ADR-032.
/// </remarks>
public class NullInsightsIntentClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_Throws_FeatureDisabledException()
    {
        // Arrange
        var sut = new NullInsightsIntentClassifier(NullLogger<InsightsIntentClassifier>.Instance);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(() =>
            sut.ClassifyAsync(
                "What will this matter cost?",
                new IntentClassificationContext("matter", "tid-1"),
                CancellationToken.None));

        ex.ErrorCode.Should().Be(NullInsightsIntentClassifier.ErrorCode);
        ex.ErrorCode.Should().Be("ai.intent-classification.disabled",
            "errorCode must be stable across releases — clients switch on this string");
        ex.Message.Should().Contain("Insights intent classification");
    }

    [Fact]
    public async Task ClassifyAsync_NullContext_StillThrows()
    {
        // Arrange — defensive: even with no context, the Null-Object must fail-fast
        var sut = new NullInsightsIntentClassifier(NullLogger<InsightsIntentClassifier>.Instance);

        // Act + Assert
        await Assert.ThrowsAsync<FeatureDisabledException>(() =>
            sut.ClassifyAsync("query", context: null, CancellationToken.None));
    }


    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new NullInsightsIntentClassifier(logger: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
