namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// The stamp's month-to-date estimated OpenAI spend (task 254). Two implementations: <see cref="RedisAiSpendLedger"/>
/// wherever Redis is on — every deployed environment, so all instances, restarts and both slots share one figure
/// (ADR-009) — and <see cref="InMemoryAiSpendLedger"/> where Redis is off (Development / Testing).
/// </summary>
public interface IAiSpendLedger
{
    /// <summary>Estimated USD spent in the UTC calendar month containing <paramref name="nowUtc"/>.</summary>
    ValueTask<decimal> GetMonthToDateUsdAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Adds <paramref name="usd"/> to the month containing <paramref name="nowUtc"/>. Does not wait for the store: it is
    /// called after every model call and must not add latency to it.
    /// </summary>
    void Add(decimal usd, DateTimeOffset nowUtc);
}
