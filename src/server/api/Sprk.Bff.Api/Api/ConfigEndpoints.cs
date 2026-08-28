using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Anonymous client configuration endpoints.
///
/// Two endpoints live here:
///
///   1. GET /api/config/client (AIPU-091) — narrow MSAL bootstrap FALLBACK for
///      the SpaarkeAi Code Page (and its LegalWorkspace embed) when Xrm context
///      + localStorage cache are BOTH absent. Returns MSAL client ID, authority,
///      scopes, BFF base URL. Derives values from AzureAd:* / request.Host.
///
///   2. GET /api/config (FR-36 — this project) — canonical runtime public
///      config bundle for external-spa + code-pages, closing the bake-at-build-time
///      pattern. Returns { bffUrl, msalClientId, tenantId, featureFlags }. Backed
///      by Tier-1 <see cref="PublicConfigOptions"/> (validated at startup per r3
///      task 061). Short-cached (60s) with an ETag so consumers can revalidate
///      without re-transferring the body.
///
/// SECURITY (both endpoints):
///   MUST NOT return secrets (client secret, API keys, KV references, connection
///   strings). Returns only client-side values required to initiate an interactive
///   auth flow + advisory feature flags.
///
/// PLACEMENT (per CLAUDE.md §10 + §11): extends this existing file rather than
/// adding a sibling file with duplicated concerns. Both endpoints answer "what
/// non-sensitive config does the caller need at cold-load" — a single owner.
///
/// All endpoints follow ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
public static class ConfigEndpoints
{
    // Cached JSON serializer options — mirror ASP.NET Core's default camelCase
    // policy so the emitted body matches the property naming Results.Ok(...)
    // would produce (i.e. bffUrl, not BffUrl). Kept as a static singleton so
    // the ETag hash is stable across requests.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    // Short-cache: 60s per POML acceptance criterion. Chosen to reduce origin
    // load while allowing feature-flag toggles to propagate within a minute.
    private const int PublicConfigMaxAgeSeconds = 60;

    /// <summary>
    /// Registers the anonymous client configuration endpoint.
    /// Called from EndpointMappingExtensions.MapSpaarkeEndpoints().
    /// </summary>
    public static IEndpointRouteBuilder MapMsalConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/config/client — anonymous, returns MSAL client config
        app.MapGet("/api/config/client", GetClientConfig)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous") // Task AUTHV2-049 — 10/min per IP
            .WithName("GetMsalClientConfig")
            .WithTags("Configuration")
            .WithSummary("Get non-sensitive client configuration for MSAL bootstrap")
            .WithDescription(
                "Returns MSAL client ID, authority, scopes, and BFF base URL. " +
                "Anonymous — used by the Code Page when Xrm context is unavailable " +
                "(direct URL access without the Dataverse MDA shell).")
            .Produces<ClientConfigResponse>(200)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    /// <summary>
    /// Registers the canonical public runtime config endpoint (FR-36).
    /// Called from EndpointMappingExtensions.MapSpaarkeEndpoints().
    /// </summary>
    public static IEndpointRouteBuilder MapPublicConfigEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /api/config — anonymous, returns the FR-36 public config bundle
        app.MapGet("/api/config", GetPublicConfig)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous")
            .WithName("GetPublicConfig")
            .WithTags("Configuration")
            .WithSummary("Public runtime config bundle (FR-36)")
            .WithDescription(
                "Returns { bffUrl, msalClientId, tenantId, featureFlags } — the per-env " +
                "public config bundle browser clients fetch at bootstrap, closing the " +
                "bake-at-build-time pattern (customer-provisioning-orchestration-r1 task 087).")
            .Produces<PublicConfigResponse>(200)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    /// <summary>
    /// Returns non-sensitive MSAL client configuration.
    /// Reads AzureAd:ClientId, AzureAd:TenantId, AzureAd:Instance from IConfiguration.
    /// HttpContext is injected automatically by Minimal API — no IHttpContextAccessor needed.
    /// </summary>
    private static IResult GetClientConfig(
        IConfiguration configuration,
        HttpContext httpContext)
    {
        var clientId = configuration["AzureAd:ClientId"];
        var tenantId = configuration["AzureAd:TenantId"];
        var instance = configuration["AzureAd:Instance"]
            ?? "https://login.microsoftonline.com/";

        if (string.IsNullOrEmpty(clientId))
        {
            return Results.Problem(
                detail: "AzureAd:ClientId is not configured.",
                statusCode: 500,
                title: "Configuration Error");
        }

        // Build MSAL authority — instance already ends with '/', append tenantId
        // Example: https://login.microsoftonline.com/{tenantId}
        var authority = tenantId is not null and not "common" and not "organizations"
            ? $"{instance.TrimEnd('/')}/{tenantId}"
            : $"{instance.TrimEnd('/')}/organizations";

        // BFF base URL: derive from the current request's origin
        // (same host that served this response is the BFF host)
        var request = httpContext.Request;
        var bffBaseUrl = $"{request.Scheme}://{request.Host}";

        // OAuth scope for the BFF API
        var scope = $"api://{clientId}/user_impersonation";

        var response = new ClientConfigResponse(
            BffBaseUrl: bffBaseUrl,
            MsalClientId: clientId,
            MsalAuthority: authority,
            MsalScopes: [scope],
            TenantId: tenantId ?? string.Empty);

        return Results.Ok(response);
    }

