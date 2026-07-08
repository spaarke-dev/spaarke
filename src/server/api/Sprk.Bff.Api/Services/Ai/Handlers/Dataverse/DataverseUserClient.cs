using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// User-OBO implementation of <see cref="IDataverseUserClient"/> — the ONLY Dataverse access
/// path reachable from the <c>dataverse.*</c> tool namespace (FR-P0-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>OBO enforcement (spec MUST rule)</b>: the caller's bearer token is extracted from the
/// ambient <c>HttpContext</c> (chat tool invocations run inside the SSE request) and exchanged
/// via MSAL <c>AcquireTokenOnBehalfOf</c> for a <c>{env}/.default</c> Dataverse token. The
/// resulting token carries the USER's identity, so Dataverse enforces the user's security
/// roles + row-level security on every call — identical guarantee to the GA Dataverse MCP
/// server's delegated <c>mcp.tools</c> model (see research note, FAQ verbatim: "The Dataverse
/// MCP server respects Dataverse security roles and row-level security").
/// </para>
/// <para>
/// <b>No app-only path exists in this type</b>: there is deliberately no
/// <c>TokenCredential</c> / <c>DefaultAzureCredential</c> / client-credentials flow here.
/// When the user context or OBO configuration is missing the call fails closed. Task-012's
/// user-OBO audit greps this file for that guarantee.
/// </para>
/// <para>
/// Confidential-client configuration mirrors the proven OBO precedents
/// (<c>AgentTokenService</c>, <c>DataverseAccessDataSource</c>): primary keys
/// <c>AzureAd:TenantId/ClientId/ClientSecret</c> (the BFF's own registration — required
/// because the inbound token's audience is the BFF app), with legacy fallbacks
/// <c>TENANT_ID</c>/<c>API_APP_ID</c>/<c>API_CLIENT_SECRET</c>. Dataverse environment URL from
/// <see cref="DataverseOptions.EnvironmentUrl"/> (fallback <c>Dataverse:ServiceUrl</c>).
/// MSAL's built-in user token cache de-duplicates exchanges per user assertion.
/// </para>
/// <para>
/// ADR-015 / NFR-07: logs carry status codes, durations, and path CLASS (never full query
/// strings, which may embed user-authored filter literals; never response bodies; never
/// tokens).
/// </para>
/// </remarks>
public sealed class DataverseUserClient : IDataverseUserClient
{
    /// <summary>
    /// Process-wide MSAL confidential-client cache keyed by (authority, clientId). This type is
    /// a typed HttpClient (transient); constructing a fresh CCA per instance would discard
    /// MSAL's in-memory user token cache and force an OBO network exchange on EVERY tool call.
    /// Sharing the CCA amortizes OBO exchanges across calls within/across chat turns while the
    /// per-user isolation stays intact (MSAL caches OBO tokens per user assertion).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IConfidentialClientApplication>
        CcaCache = new();

    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DataverseUserClient> _logger;
    private readonly string? _environmentUrl;
    private readonly IConfidentialClientApplication? _cca;

