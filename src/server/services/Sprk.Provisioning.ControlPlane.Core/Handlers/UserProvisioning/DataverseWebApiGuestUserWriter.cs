// -----------------------------------------------------------------------------
// DataverseWebApiGuestUserWriter.cs
//
// Task 232. Production IDataverseGuestUserWriter — raw Dataverse Web API v9.2 via
// HttpClient + DefaultAzureCredential, acting as the L2 Worker identity (a System
// Administrator application user of the environment, PRQ-C-09). Copies the idiom
// of H10's DataverseWebApiAppUserCreator; keyed by the guest's Entra object id.
//
//   1. GET businessunits?$filter=parentbusinessunitid eq null            root BU
//   2. GET roles?$filter=name eq '{n}' and _businessunitid_value eq {bu}  each role,
//      ALL resolved before any write — a missing role writes nothing (RoleNotFound)
//   3. GET systemusers(azureactivedirectoryobjectid={oid})?$select=systemuserid
//      — Microsoft's documented app-callable path: a member of the environment
//      security group who is not yet a Dataverse user is ADDED by this request
//      (root business unit). Not a plain POST systemusers: domainname is
//      system-required and Microsoft documents no guest behaviour for it
//      (research note: .claude/agent-memory/researcher/payg-b2b-guest-dataverse-
//      user-provisioning-2026-10-07.md). A refusal right after H11 added the guest
//      to the group can be membership propagation — Resumable, re-run.
//   4. per role: GET systemusers({id})/systemuserroles_association?$filter=roleid eq {r}
//      → POST .../systemuserroles_association/$ref only when not held
//
// Also: ReadGuestAccessAsync — organizations.restrictguestuseraccess must be
// false (PRQ-C-12, default true on new environments): with it on, guests cannot
// use Dataverse at all. H11 checks it before inviting anyone.
//
// Auth: token for {environment}/.default with explicit TenantId (§4D I5); the
// internal constructor takes a credential factory so tests never touch the chain.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IDataverseGuestUserWriter"/>
public sealed class DataverseWebApiGuestUserWriter : IDataverseGuestUserWriter
{
    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public DataverseWebApiGuestUserWriter(HttpClient httpClient, IOptions<H11UserProvisioningOptions> options)
        : this(httpClient, options,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory.</summary>
    internal DataverseWebApiGuestUserWriter(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = options.Value.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<DataverseGuestUserOutcome> EnsureGuestUserAsync(
        DataverseGuestUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            return new DataverseGuestUserOutcome.Failure($"Environment URL '{request.EnvironmentUrl}' is not an absolute URI.");
        }
        if (!Guid.TryParse(request.EntraObjectId, out var objectId))
        {
            return new DataverseGuestUserOutcome.Failure(
                $"Entra object id '{request.EntraObjectId}' is not a GUID — refusing to build an OData filter from it.");
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DataverseGuestUserOutcome.Failure($"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var rootBuId = await ReadSingleIdAsync(envUri, token,
                "businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid", "businessunitid",
                cancellationToken).ConfigureAwait(false);
            if (rootBuId is null)
            {
                return new DataverseGuestUserOutcome.Failure("The environment's root business unit was not found.");
            }

            // Every role first: a missing one must leave this guest untouched.
            var roleIds = new List<string>(request.SecurityRoleNames.Count);
            foreach (var roleName in request.SecurityRoleNames)
            {
                var literal = roleName.Replace("'", "''", StringComparison.Ordinal);
                var roleId = await ReadSingleIdAsync(envUri, token,
                    $"roles?$filter=name eq '{Uri.EscapeDataString(literal)}' and _businessunitid_value eq {rootBuId}&$select=roleid",
                    "roleid", cancellationToken).ConfigureAwait(false);
                if (roleId is null)
                {
                    return new DataverseGuestUserOutcome.RoleNotFound(roleName);
                }
                roleIds.Add(roleId);
            }

            var systemUserId = await ReadOrAddSystemUserAsync(envUri, token, objectId, cancellationToken).ConfigureAwait(false);

            foreach (var roleId in roleIds)
            {
                var held = await ReadSingleIdAsync(envUri, token,
                    $"systemusers({systemUserId})/systemuserroles_association?$filter=roleid eq {roleId}&$select=roleid",
                    "roleid", cancellationToken).ConfigureAwait(false);
                if (held is null)
                {
                    await AssociateRoleAsync(envUri, token, systemUserId, roleId, cancellationToken).ConfigureAwait(false);
                }
            }

            return new DataverseGuestUserOutcome.Success(systemUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DataverseGuestUserOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<GuestAccessOutcome> ReadGuestAccessAsync(
        string environmentUrl, string tenantId, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            return new GuestAccessOutcome.Failure($"Environment URL '{environmentUrl}' is not an absolute URI.");
        }

        try
        {
            var token = await AcquireTokenAsync(envUri, tenantId, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(envUri, "/api/data/v9.2/organizations?$select=restrictguestuseraccess"));
            ApplyHeaders(request, token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new GuestAccessOutcome.Failure(
                    $"GET organizations failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.GetArrayLength() != 1
                || !values[0].TryGetProperty("restrictguestuseraccess", out var restricted)
                || restricted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new GuestAccessOutcome.Failure("GET organizations returned no restrictguestuseraccess value.");
            }
            return restricted.ValueKind == JsonValueKind.True ? new GuestAccessOutcome.Restricted() : new GuestAccessOutcome.Allowed();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GuestAccessOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        var scope = $"{new Uri(envUri, "/").ToString().TrimEnd('/')}/.default";
        return await _credentialFactory(tenantId).GetTokenAsync(new TokenRequestContext([scope]), ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadSingleIdAsync(
        Uri envUri, AccessToken token, string relativeQuery, string idProperty, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(envUri, $"/api/data/v9.2/{relativeQuery}"));
        ApplyHeaders(request, token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {relativeQuery.Split('?')[0]} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(text, 400)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"GET {relativeQuery.Split('?')[0]} returned no 'value' array.");
        }
        return values.GetArrayLength() > 0 && values[0].TryGetProperty(idProperty, out var id) ? id.GetString() : null;
    }

    private async Task<string> ReadOrAddSystemUserAsync(Uri envUri, AccessToken token, Guid objectId, CancellationToken ct)
    {
        // Alternate key: Dataverse adds a security-group member who is not yet a user (Microsoft, group-team article).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(envUri, $"/api/data/v9.2/systemusers(azureactivedirectoryobjectid={objectId:D})?$select=systemuserid"));
        ApplyHeaders(request, token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET systemusers(azureactivedirectoryobjectid={objectId:D}) failed: {(int)response.StatusCode} " +
                $"{response.StatusCode} — Dataverse adds a user here only when it is a DIRECT member of the environment " +
                "security group (membership can take minutes to propagate; re-run). Body: " + Truncate(text, 400));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return doc.RootElement.TryGetProperty("systemuserid", out var id) && Guid.TryParse(id.GetString(), out var userId)
            ? userId.ToString("D")
            : throw new InvalidOperationException(
                $"GET systemusers(azureactivedirectoryobjectid={objectId:D}) returned no systemuserid.");
    }

    private async Task AssociateRoleAsync(Uri envUri, AccessToken token, string systemUserId, string roleId, CancellationToken ct)
    {
        var root = new Uri(envUri, "/").ToString().TrimEnd('/');
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(envUri, $"/api/data/v9.2/systemusers({systemUserId})/systemuserroles_association/$ref"))
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["@odata.id"] = $"{root}/api/data/v9.2/roles({roleId})" }),
        };
        ApplyHeaders(request, token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"POST systemuserroles_association/$ref failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, AccessToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
