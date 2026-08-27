// -----------------------------------------------------------------------------
// DataverseEnvironmentRegistryClient.cs
//
// L2 CONTROL-PLANE real (production) IDataverseEnvironmentRegistryClient impl
// (task 112 — Wave G-1 C1.4). Path X MI-native: acquires tokens via
// `DefaultAzureCredential(ManagedIdentityClientId)` pinned to the L2 UAMI
// (registered as a Dataverse Application User on the admin env by task 111's
// Grant-ControlPlaneIdentity.ps1), scoped to `{adminEnvUrl}/.default`. NO
// ClientSecret — Path X is the whole point (DS-8).
//
// SPEC / DESIGN / ADR references:
//   - spec.md FR-38:                Path X credential model for L2 admin-env
//                                   Dataverse writes; MUST NOT provision new
//                                   client-secret for L2.
//   - spec.md FR-02:                H0.5 re-consent semantics reads via this
//                                   client (LookupByTenantIdAsync).
//   - spec.md FR-18 + §15 north-star: H13 Ready-writer PATCH lands via this
//                                   client (task 184 IRegistrySetupStatusUpdater
//                                   delegates to UpdateSetupStatusAsync).
//   - design.md §9.6:               Path X mechanics — L2 UAMI as App User on
//                                   ADMIN env only, scoped "Spaarke Provisioning
//                                   Registry" role (NOT SysAdmin).
//   - DS-8 §6 rollout step 3:       Build C1.4 MI-native from day one.
//   - ADR-028:                      MI-outbound MUST rule; never account-key
//                                   or client-secret when MI is available.
//   - ADR-010:                      Feature-module registration (single
//                                   AddDataverseEnvironmentRegistry extension).
//   - ADR-032:                      NullDataverseEnvironmentRegistryClient
//                                   stays as the Null-Object kill-switch
//                                   fallback (P2 quiet no-op with WARN log) —
//                                   this task ADDS the real impl, does not
//                                   delete the Null-Object.
//
// PATTERN PARITY (shape mirrors, purpose distinct):
//   - Token acquisition:  DataverseWebApiHealthProbe.cs (H5 WhoAmI probe)
//                         — but pinned via ManagedIdentityClientId (Path X)
//                         NOT via TenantId (Path X targets the admin env in
//                         the SPAARKE tenant, not a customer's tenant).
//   - Web API URI shape:  DataverseWebApiAppUserCreator.cs (H10 App User
//                         creator) + DataverseRegistryConcurrencyStore.cs
//                         (I5 concurrency store).
//   - Options + module:   CustomerRunGuardOptions.cs + CustomerRunGuardModule.cs
//                         (also targets admin env) + CosmosModule.cs
//                         (also uses DefaultAzureCredential + ManagedIdentity:ClientId
//                         fallback).
//
// DATAVERSE WEB API SHAPES:
//   READ (LookupByTenantIdAsync):
//     GET /api/data/v9.2/{entitySet}
//       ?$filter=sprk_tenantid eq '{tenantId-escaped}'
//       &$select=sprk_dataverseenvironmentid,sprk_customerid,sprk_tenantid,sprk_setupstatus,sprk_currentrunid
//       &$top=1
//     Response ships `sprk_setupstatus` as an INTEGER (Dataverse choice
//     option-set value). ParseSnapshot maps the integer back to the display-
//     name string the higher-level SetupStatus contract exposes (H0.5's
//     NoOpStatuses / RestartStatuses HashSets compare against the display-
//     name form).
//
//   WRITE (UpdateSetupStatusAsync):
//     PATCH /api/data/v9.2/{entitySet}({environmentId})
//       Body:
//         { "sprk_setupstatus": 2, "sprk_currentrunid": null }
//         — sprk_setupstatus is a Dataverse CHOICE (option-set) and MUST be
//           sent as an INTEGER, NOT a string. Sending "Ready" as a JSON
//           string yields a 400 "Property 'sprk_setupstatus' is not a valid
//           column" — the exact silent-fail that gated task 184's ability
//           to actually land Ready. Mapping table (verified via Dataverse
//           MCP describe against admin env spaarkedev1, 2026-08-20):
//              NotStarted  = 0
//              InProgress  = 1
//              Ready       = 2   (H13 green-path terminal value)
//              Issue       = 3
//           BuildPatchBody accepts the display-name string (preserves the
//           RegistrySetupStatusUpdate contract H0.5 shares) and maps to the
//           option-set integer on the wire; unknown display names throw
//           (fail-loud rather than silently PATCH garbage).
//         — the sprk_currentrunid property is INCLUDED-AS-NULL only when
//           `ClearCurrentRunId=true` (H13 green-path). Otherwise omitted.
//       Prefer: return=minimal
//
// NULL-COLUMN PROJECTION (parity with DataverseRegistryConcurrencyStore):
//   Dataverse OData REST ships a projected null column back as ABSENT from
//   the response payload (not present as `"sprk_currentrunid": null`).
//   `LookupByTenantIdAsync` handles this by defaulting to null when the
//   property is missing from the row.
//
// SECURITY (defense-in-depth):
//   - tenantId escapes single quotes (Dataverse doubles them) + URL-encodes
//     the value. tenantIds are machine-produced GUIDs in practice, but the
//     escape is cheap insurance against a future free-text customer identifier.
//   - environmentId MUST parse as a well-formed GUID; the impl rejects
//     non-GUID input BEFORE building the OData URI (parity with H10's
//     DataverseWebApiAppUserCreator applicationId guard).
//
// NOT under test in the CI unit suite (real HTTP + real MI token). Unit tests
// cover the shape logic where it can be exercised as pure functions; the
// live-invocation seam test (env-guarded, parity with CosmosSmokeTests +
// ServiceBusSmokeTests) lives at DataverseEnvironmentRegistryClientTests.cs
// and requires the L2 UAMI to be registered as an admin-env App User (task
// 111's grant script must have run successfully) plus operator-set env vars
// per that test file's header.
//
// ADR-038 KEEP category: `tests/integration/seam/**` — vertical-slice-seam
// tests exercising the real credential + real HTTP path against a canary row.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Real (production) <see cref="IDataverseEnvironmentRegistryClient"/> — Path X
/// MI-native impl targeting the admin Dataverse env's
/// <c>sprk_dataverseenvironment</c> entity set. See file header for the full
/// pattern-parity ledger and DS-8 rollout note.
/// </summary>
public sealed class DataverseEnvironmentRegistryClient : IDataverseEnvironmentRegistryClient
{
    /// <summary>Named HttpClient for this client's outbound Dataverse calls.</summary>
    public const string HttpClientName = "DataverseEnvironmentRegistryClient";

