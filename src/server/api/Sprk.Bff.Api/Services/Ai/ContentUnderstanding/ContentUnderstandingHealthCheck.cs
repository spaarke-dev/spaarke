using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sprk.Bff.Api.Services.Ai.ContentUnderstanding;

/// <summary>
/// Catalog health check (ebilling-r1 task 043): the configured invoice analyzer exists on the configured api-version.
/// Registered with <c>AddCatalogCheck</c> (the <c>catalog</c> tag, <c>/healthz/catalog</c>, memoized), so a missing
/// analyzer is visible to operators without taking instances out of rotation.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Not configured (no endpoint) → Healthy, "not configured": the reader is off by configuration.</item>
///   <item>Analyzer GET returns 200 and the analyzer is not in status <c>failed</c> → Healthy.</item>
///   <item>Anything else (404 — for example a preview analyzer id paired with the GA api-version —, a failed analyzer, a
///   token or transport failure) → the registration's failure status, description starting
///   <c>content_understanding.analyzer_missing</c>.</item>
/// </list>
/// The description and data carry the analyzer id, api-version, HTTP status and exception type name only.
/// </remarks>
public sealed class ContentUnderstandingHealthCheck : IHealthCheck
{
    /// <summary>Prefix of the failure description.</summary>
    public const string FailureCode = "content_understanding.analyzer_missing";

    private readonly ContentUnderstandingClient _client;

    public ContentUnderstandingHealthCheck(ContentUnderstandingClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
        {
            return HealthCheckResult.Healthy("Content Understanding invoice reader not configured (ContentUnderstanding:Endpoint is empty).");
        }

        var lookup = await _client.GetAnalyzerAsync(cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, object>
        {
            ["analyzerId"] = _client.AnalyzerId,
            ["apiVersion"] = _client.ApiVersion,
            ["httpStatus"] = lookup.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            ["analyzerStatus"] = lookup.AnalyzerStatus ?? "-",
            ["failure"] = lookup.Failure ?? "-",
        };

        if (lookup.StatusCode == 200 && !string.Equals(lookup.AnalyzerStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Healthy($"Analyzer {_client.AnalyzerId} found on api-version {_client.ApiVersion}.", data);
        }

        return new HealthCheckResult(
            context.Registration.FailureStatus,
            $"{FailureCode}: analyzer {_client.AnalyzerId} on api-version {_client.ApiVersion} " +
            $"(HTTP {data["httpStatus"]}, status {data["analyzerStatus"]}, failure {data["failure"]}).",
            data: data);
    }
}
