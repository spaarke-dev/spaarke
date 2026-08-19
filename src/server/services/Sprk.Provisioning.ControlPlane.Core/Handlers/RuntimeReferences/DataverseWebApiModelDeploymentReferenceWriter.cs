// -----------------------------------------------------------------------------
// DataverseWebApiModelDeploymentReferenceWriter.cs
//
// Production IModelDeploymentReferenceWriter — issues Dataverse Web API calls
// to upsert sprk_aimodeldeployment rows (find-by-sprk_name, PATCH if found,
// POST if not). Auth via DefaultAzureCredential (ADR-028 MI-outbound MUST
// rule; §4D I5 explicit per-tenant scope — never a default-tenant
// credential). Parity in shape with DataverseWebApiAppUserCreator (task 053)
// + DataverseWebApiHealthProbe (task 048) — same token-acquisition idiom,
// same typed-HttpClient registration pattern.
//
// NOT under test in the CI unit suite (real Dataverse Web API calls). Handler
// unit tests substitute a fake IModelDeploymentReferenceWriter — parity with
// DataverseWebApiAppUserCreator's file-header note.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <inheritdoc cref="IModelDeploymentReferenceWriter"/>
public sealed class DataverseWebApiModelDeploymentReferenceWriter : IModelDeploymentReferenceWriter
{
    private readonly HttpClient _httpClient;
    private readonly RuntimeReferencesOptions _options;
    private readonly ILogger<DataverseWebApiModelDeploymentReferenceWriter> _logger;

    public DataverseWebApiModelDeploymentReferenceWriter(
        HttpClient httpClient,
        IOptions<RuntimeReferencesOptions> options,
        ILogger<DataverseWebApiModelDeploymentReferenceWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<ModelDeploymentReferenceWriteOutcome> UpsertAsync(
        ModelDeploymentReferenceWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DataverseEnvironmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        if (!Uri.TryCreate(request.DataverseEnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            return new ModelDeploymentReferenceWriteOutcome.Failure(
                $"Dataverse environment URL '{request.DataverseEnvironmentUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ModelDeploymentReferenceWriteOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var upserted = new List<string>(request.Deployments.Count);
        try
        {
            foreach (var deployment in request.Deployments)
            {
                var existingId = await FindByNameAsync(envUri, token, deployment.ModelId, cancellationToken)
                    .ConfigureAwait(false);

                var payload = BuildPayload(deployment, request.Provider);

                if (existingId is not null)
                {
                    await PatchAsync(envUri, token, existingId, payload, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CreateAsync(envUri, token, payload, cancellationToken).ConfigureAwait(false);
                }

                upserted.Add(deployment.ModelId);
            }

            return new ModelDeploymentReferenceWriteOutcome.Success(upserted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseWebApiModelDeploymentReferenceWriter infrastructure fault — {UpsertedCount}/{TotalCount} rows " +
                "upserted before the failure (retry-safe — remaining rows create-if-missing on re-run).",
                upserted.Count, request.Deployments.Count);
            return new ModelDeploymentReferenceWriteOutcome.Failure(
                $"Dataverse Web API error after upserting {upserted.Count}/{request.Deployments.Count} rows: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        return await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
    }

    private async Task<string?> FindByNameAsync(Uri envUri, AccessToken token, string modelId, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(modelId);
        var uri = new Uri(envUri,
            $"/api/data/v9.2/sprk_aimodeldeployments?$filter=sprk_name eq '{encodedName}'&$select=sprk_aimodeldeploymentid");
        var doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0
            ? values[0].GetProperty("sprk_aimodeldeploymentid").GetString()
            : null;
    }

    private static Dictionary<string, object?> BuildPayload(ModelDeploymentReference deployment, ModelProvider provider)
        => new()
        {
            ["sprk_name"] = deployment.ModelId,
            ["sprk_modelid"] = deployment.ModelId,
            ["sprk_provider"] = (int)provider,
            ["sprk_capability"] = (int)deployment.Capability,
            ["sprk_endpoint"] = deployment.EndpointUri,
            ["sprk_isactive"] = true,
            ["sprk_description"] = deployment.Description,
        };

    private async Task CreateAsync(Uri envUri, AccessToken token, Dictionary<string, object?> payload, CancellationToken ct)
    {
        var uri = new Uri(envUri, "/api/data/v9.2/sprk_aimodeldeployments");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST sprk_aimodeldeployments failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private async Task PatchAsync(Uri envUri, AccessToken token, string id, Dictionary<string, object?> payload, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/sprk_aimodeldeployments({id})");
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"PATCH sprk_aimodeldeployments({id}) failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private async Task<JsonDocument> SendAndReadAsync(Uri uri, HttpMethod method, AccessToken token, HttpContent? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{method} {uri.PathAndQuery} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static void ApplyCommonHeaders(HttpRequestMessage request, AccessToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("Prefer", "return=representation");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
