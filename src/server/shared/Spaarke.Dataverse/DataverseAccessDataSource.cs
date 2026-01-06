using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    private readonly string _apiUrl;
    private AccessToken? _currentToken;

    public DataverseAccessDataSource(
        IDataverseService dataverseService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseAccessDataSource> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];
        var clientSecret = configuration["Dataverse:ClientSecret"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        // Use managed identity if no client secret, otherwise use client credentials
        if (!string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId))
        {
            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _logger.LogInformation("DataverseAccessDataSource using ClientSecretCredential");
        }
        else
        {
            _credential = new DefaultAzureCredential();
            _logger.LogInformation("DataverseAccessDataSource using DefaultAzureCredential (managed identity)");
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("DataverseAccessDataSource: Refreshed Dataverse access token");
        }
    }

    public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        _logger.LogInformation(
            "[UAC-DIAG] GetUserAccessAsync START: AzureAdOid={UserId}, ResourceId={ResourceId}",
            userId, resourceId);

        try
        {
            // Ensure we have a valid access token for Dataverse
            await EnsureAuthenticatedAsync(ct);

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
            var permissions = await QueryUserPermissionsAsync(dataverseUserId, resourceId, ct);

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
            _logger.LogInformation("Found Dataverse user {FullName} (systemuserid: {SystemUserId}) for Azure AD OID {AzureAdOid}",
                user.FullName, user.SystemUserId, azureAdObjectId);

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
    /// Uses RetrievePrincipalAccess to get native Dataverse permissions.
    /// </summary>
    private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(string userId, string resourceId, CancellationToken ct)
    {
        try
        {
            // Use Dataverse's RetrievePrincipalAccess function to check native permissions
            // This respects Business Units, Security Roles, Teams, and Record Sharing
            // Format: POST /api/data/v9.2/RetrievePrincipalAccess with @odata.id references
            var request = new Dictionary<string, object>
            {
                ["Target"] = new Dictionary<string, string>
                {
                    ["@odata.id"] = $"sprk_documents({resourceId})"
                },
                ["Principal"] = new Dictionary<string, string>
                {
                    ["@odata.id"] = $"systemusers({userId})"
                }
            };

            _logger.LogInformation(
                "[UAC-DIAG] RetrievePrincipalAccess: User={UserId}, Resource={ResourceId}, Entity=sprk_documents",
                userId, resourceId);

            var response = await _httpClient.PostAsJsonAsync("RetrievePrincipalAccess", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Capture response body for diagnostics
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] RetrievePrincipalAccess FAILED: StatusCode={StatusCode}, User={UserId}, Resource={ResourceId}, ResponseBody={ResponseBody}",
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

                return new List<PermissionRecord>();
            }

            var result = await response.Content.ReadFromJsonAsync<PrincipalAccessResponse>(ct);

            if (result == null)
            {
                _logger.LogWarning(
                    "[UAC-DIAG] RetrievePrincipalAccess returned null/empty: User={UserId}, Resource={ResourceId}",
                    userId, resourceId);
                return new List<PermissionRecord>();
            }

            // Map Dataverse AccessRights string to our granular AccessRights enum
            var accessRights = MapDataverseAccessRights(result.AccessRights);

            _logger.LogInformation(
                "[UAC-DIAG] RetrievePrincipalAccess SUCCESS: User={UserId}, Resource={ResourceId}, DataverseRights={DataverseRights}, MappedRights={MappedRights}",
                userId, resourceId, result.AccessRights, accessRights);

            return new List<PermissionRecord>
            {
                new PermissionRecord(userId, resourceId, accessRights)
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
