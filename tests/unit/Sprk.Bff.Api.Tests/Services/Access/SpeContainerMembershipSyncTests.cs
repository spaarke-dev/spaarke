using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;
using Xunit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Services.Access;

/// <summary>
/// unified-access-control-r2 task 171 (owner rounds 69 + 70) — the SPE container-role sync. Pinned, both passes:
/// <list type="bullet">
/// <item>Standing writers: an enabled internal person in a unit whose container is stamped is GRANTED; a user who already
/// holds any role (owner, hand-granted) is never granted and so never recorded; a user flagged external, disabled, or
/// moved out of the unit loses a role ONLY when this code recorded it; nothing is concluded from a partial role list.</item>
/// <item>JIT grants on secure containers: removed when Dataverse POSITIVELY answers "no Write", the holder is disabled, or
/// the record is Restricted and the holder is flagged external; kept when Write stands; kept (and counted unknown) when the
/// answer could not be obtained — a Dataverse fault never revokes.</item>
/// </list>
/// </summary>
/// <remarks>Mocking boundary (ADR-038 §4): <see cref="IGenericEntityService"/>, the grant primitives on
/// <see cref="Membership"/> (virtual — the Graph calls themselves are not mocked), <see cref="ISecurableEntityRegistry"/>
/// and <see cref="IDataverseRecordShareService"/>.</remarks>
public class SpeContainerMembershipSyncTests
{
    private const string BuContainer = "b!bu-container";
    private const string SecureContainer = "b!secure-container";
    private static readonly Guid Unit = Guid.Parse("17110000-0000-4000-8000-000000000001");
    private static readonly Guid OtherUnit = Guid.Parse("17110000-0000-4000-8000-000000000002");
    private static readonly Guid Alice = Guid.Parse("17110000-0000-4000-8000-0000000000a1");
    private static readonly Guid Bob = Guid.Parse("17110000-0000-4000-8000-0000000000b0");
    private static readonly Guid Owner = Guid.Parse("17110000-0000-4000-8000-0000000000c0");
    private static readonly Guid SecureProject = Guid.Parse("17110000-0000-4000-8000-0000000000d0");

    private readonly Mock<IGenericEntityService> _dataverse = new();
    private readonly Mock<Membership> _membership = new(Mock.Of<IGraphClientFactory>(), NullLogger<Membership>.Instance);
    private readonly Mock<ISecurableEntityRegistry> _registry = new();
    private readonly Mock<IDataverseRecordShareService> _rights = new();
    private readonly List<Entity> _units = [];
    private readonly List<Entity> _users = [];
    private readonly List<Entity> _secureProjects = [];

