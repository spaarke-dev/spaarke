using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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

    [Fact]
    public async Task GetPlanAndSuppressionAsync_ForAReadableSignal_AnswersAsTheTwoReadsDo_WithOneAccessCheck()
    {
        // NFR-01 (2026-11-25): the plan tool asked the Signal access question twice per item opened (plan, then suppression). The combined
        // read asks it once and answers what the two separate reads answer. The suppression service is a stand-in whose count read faults,
        // so both paths answer Unavailable: the point is the same answer from one access check, not the count itself.
        var (access, plans, users, _) = DecideSignalOnAMatter();
        var cache = new StampCache();
        var capability = Capability(access, plans, Describer(), planReadStamp: cache.Stamp);

        var (plan, suppression) = await capability.GetPlanAndSuppressionAsync(SignedIn(), SignalId, CancellationToken.None);
        var combinedSignalReads = SignalReads(users);

        var separatePlan = await capability.GetPlanAsync(SignedIn(), SignalId, CancellationToken.None);
        var separateSuppression = await capability.GetSuppressionAsync(SignedIn(), SignalId, CancellationToken.None);

        plan.Outcome.Should().Be(DecisionPlanReadOutcome.Ok);
        plan.Plan.Should().BeEquivalentTo(separatePlan.Plan, "the same plan the REST route returns");
        suppression.Should().NotBeNull().And.BeEquivalentTo(separateSuppression);
        combinedSignalReads.Should().Be(1, "one access check for both reads");
        (SignalReads(users) - combinedSignalReads).Should().Be(2, "the two REST reads each still check access themselves");
        cache.Writes.Should().HaveCount(2, "the combined read writes the plan-read stamp as GetPlanAsync does");
    }

    [Fact]
    public async Task GetPlanAndSuppressionAsync_WhenThePlanIsRefused_ReadsNoSuppression_AndWritesNoStamp()
    {
        var (access, plans, users, _) = DecideSignalOnAMatter(matterReadable: false);
        var cache = new StampCache();
        var capability = Capability(access, plans, Describer(), planReadStamp: cache.Stamp); // no suppression service: a call would throw

        var (plan, suppression) = await capability.GetPlanAndSuppressionAsync(SignedIn(), SignalId, CancellationToken.None);

        plan.Outcome.Should().Be(DecisionPlanReadOutcome.NotAllowed);
        plan.Should().BeEquivalentTo(await capability.GetPlanAsync(SignedIn(), SignalId, CancellationToken.None), "the REST route's refusal");
        suppression.Should().BeNull("nothing more is read once the plan is refused");
        cache.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPlanAndSuppressionAsync_WithNoSignedInCaller_IsUserContextRequired_AndReadsNothing()
    {
        var (plan, suppression) = await Capability().GetPlanAndSuppressionAsync(null, SignalId, CancellationToken.None);

        plan.Outcome.Should().Be(DecisionPlanReadOutcome.UserContextRequired);
        suppression.Should().BeNull();
    }

    private static int SignalReads(Mock<IDataverseUserClient> users) =>
        users.Invocations.Count(i => i.Method.Name == nameof(IDataverseUserClient.GetAsync)
                                     && ((string)i.Arguments[0]).StartsWith("sprk_signals(", StringComparison.Ordinal));

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
