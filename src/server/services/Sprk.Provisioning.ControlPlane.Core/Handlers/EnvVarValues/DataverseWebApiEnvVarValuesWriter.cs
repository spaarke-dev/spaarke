// -----------------------------------------------------------------------------
// DataverseWebApiEnvVarValuesWriter.cs
//
// Production <see cref="IEnvVarValuesWriter"/> implementation — replicates
// scripts/Provision-Customer.ps1 Step 8's Dataverse Web API sequence in C#:
// for each canonical schema name, GET the environmentvariabledefinition
// (expanding its environmentvariablevalues), then PATCH the existing value
// record or POST a new one bound to the definition.
//
// AUTH:
//   OAuth2 client-credentials (confidential client) via
//   Azure.Identity.ClientSecretCredential — SAME identity + pattern H6 uses
//   for solution import (pac auth create --clientSecret against the BFF
//   Entra app-reg). NOT DefaultAzureCredential/MI: the MI-Dataverse App User
//   (H10) has not yet been created at H7's point in the DAG (H10 runs AFTER
//   H7 per design.md §4.1). Token audience is the env URL's origin +
//   `/.default` — Dataverse Web API's token scope convention (parity with
//   DataverseWebApiHealthProbe).
//
// NOT under test in the CI unit suite. Integration coverage lives in an
// env-guarded smoke test, parity with the H5 health-probe note.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <summary>
/// <see cref="IEnvVarValuesWriter"/> implementation that issues direct
/// Dataverse Web API calls against <c>environmentvariabledefinitions</c> +
/// <c>environmentvariablevalues</c>.
/// </summary>
public sealed class DataverseWebApiEnvVarValuesWriter : IEnvVarValuesWriter
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H7.DataverseWebApiEnvVarValuesWriter";

    private const string ODataVersion = "4.0";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EnvVarValuesOptions _options;
    private readonly ILogger<DataverseWebApiEnvVarValuesWriter> _logger;

    /// <summary>Constructs the writer bound to the named HttpClient + configured request timeout.</summary>
    public DataverseWebApiEnvVarValuesWriter(
        IHttpClientFactory httpClientFactory,
        IOptions<EnvVarValuesOptions> options,
        ILogger<DataverseWebApiEnvVarValuesWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<EnvVarValuesWriteOutcome> WriteAsync(
        EnvVarValuesWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientSecret);

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new EnvVarValuesWriteOutcome.Failure(
                EnvVarValuesWriteFailureKind.UnknownInvocationFailure,
                SchemaName: null,
                Diagnostic: $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new ClientSecretCredential(request.TenantId, request.ClientId, request.ClientSecret);

        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "H7 writer token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
            return new EnvVarValuesWriteOutcome.Failure(
                EnvVarValuesWriteFailureKind.AuthFailure,
                SchemaName: null,
                Diagnostic: $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        var written = new List<KeyValuePair<string, string>>(request.Values.Count);
        foreach (var (schemaName, value) in request.Values)
        {
            var outcome = await UpsertOneAsync(httpClient, envUri, token.Token, schemaName, value, cancellationToken)
                .ConfigureAwait(false);
            if (outcome is not null)
            {
                return outcome;
            }
            written.Add(new KeyValuePair<string, string>(schemaName, value));
        }

        return new EnvVarValuesWriteOutcome.Success(written);
    }

    /// <summary>
    /// Upserts a single environment-variable value. Returns null on success,
    /// or a typed <see cref="EnvVarValuesWriteOutcome.Failure"/> on any
    /// non-recoverable outcome for this variable.
    /// </summary>
    private async Task<EnvVarValuesWriteOutcome.Failure?> UpsertOneAsync(
        HttpClient httpClient,
        Uri envUri,
        string bearerToken,
        string schemaName,
        string value,
        CancellationToken cancellationToken)
    {
        // (1) Find the definition by schema name, expanding its current values.
        var filter = Uri.EscapeDataString($"schemaname eq '{schemaName}'");
        var defUri = new Uri(envUri,
            $"/api/data/v9.2/environmentvariabledefinitions?$filter={filter}" +
            "&$expand=environmentvariablevalues($select=environmentvariablevalueid,value)" +
            "&$select=environmentvariabledefinitionid,schemaname,defaultvalue");

        JsonDocument defDoc;
        try
        {
            using var defRequest = BuildRequest(HttpMethod.Get, defUri, bearerToken);
            using var defResponse = await httpClient.SendAsync(defRequest, cancellationToken).ConfigureAwait(false);
            if (!defResponse.IsSuccessStatusCode)
            {
                // ALWAYS return here — never fall through to body parsing on a
                // non-success status. A prior version fell through when
                // ClassifyNonSuccess returned null (unclassified status codes
                // like 500), which then threw an uncaught KeyNotFoundException
                // from GetProperty("value") on an OData error body instead of
                // surfacing a classified Resumable failure. The `?? new(...)`
                // fallback guarantees a return in every non-success case.
                var diagnostic =
                    $"Definition lookup for '{schemaName}' returned {(int)defResponse.StatusCode} {defResponse.StatusCode}.";
                return ClassifyNonSuccess(defResponse.StatusCode, schemaName, diagnostic)
                    ?? new EnvVarValuesWriteOutcome.Failure(
                        EnvVarValuesWriteFailureKind.UnknownInvocationFailure, schemaName, diagnostic);
            }
            var bodyText = await defResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            defDoc = JsonDocument.Parse(bodyText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "H7 writer definition-lookup infrastructure fault for schemaName={SchemaName}", schemaName);
            return new EnvVarValuesWriteOutcome.Failure(
                EnvVarValuesWriteFailureKind.UnknownInvocationFailure, schemaName,
                $"Definition lookup infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }

        using (defDoc)
        {
            var valueArray = defDoc.RootElement.GetProperty("value");
            if (valueArray.GetArrayLength() == 0)
            {
                return new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.DefinitionNotFound, schemaName,
                    $"'{schemaName}' definition not found in Dataverse. Ensure solution import (H6) created it.");
            }

            var definition = valueArray[0];
            var definitionId = definition.GetProperty("environmentvariabledefinitionid").GetString()!;
            var hasExistingValue = definition.TryGetProperty("environmentvariablevalues", out var existingValues)
                && existingValues.ValueKind == JsonValueKind.Array
                && existingValues.GetArrayLength() > 0;

            HttpResponseMessage upsertResponse;
            try
            {
                if (hasExistingValue)
                {
                    var valueId = existingValues[0].GetProperty("environmentvariablevalueid").GetString()!;
                    var patchUri = new Uri(envUri, $"/api/data/v9.2/environmentvariablevalues({valueId})");
                    using var patchRequest = BuildRequest(HttpMethod.Patch, patchUri, bearerToken);
                    patchRequest.Content = BuildJsonContent(new { value });
                    upsertResponse = await httpClient.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var postUri = new Uri(envUri, "/api/data/v9.2/environmentvariablevalues");
                    using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
                    postRequest.Content = BuildJsonContent(new Dictionary<string, object?>
                    {
                        ["value"] = value,
                        ["EnvironmentVariableDefinitionId@odata.bind"] = $"/environmentvariabledefinitions({definitionId})",
                    });
                    upsertResponse = await httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "H7 writer upsert infrastructure fault for schemaName={SchemaName}", schemaName);
                return new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.UnknownInvocationFailure, schemaName,
                    $"Upsert infrastructure error: {ex.GetType().Name}: {ex.Message}");
            }

            using (upsertResponse)
            {
                if (!upsertResponse.IsSuccessStatusCode)
                {
                    var failure = ClassifyNonSuccess(upsertResponse.StatusCode, schemaName,
                        $"Upsert for '{schemaName}' returned {(int)upsertResponse.StatusCode} {upsertResponse.StatusCode}.");
                    return failure ?? new EnvVarValuesWriteOutcome.Failure(
                        EnvVarValuesWriteFailureKind.UnknownInvocationFailure, schemaName,
                        $"Upsert for '{schemaName}' returned {(int)upsertResponse.StatusCode} {upsertResponse.StatusCode}.");
                }
            }
        }

        return null;
    }

    private static EnvVarValuesWriteOutcome.Failure? ClassifyNonSuccess(
        HttpStatusCode statusCode, string schemaName, string diagnostic)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new EnvVarValuesWriteOutcome.Failure(EnvVarValuesWriteFailureKind.AuthFailure, schemaName, diagnostic);
        }
        if ((int)statusCode == 429)
        {
            return new EnvVarValuesWriteOutcome.Failure(EnvVarValuesWriteFailureKind.RateLimited, schemaName, diagnostic);
        }
        if (statusCode == HttpStatusCode.NotFound)
        {
            return new EnvVarValuesWriteOutcome.Failure(EnvVarValuesWriteFailureKind.DefinitionNotFound, schemaName, diagnostic);
        }
        return null; // Caller decides fallback classification.
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string bearerToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private static StringContent BuildJsonContent(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
