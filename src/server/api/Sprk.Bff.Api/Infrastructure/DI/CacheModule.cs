using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Cache.NullObjects;
using Sprk.Bff.Api.Infrastructure.Caching;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for distributed cache services (ADR-009).
/// Implements the 4-branch decision logic per spaarke-redis-cache-remediation-r1
/// (FR-01..03): Redis-on (real connection, fail-fast), Redis-off + AllowInMemoryFallback
/// + Development (in-memory + Null-Object IConnectionMultiplexer per ADR-032),
/// Redis-off + AllowInMemoryFallback + NOT Development (throw at startup),
/// Redis-off + no fallback opt-in (throw at startup).
/// </summary>
public static class CacheModule
{
    /// <summary>
    /// Adds distributed cache (Redis or in-memory) and memory cache services.
    /// Production-like dev semantics: <c>AbortOnConnectFail=true</c> on the Redis-on
    /// path; deployed environments without Redis fail-fast at startup; only
    /// Development may opt into in-memory fallback.
    /// </summary>
    public static IServiceCollection AddCacheModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging,
        IHostEnvironment environment)
    {
        // Bind RedisOptions (Enabled, ConnectionString, InstanceName, AllowInMemoryFallback)
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);

        // Bootstrap logger for startup branch-selection diagnostics.
        // The host's structured logger is not yet built at DI-registration time.
        var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("CacheModule");

        var isDevelopment = environment.IsDevelopment();

        // 4-branch decision matrix on (Enabled, AllowInMemoryFallback, IsDevelopment).
        if (redisOptions.Enabled)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (a): Redis-on — real connection, fail-fast.
            // ────────────────────────────────────────────────────────────────────
            var redisConnectionString = configuration.GetConnectionString("Redis")
                ?? configuration["Redis:ConnectionString"];

            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis is enabled but no connection string was found. " +
                    "Set 'ConnectionStrings:Redis' or 'Redis:ConnectionString' in configuration. " +
                    "In Azure App Service, this is typically a Key Vault reference of the form " +
                    "'@Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)'. " +
                    "Verify the App Setting exists and that the Managed Identity has 'get' permission on the secret.");
            }

            StackExchange.Redis.ConfigurationOptions configOptions;
            try
            {
                configOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to parse Redis connection string. " +
                    "Source: 'ConnectionStrings:Redis' or 'Redis:ConnectionString' App Setting " +
                    "(typically backed by a Key Vault reference). " +
                    "Verify the secret value resolves to a valid StackExchange.Redis configuration string.",
                    ex);
            }

            // Production-like dev (FR-01): fail-fast on connect.
            configOptions.AbortOnConnectFail = true;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(1000);

            StackExchange.Redis.IConnectionMultiplexer connectionMultiplexer;
            try
            {
                connectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to connect to Redis at startup (AbortOnConnectFail=true). " +
                    "Connection-string source: 'ConnectionStrings:Redis' or 'Redis:ConnectionString' App Setting " +
                    "(typically a Key Vault reference '@Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)'). " +
                    "Check (1) the Redis instance is running and reachable; " +
                    "(2) the Key Vault secret resolves and contains a valid connection string; " +
                    "(3) firewall / private-endpoint rules allow the App Service outbound IP; " +
                    "(4) the configured password / access key matches the Redis instance.",
                    ex);
            }

            // ADR-032 symmetric registration: real IConnectionMultiplexer in Redis-on path.
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(connectionMultiplexer);

            // R7-S7 sub-gap #1 closure (2026-06-26): wire ConnectionMultiplexerFactory so
            // Microsoft.Extensions.Caching.StackExchangeRedis reuses the DI-registered multiplexer
            // instead of constructing its own internal one. Without this, the DI-registered
            // multiplexer (which `OpenTelemetry.Instrumentation.StackExchangeRedis.AddRedisInstrumentation()`
            // hooks at startup) is idle in the cache hot path and zero Redis dependency spans
            // reach App Insights. When this factory is set, options.Configuration /
            // ConfigurationOptions are ignored, so they are intentionally NOT set here.
            services.AddStackExchangeRedisCache(options =>
            {
                options.InstanceName = redisOptions.InstanceName;
                options.ConnectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer);
            });

            logging.AddSimpleConsole().Services.Configure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
            });

            // Exact log string verified by Phase 3 task 034 — do not modify.
            logger.LogInformation(
                "Distributed cache: Redis enabled with instance name '{InstanceName}'",
                redisOptions.InstanceName);
        }
        else if (redisOptions.AllowInMemoryFallback && isDevelopment)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (b): Redis-off + AllowInMemoryFallback + Development.
            // ────────────────────────────────────────────────────────────────────
            services.AddDistributedMemoryCache();

            // ADR-032 symmetric registration: Null-Object IConnectionMultiplexer
            // so consumers (e.g., JobStatusService pub/sub) can inject unconditionally.
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer, NullConnectionMultiplexer>();

            logger.LogWarning(
                "Distributed cache: In-memory mode enabled (Development only). " +
                "NOT suitable for multi-instance deployment.");
        }
        else if (redisOptions.AllowInMemoryFallback && !isDevelopment)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (c): Redis-off + AllowInMemoryFallback + NOT Development → throw.
            // ────────────────────────────────────────────────────────────────────
            throw new InvalidOperationException(
                $"AllowInMemoryFallback is restricted to Development environments. " +
                $"ASPNETCORE_ENVIRONMENT={environment.EnvironmentName}. Set Redis:Enabled=true.");
        }
        else
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (d): Redis-off + no fallback opt-in → throw.
            // ────────────────────────────────────────────────────────────────────
            throw new InvalidOperationException(
                "Redis is disabled and in-memory fallback not opted in. " +
                "Set Redis:Enabled=true (recommended) or Redis:AllowInMemoryFallback=true (Development only).");
        }

        services.AddMemoryCache();

        // ADR-009 enforcement (CICD-087, 2026-06-26): wrap IMemoryCache behind
        // IEndpointResponseCache so *Endpoints classes don't import
        // Microsoft.Extensions.Caching.Memory directly. NetArchTest
        // ADR009_CachingTests.MemoryCacheShouldNotBeSingleton enforces.
        services.AddSingleton<IEndpointResponseCache, EndpointResponseCache>();

        // R7-S7 sub-gap #2 closure (2026-06-26): decorate IDistributedCache with
        // MetricsDistributedCache so cache.* Meter instruments fire on EVERY call —
        // including the system-cache exception path (CommunicationAccountService,
        // MSAL token cache, membership refresh) that injects IDistributedCache
        // directly and bypasses TenantCache. Without this decorator, those ~11 hot-path
        // system-cache sites emitted zero metrics.
        DecorateDistributedCacheWithMetrics(services);

        // Tenant-scoped cache wrapper (FR-05, NFR-12). Wraps the (now metrics-decorated)
        // IDistributedCache and enforces mandatory tenant scoping at the public API.
        services.AddSingleton<ITenantCache, TenantCache>();

        return services;
    }

    private static void DecorateDistributedCacheWithMetrics(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        if (existing is null)
        {
            return;
        }

        services.Remove(existing);

        if (existing.ImplementationInstance is IDistributedCache instance)
        {
            services.AddSingleton<IDistributedCache>(new MetricsDistributedCache(instance));
        }
        else if (existing.ImplementationFactory is not null)
        {
            services.AddSingleton<IDistributedCache>(sp =>
                new MetricsDistributedCache((IDistributedCache)existing.ImplementationFactory(sp)));
        }
        else if (existing.ImplementationType is not null)
        {
            // Re-register concrete inner cache as itself so we can resolve and wrap it.
            services.TryAddSingleton(existing.ImplementationType);
            services.AddSingleton<IDistributedCache>(sp =>
                new MetricsDistributedCache((IDistributedCache)sp.GetRequiredService(existing.ImplementationType)));
        }
    }
}
