using System.Globalization;
using System.Net;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

// GitHub #2845 round 2 review (F1) — the process-wide bound on the access evaluator's app-only Dataverse reads.
//
// Dataverse's service protection limits CONCURRENT requests per identity (about 52), and the BFF reads app-only on one
// identity. Round 2 made the per-type compositions read concurrently (a CIAM request peaks at ~12-18 calls, a workforce
// systemuser request at ~40-90), and the SPA dashboard sends ~20 requests at once. A 429 is read as "unknown" by every reader
// (fail closed), so without a process-wide bound a busy moment silently shortens lists or turns a record into a 404 instead
// of a 503. This gate is that bound; the handler below also retries a throttled read briefly before it gives up.

/// <summary>
/// A bound on concurrent Dataverse reads. It has two uses, which must never share an instance:
/// <list type="bullet">
/// <item><b>The process-wide singleton (#2845 F1)</b> bounds the access evaluator's app-only reads: one <see cref="SemaphoreSlim"/>
/// shared by the evaluator's typed Web API clients (through <see cref="DataverseThrottlingHandler"/>) and the filing walk's
/// <c>RetrieveMultipleAsync</c> reads (<c>SecureRootInheritance</c>, through the request memo). There a permit is held only for
/// the duration of one transport call, never while waiting on another read, so it cannot deadlock.</item>
/// <item><b>Per-read instances (task 222)</b>: <c>SignalReadService.ReadAsync</c> creates a NEW gate for each worklist read and
/// never shares it. It bounds that read's caller and app-only reads together, and a permit may cover a short serial sequence
/// (all pages of one saved fetch, the caller resolution, the policy sequence) that takes no other permit of the same gate.</item>
/// </list>
/// Do NOT replace a per-read gate with the injected singleton: a permit held across several calls could then wait on clients
/// that take permits from the same semaphore through <see cref="DataverseThrottlingHandler"/>, starving or deadlocking the
/// access evaluator.
/// </summary>
internal sealed class DataverseConcurrencyGate
{
    /// <summary>Default permits: under half of Dataverse's ~52 concurrent requests per identity, leaving room for the BFF's
    /// other app-only clients (which are not gated here).</summary>
    internal const int DefaultMaxConcurrentRequests = 24;

    /// <summary>The optional configuration key that overrides <see cref="DefaultMaxConcurrentRequests"/>
    /// (<c>DataverseOptions.MaxConcurrentRequests</c>). No app setting is required.</summary>
    internal const string ConfigurationKey = "Dataverse:MaxConcurrentRequests";

    private readonly SemaphoreSlim _permits;

    internal DataverseConcurrencyGate(int maxConcurrentRequests = DefaultMaxConcurrentRequests)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRequests, 1);
        MaxConcurrentRequests = maxConcurrentRequests;
        _permits = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
    }

    /// <summary>The gate sized from <see cref="ConfigurationKey"/>, or <see cref="DefaultMaxConcurrentRequests"/> when it is
    /// absent. Read from <see cref="IConfiguration"/> directly so resolving the gate never triggers the options validation of
    /// the rest of the <c>Dataverse</c> section.</summary>
    internal static DataverseConcurrencyGate FromConfiguration(IConfiguration? configuration)
    {
        var configured = configuration?[ConfigurationKey];
        return new DataverseConcurrencyGate(
            int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var permits) && permits >= 1
                ? permits
                : DefaultMaxConcurrentRequests);
    }

    /// <summary>The most reads in flight at once, process-wide.</summary>
    internal int MaxConcurrentRequests { get; }

    /// <summary>Waits for a permit. Honors <paramref name="ct"/> (an <see cref="OperationCanceledException"/>, nothing held).</summary>
    internal Task WaitAsync(CancellationToken ct) => _permits.WaitAsync(ct);

    /// <summary>Returns a permit taken by <see cref="WaitAsync"/>.</summary>
    internal void Release() => _permits.Release();

    /// <summary><paramref name="read"/> while holding one permit.</summary>
    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);
        await WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await read(ct).ConfigureAwait(false);
        }
        finally
        {
            Release();
        }
    }
}

/// <summary>
/// The access evaluator's Dataverse Web API clients' handler (#2845 F1): every send holds a <see cref="DataverseConcurrencyGate"/>
/// permit, and a throttled READ (429 or 503) is retried at most <see cref="MaxRetries"/> times after the server's
/// <c>x-ms-retry-after-ms</c> / <c>Retry-After</c> (capped at <see cref="MaxRetryDelay"/>), the permit released while it waits.
/// The last response is returned UNCHANGED, so a read still throttled after the retries meets the caller's own fail-closed
/// mapping exactly as before. A non-idempotent verb is never retried (both clients only read).
/// </summary>
/// <remarks>The client's own timeout (15 s) covers this handler: a retry that would outlast it ends as the timeout, which
/// every caller already maps fail-closed.</remarks>
internal sealed class DataverseThrottlingHandler : DelegatingHandler
{
    /// <summary>At most this many retries of one throttled read.</summary>
    internal const int MaxRetries = 2;

    /// <summary>The longest wait before a retry, whatever the server asks.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The wait when the server names none.</summary>
    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    private readonly DataverseConcurrencyGate _gate;
    private readonly TimeProvider _time;

    public DataverseThrottlingHandler(DataverseConcurrencyGate gate, TimeProvider time)
    {
        _gate = gate;
        _time = time;
        Delay = (delay, ct) => Task.Delay(delay, _time, ct);
    }

    /// <summary>The wait before a retry. A test seam (the default is <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>).</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            if (attempt >= MaxRetries || !IsThrottled(response.StatusCode) || !IsRetryable(request.Method))
            {
                return response;
            }

            var delay = RetryDelay(response);
            response.Dispose();
            await Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsThrottled(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    /// <summary>Only a read is resent. Both clients only send GETs; anything else is returned as answered.</summary>
    private static bool IsRetryable(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head;

    /// <summary>The server's wait (<c>x-ms-retry-after-ms</c>, else <c>Retry-After</c> as seconds or a date), capped at
    /// <see cref="MaxRetryDelay"/>; <see cref="DefaultRetryDelay"/> when it names none.</summary>
    internal TimeSpan RetryDelay(HttpResponseMessage response)
    {
        TimeSpan? asked = null;
        if (response.Headers.TryGetValues("x-ms-retry-after-ms", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
        {
            asked = TimeSpan.FromMilliseconds(ms);
        }
        else if (response.Headers.RetryAfter is { } retryAfter)
        {
            asked = retryAfter.Delta ?? (retryAfter.Date is { } date ? date - _time.GetUtcNow() : null);
        }

        var delay = asked ?? DefaultRetryDelay;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }
}
