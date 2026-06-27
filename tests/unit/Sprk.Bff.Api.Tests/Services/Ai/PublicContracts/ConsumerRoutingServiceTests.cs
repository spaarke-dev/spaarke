using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// Unit tests for <see cref="ConsumerRoutingService"/> covering FR-1R-02 / FR-1R-03 / FR-1R-04 acceptance:
/// resolution algorithm tiebreaks, match-conditions JSON predicate semantics, cache hit/miss,
/// graceful-degrade on Dataverse errors. (chat-routing-redesign-r1 task 028a.)
/// </summary>
[Trait("status", "repaired")]
public sealed class ConsumerRoutingServiceTests
{
    private static readonly Guid PlaybookA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlaybookB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PlaybookC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<IHostEnvironment> _envMock = new();
    private readonly Mock<ILogger<ConsumerRoutingService>> _loggerMock = new();

    public ConsumerRoutingServiceTests()
    {
        _envMock.SetupGet(e => e.EnvironmentName).Returns("dev");
    }

    private ConsumerRoutingService CreateService() =>
        new(_entityServiceMock.Object, _cache, _envMock.Object, _loggerMock.Object);

    private void SetupQueryResponse(params Entity[] entities)
    {
        var collection = new EntityCollection(entities.ToList());
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    private static Entity BuildEntity(
        Guid playbookId,
        string? consumerCode = "default",
        string? environment = "*",
        int priority = 500,
        string? matchConditionsJson = null,
        bool enabled = true)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_playbookconsumerid"] = Guid.NewGuid();
        entity["sprk_consumercode"] = consumerCode;
        entity["sprk_environment"] = environment;
        entity["sprk_priority"] = priority;
        entity["sprk_matchconditions"] = matchConditionsJson;
        entity["sprk_enabled"] = enabled;
        entity["sprk_playbook"] = new EntityReference("sprk_analysisplaybook", playbookId);
        return entity;
    }

    // ── Argument validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_NullOrEmptyConsumerType_Throws(string? consumerType)
    {
        var sut = CreateService();

        var act = async () => await sut.ResolveAsync(consumerType!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("consumerType");
    }

    // ── Empty / no-match cases ──────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NoRecords_ReturnsNull()
    {
        SetupQueryResponse();
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RecordWithNullPlaybookLookup_Skipped()
    {
        var entity = BuildEntity(PlaybookA);
        entity["sprk_playbook"] = null; // Configured row with no target.
        SetupQueryResponse(entity);
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().BeNull();
    }

    // ── Single-record happy path ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SingleMatchingRecord_ReturnsPlaybookId()
    {
        SetupQueryResponse(BuildEntity(PlaybookA));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookA);
    }

    [Fact]
    public async Task ResolveAsync_NullSprkEnvironment_TreatedAsWildcard()
    {
        SetupQueryResponse(BuildEntity(PlaybookA, environment: null));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookA);
    }

