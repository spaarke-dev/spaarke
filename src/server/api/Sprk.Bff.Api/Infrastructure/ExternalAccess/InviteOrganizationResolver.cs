// Slice B2 — which sign-in an invite is for, decided by the server from the invited email's domain. Staff never type a
// tenant id. A work-account invite is made only for a domain whose Entra tenant is one of this deployment's customer
// tenants (WorkforceIdentity:CustomerTenantIds, the A9 list that gates both workforce token schemes); every other outcome
// that is a definite answer is a portal-account (CIAM) invite, exactly as before. A lookup that gives no definite answer
// is a fault the invite reports (503): a failed lookup could hide a customer tenant, so it never falls back silently.

using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>How an invite's organisation lookup ended.</summary>
public enum InviteOrganizationStatus
{
    /// <summary>Decided: <see cref="InviteOrganizationDecision.WorkAccountTenantId"/> names the customer tenant, or is null for a portal account.</summary>
    Decided,

    /// <summary>The email's domain is not a host name. A 400 at invite time; nothing was sent over the network.</summary>
    MalformedDomain,

    /// <summary>The lookup gave no definite answer (fault, timeout, unexpected response). A 503; nothing may be written.</summary>
    LookupUnavailable,
}

/// <summary>The organisation decision for one invite.</summary>
/// <param name="Status">How the lookup ended.</param>
/// <param name="WorkAccountTenantId">
/// When <see cref="InviteOrganizationStatus.Decided"/>: the customer tenant a work-account invite names, or null for a
/// portal-account (CIAM) invite.
/// </param>
public sealed record InviteOrganizationDecision(InviteOrganizationStatus Status, Guid? WorkAccountTenantId)
{
    /// <summary>A portal-account (CIAM) invite.</summary>
    public static InviteOrganizationDecision PortalAccount { get; } = new(InviteOrganizationStatus.Decided, null);

    /// <summary>A work-account invite for <paramref name="tenantId"/>.</summary>
    public static InviteOrganizationDecision WorkAccount(Guid tenantId) => new(InviteOrganizationStatus.Decided, tenantId);

    /// <summary>True for a work-account invite.</summary>
    public bool IsWorkAccount => Status == InviteOrganizationStatus.Decided && WorkAccountTenantId is not null;
}

/// <summary>
/// Decides whether an invite is a work-account invite (a configured customer tenant) or a portal-account invite, from the
/// invited email's domain, through Microsoft Entra's public OpenID discovery document.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The staff override (<c>sendAsPortalAccount</c>) is always a portal account, and so is a deployment with no
/// customer tenants: neither makes a call.</item>
/// <item>The domain is normalised (trim, lower-case, one trailing dot stripped, IDN to punycode) and must then be a plain
/// host name (<see cref="NormaliseDomain"/>); anything else is <see cref="InviteOrganizationStatus.MalformedDomain"/> and
/// never reaches the network.</item>
/// <item>The call is a GET of <c>https://login.microsoftonline.com/{domain}/v2.0/.well-known/openid-configuration</c>:
/// fixed scheme and host, no credentials, no cookies, no redirects followed (the named client
/// <see cref="HttpClientName"/>), a <see cref="DefaultLookupTimeout"/> timeout.</item>
/// <item>A 200 whose <c>issuer</c> is <c>https://login.microsoftonline.com/{tenant GUID}/v2.0</c> is that tenant. A 400
/// whose body carries AADSTS90002 (the tenant was not found) is "no tenant". Everything else is a fault.</item>
/// <item>A tenant in <c>WorkforceIdentity:CustomerTenantIds</c> is a work account, unless it is the consumer (Microsoft
/// account) tenant or this deployment's CIAM tenant. Any other tenant, or no tenant, is a portal account.</item>
/// <item>Cached through <see cref="ITenantCache"/> (ADR-009), keyed on the normalised domain only, holding only the parsed
/// tenant or "no tenant": a tenant for <see cref="FoundTtl"/>, no tenant for <see cref="NotFoundTtl"/>, a fault never. A
/// cache that cannot be read or written is skipped, never a fault. The customer-tenant test is applied after the cache, so a
/// change to the list applies at once.</item>
/// </list>
/// <para>Logs name the domain and the outcome, never the email address.</para>
/// </remarks>
public sealed class InviteOrganizationResolver
{
    /// <summary>The named client: no auth handler, no default headers, no cookies, no redirects (ExternalAccessModule).</summary>
    public const string HttpClientName = "EntraTenantDiscovery";

    /// <summary>The one host the lookup calls.</summary>
    public const string DiscoveryHost = "login.microsoftonline.com";

    /// <summary>The lookup's timeout.</summary>
    public static readonly TimeSpan DefaultLookupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a found tenant is cached.</summary>
    public static readonly TimeSpan FoundTtl = TimeSpan.FromHours(24);

    /// <summary>How long "no tenant" is cached: short, so a domain an organisation adds to Entra soon resolves.</summary>
    public static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(15);

