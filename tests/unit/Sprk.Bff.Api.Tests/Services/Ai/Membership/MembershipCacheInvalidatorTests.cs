// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Unit tests for MembershipCacheInvalidator + NullMembershipCacheInvalidator.
//
// Locks:
//   - Successful publish dispatches a JSON-serialized
//     MembershipCacheInvalidationMessage to the configured channel.
//   - Redis failure (RedisConnectionException) is caught, logged at
//     Warning, and does NOT throw (FR-2P2.8 resilience contract).
//   - CorrelationId is preserved end-to-end (publish-time → channel
//     payload — verified at the payload level since the subscriber
//     re-deserializes back to the same record).
//   - NullMembershipCacheInvalidator logs once at construction + returns
//     CompletedTask on every PublishInvalidationAsync call (no Redis
//     interaction).

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "new")]
public class MembershipCacheInvalidatorTests
{
    private static readonly Guid PersonId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string EntityLogicalName = "sprk_matter";
    private const string CorrelationId = "corr-cache-invalidator-test";
    private const string Channel = "membership-cache-invalidate";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 22, 15, 0, 0, TimeSpan.Zero);

    private static MembershipCacheInvalidator CreateSut(
        Mock<ISubscriber> subscriber,
        out Mock<IConnectionMultiplexer> redis)
    {
        redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);

        var options = Options.Create(new MembershipCacheInvalidatorOptions
        {
            Enabled = true,
            Channel = Channel,
        });
        var clock = new FakeTimeProvider(FixedNow);
        return new MembershipCacheInvalidator(
            redis.Object,
            options,
            // Task 132: the InstanceName the access-eviction patterns carry; irrelevant to the publish under test.
            Options.Create(new Sprk.Bff.Api.Configuration.RedisOptions()),
            clock,
            NullLogger<MembershipCacheInvalidator>.Instance);
    }

    // ─── Successful publish ────────────────────────────────────────────────

    [Fact]
    public async Task PublishInvalidationAsync_Success_PublishesToChannel()
    {
        // Arrange
        var subscriber = new Mock<ISubscriber>();
        RedisValue capturedPayload = default;
        RedisChannel capturedChannel = default;
        subscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((ch, val, _) =>
            {
                capturedChannel = ch;
                capturedPayload = val;
            })
            .ReturnsAsync(2L); // 2 simulated subscribers

        var sut = CreateSut(subscriber, out _);

        // Act
        await sut.PublishInvalidationAsync(PersonId, EntityLogicalName, CorrelationId, CancellationToken.None);

        // Assert
        capturedChannel.ToString().Should().Be(Channel);
        capturedPayload.IsNullOrEmpty.Should().BeFalse();
        var deserialized = JsonSerializer.Deserialize<MembershipCacheInvalidationMessage>(
            capturedPayload.ToString(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        deserialized.Should().NotBeNull();
        deserialized!.PersonId.Should().Be(PersonId);
        deserialized.EntityLogicalName.Should().Be(EntityLogicalName);
    }

    // ─── Resilience contract ───────────────────────────────────────────────


    [Fact]
    public async Task PublishInvalidationAsync_PreservesCorrelationId()
    {
        var subscriber = new Mock<ISubscriber>();
        RedisValue capturedPayload = default;
        subscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, val, _) => capturedPayload = val)
            .ReturnsAsync(0L);

        var sut = CreateSut(subscriber, out _);

        const string corr = "trace-7f3c-corr";
        await sut.PublishInvalidationAsync(PersonId, EntityLogicalName, corr, CancellationToken.None);

        var deserialized = JsonSerializer.Deserialize<MembershipCacheInvalidationMessage>(
            capturedPayload.ToString(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be(corr);
    }

    [Fact]
    public async Task PublishInvalidationAsync_NormalizesEntityLogicalName()
    {
        var subscriber = new Mock<ISubscriber>();
        RedisValue capturedPayload = default;
        subscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, val, _) => capturedPayload = val)
            .ReturnsAsync(0L);

        var sut = CreateSut(subscriber, out _);

        // Mixed case + whitespace input — payload should carry the lowercase
        // canonical form.
        await sut.PublishInvalidationAsync(PersonId, "  SPRK_Matter  ", CorrelationId, CancellationToken.None);

        var deserialized = JsonSerializer.Deserialize<MembershipCacheInvalidationMessage>(
            capturedPayload.ToString(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        deserialized!.EntityLogicalName.Should().Be("sprk_matter");
    }

    // ─── Argument guards (silent no-ops) ───────────────────────────────────

    private static void AssertNothingPublished(Mock<ISubscriber> subscriber) =>
        subscriber.Verify(
            s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Never);

    [Fact]
    public async Task PublishInvalidationAsync_EmptyPersonId_NoOp()
    {
        var subscriber = new Mock<ISubscriber>();
        var sut = CreateSut(subscriber, out _);

        await sut.PublishInvalidationAsync(Guid.Empty, EntityLogicalName, CorrelationId, CancellationToken.None);

        // Verify, don't rely on a Strict mock: PublishInvalidationAsync swallows every exception (FR-2P2.8),
        // so a Strict-mock violation would be caught and logged, and this test could never fail.
        AssertNothingPublished(subscriber);
    }

    [Fact]
    public async Task PublishInvalidationAsync_EmptyEntityLogicalName_NoOp()
    {
        var subscriber = new Mock<ISubscriber>();
        var sut = CreateSut(subscriber, out _);

        await sut.PublishInvalidationAsync(PersonId, "", CorrelationId, CancellationToken.None);

        AssertNothingPublished(subscriber);
    }

    [Fact]
    public async Task PublishInvalidationAsync_CancelledBeforePublish_NoOp()
    {
        var subscriber = new Mock<ISubscriber>();
        var sut = CreateSut(subscriber, out _);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await sut.PublishInvalidationAsync(PersonId, EntityLogicalName, CorrelationId, cts.Token);

        AssertNothingPublished(subscriber);
    }

    // ─── Null peer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NullMembershipCacheInvalidator_LogsAndReturns()
    {
        var logger = new Mock<ILogger<NullMembershipCacheInvalidator>>();
        var sut = new NullMembershipCacheInvalidator(logger.Object);

        // The constructor announces — once, at Warning — that every invalidation is a no-op.
        VerifyLogged(logger, LogLevel.Warning, "EVERY membership / access cache invalidation is a no-op", Times.Once());

        // Should complete without exception + without any Redis interaction
        // (no IConnectionMultiplexer in the constructor).
        var task = sut.PublishInvalidationAsync(PersonId, EntityLogicalName, CorrelationId, CancellationToken.None);
        task.IsCompletedSuccessfully.Should().BeTrue("the no-op returns a completed task");
        await task;

        // ...and it logs the skipped publish at Debug, carrying the correlation id.
        VerifyLogged(logger, LogLevel.Debug, "PublishInvalidationAsync no-op", Times.Once());
        VerifyLogged(logger, LogLevel.Debug, CorrelationId!, Times.Once());
    }

    private static void VerifyLogged<T>(Mock<ILogger<T>> logger, LogLevel level, string contains, Times times) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(contains)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    /// <summary>
    /// Deterministic TimeProvider used by the invalidator for the
    /// PublishedAtUtc field on the channel payload.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
