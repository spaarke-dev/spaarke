// -----------------------------------------------------------------------------
// DataverseRegistryConcurrencyStore.cs
//
// L2 CONTROL-PLANE production IRegistryConcurrencyStore impl (task 059).
//
// Issues direct Dataverse Web API calls against the customer's
// sprk_dataverseenvironment row to read + PATCH sprk_currentrunid with
// If-Match ETag guarding.
//
// AUTH:
//   Confidential-client (BFF app-reg client-credentials via
//   Azure.Identity.ClientSecretCredential). Same shape as H7's
//   DataverseWebApiEnvVarValuesWriter — the L2 App Service's UAMI is not
//   itself a Dataverse Application User on the admin env; the BFF app-reg is
//   the SP with a systemuser record. Future migration to DefaultAzureCredential
//   (UAMI) is documented in CustomerRunGuardOptions.cs FUTURE MIGRATION note.
//
// DATAVERSE SEMANTICS:
//   - Lookup by alt-key: GET /{entitySet}?$filter=sprk_customerid eq '{id}'
//     &$select=sprk_dataverseenvironmentid,sprk_currentrunid — returns the
//     row + column-of-interest; response's OData ETag lives in the row's
//     `@odata.etag` property.
//   - PATCH-if-null: PATCH /{entitySet}({rowId}) with IF-Match: {etag}
//     Prefer: return=minimal — body { "sprk_currentrunid": "{newRunId}" }.
//     If ETag stale -> 412 Precondition Failed.
//   - PATCH-to-null: same shape, body { "sprk_currentrunid": null }.
//
// NULL-COLUMN PROJECTION:
//   Dataverse OData REST ships a projected null column back as ABSENT from the
//   payload (not present as `"sprk_currentrunid": null`). The parser here
//   treats "property missing" as null (matches Dataverse's on-wire convention).
//
// NOT under test in the CI unit suite. Integration coverage would live in an
// env-guarded smoke test parity with CosmosSmokeTests + H7 writer. Unit
// coverage of the guard's decision logic lives against
// InMemoryRegistryConcurrencyStore.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// <see cref="IRegistryConcurrencyStore"/> that talks to the admin Dataverse
/// environment's <c>sprk_dataverseenvironments</c> entity set via Web API.
/// </summary>
public sealed class DataverseRegistryConcurrencyStore : IRegistryConcurrencyStore
{
    /// <summary>Named HttpClient for the store's outbound Dataverse calls.</summary>
    public const string HttpClientName = "CustomerRunGuard.DataverseRegistryConcurrencyStore";

    private const string ODataVersion = "4.0";
    private const string CurrentRunIdColumn = "sprk_currentrunid";
    private const string CustomerIdColumn = "sprk_customerid";
    private const string EnvironmentRowIdColumn = "sprk_dataverseenvironmentid";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CustomerRunGuardOptions _options;
    private readonly ILogger<DataverseRegistryConcurrencyStore> _logger;

