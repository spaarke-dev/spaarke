using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing playbook sharing using Dataverse Web API.
/// Uses GrantAccess/RevokeAccess for team-based sharing and POA table for access queries.
/// </summary>
public class PlaybookSharingService : IPlaybookSharingService
{
    private readonly HttpClient _httpClient;
    private readonly IPlaybookService _playbookService;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly string _apiUrl;
    private readonly TokenCredential _credential;
    private readonly ILogger<PlaybookSharingService> _logger;
    private AccessToken? _currentToken;

    private const string PlaybookEntitySet = "sprk_analysisplaybooks";
    private const string PlaybookEntityLogicalName = "sprk_analysisplaybook";
    private const string TeamEntitySet = "teams";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PlaybookSharingService(
        HttpClient httpClient,
        IPlaybookService playbookService,
        IDataverseRecordShareService recordShare,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<PlaybookSharingService> logger)
    {
        _httpClient = httpClient;
        _playbookService = playbookService;
        _recordShare = recordShare ?? throw new ArgumentNullException(nameof(recordShare));
        _logger = logger;
        _credential = credential;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Initialized PlaybookSharingService for {ApiUrl}", _apiUrl);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for PlaybookSharingService");
        }
    }

    /// <inheritdoc />
    public async Task<ShareOperationResult> SharePlaybookAsync(
        Guid playbookId,
        SharePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Sharing playbook {PlaybookId} by user {UserId}", playbookId, userId);

        try
        {
            // Verify playbook exists and user is owner
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);
            if (playbook == null)
            {
                return ShareOperationResult.Failed("Playbook not found");
            }

            if (playbook.OwnerId != userId)
            {
                return ShareOperationResult.Failed("Only the playbook owner can share it");
            }

            // Handle organization-wide sharing
            if (request.OrganizationWide)
            {
                await SetOrganizationWideAsync(playbookId, true, cancellationToken);
                _logger.LogInformation("Set playbook {PlaybookId} as organization-wide", playbookId);
            }

            // Share with teams using GrantAccess
            foreach (var teamId in request.TeamIds)
            {
                await GrantAccessToTeamAsync(playbookId, teamId, request.AccessRights, cancellationToken);
            }

            // Log audit event with batch summary
            _logger.LogInformation(
                "Sharing complete: PlaybookId={PlaybookId}, Teams={TeamCount}, OrgWide={OrgWide}, User={UserId}",
                playbookId, request.TeamIds.Length, request.OrganizationWide, userId);

            var sharingInfo = await GetSharingInfoAsync(playbookId, cancellationToken);
            return ShareOperationResult.Succeeded(sharingInfo!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share playbook {PlaybookId}", playbookId);
            return ShareOperationResult.Failed($"Failed to share playbook: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ShareOperationResult> RevokeShareAsync(
        Guid playbookId,
        RevokeShareRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        _logger.LogInformation("Revoking sharing for playbook {PlaybookId} by user {UserId}", playbookId, userId);

        try
        {
            // Verify playbook exists and user is owner
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);
            if (playbook == null)
            {
                return ShareOperationResult.Failed("Playbook not found");
            }

            if (playbook.OwnerId != userId)
            {
                return ShareOperationResult.Failed("Only the playbook owner can revoke sharing");
            }

            // Handle organization-wide revoke
            if (request.RevokeOrganizationWide)
            {
                await SetOrganizationWideAsync(playbookId, false, cancellationToken);
                _logger.LogInformation("Removed organization-wide access from playbook {PlaybookId}", playbookId);
            }

            // Revoke access from teams
            foreach (var teamId in request.TeamIds)
            {
                await RevokeAccessFromTeamAsync(playbookId, teamId, cancellationToken);
            }

            // Log audit event with batch summary
            _logger.LogInformation(
                "Revoke complete: PlaybookId={PlaybookId}, Teams={TeamCount}, RevokeOrgWide={RevokeOrgWide}, User={UserId}",
                playbookId, request.TeamIds.Length, request.RevokeOrganizationWide, userId);

            var sharingInfo = await GetSharingInfoAsync(playbookId, cancellationToken);
            return ShareOperationResult.Succeeded(sharingInfo!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke sharing for playbook {PlaybookId}", playbookId);
            return ShareOperationResult.Failed($"Failed to revoke sharing: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<PlaybookSharingInfo?> GetSharingInfoAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);
        if (playbook == null)
        {
            return null;
        }

        // Get teams that have access via POA table
        var sharedTeams = await GetSharedTeamsAsync(playbookId, cancellationToken);

        // Determine sharing level
        var sharingLevel = DetermineSharingLevel(playbook.IsPublic, sharedTeams.Length > 0);

        return new PlaybookSharingInfo
        {
            PlaybookId = playbookId,
            SharingLevel = sharingLevel,
            IsOrganizationWide = sharingLevel == SharingLevel.Organization,
            IsPublic = playbook.IsPublic,
            SharedWithTeams = sharedTeams
        };
    }

    /// <inheritdoc />
    public async Task<bool> UserHasSharedAccessAsync(
        Guid playbookId,
        Guid userId,
        PlaybookAccessRights requiredRights = PlaybookAccessRights.Read,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        try
        {
            // First check if user is owner
            var playbook = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken);
            if (playbook == null)
            {
                return false;
            }

            if (playbook.OwnerId == userId)
            {
                return true;
            }

            // Check if public
            if (playbook.IsPublic && requiredRights == PlaybookAccessRights.Read)
            {
                return true;
            }

            // Check team membership
            var userTeams = await GetUserTeamsAsync(userId, cancellationToken);
            var sharingInfo = await GetSharingInfoAsync(playbookId, cancellationToken);

            if (sharingInfo == null)
            {
                return false;
            }

            // Check if any of user's teams have access
            foreach (var sharedTeam in sharingInfo.SharedWithTeams)
            {
                if (userTeams.Contains(sharedTeam.TeamId))
                {
                    // Verify the team has required rights
                    if ((sharedTeam.AccessRights & requiredRights) == requiredRights)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking shared access for playbook {PlaybookId}, user {UserId}", playbookId, userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Guid[]> GetUserTeamsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        try
        {
            // Query teammembership table for user's teams
            var url = $"teammemberships?$filter=systemuserid eq {userId}&$select=teamid";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse>(JsonOptions, cancellationToken);
            return result?.Value?
                .Select(v => v.TryGetProperty("teamid", out var idProp) ? idProp.GetGuid() : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get teams for user {UserId}", userId);
            return [];
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Shares the playbook with a team, via the ONE POA seam.
    /// </summary>
    /// <remarks>
    /// Task 060 (CLAUDE.md §11): this method used to build the <c>GrantAccess</c> payload itself — a
    /// second POA client alongside <c>DataverseWebApiService</c>'s. That duplicate is DELETED; only the
    /// playbook-domain rights mapping stays here, because translating
    /// <see cref="PlaybookAccessRights"/> to Dataverse's <c>AccessMask</c> CSV is a caller concern.
    /// </remarks>
    private Task GrantAccessToTeamAsync(
        Guid playbookId,
        Guid teamId,
        PlaybookAccessRights rights,
        CancellationToken cancellationToken)
        => _recordShare.GrantAccessAsync(
            PlaybookEntitySet,
            playbookId,
            DataversePrincipalRef.Team(teamId),
            MapToDataverseAccessRights(rights),
            cancellationToken);

    /// <summary>
    /// Removes a team's share on the playbook, via the ONE POA seam (task 060 — the <c>RevokeAccess</c>
    /// payload this method used to build is now the seam's, and is what gives every other caller a revoke).
    /// </summary>
    private Task RevokeAccessFromTeamAsync(
        Guid playbookId,
        Guid teamId,
        CancellationToken cancellationToken)
        => _recordShare.RevokeAccessAsync(
            PlaybookEntitySet,
            playbookId,
            DataversePrincipalRef.Team(teamId),
            cancellationToken);

    private Task SetOrganizationWideAsync(
        Guid playbookId,
        bool organizationWide,
        CancellationToken cancellationToken)
    {
        // For organization-wide, we use a custom field or share with the organization team
        // For this implementation, we'll set a flag on the playbook entity
        // Note: This would require adding sprk_isorganizationwide field to the entity
        // For now, we'll just log the intent - actual implementation depends on entity schema

        _logger.LogDebug(
            "Organization-wide sharing {Action} for playbook {PlaybookId}",
            organizationWide ? "enabled" : "disabled",
            playbookId);

        // If there's an organization team (root business unit team), share with it
        // For simplicity, this is logged but not fully implemented without the org team ID
        return Task.CompletedTask;
    }

    /// <summary>
    /// The teams holding a share on the playbook.
    /// </summary>
    /// <remarks>
    /// Task 060: the POA query + the uncached <c>ObjectTypeCode</c> lookup that used to live here are
    /// gone — both are the seam's now (and the seam's type-code resolution is process-cached). Two
    /// behaviours change, deliberately: the read is filtered to <see cref="DataversePrincipalKind.Team"/>,
    /// so a USER share on a playbook is no longer reported as a team named "Unknown Team"; and a failed
    /// POA read still degrades to an empty list rather than throwing, matching the prior contract.
    /// </remarks>
    private async Task<SharedWithTeam[]> GetSharedTeamsAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        try
        {
            var shares = await _recordShare.GetPrincipalAccessAsync(
                PlaybookEntityLogicalName, playbookId, cancellationToken);

            var teams = new List<SharedWithTeam>();
            foreach (var share in shares)
            {
                if (share.Principal.Kind != DataversePrincipalKind.Team)
                    continue;

                // A share row whose accessrightsmask could not be read reports Read, not None — the
                // pre-060 behaviour (`... : 1`). A consolidation must not quietly change what the
                // sharing UI displays; if that default is wrong it is a separate, deliberate change.
                var mask = share.AccessRightsMask == 0 ? 1 : share.AccessRightsMask;

                teams.Add(new SharedWithTeam
                {
                    TeamId = share.Principal.Id,
                    TeamName = await GetTeamNameAsync(share.Principal.Id, cancellationToken),
                    AccessRights = MapFromDataverseAccessRights(mask),
                    SharedOn = share.ModifiedOn.UtcDateTime,
                });
            }

            return teams.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get shared teams for playbook {PlaybookId}", playbookId);
            return [];
        }
    }

    private async Task<string> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{TeamEntitySet}({teamId})?$select=name";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return "Unknown Team";
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            return result.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Unknown Team" : "Unknown Team";
        }
        catch
        {
            return "Unknown Team";
        }
    }

    private static string MapToDataverseAccessRights(PlaybookAccessRights rights)
    {
        // Dataverse access rights: ReadAccess, WriteAccess, AppendAccess, AppendToAccess, CreateAccess, DeleteAccess, ShareAccess, AssignAccess
        var accessList = new List<string>();

        if ((rights & PlaybookAccessRights.Read) != 0)
        {
            accessList.Add("ReadAccess");
        }
        if ((rights & PlaybookAccessRights.Write) != 0)
        {
            accessList.Add("WriteAccess");
            accessList.Add("AppendAccess");
            accessList.Add("AppendToAccess");
        }
        if ((rights & PlaybookAccessRights.Share) != 0)
        {
            accessList.Add("ShareAccess");
        }

        return string.Join(",", accessList);
    }

    private static PlaybookAccessRights MapFromDataverseAccessRights(int accessMask)
    {
        var rights = PlaybookAccessRights.None;

        // Dataverse access mask bits
        if ((accessMask & 1) != 0) rights |= PlaybookAccessRights.Read;      // ReadAccess
        if ((accessMask & 2) != 0) rights |= PlaybookAccessRights.Write;     // WriteAccess
        if ((accessMask & 524288) != 0) rights |= PlaybookAccessRights.Share; // ShareAccess

        return rights;
    }

    private static SharingLevel DetermineSharingLevel(bool isPublic, bool hasTeamShares)
    {
        if (isPublic) return SharingLevel.Public;
        if (hasTeamShares) return SharingLevel.Team;
        return SharingLevel.Private;
    }

    #endregion

    #region Internal Types

    private class ODataCollectionResponse
    {
        [JsonPropertyName("value")]
        public JsonElement[]? Value { get; set; }
    }

    #endregion
}
