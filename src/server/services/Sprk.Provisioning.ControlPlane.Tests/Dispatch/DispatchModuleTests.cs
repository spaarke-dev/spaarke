// -----------------------------------------------------------------------------
// DispatchModuleTests.cs
//
// L2 CONTROL-PLANE tests for DispatchModule.AddDispatchModule's Level-2 cache
// environment gate (task 105, Phase C'' Wave G-1 code-review follow-up).
//
// WHY THIS TEST EXISTS:
//   The original task 105 draft let AddDispatchModule silently fall back to
//   AddDistributedMemoryCache() whenever Redis:ConnectionString was unset,
//   in EVERY environment. Self-review flagged this as itself a silent-fail
//   trap of the exact class this project exists to eliminate (a deployed,
//   multi-instance Worker would silently degrade Level 2 to same-instance-
//   only dedup with no operator signal). The fix gates the fallback to
//   Development/Testing only (mirrors BFF CacheModule's isLocalLike carve-
//   out) and throws in every other environment -- this is a real,
//   NFR-05-flavored fail-fast contract, not a trivial DI-registration-
//   presence check (ADR-038 B7's banned pattern), so it earns a test.
//
// SEAM STRATEGY: builds a real ServiceCollection + IConfiguration + a
// minimal hand-rolled IHostEnvironment (only EnvironmentName is read by the
// gate) and calls AddDispatchModule directly. No live Redis connection is
// ever opened (AddStackExchangeRedisCache does not connect eagerly).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Dispatch;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class DispatchModuleTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AddDispatchModule_NoRedisConnectionString_LocalLikeEnvironment_FallsBackToInMemoryCache(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: null);
        var environment = new FakeHostEnvironment(environmentName);

        services.AddDispatchModule(configuration, environment);
        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDistributedCache>();
        cache.Should().NotBeNull(
            $"'{environmentName}' is allow-listed for the in-memory fallback -- unit-test hosts and " +
            "local dev must not require a live Redis connection.");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Demo")]
    public void AddDispatchModule_NoRedisConnectionString_DeployedEnvironment_ThrowsAtStartup(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: null);
        var environment = new FakeHostEnvironment(environmentName);

        var act = () => services.AddDispatchModule(configuration, environment);

        act.Should().Throw<InvalidOperationException>(
            $"an unconfigured Level-2 cache in a deployed environment ('{environmentName}') must fail " +
            "LOUDLY at startup (NFR-05) -- silently degrading to same-instance-only dedup with no " +
            "operator signal is exactly the silent-fail-trap class this project exists to eliminate.")
            .WithMessage("*Redis*");
    }

    // ---------- Task 242: Redis:Endpoint (managed identity) vs connection string ----------

    [Fact]
    public void AddDispatchModule_Endpoint_DeployedEnvironment_RegistersManagedIdentityMultiplexerFactory()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            redisConnectionString: null, redisEndpoint: Endpoint, managedIdentityClientId: ClientId);

        services.AddDispatchModule(configuration, new FakeHostEnvironment("Production"));
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        options.ConnectionMultiplexerFactory.Should().NotBeNull(
            "with an endpoint the Worker builds its own Entra-authenticated multiplexer (no connect at startup)");
        options.Configuration.Should().BeNull("no connection string is used when an endpoint is set");
        options.InstanceName.Should().Be("provisioning:");
    }

    [Fact]
    public void AddDispatchModule_Endpoint_WithoutManagedIdentityClientId_ThrowsNamingTheSetting()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: null, redisEndpoint: Endpoint);

        var act = () => services.AddDispatchModule(configuration, new FakeHostEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ManagedIdentity__ClientId*");
    }

    [Fact]
    public async Task BuildManagedIdentityOptionsAsync_AuthenticatesWithTheClientId_OverResp3AndTls_WithoutAPassword()
    {
        string? clientIdUsed = null;

        var options = await DispatchModule.BuildManagedIdentityOptionsAsync(
            Endpoint, ClientId, (_, id) => { clientIdUsed = id; return Task.CompletedTask; });

        clientIdUsed.Should().Be(ClientId);
        options.Protocol.Should().Be(RedisProtocol.Resp3, "RESP3 lets the token refresh re-authenticate every connection");
        options.Ssl.Should().BeTrue();
        options.Password.Should().BeNullOrEmpty();
        options.EndPoints.Should().ContainSingle().Which.ToString().Should().Contain("10000");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AddDispatchModule_ConnectionStringOnly_DeployedEnvironment_ThrowsNamingRedisEndpoint(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: "localhost:6379");

        var act = () => services.AddDispatchModule(configuration, new FakeHostEnvironment(environmentName));

        act.Should().Throw<InvalidOperationException>(
            "deployed environments are keyless (task 242) -- a connection string without an endpoint is refused")
            .WithMessage("*Redis__Endpoint*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AddDispatchModule_ConnectionStringOnly_LocalLikeEnvironment_RegistersRedisCache(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: "localhost:6379");

        services.AddDispatchModule(configuration, new FakeHostEnvironment(environmentName));
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        options.Configuration.Should().Be("localhost:6379");
    }

    [Fact]
    public void AddDispatchModule_RegistersIDispatchIdempotencyService_AsDispatchIdempotencyService()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // DispatchIdempotencyService's ctor takes ILogger<T>.
        var configuration = BuildConfiguration(redisConnectionString: null);
        var environment = new FakeHostEnvironment("Development");

        services.AddDispatchModule(configuration, environment);
        var provider = services.BuildServiceProvider();

        var idempotency = provider.GetRequiredService<IDispatchIdempotencyService>();

        idempotency.Should().BeOfType<DispatchIdempotencyService>(
            "task 105 swaps the DI-registered default from task 102's NoOpDispatchIdempotencyService " +
            "to the real Redis-backed implementation.");
    }

    private const string Endpoint = "sprk-acme-prod-redis.westus2.redis.azure.net:10000";
    private const string ClientId = "00000000-1111-2222-3333-555555555555";

    private static IConfiguration BuildConfiguration(
        string? redisConnectionString, string? redisEndpoint = null, string? managedIdentityClientId = null)
    {
        var data = new Dictionary<string, string?>
        {
            ["Dispatcher:Enabled"] = "true",
        };

        if (redisConnectionString is not null)
        {
            data["Redis:ConnectionString"] = redisConnectionString;
        }

        if (redisEndpoint is not null)
        {
            data["Redis:Endpoint"] = redisEndpoint;
        }

        if (managedIdentityClientId is not null)
        {
            data["ManagedIdentity:ClientId"] = managedIdentityClientId;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    /// <summary>Minimal hand-rolled IHostEnvironment -- only EnvironmentName is read by AddDispatchModule's gate.</summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Sprk.Provisioning.ControlPlane.Worker.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
