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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Provisioning.ControlPlane.Dispatch;
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

    [Fact]
    public void AddDispatchModule_RedisConnectionStringSet_DoesNotThrow_RegardlessOfEnvironment()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(redisConnectionString: "localhost:6379");
        var environment = new FakeHostEnvironment("Production");

        var act = () => services.AddDispatchModule(configuration, environment);

        act.Should().NotThrow(
            "a configured Redis connection string satisfies the gate in every environment -- " +
            "AddStackExchangeRedisCache does not connect eagerly, so this registers without any " +
            "live network call.");
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

    private static IConfiguration BuildConfiguration(string? redisConnectionString)
    {
        var data = new Dictionary<string, string?>
        {
            ["Dispatcher:Enabled"] = "true",
        };

        if (redisConnectionString is not null)
        {
            data["Redis:ConnectionString"] = redisConnectionString;
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
