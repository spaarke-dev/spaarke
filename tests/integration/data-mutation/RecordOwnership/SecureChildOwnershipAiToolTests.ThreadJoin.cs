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
        _stepThreads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
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

        _stepThreads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
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
                ("owningteam", new EntityReference("team", matter == SecureMatter ? Directory.SecureNamedTeam : SecureChildShareWorld.GeneralTeam)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", matter)));
        }

        if (regardingProject is { } project)
            _joinedThreadRow["sprk_regardingproject"] = new EntityReference("sprk_project", project);
    }

    private Entity? _joinedThreadRow;

    /// <summary>The join's own Dataverse: the thread it reads, and the message's thread-lookup stamp (landing in both worlds).</summary>
    private Mock<IGenericEntityService> JoinRows()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", JoinedThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _joinedThreadRow!);
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