    public SpeContainerMembershipSyncTests()
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(q.EntityName switch
            {
                "businessunit" => _units,
                "systemuser" => _users,
                "sprk_project" => _secureProjects,
                _ => [],
            }) { MoreRecords = false });
        _dataverse.Setup(d => d.GetEntitySetNameAsync("sprk_project", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_projects");
        _registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "sprk_project" });
        _membership.Setup(m => m.GrantMarkedWriterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedGrantOutcome.Granted);
        _membership.Setup(m => m.RemoveMarkedGrantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedRemovalOutcome.Removed);
    }

    private SpeContainerMembershipSync Sut() => new(
        _dataverse.Object, _membership.Object, _registry.Object, _rights.Object, NullLogger<SpeContainerMembershipSync>.Instance);

    private static Entity User(Guid id, Guid unit, bool disabled = false, bool? external = null, Guid? applicationId = null)
    {
        var user = new Entity("systemuser", id)
        {
            ["domainname"] = $"{id:N}@contoso.example",
            ["azureactivedirectoryobjectid"] = id,
            ["isdisabled"] = disabled,
            ["accessmode"] = new OptionSetValue(0),
            ["businessunitid"] = new EntityReference("businessunit", unit),
        };
        if (external is not null) user["sprk_isexternal"] = external;
        if (applicationId is not null) user["applicationid"] = applicationId;
        return user;
    }

    private void BusinessUnitContainer(params (Guid User, string PermissionId, string[] Roles, bool Marked)[] roles)
    {
        _units.Add(new Entity("businessunit", Unit) { ["sprk_containerid"] = BuContainer });
        _membership.Setup(m => m.ReadAccessAsync(BuContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                roles.Select(r => new Membership.ContainerUserRole(r.PermissionId, r.Roles, $"{r.User:N}@contoso.example", r.User.ToString())).ToList(),
                RolesComplete: true,
                roles.Where(r => r.Marked).ToDictionary(
                    r => Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, r.User), r => r.PermissionId)));
    }

    private void GrantVerified(Guid user, Times times)
        => _membership.Verify(m => m.GrantMarkedWriterAsync(
            BuContainer, Membership.StandingWriterMarkerPrefix, user, $"{user:N}@contoso.example", It.IsAny<CancellationToken>()), times);

    private void RemovalVerified(string container, Guid user, string prefix, Times times)
        => _membership.Verify(m => m.RemoveMarkedGrantAsync(
            container, Membership.MarkerKey(prefix, user), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()), times);

    // ── Standing writers ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 171: an enabled internal person in the unit is granted writer (blank sprk_isexternal is internal)")]
    public async Task Standing_EnabledInternalUser_IsGranted()
    {
        BusinessUnitContainer();
        _users.Add(User(Alice, Unit, external: null));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        GrantVerified(Alice, Times.Once());
        result.Granted.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Fact(DisplayName = "Task 171: a STALE standing marker (its grant was removed outside this code) is cleared and the eligible user is granted again")]
    public async Task Standing_StaleMarker_IsClearedAndTheUserRegranted()
    {
        _units.Add(new Entity("businessunit", Unit) { ["sprk_containerid"] = BuContainer });
        _membership.Setup(m => m.ReadAccessAsync(BuContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [],
                RolesComplete: true,
                new Dictionary<string, string> { [Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, Alice)] = "perm-gone" }));
        _membership.Setup(m => m.RemoveMarkedGrantAsync(
                BuContainer, Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, Alice), "perm-gone",
                It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedRemovalOutcome.MarkerCleared);
        _users.Add(User(Alice, Unit));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        RemovalVerified(BuContainer, Alice, Membership.StandingWriterMarkerPrefix, Times.Once());
        GrantVerified(Alice, Times.Once());
        result.MarkersCleared.Should().Be(1);
        result.Granted.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Fact(DisplayName = "Task 171: a standing marker whose grant still stands is left alone — no re-grant, no removal")]
    public async Task Standing_LiveMarker_IsLeftAlone()
    {
        BusinessUnitContainer((Alice, "perm-a", ["writer"], Marked: true));
        _users.Add(User(Alice, Unit));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        GrantVerified(Alice, Times.Never());
        RemovalVerified(BuContainer, Alice, Membership.StandingWriterMarkerPrefix, Times.Never());
        result.Failed.Should().Be(0);
    }

    [Fact(DisplayName = "Task 171: a user flagged external, a disabled user and an application user are never granted")]
    public async Task Standing_IneligibleUsers_AreNeverGranted()
    {
        BusinessUnitContainer();
        _users.Add(User(Alice, Unit, external: true));
        _users.Add(User(Bob, Unit, disabled: true));
        _users.Add(User(Owner, Unit, applicationId: Guid.NewGuid()));

        await Sut().SyncStandingWritersAsync(CancellationToken.None);

        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: an owner or hand-granted member is neither granted nor removed — even when disabled")]
    public async Task Standing_HandGrantedAndOwnerRoles_AreUntouched()
    {
        BusinessUnitContainer(
            (Owner, "perm-owner", ["owner"], Marked: false),
            (Bob, "perm-hand", ["writer"], Marked: false));
        _users.Add(User(Owner, Unit));
        _users.Add(User(Bob, Unit, disabled: true));

        await Sut().SyncStandingWritersAsync(CancellationToken.None);

        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _membership.Verify(m => m.RemoveMarkedGrantAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: a recorded standing grant is removed when its holder is disabled or flagged external")]
    public async Task Standing_RecordedGrant_OfDisabledOrExternalUser_IsRemoved()
    {
        BusinessUnitContainer(
            (Alice, "perm-a", ["writer"], Marked: true),
            (Bob, "perm-b", ["writer"], Marked: true));
        _users.Add(User(Alice, Unit, disabled: true));
        _users.Add(User(Bob, Unit, external: true));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        RemovalVerified(BuContainer, Alice, Membership.StandingWriterMarkerPrefix, Times.Once());
        RemovalVerified(BuContainer, Bob, Membership.StandingWriterMarkerPrefix, Times.Once());
        result.Removed.Should().Be(2);
    }

    [Fact(DisplayName = "Task 171: a recorded standing grant is removed when its holder MOVED to another business unit — confirmed by reading the user")]
    public async Task Standing_RecordedGrant_OfUserWhoMoved_IsRemoved()
    {
        BusinessUnitContainer((Alice, "perm-a", ["writer"], Marked: true));
        _dataverse.Setup(d => d.RetrieveAsync("systemuser", Alice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(Alice, OtherUnit));

        await Sut().SyncStandingWritersAsync(CancellationToken.None);

        RemovalVerified(BuContainer, Alice, Membership.StandingWriterMarkerPrefix, Times.Once());
    }

    [Fact(DisplayName = "Task 171: a recorded grant whose holder cannot be read is KEPT and counted unknown")]
    public async Task Standing_UnreadableHolder_IsKept()
    {
        BusinessUnitContainer((Alice, "perm-a", ["writer"], Marked: true));
        _dataverse.Setup(d => d.RetrieveAsync("systemuser", Alice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        RemovalVerified(BuContainer, Alice, Membership.StandingWriterMarkerPrefix, Times.Never());
        result.Unknown.Should().Be(1);
    }

    [Fact(DisplayName = "Task 171: a PARTIAL role list changes nothing on that container")]
    public async Task Standing_IncompleteRoleList_ChangesNothing()
    {
        _units.Add(new Entity("businessunit", Unit) { ["sprk_containerid"] = BuContainer });
        _users.Add(User(Alice, Unit));
        _membership.Setup(m => m.ReadAccessAsync(BuContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess([], RolesComplete: false, new Dictionary<string, string>()));

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        GrantVerified(Alice, Times.Never());
        result.Failed.Should().Be(1);
    }

    // ── JIT grants on secure containers ────────────────────────────────────────────────────────────────────────────

    private void SecureContainerWithJitGrant(Guid holder, bool restricted = false)
    {
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        var markers = new Dictionary<string, string> { [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, holder)] = "perm-jit" };
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>())).ReturnsAsync(markers);
        _membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-jit", ["writer"], "h@contoso.example", holder.ToString())], true, markers));
        _dataverse.Setup(d => d.RetrieveAsync("sprk_project", SecureProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", SecureProject)
            {
                ["sprk_accesspermission"] = new OptionSetValue(restricted ? 100000002 : 100000000),
            });
    }

    private void Holder(Guid id, bool disabled = false, bool? external = null)
        => _dataverse.Setup(d => d.RetrieveAsync("systemuser", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", id) { ["isdisabled"] = disabled, ["sprk_isexternal"] = external });

    [Fact(DisplayName = "Task 171: a JIT grant is REMOVED once Dataverse answers that the holder has no Write (unshare / No Access)")]
    public async Task Jit_HolderWithoutWrite_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        result.Removed.Should().Be(1);
    }

    [Fact(DisplayName = "Task 171: a JIT grant is KEPT while the holder still has Write")]
    public async Task Jit_HolderWithWrite_IsKept()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Never());
        result.Kept.Should().Be(1);
    }

    [Fact(DisplayName = "Task 171: a JIT grant of a DISABLED holder is removed without asking for rights")]
    public async Task Jit_DisabledHolder_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice, disabled: true);

        await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        _rights.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: a JIT grant on a RESTRICTED record held by a user flagged external is removed (round 67)")]
    public async Task Jit_RestrictedRecord_ExternalHolder_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice, restricted: true);
        Holder(Alice, external: true);
        _rights.Setup(r => r.GetPrincipalRightsAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Write);

        await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
    }

    [Fact(DisplayName = "Task 171: a Dataverse fault removes NOTHING and is counted unknown — never a revocation loop")]
    public async Task Jit_RightsLookupFault_RemovesNothing()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("throttled"));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Never());
        result.Unknown.Should().Be(1);
    }

    [Fact(DisplayName = "Task 171: a secure container with no JIT markers is only looked at, never read for roles")]
    public async Task Jit_ContainerWithoutMarkers_IsOnlyLookedAt()
    {
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.SecureContainers.Should().Be(1);
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The marker key (pure) ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 171: a marker key round-trips its system user, and only under its own prefix")]
    public void MarkerKey_RoundTrips_UnderItsOwnPrefixOnly()
    {
        var key = Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Alice);

        Membership.TryParseMarkerKey(key, Membership.JitWriterMarkerPrefix, out var parsed).Should().BeTrue();
        parsed.Should().Be(Alice);
        Membership.TryParseMarkerKey(key, Membership.StandingWriterMarkerPrefix, out _).Should().BeFalse();
        Membership.TryParseMarkerKey("SprkJitnot-a-guid", Membership.JitWriterMarkerPrefix, out _).Should().BeFalse();
        Membership.TryParseMarkerKey("sprk-business-unit", Membership.JitWriterMarkerPrefix, out _).Should().BeFalse();
    }
}
