using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// GitHub #2091 — a message JOINING a record thread is filed under the thread's record (S6, task 146): it is a re-file writer.
/// <see cref="ThreadResolver"/>'s JOIN and <see cref="CommunicationService"/>'s respond-into-thread run the ONE step after a
/// re-file through <c>ThreadResolver.JoinThreadAsync</c> with <c>RefileThreadStep.PlacedByWriter</c>: the message's mirror, the
/// pass over what is filed under it, and its gate Decision Records (ontology 042b) — every step but the thread reconcile, which
/// the join IS.
/// </summary>
/// <remarks>
/// The join's reparent is the REAL resolver over <see cref="Directory"/> (its owner assignment lands in the share world); the
/// step is the REAL reconciler over the share world, with the REAL gate helper over the same rows
/// (<see cref="GateHelperOverTheShareWorld"/>). The step's thread seam is a mock that counts the thread reconcile.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid JoiningMessage = Guid.Parse("a2091000-0000-4000-8000-0000000000c1");
    private static readonly Guid JoinedThread = Guid.Parse("a2091000-0000-4000-8000-0000000000e1");
    private static readonly Guid TodoOfJoiningMessage = Guid.Parse("a2091000-0000-4000-8000-0000000000d1");
    private static readonly Guid GateRecordOfJoiningMessage = Guid.Parse("a2091000-0000-4000-8000-0000000000f1");
    private static readonly Guid GateParticipant = Guid.Parse("a2091000-0000-4000-8000-0000000000f2");

    /// <summary>The step's own thread seam (SecureChildReconciler's): a join must never reach it.</summary>
    private readonly Mock<IThreadResolver> _stepThreads = new();

    /// <summary>Every thread-lookup stamp the join wrote, with how many isolation reads the share world had answered by then.</summary>
    private readonly List<(Dictionary<string, object> Fields, int IsolationReadsBefore)> _threadStamps = new();

    // ── must fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091: a message JOINING a secure matter's thread is re-owned to the Secure team, mirrored, isolates what is filed under it and runs the gate Decision Record step — never the thread reconcile")]
    public async Task ThreadJoin_IntoASecureMattersThread_RunsEveryStepAfterARefile_ButTheThreadReconcile()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(SecureMatter);

        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().Be(ThreadAssignment.Anchored(JoinedThread, "sprk_matter", SecureMatter));
        AssertTheJoinRanTheStepIntoSecure(gateOrg, gateLog);
    }

    [Fact(DisplayName = "#2091: respond-into-thread (CommunicationService) into a secure matter's thread runs every step after a re-file but the thread reconcile")]
    public async Task RespondIntoThread_IntoASecureMattersThread_RunsEveryStepAfterARefile_ButTheThreadReconcile()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(SecureMatter);

        var response = await RespondingCommunications(StepScopes()).SendAsync(
            new SendCommunicationRequest
            {
                To = new[] { "recipient@contoso.com" },
                Subject = "Re: filing",
                Body = "hello there",
                BodyFormat = BodyFormat.HTML,
                CommunicationType = CommunicationType.Email,
                ThreadId = JoinedThread,
            },
            httpContext: null,
            CancellationToken.None);

        response.CommunicationId.Should().Be(JoiningMessage);
        AssertTheJoinRanTheStepIntoSecure(gateOrg, gateLog);
    }

    [Fact(DisplayName = "#2091 (a): a thread join that files the message under an ordinary record still re-owns its gate Decision Record (042b) though the thread reconcile is skipped")]
    public async Task ThreadJoin_IntoAnOrdinaryMattersThread_StillReownsTheGateDecisionRecord_D169()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(OrdinaryMatter);

        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().Be(ThreadAssignment.Anchored(JoinedThread, "sprk_matter", OrdinaryMatter));
        _threadStamps.Should().ContainSingle();
        ShareWorld.OwnerOf("sprk_decisionrecord", GateRecordOfJoiningMessage).Should().Be(
            DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam),
            "the step's gate Decision Record re-own ran after the join (only it re-owns a user-owned gate record to an ordinary team)");
        _shareTable.MaskOf("sprk_decisionrecord", GateRecordOfJoiningMessage, DataversePrincipalRef.User(GateParticipant)).Should().BeNull(
            "its participant share is revoked once the team owner reads back");
        gateOrg.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.AssignRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        gateLog.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        _stepThreads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the join IS the message's thread placement");
    }

    // ── failure ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091 (c): a gate Decision Record re-own that faults after a thread join logs FilingReownFailed (50317) and never fails the join")]
    public async Task ThreadJoin_AGateDecisionRecordReownFault_LogsFilingReownFailed_AndTheJoinStands()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        gateOrg.Setup(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.AssignRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: Dataverse refused the Assign."));
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(OrdinaryMatter);

        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().Be(ThreadAssignment.Anchored(JoinedThread, "sprk_matter", OrdinaryMatter),
            "the join stands: the step never throws");
        _threadStamps.Should().ContainSingle();
        gateLog.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == 50317
                                              && e.EventId == DecisionRecordEvents.FilingReownFailed);
        ShareWorld.OwnerOf("sprk_decisionrecord", GateRecordOfJoiningMessage).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "never orphaned: the user owner is kept");
    }

    [Fact(DisplayName = "#2091: a thread join whose isolation read faults on the read and its retry still writes, and the step runs with the faulted answer (it releases nothing)")]
    public async Task ThreadJoin_AnIsolationReadThatFaultsTwice_StillJoins_AndTheStepRuns()
    {
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(SecureMatter);
        var faults = 0;
        ShareWorld.QueryFault = () =>
        {
            if (++faults == 2)
                ShareWorld.ClearQueryFaults();
            return new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        };
        ShareWorld.FailingQueriesOf("sprk_communication");

        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().NotBeNull("an HttpClient timeout on the read is not the caller cancelling, and refuses nothing");
        faults.Should().Be(2, "the read, then its one retry — both BEFORE the stamp");
        _threadStamps.Should().ContainSingle();
        ShareWorld.OwnerOf("sprk_communication", JoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        ShareWorld.OwnerOf("sprk_todo", TodoOfJoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
            "the step ran: a move INTO isolation needs no answer about before");
    }

    // ── must not fire ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091: a join of a Direct thread (no record) files nothing: no isolation read, no scope, no step")]
    public async Task ThreadJoin_OfADirectThread_RunsNoStep()
    {
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(regardingMatter: null);

        var assignment = await JoiningResolver(new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object)
            .ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().Be(ThreadAssignment.Direct(JoinedThread), "a strict scope factory: opening a scope would fail the join");
        _threadStamps.Should().ContainSingle();
        ShareWorld.QueriedTables.Should().BeEmpty("no isolation read and no step");
        _world.Assignments.Should().BeEmpty();
        _stepThreads.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2091: a refused thread join writes nothing, reads nothing and runs no step")]
    public async Task ThreadJoin_Refused_WritesReadsAndRunsNothing()
    {
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(regardingMatter: null, regardingProject: FlaggedProject);

        // The host's real scopes: an isolation read made before the resolver decides would land in the share world.
        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().BeNull("a refusal leaves the message unthreaded (NFR-02)");
        _threadStamps.Should().BeEmpty();
        ShareWorld.QueriedTables.Should().BeEmpty("the isolation read runs only inside the write a refusal never reaches");
        _world.Assignments.Should().BeEmpty();
        _stepThreads.Invocations.Should().BeEmpty();
    }

    // ── #2110: the filing reconcile's own join — the threadless email whose conversation key finds a record thread ─────────

    private static readonly Guid StaleSharee = Guid.Parse("a2110000-0000-4000-8000-0000000000b2");

    [Fact(DisplayName = "#2110 (UAC F1): in the step after a re-file, a threadless email joining a SECURE matter's thread is mirrored after the join — the matter's sharee sees it, no stale share stays, the gate step runs once")]
    public async Task FilingReconcile_InTheStep_AThreadlessEmailJoiningASecureMattersThread_IsMirroredOnThePostJoinState_2110()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(SecureMatter);
        AThreadlessEmail();
        // A share the ordinary message carried (a #1629 grant to the ordinary matter's members): not the secure matter's mirror.
        _shareTable.Seed("sprk_communication", JoiningMessage, DataversePrincipalRef.User(StaleSharee), CollaborateMask);

        // The step every re-file writer runs, its thread reconcile the REAL resolver. A strict scope factory: a join that ran a
        // step of its own would open a scope, and the join would fail.
        await Children(JoiningResolver(new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object)).AfterRefileAsync(
            "sprk_communication", JoiningMessage, () => Task.FromResult(false), CancellationToken.None);

        _threadStamps.Should().ContainSingle("the reconcile joined the thread its conversation key found, running no step of its own");
        _world.Assignments.Should().Equal(("sprk_communication", JoiningMessage, Directory.SecureNamedTeam));
        ShareWorld.OwnerOf("sprk_communication", JoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the mirror ran AFTER the join, on the Secure owner the join gave the email");
        _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(StaleSharee)).Should().BeNull(
            "a Secure-team-owned email carries only its mirror: the ordinary share is revoked now, not by the two-minute pass");
        ShareWorld.OwnerOf("sprk_todo", TodoOfJoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        ShareWorld.OwnerOf("sprk_decisionrecord", GateRecordOfJoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        GateStepRuns(gateLog).Should().Be(1, "the gate Decision Record step runs once per re-file, after the reconcile and the mirror");
        gateOrg.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.AssignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "#2110 (ontology K2): the sweep's standalone reconcile — a threadless email joining a thread under a record it is NOT filed to runs the step once for the join")]
    public async Task SweepReconcile_AThreadlessEmailJoiningAnotherRecordsThread_RunsTheStepOnce_2110()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(SecureMatter);
        AThreadlessEmail();

        await JoiningResolver(StepScopes()).ReconcileThreadForFilingAsync(
            JoiningMessage, FilingReconcileHost.Standalone, CancellationToken.None);

        AssertTheJoinRanTheStepIntoSecure(gateOrg, gateLog);
        GateStepRuns(gateLog).Should().Be(1, "one move, one step");
    }

    [Fact(DisplayName = "#2110 (ontology K2): the sweep's standalone reconcile — a threadless email joining the thread of the record it IS filed to adds no parent and runs no step")]
    public async Task SweepReconcile_AThreadlessEmailJoiningTheThreadOfItsOwnRecord_RunsNoStep_2110()
    {
        var (gate, gateOrg, gateLog) = GateHelperOverTheShareWorld();
        _gateOverShareWorld = gate;
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(OrdinaryMatter);
        AThreadlessEmail();

        // A strict scope factory: a step would open a scope, and the join would fail.
        await JoiningResolver(new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object).ReconcileThreadForFilingAsync(
            JoiningMessage, FilingReconcileHost.Standalone, CancellationToken.None);

        _threadStamps.Should().ContainSingle("the email joined the thread");
        ShareWorld.OwnerOf("sprk_decisionrecord", GateRecordOfJoiningMessage).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "no step ran: the gate record is not re-owned by this join (the filing the sweep follows ran it)");
        GateStepRuns(gateLog).Should().Be(0);
        gateOrg.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.AssignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _stepThreads.Invocations.Should().BeEmpty();
    }

    private static readonly Guid GrantSharee = Guid.Parse("a2110000-0000-4000-8000-0000000000b3");

    [Fact(DisplayName = "#2110 (UAC F1): a message that LEFT isolation loses its old mirror BEFORE the thread reconcile, so the reconcile's #1629 read grant to its new record survives")]
    public async Task Refile_OutOfIsolation_TheOldMirrorComesOffBeforeTheThreadReconcile_AndItsGrantSurvives_2110()
    {
        // The message is ordinary now (re-filed out of the secure matter), still carrying the secure matter's mirror share.
        ShareWorld.OrdinaryChild("sprk_communication", JoiningMessage, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        _shareTable.Seed("sprk_communication", JoiningMessage, DataversePrincipalRef.User(Sharee), CollaborateMask);
        int? oldMirrorAtTheReconcile = -1;
        var threads = new Mock<IThreadResolver>();
        threads
            .Setup(t => t.ReconcileThreadForFilingAsync(JoiningMessage, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                oldMirrorAtTheReconcile = _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(Sharee));
                // The reconcile's #1629 grant to a member of the record the email now sits under.
                _shareTable.Seed("sprk_communication", JoiningMessage, DataversePrincipalRef.User(GrantSharee), 1);
            })
            .Returns(Task.CompletedTask);

        await Children(threads.Object).AfterRefileAsync(
            "sprk_communication", JoiningMessage, () => Task.FromResult(true), CancellationToken.None);

        threads.Verify(t => t.ReconcileThreadForFilingAsync(JoiningMessage, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once);
        oldMirrorAtTheReconcile.Should().BeNull("the old mirror came off before the reconcile ran");
        _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(Sharee)).Should().BeNull();
        _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(GrantSharee)).Should().Be(1,
            "the mirror removal (which revokes every direct share) ran before the grant, never after it");
    }

    [Theory(DisplayName = "#2110 (ontology K3): the ONE re-file core re-binding a communication's held matter runs the step once only when it also MOVES the thread")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefileCore_TheHeldMatterAndAThread_RunsTheStepOnlyWhenTheThreadMoves_2110(bool movesTheThread)
    {
        var heldThread = Guid.Parse("a2110000-0000-4000-8000-0000000000e5");
        var otherThread = Guid.Parse("a2110000-0000-4000-8000-0000000000e6");
        // #2143 round 6: the resolver reads both threads (RecordThreadFiling); Direct threads, so they file the message under nothing.
        _world.WithRecord("sprk_communicationthread", heldThread, Directory.ChildBu, extra: new() { ["sprk_threadtype"] = new OptionSetValue(100000001) }) // #2143 r7d: Direct
            .WithRecord("sprk_communicationthread", otherThread, Directory.ChildBu, extra: new() { ["sprk_threadtype"] = new OptionSetValue(100000001) }); // #2143 r7d: Direct
        _world.WithRecord("sprk_communication", JoiningMessage, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", heldThread),
        });
        ShareWorld.OrdinaryChild("sprk_communication", JoiningMessage, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        var threads = new Mock<IThreadResolver>();

        // The core every child re-file route calls (ChildRecordEndpoints.UpdateAsync), with no filing-only shape: no route of
        // today names a communication's thread, so this pins the core for any that will.
        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", JoiningMessage,
            Payload(new()
            {
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                ["sprk_CommunicationThread@odata.bind"] = $"/sprk_communicationthreads({(movesTheThread ? otherThread : heldThread):D})",
            }),
            _user, _world.Resolver(), Restamper(), Children(threads.Object), HttpContextOfCaller(), NullLogger<Program>.Instance,
            CancellationToken.None);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        _world.Assignments.Should().BeEmpty("the thread decides no owner, and the matter is the one held");
        threads.Verify(t => t.ReconcileThreadForFilingAsync(JoiningMessage, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()),
            movesTheThread ? Times.Once() : Times.Never(),
            movesTheThread ? "the thread moved: the step after a re-file ran once" : "nothing moved: no step");
    }

    /// <summary>How many times the gate Decision Record step ran for the joining message's gate record (its one log line per run).</summary>
    private static int GateStepRuns(Sprk.Bff.Api.Tests.Services.Communication.CapturingLogger<GateDecisionRecordSharing> gateLog) =>
        gateLog.Entries.Count(e => e.Message.Contains($"Gate Decision Record {GateRecordOfJoiningMessage}", StringComparison.Ordinal));

    /// <summary>The joining message as the filing reconcile reads it: an email filed under the ordinary matter, with no thread.</summary>
    private void AThreadlessEmail() =>
        _joiningEmailRow = new Entity("sprk_communication", JoiningMessage)
        {
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["sprk_subject"] = "Re: filing",
        };

    private Entity? _joiningEmailRow;

    // ── assertions ────────────────────────────────────────────────────────────────────────────────────────────────

    private void AssertTheJoinRanTheStepIntoSecure(
        Mock<Microsoft.PowerPlatform.Dataverse.Client.IOrganizationServiceAsync2> gateOrg,
        Sprk.Bff.Api.Tests.Services.Communication.CapturingLogger<GateDecisionRecordSharing> gateLog)
    {
        var stamp = _threadStamps.Should().ContainSingle("one write: the join's thread lookup").Subject;
        stamp.IsolationReadsBefore.Should().BeGreaterThan(0, "whether the message was isolated is read BEFORE the stamp");
        _world.Assignments.Should().Equal(("sprk_communication", JoiningMessage, Directory.SecureNamedTeam));
        ShareWorld.OwnerOf("sprk_communication", JoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));

        // (1) the message's own mirror, (2) the pass over what is filed under it.
        _shareTable.MaskOf("sprk_communication", JoiningMessage, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the secure matter's sharee sees the message at once — the step's mirror ran for it");
        ShareWorld.OwnerOf("sprk_todo", TodoOfJoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
            "the step's pass moved the to-do filed under the now-isolated message into isolation");
        _shareTable.MaskOf("sprk_todo", TodoOfJoiningMessage, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);

        // (3) ontology 042b: the gate record follows the message into isolation, and the gate step saw it and left it to the engine.
        ShareWorld.OwnerOf("sprk_decisionrecord", GateRecordOfJoiningMessage).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_decisionrecord", GateRecordOfJoiningMessage, DataversePrincipalRef.User(GateParticipant)).Should().BeNull(
            "a participant who is not a sharee of the secure matter keeps no access to the gate record");
        gateLog.Entries.Should().Contain(e => e.Level == LogLevel.Information
                                              && e.Message.Contains($"Gate Decision Record {GateRecordOfJoiningMessage}", StringComparison.Ordinal)
                                              && e.Message.Contains("left as they are", StringComparison.Ordinal),
            "the gate Decision Record step ran after the join, asked the real resolver and left the Secure record to the engine");
        gateOrg.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Crm.Sdk.Messages.AssignRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        _stepThreads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the join IS the message's thread placement: the step's thread reconcile does not run (it could move the message back out)");
    }

    // ── worlds ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An ordinary message filed under the ordinary matter (a to-do under it, a user-owned gate Decision Record with a participant
    /// share), and the thread it joins — anchored to <paramref name="regardingMatter"/>, to <paramref name="regardingProject"/>,
    /// or to nothing (a Direct thread).
    /// </summary>
    private void AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(Guid? regardingMatter, Guid? regardingProject = null)
    {
        _world.WithRecord("sprk_communication", JoiningMessage, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
        });
        ShareWorld.OrdinaryChild("sprk_communication", JoiningMessage, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        ShareWorld.OrdinaryChild("sprk_todo", TodoOfJoiningMessage, ("sprk_regardingcommunication", "sprk_communication", JoiningMessage));
        ShareWorldGateRecord(GateRecordOfJoiningMessage, JoiningMessage);
        _shareTable.Seed("sprk_decisionrecord", GateRecordOfJoiningMessage, DataversePrincipalRef.User(GateParticipant), 1);

        _joinedThreadRow = new Entity("sprk_communicationthread", JoinedThread)
        {
            ["sprk_threadtype"] = new OptionSetValue(regardingMatter is null && regardingProject is null ? 100000001 : 100000000),
        };
        if (regardingMatter is { } matter)
        {
            _joinedThreadRow["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
            ShareWorld.Add("sprk_communicationthread", JoinedThread,
                ("sprk_threadtype", new OptionSetValue(100000000)), // #2143 r7d: a record thread
                ("owningteam", new EntityReference("team", matter == SecureMatter ? Directory.SecureNamedTeam : SecureChildShareWorld.GeneralTeam)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", matter)));
        }

        if (regardingProject is { } project)
            _joinedThreadRow["sprk_regardingproject"] = new EntityReference("sprk_project", project);

        // #2143 round 6: the resolver reads the thread a join names (RecordThreadFiling) from its own Dataverse — the same row.
        var threadColumns = new Dictionary<string, object>
        {
            // #2143 r7d: the thread's type, as the join reads it.
            ["sprk_threadtype"] = new OptionSetValue(regardingMatter is null && regardingProject is null ? 100000001 : 100000000),
        };
        if (regardingMatter is { } threadMatter)
            threadColumns["sprk_regardingmatter"] = new EntityReference("sprk_matter", threadMatter);
        if (regardingProject is { } threadProject)
            threadColumns["sprk_regardingproject"] = new EntityReference("sprk_project", threadProject);
        _world.WithRecord("sprk_communicationthread", JoinedThread, Directory.ChildBu, extra: threadColumns);
    }

    private Entity? _joinedThreadRow;

    /// <summary>The join's own Dataverse: the thread it reads, and the message's thread-lookup stamp (landing in both worlds).</summary>
    private Mock<IGenericEntityService> JoinRows()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", JoinedThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _joinedThreadRow!);
        // #2110: the email the filing reconcile reads (unset → not found, as before).
        entities
            .Setup(e => e.RetrieveAsync("sprk_communication", JoiningMessage, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _joiningEmailRow!);
        entities
            .Setup(e => e.CreateAsync(It.Is<Entity>(r => r.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JoiningMessage);
        entities
            .Setup(e => e.UpdateAsync("sprk_communication", JoiningMessage, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                if (!fields.TryGetValue(ThreadResolver.ThreadLookupOnCommunication, out var value) || value is not EntityReference thread)
                    return;

                _threadStamps.Add((fields, ShareWorld.QueriedTables.Count(t => t == "sprk_communication")));
                ShareWorld.Set("sprk_communication", JoiningMessage, ThreadResolver.ThreadLookupOnCommunication, thread);
                _world.Row("sprk_communication", JoiningMessage)[ThreadResolver.ThreadLookupOnCommunication] = thread;
            })
            .Returns(Task.CompletedTask);
        return entities;
    }

    /// <summary>The host's scope factory: its scope resolves the step's reconciler (scoped), with scope validation on.</summary>
    private IServiceScopeFactory StepScopes() =>
        new ServiceCollection()
            .AddScoped(_ => Children(_stepThreads.Object))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>A thread resolver whose channel key JOINS <see cref="JoinedThread"/>, over the REAL resolver.</summary>
    private ThreadResolver JoiningResolver(IServiceScopeFactory scopes)
    {
        var strategy = new Mock<IThreadKeyStrategy>();
        strategy.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        strategy
            .Setup(s => s.ResolveAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadKeyResolution(JoinedThread, CreateWhenAbsent: false));
        return new ThreadResolver(
            new[] { strategy.Object }, JoinRows().Object, _world.Resolver(), NullLogger<ThreadResolver>.Instance, scopeFactory: scopes);
    }

    private static ThreadResolutionRequest JoinRequest() => new()
    {
        CommunicationId = JoiningMessage,
        ChannelType = CommunicationType.Email,
        Direction = CommunicationDirection.Incoming,
        Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Re: filing" },
    };

    /// <summary>The outbound email send (shared mailbox), whose "respond into the current thread" stamps <see cref="JoinedThread"/>.</summary>
    private CommunicationService RespondingCommunications(IServiceScopeFactory scopes)
    {
        const string mailbox = "noreply@contoso.com";
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[] { new ApprovedSenderConfig { Email = mailbox, DisplayName = "Contoso", IsDefault = true } },
            DefaultMailbox = mailbox,
        };
        var validator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), null!, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { new SentEmail() },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });
        return new CommunicationService(
            dispatcher, validator, Mock.Of<ICommunicationDataverseService>(), JoinRows().Object, Mock.Of<IDocumentDataverseService>(),
            null!, null!, Mock.Of<ICommunicationEnrichmentService>(), Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(), _world.Resolver(), Mock.Of<ILogger<CommunicationService>>(),
            threadResolver: new Mock<IThreadResolver>(MockBehavior.Strict).Object, scopeFactory: scopes);
    }

    /// <summary>Graph's send, answering a provider message id.</summary>
    private sealed class SentEmail : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress, ProviderMessageId = "msg-2091@example.com" });
    }
}
