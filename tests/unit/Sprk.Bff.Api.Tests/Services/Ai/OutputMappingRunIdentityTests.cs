using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// #2257 (owner round 123, "run as the user"): a playbook's output mapping writes AS the person whose run it is, as the Update
/// Record node does; only a run with no person in it (<see cref="PlaybookRunStartedBy.System"/>) writes app-only. Both are asked
/// <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> (fix round 1: the mapping is configuration, not the person's), and
/// an Optimistic update reads its row version AS the person. A person's run whose user is unknown writes nothing. Two layers:
/// <see cref="OutputOrchestratorService"/> decides the identity per update, and <see cref="DataverseUpdateHandler"/> writes as it
/// (plain and row-version writes, and a re-file's move-out-of-secure check, the way <c>UpdateRecordActionCore</c> does).
/// </summary>
public class OutputMappingRunIdentityTests
{
    private static readonly Guid Person = Guid.Parse("22570000-0000-4000-8000-00000000a001");
    private static readonly Guid Invoice = Guid.Parse("22570000-0000-4000-8000-00000000d001");
    private static readonly Guid Event = Guid.Parse("22570000-0000-4000-8000-00000000e001");
    private static readonly Guid Matter = Guid.Parse("22570000-0000-4000-8000-00000000b001");

    // ── OutputOrchestratorService: which identity each mapped update runs as ─────────────────────────────────────────

    private readonly Mock<IDataverseUpdateHandler> _handler = new();
    private readonly Guid _playbookId = Guid.NewGuid();

