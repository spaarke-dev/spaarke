// -----------------------------------------------------------------------------
// DataverseWebApiAppUserCreator.cs
//
// Production IDataverseAppUserCreator — issues Dataverse Web API calls to
// upsert an Application User (find-by-applicationid, else create against the
// root business unit) and ensure the requested security role is associated.
// Auth via DefaultAzureCredential (ADR-028 MI-outbound MUST rule; §4D I5
// explicit per-tenant scope — never a default-tenant credential). Parity in
// shape with DataverseWebApiHealthProbe (task 048) — same token-acquisition
// idiom, same typed-HttpClient registration pattern.
//
// NOT under test in the CI unit suite (real Dataverse Web API calls). Handler
// unit tests substitute a fake IDataverseAppUserCreator — parity with
// DataverseWebApiHealthProbe's file-header note.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IDataverseAppUserCreator"/>
public sealed class DataverseWebApiAppUserCreator : IDataverseAppUserCreator
{
    private readonly HttpClient _httpClient;
    private readonly H10DataverseAppUserGraphParityOptions _options;
    private readonly ILogger<DataverseWebApiAppUserCreator> _logger;

    public DataverseWebApiAppUserCreator(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserCreator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<DataverseAppUserCreationOutcome> EnsureAppUserAsync(
        DataverseAppUserCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SecurityRoleName);

        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"Environment URL '{request.EnvironmentUrl}' is not a valid absolute URI.");
        }

        // Defense-in-depth: applicationId is interpolated directly into an OData
        // $filter query string below. It is always a machine-produced GUID from
        // InterStepState (H2a/H3 output), never free-text user input, so
        // injection risk is negligible — but validating the shape here produces
        // a clear diagnostic instead of a confusing Dataverse 400.
        if (!Guid.TryParse(request.ApplicationId, out _))
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"ApplicationId '{request.ApplicationId}' is not a valid GUID — refusing to build an OData filter from it.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DataverseAppUserCreationOutcome.Failure(
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            // (1) Idempotency: look for an existing App User by applicationid.
            var existingId = await FindSystemUserIdAsync(
                envUri, token, request.ApplicationId, cancellationToken).ConfigureAwait(false);

            string systemUserId;
            if (existingId is not null)
            {
                systemUserId = existingId;
            }
            else
            {
                // (2) Resolve root business unit.
                var rootBuId = await FindRootBusinessUnitIdAsync(envUri, token, cancellationToken).ConfigureAwait(false);
                if (rootBuId is null)
                {
                    return new DataverseAppUserCreationOutcome.Failure(
                        "Root business unit (parentbusinessunitid eq null) not found — cannot create App User.");
                }

                // (3) Create the App User.
                systemUserId = await CreateSystemUserAsync(
                    envUri, token, request.ApplicationId, rootBuId, cancellationToken).ConfigureAwait(false);
            }

            // (4) Resolve the requested security role at the root business unit.
            var roleId = await FindSecurityRoleIdAsync(
                envUri, token, request.SecurityRoleName, cancellationToken).ConfigureAwait(false);
            if (roleId is null)
            {
                return new DataverseAppUserCreationOutcome.Failure(
                    $"Security role '{request.SecurityRoleName}' not found in target environment.");
            }

            // (5) Ensure role association (idempotent — check-then-insert).
            var alreadyAssociated = await IsRoleAssociatedAsync(
                envUri, token, systemUserId, roleId, cancellationToken).ConfigureAwait(false);
            if (!alreadyAssociated)
            {
                await AssociateRoleAsync(envUri, token, systemUserId, roleId, cancellationToken).ConfigureAwait(false);
            }

            return new DataverseAppUserCreationOutcome.Success(systemUserId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseWebApiAppUserCreator infrastructure fault for applicationId={ApplicationId}",
                request.ApplicationId);
            return new DataverseAppUserCreationOutcome.Failure(
                $"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        return await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
    }

    private async Task<string?> FindSystemUserIdAsync(Uri envUri, AccessToken token, string applicationId, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/systemusers?$filter=applicationid eq {applicationId}&$select=systemuserid");
        var doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0
            ? values[0].GetProperty("systemuserid").GetString()
            : null;
    }

    private async Task<string?> FindRootBusinessUnitIdAsync(Uri envUri, AccessToken token, CancellationToken ct)
    {
        var uri = new Uri(envUri, "/api/data/v9.2/businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid");
        var doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0
            ? values[0].GetProperty("businessunitid").GetString()
            : null;
    }

    private async Task<string> CreateSystemUserAsync(
        Uri envUri, AccessToken token, string applicationId, string rootBuId, CancellationToken ct)
    {
        var uri = new Uri(envUri, "/api/data/v9.2/systemusers");
        var payload = new Dictionary<string, object?>
        {
            ["applicationid"] = applicationId,
            ["businessunitid@odata.bind"] = $"/businessunits({rootBuId})",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload),
        };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST systemusers failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }

        // Dataverse returns the new row's id in the OData-EntityId response header:
        // "{envUrl}/api/data/v9.2/systemusers(guid)".
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var entityIdHeader = values.FirstOrDefault();
            var extracted = ExtractGuidFromEntityId(entityIdHeader);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        throw new InvalidOperationException(
            "POST systemusers succeeded but the response carried no parseable OData-EntityId header.");
    }

    private async Task<string?> FindSecurityRoleIdAsync(Uri envUri, AccessToken token, string roleName, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(roleName);
        var uri = new Uri(envUri, $"/api/data/v9.2/roles?$filter=name eq '{encodedName}' and _businessunitid_value eq (Microsoft.Dynamics.CRM.GetRootBusinessUnitId())&$select=roleid");
        JsonDocument doc;
        try
        {
            doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Fallback: some environments do not expose the bound function above —
            // fall back to a plain name-only filter (root-BU system roles are unique
            // by name in the common case; if genuinely ambiguous, the operator will
            // see a role-not-found or wrong-role diagnostic downstream).
            var fallbackUri = new Uri(envUri, $"/api/data/v9.2/roles?$filter=name eq '{encodedName}'&$select=roleid");
            doc = await SendAndReadAsync(fallbackUri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        }

        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0
            ? values[0].GetProperty("roleid").GetString()
            : null;
    }

    private async Task<bool> IsRoleAssociatedAsync(Uri envUri, AccessToken token, string systemUserId, string roleId, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/systemusers({systemUserId})/systemuserroles_association?$filter=roleid eq {roleId}&$select=roleid");
        var doc = await SendAndReadAsync(uri, HttpMethod.Get, token, body: null, ct).ConfigureAwait(false);
        return doc.RootElement.GetProperty("value").GetArrayLength() > 0;
    }

    private async Task AssociateRoleAsync(Uri envUri, AccessToken token, string systemUserId, string roleId, CancellationToken ct)
    {
        var uri = new Uri(envUri, $"/api/data/v9.2/systemusers({systemUserId})/systemuserroles_association/$ref");
        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var payload = new Dictionary<string, object?>
        {
            ["@odata.id"] = $"{scopeBase}/api/data/v9.2/roles({roleId})",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload),
        };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST systemuserroles_association/$ref failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private async Task<JsonDocument> SendAndReadAsync(Uri uri, HttpMethod method, AccessToken token, HttpContent? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body };
        ApplyCommonHeaders(request, token);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{method} {uri.PathAndQuery} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static void ApplyCommonHeaders(HttpRequestMessage request, AccessToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("Prefer", "return=representation");
    }

    private static string? ExtractGuidFromEntityId(string? entityIdHeader)
    {
        if (string.IsNullOrWhiteSpace(entityIdHeader)) return null;
        var openParen = entityIdHeader.LastIndexOf('(');
        var closeParen = entityIdHeader.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen) return null;
        var candidate = entityIdHeader.Substring(openParen + 1, closeParen - openParen - 1);
        return Guid.TryParse(candidate, out var guid) ? guid.ToString() : null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
