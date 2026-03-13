using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Base class for Dataverse HTTP services that share authentication, error handling,
/// OData query building, and ownership validation infrastructure.
/// Extracted from ScopeResolverService as part of the god-class decomposition (PPI-054).
/// </summary>
public abstract class DataverseHttpServiceBase
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _apiUrl;
    private readonly ILogger _logger;
    private AccessToken? _currentToken;

    // Prefix constants for scope ownership
    protected const string SystemPrefix = "SYS-";
    protected const string CustomerPrefix = "CUST-";

    protected DataverseHttpServiceBase(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");
        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["API_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("API_CLIENT_SECRET configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// The configured HttpClient for Dataverse API calls.
    /// </summary>
    protected HttpClient Http => _httpClient;

    /// <summary>
    /// The logger instance.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Ensures the HttpClient has a valid bearer token for Dataverse.
    /// </summary>
    protected async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for {ServiceType}", GetType().Name);
        }
    }

    /// <summary>
    /// Replacement for EnsureSuccessStatusCode that captures the response body for diagnostics.
    /// </summary>
    protected async Task EnsureSuccessWithDiagnosticsAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "[DATAVERSE-ERROR] {Operation} failed with {StatusCode}. Response body: {Body}",
                operation, response.StatusCode, body);
            throw new HttpRequestException(
                $"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Dataverse error: {(body.Length > 500 ? body[..500] : body)}",
                null,
                response.StatusCode);
        }
    }

    /// <summary>
    /// Validates that a scope is not system-owned (immutable).
    /// </summary>
    protected void ValidateOwnership(string name, bool isImmutable, string operation)
    {
        if (isImmutable || name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Attempted to {Operation} system scope '{ScopeName}'", operation, name);
            throw new ScopeOwnershipException(name, operation);
        }
    }

    /// <summary>
    /// Determines if a name represents a system-owned scope.
    /// </summary>
    protected static bool IsSystemScope(string name)
        => name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures a customer scope has the CUST- prefix.
    /// </summary>
    protected static string EnsureCustomerPrefix(string name)
    {
        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{CustomerPrefix}{name}";
    }

    /// <summary>
    /// Builds an OData query string from ScopeListOptions.
    /// </summary>
    protected static string BuildODataQuery(
        ScopeListOptions options,
        string selectFields,
        string expandClause,
        string nameFieldPath,
        string? categoryFieldPath,
        Dictionary<string, string> sortFieldMappings)
    {
        var queryParts = new List<string>();

        if (!string.IsNullOrEmpty(selectFields))
        {
            queryParts.Add($"$select={selectFields}");
        }

        if (!string.IsNullOrEmpty(expandClause))
        {
            queryParts.Add($"$expand={expandClause}");
        }

        var filterClauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            var escapedName = options.NameFilter.Replace("'", "''");
            filterClauses.Add($"contains({nameFieldPath}, '{escapedName}')");
        }

        if (!string.IsNullOrWhiteSpace(options.CategoryFilter) && !string.IsNullOrEmpty(categoryFieldPath))
        {
            var escapedCategory = options.CategoryFilter.Replace("'", "''");
            filterClauses.Add($"{categoryFieldPath} eq '{escapedCategory}'");
        }

        if (filterClauses.Count > 0)
        {
            queryParts.Add($"$filter={string.Join(" and ", filterClauses)}");
        }

        var sortField = sortFieldMappings.GetValueOrDefault(options.SortBy.ToLowerInvariant(), sortFieldMappings.Values.First());
        var sortDirection = options.SortDescending ? "desc" : "asc";
        queryParts.Add($"$orderby={sortField} {sortDirection}");

        var skip = (options.Page - 1) * options.PageSize;
        if (skip > 0)
        {
            queryParts.Add($"$skip={skip}");
        }
        queryParts.Add($"$top={options.PageSize}");

        queryParts.Add("$count=true");

        return string.Join("&", queryParts);
    }

    /// <summary>
    /// Extracts the display label from a Dataverse OptionSet option element.
    /// </summary>
    protected static string? ExtractOptionLabel(JsonElement option)
    {
        if (option.TryGetProperty("Label", out var labelObj) &&
            labelObj.TryGetProperty("UserLocalizedLabel", out var userLabel) &&
            userLabel.TryGetProperty("Label", out var labelValue) &&
            labelValue.ValueKind == JsonValueKind.String)
        {
            return labelValue.GetString();
        }

        return null;
    }

    #region Shared DTOs

    /// <summary>
    /// Generic OData collection response with count support for pagination.
    /// </summary>
    protected class ODataCollectionResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.count")]
        public int? ODataCount { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    #endregion
}