    private const string ODataVersion = "4.0";
    private const string EnvironmentRowIdColumn = "sprk_dataverseenvironmentid";
    private const string CustomerIdColumn = "sprk_customerid";
    private const string TenantIdColumn = "sprk_tenantid";
    private const string SetupStatusColumn = "sprk_setupstatus";
    private const string CurrentRunIdColumn = "sprk_currentrunid";

    // Row A38a (task 205a, 2026-08-25): positive secret-free migration marker
    // state field. SINGLE-LINE-OF-TEXT column (written as a JSON string —
    // unlike sprk_setupstatus's option-set integer). Schema prerequisite:
    // column must exist on the admin env's sprk_dataverseenvironment table
    // BEFORE any environment enables RequireSecretFreeIdentity; a missing
    // column FAIL-LOUDs here as an HTTP 400 Failure naming the property.
    private const string CredentialModeColumn = "sprk_credentialmode";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DataverseEnvironmentRegistryOptions _options;
    private readonly ILogger<DataverseEnvironmentRegistryClient> _logger;

    /// <summary>Constructs the client bound to the named HttpClient + configured options.</summary>
    public DataverseEnvironmentRegistryClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DataverseEnvironmentRegistryOptions> options,
        ILogger<DataverseEnvironmentRegistryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var envUri = BuildEnvUri();
        var token = await AcquireTokenAsync(envUri, cancellationToken).ConfigureAwait(false);

