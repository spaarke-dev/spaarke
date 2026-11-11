using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 045b, fix round (F-045b-1). The rule: a regarding pair's display columns
/// (<c>sprk_regardingrecordname</c> / <c>number</c> / <c>url</c>) that the update planner derives for a pair the update itself
/// moves (rule 3) are read as the person the write runs for. With no person they are cleared, never derived app-only. Rules 2
/// and 4 follow a lookup the row holds and keep the app-only reader; the push passes its own caller reader for every rule.
/// Each app-only caller (<see cref="KeepRegardingPairWholeAppOnlyAsync"/> itself, the output orchestrator
/// <see cref="DataverseUpdateHandler"/> and the playbook core <see cref="UpdateRecordActionCore"/>) hands the planner the
/// person, or its absence. The app-only entity service is STRICT with no setup for the regarded record: a read of it as the
/// app fails the test.
/// </summary>
public sealed class RegardingPairMovedDisplayReaderTests
{
    private static readonly Guid MatterType = Guid.Parse("a0450c00-0000-4000-8000-000000000001");
    private static readonly Guid MatterA = Guid.Parse("a0450c00-0000-4000-8000-0000000000a1");
    private static readonly Guid MatterB = Guid.Parse("a0450c00-0000-4000-8000-0000000000b1");
    private static readonly Guid Row = Guid.Parse("a0450c00-0000-4000-8000-0000000000f1");
    private static readonly Guid Person = Guid.Parse("a0450c00-0000-4000-8000-0000000000aa");

    private static string MatterNameColumn => RegardingRecordType.GetPrimaryNameField("sprk_matter")!;

    private static readonly string[] Display = ["sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl"];

