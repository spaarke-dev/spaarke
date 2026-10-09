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
// TASK 227g — THE ROOT BUSINESS UNIT'S CONTAINER: before the values, the same token links the environment's root
// business unit to H8's container (businessunit.sprk_containerid), the write scripts/Provision-Customer.ps1 step 10 made
// and L2 had dropped. unified-access-control-r2 task 076 resolves a record that is not secure to its OWNING business
// unit's sprk_containerid (RecordContainerResolver), so without it every non-secure record of a fresh stamp resolves to
// no container. Equal → nothing written; empty → PATCH + read back; ANOTHER container → refused, nothing written.
// LinkRootBusinessUnitContainerAsync takes the HttpClient + token, so it IS CI-unit-tested
// (DataverseRootBusinessUnitLinkTests) against a hand-written HttpMessageHandler.
//
// T259 (ISS-010): right after the root, the CUSTOMER's business unit (H10) is linked to the same container with the same
// rules (LinkCustomerBusinessUnitContainerAsync). Every user and BFF application user lives in that unit, so the records
// they own are owned there — without the link every non-secure record of a T259 stamp resolves to no container.
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

        Guid? linkedRootBusinessUnitId = null;
        if (!string.IsNullOrWhiteSpace(request.RootBusinessUnitContainerId))
        {
            var link = await LinkRootBusinessUnitContainerAsync(
                httpClient, envUri, token.Token, request.RootBusinessUnitContainerId.Trim(), _logger, cancellationToken)
                .ConfigureAwait(false);
            if (link.Failure is not null)
            {
                return link.Failure;
            }
            linkedRootBusinessUnitId = link.RootBusinessUnitId;
        }

        Guid? linkedCustomerBusinessUnitId = null;
        if (!string.IsNullOrWhiteSpace(request.RootBusinessUnitContainerId) && request.CustomerBusinessUnitId is { } customerUnit)
        {
            var failure = await LinkCustomerBusinessUnitContainerAsync(
                httpClient, envUri, token.Token, customerUnit, request.RootBusinessUnitContainerId.Trim(), _logger, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }
            linkedCustomerBusinessUnitId = customerUnit;
        }

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

        return new EnvVarValuesWriteOutcome.Success(written, linkedRootBusinessUnitId, linkedCustomerBusinessUnitId);
    }

    /// <summary>The label H7's diagnostics use for the root business unit's container column.</summary>
    internal const string RootBusinessUnitContainerColumnLabel = "businessunit.sprk_containerid";

    /// <summary>
    /// Task 227g: makes the environment's ROOT business unit's <c>sprk_containerid</c> name <paramref name="containerId"/>.
    /// Already equal → nothing written; empty → PATCH, then read back; another container → refused (nothing written); no
    /// root or two roots → refused. An HTTP status, a transport fault or a timeout is a typed failure; the caller's
    /// cancellation throws, and so does a 200 whose body lacks the expected shape (H7 maps that to Resumable
    /// <c>writer-invocation-failed</c>).
    /// </summary>
    internal static async Task<RootBusinessUnitLinkResult> LinkRootBusinessUnitContainerAsync(
        HttpClient httpClient,
        Uri envUri,
        string bearerToken,
        string containerId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // (1) The root unit and its current container — the query H8's root-unit reader uses; $top=2 so a second root is
        // seen rather than ignored.
        var (rows, readFailure) = await GetJsonAsync(httpClient,
            new Uri(envUri, "/api/data/v9.2/businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid,sprk_containerid&$top=2"),
            bearerToken, "Reading the root business unit", logger, cancellationToken).ConfigureAwait(false);
        if (readFailure is not null)
        {
            return new RootBusinessUnitLinkResult(null, readFailure);
        }

        Guid rootId;
        using (rows)
        {
            var units = rows!.RootElement.GetProperty("value");
            if (units.GetArrayLength() != 1)
            {
                return new RootBusinessUnitLinkResult(null, new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.RootBusinessUnitUnresolved, RootBusinessUnitContainerColumnLabel,
                    $"The environment reports {units.GetArrayLength()} root business units (expected exactly one), so H7 cannot " +
                    $"link the root unit to container '{containerId}'. Nothing was written."));
            }

            var unit = units[0];
            if (!Guid.TryParse(unit.GetProperty("businessunitid").GetString(), out rootId) || rootId == Guid.Empty)
            {
                return new RootBusinessUnitLinkResult(null, new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.RootBusinessUnitUnresolved, RootBusinessUnitContainerColumnLabel,
                    "The root business unit's id is not a GUID. Nothing was written."));
            }

            var current = ContainerIdOf(unit);
            if (string.Equals(current, containerId, StringComparison.Ordinal))
            {
                return new RootBusinessUnitLinkResult(rootId, null);
            }

            if (current is not null)
            {
                return new RootBusinessUnitLinkResult(null, new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.RootBusinessUnitContainerConflict, RootBusinessUnitContainerColumnLabel,
                    $"The root business unit {rootId} already names container '{current}', not this run's container " +
                    $"'{containerId}'. Nothing was written: replacing it would move where the customer's non-secure files go. " +
                    "Decide which container is the customer's, make the root unit and the run agree, then resume."));
            }
        }

        // (2) Empty: link it.
        try
        {
            using var request = BuildRequest(HttpMethod.Patch, new Uri(envUri, $"/api/data/v9.2/businessunits({rootId})"), bearerToken);
            request.Content = BuildJsonContent(new Dictionary<string, object?> { ["sprk_containerid"] = containerId });
            using var patch = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!patch.IsSuccessStatusCode)
            {
                return new RootBusinessUnitLinkResult(null, NonSuccess(patch.StatusCode, "Linking the root business unit"));
            }
        }
        catch (Exception ex) when (IsTransportFault(ex, cancellationToken))
        {
            logger.LogWarning(ex, "H7 root business unit link: PATCH failed");
            return new RootBusinessUnitLinkResult(null, Transport("Linking the root business unit", ex));
        }

        // (3) Read it back — a write the environment did not keep is not a link.
        var (after, afterFailure) = await GetJsonAsync(httpClient,
            new Uri(envUri, $"/api/data/v9.2/businessunits({rootId})?$select=sprk_containerid"),
            bearerToken, "Reading the root business unit's container back", logger, cancellationToken).ConfigureAwait(false);
        if (afterFailure is not null)
        {
            return new RootBusinessUnitLinkResult(null, afterFailure);
        }

        using (after)
        {
            var readBack = ContainerIdOf(after!.RootElement);
            return string.Equals(readBack, containerId, StringComparison.Ordinal)
                ? new RootBusinessUnitLinkResult(rootId, null)
                : new RootBusinessUnitLinkResult(null, new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.UnknownInvocationFailure, RootBusinessUnitContainerColumnLabel,
                    $"The root business unit {rootId} did not keep container '{containerId}' (read back: '{readBack}')."));
        }
    }

    /// <summary>
    /// T259 (ISS-010): makes the CUSTOMER's business unit's <c>sprk_containerid</c> name <paramref name="containerId"/> — the
    /// root unit's rules: already equal → nothing written; empty → PATCH, then read back; another container → refused
    /// (nothing written); the unit absent (404) → refused. Returns null when linked.
    /// </summary>
    internal static async Task<EnvVarValuesWriteOutcome.Failure?> LinkCustomerBusinessUnitContainerAsync(
        HttpClient httpClient,
        Uri envUri,
        string bearerToken,
        Guid customerBusinessUnitId,
        string containerId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var unitUri = new Uri(envUri, $"/api/data/v9.2/businessunits({customerBusinessUnitId:D})?$select=sprk_containerid");
        string? current;
        try
        {
            using var read = BuildRequest(HttpMethod.Get, unitUri, bearerToken);
            using var response = await httpClient.SendAsync(read, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.CustomerBusinessUnitUnresolved, RootBusinessUnitContainerColumnLabel,
                    $"The customer's business unit {customerBusinessUnitId} (H10) does not exist, so H7 cannot link it to " +
                    $"container '{containerId}'. Nothing was written.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return NonSuccess(response.StatusCode, "Reading the customer's business unit");
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            current = ContainerIdOf(document.RootElement);
        }
        catch (Exception ex) when (IsTransportFault(ex, cancellationToken) || ex is JsonException)
        {
            logger.LogWarning(ex, "H7 customer business unit link: read failed");
            return Transport("Reading the customer's business unit", ex);
        }

        if (string.Equals(current, containerId, StringComparison.Ordinal))
        {
            return null;
        }
        if (current is not null)
        {
            return new EnvVarValuesWriteOutcome.Failure(
                EnvVarValuesWriteFailureKind.CustomerBusinessUnitContainerConflict, RootBusinessUnitContainerColumnLabel,
                $"The customer's business unit {customerBusinessUnitId} already names container '{current}', not this run's " +
                $"container '{containerId}'. Nothing was written: replacing it would move where the customer's non-secure " +
                "files go. Decide which container is the customer's, make the unit and the run agree, then resume.");
        }

        try
        {
            using var patch = BuildRequest(HttpMethod.Patch,
                new Uri(envUri, $"/api/data/v9.2/businessunits({customerBusinessUnitId:D})"), bearerToken);
            patch.Content = BuildJsonContent(new Dictionary<string, object?> { ["sprk_containerid"] = containerId });
            using var patched = await httpClient.SendAsync(patch, cancellationToken).ConfigureAwait(false);
            if (!patched.IsSuccessStatusCode)
            {
                return NonSuccess(patched.StatusCode, "Linking the customer's business unit");
            }
        }
        catch (Exception ex) when (IsTransportFault(ex, cancellationToken))
        {
            logger.LogWarning(ex, "H7 customer business unit link: PATCH failed");
            return Transport("Linking the customer's business unit", ex);
        }

        var (after, afterFailure) = await GetJsonAsync(httpClient, unitUri, bearerToken,
            "Reading the customer's business unit's container back", logger, cancellationToken).ConfigureAwait(false);
        if (afterFailure is not null)
        {
            return afterFailure;
        }
        using (after)
        {
            var readBack = ContainerIdOf(after!.RootElement);
            return string.Equals(readBack, containerId, StringComparison.Ordinal)
                ? null
                : new EnvVarValuesWriteOutcome.Failure(
                    EnvVarValuesWriteFailureKind.UnknownInvocationFailure, RootBusinessUnitContainerColumnLabel,
                    $"The customer's business unit {customerBusinessUnitId} did not keep container '{containerId}' (read back: '{readBack}').");
        }
    }

    private static string? ContainerIdOf(JsonElement unit)
        => unit.TryGetProperty("sprk_containerid", out var value) && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static async Task<(JsonDocument? Document, EnvVarValuesWriteOutcome.Failure? Failure)> GetJsonAsync(
        HttpClient httpClient, Uri uri, string bearerToken, string what, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, NonSuccess(response.StatusCode, what));
            }
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (JsonDocument.Parse(body), null);
        }
        catch (Exception ex) when (IsTransportFault(ex, cancellationToken) || ex is JsonException)
        {
            logger.LogWarning(ex, "H7 root business unit link: {What} failed", what);
            return (null, Transport(what, ex));
        }
    }

    /// <summary>A transport fault or a timeout — never the caller's own cancellation, which propagates.</summary>
    private static bool IsTransportFault(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static EnvVarValuesWriteOutcome.Failure Transport(string what, Exception ex)
        => new(EnvVarValuesWriteFailureKind.UnknownInvocationFailure, RootBusinessUnitContainerColumnLabel,
            $"{what} failed: {ex.GetType().Name}: {ex.Message}");

    private static EnvVarValuesWriteOutcome.Failure NonSuccess(HttpStatusCode status, string what)
    {
        var diagnostic = $"{what} returned {(int)status} {status}." + (status == HttpStatusCode.BadRequest
            ? " A 400 here usually means businessunit.sprk_containerid does not exist yet (the Spaarke solution, H6, is not imported)."
            : string.Empty);
        var kind = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => EnvVarValuesWriteFailureKind.AuthFailure,
            (HttpStatusCode)429 => EnvVarValuesWriteFailureKind.RateLimited,
            _ => EnvVarValuesWriteFailureKind.UnknownInvocationFailure,
        };
        return new EnvVarValuesWriteOutcome.Failure(kind, RootBusinessUnitContainerColumnLabel, diagnostic);
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

/// <summary>
/// Task 227g: the outcome of <see cref="DataverseWebApiEnvVarValuesWriter.LinkRootBusinessUnitContainerAsync"/> — the
/// linked root unit, or the typed failure (exactly one is non-null).
/// </summary>
internal sealed record RootBusinessUnitLinkResult(Guid? RootBusinessUnitId, EnvVarValuesWriteOutcome.Failure? Failure);
