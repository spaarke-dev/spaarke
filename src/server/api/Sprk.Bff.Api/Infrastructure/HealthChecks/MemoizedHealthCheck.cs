using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sprk.Bff.Api.Infrastructure.HealthChecks;

/// <summary>
/// A health check whose result is MEMOIZED: one evaluation is shared by every caller for <see cref="State.Ttl"/>,
/// and concurrent callers of an expired or empty memo share ONE in-flight evaluation (single flight). Built for
/// <c>GET /healthz/catalog</c> (unified-access-control-r2 task 167, main-session round 34 item 7).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <c>/healthz/catalog</c> is anonymous (an App Service-style probe), and each of its checks reads
/// Dataverse as the BFF's own identity: <c>RoutingConsumerTypeHealthCheck</c> reconciles the closed catalogs (2-3
/// uncached queries) and <c>ComposeIdentityKeyHealthCheck</c> reads entity-key metadata. Under the 120/min/IP
/// <c>health-probe</c> rate limit alone, one anonymous address could drive ~360-480 Dataverse requests a minute through
/// the BFF identity — a quarter to a third of Dataverse's per-identity service-protection budget. With the memo, any
/// number of callers, from any number of addresses, cost at most ONE evaluation of each check per
/// <see cref="CatalogHealthChecks.MemoTtl"/> per instance. The rate limit stays (round 34 item 7: "as well as").</para>
///
/// <para><b>Faults are memoized too, for no longer than the same window.</b> A check that throws becomes a
/// <see cref="HealthCheckRegistration.FailureStatus"/> result carrying the exception, shared like any other result and
/// re-evaluated once the window has passed — so a caller cannot defeat the memo by provoking faults, and a recovered
/// dependency is visible within one window. The shared evaluation runs on <see cref="CancellationToken.None"/> in a
/// DI scope of its own: one caller hanging up neither cancels it nor disposes services the others are waiting on;
/// that caller alone stops waiting.</para>
///
/// <para><b>Freshness cost, stated.</b> An operator who seeds the catalog (<c>scripts/Seed-TypedHandlers.ps1</c>) sees
/// the result at most <see cref="CatalogHealthChecks.MemoTtl"/> later. The startup surfaces are unchanged: each check's
/// <c>IHostedService.StartAsync</c> still logs its own evaluation at boot.</para>
///
/// <para><b>ADR-009.</b> A process-local memo of a DIAGNOSTIC result about catalog and schema metadata (catalog rows,
/// a Dataverse entity key's status) — ADR-009 "Allowed L1 Exceptions: Metadata, ≤15 min, document in code"; this is that
/// documentation. It caches no user data and no authorization decision. It is deliberately not Redis: a shared
/// in-flight evaluation cannot live in a distributed cache, a <see cref="HealthCheckResult"/> with its exception does
/// not round-trip, and the probe must not start depending on Redis to report the catalog. No <c>IMemoryCache</c>
/// (the clock is <see cref="TimeProvider"/>, so the window is testable).</para>
///
/// <para><b>ADR-010.</b> No DI registration: the shared <see cref="State"/> is created once per registration by
/// <see cref="CatalogHealthChecks.AddCatalogCheck{T}"/> and captured by the registration's factory.</para>
/// </remarks>
internal sealed class MemoizedHealthCheck : IHealthCheck
{
    private readonly State _state;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<IServiceProvider, IHealthCheck> _create;
    private readonly TimeProvider _time;

    internal MemoizedHealthCheck(
        State state,
        IServiceScopeFactory scopeFactory,
        Func<IServiceProvider, IHealthCheck> create,
        TimeProvider time)
    {
        _state = state;
        _scopeFactory = scopeFactory;
        _create = create;
        _time = time;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<Entry>? started = null;
        Task<Entry> evaluation;
        lock (_state.Gate)
        {
            var current = _state.Current;
            if (current is null || (current.IsCompleted && _time.GetUtcNow() - current.Result.CompletedAt >= _state.Ttl))
            {
                started = new TaskCompletionSource<Entry>(TaskCreationOptions.RunContinuationsAsynchronously);
                _state.Current = current = started.Task;
            }

            evaluation = current;
        }

        if (started is not null)
        {
            // Outside the lock: the inner check may run synchronously for a while before its first await.
            _ = EvaluateAsync(context, started);
        }

        var entry = await evaluation.WaitAsync(cancellationToken).ConfigureAwait(false);
        return entry.Result;
    }

    /// <summary>Runs the inner check once and completes <paramref name="completion"/>; never throws.</summary>
    private async Task EvaluateAsync(HealthCheckContext context, TaskCompletionSource<Entry> completion)
    {
        HealthCheckResult result;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var check = _create(scope.ServiceProvider);
            result = await check.CheckHealthAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new HealthCheckResult(
                context.Registration?.FailureStatus ?? HealthStatus.Unhealthy,
                "The check threw. This fault is shared like any other result and re-evaluated after the memo window.",
                ex);
        }

        completion.SetResult(new Entry(result, _time.GetUtcNow()));
    }

    /// <summary>The memo one registration shares across every probe (and every <see cref="MemoizedHealthCheck"/>
    /// instance the health-check service builds for it).</summary>
    internal sealed class State
    {
        public State(TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "The memo window must be positive.");
            }

            Ttl = ttl;
        }

        public TimeSpan Ttl { get; }

        internal object Gate { get; } = new();

        internal Task<Entry>? Current { get; set; }
    }

    internal sealed record Entry(HealthCheckResult Result, DateTimeOffset CompletedAt);
}

/// <summary>Registration of the checks <c>GET /healthz/catalog</c> runs — tagged <see cref="Tag"/> and memoized for
/// <see cref="MemoTtl"/> (main-session round 34 item 7).</summary>
internal static class CatalogHealthChecks
{
    /// <summary>The tag <c>/healthz/catalog</c> selects and <c>/healthz</c> excludes (EndpointMappingExtensions).</summary>
    public const string Tag = "catalog";

    /// <summary>How long one evaluation of a catalog check is shared — result or fault.</summary>
    public static readonly TimeSpan MemoTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers <typeparamref name="T"/> as a <c>/healthz/catalog</c> check: the <see cref="Tag"/> is added to
    /// <paramref name="tags"/>, and its result is memoized for <see cref="MemoTtl"/> through ONE
    /// <see cref="MemoizedHealthCheck.State"/> owned by this registration. <typeparamref name="T"/> is resolved the way
    /// <c>AddCheck&lt;T&gt;</c> resolves it (the registered singleton when there is one, otherwise a new instance), in
    /// the evaluation's own scope, and only when the memo needs a fresh result.
    /// </summary>
    public static IHealthChecksBuilder AddCatalogCheck<T>(
        this IHealthChecksBuilder builder,
        string name,
        HealthStatus failureStatus,
        IEnumerable<string> tags)
        where T : class, IHealthCheck
    {
        var state = new MemoizedHealthCheck.State(MemoTtl);
        var allTags = tags.Append(Tag).Distinct(StringComparer.Ordinal).ToArray();
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MemoizedHealthCheck(
                state,
                sp.GetRequiredService<IServiceScopeFactory>(),
                scoped => ActivatorUtilities.GetServiceOrCreateInstance<T>(scoped),
                sp.GetService<TimeProvider>() ?? TimeProvider.System),
            failureStatus,
            allTags));
    }
}
