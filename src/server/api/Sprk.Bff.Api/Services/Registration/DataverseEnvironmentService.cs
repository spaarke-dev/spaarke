using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;

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

    /// <summary>The Dataverse error code for "the record does not exist" — the only 404 that means absent (#1363).</summary>
    internal const string RecordNotFoundErrorCode = "0x80040217";

    /// <summary>
    /// Named <see cref="IHttpClientFactory"/> client for environment-registry reads.
    /// Registered in <c>RegistrationModule</c> so HTTP connections are pooled per ADR-010
    /// (avoids ad-hoc HttpClient instantiation, which starves the connection pool).
    /// Separate from <see cref="RegistrationDataverseService.HttpClientName"/> so neither service's
    /// client configuration (headers, handlers) leaks into the other's.
    /// </summary>
    public const string HttpClientName = "DataverseEnvironment";

    public DataverseEnvironmentService(
        IConfiguration configuration,
        TokenCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<DataverseEnvironmentService> logger)
    {
        _logger = logger;
        _credential = credential;

        // Target the admin Dataverse environment (same as RegistrationDataverseService reads from config)
        var dataverseUrl = configuration["DATAVERSE_URL"]
            ?? throw new InvalidOperationException(
                "DataverseEnvironmentService requires DATAVERSE_URL configuration.");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
        _logger.LogInformation("DataverseEnvironmentService targeting Dataverse at {ApiUrl}", _apiUrl);

        // Factory-created client (pooled handler per ADR-010). BaseAddress is set here because the
        // Dataverse URL is resolved from runtime config, not known at DI registration time.
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _httpClient.BaseAddress = new Uri(_apiUrl.TrimEnd('/') + "/");
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
    /// Returns null only when Dataverse answers that the record does not exist (404 with error code
    /// <see cref="RecordNotFoundErrorCode"/>); any other 404 (a missing entity set, a routing fault) and every other
    /// failure throws, so a caller never reads a fault as absence (#1363).
    /// NFR-01: Fresh read from Dataverse (no caching).
    /// </summary>
    /// <remarks>
    /// Virtual for the same reason as <see cref="GetActiveEnvironmentsAsync"/>: a test stands in for the registry at
    /// this boundary without an HTTP double (<c>DemoExpirationJob</c> reads each request's own target environment).
    /// </remarks>
    public virtual async Task<DataverseEnvironmentRecord?> GetByIdAsync(Guid environmentId, CancellationToken ct = default)
    {
        var select = string.Join(",", DataverseEnvironmentRecord.AllColumns);
        var url = $"{EntitySetName}({environmentId})?$select={select}";

        _logger.LogDebug("GET environment {EnvironmentId}", environmentId);

        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, url, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var errorCode = await ReadErrorCodeAsync(response, ct);
            if (string.Equals(errorCode, RecordNotFoundErrorCode, StringComparison.OrdinalIgnoreCase))
                return null;

            throw new HttpRequestException(
                $"Reading environment {environmentId} returned 404 with error code '{errorCode ?? "(none)"}', not "
                + $"{RecordNotFoundErrorCode} (record not found); the environment's existence is unknown.",
                inner: null,
                System.Net.HttpStatusCode.NotFound);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return DataverseEnvironmentRecord.MapFromJson(json);
    }

    /// <summary>The <c>error.code</c> of a Dataverse error body, or null when the body is not one.</summary>
    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return body.ValueKind == JsonValueKind.Object
                   && body.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves all active environment records.
    /// NFR-01: Fresh read from Dataverse (no caching).
    /// </summary>
    /// <remarks>
    /// Virtual so a test can stand in for the registry at this boundary without an HTTP double (ADR-038 B1) — the
    /// <c>ExternalParticipationService</c> convention. The identity-link reconciliation job reads it to reconcile
    /// every environment this BFF provisions users into (unified-access-control-r2 task 141).
    /// </remarks>
    public virtual async Task<List<DataverseEnvironmentRecord>> GetActiveEnvironmentsAsync(CancellationToken ct = default)
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
        // _httpClient is owned by IHttpClientFactory (pooled handler) — not disposed here.
        _tokenSemaphore?.Dispose();
    }

    #endregion
}
