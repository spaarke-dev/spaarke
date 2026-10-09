using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Ai.Metering;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.Metering;

/// <summary>
/// Pure-domain tests (ADR-038 §2 path #6) of the stamp's optional monthly OpenAI spend limit
/// (customer-provisioning-orchestration-r1 task 254, owner G37): no limit by default; a configured limit refuses once
/// the month's estimate reaches it; the store fails open; the estimate uses the configured rates; months reset.
/// </summary>
public sealed class AiSpendLimitTests
{
    private static readonly DateTimeOffset MidOctober = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(MidOctober);
    private readonly InMemoryAiSpendLedger _ledger = new();
    private readonly MutableOptionsMonitor _options = new(new AiSpendLimitOptions());

    private AiSpendLimit Limit(IAiSpendLedger? ledger = null)
        => new(_options, ledger ?? _ledger, _time, NullLogger<AiSpendLimit>.Instance);

    [Fact]
    public async Task NoLimitConfigured_NeverRefuses()
    {
        _ledger.Add(1_000_000m, MidOctober);

        await Limit().Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask())
            .Should().NotThrowAsync("the default is no limit (owner G37)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AZeroOrNegativeLimit_IsNoLimit(int limit)
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = limit };
        _ledger.Add(500m, MidOctober);

        await Limit().Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnderTheLimit_Proceeds()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 100m };
        _ledger.Add(99.99m, MidOctober);

        await Limit().Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask()).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public async Task AtOrOverTheLimit_Refuses_UntilTheNextUtcMonth(int spent)
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 100m };
        _ledger.Add(spent, MidOctober);

        var thrown = await Limit().Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask())
            .Should().ThrowAsync<AiSpendLimitExceededException>();

        thrown.Which.MonthlyLimitUsd.Should().Be(100m);
        thrown.Which.MonthToDateUsd.Should().Be(spent);
        thrown.Which.RetryAfter.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero) - MidOctober);
        thrown.Which.Message.Should().NotContain("$").And.NotContain("100",
            "end users see the message; the amounts are Spaarke's cost data and stay in the log");
    }

    [Fact]
    public async Task ANewMonth_StartsAtZero()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 100m };
        _ledger.Add(150m, MidOctober);
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero));

        await Limit().Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task ARaisedLimit_IsReadOnTheNextCall()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 100m };
        _ledger.Add(150m, MidOctober);
        var limit = Limit();
        await limit.Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask())
            .Should().ThrowAsync<AiSpendLimitExceededException>();

        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 200m };

        await limit.Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnUnreadableStore_LetsTheCallThrough()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 1m };

        await Limit(new ThrowingLedger()).Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask())
            .Should().NotThrowAsync("a Redis outage must not stop every AI feature (fail-open, task 077 §11)");
    }

    [Fact]
    public async Task RecordUsage_PricesTokensWithTheConfiguredRates()
    {
        Limit().RecordUsage(inputTokens: 1_000_000, outputTokens: 500_000);
        _options.CurrentValue = new AiSpendLimitOptions { InputUsdPer1MTokens = 1m, OutputUsdPer1MTokens = 2m };
        Limit().RecordUsage(inputTokens: 1_000_000, outputTokens: 1_000_000);

        // Defaults 2.50 + 10.00 × 0.5 = 7.50; then 1.00 + 2.00 = 3.00.
        (await _ledger.GetMonthToDateUsdAsync(MidOctober, CancellationToken.None)).Should().Be(10.50m);
    }

    [Fact]
    public async Task RecordUsage_IsCountedWithoutALimit_SoALimitAddedMidMonthStartsFromTheRealFigure()
    {
        Limit().RecordUsage(inputTokens: 400_000, outputTokens: 0);

        (await _ledger.GetMonthToDateUsdAsync(MidOctober, CancellationToken.None)).Should().Be(1.00m);
    }

    [Fact]
    public async Task ASlowStore_LetsTheCallThroughAfterTheReadTimeout()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 1m };
        var pending = new TaskCompletionSource<decimal>();
        var check = Limit(new PendingLedger(pending.Task)).EnsureUnderLimitAsync(CancellationToken.None).AsTask();

        check.IsCompleted.Should().BeFalse("the read is still pending");
        _time.Advance(AiSpendLimit.LedgerReadTimeout);

        await check.Invoking(t => t).Should().NotThrowAsync("a degraded store must not hold every model call (fail-open)");
    }

    [Fact]
    public async Task ACancelledCaller_IsNotFailedOpen()
    {
        _options.CurrentValue = new AiSpendLimitOptions { MonthlyLimitUsd = 1m };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Limit(new PendingLedger(new TaskCompletionSource<decimal>().Task))
            .Invoking(l => l.EnsureUnderLimitAsync(cancelled.Token).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UnbindableSettings_AreReadAsNoLimit()
    {
        var limit = new AiSpendLimit(new UnbindableOptionsMonitor(), _ledger, _time, NullLogger<AiSpendLimit>.Instance);

        await limit.Invoking(l => l.EnsureUnderLimitAsync(CancellationToken.None).AsTask())
            .Should().NotThrowAsync("a hand-set value that is not a number must not stop every AI call");
        limit.Invoking(l => l.RecordUsage(1_000, 1_000)).Should().NotThrow();
    }

    [Fact]
    public void RecordUsage_NeverThrows_WhenTheStoreFails()
    {
        Limit(new ThrowingLedger()).Invoking(l => l.RecordUsage(1_000, 1_000)).Should().NotThrow();
    }

    [Fact]
    public void UntilNextUtcMonth_CrossesTheYear()
    {
        var lastSecondOfYear = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);

        AiSpendLimit.UntilNextUtcMonth(lastSecondOfYear).Should().Be(TimeSpan.FromSeconds(1));
    }

    private sealed class PendingLedger(Task<decimal> read) : IAiSpendLedger
    {
        public ValueTask<decimal> GetMonthToDateUsdAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
            => new(read);

        public void Add(decimal usd, DateTimeOffset nowUtc)
        {
        }
    }

    private sealed class ThrowingLedger : IAiSpendLedger
    {
        public ValueTask<decimal> GetMonthToDateUsdAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
            => throw new InvalidOperationException("store unavailable");

        public void Add(decimal usd, DateTimeOffset nowUtc) => throw new InvalidOperationException("store unavailable");
    }

    private sealed class UnbindableOptionsMonitor : IOptionsMonitor<AiSpendLimitOptions>
    {
        // What ConfigurationBinder throws for AiSpendLimit:MonthlyLimitUsd = "five hundred".
        public AiSpendLimitOptions CurrentValue => throw new InvalidOperationException("Failed to convert configuration value");

        public AiSpendLimitOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AiSpendLimitOptions, string?> listener) => null;
    }

    private sealed class MutableOptionsMonitor(AiSpendLimitOptions value) : IOptionsMonitor<AiSpendLimitOptions>
    {
        public AiSpendLimitOptions CurrentValue { get; set; } = value;

        public AiSpendLimitOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AiSpendLimitOptions, string?> listener) => null;
    }
}
