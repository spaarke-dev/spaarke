using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.DI;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Cache;

/// <summary>
/// Regression (2026-10-05, customer-provisioning-orchestration-r1 T242b review): the <c>redis</c>
/// /healthz check used to call <c>services.BuildServiceProvider()</c> on every probe and run the
/// synchronous IDistributedCache set/get/remove on a fresh container. Each probe leaked a service
/// container (the dev BFF's working set grew 17–20 MB/hour) and about 30% of probes stalled
/// 500–2000 ms, polluting <c>cache.redis_call_duration_ms</c>. The check is now one async PING on the
/// app's singleton multiplexer.
/// </summary>
public class RedisHealthCheckProbeRegressionTests
{
    [Fact]
    public async Task RedisHealthCheck_WhenProbedTwice_UsesTheAppSingletonMultiplexerAndNeverTheDistributedCache()
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(database.Object);
        // Strict: any IDistributedCache call from the probe fails the test.
        var distributedCache = new Mock<IDistributedCache>(MockBehavior.Strict);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Redis:Enabled"] = "true" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(multiplexer.Object);
        services.AddSingleton(distributedCache.Object);
        services.AddTelemetryModule(configuration);
        await using var provider = services.BuildServiceProvider();
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var first = await healthChecks.CheckHealthAsync(r => r.Name == "redis");
        var second = await healthChecks.CheckHealthAsync(r => r.Name == "redis");

        first.Entries["redis"].Status.Should().Be(HealthStatus.Healthy);
        second.Entries["redis"].Status.Should().Be(HealthStatus.Healthy);
        database.Verify(d => d.PingAsync(It.IsAny<CommandFlags>()), Times.Exactly(2),
            "both probes must PING through the one singleton multiplexer the app registered");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRedisDisabled_ReturnsHealthyWithoutCallingRedis()
    {
        var result = await new RedisHealthCheck(multiplexer: null).CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("disabled");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingFails_ReturnsUnhealthyWithTheException()
    {
        var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "no route");
        var database = new Mock<IDatabase>();
        database.Setup(d => d.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(failure);
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(database.Object);

        var result = await new RedisHealthCheck(multiplexer.Object).CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeSameAs(failure);
    }

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("redis", Mock.Of<IHealthCheck>(), failureStatus: null, tags: null),
    };
}
