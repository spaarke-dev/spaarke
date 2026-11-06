using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using OwnershipDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using ScriptedUserClient =
    Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests.ScriptedUserClient;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// field-mapping-server-write-path-r1 task 049 fix round 2 (UAC-r2 ruling D1): the secure-from-creation create and
/// <c>provision-project</c>'s <see cref="DelegationRuleFilter"/> ask ONE rule — <see cref="DelegationRuleFilter.RefusalFor"/>
/// — with no second copy; and the caller's rights on a record that does not exist yet
/// (<see cref="CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync"/>) follow Dataverse's depth model.
/// </summary>
[Trait("status", "new")]
public sealed class DelegationRuleSharedPredicateTests
{
    private static readonly Guid Me = OwnershipDirectory.CallerUserId;
    private static readonly Guid Team = OwnershipDirectory.ChildTeam;

    // ── The one decision ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AccessRights.None, false, DelegationRuleFilter.DenyWriteRequired)]
    [InlineData(AccessRights.Share, false, DelegationRuleFilter.DenyWriteRequired)]
    [InlineData(AccessRights.Read | AccessRights.Share, false, DelegationRuleFilter.DenyWriteRequired)]
    [InlineData(AccessRights.Write, false, DelegationRuleFilter.DenyShareRequired)]
    [InlineData(AccessRights.Read | AccessRights.Write, false, DelegationRuleFilter.DenyShareRequired)]
    [InlineData(AccessRights.Write | AccessRights.Share, false, null)]
    [InlineData(AccessRights.Write, true, null)]
    [InlineData(AccessRights.None, true, DelegationRuleFilter.DenyWriteRequired)]
    public void RefusalFor_IsWriteFirst_ThenShareUnlessWriteSuffices(AccessRights rights, bool writeSuffices, string? expected)
    {
        DelegationRuleFilter.RefusalFor(rights, writeSuffices).Should().Be(expected);
        (expected is null).Should().Be(
            (rights & AccessRights.Write) == AccessRights.Write && (writeSuffices || DelegationRuleFilter.MayManageAccess(rights)),
            "the decision is the filter's MayManageAccess, not another copy");
    }

    [Fact]
    public void TheFilterAndTheSecureCreate_CallTheSameDecision_AndTheCreateHoldsNoCopyOfTheRule()
    {
        var root = RepoRoot();
        var filter = File.ReadAllText(Path.Combine(root, "src", "server", "api", "Sprk.Bff.Api", "Api", "ExternalAccess",
            "DelegationRuleFilter.cs"));
        var create = File.ReadAllText(Path.Combine(root, "src", "server", "api", "Sprk.Bff.Api", "Services", "RecordCreation",
            "RecordCreationService.SecureFromCreation.cs"));

        filter.Should().Contain("var refusal = RefusalFor(rights, target.Value.WriteSuffices);",
            "the filter decides through the shared predicate");
        create.Should().Contain("DelegationRuleFilter.RefusalFor(rights)", "the create decides through the same predicate");
        create.Should().NotContain("AccessRights.Share").And.NotContain("AccessRights.Write").And.NotContain("MayManageAccess",
            "no second copy of the rule in the create");
    }

    // ── The caller's rights on a record the team will own (the depth model) ───────────────────────────────────────────

    [Theory]
    [InlineData("Global", "secure", true)]  // Global reaches every record
    [InlineData("Deep", "general", true)]   // the team's unit is under the anchor
    [InlineData("Deep", "child", true)]     // the team's unit is the anchor
    [InlineData("Deep", "secure", false)]   // a sibling unit
    [InlineData("Local", "child", true)]    // the team's own unit
    [InlineData("Local", "general", false)] // Local does not reach down
    [InlineData("Basic", "child", false)]   // Basic is not counted for a team-owned target
    public async Task RightsOnRecordOwnedByTeam_FollowsTheDepthModel(string depth, string anchor, bool reaches)
    {
        var user = Caller(depth, anchor switch
        {
            "general" => OwnershipDirectory.GeneralBu,
            "child" => OwnershipDirectory.ChildBu,
            _ => OwnershipDirectory.SecureBu,
        });

        var (rights, failure) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        failure.Should().BeNull();
        rights.Should().Be(reaches ? AccessRights.Write | AccessRights.Share : AccessRights.None);
    }

    // Fix round 3 (item 10, UAC-r2): Basic is not counted for a team-owned target, member or not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RightsOnRecordOwnedByTeam_Basic_AsAMemberOfTheOwningTeam_DoesNotReach(bool member)
    {
        var user = Caller("Basic", OwnershipDirectory.ChildBu);
        if (member)
            user.TeamMemberships.Add(Team);

        var (rights, failure) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        failure.Should().BeNull();
        rights.Should().Be(AccessRights.None, "Basic is not counted for a team-owned target");
    }

    [Theory]
    [InlineData("Local", true)]
    [InlineData("Local", false)]
    [InlineData("Deep", true)]
    [InlineData("Deep", false)]
    [InlineData("Global", true)]
    [InlineData("Global", false)]
    public async Task RightsOnRecordOwnedByTeam_LocalDeepAndGlobal_Reach_MemberOrNot(string depth, bool member)
    {
        // Anchored at the owning team's own unit, which every one of the three covers.
        var user = Caller(depth, OwnershipDirectory.ChildBu);
        if (member)
            user.TeamMemberships.Add(Team);

        var (rights, failure) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        failure.Should().BeNull();
        rights.Should().Be(AccessRights.Write | AccessRights.Share);
    }

    [Fact]
    public async Task RightsOnRecordOwnedByTeam_AGrantWithNoAnchor_IsNotCounted()
    {
        var user = Caller("Global", Guid.Empty);

        var (rights, _) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        rights.Should().Be(AccessRights.None);
    }

    [Fact]
    public async Task RightsOnRecordOwnedByTeam_Basic_NeverReadsTheTeamMembership_SoAMembershipFaultCannotMatter()
    {
        var user = Caller("Basic", OwnershipDirectory.ChildBu);
        var asked = new List<string>();
        user.GetOverride = path =>
        {
            asked.Add(path);
            return path.Contains("/teammembership_association", StringComparison.Ordinal)
                ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.BadRequest, "Scripted fault.")
                : null;
        };

        var (rights, failure) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        failure.Should().BeNull();
        rights.Should().Be(AccessRights.None);
        asked.Should().NotContain(p => p.Contains("teammembership", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RightsOnRecordOwnedByTeam_WhenTheHierarchyQuestionFails_DeepDoesNotReach()
    {
        var user = Caller("Deep", OwnershipDirectory.GeneralBu);
        user.HierarchyFault = 503;

        var (rights, _) = await CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync(
            user, Me, "sprk_project", Team, CancellationToken.None);

        rights.Should().Be(AccessRights.None, "a question Dataverse does not answer never grants");
    }

    private static ScriptedUserClient Caller(string depth, Guid anchor)
    {
        var user = new ScriptedUserClient(Me);
        user.BusinessUnitOf[Team] = OwnershipDirectory.ChildBu;
        foreach (var privilege in new[] { "prvWritesprk_project", "prvSharesprk_project" })
        {
            user.Held.Add(privilege);
            user.Depths[privilege] = depth;
            user.Anchors[privilege] = anchor;
        }

        return user;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
