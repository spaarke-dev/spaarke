using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// The three generic update paths — the playbook update (<see cref="Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore"/>),
/// the output orchestrator (<see cref="DataverseUpdateHandler"/>) and the field-mapping push — update existing records only,
/// on every table: the write is <see cref="IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync(string, Guid, Dictionary{string, object?}, Guid?, CancellationToken)"/>
/// (<c>If-Match: *</c>), never the create-if-missing <see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/>. An id
/// that names no row is refused (<see cref="RecordOwnerRefusal.RecordMissing"/>) and nothing is written. The invoice family's
/// own not-found answers are in the FixRound2 part. Enforced also by <c>UpdateRecordFieldsCallerGuardTests</c> (ArchTests).
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    /// <summary>A table outside the invoice family: no guard read, no re-file (a plain text column).</summary>
    private const string PlainTable = "sprk_todo";

    /// <summary>The app-only reader of a plain-table write: it is asked nothing (strict, no setups).</summary>
    private static IGenericEntityService NoReads() => new Mock<IGenericEntityService>(MockBehavior.Strict).Object;

    private void NoRowHasThisId() =>
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(PlainTable, MissingRow,
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"{PlainTable} record was not found."));

    public static TheoryData<GenericPath, Guid?> EveryPathAndIdentity()
    {
        var data = new TheoryData<GenericPath, Guid?>();
        foreach (var path in Enum.GetValues<GenericPath>())
        {
            data.Add(path, null);
            data.Add(path, Caller);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPathAndIdentity))]
    public async Task AnUnknownId_IsRefusedRecordMissing_AndNothingIsCreated(GenericPath path, Guid? actsAs)
    {
        NoRowHasThisId();

        var outcome = await RunAsync(path, PlainTable, MissingRow, NoLookups, RenameOnly, app: NoReads(), playbookActsAs: actsAs);

        outcome.Message.Should().NotBeNull("the unknown id is refused, not written");
        outcome.Message.Should().Contain(RecordOwnerRefusal.RecordMissing);
        AssertNotFoundWording(path, actsAs, outcome.Message!, refersToOtherRecord: false);
        if (path != GenericPath.FieldMappingPush)
            outcome.Code.Should().Be(RecordOwnerRefusal.RecordMissing);
        NoGuid(outcome);
        Sent().Should().ContainSingle("the one update-only attempt, which Dataverse refused")
            .Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
        Sent().Should().NotContain(w => w.Method == nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync),
            "the create-if-missing write would have created a row with this id");
    }

    /// <summary>
    /// The exact words of the not-found answer. App-only: "was not found;". A person's playbook run: "was not found or could not
    /// be read". The push always writes as its caller, so it keeps the app-only words. A payload with an <c>@odata.bind</c> names
    /// "the record, or a record this update refers to" (Dataverse also answers 404 for a bind to a row that does not exist).
    /// </summary>
    private static void AssertNotFoundWording(GenericPath path, Guid? actsAs, string message, bool refersToOtherRecord)
    {
        var subject = refersToOtherRecord ? "record, or a record this update refers to, was not found" : "record was not found";
        var personWording = path != GenericPath.FieldMappingPush && actsAs is not null;
        if (personWording)
        {
            message.Should().Contain($"{subject} or could not be read; nothing was");
        }
        else
        {
            message.Should().Contain($"{subject}; nothing was").And.NotContain("could not be read");
        }

        if (!refersToOtherRecord)
            message.Should().NotContain("refers to");
    }

    [Theory]
    [MemberData(nameof(EveryPathAndIdentity))]
    public async Task AnUnknownId_WithALookupBind_SaysTheRecordOrARecordItRefersToWasNotFound(GenericPath path, Guid? actsAs)
    {
        NoRowHasThisId();

        // The core and the push turn this lookup into an "@odata.bind" key; the orchestrator is given the key itself.
        var lookups = Lookups(("sprk_matter", MatterRef(Matter)));
        PathOutcome outcome;
        if (path == GenericPath.OutputOrchestrator)
        {
            try
            {
                await Handler(NoReads()).UpdateAsync(
                    PlainTable, MissingRow,
                    new Dictionary<string, object?> { ["sprk_name"] = "Renamed", ["sprk_matter@odata.bind"] = $"/sprk_matters({Matter:D})" },
                    Sprk.Bff.Api.Services.Ai.ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, actsAs);
                outcome = new PathOutcome(null, null);
            }
            catch (RecordOwnerUnresolvedException refused)
            {
                outcome = new PathOutcome(refused.RefusalCode, refused.Message);
            }
        }
        else
        {
            outcome = await RunAsync(path, PlainTable, MissingRow, lookups, RenameOnly, app: NoReads(), playbookActsAs: actsAs);
        }

        outcome.Message.Should().NotBeNull("the unknown id is refused, not written");
        outcome.Message.Should().Contain(RecordOwnerRefusal.RecordMissing);
        AssertNotFoundWording(path, actsAs, outcome.Message!, refersToOtherRecord: true);
        if (path != GenericPath.FieldMappingPush)
            outcome.Code.Should().Be(RecordOwnerRefusal.RecordMissing);
        NoGuid(outcome);
        Sent().Should().ContainSingle().Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
    }

    [Theory]
    [MemberData(nameof(EveryPathAndIdentity))]
    public async Task AnExistingId_IsWrittenUpdateOnly_AsTheSameIdentity(GenericPath path, Guid? actsAs)
    {
        var outcome = await RunAsync(path, PlainTable, Event, NoLookups, RenameOnly, app: NoReads(), playbookActsAs: actsAs);

        outcome.Code.Should().BeNull();
        outcome.Message.Should().BeNull();
        var sent = Sent().Should().ContainSingle().Subject;
        sent.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync),
            "a generic update path never sends the create-if-missing write");
        sent.Table.Should().Be(PlainTable);
        sent.Id.Should().Be(Event);
        // The push always writes as its caller; the playbook paths write as the run's user, or app-only for a system run.
        sent.As.Should().Be(path == GenericPath.FieldMappingPush ? Caller : actsAs);
    }

    [Fact]
    public async Task AnyOtherDataverseFault_KeepsItsBehaviour_OnEveryPath()
    {
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 500"));

        // The playbook paths propagate it (their callers turn it into a failed node or update); the push fails this child with a
        // FIXED string (task 040, #1437 / ADR-019): the fault's text is logged, never returned.
        var core = () => RunAsync(GenericPath.PlaybookUpdate, PlainTable, Event, NoLookups, RenameOnly, app: NoReads());
        await core.Should().ThrowAsync<HttpRequestException>();
        var orchestrator = () => RunAsync(GenericPath.OutputOrchestrator, PlainTable, Event, NoLookups, RenameOnly, app: NoReads());
        await orchestrator.Should().ThrowAsync<HttpRequestException>();
        var push = await RunAsync(GenericPath.FieldMappingPush, PlainTable, Event, NoLookups, RenameOnly, app: NoReads());
        push.Message.Should().Be(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ChildUpdateFailedMessage)
            .And.NotContain("500");
    }

    [Fact]
    public async Task TheOutputOrchestrator_OnItsOptimisticPath_WritesUpdateOnly_OnEveryTable()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync(PlainTable, Event, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity(PlainTable, Event) { ["versionnumber"] = 7L });

        await Handler(app.Object).UpdateAsync(
            PlainTable, Event, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" },
            Sprk.Bff.Api.Services.Ai.ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None);

        Sent().Should().ContainSingle().Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
    }
}
