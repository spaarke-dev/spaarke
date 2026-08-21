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

        // ---------------------------------------------------------------------------------------
        // FR-A1 (auth-v4 task 010) — DECOUPLED credential selection.
        //
        // These are TWO INDEPENDENT concerns and used to share a single `if`:
        //   (1) _credential — the APP-ONLY token used by EnsureAuthenticatedAsync.
        //   (2) _cca        — the OBO confidential client for DELEGATED (per-user) access.
        //
        // The old shape selected BOTH on "is a client secret present?", which had two bugs:
        //   * The app-only path ignored Graph:ManagedIdentity:Enabled entirely, so on dev — where
        //     API_CLIENT_SECRET is set BECAUSE OBO needs it — this class ran on the client secret
        //     even though MI was enabled. That is the defect FR-A1 exists to fix.
        //   * Fixing that naively (copying DataverseWebApiService's plain if/else) would have put
        //     `_cca = null` in the MI branch, so enabling MI would DISABLE OBO and every delegated
        //     access check would throw at GetDataverseTokenViaOBOAsync. DataverseWebApiService is a
        //     safe template only because it has no OBO path; this class does.
        //
        // So: gate (1) on the flag, and build (2) whenever OBO configuration exists — independent of
        // the flag. DefaultAzureCredential cannot perform an OBO exchange (ADR-028 A4), and the MI
        // flag says nothing about delegated access. Phase 2 (task 020) swaps only the credential
        // INSIDE (2) to the MI-FIC assertion; the app-only branch is untouched by that migration.
        // ---------------------------------------------------------------------------------------

        // (1) APP-ONLY credential — gated by the flag.
        var useManagedIdentity = string.Equals(
            configuration["Graph:ManagedIdentity:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        if (useManagedIdentity)
        {
            // BFF-FIX-2026-05-24: prefer the DI-injected TokenCredential (pinned to the UAMI clientId
            // by the BFF's ManagedIdentityCredentialFactory). Fall back to DefaultAzureCredential for
            // instantiation outside the BFF DI container (tooling, integration tests).
            var miClientId = configuration["ManagedIdentity:ClientId"]
                ?? configuration["Graph:ManagedIdentity:ClientId"];
            _credential = credential ?? new DefaultAzureCredential(
                string.IsNullOrEmpty(miClientId)
                    ? new DefaultAzureCredentialOptions()
                    : new DefaultAzureCredentialOptions { ManagedIdentityClientId = miClientId });
            _logger.LogInformation(
                "DataverseAccessDataSource app-only auth: Managed Identity (ADR-028; {CredentialKind}, clientId {ClientId})",
                credential != null ? "DI-injected TokenCredential" : "DefaultAzureCredential (fallback)",
                miClientId ?? "(system-assigned)");
        }
        else
        {
            // Fail fast with an actionable message rather than handing back a null/unusable credential.
            if (string.IsNullOrEmpty(tenantId))
                throw new InvalidOperationException("TENANT_ID configuration is required (Graph:ManagedIdentity:Enabled is not true)");
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("API_APP_ID configuration is required (Graph:ManagedIdentity:Enabled is not true)");
            if (string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("API_CLIENT_SECRET configuration is required (Graph:ManagedIdentity:Enabled is not true)");

            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _logger.LogInformation(
                "DataverseAccessDataSource app-only auth: ClientSecret credential (local-dev fallback)");
        }

        // (2) OBO confidential client — INDEPENDENT of the MI flag. Built whenever OBO config exists.
        //     ADR-028 E-3: the secret here is transitional; task 020 replaces it with a MI-FIC
        //     client assertion via the shared provider, and task 033 removes the secret entirely.
        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            _cca = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .WithClientSecret(clientSecret)
                .Build();
            _logger.LogInformation("DataverseAccessDataSource delegated auth: OBO available");
        }
        else
        {
            _cca = null;
            _logger.LogWarning(
                "DataverseAccessDataSource delegated auth: OBO NOT available (no confidential-client configuration). "
                + "Delegated access checks will fail closed.");
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
    /// Checks user's access to a specific resource using Dataverse's built-in security.
    /// Uses a direct query approach: If the user can retrieve the record, they have Read access.
    /// This works with OBO (delegated) tokens where RetrievePrincipalAccess may not be available.
    /// </summary>
    /// <param name="userId">Dataverse systemuserid</param>
    /// <param name="resourceId">Document resource ID</param>
    /// <param name="dataverseToken">Dataverse access token (from OBO or service principal)</param>
    /// <param name="ct">Cancellation token</param>
    private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
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
            // This is simpler and works with delegated tokens where RetrievePrincipalAccess may fail.

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

            // Success! The user can retrieve the document, so they have at least Read access.
            // For AI operations, Read access is sufficient.
            _logger.LogInformation(
                "[UAC-DIAG] Document query SUCCESS: User={UserId}, Resource={ResourceId}, GrantedAccess=Read",
                userId, resourceId);

            return new List<PermissionRecord>
            {
                // Grant Read access - if user needs Write/Delete/etc., Dataverse will enforce that separately
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
    /// Maps Dataverse permission string to AccessRights flags.
    /// Dataverse returns comma-separated string like "ReadAccess,WriteAccess,DeleteAccess".
    /// </summary>
    /// <param name="accessRightsString">Comma-separated Dataverse rights (e.g., "ReadAccess,WriteAccess")</param>
    /// <returns>Bitwise combination of AccessRights flags</returns>
    /// <example>
    /// Input: "ReadAccess,WriteAccess,DeleteAccess"
    /// Output: AccessRights.Read | AccessRights.Write | AccessRights.Delete
    /// </example>
    private AccessRights MapDataverseAccessRights(string? accessRightsString)
    {
        if (string.IsNullOrWhiteSpace(accessRightsString))
        {
            return AccessRights.None;
        }

        // Dataverse returns comma-separated flags: "ReadAccess,WriteAccess,DeleteAccess"
        var rights = accessRightsString.Split(',', StringSplitOptions.TrimEntries);
        var accessRights = AccessRights.None;

        foreach (var right in rights)
        {
            accessRights |= right switch
            {
                "ReadAccess" => AccessRights.Read,
                "WriteAccess" => AccessRights.Write,
                "DeleteAccess" => AccessRights.Delete,
                "CreateAccess" => AccessRights.Create,
                "AppendAccess" => AccessRights.Append,
                "AppendToAccess" => AccessRights.AppendTo,
                "ShareAccess" => AccessRights.Share,
                _ => AccessRights.None
            };
        }

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
