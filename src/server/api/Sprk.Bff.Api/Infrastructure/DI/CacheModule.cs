using Azure.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
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
/// <para>
/// Redis-on authentication (task 242, owner D12/D13): with <c>Redis:Endpoint</c> set, the multiplexer authenticates
/// only with the user-assigned managed identity (Microsoft Entra — Azure Managed Redis has access keys disabled) over
/// RESP3, so the token refresh can re-authenticate the pub/sub connection (JobStatusService, membership invalidation).
/// A connection string is used only when no endpoint is set AND the environment is Development or Testing.
/// </para>
/// </summary>
public static class CacheModule
{
    /// <summary>The setting deployed environments must carry when Redis is enabled (named in startup errors).</summary>
    internal const string EndpointSettingName = "Redis__Endpoint";

    /// <summary>
    /// The settings that name the user-assigned managed identity (named in startup errors) — the two keys
    /// <see cref="ManagedIdentityCredentialFactory.ResolveUamiClientId"/> reads, in its order.
    /// </summary>
    internal const string ClientIdSettingName = "Graph__ManagedIdentity__ClientId or ManagedIdentity__ClientId";

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
        => services.AddCacheModule(
            configuration,
            logging,
            environment,
            NetworkStepsForBootedHostTests?.ConfigureForManagedIdentity
                ?? ((options, credential) => StackExchange.Redis.AzureCacheForRedis.ConfigureForAzureWithTokenCredentialAsync(options, credential)),
            NetworkStepsForBootedHostTests?.Connect
                ?? (options => StackExchange.Redis.ConnectionMultiplexer.Connect(options)));

    /// <summary>
    /// TEST SEAM for a host booted through <c>Program</c> (the same two network steps as the overload below, which a
    /// <c>WebApplicationFactory</c> cannot reach: <c>Program.cs</c> calls this one at registration time). Spaarke.ArchTests'
    /// <c>BootedBff</c> boots the BFF as Production to prove what Production maps (task 167 f2-v2). Since master T242 a
    /// Production BFF reaches Redis ONLY through its managed identity over TLS, which a test process cannot reach, so that
    /// boot substitutes these two steps for its loopback listener. <c>null</c> — the default, and always in production — runs
    /// the real ones. Never set outside a test host; the mode selection above it (endpoint required, no connection string
    /// outside Development/Testing) runs unchanged.
    /// </summary>
    internal static (Func<StackExchange.Redis.ConfigurationOptions, TokenCredential, Task> ConfigureForManagedIdentity,
        Func<StackExchange.Redis.ConfigurationOptions, StackExchange.Redis.IConnectionMultiplexer> Connect)? NetworkStepsForBootedHostTests
    { get; set; }

