// -----------------------------------------------------------------------------
// DataverseWebApiGuestUserWriter.cs
//
// Task 232. Production IDataverseGuestUserWriter — raw Dataverse Web API v9.2 via
// HttpClient + DefaultAzureCredential, acting as the L2 Worker identity (a System
// Administrator application user of the environment, PRQ-C-09).
//
//   ReadGuestAccessAsync: GET organizations?$select=restrictguestuseraccess
//   ResolveRolesAsync:    per name GET roles?$filter=name eq '{n}' and
//                         _businessunitid_value eq {customer unit} (T259: the unit's
//                         inherited copy) — exactly ONE match, else RoleNotFound /
//                         ambiguous Failure.
//   EnsureGuestUserAsync: GET systemusers(azureactivedirectoryobjectid={oid})?$select=systemuserid,_businessunitid_value
//                         — Microsoft's documented app-callable path: a member of the
//                         environment security group who is not yet a Dataverse user is
//                         ADDED by this request (root business unit). Not a plain POST
//                         systemusers: domainname is system-required and Microsoft
//                         documents no guest behaviour for it (research note:
//                         .claude/agent-memory/researcher/payg-b2b-guest-dataverse-user-
//                         provisioning-2026-10-07.md). T259: a user in the ROOT unit (where
//                         that read adds it) is moved to the customer unit — PATCH
//                         systemusers({id}) businessunitid@odata.bind, If-Match: * — and
//                         read back BEFORE any role (a unit change strips roles); a user in
//                         any other unit is InForeignBusinessUnit, nothing written (root id:
//                         H8's IDataverseRootBusinessUnitReader, reused). Then ONE read of
//                         every held role with its unit — GET systemusers({id})/
//                         systemuserroles_association?$select=roleid,_businessunitid_value —
//                         a role of any other unit is HoldsRoleOutsideBusinessUnit (nothing
//                         written or removed); otherwise POST .../systemuserroles_association/$ref
//                         for each requested role not held.
//
// Every id read from Dataverse is canonicalized (ADR-044) before it is placed in a
// filter or a reference URL. An HTTP timeout is a Failure, not an escaped exception.
// Auth: token for {environment}/.default with explicit TenantId (§4D I5); the
// internal constructor takes a credential factory so tests never touch the chain.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IDataverseGuestUserWriter"/>
public sealed class DataverseWebApiGuestUserWriter : IDataverseGuestUserWriter
{
    private readonly HttpClient _httpClient;
    private readonly IDataverseRootBusinessUnitReader _rootBusinessUnitReader;
    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public DataverseWebApiGuestUserWriter(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        IDataverseRootBusinessUnitReader rootBusinessUnitReader)
        : this(httpClient, options, rootBusinessUnitReader,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory.</summary>
    internal DataverseWebApiGuestUserWriter(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        IDataverseRootBusinessUnitReader rootBusinessUnitReader,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rootBusinessUnitReader);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _rootBusinessUnitReader = rootBusinessUnitReader;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = options.Value.DataverseRequestTimeout;
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
            using var doc = await GetJsonAsync(envUri, token, "organizations?$select=restrictguestuseraccess", cancellationToken)
                .ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.GetArrayLength() != 1
                || !values[0].TryGetProperty("restrictguestuseraccess", out var restricted)
                || restricted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new GuestAccessOutcome.Failure("GET organizations returned no restrictguestuseraccess value.");
            }
            return restricted.ValueKind == JsonValueKind.True ? new GuestAccessOutcome.Restricted() : new GuestAccessOutcome.Allowed();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GuestAccessOutcome.Failure("GET organizations timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GuestAccessOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<GuestRoleResolution> ResolveRolesAsync(
        string environmentUrl, string tenantId, Guid businessUnitId, IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleNames);
        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            return new GuestRoleResolution.Failure($"Environment URL '{environmentUrl}' is not an absolute URI.");
        }
        if (businessUnitId == Guid.Empty)
        {
            return new GuestRoleResolution.Failure("The customer's business unit id is empty (H10 output).");
        }

        try
        {
            var bu = businessUnitId;
            var token = await AcquireTokenAsync(envUri, tenantId, cancellationToken).ConfigureAwait(false);
            var roleIds = new List<Guid>(roleNames.Count);
            foreach (var roleName in roleNames)
            {
                var literal = Uri.EscapeDataString(roleName.Replace("'", "''", StringComparison.Ordinal));
                using var doc = await GetJsonAsync(envUri, token,
                    $"roles?$filter=name eq '{literal}' and _businessunitid_value eq {bu:D}&$select=roleid&$top=2",
                    cancellationToken).ConfigureAwait(false);
                var ids = ReadIds(doc, "roleid");
                switch (ids.Count)
                {
                    case 0:
                        return new GuestRoleResolution.RoleNotFound(roleName);
                    case > 1:
                        return new GuestRoleResolution.Failure(
                            $"More than one role named '{roleName}' in business unit {bu:D} — refusing to guess.");
                }
                roleIds.Add(ids[0]);
            }
            return new GuestRoleResolution.Resolved(roleIds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GuestRoleResolution.Failure("Reading the security roles timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GuestRoleResolution.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
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
                $"Entra object id '{request.EntraObjectId}' is not a GUID — refusing to build an alternate key from it.");
        }
        if (request.BusinessUnitId == Guid.Empty)
        {
            return new DataverseGuestUserOutcome.Failure("The customer's business unit id is empty (H10 output).");
        }

        try
        {
            var token = await AcquireTokenAsync(envUri, request.TenantId, cancellationToken).ConfigureAwait(false);
            var (systemUserId, unitId) = await ReadOrAddSystemUserAsync(envUri, token, objectId, cancellationToken).ConfigureAwait(false);

            // T259: the guest belongs in the customer's unit. Dataverse adds it to the ROOT on the read above; move it from
            // there (before any role — a unit change strips roles) and read it back. Never move it out of any other unit.
            if (unitId != request.BusinessUnitId)
            {
                var rootId = await _rootBusinessUnitReader.ReadRootBusinessUnitIdAsync(
                    request.EnvironmentUrl, request.TenantId, cancellationToken).ConfigureAwait(false);
                if (rootId is null)
                {
                    return new DataverseGuestUserOutcome.Failure("The environment's root business unit was not found.");
                }
                if (unitId != rootId)
                {
                    return new DataverseGuestUserOutcome.InForeignBusinessUnit(systemUserId.ToString("D"), unitId ?? Guid.Empty);
                }

                await MoveToBusinessUnitAsync(envUri, token, systemUserId, request.BusinessUnitId, cancellationToken).ConfigureAwait(false);
                var movedTo = await ReadBusinessUnitAsync(envUri, token, systemUserId, cancellationToken).ConfigureAwait(false);
                if (movedTo != request.BusinessUnitId)
                {
                    return new DataverseGuestUserOutcome.Failure(
                        $"Moving systemuser {systemUserId:D} to business unit {request.BusinessUnitId:D} did not land: it reads " +
                        $"{movedTo?.ToString("D") ?? "(no unit)"}. No role was associated; re-run H11.");
                }
            }

            // T259: every role the guest holds must belong to the customer's unit. A unit change normally strips roles, but an
            // organisation that keeps roles on a unit change (or a hand-made grant) could leave a ROOT role — Deep read there
            // reaches the Secure Record unit. Refused, never removed here.
            using var heldDoc = await GetJsonAsync(envUri, token,
                $"systemusers({systemUserId:D})/systemuserroles_association?$select=roleid,_businessunitid_value",
                cancellationToken).ConfigureAwait(false);
            var held = HeldRoles(heldDoc);
            var foreign = held.Where(r => r.BusinessUnitId != request.BusinessUnitId).ToList();
            if (foreign.Count > 0)
            {
                return new DataverseGuestUserOutcome.HoldsRoleOutsideBusinessUnit(
                    systemUserId.ToString("D"), foreign[0].RoleId, foreign[0].BusinessUnitId ?? Guid.Empty);
            }
            foreach (var roleId in request.RoleIds.Where(r => held.All(h => h.RoleId != r)))
            {
                await AssociateRoleAsync(envUri, token, systemUserId, roleId, cancellationToken).ConfigureAwait(false);
            }
            return new DataverseGuestUserOutcome.Success(systemUserId.ToString("D"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DataverseGuestUserOutcome.Failure("A Dataverse request timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DataverseGuestUserOutcome.Failure($"Dataverse Web API error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(Uri envUri, string tenantId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);   // §4D I1 / I5 — never the credential's home tenant
        var scope = $"{new Uri(envUri, "/").ToString().TrimEnd('/')}/.default";
        return await _credentialFactory(tenantId).GetTokenAsync(new TokenRequestContext([scope]), ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetJsonAsync(Uri envUri, AccessToken token, string relativeQuery, CancellationToken ct)
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
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    // ADR-044: ids read back from Dataverse are canonicalized before they reach a filter or a reference URL.
    private static List<Guid> ReadIds(JsonDocument doc, string idProperty)
    {
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The Dataverse response has no 'value' array.");
        }
        return values.EnumerateArray()
            .Select(v => v.TryGetProperty(idProperty, out var id) && Guid.TryParse(id.GetString(), out var g) && g != Guid.Empty
                ? g
                : throw new InvalidOperationException($"A Dataverse row has no usable '{idProperty}'."))
            .ToList();
    }

    private async Task<(Guid SystemUserId, Guid? BusinessUnitId)> ReadOrAddSystemUserAsync(
        Uri envUri, AccessToken token, Guid objectId, CancellationToken ct)
    {
        // Alternate key: Dataverse adds a security-group member who is not yet a user (Microsoft, group-team article).
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(envUri, $"/api/data/v9.2/systemusers(azureactivedirectoryobjectid={objectId:D})?$select=systemuserid,_businessunitid_value"));
        ApplyHeaders(request, token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var why = response.StatusCode == HttpStatusCode.NotFound
                ? "Dataverse did not add the user — it adds one on this read only when it is a DIRECT member of the " +
                  "environment security group, the environment allows guests and its billing (pay-as-you-go, PRQ-C-11) " +
                  "covers the user; membership can take minutes to propagate (re-run)"
                : "Dataverse refused the read";
            throw new InvalidOperationException(
                $"GET systemusers(azureactivedirectoryobjectid={objectId:D}) failed: {(int)response.StatusCode} " +
                $"{response.StatusCode} — {why}. Body: {Truncate(text, 400)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var userId = doc.RootElement.TryGetProperty("systemuserid", out var id) && Guid.TryParse(id.GetString(), out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"GET systemusers(azureactivedirectoryobjectid={objectId:D}) returned no systemuserid.");
        return (userId, UnitOf(doc.RootElement));
    }

    private async Task<Guid?> ReadBusinessUnitAsync(Uri envUri, AccessToken token, Guid systemUserId, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(envUri, token, $"systemusers({systemUserId:D})?$select=_businessunitid_value", ct)
            .ConfigureAwait(false);
        return UnitOf(doc.RootElement);
    }

    private static List<(Guid RoleId, Guid? BusinessUnitId)> HeldRoles(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The Dataverse response has no 'value' array.");
        }
        return values.EnumerateArray()
            .Select(v => (
                v.TryGetProperty("roleid", out var id) && Guid.TryParse(id.GetString(), out var roleId) && roleId != Guid.Empty
                    ? roleId
                    : throw new InvalidOperationException("A Dataverse row has no usable 'roleid'."),
                UnitOf(v)))
            .ToList();
    }

    private static Guid? UnitOf(JsonElement user)
        => user.TryGetProperty("_businessunitid_value", out var unit) && Guid.TryParse(unit.GetString(), out var unitId)
            && unitId != Guid.Empty
            ? unitId
            : null;

    /// <summary>T259: PATCH the user's business unit (If-Match: * — an update, never an upsert).</summary>
    private async Task MoveToBusinessUnitAsync(Uri envUri, AccessToken token, Guid systemUserId, Guid businessUnitId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(envUri, $"/api/data/v9.2/systemusers({systemUserId:D})"))
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId:D})",
            }),
        };
        ApplyHeaders(request, token);
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"PATCH systemusers({systemUserId:D}) businessunitid failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private async Task AssociateRoleAsync(Uri envUri, AccessToken token, Guid systemUserId, Guid roleId, CancellationToken ct)
    {
        var root = new Uri(envUri, "/").ToString().TrimEnd('/');
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(envUri, $"/api/data/v9.2/systemusers({systemUserId:D})/systemuserroles_association/$ref"))
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["@odata.id"] = $"{root}/api/data/v9.2/roles({roleId:D})" }),
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
