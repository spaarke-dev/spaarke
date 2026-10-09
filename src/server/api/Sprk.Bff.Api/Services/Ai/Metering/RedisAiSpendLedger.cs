using System.Globalization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Redis-backed <see cref="IAiSpendLedger"/> over the BFF's existing connection (ADR-009) — no new store or package.
/// </summary>
/// <remarks>
/// <para><b>Key</b> — <c>{InstanceName}ai-spend:month:{yyyy-MM}</c>, one per UTC month; value = integer micro-USD,
/// added with <c>INCRBY</c> (atomic across instances, no float drift). Stamp-wide by design: the stamp is one customer
/// (system key <c>SystemCacheKeys.AiSpendMonth</c>). Both slots of the app share the prefix, so a call served by the
/// staging slot counts too — it is the same customer's bill.</para>
/// <para><b>Expiry</b> — <see cref="Retention"/>, set only when the key has none (<c>EXPIRE … NX</c>), so a month's
/// figure outlives the month and then lapses.</para>
/// <para><b>Writes never wait</b> — both commands are fire-and-forget; a lost increment under-counts by one call's
/// estimate, which the limit's caller accepts (fail-open, task 077 §11).</para>
/// </remarks>
public sealed class RedisAiSpendLedger : IAiSpendLedger
{
    /// <summary>How long a month's figure is kept — past the month, so the month just ended stays readable.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(62);

    private const decimal MicroPerUsd = 1_000_000m;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisAiSpendLedger(IConnectionMultiplexer redis, IOptions<RedisOptions> redisOptions)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        ArgumentNullException.ThrowIfNull(redisOptions);
        _keyPrefix = (redisOptions.Value.InstanceName ?? string.Empty) + "ai-spend:month:";
    }

    internal RedisKey KeyFor(DateTimeOffset nowUtc)
        => _keyPrefix + nowUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async ValueTask<decimal> GetMonthToDateUsdAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _redis.GetDatabase().StringGetAsync(KeyFor(nowUtc)).ConfigureAwait(false);
        return value.TryParse(out long micro) ? micro / MicroPerUsd : 0m;
    }

    /// <inheritdoc />
    public void Add(decimal usd, DateTimeOffset nowUtc)
    {
        // Rounded UP to the micro-dollar: the estimate errs towards stopping early.
        var micro = (long)Math.Ceiling(usd * MicroPerUsd);
        if (micro <= 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        var key = KeyFor(nowUtc);
        db.StringIncrement(key, micro, CommandFlags.FireAndForget);
        db.KeyExpire(key, Retention, ExpireWhen.HasNoExpiry, CommandFlags.FireAndForget);
    }
}
