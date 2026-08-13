using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Resolves the Tier-1 <b>module entitlement</b> set for a collaboration caller — the list of module
/// codes the client (<c>/me.entitlements</c>) gates tab visibility on (spec FR-07/FR-08/FR-09, owner
/// Option B; project task 072). This is orthogonal to the Tier-2 RECORD scope on
/// <see cref="CallerPrincipal"/> (which records the caller may see): Tier-1 = "which module tabs",
/// Tier-2 = "which records within a module" (<see cref="ExternalModuleRegistry"/>).
///
/// <para><b>Option B (owner, 2026-08-10)</b> — resolved WITHOUT ever fabricating a Contact for an
/// internal user:</para>
/// <list type="bullet">
///   <item><b>Workforce (internal)</b>: the caller's Entra <b>App-Role</b> claims (<c>roles</c>) are
///   mapped through the active <c>sprk_approlemodulemap</c> rows (<c>sprk_approlename</c> →
///   <c>sprk_modulecode</c>) to a de-duplicated module-code set. No Contact record is involved. Adding
///   a role→module mapping later is a single data row — no code or schema change (FR-08).</item>
///   <item><b>CIAM (external outside counsel)</b>: BLANKET-entitled to the outside-counsel module set
///   (<see cref="OutsideCounselEntitlements"/>) — there is no per-Contact external entitlement table.
///   Record VISIBILITY is governed purely by Tier-2 grants (tasks 028 read / 070+071 write).</item>
/// </list>
///
/// <para>NFR-06: this is UX-honest entitlement DATA only — it never over-reports, and it is not the
/// security boundary (server-side Tier-1 here + Tier-2 record scope are). ADR-009: the small, global
/// App-Role→module map is cached (Redis, 60s TTL) as DATA, never an authorization decision. ADR-010:
/// registered as a concrete typed <see cref="HttpClient"/> (no interface) — a single implementation,
/// mirroring <see cref="ExternalParticipationService"/>. Broker-only (NFR-02): app-only Dataverse read,
/// no OBO, no Graph SDK / AI-internal types.</para>
/// </summary>
public class ModuleEntitlementResolver
{
    /// <summary>
    /// The outside-counsel blanket entitlement set (Option B): every CIAM external contact is entitled
    /// to the outside-counsel module family. The CIAM data widgets (Projects/Matters/Work Assignments/
    /// Documents/Invoices) are blanket-visible per plane; record visibility is Tier-2. Kept as the exact
    /// code the client's mock + widget metadata use (<c>assigned-work</c>) so the two stay aligned.
    /// </summary>
    private static readonly IReadOnlyList<string> OutsideCounselModules = new[] { "assigned-work" };

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    // ITenantCache resource key. Cached value is the small, GLOBAL App-Role→module config map (DATA per
    // ADR-009), not an authorization decision. Keyed per tenant by ITenantCache; a single logical id.
    private const string MapResource = "approle-module-map";
    private const string MapCacheId = "all";
    private const int CacheVersion = 1;

