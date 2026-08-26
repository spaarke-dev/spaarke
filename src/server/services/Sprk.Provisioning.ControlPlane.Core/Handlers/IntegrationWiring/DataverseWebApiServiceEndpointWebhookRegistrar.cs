// -----------------------------------------------------------------------------
// DataverseWebApiServiceEndpointWebhookRegistrar.cs
//
// Production IServiceEndpointWebhookRegistrar — issues Dataverse Web API
// calls to upsert a `serviceendpoint` webhook record (find-by-name, PATCH if
// exists, else POST create). Auth via DefaultAzureCredential (ADR-028
// MI-outbound MUST rule; §4D I5 explicit per-tenant scope). Parity in shape
// with DataverseWebApiAppUserCreator (task 053).
//
// NOT under test in the CI unit suite (real Dataverse Web API calls). Handler
// unit tests substitute a fake IServiceEndpointWebhookRegistrar.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IServiceEndpointWebhookRegistrar"/>
public sealed class DataverseWebApiServiceEndpointWebhookRegistrar : IServiceEndpointWebhookRegistrar
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseWebApiServiceEndpointWebhookRegistrar> _logger;

    public DataverseWebApiServiceEndpointWebhookRegistrar(
        HttpClient httpClient,
        IOptions<IntegrationWiringOptions> options,
        ILogger<DataverseWebApiServiceEndpointWebhookRegistrar> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = options.Value.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<ServiceEndpointWebhookOutcome> RegisterAsync(
        ServiceEndpointWebhookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SigningKey);

        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            return new ServiceEndpointWebhookOutcome.Failure(
                $"Environment URL '{request.EnvironmentUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = request.TenantId });
            token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { $"{scopeBase}/.default" }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ServiceEndpointWebhookOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var existingId = await FindByNameAsync(envUri, token, request.Name, cancellationToken).ConfigureAwait(false);

            var payload = new Dictionary<string, object?>
            {
                ["name"] = request.Name,
                ["url"] = request.Url,
                ["contract"] = request.Contract,
                ["messageformat"] = request.MessageFormat,
                ["authtype"] = request.AuthType,
                // saskey carries the HMAC signing material — Dataverse stores
                // it as a write-only field. ADR-028: cleartext travels only
                // through this call's process boundary, never persisted to
                // Cosmos (the H14c sub-handler never writes it to InterStepState).
                ["saskey"] = request.SigningKey,
            };

            if (existingId is not null)
            {
                await PatchAsync(envUri, token, existingId, payload, request.SigningKey, cancellationToken).ConfigureAwait(false);
                return new ServiceEndpointWebhookOutcome.Updated(existingId);
            }

            var createdId = await CreateAsync(envUri, token, payload, request.SigningKey, cancellationToken).ConfigureAwait(false);
            return new ServiceEndpointWebhookOutcome.Created(createdId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseWebApiServiceEndpointWebhookRegistrar infrastructure fault for serviceendpoint '{Name}'",
                request.Name);
            return new ServiceEndpointWebhookOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<string?> FindByNameAsync(Uri envUri, AccessToken token, string name, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(name);
        var uri = new Uri(envUri, $"/api/data/v9.2/serviceendpoints?$filter=name eq '{encodedName}'&$select=serviceendpointid");
        var doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0
            ? values[0].GetProperty("serviceendpointid").GetString()
            : null;
    }

    private async Task PatchAsync(
        Uri envUri, AccessToken token, string serviceEndpointId, Dictionary<string, object?> payload,
        string signingKey, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/serviceendpoints({serviceEndpointId})");
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"PATCH serviceendpoints({serviceEndpointId}) failed: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Body: {Truncate(RedactSecret(body, signingKey), 400)}");
        }
    }

    private async Task<string> CreateAsync(
        Uri envUri, AccessToken token, Dictionary<string, object?> payload, string signingKey, CancellationToken ct)
    {
        var uri = new Uri(envUri, "/api/data/v9.2/serviceendpoints");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST serviceendpoints failed: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Body: {Truncate(RedactSecret(body, signingKey), 400)}");
        }

        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var extracted = ExtractGuidFromEntityId(values.FirstOrDefault());
            if (extracted is not null)
            {
                return extracted;
            }
        }

        throw new InvalidOperationException(
            "POST serviceendpoints succeeded but the response carried no parseable OData-EntityId header.");
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

    private static string? ExtractGuidFromEntityId(string? entityIdHeader)
    {
        if (string.IsNullOrWhiteSpace(entityIdHeader)) return null;
        var openParen = entityIdHeader.LastIndexOf('(');
        var closeParen = entityIdHeader.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen) return null;
        var candidate = entityIdHeader.Substring(openParen + 1, closeParen - openParen - 1);
        return Guid.TryParse(candidate, out var guid) ? guid.ToString() : null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>
    /// Defense-in-depth: strips any literal occurrence of the HMAC signing
    /// key from a Dataverse error response body before it is embedded in an
    /// exception message / diagnostic. Dataverse does not normally echo a
    /// write-only field's submitted value back on a validation error, but
    /// this guard removes the theoretical leak path (a future Dataverse
    /// behavior change, or a validation-error shape this code hasn't seen)
    /// without waiting for that to actually happen. Exposed internal so
    /// unit tests can verify the redaction directly.
    /// </summary>
    internal static string RedactSecret(string body, string signingKey)
        => string.IsNullOrEmpty(body) || string.IsNullOrEmpty(signingKey)
            ? body
            : body.Replace(signingKey, "***REDACTED***", StringComparison.Ordinal);
}
