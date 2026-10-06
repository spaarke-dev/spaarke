using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
/// <para>Families: a generic re-file (<see cref="DataverseUpdateHandler"/> — documents, both project lookups, into
/// secure; out of secure is refused for this person-less writer, task 146 c1 / owner round 10 item 7); an AI task create (<see cref="TaskActionCore"/>); a record thread (<see cref="ThreadResolver"/>); a
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
    public async Task Refile_DocumentMovedOutOfEverySecureParent_ByThisBackgroundWriter_IsRefused_AndWritesNothing()
    {
        // c1, owner round 10 item 7: moving a child OUT of a secure root is an un-secure, which only a Full Access holder
        // on the root or the child's creator may do. This writer (playbook output mapping, a background job) acts for no
        // person, so there is nobody whose F3 rights could be checked: refused, fail closed. Before c1 it was re-owned.
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new() { ["sprk_project"] = new EntityReference("sprk_project", SecureProject) });
        var (handler, writes) = UpdateHandler(world.Resolver());

        var act = () => handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProject) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        writes.Should().BeEmpty("refused before the lookup is written");
        world.Assignments.Should().BeEmpty();
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
    public async Task Refile_ClearingTheOnlySecureLookupWithNull_IsAMoveOut_RefusedForThisBackgroundWriter()
    {
        // Task 146 r1 (verifier item 8): a generic update that CLEARS the lookup moves the child out of its secure parent
        // exactly as setting another parent does. c1 (owner round 10 item 7): that move is an un-secure, and this writer acts
        // for no person whose F3 rights could be checked — so the clear is recognised as a move OUT and refused.
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProject),
                ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            });
        var (handler, writes) = UpdateHandler(world.Resolver());

        var act = () => handler.UpdateAsync("sprk_document", documentId,
            new Dictionary<string, object?> { ["sprk_project"] = null, ["sprk_documentdescription"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        writes.Should().BeEmpty("refused before the clear is written");
        world.Assignments.Should().BeEmpty();
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
    // Re-file — UpdateRecordActionCore (playbook update), c1-r1 / owner round 13 item 8:
    // "a playbook that impersonates a user is checked under F3 AS THAT USER; only truly person-less writers are refused"
    // =====================================================================================

    private static readonly Guid ImpersonatedUser = Guid.Parse("13131313-0000-4000-8000-000000000008");

    /// <summary>A document filed under the secure project, owned by the named team, created by someone else.</summary>
    private static (Directory World, Guid DocumentId) SecureDocument()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProject),
                ["createdby"] = new EntityReference("systemuser", Guid.NewGuid()),
            });
        return (world, documentId);
    }

    private static UpdateRecordActionInput MoveToOrdinaryProject(Guid documentId, Guid? impersonate) => new(
        "sprk_document", documentId, FieldMappings: null, LegacyFields: null,
        Lookups: new[] { new RenderedLookup("sprk_project", "sprk_project", OrdinaryProject.ToString()) },
        ImpersonateSystemUserId: impersonate);

    [Fact]
    public async Task PlaybookUpdate_ImpersonatingAFullAccessHolder_MovesTheDocumentOutOfItsSecureProject_AskedAsThatUser()
    {
        var (world, documentId) = SecureDocument();
        var (core, writes, rights) = ActionCore(world.Resolver());
        rights.Grant(ImpersonatedUser, SecureProject, FullAccessRights);

        await core.UpdateAsync(MoveToOrdinaryProject(documentId, ImpersonatedUser), CancellationToken.None);

        rights.Asked.Should().Equal(new[] { (ImpersonatedUser, "sprk_projects", SecureProject) },
            "F3 is asked of the user the update impersonates, on the secure root the document leaves");
        writes.Should().ContainSingle().Which.As.Should().Be(ImpersonatedUser, "the PATCH still runs as that user");
        world.Assignments.Should().Equal(("sprk_document", documentId, Directory.ChildTeam));
    }

    [Fact]
    public async Task PlaybookUpdate_ImpersonatingAWriteOnlyHolder_IsRefusedNotPermitted_AndNothingIsWritten()
    {
        var (world, documentId) = SecureDocument();
        var (core, writes, rights) = ActionCore(world.Resolver());
        rights.Grant(ImpersonatedUser, SecureProject, CollaborateRights);

        var act = () => core.UpdateAsync(MoveToOrdinaryProject(documentId, ImpersonatedUser), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode,
                "a definite 'no' for that user — not 'could not be checked'");
        writes.Should().BeEmpty("refused before the PATCH");
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaybookUpdate_ImpersonatingNobody_IsTrulyPersonLess_AndRefusedUnverifiable()
    {
        var (world, documentId) = SecureDocument();
        var (core, writes, rights) = ActionCore(world.Resolver());

        var act = () => core.UpdateAsync(MoveToOrdinaryProject(documentId, impersonate: null), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.PermissionUnverifiableReasonCode);
        rights.Asked.Should().BeEmpty("nobody to ask about");
        writes.Should().BeEmpty();
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateTask_RecordsThePersonWhoAskedForIt_OnlyWhenSomebodyDid(bool asked)
    {
        // c1-r1 (owner round 13 item 9): a task the application creates for a confirming user records them; a playbook node
        // acting for nobody records nobody.
        var (core, created) = TaskCore(World().Resolver());

        await core.CreateAsync(
            new TaskActionInput("Review", null, null, SecureMatter, "sprk_matter", OwnerId: null,
                RequestedBySystemUserId: asked ? Directory.CallerUserId : null),
            CancellationToken.None);

        var row = created.Should().ContainSingle().Subject;
        if (asked)
            row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(Directory.CallerUserId);
        else
            row.Attributes.Should().NotContainKey(RecordCreatorPerson.Column);
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

        var thread = created.Should().ContainSingle().Subject;
        var owner = thread.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(Directory.SecureNamedTeam);
        // c1-r1 (owner round 13 item 9): the team-owned thread records the caller who asked for it.
        thread.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(Directory.CallerUserId);
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
    // Create — a record's DEFAULT thread (FR-09 Tier 2; owner S6 a) and the per-user master (Tier 3; E2)
    // Task 146 b2 (verifier GAP, AC2/AC17): nothing pinned this owner — a hard-coded team survived every test.
    // =====================================================================================

    [Theory]
    [InlineData("secure")]
    [InlineData("ordinary")]
    public async Task DefaultRecordThread_IsOwnedByItsRecordsTeam_TheNamedSecureTeamWhenSecure(string kind)
    {
        var matter = kind == "secure" ? SecureMatter : OrdinaryMatter;
        var expectedTeam = kind == "secure" ? Directory.SecureNamedTeam : Directory.ChildTeam;
        var communicationId = Guid.NewGuid();
        var (resolver, created, assigned) = DefaultThreads(World().Resolver(), Message(communicationId,
            regarding: new EntityReference("sprk_matter", matter)));

        var threadId = await resolver.ResolveAndAssignThreadAsync(Request(communicationId), CancellationToken.None);

        threadId.Should().NotBeNull();
        var thread = created.Should().ContainSingle().Which;
        thread.LogicalName.Should().Be("sprk_communicationthread");
        thread.GetAttributeValue<bool>("sprk_isdefaultthread").Should().BeTrue("the Tier-2 per-record default");
        var owner = thread.GetAttributeValue<EntityReference>("ownerid");
        owner.Should().NotBeNull("a record's default thread is a child of the record, never left to its creator");
        owner.LogicalName.Should().Be("team");
        owner.Id.Should().Be(expectedTeam);
        owner.Id.Should().NotBe(Directory.SecureDefaultTeam, "the Secure BU's default team is a decoy");
        assigned.Should().ContainSingle("the message joins the thread it opened");
    }

    [Fact]
    public async Task DefaultRecordThread_OfAFlaggedButNotIsolatedProject_IsRefused_CreatesNothing_LeavesTheMessageUnthreaded()
    {
        var communicationId = Guid.NewGuid();
        var (resolver, created, assigned) = DefaultThreads(World().Resolver(), Message(communicationId,
            regarding: new EntityReference("sprk_project", FlaggedNotIsolatedProject)));

        var threadId = await resolver.ResolveAndAssignThreadAsync(Request(communicationId), CancellationToken.None);

        threadId.Should().BeNull("thread resolution is best-effort (NFR-02): a refusal leaves the message unthreaded");
        created.Should().BeEmpty("no record thread is created with an ordinary owner for a flagged root");
        assigned.Should().BeEmpty();
    }

    [Fact]
    public async Task JoiningASecureRecordsThread_WhenTheOwnerAssignmentFails_TakesTheMessageBackOutOfTheThread()
    {
        // Task 146 b2 (verifier RESIDUAL FAIL-OPEN): the JOIN writes the message's thread lookup, then its owner moves to the
        // secure record's team. When that assignment fails, the thread lookup the JOIN wrote is put back (AttachColumns),
        // so the message never sits in a secure record's thread while owned elsewhere.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var previousThread = new EntityReference("sprk_communicationthread", Guid.NewGuid());
        var world = World().WithRecord("sprk_communication", communicationId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_communicationthread"] = previousThread });
        world.AssignmentFault = new InvalidOperationException("Read Privilege Check For Owner failed");
        var threads = new Mock<IGenericEntityService>();
        threads
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread", threadId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
            });

        var act = () => ThreadResolver.AssignToThreadReconcilingOwnerAsync(
            threads.Object, world.Resolver(), communicationId, threadId,
            _ =>
            {
                world.Row("sprk_communication", communicationId)[ThreadResolver.ThreadLookupOnCommunication] =
                    new EntityReference("sprk_communicationthread", threadId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        world.Row("sprk_communication", communicationId).GetAttributeValue<EntityReference>("sprk_communicationthread")
            .Should().Be(previousThread, "the JOIN is undone with its failed owner assignment");
    }

    [Fact]
    public async Task MasterThread_OfAnUnfiledMessage_KeepsItsCreator_NoTeamOwner()
    {
        // Tier 3 (no regarding): the per-user master catch-all is per-user by design (E2) — no owner is written.
        var communicationId = Guid.NewGuid();
        var (resolver, created, _) = DefaultThreads(World().Resolver(), Message(communicationId, regarding: null));

        var threadId = await resolver.ResolveAndAssignThreadAsync(Request(communicationId), CancellationToken.None);

        threadId.Should().NotBeNull();
        created.Should().ContainSingle().Which.Contains("ownerid").Should().BeFalse();
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
    public async Task InboundHold_OnTheBrokersLastDelivery_DeadLettersUnprocessed_AndAlerts_EvenOnTheFirstAttempt()
    {
        // r2 (verifier item 11): a failed delivery is ABANDONED and redelivered as the same message, so its Attempt never
        // advances; ServiceBusJobProcessor dead-letters it by delivery count. The hold keyed on attempts alone answered
        // "retry" on that last delivery, and the email was dead-lettered with no administrator alert.
        var admins = new[] { Guid.NewGuid() };
        var (handler, notifications) = InboundHandler(admins);
        var job = new JobContract
        {
            JobType = IncomingCommunicationJobHandler.JobTypeName,
            Attempt = 1,
            MaxAttempts = 3,
            DeliveryCount = JobContract.MaxDeliveryCount,
        };

        job.IsFinalDelivery.Should().BeTrue("this is the delivery the processor dead-letters");
        var outcome = await handler.HoldAsync(job, Refused(), TimeSpan.Zero, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        notifications.Select(n => n.GetAttributeValue<EntityReference>("ownerid").Id).Should().BeEquivalentTo(admins);
    }

    [Fact]
    public async Task InboundHold_BeforeTheBrokersLastDelivery_RetriesWithoutAlerting()
    {
        var (handler, notifications) = InboundHandler(admins: new[] { Guid.NewGuid() });
        var job = new JobContract
        {
            JobType = IncomingCommunicationJobHandler.JobTypeName,
            Attempt = 1,
            MaxAttempts = 3,
            DeliveryCount = JobContract.MaxDeliveryCount - 1,
        };

        var outcome = await handler.HoldAsync(job, Refused(), TimeSpan.Zero, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        notifications.Should().BeEmpty();
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
            fields.Object, Mock.Of<IGenericEntityService>(), new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, ownership,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
            NullLogger<DataverseUpdateHandler>.Instance);
        return (handler, writes);
    }

    /// <summary>Full Access: Collaborate plus Delete (RecordShareLevels).</summary>
    private const AccessRights FullAccessRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share
        | AccessRights.Delete;

    /// <summary>Collaborate: Write without Delete — the Write holder F3 does not admit.</summary>
    private const AccessRights CollaborateRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    /// <summary>
    /// The playbook update core over the REAL resolver: the PATCH (and the user it impersonates) is captured at
    /// <see cref="IFieldMappingDataverseService"/>; F3's "rights as that user" are answered by <see cref="RightsAsUser"/>,
    /// registered in the scope the core resolves its services from.
    /// </summary>
    private static (UpdateRecordActionCore Core, List<(Guid? As, Dictionary<string, object?> Fields)> Writes, RightsAsUser Rights)
        ActionCore(IRecordOwnershipResolver ownership)
    {
        var writes = new List<(Guid? As, Dictionary<string, object?> Fields)>();
        var fields = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        fields
            .Setup(f => f.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, _, f, _, impersonate) => writes.Add((impersonate, f)))
            .Returns(Task.CompletedTask);

        var rights = new RightsAsUser();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(ownership)
            .AddSingleton<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>(rights)
            // Batch 4 integration (task 156): a re-file re-stamps after the write — an in-memory restamper, empty world.
            .AddSingleton(new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper)
            .BuildServiceProvider();
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(services);
        return (new UpdateRecordActionCore(fields.Object, scopes, NullLogger.Instance), writes, rights);
    }

    /// <summary>
    /// Dataverse's answer to "what may THIS user do on that record" (RetrievePrincipalAccess as the user), by stated
    /// grants; every question is recorded. The share-writing members are never reached by these tests.
    /// </summary>
    private sealed class RightsAsUser : Sprk.Bff.Api.Services.Access.IDataverseRecordShareService
    {
        private readonly Dictionary<(Guid Principal, Guid Record), AccessRights> _grants = new();

        public List<(Guid Principal, string EntitySet, Guid Record)> Asked { get; } = new();

        public void Grant(Guid principal, Guid record, AccessRights rights) => _grants[(principal, record)] = rights;

        public Task<AccessRights> GetPrincipalRightsAsync(
            Guid principalSystemUserId, string entitySetName, Guid recordId, CancellationToken ct = default)
        {
            Asked.Add((principalSystemUserId, entitySetName, recordId));
            return Task.FromResult(_grants.TryGetValue((principalSystemUserId, recordId), out var granted)
                ? granted
                : AccessRights.None);
        }

        public Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        // Batch 4 integration (task 149): the batched share read joins the seam; never reached by these tests.
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static (TaskActionCore Core, List<Entity> Created) TaskCore(IRecordOwnershipResolver ownership)
    {
        var (entities, created) = CapturingCreates();
        return (new TaskActionCore(
            entities, CoreAncestorResolverFixtures.Inert(), ownership,
            IdentityNormalizationFixtures.NoLinkedContact(), Moq.Mock.Of<Spaarke.Dataverse.ICommunicationDataverseService>(), NullLogger.Instance), created);
    }

    private static (ThreadResolver Resolver, List<Entity> Created) Threads(IRecordOwnershipResolver ownership)
    {
        var (entities, created) = CapturingCreates();
        return (new ThreadResolver(
            Array.Empty<Sprk.Bff.Api.Services.Communication.Threads.IThreadKeyStrategy>(), entities, ownership,
            NullLogger<ThreadResolver>.Instance), created);
    }

    /// <summary>
    /// A thread resolver whose channel key never groups (Tier-1 miss → the FR-09 fallback ladder), over a message row
    /// that is filed under <c>regarding</c> (Tier 2) or under nothing (Tier 3); no default thread exists yet.
    /// </summary>
    private static (ThreadResolver Resolver, List<Entity> Created, List<Dictionary<string, object>> Assigned) DefaultThreads(
        IRecordOwnershipResolver ownership, Entity message)
    {
        var created = new List<Entity>();
        var assigned = new List<Dictionary<string, object>>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveAsync("sprk_communication", message.Id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection()); // no default thread exists yet
        entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid);
        entities
            .Setup(e => e.UpdateAsync("sprk_communication", message.Id, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => assigned.Add(f))
            .Returns(Task.CompletedTask);

        var strategy = new Mock<Sprk.Bff.Api.Services.Communication.Threads.IThreadKeyStrategy>();
        strategy.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        strategy
            .Setup(s => s.ResolveAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Services.Communication.Threads.ThreadKeyResolution(null, CreateWhenAbsent: false));

        var resolver = new ThreadResolver(new[] { strategy.Object }, entities.Object, ownership, NullLogger<ThreadResolver>.Instance);
        return (resolver, created, assigned);
    }

    private static Entity Message(Guid id, EntityReference? regarding)
    {
        var row = new Entity("sprk_communication", id) { ["ownerid"] = new EntityReference("systemuser", Directory.CallerUserId) };
        if (regarding is not null)
            row[regarding.LogicalName == "sprk_matter" ? "sprk_regardingmatter" : "sprk_regardingproject"] = regarding;
        return row;
    }

    private static ThreadResolutionRequest Request(Guid communicationId) => new()
    {
        CommunicationId = communicationId,
        ChannelType = CommunicationType.Email,
        Direction = CommunicationDirection.Incoming,
        Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Re: filing" },
    };

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
