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
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>). These writers are the Update Record playbook node and the
/// action seam acting for no person (<see cref="UpdateRecordActionCore"/>), and a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>). Their payload is built by a model or a configuration; a value or record it wrote
/// could lower a user's Restricted document and show it to partners (D7). Must not fire: the same columns on a to-do or
/// an event (allowed today), and an update acting for its user (a user's own edit).
/// ebilling-r1 task 088 adds the submission marker <c>sprk_restrictedbysubmission</c>
/// (<see cref="InheritedAccessPermission.SubmissionMarkerColumn"/>): refused on a document like the two columns above (a set
/// marker would un-hide an isolated invoice from external callers), not refused on a to-do, and a document payload that does
/// not name it is written.
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
    [InlineData("sprk_restrictedbysubmission", "true")] // ebilling-r1 task 088: the submission marker (must fire)
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
    [InlineData("sprk_todo", "sprk_restrictedbysubmission")] // ebilling-r1 task 088: the marker name off a document (must not fire)
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
    public async Task APlaybookUpdate_OfADocumentWithoutAnAccessColumnOrTheMarker_IsWritten()
    {
        // ebilling-r1 task 088 (must not fire): adding the marker to the refusal does not refuse an ordinary document payload.
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_documentdescription"] = "x" }, Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_documentdescription")),
            It.IsAny<CancellationToken>(), null), Times.Once);
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
                ImpersonateSystemUserId: User),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_accesspermission")),
            It.IsAny<CancellationToken>(), User), Times.Once);
    }

    [Theory]
    [InlineData("sprk_accesspermission")]
    [InlineData("sprk_accessinheritance")]
    [InlineData("sprk_restrictedbysubmission")] // ebilling-r1 task 088: the submission marker (must fire)
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