    /// <summary>The Microsoft account (consumer) tenant: never a work account.</summary>
    public static readonly Guid ConsumerTenantId = Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad");

    /// <summary>The <see cref="ITenantCache"/> resource of a cached lookup.</summary>
    internal const string CacheResource = "invite-org-discovery";

    /// <summary>The cached value's schema version.</summary>
    internal const int CacheVersion = 1;

    /// <summary>The largest response body read.</summary>
    private const int MaxBodyChars = 64 * 1024;

    private const int TenantNotFoundErrorCode = 90002;

    private readonly IHttpClientFactory _httpClients;
    private readonly ITenantCache _cache;
    private readonly IOptionsMonitor<WorkforceIdentityOptions> _workforce;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InviteOrganizationResolver> _logger;
    private readonly TimeSpan _lookupTimeout;

    /// <summary>Creates the resolver (DI).</summary>
    public InviteOrganizationResolver(
        IHttpClientFactory httpClients,
        ITenantCache cache,
        IOptionsMonitor<WorkforceIdentityOptions> workforce,
        IConfiguration configuration,
        ILogger<InviteOrganizationResolver> logger)
        : this(httpClients, cache, workforce, configuration, logger, DefaultLookupTimeout)
    {
    }

    /// <summary>Creates the resolver with another timeout (tests).</summary>
    internal InviteOrganizationResolver(
        IHttpClientFactory httpClients,
        ITenantCache cache,
        IOptionsMonitor<WorkforceIdentityOptions> workforce,
        IConfiguration configuration,
        ILogger<InviteOrganizationResolver> logger,
        TimeSpan lookupTimeout)
    {
        _httpClients = httpClients ?? throw new ArgumentNullException(nameof(httpClients));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _workforce = workforce ?? throw new ArgumentNullException(nameof(workforce));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lookupTimeout = lookupTimeout;
    }

    /// <summary>
    /// Which sign-in the invite to <paramref name="email"/> is for. Never throws for a lookup fault (that is
    /// <see cref="InviteOrganizationStatus.LookupUnavailable"/>); a cancelled <paramref name="ct"/> propagates.
    /// </summary>
    /// <param name="email">The invited address (only its domain is used; never logged).</param>
    /// <param name="sendAsPortalAccount">The staff override: a portal-account invite, no lookup.</param>
    /// <param name="cacheTenantId">The caller's tenant, which scopes the cache key (ADR-009); null skips the cache.</param>
    /// <param name="ct">The request's cancellation.</param>
    public async Task<InviteOrganizationDecision> DecideAsync(
        string email, bool sendAsPortalAccount, string? cacheTenantId, CancellationToken ct)
    {
        if (sendAsPortalAccount)
        {
            return InviteOrganizationDecision.PortalAccount;
        }

        var customers = _workforce.CurrentValue.ParsedCustomerTenantIds();
        if (customers.Count == 0)
        {
            return InviteOrganizationDecision.PortalAccount;
        }

        if (NormaliseDomain(email) is not { } domain)
        {
            return new InviteOrganizationDecision(InviteOrganizationStatus.MalformedDomain, null);
        }

        var lookup = await ReadCacheAsync(cacheTenantId, domain, ct).ConfigureAwait(false);
        if (lookup is null)
        {
            lookup = await LookUpAsync(domain, ct).ConfigureAwait(false);
            if (lookup is null)
            {
                return new InviteOrganizationDecision(InviteOrganizationStatus.LookupUnavailable, null);
            }

            await WriteCacheAsync(cacheTenantId, domain, lookup, ct).ConfigureAwait(false);
        }

        var tenant = lookup.TenantId;
        if (tenant is { } tid && customers.Contains(tid) && tid != ConsumerTenantId && !IsCiamTenant(tid))
        {
            _logger.LogInformation(
                "[EXT-INVITE] Domain {Domain} belongs to customer tenant {TenantId}: a work-account invite.", domain, tid);
            return InviteOrganizationDecision.WorkAccount(tid);
        }

        _logger.LogInformation(
            "[EXT-INVITE] Domain {Domain}: {Outcome}; a portal-account invite.",
            domain, tenant is null ? "no Entra tenant" : "not a customer tenant");
        return InviteOrganizationDecision.PortalAccount;
    }

