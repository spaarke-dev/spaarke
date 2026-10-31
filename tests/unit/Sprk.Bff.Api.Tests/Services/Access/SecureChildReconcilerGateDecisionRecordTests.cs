using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T>
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Access;

/// <summary>
/// Ontology task 042b round 2 (D-169, uac-r2 choice A): <see cref="SecureChildReconciler.AfterRefileAsync"/>, the one step every re-file writer
/// runs, re-owns a re-filed communication's user-owned gate Decision Records after UAC's own steps — on every communication re-file, never on
/// another table, never thrown. The reconciler's own steps run over loose doubles (it never throws by contract); the gate records live in a
/// <see cref="GateSharingWorld"/>.
/// </summary>
public sealed class SecureChildReconcilerGateDecisionRecordTests
{
    private static readonly Guid CommunicationId = Guid.Parse("c0c0c0c0-0000-4000-8000-0000000000c2");
    private static readonly Guid RecordId = Guid.Parse("d0d0d0d0-0000-4000-8000-0000000000c2");
    private static readonly Guid Owner = Guid.Parse("a0000000-0000-4000-8000-0000000000c0");
    private static readonly Guid Sharee = Guid.Parse("a0000000-0000-4000-8000-0000000000c1");
    private static readonly Guid MatterTeam = Guid.Parse("e0000000-0000-4000-8000-0000000000c3");

    private readonly Mock<IThreadResolver> _threads = new();
    private readonly CapturingLogger<SecureChildReconciler> _log = new();

    private SecureChildReconciler Reconciler(GateDecisionRecordSharing gate, Mock<IGenericEntityService>? uacEntities = null)
    {
        var entities = uacEntities ?? new Mock<IGenericEntityService>();
        var configuration = new ConfigurationBuilder().Build();
        return new SecureChildReconciler(
            entities.Object, Mock.Of<IRecordOwnershipResolver>(),
            new SecureChildShareSynchronizer(entities.Object, Mock.Of<IDataverseRecordShareService>(), null!, configuration,
                NullLogger<SecureChildShareSynchronizer>.Instance),
            null!, configuration, _log, gate, threads: _threads.Object);
    }