    private OutputOrchestratorService Orchestrator()
    {
        var playbooks = new Mock<IPlaybookService>();
        playbooks.Setup(p => p.GetPlaybookAsync(_playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(new PlaybookResponse
        {
            Id = _playbookId,
            Name = "Finance Invoice Processing",
            ConfigJson = """
                {"outputMapping":{"updates":[{"entityType":"sprk_invoice","recordIdSource":"${context.invoiceId}",
                  "fields":{"sprk_totalamount":{"type":"Money","value":"${extraction.totalAmount}"}}}]}}
                """,
        });
        return new OutputOrchestratorService(playbooks.Object, _handler.Object, NullLogger<OutputOrchestratorService>.Instance);
    }

    private static PlaybookExecutionContext Run(PlaybookRunStartedBy startedBy, Guid? runUserId)
    {
        var context = new PlaybookExecutionContext { StartedBy = startedBy, RunUserId = runUserId };
        context.SetVariable("context.invoiceId", Invoice.ToString());
        context.SetVariable("extraction.totalAmount", "125.50");
        return context;
    }

    private void VerifyWrittenAs(Guid? person) =>
        _handler.Verify(h => h.UpdateAsync(
            "sprk_invoice", Invoice, It.IsAny<Dictionary<string, object?>>(), It.IsAny<ConcurrencyMode>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>(), person), Times.Once);

    [Fact]
    public async Task APersonsRun_MapsItsOutputsAsThatPerson()
    {
        var result = await Orchestrator().ApplyOutputMappingAsync(_playbookId, Run(PlaybookRunStartedBy.Person, Person), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        VerifyWrittenAs(Person);
        _handler.Verify(h => h.UpdateAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<ConcurrencyMode>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>(), null), Times.Never, "a person's run never writes app-only");
    }

    [Fact]
    public async Task ASystemRun_MapsItsOutputsAppOnly()
    {
        var result = await Orchestrator().ApplyOutputMappingAsync(_playbookId, Run(PlaybookRunStartedBy.System, null), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        VerifyWrittenAs(null);
    }

    public static TheoryData<Guid?> UnresolvedUsers() => new() { null, Guid.Empty };

    [Theory]
    [MemberData(nameof(UnresolvedUsers))]
    public async Task APersonsRun_WhoseUserIsUnknown_WritesNothing_AndNeverAppOnly(Guid? runUserId)
    {
        var result = await Orchestrator().ApplyOutputMappingAsync(_playbookId, Run(PlaybookRunStartedBy.Person, runUserId), CancellationToken.None);

        result.Success.Should().BeFalse();
        _handler.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AMappingContextThatSaysNothing_IsAPersonsRun_AndWritesNothingWithoutAUser()
    {
        var context = new PlaybookExecutionContext();
        context.SetVariable("context.invoiceId", Invoice.ToString());

        var result = await Orchestrator().ApplyOutputMappingAsync(_playbookId, context, CancellationToken.None);

        context.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        result.Success.Should().BeFalse();
        _handler.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task APersonsUpdate_ThatDataverseRefuses_IsAFailedUpdateWithTheCode()
    {
        _handler
            .Setup(h => h.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<ConcurrencyMode>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>(), Person))
            .ThrowsAsync(new HttpRequestException("403 (Forbidden)", inner: null, HttpStatusCode.Forbidden));
        var orchestrator = Orchestrator();

        var result = await orchestrator.ApplyOutputMappingAsync(_playbookId, Run(PlaybookRunStartedBy.Person, Person), CancellationToken.None);

        // Fix round 1 (F3): the failed update carries its own code, so a deleted access-denied catch is seen here (its
        // message would then be the raw 403, not the code).
        result.Success.Should().BeFalse();
        var update = result.Updates.Should().ContainSingle("the per-update results are carried on failure").Subject;
        update.Success.Should().BeFalse();
        update.EntityType.Should().Be("sprk_invoice");
        update.ErrorMessage.Should().StartWith(PlaybookRunWriteIdentity.WriteAccessDeniedCode + ":");
        _handler.Invocations.Should().ContainSingle("the refused write is not retried app-only");
    }

    [Fact]
    public async Task ASystemUpdate_ThatDataverseRefuses_IsAFailedUpdate_WithoutThePersonsCode()
    {
        _handler
            .Setup(h => h.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<ConcurrencyMode>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new HttpRequestException("403 (Forbidden)", inner: null, HttpStatusCode.Forbidden));

        var result = await Orchestrator().ApplyOutputMappingAsync(_playbookId, Run(PlaybookRunStartedBy.System, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Updates.Should().ContainSingle().Which.ErrorMessage.Should().NotContain(PlaybookRunWriteIdentity.WriteAccessDeniedCode);
    }

    // ── DataverseUpdateHandler: writes AS the identity it is given ─────────────────────────────────────────────────────

    /// <summary>
    /// The table the handler-level tests map onto. Master merge (ebilling-r1): not <c>sprk_invoice</c>, whose writes the
    /// invoice-family guard reads first and writes update-only (<c>InvoiceFamilyGenericUpdatePathTests</c> pins the identity on
    /// that path); a plain table, so these tests isolate the run identity.
    /// </summary>
    private const string MappedTable = "sprk_analysis";

    private static readonly Guid Mapped = Guid.Parse("22570000-0000-4000-8000-00000000f001");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();
    private readonly RecordOwnershipResolverDouble _ownership = new();

    private DataverseUpdateHandler Handler(IGenericEntityService? rows = null, IImpersonatedCommunicationQuery? readAsPerson = null)
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, rows ?? world.Service, world.Restamper, _ownership,
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld(), shares: null, readAsPerson);
    }

    /// <summary>
    /// The impersonated read (<see cref="IImpersonatedCommunicationQuery"/>): table metadata for <see cref="MappedTable"/>, and the row
    /// AS <see cref="Person"/> only when <paramref name="personCanReadTheRow"/> (Dataverse returns no row the person cannot read).
    /// </summary>
    private static Mock<IImpersonatedCommunicationQuery> ReadAsPerson(bool personCanReadTheRow, long version = 42L)
    {
        static Dictionary<string, JsonElement> Row(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var read = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        read.Setup(q => q.QueryAsync("EntityDefinitions", It.Is<string>(o => o.Contains($"LogicalName eq '{MappedTable}'")), Person,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("""{"EntitySetName":"sprk_analysises","PrimaryIdAttribute":"sprk_analysisid"}""")]);
        read.Setup(q => q.QueryAsync("sprk_analysises", $"$select=versionnumber&$filter=sprk_analysisid eq {Mapped:D}", Person,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(personCanReadTheRow ? [Row($$"""{"versionnumber":{{version}}}""")] : []);
        return read;
    }

    [Fact]
    public async Task TheHandler_WritesAsThePerson()
    {
        await Handler().UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            MappedTable, Mapped, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), Person), Times.Once);
    }

    [Fact]
    public async Task TheHandler_RowVersionWrite_IsAlsoMadeAsThePerson_WithTheVersionReadAsThePerson()
    {
        // Fix round 1 (K3): the versionnumber read is made AS the person too, never app-only.
        var rows = new Mock<IGenericEntityService>();

        await Handler(rows.Object, ReadAsPerson(personCanReadTheRow: true, version: 42L).Object).UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None, Person);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            MappedTable, Mapped, It.Is<Dictionary<string, object?>>(f => Equals(f["versionnumber"], 42L)),
            It.IsAny<CancellationToken>(), Person), Times.Once);
        rows.Verify(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a person's row version is never read app-only");
    }

    [Fact]
    public async Task TheHandler_ARowThePersonCannotRead_LooksExactlyLikeAMissingRow_AndNothingIsWritten()
    {
        // Fix round 1 (K3): an app-only versionnumber read answered for a row the person could not see, so "not found" and
        // "may not write" told a missing row from a hidden one. Read AS the person, a hidden row is no row.
        var rows = new Mock<IGenericEntityService>();
        rows.Setup(r => r.RetrieveAsync(MappedTable, Mapped, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(MappedTable, Mapped) { ["versionnumber"] = 42L }); // the app-only read would see it
        var hidden = () => Handler(rows.Object, ReadAsPerson(personCanReadTheRow: false).Object).UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None, Person);

        var missing = new Mock<IGenericEntityService>();
        missing.Setup(r => r.RetrieveAsync(MappedTable, Mapped, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        var absent = () => Handler(missing.Object).UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None);

        var hiddenMessage = (await hidden.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        var missingMessage = (await absent.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        hiddenMessage.Should().Be(missingMessage, "a row the person cannot read reads exactly as a missing one");
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_WithoutTheImpersonatedRead_WritesNothingForAPerson_AndNeverReadsAppOnly()
    {
        var rows = new Mock<IGenericEntityService>();

        var act = () => Handler(rows.Object).UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None, Person);

        await act.Should().ThrowAsync<InvalidOperationException>();
        rows.Invocations.Should().BeEmpty();
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_WithoutAPerson_StillRefusesTheDenyList_AndWritesNothing()
    {
        var act = () => Handler().UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/systemusers({Person:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_ForAPerson_StillRefusesTheDenyList_AndWritesNothing()
    {
        // Fix round 1 (main-session decision): the mapping is the playbook's configuration, not the person's, so it is asked
        // RefuseAppOnlyWrite whatever identity the write runs as.
        var act = () => Handler().UpdateAsync(
            MappedTable, Mapped, new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/systemusers({Person:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_ForAPerson_LoweringADocumentsAccessPermission_IsRefused_AndNothingIsWritten()
    {
        var act = () => Handler().UpdateAsync(
            "sprk_document", Invoice, new Dictionary<string, object?> { ["sprk_accesspermission"] = InheritedAccessPermission.Standard },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    // ── Must fire (master merge after #2253): a person's mapping is asked #2251's and #2253's sets too ───────────────────
    // WriteIdentity.Caller (#2253) skips AccessTables, SecureRootColumns and AccessColumns for a write that runs AS a person;
    // it is the field-mapping push's only. The output mapping is the playbook's configuration, so the handler asks as
    // WriteIdentity.AppOnly even when it writes AS the runner. Fails if DataverseUpdateHandler passes WriteIdentity.Caller.

    public static TheoryData<string, string, object?, string> AccessSetWrites => new()
    {
        { "sprk_communicationaccount", "sprk_name", "x", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode },
        { "sprk_communication", "sprk_isinternalonly", false, InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode },
        { "sprk_matter", "sprk_containerid", "b!forged", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode },
    };

    [Theory]
    [MemberData(nameof(AccessSetWrites))]
    public async Task TheHandler_ForAPerson_AnAccessTableOrColumn_IsRefused_AndNothingIsWritten(
        string table, string column, object? value, string code)
    {
        var act = () => Handler().UpdateAsync(
            table, Matter, new Dictionary<string, object?> { [column] = value },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written, even AS the person");
    }

    [Theory]
    [MemberData(nameof(AccessSetWrites))]
    public async Task APersonsRun_ItsOutputMappingOntoAnAccessTableOrColumn_FailsThatUpdate_AndNothingIsWritten(
        string table, string column, object? value, string code)
    {
        // End to end: the orchestrator resolves the run's person and hands the write to the real handler AS that person.
        var playbookId = Guid.NewGuid();
        var playbooks = new Mock<IPlaybookService>();
        playbooks.Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(new PlaybookResponse
        {
            Id = playbookId,
            Name = "A configured mapping",
            ConfigJson = JsonSerializer.Serialize(new
            {
                outputMapping = new
                {
                    updates = new[]
                    {
                        new
                        {
                            entityType = table,
                            recordIdSource = "${context.recordId}",
                            fields = new Dictionary<string, object> { [column] = new { type = "String", value = "${run.value}" } },
                        },
                    },
                },
            }),
        });
        var run = new PlaybookExecutionContext { StartedBy = PlaybookRunStartedBy.Person, RunUserId = Person };
        run.SetVariable("context.recordId", Matter.ToString());
        run.SetVariable("run.value", value?.ToString());

        var result = await new OutputOrchestratorService(playbooks.Object, Handler(), NullLogger<OutputOrchestratorService>.Instance)
            .ApplyOutputMappingAsync(playbookId, run, CancellationToken.None);

        result.Success.Should().BeFalse();
        var update = result.Updates.Should().ContainSingle().Subject;
        update.Success.Should().BeFalse();
        update.ErrorMessage.Should().Contain($"({code})", "the deny-list refused it as WriteIdentity.AppOnly, not Dataverse");
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written, even AS the person");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheHandlersReFile_AsksTheMoveOutOfSecureOfThePersonItWritesAs(bool asPerson)
    {
        await Handler().UpdateAsync(
            "sprk_event", Event, new Dictionary<string, object?> { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({Matter:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, asPerson ? Person : null);

        var reparent = _ownership.Reparents.Should().ContainSingle().Subject;
        if (asPerson)
        {
            reparent.CallerSystemUserId.Should().Be(Person);
            reparent.SecureExitCaller.Should().NotBeNull();
            (await reparent.SecureExitCaller!.SystemUserIdAsync(CancellationToken.None)).Should().Be(Person);
        }
        else
        {
            reparent.CallerSystemUserId.Should().BeNull();
            reparent.SecureExitCaller.Should().BeNull("an app-only write acts for no person and is refused such a move");
        }

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_event", Event, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
            asPerson ? Person : null), Times.Once);
    }
}
