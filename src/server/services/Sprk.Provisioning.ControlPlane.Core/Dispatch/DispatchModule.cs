// -----------------------------------------------------------------------------
// DispatchModule.cs
//
// L2 CONTROL-PLANE dispatcher shared-infra DI composition (task 102, Phase C''
// Wave G-1).
//
// PURPOSE:
//   Registers the dispatcher's SHARED-INFRA pieces that live in .Core:
//     - DispatcherOptions        (bound from "Dispatcher" section + validated)
//     - IDistributedCache        (Redis via Redis:ConnectionString, or an
//                                 in-memory fallback when unset -- task 105)
//     - IDispatchIdempotencyService -> DispatchIdempotencyService (task 105;
//                                 Redis-backed, replaces task 102's
//                                 NoOpDispatchIdempotencyService default)
//
//   The ProvisioningHandlerDispatcher BackgroundService itself lives in
//   Sprk.Provisioning.ControlPlane.Worker/Dispatch/ (per task 102 POML
//   file-layout constraint + design intent that dispatcher CODE is a
//   .Worker-only concern). .Worker/Program.cs registers the hosted service
//   directly next to its call to AddDispatchModule -- EXACT parity with
//   how CrashRecoveryStartupService (in .Core/Reconciler/) is registered
//   in Program.cs today:
//
//     builder.Services.AddDispatchModule(builder.Configuration, builder.Environment);
//     builder.Services.AddHostedService<ProvisioningHandlerDispatcher>();
//
//   This split is a MECHANICAL consequence of the .Core/.Worker project
//   boundary (a .Core module extension cannot reference a .Worker type).
//   It does not weaken the ADR-010 god-class-ratchet posture: the two
//   dispatcher-related lines in Program.cs are the same 2-liner shape
//   CrashRecovery uses, plus one addressable module call.
//
// LIFETIME NOTES:
//   - DispatcherOptions             : Options<T> pattern (bound + validated).
//   - IDistributedCache             : Singleton. Redis
//                                     (Microsoft.Extensions.Caching.
//                                     StackExchangeRedis, added to
//                                     .Core.csproj by task 102).
//                                     Task 242 (owner D12/D13): when
//                                     "Redis:Endpoint" is set the Worker
//                                     connects to the Entra-only Azure
//                                     Managed Redis with its UAMI
//                                     (ManagedIdentity:ClientId), RESP3, no
//                                     key. A connection string
//                                     ("Redis:ConnectionString" /
//                                     ConnectionStrings:Redis) is accepted
//                                     only in Development/Testing and only
//                                     without an endpoint; elsewhere it
//                                     THROWS naming Redis__Endpoint.
//                                     NEITHER set is handled per environment,
//                                     ENVIRONMENT-GATED exactly like BFF's
//                                     CacheModule (added 2026-08-19 review
//                                     fix -- an UNGATED silent in-memory
//                                     fallback would itself be a silent-fail
//                                     trap of the exact class this project
//                                     exists to eliminate: Level 2 quietly
//                                     degrading to same-instance-only dedup
//                                     in a DEPLOYED multi-instance
//                                     environment because a config key was
//                                     simply never wired, with no operator
//                                     signal):
//                                       Development / Testing -> silent
//                                         AddDistributedMemoryCache() fallback
//                                         (dev convenience + CI-friendly, no
//                                         live Redis dependency for unit
//                                         tests).
//                                       Any other environment (Staging,
//                                         Production, Demo, ...) -> THROW at
//                                         startup (NFR-05 fail-fast) so a
//                                         missing Redis:ConnectionString is a
//                                         loud deploy-time failure, not a
//                                         silent degraded-guarantee runtime.
//                                     This is DISTINCT from the DS-2 §4-L2
//                                     fail-OPEN posture inside
//                                     DispatchIdempotencyService itself (a
//                                     configured-but-momentarily-unreachable
//                                     Redis at RUNTIME still degrades
//                                     gracefully per-call, per DS-2) --
//                                     that's resilience to a transient
//                                     OUTAGE; THIS gate is about a
//                                     CONFIGURATION gap at boot, which
//                                     deserves the same loud treatment every
//                                     other Tier-1 IOptions misconfig in this
//                                     project gets (root CLAUDE.md MUST rule:
//                                     "BFF /health fails fast at boot on any
//                                     Tier-1 IOptions misconfig" -- the same
//                                     principle applies to the Worker host).
//   - IDispatchIdempotencyService   : Singleton. DispatchIdempotencyService
//                                     (task 105) -- Redis-backed impl over
//                                     the IDistributedCache registered above.
//
// PLACEMENT (CLAUDE.md §10 / §11):
//   Extension method LIVES IN .Core because both .Worker (production
//   host) and .Api (future in-process integration test host) may want to
//   compose the shared-infra. NO reference to Sprk.Bff.Api types (project
//   MUST rule -- L2 is a PEER service).
//
// KILL-SWITCH POSTURE (ADR-032):
//   Registration is UNCONDITIONAL. The runtime kill switch is
//   DispatcherOptions.Enabled -- when false, ExecuteAsync exits cleanly
//   before creating the session processor (parity with
//   StateReconcilerService's ReconcilerOptions.Enabled pattern). This is
//   the HostedService-shaped Null-Object kill switch:
//     - Registration always present (no if/else in DI).
//     - Runtime opts out inside ExecuteAsync.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// DI registration for the L2 dispatcher shared-infra (options + Level-2
/// idempotency seam). The <c>ProvisioningHandlerDispatcher</c> hosted service
/// itself lives in <c>Sprk.Provisioning.ControlPlane.Worker/Dispatch/</c> and
/// is registered directly by <c>Sprk.Provisioning.ControlPlane.Worker/Program.cs</c>
/// via <see cref="Microsoft.Extensions.Hosting.ServiceCollectionHostedServiceExtensions.AddHostedService{THostedService}(IServiceCollection)"/>
/// — see file header for the mechanical rationale.
/// </summary>
public static class DispatchModule
{
    /// <summary>
    /// Registers dispatcher-scope shared-infra: <see cref="DispatcherOptions"/>
    /// (bound from configuration + fail-fast validated at startup) +
    /// <see cref="IDistributedCache"/> (Redis, or an environment-gated
    /// in-memory fallback -- see file header) +
    /// <see cref="IDispatchIdempotencyService"/> (task 105's Redis-backed
    /// <see cref="DispatchIdempotencyService"/>).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">
    /// Bound configuration (reads the <c>Dispatcher</c> section for
    /// <see cref="DispatcherOptions"/> + <c>Redis:Endpoint</c> with
    /// <c>ManagedIdentity:ClientId</c> — or, in Development/Testing only,
    /// <c>Redis:ConnectionString</c> / <c>ConnectionStrings:Redis</c> — for the
    /// Level-2 cache backing store).
    /// </param>
    /// <param name="environment">
    /// Host environment -- gates whether a missing Redis connection string
    /// silently falls back to in-memory (Development / Testing) or throws
    /// at startup (every other environment). See file header
    /// <see cref="IDistributedCache"/> lifetime note.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDispatchModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Bind + validate. Validate runs at PostConfigure so it fires during
        // host build, before ExecuteAsync starts -- fail-fast per NFR-05.
        services.Configure<DispatcherOptions>(configuration.GetSection(DispatcherOptions.SectionName));
        services.PostConfigure<DispatcherOptions>(o => o.Validate());

