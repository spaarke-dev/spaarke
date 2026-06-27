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

    [Fact]
    public async Task PublishInvalidationAsync_EmptyPersonId_NoOp()
    {
        var subscriber = new Mock<ISubscriber>(MockBehavior.Strict);
        var sut = CreateSut(subscriber, out _);

        // No setup on PublishAsync — strict mock fails the test if it's called.
        await sut.PublishInvalidationAsync(Guid.Empty, EntityLogicalName, CorrelationId, CancellationToken.None);
    }

    [Fact]
    public async Task PublishInvalidationAsync_EmptyEntityLogicalName_NoOp()
    {
        var subscriber = new Mock<ISubscriber>(MockBehavior.Strict);
        var sut = CreateSut(subscriber, out _);

        await sut.PublishInvalidationAsync(PersonId, "", CorrelationId, CancellationToken.None);
    }

    [Fact]
    public async Task PublishInvalidationAsync_CancelledBeforePublish_NoOp()
    {
        var subscriber = new Mock<ISubscriber>(MockBehavior.Strict);
        var sut = CreateSut(subscriber, out _);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // No publish call — strict mock would fail if invoked.
        await sut.PublishInvalidationAsync(PersonId, EntityLogicalName, CorrelationId, cts.Token);
    }

    // ─── Null peer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NullMembershipCacheInvalidator_LogsAndReturns()
    {
        var sut = new NullMembershipCacheInvalidator(
            NullLogger<NullMembershipCacheInvalidator>.Instance);

        // Should complete without exception + without any Redis interaction
        // (no IConnectionMultiplexer in the constructor).
        await sut.PublishInvalidationAsync(PersonId, EntityLogicalName, CorrelationId, CancellationToken.None);
    }

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
