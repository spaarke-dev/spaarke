using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Insights.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.Graph;

/// <summary>
/// Unit tests for the Phase 1 <see cref="StubInsightGraph"/> swap-path placeholder
/// (D-P17). Verifies that:
/// (1) the stub is reachable via DI through <see cref="InsightsModule.AddInsightsModule"/>;
/// (2) every interface method throws <see cref="NotImplementedException"/>;
/// (3) the exception message points at SPEC §3.3 Phase 1.5 so call sites can find
///     the deferral rationale.
/// </summary>
public class StubInsightGraphTests
{
    private const string ExpectedMessageFragment = "Phase 1.5";
    private const string ExpectedSpecReference = "SPEC §3.3";

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInsightsModule();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Module_registers_IInsightGraph_resolvable_via_DI()
    {
        using var sp = (ServiceProvider)BuildProvider();

        var resolved = sp.GetRequiredService<IInsightGraph>();

        resolved.Should().NotBeNull();
        resolved.Should().BeOfType<StubInsightGraph>(
            "Phase 1 ships the stub; CosmosNoSqlInsightGraph is the first Phase 1.5 deliverable per SPEC §3.3");
    }

    [Fact]
    public async Task UpsertVertexAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();
        var vertex = new InsightVertex
        {
            Id = "matter:M-1234",
            TenantId = "tenant-acme",
            VertexType = "Matter"
        };

        var act = async () => await sut.UpsertVertexAsync(vertex, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task GetVertexAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();

        var act = async () => await sut.GetVertexAsync("matter:M-1234", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task DeleteVertexAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();

        var act = async () => await sut.DeleteVertexAsync("matter:M-1234", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task UpsertEdgeAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();
        var edge = new InsightEdge
        {
            FromVertexId = "matter:M-1234",
            EdgeType = "INVOLVED_PARTY",
            ToVertexId = "party:acme",
            TenantId = "tenant-acme"
        };

        var act = async () => await sut.UpsertEdgeAsync(edge, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task DeleteEdgeAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();

        var act = async () => await sut.DeleteEdgeAsync(
            "matter:M-1234", "INVOLVED_PARTY", "party:acme", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task FindConnectedEntitiesAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();
        var spec = new GraphTraversalSpec
        {
            SeedVertexIds = new[] { "matter:M-1234" },
            TenantId = "tenant-acme",
            MaxHops = 2
        };

        var act = async () => await sut.FindConnectedEntitiesAsync(spec, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    [Fact]
    public async Task FindMattersInvolvingPartyAsync_throws_NotImplementedException_with_Phase_1_5_message()
    {
        var sut = ResolveStub();
        var scope = new GraphTraversalScope { TenantId = "tenant-acme" };

        var act = async () => await sut.FindMattersInvolvingPartyAsync(
            "party:acme", scope, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain(ExpectedMessageFragment);
        ex.Which.Message.Should().Contain(ExpectedSpecReference);
    }

    private static IInsightGraph ResolveStub()
    {
        var sp = BuildProvider();
        return sp.GetRequiredService<IInsightGraph>();
    }
}
