// REGRESSION — the OBO Graph token's Redis TTL follows the token's own expiry (spaarkeai-mcp-bff-api-r2 task 030; researcher finding
// 2026-11-26, GraphClientFactory.cs:299). KEEP path: regression.
//
// WHAT WAS WRONG: CreateOnBehalfOfClientAsync cached every OBO Graph token in Redis for a fixed 55 minutes, ignoring
// AuthenticationResult.ExpiresOn. MSAL can return a token from its own cache with far less than an hour left, so late in a long
// playbook run the Redis cache served a token that had already expired.
//
// WHAT RUNS: the REAL GraphClientFactory, the REAL GraphTokenCache and the REAL OrderedCredentialClientProvider (managed-identity
// assertion, an opaque test string). Only MSAL's user token cache is seeded (the shared MSAL cache schema, as McpCallerPathTests does)
// with an OBO Graph token that expires in 20 minutes, so the exchange is answered offline; the distributed cache is an in-memory
// recorder (a module boundary, ADR-038).

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Graph;

public sealed class GraphOboTokenCacheTtlTests
{
    private const string TenantId = "11111111-2222-4333-8444-555555555555";
    private const string BffClientId = "66666666-7777-4888-9999-aaaaaaaaaaaa";
    private const string CallerToken = "caller-assertion-030";
    private const string GraphToken = "graph-obo-token-030";
    private static readonly Guid CallerOid = Guid.Parse("0a0a0a0a-0030-4030-8030-000000000001");

    [Fact(DisplayName = "030 Graph TTL: an OBO Graph token with 20 minutes left is cached for at most 15 minutes (its expiry less the margin), never a fixed 55")]
    public async Task ForUser_TokenWithTwentyMinutesLeft_IsCachedUntilJustBeforeItsOwnExpiry()
    {
        var redis = new RecordingDistributedCache();
        var factory = await FactoryWithSeededGraphTokenAsync(redis, minutesLeft: 20);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + CallerToken;

        await factory.ForUserAsync(http);

        var (value, options) = redis.Writes.Should().ContainSingle("the exchanged token is cached once").Subject.Value;
        Encoding.UTF8.GetString(value).Should().Be(GraphToken);
        options.AbsoluteExpirationRelativeToNow.Should().NotBeNull();
        options.AbsoluteExpirationRelativeToNow!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15),
            "the cache must not outlive the token: 20 minutes of life less the 5-minute margin");
        options.AbsoluteExpirationRelativeToNow!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(13), "the TTL is the token's remaining life, not zero");
    }

    [Theory(DisplayName = "030 Graph TTL: the TTL is the remaining life less the margin; a token within the margin of its expiry is not cached")]
    [InlineData(90, 85.0)]
    [InlineData(60, 55.0)]
    [InlineData(20, 15.0)]
    [InlineData(6, 1.0)]
    [InlineData(5, null)]
    [InlineData(2, null)]
    [InlineData(-1, null)]
    public void OboTokenCacheTtl_FollowsTheTokensExpiry(int minutesLeft, double? expectedMinutes)
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        var ttl = GraphClientFactory.OboTokenCacheTtl(now.AddMinutes(minutesLeft), now);

        ttl.Should().Be(expectedMinutes is { } m ? TimeSpan.FromMinutes(m) : null);
    }

    private static async Task<GraphClientFactory> FactoryWithSeededGraphTokenAsync(IDistributedCache redis, int minutesLeft)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TENANT_ID"] = TenantId,
            ["API_APP_ID"] = BffClientId,
        }).Build();
        var provider = new OrderedCredentialClientProvider(
            Options.Create(new CredentialSelectionOptions { Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) } }),
            new ConfigurationBuilder().Build(),
            NullLogger<OrderedCredentialClientProvider>.Instance,
            new FixedAssertion());
        SeedOboGraphToken(await provider.GetClientAsync(TenantId, BffClientId), CallerToken, minutesLeft);

        var httpClients = new Mock<IHttpClientFactory>();
        httpClients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient());
        return new GraphClientFactory(
            httpClients.Object,
            NullLogger<GraphClientFactory>.Instance,
            new GraphTokenCache(redis, NullLogger<GraphTokenCache>.Instance),
            configuration,
            provider);
    }

    /// <summary>One OBO access token for the Graph scope in MSAL's user token cache, keyed by the hash of <paramref name="userAssertion"/>.</summary>
    private static void SeedOboGraphToken(IConfidentialClientApplication client, string userAssertion, int minutesLeft)
    {
        // MSAL judges the entry against the real system clock (McpCallerPathTests: fixed values are rejected), so this one read stays.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expires = (now + (minutesLeft * 60)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cache = new JsonObject
        {
            ["AccessToken"] = new JsonObject
            {
                ["seed"] = new JsonObject
                {
                    ["home_account_id"] = $"{CallerOid:D}.{TenantId}",
                    ["environment"] = "login.microsoftonline.com",
                    ["client_id"] = BffClientId,
                    ["credential_type"] = "AccessToken",
                    ["realm"] = TenantId,
                    ["target"] = "https://graph.microsoft.com/.default",
                    ["secret"] = GraphToken,
                    ["cached_at"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expires_on"] = expires,
                    ["extended_expires_on"] = expires,
                    ["token_type"] = "Bearer",
                    ["user_assertion_hash"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(userAssertion))),
                },
            },
            ["Account"] = new JsonObject(),
            ["IdToken"] = new JsonObject(),
            ["RefreshToken"] = new JsonObject(),
            ["AppMetadata"] = new JsonObject(),
        };
        var serialized = Encoding.UTF8.GetBytes(cache.ToJsonString());
        var seeded = 0;
        client.UserTokenCache.SetBeforeAccess(args =>
        {
            if (Interlocked.Exchange(ref seeded, 1) == 0)
            {
                args.TokenCache.DeserializeMsalV3(serialized, shouldClearExistingCache: false);
            }
        });
    }

    /// <summary>The BFF's managed-identity assertion: an opaque test string, never a real credential.</summary>
    private sealed class FixedAssertion : IClientAssertionProvider
    {
        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default) => Task.FromResult("test-client-assertion-030");
    }

    /// <summary>An in-memory distributed cache that records every write with its options (the Redis boundary).</summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new();

        public ConcurrentDictionary<string, (byte[] Value, DistributedCacheEntryOptions Options)> Writes { get; } = new();

        public byte[]? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _values.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value;
            Writes[key] = (value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
