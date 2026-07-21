using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Infrastructure.Cache;

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
    // Resource identifier for ITenantCache (FR-05). Cached value is per-Contact participation
    // data (project list), not an authorization decision per ADR-009.
    private const string ExternalAccessResource = "external-access-grant";
    private const int CacheVersion = 1;

    private readonly HttpClient _httpClient;
    private readonly ITenantCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ExternalParticipationService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExternalParticipationService(
        HttpClient httpClient,
        ITenantCache cache,
        IConfiguration configuration,
        TokenCredential credential,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ExternalParticipationService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _configuration = configuration;
        _credential = credential;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets active participations for a Contact. Checks Redis cache first, falls back to Dataverse.
    /// </summary>
    public virtual async Task<IReadOnlyList<ExternalParticipation>> GetParticipationsAsync(
        Guid contactId,
        CancellationToken ct = default)
    {
        var tenantId = ExtractTenantId();
        var idComponent = contactId.ToString();

        // Try cache first (only if tenantId is available — otherwise fall through to Dataverse)
        if (!string.IsNullOrEmpty(tenantId))
        {
            try
            {
                var cached = await _cache.GetAsync<List<CachedParticipation>>(
                    tenantId, ExternalAccessResource, idComponent, CacheVersion, ct: ct);
                if (cached != null)
                {
                    _logger.LogDebug("[EXT-ACCESS] Cache HIT for Contact {ContactId}: {Count} participations",
                        contactId, cached.Count);
                    return cached.Select(c => c.ToParticipation()).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EXT-ACCESS] Cache read error for Contact {ContactId}. Falling through to Dataverse.", contactId);
            }
        }

        // Cache miss — query Dataverse
        var participations = await QueryDataverseAsync(contactId, ct);

        // Cache result (fire-and-forget — don't block response). Skip when no tenant claim.
        if (!string.IsNullOrEmpty(tenantId))
        {
            _ = CacheParticipationsAsync(tenantId, idComponent, participations);
        }

        return participations;
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the current HttpContext.
    /// Returns null when no claim is present (in which case caching is skipped).
    /// </summary>
    private string? ExtractTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
    }

    /// <summary>
    /// Resolves the Dataverse Contact for an external (CIAM) caller by the stable <c>oid</c> claim
    /// (bound to <c>Contact.sprk_externalobjectid</c>) per ADR-028 Amendment A1. Email is used only as
    /// a <b>first-login</b> fallback that then binds the oid onto the Contact; once a Contact is bound
    /// to an oid, a mismatched email can neither redirect resolution nor grant access.
    ///
    /// Resolution order:
    ///   1. If <paramref name="oid"/> is present, look up the Contact by <c>sprk_externalobjectid</c>.
    ///      A hit is authoritative (email is not consulted).
    ///   2. Otherwise (no Contact bound to this oid yet), fall back to an <c>emailaddress1</c> match,
    ///      but ONLY bind the oid onto — and grant — a Contact that has no oid yet. A Contact already
    ///      bound to a <i>different</i> oid is NOT granted via email (prevents shared-email hijack).
    /// </summary>
    /// <param name="oid">The CIAM token's stable object id (immutable directory key). May be null on a
    /// non-CIAM/transitional email-only token.</param>
    /// <param name="email">The caller's email/UPN claim (first-login fallback). May be null.</param>
    public virtual async Task<Guid?> ResolveExternalContactAsync(string? oid, string? email, CancellationToken ct = default)
    {
        // 1. Stable-oid resolution — authoritative once bound.
        if (!string.IsNullOrEmpty(oid))
        {
            var byOid = await ResolveContactByOidAsync(oid, ct);
            if (byOid.HasValue)
            {
                _logger.LogDebug("[EXT-ACCESS] Resolved oid to Contact {ContactId} via sprk_externalobjectid", byOid.Value);
                return byOid;
            }
        }

        // 2. First-login email fallback (no Contact bound to this oid yet).
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        var (contactId, existingOid) = await ResolveContactRowByEmailAsync(email, ct);
        if (!contactId.HasValue)
        {
            return null;
        }

        if (string.IsNullOrEmpty(existingOid))
        {
            // Unbound Contact — bind the incoming oid so subsequent logins resolve by the stable key.
            // A bind failure is non-fatal: this login still resolves, and the next login retries the bind.
            if (!string.IsNullOrEmpty(oid))
            {
                await BindOidToContactAsync(contactId.Value, oid!, ct);
            }
            return contactId;
        }

        // Contact is already bound to an oid. Only grant if it matches the incoming oid — never let an
        // email match override an existing (different) oid binding (Amendment A1: oid is authoritative).
        if (!string.IsNullOrEmpty(oid) && string.Equals(existingOid, oid, StringComparison.OrdinalIgnoreCase))
        {
            return contactId;
        }

        _logger.LogWarning(
            "[EXT-ACCESS] Email {Email} matches a Contact already bound to a different oid — access denied (no email hijack of a bound Contact).",
            email);
        return null;
    }

    /// <summary>
    /// Resolves a Contact GUID by the stable CIAM <c>oid</c> (Contact.sprk_externalobjectid).
    /// </summary>
    public async Task<Guid?> ResolveContactByOidAsync(string oid, CancellationToken ct = default)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // OData string literal: double single quotes, then URL-encode.
            var encodedOid = Uri.EscapeDataString(oid.Replace("'", "''"));
            var query = $"{apiUrl}/contacts?$filter=sprk_externalobjectid eq '{encodedOid}'&$select=contactid&$top=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to resolve Contact by oid: {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactRow>>(ct);
            return result?.Value?.FirstOrDefault()?.contactid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error resolving Contact by oid");
            return null;
        }
    }

    /// <summary>
    /// Resolves a Contact GUID by querying contacts.emailaddress1. Retained for the first-login
    /// fallback path; delegates to <see cref="ResolveContactRowByEmailAsync"/>.
    /// </summary>
    public async Task<Guid?> ResolveContactByEmailAsync(string email, CancellationToken ct = default)
    {
        var (contactId, _) = await ResolveContactRowByEmailAsync(email, ct);
        return contactId;
    }

    /// <summary>
    /// Queries a Contact by email, returning both the Contact id and its current oid binding
    /// (<c>sprk_externalobjectid</c>, null when unbound) so callers can enforce the no-hijack rule.
    /// </summary>
    private async Task<(Guid? ContactId, string? ExistingOid)> ResolveContactRowByEmailAsync(string email, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // OData string literal: double single quotes, then URL-encode.
            var encodedEmail = Uri.EscapeDataString(email.Replace("'", "''"));
            var query = $"{apiUrl}/contacts?$filter=emailaddress1 eq '{encodedEmail}'&$select=contactid,sprk_externalobjectid&$top=1";

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
                return (null, null);
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactRow>>(ct);
            var row = result?.Value?.FirstOrDefault();

            if (row?.contactid is not null)
                _logger.LogDebug("[EXT-ACCESS] Resolved email {Email} to Contact {ContactId} (oid bound: {Bound})",
                    email, row.contactid, !string.IsNullOrEmpty(row.sprk_externalobjectid));

            return (row?.contactid, row?.sprk_externalobjectid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error resolving Contact by email {Email}", email);
            return (null, null);
        }
    }

    /// <summary>
    /// Binds the CIAM <c>oid</c> onto a Contact's <c>sprk_externalobjectid</c> at first login.
    /// Update-only (<c>If-Match: *</c>) so a missing Contact is never accidentally created.
    /// Non-fatal on failure — the caller still resolves this login and the bind is retried next time.
    /// </summary>
    private async Task BindOidToContactAsync(Guid contactId, string oid, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            var url = $"{apiUrl}/contacts({contactId})";
            using var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["sprk_externalobjectid"] = oid })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("If-Match", "*"); // update-only — do not upsert-create
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[EXT-ACCESS] Bound oid to Contact {ContactId} (first-login).", contactId);
            }
            else
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to bind oid to Contact {ContactId}: {Status}. Will retry next login.",
                    contactId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Error binding oid to Contact {ContactId}. Non-fatal.", contactId);
        }
    }

    private async Task<IReadOnlyList<ExternalParticipation>> QueryDataverseAsync(Guid contactId, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // Query active participations for this Contact.
            // Dataverse lookup field names: _sprk_contact_value (contact FK), _sprk_project_value (project FK).
            // These differ from the schema name suffix convention — verified against live Dataverse response.
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter=_sprk_contact_value eq {contactId} and statecode eq 0" +
                        $"&$select=_sprk_project_value,sprk_accesslevel";

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
                .Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)
                .Select(r => new ExternalParticipation
                {
                    ProjectId = r._sprk_project_value!.Value,
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

    private async Task CacheParticipationsAsync(
        string tenantId,
        string idComponent,
        IReadOnlyList<ExternalParticipation> participations)
    {
        try
        {
            var cached = participations.Select(p => new CachedParticipation
            {
                ProjectId = p.ProjectId,
                AccessLevel = (int)p.AccessLevel
            }).ToList();

            await _cache.SetAsync(
                tenantId, ExternalAccessResource, idComponent, CacheVersion,
                cached, CacheTtl);

            _logger.LogDebug("[EXT-ACCESS] Cached {Count} participations for Contact {ContactId} (TTL: {Ttl}s)",
                participations.Count, idComponent, CacheTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Error caching participations for Contact {ContactId}. Non-critical.", idComponent);
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

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
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
        [JsonPropertyName("_sprk_project_value")]
        public Guid? _sprk_project_value { get; set; }

        [JsonPropertyName("sprk_accesslevel")]
        public int? sprk_accesslevel { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid? contactid { get; set; }

        [JsonPropertyName("sprk_externalobjectid")]
        public string? sprk_externalobjectid { get; set; }
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
