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
/// FR-P1-03 (spaarke-ai-architecture-redesign-r1 task 022) — contract tests for
/// <see cref="IConsumerRoutingService.ResolveEventBindingsAsync"/>: the Event
/// path's routing read. Declared membership order (NOT row priority) drives the
/// member sequence; non-members and wrong-environment rows are filtered; malformed
/// membership JSON degrades to non-membership; results cache per (event, env);
/// Dataverse failures graceful-degrade to an empty rule (routing never throws).
/// KEEP rationale: this ordering + filtering contract is what the
/// <c>document_uploaded → [classify(1), summarize(2)]</c> launch rule rides on.
/// </summary>
public sealed class ConsumerRoutingServiceEventBindingsTests
{
    private static readonly Guid ClassifyRowId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid SummarizeRowId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid OtherRowId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid ActionA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<IHostEnvironment> _envMock = new();

    public ConsumerRoutingServiceEventBindingsTests()
    {
        _envMock.SetupGet(e => e.EnvironmentName).Returns("dev");
    }

    private ConsumerRoutingService CreateService() => new(
        _entityServiceMock.Object, _cache, _envMock.Object,
        Mock.Of<ILogger<ConsumerRoutingService>>());

    private void SetupQueryResponse(params Entity[] entities)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(entities.ToList()));
    }

    private static Entity BuildRow(
        Guid rowId,
        string consumerType,
        string? onEventBindingsJson,
        string environment = "*",
        int priority = 500)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_playbookconsumerid"] = rowId;
        entity["sprk_consumertype"] = consumerType;
        entity["sprk_consumercode"] = "default";
        entity["sprk_environment"] = environment;
        entity["sprk_priority"] = priority;
        entity["sprk_enabled"] = true;
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", ActionA);
        if (onEventBindingsJson is not null)
        {
            entity["sprk_oneventbindings"] = onEventBindingsJson;
        }
        return entity;
    }

    [Fact]
    public async Task ResolveEventBindingsAsync_OrdersByDeclaredMembershipOrder_NotRowPriority()
    {
        // Summarize has BETTER row priority (100 < 500) but membership order 2 — the
        // membership's order value is the execution sequence, priority only tiebreaks.
        SetupQueryResponse(
            BuildRow(SummarizeRowId, "chat-summarize",
                """[{"event":"document_uploaded","order":2}]""", priority: 100),
            BuildRow(ClassifyRowId, "chat-classify",
                """[{"event":"document_uploaded","order":1}]""", priority: 500));

        var members = await CreateService().ResolveEventBindingsAsync("document_uploaded");

        members.Select(m => m.BindingId).Should().ContainInOrder(ClassifyRowId, SummarizeRowId);
        members.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResolveEventBindingsAsync_FiltersNonMembers_AndWrongEnvironment()
    {
        SetupQueryResponse(
            BuildRow(ClassifyRowId, "chat-classify",
                """[{"event":"document_uploaded","order":1}]"""),
            BuildRow(OtherRowId, "daily-briefing-narrate",
                """[{"event":"schedule:daily-briefing","order":1}]"""), // different event
            BuildRow(SummarizeRowId, "chat-summarize",
                """[{"event":"document_uploaded","order":2}]""", environment: "prod")); // wrong env

        var members = await CreateService().ResolveEventBindingsAsync("document_uploaded");

        members.Should().ContainSingle().Which.BindingId.Should().Be(ClassifyRowId);
    }

    [Fact]
    public async Task ResolveEventBindingsAsync_MalformedMembershipJson_DegradesToNonMembership()
    {
        SetupQueryResponse(
            BuildRow(ClassifyRowId, "chat-classify", """[{"event":"document_uploaded","order":1}]"""),
            BuildRow(OtherRowId, "broken-row", """not-json-at-all"""));

        var members = await CreateService().ResolveEventBindingsAsync("document_uploaded");

        members.Should().ContainSingle("maker data-entry errors must not break event routing")
            .Which.BindingId.Should().Be(ClassifyRowId);
    }

    [Fact]
    public async Task ResolveEventBindingsAsync_CachesPerEventAndEnvironment()
    {
        SetupQueryResponse(
            BuildRow(ClassifyRowId, "chat-classify", """[{"event":"document_uploaded","order":1}]"""));

        var service = CreateService();
        _ = await service.ResolveEventBindingsAsync("document_uploaded");
        _ = await service.ResolveEventBindingsAsync("document_uploaded");

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once, "second resolve within the 5-minute TTL serves from cache (ADR-014)");
    }

    [Fact]
    public async Task ResolveEventBindingsAsync_DataverseFailure_GracefulDegradesToEmptyRule()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var members = await CreateService().ResolveEventBindingsAsync("document_uploaded");

        members.Should().BeEmpty("routing never throws to the consumer — the Event path degrades to 'no rule'");
    }
}
