using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Scheduling;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Scheduling;

/// <summary>
/// The Redis lease's contract (ADR-036 A1 rule 1, task 103): the lease is taken with Redis's atomic
/// <c>SET NX PX</c> (<c>LockTakeAsync</c>) and released only by its holder; the occurrence marker refuses a tick
/// already dispatched; and every Redis failure means "unavailable", which the host turns into "do not dispatch".
/// </summary>
/// <remarks>
/// The database mock is <see cref="MockBehavior.Strict"/>: a lease implemented as check-then-set — a read of the
/// lease key followed by a plain write — makes calls these tests never set up, and fails them.
/// </remarks>
public class RedisScheduledJobLeaseTests
{
    private const string JobId = "grant-expiry-reminder";
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);
    private static readonly RedisKey LeaseKey = "spaarke:scheduler:lease:grant-expiry-reminder";
    private static readonly RedisKey OccurrenceKey = "spaarke:scheduler:last-fire:grant-expiry-reminder";
    private static readonly DateTimeOffset Occurrence = new(2026, 9, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAcquire_FreeLease_TakesItWithAtomicSetIfAbsentAndExpiry()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockTakeAsync(LeaseKey, It.IsAny<RedisValue>(), Duration, It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var grant = await lease.TryAcquireAsync(JobId, occurrenceUtc: null, Duration, CancellationToken.None);

        grant.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        grant.Token.Should().NotBeNullOrEmpty();
        db.Verify(d => d.LockTakeAsync(LeaseKey, grant.Token!, Duration, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task TryAcquire_LeaseHeldElsewhere_IsRefused()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockTakeAsync(LeaseKey, It.IsAny<RedisValue>(), Duration, It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var grant = await lease.TryAcquireAsync(JobId, Occurrence, Duration, CancellationToken.None);

        grant.Status.Should().Be(ScheduledJobLeaseStatus.HeldElsewhere);
        grant.Token.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_OccurrenceAlreadyDispatched_GivesTheLeaseBackAndRefuses()
    {
        var (lease, db) = Create();
        RedisValue taken = default;
        db.Setup(d => d.LockTakeAsync(LeaseKey, It.IsAny<RedisValue>(), Duration, It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan, CommandFlags>((_, token, _, _) => taken = token)
            .ReturnsAsync(true);
        db.Setup(d => d.StringGetAsync(OccurrenceKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(Occurrence.UtcTicks);
        db.Setup(d => d.LockReleaseAsync(LeaseKey, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var grant = await lease.TryAcquireAsync(JobId, Occurrence, Duration, CancellationToken.None);

        grant.Status.Should().Be(ScheduledJobLeaseStatus.OccurrenceAlreadyDispatched);
        db.Verify(d => d.LockReleaseAsync(LeaseKey, taken, It.IsAny<CommandFlags>()), Times.Once,
            "the lease it just took goes back at once, with its own token");
    }

    [Fact]
    public async Task TryAcquire_NewOccurrence_RecordsItAndGrants()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockTakeAsync(LeaseKey, It.IsAny<RedisValue>(), Duration, It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringGetAsync(OccurrenceKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(Occurrence.AddDays(-1).UtcTicks);
        // keepTtl MUST be false: the marker's own expiry is what lets it lapse.
        db.Setup(d => d.StringSetAsync(
                OccurrenceKey, Occurrence.UtcTicks, It.Is<TimeSpan?>(t => t > TimeSpan.Zero), false, It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var grant = await lease.TryAcquireAsync(JobId, Occurrence, Duration, CancellationToken.None);

        grant.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        db.Verify(d => d.StringSetAsync(
            OccurrenceKey, Occurrence.UtcTicks, It.IsAny<TimeSpan?>(), false, It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task TryAcquire_RedisFails_IsReportedAsUnavailable()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockTakeAsync(LeaseKey, It.IsAny<RedisValue>(), Duration, It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "connection lost"));

        var acquire = async () => await lease.TryAcquireAsync(JobId, Occurrence, Duration, CancellationToken.None);

        (await acquire.Should().ThrowAsync<ScheduledJobLeaseUnavailableException>())
            .WithInnerException<RedisConnectionException>();
    }

    [Fact]
    public async Task TryAcquire_RedisDisconnected_IsReportedAsUnavailableWithoutACommand()
    {
        var (lease, _) = Create(connected: false);

        var acquire = async () => await lease.TryAcquireAsync(JobId, Occurrence, Duration, CancellationToken.None);

        await acquire.Should().ThrowAsync<ScheduledJobLeaseUnavailableException>();
    }

    [Fact]
    public async Task Renew_LeaseNoLongerHeldByThisToken_ReturnsFalse()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockExtendAsync(LeaseKey, (RedisValue)"old-token", Duration, It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var renewed = await lease.RenewAsync(JobId, "old-token", Duration, CancellationToken.None);

        renewed.Should().BeFalse();
    }

    [Fact]
    public async Task Release_DeletesOnlyThroughTheHoldersToken()
    {
        var (lease, db) = Create();
        db.Setup(d => d.LockReleaseAsync(LeaseKey, (RedisValue)"my-token", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await lease.ReleaseAsync(JobId, "my-token", CancellationToken.None);

        db.Verify(d => d.LockReleaseAsync(LeaseKey, (RedisValue)"my-token", It.IsAny<CommandFlags>()), Times.Once);
    }

    private static (RedisScheduledJobLease Lease, Mock<IDatabase> Db) Create(bool connected = true)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(connected);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        var options = Options.Create(new RedisOptions { InstanceName = "spaarke:" });
        return (new RedisScheduledJobLease(redis.Object, options), db);
    }
}
