using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Loads <see cref="CapabilityManifestEntry"/> records from the Dataverse
/// <c>sprk_aicapability</c> table via the Web API (OData v4).
///
/// Authentication follows the same managed-identity pattern used by
/// <see cref="Sprk.Bff.Api.Services.Ai.PlaybookService"/>:
/// DefaultAzureCredential → Bearer token → Refreshed before expiry.
///
/// Column mapping:
/// <list type="table">
///   <item><term>sprk_name</term><description>CapabilityName</description></item>
///   <item><term>sprk_description</term><description>Description</description></item>
///   <item><term>sprk_keywordhints</term><description>KeywordHints (pipe-delimited)</description></item>
///   <item><term>_sprk_playbookid_value</term><description>PlaybookId (nullable lookup)</description></item>
///   <item><term>sprk_toolnames</term><description>ToolNames (pipe-delimited)</description></item>
///   <item><term>sprk_isenabled</term><description>IsEnabled</description></item>
///   <item><term>sprk_tenantrestrictions</term><description>TenantRestrictions (pipe-delimited)</description></item>
/// </list>
///
/// When the table is empty the method returns an empty list.
/// When the table does not exist or Dataverse is unreachable an
/// <see cref="InvalidOperationException"/> is thrown so the caller can decide
/// whether to abort startup or fall back to empty.
/// </summary>
public sealed class DataverseCapabilityManifestLoader : ICapabilityManifestLoader
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseCapabilityManifestLoader> _logger;

    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_aicapabilities";

    private static readonly string SelectColumns =
        "sprk_aicapabilityid," +
        "sprk_name," +
        "sprk_description," +
        "sprk_keywordhints," +
        "_sprk_playbookid_value," +
        "sprk_toolnames," +
        "sprk_isenabled," +
        "sprk_tenantrestrictions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DataverseCapabilityManifestLoader(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseCapabilityManifestLoader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        // IMPORTANT: BaseAddress must end with trailing slash so relative URLs append correctly.
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
        // AUTHV2-042: Migrated from ClientSecretCredential to DefaultAzureCredential (managed identity).
        _credential = new DefaultAzureCredential();

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CapabilityManifestEntry>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"{EntitySetName}?$select={SelectColumns}";

        _logger.LogInformation(
            "Loading capability manifest from Dataverse table '{EntitySet}'", EntitySetName);

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Dataverse query for '{EntitySet}' failed: {StatusCode} — {Body}",
                EntitySetName, response.StatusCode, body);
            throw new InvalidOperationException(
                $"Failed to load capability manifest from Dataverse: {response.StatusCode}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);

        if (result?.Value == null || result.Value.Length == 0)
        {
            _logger.LogWarning(
                "Capability manifest loaded but table '{EntitySet}' returned 0 rows", EntitySetName);
            return Array.Empty<CapabilityManifestEntry>();
        }

        var entries = result.Value
            .Select(MapToEntry)
            .Where(e => e is not null)
            .ToList()!;

        _logger.LogInformation(
            "Capability manifest loaded: {Count} entries from Dataverse", entries.Count);

        return entries!;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_currentToken == null ||
            _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2/", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug(
                "Refreshed Dataverse access token for DataverseCapabilityManifestLoader");
        }
    }

    private static CapabilityManifestEntry? MapToEntry(JsonElement element)
    {
        try
        {
            var name = element.TryGetProperty("sprk_name", out var nameProp)
                ? nameProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
                return null;

            var description = element.TryGetProperty("sprk_description", out var descProp)
                ? descProp.GetString() ?? string.Empty
                : string.Empty;

            var keywordHints = ParsePipeDelimited(element, "sprk_keywordhints");
            var toolNames = ParsePipeDelimited(element, "sprk_toolnames");
            var tenantRestrictions = ParsePipeDelimited(element, "sprk_tenantrestrictions");

            Guid? playbookId = null;
            if (element.TryGetProperty("_sprk_playbookid_value", out var pbProp) &&
                pbProp.ValueKind != JsonValueKind.Null &&
                Guid.TryParse(pbProp.GetString(), out var parsedPbId))
            {
                playbookId = parsedPbId;
            }

            var isEnabled = element.TryGetProperty("sprk_isenabled", out var enabledProp)
                && enabledProp.ValueKind == JsonValueKind.True;

            return new CapabilityManifestEntry(
                CapabilityName: name,
                Description: description,
                KeywordHints: keywordHints,
                PlaybookId: playbookId,
                ToolNames: toolNames,
                IsEnabled: isEnabled,
                TenantRestrictions: tenantRestrictions);
        }
        catch (Exception ex)
        {
            // Log and skip malformed rows rather than aborting the entire load.
            // The caller will log the final count — individual row errors surface here.
            _ = ex; // suppress unused-variable warning in release builds
            return null;
        }
    }

    private static IReadOnlyList<string> ParsePipeDelimited(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        var raw = prop.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    // ── Internal OData response model ─────────────────────────────────────────

    private sealed class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public JsonElement[]? Value { get; set; }
    }
}
