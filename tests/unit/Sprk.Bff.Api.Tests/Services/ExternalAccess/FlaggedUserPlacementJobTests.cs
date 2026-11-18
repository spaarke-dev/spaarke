using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191e (#2179 design v2 section 3.13): the scheduled job that moves each systemuser whose
/// stored <c>sprk_isexternal</c> reads true into the External Collaborators unit, assigns the External Collaborator role from
/// that unit and reads both back.
/// </summary>
/// <remarks>
/// <para><b>The one seam</b> is <see cref="IGenericEntityService"/>, mocked STRICT over <see cref="PlacementDirectory"/>, an
/// in-memory directory that answers queries by evaluating their conditions and behaves as spike S2 found (design A.4): a
/// business-unit PATCH strips the user's direct roles and swaps its default-team membership. Any call the job should not make
/// fails the test.</para>
/// <para><b>Model 1 (BINDING).</b> A <c>#EXT#</c> guest whose flag is blank or false is a customer's licensed core user, not
/// an external one: the must-not-fire tests below give such users a <c>#EXT#</c> domain name and prove they are never moved.</para>
/// </remarks>
public class FlaggedUserPlacementJobTests
{
    private static readonly Guid RootBu = Guid.Parse("00000000-0000-0000-0000-00000000e001");
    private static readonly Guid CustomerBu = Guid.Parse("00000000-0000-0000-0000-00000000e002");
    private static readonly Guid ExternalBu = Guid.Parse("00000000-0000-0000-0000-00000000e003");

    private static readonly Guid ExternalRole = Guid.Parse("00000000-0000-0000-0000-00000000e101");
    private static readonly Guid BasicUserCustomerCopy = Guid.Parse("00000000-0000-0000-0000-00000000e102");
    private static readonly Guid SysAdminRootCopy = Guid.Parse("00000000-0000-0000-0000-00000000e103");
    private static readonly Guid SysAdminCustomerCopy = Guid.Parse("00000000-0000-0000-0000-00000000e104");

    private static readonly Guid AdminTeam = Guid.Parse("00000000-0000-0000-0000-00000000e201");

    private readonly PlacementDirectory _directory = new();
    private readonly PlacementLog<FlaggedUserPlacementJob> _log = new();

    public FlaggedUserPlacementJobTests()
    {
        _directory.BusinessUnit(RootBu, "Spaarke", null);
        _directory.BusinessUnit(CustomerBu, "Customer A", RootBu);
        _directory.BusinessUnit(ExternalBu, ExternalCollaboratorPlacement.BusinessUnitName, RootBu);

        _directory.Role(ExternalRole, ExternalCollaboratorPlacement.RoleName, ExternalBu);
        _directory.Role(BasicUserCustomerCopy, "Spaarke Basic User", CustomerBu);
        _directory.Role(SysAdminRootCopy, "System Administrator", RootBu, SecuredColumnReadProof.SystemAdministratorRoleTemplateId);
        _directory.Role(SysAdminCustomerCopy, "System Administrator", CustomerBu, SecuredColumnReadProof.SystemAdministratorRoleTemplateId);

        _directory.Team(AdminTeam, CustomerBu, isDefault: false, teamType: 0, roles: [SysAdminCustomerCopy]);
    }

    // =====================================================================================================
    // Schedule and identity
    // =====================================================================================================

    [Fact]
    public void Schedule_IsEveryTwoMinutes_AndTheTickMatchesTheCron()
    {
        FlaggedUserPlacementJob.DefaultCronSchedule.Should().Be("*/2 * * * *", "design 3.13: every 2 minutes");
        FlaggedUserPlacementJob.DefaultCronSchedule.Should().StartWith($"*/{FlaggedUserPlacementJob.TickInterval.TotalMinutes:0} ");
        FlaggedUserPlacementJob.JobIdConstant.Should().Be("flagged-user-placement");
        FlaggedUserPlacementJob.WritesEnabledConfigKey.Should().Be("ExternalAccess:FlaggedUserPlacement:WritesEnabled");
    }

    [Fact]
    public void TheUnitAndRoleNames_AreTheOnesTask191dsConfigDefines()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(RepoFile("config", "external-collaborator-role.json")));

        config.RootElement.GetProperty("businessUnit").GetProperty("name").GetString()
            .Should().Be(ExternalCollaboratorPlacement.BusinessUnitName);
        config.RootElement.GetProperty("roleName").GetString().Should().Be(ExternalCollaboratorPlacement.RoleName);
    }

    // =====================================================================================================
    // Must fire: a definite-true user outside the unit is placed
    // =====================================================================================================

    [Fact]
    public async Task WritesOn_MovesADefiniteTrueUser_AssignsOnlyTheExternalRole_AndReadsBothBack()
    {
        var user = _directory.User(CustomerBu, flag: true, roles: [BasicUserCustomerCopy]);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.BusinessUnitOf(user).Should().Be(ExternalBu);
        _directory.DirectRolesOf(user).Should().Equal(ExternalRole);
        _directory.Writes.Should().Equal($"update systemuser {user} businessunitid={ExternalBu}", $"associate {user} role {ExternalRole}");
        _directory.RetrievesOf(user).Should().Be(2, "one fresh read before the write and one read-back after it");
        Json(result).GetProperty("moved").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(user);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical, "a successful run raises no alert");
        _log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat status=ok mode=writes"));
    }

    [Fact]
    public async Task WritesOn_AUserInTheUnitWithoutTheRole_GetsTheRoleOnly_AndIsNeverMoved()
    {
        var user = _directory.User(ExternalBu, flag: true);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.Writes.Should().Equal($"associate {user} role {ExternalRole}");
        _directory.DirectRolesOf(user).Should().Equal(ExternalRole);
        Json(result).GetProperty("roleAssigned").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task APlacedUser_IsNotTouched()
    {
        _directory.User(ExternalBu, flag: true, roles: [ExternalRole]);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue();
        _directory.Writes.Should().BeEmpty();
        Json(result).GetProperty("alreadyPlaced").GetInt32().Should().Be(1);
    }

    // =====================================================================================================
    // Must not fire: never moves blank, false, unreadable, disabled, application or administrator users
    // =====================================================================================================

    /// <summary>
    /// Model 1 must-not-fire: a <c>#EXT#</c> guest whose flag is blank or false is a licensed core user. Even when Dataverse
    /// returns it for the flag query (the condition ignored), the job's own re-check keeps it internal.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task AnExtGuestWhoseFlagIsBlankOrFalse_IsNeverMoved_EvenIfDataverseReturnedIt(bool? flag)
    {
        _directory.IgnoreConditionOn(ExternalCollaboratorPlacement.IsExternalColumn);
        var guest = _directory.User(CustomerBu, flag: flag, roles: [BasicUserCustomerCopy],
            domainName: "core.user_customer.example#EXT#@spaarke.example");

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.Writes.Should().BeEmpty();
        _directory.BusinessUnitOf(guest).Should().Be(CustomerBu);
        Json(result).GetProperty("flagged").GetInt32().Should().Be(0, "only a stored true is external");
    }

    [Fact]
    public async Task AFlagThatIsNotReturned_Masked_IsInternal_AndNeverMoved()
    {
        _directory.IgnoreConditionOn(ExternalCollaboratorPlacement.IsExternalColumn);
        var masked = _directory.User(CustomerBu, flag: true);
        _directory.HideColumn(masked, ExternalCollaboratorPlacement.IsExternalColumn);

        await RunAsync(writes: true);

        _directory.Writes.Should().BeEmpty();
    }

    public static TheoryData<string> NeverMovedKinds => new()
    {
        "disabled", "application", "sysadmin-direct", "sysadmin-team", "administrative-mode", "non-interactive-mode",
        "support-mode", "unreadable-disabled", "unreadable-access-mode", "unreadable-business-unit",
    };

    [Theory]
    [MemberData(nameof(NeverMovedKinds))]
    public async Task DisabledApplicationAdministratorAndUnreadableUsers_AreNeverMoved(string kind)
    {
        var user = kind switch
        {
            "disabled" => _directory.User(CustomerBu, flag: true, disabled: true),
            "application" => _directory.User(CustomerBu, flag: true, application: true, accessMode: 4),
            "sysadmin-direct" => _directory.User(CustomerBu, flag: true, roles: [SysAdminCustomerCopy]),
            "sysadmin-team" => _directory.User(CustomerBu, flag: true),
            "administrative-mode" => _directory.User(CustomerBu, flag: true, accessMode: 1),
            "non-interactive-mode" => _directory.User(CustomerBu, flag: true, accessMode: 4),
            "support-mode" => _directory.User(CustomerBu, flag: true, accessMode: 3),
            _ => _directory.User(CustomerBu, flag: true),
        };
        if (kind == "sysadmin-team") _directory.AddMember(AdminTeam, user);
        if (kind == "unreadable-disabled") _directory.HideColumn(user, "isdisabled");
        if (kind == "unreadable-access-mode") _directory.HideColumn(user, "accessmode");
        if (kind == "unreadable-business-unit") _directory.HideColumn(user, "businessunitid");

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.Writes.Should().BeEmpty();
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AUserInsideTheUnit_IsNeverMovedOut_FlagOrNoFlag(bool? flag)
    {
        var inside = _directory.User(ExternalBu, flag: flag, roles: [ExternalRole]);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.Writes.Should().NotContain(w => w.StartsWith("update", StringComparison.Ordinal));
        _directory.BusinessUnitOf(inside).Should().Be(ExternalBu);
        var unflagged = Json(result).GetProperty("unflaggedInUnit").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid());
        if (flag == true)
            unflagged.Should().BeEmpty();
        else
            unflagged.Should().Equal(inside); // reported for an administrator, never acted on (round 115)
    }

    [Fact]
    public async Task AFlagClearedBetweenTheCandidateReadAndTheWrite_StopsTheWrite()
    {
        var user = _directory.User(CustomerBu, flag: true);
        _directory.BeforeRetrieve = id => { if (id == user) _directory.SetFlag(user, false); };

        var result = await RunAsync(writes: true);

        _directory.Writes.Should().BeEmpty();
        Json(result).GetProperty("changed").GetInt32().Should().Be(1);
    }

    // =====================================================================================================
    // Report-only (the default)
    // =====================================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("yes")]
    public async Task WritesOff_WritesNothing_AndListsTheIdsItWouldMove(string? setting)
    {
        var mover = _directory.User(CustomerBu, flag: true, roles: [BasicUserCustomerCopy]);
        var roleless = _directory.User(ExternalBu, flag: true);

        var result = await RunAsync(setting: setting);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _directory.Writes.Should().BeEmpty();
        _directory.RetrievesOf(mover).Should().Be(0);
        var json = Json(result);
        json.GetProperty("mode").GetString().Should().Be("report-only");
        json.GetProperty("wouldMove").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(mover);
        json.GetProperty("wouldAssignRole").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(roleless);
        _log.Entries.Should().Contain(e => e.Message.Contains("REPORT-ONLY") && e.Message.Contains(mover.ToString()));
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
    }

    // =====================================================================================================
    // Failures: one alert, retried next run
    // =====================================================================================================

    [Fact]
    public async Task AFailedMove_RaisesOneAlert_AndTheNextRunPlacesTheUser()
    {
        var user = _directory.User(CustomerBu, flag: true, roles: [BasicUserCustomerCopy]);
        _directory.FailNextUpdate = true;

        var first = await RunAsync(writes: true);

        first.Success.Should().BeFalse();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain(user.ToString()).And.Contain("failedStage=move");
        _directory.BusinessUnitOf(user).Should().Be(CustomerBu);
        _directory.Writes.Should().NotContain(w => w.StartsWith("associate", StringComparison.Ordinal),
            "the role is never assigned to a user that did not move");

        _log.Clear();
        var second = await RunAsync(writes: true);

        second.Success.Should().BeTrue(second.ErrorMessage);
        _directory.BusinessUnitOf(user).Should().Be(ExternalBu);
        _directory.DirectRolesOf(user).Should().Equal(ExternalRole);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task AFailedRoleAssignment_RaisesOneAlert_AndTheNextRunAssignsTheRole()
    {
        var user = _directory.User(CustomerBu, flag: true, roles: [BasicUserCustomerCopy]);
        _directory.FailNextAssociate = true;

        var first = await RunAsync(writes: true);

        first.Success.Should().BeFalse();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain("failedStage=assign-role").And.Contain("holdsExternalRole=False");
        _directory.DirectRolesOf(user).Should().BeEmpty("the move stripped the old role (spike S2); the user holds none");

        _log.Clear();
        var second = await RunAsync(writes: true);

        second.Success.Should().BeTrue(second.ErrorMessage);
        _directory.DirectRolesOf(user).Should().Equal(ExternalRole);
        Json(second).GetProperty("roleAssigned").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AFailedReadBack_RaisesOneAlert()
    {
        var user = _directory.User(CustomerBu, flag: true);
        _directory.FailRetrieveAfterWrites = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain(user.ToString()).And.Contain("could not be read back");
    }

    [Fact]
    public async Task AMoveThatKeepsTheOldRoles_ContraryToSpikeS2_RaisesAnAlert()
    {
        var user = _directory.User(CustomerBu, flag: true, roles: [BasicUserCustomerCopy]);
        _directory.KeepRolesOnMove = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain(user.ToString()).And.Contain("otherDirectRoles=1");

        // Review finding 2: the next run must NOT count the user as placed (in the unit, holding the role, AND another
        // role): it alerts again, fails again, and writes nothing (the job never removes roles).
        _log.Clear();
        var writesBefore = _directory.Writes.Count;
        var second = await RunAsync(writes: true);

        second.Success.Should().BeFalse();
        _directory.Writes.Should().HaveCount(writesBefore, "nothing is written for a user holding another role");
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain(user.ToString()).And.Contain("hold another direct role");
        Json(second).GetProperty("alreadyPlaced").GetInt32().Should().Be(0);
        Json(second).GetProperty("otherRoles").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(user);
        _log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat status=failed") && e.Message.Contains("otherRoles=1"));
    }

    [Fact]
    public async Task WritesOff_AUserInTheUnitHoldingAnotherRoleBesideTheExternalOne_IsAlerted_NotCountedPlaced()
    {
        var user = _directory.User(ExternalBu, flag: true, roles: [ExternalRole, BasicUserCustomerCopy]);

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        _directory.Writes.Should().BeEmpty();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain(user.ToString());
        Json(result).GetProperty("alreadyPlaced").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AMoveTheServerAcceptedButDidNotApply_RaisesAnAlert()
    {
        var user = _directory.User(CustomerBu, flag: true);
        _directory.IgnoreMoves = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain("inUnit=False");
        _ = user;
    }

    [Theory]
    [InlineData("systemuser")]
    [InlineData("systemuserroles")]
    [InlineData("teammembership")]
    [InlineData("teamroles")]
    [InlineData("role")]
    [InlineData("businessunit")]
    public async Task AFailedRead_MovesNobody_AndThrowsAfterItsHeartbeat_SoTheSchedulerRetries(string entity)
    {
        _directory.User(CustomerBu, flag: true);
        _directory.FailReadsOf(entity);

        var act = () => RunAsync(writes: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nobody was moved*");
        _directory.Writes.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Message.Contains("heartbeat status=error"));
    }

    [Fact]
    public async Task AUserThatCannotBeReReadBeforeTheWrite_IsNotMoved()
    {
        var user = _directory.User(CustomerBu, flag: true);
        _directory.BeforeRetrieve = _ => throw new InvalidOperationException("Test: re-read refused.");

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _directory.Writes.Should().BeEmpty();
        Json(result).GetProperty("unverified").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(user);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical, "nothing changed, so nothing is alerted");
    }

    // =====================================================================================================
    // The unit and the role must each resolve to exactly one
    // =====================================================================================================

    [Fact]
    public async Task WritesOn_WithNoUnit_MovesNobody_AndAlertsOnce()
    {
        _directory.RemoveBusinessUnit(ExternalBu);
        var user = _directory.User(CustomerBu, flag: true);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _directory.Writes.Should().BeEmpty();
        _log.Entries.Where(e => e.Level == LogLevel.Critical).Should().ContainSingle()
            .Which.Message.Should().Contain("cannot be placed").And.Contain(user.ToString());
    }

    [Fact]
    public async Task WritesOff_WithNoUnit_ReportsTheUnplaceableUsers_WithoutAlerting()
    {
        _directory.RemoveBusinessUnit(ExternalBu);
        var user = _directory.User(CustomerBu, flag: true);

        var result = await RunAsync();

        result.Success.Should().BeTrue();
        Json(result).GetProperty("unplaceable").GetProperty("ids").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(user);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task WritesOn_WithASecondUnitOfTheSameName_MovesNobody()
    {
        _directory.BusinessUnit(Guid.NewGuid(), ExternalCollaboratorPlacement.BusinessUnitName, CustomerBu);
        _directory.User(CustomerBu, flag: true);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _directory.Writes.Should().BeEmpty();
        Json(result).GetProperty("unit").GetString().Should().Be("ambiguous");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("root-copy-only")]
    public async Task WritesOn_WithoutExactlyOneRoleInTheUnit_MovesNobody(string shape)
    {
        _directory.RemoveRole(ExternalRole);
        if (shape == "ambiguous")
        {
            _directory.Role(Guid.NewGuid(), ExternalCollaboratorPlacement.RoleName, ExternalBu);
            _directory.Role(Guid.NewGuid(), ExternalCollaboratorPlacement.RoleName, ExternalBu);
        }

        if (shape == "root-copy-only")
            _directory.Role(Guid.NewGuid(), ExternalCollaboratorPlacement.RoleName, RootBu);

        _directory.User(CustomerBu, flag: true);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _directory.Writes.Should().BeEmpty("a move without the role would leave the user with no role at all");
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task NoSystemAdministratorRoleReturned_MovesNobody()
    {
        _directory.RemoveRole(SysAdminRootCopy);
        _directory.RemoveRole(SysAdminCustomerCopy);
        _directory.User(CustomerBu, flag: true);

        var act = () => RunAsync(writes: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _directory.Writes.Should().BeEmpty();
    }

    // =====================================================================================================
    // Bounds and privacy
    // =====================================================================================================

    [Fact]
    public async Task MoreUsersThanTheRunBound_AreDeferredToTheNextTick()
    {
        for (var i = 0; i < FlaggedUserPlacementJob.MaxPlacementsPerRun + 3; i++)
            _directory.User(CustomerBu, flag: true);

        var result = await RunAsync(writes: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var json = Json(result);
        json.GetProperty("moved").GetProperty("count").GetInt32().Should().Be(FlaggedUserPlacementJob.MaxPlacementsPerRun);
        json.GetProperty("deferred").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task LogsAndResult_CarryIdsOnly_NeverANameEmailOrUpn()
    {
        _directory.User(CustomerBu, flag: true, domainName: "outside.counsel@lawfirm.example", fullName: "Outside Counsel");
        _directory.FailNextUpdate = true;

        var result = await RunAsync(writes: true);

        var everything = string.Join("\n", _log.Entries.Select(e => e.Message)) + result.ResultJson + result.ErrorMessage;
        everything.Should().NotContain("lawfirm.example").And.NotContain("Outside Counsel");
        _directory.ColumnsRequested.Should().NotContain(["fullname", "domainname", "internalemailaddress"]);
    }

    [Fact]
    public void Classify_IsTheRuleTable()
    {
        var admins = new HashSet<Guid> { SysAdminCustomerCopy };
        FlaggedUser U(bool? flag = true, Guid? bu = null, bool? disabled = false, bool app = false, int? mode = 0) =>
            new(Guid.NewGuid(), bu ?? CustomerBu, disabled, app, mode, flag, null);
        PlacementDecision C(FlaggedUser u, Guid[]? direct = null, Guid[]? team = null, Guid? unit = null, bool noUnit = false) =>
            FlaggedUserPlacementJob.Classify(u, noUnit ? null : unit ?? ExternalBu, ExternalRole, direct ?? [], team ?? [], admins);

        C(U()).Should().Be(PlacementDecision.Move);
        C(U(mode: 2)).Should().Be(PlacementDecision.Move, "Read access mode is an interactive person");
        C(U(bu: ExternalBu)).Should().Be(PlacementDecision.AssignRole);
        C(U(bu: ExternalBu), direct: [ExternalRole]).Should().Be(PlacementDecision.Placed);
        C(U(bu: ExternalBu), direct: [ExternalRole, BasicUserCustomerCopy]).Should().Be(PlacementDecision.PlacedWithOtherRoles);
        C(U(bu: ExternalBu), direct: [BasicUserCustomerCopy]).Should().Be(PlacementDecision.AssignRole);
        C(U(), noUnit: true).Should().Be(PlacementDecision.Unplaceable);
        C(U(flag: null)).Should().Be(PlacementDecision.SkipNotFlagged);
        C(U(flag: false)).Should().Be(PlacementDecision.SkipNotFlagged);
        C(U(app: true)).Should().Be(PlacementDecision.SkipApplicationUser);
        C(U(disabled: true)).Should().Be(PlacementDecision.SkipDisabled);
        C(U(disabled: null)).Should().Be(PlacementDecision.SkipUnreadable);
        C(U(mode: null)).Should().Be(PlacementDecision.SkipUnreadable);
        C(U(mode: 1)).Should().Be(PlacementDecision.SkipAdministrator);
        C(U(), direct: [SysAdminCustomerCopy]).Should().Be(PlacementDecision.SkipAdministrator);
        C(U(), team: [SysAdminCustomerCopy]).Should().Be(PlacementDecision.SkipAdministrator);
        C(U(mode: 3)).Should().Be(PlacementDecision.SkipAccessMode);
        C(U(mode: 4)).Should().Be(PlacementDecision.SkipAccessMode);
        C(U(mode: 5)).Should().Be(PlacementDecision.SkipAccessMode);
    }

    // =====================================================================================================
    // Harness
    // =====================================================================================================

    private async Task<JobRunResult> RunAsync(bool writes = false, string? setting = null)
    {
        var values = new Dictionary<string, string?>();
        if (writes)
            values[FlaggedUserPlacementJob.WritesEnabledConfigKey] = "true";
        else if (setting is not null)
            values[FlaggedUserPlacementJob.WritesEnabledConfigKey] = setting;

        var services = new ServiceCollection();
        services.AddSingleton(_directory.Service());

        var job = new FlaggedUserPlacementJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new FakeTimeProvider(),
            _log);

        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-191e", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private static JsonElement Json(JobRunResult result) => JsonDocument.Parse(result.ResultJson!).RootElement;

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(parts)} above {AppContext.BaseDirectory}.");
    }
}

/// <summary>
/// The condition evaluator the in-memory Dataverse doubles share (task 191e). It evaluates the operators the BFF's directory
/// reads use (<c>Equal</c>, <c>In</c>, <c>Null</c>, <c>NotNull</c>) the way Dataverse does: lookups by id, choices by value,
/// strings case-insensitively. Any other operator throws, so a query the double cannot answer faithfully fails the test.
/// </summary>
internal static class FakeConditions
{
    public static bool Matches(Entity row, ConditionExpression condition)
    {
        row.Attributes.TryGetValue(condition.AttributeName, out var raw);
        var value = Normalize(raw);
        return condition.Operator switch
        {
            ConditionOperator.Equal => value is not null && Equals(value, Normalize(condition.Values[0])),
            ConditionOperator.In => value is not null && condition.Values.Select(Normalize).Contains(value),
            ConditionOperator.Null => value is null,
            ConditionOperator.NotNull => value is not null,
            _ => throw new NotSupportedException($"Test double: operator {condition.Operator} is not modelled."),
        };
    }

    private static object? Normalize(object? value) => value switch
    {
        EntityReference reference => reference.Id,
        OptionSetValue option => option.Value,
        string text => text.ToLowerInvariant(),
        _ => value,
    };
}

/// <summary>
/// An in-memory directory: business units, roles, users, teams and their intersect rows, answering the SDK calls the
/// placement job makes. A business-unit change behaves as spike S2 found (design A.4) unless a knob says otherwise.
/// </summary>
internal sealed class PlacementDirectory
{
    private readonly Dictionary<string, List<Entity>> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredConditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> _retrieves = new();
    private bool _wrote;

    public List<string> Writes { get; } = [];
    public HashSet<string> ColumnsRequested { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FailNextUpdate { get; set; }
    public bool FailNextAssociate { get; set; }
    public bool FailRetrieveAfterWrites { get; set; }
    public bool KeepRolesOnMove { get; set; }
    public bool IgnoreMoves { get; set; }
    public Action<Guid>? BeforeRetrieve { get; set; }

    private List<Entity> Rows(string entity) =>
        _rows.TryGetValue(entity, out var rows) ? rows : _rows[entity] = [];

    public void BusinessUnit(Guid id, string name, Guid? parent)
    {
        var row = new Entity("businessunit", id) { ["businessunitid"] = id, ["name"] = name };
        if (parent is { } p) row["parentbusinessunitid"] = new EntityReference("businessunit", p);
        Rows("businessunit").Add(row);
        Team(Guid.NewGuid(), id, isDefault: true, teamType: 0);
    }

    public void RemoveBusinessUnit(Guid id) => Rows("businessunit").RemoveAll(r => r.Id == id);

    public void Role(Guid id, string name, Guid businessUnit, Guid? templateId = null)
    {
        var row = new Entity("role", id)
        {
            ["roleid"] = id,
            ["name"] = name,
            ["businessunitid"] = new EntityReference("businessunit", businessUnit),
            ["parentrootroleid"] = new EntityReference("role", id),
        };
        if (templateId is { } t) row["roletemplateid"] = new EntityReference("roletemplate", t);
        Rows("role").Add(row);
    }

    public void RemoveRole(Guid id) => Rows("role").RemoveAll(r => r.Id == id);

    public Guid User(Guid businessUnit, bool? flag, bool disabled = false, bool application = false, int accessMode = 0,
        Guid[]? roles = null, string? domainName = null, string? fullName = null)
    {
        var id = Guid.NewGuid();
        var row = new Entity("systemuser", id)
        {
            ["systemuserid"] = id,
            ["fullname"] = fullName ?? "Person " + id.ToString()[..8],
            ["domainname"] = domainName ?? $"person.{id.ToString()[..8]}@customer.example",
            ["accessmode"] = new OptionSetValue(accessMode),
            ["isdisabled"] = disabled,
            ["businessunitid"] = new EntityReference("businessunit", businessUnit),
        };
        if (flag is { } f) row[ExternalCollaboratorPlacement.IsExternalColumn] = f;
        if (application) row["applicationid"] = Guid.NewGuid();
        Rows("systemuser").Add(row);

        foreach (var role in roles ?? [])
            AddRole(id, role);
        AddMember(DefaultTeamOf(businessUnit), id);
        return id;
    }

    public void SetFlag(Guid user, bool? flag)
    {
        var row = Rows("systemuser").Single(r => r.Id == user);
        if (flag is null) row.Attributes.Remove(ExternalCollaboratorPlacement.IsExternalColumn);
        else row[ExternalCollaboratorPlacement.IsExternalColumn] = flag.Value;
    }

    public void HideColumn(Guid user, string column) => Rows("systemuser").Single(r => r.Id == user).Attributes.Remove(column);

    public void Team(Guid id, Guid businessUnit, bool isDefault, int teamType, Guid[]? roles = null)
    {
        Rows("team").Add(new Entity("team", id)
        {
            ["teamid"] = id,
            ["name"] = isDefault ? "Default team" : "Team " + id.ToString()[..8],
            ["businessunitid"] = new EntityReference("businessunit", businessUnit),
            ["isdefault"] = isDefault,
            ["teamtype"] = new OptionSetValue(teamType),
        });
        foreach (var role in roles ?? [])
            Rows("teamroles").Add(new Entity("teamroles", Guid.NewGuid()) { ["teamid"] = id, ["roleid"] = role });
    }

    public void AddMember(Guid team, Guid user) =>
        Rows("teammembership").Add(new Entity("teammembership", Guid.NewGuid()) { ["teamid"] = team, ["systemuserid"] = user });

    public void FailReadsOf(string entity) => _failing.Add(entity);

    public void IgnoreConditionOn(string attribute) => _ignoredConditions.Add(attribute);

    public Guid BusinessUnitOf(Guid user) =>
        ((EntityReference)Rows("systemuser").Single(r => r.Id == user)["businessunitid"]).Id;

    public IReadOnlyList<Guid> DirectRolesOf(Guid user) =>
        Rows("systemuserroles").Where(r => (Guid)r["systemuserid"] == user).Select(r => (Guid)r["roleid"]).ToList();

    public int RetrievesOf(Guid id) => _retrieves.GetValueOrDefault(id);

    private Guid DefaultTeamOf(Guid businessUnit) =>
        Rows("team").Single(r => ((EntityReference)r["businessunitid"]).Id == businessUnit && (bool)r["isdefault"]).Id;

    private void AddRole(Guid user, Guid role) =>
        Rows("systemuserroles").Add(new Entity("systemuserroles", Guid.NewGuid()) { ["systemuserid"] = user, ["roleid"] = role });

    public IGenericEntityService Service()
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => Answer(q));
        mock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string[] columns, CancellationToken _) => Retrieve(entity, id, columns));
        mock.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) => Update(entity, id, fields));
        mock.Setup(s => s.AssociateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<EntityReference>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string relationship, IEnumerable<EntityReference> related, CancellationToken _) =>
                Associate(entity, id, relationship, related));
        return mock.Object;
    }

    private EntityCollection Answer(QueryExpression query)
    {
        if (_failing.Contains(query.EntityName))
            throw new InvalidOperationException($"Test: {query.EntityName} is not readable.");
        ColumnsRequested.UnionWith(query.ColumnSet.Columns);

        var rows = Rows(query.EntityName).Where(row => query.Criteria.Conditions
            .Where(c => !_ignoredConditions.Contains(c.AttributeName))
            .All(c => FakeConditions.Matches(row, c)));
        return new EntityCollection(rows.Select(r => Project(r, query.ColumnSet.Columns)).ToList()) { MoreRecords = false };
    }

    private Task<Entity> Retrieve(string entity, Guid id, string[] columns)
    {
        _retrieves[id] = _retrieves.GetValueOrDefault(id) + 1;
        if (_failing.Contains(entity))
            throw new InvalidOperationException($"Test: {entity} is not readable.");
        if (FailRetrieveAfterWrites && _wrote)
            throw new InvalidOperationException("Test: read-back refused.");
        BeforeRetrieve?.Invoke(id);
        ColumnsRequested.UnionWith(columns);
        return Task.FromResult(Project(Rows(entity).Single(r => r.Id == id), columns));
    }

    private Task Update(string entity, Guid id, Dictionary<string, object> fields)
    {
        if (FailNextUpdate)
        {
            FailNextUpdate = false;
            throw new InvalidOperationException("Test: update refused.");
        }

        _wrote = true;
        var target = (EntityReference)fields["businessunitid"];
        fields.Keys.Should().Equal("businessunitid");
        Writes.Add($"update {entity} {id} businessunitid={target.Id}");
        if (IgnoreMoves)
            return Task.CompletedTask;

        var row = Rows("systemuser").Single(r => r.Id == id);
        var from = ((EntityReference)row["businessunitid"]).Id;
        row["businessunitid"] = new EntityReference("businessunit", target.Id);

        // Spike S2: the move strips direct roles and swaps the default-team membership.
        if (!KeepRolesOnMove)
            Rows("systemuserroles").RemoveAll(r => (Guid)r["systemuserid"] == id);
        Rows("teammembership").RemoveAll(r => (Guid)r["systemuserid"] == id && (Guid)r["teamid"] == DefaultTeamOf(from));
        AddMember(DefaultTeamOf(target.Id), id);
        return Task.CompletedTask;
    }

    private Task Associate(string entity, Guid id, string relationship, IEnumerable<EntityReference> related)
    {
        if (FailNextAssociate)
        {
            FailNextAssociate = false;
            throw new InvalidOperationException("Test: associate refused.");
        }

        _wrote = true;
        entity.Should().Be("systemuser");
        relationship.Should().Be(ExternalCollaboratorPlacement.UserRoleRelationship);
        foreach (var reference in related)
        {
            reference.LogicalName.Should().Be("role");
            Writes.Add($"associate {id} role {reference.Id}");
            if (!DirectRolesOf(id).Contains(reference.Id))
                AddRole(id, reference.Id);
        }

        return Task.CompletedTask;
    }

    private static Entity Project(Entity row, IEnumerable<string> columns)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns)
        {
            if (row.Attributes.TryGetValue(column, out var value))
                copy[column] = value;
        }

        return copy;
    }
}

/// <summary>Captures every log line.</summary>
internal sealed class PlacementLog<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public void Clear() => _entries.Clear();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => _entries.Add((logLevel, formatter(state, exception)));
}
