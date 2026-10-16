// -----------------------------------------------------------------------------
// KuduZipDeployer.cs
//
// Production <see cref="IKuduZipDeployer"/> — task 132 (Wave G-3, Option D
// hybrid / DS-4 §5 re-scope). POSTs the downloaded BFF artifact zip to the
// Kudu SCM zip-deploy route on the STAGING slot, authenticated with an Azure
// AD bearer token (ARM-scope) from the shared UAMI-pinned TokenCredential
// singleton — no publish-profile, no basic auth, no stored key. Typed
// HttpClient (registered via AddHttpClient<IKuduZipDeployer, KuduZipDeployer>())
// so DefaultAzureCredential's token cache is reused across invocations
// (ADR-028 UAMI-outbound MUST rule — parity with HttpHealthProbe's typed-
// client registration).
//
// ENDPOINT (DS-4 §5 point 3 / ground-truthed against Microsoft Learn's
// documented Kudu zip-deploy automation path — the ARM SDK has NO first-class
// zip-deploy primitive, this IS the recommended mechanism):
//   POST https://{appServiceName}-{slotName}.scm.azurewebsites.net/api/zipdeploy
//   Authorization: Bearer {AAD token, resource=https://management.azure.com/}
//   Content-Type: application/zip
//   Body: raw zip bytes (streamed, not multipart)
// A caller holding App Service RBAC (Contributor/Website Contributor) on the
// target site can authenticate to its Kudu SCM endpoint with the SAME
// ARM-scoped AAD token used for management-plane calls — this is the
// documented "Authenticate with Microsoft Entra ID" zip-deploy path, so no
// separate publish-profile credential is provisioned or stored anywhere in
// this project.
//
// ASYNCHRONOUS (?isAsync=true) + status poll — T186 L45. The synchronous
// POST held one request open for the whole deploy: the typed HttpClient's
// default 100 s Timeout cut it off live (reported as a 15-minute timeout),
// and App Service's front end ends any request at ~230 s regardless. Kudu
// answers the async POST with 202 + Location (the deployment record); the
// deployer polls it every BffDeployOptions.KuduDeploymentPollInterval until
// `complete` (status 4 = success, else Failure with Kudu's status text).
// BffDeployOptions.KuduZipDeployTimeout bounds the POST and the poll together;
// the HttpClient's own Timeout is off (Worker Program.cs) so that one ceiling
// is the only one.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// POSTs the artifact zip to <c>https://{app}-{slot}.scm.azurewebsites.net/api/zipdeploy</c>.
/// </summary>
public sealed class KuduZipDeployer : IKuduZipDeployer
{
    private static readonly string[] ArmScope = { "https://management.azure.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly BffDeployOptions _options;
    private readonly ILogger<KuduZipDeployer> _logger;

    public KuduZipDeployer(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<BffDeployOptions> options,
        ILogger<KuduZipDeployer> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _credential = credential;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<KuduZipDeployResult> DeployAsync(
        KuduZipDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LocalZipPath);

        if (!File.Exists(request.LocalZipPath))
        {
            throw new FileNotFoundException(
                $"Kudu zip-deploy source not found at '{request.LocalZipPath}' — verify " +
                "IBffArtifactDownloader completed before KuduZipDeployer is invoked.",
                request.LocalZipPath);
        }

        var kuduHost = $"{request.AppServiceName}-{request.SlotName}.scm.azurewebsites.net";
        var uri = new Uri($"https://{kuduHost}/api/zipdeploy?isAsync=true");

        var stopwatch = Stopwatch.StartNew();

        var tokenResult = await _credential.GetTokenAsync(
            new TokenRequestContext(ArmScope), cancellationToken).ConfigureAwait(false);

        // One ceiling over the POST and the whole status poll (KuduZipDeployTimeout).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.KuduZipDeployTimeout);

        _logger.LogInformation(
            "H9 Kudu zip-deploy starting: host={KuduHost} localZipPath={LocalZipPath}",
            kuduHost, request.LocalZipPath);

        try
        {
            Uri statusUri;
            {
                await using var zipStream = File.OpenRead(request.LocalZipPath);
                using var content = new StreamContent(zipStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

                using var response = await _httpClient.SendAsync(requestMessage, timeoutCts.Token).ConfigureAwait(false);
                _logger.LogInformation(
                    "H9 Kudu zip-deploy accepted: host={KuduHost} statusCode={StatusCode} durationMs={DurationMs}",
                    kuduHost, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                    return new KuduZipDeployResult.Failure(
                        $"Kudu zip-deploy POST https://{kuduHost}/api/zipdeploy returned HTTP " +
                        $"{(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 600)}");
                }

                // Async deploy: 202 + Location names the deployment to poll. Kudu's documented
                // fallback when no Location comes back is the latest deployment.
                statusUri = response.Headers.Location is { } location
                    ? (location.IsAbsoluteUri ? location : new Uri(new Uri($"https://{kuduHost}"), location))
                    : new Uri($"https://{kuduHost}/api/deployments/latest");
            }

            return await PollDeploymentAsync(kuduHost, statusUri, tokenResult.Token, stopwatch, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Kudu zip-deploy to '{kuduHost}' did not finish within {_options.KuduZipDeployTimeout} " +
                $"(elapsed {stopwatch.Elapsed:hh\\:mm\\:ss}).");
        }
    }

    /// <summary>
    /// Polls Kudu's deployment record until it reports <c>complete</c>: status 4 is
    /// success, anything else is a deploy failure carrying Kudu's own status text.
    /// 202 (and 200 with <c>complete: false</c>) means still running.
    /// </summary>
    private async Task<KuduZipDeployResult> PollDeploymentAsync(
        string kuduHost, Uri statusUri, string token, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var poll = new HttpRequestMessage(HttpMethod.Get, statusUri);
            poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(poll, cancellationToken).ConfigureAwait(false);
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new KuduZipDeployResult.Failure(
                    $"Kudu deployment status GET {statusUri} returned HTTP {(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}. Body: {Truncate(body, 600)}");
            }

            var record = KuduDeploymentRecord.Parse(body);
            if (record.Complete)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "H9 Kudu zip-deploy exited: host={KuduHost} kuduStatus={KuduStatus} durationMs={DurationMs}",
                    kuduHost, record.Status, stopwatch.ElapsedMilliseconds);

                return record.Status == KuduDeploymentRecord.Success
                    ? new KuduZipDeployResult.Success(stopwatch.Elapsed)
                    : new KuduZipDeployResult.Failure(
                        $"Kudu deployment on '{kuduHost}' finished with status {record.Status} (4 = success): " +
                        $"{Truncate(record.StatusText ?? "(no status text)", 400)}. Log: {record.LogUrl ?? "(none)"}");
            }

            await Task.Delay(_options.KuduDeploymentPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The fields of Kudu's <c>/api/deployments/{id}</c> record this deployer reads.</summary>
    internal readonly record struct KuduDeploymentRecord(int? Status, bool Complete, string? StatusText, string? LogUrl)
    {
        /// <summary>Kudu DeployStatus.Success.</summary>
        public const int Success = 4;

        /// <summary>An unreadable or empty body reads as "still running"; the timeout bounds it.</summary>
        public static KuduDeploymentRecord Parse(string body)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                var root = document.RootElement;
                int? status = root.TryGetProperty("status", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? s.GetInt32() : null;
                var complete = root.TryGetProperty("complete", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True;
                var statusText = root.TryGetProperty("status_text", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String
                    ? t.GetString() : null;
                var logUrl = root.TryGetProperty("log_url", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.String
                    ? l.GetString() : null;
                return new KuduDeploymentRecord(status, complete, statusText, logUrl);
            }
            catch (System.Text.Json.JsonException)
            {
                return new KuduDeploymentRecord(null, false, null, null);
            }
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"(body unreadable: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
