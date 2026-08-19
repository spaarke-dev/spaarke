// -----------------------------------------------------------------------------
// GraphRestAppRoleGranter.cs
//
// Production IGraphAppRoleGranter — raw Microsoft Graph REST calls (NOT the
// Microsoft.Graph SDK; see H10DataverseAppUserGraphParityHandler.cs file
// header "NFR-09 IMPLEMENTATION NOTE" for the Path-C rationale) via
// HttpClient + DefaultAzureCredential. Endpoint shapes mirror
// scripts/Grant-GraphAppRoles.ps1 (task 015) exactly:
//   GET  /v1.0/servicePrincipals?$filter=appId eq '{graphResourceAppId}'
//   GET  /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments
//   POST /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments
//        { principalId, resourceId, appRoleId }
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5 — never a
// default-tenant credential); scope "https://graph.microsoft.com/.default".
//
// NOT under test in the CI unit suite (real Graph REST calls). Handler unit
// tests substitute a fake IGraphAppRoleGranter.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IGraphAppRoleGranter"/>
public sealed class GraphRestAppRoleGranter : IGraphAppRoleGranter
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly IGraphAppRolesRegistry _registry;
    private readonly ILogger<GraphRestAppRoleGranter> _logger;

    public GraphRestAppRoleGranter(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleGranter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _registry = registry;
        _logger = logger;
        _httpClient.Timeout = options.Value.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<GraphAppRoleGrantOutcome> GrantRolesAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiServicePrincipalObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(expectedRoles);

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(
                new TokenRequestContext(GraphScope), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        string graphSpId;
        try
        {
            graphSpId = await ResolveGraphResourceSpIdAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Failed to resolve Microsoft Graph resource service principal: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        HashSet<string> currentAppRoleIds;
        try
        {
            currentAppRoleIds = await ReadCurrentAppRoleIdsAsync(
                token, uamiServicePrincipalObjectId, graphSpId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Failed to read current UAMI app-role assignments: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        var failedRoles = new List<string>();
        var grantedCount = 0;
        foreach (var role in expectedRoles)
        {
            if (string.IsNullOrWhiteSpace(role.AppRoleId))
            {
                // Escalation gate (handler-level, ran BEFORE this call) already
                // refuses on any null AppRoleId — defensive skip here in case
                // this seam is ever invoked directly (e.g. from a future
                // reconciler path) without the gate re-run.
                failedRoles.Add(role.Value);
                continue;
            }

            if (currentAppRoleIds.Contains(role.AppRoleId))
            {
                grantedCount++;
                continue;
            }

            try
            {
                await PostGrantAsync(token, uamiServicePrincipalObjectId, graphSpId, role.AppRoleId, cancellationToken)
                    .ConfigureAwait(false);
                grantedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Grant FAILED for role {RoleValue} ({AppRoleId}) on UAMI SP {UamiSpId}",
                    role.Value, role.AppRoleId, uamiServicePrincipalObjectId);
                failedRoles.Add(role.Value);
                // Per Grant-GraphAppRoles.ps1: NEVER silent-skip — record + continue
                // attempting the remaining roles so one bad grant doesn't mask
                // diagnostics for the others.
            }
        }

        if (failedRoles.Count > 0)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"{failedRoles.Count} of {expectedRoles.Count} role grant(s) failed: {string.Join(", ", failedRoles)}.",
                failedRoles);
        }

        return new GraphAppRoleGrantOutcome.Success(grantedCount);
    }

    private async Task<string> ResolveGraphResourceSpIdAsync(AccessToken token, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"appId eq '{_registry.GraphResourceAppId}'");
        var uri = new Uri($"https://graph.microsoft.com/v1.0/servicePrincipals?$filter={filter}&$select=id,displayName");
        using var doc = await GetJsonAsync(uri, token, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Microsoft Graph resource service principal (appId={_registry.GraphResourceAppId}) not found in tenant.");
        }
        return values[0].GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Graph resource SP lookup returned a null id.");
    }

    private async Task<HashSet<string>> ReadCurrentAppRoleIdsAsync(
        AccessToken token, string uamiSpId, string graphSpId, CancellationToken ct)
    {
        var uri = new Uri($"https://graph.microsoft.com/v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments");
        using var doc = await GetJsonAsync(uri, token, ct).ConfigureAwait(false);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var entry in values.EnumerateArray())
        {
            var resourceId = entry.TryGetProperty("resourceId", out var r) ? r.GetString() : null;
            if (!string.Equals(resourceId, graphSpId, StringComparison.OrdinalIgnoreCase)) continue;
            var appRoleId = entry.TryGetProperty("appRoleId", out var a) ? a.GetString() : null;
            if (!string.IsNullOrWhiteSpace(appRoleId)) result.Add(appRoleId);
        }
        return result;
    }

    private async Task PostGrantAsync(AccessToken token, string uamiSpId, string graphSpId, string appRoleId, CancellationToken ct)
    {
        var uri = new Uri($"https://graph.microsoft.com/v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments");
        var payload = new Dictionary<string, object?>
        {
            ["principalId"] = uamiSpId,
            ["resourceId"] = graphSpId,
            ["appRoleId"] = appRoleId,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // Graph SDK v6 / Kiota 2.0 callers catch ODataError with an int
            // ResponseStatusCode + dict ResponseHeaders (NFR-09). This raw-HTTP
            // path surfaces the functional equivalent — status code + body —
            // in the exception message so the failure classification + operator
            // diagnostic carry the same information without the SDK dependency
            // (see file-header Path-C note).
            throw new InvalidOperationException(
                $"POST appRoleAssignments failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 300)}");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, AccessToken token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {uri} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 300)}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