        // Escape single quotes (Dataverse doubles them in string literals) and
        // then URL-encode. tenantId is a GUID in every observed case, but the
        // escape is cheap defense-in-depth against a future non-GUID column.
        var filterValue = tenantId.Replace("'", "''", StringComparison.Ordinal);
        var relative =
            $"/api/data/v9.2/{_options.EntitySetName}?" +
            $"$filter={TenantIdColumn} eq '{Uri.EscapeDataString(filterValue)}'" +
            $"&$select={EnvironmentRowIdColumn},{CustomerIdColumn},{TenantIdColumn}," +
            $"{SetupStatusColumn},{CurrentRunIdColumn}" +
            "&$top=1";
        var requestUri = new Uri(envUri, relative);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        HttpResponseMessage response;
        try
        {
            using var request = BuildRequest(HttpMethod.Get, requestUri, token.Token);
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient lookup infrastructure fault for tenantIdHash={TenantIdHash}",
                HashForLog(tenantId));
            throw new InvalidOperationException(
                $"Registry lookup infrastructure error: {ex.GetType().Name}: {ex.Message}", ex);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Registry lookup returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            if (!doc.RootElement.TryGetProperty("value", out var arr)
                || arr.ValueKind != JsonValueKind.Array
                || arr.GetArrayLength() == 0)
            {
                return null;
            }

