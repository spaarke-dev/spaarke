using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spaarke.Dataverse;

/// <summary>
/// Queries Dataverse for user access permissions and team memberships.
/// Implements fail-closed security: returns AccessLevel.None on errors.
/// </summary>
public class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseAccessDataSource> _logger;
    private readonly HttpClient _httpClient;

    public DataverseAccessDataSource(
        IDataverseService dataverseService,
        HttpClient httpClient,
        ILogger<DataverseAccessDataSource> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        _logger.LogInformation("Fetching access data for user {UserId} on resource {ResourceId}", userId, resourceId);

        try
        {
            // Query user permissions from Dataverse
            var permissions = await QueryUserPermissionsAsync(userId, resourceId, ct);

            // Query team memberships
            var teams = await QueryUserTeamMembershipsAsync(userId, ct);

            // Query user roles
            var roles = await QueryUserRolesAsync(userId, ct);

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
            _logger.LogError(ex, "Failed to fetch access data for user {UserId} on resource {ResourceId}. Fail-closed: returning AccessRights.None",
                userId, resourceId);

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
    /// Checks user's access to a specific resource using Dataverse's built-in security.
    /// Uses RetrievePrincipalAccess to get native Dataverse permissions.
    /// </summary>
    private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(string userId, string resourceId, CancellationToken ct)
    {
        try
        {
            // Use Dataverse's RetrievePrincipalAccess function to check native permissions
            // This respects Business Units, Security Roles, Teams, and Record Sharing
            var request = new
            {
                Target = new
                {
                    sprk_documentid = resourceId,
                    __metadata = new { type = "Microsoft.Dynamics.CRM.sprk_document" }
                },
                Principal = new
                {
                    systemuserid = userId,
                    __metadata = new { type = "Microsoft.Dynamics.CRM.systemuser" }
                }
            };

            _logger.LogDebug("Checking Dataverse access for user {UserId} on resource {ResourceId}", userId, resourceId);

            var response = await _httpClient.PostAsJsonAsync("RetrievePrincipalAccess", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to retrieve principal access: {StatusCode}", response.StatusCode);

                // 403 or 404 means no access
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // 403 or 404 means no access - return None instead of explicit Deny
                    // Granular model doesn't have "Deny", just absence of rights
                    return new List<PermissionRecord>();
                }

                return new List<PermissionRecord>();
            }

            var result = await response.Content.ReadFromJsonAsync<PrincipalAccessResponse>(ct);

            if (result == null)
            {
                return new List<PermissionRecord>();
            }

            // Map Dataverse AccessRights string to our granular AccessRights enum
            var accessRights = MapDataverseAccessRights(result.AccessRights);

            return new List<PermissionRecord>
            {
                new PermissionRecord(userId, resourceId, accessRights)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Dataverse access for {UserId} on {ResourceId}", userId, resourceId);
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
            _logger.LogError(ex, "Error querying team memberships for {UserId}", userId);
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
            _logger.LogError(ex, "Error querying user roles for {UserId}", userId);
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
}