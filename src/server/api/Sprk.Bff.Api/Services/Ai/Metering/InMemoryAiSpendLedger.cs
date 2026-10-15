using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Per-process <see cref="IAiSpendLedger"/> for hosts without Redis (Development / Testing). Resets on restart and is
/// not shared between instances — never used by a deployed stamp, whose BFF refuses to start without Redis.
/// </summary>
public sealed class InMemoryAiSpendLedger : IAiSpendLedger
{
    private readonly ConcurrentDictionary<(int Year, int Month), decimal> _spend = new();

    /// <inheritdoc />
    public ValueTask<decimal> GetMonthToDateUsdAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        => ValueTask.FromResult(_spend.TryGetValue(MonthOf(nowUtc), out var usd) ? usd : 0m);

    /// <inheritdoc />
    public void Add(decimal usd, DateTimeOffset nowUtc)
    {
        if (usd <= 0m)
        {
            return;
        }

        _spend.AddOrUpdate(MonthOf(nowUtc), usd, (_, current) => current + usd);
    }

    private static (int, int) MonthOf(DateTimeOffset nowUtc)
    {
        var utc = nowUtc.UtcDateTime;
        return (utc.Year, utc.Month);
    }
}
