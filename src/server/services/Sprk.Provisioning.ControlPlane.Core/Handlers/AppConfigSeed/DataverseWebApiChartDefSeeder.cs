// -----------------------------------------------------------------------------
// DataverseWebApiChartDefSeeder.cs
//
// Production IAppConfigSeeder implementation for the ChartDefinition scope
// (task 152, Wave G-5 Batch G-5B — H12b GREENFIELD seeder, not a port).
// Replaces the DeferredAppConfigSeeder no-op that previously covered this
// scope (task 004 §4b row 13 + §5b N5 delta) with a real Dataverse Web API
// upsert of the "Upcoming To Dos" sprk_chartdefinition family.
//
// SEED CONTENT SOURCE + SCOPE DECISION (per the POML's escalation trigger —
// "if the default content is NOT clearly specified anywhere ... STOP and
// escalate"): live spaarkedev1 carries ~19 sprk_chartdefinition records
// (verified via Dataverse MCP read_query against sprk_chartdefinition), but
// only ONE family has a checked-in repo source of truth AND a proven
// idempotent seed mechanism:
//   infrastructure/dataverse/charts/upcoming-todos-{matter,project,invoice,
//   workassignment}.json (4 files, "record" wrapper = literal Web API
//   payload) + scripts/Create-UpcomingTodosChartDefinitions.ps1 (idempotent,
//   keyed on sprk_name, PATCH-if-found/POST-if-not) — authored + proved live
//   under smart-todo-r4 task 080-G, spec FR-31 through FR-36. This is the
//   "default chart-definition rows every new customer needs" this scope
//   escalation-checks for: it IS clearly specified (checked-in JSON + a
//   real script + explicit FR citations), so no escalation was warranted.
//
//   The OTHER ~15 live records (MATTER HEALTH, MATTER BUDGET, TASKS &
//   EVENTS, Matter/Project KPI Scorecards, etc. — verified live but NOT
//   reproduced here) have NO JSON mirror or seed script anywhere in the
//   repo — this is precisely the gap DS-4 §3's "Per-family scripts exist
//   but a consolidated mirror does NOT" framing describes. Reproducing
//   their full sprk_fetchxmlquery/sprk_optionsjson content from a live
//   SELECT with no repo-checked-in source of truth would be exactly the
//   "inventing default seed content that could be wrong for every future
//   customer" the escalation trigger warns against — those 2 other JSON
//   files under infrastructure/dataverse/charts/ (budget-utilization-gauge,
//   monthly-spend-timeline) were inspected and rejected as a source: they
//   use an unrelated abstract planning-doc schema (financial-intelligence-
//   module-r1 task 042) with no sprk_-prefixed fields and no consuming
//   script — not a deployable Web API payload. INTENTIONALLY OUT OF SCOPE
//   for this task; recommended as a Wave-C5-style follow-on (live-export the
//   other 15 records into repo-checked-in JSON mirrors first, THEN extend
//   this seeder's SeedItems list — same idiom, new entries).
//
// UPSERT SEMANTICS (ported verbatim from Create-UpcomingTodosChartDefinitions.ps1):
//   find-by-sprk_name -> PATCH the 5 contract fields if found (always
//   refresh, parity with the sibling DataverseWebApiDataGridSeeder's
//   always-refresh semantics — these are shared-lib-fed feature config, not
//   admin-authored customization surface like field-mapping profiles) else
//   POST a new record.
//
// FAIL-FAST PARITY: matches the sibling task 151 seeders — the first HTTP
// failure returns AppConfigSeederResult.Failed immediately; records already
// upserted earlier in the SAME invocation are real, durable writes, and a
// full-scope retry is safe (find-by-name -> PATCH/POST is idempotent).
//
// AUTH: DefaultAzureCredential pinned to the L2 UAMI — same DAG-position
// rationale as the sibling task 151/152 seeders (H12b runs after H10).
//
// NOT under test in the CI unit suite for the credential-acquisition path
// itself — DataverseWebApiChartDefSeederTests injects a fake TokenCredential
// via the internal test-seam constructor, never Mock&lt;HttpMessageHandler&gt;
// (banned per ADR-038/testing.md).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> implementation that upserts the 4 "Upcoming
/// To Dos" <c>sprk_chartdefinition</c> rows (Matter, Project, Invoice, Work
/// Assignment main-form cards) via direct Dataverse Web API calls, reading
/// each row from an embedded copy of the shared, checked-in JSON payload
/// files under <c>infrastructure/dataverse/charts/</c>.
/// </summary>
public sealed class DataverseWebApiChartDefSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiChartDefSeeder";

    private const string ODataVersion = "4.0";

    /// <summary>
    /// Embedded resource logical names for the 4 "Upcoming To Dos" chart-def
    /// JSON payloads (see Sprk.Provisioning.ControlPlane.Core.csproj task 152
    /// &lt;EmbeddedResource&gt; block). Exposed <c>internal</c> for direct
    /// unit testing.
    /// </summary>
    internal static readonly IReadOnlyList<string> EmbeddedResourceLogicalNames = new[]
    {
        "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-matter.json",
        "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-project.json",
        "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-invoice.json",
        "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-workassignment.json",
    };

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiChartDefSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.ChartDefinition;

    /// <summary>Constructs the seeder bound to a typed <see cref="HttpClient"/> (production).</summary>
    public DataverseWebApiChartDefSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiChartDefSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke the real DefaultAzureCredential chain.
    /// </summary>
    internal DataverseWebApiChartDefSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiChartDefSeeder> logger,
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
                $"chart-def seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
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
            _logger.LogWarning(ex, "H12b chart-def seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"chart-def seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var processed = new List<string>(EmbeddedResourceLogicalNames.Count);
        foreach (var logicalName in EmbeddedResourceLogicalNames)
        {
            ChartDefSeedItem item;
            try
            {
                item = ReadEmbeddedChartDef(logicalName);
            }
            catch (Exception ex)
            {
                return AppConfigSeederResult.Failed(
                    $"chart-def seed FAILED — could not read/parse embedded resource '{logicalName}': " +
                    $"{ex.GetType().Name}: {ex.Message}. Verify the .csproj <EmbeddedResource> item is intact.");
            }

            var (failure, outcome) = await UpsertOneAsync(envUri, token.Token, item, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                var diagnostic =
                    $"chart-def seed FAILED for '{item.Name}': {failure.Diagnostic} " +
                    (processed.Count > 0
                        ? $"({processed.Count} row(s) already upserted this invocation: {string.Join(", ", processed)}.) "
                        : string.Empty) +
                    "Every upsert is find-by-name -> PATCH/POST, so a full retry is safe.";
                return AppConfigSeederResult.Failed(diagnostic, failure.Evidence);
            }
            processed.Add($"{item.Name} ({outcome})");
        }

        var okDiagnostic = $"chart-def seed OK — {processed.Count} row(s) upserted: {string.Join("; ", processed)}.";
        return AppConfigSeederResult.Ok(okDiagnostic, BuildEvidence(processed));
    }

    /// <summary>
    /// Upserts one chart-definition row (find-by-sprk_name, then PATCH the 5
    /// contract fields if found, else POST a new record). Returns (null,
    /// "created"|"updated") on success or (Failed, "") on a classified failure.
    /// </summary>
    private async Task<(AppConfigSeederResult? Failure, string Outcome)> UpsertOneAsync(
        Uri envUri, string bearerToken, ChartDefSeedItem item, CancellationToken cancellationToken)
    {
        var escapedName = item.Name.Replace("'", "''", StringComparison.Ordinal);
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_chartdefinitions?$filter={Uri.EscapeDataString($"sprk_name eq '{escapedName}'")}" +
            "&$select=sprk_chartdefinitionid,sprk_name");

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

        var contractBody = BuildContractBody(item);

        using (getDoc)
        {
            var values = getDoc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                var existingId = values[0].GetProperty("sprk_chartdefinitionid").GetString()!;
                var patchUri = new Uri(envUri, $"/api/data/v9.2/sprk_chartdefinitions({existingId})");
                using var patchRequest = BuildRequest(HttpMethod.Patch, patchUri, bearerToken);
                patchRequest.Content = JsonContent.Create(contractBody);

                using var patchResponse = await _httpClient.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);
                if (!patchResponse.IsSuccessStatusCode)
                {
                    var body = await patchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return (AppConfigSeederResult.Failed(
                        $"PATCH sprk_chartdefinitions({existingId}) returned {(int)patchResponse.StatusCode} " +
                        $"{patchResponse.StatusCode}. Body: {Truncate(body, 400)}", evidence: null), string.Empty);
                }
                return (null, $"updated {existingId}");
            }
        }

        var postUri = new Uri(envUri, "/api/data/v9.2/sprk_chartdefinitions");
        using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
        postRequest.Content = JsonContent.Create(contractBody);
        postRequest.Headers.Add("Prefer", "return=representation");

        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            var body = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (AppConfigSeederResult.Failed(
                $"POST sprk_chartdefinitions returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. " +
                $"Body: {Truncate(body, 400)}", evidence: null), string.Empty);
        }

        var createdText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string createdId;
        try
        {
            using var createdDoc = JsonDocument.Parse(createdText);
            createdId = createdDoc.RootElement.TryGetProperty("sprk_chartdefinitionid", out var idEl)
                ? idEl.GetString() ?? "(unknown)"
                : "(unknown)";
        }
        catch (JsonException)
        {
            createdId = "(unknown)";
        }
        return (null, $"created {createdId}");
    }

    /// <summary>
    /// Builds the 5 contract fields (sprk_name/sprk_entitylogicalname/
    /// sprk_contextfieldname/sprk_drillthroughtarget/sprk_visualtype/
    /// sprk_fetchxmlquery) sent on both PATCH and POST — mirrors
    /// Create-UpcomingTodosChartDefinitions.ps1's Get-RecordPayload, which
    /// strips the JSON file's metadata wrapper and sends only the "record"
    /// object's own fields.
    /// </summary>
    private static Dictionary<string, object?> BuildContractBody(ChartDefSeedItem item) => new()
    {
        ["sprk_name"] = item.Name,
        ["sprk_entitylogicalname"] = item.EntityLogicalName,
        ["sprk_contextfieldname"] = item.ContextFieldName,
        ["sprk_drillthroughtarget"] = item.DrillThroughTarget,
        ["sprk_visualtype"] = item.VisualType,
        ["sprk_fetchxmlquery"] = item.FetchXmlQuery,
    };

    /// <summary>
    /// Reads + parses one embedded chart-def JSON payload, extracting the
    /// "record" object per the checked-in file's own shape (see
    /// infrastructure/dataverse/charts/upcoming-todos-*.json). Exposed
    /// <c>internal</c> for direct unit testing.
    /// </summary>
    internal static ChartDefSeedItem ReadEmbeddedChartDef(string logicalName)
    {
        using var stream = typeof(DataverseWebApiChartDefSeeder).Assembly.GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{logicalName}' not found in " +
                $"{typeof(DataverseWebApiChartDefSeeder).Assembly.GetName().Name}.");
        }
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("record", out var record))
        {
            throw new InvalidOperationException($"'{logicalName}' is missing the 'record' property.");
        }

        var name = record.GetProperty("sprk_name").GetString()
            ?? throw new InvalidOperationException($"'{logicalName}' record missing 'sprk_name'.");
        var entityLogicalName = record.GetProperty("sprk_entitylogicalname").GetString()
            ?? throw new InvalidOperationException($"'{logicalName}' record missing 'sprk_entitylogicalname'.");
        var contextFieldName = record.TryGetProperty("sprk_contextfieldname", out var ctxEl) ? ctxEl.GetString() : null;
        var drillThroughTarget = record.TryGetProperty("sprk_drillthroughtarget", out var drillEl) ? drillEl.GetString() : null;
        var visualType = record.GetProperty("sprk_visualtype").GetInt32();
        var fetchXmlQuery = record.TryGetProperty("sprk_fetchxmlquery", out var fetchEl) ? fetchEl.GetString() : null;

        return new ChartDefSeedItem(name, entityLogicalName, contextFieldName, drillThroughTarget, visualType, fetchXmlQuery);
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

    /// <summary>One parsed chart-def seed row. Exposed <c>internal</c> so tests can construct fixtures directly.</summary>
    internal sealed record ChartDefSeedItem(
        string Name,
        string EntityLogicalName,
        string? ContextFieldName,
        string? DrillThroughTarget,
        int VisualType,
        string? FetchXmlQuery);
}
