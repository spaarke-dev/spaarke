// -----------------------------------------------------------------------------
// HttpHealthProbe.cs
//
// Production <see cref="IHealthProbe"/> implementation. HttpClient-based
// retry loop parity with Deploy-BffApi.ps1's Test-HealthCheck helper (24 × 5s
// default). Registered as a typed HttpClient via AddHttpClient so the SDK
// wiring + retry policy remain testable through the standard IHttpClientFactory
// path.
//
// NFR-05 RATIONALE:
//   A single 200-OK response from /health is sufficient proof that BFF's
//   ValidateOnStart (r3 task 061) has resolved every Tier-1 KV ref — the
//   BFF cannot boot to a 200 without them. See IHealthProbe file header for
//   the full rationale.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (real HTTP). Handler unit tests via
//   IHealthProbe stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// HttpClient-based /health smoke probe for H9's post-swap verification.
/// </summary>
public sealed class HttpHealthProbe : IHealthProbe
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHealthProbe> _logger;

    public HttpHealthProbe(HttpClient httpClient, ILogger<HttpHealthProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HealthProbeResult> ProbeAsync(
        HealthProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetUrl);
        if (request.MaxRetries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.MaxRetries,
                "MaxRetries must be >= 1.");
        }

        var stopwatch = Stopwatch.StartNew();
        var lastDiagnostic = "no attempts made";

        for (var attempt = 1; attempt <= request.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var perRequestCts = new CancellationTokenSource(request.RequestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, perRequestCts.Token);

            try
            {
                using var response = await _httpClient.GetAsync(request.TargetUrl, linked.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "H9 health probe SUCCESS: url={Url} attempts={Attempts} durationMs={DurationMs}",
                        request.TargetUrl, attempt, stopwatch.ElapsedMilliseconds);
                    return new HealthProbeResult.Success(attempt, stopwatch.Elapsed);
                }
                lastDiagnostic = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (OperationCanceledException) when (perRequestCts.IsCancellationRequested)
            {
                lastDiagnostic = $"request timed out after {request.RequestTimeout}";
            }
            catch (HttpRequestException ex)
            {
                lastDiagnostic = $"HttpRequestException: {ex.Message}";
            }

            if (attempt < request.MaxRetries)
            {
                _logger.LogInformation(
                    "H9 health probe retry {Attempt}/{Max}: url={Url} lastDiagnostic={Diag}",
                    attempt, request.MaxRetries, request.TargetUrl, lastDiagnostic);
                try
                {
                    await Task.Delay(request.Interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        stopwatch.Stop();
        return new HealthProbeResult.Failure(
            request.MaxRetries,
            $"Health probe failed after {request.MaxRetries} attempts. Last: {lastDiagnostic}.");
    }
}
