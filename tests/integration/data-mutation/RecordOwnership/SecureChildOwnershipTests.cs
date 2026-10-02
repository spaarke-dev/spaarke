using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 (C10 half 2, write-path invariant I-2): every server writer of a child record
/// decides its owner through the ONE <see cref="RecordOwnershipResolver"/> — the named Secure owner team when any
/// parent is secure, the parent's business-unit default team otherwise — and refuses, writing nothing, when none
/// resolves.
/// </summary>
/// <remarks>
/// <para>Unlike the writer unit tests (which double the resolver at its module boundary), these drive the REAL resolver
/// over the in-memory <see cref="OwnershipDirectory"/> — business units, the Secure BU's named team beside its default
/// team (a decoy that must never be chosen), and the parent rows — through the real writer of each family. The
/// writer's own Dataverse writes are captured at <see cref="IGenericEntityService"/> /
/// <see cref="IFieldMappingDataverseService"/>.</para>
/// <para>Families: a generic re-file (<see cref="DataverseUpdateHandler"/> — documents, both project lookups, in and
/// out of secure); an AI task create (<see cref="TaskActionCore"/>); a record thread (<see cref="ThreadResolver"/>); a
/// communication's content rows (<see cref="CommunicationParticipantIndexer"/>); the inbound-email hold
/// (<see cref="IncomingCommunicationJobHandler"/>, owner amendment R3).</para>
/// </remarks>
[Trait("status", "new")]
public class SecureChildOwnershipTests
{
    private static readonly Guid SecureProject = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrdinaryProject = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecureMatter = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OrdinaryMatter = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>A root flagged sprk_issecure but still owned in an ordinary BU — an interrupted provisioning (C11).</summary>
    private static readonly Guid FlaggedNotIsolatedProject = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static Directory World() => Directory.Standard()
        .WithSecureRoot("sprk_project", SecureProject)
        .WithOrdinaryRoot("sprk_project", OrdinaryProject)
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedNotIsolatedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam);

    // =====================================================================================
    // Re-file (reparent) — DataverseUpdateHandler, the generic update path
    // =====================================================================================

    [Fact]
    public async Task Refile_DocumentWithAnOrdinaryProject_GainingASecureRelatedProject_IsReassignedToTheNamedSecureTeam()
    {
        // Secure-if-any, the both-project-lookups case: sprk_project stays ORDINARY, sprk_relatedproject becomes SECURE.
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProject) });
        var (handler, writes) = UpdateHandler(world.Resolver());

        await handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_relatedproject"] = new EntityReference("sprk_project", SecureProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        writes.Should().ContainSingle("the lookup itself is written");
        world.Assignments.Should().Equal(("sprk_document", documentId, Directory.SecureNamedTeam));
        world.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(Directory.SecureNamedTeam, "read back");
        world.Assignments.Should().NotContain(a => a.TeamId == Directory.SecureDefaultTeam, "the Secure BU's default team is a decoy");
    }

    [Fact]
    public async Task Refile_DocumentMovedOutOfEverySecureParent_IsReassignedToTheNewParentsBusinessUnitTeam()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProject) });
        var (handler, writes) = UpdateHandler(world.Resolver());

        await handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        writes.Should().ContainSingle();
        world.Assignments.Should().Equal(("sprk_document", documentId, Directory.ChildTeam));
    }

    [Fact]
    public async Task Refile_UnderAFlaggedButNotIsolatedProject_IsRefused_AndWritesNothing()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var (handler, writes) = UpdateHandler(world.Resolver());

        var act = () => handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", FlaggedNotIsolatedProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        writes.Should().BeEmpty("a refusal writes nothing — not even the lookup");
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Refile_WhenTheDirectoryFaults_PropagatesTheFault_NotARefusal_AndWritesNothing()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var (handler, writes) = UpdateHandler(world.Resolver(fault: new TimeoutException("throttled")));

        var act = () => handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", SecureProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>("a fault is the caller's 5xx, distinct from a refusal");
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Refile_ClearingTheOnlySecureLookupWithNull_ReassignsOutOfTheSecureTeam()
    {
        // Task 146 r1 (verifier item 8): a generic update that CLEARS the lookup moves the child out of its secure parent
        // exactly as setting another parent does — it used to keep the Secure team (only EntityReference values counted).
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProject),
                ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            });
        var (handler, writes) = UpdateHandler(world.Resolver());

        await handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = null, ["sprk_documentdescription"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        writes.Should().ContainSingle("the clear itself is written");
        world.Assignments.Should().Equal(("sprk_document", documentId, Directory.ChildTeam));
    }

    [Fact]
    public async Task Refile_ClearingOnlyNonLookupColumns_WritesTheChange_AndReassignsNothing()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProject) });
        var (handler, writes) = UpdateHandler(world.Resolver());

        await handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_documentdescription"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        writes.Should().ContainSingle();
        world.Assignments.Should().BeEmpty("no parent changed");
    }

    [Fact]
    public async Task Refile_OfARootsOwnLookups_NeverReassignsTheRoot()
    {
        // A root's ownership is provisioning's (task 144 / owner S6) — a generic update never re-owns it.
        var world = World();
        var (handler, writes) = UpdateHandler(world.Resolver());

        await handler.UpdateAsync("sprk_workassignment", Guid.NewGuid(),
            new Dictionary<string, object?> { ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        writes.Should().ContainSingle();
        world.Assignments.Should().BeEmpty();
    }

    // =====================================================================================
    // Create — TaskActionCore (AI create-task: ActionSeam / CreateTaskNodeExecutor)
    // =====================================================================================

    [Theory]
    [InlineData("secure")]
    [InlineData("ordinary")]
    public async Task CreateTask_FiledToAMatter_IsOwnedByTheMattersTeam_TheNamedTeamWhenSecure(string kind)
    {
        var matter = kind == "secure" ? SecureMatter : OrdinaryMatter;
        var expectedTeam = kind == "secure" ? Directory.SecureNamedTeam : Directory.ChildTeam;
        var (core, created) = TaskCore(World().Resolver());

        var id = await core.CreateAsync(
            new TaskActionInput("Review", null, null, matter, "sprk_matter", OwnerId: Directory.CallerUserId),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        var owner = created.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(expectedTeam, "a task belongs with what it is filed against, never the supplied user");
    }

    [Fact]
    public async Task CreateTask_FiledToAMissingMatter_IsRefused_AndCreatesNothing()
    {
        var (core, created) = TaskCore(World().Resolver());

        var id = await core.CreateAsync(
            new TaskActionInput("Review", null, null, Guid.NewGuid(), "sprk_matter", OwnerId: Directory.CallerUserId),
            CancellationToken.None);

        id.Should().Be(Guid.Empty, "the writer's own contract: degraded success, nothing created");
        created.Should().BeEmpty("never an app-owned task, never the caller's team for a named-but-unreadable parent");
    }

    [Fact]
    public async Task CreateTask_WhenTheDirectoryFaults_PropagatesTheFault_AndCreatesNothing()
    {
        var (core, created) = TaskCore(World().Resolver(fault: new TimeoutException("throttled")));

        var act = () => core.CreateAsync(
            new TaskActionInput("Review", null, null, SecureMatter, "sprk_matter", OwnerId: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        created.Should().BeEmpty();
    }

    // =====================================================================================
    // Create — a record thread (owner S6)
    // =====================================================================================

    [Fact]
    public async Task CreateRecordThread_OnASecureMatter_IsOwnedByTheNamedSecureTeam_NotTheCaller()
    {
        var (resolver, created) = Threads(World().Resolver());

        await resolver.CreateRecordThreadAsync(
            Directory.CallerUserId, "Strategy", new RecordThreadAnchor("sprk_matter", SecureMatter.ToString(), "M"),
            CancellationToken.None);

        var owner = created.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task CreateRecordThread_OnAFlaggedButNotIsolatedProject_Is409WithTheStableCode_AndCreatesNothing()
    {
        var (resolver, created) = Threads(World().Resolver());

        var act = () => resolver.CreateRecordThreadAsync(
            Directory.CallerUserId, "Strategy",
            new RecordThreadAnchor("sprk_project", FlaggedNotIsolatedProject.ToString(), "P"),
            CancellationToken.None);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(409);
        problem.Code.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        created.Should().BeEmpty();
    }

    // =====================================================================================
    // Content rows — a communication's participant index
    // =====================================================================================

    [Fact]
    public async Task ParticipantRows_OfASecureCommunication_AreOwnedByTheNamedSecureTeam()
    {
        var communicationId = Guid.NewGuid();
        var world = World().WithRecord("sprk_communication", communicationId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam);
        var (indexer, created) = Indexer(world.Resolver());

        await indexer.WriteParticipantsAsync(communicationId, Participants(), CancellationToken.None);

        created.Should().HaveCount(2);
        created.Should().OnlyContain(r => r.GetAttributeValue<EntityReference>("ownerid").Id == Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task ParticipantRows_OfAnUnfiledCreatorOwnedCommunication_KeepTheirCreator()
    {
        // E1: an unfiled communication kept its creator (no owning team); its content rows do too.
        var communicationId = Guid.NewGuid();
        var world = World().WithRecord("sprk_communication", communicationId, Directory.GeneralBu);
        var (indexer, created) = Indexer(world.Resolver());

        await indexer.WriteParticipantsAsync(communicationId, Participants(), CancellationToken.None);

        created.Should().HaveCount(2);
        created.Should().OnlyContain(r => !r.Contains("ownerid"));
    }

    [Fact]
    public async Task ParticipantRows_OfAMissingCommunication_AreRefused_AndNoneIsWritten()
    {
        var (indexer, created) = Indexer(World().Resolver());

        await indexer.WriteParticipantsAsync(Guid.NewGuid(), Participants(), CancellationToken.None);

        created.Should().BeEmpty("the index's contract: a refusal is logged and writes no rows");
    }

    // =====================================================================================
    // The inbound-email HOLD (owner amendment R3: never create a record nobody can see)
    // =====================================================================================

    [Fact]
    public async Task InboundHold_BeforeTheLastAttempt_RetriesWithoutAlerting()
    {
        var (handler, notifications) = InboundHandler(admins: new[] { Guid.NewGuid() });
        var job = new JobContract { JobType = IncomingCommunicationJobHandler.JobTypeName, Attempt = 1, MaxAttempts = 3 };

        var outcome = await handler.HoldAsync(job, Refused(), TimeSpan.Zero, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "a retryable failure — the cause is often transient");
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task InboundHold_AtTheLastAttempt_DeadLettersUnprocessed_AndAlertsEveryAdministrator()
    {
        var admins = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (handler, notifications) = InboundHandler(admins);
        var job = new JobContract
        {
            JobType = IncomingCommunicationJobHandler.JobTypeName,
            Attempt = 3,
            MaxAttempts = 3,
            CorrelationId = "corr-146",
        };

        var outcome = await handler.HoldAsync(job, Refused(), TimeSpan.Zero, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "left unprocessed in the ingestion queue's dead-letter");
        notifications.Select(n => n.GetAttributeValue<EntityReference>("ownerid").Id).Should().BeEquivalentTo(admins);
        notifications.Should().OnlyContain(n =>
            n.GetAttributeValue<string>("body").Contains(RecordOwnerRefusal.SecureParentNotIsolated)
            && n.GetAttributeValue<string>("body").Contains("corr-146"));
    }

    [Fact]
    public async Task InboundHold_AtTheLastAttempt_WithNoAdministratorsConfigured_StillDeadLetters()
    {
        var (handler, notifications) = InboundHandler(admins: Array.Empty<Guid>());
        var job = new JobContract { JobType = IncomingCommunicationJobHandler.JobTypeName, Attempt = 3, MaxAttempts = 3 };

        var outcome = await handler.HoldAsync(job, Refused(), TimeSpan.Zero, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        notifications.Should().BeEmpty();
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static (DataverseUpdateHandler Handler, List<Dictionary<string, object?>> Writes) UpdateHandler(
        IRecordOwnershipResolver ownership)
    {
        var writes = new List<Dictionary<string, object?>>();
        var fields = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        fields
            .Setup(f => f.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, _, f, _, _) => writes.Add(f))
            .Returns(Task.CompletedTask);

        var handler = new DataverseUpdateHandler(
            fields.Object, Mock.Of<IGenericEntityService>(), ownership, NullLogger<DataverseUpdateHandler>.Instance);
        return (handler, writes);
    }

    private static (TaskActionCore Core, List<Entity> Created) TaskCore(IRecordOwnershipResolver ownership)
    {
        var (entities, created) = CapturingCreates();
        return (new TaskActionCore(entities, CoreAncestorResolverFixtures.Inert(), ownership, NullLogger.Instance), created);
    }

    private static (ThreadResolver Resolver, List<Entity> Created) Threads(IRecordOwnershipResolver ownership)
    {
        var (entities, created) = CapturingCreates();
        return (new ThreadResolver(
            Array.Empty<Sprk.Bff.Api.Services.Communication.Threads.IThreadKeyStrategy>(), entities, ownership,
            NullLogger<ThreadResolver>.Instance), created);
    }

    private static (CommunicationParticipantIndexer Indexer, List<Entity> Created) Indexer(IRecordOwnershipResolver ownership)
    {
        var (entities, created) = CapturingCreates();
        var indexer = new CommunicationParticipantIndexer(
            Mock.Of<ICommunicationDataverseService>(), entities, ownership, NullLogger<CommunicationParticipantIndexer>.Instance);
        return (indexer, created);
    }

    private static CommunicationParticipantSet Participants() => new()
    {
        FromAddress = "counsel@other.example",
        To = new[] { "partner@firm.example" },
    };

    /// <summary>A writer-side entity service: captures creates, answers the indexer's existing-row read as empty.</summary>
    private static (IGenericEntityService Service, List<Entity> Created) CapturingCreates()
    {
        var created = new List<Entity>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return (entities.Object, created);
    }

    private static RecordOwnerUnresolvedException Refused() => new(
        "sprk_communication",
        RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "the matter is marked secure but is not isolated"));

    private static (IncomingCommunicationJobHandler Handler, List<Entity> Notifications) InboundHandler(Guid[] admins)
    {
        var (entities, created) = CapturingCreates();
        var options = Options.Create(new CommunicationOptions { OwnershipHoldAlertUserIds = admins });

        // The processor is never invoked by HoldAsync; a constructed-but-uninitialised instance satisfies the
        // handler's non-null dependency without standing up Graph (whose sealed builders cannot be mocked).
        var processor = (IncomingCommunicationProcessor)RuntimeHelpers.GetUninitializedObject(typeof(IncomingCommunicationProcessor));
        var handler = new IncomingCommunicationJobHandler(
            processor,
            new NotificationService(entities, NullLogger<NotificationService>.Instance),
            options,
            NullLogger<IncomingCommunicationJobHandler>.Instance);
        return (handler, created);
    }
}
