// -----------------------------------------------------------------------------
// DataverseWebApiDataGridSeeder.cs
//
// Production IAppConfigSeeder implementation for the DataGrid scope (task
// 151, Wave G-5 Batch G-5A — H12b DV-REST port). Direct HttpClient +
// DefaultAzureCredential port of the retired scripts/seed-reconciliation-
// gridconfig.ps1's `az account get-access-token` + Invoke-RestMethod
// GET/PATCH/POST sequence against sprk_gridconfigurations.
//
// SEEDED RECORDS (verbatim from the source script's 4 Seed-Config calls):
//   1. "Communication Reconciliation - Needs Review"                (fixed id ...5001)
//   2. "Communication Reconciliation - My Team's Reviews"            (no fixed id — lookup by name)
//   3. "Communication Reconciliation - Email Review All"             (fixed id ...5002)
//   4. "Communication Reconciliation - Email Review Completed"       (fixed id ...5003)
// Each row's sprk_configjson is read from the shared-lib's own JSON config
// file (embedded at build time — see Sprk.Provisioning.ControlPlane.Core.csproj
// task 151 <EmbeddedResource> block — so the seed content can never drift
// from the shared-lib source, matching the retired script's own stated
// design intent).
//
// UPSERT SEMANTICS (ported verbatim from Seed-Config):
//   GET by sprk_gridconfigurationid (when a fixed id is declared) or by
//   sprk_name (when not — the "My Team's Reviews" row). If found: PATCH ONLY
//   sprk_configjson on the existing record (so a re-seed always reflects the
//   shared-lib source of truth, exactly as the script's own comment states —
//   there is NO skip-if-exists branch for this scope, unlike the sibling
//   WorkspaceLayout seeder's default skip). If not found: POST a new record
//   with sprk_name/sprk_entitylogicalname('sprk_communication')/sprk_configjson
//   (+ sprk_gridconfigurationid when a fixed id is declared).
//
// FAIL-FAST PARITY: the source script runs under
//   `$ErrorActionPreference = 'Stop'` — any GET/PATCH/POST failure aborts the
//   whole script. This port mirrors that: the first HTTP failure returns
//   AppConfigSeederResult.Failed immediately (no partial-continue across the
//   4 rows); records already upserted earlier in the SAME invocation are
//   real, durable Dataverse writes — the caller relies on GET/PATCH/POST
//   idempotency for a safe full-scope retry (parity with H7's
//   IEnvVarValuesWriter "writer stops at the first failure... a full handler
//   retry is always safe" reasoning).
//
// AUTH: DefaultAzureCredential pinned to the L2 UAMI (ADR-028 MI-outbound),
//   NOT ClientSecretCredential — per design.md §4.1 handler DAG
//   ("H10 (app-user, needs H6 solutions) → H11 → { H12a, H12b }"), H12b
//   executes AFTER H10 has already registered the L2 UAMI as a Dataverse
//   Application User, so the UAMI-pinned credential is safe here (unlike H6/
//   H7, which run BEFORE H10 and therefore use the BFF app-reg's
//   ClientSecretCredential instead). Parity with H12c's sibling writer,
//   DataverseWebApiModelDeploymentReferenceWriter, which uses the identical
//   DefaultAzureCredential(TenantId=...) idiom for the same DAG-position
//   reason.
//
// NOT under test in the CI unit suite for the credential-acquisition path
// itself (real DefaultAzureCredential chain) — DataverseWebApiDataGridSeederTests
// injects a fake TokenCredential via the internal test-seam constructor
// (parity with DataverseWebApiSolutionImporter's Func&lt;...,TokenCredential&gt;
// seam), never Mock&lt;HttpMessageHandler&gt; (banned per ADR-038/testing.md).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> implementation that upserts the 4
/// reconciliation-grid <c>sprk_gridconfiguration</c> rows via direct
/// Dataverse Web API calls, reading each row's <c>sprk_configjson</c> from an
/// embedded copy of the shared-lib's own config JSON.
/// </summary>
public sealed class DataverseWebApiDataGridSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiDataGridSeeder";

    private const string ODataVersion = "4.0";
    private const string EntityLogicalName = "sprk_communication";

    // Fixed ids verbatim from seed-reconciliation-gridconfig.ps1 — the code
    // constants those ids exist to keep in sync with (NEEDS_REVIEW_CONFIG_ID
    // etc.) live in the ReconciliationGrid shared-lib component; a null Id
    // below means "lookup + create by name only" (the "My Team's Reviews"
    // row never had a fixed id in the source script).
    private static readonly IReadOnlyList<GridConfigSeedItem> SeedItems = new[]
    {
        new GridConfigSeedItem(
            Id: Guid.Parse("00000000-0000-4000-8000-000000005001"),
            Name: "Communication Reconciliation - Needs Review",
            EmbeddedResourceLogicalName: "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.needs-review.gridconfiguration.json"),
        new GridConfigSeedItem(
            Id: null,
            Name: "Communication Reconciliation - My Team's Reviews",
            EmbeddedResourceLogicalName: "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.per-team.gridconfiguration.json"),
        new GridConfigSeedItem(
            Id: Guid.Parse("00000000-0000-4000-8000-000000005002"),
            Name: "Communication Reconciliation - Email Review All",
            EmbeddedResourceLogicalName: "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.email-review-all.gridconfiguration.json"),
        new GridConfigSeedItem(
            Id: Guid.Parse("00000000-0000-4000-8000-000000005003"),
            Name: "Communication Reconciliation - Email Review Completed",
            EmbeddedResourceLogicalName: "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.email-review-completed.gridconfiguration.json"),
    };

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiDataGridSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.DataGrid;

    /// <summary>Constructs the seeder bound to a typed <see cref="HttpClient"/> (production — registered via a factory lambda in Worker/Program.cs, see AppConfigSeedModule).</summary>
    public DataverseWebApiDataGridSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiDataGridSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke the real DefaultAzureCredential chain.
    /// </summary>
    internal DataverseWebApiDataGridSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiDataGridSeeder> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TenantId);

        if (!Uri.TryCreate(input.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return AppConfigSeederResult.Failed(
                $"data-grid seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";
            var credential = _credentialFactory(input.TenantId);
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H12b data-grid seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"data-grid seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var processed = new List<string>(SeedItems.Count);
        foreach (var item in SeedItems)
        {
            string configJson;
            try
            {
                configJson = ReadEmbeddedConfigJson(item.EmbeddedResourceLogicalName);
            }
            catch (Exception ex)
            {
                return AppConfigSeederResult.Failed(
                    $"data-grid seed FAILED — could not read embedded config JSON for '{item.Name}' " +
                    $"(resource '{item.EmbeddedResourceLogicalName}'): {ex.GetType().Name}: {ex.Message}. " +
                    "Verify the Sprk.Provisioning.ControlPlane.Core.csproj <EmbeddedResource> item is intact.");
            }

            AppConfigSeederResult? failure;
            string outcome;
            (failure, outcome) = await UpsertOneAsync(envUri, token.Token, item, configJson, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                var diagnostic =
                    $"data-grid seed FAILED for '{item.Name}': {failure.Diagnostic} " +
                    (processed.Count > 0
                        ? $"({processed.Count} row(s) already upserted this invocation: {string.Join(", ", processed)}.) "
                        : string.Empty) +
                    "Every upsert is find-by-id-or-name -> PATCH/POST, so a full retry is safe.";
                return AppConfigSeederResult.Failed(diagnostic, failure.Evidence);
            }
            processed.Add($"{item.Name} ({outcome})");
        }

        var okDiagnostic = $"data-grid seed OK — {processed.Count} row(s) upserted: {string.Join("; ", processed)}.";
        return AppConfigSeederResult.Ok(okDiagnostic, BuildEvidence(processed));
    }

    /// <summary>
    /// Upserts one grid-config row (find-by-id-or-name, then PATCH sprk_configjson
    /// if found, else POST a new record). Returns (null, "created"|"updated") on
    /// success or (Failed, "") on a classified failure.
    /// </summary>
    private async Task<(AppConfigSeederResult? Failure, string Outcome)> UpsertOneAsync(
        Uri envUri, string bearerToken, GridConfigSeedItem item, string configJson, CancellationToken cancellationToken)
    {
        var filter = item.Id is Guid id
            ? $"sprk_gridconfigurationid eq {id:D}"
            : $"sprk_name eq '{item.Name.Replace("'", "''", StringComparison.Ordinal)}'";
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_gridconfigurations?$filter={Uri.EscapeDataString(filter)}" +
            "&$select=sprk_gridconfigurationid,sprk_name");

        JsonDocument getDoc;
        try
        {
            using var getRequest = BuildRequest(HttpMethod.Get, getUri, bearerToken);
            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (AppConfigSeederResult.Failed(
                    $"lookup GET returned {(int)getResponse.StatusCode} {getResponse.StatusCode}. Body: {Truncate(body, 400)}",
                    evidence: null), string.Empty);
            }
            var text = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            getDoc = JsonDocument.Parse(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (AppConfigSeederResult.Failed(
                $"lookup GET infrastructure error: {ex.GetType().Name}: {ex.Message}", evidence: null), string.Empty);
        }

        using (getDoc)
        {
            var values = getDoc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                var existingId = values[0].GetProperty("sprk_gridconfigurationid").GetString()!;
                var patchUri = new Uri(envUri, $"/api/data/v9.2/sprk_gridconfigurations({existingId})");
                using var patchRequest = BuildRequest(HttpMethod.Patch, patchUri, bearerToken);
                patchRequest.Content = JsonContent.Create(new { sprk_configjson = configJson });

                using var patchResponse = await _httpClient.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);
                if (!patchResponse.IsSuccessStatusCode)
                {
                    var body = await patchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return (AppConfigSeederResult.Failed(
                        $"PATCH sprk_gridconfigurations({existingId}) returned {(int)patchResponse.StatusCode} " +
                        $"{patchResponse.StatusCode}. Body: {Truncate(body, 400)}", evidence: null), string.Empty);
                }
                return (null, $"updated {existingId}");
            }
        }

        var createBody = new Dictionary<string, object?>
        {
            ["sprk_name"] = item.Name,
            ["sprk_entitylogicalname"] = EntityLogicalName,
            ["sprk_configjson"] = configJson,
        };
        if (item.Id is Guid fixedId)
        {
            createBody["sprk_gridconfigurationid"] = fixedId;
        }

        var postUri = new Uri(envUri, "/api/data/v9.2/sprk_gridconfigurations");
        using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
        postRequest.Content = JsonContent.Create(createBody);
        postRequest.Headers.Add("Prefer", "return=representation");

        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            var body = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (AppConfigSeederResult.Failed(
                $"POST sprk_gridconfigurations returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. " +
                $"Body: {Truncate(body, 400)}", evidence: null), string.Empty);
        }
        var createdText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string createdId;
        try
        {
            using var createdDoc = JsonDocument.Parse(createdText);
            createdId = createdDoc.RootElement.TryGetProperty("sprk_gridconfigurationid", out var idEl)
                ? idEl.GetString() ?? "(unknown)"
                : (item.Id?.ToString("D") ?? "(unknown)");
        }
        catch (JsonException)
        {
            createdId = item.Id?.ToString("D") ?? "(unknown)";
        }
        return (null, $"created {createdId}");
    }

    /// <summary>
    /// Reads one grid-config JSON blob from this assembly's embedded
    /// resources (see Sprk.Provisioning.ControlPlane.Core.csproj task 151
    /// &lt;EmbeddedResource&gt; block). Exposed <c>internal</c> for direct
    /// unit testing.
    /// </summary>
    internal static string ReadEmbeddedConfigJson(string logicalName)
    {
        using var stream = typeof(DataverseWebApiDataGridSeeder).Assembly.GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{logicalName}' not found in " +
                $"{typeof(DataverseWebApiDataGridSeeder).Assembly.GetName().Name}.");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonElement BuildEvidence(IReadOnlyList<string> processed)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { rows = processed }));
        return doc.RootElement.Clone();
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

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    private sealed record GridConfigSeedItem(Guid? Id, string Name, string EmbeddedResourceLogicalName);
}
