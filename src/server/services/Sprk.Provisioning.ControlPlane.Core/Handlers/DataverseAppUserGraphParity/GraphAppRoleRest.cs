// -----------------------------------------------------------------------------
// GraphAppRoleRest.cs
//
// The raw Microsoft Graph REST calls H10's granter and parity verifier share
// (task 261 moved them here from the two classes, which each carried a copy):
//   GET    /v1.0/servicePrincipals?$filter=appId eq '{graphResourceAppId}'&$select=id,appRoles
//   GET    /v1.0/servicePrincipals/{principalSpId}/appRoleAssignments   (+ @odata.nextLink pages)
//   POST   /v1.0/servicePrincipals/{principalSpId}/appRoleAssignments
//   DELETE /v1.0/servicePrincipals/{principalSpId}/appRoleAssignments/{assignmentId}
// Microsoft Learn: POST needs AppRoleAssignment.ReadWrite.All + Application.Read.All (or Directory.Read.All);
// DELETE needs AppRoleAssignment.ReadWrite.All — held by the L2 Worker identity
// (ControlPlaneGraphAppRoles), which is the caller.
//
// Non-success responses throw InvalidOperationException carrying the status
// code and a truncated body (the NFR-09 Path-C equivalent documented in
// H10DataverseAppUserGraphParityHandler.cs).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>The Microsoft Graph resource service principal in the tenant, with its app-role id → value map.</summary>
internal sealed record GraphResourcePrincipal(string Id, IReadOnlyDictionary<string, string> RoleValuesById)
{
    /// <summary>The role's value (e.g. <c>Directory.ReadWrite.All</c>), or the id itself when Graph does not list it.</summary>
    public string NameOf(string appRoleId)
        => RoleValuesById.TryGetValue(appRoleId, out var value) && !string.IsNullOrWhiteSpace(value) ? value : appRoleId;
}

/// <summary>One app-role assignment a principal holds on the Microsoft Graph resource.</summary>
internal sealed record GraphAppRoleAssignment(string AssignmentId, string AppRoleId);

internal static class GraphAppRoleRest
{
    internal static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private const string GraphHost = "graph.microsoft.com";
    private const string GraphBase = "https://" + GraphHost + "/v1.0";

    /// <summary>Resolves the Graph resource SP and its app-role definitions.</summary>
    public static async Task<GraphResourcePrincipal> ResolveGraphResourceAsync(
        HttpClient http, AccessToken token, string graphResourceAppId, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"appId eq '{graphResourceAppId}'");
        var uri = new Uri($"{GraphBase}/servicePrincipals?$filter={filter}&$select=id,appRoles");
        using var doc = await GetJsonAsync(http, uri, token, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Microsoft Graph resource service principal (appId={graphResourceAppId}) not found in tenant.");
        }

        var sp = values[0];
        var id = sp.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Graph resource SP lookup returned a null id.");
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sp.TryGetProperty("appRoles", out var appRoles) && appRoles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in appRoles.EnumerateArray())
            {
                var roleId = role.TryGetProperty("id", out var r) ? r.GetString() : null;
                var value = role.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (!string.IsNullOrWhiteSpace(roleId)) roles[roleId] = value ?? string.Empty;
            }
        }
        return new GraphResourcePrincipal(id, roles);
    }

    /// <summary>The service principal's <c>appId</c> and <c>servicePrincipalType</c> (a removal target is checked first).</summary>
    public static async Task<(string? AppId, string? Type)> ReadPrincipalAsync(
        HttpClient http, AccessToken token, string principalSpId, CancellationToken ct)
    {
        var uri = new Uri($"{GraphBase}/servicePrincipals/{Uri.EscapeDataString(principalSpId)}?$select=appId,servicePrincipalType");
        using var doc = await GetJsonAsync(http, uri, token, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        return (root.TryGetProperty("appId", out var a) ? a.GetString() : null,
                root.TryGetProperty("servicePrincipalType", out var t) ? t.GetString() : null);
    }

    /// <summary>Every app-role assignment <paramref name="principalSpId"/> holds on the Graph resource (all pages).</summary>
    public static async Task<IReadOnlyList<GraphAppRoleAssignment>> ReadAssignmentsAsync(
        HttpClient http, AccessToken token, string principalSpId, string graphResourceSpId, CancellationToken ct)
    {
        var result = new List<GraphAppRoleAssignment>();
        Uri? next = new($"{GraphBase}/servicePrincipals/{Uri.EscapeDataString(principalSpId)}/appRoleAssignments");
        var pages = 0;
        while (next is not null)
        {
            if (++pages > 50)
            {
                throw new InvalidOperationException(
                    $"appRoleAssignments for {principalSpId} did not finish within 50 pages — refusing to decide on a partial list.");
            }
            using var doc = await GetJsonAsync(http, next, token, ct).ConfigureAwait(false);
            // A 200 without a `value` array is NOT an empty list — it is an answer we cannot read. Deciding "nothing
            // extra" (or "nothing granted") on it would pass a stamp that holds roles; fail closed instead.
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"GET appRoleAssignments for {principalSpId} returned 200 without a 'value' array — refusing to treat it as an empty list.");
            }
            foreach (var entry in values.EnumerateArray())
            {
                var resourceId = entry.TryGetProperty("resourceId", out var r) ? r.GetString() : null;
                if (!string.Equals(resourceId, graphResourceSpId, StringComparison.OrdinalIgnoreCase)) continue;
                var appRoleId = entry.TryGetProperty("appRoleId", out var a) ? a.GetString() : null;
                var assignmentId = entry.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(appRoleId)) continue;
                result.Add(new GraphAppRoleAssignment(assignmentId ?? string.Empty, appRoleId));
            }
            next = null;
            if (doc.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { Length: > 0 } nextLink)
            {
                // The bearer token is only ever sent to Microsoft Graph.
                if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var nextUri)
                    || nextUri.Scheme != Uri.UriSchemeHttps
                    || !string.Equals(nextUri.Host, GraphHost, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"appRoleAssignments @odata.nextLink '{nextLink}' is not on https://{GraphHost} — refusing to send the Graph token there.");
                }
                next = nextUri;
            }
        }
        return result;
    }

    /// <summary>Grants <paramref name="appRoleId"/> on the Graph resource to <paramref name="principalSpId"/>.</summary>
    public static async Task PostGrantAsync(
        HttpClient http, AccessToken token, string principalSpId, string graphResourceSpId, string appRoleId, CancellationToken ct)
    {
        var uri = new Uri($"{GraphBase}/servicePrincipals/{Uri.EscapeDataString(principalSpId)}/appRoleAssignments");
        var payload = new Dictionary<string, object?>
        {
            ["principalId"] = principalSpId,
            ["resourceId"] = graphResourceSpId,
            ["appRoleId"] = appRoleId,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST appRoleAssignments failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 300)}");
        }
    }

    /// <summary>
    /// Removes one assignment. A 404 means it is already gone — success (another run or an operator removed it).
    /// </summary>
    public static async Task DeleteAssignmentAsync(
        HttpClient http, AccessToken token, string principalSpId, string assignmentId, CancellationToken ct)
    {
        var uri = new Uri($"{GraphBase}/servicePrincipals/{Uri.EscapeDataString(principalSpId)}/appRoleAssignments/{Uri.EscapeDataString(assignmentId)}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"DELETE appRoleAssignments/{assignmentId} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 300)}");
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, Uri uri, AccessToken token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
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
