// -----------------------------------------------------------------------------
// TransientHttpRetryHandler.cs
//
// T186 audit (first live E2E run): the Worker's Dataverse Web API and Graph REST
// clients sent each request once. A Dataverse service-protection 429 or a gateway
// 502/503/504 therefore failed the handler — and several handlers (H12a's seed
// writer among them) map any writer error to QuarantineRequired.
//
// This DelegatingHandler retries what the service did not process:
//   - 429 on any method (the request was rejected, not executed);
//   - 408/502/503/504 and transport failures on GET/HEAD only (a POST/PATCH that
//     timed out at a gateway may have landed, so it is never replayed).
// It honours Retry-After (delta or date); otherwise it backs off exponentially
// from BaseDelay. Request content is buffered before the first send so a replay
// sends the same bytes.
//
// Budget (T186 review): HttpClient.Timeout spans this whole handler chain, and the
// Worker's Dataverse/Graph clients use 30 s. The total sleep must stay well under it,
// or the client's timeout fires inside the wait and the caller sees an unclassified
// cancellation instead of the 429. So: 4 attempts, 1 s base, 5 s cap (≤ 7 s of sleep),
// and a Retry-After longer than the cap is NOT slept — the 429 is returned at once for
// the caller's own failure mapping.
// -----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;

namespace Sprk.Provisioning.ControlPlane.Infrastructure.Http;

/// <summary>Bounded retry of throttled and transient gateway responses for the Worker's Dataverse and Graph REST clients.</summary>
public sealed class TransientHttpRetryHandler : DelegatingHandler
{
    private readonly ILogger<TransientHttpRetryHandler> _logger;

    /// <summary>Production defaults: 4 attempts, 1 s exponential base, 5 s cap — at most 7 s of sleep, well under the 30 s client timeout.</summary>
    public TransientHttpRetryHandler(ILogger<TransientHttpRetryHandler> logger)
        : this(logger, maxAttempts: 4, baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Test seam — small delays.</summary>
    internal TransientHttpRetryHandler(ILogger<TransientHttpRetryHandler> logger, int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _logger = logger;
        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
    }

    internal int MaxAttempts { get; }

    internal TimeSpan BaseDelay { get; }

    internal TimeSpan MaxDelay { get; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var idempotent = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (idempotent && attempt < MaxAttempts)
            {
                var transportDelay = Backoff(attempt);
                _logger.LogInformation(
                    "{Method} {Host}{Path} attempt {Attempt}/{Max}: transport failure ({Error}) — retrying in {Delay}.",
                    request.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, attempt, MaxAttempts, ex.Message, transportDelay);
                await Task.Delay(transportDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (attempt >= MaxAttempts || !IsRetryable(response.StatusCode, idempotent))
            {
                return response;
            }

            var delay = RetryAfter(response) ?? Backoff(attempt);
            if (delay > MaxDelay)
            {
                // The server asks for a longer wait than this handler's budget allows inside the client timeout —
                // hand the response back rather than sleep into a cancellation.
                return response;
            }

            _logger.LogInformation(
                "{Method} {Host}{Path} attempt {Attempt}/{Max}: {Status} — retrying in {Delay}.",
                request.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, attempt, MaxAttempts, (int)response.StatusCode, delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>429 on any method; 408/502/503/504 only when replaying cannot repeat a write.</summary>
    internal static bool IsRetryable(HttpStatusCode status, bool idempotent) => status switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout => idempotent,
        _ => false,
    };

    private TimeSpan Backoff(int attempt)
    {
        var delay = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << Math.Min(attempt - 1, 10)));
        return delay > MaxDelay ? MaxDelay : delay;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }
}
