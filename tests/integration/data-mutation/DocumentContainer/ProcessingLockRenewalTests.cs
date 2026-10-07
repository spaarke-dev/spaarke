// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the ADR-004 processing lock is what keeps two relocations of one document from both copying and
//     re-pointing it (unified-access-control-r2 task 166, owner rounds 37 and 54 item 2). These tests pin the lock store's
//     renewal and owner-checked release on the REAL IdempotencyService: a renewal extends only its owner's lock and
//     fails CLOSED; a release never removes another owner's lock.
//
// Doubles: the IDistributedCache boundary only — an in-memory cache that honours absolute expiry on a FakeTimeProvider
// (Redis's contract for these calls), and can be told to fault. No Mock<HttpMessageHandler>, no DI assertion.

using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class ProcessingLockRenewalTests
{
    private const string Event = "document-relocate-0123456789abcdef0123456789abcdef";
    private const string Key = "idempotency:lock:" + Event;
    private static readonly TimeSpan Ten = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly ExpiringCache _cache;
    private readonly IdempotencyService _locks;

    public ProcessingLockRenewalTests()
    {
        _cache = new ExpiringCache(_time);
        _locks = new IdempotencyService(_cache, NullLogger<IdempotencyService>.Instance, _time);
    }

    [Fact]
    public async Task ARenewal_ExtendsTheOwnersLock_PastItsFirstExpiry()
    {
        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-a", Ten)).Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(8));

        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(8)); // 16 minutes after the take: past the first expiry, inside the renewed one

        _cache.Holds(Key).Should().BeTrue();
        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-b", Ten)).Should().BeFalse("the renewed lock is still held");
    }

    [Fact]
    public async Task WithoutARenewal_TheLockExpires_AndAnotherOwnerTakesIt()
    {
        // The control for the test above: the expiry is real, so the renewal is what kept the lock.
        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-a", Ten)).Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(16));

        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-b", Ten)).Should().BeTrue();
        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeFalse("the lock is owner-b's now");
    }

    [Fact]
    public async Task ARenewal_OfALockAnotherOwnerHolds_OrNoOneHolds_OrAnOwnerlessOne_IsRefused_AndChangesNothing()
    {
        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeFalse("no one holds it");
        _cache.WritesOf(Key).Should().Be(0, "a refused renewal never creates the lock");

        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-b", Ten)).Should().BeTrue();
        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeFalse("another owner holds it");
        Encoding.UTF8.GetString(_cache.Get(Key)!).Should().StartWith("owner-b|");

        await _locks.ReleaseProcessingLockAsync(Event);
        (await _locks.TryAcquireProcessingLockAsync(Event, Ten)).Should().BeTrue(); // an ownerless holder ("locked")
        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeFalse("an ownerless lock is no owner's");
    }

    [Fact]
    public async Task ARenewal_WhenTheCacheFaults_IsRefused_NotAssumed()
    {
        // The acquire fails OPEN on a cache fault (#984); a renewal must fail CLOSED, or a holder would run on unlocked.
        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-a", Ten)).Should().BeTrue();
        _cache.Fault = new TimeoutException("Redis unavailable");

        (await _locks.RenewProcessingLockAsync(Event, "owner-a", Ten)).Should().BeFalse();
    }

    [Fact]
    public async Task AnOwnersRelease_RemovesOnlyItsOwnLock()
    {
        (await _locks.TryAcquireProcessingLockAsync(Event, "owner-b", Ten)).Should().BeTrue();

        await _locks.ReleaseProcessingLockAsync(Event, "owner-a");
        _cache.Holds(Key).Should().BeTrue("owner-a lost this lock to owner-b; its release leaves owner-b's lock");

        await _locks.ReleaseProcessingLockAsync(Event, "owner-b");
        _cache.Holds(Key).Should().BeFalse();
    }
}

/// <summary>
/// An in-memory <see cref="IDistributedCache"/> that honours <see cref="DistributedCacheEntryOptions.AbsoluteExpirationRelativeToNow"/>
/// on a <see cref="TimeProvider"/> (Redis's contract for the lock store's calls), counts the writes of each key, and can
/// be told to fault every call.
/// </summary>
internal sealed class ExpiringCache : IDistributedCache
{
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (byte[] Value, DateTimeOffset? Expires)> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _writes = new(StringComparer.Ordinal);

    public ExpiringCache(TimeProvider time) => _time = time;

    /// <summary>When set, every call throws it (the cache is unreachable).</summary>
    public Exception? Fault { get; set; }

    /// <summary>How many times <paramref name="key"/> was written.</summary>
    public int WritesOf(string key) => _writes.GetValueOrDefault(key);

    /// <summary>Whether <paramref name="key"/> holds an unexpired value.</summary>
    public bool Holds(string key) => Read(key) is not null;

    public byte[]? Get(string key)
    {
        ThrowIfFaulted();
        return Read(key);
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ThrowIfFaulted();
        var now = _time.GetUtcNow();
        var expires = options.AbsoluteExpirationRelativeToNow is { } ttl ? now + ttl : options.AbsoluteExpiration;
        _entries[key] = (value, expires);
        _writes.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key) => ThrowIfFaulted();

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        ThrowIfFaulted();
        _entries.TryRemove(key, out _);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    private byte[]? Read(string key)
        => _entries.TryGetValue(key, out var entry) && (entry.Expires is not { } expires || _time.GetUtcNow() < expires)
            ? entry.Value
            : null;

    private void ThrowIfFaulted()
    {
        if (Fault is not null)
        {
            throw Fault;
        }
    }
}
