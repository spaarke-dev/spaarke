using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Queries sprk_externalrecordaccess for a Contact's active participations.
/// Results are cached in Redis with 60-second TTL per ADR-009.
///
/// Cache key: sdap:external:access:{contactId}
/// </summary>
public class ExternalParticipationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string CacheKeyPrefix = "sdap:external:access:";

    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalParticipationService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExternalParticipationService(
        HttpClient httpClient,
        IDistributedCache cache,
        IConfiguration configuration,
        ILogger<ExternalParticipationService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets active participations for a Contact. Checks Redis cache first, falls back to Dataverse.
    /// </summary>
    public async Task<IReadOnlyList<ExternalParticipation>> GetParticipationsAsync(
        Guid contactId,
        CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{contactId}";

        // Try cache first
        try
        {
            var cachedJson = await _cache.GetStringAsync(cacheKey, ct);
            if (cachedJson != null)
            {
                var cached = JsonSerializer.Deserialize<List<CachedParticipation>>(cachedJson, JsonOptions);
                if (cached != null)
                {
                    _logger.LogDebug("[EXT-ACCESS] Cache HIT for Contact {ContactId}: {Count} participations",
                        contactId, cached.Count);
                    return cached.Select(c => c.ToParticipation()).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Cache read error for Contact {ContactId}. Falling through to Dataverse.", contactId);
        }

        // Cache miss — query Dataverse
        var participations = await QueryDataverseAsync(contactId, ct);

        // Cache result (fire-and-forget — don't block response)
        _ = CacheParticipationsAsync(cacheKey, participations);

        return participations;
    }

    /// <summary>
    /// Resolves a Contact GUID by querying contacts.emailaddress1.
    /// Used to map an Azure AD B2B guest's email claim to their Dataverse Contact record.
    /// </summary>
    public async Task<Guid?> ResolveContactByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            var encodedEmail = Uri.EscapeDataString(email);
            var query = $"{apiUrl}/contacts?$filter=emailaddress1 eq '{encodedEmail}'&$select=contactid&$top=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to resolve Contact by email {Email}: {Status}",
                    email, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactRow>>(ct);
            var contactId = result?.Value?.FirstOrDefault()?.contactid;

            if (contactId.HasValue)
                _logger.LogDebug("[EXT-ACCESS] Resolved email {Email} to Contact {ContactId}", email, contactId);

            return contactId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error resolving Contact by email {Email}", email);
            return null;
        }
    }

    private async Task<IReadOnlyList<ExternalParticipation>> QueryDataverseAsync(Guid contactId, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // Query active participations for this Contact
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter=_sprk_contactid_value eq {contactId} and statecode eq 0" +
                        $"&$select=_sprk_projectid_value,sprk_accesslevel";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Dataverse query failed for Contact {ContactId}: {Status}",
                    contactId, response.StatusCode);
                return Array.Empty<ExternalParticipation>();
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ExternalAccessRow>>(ct);
            var participations = result?.Value?
                .Where(r => r._sprk_projectid_value.HasValue && r.sprk_accesslevel.HasValue)
                .Select(r => new ExternalParticipation
                {
                    ProjectId = r._sprk_projectid_value!.Value,
                    AccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value
                })
                .ToList() ?? new List<ExternalParticipation>();

            _logger.LogInformation("[EXT-ACCESS] Loaded {Count} active participations for Contact {ContactId}",
                participations.Count, contactId);

            return participations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying Dataverse for Contact {ContactId}", contactId);
            return Array.Empty<ExternalParticipation>();
        }
    }

    private async Task CacheParticipationsAsync(string cacheKey, IReadOnlyList<ExternalParticipation> participations)
    {
        try
        {
            var cached = participations.Select(p => new CachedParticipation
            {
                ProjectId = p.ProjectId,
                AccessLevel = (int)p.AccessLevel
            }).ToList();

            var json = JsonSerializer.Serialize(cached, JsonOptions);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });

            _logger.LogDebug("[EXT-ACCESS] Cached {Count} participations for key {Key} (TTL: {Ttl}s)",
                participations.Count, cacheKey, CacheTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Error caching participations for key {Key}. Non-critical.", cacheKey);
        }
    }

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Dataverse token");

        try
        {
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                return _currentToken.Value.Token;

            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");

            var managedIdentityClientId = _configuration["ManagedIdentity:ClientId"]
                ?? throw new InvalidOperationException("ManagedIdentity:ClientId is required");

            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = managedIdentityClientId
            });

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private string GetDataverseApiUrl()
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");
        return $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
    }

    // DTO types for Dataverse OData responses

    private sealed class DataverseQueryResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    private sealed class ExternalAccessRow
    {
        [JsonPropertyName("_sprk_projectid_value")]
        public Guid? _sprk_projectid_value { get; set; }

        [JsonPropertyName("sprk_accesslevel")]
        public int? sprk_accesslevel { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid? contactid { get; set; }
    }

    private sealed class CachedParticipation
    {
        public Guid ProjectId { get; set; }
        public int AccessLevel { get; set; }

        public ExternalParticipation ToParticipation() => new()
        {
            ProjectId = ProjectId,
            AccessLevel = (ExternalAccessLevel)AccessLevel
        };
    }
}
