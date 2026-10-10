// -----------------------------------------------------------------------------
// DataverseWebApiSecureRecordSetup.cs
//
// T256 (H7b) — production ISecureRecordSetupDataverse: direct Dataverse Web API calls (the Worker host has no pwsh or
// pac, G38), one per seam member. The request shapes are the ones unified-access-control-r2 verified live with
// scripts/Set-SecureRecordOwnerRolePrivileges.ps1, scripts/Set-RecordCreatorPersonSchema.ps1,
// scripts/Repair-SecureFlagNulls.ps1 and docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §3–§5.5, notably:
//   - RetrieveRolePrivilegesRole(RoleId=@p) for what a role holds (depth included);
//   - AddPrivilegesRole with RolePrivilege entries at Depth "Basic";
//   - RemovePrivilegeRole with an ENTITY REFERENCE named Privilege — not a GUID named PrivilegeId (§5.4);
//   - ReplacePrivilegesRole is never used: it does not remove the SharePoint four.
//
// AUTH: the SAME identity H6/H7 sign in with — the BFF app registration, credential chosen by the FR-39 ordered chain
// (WorkerDataverseCredentialFactory over EnvVarValues:Credentials; MI-FIC on secret-free Workers). H7b reuses H7's
// EnvVarValuesOptions rather than a third copy of one identity's configuration: no new app setting, no new secret
// (ADR-028 A4). That identity is a System Administrator application user of the environment (H10), which creating a
// business unit, a team and a role, and editing role privileges, all require.
//
// CI coverage: SecureRecordSetupWebApi (the HTTP half) is tested against a hand-written HttpMessageHandler
// (DataverseWebApiSecureRecordSetupTests — never Mock<HttpMessageHandler>, ADR-038); the token half is the factory's
// own (WorkerDataverseCredentialFactoryTests).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>The Dataverse Web API implementation of <see cref="ISecureRecordSetupDataverse"/>.</summary>
public sealed class DataverseWebApiSecureRecordSetup : ISecureRecordSetupDataverse
{
    /// <summary>Named HttpClient for H7b's Dataverse calls.</summary>
    public const string HttpClientName = "H7b.DataverseWebApiSecureRecordSetup";

    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EnvVarValuesOptions _options;
    private readonly WorkerDataverseCredentialFactory _credentialFactory;
    private readonly ILogger<DataverseWebApiSecureRecordSetup> _logger;
    private readonly Dictionary<SecureRecordSetupTarget, SecureRecordSetupWebApi> _sessions = new();

    /// <summary>Creates the implementation (scoped: one per handler invocation, so its token cache dies with the run).</summary>
    public DataverseWebApiSecureRecordSetup(
        IHttpClientFactory httpClientFactory,
        IOptions<EnvVarValuesOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiSecureRecordSetup> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _credentialFactory = credentialFactory;
        _logger = logger;
    }

    private SecureRecordSetupWebApi Session(SecureRecordSetupTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_sessions.TryGetValue(target, out var session))
        {
            return session;
        }

