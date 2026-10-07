using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// unified-access-control-r2 task 171 (owner rounds 69 + 70) — the just-in-time Office-edit grant. Pinned: a grant is
/// made ONLY on a secure record's own container and ONLY for a caller Dataverse says holds Write on that record; a
/// business-unit container never gets one (standing writers there); a caller who already holds a role is reused, never
/// granted twice; a Write holder whose grant cannot be made gets 503, never a URL that cannot work.
/// </summary>
/// <remarks>Mocking boundary (ADR-038 §4): the grant primitive (<see cref="Membership"/>, virtual), the caller-rights
/// probe (virtual) and <see cref="IGenericEntityService"/>. The container classification is named at the service's own
/// virtual seam because <see cref="RecordContainerResolver"/> is sealed (ADR-010).</remarks>
public class OfficeEditAccessServiceTests
{
    private const string SecureDrive = "b!secure-project-own-container";
    private const string Upn = "writer@contoso.example";
    private static readonly Guid DocumentId = Guid.Parse("17100000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("17100000-0000-4000-8000-000000000002");
    private static readonly Guid SystemUserId = Guid.Parse("17100000-0000-4000-8000-000000000003");
    private static readonly Guid ObjectId = Guid.Parse("17100000-0000-4000-8000-000000000004");

    private readonly Mock<Membership> _membership =
        new(TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<Membership>.Instance) { CallBase = false };
    private readonly Mock<IGenericEntityService> _entities = new();

    public OfficeEditAccessServiceTests()
    {
        _entities.Setup(e => e.RetrieveAsync("systemuser", SystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", SystemUserId)
            {
                ["domainname"] = Upn,
                ["azureactivedirectoryobjectid"] = ObjectId,
            });
        _membership.Setup(m => m.ReadAccessAsync(SecureDrive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess([], RolesComplete: true, new Dictionary<string, string>()));
    }

    private Sut Build(OwningSecureRecord? secureOwner, AccessRights rights)
        => new(secureOwner, new StubProbe(rights, SystemUserId), _membership.Object, _entities.Object);

    private static DefaultHttpContext Http()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer caller-token";
        return http;
    }

    [Fact(DisplayName = "Task 171: a Write holder on a SECURE record's container is granted exactly one marked JIT writer role")]
    public async Task SecureContainer_WriteHolder_IsGrantedAJitWriterRole()
    {
        _membership.Setup(m => m.GrantMarkedWriterAsync(
                SecureDrive, Membership.JitWriterMarkerPrefix, SystemUserId, Upn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedGrantOutcome.Granted);

        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Read | AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(true, "writer-jit"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            SecureDrive, Membership.JitWriterMarkerPrefix, SystemUserId, Upn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Task 171: a Read-only caller on a secure container gets NO grant (view only)")]
    public async Task SecureContainer_ReadOnlyCaller_GetsNoGrant()
    {
        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Read)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "none"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: a repeat edit-open by a caller who already holds a role reuses it — no second grant")]
    public async Task SecureContainer_CallerAlreadyMember_ReusesTheRole()
    {
        _membership.Setup(m => m.ReadAccessAsync(SecureDrive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-1", ["writer"], Upn, ObjectId.ToString())],
                RolesComplete: true,
                new Dictionary<string, string> { [Membership.MarkerKey(Membership.JitWriterMarkerPrefix, SystemUserId)] = "perm-1" }));

        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(true, "member"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: a caller who already holds a READ-ONLY role is told they cannot edit — the role is never changed")]
    public async Task SecureContainer_CallerHoldsReader_IsReportedReadOnly()
    {
        _membership.Setup(m => m.ReadAccessAsync(SecureDrive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-r", ["reader"], Upn, ObjectId.ToString())],
                RolesComplete: true, new Dictionary<string, string>()));

        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "member-read-only"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "Task 171: an EXTERNAL Write holder on a Restricted (or unreadable) secure record gets no grant — the removal pass's rule, applied up front")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecureContainer_ExternalCallerOnRestrictedRecord_GetsNoGrant(bool recordReadable)
    {
        _entities.Setup(e => e.RetrieveAsync("systemuser", SystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", SystemUserId)
            {
                ["domainname"] = Upn,
                ["azureactivedirectoryobjectid"] = ObjectId,
                ["sprk_isexternal"] = true,
            });
        var project = _entities.Setup(e => e.RetrieveAsync("sprk_project", ProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (recordReadable)
            project.ReturnsAsync(new Entity("sprk_project", ProjectId) { ["sprk_accesspermission"] = new OptionSetValue(100000002) });
        else
            project.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "none"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: an EXTERNAL Write holder on a secure record that is NOT Restricted is granted as usual")]
    public async Task SecureContainer_ExternalCallerOnUnrestrictedRecord_IsGranted()
    {
        _entities.Setup(e => e.RetrieveAsync("systemuser", SystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", SystemUserId)
            {
                ["domainname"] = Upn,
                ["azureactivedirectoryobjectid"] = ObjectId,
                ["sprk_isexternal"] = true,
            });
        _entities.Setup(e => e.RetrieveAsync("sprk_project", ProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", ProjectId) { ["sprk_accesspermission"] = new OptionSetValue(100000000) });
        _membership.Setup(m => m.GrantMarkedWriterAsync(
                SecureDrive, Membership.JitWriterMarkerPrefix, SystemUserId, Upn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedGrantOutcome.Granted);

        var result = await Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(true, "writer-jit"));
    }

    private const string BuDrive = "b!business-unit-container";
    private static readonly Guid CallerUnit = Guid.Parse("17100000-0000-4000-8000-000000000005");

    /// <summary>The caller's user row with the standing-eligibility columns, and their unit's container.</summary>
    private void CallerOnBusinessUnit(bool? external, string unitContainer)
    {
        var user = new Entity("systemuser", SystemUserId)
        {
            ["domainname"] = Upn,
            ["azureactivedirectoryobjectid"] = ObjectId,
            ["isdisabled"] = false,
            ["accessmode"] = new OptionSetValue(0),
            ["businessunitid"] = new EntityReference("businessunit", CallerUnit),
        };
        if (external is not null) user["sprk_isexternal"] = external;
        _entities.Setup(e => e.RetrieveAsync("systemuser", SystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _entities.Setup(e => e.RetrieveAsync("businessunit", CallerUnit, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", CallerUnit) { ["sprk_containerid"] = unitContainer });
    }

    [Fact(DisplayName = "Task 171: a BUSINESS-UNIT container never gets a JIT grant; a standing writer OF THIS CONTAINER is reported canEdit")]
    public async Task BusinessUnitContainer_StandingWriterOfThisContainer_CanEdit_AndNothingIsGranted()
    {
        CallerOnBusinessUnit(external: null, unitContainer: BuDrive);

        var result = await Build(secureOwner: null, AccessRights.Write).PrepareAsync(DocumentId, BuDrive, Http());

        result.Should().Be(new OfficeEditAccess(true, "standing"));
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a standing writer is answered from Dataverse alone");
    }

    [Theory(DisplayName = "Task 171 (finding 9): on a BUSINESS-UNIT container a caller who is NOT its standing writer and holds no role is reported canEdit=false")]
    [InlineData(true, BuDrive)]                        // flagged external — never a standing writer
    [InlineData(null, "b!another-units-container")]    // internal, but their unit maps to another container
    public async Task BusinessUnitContainer_NotAStandingWriter_NoRole_CannotEdit(bool? external, string unitContainer)
    {
        CallerOnBusinessUnit(external, unitContainer);
        _membership.Setup(m => m.ReadAccessAsync(BuDrive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess([], RolesComplete: true, new Dictionary<string, string>()));

        var result = await Build(secureOwner: null, AccessRights.Write).PrepareAsync(DocumentId, BuDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "none"),
            "canEdit reports what Office will allow — a caller with no role on the container cannot edit there");
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Task 171: the grant-only call (open-links) answers a BUSINESS-UNIT container without reading anything")]
    public async Task BusinessUnitContainer_GrantOnly_ReadsNothing()
    {
        var result = await Build(secureOwner: null, AccessRights.Write)
            .PrepareAsync(DocumentId, BuDrive, Http(), describeSharedContainer: false);

        result.Should().Be(new OfficeEditAccess(false, "not-evaluated"));
        _membership.VerifyNoOtherCalls();
        _entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "nothing is granted on a shared container, so open-links must not pay for the report's reads");
    }

    [Fact(DisplayName = "Task 171 (finding 9): on a BUSINESS-UNIT container a non-standing caller who HOLDS a writer role (hand-granted) is reported as a member")]
    public async Task BusinessUnitContainer_HandGrantedWriter_CanEdit()
    {
        CallerOnBusinessUnit(external: null, unitContainer: "b!another-units-container");
        _membership.Setup(m => m.ReadAccessAsync(BuDrive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Membership.ContainerAccess(
                [new Membership.ContainerUserRole("perm-h", ["writer"], Upn, ObjectId.ToString())],
                RolesComplete: true, new Dictionary<string, string>()));

        var result = await Build(secureOwner: null, AccessRights.Write).PrepareAsync(DocumentId, BuDrive, Http());

        result.Should().Be(new OfficeEditAccess(true, "member"));
    }

    [Fact(DisplayName = "Task 171: a Write holder whose grant cannot be made gets 503 edit_access_unavailable — never a URL that cannot work")]
    public async Task SecureContainer_GrantFails_Is503()
    {
        _membership.Setup(m => m.GrantMarkedWriterAsync(
                SecureDrive, Membership.JitWriterMarkerPrefix, SystemUserId, Upn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedGrantOutcome.Failed);

        var act = () => Build(new OwningSecureRecord("sprk_project", ProjectId), AccessRights.Write)
            .PrepareAsync(DocumentId, SecureDrive, Http());

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    [Fact(DisplayName = "Task 171: a container that cannot be classified grants nothing")]
    public async Task UnclassifiableContainer_GrantsNothing()
    {
        var sut = new Sut(new InvalidOperationException("Dataverse down"), new StubProbe(AccessRights.Write, SystemUserId),
            _membership.Object, _entities.Object);

        var result = await sut.PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "unknown"));
        _membership.VerifyNoOtherCalls();
    }

    private sealed class Sut : OfficeEditAccessService
    {
        private readonly OwningSecureRecord? _owner;
        private readonly Exception? _fault;

        public Sut(OwningSecureRecord? owner, CallerRecordAccessProbe probe, Membership membership, IGenericEntityService entities)
            : base(TestRecordContainerResolver.ForBusinessUnitContainers(), probe, membership, entities,
                NullLogger<OfficeEditAccessService>.Instance)
            => _owner = owner;

        public Sut(Exception fault, CallerRecordAccessProbe probe, Membership membership, IGenericEntityService entities)
            : base(TestRecordContainerResolver.ForBusinessUnitContainers(), probe, membership, entities,
                NullLogger<OfficeEditAccessService>.Instance)
            => _fault = fault;

        protected override Task<OwningSecureRecord?> ResolveSecureOwnerAsync(string driveId, CancellationToken ct)
            => _fault is null ? Task.FromResult(_owner) : Task.FromException<OwningSecureRecord?>(_fault);
    }

    private sealed class StubProbe : CallerRecordAccessProbe
    {
        private readonly AccessRights _rights;
        private readonly Guid _systemUserId;

        public StubProbe(AccessRights rights, Guid systemUserId)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
            _rights = rights;
            _systemUserId = systemUserId;
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(callerBearerToken is null || entitySet != "sprk_projects" ? AccessRights.None : _rights);

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
            => Task.FromResult<Guid?>(_systemUserId);
    }
}
