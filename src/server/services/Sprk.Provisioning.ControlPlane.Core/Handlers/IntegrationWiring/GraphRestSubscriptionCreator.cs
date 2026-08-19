// -----------------------------------------------------------------------------
// GraphRestSubscriptionCreator.cs
//
// Production IGraphSubscriptionCreator — raw Microsoft Graph REST calls (same
// Path-C rationale as GraphRestAppRoleGranter.cs / GraphRestAppRoleParityVerifier.cs
// in Handlers/DataverseAppUserGraphParity/ — see
// H10DataverseAppUserGraphParityHandler.cs's "NFR-09 IMPLEMENTATION NOTE" file
// header for the full pivot-to-comply rationale, which applies identically
// here: L2 does not carry the Microsoft.Graph SDK; non-success HTTP status +
// response body surfaces the same diagnostic signal NFR-09 asks the BFF's
// ODataError catch to carry).
//
// IDEMPOTENT CREATE-OR-RENEW:
//   GET /v1.0/subscriptions (list all, filter client-side by resource +
//   notificationUrl match — Graph's subscriptions endpoint does not support
//   server-side $filter on those fields for all workloads); if a match is
//   found, PATCH its expirationDateTime (renew); otherwise POST create.
//
// NOT under test in the CI unit suite (real Graph REST calls). Handler unit
// tests substitute a fake IGraphSubscriptionCreator.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IGraphSubscriptionCreator"/>
public sealed class GraphRestSubscriptionCreator : IGraphSubscriptionCreator
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphRestSubscriptionCreator> _logger;

    public GraphRestSubscriptionCreator(
        HttpClient httpClient,
        IOptions<IntegrationWiringOptions> options,
        ILogger<GraphRestSubscriptionCreator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = options.Value.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<GraphSubscriptionOutcome> CreateOrUpdateAsync(
        GraphSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NotificationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientState);

        AccessToken token;
        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = request.TenantId });
            token = await credential.GetTokenAsync(
                new TokenRequestContext(GraphScope), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H14b token acquisition failed for module {ModuleName}", request.ModuleName);
            return new GraphSubscriptionOutcome.Failure($"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var existingId = await FindExistingSubscriptionIdAsync(token, request, cancellationToken).ConfigureAwait(false);
            var expiration = DateTimeOffset.UtcNow.AddMinutes(request.ExpirationMinutes).ToString("O");

            if (existingId is not null)
            {
                await RenewSubscriptionAsync(token, existingId, expiration, cancellationToken).ConfigureAwait(false);
                return new GraphSubscriptionOutcome.Renewed(existingId);
            }

            var createdId = await CreateSubscriptionAsync(token, request, expiration, cancellationToken).ConfigureAwait(false);
            return new GraphSubscriptionOutcome.Created(createdId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H14b Graph subscription create-or-renew infrastructure fault for module {ModuleName}",
                request.ModuleName);
            return new GraphSubscriptionOutcome.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<string?> FindExistingSubscriptionIdAsync(AccessToken token, GraphSubscriptionRequest request, CancellationToken ct)
    {
        var uri = new Uri("https://graph.microsoft.com/v1.0/subscriptions");
        using var doc = await GetJsonAsync(uri, token, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in values.EnumerateArray())
        {
            var resource = entry.TryGetProperty("resource", out var r) ? r.GetString() : null;
            var notificationUrl = entry.TryGetProperty("notificationUrl", out var n) ? n.GetString() : null;
            if (string.Equals(resource, request.Resource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(notificationUrl, request.NotificationUrl, StringComparison.OrdinalIgnoreCase))
            {
                return entry.TryGetProperty("id", out var id) ? id.GetString() : null;
            }
        }

        return null;
    }

    private async Task RenewSubscriptionAsync(AccessToken token, string subscriptionId, string expirationDateTime, CancellationToken ct)
    {
        var uri = new Uri($"https://graph.microsoft.com/v1.0/subscriptions/{subscriptionId}");
        var payload = new Dictionary<string, object?> { ["expirationDateTime"] = expirationDateTime };

        using var request = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"PATCH subscriptions/{subscriptionId} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private async Task<string> CreateSubscriptionAsync(AccessToken token, GraphSubscriptionRequest request, string expirationDateTime, CancellationToken ct)
    {
        var uri = new Uri("https://graph.microsoft.com/v1.0/subscriptions");
        var payload = new Dictionary<string, object?>
        {
            ["changeType"] = request.ChangeType,
            ["notificationUrl"] = request.NotificationUrl,
            ["resource"] = request.Resource,
            ["expirationDateTime"] = expirationDateTime,
            ["clientState"] = request.ClientState,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        ApplyCommonHeaders(httpRequest, token);

        using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST subscriptions failed: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Body: {Truncate(RedactSecret(text, request.ClientState), 400)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return doc.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } idValue
            ? idValue
            : throw new InvalidOperationException("POST subscriptions succeeded but the response carried no parseable 'id' property.");
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, AccessToken token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyCommonHeaders(request, token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {uri} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 300)}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static void ApplyCommonHeaders(HttpRequestMessage request, AccessToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>
    /// Defense-in-depth: strips any literal occurrence of the HMAC clientState
    /// from a Graph error response body before it is embedded in an exception
    /// message. Graph does not normally echo the submitted clientState value
    /// back on a validation error, but this guard removes the theoretical leak
    /// path without waiting for that to actually happen. Exposed internal so
    /// unit tests can verify the redaction directly. Parity with
    /// DataverseWebApiServiceEndpointWebhookRegistrar.RedactSecret.
    /// </summary>
    internal static string RedactSecret(string body, string clientState)
        => string.IsNullOrEmpty(body) || string.IsNullOrEmpty(clientState)
            ? body
            : body.Replace(clientState, "***REDACTED***", StringComparison.Ordinal);
}