    /// <summary>
    /// Returns the canonical FR-36 public runtime config bundle.
    /// Sourced from Tier-1 <see cref="PublicConfigOptions"/> — startup fails
    /// via ValidateOnStart() if BffUrl/MsalClientId/TenantId are missing, so
    /// this handler never observes an unbound options instance.
    ///
    /// Cache semantics:
    ///   - Cache-Control: public, max-age=60 (short — allows flag propagation)
    ///   - ETag: SHA256 of the serialized body (stable-per-config-value)
    ///   - Honors If-None-Match with 304 Not Modified
    /// </summary>
    private static IResult GetPublicConfig(
        IOptions<PublicConfigOptions> options,
        HttpContext httpContext)
    {
        var config = options.Value;

        var response = new PublicConfigResponse(
            BffUrl: config.BffUrl,
            MsalClientId: config.MsalClientId,
            TenantId: config.TenantId,
            FeatureFlags: config.FeatureFlags);

        // Compute a stable ETag over the serialized JSON body so consumers can
        // revalidate cheaply. Kept opaque (base64 SHA256) — the client MUST NOT
        // interpret it, only round-trip it via If-None-Match.
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, ResponseJsonOptions);
        var etag = ComputeEtag(payload);

        // If-None-Match revalidation short-circuit — return 304 without a body
        // when the client already has the current version cached.
        var ifNoneMatch = httpContext.Request.Headers[HeaderNames.IfNoneMatch].ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
        {
            httpContext.Response.Headers[HeaderNames.ETag] = etag;
            httpContext.Response.Headers[HeaderNames.CacheControl] = $"public, max-age={PublicConfigMaxAgeSeconds}";
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        httpContext.Response.Headers[HeaderNames.ETag] = etag;
        httpContext.Response.Headers[HeaderNames.CacheControl] = $"public, max-age={PublicConfigMaxAgeSeconds}";

        // Return the same JSON we hashed to guarantee ETag correctness.
        return Results.Content(
            content: Encoding.UTF8.GetString(payload),
            contentType: "application/json; charset=utf-8",
            statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Computes a strong ETag as base64(SHA256(body)) wrapped in the RFC 7232
    /// double-quote token. Strong (not W/"...") because the body is byte-stable
    /// for a given options snapshot.
    /// </summary>
    private static string ComputeEtag(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return "\"" + Convert.ToBase64String(hash) + "\"";
    }

    /// <summary>
    /// Response model for GET /api/config/client.
    /// Contains only non-sensitive MSAL configuration values.
    /// </summary>
    internal record ClientConfigResponse(
        string BffBaseUrl,
        string MsalClientId,
        string MsalAuthority,
        string[] MsalScopes,
        string TenantId);

    /// <summary>
    /// Response model for GET /api/config (FR-36).
    /// Serialized with camelCase so wire property names are bffUrl, msalClientId,
    /// tenantId, featureFlags — matching the shape browser clients consume.
    /// </summary>
    internal record PublicConfigResponse(
        string BffUrl,
        string MsalClientId,
        string TenantId,
        Dictionary<string, bool> FeatureFlags);
}
