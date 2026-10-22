using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// #2257 (owner round 123, "run as the user"): a playbook's output mapping writes AS the person whose run it is, as the Update
/// Record node does; only a run with no person in it (<see cref="PlaybookRunStartedBy.System"/>) writes app-only, behind
/// <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>. A person's run whose user is unknown writes nothing. Two layers:
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

        result.Success.Should().BeFalse();
        _handler.Invocations.Should().ContainSingle("the refused write is not retried app-only");
    }

    // ── DataverseUpdateHandler: writes AS the identity it is given ─────────────────────────────────────────────────────

    private readonly Mock<IFieldMappingDataverseService> _writes = new();
    private readonly RecordOwnershipResolverDouble _ownership = new();

    private DataverseUpdateHandler Handler(IGenericEntityService? rows = null)
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, rows ?? world.Service, world.Restamper, _ownership,
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }

    [Fact]
    public async Task TheHandler_WritesAsThePerson()
    {
        await Handler().UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_invoice", Invoice, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), Person), Times.Once);
    }

    [Fact]
    public async Task TheHandler_RowVersionWrite_IsAlsoMadeAsThePerson()
    {
        var rows = new Mock<IGenericEntityService>();
        rows.Setup(r => r.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Invoice) { ["versionnumber"] = 42L });

        await Handler(rows.Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_aisummary"] = "x" },
            ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None, Person);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_invoice", Invoice, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("versionnumber")),
            It.IsAny<CancellationToken>(), Person), Times.Once);
    }

    [Fact]
    public async Task TheHandler_WithoutAPerson_StillRefusesTheDenyList_AndWritesNothing()
    {
        var act = () => Handler().UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/systemusers({Person:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_ForAPerson_LeavesTheDecisionToTheirOwnPrivileges_AsTheUpdateRecordCoreDoes()
    {
        await Handler().UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/systemusers({Person:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Person);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_invoice", Invoice, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("ownerid@odata.bind")),
            It.IsAny<CancellationToken>(), Person), Times.Once);
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
