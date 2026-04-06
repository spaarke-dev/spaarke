using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Reads sprk_dataverseenvironment records from the admin Dataverse environment.
/// NFR-01: No caching — reads fresh from Dataverse on each call.
/// ADR-010: Registered as concrete singleton (no interface).
/// Follows the same auth pattern as RegistrationDataverseService.
/// </summary>
public class DataverseEnvironmentService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<DataverseEnvironmentService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private const string EntitySetName = "sprk_dataverseenvironments";

    public DataverseEnvironmentService(
        IConfiguration configuration,
        ILogger<DataverseEnvironmentService> logger)
    {
        _logger = logger;

        // Target the admin Dataverse environment (same as RegistrationDataverseService reads from config)
        var dataverseUrl = configuration["DATAVERSE_URL"]
            ?? throw new InvalidOperationException(
                "DataverseEnvironmentService requires DATAVERSE_URL configuration.");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["API_CLIENT_SECRET"];
        var tenantId = configuration["TENANT_ID"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                "DataverseEnvironmentService requires TENANT_ID, API_APP_ID, and API_CLIENT_SECRET configuration.");
        }

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _logger.LogInformation("DataverseEnvironmentService targeting Dataverse at {ApiUrl}", _apiUrl);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_apiUrl.TrimEnd('/') + "/")
        };
    }

    #region Token Management

    /// <summary>
    /// Thread-safe token refresh with double-check locking.
    /// Same pattern as RegistrationDataverseService.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Dataverse token refresh");

        try
        {
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                return _currentToken.Value.Token;

            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), ct);
            _logger.LogDebug("Refreshed Dataverse environment service access token");
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(
        HttpMethod method, string url, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    #endregion

    #region Environment Queries

    /// <summary>
    /// Retrieves a single environment record by ID.
    /// Returns null if not found.
    /// NFR-01: Fresh read from Dataverse (no caching).
    /// </summary>
    public async Task<DataverseEnvironmentRecord?> GetByIdAsync(Guid environmentId, CancellationToken ct = default)
    {
        var select = string.Join(",", DataverseEnvironmentRecord.AllColumns);
        var url = $"{EntitySetName}({environmentId})?$select={select}";

        _logger.LogDebug("GET environment {EnvironmentId}", environmentId);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return DataverseEnvironmentRecord.MapFromJson(json);
    }

    /// <summary>
    /// Retrieves all active environment records.
    /// NFR-01: Fresh read from Dataverse (no caching).
    /// </summary>
    public async Task<List<DataverseEnvironmentRecord>> GetActiveEnvironmentsAsync(CancellationToken ct = default)
    {
        var select = string.Join(",", DataverseEnvironmentRecord.AllColumns);
        var url = $"{EntitySetName}?$filter=sprk_isactive eq true&$select={select}&$orderby=sprk_name asc";

        _logger.LogDebug("GET active environments");

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(cancellationToken: ct);
        return result?.Value?.Select(DataverseEnvironmentRecord.MapFromJson).ToList()
            ?? new List<DataverseEnvironmentRecord>();
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        _tokenSemaphore?.Dispose();
        _httpClient?.Dispose();
    }

    #endregion
}
