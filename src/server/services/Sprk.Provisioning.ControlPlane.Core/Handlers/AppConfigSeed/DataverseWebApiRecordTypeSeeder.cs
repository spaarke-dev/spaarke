// -----------------------------------------------------------------------------
// DataverseWebApiRecordTypeSeeder.cs
//
// H12b seeder for the record-type registry (sprk_recordtype_ref) — T186 L54.
//
// WHY: sprk_recordtype_ref is platform reference data, not customer data. The
// ADR-024 regarding resolvers, sprk_signal.sprk_regardingrecordtype and every
// field-mapping profile (sprk_SourceRecordType / sprk_TargetRecordType) bind to
// its rows. The table ships in SpaarkeMaster but its rows did not: in dev they
// were added by hand (Deploy-SprkTodoEntity.ps1 and friends, Report Card during
// field-mapping's first seeding). A new stamp therefore had an empty table, and
// H12b's field-mapping seeder failed on its first profile (live T186 run
// b6941254, 2026-10-16: "no sprk_recordtype_ref row exists for logical name
// 'sprk_matter'").
//
// SOURCE OF TRUTH: SeedRecordTypes below — the 14 Active rows read from
// spaarkedev1 on 2026-10-16 (Dataverse MCP read_query over every column). No
// code anywhere resolves these rows by GUID (checked: only a client unit-test
// fixture carries one), so each environment keeps the ids Dataverse gives it.
//
// UPSERT SEMANTICS: find by sprk_recordlogicalname -> create if missing, skip if
// present (parity with the field-mapping and workspace-layout seeders). An
// existing row is never PATCHed: a customer may have edited display names.
//
// ORDER: registered FIRST in AppConfigSeedModule so the rows exist before the
// field-mapping seeder resolves them.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> that creates the platform's
/// <c>sprk_recordtype_ref</c> rows (create-if-missing, skip-if-present) via the
/// Dataverse Web API.
/// </summary>
public sealed class DataverseWebApiRecordTypeSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiRecordTypeSeeder";

    private const string ODataVersion = "4.0";
    private const string EntitySet = "sprk_recordtype_refs";

    /// <summary>
    /// The record types every Spaarke environment needs, mirrored from spaarkedev1
    /// (2026-10-16). Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static readonly IReadOnlyList<RecordTypeSeedItem> SeedRecordTypes = new[]
    {
        new RecordTypeSeedItem("account", "Account", "Account", "ACCT", "name", "sprk_regardingaccount", "accountnumber"),
        new RecordTypeSeedItem("contact", "Person", "Person", "PERS", "fullname", "sprk_regardingcontact", null),
        new RecordTypeSeedItem("sprk_analysis", "Analysis", "Analysis", "ANA", "sprk_analysisname", "sprk_regardinganalysis", "sprk_analysis_number"),
        new RecordTypeSeedItem("sprk_budget", "Budget", "Budget", "BDGT", "sprk_budgetname", "sprk_regardingbudget", "sprk_budgetnumber"),
        new RecordTypeSeedItem("sprk_document", "Document", "Document", "DOC", "sprk_documentname", "sprk_regardingdocument", "sprk_document_number"),
        new RecordTypeSeedItem("sprk_event", "Event", "Event", "EVT", "sprk_eventname", "sprk_regardingevent", "sprk_eventnumber"),
        new RecordTypeSeedItem("sprk_invoice", "Invoice", "Invoice", "INV", "sprk_invoicename", "sprk_regardinginvoice", "sprk_invoicenumber"),
        new RecordTypeSeedItem("sprk_matter", "Matter", "Matter", "MTR", "sprk_mattername", "sprk_regardingmatter", "sprk_matternumber"),
        new RecordTypeSeedItem("sprk_organization", "Organization", "Organization", "ORG", "sprk_organizationname", "sprk_regardingorganization", "sprk_organization_number"),
        new RecordTypeSeedItem("sprk_project", "Project", "Project", "PRJT", "sprk_projectname", "sprk_regardingproject", "sprk_projectnumber"),
        new RecordTypeSeedItem("sprk_reportcard", "Report Card", "Report Card", "RPTC", "sprk_name", "sprk_regardingreportcard", "sprk_reportcardnumber"),
        new RecordTypeSeedItem("sprk_servicerequest", "Service Request", "Service Request", "SVCR", "sprk_name", "sprk_regardingservicerequest", "sprk_servicerequestnumber"),
        new RecordTypeSeedItem("sprk_todo", "To Do", "To Do", "TODO", "sprk_name", "sprk_regardingtodo", null),
        new RecordTypeSeedItem("sprk_workassignment", "Work Assignment", "Work Assignment", "WRK", "sprk_name", "sprk_regardingworkassignment", "sprk_workassignmentnumber"),
    };

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiRecordTypeSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.RecordType;

    /// <summary>Constructs the seeder bound to a named <see cref="HttpClient"/> (production).</summary>
    public DataverseWebApiRecordTypeSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiRecordTypeSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects a <paramref name="credentialFactory"/>.</summary>
    internal DataverseWebApiRecordTypeSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiRecordTypeSeeder> logger,
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
                $"record-type seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
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
            _logger.LogWarning(ex, "H12b record-type seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"record-type seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var processed = new List<string>(SeedRecordTypes.Count);
        foreach (var item in SeedRecordTypes)
        {
            string outcome;
            try
            {
                outcome = await FindOrCreateAsync(envUri, token.Token, item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return AppConfigSeederResult.Failed(
                    $"record-type seed FAILED for '{item.LogicalName}': {ex.GetType().Name}: {ex.Message} " +
                    (processed.Count > 0
                        ? $"({processed.Count} record type(s) already processed this invocation: {string.Join(", ", processed)}.) "
                        : string.Empty) +
                    "Every upsert is find-by-logical-name -> create-if-missing (skip if present), so a full retry is safe.");
            }
            processed.Add($"{item.LogicalName} ({outcome})");
        }

        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new { recordTypes = processed })).RootElement.Clone();
        return AppConfigSeederResult.Ok(
            $"record-type seed OK — {processed.Count} record type(s) processed: {string.Join("; ", processed)}.", evidence);
    }

    private async Task<string> FindOrCreateAsync(
        Uri envUri, string bearerToken, RecordTypeSeedItem item, CancellationToken cancellationToken)
    {
        var filter = $"sprk_recordlogicalname eq '{item.LogicalName.Replace("'", "''", StringComparison.Ordinal)}'";
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/{EntitySet}?$filter={Uri.EscapeDataString(filter)}&$select=sprk_recordtype_refid");

        using (var getRequest = BuildRequest(HttpMethod.Get, getUri, bearerToken))
        using (var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false))
        {
            var text = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GET {EntitySet} returned {(int)getResponse.StatusCode} {getResponse.StatusCode}. Body: {Truncate(text, 400)}");
            }
            using var doc = JsonDocument.Parse(text);
            var values = doc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                return $"skipped (exists: {values[0].GetProperty("sprk_recordtype_refid").GetString()})";
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["sprk_recordlogicalname"] = item.LogicalName,
            ["sprk_recordtypename"] = item.TypeName,
            ["sprk_recorddisplayname"] = item.DisplayName,
            ["sprk_recordtypecode"] = item.TypeCode,
            ["sprk_recorddisplaynamefield"] = item.DisplayNameField,
            ["sprk_regardingfield"] = item.RegardingField,
        };
        if (item.RegardingRecordNumberField is not null)
        {
            body["sprk_regardingrecordnumberfield"] = item.RegardingRecordNumberField;
        }

        using var postRequest = BuildRequest(HttpMethod.Post, new Uri(envUri, $"/api/data/v9.2/{EntitySet}"), bearerToken);
        postRequest.Content = JsonContent.Create(body);
        postRequest.Headers.Add("Prefer", "return=representation");
        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        var postText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {EntitySet} returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. Body: {Truncate(postText, 400)}");
        }

        using var created = JsonDocument.Parse(postText);
        var id = created.RootElement.TryGetProperty("sprk_recordtype_refid", out var idEl) ? idEl.GetString() : null;
        return $"created {id ?? "(unknown)"}";
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

    /// <summary>One platform record type. Exposed <c>internal</c> so tests can inspect the seed data.</summary>
    internal sealed record RecordTypeSeedItem(
        string LogicalName,
        string TypeName,
        string DisplayName,
        string TypeCode,
        string DisplayNameField,
        string RegardingField,
        string? RegardingRecordNumberField);
}
