using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.EventRules;

/// <summary>
/// Tests for <see cref="EventPathUserState"/> — the NFR-09 per-user daily budget
/// counter + opt-out marker (FR-P1-03 bounds a/b storage). KEEP rationale: the
/// UTC-day-key rollover and the opted-in-by-absence contract are the branchy
/// business logic the daily cap rides on. FakeTimeProvider per TEST-ARCHITECTURE.
/// </summary>
public sealed class EventPathUserStateTests
{
    private const string TenantId = "tenant-state";
    private const string UserOid = "user-1";

    private readonly InMemoryTenantCache _cache = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-07-05T23:30:00Z"));

    private EventPathUserState CreateSut() =>
        new(_cache, Options.Create(new EventRulesOptions()), _time);

    [Fact]
    public async Task IsOptedOutAsync_DefaultState_IsOptedIn()
    {
        (await CreateSut().IsOptedOutAsync(TenantId, UserOid, default))
            .Should().BeFalse("auto-run is the product default; opt-out is marker-presence");
    }

    [Fact]
    public async Task SetOptOutAsync_RoundTrips_AndOptBackInRemovesMarker()
    {
        var sut = CreateSut();

        await sut.SetOptOutAsync(TenantId, UserOid, optedOut: true, default);
        (await sut.IsOptedOutAsync(TenantId, UserOid, default)).Should().BeTrue();

        await sut.SetOptOutAsync(TenantId, UserOid, optedOut: false, default);
        (await sut.IsOptedOutAsync(TenantId, UserOid, default)).Should().BeFalse();
        _cache.Store.Keys.Should().NotContain(k => k.Contains(EventPathUserState.OptOutResource),
            "opting back in removes the marker rather than storing false");
    }

    [Fact]
    public async Task AddExecutionsAsync_AccumulatesWithinTheSameUtcDay()
    {
        var sut = CreateSut();

        await sut.AddExecutionsAsync(TenantId, UserOid, 1, default);
        await sut.AddExecutionsAsync(TenantId, UserOid, 1, default);

        (await sut.GetTodayExecutionCountAsync(TenantId, UserOid, default)).Should().Be(2);
    }

    [Fact]
    public async Task GetTodayExecutionCountAsync_ResetsAtUtcDayBoundary()
    {
        var sut = CreateSut();
        await sut.AddExecutionsAsync(TenantId, UserOid, 5, default);

        // 23:30Z + 1h crosses into the next UTC day — the budget key rolls over.
        _time.Advance(TimeSpan.FromHours(1));

        (await sut.GetTodayExecutionCountAsync(TenantId, UserOid, default))
            .Should().Be(0, "the daily cap is a per-UTC-day budget (NFR-09)");
    }

    [Fact]
    public async Task Counters_AreIsolatedPerUser()
    {
        var sut = CreateSut();
        await sut.AddExecutionsAsync(TenantId, UserOid, 3, default);

        (await sut.GetTodayExecutionCountAsync(TenantId, "someone-else", default)).Should().Be(0);
    }

    /// <summary>
    /// Deterministic TimeProvider (local fake — mirrors the ManagePinnedContextHandlerTests
    /// pattern; the Microsoft.Extensions.Time.Testing package is not referenced by this project).
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan by) => _utcNow += by;
    }

    /// <summary>Minimal in-memory ITenantCache (the 4 core members; string overlays keep their defaults).</summary>
    private sealed class InMemoryTenantCache : ITenantCache
    {
        public Dictionary<string, object?> Store { get; } = new(StringComparer.Ordinal);

        private static string Key(string tenantId, string resource, string id, int version)
            => $"{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult(Store.TryGetValue(Key(tenantId, resource, id, version), out var v) && v is T t
                ? t
                : default(T?));

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Store[Key(tenantId, resource, id, version)] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            Store.Remove(Key(tenantId, resource, id, version));
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null)
            {
                return existing;
            }
            var created = await factory(ct);
            await SetAsync(tenantId, resource, id, version, created, ttl, cacheInstance, ct);
            return created;
        }
    }
}