    /// <summary>
    /// The domain of <paramref name="email"/> as a plain host name, or null when it is not one. Normalised: trimmed,
    /// lower-cased, one trailing dot stripped, IDN converted to punycode. Then: at most 253 characters, at least two labels,
    /// each 1 to 63 characters of <c>[a-z0-9-]</c> not starting or ending with a hyphen. The address must hold exactly one
    /// <c>@</c> with something before it. Pure.
    /// </summary>
    internal static string? NormaliseDomain(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@'))
        {
            return null;
        }

        var domain = trimmed[(at + 1)..].ToLowerInvariant();
        if (domain.EndsWith('.'))
        {
            domain = domain[..^1];
        }

        if (domain.Length == 0)
        {
            return null;
        }

        if (domain.Any(c => c > 0x7F))
        {
            try
            {
                domain = new IdnMapping().GetAscii(domain).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        if (domain.Length > 253)
        {
            return null;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return null;
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-'
                || !label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return null;
            }
        }

        return domain;
    }

    /// <summary>
    /// The tenant an OpenID discovery <c>issuer</c> names: exactly <c>https://login.microsoftonline.com/{GUID}/v2.0</c>
    /// with a non-empty GUID, else null. Pure.
    /// </summary>
    internal static Guid? TenantFromIssuer(string? issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, DiscoveryHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        return segments.Length == 2
               && string.Equals(segments[1], "v2.0", StringComparison.Ordinal)
               && Guid.TryParse(segments[0], out var tenant)
               && tenant != Guid.Empty
            ? tenant
            : null;
    }

    /// <summary>The discovery URL for an already-normalised <paramref name="domain"/>. Pure.</summary>
    internal static Uri DiscoveryUri(string domain)
        => new UriBuilder(Uri.UriSchemeHttps, DiscoveryHost)
        {
            Path = $"/{domain}/v2.0/.well-known/openid-configuration",
        }.Uri;

    /// <summary>
    /// One discovery call: the tenant, "no tenant" (a definite not-found), or null for a fault (logged).
    /// </summary>
    private async Task<DiscoveredTenant?> LookUpAsync(string domain, CancellationToken ct)
    {
        var uri = DiscoveryUri(domain);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, DiscoveryHost, StringComparison.Ordinal))
        {
            return Fault(domain, "the discovery URL is not the fixed host");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_lookupTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.Content.Headers.ContentLength is > MaxBodyChars)
            {
                return Fault(domain, $"HTTP {(int)response.StatusCode} with an oversized body");
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (body.Length > MaxBodyChars)
            {
                return Fault(domain, $"HTTP {(int)response.StatusCode} with an oversized body");
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return TenantFromIssuer(StringProperty(body, "issuer")) is { } tenant
                        ? new DiscoveredTenant(tenant)
                        : Fault(domain, "HTTP 200 whose issuer names no tenant");

                case HttpStatusCode.BadRequest when IsTenantNotFound(body):
                    return new DiscoveredTenant(null);

                default:
                    return Fault(domain, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fault(domain, "timed out");
        }
        catch (HttpRequestException ex)
        {
            return Fault(domain, $"request failed ({ex.HttpRequestError})");
        }
    }

    private DiscoveredTenant? Fault(string domain, string reason)
    {
        _logger.LogWarning(
            "[EXT-INVITE] The organisation lookup for domain {Domain} gave no definite answer ({Reason}); the invite is not "
            + "sent (tenant_lookup_unavailable).", domain, reason);
        return null;
    }

    /// <summary>True when a 400 body says the tenant was not found (AADSTS90002). Pure.</summary>
    internal static bool IsTenantNotFound(string? body)
    {
        using var doc = TryParse(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var root = doc.RootElement;
        if (root.TryGetProperty("error_codes", out var codes) && codes.ValueKind == JsonValueKind.Array
            && codes.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n)
                                               && n == TenantNotFoundErrorCode))
        {
            return true;
        }

        return root.TryGetProperty("error_description", out var description)
               && description.ValueKind == JsonValueKind.String
               && description.GetString()!.StartsWith("AADSTS90002:", StringComparison.Ordinal);
    }

    private static string? StringProperty(string? body, string name)
    {
        using var doc = TryParse(body);
        return doc is not null
               && doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static JsonDocument? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool IsCiamTenant(Guid tenant)
        => Guid.TryParse(_configuration["Ciam:TenantId"]?.Trim(), out var ciam) && ciam == tenant;

    private async Task<DiscoveredTenant?> ReadCacheAsync(string? cacheTenantId, string domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheTenantId))
        {
            return null;
        }

        try
        {
            return await _cache.GetAsync<DiscoveredTenant>(cacheTenantId, CacheResource, domain, CacheVersion, ct: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[EXT-INVITE] The organisation lookup cache could not be read for domain {Domain}; looking up.",
                domain);
            return null;
        }
    }

    private async Task WriteCacheAsync(string? cacheTenantId, string domain, DiscoveredTenant lookup, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheTenantId))
        {
            return;
        }

        try
        {
            await _cache.SetAsync(cacheTenantId, CacheResource, domain, CacheVersion, lookup,
                lookup.TenantId is null ? NotFoundTtl : FoundTtl, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[EXT-INVITE] The organisation lookup for domain {Domain} could not be cached.", domain);
        }
    }

    /// <summary>A lookup's definite answer: the tenant, or null for "no Entra tenant". The only value cached.</summary>
    internal sealed record DiscoveredTenant(Guid? TenantId);
}
