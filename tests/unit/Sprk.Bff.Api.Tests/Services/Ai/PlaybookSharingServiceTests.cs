using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for PlaybookSharingService.
/// Tests team-based and organization-wide sharing functionality.
/// </summary>
public class PlaybookSharingServiceTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestTeamId1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestTeamId2 = Guid.Parse("44444444-4444-4444-4444-444444444444");

    #region SharingLevel Tests

    [Fact]
    public void SharingLevel_Private_HasCorrectValue()
    {
        Assert.Equal(0, (int)SharingLevel.Private);
    }

    [Fact]
    public void SharingLevel_Team_HasCorrectValue()
    {
        Assert.Equal(1, (int)SharingLevel.Team);
    }

    [Fact]
    public void SharingLevel_Organization_HasCorrectValue()
    {
        Assert.Equal(2, (int)SharingLevel.Organization);
    }

    [Fact]
    public void SharingLevel_Public_HasCorrectValue()
    {
        Assert.Equal(3, (int)SharingLevel.Public);
    }

    #endregion

    #region PlaybookAccessRights Tests

    [Fact]
    public void PlaybookAccessRights_None_HasCorrectValue()
    {
        Assert.Equal(0, (int)PlaybookAccessRights.None);
    }

    [Fact]
    public void PlaybookAccessRights_Read_HasCorrectValue()
    {
        Assert.Equal(1, (int)PlaybookAccessRights.Read);
    }

    [Fact]
    public void PlaybookAccessRights_Write_HasCorrectValue()
    {
        Assert.Equal(2, (int)PlaybookAccessRights.Write);
    }

    [Fact]
    public void PlaybookAccessRights_Share_HasCorrectValue()
    {
        Assert.Equal(4, (int)PlaybookAccessRights.Share);
    }

    [Fact]
    public void PlaybookAccessRights_Full_IncludesAllRights()
    {
        var full = PlaybookAccessRights.Full;

        Assert.True(full.HasFlag(PlaybookAccessRights.Read));
        Assert.True(full.HasFlag(PlaybookAccessRights.Write));
        Assert.True(full.HasFlag(PlaybookAccessRights.Share));
    }

    [Fact]
    public void PlaybookAccessRights_CanCombineFlags()
    {
        var readWrite = PlaybookAccessRights.Read | PlaybookAccessRights.Write;

        Assert.True(readWrite.HasFlag(PlaybookAccessRights.Read));
        Assert.True(readWrite.HasFlag(PlaybookAccessRights.Write));
        Assert.False(readWrite.HasFlag(PlaybookAccessRights.Share));
    }

    #endregion

    #region SharePlaybookRequest Tests

    [Fact]
    public void SharePlaybookRequest_DefaultValues_AreCorrect()
    {
        var request = new SharePlaybookRequest();

        Assert.Empty(request.TeamIds);
        Assert.Equal(PlaybookAccessRights.Read, request.AccessRights);
        Assert.False(request.OrganizationWide);
    }

    [Fact]
    public void SharePlaybookRequest_WithTeams_ContainsTeamIds()
    {
        var request = new SharePlaybookRequest
        {
            TeamIds = [TestTeamId1, TestTeamId2],
            AccessRights = PlaybookAccessRights.Read
        };

        Assert.Equal(2, request.TeamIds.Length);
        Assert.Contains(TestTeamId1, request.TeamIds);
        Assert.Contains(TestTeamId2, request.TeamIds);
    }

    [Fact]
    public void SharePlaybookRequest_WithOrganizationWide_SetsFlag()
    {
        var request = new SharePlaybookRequest
        {
            OrganizationWide = true
        };

        Assert.True(request.OrganizationWide);
    }

    [Fact]
    public void SharePlaybookRequest_WithWriteAccess_SetsAccessRights()
    {
        var request = new SharePlaybookRequest
        {
            TeamIds = [TestTeamId1],
            AccessRights = PlaybookAccessRights.Write
        };

        Assert.Equal(PlaybookAccessRights.Write, request.AccessRights);
    }

    [Fact]
    public void SharePlaybookRequest_WithFullAccess_SetsAccessRights()
    {
        var request = new SharePlaybookRequest
        {
            TeamIds = [TestTeamId1],
            AccessRights = PlaybookAccessRights.Full
        };

        Assert.Equal(PlaybookAccessRights.Full, request.AccessRights);
    }

    #endregion

    #region RevokeShareRequest Tests

    [Fact]
    public void RevokeShareRequest_DefaultValues_AreCorrect()
    {
        var request = new RevokeShareRequest();

        Assert.Empty(request.TeamIds);
        Assert.False(request.RevokeOrganizationWide);
    }

    [Fact]
    public void RevokeShareRequest_WithTeams_ContainsTeamIds()
    {
        var request = new RevokeShareRequest
        {
            TeamIds = [TestTeamId1, TestTeamId2]
        };

        Assert.Equal(2, request.TeamIds.Length);
    }

    [Fact]
    public void RevokeShareRequest_WithRevokeOrganizationWide_SetsFlag()
    {
        var request = new RevokeShareRequest
        {
            RevokeOrganizationWide = true
        };

        Assert.True(request.RevokeOrganizationWide);
    }

    #endregion

    #region PlaybookSharingInfo Tests

    [Fact]
    public void PlaybookSharingInfo_Private_HasCorrectLevel()
    {
        var info = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Private,
            IsOrganizationWide = false,
            IsPublic = false,
            SharedWithTeams = []
        };

        Assert.Equal(SharingLevel.Private, info.SharingLevel);
        Assert.Empty(info.SharedWithTeams);
    }

    [Fact]
    public void PlaybookSharingInfo_WithTeams_HasTeamLevel()
    {
        var sharedTeam = new SharedWithTeam
        {
            TeamId = TestTeamId1,
            TeamName = "Test Team",
            AccessRights = PlaybookAccessRights.Read,
            SharedOn = DateTime.UtcNow
        };

        var info = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Team,
            IsOrganizationWide = false,
            IsPublic = false,
            SharedWithTeams = [sharedTeam]
        };

        Assert.Equal(SharingLevel.Team, info.SharingLevel);
        Assert.Single(info.SharedWithTeams);
        Assert.Equal("Test Team", info.SharedWithTeams[0].TeamName);
    }

    [Fact]
    public void PlaybookSharingInfo_OrganizationWide_HasOrgLevel()
    {
        var info = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Organization,
            IsOrganizationWide = true,
            IsPublic = false
        };

        Assert.Equal(SharingLevel.Organization, info.SharingLevel);
        Assert.True(info.IsOrganizationWide);
    }

    [Fact]
    public void PlaybookSharingInfo_Public_HasPublicLevel()
    {
        var info = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Public,
            IsOrganizationWide = false,
            IsPublic = true
        };

        Assert.Equal(SharingLevel.Public, info.SharingLevel);
        Assert.True(info.IsPublic);
    }

    #endregion

    #region SharedWithTeam Tests

    [Fact]
    public void SharedWithTeam_ContainsAllFields()
    {
        var sharedOn = DateTime.UtcNow;
        var team = new SharedWithTeam
        {
            TeamId = TestTeamId1,
            TeamName = "Development Team",
            AccessRights = PlaybookAccessRights.Read | PlaybookAccessRights.Write,
            SharedOn = sharedOn
        };

        Assert.Equal(TestTeamId1, team.TeamId);
        Assert.Equal("Development Team", team.TeamName);
        Assert.True(team.AccessRights.HasFlag(PlaybookAccessRights.Read));
        Assert.True(team.AccessRights.HasFlag(PlaybookAccessRights.Write));
        Assert.Equal(sharedOn, team.SharedOn);
    }

    #endregion

    #region ShareOperationResult Tests

    [Fact]
    public void ShareOperationResult_Succeeded_ReturnsSuccessResult()
    {
        var sharingInfo = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Team
        };

        var result = ShareOperationResult.Succeeded(sharingInfo);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.SharingInfo);
        Assert.Equal(TestPlaybookId, result.SharingInfo.PlaybookId);
    }

    [Fact]
    public void ShareOperationResult_Failed_ReturnsErrorResult()
    {
        var result = ShareOperationResult.Failed("Team not found");

        Assert.False(result.Success);
        Assert.Equal("Team not found", result.ErrorMessage);
        Assert.Null(result.SharingInfo);
    }

    [Fact]
    public void ShareOperationResult_Failed_WithDifferentErrors()
    {
        var result1 = ShareOperationResult.Failed("Playbook not found");
        var result2 = ShareOperationResult.Failed("User does not own this playbook");
        var result3 = ShareOperationResult.Failed("Invalid team ID");

        Assert.Equal("Playbook not found", result1.ErrorMessage);
        Assert.Equal("User does not own this playbook", result2.ErrorMessage);
        Assert.Equal("Invalid team ID", result3.ErrorMessage);
    }

    #endregion

    #region Access Rights Hierarchy Tests

    [Fact]
    public void AccessRights_Read_DoesNotIncludeWrite()
    {
        var readOnly = PlaybookAccessRights.Read;

        Assert.True(readOnly.HasFlag(PlaybookAccessRights.Read));
        Assert.False(readOnly.HasFlag(PlaybookAccessRights.Write));
        Assert.False(readOnly.HasFlag(PlaybookAccessRights.Share));
    }

    [Fact]
    public void AccessRights_Write_DoesNotAutomaticallyIncludeRead()
    {
        // Write flag is separate - must combine explicitly
        var writeOnly = PlaybookAccessRights.Write;

        Assert.False(writeOnly.HasFlag(PlaybookAccessRights.Read));
        Assert.True(writeOnly.HasFlag(PlaybookAccessRights.Write));
    }

    [Fact]
    public void AccessRights_ReadWrite_IncludesBoth()
    {
        var readWrite = PlaybookAccessRights.Read | PlaybookAccessRights.Write;

        Assert.True(readWrite.HasFlag(PlaybookAccessRights.Read));
        Assert.True(readWrite.HasFlag(PlaybookAccessRights.Write));
        Assert.False(readWrite.HasFlag(PlaybookAccessRights.Share));
    }

    #endregion

    #region Sharing Scenarios Tests

    [Fact]
    public void SharingScenario_MultipleTeams_AllReceiveAccess()
    {
        var teams = new[]
        {
            new SharedWithTeam
            {
                TeamId = TestTeamId1,
                TeamName = "Team A",
                AccessRights = PlaybookAccessRights.Read
            },
            new SharedWithTeam
            {
                TeamId = TestTeamId2,
                TeamName = "Team B",
                AccessRights = PlaybookAccessRights.Full
            }
        };

        var info = new PlaybookSharingInfo
        {
            PlaybookId = TestPlaybookId,
            SharingLevel = SharingLevel.Team,
            SharedWithTeams = teams
        };

        Assert.Equal(2, info.SharedWithTeams.Length);
        Assert.Equal(PlaybookAccessRights.Read, info.SharedWithTeams[0].AccessRights);
        Assert.Equal(PlaybookAccessRights.Full, info.SharedWithTeams[1].AccessRights);
    }

    [Fact]
    public void SharingScenario_EmptyTeamIds_IsValid()
    {
        var request = new SharePlaybookRequest
        {
            TeamIds = [],
            OrganizationWide = true // Can share org-wide without specific teams
        };

        Assert.Empty(request.TeamIds);
        Assert.True(request.OrganizationWide);
    }

    #endregion
}
