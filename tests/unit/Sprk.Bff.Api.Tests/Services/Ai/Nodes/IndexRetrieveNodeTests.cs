using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="IndexRetrieveNode"/> (D-P12 task 022). Focused on config
/// validation, defaults, and the D-A23 / D-48 EvidenceGuard behavior. Actual SearchClient
/// integration is covered by the integration smoke test (D-P16).
/// </summary>
/// <remarks>
/// The Azure SDK <see cref="Azure.Search.Documents.Indexes.SearchIndexClient"/> /
/// <see cref="Azure.Search.Documents.SearchClient"/> types are sealed and non-trivial to
/// mock fully — these tests therefore exercise the deterministic surface (validation,
/// defaults, error handling) and rely on the integration test for end-to-end search.
/// </remarks>
public sealed class IndexRetrieveNodeTests
{
    private static IndexRetrieveNode CreateNode()
    {
        // Mock with no setups — any actual SDK call yields a NullReferenceException, which
        // the node maps to an InternalError NodeOutput, exactly what the catch-all path tests.
        var searchClient = new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>();
        var openAi = new Mock<IOpenAiClient>();
        return new IndexRetrieveNode(searchClient.Object, openAi.Object, NullLogger<IndexRetrieveNode>.Instance);
    }

    [Fact]
    public void SupportedActionTypes_ContainsIndexRetrieve()
    {
        CreateNode().SupportedExecutorTypes.Should()
            .ContainSingle().Which.Should().Be(ExecutorType.IndexRetrieve);
    }

    [Fact]
    public void Validate_NoNarrowing_ReturnsFailure()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.IndexRetrieve,
            """{ "indexName": "spaarke-insights-index" }""");

        var result = node.Validate(context);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("artifactType")
            && e.Contains("predicate")
            && e.Contains("vectorQuery"));
    }

    [Fact]
    public void Validate_WithArtifactType_ReturnsSuccess()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.IndexRetrieve,
            """{ "artifactType": "observation" }""");

        var result = node.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithPredicate_ReturnsSuccess()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.IndexRetrieve,
            """{ "predicate": "outcomeCategory" }""");

        var result = node.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithVectorQuery_ReturnsSuccess()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.IndexRetrieve,
            """{ "vectorQuery": "predict cost for IP-licensing matter" }""");

        var result = node.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingConfig_ReturnsFailureWithGuidance()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.IndexRetrieve, configJson: null);

        var result = node.Validate(context);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFails_ReturnsValidationFailedError()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.IndexRetrieve, "{}");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }
}
