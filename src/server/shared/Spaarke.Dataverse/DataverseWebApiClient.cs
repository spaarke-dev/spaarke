using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse Web API client for .NET 8.0 compatibility.
/// Uses REST API instead of ServiceClient to avoid System.ServiceModel dependencies.
/// Thread-safe: uses SemaphoreSlim for token refresh and per-request Authorization headers.
/// </summary>
public class DataverseWebApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseWebApiClient> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    public DataverseWebApiClient(IConfiguration configuration, ILogger<DataverseWebApiClient> logger)
    {
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var managedIdentityClientId = configuration["ManagedIdentity:ClientId"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        if (string.IsNullOrEmpty(managedIdentityClientId))
            throw new InvalidOperationException("ManagedIdentity:ClientId configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_apiUrl)
        };

        _logger.LogInformation("Initialized Dataverse Web API client for {ApiUrl}", _apiUrl);
    }

    /// <summary>
    /// Thread-safe token refresh using SemaphoreSlim with double-check locking.
    /// Returns the current valid token for use in per-request Authorization headers.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: token is still valid (no lock needed)
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _currentToken.Value.Token;
        }

        // Slow path: acquire semaphore for token refresh (30s timeout to prevent deadlocks)
        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            throw new TimeoutException("Timed out waiting for Dataverse token refresh");
        }

        try
        {
            // Double-check: another thread may have refreshed while we waited
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _currentToken.Value.Token;
            }

            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                cancellationToken);

            _logger.LogDebug("Refreshed Dataverse access token");
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    /// <summary>
    /// Creates an HttpRequestMessage with per-request Authorization header.
    /// This avoids mutating shared DefaultRequestHeaders on the HttpClient.
    /// </summary>
    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(
        HttpMethod method, string url, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<T?> RetrieveAsync<T>(string entitySetName, Guid id, string? select = null, CancellationToken cancellationToken = default)
    {
        var query = select != null ? $"?$select={select}" : "";
        var url = $"{entitySetName}({id}){query}";

        _logger.LogDebug("GET {Url}", url);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, cancellationToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    public async Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("POST {EntitySetName}", entitySetName);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, entitySetName, cancellationToken);
        request.Content = JsonContent.Create(entity);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader != null)
        {
            var idString = entityIdHeader.Split('(', ')')[1];
            return Guid.Parse(idString);
        }

        throw new InvalidOperationException("Failed to extract entity ID from response");
    }

    public async Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
    {
        var url = $"{entitySetName}({id})";

        _logger.LogDebug("PATCH {Url}", url);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Patch, url, cancellationToken);
        request.Content = JsonContent.Create(entity);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string entitySetName, Guid id, CancellationToken cancellationToken = default)
    {
        var url = $"{entitySetName}({id})";

        _logger.LogDebug("DELETE {Url}", url);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Delete, url, cancellationToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null, int? top = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (filter != null) queryParams.Add($"$filter={filter}");
        if (select != null) queryParams.Add($"$select={select}");
        if (top != null) queryParams.Add($"$top={top}");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"{entitySetName}{query}";

        _logger.LogDebug("GET {Url}", url);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, cancellationToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<T>>(cancellationToken: cancellationToken);
        return result?.Value ?? new List<T>();
    }

    public void Dispose()
    {
        _tokenSemaphore?.Dispose();
        _httpClient?.Dispose();
    }

    private class ODataCollectionResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
