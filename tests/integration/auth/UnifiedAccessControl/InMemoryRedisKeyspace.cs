using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// One in-memory key space seen two ways, the way a real deployment sees Redis (unified-access-control-r2 task 132):
/// as the <see cref="IDistributedCache"/> the production <see cref="TenantCache"/> writes through, and as the
/// <see cref="IConnectionMultiplexer"/> the production <see cref="MembershipCacheInvalidator"/> scans and deletes
/// through.
/// </summary>
/// <remarks>
/// <para><b>Why both views share ONE store.</b> The eviction defect this harness exists to catch is a pattern that
/// matches nothing — a resource, version, segment order or <c>InstanceName</c> that differs from what readers write.
/// So readers write through the PRODUCTION <see cref="TenantCache"/>, and this cache prepends
/// <see cref="InstanceName"/> to every key exactly as <c>StackExchangeRedisCache</c> does (a raw string prefix). The
/// invalidator's patterns then run against those real keys.</para>
/// <para><b>Glob semantics.</b> <see cref="Matches"/> implements the Redis <c>SCAN MATCH</c> glob for the one
/// metacharacter the production patterns use, <c>*</c>, and THROWS for <c>?</c>, <c>[</c> and <c>\</c>, so a
/// pattern that leaned on a feature this model does not implement fails loudly rather than being judged by a wrong
/// model.</para>
/// <para>Not a transport double (ADR-038 B1): no wire format is encoded. The Redis client interfaces are stood in for
/// at their API (<c>GetEndPoints</c> / <c>GetServer</c> / <c>KeysAsync</c> / <c>KeyDeleteAsync</c>), the same seam
/// <c>MembershipCacheInvalidatorTests</c> already uses for the publish half.</para>
/// </remarks>
internal sealed class InMemoryRedisKeyspace : IDistributedCache
{
    /// <summary>The production default <c>Redis:InstanceName</c>.</summary>
    public const string InstanceName = "spaarke:";

    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    /// <summary>When true, every key scan throws — a Redis outage during eviction.</summary>
    public bool FailScans { get; set; }

    /// <summary>The on-wire keys present (InstanceName included).</summary>
    public IReadOnlyCollection<string> Keys => _store.Keys.ToList();

    /// <summary>Every pattern a scan was asked for, in order.</summary>
    public ConcurrentQueue<string> ScannedPatterns { get; } = new();

    /// <summary>The production wrapper over this key space.</summary>
    public ITenantCache TenantCache() => new TenantCache(this, NullLogger<TenantCache>.Instance);

    /// <summary>The on-wire form of a <see cref="TenantCache"/> key — what a read wrote.</summary>
    public static string OnWire(string tenant, string resource, string id, int version)
        => InstanceName + Sprk.Bff.Api.Infrastructure.Cache.TenantCache.BuildKey(tenant, resource, id, version);

    public bool Contains(string onWireKey) => _store.ContainsKey(onWireKey);

    /// <summary>The PRODUCTION invalidator over this key space, with the channel switch as given.</summary>
    public MembershipCacheInvalidator Invalidator(bool channelSwitch = false, Microsoft.Extensions.Logging.ILogger<MembershipCacheInvalidator>? logger = null)
        => new(
            Multiplexer(),
            Options.Create(new MembershipCacheInvalidatorOptions { Enabled = channelSwitch }),
            Options.Create(new RedisOptions { InstanceName = InstanceName }),
            TimeProvider.System,
            logger ?? NullLogger<MembershipCacheInvalidator>.Instance);

    /// <summary>
    /// A Redis glob match for the subset the production patterns use (see the class remarks).
    /// </summary>
    public static bool Matches(string pattern, string key)
    {
        if (pattern.IndexOfAny(new[] { '?', '[', '\\' }) >= 0)
        {
            throw new NotSupportedException(
                $"Pattern '{pattern}' uses a glob feature this model does not implement; extend Matches before relying on it.");
        }

        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(key, regex, RegexOptions.CultureInvariant);
    }

    /// <summary>The <see cref="IConnectionMultiplexer"/> view: one connected primary over this store.</summary>
    public IConnectionMultiplexer Multiplexer()
    {
        var endpoint = new DnsEndPoint("in-memory-redis", 6379);

        var server = new Mock<IServer>();
        server.SetupGet(s => s.IsConnected).Returns(true);
        server.SetupGet(s => s.IsReplica).Returns(false);
        server
            .Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns((int _, RedisValue pattern, int _, long _, int _, CommandFlags _) => Scan(pattern.ToString()));

        var database = new Mock<IDatabase>();
        database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, CommandFlags flags) => Task.FromResult(_store.TryRemove(key.ToString(), out _)));

        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { endpoint });
        multiplexer.Setup(m => m.GetServer(It.IsAny<EndPoint>(), It.IsAny<object?>())).Returns(server.Object);
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(database.Object);
        multiplexer.Setup(m => m.GetSubscriber(It.IsAny<object?>())).Returns(Mock.Of<ISubscriber>());
        return multiplexer.Object;
    }

    private async IAsyncEnumerable<RedisKey> Scan(string pattern)
    {
        ScannedPatterns.Enqueue(pattern);
        if (FailScans)
        {
            throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "simulated Redis outage during SCAN");
        }

        // Snapshot first, like a SCAN cursor over a key space that changes underneath it.
        foreach (var key in _store.Keys.Where(k => Matches(pattern, k)).ToList())
        {
            await Task.Yield();
            yield return key;
        }
    }

    // ── IDistributedCache — InstanceName applied the way StackExchangeRedisCache applies it ────────────────

    public byte[]? Get(string key) => _store.TryGetValue(InstanceName + key, out var value) ? value : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[InstanceName + key] = value;

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _store.TryRemove(InstanceName + key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}
