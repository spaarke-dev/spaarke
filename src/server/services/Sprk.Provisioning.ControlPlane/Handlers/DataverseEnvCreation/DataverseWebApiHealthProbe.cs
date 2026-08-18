// -----------------------------------------------------------------------------
// DataverseWebApiHealthProbe.cs
//
// Production <see cref="IDataverseHealthProbe"/> implementation — issues
// `GET {envUrl}/api/data/v9.2/WhoAmI` with a DefaultAzureCredential-issued
// bearer token scoped to the Dataverse env's tenant. Returns Reachable on
// 200, InProgress on retryable signals (503 replicating, 404 with env-preparing
// body), Unreachable on any terminal failure.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-08 acceptance:
//       env is accessible — verified via WhoAmI post-create.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2:
//       Health probe is the acceptance gate distinct from creation success.
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: MI-outbound MUST
//       rule — DefaultAzureCredential; never account-key.
//
// AUTH SCOPE:
//   The token audience is `{envUrl}/.default` — Dataverse env-scoped tokens
//   per Web API convention. The tenantId parameter drives credential
//   options via TenantId = tenantId so the app-only credential targets the
//   correct tenant per §4D I1 (parity with H2a's per-customer sub target).
//
// NOT under test in the CI unit suite. Integration coverage lives in a
// smoke test (env-guarded — set DV_HEALTH_SMOKE_URL + DV_HEALTH_SMOKE_TENANT
// and provide the creds the probe expects) that can be added alongside the
// existing CosmosSmokeTests / ServiceBusSmokeTests when a dedicated dev
// tenant is available.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// <see cref="IDataverseHealthProbe"/> implementation that issues
/// <c>GET /api/data/v9.2/WhoAmI</c> against a Dataverse environment URL.
/// </summary>
public sealed class DataverseWebApiHealthProbe : IDataverseHealthProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DataverseEnvCreationOptions _options;
    private readonly ILogger<DataverseWebApiHealthProbe> _logger;

    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H5.DataverseWebApiHealthProbe";

    /// <summary>Constructs the probe bound to the named HttpClient + per-request timeout.</summary>
    public DataverseWebApiHealthProbe(
        IHttpClientFactory httpClientFactory,
        IOptions<DataverseEnvCreationOptions> options,
        ILogger<DataverseWebApiHealthProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DataverseHealthProbeResult> CheckHealthAsync(
        string environmentUrl,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            return new DataverseHealthProbeResult.Unreachable(
                $"Environment URL '{environmentUrl}' is not a valid absolute URI.");
        }

        // Token audience is the env URL's origin — Dataverse Web API's
        // token scope. §4D I1: tenantId flows through explicitly.
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { TenantId = tenantId });

        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "H5 health probe token acquisition failed for env={EnvUrl} tenantIdHash={TenantIdHash}",
                environmentUrl, HashForLog(tenantId));
            return new DataverseHealthProbeResult.Unreachable(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.HealthProbeRequestTimeout;

        var whoAmIUri = new Uri(envUri, "/api/data/v9.2/WhoAmI");
        using var request = new HttpRequestMessage(HttpMethod.Get, whoAmIUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", token.Token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        // Dataverse Web API version header per canonical usage.
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException tcex) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-request timeout — treat as InProgress so the outer polling
            // loop retries within the total window.
            return new DataverseHealthProbeResult.InProgress(
                $"Per-request timeout after {_options.HealthProbeRequestTimeout}: {tcex.Message}");
        }
        catch (HttpRequestException hrex)
        {
            return new DataverseHealthProbeResult.Unreachable(
                $"HTTP request error: {hrex.Message}");
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return new DataverseHealthProbeResult.Reachable();
            }

            // Retryable signals: 503 with replicating hint, 404 with env-preparing body.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable
                || response.StatusCode == HttpStatusCode.NotFound
                || (int)response.StatusCode == 429)
            {
                string bodyPreview;
                try
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    bodyPreview = Truncate(body, 200);
                }
                catch
                {
                    bodyPreview = "(body unavailable)";
                }
                return new DataverseHealthProbeResult.InProgress(
                    $"WhoAmI returned {(int)response.StatusCode} {response.StatusCode}. Body: {bodyPreview}");
            }

            return new DataverseHealthProbeResult.Unreachable(
                $"WhoAmI returned {(int)response.StatusCode} {response.StatusCode}.");
        }
        finally
        {
            response.Dispose();
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";

    // First 8 hex chars of SHA256(value) — enough to correlate log lines
    // for the SAME tenant without exposing the tenant id itself.
    private static string HashForLog(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).AsSpan(0, 8).ToString();
    }
}
