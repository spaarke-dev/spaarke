using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 013 (design §8 B-1, spec FR-06/FR-10): the decision capability's plan read writes the plan-read stamp
/// exactly as the plan route did (task 043, K4): the same key family, key and 4-hour TTL, only when the plan is returned. The commit
/// route reads that stamp as the start of the review, so a moved key or TTL would silently change which system closures re-close.
/// The capability's REST bytes are pinned by task 011's golden baselines; its caller guard by DecisionCapabilityCallerTests.
/// </summary>
public sealed class DecisionCapabilityListPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPlanAsync_ForACallerWhoCanReadTheSignal_ReturnsThePlanAndWritesTheStampWithTheSameKeyAndTtl()
    {
        var (access, plans, _, _) = DecideSignalOnAMatter();
        var cache = new StampCache();
        var capability = Capability(access, plans, Describer(), planReadStamp: cache.Stamp);

        var result = await capability.GetPlanAsync(SignedIn(), SignalId, CancellationToken.None);

        result.Outcome.Should().Be(DecisionPlanReadOutcome.Ok);
        result.Plan!.Actions.Select(a => a.Code).Should().Equal("approve-variance");
        cache.Writes.Should().ContainSingle().Which.Should().Be(new StampWrite(
            TenantId, "decision-plan-read", $"{CallerOid:D}:{SignalId:D}", 1, Now, TimeSpan.FromHours(4)));
        (await cache.Stamp.ReadAsync(TenantId, CallerOid.ToString("D"), SignalId, CancellationToken.None))
            .Should().Be(Now, "the commit route reads the stamp back as the start of the review");
    }

    [Fact]
    public async Task GetPlanAsync_ThroughTheRestRoute_WritesTheSameStampAsTheCapability()
    {
        // The REST route is a thin adapter: the stamp it writes is the capability's, not a second write.
        var (access, plans, _, _) = DecideSignalOnAMatter();
        var cache = new StampCache();

        var rest = await DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, SignedIn(), Capability(access, plans, Describer(), planReadStamp: cache.Stamp), CancellationToken.None);

        rest.Should().BeOfType<Ok<DecisionPlanResponse>>();
        cache.Writes.Should().ContainSingle().Which.Key.Should().Be($"{CallerOid:D}:{SignalId:D}");
    }

    [Fact]
    public async Task GetPlanAsync_ForAPlanThatDoesNotResolve_IsRefusedWithTheResolverReason_AndWritesNoStamp()
    {
        var (access, plans, _, _) = DecideSignalOnAMatter(plan: """{"actions":["not-a-catalog-action"]}""");
        var cache = new StampCache();
        var capability = Capability(access, plans, Describer(), planReadStamp: cache.Stamp);

        var result = await capability.GetPlanAsync(SignedIn(), SignalId, CancellationToken.None);

        result.Outcome.Should().Be(DecisionPlanReadOutcome.Refused);
        result.RefusalReason.Should().NotBeNullOrWhiteSpace();
        cache.Writes.Should().BeEmpty("a refused plan was not read, so no review started");
        DecisionResultMapper.ToToolError(result)!.ReasonCode.Should().Be(DecisionPlanEndpoints.PlanRefusedReasonCode);
    }

    private sealed record StampWrite(string Tenant, string Resource, string Key, int Version, DateTimeOffset ReadOn, TimeSpan? Ttl);

    /// <summary>The tenant cache (Redis) at its boundary: records each stamp write and serves it back to the commit route's reader.</summary>
    private sealed class StampCache
    {
        private readonly Dictionary<string, DecisionPlanReadStamp.Stamp> _store = new(StringComparer.Ordinal);

        public StampCache()
        {
            var cache = new Mock<ITenantCache>();
            cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<DecisionPlanReadStamp.Stamp>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string tenant, string resource, string key, int version, DecisionPlanReadStamp.Stamp stamp, TimeSpan? ttl, string _, CancellationToken _) =>
                {
                    Writes.Add(new StampWrite(tenant, resource, key, version, stamp.ReadOn, ttl));
                    _store[$"{tenant}|{resource}|{key}|{version}"] = stamp;
                })
                .Returns(Task.CompletedTask);
            cache.Setup(c => c.GetAsync<DecisionPlanReadStamp.Stamp>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenant, string resource, string key, int version, string _, CancellationToken _) =>
                    _store.TryGetValue($"{tenant}|{resource}|{key}|{version}", out var stamp) ? stamp : null);
            Stamp = new DecisionPlanReadStamp(cache.Object, new FakeTimeProvider(Now), NullLogger<DecisionPlanReadStamp>.Instance);
        }

        public DecisionPlanReadStamp Stamp { get; }

        public List<StampWrite> Writes { get; } = new();
    }
}
