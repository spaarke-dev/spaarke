using System.ClientModel;
using System.Net;
using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.Diagnostics;

/// <summary>
/// Runs one keyless probe (task 230b) under a time limit and turns its result or exception into a
/// <see cref="KeylessProbeResult"/>: <see cref="KeylessProofContract.Outcomes.Refused"/> for 401/403 or a credential that
/// produced no token, <see cref="KeylessProofContract.Outcomes.Unreachable"/> for transport faults, timeouts, 408/429/5xx,
/// <see cref="KeylessProofContract.Outcomes.Failed"/> for any other error. Only a status and a short code leave this class —
/// never exception text (the endpoint's contract).
/// </summary>
/// <remarks>
/// Shared by the AI-owned probes (<c>Services/Ai/Diagnostics/AiKeylessProbe</c>) and the platform probes
/// (<c>Infrastructure/Diagnostics/KeylessProofService</c>) so both classify a refusal identically — H13 fails
/// acceptance on <see cref="KeylessProofContract.Outcomes.Refused"/> and resumes on <see cref="KeylessProofContract.Outcomes.Unreachable"/>,
/// so a misclassification either blocks a healthy stamp or passes a refused one.
/// </remarks>
public static class KeylessProbeRunner
{
    /// <summary>Time limit for one probe. Generous: a cold managed-identity token plus a first connection.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="probe"/>. The probe returns the HTTP status it observed (or null when the SDK exposes
    /// none); reaching the end of the probe without an exception counts as <see cref="KeylessProofContract.Outcomes.Proved"/>.
    /// </summary>
    public static async Task<KeylessProbeResult> RunAsync(
        string service,
        Func<CancellationToken, Task<int?>> probe,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        using var deadline = new CancellationTokenSource(timeout ?? DefaultTimeout, clock);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        long ElapsedMs() => (long)clock.GetElapsedTime(started).TotalMilliseconds;

        try
        {
            var status = await probe(limit.Token).ConfigureAwait(false);
            return new KeylessProbeResult(service, KeylessProofContract.Outcomes.Proved, status, ElapsedMs(), "ok");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Keyless probe {Service} timed out after {ElapsedMs} ms.", service, ElapsedMs());
            return new KeylessProbeResult(service, KeylessProofContract.Outcomes.Unreachable, null, ElapsedMs(), "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var (outcome, status, code) = Classify(ex);
            // The exception stays in the BFF's own log; the response carries the code only.
            logger.LogWarning(ex, "Keyless probe {Service}: {Outcome} ({Code}).", service, outcome, code);
            return new KeylessProbeResult(service, outcome, status, ElapsedMs(), code);
        }
    }

    /// <summary>A result for a service that is not called because a setting is missing.</summary>
    public static KeylessProbeResult NotConfigured(string service, string settingKey)
        => new(service, KeylessProofContract.Outcomes.NotConfigured, null, 0, "setting-missing:" + settingKey);

    /// <summary>A result for a service that is not called because a key or connection string is configured.</summary>
    public static KeylessProbeResult KeyCredential(string service, string settingKey)
        => new(service, KeylessProofContract.Outcomes.KeyCredential, null, 0, "key-configured:" + settingKey);

    /// <summary>Maps an exception thrown by an Azure SDK (or a token credential) to an outcome, status and code.</summary>
    internal static (string Outcome, int? Status, string Code) Classify(Exception ex) => ex switch
    {
        // The Azure SDK retry layers throw AggregateException("Retry failed after N tries") when every attempt failed:
        // a refusal among them is a refusal; otherwise the worst inner verdict (all transport faults → unreachable).
        AggregateException { InnerExceptions.Count: > 0 } agg => Worst(agg.InnerExceptions.Select(Classify)),
        AuthenticationFailedException or CredentialUnavailableException
            => (KeylessProofContract.Outcomes.Refused, null, "token-unavailable"),
        RequestFailedException rfe => FromStatus(rfe.Status),
        ClientResultException cre => FromStatus(cre.Status),
        CosmosException ce => FromStatus((int)ce.StatusCode),
        HttpRequestException hre => hre.StatusCode is { } s
            ? FromStatus((int)s)
            : (KeylessProofContract.Outcomes.Unreachable, null, "transport"),
        UnauthorizedAccessException => (KeylessProofContract.Outcomes.Refused, 401, "unauthorized"),
        ServiceBusException sbe => sbe.Reason switch
        {
            ServiceBusFailureReason.ServiceCommunicationProblem or ServiceBusFailureReason.ServiceTimeout
                or ServiceBusFailureReason.ServiceBusy => (KeylessProofContract.Outcomes.Unreachable, null, "service-bus-" + Kebab(sbe.Reason)),
            _ => (KeylessProofContract.Outcomes.Failed, null, "service-bus-" + Kebab(sbe.Reason)),
        },
        RedisServerException rse when rse.Message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase)
                                    || rse.Message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase)
                                    || rse.Message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase)
            => (KeylessProofContract.Outcomes.Refused, null, "redis-auth"),
        RedisConnectionException rce when rce.FailureType == ConnectionFailureType.AuthenticationFailure
            => (KeylessProofContract.Outcomes.Refused, null, "redis-auth"),
        RedisConnectionException or RedisTimeoutException => (KeylessProofContract.Outcomes.Unreachable, null, "redis-connection"),
        _ => (KeylessProofContract.Outcomes.Failed, null, "unexpected-error"),
    };

    private static (string Outcome, int? Status, string Code) Worst(IEnumerable<(string Outcome, int? Status, string Code)> verdicts)
        => verdicts.OrderByDescending(v => v.Outcome switch
        {
            KeylessProofContract.Outcomes.Refused => 3,
            KeylessProofContract.Outcomes.Failed => 2,
            KeylessProofContract.Outcomes.Unreachable => 1,
            _ => 0,
        }).First();

    private static (string Outcome, int? Status, string Code) FromStatus(int status) => status switch
    {
        (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden
            => (KeylessProofContract.Outcomes.Refused, status, "http-" + status),
        0 => (KeylessProofContract.Outcomes.Unreachable, null, "transport"),
        (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests or >= 500
            => (KeylessProofContract.Outcomes.Unreachable, status, "http-" + status),
        _ => (KeylessProofContract.Outcomes.Failed, status, "http-" + status),
    };

    private static string Kebab(ServiceBusFailureReason reason)
        => string.Concat(reason.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
