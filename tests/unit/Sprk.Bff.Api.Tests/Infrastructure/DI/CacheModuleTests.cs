// spaarke-redis-cache-remediation-r1 — Task 009 (2026-06-25)
// Unit tests for CacheModule (4-branch decision matrix) + NullConnectionMultiplexer
// observable contract. Binds FR-08 + NFR-13.
//
// Test categories:
//   1. Redis_On_*                              — Redis-on branch (branch a)
//   2. Redis_Off_AllowFallback_Development_*   — In-memory + Null-Object (branch b)
//   3. Redis_Off_AllowFallback_NonDev_Throws   — Env-guard fail-fast (branch c)
//   4. Redis_Off_NoFallbackOptIn_Throws        — Default fail-fast (branch d)
//   5. NullConnectionMultiplexer_*             — ADR-032 Null-Object semantics
//
// Reference: projects/spaarke-redis-cache-remediation-r1/tasks/009-cache-module-tests.poml
//            src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs
//            src/server/api/Sprk.Bff.Api/Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Cache.NullObjects;
using Sprk.Bff.Api.Infrastructure.DI;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// Unit tests for CacheModule (4-branch decision matrix) + Null-Object semantics
/// of the registered <see cref="IConnectionMultiplexer"/> in Redis-off branches.
/// Per FR-08 + NFR-13 (coverage for all 4 branches is mandatory).
/// </summary>
public class CacheModuleTests
{
    // -------------------------------------------------------------------
    // Test fakes
    // -------------------------------------------------------------------

    /// <summary>
    /// Minimal fake <see cref="IHostEnvironment"/> for controlling
    /// <see cref="HostEnvironmentEnvExtensions.IsDevelopment"/> in tests.
    /// </summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Sprk.Bff.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ILoggingBuilder GetLoggingBuilder(IServiceCollection services)
    {
        // CacheModule signature takes ILoggingBuilder; the easiest way to obtain one
        // is via services.AddLogging which exposes the builder through a callback.
        ILoggingBuilder? captured = null;
        services.AddLogging(builder => captured = builder);
        return captured!;
    }

    // ===================================================================
    // Branch (a) — Redis on
    // ===================================================================

    [Fact(Skip = "Requires live Redis to satisfy AbortOnConnectFail=true (ConnectionMultiplexer.Connect runs at DI-registration time per CacheModule.cs:84). Covered by manual harness tests/manual/RedisValidationTests.ps1.")]
    public void Redis_On_RegistersRealConnectionMultiplexer_And_RedisCache()
    {
        // Arrange
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "true",
            ["Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
            ["Redis:InstanceName"] = "spaarke:",
        });

        // Act
        services.AddCacheModule(config, logging, env);
        using var provider = services.BuildServiceProvider();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        var distCache = provider.GetRequiredService<IDistributedCache>();

