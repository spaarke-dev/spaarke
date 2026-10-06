// -----------------------------------------------------------------------------
// DataverseWebApiEnvVarValuesWriter.cs
//
// Production <see cref="IEnvVarValuesWriter"/> implementation — replicates
// scripts/Provision-Customer.ps1 Step 8's Dataverse Web API sequence in C#:
// for each canonical schema name, GET the environmentvariabledefinition
// (expanding its environmentvariablevalues), then PATCH the existing value
// record or POST a new one bound to the definition.
//
// AUTH (A44.5, task 205i — supersedes the task-050 ClientSecret-only note):
//   OAuth2 client-credentials (confidential client) as the shared BFF Entra
//   app-reg — SAME identity + pattern H6 uses for solution import. The
//   CREDENTIAL is now selected by the FR-39 ordered chain
//   (WorkerDataverseCredentialFactory over EnvVarValues:Credentials:Order —
//   MI-FIC first on secret-free envs, ClientSecret fallback for prong-3
//   unmigrated envs), mirroring master's DataverseServiceClientImpl
//   migration (auth-v4 task 022, brought in via A35). Raw
//   `new ClientSecretCredential(...)` construction is confined to the
//   factory's ClientSecret (pre-migration/fallback) branch — never on the
//   secret-free branch. NOT DefaultAzureCredential-as-the-Worker: H7
//   authenticates AS the BFF app-reg (the MI-Dataverse App User (H10) has
//   not yet been created at H7's point in the DAG; H10 runs AFTER H7 per
//   design.md §4.1) — under MI-FIC the Worker's UAMI merely MINTS the
//   federated assertion the app-reg trusts (H3-created FIC). Token audience
//   is the env URL's origin + `/.default` — Dataverse Web API's token scope
//   convention (parity with DataverseWebApiHealthProbe).
//
// NOT under test in the CI unit suite. Integration coverage lives in an
// env-guarded smoke test, parity with the H5 health-probe note. (The FR-39
// selection itself IS CI-unit-tested at the factory boundary —
// WorkerDataverseCredentialFactoryTests.)
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

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
    private readonly WorkerDataverseCredentialFactory _credentialFactory;
    private readonly ILogger<DataverseWebApiEnvVarValuesWriter> _logger;

    /// <summary>
    /// Constructs the writer bound to the named HttpClient + configured
    /// request timeout + the FR-39 ordered credential factory (A44.5 — the
    /// factory is injected concretely per ADR-010; it performs no I/O at
    /// construction or selection time).
    /// </summary>
    public DataverseWebApiEnvVarValuesWriter(
        IHttpClientFactory httpClientFactory,
        IOptions<EnvVarValuesOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiEnvVarValuesWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _credentialFactory = credentialFactory;
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
        // A44.5: request.ClientSecret is deliberately NOT required here — on
        // secret-free envs it is empty (the signal, §9.1) and the FR-39
        // factory below selects MI-FIC from the configured chain.

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new EnvVarValuesWriteOutcome.Failure(
                EnvVarValuesWriteFailureKind.UnknownInvocationFailure,
                SchemaName: null,
                Diagnostic: $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";

        AccessToken token;
        try
        {
            // FR-39 ordered selection (A44.5): MI-FIC first on secret-free
            // chains; ClientSecret only for prong-3 unmigrated envs. An
            // exhausted chain throws (fail-closed) and classifies AuthFailure
            // → §4C Resumable at the handler — the correct failure boundary
            // for a per-run collaborator (see factory file header for the
            // documented narrowing vs the BFF's hot-path provider).
            var selected = _credentialFactory.Create(
                _options.Credentials,
                EnvVarValuesOptions.SectionName,
                request.TenantId,
                request.ClientId,
                request.ClientSecret);
            token = await selected.Credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Prefix convention preserved across H6/H7 collaborators (task-141
            // test contract parity); credential-SELECTION failures carry their
            // own "No credential could be selected …" inner message.
            _logger.LogWarning(ex,
                "H7 writer credential selection / token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
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