    /// <summary>
    /// Test seam: the two network steps of the Redis-on branch — authenticating with the managed-identity credential
    /// (the first Entra token is fetched here) and connecting — are passed in, so mode selection is verifiable
    /// without Azure or a live Redis.
    /// </summary>
    internal static IServiceCollection AddCacheModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging,
        IHostEnvironment environment,
        Func<StackExchange.Redis.ConfigurationOptions, TokenCredential, Task> configureForManagedIdentity,
        Func<StackExchange.Redis.ConfigurationOptions, StackExchange.Redis.IConnectionMultiplexer> connect)
    {
        // Bind RedisOptions (Enabled, Endpoint, ConnectionString, InstanceName, AllowInMemoryFallback)
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);

        // Bootstrap logger for startup branch-selection diagnostics.
        // The host's structured logger is not yet built at DI-registration time.
        var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("CacheModule");

        var isDevelopment = environment.IsDevelopment();
        // CI safety carve-out (2026-06-29, follow-on to AzureMonitorGuard Testing
        // allow-list — see Infrastructure/Startup/AzureMonitorGuard.cs for the
        // canonical pattern): treat `Testing` like Development for the
        // AllowInMemoryFallback path. WebApplicationFactory<Program>-based
        // integration tests set ASPNETCORE_ENVIRONMENT=Testing and rely on
        // Redis:Enabled=false + AllowInMemoryFallback=true to avoid network
        // I/O. Throwing here breaks every WAF-based fixture with no benefit
        // (CI doesn't deploy; App Service uses Production, never Testing).
        var isLocalLike = isDevelopment ||
            string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        // 4-branch decision matrix on (Enabled, AllowInMemoryFallback, IsDevelopment).
        if (redisOptions.Enabled)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (a): Redis-on — real connection, fail-fast.
            // ────────────────────────────────────────────────────────────────────
            var (configOptions, authMode) = string.IsNullOrWhiteSpace(redisOptions.Endpoint)
                ? BuildConnectionStringOptions(configuration, environment, isLocalLike)
                : BuildManagedIdentityOptions(redisOptions.Endpoint, configuration, configureForManagedIdentity);

            // Production-like dev (FR-01): fail-fast on connect.
            configOptions.AbortOnConnectFail = true;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(1000);

            StackExchange.Redis.IConnectionMultiplexer connectionMultiplexer;
            try
            {
                connectionMultiplexer = connect(configOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Redis at startup (AbortOnConnectFail=true, authentication: {authMode}). " +
                    (authMode == RedisAuthMode.ManagedIdentity
                        ? $"Check (1) '{EndpointSettingName}' is the cache's host:port (Azure Managed Redis listens on 10000); " +
                          $"(2) the user-assigned identity named by '{ClientIdSettingName}' holds an access-policy assignment " +
                          "on the cache's database; (3) the cache allows the App Service's network path."
                        : "Check (1) the Redis instance is running and reachable; " +
                          "(2) the connection string ('ConnectionStrings:Redis' or 'Redis:ConnectionString') is valid."),
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

            // Which authentication path is live (task 242). Names the mode only — never a key or connection string.
            logger.LogInformation("Distributed cache: Redis authentication is {AuthMode}", authMode);
        }
        else if (redisOptions.AllowInMemoryFallback && isLocalLike)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (b): Redis-off + AllowInMemoryFallback + Development/Testing.
            // ────────────────────────────────────────────────────────────────────
            services.AddDistributedMemoryCache();

            // ADR-032 symmetric registration: Null-Object IConnectionMultiplexer
            // so consumers (e.g., JobStatusService pub/sub) can inject unconditionally.
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer, NullConnectionMultiplexer>();

            logger.LogWarning(
                "Distributed cache: In-memory mode enabled ({EnvName} env). " +
                "NOT suitable for multi-instance deployment.",
                environment.EnvironmentName);
        }
        else if (redisOptions.AllowInMemoryFallback && !isLocalLike)
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (c): Redis-off + AllowInMemoryFallback + deployed env → throw.
            // Development and Testing are allow-listed (the latter for CI fixtures);
            // every other env (Staging, Production, Demo, etc.) is treated as
            // deployed and rejects in-memory fallback because it would silently
            // break multi-instance deployments.
            // ────────────────────────────────────────────────────────────────────
            throw new InvalidOperationException(
                $"AllowInMemoryFallback is restricted to Development and Testing environments. " +
                $"ASPNETCORE_ENVIRONMENT={environment.EnvironmentName}. Set Redis:Enabled=true.");
        }
        else
        {
            // ────────────────────────────────────────────────────────────────────
            // Branch (d): Redis-off + no fallback opt-in → throw.
            // ────────────────────────────────────────────────────────────────────
            throw new InvalidOperationException(
                "Redis is disabled and in-memory fallback not opted in. " +
                "Set Redis:Enabled=true (recommended) or Redis:AllowInMemoryFallback=true (Development or Testing only).");
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

    /// <summary>Names of the two Redis authentication paths (logged at startup; never a credential).</summary>
    internal static class RedisAuthMode
    {
        public const string ManagedIdentity = "managed identity (Microsoft Entra)";
        public const string ConnectionString = "connection string (Development/Testing only)";
    }

    /// <summary>
    /// Endpoint path: Microsoft Entra with the user-assigned managed identity, RESP3, TLS, no password.
    /// </summary>
    private static (StackExchange.Redis.ConfigurationOptions Options, string AuthMode) BuildManagedIdentityOptions(
        string endpoint,
        IConfiguration configuration,
        Func<StackExchange.Redis.ConfigurationOptions, TokenCredential, Task> configureForManagedIdentity)
    {
        // ADR-028 A4 — one shared credential path: the identity is resolved by the same lookup every app-only consumer
        // uses, and the credential is the shared tenant-pinned one (ManagedIdentityCredentialFactory.Create), not one
        // the Redis library builds for itself. A client id is REQUIRED here: without it the credential could fall back
        // to another identity on the host, and only the configured UAMI holds the cache's access policy.
        var clientId = ManagedIdentityCredentialFactory.ResolveUamiClientId(configuration);
        if (clientId is null)
        {
            throw new InvalidOperationException(
                $"'{EndpointSettingName}' is set but no managed-identity client id is configured. Set " +
                $"'{ClientIdSettingName}' to the client id of the user-assigned identity that holds the cache's " +
                "access policy — Azure Managed Redis here has access keys disabled, so the managed identity is the " +
                "only way in.");
        }

        StackExchange.Redis.ConfigurationOptions options;
        try
        {
            options = StackExchange.Redis.ConfigurationOptions.Parse(endpoint);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"'{EndpointSettingName}' could not be parsed. It must be host:port of the Azure Managed Redis " +
                "(e.g. name.region.redis.azure.net:10000).", ex);
        }

        if (!string.IsNullOrEmpty(options.Password) || !string.IsNullOrEmpty(options.User))
        {
            // Value deliberately not echoed: it carries a credential.
            throw new InvalidOperationException(
                $"'{EndpointSettingName}' contains a credential. It must be host:port only — the BFF authenticates " +
                "with its managed identity; Redis keys and connection strings are not used outside Development/Testing.");
        }

        options.Ssl = true;
        // RESP3: the token refresh re-authenticates every connection, including the pub/sub one (RESP2 cannot).
        options.Protocol = StackExchange.Redis.RedisProtocol.Resp3;

        try
        {
            // Startup-time, like the synchronous Connect below: there is no SynchronizationContext at DI registration.
            configureForManagedIdentity(options, ManagedIdentityCredentialFactory.Create(configuration)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not obtain a Microsoft Entra token for Redis with the managed identity named by " +
                $"'{ClientIdSettingName}'. Check the identity is attached to this App Service and the value is its " +
                "client id (not its object id).", ex);
        }

        return (options, RedisAuthMode.ManagedIdentity);
    }

    /// <summary>
    /// Connection-string path: Development and Testing only, and only without an endpoint.
    /// </summary>
    private static (StackExchange.Redis.ConfigurationOptions Options, string AuthMode) BuildConnectionStringOptions(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isLocalLike)
    {
        if (!isLocalLike)
        {
            throw new InvalidOperationException(
                $"Redis is enabled but '{EndpointSettingName}' is not set (ASPNETCORE_ENVIRONMENT=" +
                $"{environment.EnvironmentName}). Set '{EndpointSettingName}' to the Azure Managed Redis host:port " +
                $"(the BFF connects with its managed identity, '{ClientIdSettingName}'). A Redis connection string " +
                "('ConnectionStrings:Redis' / 'Redis:ConnectionString') is accepted only in Development and Testing.");
        }

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException(
                $"Redis is enabled but neither '{EndpointSettingName}' nor a connection string is set. " +
                $"Set '{EndpointSettingName}' (managed identity) or, in Development/Testing only, " +
                "'ConnectionStrings:Redis' / 'Redis:ConnectionString' (e.g. localhost:6379).");
        }

        try
        {
            return (StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString), RedisAuthMode.ConnectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to parse the Redis connection string ('ConnectionStrings:Redis' or 'Redis:ConnectionString'). " +
                "Verify it is a valid StackExchange.Redis configuration string.",
                ex);
        }
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
