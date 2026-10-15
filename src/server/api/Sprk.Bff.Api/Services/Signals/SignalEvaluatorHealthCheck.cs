using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// Diagnostic health check for the re-evaluating job (task 031; owner directive 2026-10-04 (c)): Unhealthy when no run has
/// SUCCEEDED for longer than <c>Ontology:Evaluator:StaleAfterHours</c> (default 26, one nightly run plus slack), or when no
/// success was ever recorded. Registered with the <c>catalog</c> tag, so it is on <c>/healthz/catalog</c> and never on the
/// <c>/healthz</c> liveness probe: a stale evaluator must not recycle instances.
/// </summary>
/// <remarks>
/// The last success is read from the distributed cache the job writes (<see cref="SignalReevaluationJob.LastSuccessCacheKey"/>),
/// so every instance reports the same answer whichever instance held the run's lease.
/// §11: <i>Existing</i> — the admin jobs endpoint shows run history, but from process memory and with no staleness verdict;
/// no check covers this job. <i>Extension</i> — the scheduling host has no per-job staleness check to extend. <i>Cost of
/// doing nothing</i> — a dead evaluator looks exactly like "no conditions found" (the owner's no-silent-failure rule).
/// </remarks>
public sealed class SignalEvaluatorHealthCheck : IHealthCheck
{
    internal const string StaleAfterHoursKey = "Ontology:Evaluator:StaleAfterHours";
    internal const double DefaultStaleAfterHours = 26;

    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public SignalEvaluatorHealthCheck(IDistributedCache cache, IConfiguration configuration, TimeProvider timeProvider)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var staleAfter = TimeSpan.FromHours(
            double.TryParse(_configuration[StaleAfterHoursKey], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0
                ? hours
                : DefaultStaleAfterHours);

        string? raw;
        try
        {
            raw = await _cache.GetStringAsync(SignalReevaluationJob.LastSuccessCacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The Signal re-evaluation job's last successful run could not be read.", ex);
        }

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSuccess))
        {
            return HealthCheckResult.Unhealthy(
                "The Signal re-evaluation job has no recorded successful run.",
                data: new Dictionary<string, object> { ["lastSuccess"] = "never", ["staleAfterHours"] = staleAfter.TotalHours });
        }

        var age = _timeProvider.GetUtcNow() - lastSuccess;
        var data = new Dictionary<string, object>
        {
            ["lastSuccess"] = lastSuccess.ToString("o", CultureInfo.InvariantCulture),
            ["ageHours"] = Math.Round(age.TotalHours, 2),
            ["staleAfterHours"] = staleAfter.TotalHours,
        };

        return age > staleAfter
            ? HealthCheckResult.Unhealthy(
                $"The Signal re-evaluation job last succeeded at {lastSuccess:o}, more than {staleAfter.TotalHours:0.#} hours ago.", data: data)
            : HealthCheckResult.Healthy($"The Signal re-evaluation job last succeeded at {lastSuccess:o}.", data);
    }
}
