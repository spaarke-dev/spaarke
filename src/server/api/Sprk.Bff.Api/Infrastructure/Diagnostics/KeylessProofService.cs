using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Cache.NullObjects;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.Diagnostics;

/// <summary>
/// The keyless proof (task 230b, owner D13): one real, minimal, side-effect-free call to each Azure service this
/// stamp's BFF uses, authenticated with the BFF's own managed identity. Provisioning's acceptance gate (H13) calls it
/// through <c>POST /api/platform/keyless-proof</c> and refuses Ready unless every service is
/// <see cref="KeylessProofContract.Outcomes.Proved"/>.
/// </summary>
/// <remarks>
/// <para><b>Why the BFF runs it.</b> Only the stamp's user-assigned identity holds the data-plane roles, and it is
/// usable only inside the BFF process (ADR-028: Kudu and the L2 Worker have no route to it). So the per-service
/// proof cannot run anywhere else.</para>
/// <para>This class runs the platform probes (Service Bus, Redis) and adds the AI-owned probes through the
/// <see cref="IAiKeylessProbe"/> facade (ADR-013).</para>
/// </remarks>
public sealed class KeylessProofService
{
    private readonly IAiKeylessProbe _aiProbe;
    private readonly IOptions<ServiceBusOptions> _serviceBusOptions;
    private readonly IOptions<RedisOptions> _redisOptions;
    private readonly IConnectionMultiplexer _redis;
    private readonly TokenCredential _credential;
    private readonly ILogger<KeylessProofService> _logger;

    public KeylessProofService(
        IAiKeylessProbe aiProbe,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<RedisOptions> redisOptions,
        IConnectionMultiplexer redis,
        TokenCredential credential,
        ILogger<KeylessProofService> logger)
    {
        _aiProbe = aiProbe;
        _serviceBusOptions = serviceBusOptions;
        _redisOptions = redisOptions;
        _redis = redis;
        _credential = credential;
        _logger = logger;
    }

    /// <summary>Runs every probe concurrently and returns one result per service.</summary>
    public async Task<IReadOnlyList<KeylessProbeResult>> ProveAsync(CancellationToken cancellationToken)
    {
        var serviceBus = ServiceBusAsync(cancellationToken);
        var redis = RedisAsync(cancellationToken);
        var ai = _aiProbe.ProbeAsync(cancellationToken);

        await Task.WhenAll(serviceBus, redis, ai).ConfigureAwait(false);

        var results = new List<KeylessProbeResult> { await serviceBus.ConfigureAwait(false), await redis.ConfigureAwait(false) };
        results.AddRange(await ai.ConfigureAwait(false));

        _logger.LogInformation(
            "Keyless proof: {Summary}",
            string.Join(", ", results.Select(r => $"{r.Service}={r.Outcome}({r.Code})")));
        return results;
    }

    private Task<KeylessProbeResult> ServiceBusAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.ServiceBus;
        var options = _serviceBusOptions.Value;
        if (!ServiceBusClientFactory.UseManagedIdentity(options))
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(options.ConnectionString)
                ? KeylessProbeRunner.NotConfigured(service, "ServiceBus:FullyQualifiedNamespace")
                : KeylessProbeRunner.KeyCredential(service, "ServiceBus:ConnectionString"));
        }
        if (string.IsNullOrWhiteSpace(options.QueueName))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "ServiceBus:QueueName"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            // PEEK: reads without locking, completing or dead-lettering anything. Needs Data Receiver.
            await using var client = ServiceBusClientFactory.Create(options, _credential);
            await using var receiver = client.CreateReceiver(options.QueueName);
            await receiver.PeekMessageAsync(cancellationToken: token).ConfigureAwait(false);
            return null;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> RedisAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.Redis;
        var options = _redisOptions.Value;
        if (!options.Enabled || _redis is NullConnectionMultiplexer)
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "Redis:Enabled"));
        // Without an endpoint the multiplexer was built from a connection string (CacheModule) — a key.
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            return Task.FromResult(KeylessProbeRunner.KeyCredential(service, "Redis:ConnectionString"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            // PING on the multiplexer the BFF already authenticated with its managed identity.
            await _redis.GetDatabase().PingAsync().WaitAsync(token).ConfigureAwait(false);
            return null;
        }, _logger, ct);
    }
}