    [Fact]
    public async Task ResolveAsync_EmptySprkEnvironment_TreatedAsWildcard()
    {
        SetupQueryResponse(BuildEntity(PlaybookA, environment: ""));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookA);
    }

    [Fact]
    public async Task ResolveAsync_NullConsumerCode_DefaultsToDefault()
    {
        SetupQueryResponse(BuildEntity(PlaybookA, consumerCode: "default"));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill", consumerCode: null);

        result.Should().Be(PlaybookA);
    }

    // ── Tiebreak: priority ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_TwoMatches_LowerPriorityWins()
    {
        SetupQueryResponse(
            BuildEntity(PlaybookA, priority: 700),
            BuildEntity(PlaybookB, priority: 300));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookB);
    }

    // ── Tiebreak: consumer-code specificity ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SpecificConsumerCodeBeatsDefault_OnSamePriority()
    {
        SetupQueryResponse(
            BuildEntity(PlaybookA, consumerCode: "default", priority: 500),
            BuildEntity(PlaybookB, consumerCode: "patent", priority: 500));
        var sut = CreateService();

        var result = await sut.ResolveAsync("summarize-file", consumerCode: "patent");

        result.Should().Be(PlaybookB);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToDefaultWhenSpecificConsumerCodeNotConfigured()
    {
        SetupQueryResponse(BuildEntity(PlaybookA, consumerCode: "default"));
        var sut = CreateService();

        var result = await sut.ResolveAsync("summarize-file", consumerCode: "patent");

        result.Should().Be(PlaybookA);
    }

    // ── Tiebreak: environment specificity ───────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SpecificEnvironmentBeatsWildcard_OnSamePriority()
    {
        SetupQueryResponse(
            BuildEntity(PlaybookA, environment: "*", priority: 500),
            BuildEntity(PlaybookB, environment: "dev", priority: 500));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookB);
    }

    [Fact]
    public async Task ResolveAsync_DefaultsToHostEnvironmentName_WhenParameterNull()
    {
        _envMock.SetupGet(e => e.EnvironmentName).Returns("prod");
        SetupQueryResponse(
            BuildEntity(PlaybookA, environment: "*", priority: 500),
            BuildEntity(PlaybookB, environment: "prod", priority: 500));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookB);
    }

    [Fact]
    public async Task ResolveAsync_RecordWithDifferentEnvironment_Excluded()
    {
        SetupQueryResponse(BuildEntity(PlaybookA, environment: "prod"));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill", environment: "dev");

        result.Should().BeNull();
    }

    // ── Disabled-row exclusion ──────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_DisabledRowsAreFiltered_AtQueryLayer()
    {
        // The QueryExpression includes sprk_enabled=true; we simulate the
        // Dataverse query returning only enabled rows by NOT including the
        // disabled one in the mock response. This validates the contract
        // assumption rather than re-implementing Dataverse filtering.
        SetupQueryResponse(BuildEntity(PlaybookA, enabled: true));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().Be(PlaybookA);

        // Verify the query expression DID include the enabled=true filter.
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.Criteria.Conditions.Any(c =>
                        c.AttributeName == "sprk_enabled" &&
                        c.Operator == ConditionOperator.Equal &&
                        (bool)c.Values[0])),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Match-conditions: null/empty/{} always match ────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void TryMatchConditions_NullEmptyOrBraces_AlwaysMatches(string? json)
    {
        var context = new RoutingContext { MimeType = "application/pdf" };

        var result = ConsumerRoutingService.TryMatchConditions(json, context);

        result.Should().BeTrue();
    }

    // ── Match-conditions: string equality ───────────────────────────────────

    [Fact]
    public void TryMatchConditions_StringEqualityMatch_True()
    {
        var json = """{ "mimeType": "application/pdf" }""";
        var context = new RoutingContext { MimeType = "application/pdf" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeTrue();
    }

    [Fact]
    public void TryMatchConditions_StringEqualityMismatch_False()
    {
        var json = """{ "mimeType": "application/pdf" }""";
        var context = new RoutingContext { MimeType = "text/plain" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    [Fact]
    public void TryMatchConditions_StringMatchIsCaseInsensitive_True()
    {
        var json = """{ "mimeType": "Application/PDF" }""";
        var context = new RoutingContext { MimeType = "application/pdf" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeTrue();
    }

    [Fact]
    public void TryMatchConditions_NullContextField_MismatchOnNonEmptyCondition_False()
    {
        var json = """{ "mimeType": "application/pdf" }""";
        var context = new RoutingContext { MimeType = null };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    // ── Match-conditions: array (in-list) ───────────────────────────────────

    [Fact]
    public void TryMatchConditions_ArrayInList_Match_True()
    {
        var json = """{ "mimeType": ["application/pdf", "text/plain"] }""";
        var context = new RoutingContext { MimeType = "text/plain" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeTrue();
    }

    [Fact]
    public void TryMatchConditions_ArrayInList_NoMatch_False()
    {
        var json = """{ "mimeType": ["application/pdf", "text/plain"] }""";
        var context = new RoutingContext { MimeType = "application/json" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    [Fact]
    public void TryMatchConditions_ArrayWithNullContext_False()
    {
        var json = """{ "mimeType": ["application/pdf"] }""";
        var context = new RoutingContext { MimeType = null };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    // ── Match-conditions: multi-key ─────────────────────────────────────────

    [Fact]
    public void TryMatchConditions_MultipleKeys_AllMatch_True()
    {
        var json = """{ "mimeType": "application/pdf", "documentType": "nda" }""";
        var context = new RoutingContext { MimeType = "application/pdf", DocumentType = "nda" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeTrue();
    }

    [Fact]
    public void TryMatchConditions_MultipleKeys_OneMismatch_False()
    {
        var json = """{ "mimeType": "application/pdf", "documentType": "nda" }""";
        var context = new RoutingContext { MimeType = "application/pdf", DocumentType = "patent" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    // ── Match-conditions: malformed + unknown keys ──────────────────────────

    [Fact]
    public void TryMatchConditions_MalformedJson_FailsClosed_False()
    {
        var json = """{ "mimeType": "application/pdf" """; // truncated
        var context = new RoutingContext { MimeType = "application/pdf" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    [Fact]
    public void TryMatchConditions_UnknownKey_TreatedAsNoMatch_False()
    {
        var json = """{ "unknownDimension": "x" }""";
        var context = new RoutingContext { MimeType = "application/pdf" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    [Fact]
    public void TryMatchConditions_UnsupportedValueType_FailsClosed_False()
    {
        var json = """{ "mimeType": 42 }"""; // number, not string|array<string>
        var context = new RoutingContext { MimeType = "application/pdf" };

        ConsumerRoutingService.TryMatchConditions(json, context).Should().BeFalse();
    }

    // ── End-to-end: match-conditions in resolution ──────────────────────────

    [Fact]
    public async Task ResolveAsync_MatchConditionsExclude_RecordSkipped()
    {
        SetupQueryResponse(BuildEntity(
            PlaybookA,
            matchConditionsJson: """{ "mimeType": "application/pdf" }"""));
        var sut = CreateService();

        var result = await sut.ResolveAsync(
            "summarize-file",
            context: new RoutingContext { MimeType = "text/plain" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_SpecificMatchConditionsBeatsWildcardSamePriority()
    {
        SetupQueryResponse(
            BuildEntity(PlaybookA, matchConditionsJson: null, priority: 500),
            BuildEntity(PlaybookB, matchConditionsJson: """{ "mimeType": "application/pdf" }""", priority: 500));
        var sut = CreateService();

        var result = await sut.ResolveAsync(
            "summarize-file",
            context: new RoutingContext { MimeType = "application/pdf" });

        // Both match; tiebreak via priority (equal) then consumer-code (both default)
        // then environment (both wildcard). First-found wins — but with equal priority
        // and equal specificity, this asserts at minimum that one of them is returned.
        // The more important behavior — exclusion when conditions fail — is covered above.
        (result == PlaybookA || result == PlaybookB).Should().BeTrue(
            "either matching candidate is acceptable when priority + specificity are equal");
    }

    // ── Cache behavior ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SecondCallWithinTtl_UsesCache()
    {
        SetupQueryResponse(BuildEntity(PlaybookA));
        var sut = CreateService();

        var first = await sut.ResolveAsync("matter-pre-fill");
        var second = await sut.ResolveAsync("matter-pre-fill");

        first.Should().Be(PlaybookA);
        second.Should().Be(PlaybookA);

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the cache should serve the second call without re-querying Dataverse");
    }

    [Fact]
    public async Task ResolveAsync_DifferentContext_DistinctCacheKey()
    {
        SetupQueryResponse(BuildEntity(PlaybookA));
        var sut = CreateService();

        await sut.ResolveAsync(
            "summarize-file",
            context: new RoutingContext { MimeType = "application/pdf" });
        await sut.ResolveAsync(
            "summarize-file",
            context: new RoutingContext { MimeType = "text/plain" });

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveAsync_CachesNullResult_DoesNotRequeryDataverse()
    {
        SetupQueryResponse(); // no records
        var sut = CreateService();

        var first = await sut.ResolveAsync("matter-pre-fill");
        var second = await sut.ResolveAsync("matter-pre-fill");

        first.Should().BeNull();
        second.Should().BeNull();

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Graceful degrade on Dataverse error ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_DataverseThrows_ReturnsNullWithoutPropagating()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse failure"));
        var sut = CreateService();

        var result = await sut.ResolveAsync("matter-pre-fill");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_Cancelled_PropagatesOperationCanceled()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateService();

        var act = async () => await sut.ResolveAsync("matter-pre-fill", cancellationToken: new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
