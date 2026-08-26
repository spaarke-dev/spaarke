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
// SYNCHRONOUS (not ?isAsync=true): matches the CI workflow's own
// `az webapp deploy ... --async false` precedent (deploy-bff-api.yml Job 3)
// and keeps the response contract simple (2xx = done, non-2xx = Failure) —
// the request timeout (BffDeployOptions.KuduZipDeployTimeout) bounds worst-
// case wait, consistent with every other collaborator's timeout-wrapped
// pattern in this handler family.
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
        var uri = new Uri($"https://{kuduHost}/api/zipdeploy");

        var stopwatch = Stopwatch.StartNew();

        var tokenResult = await _credential.GetTokenAsync(
            new TokenRequestContext(ArmScope), cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.KuduZipDeployTimeout);

        HttpResponseMessage response;
        await using var zipStream = File.OpenRead(request.LocalZipPath);
        using var content = new StreamContent(zipStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        _logger.LogInformation(
            "H9 Kudu zip-deploy starting: host={KuduHost} localZipPath={LocalZipPath}",
            kuduHost, request.LocalZipPath);

        try
        {
            response = await _httpClient.SendAsync(requestMessage, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Kudu zip-deploy to '{kuduHost}' timed out after {_options.KuduZipDeployTimeout}.");
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "H9 Kudu zip-deploy exited: host={KuduHost} statusCode={StatusCode} durationMs={DurationMs}",
            kuduHost, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            var diagnostic =
                $"Kudu zip-deploy POST https://{kuduHost}/api/zipdeploy returned HTTP " +
                $"{(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 600)}";
            response.Dispose();
            return new KuduZipDeployResult.Failure(diagnostic);
        }

        response.Dispose();
        return new KuduZipDeployResult.Success(stopwatch.Elapsed);
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