        if (!Uri.TryCreate(target.DataverseUrl, UriKind.Absolute, out var envUri))
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"Target Dataverse URL '{target.DataverseUrl}' is not a valid absolute URI.");
        }

        var scope = new Uri(envUri, "/").ToString().TrimEnd('/') + "/.default";
        AccessToken? token = null;
        TokenCredential? credential = null;
        async ValueTask<string> TokenAsync(CancellationToken ct)
        {
            if (token is { } current && current.ExpiresOn - DateTimeOffset.UtcNow > TokenRefreshMargin)
            {
                return current.Token;
            }
            try
            {
                credential ??= _credentialFactory.Create(
                    _options.Credentials, EnvVarValuesOptions.SectionName, target.TenantId, target.ClientId, _options.ClientSecret)
                    .Credential;
                token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
                return token.Value.Token;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "H7b credential selection / token acquisition failed for env={EnvUrl}", target.DataverseUrl);
                throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Auth,
                    $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = _options.RequestTimeout;
        session = new SecureRecordSetupWebApi(httpClient, envUri, TokenAsync);
        _sessions[target] = session;
        return session;
    }

    /// <inheritdoc/>
    public Task<SecureSetupNoAccessEntryProbe> ProbeNoAccessEntryAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
        => Session(target).ProbeNoAccessEntryAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<bool> ReadShareToPreviousOwnerOnAssignAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
        => Session(target).ReadShareToPreviousOwnerOnAssignAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupBusinessUnit>> FindRootBusinessUnitsAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
        => Session(target).FindRootBusinessUnitsAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupBusinessUnit>> FindBusinessUnitsByNameAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken)
        => Session(target).FindBusinessUnitsByNameAsync(name, cancellationToken);

    /// <inheritdoc/>
    public Task<SecureSetupBusinessUnit?> GetBusinessUnitAsync(SecureRecordSetupTarget target, Guid businessUnitId, CancellationToken cancellationToken)
        => Session(target).GetBusinessUnitAsync(businessUnitId, cancellationToken);

    /// <inheritdoc/>
    public Task<Guid?> GetUserBusinessUnitAsync(SecureRecordSetupTarget target, Guid systemUserId, CancellationToken cancellationToken)
        => Session(target).GetUserBusinessUnitAsync(systemUserId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListBusinessUnitUsersAsync(SecureRecordSetupTarget target, Guid businessUnitId, int top, CancellationToken cancellationToken)
        => Session(target).ListBusinessUnitUsersAsync(businessUnitId, top, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupTeam>> FindNamedOwnerTeamsAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, CancellationToken cancellationToken)
        => Session(target).FindNamedOwnerTeamsAsync(businessUnitId, name, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupTeam>> FindDefaultTeamsAsync(SecureRecordSetupTarget target, Guid? businessUnitId, CancellationToken cancellationToken)
        => Session(target).FindDefaultTeamsAsync(businessUnitId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListTeamMembersAsync(SecureRecordSetupTarget target, Guid teamId, int top, CancellationToken cancellationToken)
        => Session(target).ListTeamMembersAsync(teamId, top, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupRole>> FindRolesAsync(SecureRecordSetupTarget target, string name, Guid businessUnitId, CancellationToken cancellationToken)
        => Session(target).FindRolesAsync(name, businessUnitId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupRole>> ListTeamRolesAsync(SecureRecordSetupTarget target, Guid teamId, CancellationToken cancellationToken)
        => Session(target).ListTeamRolesAsync(teamId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListRoleTeamHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
        => Session(target).ListRoleTeamHoldersAsync(roleId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListRoleUserHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
        => Session(target).ListRoleUserHoldersAsync(roleId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupPrivilege>> GetReadPrivilegesAsync(SecureRecordSetupTarget target, string logicalName, CancellationToken cancellationToken)
        => Session(target).GetReadPrivilegesAsync(logicalName, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupHeldPrivilege>> GetRolePrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
        => Session(target).GetRolePrivilegesAsync(roleId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> FindFieldSecurityProfilesAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken)
        => Session(target).FindFieldSecurityProfilesAsync(name, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListProfileTeamsAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken)
        => Session(target).ListProfileTeamsAsync(profileId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupProfileUser>> ListProfileUsersAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken)
        => Session(target).ListProfileUsersAsync(profileId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SecureSetupFieldPermission>> ListFieldPermissionsAsync(SecureRecordSetupTarget target, string attributeLogicalName, CancellationToken cancellationToken)
        => Session(target).ListFieldPermissionsAsync(attributeLogicalName, cancellationToken);

    /// <inheritdoc/>
    public Task<bool?> IsAttributeSecuredAsync(SecureRecordSetupTarget target, string tableLogicalName, string attributeLogicalName, CancellationToken cancellationToken)
        => Session(target).IsAttributeSecuredAsync(tableLogicalName, attributeLogicalName, cancellationToken);

    /// <inheritdoc/>
    public Task<SecureSetupTableIdentity> GetTableIdentityAsync(SecureRecordSetupTarget target, string tableLogicalName, CancellationToken cancellationToken)
        => Session(target).GetTableIdentityAsync(tableLogicalName, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> ListRowsWithNullColumnAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, string attributeLogicalName, int top, CancellationToken cancellationToken)
        => Session(target).ListRowsWithNullColumnAsync(table, attributeLogicalName, top, cancellationToken);

    /// <inheritdoc/>
    public Task<Guid> CreateBusinessUnitAsync(SecureRecordSetupTarget target, string name, Guid parentId, CancellationToken cancellationToken)
        => Session(target).CreateBusinessUnitAsync(name, parentId, cancellationToken);

    /// <inheritdoc/>
    public Task<Guid> CreateOwnerTeamAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken)
        => Session(target).CreateOwnerTeamAsync(businessUnitId, name, description, cancellationToken);

    /// <inheritdoc/>
    public Task<Guid> CreateRoleAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken)
        => Session(target).CreateRoleAsync(businessUnitId, name, description, cancellationToken);

    /// <inheritdoc/>
    public Task AddBasicPrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, Guid businessUnitId, IReadOnlyList<SecureSetupPrivilege> privileges, CancellationToken cancellationToken)
        => Session(target).AddBasicPrivilegesAsync(roleId, businessUnitId, privileges, cancellationToken);

    /// <inheritdoc/>
    public Task RemovePrivilegeAsync(SecureRecordSetupTarget target, Guid roleId, Guid privilegeId, CancellationToken cancellationToken)
        => Session(target).RemovePrivilegeAsync(roleId, privilegeId, cancellationToken);

    /// <inheritdoc/>
    public Task AssociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken)
        => Session(target).AssociateTeamRoleAsync(teamId, roleId, cancellationToken);

    /// <inheritdoc/>
    public Task DisassociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken)
        => Session(target).DisassociateTeamRoleAsync(teamId, roleId, cancellationToken);

    /// <inheritdoc/>
    public Task AssociateProfileTeamAsync(SecureRecordSetupTarget target, Guid profileId, Guid teamId, CancellationToken cancellationToken)
        => Session(target).AssociateProfileTeamAsync(profileId, teamId, cancellationToken);

    /// <inheritdoc/>
    public Task AssociateProfileUserAsync(SecureRecordSetupTarget target, Guid profileId, Guid userId, CancellationToken cancellationToken)
        => Session(target).AssociateProfileUserAsync(profileId, userId, cancellationToken);

    /// <inheritdoc/>
    public Task SetColumnFalseAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, Guid rowId, string attributeLogicalName, CancellationToken cancellationToken)
        => Session(target).SetColumnFalseAsync(table, rowId, attributeLogicalName, cancellationToken);
}

/// <summary>
/// The HTTP half of <see cref="DataverseWebApiSecureRecordSetup"/>: one environment, one bearer-token source. Separate so
/// the request shapes are CI-tested against a hand-written <see cref="HttpMessageHandler"/>.
/// </summary>
internal sealed class SecureRecordSetupWebApi
{
    /// <summary>
    /// The BFF deny-list reader's <c>$select</c> (<c>NoAccessListReader.RowSelect</c>). Probing with the same columns
    /// proves the table AND every column the BFF reads exist — a missing one fails the BFF closed on every read.
    /// Spaarke.ArchTests SecureRecordOwnerRoleSetParityTests pins it equal to the BFF's.
    /// </summary>
    internal const string NoAccessEntryRowSelect =
        "sprk_noaccessentryid,_sprk_subjectcontact_value,_sprk_subjectorganization_value," +
        "_sprk_subjectsystemuser_value," +
        "_sprk_objectorganization_value,_sprk_objectrecordtype_value,sprk_objectrecordid";

    private const string Api = "/api/data/v9.2/";
    private const int MaxPages = 100;
    private static readonly Regex EntityIdPattern = new(@"\(([0-9a-fA-F-]{36})\)\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private readonly HttpClient _http;
    private readonly Uri _envUri;
    private readonly string _apiBase;
    private readonly Func<CancellationToken, ValueTask<string>> _token;

    internal SecureRecordSetupWebApi(HttpClient http, Uri envUri, Func<CancellationToken, ValueTask<string>> token)
    {
        _http = http;
        _envUri = new Uri(envUri, "/");
        _apiBase = _envUri.ToString().TrimEnd('/') + Api.TrimEnd('/');
        _token = token;
    }

    // ------------------------------------------------------------ reads

    internal async Task<SecureSetupNoAccessEntryProbe> ProbeNoAccessEntryAsync(CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"sprk_noaccessentries?$select={NoAccessEntryRowSelect}&$top=1", null, ct)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return new SecureSetupNoAccessEntryProbe(true, "present");
        }
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new SecureSetupNoAccessEntryProbe(false, $"{(int)response.StatusCode} {response.StatusCode}: {ErrorMessage(body)}");
        }
        throw Fault(response.StatusCode, "Probing sprk_noaccessentries");
    }

    internal async Task<bool> ReadShareToPreviousOwnerOnAssignAsync(CancellationToken ct)
    {
        var rows = await GetValuesAsync("organizations?$select=sharetopreviousowneronassign", "Reading the organization", ct)
            .ConfigureAwait(false);
        if (rows.Count != 1)
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"Reading the organization returned {rows.Count} rows (expected one).");
        }
        return rows[0].TryGetProperty("sharetopreviousowneronassign", out var value) && value.ValueKind == JsonValueKind.True;
    }

    internal async Task<IReadOnlyList<SecureSetupBusinessUnit>> FindRootBusinessUnitsAsync(CancellationToken ct)
        => (await GetValuesAsync(
                "businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid,name,_parentbusinessunitid_value&$top=2",
                "Reading the root business unit", ct).ConfigureAwait(false))
            .Select(BusinessUnit).ToArray();

    internal async Task<IReadOnlyList<SecureSetupBusinessUnit>> FindBusinessUnitsByNameAsync(string name, CancellationToken ct)
        => (await GetValuesAsync(
                $"businessunits?$filter=name eq '{Literal(name)}'&$select=businessunitid,name,_parentbusinessunitid_value&$top=2",
                $"Reading business unit '{name}'", ct).ConfigureAwait(false))
            .Select(BusinessUnit).ToArray();

    internal async Task<SecureSetupBusinessUnit?> GetBusinessUnitAsync(Guid businessUnitId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"businessunits({businessUnitId})?$select=businessunitid,name,_parentbusinessunitid_value", null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        EnsureSuccess(response, "Reading the customer's business unit");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return BusinessUnit(document.RootElement);
    }

    internal async Task<Guid?> GetUserBusinessUnitAsync(Guid systemUserId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"systemusers({systemUserId})?$select=_businessunitid_value", null, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        EnsureSuccess(response, "Reading the application user's business unit");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return Id(document.RootElement, "_businessunitid_value");
    }

    internal async Task<IReadOnlyList<Guid>> ListBusinessUnitUsersAsync(Guid businessUnitId, int top, CancellationToken ct)
        => (await GetValuesAsync(
                $"systemusers?$filter=_businessunitid_value eq {businessUnitId}&$select=systemuserid&$top={top}",
                "Reading the business unit's users", ct).ConfigureAwait(false))
            .Select(r => Id(r, "systemuserid")).ToArray();

    internal async Task<IReadOnlyList<SecureSetupTeam>> FindNamedOwnerTeamsAsync(Guid businessUnitId, string name, CancellationToken ct)
        => (await GetValuesAsync(
                $"teams?$filter=_businessunitid_value eq {businessUnitId} and name eq '{Literal(name)}' and teamtype eq 0 and isdefault eq false" +
                "&$select=teamid,name,_businessunitid_value,isdefault&$top=2",
                $"Reading team '{name}'", ct).ConfigureAwait(false))
            .Select(Team).ToArray();

    internal async Task<IReadOnlyList<SecureSetupTeam>> FindDefaultTeamsAsync(Guid? businessUnitId, CancellationToken ct)
    {
        var filter = businessUnitId is { } unit ? $"isdefault eq true and _businessunitid_value eq {unit}" : "isdefault eq true";
        return (await GetAllValuesAsync($"teams?$filter={filter}&$select=teamid,name,_businessunitid_value,isdefault",
                "Reading default teams", ct).ConfigureAwait(false))
            .Select(Team).ToArray();
    }

    internal async Task<IReadOnlyList<Guid>> ListTeamMembersAsync(Guid teamId, int top, CancellationToken ct)
        => (await GetValuesAsync($"teammemberships?$filter=teamid eq {teamId}&$select=systemuserid&$top={top}",
                "Reading the team's members", ct).ConfigureAwait(false))
            .Select(r => Id(r, "systemuserid")).ToArray();

    internal async Task<IReadOnlyList<SecureSetupRole>> FindRolesAsync(string name, Guid businessUnitId, CancellationToken ct)
        => (await GetValuesAsync(
                $"roles?$filter=name eq '{Literal(name)}' and _businessunitid_value eq {businessUnitId}" +
                "&$select=roleid,name,_businessunitid_value,_parentrootroleid_value&$top=2",
                $"Reading role '{name}'", ct).ConfigureAwait(false))
            .Select(Role).ToArray();

    internal async Task<IReadOnlyList<SecureSetupRole>> ListTeamRolesAsync(Guid teamId, CancellationToken ct)
        => (await GetAllValuesAsync(
                $"teams({teamId})/teamroles_association?$select=roleid,name,_businessunitid_value,_parentrootroleid_value",
                "Reading the team's roles", ct).ConfigureAwait(false))
            .Select(Role).ToArray();

    internal async Task<IReadOnlyList<Guid>> ListRoleTeamHoldersAsync(Guid roleId, CancellationToken ct)
        => (await GetAllValuesAsync($"roles({roleId})/teamroles_association?$select=teamid", "Reading the role's teams", ct)
                .ConfigureAwait(false))
            .Select(r => Id(r, "teamid")).ToArray();

    internal async Task<IReadOnlyList<Guid>> ListRoleUserHoldersAsync(Guid roleId, CancellationToken ct)
        => (await GetAllValuesAsync($"roles({roleId})/systemuserroles_association?$select=systemuserid", "Reading the role's users", ct)
                .ConfigureAwait(false))
            .Select(r => Id(r, "systemuserid")).ToArray();

    internal async Task<IReadOnlyList<SecureSetupPrivilege>> GetReadPrivilegesAsync(string logicalName, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"EntityDefinitions(LogicalName='{Literal(logicalName)}')/Privileges", null, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];   // no such table: the procedure names it
        }
        EnsureSuccess(response, $"Reading {logicalName}'s privileges");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return Values(document.RootElement)
            .Where(p => string.Equals(Text(p, "PrivilegeType"), "Read", StringComparison.Ordinal))
            .Select(p => new SecureSetupPrivilege(Id(p, "PrivilegeId"), Text(p, "Name") ?? string.Empty))
            .ToArray();
    }

    internal async Task<IReadOnlyList<SecureSetupHeldPrivilege>> GetRolePrivilegesAsync(Guid roleId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"RetrieveRolePrivilegesRole(RoleId=@p)?@p={roleId}", null, ct)
            .ConfigureAwait(false);
        EnsureSuccess(response, "Reading the role's privileges");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("RolePrivileges", out var privileges) || privileges.ValueKind != JsonValueKind.Array)
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                "RetrieveRolePrivilegesRole returned no RolePrivileges array.");
        }
        return privileges.EnumerateArray()
            .Select(p => new SecureSetupHeldPrivilege(Id(p, "PrivilegeId"), Text(p, "PrivilegeName"), Depth(p)))
            .ToArray();
    }

    internal async Task<IReadOnlyList<Guid>> FindFieldSecurityProfilesAsync(string name, CancellationToken ct)
        => (await GetValuesAsync($"fieldsecurityprofiles?$filter=name eq '{Literal(name)}'&$select=fieldsecurityprofileid&$top=2",
                $"Reading field-security profile '{name}'", ct).ConfigureAwait(false))
            .Select(r => Id(r, "fieldsecurityprofileid")).ToArray();

    internal async Task<IReadOnlyList<Guid>> ListProfileTeamsAsync(Guid profileId, CancellationToken ct)
        => (await GetAllValuesAsync($"fieldsecurityprofiles({profileId})/teamprofiles_association?$select=teamid",
                "Reading the profile's teams", ct).ConfigureAwait(false))
            .Select(r => Id(r, "teamid")).ToArray();

    internal async Task<IReadOnlyList<SecureSetupProfileUser>> ListProfileUsersAsync(Guid profileId, CancellationToken ct)
        => (await GetAllValuesAsync($"fieldsecurityprofiles({profileId})/systemuserprofiles_association?$select=systemuserid,applicationid",
                "Reading the profile's users", ct).ConfigureAwait(false))
            .Select(r => new SecureSetupProfileUser(Id(r, "systemuserid"), OptionalId(r, "applicationid"))).ToArray();

    internal async Task<IReadOnlyList<SecureSetupFieldPermission>> ListFieldPermissionsAsync(string attributeLogicalName, CancellationToken ct)
        => (await GetAllValuesAsync(
                $"fieldpermissions?$filter=attributelogicalname eq '{Literal(attributeLogicalName)}'" +
                "&$select=entityname,canread,cancreate,canupdate,_fieldsecurityprofileid_value",
                $"Reading the field permissions on {attributeLogicalName}", ct).ConfigureAwait(false))
            .Select(r => new SecureSetupFieldPermission(
                Id(r, "_fieldsecurityprofileid_value"), Text(r, "entityname") ?? string.Empty,
                Int(r, "canread"), Int(r, "cancreate"), Int(r, "canupdate")))
            .ToArray();

    internal async Task<bool?> IsAttributeSecuredAsync(string tableLogicalName, string attributeLogicalName, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"EntityDefinitions(LogicalName='{Literal(tableLogicalName)}')/Attributes(LogicalName='{Literal(attributeLogicalName)}')?$select=IsSecured",
            null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        EnsureSuccess(response, $"Reading {tableLogicalName}.{attributeLogicalName}");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return document.RootElement.TryGetProperty("IsSecured", out var secured) && secured.ValueKind == JsonValueKind.True;
    }

    internal async Task<SecureSetupTableIdentity> GetTableIdentityAsync(string tableLogicalName, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"EntityDefinitions(LogicalName='{Literal(tableLogicalName)}')?$select=EntitySetName,PrimaryIdAttribute", null, ct)
            .ConfigureAwait(false);
        EnsureSuccess(response, $"Reading table {tableLogicalName}");
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        var set = Text(document.RootElement, "EntitySetName");
        var key = Text(document.RootElement, "PrimaryIdAttribute");
        if (string.IsNullOrWhiteSpace(set) || string.IsNullOrWhiteSpace(key))
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"Table {tableLogicalName} reports no EntitySetName or PrimaryIdAttribute.");
        }
        return new SecureSetupTableIdentity(tableLogicalName, set, key);
    }

    internal async Task<IReadOnlyList<Guid>> ListRowsWithNullColumnAsync(SecureSetupTableIdentity table, string attributeLogicalName, int top, CancellationToken ct)
        => (await GetValuesAsync($"{table.EntitySetName}?$filter={attributeLogicalName} eq null&$select={table.PrimaryIdAttribute}&$top={top}",
                $"Reading {table.LogicalName} rows with {attributeLogicalName} NULL", ct).ConfigureAwait(false))
            .Select(r => Id(r, table.PrimaryIdAttribute)).ToArray();

    // ------------------------------------------------------------ writes

    internal Task<Guid> CreateBusinessUnitAsync(string name, Guid parentId, CancellationToken ct)
        => CreateAsync("businessunits", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["parentbusinessunitid@odata.bind"] = $"/businessunits({parentId})",
        }, $"Creating business unit '{name}'", ct);

    internal Task<Guid> CreateOwnerTeamAsync(Guid businessUnitId, string name, string description, CancellationToken ct)
        => CreateAsync("teams", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["teamtype"] = 0,
            ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId})",
            ["description"] = description,
        }, $"Creating team '{name}'", ct);

    internal Task<Guid> CreateRoleAsync(Guid businessUnitId, string name, string description, CancellationToken ct)
        => CreateAsync("roles", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId})",
            ["description"] = description,
        }, $"Creating role '{name}'", ct);

    internal Task AddBasicPrivilegesAsync(Guid roleId, Guid businessUnitId, IReadOnlyList<SecureSetupPrivilege> privileges, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"roles({roleId})/Microsoft.Dynamics.CRM.AddPrivilegesRole", new Dictionary<string, object?>
        {
            ["Privileges"] = privileges.Select(p => new Dictionary<string, object?>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.RolePrivilege",
                ["Depth"] = SecureRecordSetupProcedure.BasicDepth,
                ["PrivilegeId"] = p.Id,
                ["PrivilegeName"] = p.Name,
                ["BusinessUnitId"] = businessUnitId,
            }).ToArray(),
        }, "AddPrivilegesRole", ct);

    internal Task RemovePrivilegeAsync(Guid roleId, Guid privilegeId, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"roles({roleId})/Microsoft.Dynamics.CRM.RemovePrivilegeRole", new Dictionary<string, object?>
        {
            // An entity reference named Privilege — a GUID named PrivilegeId is an OData parameter error (guide §5.4).
            ["Privilege"] = new Dictionary<string, object?>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.privilege",
                ["privilegeid"] = privilegeId,
            },
        }, "RemovePrivilegeRole", ct);

    internal Task AssociateTeamRoleAsync(Guid teamId, Guid roleId, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"teams({teamId})/teamroles_association/$ref", Reference("roles", roleId), "Giving the team the role", ct);

    internal Task DisassociateTeamRoleAsync(Guid teamId, Guid roleId, CancellationToken ct)
        => WriteAsync(HttpMethod.Delete, $"teams({teamId})/teamroles_association({roleId})/$ref", null, "Taking the role from the team", ct);

    internal Task AssociateProfileTeamAsync(Guid profileId, Guid teamId, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"fieldsecurityprofiles({profileId})/teamprofiles_association/$ref", Reference("teams", teamId),
            "Adding a team to the field-security profile", ct);

    internal Task AssociateProfileUserAsync(Guid profileId, Guid userId, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"fieldsecurityprofiles({profileId})/systemuserprofiles_association/$ref", Reference("systemusers", userId),
            "Adding a user to the field-security profile", ct);

    internal Task SetColumnFalseAsync(SecureSetupTableIdentity table, Guid rowId, string attributeLogicalName, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"{table.EntitySetName}({rowId})", new Dictionary<string, object?> { [attributeLogicalName] = false },
            $"Setting {table.LogicalName}.{attributeLogicalName}", ct, ifMatchAny: true);

    // ------------------------------------------------------------ plumbing

    private Dictionary<string, object?> Reference(string entitySet, Guid id)
        => new() { ["@odata.id"] = $"{_apiBase}/{entitySet}({id})" };

    private async Task<Guid> CreateAsync(string entitySet, Dictionary<string, object?> body, string what, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, entitySet, body, ct).ConfigureAwait(false);
        EnsureSuccess(response, what);
        var entityId = response.Headers.TryGetValues("OData-EntityId", out var values) ? values.FirstOrDefault() : null;
        var match = entityId is null ? null : EntityIdPattern.Match(entityId);
        if (match is not { Success: true } || !Guid.TryParse(match.Groups[1].Value, out var id))
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"{what}: the response carried no OData-EntityId header with the new id.");
        }
        return id;
    }

    private async Task WriteAsync(HttpMethod method, string relative, object? body, string what, CancellationToken ct, bool ifMatchAny = false)
    {
        using var response = await SendAsync(method, relative, body, ct, ifMatchAny).ConfigureAwait(false);
        EnsureSuccess(response, what);
    }

    private async Task<List<JsonElement>> GetValuesAsync(string relative, string what, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, relative, null, ct).ConfigureAwait(false);
        EnsureSuccess(response, what);
        using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return Values(document.RootElement).Select(e => e.Clone()).ToList();
    }

    /// <summary>Every page (<c>@odata.nextLink</c>), up to <see cref="MaxPages"/>.</summary>
    private async Task<List<JsonElement>> GetAllValuesAsync(string relative, string what, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        Uri? next = new(_apiBase + "/" + relative);
        for (var page = 0; next is not null; page++)
        {
            if (page == MaxPages)
            {
                throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                    $"{what}: more than {MaxPages} pages — refusing to read on.");
            }
            using var response = await SendAsync(HttpMethod.Get, next, null, ct).ConfigureAwait(false);
            EnsureSuccess(response, what);
            using var document = await ReadJsonAsync(response, ct).ConfigureAwait(false);
            all.AddRange(Values(document.RootElement).Select(e => e.Clone()));
            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.ValueKind == JsonValueKind.String
                ? new Uri(link.GetString()!)
                : null;
        }
        return all;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, CancellationToken ct, bool ifMatchAny = false)
        => SendAsync(method, new Uri(_apiBase + "/" + relative), body, ct, ifMatchAny);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, object? body, CancellationToken ct, bool ifMatchAny = false)
    {
        var token = await _token(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");
        if (ifMatchAny)
        {
            request.Headers.TryAddWithoutValidation("If-Match", "*");
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"{method} {uri.AbsolutePath} failed: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other,
                $"{response.RequestMessage?.RequestUri?.AbsolutePath} returned a body that is not JSON.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw Fault(response.StatusCode, what);
        }
    }

    private static SecureRecordSetupDataverseException Fault(HttpStatusCode status, string what)
    {
        var kind = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SecureRecordSetupFaultKind.Auth,
            (HttpStatusCode)429 => SecureRecordSetupFaultKind.RateLimited,
            _ => SecureRecordSetupFaultKind.Other,
        };
        return new SecureRecordSetupDataverseException(kind, $"{what} returned {(int)status} {status}.");
    }

    private static IEnumerable<JsonElement> Values(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other, "The response has no 'value' array.");
        }
        return value.EnumerateArray();
    }

    /// <summary>An OData string literal's content: quotes doubled, then URL-escaped.</summary>
    private static string Literal(string value) => Uri.EscapeDataString(value.Replace("'", "''", StringComparison.Ordinal));

    private static SecureSetupBusinessUnit BusinessUnit(JsonElement e)
        => new(Id(e, "businessunitid"), Text(e, "name") ?? string.Empty, OptionalId(e, "_parentbusinessunitid_value"));

    private static SecureSetupTeam Team(JsonElement e)
        => new(Id(e, "teamid"), Text(e, "name") ?? string.Empty, Id(e, "_businessunitid_value"),
            e.TryGetProperty("isdefault", out var d) && d.ValueKind == JsonValueKind.True);

    private static SecureSetupRole Role(JsonElement e)
        => new(Id(e, "roleid"), Text(e, "name") ?? string.Empty, Id(e, "_businessunitid_value"), OptionalId(e, "_parentrootroleid_value"));

    private static string? Text(JsonElement e, string property)
        => e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string property)
        => e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static Guid Id(JsonElement e, string property)
        => OptionalId(e, property)
           ?? throw new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other, $"A row has no GUID '{property}'.");

    private static Guid? OptionalId(JsonElement e, string property)
        => e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id)
            ? id
            : null;

    /// <summary>
    /// A RolePrivilege's depth. The Web API sends the PrivilegeDepth enum NAME; a number is the enum value (Basic 0,
    /// Local 1, Deep 2, Global 3). Anything else is returned as read, so it never compares equal to Basic (fail closed).
    /// </summary>
    private static string Depth(JsonElement e)
    {
        if (!e.TryGetProperty("Depth", out var depth))
        {
            return string.Empty;
        }
        if (depth.ValueKind == JsonValueKind.String)
        {
            return depth.GetString() ?? string.Empty;
        }
        return depth.ValueKind == JsonValueKind.Number && depth.TryGetInt32(out var value)
            ? value switch { 0 => "Basic", 1 => "Local", 2 => "Deep", 3 => "Global", _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : string.Empty;
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && Text(error, "message") is { } message)
            {
                return message.Length > 300 ? message[..300] : message;
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return body.Length > 300 ? body[..300] : body;
    }
}