    private readonly HttpClient _httpClient;
    private readonly ITenantCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ModuleEntitlementResolver> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    public ModuleEntitlementResolver(
        HttpClient httpClient,
        ITenantCache cache,
        IConfiguration configuration,
        TokenCredential credential,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ModuleEntitlementResolver> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _configuration = configuration;
        _credential = credential;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>The outside-counsel blanket entitlement set surfaced for the endpoint + tests.</summary>
    public static IReadOnlyList<string> OutsideCounselEntitlements => OutsideCounselModules;

    /// <summary>
    /// Resolves the caller's module entitlement codes per plane (Option B). CIAM → blanket
    /// outside-counsel set; workforce → App-Role claims ∩ active <c>sprk_approlemodulemap</c>.
    /// </summary>
    /// <param name="principal">The resolved plane-agnostic caller (its <see cref="CallerPrincipal.Plane"/>
    /// selects the resolution term).</param>
    /// <param name="user">The caller's validated <see cref="ClaimsPrincipal"/> (source of the workforce
    /// App-Role <c>roles</c> claims). Ignored for the CIAM term.</param>
    public virtual async Task<IReadOnlyList<string>> ResolveAsync(
        CallerPrincipal principal, ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // External (CIAM) plane → blanket outside-counsel set. No map read, no Contact lookup.
        if (principal.Plane == CallerPrincipalPlane.CiamContact)
        {
            return OutsideCounselModules;
        }

        // Internal (workforce) plane → App-Role claims ∩ active sprk_approlemodulemap.
        var roles = ExtractAppRoles(user);
        if (roles.Count == 0)
        {
            // No App-Roles on the token → no internal modules (NFR-06: never over-report). This is the
            // negative case: an internal caller whose token carries no mapped role sees no internal tabs.
            _logger.LogInformation("[EXT-ENTITLE] Workforce caller has no App-Role claims — 0 internal modules.");
            return Array.Empty<string>();
        }

        var map = await GetActiveMapAsync(ct).ConfigureAwait(false);
        var entitlements = ResolveWorkforceEntitlements(roles, map);
        _logger.LogInformation(
            "[EXT-ENTITLE] Workforce caller with {RoleCount} App-Role(s) → {ModuleCount} module(s): {Modules}",
            roles.Count, entitlements.Count, string.Join(",", entitlements));
        return entitlements;
    }

    /// <summary>
    /// The PURE resolution core (unit-tested directly, no Dataverse): the module codes granted by the
    /// caller's App-Roles, i.e. every active mapping whose <c>sprk_approlename</c> is one of the caller's
    /// roles, de-duplicated. Case-insensitive on role name; deterministic order.
    /// </summary>
    internal static IReadOnlyList<string> ResolveWorkforceEntitlements(
        IReadOnlyCollection<string> roles, IReadOnlyCollection<AppRoleModuleMapping> map)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        return map
            .Where(m => !string.IsNullOrWhiteSpace(m.ModuleCode)
                        && !string.IsNullOrWhiteSpace(m.AppRoleName)
                        && roleSet.Contains(m.AppRoleName))
            .Select(m => m.ModuleCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Extracts the caller's Entra App-Role values from BOTH the raw <c>roles</c> claim (App-Roles land
    /// here on a v2 Entra token) and the mapped <see cref="ClaimTypes.Role"/>, de-duplicated. Returns an
    /// empty list for a null principal or a token with no roles (CIAM tokens carry none).
    /// </summary>
    internal static IReadOnlyList<string> ExtractAppRoles(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return Array.Empty<string>();
        }
        return user.FindAll("roles").Select(c => c.Value)
            .Concat(user.FindAll(ClaimTypes.Role).Select(c => c.Value))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Loads the active <c>sprk_approlemodulemap</c> rows (App-Role → module) — Redis cache first (60s
    /// TTL, ADR-009), else an app-only Dataverse read. The map is small + global; caching it once per
    /// tenant avoids a per-request round-trip while keeping FR-08 "add a row" reflection within one TTL.
    /// virtual so an endpoint-level test can stub the Dataverse read.
    /// </summary>
    public virtual async Task<IReadOnlyList<AppRoleModuleMapping>> GetActiveMapAsync(CancellationToken ct = default)
    {
        var tenantId = ExtractTenantId();

        if (!string.IsNullOrEmpty(tenantId))
        {
            try
            {
                var cached = await _cache.GetAsync<List<AppRoleModuleMapping>>(
                    tenantId, MapResource, MapCacheId, CacheVersion, ct: ct);
                if (cached != null)
                {
                    _logger.LogDebug("[EXT-ENTITLE] Cache HIT for App-Role→module map: {Count} row(s).", cached.Count);
                    return cached;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EXT-ENTITLE] Cache read error for App-Role→module map. Falling through to Dataverse.");
            }
        }

        var map = await QueryActiveMapAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(tenantId))
        {
            _ = CacheMapAsync(tenantId, map);
        }

        return map;
    }

    private async Task<IReadOnlyList<AppRoleModuleMapping>> QueryActiveMapAsync(CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            var query = $"{apiUrl}/sprk_approlemodulemaps" +
                        $"?$filter=statecode eq 0" +
                        $"&$select=sprk_approlename,sprk_modulecode";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ENTITLE] Dataverse query for App-Role→module map failed: {Status}", response.StatusCode);
                return Array.Empty<AppRoleModuleMapping>();
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<MapRow>>(ct);
            var rows = result?.Value ?? new List<MapRow>();

            var map = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.sprk_approlename) && !string.IsNullOrWhiteSpace(r.sprk_modulecode))
                .Select(r => new AppRoleModuleMapping { AppRoleName = r.sprk_approlename!, ModuleCode = r.sprk_modulecode! })
                .ToList();

            _logger.LogInformation("[EXT-ENTITLE] Loaded {Count} active App-Role→module mapping(s).", map.Count);
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ENTITLE] Error querying the App-Role→module map. Returning empty (fail-closed).");
            return Array.Empty<AppRoleModuleMapping>();
        }
    }

    private async Task CacheMapAsync(string tenantId, IReadOnlyList<AppRoleModuleMapping> map)
    {
        try
        {
            await _cache.SetAsync(tenantId, MapResource, MapCacheId, CacheVersion, map.ToList(), CacheTtl);
            _logger.LogDebug("[EXT-ENTITLE] Cached App-Role→module map ({Count} row(s), TTL {Ttl}s).", map.Count, CacheTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ENTITLE] Error caching App-Role→module map. Non-critical (degrades to per-request read).");
        }
    }

    private string? ExtractTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
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

    private sealed class DataverseQueryResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    private sealed class MapRow
    {
        [JsonPropertyName("sprk_approlename")]
        public string? sprk_approlename { get; set; }

        [JsonPropertyName("sprk_modulecode")]
        public string? sprk_modulecode { get; set; }
    }
}

/// <summary>
/// One active App-Role → module mapping (a <c>sprk_approlemodulemap</c> row projected to the two fields
/// the resolver needs). Serialized into the Redis map cache.
/// </summary>
public sealed class AppRoleModuleMapping
{
    public required string AppRoleName { get; init; }
    public required string ModuleCode { get; init; }
}