    /// <summary>The app-only world: a to-do filed on matter A, and the matter catalog row. The regarded matters have NO setup.</summary>
    private static Mock<IGenericEntityService> AppOnlyWorld()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_todo", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_todo", Row)
            {
                [SecureRootInheritance.PairIdColumn] = MatterA.ToString("D"),
                [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
            });
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType)
            {
                ["sprk_recordlogicalname"] = "sprk_matter",
                [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber",
            });
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        return entities;
    }

    private static void TheMatterWasNeverReadAsTheApp(Mock<IGenericEntityService> entities) =>
        entities.Verify(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);

    private static Dictionary<string, object?> MovesTheToDo() => new() { ["sprk_regardingrecordid"] = MatterB.ToString("D") };

    public enum PersonRead { MayRead, Forbidden, NotFound, NoRows, Fault }

    /// <summary>The person's read of the matter (<c>MSCRMCallerID</c>), faked.</summary>
    private static Mock<IImpersonatedCommunicationQuery> PersonsQuery(PersonRead answer)
    {
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        var setup = query.Setup(q => q.QueryAsync("sprk_matters", It.IsAny<string?>(), Person, It.IsAny<CancellationToken>()));
        switch (answer)
        {
            case PersonRead.MayRead:
                setup.ReturnsAsync(new List<Dictionary<string, JsonElement>>
                {
                    new()
                    {
                        [MatterNameColumn] = JsonSerializer.SerializeToElement("Beta v. Gamma"),
                        ["sprk_matternumber"] = JsonSerializer.SerializeToElement("M-0002"),
                    },
                });
                break;
            case PersonRead.Forbidden:
                setup.ThrowsAsync(new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden));
                break;
            case PersonRead.NotFound:
                setup.ThrowsAsync(new HttpRequestException("missing", null, HttpStatusCode.NotFound));
                break;
            case PersonRead.NoRows:
                setup.ReturnsAsync(new List<Dictionary<string, JsonElement>>());
                break;
            default:
                setup.ThrowsAsync(new TimeoutException("socket"));
                break;
        }

        return query;
    }

    private static void ShouldBeCleared(Dictionary<string, object?> written)
    {
        foreach (var column in Display)
            written.Should().ContainKey(column).WhoseValue.Should().BeNull($"{column} is cleared, not derived");
        written["sprk_regardingrecordid"].Should().Be(MatterB.ToString("D"), "the move itself stands");
    }

    private static void ShouldBeDerivedFromThePersonsRead(Dictionary<string, object?> written)
    {
        written["sprk_regardingrecordname"].Should().Be("Beta v. Gamma");
        written["sprk_regardingrecordnumber"].Should().Be("M-0002");
        written["sprk_regardingrecordurl"].Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", MatterB.ToString("D")));
    }

    // ── The planner entry (KeepRegardingPairWholeAppOnlyAsync) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PersonRead.Forbidden)]
    [InlineData(PersonRead.NotFound)]
    [InlineData(PersonRead.NoRows)]
    public async Task Rule3_APersonWhoseReadAnswersForbiddenOrNotFound_HasTheDisplayCleared_AndTheAppNeverReadsTheRecord(PersonRead answer)
    {
        var entities = AppOnlyWorld();
        var query = PersonsQuery(answer);

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(query.Object, entities.Object, Person));

        ShouldBeCleared(written);
        TheMatterWasNeverReadAsTheApp(entities);
        query.Verify(q => q.QueryAsync("sprk_matters", It.IsAny<string?>(), Person, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rule3_WithNoPerson_ClearsTheDisplay_AndNeverReadsTheRecord(bool emptyPerson)
    {
        var entities = AppOnlyWorld();
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(query.Object, entities.Object, emptyPerson ? Guid.Empty : null));

        ShouldBeCleared(written);
        TheMatterWasNeverReadAsTheApp(entities);
        query.Invocations.Should().BeEmpty("no person, so nobody to read as");
    }

    [Fact]
    public async Task Rule3_WhenTheCallerPassesNoReaderAtAll_ClearsTheDisplay_TheSecureDefault()
    {
        var entities = AppOnlyWorld();

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default);

        ShouldBeCleared(written);
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task Rule3_APersonWhoMayRead_HasTheDisplayDerivedFromTheirRead_NotTheApps()
    {
        var entities = AppOnlyWorld();
        var query = PersonsQuery(PersonRead.MayRead);

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(query.Object, entities.Object, Person));

        ShouldBeDerivedFromThePersonsRead(written);
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task Rule3_APersonWhoseReadFaults_RefusesTheWrite()
    {
        var entities = AppOnlyWorld();
        var query = PersonsQuery(PersonRead.Fault);

        var act = () => KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(query.Object, entities.Object, Person));

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task Rule3_APersonWithNoQueryAvailable_RefusesTheWrite_NeverReadingAppOnly()
    {
        var entities = AppOnlyWorld();

        var act = () => KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "UpdateRecord", null, default,
            movedDisplayRecord: MovedDisplayReader(null, entities.Object, Person));

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task Rule3_ADisplayColumnTheUpdateSendsItself_IsKept_AndTheOthersAreCleared()
    {
        var entities = AppOnlyWorld();
        var payload = MovesTheToDo();
        payload["sprk_regardingrecordname"] = "Sent by the update";

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, payload, "UpdateRecord", null, default);

        written["sprk_regardingrecordname"].Should().Be("Sent by the update");
        written.Should().ContainKey("sprk_regardingrecordnumber").WhoseValue.Should().BeNull();
        written.Should().ContainKey("sprk_regardingrecordurl").WhoseValue.Should().BeNull();
    }

    [Fact]
    public async Task Rule4_ALookupThePairRepeatedMoves_StillDerivesTheDisplayAppOnly_WithNoPerson()
    {
        var matterColumn = PairAgreementColumns("sprk_invoice").First(c => c.Value == "sprk_matter").Key;
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_invoice", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Row)
            {
                [matterColumn] = new EntityReference("sprk_matter", MatterA),
                [SecureRootInheritance.PairIdColumn] = MatterA.ToString("D"),
                [SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", MatterType),
            });
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterB, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", MatterB) { [MatterNameColumn] = "Beta v. Gamma" });

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_invoice", Row,
            new() { [matterColumn] = new EntityReference("sprk_matter", MatterB) }, "UpdateRecord", null, default);

        written["sprk_regardingrecordid"].Should().Be(MatterB.ToString("D"));
        written["sprk_regardingrecordname"].Should().Be("Beta v. Gamma", "rule 4 follows a lookup the row holds: read app-only, unchanged");
        entities.Verify(e => e.RetrieveAsync("sprk_matter", MatterB, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rule2_ATypeOnlyUpdate_StillDerivesTheDisplayAppOnly_WithNoPerson()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_workassignment", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_workassignment", Row) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterA) });
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType)
            {
                ["sprk_recordlogicalname"] = "sprk_matter",
                [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber",
            });
        entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterA, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", MatterA) { [MatterNameColumn] = "Acme v. Widget", ["sprk_matternumber"] = "M-0001" });

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_workassignment", Row,
            new() { ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})" }, "UpdateRecord", null, default);

        written["sprk_regardingrecordid"].Should().Be(MatterA.ToString("D"));
        written["sprk_regardingrecordname"].Should().Be("Acme v. Widget");
        written["sprk_regardingrecordnumber"].Should().Be("M-0001");
    }

    [Fact]
    public async Task ThePush_WithItsCallerReader_ReadsEveryRulesDisplayThroughIt_AsBefore()
    {
        var entities = AppOnlyWorld();
        var pushReads = 0;

        var written = await KeepRegardingPairWholeAppOnlyAsync(entities.Object, "sprk_todo", Row, MovesTheToDo(), "FIELD-MAPPING-PUSH", null, default,
            displayRecord: (_, _, asked, _) =>
            {
                pushReads++;
                return Task.FromResult(RegardingPairRead<IReadOnlyDictionary<string, string?>>.Ok(
                    asked.ToDictionary(c => c, c => c == MatterNameColumn ? "Pushed" : (string?)"M-9", StringComparer.OrdinalIgnoreCase)));
            });

        pushReads.Should().Be(1);
        written["sprk_regardingrecordname"].Should().Be("Pushed");
        TheMatterWasNeverReadAsTheApp(entities);
    }

    // ── The output orchestrator (DataverseUpdateHandler) hands the planner its person ─────────────────────────────────────

    private static (DataverseUpdateHandler Handler, Func<Dictionary<string, object?>> Written) HandlerOver(
        IGenericEntityService entities, IImpersonatedCommunicationQuery? asPerson)
    {
        Dictionary<string, object?>? sent = null;
        var writes = new Mock<IFieldMappingDataverseService>();
        writes.Setup(w => w.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, Dictionary<string, object?> fields, Guid? _, CancellationToken _) => sent = fields)
            .Returns(Task.CompletedTask);
        var handler = new DataverseUpdateHandler(writes.Object, entities, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld(), shares: null, readAsPerson: asPerson);
        return (handler, () => sent ?? throw new InvalidOperationException("nothing was written"));
    }

    [Theory]
    [InlineData(PersonRead.Forbidden)]
    [InlineData(PersonRead.NotFound)]
    public async Task OutputOrchestrator_APersonWhoseReadIsRefused_HasTheMovedPairsDisplayCleared(PersonRead answer)
    {
        var entities = AppOnlyWorld();
        var (handler, written) = HandlerOver(entities.Object, PersonsQuery(answer).Object);

        await handler.UpdateAsync("sprk_todo", Row, MovesTheToDo(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        ShouldBeCleared(written());
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task OutputOrchestrator_WithNoPerson_ClearsTheMovedPairsDisplay_AndNeverReadsTheRecord()
    {
        var entities = AppOnlyWorld();
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        var (handler, written) = HandlerOver(entities.Object, query.Object);

        await handler.UpdateAsync("sprk_todo", Row, MovesTheToDo(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        ShouldBeCleared(written());
        TheMatterWasNeverReadAsTheApp(entities);
        query.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task OutputOrchestrator_APersonWhoMayRead_HasTheMovedPairsDisplayDerivedFromTheirRead()
    {
        var entities = AppOnlyWorld();
        var (handler, written) = HandlerOver(entities.Object, PersonsQuery(PersonRead.MayRead).Object);

        await handler.UpdateAsync("sprk_todo", Row, MovesTheToDo(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        ShouldBeDerivedFromThePersonsRead(written());
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task OutputOrchestrator_APersonWithNoQueryRegistered_RefusesTheWrite()
    {
        var entities = AppOnlyWorld();
        var (handler, _) = HandlerOver(entities.Object, null);

        var act = () => handler.UpdateAsync("sprk_todo", Row, MovesTheToDo(), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        TheMatterWasNeverReadAsTheApp(entities);
    }

    // ── The playbook Update Record core (UpdateRecordActionCore) hands the planner its person ─────────────────────────────

    private static (UpdateRecordActionCore Core, Func<Dictionary<string, object?>> Written) CoreOver(
        IGenericEntityService entities, IImpersonatedCommunicationQuery? asPerson)
    {
        Dictionary<string, object?>? sent = null;
        var writes = new Mock<IFieldMappingDataverseService>();
        writes.Setup(w => w.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, Dictionary<string, object?> fields, Guid? _, CancellationToken _) => sent = fields)
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .AddSingleton(entities);
        if (asPerson is not null)
            services.AddSingleton(asPerson);
        var core = new UpdateRecordActionCore(writes.Object,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);
        return (core, () => sent ?? throw new InvalidOperationException("nothing was written"));
    }

    private static UpdateRecordActionInput MovesTheToDoInput(Guid? actsAs) => new(
        "sprk_todo", Row, FieldMappings: null,
        LegacyFields: new Dictionary<string, string?> { ["sprk_regardingrecordid"] = MatterB.ToString("D") },
        Lookups: null, ImpersonateSystemUserId: actsAs);

    [Theory]
    [InlineData(PersonRead.Forbidden)]
    [InlineData(PersonRead.NotFound)]
    public async Task PlaybookCore_APersonWhoseReadIsRefused_HasTheMovedPairsDisplayCleared(PersonRead answer)
    {
        var entities = AppOnlyWorld();
        var (core, written) = CoreOver(entities.Object, PersonsQuery(answer).Object);

        await core.UpdateAsync(MovesTheToDoInput(Person), CancellationToken.None);

        ShouldBeCleared(written());
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task PlaybookCore_WithNoPerson_ClearsTheMovedPairsDisplay_AndNeverReadsTheRecord()
    {
        var entities = AppOnlyWorld();
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        var (core, written) = CoreOver(entities.Object, query.Object);

        await core.UpdateAsync(MovesTheToDoInput(null), CancellationToken.None);

        ShouldBeCleared(written());
        TheMatterWasNeverReadAsTheApp(entities);
        query.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaybookCore_APersonWhoMayRead_HasTheMovedPairsDisplayDerivedFromTheirRead()
    {
        var entities = AppOnlyWorld();
        var (core, written) = CoreOver(entities.Object, PersonsQuery(PersonRead.MayRead).Object);

        await core.UpdateAsync(MovesTheToDoInput(Person), CancellationToken.None);

        ShouldBeDerivedFromThePersonsRead(written());
        TheMatterWasNeverReadAsTheApp(entities);
    }

    [Fact]
    public async Task PlaybookCore_APersonWithNoQueryRegistered_RefusesTheWrite()
    {
        var entities = AppOnlyWorld();
        var (core, _) = CoreOver(entities.Object, null);

        var act = () => core.UpdateAsync(MovesTheToDoInput(Person), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        TheMatterWasNeverReadAsTheApp(entities);
    }
}