    /// <summary>The re-filed communication's user-owned gate record, shared with a participant, and the resolver's ordinary answer.</summary>
    private static GateSharingWorld SharedGateRecord()
    {
        var gate = new GateSharingWorld()
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Record, RecordId, Sharee);
        gate.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));
        return gate;
    }

    private static Task<bool> NotIsolated() => Task.FromResult(false);

    [Fact]
    public async Task AfterRefile_OfACommunication_ReownsItsGateDecisionRecord_AfterTheReconcilersOwnSteps_AndRevokesItsShares()
    {
        var gate = SharedGateRecord();
        _threads.Setup(t => t.ReconcileThreadForFilingAsync(CommunicationId, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => gate.Calls.Add("thread-reconcile"))
            .Returns(Task.CompletedTask);

        await Reconciler(gate.Sharing).AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        gate.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        gate.RecordSharees(RecordId).Should().BeEmpty();
        gate.Calls.IndexOf("thread-reconcile").Should().BeGreaterThanOrEqualTo(0)
            .And.BeLessThan(gate.Calls.FindIndex(c => c.StartsWith("assign:", StringComparison.Ordinal)), "after UAC's own steps");
    }

    [Fact]
    public async Task AfterRefile_OfACommunication_RunsTheThreadReconcileFirst_ThenTheMirror_ThenTheGateStepOnce_2110()
    {
        // #2110 (UAC F1): the thread reconcile runs BEFORE the row's own mirror (a join it makes can re-own the email into a
        // Secure record's isolation, so the mirror must see it), and the gate Decision Record step runs ONCE, after both.
        var gate = SharedGateRecord();
        _threads.Setup(t => t.ReconcileThreadForFilingAsync(CommunicationId, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => gate.Calls.Add("thread-reconcile"))
            .Returns(Task.CompletedTask);
        var uac = new Mock<IGenericEntityService>();
        uac.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((query, _) => gate.Calls.Add($"uac-query:{query.EntityName}"))
            .ReturnsAsync(new EntityCollection());

        await Reconciler(gate.Sharing, uac).AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        var reconcile = gate.Calls.IndexOf("thread-reconcile");
        var firstUacRead = gate.Calls.FindIndex(c => c.StartsWith("uac-query:", StringComparison.Ordinal));
        var lastUacRead = gate.Calls.FindLastIndex(c => c.StartsWith("uac-query:", StringComparison.Ordinal));
        var assign = gate.Calls.FindIndex(c => c.StartsWith("assign:", StringComparison.Ordinal));
        gate.Calls.Count(c => c == "thread-reconcile").Should().Be(1);
        reconcile.Should().BeGreaterThanOrEqualTo(0);
        firstUacRead.Should().BeGreaterThan(reconcile, "the mirror (its Secure team read first) runs after the thread reconcile");
        assign.Should().BeGreaterThan(lastUacRead, "the gate step runs after the mirror and the pass");
        gate.Calls.Count(c => c.StartsWith("assign:", StringComparison.Ordinal)).Should().Be(1, "the gate step runs once per re-file");
    }

    [Fact]
    public async Task AfterRefile_OfACommunication_WhenTheThreadReconcileThrows_StillMirrorsAndReownsTheGateDecisionRecord_2110()
    {
        // #2110 (ontology re-check K3): the thread reconcile runs first, so a fault in it must not skip the mirror, the pass or
        // the gate Decision Record step after it, and the re-file never fails.
        var gate = SharedGateRecord();
        _threads.Setup(t => t.ReconcileThreadForFilingAsync(CommunicationId, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => gate.Calls.Add("thread-reconcile"))
            .ThrowsAsync(new InvalidOperationException("Test: the thread reconcile faulted."));
        var uac = new Mock<IGenericEntityService>();
        uac.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((query, _) => gate.Calls.Add($"uac-query:{query.EntityName}"))
            .ReturnsAsync(new EntityCollection());

        var refile = async () => await Reconciler(gate.Sharing, uac).AfterRefileAsync(
            "sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        await refile.Should().NotThrowAsync();
        var reconcile = gate.Calls.IndexOf("thread-reconcile");
        reconcile.Should().BeGreaterThanOrEqualTo(0);
        gate.Calls.FindIndex(c => c.StartsWith("uac-query:", StringComparison.Ordinal))
            .Should().BeGreaterThan(reconcile, "the mirror still runs after the faulted reconcile");
        gate.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam")!.Id.Should().Be(MatterTeam);
        gate.RecordSharees(RecordId).Should().BeEmpty();
    }

    [Fact]
    public async Task AfterRefile_PlacedByWriter_SkipsOnlyTheThreadReconcile_AndStillReownsTheGateDecisionRecord_2091()
    {
        // #2091 (a): a thread join IS the communication's thread placement, so it skips the step's thread reconcile — and every
        // other step runs, the gate Decision Record re-own included.
        var gate = SharedGateRecord();

        await Reconciler(gate.Sharing).AfterRefileAsync(
            "sprk_communication", CommunicationId, NotIsolated, RefileThreadStep.PlacedByWriter, CancellationToken.None);

        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never);
        gate.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        gate.RecordSharees(RecordId).Should().BeEmpty();
    }

    [Fact]
    public async Task AfterRefile_PlacedByWriter_AGateReownFault_LogsFilingReownFailed50317_AndNeverThrows_2091()
    {
        // #2091 (c): with the thread reconcile skipped, a failure of the gate Decision Record step is still logged as
        // FilingReownFailed (event id 50317) and never fails the re-file.
        var gate = SharedGateRecord();
        gate.AssignLands = false;
        gate.AssignFault = new InvalidOperationException("Test: Dataverse refused the Assign.");

        var act = () => Reconciler(gate.Sharing).AfterRefileAsync(
            "sprk_communication", CommunicationId, NotIsolated, RefileThreadStep.PlacedByWriter, CancellationToken.None);

        await act.Should().NotThrowAsync("the re-file it follows has landed");
        gate.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == 50317
                                               && e.EventId == DecisionRecordEvents.FilingReownFailed);
        gate.RecordSharees(RecordId).Should().Equal(new[] { Sharee }, "the shares stay until a team owner reads back");
    }

    [Fact]
    public async Task AfterRefile_Reconcile_RunsTheThreadReconcileOnce_2091()
    {
        // Must-fire control: every other writer (the four-argument overload) has the communication's thread follow the filing.
        var gate = SharedGateRecord();

        await Reconciler(gate.Sharing).AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        _threads.Verify(t => t.ReconcileThreadForFilingAsync(CommunicationId, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_todo")]
    public async Task AfterRefile_OfAnotherTable_NeverCallsTheGateHelper(string table)
    {
        var gate = SharedGateRecord();

        await Reconciler(gate.Sharing).AfterRefileAsync(table, CommunicationId, NotIsolated, CancellationToken.None);

        gate.Entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
        gate.Calls.Should().BeEmpty();
        gate.RecordSharees(RecordId).Should().Equal(Sharee);
    }

    [Fact]
    public async Task AfterRefile_AGateHelperFault_IsLoggedAtError_AndNeverThrown()
    {
        var act = () => Reconciler(GateSharingWorld.Faulting())
            .AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        await act.Should().NotThrowAsync("the re-file it follows has landed");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("gate Decision Records were not re-owned")
            && e.Message.Contains(CommunicationId.ToString()));
    }

    [Fact]
    public async Task AfterRefile_IntoASecureRecord_TheGateHelperAssignsAndRevokesNothing()
    {
        var gate = SharedGateRecord();
        gate.OwnerAnswers(RecordOwnerResolution.Owned(Guid.NewGuid()) with { IsSecureOwner = true });

        await Reconciler(gate.Sharing).AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        gate.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal) || c.StartsWith("revoke:", StringComparison.Ordinal));
        gate.RecordSharees(RecordId).Should().Equal(Sharee);
    }

    [Fact]
    public async Task AfterRefile_Twice_ReownsOnce()
    {
        var gate = SharedGateRecord();
        var reconciler = Reconciler(gate.Sharing);

        await reconciler.AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);
        await reconciler.AfterRefileAsync("sprk_communication", CommunicationId, NotIsolated, CancellationToken.None);

        gate.Calls.Count(c => c.StartsWith("assign:", StringComparison.Ordinal)).Should().Be(1);
        gate.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
    }
}