    public DataverseUserClient(
        HttpClient httpClient,
        IOptions<DataverseOptions> dataverseOptions,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DataverseUserClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = dataverseOptions?.Value;
        _environmentUrl = FirstNonEmpty(
            options?.EnvironmentUrl,
            configuration["Dataverse:ServiceUrl"])?.TrimEnd('/');

        // BFF confidential client (audience of the inbound user token). AzureAd:* is the
        // canonical OBO configuration (module CLAUDE.md); TENANT_ID / API_APP_ID /
        // API_CLIENT_SECRET are the legacy keys DataverseAccessDataSource uses.
        var tenantId = FirstNonEmpty(configuration["AzureAd:TenantId"], configuration["TENANT_ID"]);
        var clientId = FirstNonEmpty(configuration["AzureAd:ClientId"], configuration["API_APP_ID"]);
        var clientSecret = FirstNonEmpty(configuration["AzureAd:ClientSecret"], configuration["API_CLIENT_SECRET"]);

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            // Shared per (tenant, client) so MSAL's user token cache survives across the
            // transient typed-HttpClient lifetimes — see CcaCache doc comment.
            _cca = CcaCache.GetOrAdd($"{tenantId}|{clientId}", _ =>
                ConfidentialClientApplicationBuilder
                    .Create(clientId)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                    .WithClientSecret(clientSecret)
                    .Build());
            _logger.LogDebug("[dataverse.*] DataverseUserClient resolved OBO confidential client (user-context only; no app-only path)");
        }
        else
        {
            // Fail-closed by construction: without OBO config, every call errors. We do NOT
            // fall back to managed identity / client credentials — that would create the
            // app-only authorization side-channel the spec MUST rule forbids.
            _cca = null;
            _logger.LogWarning("[dataverse.*] DataverseUserClient missing OBO configuration — dataverse.* tools will fail closed (no app-only fallback by design)");
        }
    }

    /// <inheritdoc />
    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/api/data/v9.2/{relativePath.TrimStart('/')}", jsonBody: null, cancellationToken);

    /// <inheritdoc />
    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, absoluteApiPath, jsonBody, cancellationToken);

    /// <inheritdoc />
    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, absoluteApiPath, jsonBody, cancellationToken, preferRepresentation: preferRepresentation);

    /// <inheritdoc />
    /// <remarks>
    /// Always sends <c>If-Match: *</c> (update-only; never upsert-creates) — see the interface
    /// remark. Task-009 write semantics: a record that does not exist or is invisible to the
    /// calling user yields the user's own 404, never a silent create.
    /// </remarks>
    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, $"/api/data/v9.2/{relativePath.TrimStart('/')}", jsonBody, cancellationToken, ifMatchAnyExisting: true);

    /// <inheritdoc />
    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/api/data/v9.2/{relativePath.TrimStart('/')}", jsonBody: null, cancellationToken);

    private async Task<DataverseUserResponse> SendAsync(
        HttpMethod method,
        string apiPath,
        string? jsonBody,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        bool ifMatchAnyExisting = false)
    {
        if (string.IsNullOrEmpty(_environmentUrl))
        {
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboNotConfigured,
                "Dataverse environment URL is not configured (Dataverse:EnvironmentUrl).");
        }

        if (_cca is null)
        {
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboNotConfigured,
                "OBO confidential-client configuration is missing; dataverse.* tools require user-context OBO and have no app-only fallback.");
        }

        // MUST rule: resolve the CALLING USER's identity from the request context. No user →
        // no Dataverse call. Never substitute a service identity.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No HTTP request context is available; dataverse.* tools execute only under an authenticated user's session.");
        }

        string userToken;
        try
        {
            userToken = TokenHelper.ExtractBearerToken(httpContext);
        }
        catch (UnauthorizedAccessException)
        {
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session.");
        }

        string dataverseToken;
        try
        {
            // MSAL user token cache makes repeat exchanges within a chat turn cheap.
            var result = await _cca.AcquireTokenOnBehalfOf(
                    new[] { $"{_environmentUrl}/.default" },
                    new UserAssertion(userToken))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            dataverseToken = result.AccessToken;
        }
        catch (MsalException ex)
        {
            // ADR-015: MSAL error CODE only — never the assertion or token material.
            _logger.LogWarning("[dataverse.*] OBO exchange failed: {ErrorCode}", ex.ErrorCode);
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed,
                $"On-Behalf-Of token exchange for Dataverse failed ({ex.ErrorCode}).");
        }

        using var request = new HttpRequestMessage(method, $"{_environmentUrl}{apiPath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (apiPath.StartsWith("/api/data/", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
        }
        if (preferRepresentation)
        {
            // dataverse.create_record: echo the created row so the handler can extract the
            // primary id for citation referencing (task-009; task-008 review suggestion).
            request.Headers.Add("Prefer", "return=representation");
        }
        if (ifMatchAnyExisting)
        {
            // dataverse.update_record: update-only — never let Web API PATCH upsert-create.
            request.Headers.TryAddWithoutValidation("If-Match", "*");
        }
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var startedAt = TimeProvider.System.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("[dataverse.*] Dataverse request transport failure: {ExceptionType}", ex.GetType().Name);
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError,
                "Dataverse request failed at the transport layer.");
        }

        using (response)
        {
            var elapsedMs = TimeProvider.System.GetElapsedTime(startedAt).TotalMilliseconds;
            // NFR-07: path CLASS (first two segments), status, duration — never full query
            // strings (may embed user-authored literals), never bodies, never tokens.
            _logger.LogInformation(
                "[dataverse.*][ADR-015] {Method} {PathClass} → {StatusCode} in {ElapsedMs:F0}ms (userObo=true)",
                method.Method, GetPathClass(apiPath), (int)response.StatusCode, elapsedMs);

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
                {
                    return DataverseUserResponse.Ok((int)response.StatusCode, body: null);
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return DataverseUserResponse.Ok((int)response.StatusCode, body: null);
                }

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    return DataverseUserResponse.Ok((int)response.StatusCode, doc.RootElement.Clone());
                }
                catch (JsonException)
                {
                    return DataverseUserResponse.Fail((int)response.StatusCode, DataverseUserClientErrorCodes.ServiceError,
                        "Dataverse returned a non-JSON success response.");
                }
            }

            var mappedCode = MapErrorCode((int)response.StatusCode);
            var dataverseMessage = await TryReadErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
            return DataverseUserResponse.Fail((int)response.StatusCode, mappedCode,
                dataverseMessage ?? $"Dataverse returned HTTP {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// Maps HTTP status codes to the shared <see cref="DataverseUserClientErrorCodes"/>
    /// vocabulary (reused verbatim by the task-009 write handlers).
    /// </summary>
    internal static string MapErrorCode(int statusCode) => statusCode switch
    {
        401 or 403 => DataverseUserClientErrorCodes.AccessDenied,
        404 => DataverseUserClientErrorCodes.NotFound,
        400 or 405 or 412 or 422 => DataverseUserClientErrorCodes.BadRequest,
        429 => DataverseUserClientErrorCodes.RateLimited,
        _ => DataverseUserClientErrorCodes.ServiceError
    };

    /// <summary>First path segment pair for telemetry — e.g. <c>/api/data</c> or <c>/api/search</c>.</summary>
    internal static string GetPathClass(string apiPath)
    {
        var segments = apiPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"/{segments[0]}/{segments[1]}" : apiPath;
    }

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
