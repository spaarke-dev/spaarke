using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Spaarke.Dataverse;

/// <summary>
/// Queries Dataverse for user access permissions and team memberships.
/// Implements fail-closed security: returns AccessRights.None on errors.
/// </summary>
public class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseAccessDataSource> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly IConfidentialClientApplication? _cca;
    private readonly string _apiUrl;
    private readonly string _dataverseScope;
    private AccessToken? _currentToken;

    public DataverseAccessDataSource(
        IDataverseService dataverseService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseAccessDataSource> logger,
        TokenCredential? credential = null)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["API_CLIENT_SECRET"]; // Same app registration as Graph

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
        _dataverseScope = $"{dataverseUrl.TrimEnd('/')}/.default";

        // Use ClientSecretCredential when configured (enables OBO token exchange), else use the
        // DI-injected TokenCredential (UAMI-pinned via the BFF's ManagedIdentityCredentialFactory).
        // Constructor TokenCredential is optional for backwards-compat with non-DI instantiation;
        // production registrations from BFF will always provide it.
        if (!string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId))
        {
            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _logger.LogInformation("DataverseAccessDataSource using ClientSecretCredential for service principal auth");

            // Initialize MSAL for OBO token exchange
            _cca = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .WithClientSecret(clientSecret)
                .Build();

            _logger.LogInformation("DataverseAccessDataSource initialized with OBO support");
        }
        else
        {
            // BFF-FIX-2026-05-24: prefer the DI-injected TokenCredential (pinned to UAMI clientId).
            // Falls back to DefaultAzureCredential() for cases where this type is instantiated
            // outside the BFF DI container (e.g. tooling, integration tests).
            _credential = credential ?? new DefaultAzureCredential();
            _cca = null; // No OBO support with managed identity
            _logger.LogInformation(
                "DataverseAccessDataSource using {CredentialKind} - OBO not available",
                credential != null ? "DI-injected TokenCredential" : "DefaultAzureCredential (fallback)");
        }
    }

    /// <summary>
    /// Ensures the HttpClient has a valid authentication token.
    /// Uses service principal (app-only) authentication.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { _dataverseScope }),
                ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("DataverseAccessDataSource: Refreshed service principal access token");
        }
    }

    /// <summary>
    /// Performs On-Behalf-Of token exchange to get Dataverse token for the user.
    /// </summary>
    /// <param name="userAccessToken">User's bearer token from Authorization header</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dataverse access token for the user</returns>
    private async Task<string> GetDataverseTokenViaOBOAsync(string userAccessToken, CancellationToken ct = default)
    {
        if (_cca == null)
        {
            throw new InvalidOperationException(
                "OBO authentication requires client credentials to be configured. " +
                "Ensure TENANT_ID, API_APP_ID, and API_CLIENT_SECRET are set.");
        }

        _logger.LogDebug("Performing OBO token exchange for Dataverse access");

        try
        {
            var result = await _cca.AcquireTokenOnBehalfOf(
                new[] { _dataverseScope },
                new UserAssertion(userAccessToken))
                .ExecuteAsync(ct);

            _logger.LogInformation("OBO token exchange successful for Dataverse. Scopes: {Scopes}",
                string.Join(", ", result.Scopes));

            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL UI required. ErrorCode: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL service exception. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}",
                ex.ErrorCode, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBO failed - unexpected exception");
            throw;
        }
    }

    public async Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        _logger.LogInformation(
            "[UAC-DIAG] GetUserAccessAsync START: AzureAdOid={UserId}, ResourceId={ResourceId}, UsingOBO={UsingOBO}",
            userId, resourceId, !string.IsNullOrEmpty(userAccessToken));

        try
        {
            // Determine which authentication mode to use
            string dataverseToken;

            if (!string.IsNullOrEmpty(userAccessToken))
            {
                // Use OBO to call Dataverse as the user
                _logger.LogDebug("[UAC-DIAG] Using OBO authentication for user context");
                dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

                // CRITICAL: Set the OBO token on HttpClient headers for all subsequent API calls
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", dataverseToken);

                _logger.LogDebug("[UAC-DIAG] Set OBO token on HttpClient authorization header");
            }
            else
            {
                // Use service principal (app-only) authentication
                _logger.LogDebug("[UAC-DIAG] Using service principal authentication");
                await EnsureAuthenticatedAsync(ct);
                dataverseToken = _currentToken!.Value.Token;
            }

            // Map Azure AD Object ID to Dataverse systemuserid
            var dataverseUserId = await LookupDataverseUserIdAsync(userId, ct);
            if (string.IsNullOrEmpty(dataverseUserId))
            {
                _logger.LogWarning("Could not find Dataverse user for Azure AD OID {AzureAdOid}. Returning None access.", userId);
                return new AccessSnapshot
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    AccessRights = AccessRights.None,
                    TeamMemberships = Array.Empty<string>(),
                    Roles = Array.Empty<string>(),
                    CachedAt = DateTimeOffset.UtcNow
                };
            }

            _logger.LogDebug("Mapped Azure AD OID {AzureAdOid} to Dataverse systemuserid {DataverseUserId}", userId, dataverseUserId);

            // Query user permissions from Dataverse using the Dataverse user ID
            var permissions = await QueryUserPermissionsAsync(dataverseUserId, resourceId, dataverseToken, ct);

            // Query team memberships using Dataverse user ID
            var teams = await QueryUserTeamMembershipsAsync(dataverseUserId, ct);

            // Query user roles using Dataverse user ID
            var roles = await QueryUserRolesAsync(dataverseUserId, ct);

            // Determine granular access rights based on permissions
            var accessRights = DetermineAccessLevel(permissions);

            var snapshot = new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = accessRights,
                TeamMemberships = teams,
                Roles = roles,
                CachedAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Access snapshot retrieved for user {UserId}: AccessRights={AccessRights}, Teams={TeamCount}, Roles={RoleCount}",
                userId, accessRights, teams.Count(), roles.Count());

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to fetch access data for user {UserId} on resource {ResourceId}. Fail-closed: returning AccessRights.None",
                userId,
                resourceId);

            // Fail-closed security: Return None on errors
            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = AccessRights.None,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid for a given Azure AD Object ID.
    /// </summary>
    /// <param name="azureAdObjectId">Azure AD Object ID (from token 'oid' claim)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dataverse systemuserid, or null if not found</returns>
    private async Task<string?> LookupDataverseUserIdAsync(string azureAdObjectId, CancellationToken ct)
    {
        try
        {
            // Query systemusers by azureactivedirectoryobjectid
            var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{azureAdObjectId}'&$select=systemuserid,fullname";

            _logger.LogDebug("Looking up Dataverse user for Azure AD OID {AzureAdOid}: {Url}", azureAdObjectId, url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to lookup Dataverse user: {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<SystemUserDto>>(ct);

            if (result?.Value == null || !result.Value.Any())
            {
                _logger.LogWarning("No Dataverse user found for Azure AD OID {AzureAdOid}", azureAdObjectId);
                return null;
            }

            var user = result.Value.First();
            // PII (D9-01): user full name removed from this authorization-path log. The systemuserid
            // and Azure AD OID GUIDs are sufficient non-PII correlation identifiers for diagnostics.
            _logger.LogInformation("Found Dataverse user (systemuserid: {SystemUserId}) for Azure AD OID {AzureAdOid}",
                user.SystemUserId, azureAdObjectId);

            return user.SystemUserId;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error looking up Dataverse user for Azure AD OID {AzureAdOid}", azureAdObjectId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the principal's ACTUAL access rights on a record.
    /// </summary>
    /// <param name="userId">Dataverse systemuserid (already mapped from the Entra oid by the caller)</param>
    /// <param name="resourceId">Document resource ID</param>
    /// <param name="dataverseToken">Dataverse access token (from OBO or service principal)</param>
    /// <param name="ct">Cancellation token</param>
    /// <remarks>
    /// <para><b>unified-access-control-r2 task 005 (spec FR-04, finding A-20 Read-ceiling half).</b>
    /// This method used to answer with a single hard-coded <see cref="AccessRights.Read"/> on success:
    /// it probed <c>GET sprk_documents({id})</c> and reasoned "the query succeeded, therefore Read".
    /// The comment on the old implementation said Dataverse "will enforce Write/Delete separately" —
    /// but on the SPA/Teams surface the BFF filter IS the enforcement point, so nothing enforced them.
    /// Every policy requiring more than Read was unsatisfiable: <c>upload_file</c> (Write|Create),
    /// <c>create_container</c> (Create|Write), <c>download_file</c> (Write), <c>delete_file</c>
    /// (Delete), <c>share_document</c> (Share) denied for every caller, however privileged.</para>
    ///
    /// <para><b>Now:</b> <c>RetrievePrincipalAccess</c> — the Dataverse function that answers exactly
    /// this question — is called first, and its full flag set is mapped by
    /// <see cref="MapDataverseAccessRights"/>. Both that mapper and
    /// <see cref="PrincipalAccessResponse"/> already existed in this file but were <b>dead code</b>:
    /// orphaned wiring left behind when the direct-query probe replaced the original implementation.
    /// This task reconnects them rather than writing anything new.</para>
    ///
    /// <para><b>Why the probe survives as a fallback.</b> The removed comment claimed
    /// RetrievePrincipalAccess "may not be available" with delegated tokens. That claim is unverified
    /// (it has zero call sites repo-wide, so nothing ever exercised it) and cannot be settled offline.
    /// Rather than bet the fix on it, any RetrievePrincipalAccess failure falls back to the original
    /// probe. The fallback is strictly safe: it grants Read only when the principal can genuinely read
    /// the record, so the snapshot is never wider than today's and never wider than Dataverse's own
    /// answer. A failure is logged with the <c>RPA-FALLBACK</c> marker so a systematic outage is
    /// visible rather than silently capping everyone at Read again.</para>
    ///
    /// <para><b>Fail-closed.</b> No path infers rights from anything but Dataverse's answer. Errors
    /// yield no rights (an empty record list → <see cref="AccessRights.None"/>).</para>
    /// </remarks>
    private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
        string userId,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        // AUTHORITATIVE: ask Dataverse what rights this principal holds on this record.
        var principalRights = await TryRetrievePrincipalAccessAsync(userId, resourceId, dataverseToken, ct);

        if (principalRights.HasValue)
        {
            if (principalRights.Value == AccessRights.None)
            {
                _logger.LogInformation(
                    "[UAC-DIAG] RetrievePrincipalAccess: no rights. User={UserId}, Resource={ResourceId}",
                    userId, resourceId);
                return new List<PermissionRecord>();
            }

            _logger.LogInformation(
                "[UAC-DIAG] RetrievePrincipalAccess SUCCESS: User={UserId}, Resource={ResourceId}, GrantedAccess={AccessRights}",
                userId, resourceId, principalRights.Value);

            return new List<PermissionRecord>
            {
                new PermissionRecord(userId, resourceId, principalRights.Value)
            };
        }

        // FALLBACK: RetrievePrincipalAccess was unusable. Degrade to the original read probe, which
        // grants at most Read and only when the principal can actually retrieve the record.
        return await QueryReadAccessByProbeAsync(userId, resourceId, dataverseToken, ct);
    }

    /// <summary>
    /// Calls Dataverse's <c>RetrievePrincipalAccess</c> function for one principal against one record.
    /// </summary>
    /// <returns>
    /// The principal's rights (possibly <see cref="AccessRights.None"/>) when Dataverse answered, or
    /// <c>null</c> when the function could not be used — the signal to fall back. The distinction
    /// matters: <c>None</c> is an authoritative "no rights", <c>null</c> is "no answer".
    /// </returns>
    private async Task<AccessRights?> TryRetrievePrincipalAccessAsync(
        string userId,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        try
        {
            // GET systemusers(<systemuserid>)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)
            //     ?@p1={"@odata.id":"sprk_documents(<recordid>)"}
            // The function is bound to the PRINCIPAL; Target names the record. The response carries a
            // comma-separated rights string ("ReadAccess,WriteAccess,AppendToAccess,...") — exactly the
            // shape MapDataverseAccessRights and PrincipalAccessResponse were written to consume.
            var target = $"{{\"@odata.id\":\"sprk_documents({resourceId})\"}}";
            var url = $"systemusers({userId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)"
                      + $"?@p1={Uri.EscapeDataString(target)}";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess returned {StatusCode} for User={UserId}, " +
                    "Resource={ResourceId}. Falling back to the read probe, which caps rights at Read — " +
                    "Write+ operations will deny for this request. ResponseBody={ResponseBody}",
                    response.StatusCode, userId, resourceId, responseBody);

                return null;
            }

            var principalAccess = await response.Content.ReadFromJsonAsync<PrincipalAccessResponse>(ct);

            if (principalAccess is null)
            {
                _logger.LogWarning(
                    "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess returned an unparseable body for " +
                    "User={UserId}, Resource={ResourceId}. Falling back to the read probe.",
                    userId, resourceId);

                return null;
            }

            // An absent/empty rights string is an authoritative "no rights", not a parse failure:
            // Dataverse answered, and the answer was nothing. MapDataverseAccessRights returns None.
            return MapDataverseAccessRights(principalAccess.AccessRights);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess threw for User={UserId}, " +
                         "Resource={ResourceId}. Falling back to the read probe.",
                userId, resourceId);

            return null;
        }
    }

    /// <summary>
    /// The original (pre-task-005) access probe, retained as the fallback path.
    /// If the principal can retrieve the record, they have at least Read; otherwise nothing.
    /// Grants at most <see cref="AccessRights.Read"/> — it cannot observe Write/Delete/Share.
    /// </summary>
    private async Task<List<PermissionRecord>> QueryReadAccessByProbeAsync(
        string userId,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        try
        {
            // APPROACH: Query the document directly using the OBO token.
            // If the query succeeds, the user has at least Read access (Dataverse enforces this).
            // If it fails with 403/404, they don't have access.

            _logger.LogInformation(
                "[UAC-DIAG] Checking document access via direct query: User={UserId}, Resource={ResourceId}",
                userId, resourceId);

            // Query the document - just retrieve the ID to minimize data transfer
            var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

            // Create request message with the OBO token
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Capture response body for diagnostics
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] Document query FAILED: StatusCode={StatusCode}, User={UserId}, Resource={ResourceId}, ResponseBody={ResponseBody}",
                    response.StatusCode, userId, resourceId, responseBody);

                // 403 or 404 means no access
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Log specific failure reason for diagnostics
                    var failureReason = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Document not found (404) - possible replication lag or invalid ID"
                        : "Access forbidden (403) - user lacks permission to this record";

                    _logger.LogWarning(
                        "[UAC-DIAG] Access denied: {FailureReason}, User={UserId}, Resource={ResourceId}",
                        failureReason, userId, resourceId);

                    return new List<PermissionRecord>();
                }

                // Other errors - log and return empty (fail-closed)
                return new List<PermissionRecord>();
            }

            // Success: the principal can retrieve the document, so they hold at least Read.
            _logger.LogInformation(
                "[UAC-DIAG] Document query SUCCESS (fallback probe): User={UserId}, Resource={ResourceId}, GrantedAccess=Read",
                userId, resourceId);

            return new List<PermissionRecord>
            {
                // Read only. This probe cannot observe Write/Delete/Create/Share — it only knows the
                // record was retrievable. The old comment here claimed "Dataverse will enforce
                // Write/Delete separately"; on the SPA/Teams surface that is false, because the BFF
                // filter IS the enforcement point (finding A-20). RetrievePrincipalAccess above is the
                // path that answers the full question; reaching here means it was unavailable.
                new PermissionRecord(userId, resourceId, AccessRights.Read)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying Dataverse access for {UserId} on {ResourceId}", userId, resourceId);
            return new List<PermissionRecord>();
        }
    }

    /// <summary>
    /// Maps a Dataverse rights string to <see cref="AccessRights"/> flags, and logs the mapping.
    /// </summary>
    private AccessRights MapDataverseAccessRights(string? accessRightsString)
    {
        var accessRights = DataverseAccessRightsMapper.FromAccessRightsString(accessRightsString);

        _logger.LogDebug("Mapped Dataverse rights '{Rights}' to {AccessRights}",
            accessRightsString, accessRights);

        return accessRights;
    }

    /// <summary>
    /// Queries user's team memberships.
    /// </summary>
    private async Task<IEnumerable<string>> QueryUserTeamMembershipsAsync(string userId, CancellationToken ct)
    {
        try
        {
            // OData query: GET /systemusers(userId)/teammembership_association?$select=name
            var url = $"systemusers({userId})/teammembership_association?$select=name,teamid";

            _logger.LogDebug("Querying team memberships: {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query team memberships: {StatusCode}", response.StatusCode);
                return Array.Empty<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<TeamDto>>(ct);

            if (result?.Value == null)
            {
                return Array.Empty<string>();
            }

            return result.Value.Select(t => t.TeamId ?? t.Name ?? "unknown").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying team memberships for {UserId}", userId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Queries user's security roles.
    /// </summary>
    private async Task<IEnumerable<string>> QueryUserRolesAsync(string userId, CancellationToken ct)
    {
        try
        {
            // OData query: GET /systemusers(userId)/systemuserroles_association?$select=name
            var url = $"systemusers({userId})/systemuserroles_association?$select=name,roleid";

            _logger.LogDebug("Querying user roles: {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query user roles: {StatusCode}", response.StatusCode);
                return Array.Empty<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<RoleDto>>(ct);

            if (result?.Value == null)
            {
                return Array.Empty<string>();
            }

            return result.Value.Select(r => r.Name ?? "unknown").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying user roles for {UserId}", userId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Aggregates granular access rights from all permission records.
    /// Combines permissions using bitwise OR to allow cumulative rights.
    /// </summary>
    /// <param name="permissions">List of permission records from Dataverse</param>
    /// <returns>Combined AccessRights from all sources (teams, roles, direct grants)</returns>
    private AccessRights DetermineAccessLevel(List<PermissionRecord> permissions)
    {
        if (!permissions.Any())
        {
            return AccessRights.None;
        }

        // Aggregate all permissions (user may have rights from multiple sources: direct, teams, roles)
        var aggregatedRights = AccessRights.None;

        foreach (var permission in permissions)
        {
            aggregatedRights |= permission.AccessRights;
        }

        _logger.LogDebug("Aggregated access rights: {AccessRights} from {PermissionCount} permission record(s)",
            aggregatedRights, permissions.Count);

        return aggregatedRights;
    }

    // DTOs for Dataverse responses
    private record PermissionRecord(string UserId, string ResourceId, AccessRights AccessRights);

    private class ODataResponse<T>
    {
        public List<T>? Value { get; set; }
    }

    private class PrincipalAccessResponse
    {
        public string? AccessRights { get; set; }
    }

    private class TeamDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("teamid")]
        public string? TeamId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class RoleDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("roleid")]
        public string? RoleId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class SystemUserDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("systemuserid")]
        public string? SystemUserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fullname")]
        public string? FullName { get; set; }
    }
}
