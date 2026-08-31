// -----------------------------------------------------------------------------
// GraphRestAppRoleParityVerifier.cs
//
// Production IGraphAppRoleParityVerifier — the T3 silent-fail trap post-
// condition check (spec.md FR-33). Raw Microsoft Graph REST call (same
// Path-C rationale as GraphRestAppRoleGranter.cs) — independently re-reads
// GET /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments (Graph-resource-
// scoped) and asserts every expected AppRoleId is present.
//
// NOT under test in the CI unit suite (real Graph REST call). Handler unit
// tests substitute a fake IGraphAppRoleParityVerifier.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IGraphAppRoleParityVerifier"/>
public sealed class GraphRestAppRoleParityVerifier : IGraphAppRoleParityVerifier
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly IGraphAppRolesRegistry _registry;
    private readonly ILogger<GraphRestAppRoleParityVerifier> _logger;

    public GraphRestAppRoleParityVerifier(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleParityVerifier> logger)
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
    public async Task<GraphAppRoleParityResult> VerifyAsync(
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
            _logger.LogWarning(ex, "T3 verify: token acquisition failed for UAMI SP {UamiSpId}", uamiServicePrincipalObjectId);
            return new GraphAppRoleParityResult.Partial(
                expectedRoles.Select(r => r.Value).ToList(), GrantedCount: 0, ExpectedCount: expectedRoles.Count);
        }

        HashSet<string> currentIds;
        try
        {
            var graphSpId = await ResolveGraphResourceSpIdAsync(token, cancellationToken).ConfigureAwait(false);
            currentIds = await ReadCurrentAppRoleIdsAsync(token, uamiServicePrincipalObjectId, graphSpId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T3 verify: appRoleAssignments read failed for UAMI SP {UamiSpId}", uamiServicePrincipalObjectId);
            return new GraphAppRoleParityResult.Partial(
                expectedRoles.Select(r => r.Value).ToList(), GrantedCount: 0, ExpectedCount: expectedRoles.Count);
        }

        var missing = expectedRoles
            .Where(r => string.IsNullOrWhiteSpace(r.AppRoleId) || !currentIds.Contains(r.AppRoleId))
            .Select(r => r.Value)
            .ToList();

        if (missing.Count == 0)
        {
            return new GraphAppRoleParityResult.Verified(expectedRoles.Count);
        }

        return new GraphAppRoleParityResult.Partial(
            missing, GrantedCount: expectedRoles.Count - missing.Count, ExpectedCount: expectedRoles.Count);
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
