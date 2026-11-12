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

        // Task 175 (#1478): a work assignment's or project's Restricted state also comes from what it is filed under. These
        // records are filed under nothing (the filing read returns no row); an unanswered read would fail closed.
        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
    }

    private readonly ExternalVisibilitySeamStub _visibility = new();
    private readonly Dictionary<string, string?> _settings = [];

    private Sut Build(OwningSecureRecord? secureOwner, AccessRights rights)
        => new(secureOwner, new StubProbe(rights, SystemUserId), _membership.Object, _entities.Object, _visibility.Seam,
            new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

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

    // ── Task 191g (owner Q2 = b): a flagged-external caller gets an item permission on the one file, never a container role ──

    private const string ItemId = "item-191g";

    /// <summary>A flagged caller (stored sprk_isexternal = true) and a project that is not Restricted.</summary>
    private void FlaggedCallerOnUnrestrictedProject(bool settingOn = true)
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
        _membership.Setup(m => m.GrantMarkedItemWriterAsync(
                SecureDrive, ItemId, DocumentId, SystemUserId, ObjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.ItemGrantOutcome.Granted);
        if (settingOn)
            _settings[OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
    }

    private void NoContainerGrantOrRead()
    {
        _membership.Verify(m => m.GrantMarkedWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a flagged caller never reaches the container grant");
        _membership.Verify(m => m.ReadAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void NoItemGrant()
        => _membership.Verify(m => m.GrantMarkedItemWriterAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

    private Task<OfficeEditAccess> OpenAsFlagged(AccessRights rights = AccessRights.Read | AccessRights.Write, string? itemId = ItemId)
        => Build(new OwningSecureRecord("sprk_project", ProjectId), rights).PrepareAsync(DocumentId, SecureDrive, Http(), itemId: itemId);

    [Fact(DisplayName = "Task 191g: a flagged Write holder on a secure, non-Restricted record opening a VISIBLE document gets an item permission on that file only — never a container role")]
    public async Task Flagged_WriteVisible_GetsItemGrantOnly()
    {
        FlaggedCallerOnUnrestrictedProject();

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(true, "item-writer-jit"));
        _membership.Verify(m => m.GrantMarkedItemWriterAsync(
            SecureDrive, ItemId, DocumentId, SystemUserId, ObjectId, It.IsAny<CancellationToken>()), Times.Once);
        NoContainerGrantOrRead();
        _visibility.Calls.Should().ContainSingle().Which.Should().Equal(("sprk_document", DocumentId));
    }

    [Theory(DisplayName = "Task 191g (must not fire): the item-grant setting absent, false or unparseable grants a flagged caller NOTHING (view only)")]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("on")]
    public async Task Flagged_SettingOff_GrantsNothing(string? setting)
    {
        FlaggedCallerOnUnrestrictedProject(settingOn: false);
        if (setting is not null)
            _settings[OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = setting;

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(false, "none"));
        NoItemGrant();
        NoContainerGrantOrRead();
    }

    [Fact(DisplayName = "Task 191g: a flagged caller WITHOUT Write gets no grant of any kind")]
    public async Task Flagged_NoWrite_NoGrant()
    {
        FlaggedCallerOnUnrestrictedProject();

        var result = await OpenAsFlagged(AccessRights.Read);

        result.Should().Be(new OfficeEditAccess(false, "none"));
        NoItemGrant();
        NoContainerGrantOrRead();
    }

    [Theory(DisplayName = "Task 191g: a flagged Write holder on a RESTRICTED (or unreadable) record gets no item grant")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Flagged_RestrictedRecord_NoItemGrant(bool recordReadable)
    {
        FlaggedCallerOnUnrestrictedProject();
        var project = _entities.Setup(e => e.RetrieveAsync("sprk_project", ProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (recordReadable)
            project.ReturnsAsync(new Entity("sprk_project", ProjectId) { ["sprk_accesspermission"] = new OptionSetValue(100000002) });
        else
            project.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(false, "none"));
        NoItemGrant();
        NoContainerGrantOrRead();
    }

    [Theory(DisplayName = "Task 191g: a flagged Write holder on a document that is NOT externally visible (hidden, undecided, faulted) gets no item grant")]
    [InlineData("Hidden", "DocumentHidden", false)]
    [InlineData("Hidden", "CommunicationHidden", false)]
    [InlineData("Undecided", "NoRoot", false)]
    [InlineData("Undecided", "ReadFailed", true)]
    [InlineData("Visible", "Visible", true)]
    public async Task Flagged_DocumentNotVisible_NoItemGrant(string verdict, string reason, bool faulted)
    {
        FlaggedCallerOnUnrestrictedProject();
        _visibility.Set("sprk_document", DocumentId, Enum.Parse<ExternalVisibilityVerdict>(verdict), Enum.Parse<ExternalVisibilityReason>(reason));
        _visibility.Faulted = faulted;

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(false, "none"));
        NoItemGrant();
        NoContainerGrantOrRead();
    }

    [Fact(DisplayName = "Task 191g: a visibility decision that throws gives a flagged caller no item grant (fail closed)")]
    public async Task Flagged_VisibilityThrows_NoItemGrant()
    {
        FlaggedCallerOnUnrestrictedProject();
        _visibility.Throws = new HttpRequestException("throttled");

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(false, "none"));
        NoItemGrant();
    }

    [Fact(DisplayName = "Task 191g: a caller whose flag cannot be read (the user read fails) gets no grant of any kind — 503, never links that look usable")]
    public async Task UnreadableFlag_NoGrantOfAnyKind()
    {
        _settings[OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        _entities.Setup(e => e.RetrieveAsync("systemuser", SystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var act = () => OpenAsFlagged();

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
        NoItemGrant();
        NoContainerGrantOrRead();
    }

    [Fact(DisplayName = "Task 191g (must not fire): an INTERNAL Write holder still gets the container JIT writer with the item-grant setting on")]
    public async Task Internal_SettingOn_StillGetsContainerJitWriter()
    {
        _settings[OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey] = "true";
        _membership.Setup(m => m.GrantMarkedWriterAsync(
                SecureDrive, Membership.JitWriterMarkerPrefix, SystemUserId, Upn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.MarkedGrantOutcome.Granted);

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(true, "writer-jit"));
        NoItemGrant();
        _visibility.Calls.Should().BeEmpty("an internal caller's edit is not an external-visibility question");
    }

    [Theory(DisplayName = "Task 191g: an item grant that already stands, or a direct permission the caller already holds, is reported truthfully")]
    [InlineData(Membership.ItemGrantOutcome.Reused, true, "member")]
    [InlineData(Membership.ItemGrantOutcome.HeldByOther, true, "member")]
    [InlineData(Membership.ItemGrantOutcome.HeldReadOnly, false, "member-read-only")]
    public async Task Flagged_ExistingPermission_IsReported(Membership.ItemGrantOutcome outcome, bool canEdit, string role)
    {
        FlaggedCallerOnUnrestrictedProject();
        _membership.Setup(m => m.GrantMarkedItemWriterAsync(
                SecureDrive, ItemId, DocumentId, SystemUserId, ObjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var result = await OpenAsFlagged();

        result.Should().Be(new OfficeEditAccess(canEdit, role));
        NoContainerGrantOrRead();
    }

    [Fact(DisplayName = "Task 191g: an item grant that cannot be made, read back or recorded answers 503 edit_access_unavailable")]
    public async Task Flagged_ItemGrantFails_Is503()
    {
        FlaggedCallerOnUnrestrictedProject();
        _membership.Setup(m => m.GrantMarkedItemWriterAsync(
                SecureDrive, ItemId, DocumentId, SystemUserId, ObjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.ItemGrantOutcome.Failed);

        var act = () => OpenAsFlagged();

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(503);
        problem.Code.Should().Be("edit_access_unavailable");
        NoContainerGrantOrRead();
    }

    [Fact(DisplayName = "Task 191g: a visible document whose file id is unknown answers 503 — never a URL that cannot work, never a container role")]
    public async Task Flagged_NoItemId_Is503()
    {
        FlaggedCallerOnUnrestrictedProject();

        var act = () => OpenAsFlagged(itemId: null);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
        NoItemGrant();
        NoContainerGrantOrRead();
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
            _membership.Object, _entities.Object, _visibility.Seam, new ConfigurationBuilder().Build());

        var result = await sut.PrepareAsync(DocumentId, SecureDrive, Http());

        result.Should().Be(new OfficeEditAccess(false, "unknown"));
        _membership.VerifyNoOtherCalls();
    }

    private sealed class Sut : OfficeEditAccessService
    {
        private readonly OwningSecureRecord? _owner;
        private readonly Exception? _fault;

        public Sut(OwningSecureRecord? owner, CallerRecordAccessProbe probe, Membership membership, IGenericEntityService entities,
            AppOnlyExternalVisibility visibility, IConfiguration configuration)
            : base(TestRecordContainerResolver.ForBusinessUnitContainers(), probe, membership, entities, visibility, configuration,
                NullLogger<OfficeEditAccessService>.Instance)
            => _owner = owner;

        public Sut(Exception fault, CallerRecordAccessProbe probe, Membership membership, IGenericEntityService entities,
            AppOnlyExternalVisibility visibility, IConfiguration configuration)
            : base(TestRecordContainerResolver.ForBusinessUnitContainers(), probe, membership, entities, visibility, configuration,
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
