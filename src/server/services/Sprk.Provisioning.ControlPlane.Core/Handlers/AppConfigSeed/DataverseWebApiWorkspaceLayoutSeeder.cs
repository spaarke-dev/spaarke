// -----------------------------------------------------------------------------
// DataverseWebApiWorkspaceLayoutSeeder.cs
//
// Production IAppConfigSeeder implementation for the WorkspaceLayout scope
// (task 151, Wave G-5 Batch G-5A — H12b DV-REST port). Direct HttpClient +
// DefaultAzureCredential port of the retired
// scripts/Deploy-SystemWorkspaceLayouts.ps1's DATA HALF ONLY — for each
// system layout declared in scripts/system-layouts.json, look up an
// existing sprk_workspacelayout row by (sprk_name, sprk_issystem=true) and,
// if absent, POST a new one. If present, SKIP (parity with the script's
// DEFAULT no-`-Force` behavior — PowerShellAppConfigSeeder never passed
// -Force to the wrapped script, so this port's default MUST also skip, not
// refresh, an existing row; this is the deliberate asymmetry vs the sibling
// DataverseWebApiDataGridSeeder, which always refreshes on match because
// seed-reconciliation-gridconfig.ps1 has no -Force gate at all).
//
// SCHEMA HALF INTENTIONALLY NOT PORTED (documented decision, not an
// oversight): the source script's Add-IsSystemAttribute half creates the
// sprk_issystem Boolean column on sprk_workspacelayout via the Dataverse
// metadata Web API if it does not already exist. Task 151's POML dependency
// note states "H6 must have imported the sprk_gridconfigurations table +
// workspace-layout schema H12b seeds into" — i.e. by the time H12b runs in
// the r1 handler DAG, H6 (solution import, task 141) has already imported
// the managed solution carrying sprk_workspacelayout.sprk_issystem (the
// column was captured into the managed solution after the original script
// added it to spaarkedev1 — see the script's own file-header note: "if a
// future task adds the column through the maker portal the script no-ops").
// Porting a metadata-schema-mutation collaborator into a per-customer data
// seeder would also be a poor fit for Option D's "12 handlers are pure
// Dataverse Web API UPSERTS" characterization (design.md §4.1b H12b row —
// "~40-line mechanical ports"); a schema drift-detection gap here is a
// distinct concern from this task's scope (data seeding), consistent with
// CLAUDE.md §11 (extend, don't scope-creep). If a live-ceremony run
// discovers sprk_issystem missing on a fresh customer env, that is a H6
// solution-content gap to fix at the solution-export layer, not something
// this seeder should silently work around by re-adding metadata-mutation
// shell-out-equivalent logic.
//
// AUTH: DefaultAzureCredential pinned to the L2 UAMI — same DAG-position
// rationale as the sibling DataverseWebApiDataGridSeeder (see that file's
// header); H12b runs after H10 in design.md's handler DAG.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> implementation that upserts (create-if-missing,
/// skip-if-present) the system <c>sprk_workspacelayout</c> rows declared in
/// the embedded copy of <c>scripts/system-layouts.json</c>.
/// </summary>
public sealed class DataverseWebApiWorkspaceLayoutSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiWorkspaceLayoutSeeder";

    /// <summary>Embedded resource logical name — see Sprk.Provisioning.ControlPlane.Core.csproj task 151 &lt;EmbeddedResource&gt; block.</summary>
    internal const string EmbeddedResourceName =
        "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.SystemLayouts.system-layouts.json";

    private const string ODataVersion = "4.0";
    private const string SingleColumnTemplateId = "single-column";

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiWorkspaceLayoutSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.WorkspaceLayout;

    /// <summary>Constructs the seeder bound to a typed <see cref="HttpClient"/> (production).</summary>
    public DataverseWebApiWorkspaceLayoutSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiWorkspaceLayoutSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke the real DefaultAzureCredential chain.
    /// </summary>
    internal DataverseWebApiWorkspaceLayoutSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiWorkspaceLayoutSeeder> logger,
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
                $"workspace-layout seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        IReadOnlyList<LayoutSeedItem> layouts;
        try
        {
            layouts = ReadEmbeddedLayouts();
        }
        catch (Exception ex)
        {
            return AppConfigSeederResult.Failed(
                $"workspace-layout seed FAILED — could not read/parse embedded '{EmbeddedResourceName}': " +
                $"{ex.GetType().Name}: {ex.Message}. Verify the .csproj <EmbeddedResource> item is intact.");
        }

        if (layouts.Count == 0)
        {
            return AppConfigSeederResult.Failed(
                $"workspace-layout seed FAILED — embedded '{EmbeddedResourceName}' contains zero layouts.");
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
            _logger.LogWarning(ex, "H12b workspace-layout seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"workspace-layout seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var processed = new List<string>(layouts.Count);
        foreach (var layout in layouts)
        {
            string sectionsJson;
            try
            {
                sectionsJson = BuildSectionsJson(layout.SectionId, layout.LayoutTemplateId);
            }
            catch (NotSupportedException ex)
            {
                return AppConfigSeederResult.Failed(
                    $"workspace-layout seed FAILED for '{layout.Name}': {ex.Message} " +
                    (processed.Count > 0 ? $"({processed.Count} row(s) already processed: {string.Join(", ", processed)}.)" : string.Empty));
            }

            var (failure, outcome) = await UpsertOneAsync(envUri, token.Token, layout, sectionsJson, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                var diagnostic =
                    $"workspace-layout seed FAILED for '{layout.Name}': {failure.Diagnostic} " +
                    (processed.Count > 0
                        ? $"({processed.Count} row(s) already processed this invocation: {string.Join(", ", processed)}.) "
                        : string.Empty) +
                    "Every upsert is find-by-name+isSystem -> create-if-missing (skip if present), so a full retry is safe.";
                return AppConfigSeederResult.Failed(diagnostic, failure.Evidence);
            }
            processed.Add($"{layout.Name} ({outcome})");
        }

        var okDiagnostic = $"workspace-layout seed OK — {processed.Count} row(s) processed: {string.Join("; ", processed)}.";
        return AppConfigSeederResult.Ok(okDiagnostic, BuildEvidence(processed));
    }

    /// <summary>
    /// Upserts one system-workspace-layout row (find-by-name+isSystem, skip
    /// if found, else POST). Returns (null, "created"|"skipped (exists)") on
    /// success or (Failed, "") on a classified failure.
    /// </summary>
    private async Task<(AppConfigSeederResult? Failure, string Outcome)> UpsertOneAsync(
        Uri envUri, string bearerToken, LayoutSeedItem layout, string sectionsJson, CancellationToken cancellationToken)
    {
        var escapedName = layout.Name.Replace("'", "''", StringComparison.Ordinal);
        var filter = $"sprk_name eq '{escapedName}' and sprk_issystem eq true";
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_workspacelayouts?$filter={Uri.EscapeDataString(filter)}" +
            "&$select=sprk_workspacelayoutid,sprk_name");

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
                // Default (no -Force) behavior of the source script — skip an
                // existing record rather than refresh it (see file header).
                var existingId = values[0].GetProperty("sprk_workspacelayoutid").GetString()!;
                return (null, $"skipped (exists: {existingId})");
            }
        }

        var createBody = new Dictionary<string, object?>
        {
            ["sprk_name"] = layout.Name,
            ["sprk_layouttemplateid"] = layout.LayoutTemplateId,
            ["sprk_sectionsjson"] = sectionsJson,
            ["sprk_isdefault"] = layout.IsDefault,
            ["sprk_sortorder"] = layout.SortOrder,
            ["sprk_issystem"] = true,
        };

        var postUri = new Uri(envUri, "/api/data/v9.2/sprk_workspacelayouts");
        using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
        postRequest.Content = JsonContent.Create(createBody);
        postRequest.Headers.Add("Prefer", "return=representation");

        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            var body = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (AppConfigSeederResult.Failed(
                $"POST sprk_workspacelayouts returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. " +
                $"Body: {Truncate(body, 400)}", evidence: null), string.Empty);
        }

        var createdText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string createdId;
        try
        {
            using var createdDoc = JsonDocument.Parse(createdText);
            createdId = createdDoc.RootElement.TryGetProperty("sprk_workspacelayoutid", out var idEl)
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
    /// Builds the single-column <c>sprk_sectionsjson</c> payload — C# port of
    /// the source script's Build-SectionsJson (4 stacked rows, section placed
    /// in row-1 slot 0, rows 2-4 empty). Only <c>single-column</c> is
    /// supported (parity with the script's own hard stop on any other
    /// template id). Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static string BuildSectionsJson(string sectionId, string layoutTemplateId)
    {
        if (!string.Equals(layoutTemplateId, SingleColumnTemplateId, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Unsupported layoutTemplateId '{layoutTemplateId}'. Only 'single-column' is supported in this seed.");
        }

        var payload = new
        {
            schemaVersion = 1,
            rows = new object[]
            {
                new { id = "row-1", columns = "1fr", columnsSmall = "1fr", sections = new[] { sectionId } },
                new { id = "row-2", columns = "1fr", columnsSmall = "1fr", sections = new[] { string.Empty } },
                new { id = "row-3", columns = "1fr", columnsSmall = "1fr", sections = new[] { string.Empty } },
                new { id = "row-4", columns = "1fr", columnsSmall = "1fr", sections = new[] { string.Empty } },
            },
            scope = "my",
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Reads + parses the embedded <c>system-layouts.json</c> sidecar.
    /// Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static IReadOnlyList<LayoutSeedItem> ReadEmbeddedLayouts()
    {
        using var stream = typeof(DataverseWebApiWorkspaceLayoutSeeder).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' not found in " +
                $"{typeof(DataverseWebApiWorkspaceLayoutSeeder).Assembly.GetName().Name}.");
        }
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("layouts", out var layoutsEl) || layoutsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("system-layouts.json is missing a 'layouts' array.");
        }

        var result = new List<LayoutSeedItem>();
        foreach (var element in layoutsEl.EnumerateArray())
        {
            var name = element.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("system-layouts.json layout entry missing 'name'.");
            var sectionId = element.GetProperty("sectionId").GetString()
                ?? throw new InvalidOperationException($"system-layouts.json layout '{name}' missing 'sectionId'.");
            var layoutTemplateId = element.GetProperty("layoutTemplateId").GetString()
                ?? throw new InvalidOperationException($"system-layouts.json layout '{name}' missing 'layoutTemplateId'.");
            var sortOrder = element.GetProperty("sortOrder").GetInt32();
            var isDefault = element.TryGetProperty("isDefault", out var isDefaultEl) && isDefaultEl.GetBoolean();

            result.Add(new LayoutSeedItem(name, sectionId, layoutTemplateId, sortOrder, isDefault));
        }
        return result;
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

    /// <summary>One parsed row from <c>system-layouts.json</c>. Exposed <c>internal</c> so tests can construct fixtures directly.</summary>
    internal sealed record LayoutSeedItem(string Name, string SectionId, string LayoutTemplateId, int SortOrder, bool IsDefault);
}