        // Level-2 idempotency backing store (task 105 / DS-2 §4-L2 -- "the
        // one new config key the dispatcher introduces"). Guarded so a
        // second AddDispatchModule call (e.g. a future .Api in-process
        // integration test host per the file header) does not double-
        // register IDistributedCache.
        if (!services.Any(d => d.ServiceType == typeof(IDistributedCache)))
        {
            var redisEndpoint = configuration["Redis:Endpoint"];
            var redisConnectionString = configuration.GetConnectionString("Redis")
                ?? configuration["Redis:ConnectionString"];
            var isLocalLike = environment.IsDevelopment()
                || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(redisEndpoint))
            {
                // Task 242: Entra-only Azure Managed Redis with the Worker UAMI. Validated here (startup);
                // the connection itself is made lazily on first cache use, as before.
                var clientId = configuration["ManagedIdentity:ClientId"];
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    throw new InvalidOperationException(
                        $"'{RedisEndpointSettingName}' is set but '{ClientIdSettingName}' is not. Set it to the client " +
                        "id of the Worker's user-assigned identity, which holds the cache's access policy -- the cache " +
                        "has access keys disabled, so the managed identity is the only way in.");
                }

                _ = ParseEndpoint(redisEndpoint);
                services.AddStackExchangeRedisCache(options =>
                {
                    options.InstanceName = "provisioning:";
                    options.ConnectionMultiplexerFactory = async () =>
                    {
                        var configurationOptions = await BuildManagedIdentityOptionsAsync(
                            redisEndpoint,
                            clientId,
                            (o, id) => o.ConfigureForAzureWithUserAssignedManagedIdentityAsync(id)).ConfigureAwait(false);
                        return await ConnectionMultiplexer.ConnectAsync(configurationOptions).ConfigureAwait(false);
                    };
                });
            }
            else if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                if (!isLocalLike)
                {
                    throw new InvalidOperationException(
                        $"Dispatcher Level-2 idempotency cache: a Redis connection string is set but " +
                        $"'{RedisEndpointSettingName}' is not, and ASPNETCORE_ENVIRONMENT='{environment.EnvironmentName}' " +
                        "is not Development or Testing. Deployed environments are keyless (task 242): set " +
                        $"'{RedisEndpointSettingName}' to the Azure Managed Redis host:port (the Worker connects with " +
                        $"'{ClientIdSettingName}') and remove the connection-string setting.");
                }

                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "provisioning:";
                });
            }
            else
            {
                // No Redis configured. Environment-gated fallback -- see the
                // IDistributedCache lifetime note in the file header for why
                // an UNGATED silent fallback would itself be a silent-fail
                // trap. isLocalLike mirrors BFF CacheModule's exact carve-out
                // (Testing is allow-listed alongside Development so
                // WebApplicationFactory<Program>-based fixtures work without
                // a live Redis dependency; CI doesn't deploy, so this never
                // masks a real deployment gap).
                if (isLocalLike)
                {
                    services.AddDistributedMemoryCache();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Dispatcher Level-2 idempotency cache is unconfigured: '{RedisEndpointSettingName}' is not " +
                        $"set, and ASPNETCORE_ENVIRONMENT='{environment.EnvironmentName}' is not " +
                        $"Development or Testing. Set '{RedisEndpointSettingName}' to the per-environment Azure " +
                        $"Managed Redis host:port (the Worker connects with '{ClientIdSettingName}') before " +
                        "deploying this Worker host. An unconfigured Level-2 cache in a deployed, " +
                        "multi-instance environment would silently degrade to same-instance-only " +
                        "duplicate suppression with no operator signal -- exactly the class of " +
                        "silent-fail trap this project exists to eliminate.");
                }
            }
        }

        // Level-2 idempotency seam. TryAdd so a test host can pre-register a
        // different implementation and win. Singleton because
        // DispatchIdempotencyService is stateless over the injected
        // IDistributedCache (no per-request state).
        services.TryAddSingleton<IDispatchIdempotencyService, DispatchIdempotencyService>();

        return services;
    }

    /// <summary>The Worker setting that names the Azure Managed Redis (named in startup errors).</summary>
    internal const string RedisEndpointSettingName = "Redis__Endpoint";

    /// <summary>The Worker setting that names its user-assigned managed identity (named in startup errors).</summary>
    internal const string ClientIdSettingName = "ManagedIdentity__ClientId";

    /// <summary>
    /// Connection options for the Entra-only cache: host:port from <c>Redis:Endpoint</c>, TLS, RESP3 (so the
    /// token refresh can re-authenticate every connection), authenticated by <paramref name="configureForManagedIdentity"/>
    /// with the user-assigned identity's client id. Never a password.
    /// </summary>
    internal static async Task<ConfigurationOptions> BuildManagedIdentityOptionsAsync(
        string endpoint,
        string clientId,
        Func<ConfigurationOptions, string, Task> configureForManagedIdentity)
    {
        var options = ParseEndpoint(endpoint);
        options.Ssl = true;
        options.Protocol = RedisProtocol.Resp3;
        await configureForManagedIdentity(options, clientId).ConfigureAwait(false);
        return options;
    }

    private static ConfigurationOptions ParseEndpoint(string endpoint)
    {
        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(endpoint);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"'{RedisEndpointSettingName}' could not be parsed. It must be host:port of the Azure Managed Redis " +
                "(e.g. name.region.redis.azure.net:10000).", ex);
        }

        if (!string.IsNullOrEmpty(options.Password) || !string.IsNullOrEmpty(options.User))
        {
            // Value deliberately not echoed: it carries a credential.
            throw new InvalidOperationException(
                $"'{RedisEndpointSettingName}' contains a credential. It must be host:port only -- the Worker " +
                "authenticates with its managed identity.");
        }

        return options;
    }
}