    /// <summary>Constructs the store bound to the named HttpClient + configured options.</summary>
    public DataverseRegistryConcurrencyStore(
        IHttpClientFactory httpClientFactory,
        IOptions<CustomerRunGuardOptions> options,
        ILogger<DataverseRegistryConcurrencyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LookupOutcome> LookupAsync(
        string customerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (!TryBuildEnvUri(out var envUri, out var authError))
        {
            return new LookupOutcome.TransientFailure(customerId, authError!);
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CustomerRunGuard token acquisition failed for env={EnvUrl}", envUri);
            return new LookupOutcome.TransientFailure(customerId,
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        // Alt-key filter — Dataverse escapes single quotes by doubling.
        var filterValue = customerId.Replace("'", "''", StringComparison.Ordinal);
        var relative =
            $"/api/data/v9.2/{_options.EntitySetName}?" +
            $"$filter={CustomerIdColumn} eq '{Uri.EscapeDataString(filterValue)}'" +
            $"&$select={EnvironmentRowIdColumn},{CurrentRunIdColumn}" +
            "&$top=1";
        var requestUri = new Uri(envUri!, relative);

        try
        {
            using var request = BuildRequest(HttpMethod.Get, requestUri, token.Token);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new LookupOutcome.TransientFailure(customerId,
                    $"Lookup for customerId='{customerId}' returned {(int)response.StatusCode} {response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("value", out var arr)
                || arr.ValueKind != JsonValueKind.Array
                || arr.GetArrayLength() == 0)
            {
                return new LookupOutcome.NotFound(customerId);
            }

            var row = arr[0];
            if (!row.TryGetProperty(EnvironmentRowIdColumn, out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                return new LookupOutcome.TransientFailure(customerId,
                    $"Lookup for customerId='{customerId}' returned a row missing '{EnvironmentRowIdColumn}'.");
            }
            if (!Guid.TryParse(idProp.GetString(), out var environmentRowId))
            {
                return new LookupOutcome.TransientFailure(customerId,
                    $"Lookup for customerId='{customerId}' returned a non-GUID '{EnvironmentRowIdColumn}'.");
            }

            // Null projection convention: missing property == null value.
            string? currentRunId = null;
            if (row.TryGetProperty(CurrentRunIdColumn, out var currentProp)
                && currentProp.ValueKind == JsonValueKind.String)
            {
                currentRunId = currentProp.GetString();
            }

            var etag = ExtractEtag(row);
            if (etag is null)
            {
                return new LookupOutcome.TransientFailure(customerId,
                    $"Lookup for customerId='{customerId}' returned a row without an @odata.etag header.");
            }

            return new LookupOutcome.Found(environmentRowId, currentRunId, etag);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "CustomerRunGuard lookup infrastructure fault for customerId={CustomerId}", customerId);
            return new LookupOutcome.TransientFailure(customerId,
                $"Lookup infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<WriteOutcome> TrySetIfNullAsync(
        Guid environmentRowId,
        string newRunId,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatchEtag);

        // Body: { "sprk_currentrunid": "{newRunId}" }
        var json = BuildPatchBody(newRunId);
        return PatchAsync(environmentRowId, json, ifMatchEtag, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<WriteOutcome> TryClearAsync(
        Guid environmentRowId,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatchEtag);

        // Body: { "sprk_currentrunid": null }
        var json = BuildPatchBody(null);
        return PatchAsync(environmentRowId, json, ifMatchEtag, cancellationToken);
    }

    private async Task<WriteOutcome> PatchAsync(
        Guid environmentRowId,
        string bodyJson,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        if (!TryBuildEnvUri(out var envUri, out var authError))
        {
            return new WriteOutcome.TransientFailure(authError!);
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CustomerRunGuard token acquisition failed for env={EnvUrl}", envUri);
            return new WriteOutcome.TransientFailure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        var relative = $"/api/data/v9.2/{_options.EntitySetName}({environmentRowId})";
        var requestUri = new Uri(envUri!, relative);

        try
        {
            using var request = BuildRequest(HttpMethod.Patch, requestUri, token.Token);
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(ifMatchEtag, isWeak: false));
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return WriteOutcome.Success.Instance;
            }

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return WriteOutcome.PreconditionFailed.Instance;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return WriteOutcome.NotFound.Instance;
            }

            return new WriteOutcome.TransientFailure(
                $"PATCH {relative} returned {(int)response.StatusCode} {response.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "CustomerRunGuard PATCH infrastructure fault for environmentRowId={EnvironmentRowId}",
                environmentRowId);
            return new WriteOutcome.TransientFailure(
                $"PATCH infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryBuildEnvUri(out Uri? envUri, out string? diagnostic)
    {
        envUri = null;
        diagnostic = null;
        var raw = _options.TargetDataverseUrl;
        if (string.IsNullOrWhiteSpace(raw))
        {
            diagnostic = "CustomerRunGuard:TargetDataverseUrl is not configured.";
            return false;
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            diagnostic = $"CustomerRunGuard:TargetDataverseUrl '{raw}' is not a valid absolute URI.";
            return false;
        }
        envUri = parsed;
        return true;
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, CancellationToken cancellationToken)
    {
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new ClientSecretCredential(
            _options.TenantId!, _options.ClientId!, _options.ClientSecret!);
        return await credential.GetTokenAsync(
            new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
    }

    private static string BuildPatchBody(string? currentRunIdValue)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (currentRunIdValue is null)
            {
                writer.WriteNull(CurrentRunIdColumn);
            }
            else
            {
                writer.WriteString(CurrentRunIdColumn, currentRunIdValue);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string bearerToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private static string? ExtractEtag(JsonElement row)
    {
        // Dataverse's OData v4 embeds the row's ETag inline as `@odata.etag`.
        if (row.TryGetProperty("@odata.etag", out var etagProp)
            && etagProp.ValueKind == JsonValueKind.String)
        {
            var raw = etagProp.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // Dataverse formats as `W/"12345"` for weak ETags. We PATCH
                // with the exact string Dataverse returned; EntityTagHeaderValue
                // parses the weak-prefix form as well.
                return raw;
            }
        }
        return null;
    }
}
