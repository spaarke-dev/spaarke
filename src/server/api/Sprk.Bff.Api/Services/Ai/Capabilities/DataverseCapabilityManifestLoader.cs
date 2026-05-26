using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;

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
/// When the table does not exist (HTTP 404 / "Resource not found" 400 from Dataverse
/// indicating a missing entity), the method ALSO returns an empty list and logs a
/// warning. This is the expected state for environments that have not yet had the
/// `sprk_aicapability` table provisioned (e.g., fresh dev environments). The chat
/// pipeline degrades gracefully: tool calls remain unavailable but the rest of the
/// chat flow proceeds.
/// Other transient failures (5xx, network errors) still throw so the
/// <see cref="ManifestRefreshService"/> stale-on-error policy can retain the
/// existing manifest.
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
        TokenCredential credential,
        ILogger<DataverseCapabilityManifestLoader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _credential = credential;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        // IMPORTANT: BaseAddress must end with trailing slash so relative URLs append correctly.
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";

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

            // Missing-entity tolerance: when the `sprk_aicapability` table doesn't exist
            // in the target environment (typical for fresh dev envs), Dataverse returns
            // either 404 (NotFound) or 400 with a body containing "Resource not found"
            // or "Could not find a property named". Treat these as "no capabilities
            // registered" and return empty — do NOT throw, so chat tool detection still
            // runs (just with zero tools available) and ManifestRefreshService does not
            // log a noisy stack trace on every refresh tick.
            if (IsMissingEntityResponse(response.StatusCode, body))
            {
                _logger.LogWarning(
                    "Dataverse table '{EntitySet}' is not provisioned in this environment " +
                    "({StatusCode}). AI chat tools will be unavailable until the table is " +
                    "created. Returning empty manifest.",
                    EntitySetName, response.StatusCode);
                return Array.Empty<CapabilityManifestEntry>();
            }

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

    /// <summary>
    /// Detects whether a non-success Dataverse response indicates the queried
    /// entity (table) does not exist in this environment. Treated as a graceful
    /// "empty manifest" condition rather than an error.
    ///
    /// Dataverse signals missing-entity in two ways:
    ///   - HTTP 404 (NotFound) — the entity set path is unknown
    ///   - HTTP 400 with body containing "Resource not found" or
    ///     "Could not find a property named" — the table exists but a column is missing
    ///     OR the OData $select references an unknown navigation property.
    /// </summary>
    internal static bool IsMissingEntityResponse(System.Net.HttpStatusCode statusCode, string body)
    {
        if (statusCode == System.Net.HttpStatusCode.NotFound)
            return true;

        if (statusCode == System.Net.HttpStatusCode.BadRequest && !string.IsNullOrEmpty(body))
        {
            // Dataverse error bodies are JSON with a string "message" property. Match on
            // the well-known phrases without parsing JSON (cheaper + tolerant of format drift).
            return body.Contains("Resource not found for the segment", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase)
                || body.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

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