        // Assert
        multiplexer.Should().NotBeNull();
        multiplexer.Should().NotBeOfType<NullConnectionMultiplexer>();
        distCache.GetType().Name.Should().Be("RedisCache");
    }

    [Fact]
    public void Redis_On_NoConnectionString_Throws()
    {
        // Arrange — exercises the Redis-on branch up to the connection-string guard
        // without requiring a live Redis instance. Verifies that the helpful error
        // message is preserved (FR-01 fail-fast at startup with operator-friendly text).
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "true",
            // Intentionally no ConnectionStrings:Redis and no Redis:ConnectionString
        });

        // Act
        Action act = () => services.AddCacheModule(config, logging, env);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Redis is enabled but no connection string was found*");
    }

    // ===================================================================
    // Branch (b) — Redis off + AllowInMemoryFallback + Development
    // ===================================================================

    [Fact]
    public void Redis_Off_AllowFallback_Development_RegistersInMemoryAndNullMultiplexer()
    {
        // Arrange
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
        });

        // Act
        services.AddCacheModule(config, logging, env);
        using var provider = services.BuildServiceProvider();
        var distCache = provider.GetRequiredService<IDistributedCache>();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        var memCache = provider.GetRequiredService<IMemoryCache>();
        var tenantCache = provider.GetRequiredService<ITenantCache>();

        // Assert — IDistributedCache is the MetricsDistributedCache decorator wrapping the
        // in-memory cache (R7-S7 sub-gap #2 closure 2026-06-26); Null-Object multiplexer;
        // companion IMemoryCache + ITenantCache all resolve symmetrically.
        distCache.Should().BeOfType<MetricsDistributedCache>();
        multiplexer.Should().BeOfType<NullConnectionMultiplexer>();
        memCache.Should().NotBeNull();
        tenantCache.Should().BeOfType<TenantCache>();
    }

    // ===================================================================
    // Branch (b) extended — Redis off + AllowInMemoryFallback + Testing
    // (CI safety carve-out, 2026-06-29 follow-on to AzureMonitorGuard Testing
    // allow-list; WAF-based integration tests use ASPNETCORE_ENVIRONMENT=Testing).
    // ===================================================================

    [Fact]
    public void Redis_Off_AllowFallback_Testing_RegistersInMemoryAndNullMultiplexer()
    {
        // Arrange
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = "Testing" };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
        });

        // Act
        services.AddCacheModule(config, logging, env);
        using var provider = services.BuildServiceProvider();
        var distCache = provider.GetRequiredService<IDistributedCache>();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        // Assert — same wiring as Development branch: decorator + Null-Object multiplexer.
        distCache.Should().BeOfType<MetricsDistributedCache>();
        multiplexer.Should().BeOfType<NullConnectionMultiplexer>();
    }

    [Theory]
    [InlineData("testing")]    // lowercase
    [InlineData("TESTING")]    // uppercase
    [InlineData("Testing")]    // canonical
    public void Redis_Off_AllowFallback_TestingEnvNameIsCaseInsensitive(string envName)
    {
        // Mirrors AzureMonitorGuardTests case-insensitive Theory — env name
        // matching must follow ASP.NET Core convention regardless of casing.
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = envName };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
        });

        Action act = () => services.AddCacheModule(config, logging, env);

        act.Should().NotThrow();
    }

    // ===================================================================
    // Branch (c) — Redis off + AllowInMemoryFallback + deployed env → throws
    // (Development AND Testing are allow-listed; Staging/Production/etc. throw.)
    // ===================================================================

    [Fact]
    public void Redis_Off_AllowFallback_NonDevelopment_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = "Staging" };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
        });

        // Act
        Action act = () => services.AddCacheModule(config, logging, env);

        // Assert — env-guard fail-fast (FR-03). Error message must surface
        // the actual env name + remediation pointer ("Set Redis:Enabled=true").
        // The message was updated 2026-06-29 to say "Development and Testing
        // environments" (plural carve-out) instead of the original "Development
        // environments" phrasing.
        act.Should()
            .Throw<InvalidOperationException>()
            .Where(ex =>
                ex.Message.Contains("Development and Testing environments", StringComparison.Ordinal) &&
                ex.Message.Contains("Staging", StringComparison.Ordinal) &&
                ex.Message.Contains("Redis:Enabled=true", StringComparison.Ordinal));
    }

    [Fact]
    public void Redis_Off_AllowFallback_Production_Throws()
    {
        // Parity coverage — Production should reject the fallback path just
        // like Staging. Confirms Testing-allow-listing didn't accidentally
        // open the Production branch.
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = "Production" };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true",
        });

        Action act = () => services.AddCacheModule(config, logging, env);

        act.Should()
            .Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Production", StringComparison.Ordinal));
    }

    // ===================================================================
    // Branch (d) — Redis off + no fallback opt-in → throws
    // ===================================================================

    [Fact]
    public void Redis_Off_NoFallbackOptIn_Throws()
    {
        // Arrange — even in Development, omitting AllowInMemoryFallback fails fast.
        var services = new ServiceCollection();
        var logging = GetLoggingBuilder(services);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "false",
        });

        // Act
        Action act = () => services.AddCacheModule(config, logging, env);

        // Assert — default fail-fast (FR-02). Error message must list both
        // remediation paths and mark the in-memory path as Development-or-Testing
        // only (Testing was added 2026-06-29 as a CI safety carve-out — see
        // CacheModule.cs branch (b)/(c) comments).
        act.Should()
            .Throw<InvalidOperationException>()
            .Where(ex =>
                ex.Message.Contains("Redis:Enabled=true", StringComparison.Ordinal) &&
                ex.Message.Contains("AllowInMemoryFallback=true", StringComparison.Ordinal) &&
                ex.Message.Contains("Development or Testing only", StringComparison.Ordinal));
    }

    // ===================================================================
    // NullConnectionMultiplexer — ADR-032 Null-Object semantics
    // ===================================================================

    [Fact]
    public void NullConnectionMultiplexer_Subscribe_IsNoOp_AndPublishReturnsZero()
    {
        // Arrange
        var logger = NullLogger<NullConnectionMultiplexer>.Instance;
        var sut = new NullConnectionMultiplexer(logger);
        var handlerInvoked = false;
        var subscriber = sut.GetSubscriber();
        var channel = RedisChannel.Literal("test-channel");

        // Act — Subscribe accepts the callback but must never deliver it.
        subscriber.Subscribe(
            channel,
            (_, _) => handlerInvoked = true);

        // Publish returns 0 (no subscribers reached) per ADR-032 P2 Quiet semantics.
        var publishedCount = subscriber.Publish(channel, RedisValue.Null);

        // Assert
        handlerInvoked.Should().BeFalse("Null-Object Subscribe must be a no-op (P2 Quiet)");
        publishedCount.Should().Be(0L);
    }

    [Fact]
    public async Task NullConnectionMultiplexer_GetDatabase_Throws_NotSupported()
    {
        // Arrange
        var logger = NullLogger<NullConnectionMultiplexer>.Instance;
        var sut = new NullConnectionMultiplexer(logger);
        var database = sut.GetDatabase();

        // Act
        Func<Task> act = async () => await database.StringGetAsync("foo");

        // Assert — P3 Fail-fast for direct DB ops; message must instruct
        // operators to use IDistributedCache instead.
        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage(
                "*In-memory cache mode does not support direct Redis database operations*" +
                "*IDistributedCache*");
    }

    [Fact]
    public void NullConnectionMultiplexer_LogsWarningOnFirstGetSubscriber_Only()
    {
        // Arrange — verifies the log-once-at-first-use pattern (sentinel
        // Interlocked.Exchange ref _subscriberWarningLogged on NullConnectionMultiplexer:135).
        var logger = new CountingLogger<NullConnectionMultiplexer>();
        var sut = new NullConnectionMultiplexer(logger);

        // Act — call GetSubscriber multiple times.
        _ = sut.GetSubscriber();
        _ = sut.GetSubscriber();
        _ = sut.GetSubscriber();

        // Assert — exactly one Warning entry across the three calls.
        // (Information entry from the ctor is counted separately.)
        logger.WarningCount.Should().Be(1, "GetSubscriber must log a warning exactly once");
        logger.InformationCount.Should().Be(1, "Constructor logs Information once; never duplicated");
    }

    // -------------------------------------------------------------------
    // Counting logger — tracks invocations of LogWarning / LogInformation
    // without depending on Moq's tricky extension-method patterns.
    // -------------------------------------------------------------------
    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }
        public int InformationCount { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            switch (logLevel)
            {
                case LogLevel.Warning:
                    WarningCount++;
                    break;
                case LogLevel.Information:
                    InformationCount++;
                    break;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
