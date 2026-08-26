using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// r1 MVP implementation of <see cref="ITenantTokenLedger"/> — per-process
/// <c>ConcurrentDictionary</c> keyed on <c>(tenantId, yyyy-MM)</c>. Resets on cold-start; the
/// authoritative record remains Application Insights (via <c>ai.metering.tokens</c> counter shipped
/// by <c>spaarke-ai-architecture-redesign-r1</c> task 054). See <see cref="ITenantTokenLedger"/>
/// XML for the r1-scope rationale.
/// </summary>
/// <remarks>
/// Thread-safe. Zero-allocation on the hot path (single dictionary lookup + interlocked add).
/// Singleton lifetime (registered by <c>AiMeteringModule</c>).
/// </remarks>
public sealed class InMemoryTenantTokenLedger : ITenantTokenLedger
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<LedgerKey, decimal> _spendByTenantAndMonth = new();

    public InMemoryTenantTokenLedger(TimeProvider? timeProvider = null)
    {
        // Optional TimeProvider dependency (per docs/standards/TEST-ARCHITECTURE.md; enables
        // deterministic month-boundary tests without Stopwatch/DateTime.UtcNow flakiness).
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public decimal GetMonthToDateSpendUsd(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return 0m;
        }

        var key = KeyFor(tenantId);
        return _spendByTenantAndMonth.TryGetValue(key, out var spend) ? spend : 0m;
    }

    /// <inheritdoc />
    public void AddSpend(string tenantId, decimal deltaUsd)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || deltaUsd <= 0m)
        {
            return;
        }

        var key = KeyFor(tenantId);
        _spendByTenantAndMonth.AddOrUpdate(
            key,
            addValueFactory: _ => deltaUsd,
            updateValueFactory: (_, current) => current + deltaUsd);
    }

    private LedgerKey KeyFor(string tenantId)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return new LedgerKey(tenantId.ToLowerInvariant(), nowUtc.Year, nowUtc.Month);
    }

    /// <summary>
    /// Composite key: (lower-cased tenantId, year, month). Reset boundary is UTC month-rollover;
    /// stale rows persist in memory for the current process lifetime but are never read (a fresh
    /// month's key returns 0 by default) — no cleanup thread needed at r1 scale.
    /// </summary>
    private readonly record struct LedgerKey(string TenantId, int Year, int Month);
}
