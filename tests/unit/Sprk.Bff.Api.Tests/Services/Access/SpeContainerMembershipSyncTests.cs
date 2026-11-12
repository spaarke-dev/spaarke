using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.TestInfrastructure;
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
    private readonly Mock<Membership> _membership = new(TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<Membership>.Instance);
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
            })
            { MoreRecords = false });
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

    private readonly ExternalVisibilitySeamStub _visibility = new();
    private readonly Dictionary<string, string?> _settings = [];

    private SpeContainerMembershipSync Sut(Membership? membership = null, ILogger<SpeContainerMembershipSync>? logger = null) => new(
        _dataverse.Object, membership ?? _membership.Object, _registry.Object, _rights.Object, _visibility.Seam,
        new ConfigurationBuilder().AddInMemoryCollection(_settings).Build(),
        logger ?? NullLogger<SpeContainerMembershipSync>.Instance);

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
            .ReturnsAsync(new Entity("systemuser", id) { ["isdisabled"] = disabled, ["sprk_isexternal"] = external, ["azureactivedirectoryobjectid"] = OidOf(id) });

    [Fact(DisplayName = "Task 171: a JIT grant is REMOVED once Dataverse answers that the holder has no Write (unshare / No Access)")]
    public async Task Jit_HolderWithoutWrite_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
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
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
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
        _rights.Verify(r => r.GetPrincipalRightsOrUnknownAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Issue #1576: a JIT grant whose holder row carries NO isdisabled value is removed (fail closed), without asking for rights")]
    public async Task Jit_HolderWithoutIsDisabledValue_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice);
        _dataverse.Setup(d => d.RetrieveAsync("systemuser", Alice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", Alice) { ["sprk_isexternal"] = false });

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        result.Removed.Should().Be(1);
        _rights.Verify(r => r.GetPrincipalRightsOrUnknownAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: a JIT grant on a RESTRICTED record held by a user flagged external is removed (round 67)")]
    public async Task Jit_RestrictedRecord_ExternalHolder_IsRemoved()
    {
        SecureContainerWithJitGrant(Alice, restricted: true);
        Holder(Alice, external: true);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Write);

        await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
    }

    [Fact(DisplayName = "Task 171: a Dataverse fault removes NOTHING and is counted unknown — never a revocation loop")]
    public async Task Jit_RightsLookupFault_RemovesNothing()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("throttled"));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Never());
        result.Unknown.Should().Be(1);
    }

    [Fact(DisplayName = "Task 171 (finding 3): a rights read that is NOT an access answer (e.g. the app user lacks prvActOnBehalfOfAnotherUser) removes NOTHING")]
    public async Task Jit_NoAccessAnswer_RemovesNothing()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccessRights?)null);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Never());
        result.Unknown.Should().Be(1);
        _rights.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the lenient read maps EVERY 403 to 'no rights' — safe for a deny, destructive for a revoke");
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

    // ── Task 191g: item grants (flagged-external holders, one file) ───────────────────────────────────────────────

    private static readonly Guid Doc = Guid.Parse("191a0000-0000-4000-8000-0000000000e1");
    private const string ItemId = "016ITEM191G";

    private string ItemKey(Guid holder) => Membership.ItemMarkerKey(holder, Doc);

    /// <summary>A deterministic Entra object id per holder (the removal deletes only that identity's plain write).</summary>
    private static Guid OidOf(Guid systemUserId) => new(systemUserId.ToByteArray().Reverse().ToArray());

    /// <summary>A secure container carrying one item grant for <paramref name="holder"/> on <see cref="Doc"/>, and no JIT
    /// container grant.</summary>
    /// <summary>A secure container holding one marked item grant; the feature setting ON unless <paramref name="settingOn"/> says off.</summary>
    private void SecureContainerWithItemGrant(Guid holder, bool restricted = false, bool settingOn = true)
    {
        if (settingOn)
            _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        var markers = new Dictionary<string, string> { [ItemKey(holder)] = Membership.ItemMarkerValue(holder, Doc, ItemId)! };
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>())).ReturnsAsync(markers);
        _membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess([], true, markers));
        _membership.Setup(m => m.RemoveMarkedItemGrantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedRemovalOutcome.Removed);
        _dataverse.Setup(d => d.RetrieveAsync("sprk_project", SecureProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", SecureProject)
            {
                ["sprk_accesspermission"] = new OptionSetValue(restricted ? 100000002 : 100000000),
            });
    }

    private void HolderRights(Guid holder, AccessRights? rights)
        => _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(holder, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);

    private void ItemRemovalVerified(Guid holder, Times times)
        => _membership.Verify(m => m.RemoveMarkedItemGrantAsync(
            SecureContainer, ItemKey(holder), ItemId, OidOf(holder), It.IsAny<CancellationToken>()), times);

    [Fact(DisplayName = "Task 191g: an item grant is KEPT while the flagged holder has Write and the document is visible")]
    public async Task Item_WriteAndVisible_IsKept()
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read | AccessRights.Write);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Never());
        result.ItemGrantsSeen.Should().Be(1);
        result.Kept.Should().Be(1);
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "an item grant is decided without the container's roles (no flagged user to sweep)");
    }

    [Fact(DisplayName = "Task 191g: an item grant is REMOVED when Dataverse positively answers the holder has no Write")]
    public async Task Item_WriteGone_IsRemoved()
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Once());
        result.Removed.Should().Be(1);
    }

    [Fact(DisplayName = "Task 191g: an item grant of a DISABLED holder is removed")]
    public async Task Item_DisabledHolder_IsRemoved()
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, disabled: true, external: true);

        await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Once());
    }

    [Fact(DisplayName = "Task 191g: an item grant on a RESTRICTED record is removed")]
    public async Task Item_RestrictedRecord_IsRemoved()
    {
        SecureContainerWithItemGrant(Alice, restricted: true);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Write);

        await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Once());
    }

    [Theory(DisplayName = "Task 191g: an item grant is REMOVED when the document is no longer externally visible (hidden, or structurally undecidable), even with Write")]
    [InlineData("Hidden", "DocumentHidden")]
    [InlineData("Hidden", "RootRestricted")]
    [InlineData("Undecided", "NoRoot")]
    public async Task Item_DocumentNotVisible_IsRemoved(string verdict, string reason)
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Write);
        _visibility.Set("sprk_document", Doc, Enum.Parse<ExternalVisibilityVerdict>(verdict), Enum.Parse<ExternalVisibilityReason>(reason));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Once());
        result.Removed.Should().Be(1);
        _visibility.Calls.Should().ContainSingle().Which.Should().Equal(("sprk_document", Doc));
    }

    [Theory(DisplayName = "Task 191g: an item grant whose answer could not be obtained is KEPT, counted unknown and reported — never a revocation loop")]
    [InlineData("rights")]
    [InlineData("visibility-throws")]
    [InlineData("visibility-faulted")]
    [InlineData("visibility-read-failed")]
    [InlineData("visibility-row-not-found")]
    public async Task Item_UnknownAnswer_IsKeptAndReported(string unknownBecause)
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, unknownBecause == "rights" ? null : AccessRights.Write);
        switch (unknownBecause)
        {
            case "visibility-throws": _visibility.Throws = new HttpRequestException("throttled"); break;
            case "visibility-faulted": _visibility.Faulted = true; break;
            case "visibility-read-failed":
                _visibility.Set("sprk_document", Doc, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);
                break;
            case "visibility-row-not-found":   // 191c's transient reasons: a later read may answer
                _visibility.Set("sprk_document", Doc, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.RowNotFound);
                break;
        }

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Never());
        result.Unknown.Should().Be(1);
        result.Problems.Should().ContainSingle().Which.Should().Contain(Doc.ToString()).And.Contain("kept this run");
    }

    [Fact(DisplayName = "Task 191g: a malformed item marker, or one whose key is not the key its ids derive, is reported and left alone")]
    public async Task Item_MalformedMarkerValue_IsReportedAndLeftAlone()
    {
        SecureContainerWithItemGrant(Alice);
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { [ItemKey(Alice)] = "no-separator", [Membership.ItemMarkerKey(Bob, Doc)] = Membership.ItemMarkerValue(Alice, Doc, ItemId)! });

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.ItemGrantsSeen.Should().Be(0);
        result.Problems.Should().HaveCount(2).And.OnlyContain(p => p.Contains("malformed or does not match its key"));
        _membership.Verify(m => m.RemoveMarkedItemGrantAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>MUTATION: deciding item grants on Write + visibility whatever the setting says.</summary>
    [Theory(DisplayName = "Task 191g: with the setting OFF (absent, false, unparseable) every item grant is removed — the kill switch — even with Write and visible")]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("yes")]
    public async Task Item_SettingOff_IsRemoved(string? setting)
    {
        SecureContainerWithItemGrant(Alice, settingOn: false);
        if (setting is not null)
            _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = setting;
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read | AccessRights.Write);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        ItemRemovalVerified(Alice, Times.Once());
        result.Removed.Should().Be(1);
        _visibility.Calls.Should().BeEmpty("the feature is off: nothing to decide");
    }

    // ── Task 191g: flagged users' container roles on secure containers (setting-gated sweep) ─────────────────────

    private const string FlaggedRole = "perm-flagged";
    private const string InternalRole = "perm-internal";

    /// <summary>A secure container with no markers on which Alice (flagged) and Bob (internal) hold hand-granted roles.</summary>
    private void SecureContainerWithHandGrantedRoles()
    {
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [
                    new Membership.ContainerUserRole(FlaggedRole, ["writer"], $"{Alice:N}@contoso.example", Alice.ToString()),
                    new Membership.ContainerUserRole(InternalRole, ["writer"], $"{Bob:N}@contoso.example", Bob.ToString()),
                ],
                true, new Dictionary<string, string>()));
        _membership.Setup(m => m.RemoveFlaggedUserContainerRoleAsync(SecureContainer, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _users.Add(User(Alice, Unit, external: true)); // the flagged-user query answers from _users
    }

    [Fact(DisplayName = "Task 191g: with the setting ON, a flagged user's hand-granted container role on a secure container is removed; an internal user's is not")]
    public async Task Sweep_On_RemovesFlaggedRoleOnly()
    {
        _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        SecureContainerWithHandGrantedRoles();

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        _membership.Verify(m => m.RemoveFlaggedUserContainerRoleAsync(SecureContainer, FlaggedRole, It.IsAny<CancellationToken>()), Times.Once);
        _membership.Verify(m => m.RemoveFlaggedUserContainerRoleAsync(SecureContainer, InternalRole, It.IsAny<CancellationToken>()), Times.Never);
        result.FlaggedRoleSweep.Should().BeTrue();
        result.FlaggedRolesRemoved.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Theory(DisplayName = "Task 191g (must not fire): with the setting OFF (absent, false, unparseable), no container role is removed and no role list is read")]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("yes")]
    public async Task Sweep_Off_TouchesNothing(string? setting)
    {
        if (setting is not null)
            _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = setting;
        SecureContainerWithHandGrantedRoles();

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        _membership.Verify(m => m.RemoveFlaggedUserContainerRoleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.FlaggedRoleSweep.Should().BeFalse();
    }

    [Fact(DisplayName = "Task 191g: with the setting ON, a flagged holder's MARKED JIT container grant is removed even with Write — once, through the marked removal")]
    public async Task Sweep_On_FlaggedMarkedJitGrant_IsRemovedOnce()
    {
        _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        SecureContainerWithJitGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read | AccessRights.Write);
        _users.Add(User(Alice, Unit, external: true));
        _membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-jit", ["writer"], $"{Alice:N}@contoso.example", Alice.ToString())], true,
                new Dictionary<string, string> { [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Alice)] = "perm-jit" }));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        _membership.Verify(m => m.RemoveFlaggedUserContainerRoleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the marked removal already took it");
        result.Removed.Should().Be(1);
    }

    [Fact(DisplayName = "Task 191g (must not fire): with the setting OFF, a flagged holder's JIT container grant with Write is KEPT (today's behaviour until the go)")]
    public async Task Sweep_Off_FlaggedMarkedJitGrantWithWrite_IsKept()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read | AccessRights.Write);
        _users.Add(User(Alice, Unit, external: true));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Never());
        result.Kept.Should().Be(1);
    }

    [Fact(DisplayName = "Task 191g: with the setting ON, a flagged-user list that cannot be read sweeps nothing and is a named failure")]
    public async Task Sweep_On_FlaggedListUnreadable_SweepsNothing()
    {
        _settings[Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        SecureContainerWithHandGrantedRoles();
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("throttled"));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        _membership.Verify(m => m.RemoveFlaggedUserContainerRoleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Failed.Should().Be(1);
        result.Problems.Should().Contain(p => p.Contains("flagged-external users could not be listed"));
    }

    [Fact(DisplayName = "Task 191g (option B): the item marker has the live-proven profile — a 39-character key of letters and digits, a value of letters, digits and '.' — and round-trips only with its own key")]
    public void ItemMarker_HasTheProvenProfile_AndRoundTripsOnlyWithItsKey()
    {
        var key = Membership.ItemMarkerKey(Alice, Doc);
        var value = Membership.ItemMarkerValue(Alice, Doc, ItemId)!;

        key.Should().HaveLength(Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Alice).Length, "the same length as the live SprkJit keys")
            .And.StartWith(Membership.ItemWriterMarkerPrefix)
            .And.MatchRegex("^[A-Za-z0-9]+$");
        value.Should().MatchRegex(@"^[A-Za-z0-9.]+$").And.NotContain("|");
        Membership.ItemMarkerKey(Alice, Doc).Should().Be(key, "deterministic");
        Membership.ItemMarkerKey(Bob, Doc).Should().NotBe(key);

        Membership.TryParseItemMarker(key, value, out var holder, out var doc, out var item).Should().BeTrue();
        (holder, doc, item).Should().Be((Alice, Doc, ItemId));
        Membership.TryParseItemMarker(Membership.ItemMarkerKey(Bob, Doc), value, out _, out _, out _).Should().BeFalse("a value under another key is not acted on");
        Membership.TryParseItemMarker(key, value + ".x", out _, out _, out _).Should().BeFalse();
        Membership.ItemMarkerValue(Alice, Doc, "item|perm").Should().BeNull("an item id outside the profile makes no grant");
        Membership.IsGrantMarkerName(key).Should().BeTrue("the SPE admin property editor must not show or change it");
    }

    [Fact(DisplayName = "Task 191g: an item grant due for removal whose holder's Entra object id cannot be read is kept, failed and reported — never a removal by a guessed identity")]
    public async Task Item_HolderObjectIdUnreadable_IsKeptAndFailed()
    {
        SecureContainerWithItemGrant(Alice);
        Holder(Alice, external: true);
        HolderRights(Alice, AccessRights.Read);
        _dataverse.Setup(d => d.RetrieveAsync("systemuser", Alice, It.Is<string[]>(c => c.Length == 1 && c[0] == "azureactivedirectoryobjectid"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", Alice));

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        _membership.Verify(m => m.RemoveMarkedItemGrantAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Failed.Should().Be(1);
        result.Problems.Should().Contain(p => p.Contains("Entra object id could not be read"));
    }

    private static SdapProblemException NotOwned() => new(
        SpeContainerOwnershipGuard.NotOwnedErrorCode, "Not Found", "SharePoint Embedded container not found.", 404);

    [Fact(DisplayName = "Task 227d: a secure record pointing at a container this stamp does not own is skipped — not a failure, the pass goes on")]
    public async Task Jit_ContainerRefusedByOwnershipGuard_IsSkippedNotFailed()
    {
        var other = Guid.Parse("17110000-0000-4000-8000-0000000000d1");
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = "b!foreign-container" });
        _secureProjects.Add(new Entity("sprk_project", other) { ["sprk_containerid"] = SecureContainer });
        _membership.Setup(m => m.ReadMarkersAsync("b!foreign-container", It.IsAny<CancellationToken>())).ThrowsAsync(NotOwned());
        _membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.SecureContainers.Should().Be(2);
        result.Failed.Should().Be(0);
        result.Problems.Should().ContainSingle().Which.Should().Contain("not one of this stamp's containers");
        _membership.Verify(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── #1830 (task 192): a secure record whose container id does not resolve in this tenant ───────────────────────

    private const string DeadContainer = "b!dead-container";
    private static readonly Guid DeadProject = Guid.Parse("18300000-0000-4000-8000-0000000000d0");

    /// <summary>
    /// Record A points at <see cref="DeadContainer"/>, whose marker read throws <paramref name="fault"/>; record B is
    /// <see cref="SecureProject"/> with a JIT grant whose holder (Alice) lost Write. <paramref name="deadFirst"/> orders them.
    /// </summary>
    private void DeadContainerBesideARevokedGrant(Exception fault, bool deadFirst)
    {
        SecureContainerWithJitGrant(Alice);
        var dead = new Entity("sprk_project", DeadProject) { ["sprk_containerid"] = DeadContainer };
        if (deadFirst)
            _secureProjects.Insert(0, dead);
        else
            _secureProjects.Add(dead);
        _membership.Setup(m => m.ReadMarkersAsync(DeadContainer, It.IsAny<CancellationToken>())).ThrowsAsync(fault);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
    }

    private static ODataError GraphError(int status, string code, string message)
        => new() { ResponseStatusCode = status, Error = new MainError { Code = code, Message = message } };

    private static ODataError InvalidHostname() => GraphError(400, "invalidRequest", "Invalid hostname for this tenancy");

    /// <summary>MUTATION: removing the unresolvable-container catch (it fell to the generic catch: Error + failed++).</summary>
    [Theory(DisplayName = "#1830: a container that does not resolve in this tenant is REPORTED, not failed; the other record's revoked grant is still removed")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Jit_UnresolvableContainer_IsReportedNotFailed_AndTheOtherGrantIsRemoved(bool deadFirst)
    {
        DeadContainerBesideARevokedGrant(InvalidHostname(), deadFirst);
        var log = new RecordingLogger<SpeContainerMembershipSync>();

        var result = await Sut(logger: log).RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.SecureContainers.Should().Be(2);
        result.Removed.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Unresolvable.Should().Be(1);
        result.Problems.Should().ContainSingle().Which.Should()
            .Contain(DeadProject.ToString()).And.Contain(DeadContainer).And.Contain("does not resolve in this tenant");
        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        _membership.Verify(m => m.RemoveMarkedGrantAsync(
            DeadContainer, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()), Times.Never);
        _membership.Verify(m => m.ReadAccessAsync(DeadContainer, It.IsAny<CancellationToken>()), Times.Never);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(DeadContainer));
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    public static TheoryData<string, Exception, bool> GenuineFaults() => new()
    {
        { "Graph 503", GraphError(503, "serviceNotAvailable", "Service unavailable"), true },
        { "Graph 503", GraphError(503, "serviceNotAvailable", "Service unavailable"), false },
        { "Graph 429", GraphError(429, "TooManyRequests", "Throttled"), true },
        { "SpaarkeStorageException (the guard's translated no-answer)", new SpaarkeStorageException("ownership read failed", 503), true },
        { "SpaarkeStorageException (the guard's translated no-answer)", new SpaarkeStorageException("ownership read failed", 503), false },
        { "Graph 400 with another code (a malformed query)", GraphError(400, "BadRequest", "Invalid hostname for this tenancy"), true },
        { "Graph 400 with another code (a malformed query)", GraphError(400, "BadRequest", "Invalid hostname for this tenancy"), false },
    };

    [Theory(DisplayName = "#1830 (must not fire): a genuine fault on a container is still an Error and failed++, and the other record's revoked grant is still removed")]
    [MemberData(nameof(GenuineFaults))]
    public async Task Jit_GenuineFault_IsStillFailed_AndTheOtherGrantIsRemoved(string because, Exception fault, bool deadFirst)
    {
        DeadContainerBesideARevokedGrant(fault, deadFirst);
        var log = new RecordingLogger<SpeContainerMembershipSync>();

        var result = await Sut(logger: log).RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.Failed.Should().Be(1, because);
        result.Unresolvable.Should().Be(0, because);
        result.Removed.Should().Be(1, because);
        result.Problems.Should().ContainSingle().Which.Should().Contain(DeadProject.ToString()).And.NotContain("does not resolve");
        RemovalVerified(SecureContainer, Alice, Membership.JitWriterMarkerPrefix, Times.Once());
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(DeadContainer), because);
    }

    [Fact(DisplayName = "#1830 (must not fire): an all-healthy run reports no problem and no failure")]
    public async Task Jit_HealthyRun_IsUnchanged()
    {
        SecureContainerWithJitGrant(Alice);
        Holder(Alice);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Alice, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

        var result = await Sut().RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.Should().BeEquivalentTo(new { SecureContainers = 1, GrantsSeen = 1, Removed = 1, Failed = 0, Unresolvable = 0, Unknown = 0, Kept = 0 });
        result.Problems.Should().BeEmpty();
    }

    private const string LiveContainer = "b!live-container";
    private static readonly Guid LiveProject = Guid.Parse("18300000-0000-4000-8000-0000000000e0");

    /// <summary>
    /// MUTATION: the unresolvable catch filter without <c>!resolved</c> (any read's 400 invalidRequest classified) — a live
    /// container's revocations were skipped while the run reported success.
    /// </summary>
    [Theory(DisplayName = "#1830 F2-1: a 400 invalidRequest AFTER the marker read resolved the id is a failure (Error, failed++), not an unresolvable container; the other record's revoked grant is still removed")]
    [InlineData("ReadAccessAsync's container read", 1)]
    [InlineData("the permission collection", 1)]
    [InlineData("an @odata.nextLink page of the permission collection", 2)]
    public async Task Jit_InvalidRequestAfterTheIdResolved_IsFailed(string where, int permissionGets)
    {
        // The real ReadAccessAsync over the Graph SDK seam; the marker read (the FIRST read) answers.
        var graph = new SpeContainerGraphStub();
        switch (where)
        {
            case "ReadAccessAsync's container read":
                graph.ContainerReadError = SpeContainerGraphStub.InvalidHostname();
                break;
            case "the permission collection":
                graph.PermissionPageError = (1, SpeContainerGraphStub.InvalidHostname());
                break;
            default:
                graph.PermissionPages = 2;
                graph.PermissionPageError = (2, SpeContainerGraphStub.InvalidHostname());
                break;
        }

        var membership = new Mock<Membership>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph.Client)), NullLogger<Membership>.Instance)
        { CallBase = true };
        membership.Setup(m => m.ReadMarkersAsync(LiveContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Alice)] = "perm-alice" });
        _secureProjects.Add(new Entity("sprk_project", LiveProject) { ["sprk_containerid"] = LiveContainer });

        // Record B: a healthy container whose JIT holder (Bob) lost Write.
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        var markers = new Dictionary<string, string> { [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Bob)] = "perm-bob" };
        membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>())).ReturnsAsync(markers);
        membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-bob", ["writer"], "b@contoso.example", Bob.ToString())], true, markers));
        membership.Setup(m => m.RemoveMarkedGrantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedRemovalOutcome.Removed);
        _dataverse.Setup(d => d.RetrieveAsync("sprk_project", SecureProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", SecureProject) { ["sprk_accesspermission"] = new OptionSetValue(100000000) });
        Holder(Bob);
        _rights.Setup(r => r.GetPrincipalRightsOrUnknownAsync(Bob, "sprk_projects", SecureProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
        var log = new RecordingLogger<SpeContainerMembershipSync>();

        var result = await Sut(membership.Object, log).RemoveRevokedJitGrantsAsync(CancellationToken.None);

        graph.Requests.Count(r => r.Contains("/permissions", StringComparison.Ordinal))
            .Should().Be(where == "ReadAccessAsync's container read" ? 0 : permissionGets, "the fault comes from " + where);
        result.Failed.Should().Be(1, where);
        result.Unresolvable.Should().Be(0, where);
        result.Removed.Should().Be(1, where);
        result.Problems.Should().ContainSingle().Which.Should().Contain(LiveProject.ToString()).And.NotContain("does not resolve");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(LiveContainer), where);
        membership.Verify(m => m.RemoveMarkedGrantAsync(
            SecureContainer, Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Bob), "perm-bob",
            It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(m => m.RemoveMarkedGrantAsync(
            LiveContainer, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Membership.ContainerAccess>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// MUTATION: <see cref="Membership.RemoveMarkedGrantAsync"/>'s ownership check back outside its try — the guard's fault
    /// threw out of the per-container loop and the container's other revoked grant waited for the next run.
    /// </summary>
    [Fact(DisplayName = "#1830: a guard fault while removing one grant fails that grant only — the container's other revoked grant is removed")]
    public async Task Jit_GuardFaultOnOneRemoval_DoesNotSkipTheOtherGrants()
    {
        var graph = new SpeContainerGraphStub();
        var guard = new ScriptedOwnershipGuard(graph.Client, (call, _) => call == 1
            ? throw new SpaarkeStorageException("ownership read throttled", 429)
            : true);
        var membership = new Mock<Membership>(guard, NullLogger<Membership>.Instance) { CallBase = true };
        _secureProjects.Add(new Entity("sprk_project", SecureProject) { ["sprk_containerid"] = SecureContainer });
        var markers = new Dictionary<string, string>
        {
            [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Alice)] = "perm-alice",
            [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, Bob)] = "perm-bob",
        };
        membership.Setup(m => m.ReadMarkersAsync(SecureContainer, It.IsAny<CancellationToken>())).ReturnsAsync(markers);
        membership.Setup(m => m.ReadAccessAsync(SecureContainer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [
                    new Membership.ContainerUserRole("perm-alice", ["writer"], "a@contoso.example", Alice.ToString()),
                    new Membership.ContainerUserRole("perm-bob", ["writer"], "b@contoso.example", Bob.ToString()),
                ],
                true,
                markers));
        _dataverse.Setup(d => d.RetrieveAsync("sprk_project", SecureProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", SecureProject) { ["sprk_accesspermission"] = new OptionSetValue(100000000) });
        Holder(Alice, disabled: true);
        Holder(Bob, disabled: true);

        var result = await Sut(membership.Object).RemoveRevokedJitGrantsAsync(CancellationToken.None);

        result.GrantsSeen.Should().Be(2);
        result.Removed.Should().Be(1);
        result.Failed.Should().Be(1, "the grant whose ownership check faulted stays recorded for the next run");
        guard.Calls.Should().Be(2);
        graph.Requests.Count(r => r.StartsWith("DELETE ", StringComparison.Ordinal)).Should().Be(1);
    }

    // ── The job's verdict ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<JobRunResult> RunJobAsync(ILogger<SpeContainerMembershipSyncJob>? logger = null)
    {
        var services = new ServiceCollection().AddSingleton(Sut()).BuildServiceProvider();
        var job = new SpeContainerMembershipSyncJob(
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            logger ?? NullLogger<SpeContainerMembershipSyncJob>.Instance);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr", JobRunTrigger.Scheduled, new Dictionary<string, object>()), CancellationToken.None);
    }

    /// <summary>MUTATION: the sweep not counting the skip (<c>unresolvable++</c> removed) — a dead container ran green, uncounted.</summary>
    [Fact(DisplayName = "#1830: the job SUCCEEDS on a run whose only problem is an unresolvable container, and COUNTS it (unresolvable=1) in the heartbeat and ResultJson")]
    public async Task Job_UnresolvableContainerOnly_Succeeds()
    {
        DeadContainerBesideARevokedGrant(InvalidHostname(), deadFirst: true);
        var log = new RecordingLogger<SpeContainerMembershipSyncJob>();

        var run = await RunJobAsync(log);

        run.Success.Should().BeTrue();
        run.ProcessedItems.Should().Be(1);
        run.ResultJson.Should().Contain(DeadContainer).And.Contain("does not resolve in this tenant")
            .And.Contain("\"failed\":0,\"unresolvable\":1");
        log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain("failed=0, unresolvable=1, truncated=False");
    }

    [Fact(DisplayName = "#1830 (must not fire): the job still FAILS on a genuine container fault")]
    public async Task Job_GenuineFault_Fails()
    {
        DeadContainerBesideARevokedGrant(GraphError(503, "serviceNotAvailable", "Service unavailable"), deadFirst: true);
        var log = new RecordingLogger<SpeContainerMembershipSyncJob>();

        var run = await RunJobAsync(log);

        run.Success.Should().BeFalse();
        run.ProcessedItems.Should().Be(1);
        run.ResultJson.Should().Contain("\"failed\":1,\"unresolvable\":0");
        log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain("failed=1, unresolvable=0, truncated=False");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact(DisplayName = "Task 227d: a business unit stamped with a container this stamp does not own is a named failure, and the pass goes on")]
    public async Task Standing_ContainerRefusedByOwnershipGuard_IsANamedFailure()
    {
        BusinessUnitContainer();
        _users.Add(User(Alice, Unit));
        _units.Add(new Entity("businessunit", OtherUnit) { ["sprk_containerid"] = "b!foreign-container" });
        _membership.Setup(m => m.ReadAccessAsync("b!foreign-container", It.IsAny<CancellationToken>())).ThrowsAsync(NotOwned());

        var result = await Sut().SyncStandingWritersAsync(CancellationToken.None);

        result.Failed.Should().Be(1);
        result.Problems.Should().ContainSingle().Which.Should().Contain("b!foreign-container").And.Contain("not one of this stamp's containers");
        GrantVerified(Alice, Times.Once());
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