            return ParseSnapshot(arr[0]);
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
        RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.SetupStatus);

        // Guard: environmentId MUST parse as a GUID. Interpolated into an
        // OData URI (segment, not filter literal) — parity with H10's
        // applicationId guard on DataverseWebApiAppUserCreator.
        if (!Guid.TryParse(update.EnvironmentId, out var envRowId))
        {
            return new RegistryUpdateOutcome.Failure(
                $"EnvironmentId '{update.EnvironmentId}' is not a valid GUID — refusing to build an OData URI from it.");
        }

        var envUri = BuildEnvUri();

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient token acquisition failed for env={EnvUrl}",
                envUri);
            return new RegistryUpdateOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var relative = $"/api/data/v9.2/{_options.EntitySetName}({envRowId})";
        var requestUri = new Uri(envUri, relative);
        var bodyJson = BuildPatchBody(update.SetupStatus, update.ClearCurrentRunId);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        try
        {
            using var request = BuildRequest(HttpMethod.Patch, requestUri, token.Token);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "DataverseEnvironmentRegistryClient PATCH ok: environmentId={EnvironmentId} setupStatus={SetupStatus} " +
                    "clearCurrentRunId={ClearCurrentRunId} customerId={CustomerId} runId={RunId}",
                    envRowId, update.SetupStatus, update.ClearCurrentRunId,
                    update.CustomerIdForLog, update.RunIdForLog);
                return new RegistryUpdateOutcome.Success();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                return new RegistryUpdateOutcome.NotFound(
                    $"PATCH {relative} returned 404 NotFound. Body: {Truncate(body, 400)}");
            }

            var errBody = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH {relative} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(errBody, 400)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient PATCH infrastructure fault for environmentId={EnvironmentId}",
                envRowId);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<RegistryUpdateOutcome> UpdateCredentialModeAsync(
        RegistryCredentialModeUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.CredentialMode);

        // GUID guard — parity with UpdateSetupStatusAsync (the id is
        // interpolated into an OData URI segment).
        if (!Guid.TryParse(update.EnvironmentId, out var envRowId))
        {
            return new RegistryUpdateOutcome.Failure(
                $"EnvironmentId '{update.EnvironmentId}' is not a valid GUID — refusing to build an OData URI from it.");
        }

        var envUri = BuildEnvUri();

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient credential-mode token acquisition failed for env={EnvUrl}",
                envUri);
            return new RegistryUpdateOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var relative = $"/api/data/v9.2/{_options.EntitySetName}({envRowId})";
        var requestUri = new Uri(envUri, relative);
        var bodyJson = BuildCredentialModePatchBody(update.CredentialMode);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        try
        {
            using var request = BuildRequest(HttpMethod.Patch, requestUri, token.Token);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "DataverseEnvironmentRegistryClient credential-mode PATCH ok: environmentId={EnvironmentId} " +
                    "credentialMode={CredentialMode} customerId={CustomerId} runId={RunId}",
                    envRowId, update.CredentialMode, update.CustomerIdForLog, update.RunIdForLog);
                return new RegistryUpdateOutcome.Success();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                return new RegistryUpdateOutcome.NotFound(
                    $"PATCH {relative} returned 404 NotFound. Body: {Truncate(body, 400)}");
            }

            var errBody = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH {relative} ({CredentialModeColumn}) returned {(int)response.StatusCode} {response.StatusCode}. " +
                $"Body: {Truncate(errBody, 400)}. If the body names '{CredentialModeColumn}' as an invalid " +
                "property, the A38a schema prerequisite (single-line-of-text column on " +
                "sprk_dataverseenvironment) has not been created on the admin env yet.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient credential-mode PATCH infrastructure fault for environmentId={EnvironmentId}",
                envRowId);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<RegistryUpdateOutcome> UpdateColumnsAsync(
        string environmentId,
        IReadOnlyDictionary<string, object?> columns,
        string customerIdForLog,
        string runIdForLog,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentNullException.ThrowIfNull(columns);

        // Empty dictionary → no-op Success. Caller decided nothing was worth
        // writing; do NOT issue an empty-body PATCH (Dataverse would reject as
        // 400 "at least one property required") — the caller's intent is clearer.
        if (columns.Count == 0)
        {
            _logger.LogDebug(
                "DataverseEnvironmentRegistryClient.UpdateColumnsAsync no-op (empty column set): " +
                "environmentId={EnvironmentId} customerId={CustomerId} runId={RunId}",
                environmentId, customerIdForLog, runIdForLog);
            return new RegistryUpdateOutcome.Success();
        }

        // GUID guard — parity with UpdateSetupStatusAsync / UpdateCredentialModeAsync.
        if (!Guid.TryParse(environmentId, out var envRowId))
        {
            return new RegistryUpdateOutcome.Failure(
                $"EnvironmentId '{environmentId}' is not a valid GUID — refusing to build an OData URI from it.");
        }

        var envUri = BuildEnvUri();

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient columns-PATCH token acquisition failed for env={EnvUrl}",
                envUri);
            return new RegistryUpdateOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var relative = $"/api/data/v9.2/{_options.EntitySetName}({envRowId})";
        var requestUri = new Uri(envUri, relative);
        var bodyJson = BuildColumnsPatchBody(columns);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;

        try
        {
            using var request = BuildRequest(HttpMethod.Patch, requestUri, token.Token);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "DataverseEnvironmentRegistryClient columns-PATCH ok: environmentId={EnvironmentId} " +
                    "columnCount={ColumnCount} columnNames={ColumnNames} customerId={CustomerId} runId={RunId}",
                    envRowId, columns.Count, string.Join(",", columns.Keys),
                    customerIdForLog, runIdForLog);
                return new RegistryUpdateOutcome.Success();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                return new RegistryUpdateOutcome.NotFound(
                    $"PATCH {relative} returned 404 NotFound. Body: {Truncate(body, 400)}");
            }

            var errBody = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH {relative} ({columns.Count} columns: {string.Join(",", columns.Keys)}) " +
                $"returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(errBody, 400)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "DataverseEnvironmentRegistryClient columns-PATCH infrastructure fault for environmentId={EnvironmentId}",
                envRowId);
            return new RegistryUpdateOutcome.Failure(
                $"PATCH infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    // REG-01 — arbitrary-columns PATCH body. Column NAMES are the dictionary
    // keys (must be Dataverse lowercase logical names). VALUES are serialized
    // per JSON type: strings → strings, DateTimeOffset → ISO 8601 UTC,
    // bool → JSON bool, integer types → JSON number, null → JSON null (clears
    // the column). Internal for pure-function test coverage.
    internal static string BuildColumnsPatchBody(IReadOnlyDictionary<string, object?> columns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in columns)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        "UpdateColumnsAsync received an entry with an empty column name.");
                }
                switch (value)
                {
                    case null:
                        writer.WriteNull(name);
                        break;
                    case string s:
                        writer.WriteString(name, s);
                        break;
                    case bool b:
                        writer.WriteBoolean(name, b);
                        break;
                    case DateTimeOffset dto:
                        // ISO 8601 UTC (round-trip) — Dataverse DateTime columns
                        // accept the format via OData v4.
                        writer.WriteString(name, dto.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case DateTime dt:
                        writer.WriteString(name, dt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case int i:
                        writer.WriteNumber(name, i);
                        break;
                    case long l:
                        writer.WriteNumber(name, l);
                        break;
                    case double d:
                        writer.WriteNumber(name, d);
                        break;
                    case decimal dec:
                        writer.WriteNumber(name, dec);
                        break;
                    case Guid g:
                        // Dataverse string-column columns holding a GUID want
                        // canonical bare-lowercase (ADR-044). Callers can
                        // pass a string if they need braces.
                        writer.WriteString(name, g.ToString("D").ToLowerInvariant());
                        break;
                    default:
                        // Fallback: use ToString() invariant. Callers should
                        // convert to a supported primitive before calling
                        // (this branch keeps a clear FAIL surface — a "System.Object"
                        // string in the row indicates the caller passed
                        // something unexpected).
                        writer.WriteString(name, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private Uri BuildEnvUri()
    {
        var raw = _options.AdminEnvironmentUrl;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Options.Validate() is called at boot; reaching this is only
            // possible when a test bypasses IOptions binding.
            throw new InvalidOperationException(
                $"'{DataverseEnvironmentRegistryOptions.SectionName}:AdminEnvironmentUrl' is not configured.");
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{DataverseEnvironmentRegistryOptions.SectionName}:AdminEnvironmentUrl' " +
                $"'{raw}' is not a valid absolute URI.");
        }
        return parsed;
    }

    // Path X token acquisition: DefaultAzureCredential pinned via
    // ManagedIdentityClientId (the L2 UAMI). NO TenantId set — the L2 UAMI
    // and the admin Dataverse env both live in the SPAARKE platform tenant,
    // so token issuance targets the UAMI's own tenant intrinsically.
    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, CancellationToken cancellationToken)
    {
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId))
        {
            credOptions.ManagedIdentityClientId = _options.ManagedIdentityClientId;
        }
        var credential = new DefaultAzureCredential(credOptions);
        return await credential.GetTokenAsync(
            new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
    }

    // Public for test coverage of the request-shape builder as a pure function
    // (ADR-038 KEEP: pure-function tests avoid HttpMessageHandler mocks).
    //
    // WIRE SHAPE (task 184 correctness fix, 2026-08-20): sprk_setupstatus is
    // a Dataverse CHOICE (option-set integer) — writing it as a JSON string
    // yields 400 from the Web API. BuildPatchBody accepts the display-name
    // string form (preserves RegistrySetupStatusUpdate.SetupStatus contract
    // shared with H0.5) and maps to the option-set integer here. Unknown
    // display names throw so a caller cannot silently PATCH garbage. See
    // MapDisplayNameToOptionSet for the verified integer mapping.
    internal static string BuildPatchBody(string setupStatus, bool clearCurrentRunId)
    {
        var optionSetValue = MapDisplayNameToOptionSet(setupStatus);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber(SetupStatusColumn, optionSetValue);
            if (clearCurrentRunId)
            {
                writer.WriteNull(CurrentRunIdColumn);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Row A38a — credential-mode PATCH body. UNLIKE sprk_setupstatus (choice/
    // option-set integer), sprk_credentialmode is a single-line-of-text
    // column, so the value ships as a JSON STRING verbatim. Internal for
    // pure-function test coverage (ADR-038 posture — no HttpMessageHandler
    // mocks).
    internal static string BuildCredentialModePatchBody(string credentialMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialMode);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(CredentialModeColumn, credentialMode);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Extracts a snapshot from a row JsonElement. Null-column projection
    // convention: missing property == null value.
    //
    // sprk_setupstatus is a Dataverse CHOICE (option-set integer) — the raw
    // Web API response ships it as a JSON number. The higher-level SetupStatus
    // contract (H0.5's NoOpStatuses / RestartStatuses HashSets) compares
    // against the display-name string form, so ParseSnapshot maps the option-
    // set integer back to the display name for continuity with H0.5's existing
    // decision logic. Unknown option-set values map to the raw integer as a
    // string (e.g. "42") — surfaces a "unmapped status" branch in H0.5 rather
    // than silently coercing to a known value.
    internal static DataverseEnvironmentRegistrySnapshot ParseSnapshot(JsonElement row)
    {
        var environmentId = ReadRequiredString(row, EnvironmentRowIdColumn);
        var customerId = ReadRequiredString(row, CustomerIdColumn);
        var tenantId = ReadRequiredString(row, TenantIdColumn);
        var setupStatus = ReadSetupStatusAsDisplayName(row) ?? string.Empty;
        var currentRunId = ReadOptionalString(row, CurrentRunIdColumn);
        return new DataverseEnvironmentRegistrySnapshot(
            environmentId, customerId, tenantId, setupStatus, currentRunId);
    }

    // Maps a display-name string (as used by RegistrySetupStatusUpdate.SetupStatus
    // and H0.5's NoOpStatuses / RestartStatuses) to the Dataverse choice
    // option-set integer for sprk_setupstatus. Verified against admin env
    // sprk_dataverseenvironment schema via Dataverse MCP describe (2026-08-20):
    //   NotStarted = 0, InProgress = 1, Ready = 2, Issue = 3.
    // Case-insensitive to survive minor differences in caller casing (H0.5
    // uses OrdinalIgnoreCase). Throws for unknown display names — task 184
    // silent-fail guard: PATCHing an unknown-integer would silently corrupt
    // the row (Dataverse accepts arbitrary integers on a choice write and
    // returns 204 No Content).
    internal static int MapDisplayNameToOptionSet(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (string.Equals(displayName, "NotStarted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, "Not Started", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(displayName, "InProgress", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, "In Progress", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(displayName, "Ready", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(displayName, "Issue", StringComparison.OrdinalIgnoreCase))
            return 3;
        throw new InvalidOperationException(
            $"Unknown sprk_setupstatus display name '{displayName}'. " +
            "Dataverse choice supports: NotStarted (0), InProgress (1), Ready (2), Issue (3). " +
            "Update DataverseEnvironmentRegistryClient.MapDisplayNameToOptionSet if the schema changes.");
    }

    // Reverse of MapDisplayNameToOptionSet — maps the Dataverse choice option-
    // set integer back to a display-name string for callers comparing against
    // the display-name form (H0.5). Absent property yields null (H0.5 first-
    // consent path); unknown integer yields the integer as a string (surfaces
    // the "unmapped status" WARN branch rather than silently coercing).
    private static string? ReadSetupStatusAsDisplayName(JsonElement row)
    {
        if (!row.TryGetProperty(SetupStatusColumn, out var prop))
        {
            return null;
        }
        // Dataverse ships choice as a JSON number. If a payload variant ever
        // ships it as a string (SDK-shaped stub, hand-crafted response), fall
        // through to the string reader.
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var raw))
        {
            return raw switch
            {
                0 => "NotStarted",
                1 => "InProgress",
                2 => "Ready",
                3 => "Issue",
                _ => raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        if (prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    private static string ReadRequiredString(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Registry row payload missing required string property '{column}'.");
        }
        var value = prop.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Registry row payload carried an empty '{column}'.");
        }
        return value;
    }

    private static string? ReadOptionalString(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return "(body unavailable)";
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    // First 8 hex chars of SHA256(value) — parity with DataverseWebApiHealthProbe's
    // hash-for-log helper so log lines correlate without exposing tenantId.
    private static string HashForLog(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).AsSpan(0, 8).ToString();
    }
}
