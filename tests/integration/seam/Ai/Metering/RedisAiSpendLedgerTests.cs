using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Metering;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Metering;

/// <summary>
/// Task 254 — the ledger every deployed stamp uses, over a strict Redis double (the pattern of
/// RedisScheduledJobLeaseTests). Pins what the instances and both slots must agree on: the key
/// (<c>{InstanceName}ai-spend:month:{yyyy-MM}</c>, UTC month), integer micro-USD rounded UP, the increment and expiry
/// commands, and how a stored figure is read back.
/// </summary>
public sealed class RedisAiSpendLedgerTests
{
    private static readonly DateTimeOffset LateOctoberUtc = new(2026, 10, 31, 23, 30, 0, TimeSpan.Zero);
    private static readonly RedisKey OctoberKey = "spaarke:ai-spend:month:2026-10";

    [Fact]
    public void Add_IncrementsTheUtcMonthKeyInMicroDollarsRoundedUp_AndSetsTheExpiryOnlyIfAbsent()
    {
        var (ledger, db) = Create();
        db.Setup(d => d.StringIncrement(OctoberKey, 3L, CommandFlags.FireAndForget)).Returns(0);
        db.Setup(d => d.KeyExpire(OctoberKey, RedisAiSpendLedger.Retention, ExpireWhen.HasNoExpiry, CommandFlags.FireAndForget))
            .Returns(false);

        // 2.5 micro-dollars → 3 (round up: the estimate errs towards stopping early).
        ledger.Add(0.0000025m, LateOctoberUtc);

        db.VerifyAll();
    }

    [Fact]
    public void Add_UsesTheUtcMonth_NotTheCallersOffset()
    {
        var (ledger, db) = Create();
        // 2026-11-01 01:00 at +02:00 is still October in UTC.
        var sameInstantWithOffset = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(2));
        db.Setup(d => d.StringIncrement(OctoberKey, 1_000_000L, CommandFlags.FireAndForget)).Returns(0);
        db.Setup(d => d.KeyExpire(OctoberKey, It.IsAny<TimeSpan?>(), ExpireWhen.HasNoExpiry, CommandFlags.FireAndForget))
            .Returns(false);

        ledger.Add(1m, sameInstantWithOffset);

        db.VerifyAll();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_NothingToCount_SendsNoCommand(int usd)
    {
        var (ledger, db) = Create();

        ledger.Add(usd, LateOctoberUtc);

        db.VerifyNoOtherCalls();   // strict: any call would throw before this
    }

    [Theory]
    [InlineData("12500000", 12.5)]
    [InlineData("1", 0.000001)]
    public async Task GetMonthToDate_ReadsMicroDollars(string stored, double expectedUsd)
    {
        var (ledger, db) = Create();
        db.Setup(d => d.StringGetAsync(OctoberKey, It.IsAny<CommandFlags>())).ReturnsAsync((RedisValue)stored);

        (await ledger.GetMonthToDateUsdAsync(LateOctoberUtc, CancellationToken.None)).Should().Be((decimal)expectedUsd);
    }

    [Fact]
    public async Task GetMonthToDate_NoKey_IsZero()
    {
        var (ledger, db) = Create();
        db.Setup(d => d.StringGetAsync(OctoberKey, It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);

        (await ledger.GetMonthToDateUsdAsync(LateOctoberUtc, CancellationToken.None)).Should().Be(0m);
    }

    private static (RedisAiSpendLedger Ledger, Mock<IDatabase> Db) Create()
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (new RedisAiSpendLedger(redis.Object, Options.Create(new RedisOptions { InstanceName = "spaarke:" })), db);
    }
}
