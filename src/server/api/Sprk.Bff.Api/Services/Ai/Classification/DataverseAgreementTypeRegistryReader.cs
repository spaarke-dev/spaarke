using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Bff.Api.Services.Ai.Classification;

/// <summary>
/// Dataverse-backed <see cref="IAgreementTypeRegistryReader"/>. Reads the active
/// <c>sprk_agreementtype</c> rows via the Dataverse Web API using the same HttpClient + managed-identity
/// <see cref="TokenCredential"/> pattern as <see cref="ScopeResolverService"/> (ADR-028 server-outbound
/// auth). Registered via <c>AddHttpClient</c> symmetric with the ScopeResolverService registration.
/// </summary>
/// <remarks>
/// ai-advanced-capabilities-agreements-r1 task 020 (FR-07 / design Lens 3d). Read-only reference-data
/// query; ADR-015: only identifiers/counts are logged, never row content beyond keys.
/// </remarks>
public sealed class DataverseAgreementTypeRegistryReader : IAgreementTypeRegistryReader
{
    private const string EntitySet = "sprk_agreementtypes";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseAgreementTypeRegistryReader> _logger;
    private readonly string _apiUrl;
    private AccessToken? _currentToken;

    public DataverseAgreementTypeRegistryReader(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<DataverseAgreementTypeRegistryReader> logger)
    {
        _httpClient = httpClient;
        _credential = credential;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgreementTypeRow>> GetRowsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // Active rows only; select exactly the columns the classifier + gate need.
        const string url = EntitySet
            + "?$select=sprk_key,sprk_name,sprk_classificationcue,sprk_isfallback,sprk_confidencethreshold"
            + "&$filter=statecode eq 0";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "[AGREEMENT-TYPE-REGISTRY] Read failed with {StatusCode}. Body: {Body}",
                    response.StatusCode, body.Length > 500 ? body[..500] : body);
                throw new HttpRequestException(
                    $"sprk_agreementtype read failed: {(int)response.StatusCode} {response.ReasonPhrase}",
                    null,
                    response.StatusCode);
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
                valueArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("[AGREEMENT-TYPE-REGISTRY] Read returned no 'value' array");
                return Array.Empty<AgreementTypeRow>();
            }

            var rows = new List<AgreementTypeRow>();
            foreach (var item in valueArray.EnumerateArray())
            {
                var key = GetString(item, "sprk_key");
                if (string.IsNullOrWhiteSpace(key))
                    continue; // a row without a key is not routable; skip it defensively

                rows.Add(new AgreementTypeRow(
                    Key: key,
                    Name: GetString(item, "sprk_name"),
                    ClassificationCue: GetString(item, "sprk_classificationcue"),
                    IsFallback: GetBool(item, "sprk_isfallback"),
                    ConfidenceThreshold: GetDecimal(item, "sprk_confidencethreshold")));
            }

            _logger.LogInformation(
                "[AGREEMENT-TYPE-REGISTRY] Read {Count} active sprk_agreementtype row(s): [{Keys}]",
                rows.Count, string.Join(", ", rows.Select(r => r.Key)));

            return rows;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[AGREEMENT-TYPE-REGISTRY] Read failed querying {EntitySet}", EntitySet);
            throw;
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for DataverseAgreementTypeRegistryReader");
        }
    }

    private static string? GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool GetBool(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static decimal? GetDecimal(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal()
            : null;
}
