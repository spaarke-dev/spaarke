using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 189 fix round C (K5): an APP-ONLY generic writer never writes a document's
/// <c>sprk_accesspermission</c> or its own-narrowing record <c>sprk_accessinheritance</c>
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>). These writers are the Update Record playbook node and a
/// playbook's output mapping (<see cref="DataverseUpdateHandler"/>) whatever identity the run writes as (#2257 fix round 1),
/// and the action seam acting for no person (<see cref="UpdateRecordActionCore"/>). Their payload is built by a model or a configuration; a value or record it wrote
/// could lower a user's Restricted document and show it to partners (D7). Must not fire: the same columns on a to-do or
/// an event (allowed today), and the action seam's apply of a proposal its impersonated user confirmed (a user's own edit).
/// </summary>
public class DocumentAccessPermissionAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("18900000-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("18900000-0000-0000-0000-0000000000a1");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    [Theory]
    [InlineData("sprk_accesspermission", "100000000")]
    [InlineData("sprk_accessinheritance", "{\"v\":1,\"parents\":[],\"floorPermission\":100000000}")]
    [InlineData("SPRK_AccessPermission", "100000000")]
    public async Task APlaybookUpdate_OfADocumentsAccessColumns_IsRefused_AndNothingIsWritten(string column, string value)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_documentdescription"] = "x", [column] = value },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_accesspermission")]
    [InlineData("sprk_event", "sprk_accesspermission")]
    [InlineData("sprk_todo", "sprk_accessinheritance")]
    public async Task APlaybookUpdate_OfTheSameColumnOnAToDoOrAnEvent_IsWritten(string table, string column)
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { [column] = "100000002" },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey(column)), It.IsAny<CancellationToken>(), null),
            Times.Once);
    }

    [Fact]
    public async Task AnUpdateActingForItsUser_OfADocumentsAccessPermission_IsWritten_AsThatUser()
    {
        // A user's own edit (the action seam's confirmed apply, impersonating the confirming user): Dataverse applies their
        // Write, and the document rule reads a user's narrowing as their own.
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000002" }, Lookups: null,
                ImpersonateSystemUserId: User, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_accesspermission")),
            It.IsAny<CancellationToken>(), User), Times.Once);
    }

    [Fact]
    public async Task AConfiguredPayload_WrittenAsItsRunsUser_IsStillRefused_AndNothingIsWritten()
    {
        // #2257 fix round 1 (main-session decision): a payload built by configuration (the Update Record node's: a playbook
        // author's, who may not be the person running it) is asked the refusal even when the write impersonates that person.
        // The input's default is Configuration, so a new caller is refused unless it says it applies a confirmed proposal.
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000000" }, Lookups: null,
                ImpersonateSystemUserId: User),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AConfirmedProposal_ActingForNoUser_IsStillRefused()
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000000" }, Lookups: null,
                PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AConfirmedProposal_ForAnEmptyUser_IsRefused_AsNoPerson()
    {
        // #2257 fix round 2 (K4): Guid.Empty names no person, so it never earns the confirmed-proposal skip.
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000000" }, Lookups: null,
                ImpersonateSystemUserId: Guid.Empty, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheActionSeamsImpersonatedApply_IsUnchanged_AndWritesAsTheConfirmingUser()
    {
        // #2257 fix round 1: every IActionSeam.UpdateRecordAsync caller (the Job B apply services) applies a proposal its user
        // confirmed, so the seam passes ConfirmedProposal and keeps its behaviour.
        var result = await Seam().UpdateRecordAsync(
            new Sprk.Bff.Api.Services.Ai.PublicContracts.UpdateRecordRequest
            {
                EntityLogicalName = "sprk_document",
                RecordId = Row,
                LegacyFields = new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000002" },
                ImpersonateSystemUserId = User,
            },
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_accesspermission")),
            It.IsAny<CancellationToken>(), User), Times.Once);
    }

    [Fact]
    public async Task TheActionSeam_ActingForNoUser_IsStillRefused()
    {
        var result = await Seam().UpdateRecordAsync(
            new Sprk.Bff.Api.Services.Ai.PublicContracts.UpdateRecordRequest
            {
                EntityLogicalName = "sprk_document",
                RecordId = Row,
                LegacyFields = new Dictionary<string, string?> { ["sprk_accesspermission"] = "100000000" },
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    private Sprk.Bff.Api.Services.Ai.PublicContracts.ActionSeam Seam() => new(
        Mock.Of<IGenericEntityService>(),
        _writes.Object,
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        CoreAncestorResolverFixtures.Inert(),
        new RecordOwnershipResolverDouble(),
        IdentityNormalizationFixtures.NoLinkedContact(),
        Mock.Of<ICommunicationDataverseService>(),
        NullLogger<Sprk.Bff.Api.Services.Ai.PublicContracts.ActionSeam>.Instance);

    [Theory]
    [InlineData("sprk_accesspermission")]
    [InlineData("sprk_accessinheritance")]
    public async Task AnOutputMapping_OfADocumentsAccessColumns_IsRefused_AndNothingIsWritten(string column)
    {
        var act = () => Handler().UpdateAsync(
            "sprk_document", Row, new Dictionary<string, object?> { [column] = InheritedAccessPermission.Standard },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    public async Task AnOutputMapping_OfTheSameColumnOnAToDoOrAnEvent_IsWritten(string table)
    {
        await Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { ["sprk_accesspermission"] = InheritedAccessPermission.Restricted },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_accesspermission")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler()
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }
}
