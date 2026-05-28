using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="LiveFactNode"/> (D-P12 task 022). Covers happy path +
/// validation + unsupported-predicate + null-resolution error paths.
/// </summary>
public sealed class LiveFactNodeTests
{
    private static FactArtifact BuildFact(string subject, string predicate, double rawValue) => new()
    {
        Id = $"fact:{subject}:{predicate}",
        Subject = subject,
        Predicate = predicate,
        Value = new Value { Raw = JsonDocument.Parse(rawValue.ToString()).RootElement, DisplayHint = "currency-usd" },
        Evidence = new[] { new EvidenceRef { RefType = "fact-source", Ref = $"dataverse://sprk_matter/{subject}#{predicate}" } },
        AsOf = DateTimeOffset.UtcNow,
        ProducedBy = new ProducedBy { Kind = "query", Id = "query://matter-totalspend", Version = "v1" },
        Scope = new Scope { TenantId = "tenant-x" },
        TenantId = "tenant-x"
    };

    [Fact]
    public void SupportedActionTypes_ContainsLiveFact()
    {
        var node = new LiveFactNode(Mock.Of<ILiveFactResolver>(), NullLogger<LiveFactNode>.Instance);
        node.SupportedActionTypes.Should().ContainSingle().Which.Should().Be(ActionType.LiveFact);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_EmitsFactArtifactWithConfidence1()
    {
        var resolver = new Mock<ILiveFactResolver>();
        var fact = BuildFact("matter:M-1234", "totalSpend", 287500.0);
        resolver.Setup(r => r.ResolveAsync("matter:M-1234", "totalSpend", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fact);

        var node = new LiveFactNode(resolver.Object, NullLogger<LiveFactNode>.Instance);
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.LiveFact,
            """{ "subject": "matter:M-1234", "predicate": "totalSpend" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Confidence.Should().Be(1.0); // Facts are always certain per design.md §2.1.
        var emitted = result.GetData<FactArtifact>();
        emitted.Should().NotBeNull();
        emitted!.Subject.Should().Be("matter:M-1234");
        emitted.Predicate.Should().Be("totalSpend");
        emitted.Evidence.Should().NotBeEmpty(); // D-04 provenance contract.
    }

    [Fact]
    public async Task ExecuteAsync_MissingConfig_ReturnsValidationError()
    {
        var node = new LiveFactNode(Mock.Of<ILiveFactResolver>(), NullLogger<LiveFactNode>.Instance);
        var context = InsightsNodeTestHelpers.CreateContext(ActionType.LiveFact, configJson: null);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("subject", because: "validation should mention missing required field");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedPredicate_ReturnsInvalidConfiguration()
    {
        var resolver = new Mock<ILiveFactResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LiveFactNotSupportedException("matter:M-1234", "nonExistent"));

        var node = new LiveFactNode(resolver.Object, NullLogger<LiveFactNode>.Instance);
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.LiveFact,
            """{ "subject": "matter:M-1234", "predicate": "nonExistent" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
    }

    [Fact]
    public async Task ExecuteAsync_NullResolution_SubjectNotFoundError()
    {
        var resolver = new Mock<ILiveFactResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactArtifact?)null);

        var node = new LiveFactNode(resolver.Object, NullLogger<LiveFactNode>.Instance);
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.LiveFact,
            """{ "subject": "matter:DOES-NOT-EXIST", "predicate": "totalSpend" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }
}
